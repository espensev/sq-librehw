using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Cpu;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class AmdSensorIdentityTests
{
    [Fact]
    public void NewPmTable_DoesNotConsumeLegacyCoreAndCcdIndices()
    {
        var next = new Dictionary<SensorType, int>
        {
            [SensorType.Voltage] = 2,
            [SensorType.Temperature] = 3,
            [SensorType.Clock] = 3
        };

        int voltage = Amd17Cpu.AllocatePmTableSensorIndex(0x00621202, 54, SensorType.Voltage, next);
        int temperature = Amd17Cpu.AllocatePmTableSensorIndex(0x00621202, 11, SensorType.Temperature, next);
        int clock = Amd17Cpu.AllocatePmTableSensorIndex(0x00621202, 71, SensorType.Clock, next);

        Assert.Equal(1054, voltage);
        Assert.Equal(1011, temperature);
        Assert.Equal(1071, clock);
        Assert.Equal(2, next[SensorType.Voltage]);
        Assert.Equal(3, next[SensorType.Temperature]);
        Assert.Equal(3, next[SensorType.Clock]);
        Assert.True(temperature < Amd17Cpu.AdditionalCoreSensorIndex);
    }

    [Fact]
    public void ExistingPmTable_KeepsSequentialIndices()
    {
        var next = new Dictionary<SensorType, int> { [SensorType.Voltage] = 2 };

        Assert.Equal(2, Amd17Cpu.AllocatePmTableSensorIndex(0x00540004, 54, SensorType.Voltage, next));
        Assert.Equal(3, Amd17Cpu.AllocatePmTableSensorIndex(0x00540004, 58, SensorType.Voltage, next));
        Assert.Equal(4, next[SensorType.Voltage]);
    }
}
