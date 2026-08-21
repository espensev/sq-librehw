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

namespace LibreHardwareMonitor.Hardware.Gpu.Nvidia;

/// <summary>
/// Owns the NVIDIA group lifecycle mechanics behind the <see cref="NvidiaGroup" /> facade:
/// discovery (GPU enumeration, display handles, NVML lease usage), the handle-diff refresh with
/// commit and immutable snapshot publication, and close (bounded monitor join, lease release,
/// device close). Extracted from <see cref="NvidiaGroup" /> without behavior, timing, or NVML
/// lifetime change. Change notifications are raised through the handler-list accessors supplied by
/// the facade, which keep subscriber semantics identical to the pre-extraction events.
/// </summary>
internal sealed class NvidiaGroupLifecycle
{
    private readonly Func<int, NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle, ISettings, Hardware> _createHardware;
    private readonly Func<IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>> _getDisplayHandles;
    private readonly Dictionary<NvApi.NvPhysicalGpuHandle, Hardware> _hardwareByHandle = new();
    private readonly List<Hardware> _hardware = [];
    private readonly Func<HardwareEventHandler> _hardwareAddedHandlers;
    private readonly Func<HardwareEventHandler> _hardwareRemovedHandlers;
    private readonly NvidiaMlLeaseTracker _lease;
    private readonly NvidiaMonitorLoop _monitorLoop;
    private readonly object _refreshSync = new();
    private readonly StringBuilder _report = new();
    private readonly ISettings _settings;
    private readonly object _sync = new();
    private readonly NvidiaGroup.TryEnumerateGpusDelegate _tryEnumerateGpus;

    private volatile bool _disposed;
    private IReadOnlyList<IHardware> _hardwareSnapshot = Array.AsReadOnly(Array.Empty<IHardware>());
    private bool _nvidiaWasAvailable;

    internal NvidiaGroupLifecycle
    (
        ISettings settings,
        NvidiaGroup.TryEnumerateGpusDelegate tryEnumerateGpus,
        Func<IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle>> getDisplayHandles,
        Func<int, NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle, ISettings, Hardware> createHardware,
        Func<bool> acquireNvidiaMl,
        Action releaseNvidiaMl,
        bool monitorHardwareChanges,
        TimeSpan? monitorInterval,
        Func<HardwareEventHandler> hardwareAddedHandlers,
        Func<HardwareEventHandler> hardwareRemovedHandlers)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _tryEnumerateGpus = tryEnumerateGpus ?? throw new ArgumentNullException(nameof(tryEnumerateGpus));
        _getDisplayHandles = getDisplayHandles ?? throw new ArgumentNullException(nameof(getDisplayHandles));
        _createHardware = createHardware ?? throw new ArgumentNullException(nameof(createHardware));
        _lease = new NvidiaMlLeaseTracker(_sync,
                                          () => _disposed,
                                          acquireNvidiaMl ?? throw new ArgumentNullException(nameof(acquireNvidiaMl)),
                                          releaseNvidiaMl ?? throw new ArgumentNullException(nameof(releaseNvidiaMl)));
        _hardwareAddedHandlers = hardwareAddedHandlers ?? throw new ArgumentNullException(nameof(hardwareAddedHandlers));
        _hardwareRemovedHandlers = hardwareRemovedHandlers ?? throw new ArgumentNullException(nameof(hardwareRemovedHandlers));

        TimeSpan interval = monitorInterval ?? TimeSpan.FromSeconds(1);

        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(monitorInterval));

        _monitorLoop = new NvidiaMonitorLoop(_sync, () => _disposed, interval, () => RefreshHardware(raiseEvents: true));

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
                _monitorLoop.Start();
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

    internal IReadOnlyList<IHardware> Hardware => Volatile.Read(ref _hardwareSnapshot);

    internal Exception LastMonitorError => _monitorLoop.LastError;

    internal int MonitorErrorCount => _monitorLoop.ErrorCount;

    internal IReadOnlyList<Exception> MonitorErrors => _monitorLoop.Errors;

    internal string GetReport()
    {
        lock (_sync)
            return _report.ToString();
    }

    internal void Close()
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
            _monitorLoop.Detach(out cancellationTokenSource, out monitorTask);

            hardwareToClose = _hardware.ToList();
            releaseNvidiaMl = _lease.DetachForClose();

            _hardware.Clear();
            _hardwareByHandle.Clear();
            PublishHardwareSnapshot();
        }

        NvidiaMonitorLoop.CancelJoinAndDispose(cancellationTokenSource, monitorTask);

        // NvidiaGpu.Close restores NVAPI controls but does not use NVML. Release
        // the process-wide lease before external/device cleanup so a blocked
        // close callback cannot retain the last native lease indefinitely.
        Exception firstCloseError = _lease.ReleaseBestEffort(releaseNvidiaMl, null);
        firstCloseError = CloseHardwareBestEffort(hardwareToClose, firstCloseError);

        if (firstCloseError != null)
            throw firstCloseError;
    }

    internal void RefreshHardware() => RefreshHardware(raiseEvents: true);

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

            firstError = _lease.CompleteRefresh(firstError);
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

        if (!_lease.BeginRefreshUse())
            return;

        _lease.EnsureLease();

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

        Exception firstError = NotifyHardwareBestEffort(_hardwareAddedHandlers(), added, raiseEvents, null);
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

            releaseNvidiaMl = _lease.DetachHeldLease();
            _nvidiaWasAvailable = false;
            removed = _hardware.ToList();
            _hardware.Clear();
            _hardwareByHandle.Clear();

            if (removed.Count > 0)
                PublishHardwareSnapshot();
        }

        // The driver is already unavailable. Drop this group's NVML lease
        // before invoking external removal callbacks or device cleanup.
        Exception firstError = _lease.ReleaseBestEffort(releaseNvidiaMl, null);
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
                firstError = InvokeHardwareEventBestEffort(_hardwareRemovedHandlers(), gpu, firstError);

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

    internal static Exception CombineErrors(Exception firstError, Exception nextError)
    {
        if (firstError == null)
            return nextError;

        return new AggregateException(firstError, nextError).Flatten();
    }

    private void PublishHardwareSnapshot()
    {
        IReadOnlyList<IHardware> snapshot = Array.AsReadOnly(_hardware.Cast<IHardware>().ToArray());
        Volatile.Write(ref _hardwareSnapshot, snapshot);
    }
}

/// <summary>
/// Owns the NVIDIA group's NVML lease state machine: acquisition reentrancy, refresh-use tracking,
/// deferred release when a close lands mid-refresh, and best-effort release with first-error
/// aggregation. Extracted from <see cref="NvidiaGroup" /> without NVML lifetime change. The group
/// lock is shared with the lifecycle so lease and hardware state transitions stay atomic, exactly
/// as before the extraction.
/// </summary>
internal sealed class NvidiaMlLeaseTracker
{
    private readonly Func<bool> _acquireNvidiaMl;
    private readonly Func<bool> _isDisposed;
    private readonly Action _releaseNvidiaMl;
    private readonly object _sync;

    private bool _nvidiaMlLeaseAcquiring;
    private bool _nvidiaMlLeaseHeld;
    private bool _refreshUsesNvidiaMl;
    private bool _releaseNvidiaMlWhenRefreshCompletes;

    internal NvidiaMlLeaseTracker
    (
        object sync,
        Func<bool> isDisposed,
        Func<bool> acquireNvidiaMl,
        Action releaseNvidiaMl)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
        _acquireNvidiaMl = acquireNvidiaMl ?? throw new ArgumentNullException(nameof(acquireNvidiaMl));
        _releaseNvidiaMl = releaseNvidiaMl ?? throw new ArgumentNullException(nameof(releaseNvidiaMl));
    }

    internal bool EnsureLease()
    {
        lock (_sync)
        {
            if (_isDisposed())
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
                if (_isDisposed())
                    releaseLateLease = true;
                else
                {
                    _nvidiaMlLeaseHeld = true;
                    keepLease = true;
                }
            }
        }

        firstError = ReleaseBestEffort(releaseLateLease, firstError);
        if (firstError != null)
            throw firstError;

        return keepLease;
    }

    internal bool BeginRefreshUse()
    {
        lock (_sync)
        {
            if (_isDisposed())
                return false;

            _refreshUsesNvidiaMl = true;
            return true;
        }
    }

    internal Exception CompleteRefresh(Exception firstError)
    {
        bool releaseNvidiaMl;

        lock (_sync)
        {
            _refreshUsesNvidiaMl = false;
            releaseNvidiaMl = _releaseNvidiaMlWhenRefreshCompletes;
            _releaseNvidiaMlWhenRefreshCompletes = false;
        }

        return ReleaseBestEffort(releaseNvidiaMl, firstError);
    }

    /// <summary>
    /// Detaches the lease state for close. Must be called while holding the shared group lock.
    /// Returns whether the caller must release the lease now; when a refresh is in flight the
    /// release is deferred and lands in <see cref="CompleteRefresh" /> instead.
    /// </summary>
    internal bool DetachForClose()
    {
        bool releaseNvidiaMl = _nvidiaMlLeaseHeld && !_refreshUsesNvidiaMl;
        _releaseNvidiaMlWhenRefreshCompletes = _nvidiaMlLeaseHeld && _refreshUsesNvidiaMl;
        _nvidiaMlLeaseHeld = false;
        return releaseNvidiaMl;
    }

    /// <summary>
    /// Detaches the held lease for a driver-unavailable transition. Must be called while holding
    /// the shared group lock. Returns whether the caller must release the lease.
    /// </summary>
    internal bool DetachHeldLease()
    {
        bool releaseNvidiaMl = _nvidiaMlLeaseHeld;
        _nvidiaMlLeaseHeld = false;
        return releaseNvidiaMl;
    }

    internal Exception ReleaseBestEffort(bool release, Exception firstError)
    {
        if (!release)
            return firstError;

        try
        {
            _releaseNvidiaMl();
        }
        catch (Exception ex)
        {
            firstError = NvidiaGroupLifecycle.CombineErrors(firstError, ex);
        }

        return firstError;
    }
}

/// <summary>
/// Owns the NVIDIA group's background monitor mechanics: the single monitor task on its unchanged
/// cadence, the bounded error queue, and the detach/cancel/bounded-join/dispose sequence used by
/// close. Extracted from <see cref="NvidiaGroup" /> without timing or threading change; the monitor
/// task remains the only background thread. The group lock is shared with the lifecycle so error
/// retention stays consistent with the group's observability surface, exactly as before the
/// extraction.
/// </summary>
internal sealed class NvidiaMonitorLoop
{
    internal const int MaxRetainedMonitorErrors = 8;

    private readonly Action _cycle;
    private readonly Queue<Exception> _errors = new();
    private readonly TimeSpan _interval;
    private readonly Func<bool> _isDisposed;
    private readonly object _sync;

    private CancellationTokenSource _cancellationTokenSource;
    private Exception _lastError;
    private int _errorCount;
    private Task _task;

    internal NvidiaMonitorLoop
    (
        object sync,
        Func<bool> isDisposed,
        TimeSpan interval,
        Action cycle)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
        _interval = interval;
        _cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
    }

    internal Exception LastError => Volatile.Read(ref _lastError);

    internal int ErrorCount => Volatile.Read(ref _errorCount);

    internal IReadOnlyList<Exception> Errors
    {
        get
        {
            lock (_sync)
                return Array.AsReadOnly(_errors.ToArray());
        }
    }

    internal void Start()
    {
        CancellationTokenSource cts = new();
        _cancellationTokenSource = cts;

        CancellationToken token = cts.Token;

        _task = Task.Run(async () =>
                         {
                             while (!token.IsCancellationRequested)
                             {
                                 try
                                 {
                                     await Task.Delay(_interval, token).ConfigureAwait(false);
                                 }
                                 catch (OperationCanceledException) when (token.IsCancellationRequested)
                                 {
                                     break;
                                 }

                                 if (_isDisposed())
                                 {
                                     break;
                                 }

                                 try
                                 {
                                     _cycle();
                                 }
                                 catch (Exception ex)
                                 {
                                     RecordError(ex);
                                 }
                             }
                         },
                         token);
    }

    /// <summary>
    /// Detaches the monitor task and its cancellation source. Must be called while holding the
    /// shared group lock.
    /// </summary>
    internal void Detach(out CancellationTokenSource cancellationTokenSource, out Task task)
    {
        cancellationTokenSource = _cancellationTokenSource;
        task = _task;
        _cancellationTokenSource = null;
        _task = null;
    }

    /// <summary>
    /// Cancels the monitor task, joins it with a bounded wait, and disposes the cancellation
    /// source. The join is skipped when close runs on the monitor task itself.
    /// </summary>
    internal static void CancelJoinAndDispose(CancellationTokenSource cancellationTokenSource, Task task)
    {
        cancellationTokenSource?.Cancel();

        if (task != null && Task.CurrentId != task.Id)
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // The monitor may already be faulted. State ownership has still been
                // detached, so cleanup below must continue.
            }
        }

        cancellationTokenSource?.Dispose();
    }

    private void RecordError(Exception error)
    {
        Volatile.Write(ref _lastError, error);
        Interlocked.Increment(ref _errorCount);

        lock (_sync)
        {
            if (_errors.Count == MaxRetainedMonitorErrors)
                _errors.Dequeue();

            _errors.Enqueue(error);
        }
    }
}

/// <summary>
/// The production NvApi discovery entry points used by the public <see cref="NvidiaGroup" />
/// constructor: physical-GPU enumeration and display-handle mapping. Moved verbatim from
/// <see cref="NvidiaGroup" />.
/// </summary>
internal static class NvidiaDiscovery
{
    internal static IDictionary<NvApi.NvPhysicalGpuHandle, NvApi.NvDisplayHandle> GetDisplayHandles()
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

    internal static bool TryEnumerateGpus(out NvApi.NvPhysicalGpuHandle[] handles, out int count)
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
