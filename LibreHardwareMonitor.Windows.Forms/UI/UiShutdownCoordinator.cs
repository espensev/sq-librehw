// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Marshals shutdown requests onto the UI thread and runs the shutdown body exactly once.
/// A background request claims the single UI handoff before dispatching it. The UI callback then
/// claims execution, which still lets FormClosed win if it runs before that callback is dispatched.
/// </summary>
internal sealed class UiShutdownCoordinator
{
    private const int NotRequested = 0;
    private const int Requested = 1;
    private const int Executing = 2;
    private const int Completed = 3;

    private readonly Func<bool> _isOnUiThread;
    private readonly Action<Action> _invokeOnUiThread;
    private readonly Func<Task> _shutdown;
    private readonly TaskCompletionSource<object> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    internal UiShutdownCoordinator(Func<bool> isOnUiThread, Action<Action> invokeOnUiThread, Action shutdown)
        : this(isOnUiThread, invokeOnUiThread, () =>
        {
            shutdown();
            return Task.CompletedTask;
        })
    {
        if (shutdown == null)
            throw new ArgumentNullException(nameof(shutdown));
    }

    internal UiShutdownCoordinator(Func<bool> isOnUiThread, Action<Action> invokeOnUiThread, Func<Task> shutdown)
    {
        _isOnUiThread = isOnUiThread ?? throw new ArgumentNullException(nameof(isOnUiThread));
        _invokeOnUiThread = invokeOnUiThread ?? throw new ArgumentNullException(nameof(invokeOnUiThread));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    internal bool IsShutdownRequested => Volatile.Read(ref _state) != NotRequested;

    internal void Request()
    {
        RequestCore();
    }

    internal Task RequestAsync()
    {
        RequestCore();
        return _completion.Task;
    }

    private void RequestCore()
    {
        if (_isOnUiThread())
        {
            Interlocked.CompareExchange(ref _state, Requested, NotRequested);
            RunClaimed();
            return;
        }

        // Claim the handoff before invoking the UI thread so concurrent SessionEnded notifications
        // cannot queue duplicate callbacks. MainForm supplies synchronous Control.Invoke here, so
        // the winning system-event handler does not return until the final save has completed.
        if (Interlocked.CompareExchange(ref _state, Requested, NotRequested) != NotRequested)
            return;

        try
        {
            _invokeOnUiThread(RunClaimed);
        }
        catch
        {
            // If dispatch failed before the callback ran (for example because the form handle was
            // destroyed), allow the UI-thread FormClosed path to claim shutdown instead.
            Interlocked.CompareExchange(ref _state, NotRequested, Requested);
            throw;
        }
    }

    private async void RunClaimed()
    {
        if (Interlocked.CompareExchange(ref _state, Executing, Requested) != Requested)
            return;

        try
        {
            await _shutdown().ConfigureAwait(true);
            _completion.TrySetResult(null);
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
        }
        finally
        {
            Volatile.Write(ref _state, Completed);
        }
    }
}
