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

        Task request = Task.Run(coordinator.Request);
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
}
