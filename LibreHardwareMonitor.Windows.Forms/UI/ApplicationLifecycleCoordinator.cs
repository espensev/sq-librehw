// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Owns application hardware initialization, ordered option/reset work, independent
/// single-flight polling, and the drain required before hardware is closed.
/// </summary>
internal sealed class ApplicationLifecycleCoordinator : IDisposable
{
    private readonly Action<HardwareOptionKind, bool> _applyOption;
    private readonly Func<Task> _closeAsync;
    private readonly HardwareOperationCoordinator _hardwareOperations;
    private readonly TaskCompletionSource<object> _initializationCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Func<CancellationToken, Task> _openAsync;
    private readonly Action _reset;
    private readonly object _sync = new();

    private bool _disposed;
    private bool _initializationInProgress;
    private Task _initializationTask;
    private TaskCompletionSource<object> _pollCompletion;
    private bool _pollInProgress;
    private Task _stopTask;
    private bool _stopping;

    internal ApplicationLifecycleCoordinator(
        Func<CancellationToken, Task> openAsync,
        Action<HardwareOptionKind, bool> applyOption,
        Action reset,
        Func<Task> closeAsync)
    {
        _openAsync = openAsync ?? throw new ArgumentNullException(nameof(openAsync));
        _applyOption = applyOption ?? throw new ArgumentNullException(nameof(applyOption));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        _closeAsync = closeAsync ?? throw new ArgumentNullException(nameof(closeAsync));

        _hardwareOperations = new HardwareOperationCoordinator(
            ExecuteOptionAsync,
            ExecuteResetAsync,
            _lifecycleCancellation.Token);
        _hardwareOperations.StateChanged += HardwareOperations_StateChanged;
        _hardwareOperations.OptionFailed += HardwareOperations_OptionFailed;
        _hardwareOperations.ResetFailed += HardwareOperations_ResetFailed;
    }

    internal event Action StateChanged;

    internal event Action<HardwareOptionKind, bool, Exception> OptionFailed;

    internal event Action<Exception> ResetFailed;

    internal bool HasResetWork => _hardwareOperations.HasResetWork;

    internal bool IsBusy => _hardwareOperations.IsBusy;

    internal bool IsInitializationInProgress
    {
        get
        {
            lock (_sync)
                return _initializationInProgress;
        }
    }

    internal bool IsStopping
    {
        get
        {
            lock (_sync)
                return _stopping;
        }
    }

    internal Task InitializeAsync()
    {
        TaskCompletionSource<object> start = null;
        Task initializationTask;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_initializationTask != null)
                return _initializationTask;

            if (_stopping)
                return Task.FromCanceled(new CancellationToken(canceled: true));

            _initializationInProgress = true;
            start = NewCompletionSource();
            _initializationTask = InitializeAfterStartAsync(start.Task);
            initializationTask = _initializationTask;
        }

        try
        {
            RaiseStateChanged();
        }
        finally
        {
            start.TrySetResult(null);
        }

        return initializationTask;
    }

    internal bool RequestOption(HardwareOptionKind option, bool value)
    {
        lock (_sync)
        {
            if (_disposed || _stopping)
                return false;

            // UserOption raises its initial value during MainForm construction. Keep that
            // application synchronous so discovery sees the selected hardware groups.
            if (_initializationTask == null)
            {
                _applyOption(option, value);
                return true;
            }
        }

        return _hardwareOperations.RequestOption(option, value);
    }

    internal bool RequestReset()
    {
        lock (_sync)
        {
            if (_disposed || _stopping)
                return false;
        }

        return _hardwareOperations.RequestReset();
    }

    internal bool TryBeginPoll()
    {
        lock (_sync)
        {
            if (_disposed || _stopping || _pollInProgress)
                return false;

            _pollInProgress = true;
            _pollCompletion = NewCompletionSource();
            return true;
        }
    }

    internal void CompletePoll()
    {
        TaskCompletionSource<object> completion;
        lock (_sync)
        {
            if (!_pollInProgress)
                return;

            _pollInProgress = false;
            completion = _pollCompletion;
            _pollCompletion = null;
        }

        completion.TrySetResult(null);
    }

    internal void BeginStop()
    {
        bool releaseUnusedInitializationBarrier = false;

        lock (_sync)
        {
            if (_disposed || _stopping)
                return;

            _stopping = true;
            releaseUnusedInitializationBarrier = _initializationTask == null;
        }

        try
        {
            _lifecycleCancellation.Cancel();
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Application lifecycle cancellation callback failed: " + exception);
        }
        finally
        {
            // A reset can be queued before initialization starts. Release that executor only
            // after cancellation so it observes stop instead of running hardware work.
            if (releaseUnusedInitializationBarrier)
                _initializationCompletion.TrySetResult(null);
        }

        RaiseStateChanged();
    }

    internal Task StopAsync()
    {
        BeginStop();

        TaskCompletionSource<object> start = null;
        Task stopTask;
        lock (_sync)
        {
            if (_stopTask == null)
            {
                start = NewCompletionSource();
                _stopTask = StopAfterStartAsync(start.Task);
            }

            stopTask = _stopTask;
        }

        start?.TrySetResult(null);
        return stopTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            if (_stopTask == null || !_stopTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Application lifecycle resources can only be disposed after stop has completed.");
            }

            _disposed = true;
        }

        _hardwareOperations.StateChanged -= HardwareOperations_StateChanged;
        _hardwareOperations.OptionFailed -= HardwareOperations_OptionFailed;
        _hardwareOperations.ResetFailed -= HardwareOperations_ResetFailed;
        _lifecycleCancellation.Dispose();
        _lifecycleGate.Dispose();
    }

    private async Task InitializeAfterStartAsync(Task start)
    {
        await start.ConfigureAwait(false);
        CancellationToken cancellationToken = _lifecycleCancellation.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _openAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        finally
        {
            lock (_sync)
                _initializationInProgress = false;

            _initializationCompletion.TrySetResult(null);
            RaiseStateChanged();
        }
    }

    private async Task ExecuteOptionAsync(
        HardwareOptionKind option,
        bool value,
        CancellationToken cancellationToken)
    {
        await _initializationCompletion.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _applyOption(option, value);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ExecuteResetAsync(CancellationToken cancellationToken)
    {
        await _initializationCompletion.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reset();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void HardwareOperations_StateChanged()
    {
        RaiseStateChanged();
    }

    private void HardwareOperations_OptionFailed(
        HardwareOptionKind option,
        bool value,
        Exception exception)
    {
        try
        {
            OptionFailed?.Invoke(option, value, exception);
        }
        catch (Exception callbackException)
        {
            Debug.WriteLine("Application option failure callback failed: " + callbackException);
        }
    }

    private void HardwareOperations_ResetFailed(Exception exception)
    {
        try
        {
            ResetFailed?.Invoke(exception);
        }
        catch (Exception callbackException)
        {
            Debug.WriteLine("Application reset failure callback failed: " + callbackException);
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Application lifecycle state callback failed: " + exception);
        }
    }

    private async Task StopAfterStartAsync(Task start)
    {
        // Intentionally preserve the first StopAsync caller's SynchronizationContext through
        // this method. MainForm calls stop on its UI thread, and the close delegate owns the
        // existing UI-object teardown that must run on that same context before hardware close.
        await start;

        Task initializationTask;
        lock (_sync)
            initializationTask = _initializationTask;

        if (initializationTask != null)
        {
            try
            {
                await initializationTask;
            }
            catch (Exception exception)
            {
                // InitializeAsync is the observation surface for discovery failure. Shutdown
                // still drains and closes hardware after that failure.
                Debug.WriteLine("Hardware initialization ended before application stop: " + exception);
            }
        }

        await _hardwareOperations.WhenIdleAsync();

        Task pollCompletion;
        lock (_sync)
            pollCompletion = _pollCompletion?.Task ?? Task.CompletedTask;

        await pollCompletion;
        await _lifecycleGate.WaitAsync();
        try
        {
            await _closeAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ApplicationLifecycleCoordinator));
    }

    private static TaskCompletionSource<object> NewCompletionSource()
    {
        return new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
