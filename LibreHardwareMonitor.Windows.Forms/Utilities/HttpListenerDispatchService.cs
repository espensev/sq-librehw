// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal sealed class HttpListenerDispatchService
{
    internal const int DefaultMaxConcurrentHandlers = 16;

    private readonly Func<HttpListenerContext, CancellationToken, Task> _dispatchAsync;
    private readonly HttpListener _listener;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly BoundedRequestHandlerPool _requestHandlers;

    private Task _listenerTask;
    private CancellationTokenSource _cts;

    internal HttpListenerDispatchService(
        Func<HttpListenerContext, CancellationToken, Task> dispatchAsync,
        int maxConcurrentHandlers = DefaultMaxConcurrentHandlers)
    {
        _dispatchAsync = dispatchAsync ?? throw new ArgumentNullException(nameof(dispatchAsync));
        _requestHandlers = new BoundedRequestHandlerPool(maxConcurrentHandlers);

        try
        {
            _listener = new HttpListener { IgnoreWriteExceptions = true };
        }
        catch (PlatformNotSupportedException)
        {
            _listener = null;
        }
    }

    internal bool PlatformNotSupported => _listener == null;

    internal bool Start(string listenerIp, int listenerPort, bool authEnabled, out string effectiveListenerIp)
    {
        effectiveListenerIp = listenerIp;
        if (PlatformNotSupported)
            return false;

        _lifecycleGate.Wait();
        try
        {
            if (_listener.IsListening)
                return true;

            // A timed-out stop retains its canceled session so a restart cannot overwrite the
            // cancellation source while old handlers still own it. A later stop can drain it.
            if (_cts != null || _listenerTask != null)
                return false;

            // Validate that the selected IP exists (it could have been previously selected
            // before switching networks). Enumerate local interfaces instead of a DNS
            // round-trip: Dns.GetHostEntry can block for seconds on the UI thread while
            // NICs initialize or DNS is misconfigured.
            bool ipFound = false;
            foreach (System.Net.NetworkInformation.NetworkInterface nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (effectiveListenerIp == address.Address.ToString())
                    {
                        ipFound = true;
                        break;
                    }
                }

                if (ipFound)
                    break;
            }

            if (!ipFound)
            {
                // Default to the previous behavior when the selected interface no longer exists.
                effectiveListenerIp = "+";
            }

            string prefix = "http://" + effectiveListenerIp + ":" + listenerPort + "/";

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(prefix);
            _listener.Realm = "Libre Hardware Monitor";
            _listener.AuthenticationSchemes = authEnabled ? AuthenticationSchemes.Basic : AuthenticationSchemes.Anonymous;
            _listener.Start();

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ProcessRequestsAsync(_cts.Token));
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        return true;
    }

    internal async Task<bool> StopAsync()
    {
        if (PlatformNotSupported)
            return false;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource cancellation = _cts;
            Task listenerTask = _listenerTask;

            if (cancellation == null && listenerTask == null && !_listener.IsListening)
                return true;

            cancellation?.Cancel();
            // Stop() faults the pending GetContextAsync (which ignores the token) so the accept
            // loop exits immediately. Active request registrations abort their responses.
            _listener?.Stop();

            bool listenerStopped = await WaitForCompletionAsync(listenerTask, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            bool handlersDrained = listenerStopped &&
                                   await _requestHandlers.DrainAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            if (listenerStopped && handlersDrained)
            {
                _listenerTask = null;
                _cts = null;
                cancellation?.Dispose();
            }

            return listenerStopped && handlersDrained;
        }
        catch (HttpListenerException)
        { }
        catch (OperationCanceledException)
        { }
        catch (NullReferenceException)
        { }
        catch (Exception)
        { }
        finally
        {
            _lifecycleGate.Release();
        }

        return false;
    }

    internal void Abort()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Abort();
        }
        catch
        { }
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                try
                {
                    HttpListenerContext acceptedContext = context;
                    await _requestHandlers.QueueAsync(token => HandleContextAsync(acceptedContext, token), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AbortContext(context);
                    break;
                }
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 50)
            {
                // Handle Windows update bug (e.g., 2025-10 Cumulative Update): retry after delay.
                System.Diagnostics.Debug.WriteLine($"HttpListener error (code {ex.ErrorCode}): {ex.Message}. Retrying in 5 seconds.");
                await Task.Delay(5000, cancellationToken);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995)
            {
                break; // ERROR_OPERATION_ABORTED: Stop()/Abort() faulted the pending accept.
            }
            catch (ObjectDisposedException)
            {
                break; // Listener stopped.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected HttpListener error: {ex.Message}");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        // Backstop: any unhandled error while handling a request must still close the response.
        // Otherwise the client connection hangs until it times out.
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(state => AbortContext((HttpListenerContext)state), context);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _dispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP request handler error: {ex.Message}");
            try { context.Response.StatusCode = 500; }
            catch { }
        }
        finally
        {
            try { context.Response.Close(); }
            catch { /* Client closed the connection before the content was sent. */ }
        }
    }

    private static void AbortContext(HttpListenerContext context)
    {
        try
        {
            context?.Response.Abort();
        }
        catch
        {
            // The response may already have completed or been aborted by the client.
        }
    }

    private static async Task<bool> WaitForCompletionAsync(Task task, TimeSpan timeout)
    {
        if (task == null)
            return true;

        Task completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != task)
            return false;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is HttpListenerException ||
                                   ex is ObjectDisposedException)
        { }

        return true;
    }
}

internal sealed class BoundedRequestHandlerPool
{
    private readonly HashSet<Task> _activeHandlers = new();
    private readonly object _activeHandlersLock = new();
    private readonly SemaphoreSlim _handlerSlots;
    private int _activeCount;
    private int _peakActiveCount;

    public BoundedRequestHandlerPool(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        MaxConcurrency = maxConcurrency;
        _handlerSlots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public int MaxConcurrency { get; }

    internal int ActiveCount => Volatile.Read(ref _activeCount);

    internal int PeakActiveCount => Volatile.Read(ref _peakActiveCount);

    public async Task QueueAsync(Func<CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        await _handlerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

        Task handlerTask;
        try
        {
            handlerTask = ExecuteAsync(handler, cancellationToken);
        }
        catch
        {
            _handlerSlots.Release();
            throw;
        }

        lock (_activeHandlersLock)
            _activeHandlers.Add(handlerTask);

        _ = handlerTask.ContinueWith(completedTask =>
        {
            // Observe a delegate fault even though production handlers contain their own
            // exception boundary. This keeps test/injected handlers from becoming unobserved.
            _ = completedTask.Exception;
            lock (_activeHandlersLock)
                _activeHandlers.Remove(completedTask);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            Task[] handlers;
            lock (_activeHandlersLock)
                handlers = _activeHandlers.ToArray();

            if (handlers.Length == 0)
                return true;

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return false;

            Task allHandlers = Task.WhenAll(handlers);
            Task completed = await Task.WhenAny(allHandlers, Task.Delay(remaining)).ConfigureAwait(false);
            if (completed != allHandlers)
                return false;

            try
            {
                await allHandlers.ConfigureAwait(false);
            }
            catch
            {
                // Completion, rather than success, is the lifetime condition. Individual
                // failures are observed by the tracking continuation above.
            }
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        int activeCount = Interlocked.Increment(ref _activeCount);
        UpdatePeakActiveCount(activeCount);

        try
        {
            await handler(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
            _handlerSlots.Release();
        }
    }

    private void UpdatePeakActiveCount(int activeCount)
    {
        int observedPeak = Volatile.Read(ref _peakActiveCount);
        while (activeCount > observedPeak)
        {
            int priorPeak = Interlocked.CompareExchange(ref _peakActiveCount, activeCount, observedPeak);
            if (priorPeak == observedPeak)
                return;

            observedPeak = priorPeak;
        }
    }
}
