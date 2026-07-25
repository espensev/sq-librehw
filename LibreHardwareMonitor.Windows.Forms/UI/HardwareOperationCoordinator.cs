// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal enum HardwareOptionKind
{
    Motherboard,
    Cpu,
    Memory,
    Gpu,
    PowerMonitor,
    Controller,
    Storage,
    Network,
    Psu,
    Battery
}

/// <summary>
/// Keeps runtime hardware-option and reset requests bounded and serialized.
/// One latest value is retained per option, while distinct options keep their
/// user-request order. A reset runs after the currently pending option values.
/// </summary>
internal sealed class HardwareOperationCoordinator
{
    private sealed class PendingOption
    {
        internal PendingOption(bool value, long sequence)
        {
            Value = value;
            Sequence = sequence;
        }

        internal long Sequence { get; }

        internal bool Value { get; }
    }

    private readonly Func<HardwareOptionKind, bool, CancellationToken, Task> _applyOptionAsync;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<HardwareOptionKind, PendingOption> _pendingOptions = new();
    private readonly Func<CancellationToken, Task> _resetAsync;
    private readonly object _sync = new();

    private bool _busy;
    private Task _drainTask = Task.CompletedTask;
    private bool _resetActive;
    private bool _resetPending;
    private long _sequence;

    internal HardwareOperationCoordinator(
        Func<HardwareOptionKind, bool, CancellationToken, Task> applyOptionAsync,
        Func<CancellationToken, Task> resetAsync,
        CancellationToken cancellationToken)
    {
        _applyOptionAsync = applyOptionAsync ?? throw new ArgumentNullException(nameof(applyOptionAsync));
        _resetAsync = resetAsync ?? throw new ArgumentNullException(nameof(resetAsync));
        _cancellationToken = cancellationToken;
    }

    internal event Action StateChanged;

    internal event Action<HardwareOptionKind, bool, Exception> OptionFailed;

    internal event Action<Exception> ResetFailed;

    internal bool HasResetWork
    {
        get
        {
            lock (_sync)
                return _resetPending || _resetActive;
        }
    }

    internal bool IsBusy
    {
        get
        {
            lock (_sync)
                return _busy;
        }
    }

    internal bool RequestOption(HardwareOptionKind option, bool value)
    {
        TaskCompletionSource<object> drainStart = null;
        lock (_sync)
        {
            if (_cancellationToken.IsCancellationRequested)
                return false;

            _pendingOptions[option] = new PendingOption(value, ++_sequence);
            drainStart = StartDrainIfIdle();
        }

        SignalStateAndStartDrain(drainStart);
        return true;
    }

    internal bool RequestReset()
    {
        TaskCompletionSource<object> drainStart = null;
        lock (_sync)
        {
            if (_cancellationToken.IsCancellationRequested)
                return false;

            // One pending reset is enough. A request that arrives while a reset is
            // active still records one follow-up reset.
            if (_resetPending)
                return false;

            _resetPending = true;
            drainStart = StartDrainIfIdle();
        }

        SignalStateAndStartDrain(drainStart);
        return true;
    }

    internal Task WhenIdleAsync()
    {
        lock (_sync)
            return _drainTask;
    }

    private async Task DrainAfterStartAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await DrainAsync().ConfigureAwait(false);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            HardwareOptionKind option = default;
            bool optionValue = false;
            bool isReset;

            lock (_sync)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    StopAfterCancellation();
                    isReset = false;
                }
                else if (TryTakeNextOption(out option, out optionValue))
                {
                    isReset = false;
                }
                else if (_resetPending)
                {
                    _resetPending = false;
                    _resetActive = true;
                    isReset = true;
                }
                else
                {
                    _busy = false;
                    RaiseStateChanged();
                    return;
                }
            }

            if (_cancellationToken.IsCancellationRequested)
            {
                StopAfterCancellationThreadSafe();
                return;
            }

            if (isReset)
            {
                try
                {
                    await _resetAsync(_cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    StopAfterCancellationThreadSafe();
                    return;
                }
                catch (Exception exception)
                {
                    RaiseResetFailed(exception);
                }
                finally
                {
                    lock (_sync)
                        _resetActive = false;

                    RaiseStateChanged();
                }
            }
            else
            {
                try
                {
                    await _applyOptionAsync(option, optionValue, _cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    StopAfterCancellationThreadSafe();
                    return;
                }
                catch (Exception exception)
                {
                    RaiseOptionFailed(option, optionValue, exception);
                }
            }
        }
    }

    private void RaiseOptionFailed(HardwareOptionKind option, bool value, Exception exception)
    {
        try
        {
            OptionFailed?.Invoke(option, value, exception);
        }
        catch (Exception callbackException)
        {
            Debug.WriteLine("Hardware option failure callback failed: " + callbackException);
        }
    }

    private void RaiseResetFailed(Exception exception)
    {
        try
        {
            ResetFailed?.Invoke(exception);
        }
        catch (Exception callbackException)
        {
            Debug.WriteLine("Hardware reset failure callback failed: " + callbackException);
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
            Debug.WriteLine("Hardware operation state callback failed: " + exception);
        }
    }

    private void SignalStateAndStartDrain(TaskCompletionSource<object> drainStart)
    {
        try
        {
            RaiseStateChanged();
        }
        finally
        {
            drainStart?.TrySetResult(null);
        }
    }

    private TaskCompletionSource<object> StartDrainIfIdle()
    {
        if (_busy)
            return null;

        _busy = true;
        var start = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _drainTask = DrainAfterStartAsync(start.Task);
        return start;
    }

    private void StopAfterCancellation()
    {
        _pendingOptions.Clear();
        _resetPending = false;
        _resetActive = false;
        _busy = false;
    }

    private void StopAfterCancellationThreadSafe()
    {
        lock (_sync)
            StopAfterCancellation();

        RaiseStateChanged();
    }

    private bool TryTakeNextOption(out HardwareOptionKind option, out bool value)
    {
        option = default;
        value = false;
        PendingOption selected = null;

        foreach (KeyValuePair<HardwareOptionKind, PendingOption> candidate in _pendingOptions)
        {
            if (selected == null || candidate.Value.Sequence < selected.Sequence)
            {
                option = candidate.Key;
                selected = candidate.Value;
            }
        }

        if (selected == null)
            return false;

        value = selected.Value;
        _pendingOptions.Remove(option);
        return true;
    }
}
