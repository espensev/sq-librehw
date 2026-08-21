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
        var optionStarted = NewCompletionSource();
        var releaseOption = NewCompletionSource();
        var openStarted = NewCompletionSource();
        var initializationContext = new RecordingSynchronizationContext();
        SynchronizationContext openContext = null;
        var calls = new ConcurrentQueue<string>();
        var coordinator = CreateCoordinator(
            openAsync: _ =>
            {
                openContext = SynchronizationContext.Current;
                calls.Enqueue("Open");
                openStarted.TrySetResult(null);
                return Task.CompletedTask;
            },
            applyOption: (option, value) =>
            {
                calls.Enqueue($"{option}={value}:Start");
                optionStarted.TrySetResult(null);
                releaseOption.Task.GetAwaiter().GetResult();
                calls.Enqueue($"{option}={value}:End");
            });

        Task<bool> request = null;
        try
        {
            request = Task.Run(() => coordinator.RequestOption(HardwareOptionKind.Cpu, false));
            await optionStarted.Task.WaitAsync(Timeout);
            Assert.False(request.IsCompleted);

            Task initialization;
            SynchronizationContext originalContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(initializationContext);
                initialization = coordinator.InitializeAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }

            Assert.True(coordinator.IsInitializationInProgress);
            Assert.False(initialization.IsCompleted);
            Assert.False(openStarted.Task.IsCompleted);

            releaseOption.TrySetResult(null);
            Assert.True(await request.WaitAsync(Timeout));
            await initialization.WaitAsync(Timeout);
            await openStarted.Task.WaitAsync(Timeout);

            Assert.Equal(new[] { "Cpu=False:Start", "Cpu=False:End", "Open" }, calls);
            Assert.Same(initializationContext, openContext);
            Assert.True(Volatile.Read(ref initializationContext.PostCount) > 0);
            Assert.False(coordinator.IsInitializationInProgress);
        }
        finally
        {
            releaseOption.TrySetResult(null);
            if (request != null)
                await request.WaitAsync(Timeout);

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
        var optionStarted = NewCompletionSource();
        var releaseOption = NewCompletionSource();
        int resetCount = 0;
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            applyOption: (_, _) =>
            {
                optionStarted.TrySetResult(null);
                releaseOption.Task.GetAwaiter().GetResult();
            },
            reset: () => Interlocked.Increment(ref resetCount),
            closeAsync: () =>
            {
                Interlocked.Increment(ref closeCount);
                return Task.CompletedTask;
            });
        coordinator.StateChanged += () => throw new InvalidOperationException("observer failed");

        Task<bool> admittedOption = null;
        Task stop = null;
        try
        {
            admittedOption = Task.Run(
                () => coordinator.RequestOption(HardwareOptionKind.Cpu, false));
            await optionStarted.Task.WaitAsync(Timeout);
            Assert.False(admittedOption.IsCompleted);

            // The option callback is outside the coordinator lock, so reset admission stays
            // responsive. With no initialization, that reset waits on the lifecycle barrier.
            Assert.True(coordinator.RequestReset());
            coordinator.BeginStop();
            stop = coordinator.StopAsync();

            Assert.True(coordinator.IsStopping);
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));
            Assert.False(coordinator.RequestOption(HardwareOptionKind.Battery, true));
            Assert.False(coordinator.RequestReset());
            Assert.False(coordinator.TryBeginPoll());
            Assert.True(coordinator.InitializeAsync().IsCanceled);

            releaseOption.TrySetResult(null);
            Assert.True(await admittedOption.WaitAsync(Timeout));
            await stop.WaitAsync(Timeout);

            Assert.Equal(0, Volatile.Read(ref resetCount));
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseOption.TrySetResult(null);
            if (admittedOption != null)
                await admittedOption.WaitAsync(Timeout);

            await (stop ?? coordinator.StopAsync()).WaitAsync(Timeout);
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WaitsForInitializationOperationsAndActivePollBeforeClose()
    {
        await AssertConcurrentStopWaitsForAdmissionClosureAsync();
        await AssertStopWaitsForActiveInitializationAsync();
        await AssertStopWaitsForActiveOperationAndPollAsync();
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

    private static async Task AssertConcurrentStopWaitsForAdmissionClosureAsync()
    {
        var openStarted = NewCompletionSource();
        var cancellationStarted = NewCompletionSource();
        var releaseCancellation = NewCompletionSource();
        var releaseOpen = NewCompletionSource();
        CancellationTokenRegistration cancellationRegistration = default;
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            openAsync: async cancellationToken =>
            {
                cancellationRegistration = cancellationToken.Register(() =>
                {
                    cancellationStarted.TrySetResult(null);
                    releaseCancellation.Task.GetAwaiter().GetResult();
                });
                openStarted.TrySetResult(null);
                await releaseOpen.Task.ConfigureAwait(false);
            },
            closeAsync: () =>
            {
                Interlocked.Increment(ref closeCount);
                return Task.CompletedTask;
            });

        Task beginStop = null;
        Task stop = null;
        try
        {
            Task initialization = coordinator.InitializeAsync();
            await openStarted.Task.WaitAsync(Timeout);

            beginStop = Task.Run(coordinator.BeginStop);
            await cancellationStarted.Task.WaitAsync(Timeout);

            // This second stop caller sees _stopping while the first caller is still inside
            // CancellationTokenSource.Cancel. Admission closure keeps stop from running ahead.
            stop = coordinator.StopAsync();
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));

            // Let initialization finish while cancellation is still held. With no admission-
            // closed barrier, this second stop caller could now observe every drain idle and close.
            releaseOpen.TrySetResult(null);
            await initialization.WaitAsync(Timeout);
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));

            releaseCancellation.TrySetResult(null);
            await beginStop.WaitAsync(Timeout);
            await stop.WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseCancellation.TrySetResult(null);
            releaseOpen.TrySetResult(null);
            if (beginStop != null)
                await beginStop.WaitAsync(Timeout);

            await (stop ?? coordinator.StopAsync()).WaitAsync(Timeout);
            cancellationRegistration.Dispose();
            coordinator.Dispose();
        }
    }

    private static async Task AssertStopWaitsForActiveInitializationAsync()
    {
        var openStarted = NewCompletionSource();
        var releaseOpen = NewCompletionSource();
        int closeCount = 0;
        var coordinator = CreateCoordinator(
            openAsync: async _ =>
            {
                openStarted.TrySetResult(null);
                await releaseOpen.Task.ConfigureAwait(false);
            },
            closeAsync: () =>
            {
                Interlocked.Increment(ref closeCount);
                return Task.CompletedTask;
            });

        Task stop = null;
        try
        {
            Task initialization = coordinator.InitializeAsync();
            await openStarted.Task.WaitAsync(Timeout);

            stop = coordinator.StopAsync();
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref closeCount));

            releaseOpen.TrySetResult(null);
            await initialization.WaitAsync(Timeout);
            await stop.WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref closeCount));
        }
        finally
        {
            releaseOpen.TrySetResult(null);
            await (stop ?? coordinator.StopAsync()).WaitAsync(Timeout);
            coordinator.Dispose();
        }
    }

    private static async Task AssertStopWaitsForActiveOperationAndPollAsync()
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
