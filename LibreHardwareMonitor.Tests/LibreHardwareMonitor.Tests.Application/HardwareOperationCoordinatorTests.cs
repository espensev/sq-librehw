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

public sealed class HardwareOperationCoordinatorTests
{
    [Fact]
    public async Task OptionRequests_AcceptSecondWhileBusy_AndStayBusyUntilBothApply()
    {
        var firstStarted = NewCompletionSource();
        var releaseFirst = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        var busyStates = new ConcurrentQueue<bool>();

        HardwareOperationCoordinator coordinator = null;
        coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (option == HardwareOptionKind.Cpu)
                {
                    firstStarted.TrySetResult(null);
                    await releaseFirst.Task.ConfigureAwait(false);
                }
            },
            _ => Task.CompletedTask,
            CancellationToken.None);
        coordinator.StateChanged += () => busyStates.Enqueue(coordinator.IsBusy);

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Memory, false));
        Assert.True(coordinator.IsBusy);
        releaseFirst.TrySetResult(null);

        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False", "Memory=False" }, calls);
        Assert.False(coordinator.IsBusy);
        bool[] states = busyStates.ToArray();
        Assert.NotEmpty(states);
        Assert.False(states[^1]);
        Assert.DoesNotContain(false, states.Take(states.Length - 1));
    }

    [Fact]
    public async Task PendingOptions_CoalescePerKey_AndKeepLatestRequestOrder()
    {
        var firstStarted = NewCompletionSource();
        var releaseFirst = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();

        var coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (option == HardwareOptionKind.Cpu)
                {
                    firstStarted.TrySetResult(null);
                    await releaseFirst.Task.ConfigureAwait(false);
                }
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        coordinator.RequestOption(HardwareOptionKind.Cpu, false);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.RequestOption(HardwareOptionKind.Memory, false);
        coordinator.RequestOption(HardwareOptionKind.Gpu, false);
        coordinator.RequestOption(HardwareOptionKind.Memory, true);
        releaseFirst.TrySetResult(null);

        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False", "Gpu=False", "Memory=True" }, calls);
    }

    [Fact]
    public async Task ResetRequests_CoalesceAndRunAfterPendingOptions()
    {
        var firstStarted = NewCompletionSource();
        var releaseFirst = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();

        var coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (option == HardwareOptionKind.Cpu)
                {
                    firstStarted.TrySetResult(null);
                    await releaseFirst.Task.ConfigureAwait(false);
                }
            },
            _ =>
            {
                calls.Enqueue("Reset");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        coordinator.RequestOption(HardwareOptionKind.Cpu, false);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.RequestReset());
        Assert.False(coordinator.RequestReset());
        coordinator.RequestOption(HardwareOptionKind.Memory, false);
        releaseFirst.TrySetResult(null);

        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False", "Memory=False", "Reset" }, calls);
        Assert.False(coordinator.HasResetWork);
    }

    [Fact]
    public async Task ResetRequest_RemainsPendingAcrossInitializationBarrier()
    {
        var resetExecutorStarted = NewCompletionSource();
        var initializationComplete = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();

        var coordinator = new HardwareOperationCoordinator(
            (_, _, _) => Task.CompletedTask,
            async _ =>
            {
                resetExecutorStarted.TrySetResult(null);
                await initializationComplete.Task.ConfigureAwait(false);
                calls.Enqueue("Reset");
            },
            CancellationToken.None);

        Assert.True(coordinator.RequestReset());
        await resetExecutorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(calls);
        Assert.True(coordinator.HasResetWork);

        initializationComplete.TrySetResult(null);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Reset" }, calls);
        Assert.False(coordinator.HasResetWork);
    }

    [Fact]
    public async Task Cancellation_DropsOnlyPendingWorkAndDrainsCleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var firstStarted = NewCompletionSource();
        var calls = new ConcurrentQueue<HardwareOptionKind>();

        var coordinator = new HardwareOperationCoordinator(
            async (option, _, token) =>
            {
                calls.Enqueue(option);
                firstStarted.TrySetResult(null);
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            },
            _ => Task.CompletedTask,
            cancellation.Token);

        coordinator.RequestOption(HardwareOptionKind.Cpu, false);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.RequestOption(HardwareOptionKind.Memory, false);
        cancellation.Cancel();

        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { HardwareOptionKind.Cpu }, calls);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task ActiveOption_SameKeyBurstRunsOneLatestFollowUp()
    {
        var firstStarted = NewCompletionSource();
        var releaseFirst = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        int callCount = 0;

        var coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstStarted.TrySetResult(null);
                    await releaseFirst.Task.ConfigureAwait(false);
                }
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, true));
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, true));
        Assert.Single(calls);

        releaseFirst.TrySetResult(null);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False", "Cpu=True" }, calls);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task ResetRequestedWhileActive_CoalescesOneFollowUpAfterOptionsQueuedDuringReset()
    {
        var firstResetStarted = NewCompletionSource();
        var releaseFirstReset = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        int resetCount = 0;

        var coordinator = new HardwareOperationCoordinator(
            (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                return Task.CompletedTask;
            },
            async _ =>
            {
                int currentReset = Interlocked.Increment(ref resetCount);
                calls.Enqueue($"Reset{currentReset}");
                if (currentReset == 1)
                {
                    firstResetStarted.TrySetResult(null);
                    await releaseFirstReset.Task.ConfigureAwait(false);
                }
            },
            CancellationToken.None);

        Assert.True(coordinator.RequestReset());
        await firstResetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.HasResetWork);

        Assert.True(coordinator.RequestReset());
        Assert.False(coordinator.RequestReset());
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Memory, false));
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Gpu, true));

        releaseFirstReset.TrySetResult(null);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Reset1", "Memory=False", "Gpu=True", "Reset2" }, calls);
        Assert.Equal(2, Volatile.Read(ref resetCount));
        Assert.False(coordinator.HasResetWork);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task ExecutorFailures_RaiseTypedEventsAndContinueOrderedDrain()
    {
        var optionExecutorStarted = NewCompletionSource();
        var releaseOptionExecutor = NewCompletionSource();
        var resetExecutorStarted = NewCompletionSource();
        var releaseResetExecutor = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();
        var optionFailures = new ConcurrentQueue<(HardwareOptionKind Option, bool Value, Exception Error)>();
        var resetFailures = new ConcurrentQueue<Exception>();
        var optionFailure = new InvalidOperationException("option failed");
        var resetFailure = new InvalidOperationException("reset failed");

        var coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (option == HardwareOptionKind.Cpu)
                {
                    optionExecutorStarted.TrySetResult(null);
                    await releaseOptionExecutor.Task.ConfigureAwait(false);
                    throw optionFailure;
                }
            },
            async _ =>
            {
                calls.Enqueue("Reset");
                resetExecutorStarted.TrySetResult(null);
                await releaseResetExecutor.Task.ConfigureAwait(false);
                throw resetFailure;
            },
            CancellationToken.None);
        coordinator.OptionFailed += (option, value, error) => optionFailures.Enqueue((option, value, error));
        coordinator.ResetFailed += error => resetFailures.Enqueue(error);

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
        await optionExecutorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.RequestReset());
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Memory, true));

        releaseOptionExecutor.TrySetResult(null);
        await resetExecutorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Gpu, false));

        releaseResetExecutor.TrySetResult(null);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False", "Memory=True", "Reset", "Gpu=False" }, calls);
        Assert.True(optionFailures.TryDequeue(out var observedOptionFailure));
        Assert.Equal(HardwareOptionKind.Cpu, observedOptionFailure.Option);
        Assert.False(observedOptionFailure.Value);
        Assert.Same(optionFailure, observedOptionFailure.Error);
        Assert.Empty(optionFailures);
        Assert.True(resetFailures.TryDequeue(out Exception observedResetFailure));
        Assert.Same(resetFailure, observedResetFailure);
        Assert.Empty(resetFailures);
        Assert.False(coordinator.HasResetWork);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task CancellationAfterNonCooperativeActiveOption_DropsPendingAndRejectsNewRequests()
    {
        using var cancellation = new CancellationTokenSource();
        var activeOptionStarted = NewCompletionSource();
        var releaseActiveOption = NewCompletionSource();
        var calls = new ConcurrentQueue<string>();

        var coordinator = new HardwareOperationCoordinator(
            async (option, value, _) =>
            {
                calls.Enqueue($"{option}={value}");
                if (option == HardwareOptionKind.Cpu)
                {
                    activeOptionStarted.TrySetResult(null);
                    await releaseActiveOption.Task.ConfigureAwait(false);
                }
            },
            _ =>
            {
                calls.Enqueue("Reset");
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.True(coordinator.RequestOption(HardwareOptionKind.Cpu, false));
        await activeOptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.RequestOption(HardwareOptionKind.Memory, true));
        Assert.True(coordinator.RequestReset());

        Task drain = coordinator.WhenIdleAsync();
        cancellation.Cancel();

        Assert.False(drain.IsCompleted);
        Assert.True(coordinator.IsBusy);
        Assert.False(coordinator.RequestOption(HardwareOptionKind.Gpu, true));
        Assert.False(coordinator.RequestReset());

        releaseActiveOption.TrySetResult(null);
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "Cpu=False" }, calls);
        Assert.False(coordinator.HasResetWork);
        Assert.False(coordinator.IsBusy);
    }

    private static TaskCompletionSource<object> NewCompletionSource()
    {
        return new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
