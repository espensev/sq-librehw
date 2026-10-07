// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Psu;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using Xunit;
using static LibreHardwareMonitor.Tests.CorsairPsuTestFixture;

namespace LibreHardwareMonitor.Tests;

[Collection("Corsair PSU mutex")]
public sealed class CorsairPsuDiscoveryTests : IDisposable
{
    public CorsairPsuDiscoveryTests() => Mutexes.Open($@"Local\LibreHardwareMonitor.CorsairPsuTests.{Guid.NewGuid():N}");

    public void Dispose() => Mutexes.Close();

    [Fact]
    public void FirmwareInfo_MismatchedVendorReply_ReproducesReportedError()
    {
        var device = new TestDevice("unresponsive-psu", Failure.VendorReply);
        using HidStream stream = device.Open();

        ProtocolError error = Assert.Throws<ProtocolError>(() => UsbApi.FwInfo(stream));

        Assert.Contains("Can't read vendor string", error.Message);
        Assert.Contains(device.DevicePath, error.Message);
    }

    [Fact]
    public void FirmwareInfo_StaleReplies_UsesMatchingIdentityPayload()
    {
        var device = new TestDevice("stale-psu", Failure.StaleIdentityReply);
        using HidStream stream = device.Open();

        UsbApi.FirmwareInfo info = UsbApi.FwInfo(stream);

        Assert.Equal("CORSAIR", info.Vendor);
        Assert.Equal("HX1500i", info.Product);
        Assert.Equal(4, Assert.Single(device.Streams).ReadCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(18)]
    public void FirmwareInfo_TruncatedReplies_ReportsBoundedProtocolFailure(int replyLength)
    {
        var device = new TestDevice("short-psu") { ReplyLength = replyLength };
        using HidStream stream = device.Open();

        ProtocolError error = Assert.Throws<ProtocolError>(() => UsbApi.FwInfo(stream));

        Assert.Contains("Can't read vendor string", error.Message);
        Assert.Equal(4, Assert.Single(device.Streams).ReadCount);
    }

    [Fact]
    public void FirmwareInfo_OnlyMismatchedReplies_StopsAtReplyLimit()
    {
        var device = new TestDevice("stale-psu", Failure.VendorReply);
        using HidStream stream = device.Open();

        Assert.Throws<ProtocolError>(() => UsbApi.FwInfo(stream));

        Assert.Equal(4, Assert.Single(device.Streams).ReadCount);
    }

    [Fact]
    public void OptionalCommands_UnsupportedReply_DoesNotRetry()
    {
        var device = new TestDevice("unsupported-psu", Failure.UnsupportedInputCurrent);
        using HidStream stream = device.Open();

        Assert.Equal(UsbApi.OptionalCommands.None, UsbApi.GetOptionalCommands(stream));
        Assert.Equal(1, Assert.Single(device.Streams).ReadCount);
    }

    [Theory]
    [InlineData(Failure.VendorReply, "Can't read vendor string")]
    [InlineData(Failure.ProductReply, "Can't read product")]
    [InlineData(Failure.ReadIo, "device disconnected")]
    [InlineData(Failure.ReadTimeout, "read timed out")]
    [InlineData(Failure.OpenDenied, "device access denied")]
    public void Discovery_OneDeviceFails_KeepsWorkingDevicesAndReportsFailure(Failure failure, string message)
    {
        var first = new TestDevice("first");
        var failed = new TestDevice("failed", failure);
        var last = new TestDevice("last");

        var group = new CorsairPsuGroup(new TestSettings(), [first, failed, last]);
        try
        {
            Assert.Equal(2, group.Hardware.Count);
            Assert.All(group.Hardware, hardware => Assert.Equal("Corsair HX1500i", hardware.Name));
            Assert.All(group.Hardware, hardware => Assert.NotEmpty(hardware.Sensors));
            Assert.Equal(["/psu/corsair/0", "/psu/corsair/2"], group.Hardware.Select(hardware => hardware.Identifier.ToString()));
            Assert.Contains(failed.DevicePath, group.GetReport());
            Assert.Contains(message, group.GetReport());
            Assert.All(first.Streams.Concat(failed.Streams).Concat(last.Streams), stream => Assert.True(stream.IsDisposed));
        }
        finally
        {
            group.Close();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Discovery_FailureAndRecovery_PreserveSupportedSlotsAndSavedSensorLabels(int failedSlots)
    {
        var settings = new TestSettings();
        TestDevice[] supported = [new("first"), new("middle"), new("last")];
        var unsupported = new TestDevice("unsupported") { ProductId = 0xffff };
        HidDevice[] devices = [unsupported, supported[0], unsupported, supported[1], supported[2]];
        string[] identifiers = ["/psu/corsair/0", "/psu/corsair/1", "/psu/corsair/2"];
        string[] labels = ["First VRM", "Middle VRM", "Last VRM"];

        var healthy = new CorsairPsuGroup(settings, devices);
        try
        {
            Assert.Equal(identifiers, healthy.Hardware.Select(hardware => hardware.Identifier.ToString()));
            for (int index = 0; index < supported.Length; index++)
                VrmSensor(healthy.Hardware[index]).Name = labels[index];
        }
        finally { healthy.Close(); }

        for (int index = 0; index < supported.Length; index++)
            supported[index].Failure = (failedSlots & (1 << index)) != 0 ? Failure.VendorReply : Failure.None;

        var failed = new CorsairPsuGroup(settings, devices);
        try
        {
            int[] workingSlots = Enumerable.Range(0, supported.Length).Where(index => (failedSlots & (1 << index)) == 0).ToArray();
            Assert.Equal(workingSlots.Select(index => identifiers[index]), failed.Hardware.Select(hardware => hardware.Identifier.ToString()));
            Assert.Equal(workingSlots.Select(index => labels[index]), failed.Hardware.Select(hardware => VrmSensor(hardware).Name));
        }
        finally { failed.Close(); }

        foreach (TestDevice device in supported)
            device.Failure = Failure.None;

        var recovered = new CorsairPsuGroup(settings, devices);
        try
        {
            Assert.Equal(identifiers, recovered.Hardware.Select(hardware => hardware.Identifier.ToString()));
            Assert.Equal(labels, recovered.Hardware.Select(hardware => VrmSensor(hardware).Name));
            Assert.Empty(unsupported.Streams);
            Assert.All(supported.SelectMany(device => device.Streams), stream => Assert.True(stream.IsDisposed));
        }
        finally { recovered.Close(); }

        static ISensor VrmSensor(IHardware hardware) => Assert.Single(hardware.Sensors, sensor => sensor.SensorType == SensorType.Temperature && sensor.Index == 0);
    }

    [Fact]
    public void ComputerOpen_FailedCorsairDiscovery_KeepsOtherHardwareAndAllowsRetry()
    {
        var settings = new TestSettings();
        var device = new TestDevice("unresponsive-psu", Failure.VendorReply);
        var otherHardware = new TestHardware(settings);
        var dependencies = new Computer.OpenDependencies(
            () => null, () => { }, () => { }, () => { }, () => { },
            add =>
            {
                add(new TestGroup(otherHardware));
                add(new CorsairPsuGroup(settings, [device]));
            });
        var computer = new Computer(settings, dependencies);
        try
        {
            computer.Open();

            Assert.Same(otherHardware, Assert.Single(computer.Hardware));
            Assert.All(device.Streams, stream => Assert.True(stream.IsDisposed));

            computer.Close();
            device.Failure = Failure.None;
            computer.Open();

            Assert.Equal(2, computer.Hardware.Count);
            Assert.Contains(computer.Hardware, hardware => hardware.HardwareType == HardwareType.Psu);
        }
        finally
        {
            computer.Close();
        }
    }

    [Fact]
    public void Discovery_UnexpectedFailure_IsNotSuppressed()
    {
        var device = new TestDevice("unexpected", Failure.Unexpected);

        Assert.Throws<InvalidOperationException>(() => new CorsairPsuGroup(new TestSettings(), [device]));
        Assert.True(Assert.Single(device.Streams).IsDisposed);
    }

    private sealed class TestHardware(ISettings settings) : Hardware.Hardware("CPU", new Identifier("test-cpu"), settings)
    {
        public override HardwareType HardwareType => HardwareType.Cpu;
        public override void Update() { }
    }

    private sealed class TestGroup(IHardware hardware) : IGroup
    {
        public IReadOnlyList<IHardware> Hardware { get; } = [hardware];
        public void Close() { }
        public string GetReport() => "Test CPU";
    }

    private sealed class TestSettings : ISettings
    {
        private readonly Dictionary<string, string> _values = [];

        public bool Contains(string name) => _values.ContainsKey(name);
        public string GetValue(string name, string value) => _values.TryGetValue(name, out string stored) ? stored : value;
        public void SetValue(string name, string value) => _values[name] = value;
        public void Remove(string name) => _values.Remove(name);
    }
}
