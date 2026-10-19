// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Cpu;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class AmdPmTableFreshnessTests
{
    [Fact]
    public void Aggregate_MissingOneCoreIsUnavailableInsteadOfAveragingMissingVoltageAsZero()
    {
        var fixture = new PmTableFixture(twoCores: true);
        fixture.Update(1.2f, 42);
        Assert.Equal(1.2f, fixture.CoreVids.Value);

        fixture.FailSecondCoreVoltage();

        Assert.Equal(1.25d, fixture.CoreVoltage);
        Assert.Equal(52f, fixture.Temperature.Value);
        Assert.Null(fixture.CoreVids.Value);
        Assert.Equal(1.2f, fixture.CoreVids.Min);
        Assert.Equal(1.2f, fixture.CoreVids.Max);

        fixture.Update(1.3f, 60);
        Assert.Equal(1.3f, fixture.CoreVids.Value);
        Assert.Equal(1.2f, fixture.CoreVids.Min);
        Assert.Equal(1.3f, fixture.CoreVids.Max);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("short")]
    [InlineData("unknown-version")]
    [InlineData("nan")]
    [InlineData("out-of-range")]
    public void CoreAndAggregate_InvalidateMissingPmSamplesAndRecoverWithoutChangingExtrema(string failure)
    {
        var fixture = new PmTableFixture();
        fixture.Update(1.2f, 42);
        fixture.Update(1.3f, 60);
        Assert.Equal(60f, fixture.Temperature.Value);
        Assert.Equal(1.3f, fixture.CoreVids.Value);

        fixture.Fail(failure);

        Assert.Null(fixture.Temperature.Value);
        Assert.Equal(0d, fixture.CoreVoltage);
        Assert.Null(fixture.CoreVids.Value);
        Assert.Equal(42f, fixture.Temperature.Min);
        Assert.Equal(60f, fixture.Temperature.Max);
        Assert.Equal(1.2f, fixture.CoreVids.Min);
        Assert.Equal(1.3f, fixture.CoreVids.Max);

        fixture.Update(1.25f, 52);

        Assert.Equal(52f, fixture.Temperature.Value);
        Assert.Equal(1.25d, fixture.CoreVoltage);
        Assert.Equal(1.25f, fixture.CoreVids.Value);
        Assert.Equal(42f, fixture.Temperature.Min);
        Assert.Equal(60f, fixture.Temperature.Max);
        Assert.Equal(1.2f, fixture.CoreVids.Min);
        Assert.Equal(1.3f, fixture.CoreVids.Max);
    }

    private sealed class PmTableFixture
    {
        private const uint SupportedVersion = 0x00621202;
        private readonly object _processor;
        private readonly object _core;
        private readonly object _secondCore;
        private readonly RyzenSMU _smu;

        internal PmTableFixture(bool twoCores = false)
        {
            // Bypass hardware-owning constructors; execute the production table and
            // aggregate methods against real Sensor instances with in-memory inputs.
            var cpu = (Amd17Cpu)RuntimeHelpers.GetUninitializedObject(typeof(Amd17Cpu));
            var settings = new TestSettings();
            SetField(cpu, "<Identifier>k__BackingField", new Identifier("amdcpu", "fixture"));
            SetField(cpu, "_active", new HashSet<ISensor>());
            _smu = (RyzenSMU)RuntimeHelpers.GetUninitializedObject(typeof(RyzenSMU));
            SetField(cpu, "_smu", _smu);

            Type processorType = typeof(Amd17Cpu).GetNestedType("Processor", BindingFlags.NonPublic);
            Type coreType = typeof(Amd17Cpu).GetNestedType("Core", BindingFlags.NonPublic);
            Type nodeType = typeof(Amd17Cpu).GetNestedType("NumaNode", BindingFlags.NonPublic);
            _processor = RuntimeHelpers.GetUninitializedObject(processorType);
            _core = RuntimeHelpers.GetUninitializedObject(coreType);
            SetField(cpu, "_processor", _processor);
            SetField(_processor, "_cpu", cpu);
            SetField(_core, "_cpu", cpu);
            SetField(_core, "<CoreId>k__BackingField", 1);

            Temperature = new Sensor("Core #1", 2001, SensorType.Temperature, cpu, settings);
            CoreVids = new Sensor("Core VIDs", 2000, SensorType.Voltage, cpu, settings);
            SetField(_core, "_temperature", Temperature);
            SetField(_processor, "_coreVids", CoreVids);
            SetField(_processor, "_avgClock", new Sensor("Average", 0, SensorType.Clock, cpu, settings));
            SetField(_processor, "_avgClockEffective", new Sensor("Effective", 1, SensorType.Clock, cpu, settings));

            object node = Activator.CreateInstance(nodeType, new object[] { cpu, 0 });
            var cores = (IList)nodeType.GetProperty("Cores").GetValue(node);
            cores.Add(_core);
            if (twoCores)
            {
                _secondCore = RuntimeHelpers.GetUninitializedObject(coreType);
                SetField(_secondCore, "_cpu", cpu);
                SetField(_secondCore, "<CoreId>k__BackingField", 2);
                SetField(_secondCore, "_temperature", new Sensor("Core #2", 2002, SensorType.Temperature, cpu, settings));
                cores.Add(_secondCore);
            }
            var nodes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nodeType));
            nodes.Add(node);
            SetField(_processor, "<Nodes>k__BackingField", nodes);
        }

        internal Sensor Temperature { get; }
        internal Sensor CoreVids { get; }
        internal double CoreVoltage => (double)_core.GetType().GetProperty("CoreVoltage").GetValue(_core);

        internal void Update(float voltage, float temperature)
        {
            SetField(_smu, "_pmTableVersion", SupportedVersion);
            var table = new float[335];
            table[317] = voltage;
            table[333] = temperature;
            table[318] = voltage;
            table[334] = temperature;
            Apply(table);
        }

        internal void FailSecondCoreVoltage()
        {
            var table = new float[335];
            table[317] = 1.25f;
            table[318] = float.NaN;
            table[333] = table[334] = 52f;
            Apply(table);
        }

        internal void Fail(string failure)
        {
            switch (failure)
            {
                case "empty":
                    Apply(Array.Empty<float>());
                    break;
                case "short":
                    Apply(new float[317]);
                    break;
                case "unknown-version":
                    SetField(_smu, "_pmTableVersion", 0u);
                    Apply(new float[334]);
                    break;
                case "nan":
                    Update(float.NaN, float.NaN);
                    break;
                case "out-of-range":
                    Update(3f, 125f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        private void Apply(float[] table)
        {
            SetField(_processor, "_pmTable", table);
            _core.GetType().GetMethod("UpdatePmTableSensors", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_core, null);
            if (_secondCore != null)
                _secondCore.GetType().GetMethod("UpdatePmTableSensors", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_secondCore, null);
            _processor.GetType().GetMethod("UpdateVirtualSensor").Invoke(_processor, null);
        }
    }

    private static void SetField(object instance, string name, object value)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                continue;
            field.SetValue(instance, value);
            return;
        }

        throw new MissingFieldException(instance.GetType().FullName, name);
    }

    private sealed class TestSettings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void Remove(string name) { }
        public void SetValue(string name, string value) { }
    }
}
