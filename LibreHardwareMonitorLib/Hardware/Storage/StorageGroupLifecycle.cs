// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DiskInfoToolkit;
using StorageDeviceDIT = DiskInfoToolkit.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

/// <summary>
/// Owns the storage group lifecycle mechanics behind the <see cref="StorageGroup" /> facade:
/// discovery (subscribe-before-enumerate with buffered synchronous changes), change application
/// (diff/coalesce/close/notify with commit ordering), and close with unsubscribe-retry ownership.
/// Extracted from <see cref="StorageGroup" /> without behavior, timing, or DiskInfoToolkit change.
/// Change notifications are raised through the handler-list accessors supplied by the facade,
/// which keep subscriber semantics identical to the pre-extraction events.
/// </summary>
internal sealed class StorageGroupLifecycle
{
    private readonly object _changeSync = new();
    private readonly Func<StorageDeviceDIT, ISettings, StorageDevice> _createStorageDevice;
    private readonly Func<List<StorageDeviceDIT>> _getDisks;
    private readonly List<StorageDevice> _hardware = new();
    private readonly Func<HardwareEventHandler> _hardwareAddedHandlers;
    private readonly Func<HardwareEventHandler> _hardwareRemovedHandlers;
    private readonly StorageInitializationChangeBuffer _initializationChanges = new();
    private readonly ISettings _settings;
    private readonly Action<EventHandler<StorageDevicesChangedEventArgs>> _subscribeDevicesChanged;
    private readonly object _sync = new();
    private readonly Action<EventHandler<StorageDevicesChangedEventArgs>> _unsubscribeDevicesChanged;

    private volatile bool _closed;
    private IReadOnlyList<IHardware> _hardwareSnapshot = Array.AsReadOnly(Array.Empty<IHardware>());
    private bool _initializing = true;
    private bool _subscribed;

    internal StorageGroupLifecycle
    (
        ISettings settings,
        Func<List<StorageDeviceDIT>> getDisks,
        Action<EventHandler<StorageDevicesChangedEventArgs>> subscribeDevicesChanged,
        Action<EventHandler<StorageDevicesChangedEventArgs>> unsubscribeDevicesChanged,
        Func<StorageDeviceDIT, ISettings, StorageDevice> createStorageDevice,
        Func<HardwareEventHandler> hardwareAddedHandlers,
        Func<HardwareEventHandler> hardwareRemovedHandlers)
    {
        _settings = settings;
        _createStorageDevice = createStorageDevice ?? ((storage, hardwareSettings) => new StorageDevice(storage, hardwareSettings));
        _getDisks = getDisks ?? throw new ArgumentNullException(nameof(getDisks));
        _subscribeDevicesChanged = subscribeDevicesChanged ?? throw new ArgumentNullException(nameof(subscribeDevicesChanged));
        _unsubscribeDevicesChanged = unsubscribeDevicesChanged ?? throw new ArgumentNullException(nameof(unsubscribeDevicesChanged));
        _hardwareAddedHandlers = hardwareAddedHandlers ?? throw new ArgumentNullException(nameof(hardwareAddedHandlers));
        _hardwareRemovedHandlers = hardwareRemovedHandlers ?? throw new ArgumentNullException(nameof(hardwareRemovedHandlers));

        AddHardware();
    }

    internal IReadOnlyList<IHardware> Hardware => Volatile.Read(ref _hardwareSnapshot);

    internal void Close()
    {
        StorageDevice[] devices = Array.Empty<StorageDevice>();
        bool firstClose;

        lock (_sync)
        {
            firstClose = !_closed;
            if (firstClose)
            {
                _closed = true;
                devices = _hardware.ToArray();
                _hardware.Clear();
                PublishHardwareSnapshot();
            }
            else if (!_subscribed)
                return;
        }

        Exception firstCloseError = null;

        lock (_changeSync)
        {
            bool unsubscribe;
            lock (_sync)
                unsubscribe = _subscribed;

            if (unsubscribe)
            {
                try
                {
                    _unsubscribeDevicesChanged(OnStoragesChanged);
                    lock (_sync)
                        _subscribed = false;
                }
                catch (Exception ex)
                {
                    // Keep subscription ownership when removal fails. The group
                    // is already closed, so callbacks are inert, and a later
                    // Close can retry without closing the devices again.
                    firstCloseError ??= ex;
                }
            }
        }

        firstCloseError = CloseStorageDevicesBestEffort(devices, firstCloseError);

        if (firstCloseError != null)
            throw firstCloseError;
    }

    private void AddHardware()
    {
        var initialDevices = new List<StorageDevice>();
        Exception initializationError = null;
        bool subscriptionAttempted = false;

        lock (_changeSync)
        {
            try
            {
                // Subscribe before enumerating so a topology change after the
                // initial monitoring baseline cannot fall into a blind window.
                // A synchronous callback from the subscription delegate is
                // buffered because Monitor is reentrant; callbacks from other
                // threads wait at _changeSync until the initial commit finishes.
                subscriptionAttempted = true;
                _subscribeDevicesChanged(OnStoragesChanged);

                lock (_sync)
                    _subscribed = true;

                List<StorageDeviceDIT> disks = _getDisks();
                foreach (StorageDeviceDIT disk in disks)
                    initialDevices.Add(_createStorageDevice(disk, _settings));

                lock (_sync)
                {
                    _hardware.AddRange(initialDevices);
                    initialDevices.Clear();
                    PublishHardwareSnapshot();
                }

                ReplayPendingInitializationChanges();
                _initializing = false;
                return;
            }
            catch (Exception ex)
            {
                initializationError = ex;
                _initializing = false;
                _initializationChanges.Clear();
            }
        }

        throw CleanupFailedInitialization(initialDevices, subscriptionAttempted, initializationError);
    }

    private void OnStoragesChanged(object sender, StorageDevicesChangedEventArgs e)
    {
        lock (_changeSync)
        {
            lock (_sync)
            {
                if (_closed)
                    return;
            }

            if (_initializing)
            {
                _initializationChanges.Queue(e?.Added, e?.Removed);
                return;
            }

            ApplyStorageChanges(e?.Added, e?.Removed);
        }
    }

    private void ApplyStorageChanges
    (
        IEnumerable<StorageDeviceDIT> added,
        IEnumerable<StorageDeviceDIT> removed)
    {
        var additionsToCreate = new List<StorageDeviceDIT>();

        lock (_sync)
        {
            if (_closed)
                return;

            foreach (StorageDeviceDIT storage in added ?? Enumerable.Empty<StorageDeviceDIT>())
            {
                if (_hardware.Any(device => device.Storage == storage) ||
                    additionsToCreate.Any(candidate => candidate == storage))
                    continue;

                additionsToCreate.Add(storage);
            }
        }

        var addedDevices = new List<StorageDevice>(additionsToCreate.Count);

        try
        {
            foreach (StorageDeviceDIT storage in additionsToCreate)
                addedDevices.Add(_createStorageDevice(storage, _settings));
        }
        catch (Exception ex)
        {
            throw CloseStorageDevicesBestEffort(addedDevices, ex);
        }

        List<StorageDevice> acceptedDevices = [];
        List<StorageDevice> redundantDevices = [];
        List<StorageDevice> removedDevices = [];
        bool committed;

        lock (_sync)
        {
            committed = !_closed;
            if (committed)
            {
                foreach (StorageDevice addedDevice in addedDevices)
                {
                    if (_hardware.Any(device => device.Storage == addedDevice.Storage))
                        redundantDevices.Add(addedDevice);
                    else
                    {
                        _hardware.Add(addedDevice);
                        acceptedDevices.Add(addedDevice);
                    }
                }

                foreach (StorageDeviceDIT removedStorage in removed ?? Enumerable.Empty<StorageDeviceDIT>())
                {
                    StorageDevice storageDevice = _hardware.Find(device => device.Storage == removedStorage);
                    if (storageDevice == null)
                        continue;

                    _hardware.Remove(storageDevice);
                    removedDevices.Add(storageDevice);
                }

                if (acceptedDevices.Count > 0 || removedDevices.Count > 0)
                    PublishHardwareSnapshot();
            }
        }

        if (!committed)
        {
            Exception lateCleanupError = CloseStorageDevicesBestEffort(addedDevices, null);
            if (lateCleanupError != null)
                throw lateCleanupError;

            return;
        }

        Exception firstError = CloseStorageDevicesBestEffort(redundantDevices, null);
        firstError = NotifyHardwareBestEffort(_hardwareAddedHandlers(), acceptedDevices, firstError);

        foreach (StorageDevice storageDevice in removedDevices)
        {
            if (!_closed)
                firstError = InvokeHardwareEventBestEffort(_hardwareRemovedHandlers(), storageDevice, firstError);

            try
            {
                storageDevice.Close();
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (firstError != null)
            throw firstError;
    }

    private Exception CleanupFailedInitialization
    (
        IEnumerable<StorageDevice> initialDevices,
        bool subscriptionAttempted,
        Exception firstError)
    {
        List<StorageDevice> devicesToClose;
        bool unsubscribe;

        lock (_sync)
        {
            _closed = true;
            unsubscribe = _subscribed || subscriptionAttempted;
            _subscribed = false;
            devicesToClose = _hardware.ToList();
            _hardware.Clear();
            PublishHardwareSnapshot();
        }

        if (unsubscribe)
        {
            try
            {
                _unsubscribeDevicesChanged(OnStoragesChanged);
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        devicesToClose.AddRange(initialDevices);
        return CloseStorageDevicesBestEffort(devicesToClose, firstError);
    }

    private void ReplayPendingInitializationChanges()
    {
        while (_initializationChanges.HasPendingChanges)
        {
            _initializationChanges.Drain(out StorageDeviceDIT[] added, out StorageDeviceDIT[] removed);
            ApplyStorageChanges(added, removed);
        }
    }

    private Exception NotifyHardwareBestEffort
    (
        HardwareEventHandler handlers,
        IEnumerable<StorageDevice> hardware,
        Exception firstError)
    {
        foreach (StorageDevice item in hardware)
        {
            if (_closed)
                break;

            firstError = InvokeHardwareEventBestEffort(handlers, item, firstError);
        }

        return firstError;
    }

    private static Exception InvokeHardwareEventBestEffort
    (
        HardwareEventHandler handlers,
        IHardware hardware,
        Exception firstError)
    {
        if (handlers == null)
            return firstError;

        foreach (HardwareEventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(hardware);
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        return firstError;
    }

    private static Exception CloseStorageDevicesBestEffort
    (
        IEnumerable<StorageDevice> devices,
        Exception firstError)
    {
        foreach (StorageDevice storageDevice in devices)
        {
            try
            {
                storageDevice.Close();
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        return firstError;
    }

    private void PublishHardwareSnapshot()
    {
        IReadOnlyList<IHardware> snapshot = Array.AsReadOnly(_hardware.Cast<IHardware>().ToArray());
        Volatile.Write(ref _hardwareSnapshot, snapshot);
    }
}

/// <summary>
/// Owns the bounded pending-change buffer used while the initial storage snapshot is being
/// created: additions and removals are coalesced by storage reference and capped at
/// <see cref="MaxPendingInitializationChanges" /> entries. Extracted from
/// <see cref="StorageGroup" /> without behavior change. All members must be called while holding
/// the lifecycle change lock, exactly as before the extraction.
/// </summary>
internal sealed class StorageInitializationChangeBuffer
{
    internal const int MaxPendingInitializationChanges = 256;

    private readonly List<StorageDeviceDIT> _pendingAdded = new();
    private readonly List<StorageDeviceDIT> _pendingRemoved = new();

    internal bool HasPendingChanges => _pendingAdded.Count > 0 || _pendingRemoved.Count > 0;

    internal void Queue
    (
        IEnumerable<StorageDeviceDIT> added,
        IEnumerable<StorageDeviceDIT> removed)
    {
        foreach (StorageDeviceDIT storage in added ?? Enumerable.Empty<StorageDeviceDIT>())
            CoalescePendingChange(storage, _pendingAdded, _pendingRemoved);

        foreach (StorageDeviceDIT storage in removed ?? Enumerable.Empty<StorageDeviceDIT>())
            CoalescePendingChange(storage, _pendingRemoved, _pendingAdded);
    }

    internal void Drain(out StorageDeviceDIT[] added, out StorageDeviceDIT[] removed)
    {
        added = _pendingAdded.ToArray();
        removed = _pendingRemoved.ToArray();
        _pendingAdded.Clear();
        _pendingRemoved.Clear();
    }

    internal void Clear()
    {
        _pendingAdded.Clear();
        _pendingRemoved.Clear();
    }

    private void CoalescePendingChange
    (
        StorageDeviceDIT storage,
        List<StorageDeviceDIT> destination,
        List<StorageDeviceDIT> opposite)
    {
        int oppositeIndex = opposite.FindIndex(candidate => candidate == storage);
        if (oppositeIndex >= 0)
            opposite.RemoveAt(oppositeIndex);

        int existingIndex = destination.FindIndex(candidate => candidate == storage);
        if (existingIndex >= 0)
        {
            destination[existingIndex] = storage;
            return;
        }

        if (_pendingAdded.Count + _pendingRemoved.Count >= MaxPendingInitializationChanges)
            throw new InvalidOperationException("Too many storage changes occurred while the initial device snapshot was being created.");

        destination.Add(storage);
    }
}
