// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Adapters;
using LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class SensorSnapshotTests
{
    [Fact]
    public void Capture_PreservesProducerOrderKindsIdentityValuesAndLegacyImages()
    {
        RunWithInvariantCulture(() =>
        {
            SnapshotFixture fixture = CreateFixture(
                new Identifier("GPU", "CaseSensitive"),
                "Graphics Device",
                HardwareType.GpuAmd,
                SensorType.Temperature,
                4,
                "Hot Spot",
                float.NaN,
                -1.25f,
                float.PositiveInfinity,
                false,
                "TeMp-CaSe");

            fixture.Root.Nodes.Insert(0, new Node("Zulu group"));
            fixture.Root.Nodes.Add(new Node("Alpha group"));
            fixture.Hardware.ClearReads();
            fixture.Sensor.ClearReads();
            fixture.Hardware.RequireSyncRoot = true;
            fixture.Sensor.RequireSyncRoot = true;

            SensorSnapshot snapshot = WinFormsNodeSensorSnapshotSource.Capture(fixture.Root);

            fixture.Hardware.RequireSyncRoot = false;
            fixture.Sensor.RequireSyncRoot = false;

            Assert.Equal(new[] { "Name", "Identifier", "HardwareType" }, fixture.Hardware.Reads);
            Assert.Equal(
                new[]
                {
                    "Name", "Identifier", "SensorType",
                    "Min", "SensorType", "Value", "SensorType", "Max", "SensorType",
                    "Min", "Value", "Max"
                },
                fixture.Sensor.Reads);

            SensorNodeSnapshot root = snapshot.Root;
            Assert.Equal(SensorSnapshotNodeKind.Group, root.Kind);
            Assert.Equal("Snapshot host", root.Text);
            Assert.Equal("images_icon/computer.png", root.ImageUrl);
            Assert.Equal(new[] { "Zulu group", "Graphics Device", "Alpha group" }, root.Children.Select(child => child.Text));

            SensorNodeSnapshot hardware = root.Children[1];
            Assert.Equal(SensorSnapshotNodeKind.Hardware, hardware.Kind);
            Assert.Equal("/GPU/CaseSensitive", hardware.HardwareId);
            Assert.Null(hardware.SensorId);
            Assert.Equal("images_icon/ati.png", hardware.ImageUrl);

            SensorNodeSnapshot type = Assert.Single(hardware.Children);
            Assert.Equal(SensorSnapshotNodeKind.Group, type.Kind);
            Assert.Equal("Temperatures", type.Text);
            Assert.Equal("images_icon/temperature.png", type.ImageUrl);

            SensorNodeSnapshot sensor = Assert.Single(type.Children);
            Assert.Equal(SensorSnapshotNodeKind.Sensor, sensor.Kind);
            Assert.Equal("Hot Spot", sensor.Text);
            Assert.Equal("/GPU/CaseSensitive/TeMp-CaSe/4", sensor.SensorId);
            Assert.Equal("Temperature", sensor.SensorType);
            Assert.Null(sensor.HardwareId);
            Assert.Equal("images/transparent.png", sensor.ImageUrl);
            Assert.Equal(-1.25f, sensor.Minimum.Raw);
            Assert.Equal("-1.2 °C", sensor.Minimum.Display);
            Assert.True(float.IsNaN(sensor.Current.Raw.Value));
            Assert.Contains("NaN", sensor.Current.Display);
            Assert.True(float.IsPositiveInfinity(sensor.Maximum.Raw.Value));
            Assert.Contains("Infinity", sensor.Maximum.Display);
        });
    }

    [Fact]
    public void Capture_RemainsDetachedAfterTreeNamesAndSensorValuesMutate()
    {
        SnapshotFixture fixture = CreateFixture(
            new Identifier("cpu", "0"),
            "Original hardware",
            HardwareType.Cpu,
            SensorType.Load,
            0,
            "Original sensor",
            42f,
            10f,
            90f);

        SensorSnapshot snapshot = WinFormsNodeSensorSnapshotSource.Capture(fixture.Root);

        fixture.Root.Text = "Mutated host";
        fixture.Hardware.Name = "Mutated hardware";
        fixture.TypeNode.Text = "Mutated type";
        fixture.Sensor.Name = "Mutated sensor";
        fixture.Sensor.Min = -100f;
        fixture.Sensor.Value = -50f;
        fixture.Sensor.Max = 500f;
        fixture.SensorNode.IsVisible = false;
        fixture.TypeNode.Nodes.Remove(fixture.SensorNode);
        fixture.Root.Nodes.Clear();
        fixture.Root.Nodes.Add(new Node("Replacement"));

        SensorNodeSnapshot capturedHardware = Assert.Single(snapshot.Root.Children);
        SensorNodeSnapshot capturedType = Assert.Single(capturedHardware.Children);
        SensorNodeSnapshot capturedSensor = Assert.Single(capturedType.Children);

        Assert.Equal("Snapshot host", snapshot.Root.Text);
        Assert.Equal("Original hardware", capturedHardware.Text);
        Assert.Equal("Load", capturedType.Text);
        Assert.Equal("Original sensor", capturedSensor.Text);
        Assert.Equal(10f, capturedSensor.Minimum.Raw);
        Assert.Equal(42f, capturedSensor.Current.Raw);
        Assert.Equal(90f, capturedSensor.Maximum.Raw);
        Assert.Equal("10.0 %", capturedSensor.Minimum.Display);
        Assert.Equal("42.0 %", capturedSensor.Current.Display);
        Assert.Equal("90.0 %", capturedSensor.Maximum.Display);
    }

    [Fact]
    public void Capture_ExposesReadOnlyChildrenWithoutMutableSourceReferences()
    {
        SensorNodeSnapshot originalChild = CreateGroupSnapshot("Original child", Array.Empty<SensorNodeSnapshot>());
        List<SensorNodeSnapshot> suppliedChildren = new() { originalChild };
        SensorNodeSnapshot parent = CreateGroupSnapshot("Parent", suppliedChildren);
        SensorSnapshot snapshot = new(parent);
        SensorNodeSnapshot replacement = CreateGroupSnapshot("Replacement", Array.Empty<SensorNodeSnapshot>());

        suppliedChildren.Clear();
        suppliedChildren.Add(replacement);

        Assert.Same(originalChild, Assert.Single(parent.Children));

        IList<SensorNodeSnapshot> genericList = Assert.IsAssignableFrom<IList<SensorNodeSnapshot>>(parent.Children);
        Assert.Throws<NotSupportedException>(() => genericList.Add(replacement));
        Assert.Throws<NotSupportedException>(() => genericList[0] = replacement);

        IList nonGenericList = Assert.IsAssignableFrom<IList>(parent.Children);
        Assert.Throws<NotSupportedException>(() => nonGenericList.Add(replacement));
        Assert.Throws<NotSupportedException>(() => nonGenericList[0] = replacement);

        Assert.Throws<ArgumentNullException>(() => WinFormsNodeSensorSnapshotSource.Capture(null));
        AssertSnapshotPropertyGraphContainsNoSourceReferences(snapshot, suppliedChildren);
    }

    [Fact]
    public void Capture_IncludesHiddenNodes()
    {
        SnapshotFixture fixture = CreateFixture(
            new Identifier("hidden", "0"),
            "Hidden hardware",
            HardwareType.SuperIO,
            SensorType.Voltage,
            0,
            "Hidden voltage",
            1.1f,
            1f,
            1.2f,
            true);
        Node hiddenGroup = new("Hidden generic group") { IsVisible = false };
        fixture.Root.Nodes.Insert(0, hiddenGroup);

        Assert.False(hiddenGroup.IsVisible);
        Assert.False(fixture.SensorNode.IsVisible);
        Assert.False(fixture.TypeNode.IsVisible);

        SensorSnapshot snapshot = WinFormsNodeSensorSnapshotSource.Capture(fixture.Root);

        Assert.Equal(2, snapshot.Root.Children.Count);
        Assert.Equal("Hidden generic group", snapshot.Root.Children[0].Text);
        SensorNodeSnapshot hardware = snapshot.Root.Children[1];
        SensorNodeSnapshot type = Assert.Single(hardware.Children);
        Assert.Equal("Hidden voltage", Assert.Single(type.Children).Text);
    }

    [Fact]
    public void Capture_MapsEveryLegacyHardwareAndSensorTypeImageIncludingFallbacks()
    {
        (HardwareType Type, string ImageUrl)[] hardwareCases =
        {
            (HardwareType.Motherboard, "images_icon/mainboard.png"),
            (HardwareType.SuperIO, "images_icon/chip.png"),
            (HardwareType.Cpu, "images_icon/cpu.png"),
            (HardwareType.Memory, "images_icon/ram.png"),
            (HardwareType.GpuNvidia, "images_icon/nvidia.png"),
            (HardwareType.GpuAmd, "images_icon/ati.png"),
            (HardwareType.GpuIntel, "images_icon/intel.png"),
            (HardwareType.Storage, "images_icon/hdd.png"),
            (HardwareType.Network, "images_icon/nic.png"),
            (HardwareType.Cooler, "images_icon/fan.png"),
            (HardwareType.EmbeddedController, "images_icon/cpu.png"),
            (HardwareType.Psu, "images_icon/power-supply.png"),
            (HardwareType.Battery, "images_icon/battery.png"),
            (HardwareType.PowerMonitor, "images_icon/powermonitor.png")
        };
        (SensorType Type, string ImageUrl)[] sensorTypeCases =
        {
            (SensorType.Voltage, "images_icon/voltage.png"),
            (SensorType.Current, "images_icon/voltage.png"),
            (SensorType.Power, "images_icon/power.png"),
            (SensorType.Clock, "images_icon/clock.png"),
            (SensorType.Temperature, "images_icon/temperature.png"),
            (SensorType.Load, "images_icon/load.png"),
            (SensorType.Frequency, "images_icon/power.png"),
            (SensorType.Fan, "images_icon/fan.png"),
            (SensorType.Flow, "images_icon/flow.png"),
            (SensorType.Control, "images_icon/control.png"),
            (SensorType.Level, "images_icon/level.png"),
            (SensorType.Factor, "images_icon/power.png"),
            (SensorType.Data, "images_icon/power.png"),
            (SensorType.SmallData, "images_icon/power.png"),
            (SensorType.Throughput, "images_icon/throughput.png"),
            (SensorType.TimeSpan, "images_icon/power.png"),
            (SensorType.Timing, "images_icon/clock.png"),
            (SensorType.Energy, "images_icon/power.png"),
            (SensorType.Noise, "images_icon/loudspeaker.png"),
            (SensorType.Conductivity, "images_icon/voltage.png"),
            (SensorType.Humidity, "images_icon/flow.png"),
            (SensorType.TemperatureRate, "images_icon/temperature.png")
        };

        Assert.Equal(Enum.GetValues(typeof(HardwareType)).Length, hardwareCases.Length);
        Assert.Equal(Enum.GetValues(typeof(SensorType)).Length, sensorTypeCases.Length);

        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);
        Node hardwareRoot = new("Hardware map");
        for (int i = 0; i < hardwareCases.Length; i++)
        {
            FakeHardware hardware = new(
                new Identifier("hardware", i.ToString(CultureInfo.InvariantCulture)),
                hardwareCases[i].Type.ToString(),
                hardwareCases[i].Type);
            hardwareRoot.Nodes.Add(new HardwareNode(hardware, settings, unitManager));
        }

        SensorSnapshot hardwareSnapshot = WinFormsNodeSensorSnapshotSource.Capture(hardwareRoot);
        Assert.Equal("images_icon/computer.png", hardwareSnapshot.Root.ImageUrl);
        for (int i = 0; i < hardwareCases.Length; i++)
        {
            Assert.Equal(SensorSnapshotNodeKind.Hardware, hardwareSnapshot.Root.Children[i].Kind);
            Assert.Equal(hardwareCases[i].ImageUrl, hardwareSnapshot.Root.Children[i].ImageUrl);
        }

        Node typeRoot = new("Type map");
        Identifier typeParent = new("types");
        foreach ((SensorType type, string _) in sensorTypeCases)
            typeRoot.Nodes.Add(new TypeNode(type, typeParent, settings));

        SensorSnapshot typeSnapshot = WinFormsNodeSensorSnapshotSource.Capture(typeRoot);
        for (int i = 0; i < sensorTypeCases.Length; i++)
        {
            Assert.Equal(SensorSnapshotNodeKind.Group, typeSnapshot.Root.Children[i].Kind);
            Assert.Equal(sensorTypeCases[i].ImageUrl, typeSnapshot.Root.Children[i].ImageUrl);
        }

        FakeHardware sensorHardware = new(new Identifier("sensor-image"), "Sensor image", HardwareType.Cpu);
        FakeSensor sensor = new(sensorHardware, SensorType.Power, 0, "Power", 5f, 1f, 10f);
        SensorSnapshot sensorSnapshot = WinFormsNodeSensorSnapshotSource.Capture(new SensorNode(sensor, settings, unitManager));
        Assert.Equal(SensorSnapshotNodeKind.Sensor, sensorSnapshot.Root.Kind);
        Assert.Equal("images/transparent.png", sensorSnapshot.Root.ImageUrl);
    }

    private static SnapshotFixture CreateFixture(
        Identifier hardwareId,
        string hardwareName,
        HardwareType hardwareType,
        SensorType sensorType,
        int sensorIndex,
        string sensorName,
        float? value,
        float? min,
        float? max,
        bool defaultHidden = false,
        string sensorIdentifierSegment = null)
    {
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);
        FakeHardware hardware = new(hardwareId, hardwareName, hardwareType);
        FakeSensor sensor = new(
            hardware,
            sensorType,
            sensorIndex,
            sensorName,
            value,
            min,
            max,
            defaultHidden,
            sensorIdentifierSegment);
        hardware.AddSensor(sensor);

        HardwareNode hardwareNode = new(hardware, settings, unitManager);
        TypeNode typeNode = Assert.IsType<TypeNode>(Assert.Single(hardwareNode.Nodes));
        SensorNode sensorNode = Assert.IsType<SensorNode>(Assert.Single(typeNode.Nodes));
        Node root = new("Snapshot host");
        root.Nodes.Add(hardwareNode);

        return new SnapshotFixture(root, hardwareNode, typeNode, sensorNode, hardware, sensor);
    }

    private static SensorNodeSnapshot CreateGroupSnapshot(string text, IEnumerable<SensorNodeSnapshot> children)
    {
        return new SensorNodeSnapshot(
            SensorSnapshotNodeKind.Group,
            text,
            null,
            null,
            null,
            "images_icon/computer.png",
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            children);
    }

    private static void AssertSnapshotPropertyGraphContainsNoSourceReferences(SensorSnapshot snapshot, object sourceCollection)
    {
        Stack<object> pending = new();
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        pending.Push(snapshot);

        while (pending.Count > 0)
        {
            object value = pending.Pop();
            if (value == null || value is string || value.GetType().IsValueType || !visited.Add(value))
                continue;

            Assert.NotSame(sourceCollection, value);
            Assert.False(value is Node);
            Assert.False(value is IHardware);
            Assert.False(value is ISensor);
            Assert.False(value is Delegate);
            Assert.False(value is System.Drawing.Image);
            Assert.False(value is System.Windows.Forms.Control);

            if (value is IEnumerable sequence)
            {
                foreach (object item in sequence)
                    pending.Push(item);
                continue;
            }

            Type type = value.GetType();
            Assert.Equal("LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots", type.Namespace);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length == 0)
                    pending.Push(property.GetValue(value));
            }
        }
    }

    private static void RunWithInvariantCulture(Action action)
    {
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        CultureInfo currentUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = currentUiCulture;
        }
    }

    private sealed class SnapshotFixture
    {
        internal SnapshotFixture(
            Node root,
            HardwareNode hardwareNode,
            TypeNode typeNode,
            SensorNode sensorNode,
            FakeHardware hardware,
            FakeSensor sensor)
        {
            Root = root;
            HardwareNode = hardwareNode;
            TypeNode = typeNode;
            SensorNode = sensorNode;
            Hardware = hardware;
            Sensor = sensor;
        }

        internal Node Root { get; }
        internal HardwareNode HardwareNode { get; }
        internal TypeNode TypeNode { get; }
        internal SensorNode SensorNode { get; }
        internal FakeHardware Hardware { get; }
        internal FakeSensor Sensor { get; }
    }

#pragma warning disable CS0067 // Events required by the interfaces are never raised by these fakes.

    private sealed class FakeHardware : IHardware
    {
        private readonly List<string> _reads = new();
        private readonly List<ISensor> _sensors = new();
        private readonly HardwareType _hardwareType;
        private readonly Identifier _identifier;
        private string _name;

        internal FakeHardware(Identifier identifier, string name, HardwareType hardwareType)
        {
            _identifier = identifier;
            _name = name;
            _hardwareType = hardwareType;
        }

        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;

        internal bool RequireSyncRoot { get; set; }
        internal IReadOnlyList<string> Reads => _reads;

        public HardwareType HardwareType => Read("HardwareType", _hardwareType);
        public Identifier Identifier => Read("Identifier", _identifier);
        public string Name
        {
            get => Read("Name", _name);
            set => _name = value;
        }

        public IHardware Parent => null;
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public ISensor[] Sensors => _sensors.ToArray();
        public IHardware[] SubHardware => Array.Empty<IHardware>();

        internal void AddSensor(ISensor sensor) => _sensors.Add(sensor);
        internal void ClearReads() => _reads.Clear();

        public string GetReport() => string.Empty;
        public void Update() { }
        public void Accept(IVisitor visitor) => visitor.VisitHardware(this);

        public void Traverse(IVisitor visitor)
        {
            foreach (ISensor sensor in _sensors)
                sensor.Accept(visitor);
        }

        private T Read<T>(string member, T value)
        {
            if (RequireSyncRoot && !Monitor.IsEntered(Node.SyncRoot))
                throw new InvalidOperationException($"{member} was read outside Node.SyncRoot.");

            _reads.Add(member);
            return value;
        }
    }

    private sealed class FakeSensor : ISensor
    {
        private readonly List<string> _reads = new();
        private readonly IHardware _hardware;
        private readonly Identifier _identifier;
        private readonly int _index;
        private readonly SensorType _sensorType;
        private string _name;
        private float? _max;
        private float? _min;
        private float? _value;

        internal FakeSensor(
            IHardware hardware,
            SensorType sensorType,
            int index,
            string name,
            float? value,
            float? min,
            float? max,
            bool isDefaultHidden = false,
            string identifierSegment = null)
        {
            _hardware = hardware;
            _sensorType = sensorType;
            _index = index;
            _name = name;
            _value = value;
            _min = min;
            _max = max;
            IsDefaultHidden = isDefaultHidden;
            _identifier = new Identifier(
                hardware.Identifier,
                identifierSegment ?? sensorType.ToString().ToLowerInvariant(),
                index.ToString(CultureInfo.InvariantCulture));
        }

        internal bool RequireSyncRoot { get; set; }
        internal IReadOnlyList<string> Reads => _reads;

        public IControl Control => null;
        public IHardware Hardware => _hardware;
        public Identifier Identifier => Read("Identifier", _identifier);
        public int Index => Read("Index", _index);
        public bool IsDefaultHidden { get; }
        public float? Max
        {
            get => Read("Max", _max);
            internal set => _max = value;
        }

        public float? Min
        {
            get => Read("Min", _min);
            internal set => _min = value;
        }

        public string Name
        {
            get => Read("Name", _name);
            set => _name = value;
        }

        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType => Read("SensorType", _sensorType);
        public float? Value
        {
            get => Read("Value", _value);
            internal set => _value = value;
        }

        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }

        internal void ClearReads() => _reads.Clear();

        public void ResetMin() { }
        public void ResetMax() { }
        public void ClearValues() { }
        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);
        public void Traverse(IVisitor visitor) { }

        private T Read<T>(string member, T value)
        {
            if (RequireSyncRoot && !Monitor.IsEntered(Node.SyncRoot))
                throw new InvalidOperationException($"{member} was read outside Node.SyncRoot.");

            _reads.Add(member);
            return value;
        }
    }

#pragma warning restore CS0067
}
