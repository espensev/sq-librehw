// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HidSharp;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Psu;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class CorsairPsuDiscoveryTests
{
    [Fact]
    public void FirmwareInfo_MismatchedVendorReply_ReproducesReportedError()
    {
        var device = new TestDevice("unresponsive-psu", Failure.VendorReply);
        using HidStream stream = device.Open();

        ProtocolError error = Assert.Throws<ProtocolError>(() => UsbApi.FwInfo(stream));

        Assert.Contains("Can't read vendor string", error.Message);
        Assert.Contains(device.DevicePath, error.Message);
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
            Assert.Equal(["/psu/corsair/0", "/psu/corsair/1"], group.Hardware.Select(hardware => hardware.Identifier.ToString()));
            Assert.Contains(failed.DevicePath, group.GetReport());
            Assert.Contains(message, group.GetReport());
            Assert.All(first.Streams.Concat(failed.Streams).Concat(last.Streams), stream => Assert.True(stream.IsDisposed));
        }
        finally
        {
            group.Close();
        }
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

    public enum Failure { None, VendorReply, ProductReply, ReadIo, ReadTimeout, OpenDenied, Unexpected }

    private sealed class TestDevice(string path, Failure failure = Failure.None) : HidDevice
    {
        public Failure Failure { get; set; } = failure;
        public List<TestStream> Streams { get; } = [];
        public override string DevicePath => path;
        public override int VendorID => 0x1b1c;
        public override int ProductID => 0x1c1f;
        public override int ReleaseNumberBcd => 0;
        public override string GetFileSystemName() => path;
        public override int GetMaxInputReportLength() => 65;
        public override int GetMaxOutputReportLength() => 65;
        public override int GetMaxFeatureReportLength() => 0;

        protected override DeviceStream OpenDeviceDirectly(OpenConfiguration configuration)
        {
            if (Failure == Failure.OpenDenied)
                throw new UnauthorizedAccessException("device access denied");

            var stream = new TestStream(this);
            Streams.Add(stream);
            return stream;
        }
    }

    private sealed class TestStream(TestDevice device) : HidStream(device)
    {
        private readonly byte[] _request = new byte[65];
        public bool IsDisposed { get; private set; }
        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }
        public override void GetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Array.Copy(buffer, offset, _request, 0, count);

        public override int Read(byte[] buffer, int offset, int count)
        {
            byte command = _request[2];
            if (command == (byte)UsbApi.Command.VEND_STR)
            {
                if (device.Failure == Failure.ReadIo)
                    throw new IOException("device disconnected");
                if (device.Failure == Failure.ReadTimeout)
                    throw new TimeoutException("read timed out");
                if (device.Failure == Failure.Unexpected)
                    throw new InvalidOperationException("unexpected programming error");
            }

            var reply = new byte[65];
            reply[1] = _request[1];
            reply[2] = command;
            if (command == (byte)UsbApi.Command.VEND_STR)
                Encoding.ASCII.GetBytes("CORSAIR").CopyTo(reply, 3);
            else if (command == (byte)UsbApi.Command.PROD_STR)
                Encoding.ASCII.GetBytes("HX1500i").CopyTo(reply, 3);

            if ((device.Failure == Failure.VendorReply && command == (byte)UsbApi.Command.VEND_STR) ||
                (device.Failure == Failure.ProductReply && command == (byte)UsbApi.Command.PROD_STR))
                reply[2] = 0;

            Assert.True(count >= reply.Length);
            Array.Copy(reply, 0, buffer, offset, reply.Length);
            return reply.Length;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
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
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void SetValue(string name, string value) { }
        public void Remove(string name) { }
    }
}
