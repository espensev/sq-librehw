// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DiskInfoToolkit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;
using StorageDIT = DiskInfoToolkit.Storage;

namespace LibreHardwareMonitor.Hardware.Storage;

internal class StorageGroup : IGroup, IHardwareChanged
{
    private readonly List<StorageDevice> _hardware = new();
    private readonly Func<List<StorageDeviceDIT>> _getDisks;
    private readonly Action<EventHandler<StorageDevicesChangedEventArgs>> _subscribeDevicesChanged;
    private readonly Action<EventHandler<StorageDevicesChangedEventArgs>> _unsubscribeDevicesChanged;
    private readonly object _sync = new();
    private readonly ISettings _settings;
    private bool _closed;
    private bool _subscribed;

    public event HardwareEventHandler HardwareAdded;
    public event HardwareEventHandler HardwareRemoved;

    public StorageGroup(ISettings settings)
    {
        _settings = settings;
        _getDisks = StorageDIT.GetDisks;
        _subscribeDevicesChanged = handler => StorageDIT.DevicesChanged += handler;
        _unsubscribeDevicesChanged = handler => StorageDIT.DevicesChanged -= handler;

        if (Software.OperatingSystem.IsUnix)
            return;

        AddHardware();
    }

    internal StorageGroup
    (
        ISettings settings,
        Func<List<StorageDeviceDIT>> getDisks,
        Action<EventHandler<StorageDevicesChangedEventArgs>> subscribeDevicesChanged,
        Action<EventHandler<StorageDevicesChangedEventArgs>> unsubscribeDevicesChanged)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _getDisks = getDisks ?? throw new ArgumentNullException(nameof(getDisks));
        _subscribeDevicesChanged = subscribeDevicesChanged ?? throw new ArgumentNullException(nameof(subscribeDevicesChanged));
        _unsubscribeDevicesChanged = unsubscribeDevicesChanged ?? throw new ArgumentNullException(nameof(unsubscribeDevicesChanged));

        AddHardware();
    }

    public IReadOnlyList<IHardware> Hardware => _hardware;

    private void AddHardware()
    {
        List<StorageDeviceDIT> disks = _getDisks();
        var devices = new List<StorageDevice>(disks.Count);

        try
        {
            devices.AddRange(disks.Select(storage => new StorageDevice(storage, _settings)));
        }
        catch
        {
            foreach (StorageDevice storageDevice in devices)
                storageDevice.Close();

            throw;
        }

        lock (_sync)
        {
            if (_closed)
            {
                foreach (StorageDevice storageDevice in devices)
                    storageDevice.Close();

                return;
            }

            // Transform storage devices to hardware before subscribing. This keeps the event
            // ownership balanced even when enumeration or construction fails.
            _hardware.AddRange(devices);
            _subscribeDevicesChanged(OnStoragesChanged);
            _subscribed = true;
        }
    }

    private void OnStoragesChanged(object sender, StorageDevicesChangedEventArgs e)
    {
        lock (_sync)
        {
            if (_closed)
                return;

            foreach (StorageDeviceDIT added in e.Added)
            {
                var storageDevice = new StorageDevice(added, _settings);

                _hardware.Add(storageDevice);
                HardwareAdded?.Invoke(storageDevice);
            }

            foreach (StorageDeviceDIT removed in e.Removed)
            {
                StorageDevice storageDevice = _hardware.Find(device => device.Storage == removed);
                if (storageDevice != null)
                {
                    _hardware.Remove(storageDevice);
                    HardwareRemoved?.Invoke(storageDevice);
                    storageDevice.Close();
                }
            }
        }
    }

    public void Close()
    {
        StorageDevice[] devices;

        lock (_sync)
        {
            if (_closed)
                return;

            _closed = true;

            if (_subscribed)
            {
                _unsubscribeDevicesChanged(OnStoragesChanged);
                _subscribed = false;
            }

            devices = _hardware.ToArray();
            _hardware.Clear();
        }

        Exception firstCloseError = null;
        foreach (StorageDevice storageDevice in devices)
        {
            try
            {
                storageDevice.Close();
            }
            catch (Exception ex)
            {
                firstCloseError ??= ex;
            }
        }

        if (firstCloseError != null)
            throw firstCloseError;
    }

    public string GetReport() => null;
}
