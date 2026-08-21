// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware.Gpu.Nvidia;
using LibreHardwareMonitor.Interop;

namespace LibreHardwareMonitor.Hardware.Gpu;

/// <summary>
/// The NVIDIA hardware group facade. Discovery, background-monitor update, and close mechanics
/// live in the internal collaborators of <see cref="NvidiaGroupLifecycle" />; this type keeps the
/// public constructors, the <see cref="IGroup" /> / <see cref="IHardwareChanged" /> surface, and
/// the settings field, and delegates the mechanics without behavior change.
/// </summary>
internal class NvidiaGroup : IGroup, IHardwareChanged
{
    internal delegate bool TryEnumerateGpusDelegate(out NvApi.NvPhysicalGpuHandle[] handles, out int count);

    private readonly NvidiaGroupLifecycle _lifecycle;
    private readonly ISettings _settings;

    public NvidiaGroup(ISettings settings)
        : this(settings,
               NvidiaDiscovery.TryEnumerateGpus,
               NvidiaDiscovery.GetDisplayHandles,
               (adapterIndex, handle, displayHandle, hardwareSettings) => new NvidiaGpu(adapterIndex, handle, displayHandle, hardwareSettings),
               NvidiaML.Initialize,
               NvidiaML.Close,
               monitorHardwareChanges: true)
    {
    }

    internal NvidiaGroup
    (
        ISettings settings,
        TryEnumerateGpusDelegate tryEnumerateGpus,
        Func<IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>> getDisplayHandles,
        Func<int, NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle, ISettings, Hardware> createHardware,
        Func<bool> acquireNvidiaMl,
        Action releaseNvidiaMl,
        bool monitorHardwareChanges,
        TimeSpan? monitorInterval = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _lifecycle = new NvidiaGroupLifecycle(_settings,
                                              tryEnumerateGpus,
                                              getDisplayHandles,
                                              createHardware,
                                              acquireNvidiaMl,
                                              releaseNvidiaMl,
                                              monitorHardwareChanges,
                                              monitorInterval,
                                              () => HardwareAdded,
                                              () => HardwareRemoved);
    }

    public event HardwareEventHandler HardwareAdded;

    public event HardwareEventHandler HardwareRemoved;

    public IReadOnlyList<IHardware> Hardware => _lifecycle.Hardware;

    internal Exception LastMonitorError => _lifecycle.LastMonitorError;

    internal int MonitorErrorCount => _lifecycle.MonitorErrorCount;

    internal IReadOnlyList<Exception> MonitorErrors => _lifecycle.MonitorErrors;

    public string GetReport() => _lifecycle.GetReport();

    public void Close() => _lifecycle.Close();

    internal void RefreshHardware() => _lifecycle.RefreshHardware();
}
