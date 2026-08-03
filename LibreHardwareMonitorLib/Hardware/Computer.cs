// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware.Battery;
using LibreHardwareMonitor.Hardware.Controller.AeroCool;
using LibreHardwareMonitor.Hardware.Controller.AquaComputer;
using LibreHardwareMonitor.Hardware.Controller.Arctic;
using LibreHardwareMonitor.Hardware.Controller.Heatmaster;
using LibreHardwareMonitor.Hardware.Controller.MSI;
using LibreHardwareMonitor.Hardware.Controller.Nzxt;
using LibreHardwareMonitor.Hardware.Controller.Razer;
using LibreHardwareMonitor.Hardware.Controller.TBalancer;
using LibreHardwareMonitor.Hardware.Cpu;
using LibreHardwareMonitor.Hardware.Gpu;
using LibreHardwareMonitor.Hardware.Memory;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Hardware.Network;
using LibreHardwareMonitor.Hardware.PowerMonitor;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using LibreHardwareMonitor.Hardware.Psu.Msi;
using LibreHardwareMonitor.Hardware.Storage;

namespace LibreHardwareMonitor.Hardware;

/// <summary>
/// Stores all hardware groups and decides which devices should be enabled and updated.
/// </summary>
public class Computer : IComputer
{
    internal const int MaxConfigurationBuildAttempts = 4;

    private readonly HardwareGroupRegistry _registry;
    private readonly List<IGroup> _groups;
    private readonly object _lock;
    private readonly OpenDependencies _openDependencies;
    private readonly ISettings _settings;

    private bool _batteryEnabled;
    private bool _closing;
    private bool _controllerEnabled;
    private bool _cpuEnabled;
    private int _enabledChangeVersion;
    private bool _gpuEnabled;
    private bool _powerMonitorEnabled;
    private bool _memoryEnabled;
    private bool _motherboardEnabled;
    private bool _networkEnabled;
    private bool _open;
    private bool _opening;
    private bool _psuEnabled;
    private bool _resetting;
    private SMBios _smbios;
    private bool _storageEnabled;

    /// <summary>
    /// Creates a new <see cref="IComputer" /> instance with basic initial <see cref="Settings" />.
    /// </summary>
    public Computer()
        : this(null, OpenDependencies.Default)
    {
    }

    /// <summary>
    /// Creates a new <see cref="IComputer" /> instance with additional <see cref="ISettings" />.
    /// </summary>
    /// <param name="settings">Computer settings that will be transferred to each <see cref="IHardware" />.</param>
    public Computer(ISettings settings)
        : this(settings, OpenDependencies.Default)
    {
    }

    internal Computer(ISettings settings, OpenDependencies openDependencies)
    {
        _settings = settings ?? new Settings();
        _openDependencies = openDependencies ?? throw new ArgumentNullException(nameof(openDependencies));
        _registry = new HardwareGroupRegistry(() => HardwareAdded, () => HardwareRemoved);
        _groups = _registry.Groups;
        _lock = _registry.SyncRoot;
    }

    /// <inheritdoc />
    public event HardwareEventHandler HardwareAdded;

    /// <inheritdoc />
    public event HardwareEventHandler HardwareRemoved;

    /// <inheritdoc />
    public IList<IHardware> Hardware
    {
        get
        {
            lock (_lock)
            {
                List<IHardware> list = new();

                foreach (IGroup group in _groups)
                    list.AddRange(group.Hardware);

                return list;
            }
        }
    }

    /// <inheritdoc />
    public bool IsBatteryEnabled
    {
        get { return _batteryEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _batteryEnabled))
                return;

            if (value)
            {
                _registry.Add(new BatteryGroup(_settings));
            }
            else
            {
                _registry.RemoveType<BatteryGroup>();
            }

            CommitEnabledChange(value, ref _batteryEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsControllerEnabled
    {
        get { return _controllerEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _controllerEnabled))
                return;

            if (value)
            {
                _registry.Add(new TBalancerGroup(_settings));
                _registry.Add(new HeatmasterGroup(_settings));
                _registry.Add(new AquaComputerGroup(_settings));
                _registry.Add(new AeroCoolGroup(_settings));
                _registry.Add(new NzxtGroup(_settings));
                _registry.Add(new RazerGroup(_settings));
                _registry.Add(new ArcticGroup(_settings));
                _registry.Add(new MsiGroup(_settings));
            }
            else
            {
                _registry.RemoveType<TBalancerGroup>();
                _registry.RemoveType<HeatmasterGroup>();
                _registry.RemoveType<AquaComputerGroup>();
                _registry.RemoveType<AeroCoolGroup>();
                _registry.RemoveType<NzxtGroup>();
                _registry.RemoveType<RazerGroup>();
                _registry.RemoveType<ArcticGroup>();
                _registry.RemoveType<MsiGroup>();
            }

            CommitEnabledChange(value, ref _controllerEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsCpuEnabled
    {
        get { return _cpuEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _cpuEnabled))
                return;

            if (value)
                _registry.Add(new CpuGroup(_settings));
            else
                _registry.RemoveType<CpuGroup>();

            CommitEnabledChange(value, ref _cpuEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsGpuEnabled
    {
        get { return _gpuEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _gpuEnabled))
                return;

            if (value)
            {
                _registry.Add(new AmdGpuGroup(_settings));
                _registry.Add(new NvidiaGroup(_settings));

                if (_cpuEnabled)
                    _registry.Add(new IntelGpuGroup(GetIntelCpus(), _settings));
            }
            else
            {
                _registry.RemoveType<AmdGpuGroup>();
                _registry.RemoveType<NvidiaGroup>();
                _registry.RemoveType<IntelGpuGroup>();
            }

            CommitEnabledChange(value, ref _gpuEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsPowerMonitorEnabled
    {
        get { return _powerMonitorEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _powerMonitorEnabled))
                return;

            if (value)
                _registry.Add(new PowerMonitorGroup(_settings));
            else
                _registry.RemoveType<PowerMonitorGroup>();

            CommitEnabledChange(value, ref _powerMonitorEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsMemoryEnabled
    {
        get { return _memoryEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _memoryEnabled))
                return;

            if (value)
                _registry.Add(new MemoryGroup(_settings));
            else
                _registry.RemoveType<MemoryGroup>();

            CommitEnabledChange(value, ref _memoryEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsMotherboardEnabled
    {
        get { return _motherboardEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _motherboardEnabled))
                return;

            if (value)
                _registry.Add(new MotherboardGroup(_smbios, _settings));
            else
                _registry.RemoveType<MotherboardGroup>();

            CommitEnabledChange(value, ref _motherboardEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsNetworkEnabled
    {
        get { return _networkEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _networkEnabled))
                return;

            if (value)
                _registry.Add(new NetworkGroup(_settings));
            else
                _registry.RemoveType<NetworkGroup>();

            CommitEnabledChange(value, ref _networkEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsPsuEnabled
    {
        get { return _psuEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _psuEnabled))
                return;

            if (value)
            {
                _registry.Add(new CorsairPsuGroup(_settings));
                _registry.Add(new MsiPsuGroup(_settings));
            }
            else
            {
                _registry.RemoveType<CorsairPsuGroup>();
                _registry.RemoveType<MsiPsuGroup>();
            }

            CommitEnabledChange(value, ref _psuEnabled);
        }
    }

    /// <inheritdoc />
    public bool IsStorageEnabled
    {
        get { return _storageEnabled; }
        set
        {
            if (!ShouldApplyEnabledChange(value, ref _storageEnabled))
                return;

            if (value)
                _registry.Add(new StorageGroup(_settings));
            else
                _registry.RemoveType<StorageGroup>();

            CommitEnabledChange(value, ref _storageEnabled);
        }
    }

    private bool ShouldApplyEnabledChange(bool value, ref bool enabled)
    {
        lock (_lock)
        {
            if (value == enabled)
                return false;

            if (!_open || _opening || _closing || _resetting)
            {
                // Lifecycle callbacks may request the next configuration, but
                // they must not mutate the group set being opened or drained.
                enabled = value;
                unchecked
                {
                    _enabledChangeVersion++;
                }

                return false;
            }

            return true;
        }
    }

    private void CommitEnabledChange(bool value, ref bool enabled)
    {
        lock (_lock)
        {
            if (value == enabled)
                return;

            enabled = value;
            unchecked
            {
                _enabledChangeVersion++;
            }
        }
    }

    /// <summary>
    /// Contains computer information table read in accordance with <see href="https://www.dmtf.org/standards/smbios">System Management BIOS (SMBIOS) Reference Specification</see>.
    /// </summary>
    public SMBios SMBios
    {
        get
        {
            if (!_open)
                throw new InvalidOperationException("SMBIOS cannot be accessed before opening.");

            return _smbios;
        }
    }

    //// <inheritdoc />
    public string GetReport()
    {
        lock (_lock)
        {
            using StringWriter w = new(CultureInfo.InvariantCulture);

            w.WriteLine();
            w.WriteLine(nameof(LibreHardwareMonitor) + " Report");
            w.WriteLine();

            Version version = typeof(Computer).Assembly.GetName().Version;

            NewSection(w);
            w.Write("Version: ");
            w.WriteLine(version.ToString());
            w.WriteLine();

            NewSection(w);
            w.Write("Common Language Runtime: ");
            w.WriteLine(Environment.Version.ToString());
            w.Write("Operating System: ");
            w.WriteLine(Environment.OSVersion.ToString());
            w.Write("Process Type: ");
            w.WriteLine(IntPtr.Size == 4 ? "32-Bit" : "64-Bit");
            w.WriteLine();

            NewSection(w);
            w.WriteLine("Sensors");
            w.WriteLine();

            var hardwareSnapshots = new Dictionary<IGroup, IReadOnlyList<IHardware>>(_groups.Count);
            foreach (IGroup group in _groups)
                hardwareSnapshots.Add(group, group.Hardware);

            foreach (IGroup group in _groups)
            {
                foreach (IHardware hardware in hardwareSnapshots[group])
                    ReportHardwareSensorTree(hardware, w, string.Empty);
            }

            w.WriteLine();

            NewSection(w);
            w.WriteLine("Parameters");
            w.WriteLine();

            foreach (IGroup group in _groups)
            {
                foreach (IHardware hardware in hardwareSnapshots[group])
                    ReportHardwareParameterTree(hardware, w, string.Empty);
            }

            w.WriteLine();

            foreach (IGroup group in _groups)
            {
                string report = group.GetReport();
                if (!string.IsNullOrEmpty(report))
                {
                    NewSection(w);
                    w.Write(report);
                }

                foreach (IHardware hardware in hardwareSnapshots[group])
                    ReportHardware(hardware, w);
            }

            return w.ToString();
        }
    }

    /// <summary>
    /// Triggers the <see cref="IVisitor.VisitComputer" /> method for the given observer.
    /// </summary>
    /// <param name="visitor">Observer who call to devices.</param>
    public void Accept(IVisitor visitor)
    {
        if (visitor == null)
            throw new ArgumentNullException(nameof(visitor));

        visitor.VisitComputer(this);
    }

    /// <summary>
    /// Triggers the <see cref="IElement.Accept" /> method with the given visitor for each device in each group.
    /// </summary>
    /// <param name="visitor">Observer who call to devices.</param>
    public void Traverse(IVisitor visitor)
    {
        lock (_lock)
        {
            // Use a for-loop instead of foreach to avoid a collection modified exception after sleep, even though everything is under a lock.
            for (int i = 0; i < _groups.Count; i++)
            {
                IGroup group = _groups[i];
                IReadOnlyList<IHardware> hardware = group.Hardware;

                for (int j = 0; j < hardware.Count; j++)
                    hardware[j].Accept(visitor);
            }
        }
    }

    /// <summary>
    /// If hasn't been opened before, opens <see cref="SMBios" />, <see cref="OpCode" /> and triggers the private <see cref="AddGroups" /> method depending on which categories are
    /// enabled.
    /// </summary>
    public void Open()
    {
        lock (_lock)
        {
            if (_open || _opening || _closing)
                return;

            _opening = true;
        }

        bool mutexesOpenAttempted = false;
        bool opCodeOpenAttempted = false;

        try
        {
            _smbios = _openDependencies.CreateSmbios();

            mutexesOpenAttempted = true;
            _openDependencies.OpenMutexes();

            opCodeOpenAttempted = true;
            _openDependencies.OpenOpCode();

            for (int attempt = 0; attempt < MaxConfigurationBuildAttempts; attempt++)
            {
                int enabledChangeVersion;
                lock (_lock)
                    enabledChangeVersion = _enabledChangeVersion;

                AddGroups();

                lock (_lock)
                {
                    if (enabledChangeVersion == _enabledChangeVersion)
                    {
                        // Publish the completed generation and leave the
                        // lifecycle guard in one transition. A later setter
                        // must take the normal live-mutation path.
                        _open = true;
                        _opening = false;
                        return;
                    }
                }

                if (attempt + 1 < MaxConfigurationBuildAttempts)
                    _registry.RemoveGroups();
            }

            throw new InvalidOperationException(
                $"Hardware configuration did not stabilize after {MaxConfigurationBuildAttempts} build attempts.");
        }
        catch
        {
            // Rollback is deliberately best-effort so a cleanup failure cannot
            // replace the exception that made Open fail.
            try
            {
                RollbackOpen(opCodeOpenAttempted, mutexesOpenAttempted);
            }
            catch
            {
                // Preserve the original exception.
            }

            throw;
        }
        finally
        {
            lock (_lock)
                _opening = false;
        }
    }

    private void RollbackOpen(bool closeOpCode, bool closeMutexes)
    {
        List<IGroup> groups;

        lock (_lock)
        {
            groups = new List<IGroup>(_groups);
            _groups.Clear();
            _open = false;
            _smbios = null;

            foreach (IGroup group in groups)
                _registry.Detach(group);
        }

        for (int i = groups.Count - 1; i >= 0; i--)
        {
            IGroup group = groups[i];

            NotifyHardwareRemovedDuringRollback(group);

            try
            {
                group.Close();
            }
            catch
            {
                // Keep rolling back the remaining owners.
            }
        }

        if (closeOpCode)
        {
            try
            {
                _openDependencies.CloseOpCode();
            }
            catch
            {
                // Keep rolling back the remaining owners.
            }
        }

        if (closeMutexes)
        {
            try
            {
                _openDependencies.CloseMutexes();
            }
            catch
            {
                // The original Open exception remains authoritative.
            }
        }
    }

    private void NotifyHardwareRemovedDuringRollback(IGroup group)
    {
        IReadOnlyList<IHardware> hardware;

        try
        {
            hardware = group.Hardware;
        }
        catch
        {
            return;
        }

        if (hardware == null)
            return;

        int count;

        try
        {
            count = hardware.Count;
        }
        catch
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            IHardware item;

            try
            {
                item = hardware[i];
            }
            catch
            {
                continue;
            }

            HardwareEventHandler handlers = HardwareRemoved;
            if (handlers == null)
                continue;

            foreach (HardwareEventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(item);
                }
                catch
                {
                    // A consumer must not prevent remaining cleanup.
                }
            }
        }
    }

    private void AddGroups()
    {
        if (_openDependencies.AddGroups != null)
        {
            _openDependencies.AddGroups(_registry.Add);
            return;
        }

        if (_motherboardEnabled)
            _registry.Add(new MotherboardGroup(_smbios, _settings));

        if (_cpuEnabled)
            _registry.Add(new CpuGroup(_settings));

        if (_memoryEnabled)
            _registry.Add(new MemoryGroup(_settings));

        if (_gpuEnabled)
        {
            _registry.Add(new AmdGpuGroup(_settings));
            _registry.Add(new NvidiaGroup(_settings));

            if (_cpuEnabled)
                _registry.Add(new IntelGpuGroup(GetIntelCpus(), _settings));
        }

        if (_powerMonitorEnabled)
            _registry.Add(new PowerMonitorGroup(_settings));

        if (_controllerEnabled)
        {
            _registry.Add(new TBalancerGroup(_settings));
            _registry.Add(new HeatmasterGroup(_settings));
            _registry.Add(new AquaComputerGroup(_settings));
            _registry.Add(new AeroCoolGroup(_settings));
            _registry.Add(new NzxtGroup(_settings));
            _registry.Add(new RazerGroup(_settings));
            _registry.Add(new ArcticGroup(_settings));
            _registry.Add(new MsiGroup(_settings));
        }

        if (_storageEnabled)
            _registry.Add(new StorageGroup(_settings));

        if (_networkEnabled)
            _registry.Add(new NetworkGroup(_settings));

        if (_psuEnabled)
        {
            _registry.Add(new CorsairPsuGroup(_settings));
            _registry.Add(new MsiPsuGroup(_settings));
        }

        if (_batteryEnabled)
            _registry.Add(new BatteryGroup(_settings));
    }

    private static void NewSection(TextWriter writer)
    {
        for (int i = 0; i < 8; i++)
            writer.Write("----------");

        writer.WriteLine();
        writer.WriteLine();
    }

    private static int CompareSensor(ISensor a, ISensor b)
    {
        int c = a.SensorType.CompareTo(b.SensorType);
        if (c == 0)
            return a.Index.CompareTo(b.Index);

        return c;
    }

    private static void ReportHardwareSensorTree(IHardware hardware, TextWriter w, string space)
    {
        w.WriteLine("{0}|", space);
        w.WriteLine("{0}+- {1} ({2})", space, hardware.Name, hardware.Identifier);

        ISensor[] sensors = hardware.Sensors;
        Array.Sort(sensors, CompareSensor);

        foreach (ISensor sensor in sensors)
            w.WriteLine("{0}|  +- {1,-14} : {2,8:G6} {3,8:G6} {4,8:G6} ({5})", space, sensor.Name, sensor.Value, sensor.Min, sensor.Max, sensor.Identifier);

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardwareSensorTree(subHardware, w, "|  ");
    }

    private static void ReportHardwareParameterTree(IHardware hardware, TextWriter w, string space)
    {
        w.WriteLine("{0}|", space);
        w.WriteLine("{0}+- {1} ({2})", space, hardware.Name, hardware.Identifier);

        ISensor[] sensors = hardware.Sensors;
        Array.Sort(sensors, CompareSensor);

        foreach (ISensor sensor in sensors)
        {
            string innerSpace = space + "|  ";
            if (sensor.Parameters.Count > 0)
            {
                w.WriteLine("{0}|", innerSpace);
                w.WriteLine("{0}+- {1} ({2})", innerSpace, sensor.Name, sensor.Identifier);

                foreach (IParameter parameter in sensor.Parameters)
                {
                    string innerInnerSpace = innerSpace + "|  ";
                    w.WriteLine("{0}+- {1} : {2}", innerInnerSpace, parameter.Name, string.Format(CultureInfo.InvariantCulture, "{0} : {1}", parameter.DefaultValue, parameter.Value));
                }
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardwareParameterTree(subHardware, w, "|  ");
    }

    private static void ReportHardware(IHardware hardware, TextWriter w)
    {
        string hardwareReport = hardware.GetReport();
        if (!string.IsNullOrEmpty(hardwareReport))
        {
            NewSection(w);
            w.Write(hardwareReport);
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            ReportHardware(subHardware, w);
    }

    /// <summary>
    /// If opened before, removes all <see cref="IGroup" /> and triggers <see cref="OpCode.Close" />.
    /// </summary>
    public void Close()
    {
        lock (_lock)
        {
            if (!_open || _closing || _resetting)
                return;

            _closing = true;
        }

        Exception firstFailure = null;
        try
        {
            HardwareGroupRegistry.CaptureFailure(_registry.RemoveGroups, ref firstFailure);
            HardwareGroupRegistry.CaptureFailure(_openDependencies.CloseOpCode, ref firstFailure);
            HardwareGroupRegistry.CaptureFailure(_openDependencies.CloseMutexes, ref firstFailure);
        }
        finally
        {
            lock (_lock)
            {
                _smbios = null;
                _open = false;
                _closing = false;
            }
        }

        HardwareGroupRegistry.ThrowIfFailed(firstFailure);
    }

    /// <summary>
    /// If opened before, removes all <see cref="IGroup" /> and recreates it.
    /// </summary>
    public void Reset()
    {
        bool resetStarted = false;
        try
        {
            lock (_lock)
            {
                if (!_open || _closing || _resetting)
                    return;

                _resetting = true;
                resetStarted = true;
            }

            _registry.RemoveGroups();
            try
            {
                for (int attempt = 0; attempt < MaxConfigurationBuildAttempts; attempt++)
                {
                    int enabledChangeVersion;
                    lock (_lock)
                        enabledChangeVersion = _enabledChangeVersion;

                    AddGroups();

                    lock (_lock)
                    {
                        if (enabledChangeVersion == _enabledChangeVersion)
                        {
                            // Let subsequent setters mutate the stable group
                            // generation normally. The local flag prevents this
                            // Reset from clearing a newer reentrant Reset.
                            _resetting = false;
                            resetStarted = false;
                            return;
                        }
                    }

                    if (attempt + 1 < MaxConfigurationBuildAttempts)
                        _registry.RemoveGroups();
                }

                throw new InvalidOperationException(
                    $"Hardware configuration did not stabilize after {MaxConfigurationBuildAttempts} build attempts.");
            }
            catch
            {
                // A reset cannot restore already-closed groups, but it must not
                // leave a partial replacement set behind.
                try
                {
                    _registry.RemoveGroups();
                }
                catch
                {
                    // Preserve the replacement failure.
                }

                throw;
            }
        }
        finally
        {
            if (resetStarted)
            {
                lock (_lock)
                    _resetting = false;
            }
        }
    }

    private List<IntelCpu> GetIntelCpus()
    {
        // Create a temporary cpu group if one has not been added.
        lock (_lock)
        {
            IGroup cpuGroup = _groups.Find(x => x is CpuGroup) ?? new CpuGroup(_settings);
            return cpuGroup.Hardware.Select(x => x as IntelCpu).ToList();
        }
    }

    /// <summary>
    /// <see cref="Computer" /> specific additional settings passed to its <see cref="IHardware" />.
    /// </summary>
    private class Settings : ISettings
    {
        public bool Contains(string name)
        {
            return false;
        }

        public void SetValue(string name, string value)
        { }

        public string GetValue(string name, string value)
        {
            return value;
        }

        public void Remove(string name)
        { }
    }

    internal sealed class OpenDependencies
    {
        private static readonly SharedOwner MutexOwner = new(
            () =>
            {
                if (Software.OperatingSystem.IsWindows8OrGreater)
                    Mutexes.Open();
            },
            Mutexes.Close);
        private static readonly SharedOwner OpCodeOwner = new(OpCode.Open, OpCode.Close);

        internal static readonly OpenDependencies Default = new(
            () => new SMBios(),
            MutexOwner.Open,
            MutexOwner.Close,
            OpCodeOwner.Open,
            OpCodeOwner.Close,
            null);

        internal OpenDependencies(
            Func<SMBios> createSmbios,
            Action openMutexes,
            Action closeMutexes,
            Action openOpCode,
            Action closeOpCode,
            Action<Action<IGroup>> addGroups)
        {
            CreateSmbios = createSmbios ?? throw new ArgumentNullException(nameof(createSmbios));
            OpenMutexes = openMutexes ?? throw new ArgumentNullException(nameof(openMutexes));
            CloseMutexes = closeMutexes ?? throw new ArgumentNullException(nameof(closeMutexes));
            OpenOpCode = openOpCode ?? throw new ArgumentNullException(nameof(openOpCode));
            CloseOpCode = closeOpCode ?? throw new ArgumentNullException(nameof(closeOpCode));
            AddGroups = addGroups;
        }

        internal Func<SMBios> CreateSmbios { get; }

        internal Action OpenMutexes { get; }

        internal Action CloseMutexes { get; }

        internal Action OpenOpCode { get; }

        internal Action CloseOpCode { get; }

        internal Action<Action<IGroup>> AddGroups { get; }
    }

    internal sealed class SharedOwner
    {
        private readonly Action _close;
        private readonly Action _open;
        private readonly object _sync = new();
        private int _leaseCount;

        internal SharedOwner(Action open, Action close)
        {
            _open = open ?? throw new ArgumentNullException(nameof(open));
            _close = close ?? throw new ArgumentNullException(nameof(close));
        }

        internal void Open()
        {
            lock (_sync)
            {
                if (_leaseCount == 0)
                {
                    try
                    {
                        _open();
                    }
                    catch
                    {
                        try
                        {
                            _close();
                        }
                        catch
                        {
                            // Preserve the acquisition failure.
                        }

                        throw;
                    }
                }

                _leaseCount++;
            }
        }

        internal void Close()
        {
            lock (_sync)
            {
                if (_leaseCount == 0)
                    return;

                _leaseCount--;
                if (_leaseCount == 0)
                    _close();
            }
        }
    }
}
