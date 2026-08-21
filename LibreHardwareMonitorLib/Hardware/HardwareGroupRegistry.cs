// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Owns the <see cref="IGroup" /> list of a <see cref="Computer" />: registration, dedupe,
/// <see cref="IHardwareChanged" /> forwarding, notification ordering, reverse-of-registration
/// drain, and first-failure aggregation. Extracted from <see cref="Computer" /> without behavior
/// change. Groups are stored and their <see cref="IGroup.Hardware" /> property is read as exposed;
/// no snapshot versus live-list semantics are assumed.
/// </summary>
internal sealed class HardwareGroupRegistry
{
    private readonly List<IGroup> _groups = new();
    private readonly object _lock = new();
    private readonly Func<HardwareEventHandler> _hardwareAddedHandlers;
    private readonly Func<HardwareEventHandler> _hardwareRemovedHandlers;

    /// <summary>
    /// Creates a registry that raises notifications through the handler lists returned by the
    /// given accessors. Each accessor must return the current downstream
    /// <see cref="HardwareEventHandler" /> invocation list, or <c>null</c> when there are no
    /// subscribers; a <c>null</c> list skips the hardware snapshot capture entirely, exactly as
    /// before the extraction.
    /// </summary>
    internal HardwareGroupRegistry(
        Func<HardwareEventHandler> hardwareAddedHandlers,
        Func<HardwareEventHandler> hardwareRemovedHandlers)
    {
        _hardwareAddedHandlers = hardwareAddedHandlers ?? throw new ArgumentNullException(nameof(hardwareAddedHandlers));
        _hardwareRemovedHandlers = hardwareRemovedHandlers ?? throw new ArgumentNullException(nameof(hardwareRemovedHandlers));
    }

    /// <summary>
    /// The registered groups in registration order. Access only while holding <see cref="SyncRoot" />.
    /// </summary>
    internal List<IGroup> Groups => _groups;

    /// <summary>
    /// The lock guarding <see cref="Groups" />. <see cref="Computer" /> shares this lock so its
    /// lifecycle guards stay mutually exclusive with every registry mutation, exactly as before
    /// the extraction.
    /// </summary>
    internal object SyncRoot => _lock;

    internal void Add(IGroup group)
    {
        if (group == null)
            return;

        lock (_lock)
        {
            if (_groups.Contains(group))
                return;

            _groups.Add(group);

            if (group is IHardwareChanged hardwareChanged)
            {
                hardwareChanged.HardwareAdded += HardwareAddedEvent;
                hardwareChanged.HardwareRemoved += HardwareRemovedEvent;
            }
        }

        Exception firstFailure = null;
        HardwareEventHandler handlers = _hardwareAddedHandlers();
        IReadOnlyList<IHardware> hardwareSnapshot = null;

        if (handlers != null)
        {
            CaptureFailure(() => hardwareSnapshot = group.Hardware, ref firstFailure);
            NotifyHardwareHandlers(handlers, hardwareSnapshot, ref firstFailure);
        }

        ThrowIfFailed(firstFailure);
    }

    internal void Remove(IGroup group)
    {
        Exception firstFailure = null;

        lock (_lock)
        {
            if (!_groups.Contains(group))
                return;

            _groups.Remove(group);

            if (group is IHardwareChanged hardwareChanged)
            {
                CaptureFailure(
                    () => hardwareChanged.HardwareAdded -= HardwareAddedEvent,
                    ref firstFailure);
                CaptureFailure(
                    () => hardwareChanged.HardwareRemoved -= HardwareRemovedEvent,
                    ref firstFailure);
            }
        }

        HardwareEventHandler handlers = _hardwareRemovedHandlers();
        IReadOnlyList<IHardware> hardwareSnapshot = null;
        if (handlers != null)
        {
            CaptureFailure(() => hardwareSnapshot = group.Hardware, ref firstFailure);
            NotifyHardwareHandlers(handlers, hardwareSnapshot, ref firstFailure);
        }

        CaptureFailure(group.Close, ref firstFailure);
        ThrowIfFailed(firstFailure);
    }

    internal void RemoveType<T>() where T : IGroup
    {
        List<T> list = [];

        lock (_lock)
        {
            foreach (IGroup group in _groups)
            {
                if (group is T t)
                    list.Add(t);
            }
        }

        Exception firstFailure = null;
        foreach (T group in list)
            CaptureFailure(() => Remove(group), ref firstFailure);

        ThrowIfFailed(firstFailure);
    }

    internal void RemoveGroups()
    {
        Exception firstFailure = null;
        while (true)
        {
            IGroup group;
            lock (_lock)
            {
                if (_groups.Count == 0)
                    break;

                group = _groups[_groups.Count - 1];
            }

            CaptureFailure(() => Remove(group), ref firstFailure);
        }

        ThrowIfFailed(firstFailure);
    }

    /// <summary>
    /// Best-effort detach of the forwarding subscriptions of <paramref name="group" />.
    /// Used by <see cref="Computer" /> rollback while the caller holds <see cref="SyncRoot" />.
    /// </summary>
    internal void Detach(IGroup group)
    {
        if (group is not IHardwareChanged hardwareChanged)
            return;

        try
        {
            hardwareChanged.HardwareAdded -= HardwareAddedEvent;
        }
        catch
        {
            // Keep rolling back the remaining owners.
        }

        try
        {
            hardwareChanged.HardwareRemoved -= HardwareRemovedEvent;
        }
        catch
        {
            // Keep rolling back the remaining owners.
        }
    }

    private void HardwareAddedEvent(IHardware hardware)
    {
        Exception firstFailure = null;
        NotifyHardwareHandlers(_hardwareAddedHandlers(), hardware, ref firstFailure);
        ThrowIfFailed(firstFailure);
    }

    private void HardwareRemovedEvent(IHardware hardware)
    {
        Exception firstFailure = null;
        NotifyHardwareHandlers(_hardwareRemovedHandlers(), hardware, ref firstFailure);
        ThrowIfFailed(firstFailure);
    }

    internal static void CaptureFailure(Action action, ref Exception firstFailure)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            firstFailure ??= exception;
        }
    }

    internal static void NotifyHardwareHandlers(
        HardwareEventHandler handlers,
        IReadOnlyList<IHardware> hardware,
        ref Exception firstFailure)
    {
        if (handlers == null || hardware == null)
            return;

        int count;
        try
        {
            count = hardware.Count;
        }
        catch (Exception exception)
        {
            firstFailure ??= exception;
            return;
        }

        for (int i = 0; i < count; i++)
        {
            IHardware item;
            try
            {
                item = hardware[i];
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                continue;
            }

            NotifyHardwareHandlers(handlers, item, ref firstFailure);
        }
    }

    internal static void NotifyHardwareHandlers(
        HardwareEventHandler handlers,
        IHardware hardware,
        ref Exception firstFailure)
    {
        if (handlers == null)
            return;

        foreach (HardwareEventHandler handler in handlers.GetInvocationList())
            CaptureFailure(() => handler(hardware), ref firstFailure);
    }

    internal static void ThrowIfFailed(Exception failure)
    {
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
