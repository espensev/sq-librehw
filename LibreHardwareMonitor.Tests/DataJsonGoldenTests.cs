// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Locks the data.json external contract: the downstream consumer keys on the exact property
/// names, ordering, and value formatting this endpoint emits, so any change to the object graph
/// or the serialization path must keep the bytes identical.
///
/// The golden file lives next to this source file (data.golden.json). It embeds the assembly
/// version string, so a version bump legitimately changes the output: delete the golden file and
/// re-run the test once to regenerate it, then review the diff before committing.
/// </summary>
public class DataJsonGoldenTests
{
    [Fact]
    public void StreamSerializationMatchesArraySerialization()
    {
        RunWithInvariantCulture(() =>
        {
            HttpServer server = CreateServerWithSyntheticTree();

            byte[] arrayPath = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(server.BuildDataJsonObject());

            using MemoryStream stream = new();
            server.WriteDataJson(stream);

            Assert.Equal(Encoding.UTF8.GetString(arrayPath), Encoding.UTF8.GetString(stream.ToArray()));
        });
    }

    [Fact]
    public void DataJsonMatchesGoldenMaster()
    {
        RunWithInvariantCulture(() =>
        {
            HttpServer server = CreateServerWithSyntheticTree();

            using MemoryStream stream = new();
            server.WriteDataJson(stream);
            byte[] actual = stream.ToArray();

            string goldenPath = Path.Combine(SourceDirectory(), "data.golden.json");
            if (!File.Exists(goldenPath))
            {
                File.WriteAllBytes(goldenPath, actual);
                Assert.True(false, $"Golden master did not exist and was created at {goldenPath}; review it, then re-run.");
            }

            byte[] expected = File.ReadAllBytes(goldenPath);
            Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        });
    }

    // SensorNode formats display strings with the current culture; pin it so the golden bytes do
    // not depend on the machine's locale.
    private static void RunWithInvariantCulture(Action test)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static string SourceDirectory([CallerFilePath] string path = "")
    {
        return Path.GetDirectoryName(path);
    }

    /// <summary>
    /// A deterministic tree exercising the contract's edge cases: formatted vs raw values,
    /// non-finite readings (NaN/Infinity map to null), missing readings, JSON string escaping
    /// (quotes, non-ASCII), and the node-type-specific properties (SensorId/HardwareId/ImageURL).
    /// </summary>
    private static HttpServer CreateServerWithSyntheticTree()
    {
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);

        FakeHardware hardware = new(new Identifier("golden", "0"), "Golden CPU", HardwareType.Cpu);
        hardware.AddSensor(new FakeSensor(hardware, SensorType.Temperature, 0, "CPU Core #1", 49.5f, 30.25f, 80f));
        hardware.AddSensor(new FakeSensor(hardware, SensorType.Load, 0, "CPU Total", float.NaN, 0f, float.PositiveInfinity));
        hardware.AddSensor(new FakeSensor(hardware, SensorType.Throughput, 0, "Upload Speed", 2621440f, null, 3145728f));
        hardware.AddSensor(new FakeSensor(hardware, SensorType.Fan, 0, "Fan \"Æøå\" #1", 1234f, 980f, 2200f));
        hardware.AddSensor(new FakeSensor(hardware, SensorType.Voltage, 3, "VDDCR_SOC", null, null, null));

        Node root = new("GOLDEN-PC");
        root.Nodes.Add(new HardwareNode(hardware, settings, unitManager));

        return new HttpServer(root, hardware, "?", 8085);
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

        public IControl Control => null;
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

#pragma warning restore CS0067
}
