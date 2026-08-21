// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DiskInfoToolkit;
using DiskInfoToolkit.Smart;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using Xunit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;
using StorageHardware = LibreHardwareMonitor.Hardware.Storage.StorageDevice;

namespace LibreHardwareMonitor.Tests;

public sealed class StorageGroupLifetimeTests
{
    [Fact]
    public void Close_UnsubscribesExactlyOnceAndReleasesTheGroup()
    {
        var publisher = new DeviceChangePublisher();
        WeakReference groupReference = CreateClosedGroup(publisher);

        Assert.Equal(1, publisher.SubscriptionCount);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);

        CollectUntilDead(groupReference);

        Assert.False(groupReference.IsAlive);
    }

    [Fact]
    public void RepeatedResetResumeOrToggleReplacement_DoesNotAccumulateDeviceChangeSubscriptions()
    {
        const int replacementCount = 8;
        var publisher = new DeviceChangePublisher();
        var groupReferences = new List<WeakReference>(replacementCount);

        for (int i = 0; i < replacementCount; i++)
            groupReferences.Add(CreateClosedGroup(publisher));

        Assert.Equal(replacementCount, publisher.SubscriptionCount);
        Assert.Equal(replacementCount, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);

        foreach (WeakReference groupReference in groupReferences)
        {
            CollectUntilDead(groupReference);
            Assert.False(groupReference.IsAlive);
        }
    }

    [Fact]
    public void Hardware_CapturedSnapshotRemainsStableAcrossAddRemoveAndClose()
    {
        var publisher = new DeviceChangePublisher();
        StorageDeviceDIT firstDisk = CreateDisk(1);
        StorageDeviceDIT secondDisk = CreateDisk(2);
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { firstDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe);

        IReadOnlyList<IHardware> initialSnapshot = group.Hardware;
        Assert.Single(initialSnapshot);
        AssertReadOnly(initialSnapshot);

        publisher.Raise(added: secondDisk);

        IReadOnlyList<IHardware> addedSnapshot = group.Hardware;
        Assert.Single(initialSnapshot);
        Assert.Equal(2, addedSnapshot.Count);
        Assert.Same(initialSnapshot[0], addedSnapshot[0]);
        AssertReadOnly(addedSnapshot);

        publisher.Raise(removed: firstDisk);

        IReadOnlyList<IHardware> removedSnapshot = group.Hardware;
        Assert.Single(initialSnapshot);
        Assert.Equal(2, addedSnapshot.Count);
        Assert.Single(removedSnapshot);
        Assert.Same(addedSnapshot[1], removedSnapshot[0]);

        group.Close();

        Assert.Empty(group.Hardware);
        Assert.Single(initialSnapshot);
        Assert.Equal(2, addedSnapshot.Count);
        Assert.Single(removedSnapshot);
    }

    [Fact]
    public void Constructor_SubscribeThrowsAfterAttaching_RollsBackSubscription()
    {
        var publisher = new DeviceChangePublisher();
        var subscribeError = new InvalidOperationException("Subscribe failed after attaching.");
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.SubscribeError = subscribeError;
        publisher.AttachBeforeSubscribeError = true;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            new StorageGroup(new TestSettings(),
                             () => new List<StorageDeviceDIT> { CreateDisk(1), CreateDisk(2) },
                             publisher.Subscribe,
                             publisher.Unsubscribe,
                             (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices)));

        Assert.Same(subscribeError, actual);
        Assert.Equal(1, publisher.SubscriptionCount);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);
        Assert.Empty(createdDevices);
        Assert.Equal(createdDevices, closedDevices);
    }

    [Fact]
    public void Constructor_SynchronousRemovalDuringSubscribe_ReconcilesInitialSnapshot()
    {
        var publisher = new DeviceChangePublisher();
        StorageDeviceDIT initialDisk = CreateDisk(1);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.ChangeDuringSubscribe = DeviceChangePublisher.CreateChange(removed: initialDisk);

        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { initialDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));

        Assert.Empty(group.Hardware);
        Assert.Single(createdDevices);
        Assert.Equal(createdDevices, closedDevices);

        group.Close();
        Assert.Equal(0, publisher.ActiveHandlerCount);
    }

    [Fact]
    public void Constructor_SynchronousAdditionDuringSubscribe_AlreadyInInitialSnapshot_IsAppliedOnce()
    {
        var publisher = new DeviceChangePublisher();
        StorageDeviceDIT addedDisk = CreateDisk(1);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.ChangeDuringSubscribe = DeviceChangePublisher.CreateChange(added: addedDisk);

        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { addedDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));

        Assert.Single(group.Hardware);
        Assert.Single(createdDevices);
        Assert.Empty(closedDevices);

        group.Close();
        Assert.Equal(createdDevices, closedDevices);
        Assert.Equal(0, publisher.ActiveHandlerCount);
    }

    [Fact]
    public void Constructor_SynchronousAdditionDuringSubscribe_MissingFromInitialSnapshot_IsRetained()
    {
        var publisher = new DeviceChangePublisher();
        StorageDeviceDIT initialDisk = CreateDisk(1);
        StorageDeviceDIT addedDisk = CreateDisk(2);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        publisher.ChangeDuringSubscribe = DeviceChangePublisher.CreateChange(added: addedDisk);

        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { initialDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));

        Assert.Equal(2, group.Hardware.Count);
        Assert.Equal(2, createdDevices.Count);
        Assert.Empty(closedDevices);

        group.Close();
        Assert.Equal(createdDevices, closedDevices);
        Assert.Equal(0, publisher.ActiveHandlerCount);
    }

    [Fact]
    public void Close_UnsubscribeThrows_RetryDetachesWithoutClosingDevicesAgain()
    {
        var publisher = new DeviceChangePublisher();
        var unsubscribeError = new InvalidOperationException("Unsubscribe failed.");
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { CreateDisk(1), CreateDisk(2) },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));
        publisher.UnsubscribeError = unsubscribeError;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(group.Close);

        Assert.Same(unsubscribeError, actual);
        Assert.Empty(group.Hardware);
        Assert.Equal(createdDevices, closedDevices);
        Assert.Equal(1, publisher.UnsubscriptionCount);
        Assert.Equal(1, publisher.ActiveHandlerCount);

        publisher.UnsubscribeError = null;
        group.Close();
        group.Close();

        Assert.Equal(2, publisher.UnsubscriptionCount);
        Assert.Equal(0, publisher.ActiveHandlerCount);
        Assert.Equal(createdDevices, closedDevices);
    }

    [Fact]
    public void RemovalCallbackAndCloseFailures_StillCloseEveryRemovedDevice()
    {
        var publisher = new DeviceChangePublisher();
        StorageDeviceDIT firstDisk = CreateDisk(1);
        StorageDeviceDIT secondDisk = CreateDisk(2);
        var createdDevices = new List<StorageHardware>();
        var closedDevices = new List<StorageHardware>();
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT> { firstDisk, secondDisk },
                                     publisher.Subscribe,
                                     publisher.Unsubscribe,
                                     (storage, settings) => CreateTrackedStorageDevice(storage, settings, createdDevices, closedDevices));
        var callbackError = new InvalidOperationException("Removal callback failed.");
        int callbackCount = 0;

        createdDevices[0].Closing += _ => throw new ApplicationException("First close failed.");
        group.HardwareRemoved += _ =>
        {
            callbackCount++;
            throw callbackError;
        };

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => publisher.RaiseRemoved(firstDisk, secondDisk));

        Assert.Same(callbackError, actual);
        Assert.Equal(2, callbackCount);
        Assert.Empty(group.Hardware);
        Assert.Equal(createdDevices, closedDevices);

        group.Close();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateClosedGroup(DeviceChangePublisher publisher)
    {
        var group = new StorageGroup(new TestSettings(),
                                     () => new List<StorageDeviceDIT>(),
                                     publisher.Subscribe,
                                     publisher.Unsubscribe);

        Assert.Equal(1, publisher.ActiveHandlerCount);
        Assert.Empty(group.Hardware);

        group.Close();
        group.Close();

        Assert.Empty(group.Hardware);
        return new WeakReference(group);
    }

    private static void CollectUntilDead(WeakReference reference)
    {
        for (int attempt = 0; attempt < 3 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
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

    private static void AssertReadOnly(IReadOnlyList<IHardware> snapshot)
    {
        IList<IHardware> list = Assert.IsAssignableFrom<IList<IHardware>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
    }

    private sealed class DeviceChangePublisher
    {
        private EventHandler<StorageDevicesChangedEventArgs> _handlers;

        public int ActiveHandlerCount => _handlers?.GetInvocationList().Length ?? 0;

        public bool AttachBeforeSubscribeError { get; set; }

        public StorageDevicesChangedEventArgs ChangeDuringSubscribe { get; set; }

        public Exception SubscribeError { get; set; }

        public int SubscriptionCount { get; private set; }

        public Exception UnsubscribeError { get; set; }

        public int UnsubscriptionCount { get; private set; }

        public void Subscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            SubscriptionCount++;

            if (AttachBeforeSubscribeError)
                _handlers += handler;

            if (SubscribeError != null)
                throw SubscribeError;

            if (!AttachBeforeSubscribeError)
                _handlers += handler;

            if (ChangeDuringSubscribe != null)
                handler(this, ChangeDuringSubscribe);
        }

        public void Unsubscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            UnsubscriptionCount++;

            if (UnsubscribeError != null)
                throw UnsubscribeError;

            _handlers -= handler;
        }

        public void Raise(StorageDeviceDIT added = null, StorageDeviceDIT removed = null)
        {
            _handlers?.Invoke(this,
                              new StorageDevicesChangedEventArgs
                              {
                                  Added = added == null ? new List<StorageDeviceDIT>() : new List<StorageDeviceDIT> { added },
                                  Removed = removed == null ? new List<StorageDeviceDIT>() : new List<StorageDeviceDIT> { removed },
                                  Updated = new List<StorageDeviceDIT>(),
                                  Current = new List<StorageDeviceDIT>()
                              });
        }

        public void RaiseRemoved(params StorageDeviceDIT[] removed)
        {
            _handlers?.Invoke(this, CreateChange(removed: removed));
        }

        public static StorageDevicesChangedEventArgs CreateChange
        (
            StorageDeviceDIT added = null,
            StorageDeviceDIT removed = null)
        {
            return CreateChange
            (
                added == null ? Array.Empty<StorageDeviceDIT>() : new[] { added },
                removed == null ? Array.Empty<StorageDeviceDIT>() : new[] { removed });
        }

        private static StorageDevicesChangedEventArgs CreateChange
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

        public void Remove(string name) { }

        public void SetValue(string name, string value) { }
    }
}
