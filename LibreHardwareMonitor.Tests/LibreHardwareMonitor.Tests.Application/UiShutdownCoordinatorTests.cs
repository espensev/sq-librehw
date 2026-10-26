// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class UiShutdownCoordinatorTests
{
    [Fact]
    public void FormClosedRequest_CanWinBeforeQueuedSessionEndedRequest()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action => queued.Enqueue(action),
            () => Interlocked.Increment(ref shutdownCount));

        // Model SessionEnded arriving from the SystemEvents monitoring thread.
        coordinator.Request();
        Assert.Equal(0, Volatile.Read(ref shutdownCount));
        Assert.Single(queued);
        Assert.True(coordinator.IsShutdownRequested);

        // If FormClosed runs before the posted callback, its UI-thread request owns shutdown.
        isOnUiThread = true;
        coordinator.Request();
        Assert.Equal(1, Volatile.Read(ref shutdownCount));

        Assert.True(queued.TryDequeue(out Action delayedSessionEnded));
        delayedSessionEnded();
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public async Task ConcurrentSessionEndedRequests_DispatchAndRunShutdownExactlyOnce()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        int dispatchCount = 0;
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action =>
            {
                Interlocked.Increment(ref dispatchCount);
                queued.Enqueue(action);
            },
            () => Interlocked.Increment(ref shutdownCount));

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(coordinator.Request)));
        Assert.Equal(1, Volatile.Read(ref dispatchCount));
        Assert.Single(queued);

        isOnUiThread = true;
        Assert.True(queued.TryDequeue(out Action dispatchedShutdown));
        dispatchedShutdown();
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.True(coordinator.IsShutdownRequested);
    }

    [Fact]
    public void DispatchFailure_ReleasesClaimForUiThreadShutdown()
    {
        bool isOnUiThread = false;
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            _ => throw new InvalidOperationException("UI handle unavailable"),
            () => Interlocked.Increment(ref shutdownCount));

        Assert.Throws<InvalidOperationException>(coordinator.Request);
        Assert.False(coordinator.IsShutdownRequested);

        isOnUiThread = true;
        coordinator.Request();

        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.True(coordinator.IsShutdownRequested);
    }

    [Fact]
    public async Task BackgroundRequest_DoesNotReturnBeforeSynchronousShutdownCompletes()
    {
        bool isOnUiThread = false;
        using var shutdownEntered = new ManualResetEventSlim();
        using var releaseShutdown = new ManualResetEventSlim();
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action =>
            {
                isOnUiThread = true;
                action();
            },
            () =>
            {
                shutdownEntered.Set();
                releaseShutdown.Wait();
            });

        Task request = Task.Factory.StartNew(coordinator.Request,
                                             CancellationToken.None,
                                             TaskCreationOptions.LongRunning,
                                             TaskScheduler.Default);
        try
        {
            Assert.True(shutdownEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(request.IsCompleted);
        }
        finally
        {
            releaseShutdown.Set();
        }

        await request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.IsShutdownRequested);
    }

    [Fact]
    public void ReentrantUiRequest_RunsShutdownExactlyOnce()
    {
        int shutdownCount = 0;
        UiShutdownCoordinator coordinator = null;
        coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () =>
            {
                Interlocked.Increment(ref shutdownCount);
                coordinator.Request();
            });

        coordinator.Request();

        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public async Task AsyncShutdown_BackgroundRequesterWaitsForCompletionAndRunsOnce()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        var releaseShutdown = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action => queued.Enqueue(action),
            async () =>
            {
                Interlocked.Increment(ref shutdownCount);
                await releaseShutdown.Task;
            });

        Task first = coordinator.RequestAsync();
        Task second = coordinator.RequestAsync();
        Assert.Same(first, second);
        Assert.Single(queued);
        Assert.False(first.IsCompleted);

        isOnUiThread = true;
        Assert.True(queued.TryDequeue(out Action dispatchedShutdown));
        dispatchedShutdown();
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.False(first.IsCompleted);

        releaseShutdown.SetResult(null);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public async Task UiCloseTakeover_CompletesQueuedBackgroundRequestTaskExactlyOnce()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action => queued.Enqueue(action),
            () => Interlocked.Increment(ref shutdownCount));

        Task backgroundCompletion = coordinator.RequestAsync();
        Assert.Single(queued);
        Assert.False(backgroundCompletion.IsCompleted);

        isOnUiThread = true;
        Task uiCompletion = coordinator.RequestAsync();
        Assert.Same(backgroundCompletion, uiCompletion);
        await uiCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));

        Assert.True(queued.TryDequeue(out Action delayedBackgroundDispatch));
        delayedBackgroundDispatch();

        await backgroundCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public async Task BeginInvokeStyleSessionEndedWait_DoesNotReturnBeforeAsyncShutdownCompletes()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        var dispatchQueued = NewCompletionSource();
        var shutdownStarted = NewCompletionSource();
        var releaseShutdown = NewCompletionSource();
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action =>
            {
                queued.Enqueue(action);
                dispatchQueued.TrySetResult(null);
            },
            async () =>
            {
                Interlocked.Increment(ref shutdownCount);
                shutdownStarted.TrySetResult(null);
                await releaseShutdown.Task.ConfigureAwait(true);
            });

        Task sessionEndedWaiter = Task.Run(() => coordinator.RequestAsync().GetAwaiter().GetResult());
        await dispatchQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sessionEndedWaiter.IsCompleted);

        isOnUiThread = true;
        Assert.True(queued.TryDequeue(out Action dispatchedShutdown));
        dispatchedShutdown();
        await shutdownStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sessionEndedWaiter.IsCompleted);

        releaseShutdown.TrySetResult(null);
        await sessionEndedWaiter.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public async Task ShutdownFailure_FaultsSharedCompletionAndDoesNotRetry()
    {
        var shutdownFailure = new InvalidOperationException("shutdown failed");
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () =>
            {
                Interlocked.Increment(ref shutdownCount);
                return Task.FromException(shutdownFailure);
            });

        Task first = coordinator.RequestAsync();
        Task second = coordinator.RequestAsync();
        Assert.Same(first, second);

        InvalidOperationException firstObserved = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        InvalidOperationException secondObserved = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Same(shutdownFailure, firstObserved);
        Assert.Same(shutdownFailure, secondObserved);

        Task later = coordinator.RequestAsync();
        Assert.Same(first, later);
        InvalidOperationException laterObserved = await Assert.ThrowsAsync<InvalidOperationException>(() => later);
        Assert.Same(shutdownFailure, laterObserved);
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.True(coordinator.IsShutdownRequested);
    }

    [Fact]
    public async Task RequestAsyncDispatchFailure_ReleasesClaimForUiRetry()
    {
        bool isOnUiThread = false;
        var dispatchFailure = new InvalidOperationException("UI handle unavailable");
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            _ => throw dispatchFailure,
            () => Interlocked.Increment(ref shutdownCount));

        Action failedRequest = () => _ = coordinator.RequestAsync();
        InvalidOperationException observedDispatchFailure =
            Assert.Throws<InvalidOperationException>(failedRequest);
        Assert.Same(dispatchFailure, observedDispatchFailure);
        Assert.False(coordinator.IsShutdownRequested);

        isOnUiThread = true;
        Task retry = coordinator.RequestAsync();
        await retry.WaitAsync(TimeSpan.FromSeconds(5));

        Task repeated = coordinator.RequestAsync();
        Assert.Same(retry, repeated);
        await repeated.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.True(coordinator.IsShutdownRequested);
    }

    [Fact]
    public void QueryEndSession_AnswersTrueWithoutCheckpointOrShutdown()
    {
        int checkpointCount = 0;
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => Interlocked.Increment(ref shutdownCount));

        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_QUERYENDSESSION,
            IntPtr.Zero,
            () => checkpointCount++,
            out IntPtr result));

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(0, checkpointCount);
        Assert.Equal(0, Volatile.Read(ref shutdownCount));
        Assert.False(coordinator.IsShutdownRequested);
    }

    [Fact]
    public void CancelledEndSession_LeavesApplicationRunning()
    {
        int checkpointCount = 0;
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => Interlocked.Increment(ref shutdownCount));

        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_QUERYENDSESSION, IntPtr.Zero, () => checkpointCount++, out _));
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, IntPtr.Zero, () => checkpointCount++, out IntPtr result));

        Assert.Equal(IntPtr.Zero, result);
        Assert.Equal(0, checkpointCount);
        Assert.Equal(0, Volatile.Read(ref shutdownCount));
        Assert.False(coordinator.IsShutdownRequested);
    }

    [Fact]
    public void EndingSession_RunsCheckpointBeforeShutdownBody()
    {
        var order = new ConcurrentQueue<string>();
        var releaseShutdown = NewCompletionSource();
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            async () =>
            {
                order.Enqueue("shutdown");
                await releaseShutdown.Task;
            });

        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION,
            (IntPtr)1,
            () => order.Enqueue("checkpoint"),
            out IntPtr result));

        // The handler returns while the asynchronous body is still pending, as WM_ENDSESSION must.
        Assert.Equal(IntPtr.Zero, result);
        Assert.Equal(new[] { "checkpoint", "shutdown" }, order.ToArray());
        Assert.True(coordinator.IsShutdownRequested);
        releaseShutdown.TrySetResult(null);
    }

    [Fact]
    public void FormAndSessionEndedNotifications_CheckpointAndShutDownOnce()
    {
        int checkpointCount = 0;
        int shutdownCount = 0;
        var releaseShutdown = NewCompletionSource();
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            async () =>
            {
                Interlocked.Increment(ref shutdownCount);
                await releaseShutdown.Task;
            });

        // The form's WM_ENDSESSION and SystemEvents.SessionEnded may arrive in either order.
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, () => checkpointCount++, out _));
        coordinator.RequestAfterCheckpoint(() => checkpointCount++);
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, () => checkpointCount++, out _));

        Assert.Equal(1, checkpointCount);
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        releaseShutdown.TrySetResult(null);
    }

    [Fact]
    public void EndingSession_DuringRunningShutdown_CheckpointsOnce()
    {
        int checkpointCount = 0;
        int shutdownCount = 0;
        var releaseShutdown = NewCompletionSource();
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            async () =>
            {
                Interlocked.Increment(ref shutdownCount);
                await releaseShutdown.Task;
            });

        // A user Exit is still awaiting server stop or hardware close when the session ends. Its
        // final save sits behind those awaits, so the session end still writes the checkpoint.
        Task userExit = coordinator.RequestAsync();
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, () => checkpointCount++, out _));
        coordinator.RequestAfterCheckpoint(() => checkpointCount++);

        Assert.Equal(1, checkpointCount);
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.False(userExit.IsCompleted);
        releaseShutdown.TrySetResult(null);
    }

    [Fact]
    public async Task EndingSession_AfterShutdownCompleted_SkipsCheckpoint()
    {
        int checkpointCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => { });

        await coordinator.RequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, () => checkpointCount++, out _));

        Assert.Equal(0, checkpointCount);
    }

    [Fact]
    public void CloseMessage_IsSwallowedOnlyOnceShutdownIsRequested()
    {
        var releaseShutdown = NewCompletionSource();
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => releaseShutdown.Task);

        Assert.False(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_CLOSE, IntPtr.Zero, () => { }, out _));

        // Restart Manager sends WM_CLOSE right after a confirmed WM_ENDSESSION returns.
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_ENDSESSION, (IntPtr)1, () => { }, out _));
        Assert.True(coordinator.TryHandleShutdownMessage(
            UiShutdownCoordinator.WM_CLOSE, IntPtr.Zero, () => { }, out IntPtr result));

        Assert.Equal(IntPtr.Zero, result);
        releaseShutdown.TrySetResult(null);
    }

    [Fact]
    public void ThrowingCheckpoint_StillStartsShutdown()
    {
        var checkpointFailure = new InvalidOperationException("checkpoint failed");
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => Interlocked.Increment(ref shutdownCount));

        InvalidOperationException observed = Assert.Throws<InvalidOperationException>(
            () => coordinator.RequestAfterCheckpoint(() => throw checkpointFailure));

        Assert.Same(checkpointFailure, observed);
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Fact]
    public void BackgroundCheckpointRequest_NeverRunsCheckpointOffUiThread()
    {
        bool isOnUiThread = false;
        var queued = new ConcurrentQueue<Action>();
        int checkpointCount = 0;
        int shutdownCount = 0;
        var coordinator = new UiShutdownCoordinator(
            () => isOnUiThread,
            action => queued.Enqueue(action),
            () => Interlocked.Increment(ref shutdownCount));

        coordinator.RequestAfterCheckpoint(() => checkpointCount++);
        Assert.Equal(0, checkpointCount);
        Assert.Single(queued);

        // The UI thread can still checkpoint before it runs the queued body.
        isOnUiThread = true;
        coordinator.RequestAfterCheckpoint(() => checkpointCount++);
        Assert.Equal(1, checkpointCount);
        Assert.Equal(1, Volatile.Read(ref shutdownCount));

        Assert.True(queued.TryDequeue(out Action delayed));
        delayed();
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
    }

    [Theory]
    [InlineData(0x0010)] // WM_CLOSE before shutdown
    [InlineData(0x0112)] // WM_SYSCOMMAND
    [InlineData(0x001A)] // WM_WININICHANGE
    public void OtherMessages_AreLeftToTheCaller(int msg)
    {
        var coordinator = new UiShutdownCoordinator(
            () => true,
            _ => throw new InvalidOperationException("UI dispatch is not expected"),
            () => throw new InvalidOperationException("shutdown is not expected"));

        Assert.False(coordinator.TryHandleShutdownMessage(
            msg, (IntPtr)1, () => throw new InvalidOperationException("checkpoint is not expected"), out _));
        Assert.False(coordinator.IsShutdownRequested);
    }

    private static TaskCompletionSource<object> NewCompletionSource()
    {
        return new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
