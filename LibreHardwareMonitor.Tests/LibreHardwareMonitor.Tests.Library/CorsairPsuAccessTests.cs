// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using Xunit;
using static LibreHardwareMonitor.Tests.CorsairPsuTestFixture;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition("Corsair PSU mutex", DisableParallelization = true)]
public sealed class CorsairPsuMutexCollection;

[Collection("Corsair PSU mutex")]
public sealed class CorsairPsuAccessTests : IDisposable
{
    private readonly string _mutexName = $@"Local\LibreHardwareMonitor.CorsairPsuTests.{Guid.NewGuid():N}";

    public CorsairPsuAccessTests() => Mutexes.Open(_mutexName);

    public void Dispose() => Mutexes.Close();

    [Fact]
    public void DiscoveryUpdateAndProperties_ExcludeOtherMonitorForEntireTransaction()
    {
        var device = new TestDevice("shared-psu") { OnAccess = AssertOtherMonitorCannotEnter };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            Assert.NotEmpty(hardware.Properties);

            Assert.Equal(3, device.Streams.Count);
            Assert.All(device.Streams, stream =>
            {
                Assert.True(stream.IsDisposed);
                Assert.InRange(stream.ReadTimeout, 1, 250);
                Assert.InRange(stream.WriteTimeout, 1, 250);
            });
            Assert.True(OtherMonitorCanEnter(1000));
        }
        finally
        {
            hardware.Close();
        }
    }

    [Theory]
    [InlineData("discovery")]
    [InlineData("update")]
    [InlineData("properties")]
    public void MissingMutex_PerformsNoDeviceIoAndRecoversWithPrivateMutex(string operation)
    {
        var device = new TestDevice("missing-mutex-psu") { NumericValue = 20 };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            hardware.Update();
            Assert.All(hardware.Sensors, sensor => Assert.NotNull(sensor.Value));
            int streamCount = device.Streams.Count;
            int accessCount = 0;
            device.OnAccess = () => accessCount++;
            Mutexes.Close();
            try
            {
                if (operation == "discovery")
                {
                    var group = new CorsairPsuGroup(new Settings(), [device]);
                    try
                    {
                        Assert.Empty(group.Hardware);
                        Assert.Contains("Timed out waiting for Corsair PSU access", group.GetReport());
                        Assert.Contains(device.DevicePath, group.GetReport());
                    }
                    finally { group.Close(); }
                }
                else if (operation == "update")
                {
                    hardware.Update();
                    Assert.All(hardware.Sensors, sensor => Assert.Null(sensor.Value));
                }
                else
                    Assert.Empty(hardware.Properties);

                Assert.Equal(0, accessCount);
                Assert.Equal(streamCount, device.Streams.Count);
            }
            finally { Mutexes.Open(_mutexName); }

            device.OnAccess = AssertOtherMonitorCannotEnter;
            if (operation == "discovery")
            {
                var recovered = new CorsairPsuGroup(new Settings(), [device]);
                try { Assert.Single(recovered.Hardware); }
                finally { recovered.Close(); }
            }
            else if (operation == "update")
            {
                hardware.Update();
                Assert.All(hardware.Sensors, sensor => Assert.NotNull(sensor.Value));
            }
            else
                Assert.NotEmpty(hardware.Properties);

            Assert.Equal(streamCount + 1, device.Streams.Count);
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));
            Assert.True(OtherMonitorCanEnter(1000));
        }
        finally { hardware.Close(); }
    }

    [Theory]
    [InlineData(Failure.OpenDenied)]
    [InlineData(Failure.ReadIo)]
    public void DiscoveryFailure_ReleasesSharedAccess(Failure failure)
    {
        var device = new TestDevice("failed-psu", failure) { OnAccess = AssertOtherMonitorCannotEnter };
        var group = new CorsairPsuGroup(new Settings(), [device]);
        try
        {
            Assert.Empty(group.Hardware);
            Assert.True(OtherMonitorCanEnter(1000));
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));
        }
        finally
        {
            group.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PollingFailure_ReleasesSharedAccess(bool readProperties)
    {
        var device = new TestDevice("failed-poll") { OnAccess = AssertOtherMonitorCannotEnter };
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            device.Failure = Failure.OpenDenied;
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                if (readProperties)
                    _ = hardware.Properties;
                else
                    hardware.Update();
            });
            Assert.True(OtherMonitorCanEnter(1000));
        }
        finally
        {
            hardware.Close();
        }
    }

    [Theory]
    [InlineData("discovery")]
    [InlineData("update")]
    [InlineData("properties")]
    public void BusyMutex_PerformsNoDeviceIo(string operation)
    {
        var device = new TestDevice("busy-psu");
        var hardware = new CorsairPsu(device, new Settings(), 0);
        try
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var otherMonitor = new Thread(() =>
            {
                using var mutex = new Mutex(false, _mutexName);
                Acquire(mutex);
                entered.Set();
                try { release.Wait(); }
                finally { mutex.ReleaseMutex(); }
            }) { IsBackground = true };
            otherMonitor.Start();
            try
            {
                Assert.True(entered.Wait(5000));
                if (operation == "discovery")
                {
                    var group = new CorsairPsuGroup(new Settings(), [device]);
                    try
                    {
                        Assert.Empty(group.Hardware);
                        Assert.Contains("access", group.GetReport(), StringComparison.OrdinalIgnoreCase);
                    }
                    finally { group.Close(); }
                }
                else if (operation == "update")
                    hardware.Update();
                else
                    Assert.Empty(hardware.Properties);

                Assert.Single(device.Streams);
            }
            finally
            {
                release.Set();
                Assert.True(otherMonitor.Join(5000));
            }
        }
        finally
        {
            hardware.Close();
        }
    }

    private void AssertOtherMonitorCannotEnter() => Assert.False(OtherMonitorCanEnter(0));

    private bool OtherMonitorCanEnter(int timeout)
    {
        bool acquired = false;
        var otherMonitor = new Thread(() =>
        {
            using var mutex = new Mutex(false, _mutexName);
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired)
                mutex.ReleaseMutex();
        }) { IsBackground = true };
        otherMonitor.Start();
        Assert.True(otherMonitor.Join(5000));
        return acquired;
    }

    private static void Acquire(Mutex mutex)
    {
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
    }

    private sealed class Settings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void SetValue(string name, string value) { }
        public void Remove(string name) { }
    }
}
