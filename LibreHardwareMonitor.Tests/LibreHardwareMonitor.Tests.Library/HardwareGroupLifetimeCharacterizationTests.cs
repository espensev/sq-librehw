// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DiskInfoToolkit;
using DiskInfoToolkit.Smart;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Interop;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;
using StorageHardware = LibreHardwareMonitor.Hardware.Storage.StorageDevice;
using StorageGroup = LibreHardwareMonitor.Hardware.Storage.StorageGroup;

namespace LibreHardwareMonitor.Tests;

public sealed class HardwareGroupLifetimeCharacterizationTests
{
    [Fact]
    public void Computer_OpenClose_DynamicGroupsForwardOnlyWhileRegisteredAndDrainInReverseOrder()
    {
        var closeFailure = new InvalidOperationException("second group close failed");
        var firstHardware = new TestHardware("first");
        var secondHardware = new TestHardware("second");
        var liveHardware = new TestHardware("live");
        var lateHardware = new TestHardware("late");
        var closeOrder = new List<string>();
        var firstGroup = new DynamicTestGroup("first", firstHardware)
        {
            CloseOrder = closeOrder
        };
        var secondGroup = new DynamicTestGroup("second", secondHardware)
        {
            CloseAddedHardware = lateHardware,
            CloseFailure = closeFailure,
            CloseOrder = closeOrder,
            CloseRemovedHardware = secondHardware
        };
        var probe = new OpenProbe(closeOrder)
        {
            AddGroups = add =>
            {
                add(firstGroup);
                add(secondGroup);
            }
        };
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var added = new List<IHardware>();
        var removed = new List<IHardware>();
        computer.HardwareAdded += added.Add;
        computer.HardwareRemoved += removed.Add;

        computer.Open();

        Assert.Equal([firstHardware, secondHardware], added);
        Assert.Equal([firstHardware, secondHardware], computer.Hardware);

        firstGroup.RaiseAdded(liveHardware);
        firstGroup.RaiseRemoved(firstHardware);

        Assert.Equal([firstHardware, secondHardware, liveHardware], added);
        Assert.Equal([firstHardware], removed);
        Assert.Equal([liveHardware, secondHardware], computer.Hardware);

        closeOrder.Clear();
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Close);

        Assert.Same(closeFailure, thrown);
        Assert.Empty(computer.Hardware);
        Assert.Equal([firstHardware, secondHardware, liveHardware], added);
        Assert.Equal([firstHardware, secondHardware, liveHardware], removed);
        Assert.Equal(["second.close", "first.close", "opcode.close", "mutex.close"], closeOrder);
        Assert.Equal(1, firstGroup.CloseCount);
        Assert.Equal(1, secondGroup.CloseCount);
        firstGroup.AssertSubscriptionCounts(1, 1);
        secondGroup.AssertSubscriptionCounts(1, 1);
        probe.AssertCounts(1, 1);

        firstGroup.RaiseAdded(lateHardware);
        secondGroup.RaiseRemoved(secondHardware);

        Assert.Equal([firstHardware, secondHardware, liveHardware], added);
        Assert.Equal([firstHardware, secondHardware, liveHardware], removed);
    }

    [Fact]
    public void Computer_Open_WhenRegisteredGroupSnapshotThrows_RollsBackSubscriptionAndCanRetry()
    {
        var snapshotFailure = new InvalidOperationException("group snapshot failed");
        var failedHardware = new TestHardware("failed");
        var lateHardware = new TestHardware("late");
        var retryHardware = new TestHardware("retry");
        var failedGroup = new DynamicTestGroup("failed", failedHardware)
        {
            SnapshotFailure = snapshotFailure
        };
        var retryGroup = new DynamicTestGroup("retry", retryHardware);
        var probe = new OpenProbe();
        int attempt = 0;
        probe.AddGroups = add => add(++attempt == 1 ? failedGroup : retryGroup);
        var computer = new Computer(new TestSettings(), probe.CreateDependencies());
        var added = new List<IHardware>();
        var removed = new List<IHardware>();
        computer.HardwareAdded += added.Add;
        computer.HardwareRemoved += removed.Add;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(computer.Open);

        Assert.Same(snapshotFailure, thrown);
        Assert.Empty(computer.Hardware);
        Assert.Empty(added);
        Assert.Empty(removed);
        Assert.Equal(1, failedGroup.CloseCount);
        failedGroup.AssertSubscriptionCounts(1, 1);
        probe.AssertCounts(1, 1);

        failedGroup.RaiseAdded(lateHardware);
        failedGroup.RaiseRemoved(failedHardware);

        Assert.Empty(added);
        Assert.Empty(removed);

        computer.Open();
        computer.Close();

        Assert.Empty(computer.Hardware);
        Assert.Equal([retryHardware], added);
        Assert.Equal([retryHardware], removed);
        Assert.Equal(1, retryGroup.CloseCount);
        retryGroup.AssertSubscriptionCounts(1, 1);
        probe.AssertCounts(2, 2);
    }

    [Fact]
    public void NvidiaGroup_Constructor_WhenSecondHardwareFactoryThrows_ClosesFirstHardwareAndReleasesLease()
    {
        var factoryFailure = new InvalidOperationException("second GPU factory failed");
        var timeline = new List<string>();
        NvApi.NvPhysicalGpuHandle firstHandle = CreateHandle(1);
        NvApi.NvPhysicalGpuHandle secondHandle = CreateHandle(2);
        TestHardware firstHardware = null;
        int acquireCount = 0;
        int releaseCount = 0;

        bool TryEnumerate(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
        {
            handles = [firstHandle, secondHandle];
            count = handles.Length;
            return true;
        }

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            new NvidiaGroup(new TestSettings(),
                            TryEnumerate,
                            () => new Dictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>(),
                            (index, _, _, settings) =>
                            {
                                if (index == 0)
                                {
                                    timeline.Add("create first");
                                    firstHardware = new TestHardware("first-gpu",
                                                                     HardwareType.GpuNvidia,
                                                                     settings,
                                                                     () => timeline.Add("close first"));
                                    return firstHardware;
                                }

                                timeline.Add("fail second");
                                throw factoryFailure;
                            },
                            () =>
                            {
                                acquireCount++;
                                timeline.Add("acquire");
                                return true;
                            },
                            () =>
                            {
                                releaseCount++;
                                timeline.Add("release");
                            },
                            monitorHardwareChanges: false));

        Assert.Same(factoryFailure, thrown);
        Assert.NotNull(firstHardware);
        Assert.Equal(1, firstHardware.CloseCount);
        Assert.Equal(1, acquireCount);
        Assert.Equal(1, releaseCount);
        Assert.Equal(["acquire", "create first", "fail second", "close first", "release"], timeline);
    }

    [Fact]
    public void StorageGroup_Constructor_WhenSecondDeviceFactoryThrows_UnsubscribesAndClosesFirstDevice()
    {
        var factoryFailure = new InvalidOperationException("second storage factory failed");
        var timeline = new List<string>();
        var publisher = new StorageChangePublisher(timeline);
        StorageDeviceDIT firstDisk = CreateDisk(1);
        StorageDeviceDIT secondDisk = CreateDisk(2);
        int closeCount = 0;
        int factoryCallCount = 0;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            new StorageGroup(new TestSettings(),
                             () => new List<StorageDeviceDIT> { firstDisk, secondDisk },
                             publisher.Subscribe,
                             publisher.Unsubscribe,
                             (storage, settings) =>
                             {
                                 factoryCallCount++;
                                 if (factoryCallCount == 2)
                                 {
                                     timeline.Add("fail second");
                                     throw factoryFailure;
                                 }

                                 timeline.Add("create first");
                                 var hardware = new StorageHardware(storage, settings);
                                 hardware.Closing += _ =>
                                 {
                                     closeCount++;
                                     timeline.Add("close first");
                                 };
                                 return hardware;
                             }));

        Assert.Same(factoryFailure, thrown);
        Assert.Equal(2, factoryCallCount);
        Assert.Equal(1, publisher.SubscriptionCount);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);
        Assert.Equal(1, closeCount);
        Assert.Equal(["subscribe", "create first", "fail second", "unsubscribe", "close first"], timeline);
    }

    private static NvApi.NvPhysicalGpuHandle CreateHandle(int value)
    {
        Span<IntPtr> source = stackalloc IntPtr[1];
        source[0] = new IntPtr(value);
        return MemoryMarshal.Cast<IntPtr, NvApi.NvPhysicalGpuHandle>(source)[0];
    }

    private static StorageDeviceDIT CreateDisk(uint number)
    {
        return new StorageDeviceDIT
        {
            ProductName = $"Test disk {number}",
            StorageDeviceNumber = number,
            Scsi = new StorageScsiInfo(),
            Controller = new StorageControllerInfo(),
            SmartAttributes = new List<SmartAttributeEntry>(),
            Partitions = new List<StoragePartitionInfo>(),
            ProbeTrace = new List<string>()
        };
    }

    private sealed class DynamicTestGroup : IGroup, IHardwareChanged
    {
        private readonly List<IHardware> _hardware = new();
        private readonly string _name;
        private HardwareEventHandler _hardwareAdded;
        private HardwareEventHandler _hardwareRemoved;
        private IReadOnlyList<IHardware> _snapshot;

        public DynamicTestGroup(string name, params IHardware[] hardware)
        {
            _name = name;
            _hardware.AddRange(hardware);
            PublishSnapshot();
        }

        public event HardwareEventHandler HardwareAdded
        {
            add
            {
                HardwareAddedSubscriptionCount++;
                _hardwareAdded += value;
            }
            remove
            {
                HardwareAddedUnsubscriptionCount++;
                _hardwareAdded -= value;
            }
        }

        public event HardwareEventHandler HardwareRemoved
        {
            add
            {
                HardwareRemovedSubscriptionCount++;
                _hardwareRemoved += value;
            }
            remove
            {
                HardwareRemovedUnsubscriptionCount++;
                _hardwareRemoved -= value;
            }
        }

        public IHardware CloseAddedHardware { get; set; }

        public Exception CloseFailure { get; set; }

        public int CloseCount { get; private set; }

        public IList<string> CloseOrder { get; set; }

        public IHardware CloseRemovedHardware { get; set; }

        public IReadOnlyList<IHardware> Hardware
        {
            get
            {
                if (SnapshotFailure != null)
                    throw SnapshotFailure;

                return _snapshot;
            }
        }

        public int HardwareAddedSubscriptionCount { get; private set; }

        public int HardwareAddedUnsubscriptionCount { get; private set; }

        public int HardwareRemovedSubscriptionCount { get; private set; }

        public int HardwareRemovedUnsubscriptionCount { get; private set; }

        public Exception SnapshotFailure { get; set; }

        public void AssertSubscriptionCounts(int subscriptionCount, int unsubscriptionCount)
        {
            Assert.Equal(subscriptionCount, HardwareAddedSubscriptionCount);
            Assert.Equal(subscriptionCount, HardwareRemovedSubscriptionCount);
            Assert.Equal(unsubscriptionCount, HardwareAddedUnsubscriptionCount);
            Assert.Equal(unsubscriptionCount, HardwareRemovedUnsubscriptionCount);
        }

        public void Close()
        {
            CloseCount++;
            CloseOrder?.Add($"{_name}.close");

            if (CloseAddedHardware != null)
                RaiseAdded(CloseAddedHardware);

            if (CloseRemovedHardware != null)
                RaiseRemoved(CloseRemovedHardware);

            if (CloseFailure != null)
                throw CloseFailure;
        }

        public string GetReport() => null;

        public void RaiseAdded(IHardware hardware)
        {
            _hardware.Add(hardware);
            PublishSnapshot();
            _hardwareAdded?.Invoke(hardware);
        }

        public void RaiseRemoved(IHardware hardware)
        {
            _hardware.Remove(hardware);
            PublishSnapshot();
            _hardwareRemoved?.Invoke(hardware);
        }

        private void PublishSnapshot()
        {
            _snapshot = Array.AsReadOnly(_hardware.ToArray());
        }
    }

    private sealed class OpenProbe
    {
        private readonly IList<string> _timeline;

        public OpenProbe(IList<string> timeline = null)
        {
            _timeline = timeline;
        }

        public Action<Action<IGroup>> AddGroups { get; set; }

        public int CloseMutexesCount { get; private set; }

        public int CloseOpCodeCount { get; private set; }

        public int CreateSmbiosCount { get; private set; }

        public int OpenMutexesCount { get; private set; }

        public int OpenOpCodeCount { get; private set; }

        public Computer.OpenDependencies CreateDependencies()
        {
            return new Computer.OpenDependencies(
                () =>
                {
                    CreateSmbiosCount++;
                    return null;
                },
                () => OpenMutexesCount++,
                () =>
                {
                    CloseMutexesCount++;
                    _timeline?.Add("mutex.close");
                },
                () => OpenOpCodeCount++,
                () =>
                {
                    CloseOpCodeCount++;
                    _timeline?.Add("opcode.close");
                },
                add => AddGroups(add));
        }

        public void AssertCounts(int openCount, int closeCount)
        {
            Assert.Equal(openCount, CreateSmbiosCount);
            Assert.Equal(openCount, OpenMutexesCount);
            Assert.Equal(openCount, OpenOpCodeCount);
            Assert.Equal(closeCount, CloseOpCodeCount);
            Assert.Equal(closeCount, CloseMutexesCount);
        }
    }

    private sealed class StorageChangePublisher
    {
        private readonly IList<string> _timeline;
        private EventHandler<StorageDevicesChangedEventArgs> _handlers;

        public StorageChangePublisher(IList<string> timeline)
        {
            _timeline = timeline;
        }

        public int ActiveHandlerCount => _handlers?.GetInvocationList().Length ?? 0;

        public int SubscriptionCount { get; private set; }

        public int UnsubscriptionCount { get; private set; }

        public void Subscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            SubscriptionCount++;
            _timeline.Add("subscribe");
            _handlers += handler;
        }

        public void Unsubscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            UnsubscriptionCount++;
            _timeline.Add("unsubscribe");
            _handlers -= handler;
        }
    }

    private sealed class TestHardware : HardwareBase
    {
        private readonly HardwareType _hardwareType;
        private readonly Action _onClose;

        public TestHardware
        (
            string identifier,
            HardwareType hardwareType = HardwareType.Cpu,
            ISettings settings = null,
            Action onClose = null)
            : base("Test hardware", new Identifier(identifier), settings ?? new TestSettings())
        {
            _hardwareType = hardwareType;
            _onClose = onClose;
        }

        public int CloseCount { get; private set; }

        public override HardwareType HardwareType => _hardwareType;

        public override void Close()
        {
            CloseCount++;
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

        public void Remove(string name)
        {
        }

        public void SetValue(string name, string value)
        {
        }
    }
}
