// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class Nct6701TemperatureFreshnessTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xA0)]
    [InlineData(0x7E)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    public void Update_InvalidOrRemappedSourceClearsPreviousTemperatureAndRecovers(byte invalidTemperature)
    {
        FakeLpcPort port = FakeLpcPort.Create();
        var chip = new Nct677X(port, Chip.NCT6701D, 0, FakeLpcPort.BasePort);
        port.Registers[0x100] = 16; // PECI_0 source selector.
        port.Registers[0x073] = 60;
        port.Registers[0x491] = 48; // Independent CPUTIN source.
        chip.Update();
        Assert.Equal(60f, chip.Temperatures[0]);
        Assert.Equal(48f, chip.Temperatures[1]);

        port.Registers[0x073] = invalidTemperature;
        chip.Update();
        Assert.Null(chip.Temperatures[0]);
        Assert.Equal(48f, chip.Temperatures[1]);

        port.Registers[0x073] = 62;
        chip.Update();
        Assert.Equal(62f, chip.Temperatures[0]);

        // The same physical register now reports CPUTIN. The old PECI slot
        // must clear, while the newly populated slot survives later loop iterations.
        port.Registers[0x100] = 2;
        port.Registers[0x073] = 66;
        chip.Update();
        Assert.Null(chip.Temperatures[0]);
        Assert.Equal(66f, chip.Temperatures[1]);

        // An unknown selector cannot preserve either the former source or value.
        port.Registers[0x100] = 0xFF;
        chip.Update();
        Assert.Null(chip.Temperatures[0]);
        Assert.Equal(48f, chip.Temperatures[1]);

        port.Registers[0x100] = 16;
        port.Registers[0x073] = 64;
        chip.Update();
        Assert.Equal(64f, chip.Temperatures[0]);
        Assert.Equal(48f, chip.Temperatures[1]);
    }

    private sealed class FakeLpcPort : LpcPort
    {
        internal const ushort BasePort = 0x290;
        private byte _bank;
        private byte _selectedRegister;
        internal Dictionary<ushort, byte> Registers { get; private set; }

        // Never invoked: the fixture bypasses the PawnIO-owning base constructor.
        private FakeLpcPort() : base(0, 0) { }

        internal static FakeLpcPort Create()
        {
            var port = (FakeLpcPort)RuntimeHelpers.GetUninitializedObject(typeof(FakeLpcPort));
            port.Registers = new Dictionary<ushort, byte>();
            return port;
        }

        public override byte ReadIoPort(ushort port)
        {
            Assert.Equal(BasePort + 6, port);
            return Registers.GetValueOrDefault((ushort)((_bank << 8) | _selectedRegister));
        }

        public override void WriteIoPort(ushort port, byte value)
        {
            if (port == BasePort + 5)
            {
                _selectedRegister = value;
                return;
            }

            Assert.Equal(BasePort + 6, port);
            if (_selectedRegister == 0x4E)
                _bank = value;
            else
                Registers[(ushort)((_bank << 8) | _selectedRegister)] = value;
        }
    }
}
