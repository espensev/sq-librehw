// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class UpdateVisitorTests
{
    [Fact]
    public void FailedParentAndChildDoNotStarveHealthyChildrenSiblingsOrLaterDevices()
    {
        var parent = new TestHardware("parent") { Fail = true };
        var failedChild = new TestHardware("failed-child") { Fail = true };
        var healthyChild = new TestHardware("healthy-child");
        parent.Children = [failedChild, healthyChild];
        var later = new TestHardware("later");
        Computer computer = CreateComputer(parent, later);
        var visitor = new UpdateVisitor();

        try
        {
            computer.Accept(visitor);
            computer.Accept(visitor);

            Assert.Equal(2, parent.Updates);
            Assert.Equal(2, failedChild.Updates);
            Assert.Equal(2, healthyChild.Updates);
            Assert.Equal(2, later.Updates);
            Assert.Null(parent.Reading.Value);
            Assert.Null(failedChild.Reading.Value);
            Assert.Equal(42, healthyChild.Reading.Value);
            Assert.Equal(42, later.Reading.Value);
        }
        finally
        {
            computer.Close();
        }
    }

    [Fact]
    public void PartialFailureClearsCurrentValuesPreservesHistoryAndRecoversWithoutReplacingSensors()
    {
        var hardware = new TestHardware("partial");
        hardware.Now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 4; i++)
            hardware.Reading.Value = 10;
        SensorValue[] history = hardware.Reading.Values.ToArray();
        Assert.Single(history);
        string identifier = hardware.Reading.Identifier.ToString();
        hardware.Fail = true;
        var visitor = new UpdateVisitor();

        visitor.VisitHardware(hardware);

        Assert.Null(hardware.Reading.Value);
        Assert.Equal(10, hardware.Reading.Min);
        Assert.Equal(42, hardware.Reading.Max);
        Assert.Equal(history, hardware.Reading.Values);
        hardware.Fail = false;
        hardware.NextValue = 20;
        hardware.Now = hardware.Now.AddSeconds(2);
        for (int i = 0; i < 3; i++)
            visitor.VisitHardware(hardware);
        Assert.Single(hardware.Reading.Values);
        visitor.VisitHardware(hardware);

        Assert.Equal(20, hardware.Reading.Value);
        Assert.Equal(identifier, hardware.Reading.Identifier.ToString());
        Assert.Equal(new float[] { 10, 20 }, hardware.Reading.Values.Select(value => value.Value));
    }

    [Fact]
    public void FailedPollStillWritesCsvAndRecoveryUsesTheSameColumns()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LhmPoll-" + Guid.NewGuid().ToString("N"));
        var failed = new TestHardware("failed");
        var healthy = new TestHardware("healthy");
        Computer computer = CreateComputer(failed, healthy);
        try
        {
            var logger = new Logger(computer, directory) { LoggingInterval = TimeSpan.Zero };
            var visitor = new UpdateVisitor();
            computer.Accept(visitor);
            logger.Log();
            failed.Fail = true;
            computer.Accept(visitor);
            logger.Log();
            computer.Accept(visitor);
            logger.Log();
            failed.Fail = false;
            computer.Accept(visitor);
            logger.Log();

            string[][] rows = File.ReadAllLines(Assert.Single(Directory.GetFiles(directory, "*.csv")))
                .Select(row => row.Split(',')).ToArray();
            Assert.Equal(6, rows.Length);
            int failedColumn = Array.IndexOf(rows[0], failed.Reading.Identifier.ToString());
            int healthyColumn = Array.IndexOf(rows[0], healthy.Reading.Identifier.ToString());
            Assert.True(failedColumn > 0 && healthyColumn > 0);
            Assert.Equal(new[] { "42", "", "", "42" }, rows.Skip(2).Select(row => row[failedColumn]));
            Assert.All(rows.Skip(2), row => Assert.Equal("42", row[healthyColumn]));
        }
        finally
        {
            computer.Close();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FatalFailuresStillPropagate()
    {
        var hardware = new TestHardware("fatal") { Failure = new OutOfMemoryException("fixture") };
        Assert.Throws<OutOfMemoryException>(() => new UpdateVisitor().VisitHardware(hardware));
    }

    [Fact]
    public void RateSensorDiscardsFailedPollSamplesAndRequiresFreshWarmup()
    {
        var hardware = new TestHardware("rate");
        var rate = new TemperatureRateSensor("Rate", 0, hardware, new PersistentSettings(), () => hardware.Now);
        hardware.Add(rate);
        hardware.OnUpdate = () => rate.Observe(hardware.NextValue);
        var visitor = new UpdateVisitor();
        for (int i = 0; i < 3; i++)
        {
            hardware.NextValue = 40 + i;
            visitor.VisitHardware(hardware);
            hardware.Now = hardware.Now.AddSeconds(1);
        }
        Assert.Equal(1, rate.Value);
        hardware.NextValue = 43;
        hardware.Fail = true;
        visitor.VisitHardware(hardware);
        Assert.Null(rate.Value);
        Assert.Equal(0, rate.SampleCount);
        Assert.Equal(1, rate.Min);
        hardware.Fail = false;
        for (int i = 0; i < 3; i++)
        {
            hardware.NextValue = 60 + (2 * i);
            hardware.Now = hardware.Now.AddSeconds(1);
            visitor.VisitHardware(hardware);
            if (i < 2)
                Assert.Null(rate.Value);
        }
        Assert.Equal(2, rate.Value);
    }

    [Fact]
    public void FailureDiagnosticsRetainOriginalExceptionAndCannotStarveLaterDevices()
    {
        var failure = new IOException("read failed");
        var hardware = new TestHardware("failed") { Failure = failure };
        var later = new TestHardware("later");
        Exception reported = null;
        var visitor = new UpdateVisitor((device, exception) =>
        {
            Assert.Same(hardware, device);
            reported = exception;
            throw new InvalidOperationException("diagnostic sink failed");
        });
        Computer computer = CreateComputer(hardware, later);
        try
        {
            computer.Accept(visitor);
            Assert.Same(failure, reported);
            Assert.Equal(1, later.Updates);
            Assert.Null(hardware.Reading.Value);
        }
        finally
        {
            computer.Close();
        }
    }

    [Fact]
    public void InvalidationFailureCannotStarveChildUpdates()
    {
        var hardware = new TestHardware("failed") { Fail = true, FailSensorEnumeration = true };
        var child = new TestHardware("child");
        hardware.Children = [child];
        var reported = new System.Collections.Generic.List<Exception>();

        new UpdateVisitor((_, exception) => reported.Add(exception)).VisitHardware(hardware);

        Assert.Equal(1, child.Updates);
        Assert.Contains(reported, exception => exception is IOException);
        Assert.Contains(reported, exception => exception is InvalidOperationException);
    }

    private static Computer CreateComputer(params IHardware[] hardware)
    {
        var dependencies = new Computer.OpenDependencies(
            () => null, () => { }, () => { }, () => { }, () => { },
            add => add(new TestGroup(hardware)));
        var computer = new Computer(new PersistentSettings(), dependencies);
        computer.Open();
        return computer;
    }

    private sealed class TestGroup(IHardware[] hardware) : IGroup
    {
        public System.Collections.Generic.IReadOnlyList<IHardware> Hardware => hardware;
        public string GetReport() => null;
        public void Close() { }
    }

    private sealed class TestHardware : HardwareBase
    {
        internal TestHardware(string name) : base(name, new Identifier(name), new PersistentSettings())
        {
            Reading = new Sensor("Reading", 0, false, SensorType.Temperature, this, null, _settings, false, () => Now);
            ActivateSensor(Reading);
        }

        internal Sensor Reading { get; }
        internal DateTime Now { get; set; } = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal bool Fail { get; set; }
        internal bool FailSensorEnumeration { get; set; }
        internal Exception Failure { get; set; }
        internal Action OnUpdate { get; set; }
        internal float NextValue { get; set; } = 42;
        internal int Updates { get; private set; }
        internal IHardware[] Children { get; set; } = [];
        public override HardwareType HardwareType => HardwareType.Cpu;
        public override IHardware[] SubHardware => Children;
        public override ISensor[] Sensors => FailSensorEnumeration ? throw new InvalidOperationException("sensor enumeration failed") : base.Sensors;
        internal void Add(ISensor sensor) => ActivateSensor(sensor);

        public override void Update()
        {
            Updates++;
            Reading.Value = NextValue;
            OnUpdate?.Invoke();
            if (Failure != null)
                throw Failure;
            if (Fail)
                throw new IOException("fixture read failure after partial update");
        }
    }
}
