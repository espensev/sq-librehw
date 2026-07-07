// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class HttpServerSensorApiTests
{
    [Fact]
    public void GetSensorMissingIdReturnsJsonFailure()
    {
        SensorFixture fixture = CreateServerWithControlledSensor();

        Dictionary<string, object> result = fixture.Server.HandleGetSensorRequest(Query(("action", "Get"), ("id", "/missing")));

        Assert.Equal("fail", Assert.IsType<string>(result["result"]));
        Assert.Equal("Unknown id /missing specified", Assert.IsType<string>(result["message"]));
    }

    [Fact]
    public void GetSensorMissingActionReturnsJsonFailure()
    {
        SensorFixture fixture = CreateServerWithControlledSensor();

        Dictionary<string, object> result = fixture.Server.HandleGetSensorRequest(Query(("id", fixture.SensorId)));

        Assert.Equal("fail", Assert.IsType<string>(result["result"]));
        Assert.Equal("No action provided", Assert.IsType<string>(result["message"]));
    }

    [Fact]
    public void GetSensorInvalidActionReturnsJsonFailure()
    {
        SensorFixture fixture = CreateServerWithControlledSensor();

        Dictionary<string, object> result = fixture.Server.HandleGetSensorRequest(Query(("action", "Bogus"), ("id", fixture.SensorId)));

        Assert.Equal("fail", Assert.IsType<string>(result["result"]));
        Assert.Equal("Unknown action type Bogus", Assert.IsType<string>(result["message"]));
    }

    [Fact]
    public void GetSensorSetActionReturnsJsonFailureWithoutWriting()
    {
        SensorFixture fixture = CreateServerWithControlledSensor();

        Dictionary<string, object> result = fixture.Server.HandleGetSensorRequest(Query(("action", "Set"), ("id", fixture.SensorId), ("value", "55")));

        Assert.Equal("fail", Assert.IsType<string>(result["result"]));
        Assert.Equal("Set requires a POST request", Assert.IsType<string>(result["message"]));
        Assert.Equal(0, fixture.Control.SetSoftwareCallCount);
    }

    [Theory]
    [InlineData("-10", 10f)]
    [InlineData("55.5", 55.5f)]
    [InlineData("125", 90f)]
    public void SetSensorControlValueClampsToControlRange(string rawValue, float expectedValue)
    {
        SensorFixture fixture = CreateServerWithControlledSensor();
        SensorNode sensorNode = fixture.FindSensorNode();

        fixture.Server.SetSensorControlValue(sensorNode, rawValue);

        Assert.Equal(ControlMode.Software, fixture.Control.ControlMode);
        Assert.Equal(expectedValue, fixture.Control.SoftwareValue);
        Assert.Equal(1, fixture.Control.SetSoftwareCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void SetSensorControlValueRejectsInvalidValues(string rawValue)
    {
        SensorFixture fixture = CreateServerWithControlledSensor();
        SensorNode sensorNode = fixture.FindSensorNode();

        ArgumentException ex = Assert.Throws<ArgumentException>(() => fixture.Server.SetSensorControlValue(sensorNode, rawValue));

        Assert.Contains("Invalid control value", ex.Message);
        Assert.Equal(ControlMode.Undefined, fixture.Control.ControlMode);
        Assert.Equal(0, fixture.Control.SetSoftwareCallCount);
    }

    [Fact]
    public void SetSensorControlValueNullRestoresDefault()
    {
        SensorFixture fixture = CreateServerWithControlledSensor();
        SensorNode sensorNode = fixture.FindSensorNode();

        fixture.Server.SetSensorControlValue(sensorNode, "null");

        Assert.Equal(ControlMode.Default, fixture.Control.ControlMode);
        Assert.Equal(1, fixture.Control.SetDefaultCallCount);
        Assert.Equal(0, fixture.Control.SetSoftwareCallCount);
    }

    private static NameValueCollection Query(params (string Key, string Value)[] values)
    {
        NameValueCollection query = new();
        foreach ((string key, string value) in values)
            query.Add(key, value);
        return query;
    }

    private static SensorFixture CreateServerWithControlledSensor()
    {
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);

        FakeHardware hardware = new(new Identifier("api", "0"), "API Test Hardware", HardwareType.Cooler);
        FakeSensor sensor = new(hardware, SensorType.Control, 0, "Pump", 42f, 10f, 90f);
        FakeControl control = new(sensor, 10f, 90f);
        sensor.Control = control;
        hardware.AddSensor(sensor);

        Node root = new("API-PC");
        root.Nodes.Add(new HardwareNode(hardware, settings, unitManager));

        return new SensorFixture
        {
            Control = control,
            Root = root,
            SensorId = sensor.Identifier.ToString(),
            Server = new HttpServer(root, hardware, "?", 8085)
        };
    }

    private sealed class SensorFixture
    {
        public FakeControl Control { get; init; }
        public Node Root { get; init; }
        public HttpServer Server { get; init; }
        public string SensorId { get; init; }

        public SensorNode FindSensorNode()
        {
            SensorNode sensorNode = Server.FindSensor(Root, SensorId);
            Assert.NotNull(sensorNode);
            return sensorNode;
        }
    }

#pragma warning disable CS0067 // events required by the interfaces, never raised by the fakes

    private sealed class FakeHardware : IHardware
    {
        private readonly List<ISensor> _sensors = new();

        public FakeHardware(Identifier identifier, string name, HardwareType hardwareType)
        {
            Identifier = identifier;
            Name = name;
            HardwareType = hardwareType;
        }

        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;

        public HardwareType HardwareType { get; }
        public Identifier Identifier { get; }
        public string Name { get; set; }
        public IHardware Parent => null;
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public ISensor[] Sensors => _sensors.ToArray();
        public IHardware[] SubHardware => Array.Empty<IHardware>();

        public void AddSensor(ISensor sensor) => _sensors.Add(sensor);

        public string GetReport() => string.Empty;

        public void Update() { }

        public void Accept(IVisitor visitor) => visitor.VisitHardware(this);

        public void Traverse(IVisitor visitor)
        {
            foreach (ISensor sensor in _sensors)
                sensor.Accept(visitor);
        }
    }

    private sealed class FakeSensor : ISensor
    {
        public FakeSensor(IHardware hardware, SensorType sensorType, int index, string name, float? value, float? min, float? max)
        {
            Hardware = hardware;
            SensorType = sensorType;
            Index = index;
            Name = name;
            Value = value;
            Min = min;
            Max = max;
            Identifier = new Identifier(hardware.Identifier, sensorType.ToString().ToLowerInvariant(), index.ToString(CultureInfo.InvariantCulture));
        }

        public IControl Control { get; set; }
        public IHardware Hardware { get; }
        public Identifier Identifier { get; }
        public int Index { get; }
        public bool IsDefaultHidden => false;
        public float? Max { get; }
        public float? Min { get; }
        public string Name { get; set; }
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType { get; }
        public float? Value { get; }
        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }

        public void ResetMax() { }

        public void ClearValues() { }

        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);

        public void Traverse(IVisitor visitor) { }
    }

    private sealed class FakeControl : IControl
    {
        public FakeControl(ISensor sensor, float minSoftwareValue, float maxSoftwareValue)
        {
            Sensor = sensor;
            MinSoftwareValue = minSoftwareValue;
            MaxSoftwareValue = maxSoftwareValue;
            Identifier = new Identifier(sensor.Identifier, "control");
        }

        public ControlMode ControlMode { get; private set; } = ControlMode.Undefined;
        public Identifier Identifier { get; }
        public float MaxSoftwareValue { get; }
        public float MinSoftwareValue { get; }
        public ISensor Sensor { get; }
        public float SoftwareValue { get; private set; }
        public int SetDefaultCallCount { get; private set; }
        public int SetSoftwareCallCount { get; private set; }

        public void SetDefault()
        {
            ControlMode = ControlMode.Default;
            SetDefaultCallCount++;
        }

        public void SetSoftware(float value)
        {
            ControlMode = ControlMode.Software;
            SoftwareValue = value;
            SetSoftwareCallCount++;
        }
    }

#pragma warning restore CS0067
}
