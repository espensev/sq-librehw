// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc.EC;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class EmbeddedControllerCompatibilityTests
{
    [Fact]
    public void Intel700_WaterFlowReadsCorrectedRegisterAndRetainsLitresPerHour()
    {
        var settings = new TestSettings();
        // The real board factory supplies the mapping; only register I/O is replaced.
        EmbeddedController configured = EmbeddedController.Create(Model.ROG_MAXIMUS_Z790_HERO, settings);
        Assert.NotNull(configured);
        FieldInfo sourcesField = typeof(EmbeddedController).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(sourcesField);
        var sources = Assert.IsAssignableFrom<IEnumerable<EmbeddedControllerSource>>(sourcesField.GetValue(configured));
        var io = new RegisterIo();
        var controller = new TestController(sources, settings, io);
        try
        {
            controller.Update();

            Assert.Contains((ushort)0x00bc, io.Registers);
            Assert.Contains((ushort)0x00bd, io.Registers);
            Assert.DoesNotContain((ushort)0x00be, io.Registers);
            ISensor flow = Assert.Single(controller.Sensors, sensor => sensor.SensorType == SensorType.Flow);
            Assert.Equal("/lpc/ec/flow/0", flow.Identifier.ToString());
            Assert.True(flow.Value.HasValue);
            Assert.Equal(60f, flow.Value.Value, 4);
            Assert.True(io.Disposed);
        }
        finally
        {
            controller.Close();
            configured.Close();
        }
    }

    private sealed class TestController(
        IEnumerable<EmbeddedControllerSource> sources, ISettings settings, IEmbeddedControllerIO io)
        : EmbeddedController(sources, settings)
    {
        protected override IEmbeddedControllerIO AcquireIOInterface() => io;
    }

    private sealed class RegisterIo : IEmbeddedControllerIO
    {
        internal ushort[] Registers { get; private set; }
        internal bool Disposed { get; private set; }

        public void Read(ushort[] registers, byte[] data)
        {
            Registers = (ushort[])registers.Clone();
            for (int i = 0; i < registers.Length; i++)
                data[i] = registers[i] == 0x00bd ? (byte)42 : (byte)0;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class TestSettings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void Remove(string name) { }
        public void SetValue(string name, string value) { }
    }
}
