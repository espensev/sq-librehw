// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Interop;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class NvidiaGroupSnapshotTests
{
    [Fact]
    public void Hardware_CapturedSnapshotsRemainStableAcrossRemoveAddAndClose()
    {
        var enumerator = new SequenceEnumerator(true, false, true);
        var createdHardware = new List<TestHardware>();
        int nvidiaMlCloseCount = 0;
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        var hardware = new TestHardware($"gpu-{createdHardware.Count}", settings);
                                        createdHardware.Add(hardware);
                                        return hardware;
                                    },
                                    () => true,
                                    () => nvidiaMlCloseCount++,
                                    monitorHardwareChanges: false);

        IReadOnlyList<IHardware> initialSnapshot = group.Hardware;
        Assert.Single(initialSnapshot);
        AssertReadOnly(initialSnapshot);

        IHardware removedHardware = null;
        group.HardwareRemoved += hardware => removedHardware = hardware;
        group.RefreshHardware();

        IReadOnlyList<IHardware> removedSnapshot = group.Hardware;
        Assert.Empty(removedSnapshot);
        Assert.Single(initialSnapshot);
        Assert.Same(initialSnapshot[0], removedHardware);
        Assert.Equal(1, createdHardware[0].CloseCount);

        IHardware addedHardware = null;
        group.HardwareAdded += hardware => addedHardware = hardware;
        group.RefreshHardware();

        IReadOnlyList<IHardware> addedSnapshot = group.Hardware;
        Assert.Single(addedSnapshot);
        Assert.Empty(removedSnapshot);
        Assert.Single(initialSnapshot);
        Assert.Same(addedSnapshot[0], addedHardware);
        Assert.NotSame(initialSnapshot[0], addedSnapshot[0]);
        AssertReadOnly(addedSnapshot);

        group.Close();
        group.Close();

        Assert.Empty(group.Hardware);
        Assert.Single(initialSnapshot);
        Assert.Empty(removedSnapshot);
        Assert.Single(addedSnapshot);
        Assert.Equal(1, createdHardware[0].CloseCount);
        Assert.Equal(1, createdHardware[1].CloseCount);
        Assert.Equal(2, nvidiaMlCloseCount);
    }

    [Fact]
    public async Task Close_ReturnsWhileEnumerationIsBlocked_AndLateRefreshCannotAcquireOrRepublish()
    {
        using var refreshEntered = new ManualResetEventSlim();
        using var releaseRefresh = new ManualResetEventSlim();
        var enumerator = new BlockingEnumerator(refreshEntered, releaseRefresh);
        TestHardware createdHardware = null;
        int nativeInitializeCount = 0;
        int nativeShutdownCount = 0;
        var leaseOwner = new NvidiaML.LeaseOwner(() =>
                                                {
                                                    Interlocked.Increment(ref nativeInitializeCount);
                                                    return true;
                                                },
                                                () => Interlocked.Increment(ref nativeShutdownCount));
        int hardwareAddedCount = 0;
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) => createdHardware = new TestHardware("gpu-race", settings),
                                    leaseOwner.Acquire,
                                    leaseOwner.Release,
                                    monitorHardwareChanges: true,
                                    monitorInterval: TimeSpan.FromMilliseconds(10));

        group.HardwareAdded += _ => Interlocked.Increment(ref hardwareAddedCount);

        Assert.NotNull(createdHardware);
        Assert.Equal(1, leaseOwner.LeaseCount);
        Assert.Equal(1, Volatile.Read(ref nativeInitializeCount));
        Assert.Equal(0, Volatile.Read(ref nativeShutdownCount));
        Assert.True(refreshEntered.Wait(TimeSpan.FromSeconds(5)));

        Task closeTask = Task.Run(group.Close);
        try
        {
            await closeTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(group.Hardware);
            Assert.Equal(1, createdHardware.CloseCount);
            Assert.Equal(0, leaseOwner.LeaseCount);
            Assert.Equal(1, Volatile.Read(ref nativeInitializeCount));
            Assert.Equal(1, Volatile.Read(ref nativeShutdownCount));

            releaseRefresh.Set();
            group.RefreshHardware();

            Assert.Equal(0, leaseOwner.LeaseCount);
            Assert.Equal(1, Volatile.Read(ref nativeInitializeCount));
            Assert.Equal(1, Volatile.Read(ref nativeShutdownCount));
            Assert.Equal(0, Volatile.Read(ref hardwareAddedCount));
            Assert.Empty(group.Hardware);
        }
        finally
        {
            releaseRefresh.Set();
            group.Close();
        }
    }

    [Fact]
    public async Task NvidiaMlLease_ReleasesBeforeBlockingDeviceCloseAndUnavailableCallback()
    {
        using (var closeEntered = new ManualResetEventSlim())
        using (var releaseClose = new ManualResetEventSlim())
        {
            int nativeShutdownCount = 0;
            var leaseOwner = new NvidiaML.LeaseOwner(() => true,
                                                    () => Interlocked.Increment(ref nativeShutdownCount));
            TestHardware hardware = null;
            var group = new NvidiaGroup(new TestSettings(),
                                        new SequenceEnumerator(true).TryEnumerate,
                                        EmptyDisplayHandles,
                                        (_, _, _, settings) =>
                                            hardware = new TestHardware("gpu-blocked-close", settings, closeEntered, releaseClose),
                                        leaseOwner.Acquire,
                                        leaseOwner.Release,
                                        monitorHardwareChanges: false);
            Task closeTask = Task.Run(group.Close);

            try
            {
                Assert.True(closeEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(closeTask.IsCompleted);
                Assert.Equal(0, leaseOwner.LeaseCount);
                Assert.Equal(1, Volatile.Read(ref nativeShutdownCount));
            }
            finally
            {
                releaseClose.Set();
                await closeTask.WaitAsync(TimeSpan.FromSeconds(3));
                group.Close();
            }

            Assert.Equal(1, hardware.CloseCount);
        }

        using (var callbackEntered = new ManualResetEventSlim())
        using (var releaseCallback = new ManualResetEventSlim())
        {
            int nativeShutdownCount = 0;
            var leaseOwner = new NvidiaML.LeaseOwner(() => true,
                                                    () => Interlocked.Increment(ref nativeShutdownCount));
            TestHardware hardware = null;
            var group = new NvidiaGroup(new TestSettings(),
                                        new SequenceEnumerator(true, false).TryEnumerate,
                                        EmptyDisplayHandles,
                                        (_, _, _, settings) =>
                                            hardware = new TestHardware("gpu-unavailable", settings),
                                        leaseOwner.Acquire,
                                        leaseOwner.Release,
                                        monitorHardwareChanges: false);
            group.HardwareRemoved += _ =>
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            };
            Task refreshTask = Task.Run(group.RefreshHardware);

            try
            {
                Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(refreshTask.IsCompleted);
                Assert.Equal(0, leaseOwner.LeaseCount);
                Assert.Equal(1, Volatile.Read(ref nativeShutdownCount));
                Assert.Equal(0, hardware.CloseCount);
            }
            finally
            {
                releaseCallback.Set();
                await refreshTask.WaitAsync(TimeSpan.FromSeconds(3));
                group.Close();
            }

            Assert.Equal(1, hardware.CloseCount);
        }
    }

    [Fact]
    public void RefreshHardware_RemovalCallbackAndCloseFailures_DoNotOrphanLaterHardware()
    {
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        var enumerator = new HandleSequenceEnumerator([firstHandle, secondHandle], Array.Empty<NvApi.NvPhysicalGpuHandle>());
        var createdHardware = new List<TestHardware>();
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        var hardware = new TestHardware($"gpu-{createdHardware.Count}", settings);
                                        createdHardware.Add(hardware);
                                        return hardware;
                                    },
                                    () => true,
                                    () => { },
                                    monitorHardwareChanges: false);
        var callbackError = new InvalidOperationException("Removal callback failed.");
        var closeError = new ApplicationException("First close failed.");
        int callbackCount = 0;

        createdHardware[0].CloseError = closeError;
        group.HardwareRemoved += _ =>
        {
            callbackCount++;
            throw callbackError;
        };

        AggregateException actual = Assert.Throws<AggregateException>(group.RefreshHardware);

        Assert.Equal(3, actual.InnerExceptions.Count);
        Assert.Equal(2, actual.InnerExceptions.Count(error => ReferenceEquals(error, callbackError)));
        Assert.Single(actual.InnerExceptions, error => ReferenceEquals(error, closeError));
        Assert.Equal(2, callbackCount);
        Assert.Empty(group.Hardware);
        Assert.All(createdHardware, hardware => Assert.Equal(1, hardware.CloseCount));

        group.Close();
    }

    [Fact]
    public void TwoGroups_ShareNvidiaMlUntilLastGroupCloses()
    {
        int nativeInitializeCount = 0;
        int nativeShutdownCount = 0;
        var leaseOwner = new NvidiaML.LeaseOwner(() =>
                                                {
                                                    nativeInitializeCount++;
                                                    return true;
                                                },
                                                () => nativeShutdownCount++);
        var firstHardware = new List<TestHardware>();
        var secondHardware = new List<TestHardware>();
        var firstGroup = new NvidiaGroup(new TestSettings(),
                                         new SequenceEnumerator(true).TryEnumerate,
                                         EmptyDisplayHandles,
                                         (_, _, _, settings) =>
                                         {
                                             var hardware = new TestHardware("gpu-first", settings);
                                             firstHardware.Add(hardware);
                                             return hardware;
                                         },
                                         leaseOwner.Acquire,
                                         leaseOwner.Release,
                                         monitorHardwareChanges: false);
        var secondGroup = new NvidiaGroup(new TestSettings(),
                                          new SequenceEnumerator(true).TryEnumerate,
                                          EmptyDisplayHandles,
                                          (_, _, _, settings) =>
                                          {
                                              var hardware = new TestHardware("gpu-second", settings);
                                              secondHardware.Add(hardware);
                                              return hardware;
                                          },
                                          leaseOwner.Acquire,
                                          leaseOwner.Release,
                                          monitorHardwareChanges: false);

        Assert.Equal(2, leaseOwner.LeaseCount);
        Assert.Equal(1, nativeInitializeCount);
        Assert.Equal(0, nativeShutdownCount);

        firstGroup.Close();

        Assert.Equal(1, leaseOwner.LeaseCount);
        Assert.Equal(0, nativeShutdownCount);
        Assert.Single(secondGroup.Hardware);
        Assert.Equal(1, firstHardware[0].CloseCount);
        Assert.Equal(0, secondHardware[0].CloseCount);

        secondGroup.Close();

        Assert.Equal(0, leaseOwner.LeaseCount);
        Assert.Equal(1, nativeInitializeCount);
        Assert.Equal(1, nativeShutdownCount);
        Assert.Equal(1, secondHardware[0].CloseCount);
    }

    [Fact]
    public async Task Monitor_RecordsCycleFailuresAndContinuesRefreshing()
    {
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startMonitorCycle = new ManualResetEventSlim();
        var refreshError = new InvalidOperationException("Enumeration failed.");
        var callbackError = new ApplicationException("Removal callback failed.");
        var closeError = new NotSupportedException("GPU close failed.");
        var enumerator = new RecoveringEnumerator(refreshError, startMonitorCycle);
        var createdHardware = new List<TestHardware>();
        int leaseAcquireCount = 0;
        int leaseReleaseCount = 0;
        var group = new NvidiaGroup(new TestSettings(),
                                    enumerator.TryEnumerate,
                                    EmptyDisplayHandles,
                                    (_, _, _, settings) =>
                                    {
                                        var hardware = new TestHardware($"gpu-{createdHardware.Count}", settings);
                                        createdHardware.Add(hardware);
                                        return hardware;
                                    },
                                    () =>
                                    {
                                        Interlocked.Increment(ref leaseAcquireCount);
                                        return true;
                                    },
                                    () => Interlocked.Increment(ref leaseReleaseCount),
                                    monitorHardwareChanges: true,
                                    monitorInterval: TimeSpan.FromMilliseconds(25));

        createdHardware[0].CloseError = closeError;
        group.HardwareRemoved += _ => throw callbackError;
        group.HardwareAdded += _ => recovered.TrySetResult(true);
        startMonitorCycle.Set();

        try
        {
            Assert.True(await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(2, group.MonitorErrorCount);
            IReadOnlyList<Exception> monitorErrors = group.MonitorErrors;
            Assert.Equal(2, monitorErrors.Count);
            Assert.Same(refreshError, monitorErrors[0]);
            AggregateException removalError = Assert.IsType<AggregateException>(monitorErrors[1]);
            Assert.Equal(2, removalError.InnerExceptions.Count);
            Assert.Single(removalError.InnerExceptions, error => ReferenceEquals(error, callbackError));
            Assert.Single(removalError.InnerExceptions, error => ReferenceEquals(error, closeError));
            Assert.Same(removalError, group.LastMonitorError);
            Assert.Equal(1, createdHardware[0].CloseCount);
            Assert.Single(group.Hardware);
            Assert.Same(createdHardware[1], group.Hardware[0]);
            Assert.Equal(2, Volatile.Read(ref leaseAcquireCount));
            Assert.Equal(1, Volatile.Read(ref leaseReleaseCount));
        }
        finally
        {
            createdHardware[0].CloseError = null;
            group.Close();
        }

        Assert.Equal(2, Volatile.Read(ref leaseReleaseCount));
    }

    private static IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> EmptyDisplayHandles()
    {
        return new Dictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>();
    }

    private static void AssertReadOnly(IReadOnlyList<IHardware> snapshot)
    {
        IList<IHardware> list = Assert.IsAssignableFrom<IList<IHardware>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
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

    private sealed class BlockingEnumerator
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;
        private int _callCount;

        public BlockingEnumerator(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            _entered = entered;
            _release = release;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                handles = [default];
                count = 1;
                return true;
            }

            _entered.Set();
            _release.Wait();
            handles = [default];
            count = 1;
            return true;
        }
    }

    private sealed class RecoveringEnumerator
    {
        private readonly Exception _refreshError;
        private readonly ManualResetEventSlim _startMonitorCycle;
        private int _callCount;

        public RecoveringEnumerator(Exception refreshError, ManualResetEventSlim startMonitorCycle)
        {
            _refreshError = refreshError;
            _startMonitorCycle = startMonitorCycle;
        }

        public bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            switch (Interlocked.Increment(ref _callCount))
            {
                case 1:
                    handles = [CreateHandle(1)];
                    count = 1;
                    return true;
                case 2:
                    _startMonitorCycle.Wait();
                    throw _refreshError;
                case 3:
                    handles = Array.Empty<NvApi.NvPhysicalGpuHandle>();
                    count = 0;
                    return false;
                default:
                    handles = [CreateHandle(2)];
                    count = 1;
                    return true;
            }
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

    private sealed class TestHardware : HardwareBase
    {
        private readonly ManualResetEventSlim _closeEntered;
        private readonly ManualResetEventSlim _releaseClose;
        private int _closeCount;

        public TestHardware
        (
            string identifier,
            ISettings settings,
            ManualResetEventSlim closeEntered = null,
            ManualResetEventSlim releaseClose = null)
            : base("Test NVIDIA GPU", new Identifier(identifier), settings)
        {
            _closeEntered = closeEntered;
            _releaseClose = releaseClose;
        }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public Exception CloseError { get; set; }

        public override HardwareType HardwareType => HardwareType.GpuNvidia;

        public override void Close()
        {
            Interlocked.Increment(ref _closeCount);
            _closeEntered?.Set();
            _releaseClose?.Wait();

            if (CloseError != null)
                throw CloseError;

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
