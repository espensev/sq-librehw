// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HidSharp;
using LibreHardwareMonitor.Hardware.Psu.Corsair;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public static class CorsairPsuTestFixture
{
    public enum Failure { None, VendorReply, ProductReply, ReadIo, ReadTimeout, OpenDenied, Unexpected, StaleIdentityReply, UnsupportedInputCurrent }

    internal sealed class TestDevice(string path, Failure failure = Failure.None) : HidDevice
    {
        public Failure Failure { get; set; } = failure;
        public Action OnAccess { get; set; }
        public int ReplyLength { get; set; } = 65;
        public int ProductId { get; set; } = 0x1c1f;
        public ushort NumericValue { get; set; }
        public UsbApi.Command? RejectedCommand { get; set; }
        public List<TestStream> Streams { get; } = [];
        public override string DevicePath => path;
        public override int VendorID => 0x1b1c;
        public override int ProductID => ProductId;
        public override int ReleaseNumberBcd => 0;
        public override string GetFileSystemName() => path;
        public override int GetMaxInputReportLength() => 65;
        public override int GetMaxOutputReportLength() => 65;
        public override int GetMaxFeatureReportLength() => 0;

        protected override DeviceStream OpenDeviceDirectly(OpenConfiguration configuration)
        {
            OnAccess?.Invoke();
            if (Failure == Failure.OpenDenied)
                throw new UnauthorizedAccessException("device access denied");

            var stream = new TestStream(this);
            Streams.Add(stream);
            return stream;
        }
    }

    internal sealed class TestStream(TestDevice device) : HidStream(device)
    {
        private readonly byte[] _request = new byte[65];
        private int _readsForCommand;
        public bool IsDisposed { get; private set; }
        public int ReadCount { get; private set; }
        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }
        public override void GetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetFeature(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            device.OnAccess?.Invoke();
            Array.Copy(buffer, offset, _request, 0, count);
            _readsForCommand = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            device.OnAccess?.Invoke();
            ReadCount++;
            _readsForCommand++;
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
            else
                BitConverter.GetBytes(device.NumericValue).CopyTo(reply, 3);

            if (device.RejectedCommand.HasValue && command == (byte)device.RejectedCommand.Value)
                reply[2] = 0xff;

            if ((device.Failure == Failure.VendorReply && command == (byte)UsbApi.Command.VEND_STR) ||
                (device.Failure == Failure.ProductReply && command == (byte)UsbApi.Command.PROD_STR) ||
                (device.Failure == Failure.UnsupportedInputCurrent && command == (byte)UsbApi.Command.IN_AMPS))
                reply[2] = 0;

            if (device.Failure == Failure.StaleIdentityReply && _readsForCommand == 1)
            {
                // Replay the previous command echo observed on the live HX1500i.
                reply[1] = command == (byte)UsbApi.Command.VEND_STR ? (byte)0xfe : (byte)3;
                reply[2] = command == (byte)UsbApi.Command.VEND_STR ? (byte)3 : (byte)UsbApi.Command.VEND_STR;
                Encoding.ASCII.GetBytes("STALE").CopyTo(reply, 3);
            }

            Assert.True(count >= device.ReplyLength);
            Array.Copy(reply, 0, buffer, offset, device.ReplyLength);
            return device.ReplyLength;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
