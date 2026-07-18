// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class HttpServerPrometheusCollection
{
    public const string CollectionName = "HttpServer Prometheus tests";
}

[Collection(HttpServerPrometheusCollection.CollectionName)]
public sealed class HttpServerPrometheusTests
{
    private static readonly DateTime _historyStart = new(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultMetricsUsesOneValueFromHistoryReaderAndPreservesResponseContract()
    {
        FakeHardware hardware = new(new Identifier("metrics", "0"), "Metric CPU", HardwareType.Cpu);
        HistoryReaderSensor clock = new(
            hardware,
            SensorType.Clock,
            0,
            "Core #1",
            new SensorValue(2, _historyStart),
            new SensorValue(3, _historyStart.AddSeconds(1)));
        HistoryReaderSensor temperature = new(
            hardware,
            SensorType.Temperature,
            1,
            "Package",
            new SensorValue(float.NaN, _historyStart.AddSeconds(2)));
        hardware.AddSensor(clock);
        hardware.AddSensor(temperature);

        HttpServer server = CreateServer(hardware);

        (string content, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) = server.BuildPrometheusResponse(null);

        Assert.Equal(new long[] { 0 }, clock.RequestedSinceVersions);
        Assert.Equal(new[] { 1 }, clock.RequestedMaxValues);
        Assert.Equal(new long[] { 0 }, temperature.RequestedSinceVersions);
        Assert.Equal(new[] { 1 }, temperature.RequestedMaxValues);
        AssertPrometheusHeaders(contentType, headers, archiveLength: 0, timestamps: 0, lastValue: 1);

        const string clockTag = "lhm_cpu_clock_hertz {\"sensorName\"=\"Core 1\", \"sensorAlias\"=\"Core 1 (/clock/0)\", \"hardwareName\"=\"Metric CPU\", \"hardwareAlias\"=\"Metric CPU (/metrics/0)\", \"sensorId\"=\"/clock/0\", \"hardwareId\"=\"/metrics/0\", \"host\"=\"METRICS-HOST\"}";
        const string temperatureTag = "lhm_cpu_temperature_celsius {\"sensorName\"=\"Package\", \"sensorAlias\"=\"Package (/temperature/1)\", \"hardwareName\"=\"Metric CPU\", \"hardwareAlias\"=\"Metric CPU (/metrics/0)\", \"sensorId\"=\"/temperature/1\", \"hardwareId\"=\"/metrics/0\", \"host\"=\"METRICS-HOST\"}";
        string expected =
            "# TYPE lhm_cpu_clock_hertz gauge\n" +
            clockTag + " 3000000\n" +
            "# TYPE lhm_cpu_temperature_celsius gauge\n" +
            "# HELP " + temperatureTag + " has an invalid value and was skipped.\n";
        Assert.Equal(expected, content);
    }

    [Fact]
    public void ArchiveMetricsReadsOnlyRequestedTailAndEmitsNewestToOldest()
    {
        FakeHardware hardware = new(new Identifier("metrics", "0"), "Metric CPU", HardwareType.Cpu);
        HistoryReaderSensor sensor = new(
            hardware,
            SensorType.Clock,
            0,
            "Core #1",
            History(1, 6));
        hardware.AddSensor(sensor);

        HttpServer server = CreateServer(hardware);

        (string content, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) = server.BuildPrometheusResponse(
            Query(("archivelength", "3"), ("timestamps", "0"), ("lastvalue", "0")));

        Assert.Equal(new long[] { 0 }, sensor.RequestedSinceVersions);
        Assert.Equal(new[] { 4 }, sensor.RequestedMaxValues);
        AssertPrometheusHeaders(contentType, headers, archiveLength: 3, timestamps: 1, lastValue: 0);

        const string tag = "lhm_cpu_clock_hertz {\"sensorName\"=\"Core 1\", \"sensorAlias\"=\"Core 1 (/clock/0)\", \"hardwareName\"=\"Metric CPU\", \"hardwareAlias\"=\"Metric CPU (/metrics/0)\", \"sensorId\"=\"/clock/0\", \"hardwareId\"=\"/metrics/0\", \"host\"=\"METRICS-HOST\"}";
        string expected =
            "# TYPE lhm_cpu_clock_hertz gauge\n" +
            $"{tag} 5000000 {Timestamp(_historyStart.AddSeconds(4))}\n" +
            $"{tag} 4000000 {Timestamp(_historyStart.AddSeconds(3))}\n" +
            $"{tag} 3000000 {Timestamp(_historyStart.AddSeconds(2))}\n";
        Assert.Equal(expected, content);
    }

    [Theory]
    [InlineData("archivelength,timestamps,lastvalue")]
    [InlineData("archivelength,lastvalue,timestamps")]
    [InlineData("timestamps,archivelength,lastvalue")]
    [InlineData("timestamps,lastvalue,archivelength")]
    [InlineData("lastvalue,archivelength,timestamps")]
    [InlineData("lastvalue,timestamps,archivelength")]
    public void ArchiveQueryOrderingPreservesForcedTimestampSemantics(string keyOrder)
    {
        (HttpServer server, HistoryReaderSensor sensor) = CreateReaderServer(20);

        (string _, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) = server.BuildPrometheusResponse(
            OrderedQuery(keyOrder, archiveLength: 3, timestamps: 0, lastValue: 0));

        Assert.Equal(new long[] { 0 }, sensor.RequestedSinceVersions);
        Assert.Equal(new[] { 4 }, sensor.RequestedMaxValues);
        AssertPrometheusHeaders(contentType, headers, archiveLength: 3, timestamps: 1, lastValue: 0);
    }

    [Theory]
    [InlineData(99, -4, 9, 10, 1, 1, 11)]
    [InlineData(-7, 4, 1, 0, 0, 1, 1)]
    [InlineData(0, 0, 0, 1, 1, 0, 2)]
    public void MetricsQueryValuesRemainBounded(
        int requestedArchiveLength,
        int requestedTimestamps,
        int requestedLastValue,
        int expectedArchiveLength,
        int expectedTimestamps,
        int expectedLastValue,
        int expectedMaxValues)
    {
        (HttpServer server, HistoryReaderSensor sensor) = CreateReaderServer(20);

        (string _, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) = server.BuildPrometheusResponse(
            Query(
                ("archivelength", requestedArchiveLength.ToString(CultureInfo.InvariantCulture)),
                ("timestamps", requestedTimestamps.ToString(CultureInfo.InvariantCulture)),
                ("lastvalue", requestedLastValue.ToString(CultureInfo.InvariantCulture))));

        Assert.Equal(new long[] { 0 }, sensor.RequestedSinceVersions);
        Assert.Equal(new[] { expectedMaxValues }, sensor.RequestedMaxValues);
        AssertPrometheusHeaders(contentType, headers, expectedArchiveLength, expectedTimestamps, expectedLastValue);
    }

    [Fact]
    public void SensorWithoutHistoryReaderKeepsValuesFallbackBehavior()
    {
        FakeHardware hardware = new(new Identifier("metrics", "0"), "Metric CPU", HardwareType.Cpu);
        FallbackSensor sensor = new(
            hardware,
            SensorType.Temperature,
            0,
            "Package",
            History(10, 3));
        hardware.AddSensor(sensor);

        HttpServer server = CreateServer(hardware);

        (string content, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) = server.BuildPrometheusResponse(
            Query(("archivelength", "2")));

        Assert.Equal(1, sensor.ValuesAccessCount);
        AssertPrometheusHeaders(contentType, headers, archiveLength: 2, timestamps: 1, lastValue: 1);

        const string tag = "lhm_cpu_temperature_celsius {\"sensorName\"=\"Package\", \"sensorAlias\"=\"Package (/temperature/0)\", \"hardwareName\"=\"Metric CPU\", \"hardwareAlias\"=\"Metric CPU (/metrics/0)\", \"sensorId\"=\"/temperature/0\", \"hardwareId\"=\"/metrics/0\", \"host\"=\"METRICS-HOST\"}";
        string expected =
            "# TYPE lhm_cpu_temperature_celsius gauge\n" +
            $"{tag} 12 {Timestamp(_historyStart.AddSeconds(2))}\n" +
            $"{tag} 11 {Timestamp(_historyStart.AddSeconds(1))}\n" +
            $"{tag} 10 {Timestamp(_historyStart)}\n";
        Assert.Equal(expected, content);
    }

    [Fact]
    public void TemperatureRateUsesExplicitCelsiusPerSecondPrometheusUnit()
    {
        FakeHardware hardware = new(new Identifier("metrics", "0"), "Metric CPU", HardwareType.Cpu);
        HistoryReaderSensor sensor = new(
            hardware,
            SensorType.TemperatureRate,
            0,
            "Package Rate",
            new SensorValue(1.25f, _historyStart));
        hardware.AddSensor(sensor);
        HttpServer server = CreateServer(hardware);

        (string content, _, _) = server.BuildPrometheusResponse(null);

        Assert.Contains("# TYPE lhm_cpu_temperaturerate_celsius_per_second gauge\n", content);
        Assert.Contains("\"sensorId\"=\"/temperaturerate/0\"", content);
        Assert.EndsWith(" 1.25\n", content);
    }

    private static (HttpServer Server, HistoryReaderSensor Sensor) CreateReaderServer(int historyCount)
    {
        FakeHardware hardware = new(new Identifier("metrics", "0"), "Metric CPU", HardwareType.Cpu);
        HistoryReaderSensor sensor = new(
            hardware,
            SensorType.Clock,
            0,
            "Core #1",
            History(1, historyCount));
        hardware.AddSensor(sensor);
        return (CreateServer(hardware), sensor);
    }

    private static HttpServer CreateServer(FakeHardware hardware)
    {
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);
        Node root = new("METRICS-HOST");
        root.Nodes.Add(new HardwareNode(hardware, settings, unitManager));
        return new HttpServer(root, hardware, "?", 8085);
    }

    private static SensorValue[] History(int firstValue, int count)
    {
        SensorValue[] values = new SensorValue[count];
        for (int i = 0; i < count; i++)
            values[i] = new SensorValue(firstValue + i, _historyStart.AddSeconds(i));

        return values;
    }

    private static long Timestamp(DateTime value)
    {
        return ((DateTimeOffset)value).ToUnixTimeMilliseconds();
    }

    private static NameValueCollection Query(params (string Key, string Value)[] values)
    {
        NameValueCollection query = new();
        foreach ((string key, string value) in values)
            query.Add(key, value);

        return query;
    }

    private static NameValueCollection OrderedQuery(string keyOrder, int archiveLength, int timestamps, int lastValue)
    {
        Dictionary<string, string> values = new()
        {
            ["archivelength"] = archiveLength.ToString(CultureInfo.InvariantCulture),
            ["timestamps"] = timestamps.ToString(CultureInfo.InvariantCulture),
            ["lastvalue"] = lastValue.ToString(CultureInfo.InvariantCulture)
        };
        NameValueCollection query = new();
        foreach (string key in keyOrder.Split(','))
            query.Add(key, values[key]);

        return query;
    }

    private static void AssertPrometheusHeaders(
        string contentType,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        int archiveLength,
        int timestamps,
        int lastValue)
    {
        Assert.Equal("text/plain", contentType);
        Assert.Collection(
            headers,
            header => AssertHeader(header, "Cache-Control", "no-cache"),
            header => AssertHeader(header, "Access-Control-Allow-Origin", "*"),
            header => AssertHeader(header, "X-archivelength", archiveLength.ToString(CultureInfo.InvariantCulture)),
            header => AssertHeader(header, "X-timestamps", timestamps.ToString(CultureInfo.InvariantCulture)),
            header => AssertHeader(header, "X-lastvalue", lastValue.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AssertHeader(KeyValuePair<string, string> header, string expectedName, string expectedValue)
    {
        Assert.Equal(expectedName, header.Key);
        Assert.Equal(expectedValue, header.Value);
    }

#pragma warning disable CS0067 // Events required by the interfaces are not raised by these fakes.

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

    private abstract class FakeSensor : ISensor
    {
        protected FakeSensor(
            IHardware hardware,
            SensorType sensorType,
            int index,
            string name,
            IReadOnlyList<SensorValue> history)
        {
            Hardware = hardware;
            SensorType = sensorType;
            Index = index;
            Name = name;
            HistoryValues = history;
            Identifier = new Identifier(
                hardware.Identifier,
                sensorType.ToString().ToLowerInvariant(),
                index.ToString(CultureInfo.InvariantCulture));
        }

        protected IReadOnlyList<SensorValue> HistoryValues { get; }

        public IControl Control => null;
        public IHardware Hardware { get; }
        public Identifier Identifier { get; }
        public int Index { get; }
        public bool IsDefaultHidden => false;
        public float? Max => HistoryValues.Count == 0 ? null : HistoryValues.Max(value => value.Value);
        public float? Min => HistoryValues.Count == 0 ? null : HistoryValues.Min(value => value.Value);
        public string Name { get; set; }
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType { get; }
        public float? Value => HistoryValues.Count == 0 ? null : HistoryValues[HistoryValues.Count - 1].Value;
        public abstract IEnumerable<SensorValue> Values { get; }
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }

        public void ResetMax() { }

        public void ClearValues() { }

        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);

        public void Traverse(IVisitor visitor) { }
    }

    private sealed class HistoryReaderSensor : FakeSensor, ISensorHistoryReader
    {
        public HistoryReaderSensor(
            IHardware hardware,
            SensorType sensorType,
            int index,
            string name,
            params SensorValue[] history)
            : base(hardware, sensorType, index, name, history)
        { }

        public List<long> RequestedSinceVersions { get; } = new();
        public List<int> RequestedMaxValues { get; } = new();
        public long HistoryVersion => HistoryValues.Count;

        public override IEnumerable<SensorValue> Values =>
            throw new InvalidOperationException("Reader-backed metrics must not enumerate ISensor.Values.");

        public SensorHistorySlice ReadHistory(long sinceVersion, int maxValues)
        {
            RequestedSinceVersions.Add(sinceVersion);
            RequestedMaxValues.Add(maxValues);

            int startIndex = Math.Max(0, HistoryValues.Count - maxValues);
            SensorValue[] tail = new SensorValue[HistoryValues.Count - startIndex];
            for (int i = 0; i < tail.Length; i++)
                tail[i] = HistoryValues[startIndex + i];

            return new SensorHistorySlice(HistoryVersion, resetRequired: true, tail);
        }
    }

    private sealed class FallbackSensor : FakeSensor
    {
        public FallbackSensor(
            IHardware hardware,
            SensorType sensorType,
            int index,
            string name,
            params SensorValue[] history)
            : base(hardware, sensorType, index, name, history)
        { }

        public int ValuesAccessCount { get; private set; }

        public override IEnumerable<SensorValue> Values
        {
            get
            {
                ValuesAccessCount++;
                return HistoryValues;
            }
        }
    }

#pragma warning restore CS0067
}
