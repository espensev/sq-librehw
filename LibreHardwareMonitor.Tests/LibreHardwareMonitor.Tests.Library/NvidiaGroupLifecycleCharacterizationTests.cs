// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Interop;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Characterization facts pinning the NVIDIA group lifecycle seams before their extraction:
/// lease-first unavailable transitions, deferred lease release on in-flight refresh, the bounded
/// monitor error queue, handle-diff commit ordering, and the bounded monitor join. These facts run
/// against the <see cref="NvidiaGroup" /> internal-constructor injection seams only; no real
/// hardware is touched.
/// </summary>
public sealed class NvidiaGroupLifecycleCharacterizationTests
{
    [Fact]
    public void CommitUnavailableHardware_ReleasesLeaseBeforeRemovedCallbackAndDeviceClose()
    {
        var timeline = new List<string>();
        var leaseOwner = new NvidiaML.LeaseOwner(() => true,
                                                 () => timeline.Add("release"));
        TestHardware hardware = null;
        var group = new NvidiaGroup(new TestSettings(),
                                    new SequenceEnumerator(true, false).TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        hardware = new TestHardware("gpu-unavailable-order",
                                                                    settings,
                                                                    () => timeline.Add("device-close"));
                                        return hardware;
                                    },
                                    leaseOwner.Acquire,
                                    leaseOwner.Release,
                                    monitorHardwareChanges: false);

        Assert.Equal(1, leaseOwner.LeaseCount);

        IHardware removedHardware = null;
        int leaseCountAtCallback = -1;
        int closeCountAtCallback = -1;
        bool hardwareEmptyAtCallback = false;
        group.HardwareRemoved += removed =>
        {
            timeline.Add("removed-callback");
            removedHardware = removed;
            leaseCountAtCallback = leaseOwner.LeaseCount;
            closeCountAtCallback = hardware.CloseCount;
            hardwareEmptyAtCallback = group.Hardware.Count == 0;
        };

        group.RefreshHardware();

        Assert.Equal(["release", "removed-callback", "device-close"], timeline);
        Assert.Same(hardware, removedHardware);
        Assert.Equal(0, leaseCountAtCallback);
        Assert.Equal(0, closeCountAtCallback);
        Assert.True(hardwareEmptyAtCallback);
        Assert.Equal(1, hardware.CloseCount);
        Assert.Equal(0, leaseOwner.LeaseCount);
        Assert.Empty(group.Hardware);

        group.Close();
    }

    [Fact]
    public async Task Close_DuringInFlightRefresh_DefersLeaseReleaseUntilRefreshCompletesOnRefreshThread()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        var enumerator = new HandleSequenceEnumerator([firstHandle], [firstHandle, secondHandle]);
        var createdHardware = new List<TestHardware>();
        int releaseCount = 0;
        int releaseThreadId = -1;
        int refreshThreadId = -1;
        int closeThreadId = -1;
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        int index = createdHardware.Count;
                                        var hardware = new TestHardware($"gpu-{index}",
                                                                        settings,
                                                                        index == 0
                                                                            ? () => Interlocked.Exchange(ref closeThreadId, Environment.CurrentManagedThreadId)
                                                                            : null);
                                        createdHardware.Add(hardware);

                                        if (index == 1)
                                        {
                                            Interlocked.Exchange(ref refreshThreadId, Environment.CurrentManagedThreadId);
                                            factoryEntered.Set();
                                            releaseFactory.Wait();
                                        }

                                        return hardware;
                                    },
                                    () => true,
                                    () =>
                                    {
                                        Interlocked.Exchange(ref releaseThreadId, Environment.CurrentManagedThreadId);
                                        Interlocked.Increment(ref releaseCount);
                                    },
                                    monitorHardwareChanges: false);

        Assert.Single(group.Hardware);

        Task refreshTask = Task.Run(group.RefreshHardware);
        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5)));

        Task closeTask = Task.Run(group.Close);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, Volatile.Read(ref releaseCount));
        Assert.Equal(1, createdHardware[0].CloseCount);
        Assert.Empty(group.Hardware);

        releaseFactory.Set();
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, Volatile.Read(ref releaseCount));
        Assert.Equal(Volatile.Read(ref refreshThreadId), Volatile.Read(ref releaseThreadId));
        Assert.NotEqual(Volatile.Read(ref closeThreadId), Volatile.Read(ref releaseThreadId));
        Assert.Equal(1, createdHardware[1].CloseCount);
        Assert.Empty(group.Hardware);

        group.Close();
    }

    [Fact]
    public void MonitorErrorQueue_RetainsOnlyLastEightErrorsWithAccurateCountAndLastError()
    {
        const int thrownErrors = 12;
        using var drain = new ManualResetEventSlim();
        var enumerator = new ThrowingThenBlockingEnumerator(thrownErrors, drain);
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) => new TestHardware("gpu-errors", settings),
                                    () => true,
                                    () => { },
                                    monitorHardwareChanges: true,
                                    monitorInterval: TimeSpan.FromMilliseconds(10));

        try
        {
            Assert.True(SpinWait.SpinUntil(() => group.MonitorErrorCount >= thrownErrors, TimeSpan.FromSeconds(10)));

            // The enumerator is now blocked, so the retained window is stable.
            Assert.Equal(thrownErrors, group.MonitorErrorCount);

            IReadOnlyList<Exception> errors = group.MonitorErrors;
            Assert.Equal(8, errors.Count);

            for (int i = 0; i < errors.Count; i++)
                Assert.Equal($"cycle-{thrownErrors - 8 + i + 1}", errors[i].Message);

            Assert.Same(errors[errors.Count - 1], group.LastMonitorError);
            Assert.True(Assert.IsAssignableFrom<IList<Exception>>(errors).IsReadOnly);
        }
        finally
        {
            drain.Set();
            group.Close();
        }

        Assert.Equal(thrownErrors, group.MonitorErrorCount);
    }

    [Fact]
    public void RefreshHardware_CommitsAdditionsAndPublishesSnapshotBeforeClosingRemovals()
    {
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        NvApi.NvPhysicalGpuHandle thirdHandle = CreateHandle(3);
        var enumerator = new HandleSequenceEnumerator([firstHandle, secondHandle], [secondHandle, thirdHandle]);
        var timeline = new List<string>();
        var createdHardware = new List<TestHardware>();
        IReadOnlyList<IHardware> hardwareAtFactory = null;
        NvidiaGroup group = null;
        group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        int index = createdHardware.Count;
                                        var hardware = new TestHardware($"gpu-{index}",
                                                                        settings,
                                                                        () => timeline.Add($"close-{index}"));
                                        createdHardware.Add(hardware);

                                        if (index == 2)
                                        {
                                            timeline.Add("create-2");
                                            hardwareAtFactory = group.Hardware;
                                        }

                                        return hardware;
                                    },
                                    () => true,
                                    () => { },
                                    monitorHardwareChanges: false);

        IReadOnlyList<IHardware> initialSnapshot = group.Hardware;
        IHardware addedHardware = null;
        IHardware removedHardware = null;
        IReadOnlyList<IHardware> hardwareAtAdded = null;
        IReadOnlyList<IHardware> hardwareAtRemoved = null;
        int closeCountOfFirstAtAdded = -1;
        int closeCountOfFirstAtRemoved = -1;
        group.HardwareAdded += added =>
        {
            timeline.Add("added-2");
            addedHardware = added;
            hardwareAtAdded = group.Hardware;
            closeCountOfFirstAtAdded = createdHardware[0].CloseCount;
        };
        group.HardwareRemoved += removed =>
        {
            timeline.Add("removed-0");
            removedHardware = removed;
            hardwareAtRemoved = group.Hardware;
            closeCountOfFirstAtRemoved = createdHardware[0].CloseCount;
        };

        group.RefreshHardware();

        Assert.Equal(["create-2", "added-2", "removed-0", "close-0"], timeline);
        Assert.Same(createdHardware[2], addedHardware);
        Assert.Same(createdHardware[0], removedHardware);

        // The addition was created before the commit, against the pre-refresh snapshot.
        Assert.NotNull(hardwareAtFactory);
        Assert.Equal(2, hardwareAtFactory.Count);
        Assert.Same(createdHardware[0], hardwareAtFactory[0]);
        Assert.Same(createdHardware[1], hardwareAtFactory[1]);

        // One immutable snapshot containing the retained and added hardware was published
        // before either notification ran, and the removal close ran after its notification.
        AssertSnapshot(hardwareAtAdded, createdHardware[1], createdHardware[2]);
        AssertSnapshot(hardwareAtRemoved, createdHardware[1], createdHardware[2]);
        Assert.Equal(0, closeCountOfFirstAtAdded);
        Assert.Equal(0, closeCountOfFirstAtRemoved);

        Assert.Equal(1, createdHardware[0].CloseCount);
        Assert.Equal(0, createdHardware[1].CloseCount);
        Assert.Equal(0, createdHardware[2].CloseCount);
        AssertSnapshot(group.Hardware, createdHardware[1], createdHardware[2]);

        // The pre-refresh snapshot is immutable and still observes the old set.
        Assert.Equal(2, initialSnapshot.Count);
        Assert.Same(createdHardware[0], initialSnapshot[0]);
        Assert.Same(createdHardware[1], initialSnapshot[1]);

        group.Close();
    }

    [Fact]
    public async Task Close_WhenMonitorCycleIsInFlight_JoinsMonitorWithBoundedWait()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        var enumerator = new HandleSequenceEnumerator([firstHandle], [firstHandle, secondHandle]);
        var createdHardware = new List<TestHardware>();
        int releaseCount = 0;
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        int index = createdHardware.Count;
                                        var hardware = new TestHardware($"gpu-{index}", settings);
                                        createdHardware.Add(hardware);

                                        if (index == 1)
                                        {
                                            factoryEntered.Set();
                                            releaseFactory.Wait();
                                        }

                                        return hardware;
                                    },
                                    () => true,
                                    () => Interlocked.Increment(ref releaseCount),
                                    monitorHardwareChanges: true,
                                    monitorInterval: TimeSpan.FromMilliseconds(10));

        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5)));

        Task closeTask = Task.Run(group.Close);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Close returned while the monitor task was still blocked: the join is bounded, and the
        // lease release is deferred until the in-flight refresh completes.
        Assert.Equal(0, Volatile.Read(ref releaseCount));
        Assert.Equal(1, createdHardware[0].CloseCount);
        Assert.Empty(group.Hardware);

        releaseFactory.Set();

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref releaseCount) == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, createdHardware[1].CloseCount);
        Assert.Equal(0, group.MonitorErrorCount);

        group.Close();
    }

    [Fact]
    public void Close_FromHardwareAddedCallbackOnMonitorTask_SkipsMonitorJoin()
    {
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        var enumerator = new HandleSequenceEnumerator([firstHandle], [firstHandle, secondHandle]);
        var createdHardware = new List<TestHardware>();
        int releaseCount = 0;
        int monitorCycleThreadId = -1;
        int closeThreadId = -1;
        using var closeReturned = new ManualResetEventSlim();
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        int index = createdHardware.Count;

                                        if (index == 1)
                                            Interlocked.Exchange(ref monitorCycleThreadId, Environment.CurrentManagedThreadId);

                                        var hardware = new TestHardware($"gpu-{index}", settings);
                                        createdHardware.Add(hardware);
                                        return hardware;
                                    },
                                    () => true,
                                    () => Interlocked.Increment(ref releaseCount),
                                    monitorHardwareChanges: true,
                                    monitorInterval: TimeSpan.FromMilliseconds(10));

        group.HardwareAdded += _ =>
        {
            Interlocked.Exchange(ref closeThreadId, Environment.CurrentManagedThreadId);
            group.Close();
            closeReturned.Set();
        };

        Assert.True(closeReturned.Wait(TimeSpan.FromSeconds(5)));

        // Close really ran on the monitor task itself; the self-join was skipped and Close did
        // not throw back into the monitor error queue.
        Assert.Equal(Volatile.Read(ref monitorCycleThreadId), Volatile.Read(ref closeThreadId));
        Assert.NotEqual(Environment.CurrentManagedThreadId, Volatile.Read(ref closeThreadId));

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref releaseCount) == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, createdHardware[0].CloseCount);
        Assert.Equal(1, createdHardware[1].CloseCount);
        Assert.Empty(group.Hardware);
        Assert.Equal(0, group.MonitorErrorCount);

        group.Close();
    }

    private static void AssertSnapshot(IReadOnlyList<IHardware> snapshot, params IHardware[] expected)
    {
        Assert.NotNull(snapshot);
        Assert.Equal(expected.Length, snapshot.Count);

        for (int i = 0; i < expected.Length; i++)
            Assert.Same(expected[i], snapshot[i]);
    }

    private static IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> EmptyDisplayHandles()
    {
        return new Dictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>();
    }

    private static NvApi.NvPhysicalGpuHandle CreateHandle(int value)
    {
        Span<IntPtr> source = stackalloc IntPtr[1];
        source[0] = new IntPtr(value);
        return MemoryMarshal.Cast<IntPtr, NvApi.NvPhysicalGpuHandle>(source)[0];
    }

    private sealed class SequenceEnumerator
    {
        private readonly Queue<bool> _availability;

        public SequenceEnumerator(params bool[] available)
        {
            _availability = new Queue<bool>(available);
        }

        public bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            bool available = _availability.Dequeue();
            handles = available ? [default] : Array.Empty<NvApi.NvPhysicalGpuHandle>();
            count = handles.Length;
            return available;
        }
    }

    private sealed class HandleSequenceEnumerator
    {
        private readonly Queue<NvApi.NvPhysicalGpuHandle[]> _handles;

        public HandleSequenceEnumerator(params NvApi.NvPhysicalGpuHandle[][] handles)
        {
            _handles = new Queue<NvApi.NvPhysicalGpuHandle[]>(handles);
        }

        public bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            handles = _handles.Dequeue();
            count = handles.Length;
            return count > 0;
        }
    }

    private sealed class ThrowingThenBlockingEnumerator
    {
        private readonly ManualResetEventSlim _drain;
        private readonly int _thrownErrors;
        private int _callCount;

        public ThrowingThenBlockingEnumerator(int thrownErrors, ManualResetEventSlim drain)
        {
            _thrownErrors = thrownErrors;
            _drain = drain;
        }

        public bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            int call = Interlocked.Increment(ref _callCount);

            if (call == 1)
            {
                handles = [CreateHandle(1)];
                count = 1;
                return true;
            }

            if (call <= _thrownErrors + 1)
                throw new InvalidOperationException($"cycle-{call - 1}");

            _drain.Wait();
            handles = [CreateHandle(1)];
            count = 1;
            return true;
        }
    }

    private sealed class TestHardware : HardwareBase
    {
        private readonly Action _onClose;
        private int _closeCount;

        public TestHardware
        (
            string identifier,
            ISettings settings,
            Action onClose = null)
            : base("Test NVIDIA GPU", new Identifier(identifier), settings)
        {
            _onClose = onClose;
        }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public override HardwareType HardwareType => HardwareType.GpuNvidia;

        public override void Close()
        {
            Interlocked.Increment(ref _closeCount);
            _onClose?.Invoke();
            base.Close();
        }

        public override void Update()
        {
        }
    }

    private sealed class TestSettings : ISettings
    {
        public bool Contains(string name) => false;

        public string GetValue(string name, string value) => value;

        public void Remove(string name) { }

        public void SetValue(string name, string value) { }
    }
}
