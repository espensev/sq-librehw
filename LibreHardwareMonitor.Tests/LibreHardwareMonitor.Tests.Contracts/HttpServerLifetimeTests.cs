// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class HttpServerLifetimeTests
{
    [Fact]
    public async Task StopBeforeStart_IsIdempotentAndNonBlocking()
    {
        var server = new HttpServer(null, null, "127.0.0.1", 0);

        if (server.PlatformNotSupported)
            return;

        Assert.True(await server.StopHttpListenerAsync());
        Assert.True(await server.StopHttpListenerAsync());
        await server.QuitAsync();
    }

    [Fact]
    public async Task HandlerPool_BoundsConcurrencyAndTracksQueuedWorkUntilCompletion()
    {
        var pool = new BoundedRequestHandlerPool(2);
        var releaseHandlers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int startedHandlers = 0;

        async Task SlowHandler(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref startedHandlers);
            await releaseHandlers.Task;
        }

        await pool.QueueAsync(SlowHandler, CancellationToken.None);
        await pool.QueueAsync(SlowHandler, CancellationToken.None);

        Task thirdQueue = pool.QueueAsync(SlowHandler, CancellationToken.None);
        await Task.Delay(50);

        Assert.False(thirdQueue.IsCompleted);
        Assert.Equal(2, Volatile.Read(ref startedHandlers));
        Assert.Equal(2, pool.ActiveCount);
        Assert.Equal(2, pool.PeakActiveCount);
        Assert.False(await pool.DrainAsync(TimeSpan.FromMilliseconds(25)));

        releaseHandlers.SetResult(true);
        await thirdQueue;

        Assert.True(await pool.DrainAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(3, Volatile.Read(ref startedHandlers));
        Assert.Equal(0, pool.ActiveCount);
        Assert.Equal(2, pool.PeakActiveCount);
    }

    [Fact]
    public async Task HandlerPool_CancellationIsObservedAndDrainWaitsForExit()
    {
        var pool = new BoundedRequestHandlerPool(1);
        using var cancellation = new CancellationTokenSource();

        await pool.QueueAsync(token => Task.Delay(Timeout.Infinite, token), cancellation.Token);
        Assert.Equal(1, pool.ActiveCount);
        Assert.False(await pool.DrainAsync(TimeSpan.FromMilliseconds(25)));

        cancellation.Cancel();

        Assert.True(await pool.DrainAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void HandlerPool_RejectsInvalidConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedRequestHandlerPool(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedRequestHandlerPool(-1));
    }
}
