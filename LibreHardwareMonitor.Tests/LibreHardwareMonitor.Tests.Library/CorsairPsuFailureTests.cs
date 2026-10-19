// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using Xunit;
using static LibreHardwareMonitor.Tests.CorsairPsuTestFixture;

namespace LibreHardwareMonitor.Tests;

[Collection("Corsair PSU mutex")]
public sealed class CorsairPsuFailureTests : IDisposable
{
    private readonly string _mutexName = $@"Local\LibreHardwareMonitor.CorsairPsuFailureTests.{Guid.NewGuid():N}";

    public CorsairPsuFailureTests() => Mutexes.Open(_mutexName);

    public void Dispose() => Mutexes.Close();

    [Fact]
    public void BusyUpdate_InvalidatesImmediatelyAndRepeatedly_ThenRecoversWithoutResettingExtrema()
    {
        var device = new TestDevice("busy-poll") { NumericValue = 21 };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            AssertReadings(hardware, 21);
            var extrema = RawSensors(hardware).Select(sensor => new { Sensor = sensor, sensor.Min, sensor.Max }).ToArray();
            int streamCount = device.Streams.Count;

            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var otherMonitor = new Thread(() =>
            {
                using var mutex = new Mutex(false, _mutexName);
                try { mutex.WaitOne(); }
                catch (AbandonedMutexException) { }
                entered.Set();
                try { release.Wait(); }
                finally { mutex.ReleaseMutex(); }
            }) { IsBackground = true };
            otherMonitor.Start();
            try
            {
                Assert.True(entered.Wait(5000));
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    hardware.Update();

                    Assert.All(hardware.Sensors, sensor => Assert.Null(sensor.Value));
                    Assert.Equal(streamCount, device.Streams.Count);
                    Assert.All(extrema, original =>
                    {
                        Assert.Equal(original.Min, original.Sensor.Min);
                        Assert.Equal(original.Max, original.Sensor.Max);
                    });
                }
            }
            finally
            {
                release.Set();
                Assert.True(otherMonitor.Join(5000));
            }

            device.NumericValue = 14;
            hardware.Update();

            AssertReadings(hardware, 14);
            Assert.All(extrema.Where(original => original.Min.HasValue), original =>
            {
                Assert.Equal(14f, original.Sensor.Min);
                Assert.Equal(21f, original.Sensor.Max);
            });
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));
        }
        finally
        {
            hardware.Close();
        }
    }

    [Theory]
    [InlineData(1)] // Opening the stream.
    [InlineData(3)] // The first read, before any new sensor value.
    [InlineData(7)] // The third read, after two sensor values changed.
    public void FailedUpdate_InvalidatesEveryReading_PreservesExceptionAndExtrema_ThenRecovers(int failureAccess)
    {
        var device = new TestDevice("failed-poll") { NumericValue = 14 };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            device.NumericValue = 28;
            hardware.Update();
            AssertReadings(hardware, 28);
            var extrema = RawSensors(hardware).Select(sensor => new { Sensor = sensor, sensor.Min, sensor.Max }).ToArray();
            var failure = new IOException("injected poll failure");
            int accesses = 0;
            device.NumericValue = 21;
            device.OnAccess = () =>
            {
                if (++accesses != failureAccess)
                    return;

                if (failureAccess == 7)
                    Assert.Equal(21f, Assert.Single(hardware.Sensors, sensor => sensor.Name == "VRM").Value);
                throw failure;
            };

            Assert.Same(failure, Assert.Throws<IOException>(() => hardware.Update()));
            Assert.All(hardware.Sensors, sensor => Assert.Null(sensor.Value));
            Assert.All(extrema, original =>
            {
                Assert.Equal(original.Min, original.Sensor.Min);
                Assert.Equal(original.Max, original.Sensor.Max);
            });
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));

            device.OnAccess = null;
            device.NumericValue = 7;
            hardware.Update();

            AssertReadings(hardware, 7);
            Assert.All(extrema.Where(original => original.Min.HasValue), original =>
            {
                Assert.Equal(7f, original.Sensor.Min);
                Assert.Equal(28f, original.Sensor.Max);
            });
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));
        }
        finally
        {
            hardware.Close();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TotalOutput_AnyMissingPowerRail_IsUnavailableAndRecovers(int missingRail)
    {
        var device = new TestDevice("missing-rail") { NumericValue = 21 };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            AssertReadings(hardware, 21);
            Sensor[] powerRails = RawSensors(hardware)
                .Where(sensor => sensor.SensorType == SensorType.Power && sensor.Name.StartsWith("+", StringComparison.Ordinal))
                .OrderBy(sensor => sensor.Index)
                .ToArray();
            Assert.Equal(3, powerRails.Length);
            powerRails[missingRail].Value = null;

            Assert.Null(TotalOutput(hardware).Value);
            Assert.Equal(2, powerRails.Count(sensor => sensor.Value == 21));

            hardware.Update();

            AssertReadings(hardware, 21);
        }
        finally
        {
            hardware.Close();
        }
    }

    [Fact]
    public void TotalOutput_AllUnavailableIsNull_ValidZeroRemainsZero()
    {
        var device = new TestDevice("zero-rail");
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            Assert.All(RawSensors(hardware), sensor => Assert.Null(sensor.Value));
            Assert.Null(TotalOutput(hardware).Value);

            hardware.Update();

            AssertReadings(hardware, 0);
        }
        finally
        {
            hardware.Close();
        }
    }

    [Fact]
    public void RejectedCommand_InvalidatesOnlyItsReading_ThenRecovers()
    {
        var device = new TestDevice("rejected-current") { NumericValue = 21 };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            AssertReadings(hardware, 21);
            ISensor inputCurrent = Assert.Single(hardware.Sensors, sensor => sensor.Name == "Input" && sensor.SensorType == SensorType.Current);
            device.RejectedCommand = UsbApi.Command.IN_AMPS;

            hardware.Update();

            Assert.Null(inputCurrent.Value);
            Assert.Equal(21f, inputCurrent.Min);
            Assert.Equal(21f, inputCurrent.Max);
            Assert.All(RawSensors(hardware).Where(sensor => sensor != inputCurrent), sensor => Assert.Equal(21f, sensor.Value));
            Assert.Equal(63f, TotalOutput(hardware).Value);

            device.RejectedCommand = null;
            hardware.Update();

            AssertReadings(hardware, 21);
        }
        finally
        {
            hardware.Close();
        }
    }

    private static Sensor[] RawSensors(CorsairPsu hardware) => hardware.Sensors.OfType<Sensor>().Where(sensor => sensor is not CompositeSensor).ToArray();

    private static ISensor TotalOutput(CorsairPsu hardware) => Assert.Single(hardware.Sensors, sensor => sensor.Name == "Total Output");

    private static void AssertReadings(CorsairPsu hardware, float value)
    {
        Assert.NotEmpty(RawSensors(hardware));
        Assert.All(RawSensors(hardware), sensor => Assert.Equal(value, sensor.Value));
        Assert.Equal(3 * value, TotalOutput(hardware).Value);
    }

    private sealed class Settings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void SetValue(string name, string value) { }
        public void Remove(string name) { }
    }
}
