// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Interop;

namespace LibreHardwareMonitor.Hardware.Gpu;

internal class NvidiaGroup : IGroup, IHardwareChanged
{
    private const int MaxRetainedMonitorErrors = 8;

    internal delegate bool TryEnumerateGpusDelegate(out NvApi.NvPhysicalGpuHandle[] handles, out int count);

    private readonly Func<bool> _acquireNvidiaMl;
    private readonly Func<int, NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle, ISettings, Hardware> _createHardware;
    private readonly Func<IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>> _getDisplayHandles;
    private readonly Dictionary<NvApi.NvPhysicalGpuHandle, Hardware> _hardwareByHandle = new();
    private readonly List<Hardware> _hardware = [];
    private readonly TimeSpan _monitorInterval;
    private readonly Queue<Exception> _monitorErrors = new();
    private readonly object _refreshSync = new();
    private readonly Action _releaseNvidiaMl;
    private readonly StringBuilder _report = new();
    private readonly ISettings _settings;
    private readonly object _sync = new();
    private readonly TryEnumerateGpusDelegate _tryEnumerateGpus;

    private CancellationTokenSource _cancellationTokenSource;
    private volatile bool _disposed;
    private IReadOnlyList<IHardware> _hardwareSnapshot = Array.AsReadOnly(Array.Empty<IHardware>());
    private Exception _lastMonitorError;
    private int _monitorErrorCount;
    private Task _monitorTask;
    private bool _nvidiaMlLeaseAcquiring;
    private bool _nvidiaMlLeaseHeld;
    private bool _nvidiaWasAvailable;
    private bool _refreshUsesNvidiaMl;
    private bool _releaseNvidiaMlWhenRefreshCompletes;

    public NvidiaGroup(ISettings settings)
        : this(settings,
               TryEnumerateGpus,
               GetDisplayHandles,
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
        _tryEnumerateGpus = tryEnumerateGpus ?? throw new ArgumentNullException(nameof(tryEnumerateGpus));
        _getDisplayHandles = getDisplayHandles ?? throw new ArgumentNullException(nameof(getDisplayHandles));
        _createHardware = createHardware ?? throw new ArgumentNullException(nameof(createHardware));
        _acquireNvidiaMl = acquireNvidiaMl ?? throw new ArgumentNullException(nameof(acquireNvidiaMl));
        _releaseNvidiaMl = releaseNvidiaMl ?? throw new ArgumentNullException(nameof(releaseNvidiaMl));
        _monitorInterval = monitorInterval ?? TimeSpan.FromSeconds(1);

        if (_monitorInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(monitorInterval));

        _report.AppendLine("NvApi");
        _report.AppendLine();

        if (Software.OperatingSystem.IsUnix)
        {
            _report.AppendLine("Status: Not supported on Unix for NvApi group");
            _report.AppendLine();
            return;
        }

        try
        {
            RefreshHardware(raiseEvents: false);
            if (monitorHardwareChanges)
                StartMonitorTask();
        }
        catch
        {
            try
            {
                Close();
            }
            catch
            {
                // Preserve the construction failure after best-effort lifetime cleanup.
            }

            throw;
        }
    }

    public event HardwareEventHandler HardwareAdded;

    public event HardwareEventHandler HardwareRemoved;

    public IReadOnlyList<IHardware> Hardware => Volatile.Read(ref _hardwareSnapshot);

    internal Exception LastMonitorError => Volatile.Read(ref _lastMonitorError);

    internal int MonitorErrorCount => Volatile.Read(ref _monitorErrorCount);

    internal IReadOnlyList<Exception> MonitorErrors
    {
        get
        {
            lock (_sync)
                return Array.AsReadOnly(_monitorErrors.ToArray());
        }
    }

    public string GetReport()
    {
        lock (_sync)
            return _report.ToString();
    }

    public void Close()
    {
        CancellationTokenSource cancellationTokenSource;
        Task monitorTask;
        List<Hardware> hardwareToClose;
        bool releaseNvidiaMl;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            cancellationTokenSource = _cancellationTokenSource;
            monitorTask = _monitorTask;
            _cancellationTokenSource = null;
            _monitorTask = null;

            hardwareToClose = _hardware.ToList();
            releaseNvidiaMl = _nvidiaMlLeaseHeld && !_refreshUsesNvidiaMl;
            _releaseNvidiaMlWhenRefreshCompletes = _nvidiaMlLeaseHeld && _refreshUsesNvidiaMl;
            _nvidiaMlLeaseHeld = false;

            _hardware.Clear();
            _hardwareByHandle.Clear();
            PublishHardwareSnapshot();
        }

        cancellationTokenSource?.Cancel();

        if (monitorTask != null && Task.CurrentId != monitorTask.Id)
        {
            try
            {
                monitorTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // The monitor may already be faulted. State ownership has still been
                // detached, so cleanup below must continue.
            }
        }

        cancellationTokenSource?.Dispose();

        // NvidiaGpu.Close restores NVAPI controls but does not use NVML. Release
        // the process-wide lease before external/device cleanup so a blocked
        // close callback cannot retain the last native lease indefinitely.
        Exception firstCloseError = ReleaseNvidiaMlBestEffort(releaseNvidiaMl, null);
        firstCloseError = CloseHardwareBestEffort(hardwareToClose, firstCloseError);

        if (firstCloseError != null)
            throw firstCloseError;
    }

    private void StartMonitorTask()
    {
        CancellationTokenSource cts = new();
        _cancellationTokenSource = cts;

        CancellationToken token = cts.Token;

        _monitorTask = Task.Run(async () =>
                                {
                                    while (!token.IsCancellationRequested)
                                    {
                                        try
                                        {
                                            await Task.Delay(_monitorInterval, token).ConfigureAwait(false);
                                        }
                                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                                        {
                                            break;
                                        }

                                        if (_disposed)
                                        {
                                            break;
                                        }

                                        try
                                        {
                                            RefreshHardware(raiseEvents: true);
                                        }
                                        catch (Exception ex)
                                        {
                                            RecordMonitorError(ex);
                                        }
                                    }
                                },
                                token);
    }

    private void RecordMonitorError(Exception error)
    {
        Volatile.Write(ref _lastMonitorError, error);
        Interlocked.Increment(ref _monitorErrorCount);

        lock (_sync)
        {
            if (_monitorErrors.Count == MaxRetainedMonitorErrors)
                _monitorErrors.Dequeue();

            _monitorErrors.Enqueue(error);
        }
    }

    private void RefreshHardware(bool raiseEvents)
    {
        lock (_refreshSync)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
            }

            Exception firstError = null;

            try
            {
                RefreshHardwareCore(raiseEvents);
            }
            catch (Exception ex)
            {
                firstError = ex;
            }

            firstError = CompleteRefresh(firstError);
            if (firstError != null)
                throw firstError;
        }
    }

    private void RefreshHardwareCore(bool raiseEvents)
    {
            Dictionary<NvApi.NvPhysicalGpuHandle, Hardware> existingByHandle;
            List<Hardware> existingHardware;
            bool nvidiaWasAvailable;

            lock (_sync)
            {
                if (_disposed)
                    return;

                existingByHandle = new Dictionary<NvApi.NvPhysicalGpuHandle, Hardware>(_hardwareByHandle);
                existingHardware = _hardware.ToList();
                nvidiaWasAvailable = _nvidiaWasAvailable;
            }

            bool isAvailable = _tryEnumerateGpus(out NvApi.NvPhysicalGpuHandle[] handles, out int count);
            if (!isAvailable)
            {
                CommitUnavailableHardware(raiseEvents);
                return;
            }

            if (!BeginNvidiaMlRefreshUse())
                return;

            EnsureNvidiaMlLease();

            if (_disposed)
                return;

            string version = null;
            if (!nvidiaWasAvailable)
            {
                if (NvApi.NvAPI_GetInterfaceVersionString != null &&
                    NvApi.NvAPI_GetInterfaceVersionString(out string interfaceVersion) == NvApi.NvStatus.OK)
                    version = interfaceVersion;
            }

            IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> displayHandles = _getDisplayHandles();

            handles ??= Array.Empty<NvApi.NvPhysicalGpuHandle>();
            int boundedCount = Math.Max(0, Math.Min(count, handles.Length));
            var currentSet = new HashSet<NvApi.NvPhysicalGpuHandle>(handles.Take(boundedCount));
            var nextByHandle = new Dictionary<NvApi.NvPhysicalGpuHandle, Hardware>(existingByHandle);
            var nextHardware = new List<Hardware>(existingHardware);
            List<Hardware> added = [];
            List<Hardware> removed = [];

            foreach (KeyValuePair<NvApi.NvPhysicalGpuHandle, Hardware> pair in existingByHandle)
            {
                if (!currentSet.Contains(pair.Key))
                {
                    nextByHandle.Remove(pair.Key);
                    nextHardware.Remove(pair.Value);
                    removed.Add(pair.Value);
                }
            }

            try
            {
                for (int i = 0; i < boundedCount; ++i)
                {
                    NvApi.NvPhysicalGpuHandle handle = handles[i];

                    if (nextByHandle.ContainsKey(handle))
                        continue;

                    displayHandles.TryGetValue(handle, out NvApi.NvDisplayHandle displayHandle);

                    Hardware gpu = _createHardware(i, handle, displayHandle, _settings);
                    nextByHandle.Add(handle, gpu);
                    nextHardware.Add(gpu);
                    added.Add(gpu);
                }
            }
            catch (Exception ex)
            {
                throw CloseHardwareBestEffort(added, ex);
            }

            bool committed;
            lock (_sync)
            {
                committed = !_disposed;
                if (committed)
                {
                    _hardwareByHandle.Clear();
                    foreach (KeyValuePair<NvApi.NvPhysicalGpuHandle, Hardware> pair in nextByHandle)
                        _hardwareByHandle.Add(pair.Key, pair.Value);

                    _hardware.Clear();
                    _hardware.AddRange(nextHardware);
                    _nvidiaWasAvailable = true;

                    if (!nvidiaWasAvailable)
                    {
                        if (version != null)
                        {
                            _report.Append("Version: ");
                            _report.AppendLine(version);
                        }

                        _report.Append("Number of GPUs: ");
                        _report.AppendLine(boundedCount.ToString(CultureInfo.InvariantCulture));
                        _report.AppendLine();
                    }

                    if (added.Count > 0 || removed.Count > 0)
                        PublishHardwareSnapshot();
                }
            }

            if (!committed)
            {
                Exception lateCleanupError = CloseHardwareBestEffort(added, null);
                if (lateCleanupError != null)
                    throw lateCleanupError;

                return;
            }

            Exception firstError = NotifyHardwareBestEffort(HardwareAdded, added, raiseEvents, null);
            firstError = NotifyAndCloseRemovedHardwareBestEffort(removed, raiseEvents, firstError);

            if (firstError != null)
                throw firstError;
    }

    private void CommitUnavailableHardware(bool raiseEvents)
    {
        List<Hardware> removed;
        bool releaseNvidiaMl;

        lock (_sync)
        {
            if (_disposed)
                return;

            releaseNvidiaMl = _nvidiaMlLeaseHeld;
            _nvidiaMlLeaseHeld = false;
            _nvidiaWasAvailable = false;
            removed = _hardware.ToList();
            _hardware.Clear();
            _hardwareByHandle.Clear();

            if (removed.Count > 0)
                PublishHardwareSnapshot();
        }

        // The driver is already unavailable. Drop this group's NVML lease
        // before invoking external removal callbacks or device cleanup.
        Exception firstError = ReleaseNvidiaMlBestEffort(releaseNvidiaMl, null);
        firstError = NotifyAndCloseRemovedHardwareBestEffort(removed, raiseEvents, firstError);
        if (firstError != null)
            throw firstError;
    }

    private Exception NotifyAndCloseRemovedHardwareBestEffort
    (
        IEnumerable<Hardware> removed,
        bool raiseEvents,
        Exception firstError)
    {
        foreach (Hardware gpu in removed)
        {
            if (raiseEvents && !_disposed)
                firstError = InvokeHardwareEventBestEffort(HardwareRemoved, gpu, firstError);

            try
            {
                gpu.Close();
            }
            catch (Exception ex)
            {
                firstError = CombineErrors(firstError, ex);
            }
        }

        return firstError;
    }

    private bool EnsureNvidiaMlLease()
    {
        lock (_sync)
        {
            if (_disposed)
                return false;

            if (_nvidiaMlLeaseHeld)
                return true;

            if (_nvidiaMlLeaseAcquiring)
                return false;

            _nvidiaMlLeaseAcquiring = true;
        }

        bool acquired = false;
        Exception firstError = null;

        try
        {
            acquired = _acquireNvidiaMl();
        }
        catch (Exception ex)
        {
            firstError = ex;
        }

        bool keepLease = false;
        bool releaseLateLease = false;

        lock (_sync)
        {
            _nvidiaMlLeaseAcquiring = false;

            if (acquired)
            {
                if (_disposed)
                    releaseLateLease = true;
                else
                {
                    _nvidiaMlLeaseHeld = true;
                    keepLease = true;
                }
            }
        }

        firstError = ReleaseNvidiaMlBestEffort(releaseLateLease, firstError);
        if (firstError != null)
            throw firstError;

        return keepLease;
    }

    private bool BeginNvidiaMlRefreshUse()
    {
        lock (_sync)
        {
            if (_disposed)
                return false;

            _refreshUsesNvidiaMl = true;
            return true;
        }
    }

    private Exception CompleteRefresh(Exception firstError)
    {
        bool releaseNvidiaMl;

        lock (_sync)
        {
            _refreshUsesNvidiaMl = false;
            releaseNvidiaMl = _releaseNvidiaMlWhenRefreshCompletes;
            _releaseNvidiaMlWhenRefreshCompletes = false;
        }

        return ReleaseNvidiaMlBestEffort(releaseNvidiaMl, firstError);
    }

    private Exception ReleaseNvidiaMlBestEffort(bool release, Exception firstError)
    {
        if (!release)
            return firstError;

        try
        {
            _releaseNvidiaMl();
        }
        catch (Exception ex)
        {
            firstError = CombineErrors(firstError, ex);
        }

        return firstError;
    }

    private Exception NotifyHardwareBestEffort
    (
        HardwareEventHandler handlers,
        IEnumerable<Hardware> hardware,
        bool raiseEvents,
        Exception firstError)
    {
        if (!raiseEvents)
            return firstError;

        foreach (Hardware item in hardware)
        {
            if (_disposed)
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
                firstError = CombineErrors(firstError, ex);
            }
        }

        return firstError;
    }

    private static Exception CloseHardwareBestEffort
    (
        IEnumerable<Hardware> hardware,
        Exception firstError)
    {
        foreach (Hardware item in hardware)
        {
            try
            {
                item.Close();
            }
            catch (Exception ex)
            {
                firstError = CombineErrors(firstError, ex);
            }
        }

        return firstError;
    }

    private static Exception CombineErrors(Exception firstError, Exception nextError)
    {
        if (firstError == null)
            return nextError;

        return new AggregateException(firstError, nextError).Flatten();
    }

    internal void RefreshHardware() => RefreshHardware(raiseEvents: true);

    private void PublishHardwareSnapshot()
    {
        IReadOnlyList<IHardware> snapshot = Array.AsReadOnly(_hardware.Cast<IHardware>().ToArray());
        Volatile.Write(ref _hardwareSnapshot, snapshot);
    }

    private static IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> GetDisplayHandles()
    {
        Dictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> displayHandles = [];

        if (NvApi.NvAPI_EnumNvidiaDisplayHandle == null || NvApi.NvAPI_GetPhysicalGPUsFromDisplay == null)
        {
            return displayHandles;
        }

        NvApi.NvStatus status = NvApi.NvStatus.OK;
        int i = 0;

        while (status == NvApi.NvStatus.OK)
        {
            NvApi.NvDisplayHandle displayHandle = new();
            status = NvApi.NvAPI_EnumNvidiaDisplayHandle(i, ref displayHandle);
            i++;

            if (status != NvApi.NvStatus.OK)
            {
                continue;
            }

            NvApi.NvPhysicalGpuHandle[] handlesFromDisplay = new NvApi.NvPhysicalGpuHandle[NvApi.MAX_PHYSICAL_GPUS];
            if (NvApi.NvAPI_GetPhysicalGPUsFromDisplay(displayHandle, handlesFromDisplay, out uint countFromDisplay) != NvApi.NvStatus.OK)
            {
                continue;
            }

            for (int j = 0; j < countFromDisplay; j++)
            {
                if (!displayHandles.ContainsKey(handlesFromDisplay[j]))
                {
                    displayHandles.Add(handlesFromDisplay[j], displayHandle);
                }
            }
        }

        return displayHandles;
    }

    private static bool TryEnumerateGpus(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
    {
        handles = new NvApi.NvPhysicalGpuHandle[NvApi.MAX_PHYSICAL_GPUS];
        count = 0;

        NvApi.Initialize();

        if (!NvApi.IsAvailable || NvApi.NvAPI_EnumPhysicalGPUs == null)
        {
            return false;
        }

        NvApi.NvStatus status = NvApi.NvAPI_EnumPhysicalGPUs(handles, out count);
        return status == NvApi.NvStatus.OK && count > 0;
    }
}
