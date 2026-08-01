// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class ApplicationLifecycleCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RequestOption_BeforeInitialization_AppliesSynchronously()
    {
        var calls = new ConcurrentQueue<string>();
        var coordinator = CreateCoordinator(
            applyOption: (option, value) => calls.Enqueue($"{option}={value}"));

        try
        {
            Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
            Assert.Equal(new[] { "Cpu=False" }, calls);
            Assert.False(coordinator.IsInitializationInProgress);
        }
        finally
        {
            await StopAndDisposeAsync(coordinator);
        }
    }

    [Fact]
    public async Task InitializeAsync_BlocksQueuedOptionUntilOpenCompletes()
    {
        var openStarted = NewCompletionSource();
        var releaseOpen = NewCompletionSource();
        var optionApplied = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        var coordinator = CreateCoordinator(
            openAsync: async _ =>
            {
                calls.Enqueue("OpenStart");
                openStarted.TrySetResult(null);
                await releaseOpen.Task.ConfigureAwait(false);
                calls.Enqueue("OpenEnd");
            },
            applyOption: (option, value) =>
            {
                calls.Enqueue($"{option}={value}");
                optionApplied.TrySetResult(null);
            });

        try
        {
            Task initialization = coordinator.InitializeAsync();
            await openStarted.Task.WaitAsync(Timeout);
            Assert.True(coordinator.IsInitializationInProgress);
            Assert.True(coordinator.RequestOption(HardwareOptionKind.Memory, true));
            Assert.False(optionApplied.Task.IsCompleted);

            releaseOpen.TrySetResult(null);
            await initialization.WaitAsync(Timeout);
            await optionApplied.Task.WaitAsync(Timeout);

            Assert.Equal(new[] { "OpenStart", "OpenEnd", "Memory=True" }, calls);
            Assert.False(coordinator.IsInitializationInProgress);
        }
        finally
        {
            releaseOpen.TrySetResult(null);
            await StopAndDisposeAsync(coordinator);
        }
    }

    [Fact]
    public async Task InitializeAsync_FailureReleasesBarrierAndStopStillClosesOnce()
    {
        var openFailure = new InvalidOperationException("open failed");
        var openStarted = NewCompletionSource();
        var releaseOpen = NewCompletionSource();
        var optionApplied = NewCompletionSource();
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            openAsync: async _ =>
            {
                openStarted.TrySetResult(null);
                await releaseOpen.Task.ConfigureAwait(false);
                throw openFailure;
            },
            applyOption: (_, _) => optionApplied.TrySetResult(null),
            closeAsync: () =>
            {
                Interlocked.Increment(ref closeCount);
                return Task.CompletedTask;
            });

        try
        {
            Task initialization = coordinator.InitializeAsync();
            await openStarted.Task.WaitAsync(Timeout);
            Assert.True(coordinator.RequestOption(HardwareOptionKind.Gpu, false));

            releaseOpen.TrySetResult(null);
            InvalidOperationException observed =
                await Assert.ThrowsAsync<InvalidOperationException>(() => initialization);
            Assert.Same(openFailure, observed);
            await optionApplied.Task.WaitAsync(Timeout);

            await coordinator.StopAsync().WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseOpen.TrySetResult(null);
            await coordinator.StopAsync().WaitAsync(Timeout);
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task TryBeginPoll_DropsBusyTicksUntilCompletion()
    {
        var coordinator = CreateCoordinator();

        try
        {
            Assert.True(coordinator.TryBeginPoll());
            Assert.False(coordinator.TryBeginPoll());

            coordinator.CompletePoll();
            Assert.True(coordinator.TryBeginPoll());
            Assert.False(coordinator.TryBeginPoll());
        }
        finally
        {
            coordinator.CompletePoll();
            await StopAndDisposeAsync(coordinator);
        }
    }

    [Fact]
    public async Task ActivePoll_DoesNotSerializeOptionWork()
    {
        var optionApplied = NewCompletionSource();
        var coordinator = CreateCoordinator(
            applyOption: (_, _) => optionApplied.TrySetResult(null));

        try
        {
            await coordinator.InitializeAsync().WaitAsync(Timeout);
            Assert.True(coordinator.TryBeginPoll());
            Assert.True(coordinator.RequestOption(HardwareOptionKind.Storage, false));

            await optionApplied.Task.WaitAsync(Timeout);
            Assert.False(coordinator.TryBeginPoll());
        }
        finally
        {
            coordinator.CompletePoll();
            await StopAndDisposeAsync(coordinator);
        }
    }

    [Fact]
    public async Task BeginStop_RejectsNewOptionResetAndPollAdmission()
    {
        int resetCount = 0;
        var coordinator = CreateCoordinator(reset: () => Interlocked.Increment(ref resetCount));
        coordinator.StateChanged += () => throw new InvalidOperationException("observer failed");

        // Exercise the no-initialization edge directly: the pending reset is waiting on the
        // initialization barrier when stop must cancel and release it without running reset.
        Assert.True(coordinator.RequestReset());
        coordinator.BeginStop();

        Assert.True(coordinator.IsStopping);
        Assert.False(coordinator.RequestOption(HardwareOptionKind.Battery, true));
        Assert.False(coordinator.RequestReset());
        Assert.False(coordinator.TryBeginPoll());
        Assert.True(coordinator.InitializeAsync().IsCanceled);

        await StopAndDisposeAsync(coordinator);
        Assert.Equal(0, Volatile.Read(ref resetCount));
    }

    [Fact]
    public async Task StopAsync_WaitsForInitializationOperationsAndActivePollBeforeClose()
    {
        var optionStarted = NewCompletionSource();
        var releaseOption = NewCompletionSource();
        var optionFinished = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            openAsync: _ =>
            {
                calls.Enqueue("Open");
                return Task.CompletedTask;
            },
            applyOption: (_, _) =>
            {
                calls.Enqueue("OptionStart");
                optionStarted.TrySetResult(null);
                releaseOption.Task.GetAwaiter().GetResult();
                calls.Enqueue("OptionEnd");
                optionFinished.TrySetResult(null);
            },
            closeAsync: () =>
            {
                calls.Enqueue("Close");
                Interlocked.Increment(ref closeCount);
                return Task.CompletedTask;
            });

        Task stop = null;
        try
        {
            await coordinator.InitializeAsync().WaitAsync(Timeout);
            Assert.True(coordinator.RequestOption(HardwareOptionKind.Network, false));
            await optionStarted.Task.WaitAsync(Timeout);
            Assert.True(coordinator.TryBeginPoll());

            stop = coordinator.StopAsync();
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));

            releaseOption.TrySetResult(null);
            await optionFinished.Task.WaitAsync(Timeout);
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));

            coordinator.CompletePoll();
            await stop.WaitAsync(Timeout);

            Assert.Equal(new[] { "Open", "OptionStart", "OptionEnd", "Close" }, calls);
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseOption.TrySetResult(null);
            coordinator.CompletePoll();
            await (stop ?? coordinator.StopAsync()).WaitAsync(Timeout);
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentStopAsync_ClosesExactlyOnceAndSharesCompletion()
    {
        var closeStarted = NewCompletionSource();
        var releaseClose = NewCompletionSource();
        var stopContext = new RecordingSynchronizationContext();
        SynchronizationContext closeContext = null;
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            closeAsync: async () =>
            {
                closeContext = SynchronizationContext.Current;
                Interlocked.Increment(ref closeCount);
                closeStarted.TrySetResult(null);
                await releaseClose.Task.ConfigureAwait(false);
            });

        Task first = null;
        try
        {
            SynchronizationContext originalContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(stopContext);
                first = coordinator.StopAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }

            Task second = coordinator.StopAsync();
            Assert.Same(first, second);

            await closeStarted.Task.WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref closeCount));
            Assert.Same(stopContext, closeContext);
            Assert.True(Volatile.Read(ref stopContext.PostCount) > 0);
            Assert.False(first.IsCompleted);
            Assert.Same(first, coordinator.StopAsync());

            releaseClose.TrySetResult(null);
            await Task.WhenAll(first, second).WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseClose.TrySetResult(null);
            await (first ?? coordinator.StopAsync()).WaitAsync(Timeout);
            coordinator.Dispose();
        }
    }

    private static ApplicationLifecycleCoordinator CreateCoordinator(
        Func<CancellationToken, Task> openAsync = null,
        Action<HardwareOptionKind, bool> applyOption = null,
        Action reset = null,
        Func<Task> closeAsync = null)
    {
        return new ApplicationLifecycleCoordinator(
            openAsync ?? (_ => Task.CompletedTask),
            applyOption ?? ((_, _) => { }),
            reset ?? (() => { }),
            closeAsync ?? (() => Task.CompletedTask));
    }

    private static async Task StopAndDisposeAsync(ApplicationLifecycleCoordinator coordinator)
    {
        await coordinator.StopAsync().WaitAsync(Timeout);
        coordinator.Dispose();
    }

    private static TaskCompletionSource<object> NewCompletionSource()
    {
        return new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        internal int PostCount;

        public override void Post(SendOrPostCallback callback, object state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SynchronizationContext originalContext = Current;
                try
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(originalContext);
                }
            });
        }
    }
}
