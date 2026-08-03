// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DiskInfoToolkit;
using DiskInfoToolkit.Smart;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using Xunit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;
using StorageHardware = LibreHardwareMonitor.Hardware.Storage.StorageDevice;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Characterization facts pinning the storage group lifecycle quirks that must survive the
/// collaborator extraction: subscribe-before-enumerate, buffered and coalesced initialization
/// changes capped at 256, redundant late-add close, notify-after-commit ordering, and
/// unsubscribe-retry ownership. Written against the pre-extraction <see cref="StorageGroup" />.
/// </summary>
public sealed class StorageGroupLifecycleCharacterizationTests
{
    [Fact]
    public void Constructor_SubscribesBeforeEnumerating_SoNoChangeFallsIntoTheBlindWindow()
    {
        var timeline = new List<string>();
        var publisher = new StorageChangePublisher(timeline);

        var group = new StorageGroup(new TestSettings(),
                                     () =>
                                     {
                                         timeline.Add("enumerate");
                                         return new List<StorageDeviceDIT>();
                                     },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe);

        Assert.Equal(["subscribe", "enumerate"], timeline);
        Assert.Equal(1, publisher.SubscriptionCount);
        Assert.Equal(1, publisher.ActiveHandlerCount);

        group.Close();
    }

    [Fact]
    public void Constructor_SynchronousChangesDuringSubscribe_AreCoalescedAndReplayedAfterEnumeration()
    {
        var timeline = new List<string>();
        var publisher = new StorageChangePublisher(timeline);
        StorageDeviceDIT initialDisk = CreateDisk(1);
        StorageDeviceDIT addedDisk = CreateDisk(2);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.ChangesDuringSubscribe.Add(StorageChangePublisher.CreateChange(added: new[] { initialDisk, initialDisk, addedDisk },
                                                                                 removed: new[] { initialDisk }));

        var group = new StorageGroup(new TestSettings(),
                                     () =>
                                     {
                                         timeline.Add("enumerate");
                                         return new List<StorageDeviceDIT> { initialDisk };
                                     },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) =>
                                     {
                                         timeline.Add($"create {storage.StorageDeviceNumber}");
                                         return CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices);
                                     });

        // The duplicate pending addition collapses in place and the pending removal cancels
        // the pending addition of the same storage, so the replay creates only addedDisk
        // after the enumerated device commits and then removes the enumerated device.
        Assert.Equal(["subscribe", "enumerate", "create 1", "create 2"], timeline);
        Assert.Equal(2, createdDevices.Count);
        Assert.Single(group.Hardware);
        Assert.Same(createdDevices[1], group.Hardware[0]);
        Assert.Equal([createdDevices[0]], closedDevices);

        group.Close();
        Assert.Equal(createdDevices, closedDevices);
    }

    [Fact]
    public void Constructor_MultipleSynchronousChangesDuringSubscribe_ReplayInEventOrder()
    {
        var timeline = new List<string>();
        var publisher = new StorageChangePublisher(timeline);
        StorageDeviceDIT firstDisk = CreateDisk(1);
        StorageDeviceDIT secondDisk = CreateDisk(2);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.ChangesDuringSubscribe.Add(StorageChangePublisher.CreateChange(added: firstDisk));
        publisher.ChangesDuringSubscribe.Add(StorageChangePublisher.CreateChange(added: secondDisk));

        var group = new StorageGroup(new TestSettings(),
                                     () =>
                                     {
                                         timeline.Add("enumerate");
                                         return new List<StorageDeviceDIT>();
                                     },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) =>
                                     {
                                         timeline.Add($"create {storage.StorageDeviceNumber}");
                                         return CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices);
                                     });

        Assert.Equal(["subscribe", "enumerate", "create 1", "create 2"], timeline);
        Assert.Equal<IHardware>(createdDevices, group.Hardware);

        group.Close();
        Assert.Equal(createdDevices, closedDevices);
    }

    [Fact]
    public void Constructor_PendingChangesBeyondThe256Cap_FailInitializationAndRollBackSubscription()
    {
        var publisher = new StorageChangePublisher();
        var disks = new List<StorageDeviceDIT>();
        for (uint i = 1; i <= 257; i++)
            disks.Add(CreateDisk(i));
        publisher.ChangesDuringSubscribe.Add(StorageChangePublisher.CreateChange(added: disks));
        int enumerateCount = 0;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            new StorageGroup(new TestSettings(),
                             () =>
                             {
                                 enumerateCount++;
                                 return new List<StorageDeviceDIT>();
                             },
                             publisher.Subscribe,
                             publisher.Unsubscribe));

        Assert.Equal("Too many storage changes occurred while the initial device snapshot was being created.", actual.Message);
        Assert.Equal(0, enumerateCount);
        Assert.Equal(1, publisher.SubscriptionCount);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);
    }

    [Fact]
    public void Constructor_PendingChangesAtThe256Cap_AreRetainedAndReplayedInFull()
    {
        const int pendingCount = 256;
        var publisher = new StorageChangePublisher();
        var disks = new List<StorageDeviceDIT>();
        for (uint i = 1; i <= pendingCount; i++)
            disks.Add(CreateDisk(i));
        publisher.ChangesDuringSubscribe.Add(StorageChangePublisher.CreateChange(added: disks));
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();

        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT>(),
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));

        Assert.Equal(pendingCount, createdDevices.Count);
        Assert.Equal(pendingCount, group.Hardware.Count);

        group.Close();
        Assert.Equal(createdDevices, closedDevices);
    }

    [Fact]
    public void ChangeCallback_RedundantLateAddition_IsClosedInsteadOfAddedAndNotifiedOnce()
    {
        var publisher = new StorageChangePublisher();
        StorageDeviceDIT disk = CreateDisk(1);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        var addedNotifications = new List<IHardware>();
        bool nestedRaisePending = true;

        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT>(),
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) =>
                                     {
                                         if (nestedRaisePending)
                                         {
                                             nestedRaisePending = false;
                                             // Reentrant callback: the nested addition commits
                                             // first, making the outer creation for the same
                                             // storage redundant.
                                             publisher.Raise(added: storage);
                                         }

                                         return CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices);
                                     });

        group.HardwareAdded += addedNotifications.Add;

        publisher.Raise(added: disk);

        Assert.Equal(2, createdDevices.Count);
        Assert.Single(group.Hardware);
        Assert.Same(createdDevices[0], group.Hardware[0]);
        Assert.Equal([createdDevices[0]], addedNotifications);
        Assert.Equal([createdDevices[1]], closedDevices);

        group.Close();
        Assert.Equal([createdDevices[1], createdDevices[0]], closedDevices);
    }

    [Fact]
    public void ChangeCallback_NotificationsFireAfterCommit_AndRemovalClosesDeviceAfterNotifying()
    {
        var publisher = new StorageChangePublisher();
        StorageDeviceDIT initialDisk = CreateDisk(1);
        StorageDeviceDIT addedDisk = CreateDisk(2);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { initialDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));
        var observations = new List<string>();
        group.HardwareAdded += hardware => observations.Add($"added-committed:{group.Hardware.Contains(hardware)}");
        group.HardwareRemoved += hardware =>
        {
            observations.Add($"removed-committed:{!group.Hardware.Contains(hardware)}");
            observations.Add($"removed-closed-before-notify:{closedDevices.Count > 0}");
        };

        publisher.Raise(added: addedDisk);

        Assert.Equal(["added-committed:True"], observations);

        publisher.Raise(removed: initialDisk);

        Assert.Equal(["added-committed:True", "removed-committed:True", "removed-closed-before-notify:False"], observations);
        Assert.Equal([createdDevices[0]], closedDevices);
        Assert.Single(group.Hardware);
        Assert.Same(createdDevices[1], group.Hardware[0]);

        group.Close();
    }

    [Fact]
    public void Close_UnsubscribeFails_KeepsDeviceOwnershipAndRetriesUnsubscribeOnly()
    {
        var publisher = new StorageChangePublisher();
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { CreateDisk(1), CreateDisk(2) },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));
        var unsubscribeError = new InvalidOperationException("Unsubscribe failed.");
        publisher.UnsubscribeError = unsubscribeError;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(group.Close);

        Assert.Same(unsubscribeError, actual);
        Assert.Empty(group.Hardware);
        Assert.Equal(createdDevices, closedDevices);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(1, publisher.ActiveHandlerCount);

        publisher.UnsubscribeError = null;
        group.Close();

        Assert.Equal(2, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);
        Assert.Equal(createdDevices, closedDevices);

        group.Close();

        Assert.Equal(2, publisher.UnsubscriptionCount);
        Assert.Equal(createdDevices, closedDevices);
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

    private static StorageHardware CreateTrackedStorageDevice
    (
        StorageDeviceDIT storage,
        ISettings settings,
        ICollection<StorageHardware> createdDevices,
        ICollection<StorageHardware> closedDevices)
    {
        var storageDevice = new StorageHardware(storage, settings);
        createdDevices.Add(storageDevice);
        storageDevice.Closing += hardware => closedDevices.Add((StorageHardware)hardware);
        return storageDevice;
    }

    private sealed class StorageChangePublisher
    {
        private readonly IList<string> _timeline;
        private EventHandler<StorageDevicesChangedEventArgs> _handlers;

        public StorageChangePublisher(IList<string> timeline = null)
        {
            _timeline = timeline;
        }

        public int ActiveHandlerCount => _handlers?.GetInvocationList().Length ?? 0;

        public IList<StorageDevicesChangedEventArgs> ChangesDuringSubscribe { get; } = new List<StorageDevicesChangedEventArgs>();

        public int SubscriptionCount { get; private set; }

        public Exception UnsubscribeError { get; set; }

        public int UnsubscriptionCount { get; private set; }

        public void Subscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            SubscriptionCount++;
            _timeline?.Add("subscribe");
            _handlers += handler;

            foreach (StorageDevicesChangedEventArgs change in ChangesDuringSubscribe)
                handler(this, change);
        }

        public void Unsubscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            UnsubscriptionCount++;
            _timeline?.Add("unsubscribe");

            if (UnsubscribeError != null)
                throw UnsubscribeError;

            _handlers -= handler;
        }

        public void Raise(StorageDeviceDIT added = null, StorageDeviceDIT removed = null)
        {
            _handlers?.Invoke(this, CreateChange(added, removed));
        }

        public static StorageDevicesChangedEventArgs CreateChange
        (
            StorageDeviceDIT added = null,
            StorageDeviceDIT removed = null)
        {
            return CreateChange(added == null ? Array.Empty<StorageDeviceDIT>() : new[] { added },
                                removed == null ? Array.Empty<StorageDeviceDIT>() : new[] { removed });
        }

        public static StorageDevicesChangedEventArgs CreateChange
        (
            IEnumerable<StorageDeviceDIT> added = null,
            IEnumerable<StorageDeviceDIT> removed = null)
        {
            return new StorageDevicesChangedEventArgs
            {
                Added = new List<StorageDeviceDIT>(added ?? Array.Empty<StorageDeviceDIT>()),
                Removed = new List<StorageDeviceDIT>(removed ?? Array.Empty<StorageDeviceDIT>()),
                Updated = new List<StorageDeviceDIT>(),
                Current = new List<StorageDeviceDIT>()
            };
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
