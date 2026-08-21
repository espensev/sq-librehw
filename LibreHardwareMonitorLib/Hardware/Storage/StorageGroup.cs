// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using DiskInfoToolkit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;
using StorageDIT = DiskInfoToolkit.Storage;

namespace LibreHardwareMonitor.Hardware.Storage;

/// <summary>
/// The storage hardware group facade. Discovery (subscribe-before-enumerate with buffered
/// synchronous changes), change application (diff/coalesce/close/notify), and
/// close/unsubscribe-retry mechanics live in the internal collaborators of
/// <see cref="StorageGroupLifecycle" />; this type keeps the constructors, the
/// <see cref="IGroup" /> / <see cref="IHardwareChanged" /> surface, and the settings field, and
/// delegates the mechanics without behavior change.
/// </summary>
internal class StorageGroup : IGroup, IHardwareChanged
{
    private static readonly IReadOnlyList<IHardware> EmptyHardware = Array.AsReadOnly(Array.Empty<IHardware>());

    private readonly StorageGroupLifecycle _lifecycle;
    private readonly ISettings _settings;

    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;

    public StorageGroup(ISettings settings)
    {
        _settings = settings;

        if (Software.OperatingSystem.IsUnix)
            return;

        _lifecycle = new StorageGroupLifecycle(settings,
                                               StorageDIT.GetDisks,
                                               handler => StorageDIT.DevicesChanged += handler,
                                               handler => StorageDIT.DevicesChanged -= handler,
                                               (storage, hardwareSettings) => new StorageDevice(storage, hardwareSettings),
                                               () => HardwareAdded,
                                               () => HardwareRemoved);
    }

    internal StorageGroup
    (
        ISettings settings,
        Func<List<StorageDeviceDIT>> getDisks,
        Action<EventHandler<StorageDevicesChangedEventArgs>> subscribeDevicesChanged,
        Action<EventHandler<StorageDevicesChangedEventArgs>> unsubscribeDevicesChanged,
        Func<StorageDeviceDIT, ISettings, StorageDevice> createStorageDevice = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _lifecycle = new StorageGroupLifecycle(settings,
                                               getDisks,
                                               subscribeDevicesChanged,
                                               unsubscribeDevicesChanged,
                                               createStorageDevice,
                                               () => HardwareAdded,
                                               () => HardwareRemoved);
    }

    public IReadOnlyList<IHardware> Hardware => _lifecycle?.Hardware ?? EmptyHardware;

    public void Close() => _lifecycle?.Close();

    public string GetReport() => null;
}
