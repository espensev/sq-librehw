// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using static LibreHardwareMonitor.Tests.CorsairPsuTestFixture;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition("Corsair export mutex", DisableParallelization = true)]
public sealed class CorsairExportMutexCollection;

[Collection("Corsair export mutex")]
public sealed class CorsairPsuExportTests
{
    [Fact]
    public void MissingMutex_ExportsUnavailableReadingsAndRecoversInSameColumns()
    {
        string mutexName = $@"Local\LibreHardwareMonitor.CorsairExportTests.{Guid.NewGuid():N}";
        string directory = Path.Combine(Path.GetTempPath(), "LhmCorsairExport-" + Guid.NewGuid().ToString("N"));
        var settings = new PersistentSettings();
        var device = new TestDevice("export-psu") { NumericValue = 12 };
        var dependencies = new Computer.OpenDependencies(
            () => null, () => { }, () => { }, () => { }, () => { },
            add => add(new CorsairPsuGroup(settings, [device])));
        var computer = new Computer(settings, dependencies);
        HardwareNode hardwareNode = null;
        HttpServer server = null;
        Mutexes.Open(mutexName);
        try
        {
            computer.Open();
            var hardware = Assert.IsType<CorsairPsu>(Assert.Single(computer.Hardware));
            hardware.Update();
            ISensor[] sensors = hardware.Sensors;
            Assert.All(sensors, sensor => Assert.True(sensor.Value > 0));
            var extrema = sensors.ToDictionary(sensor => sensor.Identifier.ToString(), sensor => (sensor.Min, sensor.Max));

            hardwareNode = new HardwareNode(hardware, settings, new UnitManager(settings));
            var root = new Node("CORSAIR-TEST");
            root.Nodes.Add(hardwareNode);
            server = new HttpServer(root, computer, "127.0.0.1", 18085);
            var logger = new Logger(computer, directory) { LoggingInterval = TimeSpan.Zero };
            AssertJsonReadings(server, sensors, available: true);
            logger.Log();

            int streamsBeforeFailure = device.Streams.Count;
            Mutexes.Close();
            hardware.Update();

            Assert.Equal(streamsBeforeFailure, device.Streams.Count);
            AssertJsonReadings(server, sensors, available: false);
            Assert.All(sensors, sensor => Assert.Equal(extrema[sensor.Identifier.ToString()], (sensor.Min, sensor.Max)));
            logger.Log();

            Mutexes.Open(mutexName);
            hardware.Update();
            AssertJsonReadings(server, sensors, available: true);
            logger.Log();

            string[] rows = File.ReadAllLines(Assert.Single(Directory.GetFiles(directory, "*.csv")));
            Assert.Equal(5, rows.Length);
            string[] columns = rows[0].Split(',');
            Assert.Equal(sensors.Length + 1, columns.Length);
            string[][] values = rows.Skip(2).Select(row => row.Split(',')).ToArray();
            Assert.All(values, row => Assert.Equal(columns.Length, row.Length));
            foreach (ISensor sensor in sensors)
            {
                int column = Array.IndexOf(columns, sensor.Identifier.ToString());
                Assert.InRange(column, 1, columns.Length - 1);
                Assert.NotEmpty(values[0][column]);
                Assert.Equal(string.Empty, values[1][column]);
                Assert.NotEmpty(values[2][column]);
            }
        }
        finally
        {
            server?.Quit();
            hardwareNode?.Dispose();
            computer.Close();
            Mutexes.Close();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertJsonReadings(HttpServer server, ISensor[] sensors, bool available)
    {
        using var stream = new MemoryStream();
        server.WriteDataJson(stream);
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        Dictionary<string, JsonElement> readings = SensorNodes(document.RootElement)
            .ToDictionary(node => node.GetProperty("SensorId").GetString());
        Assert.Equal(sensors.Length, readings.Count);
        foreach (ISensor sensor in sensors)
        {
            JsonElement reading = readings[sensor.Identifier.ToString()];
            JsonElement raw = reading.GetProperty("RawValue");
            if (available)
            {
                Assert.Equal(JsonValueKind.Number, raw.ValueKind);
                Assert.Equal(sensor.Value.Value, raw.GetSingle());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, raw.ValueKind);
                Assert.Equal("-", reading.GetProperty("Value").GetString());
            }
        }
    }

    private static IEnumerable<JsonElement> SensorNodes(JsonElement node)
    {
        if (node.TryGetProperty("SensorId", out _))
            yield return node;
        foreach (JsonElement child in node.GetProperty("Children").EnumerateArray())
        foreach (JsonElement sensor in SensorNodes(child))
            yield return sensor;
    }
}
