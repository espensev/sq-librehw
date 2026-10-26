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
/// claims execution, which still lets a UI-thread request (Exit, FormClosing, or session end) win if
/// it runs before that callback is dispatched.
/// </summary>
internal sealed class UiShutdownCoordinator
{
    private const int NotRequested = 0;
    private const int Requested = 1;
    private const int Executing = 2;
    private const int Completed = 3;

    internal const int WM_CLOSE = 0x0010;
    internal const int WM_QUERYENDSESSION = 0x0011;
    internal const int WM_ENDSESSION = 0x0016;

    private readonly Func<bool> _isOnUiThread;
    private readonly Action<Action> _invokeOnUiThread;
    private readonly Func<Task> _shutdown;
    private readonly TaskCompletionSource<object> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;
    private int _checkpointClaimed;

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

    /// <summary>
    /// Answers the Windows session-end messages for the window that owns this coordinator.
    /// WM_QUERYENDSESSION is answered TRUE at once and has no side effects, so a session end that
    /// is later cancelled (another application vetoes, or a Restart Manager shutdown is abandoned)
    /// leaves the application running. WM_ENDSESSION with wParam FALSE is that cancellation and is
    /// ignored. WM_ENDSESSION with wParam TRUE runs <paramref name="checkpoint"/> and then starts
    /// shutdown before the message returns, because Windows may terminate the process at any point
    /// after that. WM_CLOSE is swallowed once shutdown has been requested: Restart Manager sends it
    /// right after WM_ENDSESSION returns, and letting Form close and dispose the window would end
    /// the message loop before hardware close and the final save. The shutdown body still exits the
    /// application when it finishes. Returns false for every other message, and for WM_CLOSE before
    /// shutdown, which the caller must handle itself.
    /// </summary>
    internal bool TryHandleShutdownMessage(int msg, IntPtr wParam, Action checkpoint, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case WM_QUERYENDSESSION:
                result = (IntPtr)1;
                return true;
            case WM_ENDSESSION:
                if (wParam != IntPtr.Zero)
                    RequestAfterCheckpoint(checkpoint);

                return true;
            case WM_CLOSE:
                return IsShutdownRequested;
            default:
                return false;
        }
    }

    /// <summary>
    /// Runs <paramref name="checkpoint"/> synchronously, then requests shutdown. The checkpoint runs
    /// at most once, only on the UI thread, and only until the shutdown body has completed: a body
    /// that is already running (for example a user Exit) still has its final save behind awaits that
    /// Windows does not wait for. A throwing checkpoint still requests shutdown.
    /// </summary>
    internal void RequestAfterCheckpoint(Action checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));

        try
        {
            if (_isOnUiThread() &&
                Volatile.Read(ref _state) != Completed &&
                Interlocked.Exchange(ref _checkpointClaimed, 1) == 0)
            {
                checkpoint();
            }
        }
        finally
        {
            Request();
        }
    }

    private void RequestCore()
    {
        if (_isOnUiThread())
        {
            Interlocked.CompareExchange(ref _state, Requested, NotRequested);
            RunClaimed();
            return;
        }

        // Claim the handoff before invoking the UI thread so concurrent background requests cannot
        // queue duplicate callbacks. MainForm supplies queued Control.BeginInvoke here; RequestAsync
        // completion lets a background requester wait for the final save. MainForm's own session-end
        // requests arrive on the UI thread (SystemEvents raises SessionEnded through the form's
        // synchronization context), so they take the inline branch above.
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
