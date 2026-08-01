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
    private readonly TaskCompletionSource<object> _admissionClosedCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private int _admissionCount;
    private TaskCompletionSource<object> _admissionDrainCompletion;
    private bool _disposed;
    private bool _initializationInProgress;
    private Task _initializationTask;
    private TaskCompletionSource<object> _pollCompletion;
    private bool _pollInProgress;
    private int _preInitializationAdmissionCount;
    private TaskCompletionSource<object> _preInitializationAdmissionDrainCompletion;
    private Task _preInitializationOptionTail = Task.CompletedTask;
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
        Task preInitializationAdmissionDrain;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_initializationTask != null)
                return _initializationTask;

            if (_stopping)
                return Task.FromCanceled(new CancellationToken(canceled: true));

            preInitializationAdmissionDrain =
                _preInitializationAdmissionDrainCompletion?.Task ?? Task.CompletedTask;
            _initializationInProgress = true;
            start = NewCompletionSource();
            _initializationTask = InitializeAfterStartAsync(
                start.Task,
                preInitializationAdmissionDrain);
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
        bool isPreInitialization;
        Task precedingPreInitializationOption = null;
        TaskCompletionSource<object> preInitializationOptionCompletion = null;

        lock (_sync)
        {
            if (_disposed || _stopping)
                return false;

            isPreInitialization = _initializationTask == null;
            ReserveAdmissionUnderLock(isPreInitialization);
            if (isPreInitialization)
            {
                precedingPreInitializationOption = _preInitializationOptionTail;
                preInitializationOptionCompletion = NewCompletionSource();
                _preInitializationOptionTail = preInitializationOptionCompletion.Task;
            }
        }

        try
        {
            if (isPreInitialization)
            {
                // UserOption raises its initial value during MainForm construction. Execute
                // outside _sync, but keep each caller synchronous and preserve admission order.
                try
                {
                    precedingPreInitializationOption.GetAwaiter().GetResult();
                    _applyOption(option, value);
                    return true;
                }
                finally
                {
                    preInitializationOptionCompletion.TrySetResult(null);
                }
            }

            return _hardwareOperations.RequestOption(option, value);
        }
        finally
        {
            ReleaseAdmission(isPreInitialization);
        }
    }

    internal bool RequestReset()
    {
        bool isPreInitialization;
        lock (_sync)
        {
            if (_disposed || _stopping)
                return false;

            isPreInitialization = _initializationTask == null;
            ReserveAdmissionUnderLock(isPreInitialization);
        }

        try
        {
            return _hardwareOperations.RequestReset();
        }
        finally
        {
            ReleaseAdmission(isPreInitialization);
        }
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

            // A concurrent StopAsync can observe _stopping while this first BeginStop is still
            // running cancellation callbacks. Do not let its stop body inspect drains yet.
            _admissionClosedCompletion.TrySetResult(null);
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

    private async Task InitializeAfterStartAsync(Task start, Task preInitializationAdmissionDrain)
    {
        // MainForm starts initialization on its UI thread and supplies the existing PawnIO
        // prompt/install plus hardware-open sequence as the delegate. Preserve that first
        // caller's SynchronizationContext while still waiting for pre-initialization admission.
        await start;
        await preInitializationAdmissionDrain;
        CancellationToken cancellationToken = _lifecycleCancellation.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _openAsync(cancellationToken);
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
        await _admissionClosedCompletion.Task;

        Task admissionDrain;
        lock (_sync)
            admissionDrain = _admissionDrainCompletion?.Task ?? Task.CompletedTask;

        await admissionDrain;

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

    private void ReserveAdmissionUnderLock(bool isPreInitialization)
    {
        if (++_admissionCount == 1)
            _admissionDrainCompletion = NewCompletionSource();

        if (isPreInitialization && ++_preInitializationAdmissionCount == 1)
            _preInitializationAdmissionDrainCompletion = NewCompletionSource();
    }

    private void ReleaseAdmission(bool isPreInitialization)
    {
        TaskCompletionSource<object> admissionDrain = null;
        TaskCompletionSource<object> preInitializationAdmissionDrain = null;

        lock (_sync)
        {
            if (--_admissionCount == 0)
            {
                admissionDrain = _admissionDrainCompletion;
                _admissionDrainCompletion = null;
            }

            if (isPreInitialization && --_preInitializationAdmissionCount == 0)
            {
                preInitializationAdmissionDrain = _preInitializationAdmissionDrainCompletion;
                _preInitializationAdmissionDrainCompletion = null;
            }
        }

        preInitializationAdmissionDrain?.TrySetResult(null);
        admissionDrain?.TrySetResult(null);
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
