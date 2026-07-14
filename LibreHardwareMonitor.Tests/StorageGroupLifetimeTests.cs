// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DiskInfoToolkit;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using Xunit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;

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

    private sealed class DeviceChangePublisher
    {
        private EventHandler<StorageDevicesChangedEventArgs> _handlers;

        public int ActiveHandlerCount => _handlers?.GetInvocationList().Length ?? 0;

        public int SubscriptionCount { get; private set; }

        public int UnsubscriptionCount { get; private set; }

        public void Subscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            SubscriptionCount++;
            _handlers += handler;
        }

        public void Unsubscribe(EventHandler<StorageDevicesChangedEventArgs> handler)
        {
            UnsubscriptionCount++;
            _handlers -= handler;
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
