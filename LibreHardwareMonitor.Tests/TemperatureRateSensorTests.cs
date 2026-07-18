// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class TemperatureRateSensorTests
{
    [Fact]
    public void WarmupRequiresThreeSamplesAndTwoSecondsBeforePublishing()
    {
        Fixture fixture = CreateFixture();

        fixture.Sensor.Observe(40);
        Assert.Null(fixture.Sensor.Value);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(41);
        Assert.Null(fixture.Sensor.Value);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(42);

        Assert.InRange(fixture.Sensor.Value.Value, 0.999f, 1.001f);
        Assert.Equal(SensorType.TemperatureRate, fixture.Sensor.SensorType);
        Assert.Equal("/test/temperaturerate/0", fixture.Sensor.Identifier.ToString());
    }

    [Fact]
    public void FiveSecondRegressionExpiresOldSamplesWithoutChangingLinearSlope()
    {
        Fixture fixture = CreateFixture();
        fixture.Sensor.Observe(50);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(52);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(54);

        Assert.InRange(fixture.Sensor.Value.Value, 1.999f, 2.001f);

        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Sensor.Observe(62);

        Assert.Equal(3, fixture.Sensor.SampleCount);
        Assert.InRange(fixture.Sensor.Value.Value, 1.999f, 2.001f);
    }

    [Fact]
    public void DropoutAndBackwardClockClearTheWindowAndRequireFreshWarmup()
    {
        Fixture fixture = CreateFixture();
        Warm(fixture, 40, 41, 42);
        Assert.NotNull(fixture.Sensor.Value);

        fixture.Sensor.Observe(null);
        Assert.Null(fixture.Sensor.Value);
        Assert.Equal(0, fixture.Sensor.SampleCount);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Warm(fixture, 50, 52, 54);
        Assert.InRange(fixture.Sensor.Value.Value, 1.999f, 2.001f);

        fixture.Clock.SetUtcNow(new DateTime(2029, 12, 31, 23, 0, 0, DateTimeKind.Utc));
        fixture.Sensor.Observe(10);

        Assert.Null(fixture.Sensor.Value);
        Assert.Equal(1, fixture.Sensor.SampleCount);
    }

    [Fact]
    public void NonFiniteInputClearsStateAndSampleStorageStaysBounded()
    {
        Fixture fixture = CreateFixture();
        Warm(fixture, 40, 41, 42);

        fixture.Sensor.Observe(float.NaN);

        Assert.Null(fixture.Sensor.Value);
        Assert.Equal(0, fixture.Sensor.SampleCount);

        for (int i = 0; i < 100; i++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
            fixture.Sensor.Observe(40 + i * 0.01f);
        }

        Assert.Equal(TemperatureRateSensor.MaxSamples, fixture.Sensor.SampleCount);
    }

    [Fact]
    public void SensorNodeFormatsCelsiusAndFahrenheitRatesWithoutAnOffset()
    {
        Fixture fixture = CreateFixture();
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);
        SensorNode node = new(fixture.Sensor, settings, unitManager);

        Assert.Equal("1.25 °C/s", node.ValueToString(1.25f));

        unitManager.TemperatureUnit = TemperatureUnit.Fahrenheit;

        Assert.Equal("2.25 °F/s", node.ValueToString(1.25f));
    }

    private static Fixture CreateFixture()
    {
        SimulatedClock clock = new(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        TestSettings settings = new();
        TestHardware hardware = new(settings);
        TemperatureRateSensor sensor = new("Test Temperature Rate", 0, hardware, settings, clock.GetUtcNow);
        return new Fixture(sensor, clock);
    }

    private static void Warm(Fixture fixture, float first, float second, float third)
    {
        fixture.Sensor.Observe(first);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(second);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Sensor.Observe(third);
    }

    private sealed class Fixture
    {
        internal Fixture(TemperatureRateSensor sensor, SimulatedClock clock)
        {
            Sensor = sensor;
            Clock = clock;
        }

        internal TemperatureRateSensor Sensor { get; }

        internal SimulatedClock Clock { get; }
    }

    private sealed class SimulatedClock
    {
        private DateTime _utcNow;

        internal SimulatedClock(DateTime utcNow)
        {
            _utcNow = utcNow;
        }

        internal DateTime GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);

        internal void SetUtcNow(DateTime utcNow) => _utcNow = utcNow;
    }

    private sealed class TestHardware : HardwareBase
    {
        internal TestHardware(ISettings settings) : base("Test Hardware", new Identifier("test"), settings)
        { }

        public override HardwareType HardwareType => HardwareType.Cpu;

        public override void Update()
        { }
    }

    private sealed class TestSettings : ISettings
    {
        private readonly Dictionary<string, string> _values = new();

        public bool Contains(string name) => _values.ContainsKey(name);

        public void SetValue(string name, string value) => _values[name] = value;

        public string GetValue(string name, string value) =>
            _values.TryGetValue(name, out string stored) ? stored : value;

        public void Remove(string name) => _values.Remove(name);
    }
}
