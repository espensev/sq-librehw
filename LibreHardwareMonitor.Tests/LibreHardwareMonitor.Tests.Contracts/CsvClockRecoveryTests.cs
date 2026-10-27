// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class CsvClockRecoveryTests : IDisposable
{
    private readonly string _testBase = Path.Combine(Path.GetTempPath(), nameof(CsvClockRecoveryTests));
    private readonly string _directory;

    public CsvClockRecoveryTests()
    {
        _directory = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void FirstRow_RefreshesAnAlreadyStaleZone()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 7, 0, 0, 123, DateTimeKind.Utc), -4)
        {
            SystemOffsetHours = 2
        };
        Logger logger = CreateLogger(clock);

        logger.Log();

        Assert.Equal("09/28/2026 09:00:00.123,42", Rows("2026-09-28").Single());
    }

    [Theory]
    [InlineData(6, "09/28/2026 18:01:00.000,42")]
    [InlineData(-6, "09/28/2026 06:01:00.000,42")]
    public void RunningCollector_RecoversEitherDirectionWithoutRewritingRows(int offset, string expectedRow)
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc), 0);
        Logger logger = CreateLogger(clock);
        logger.Log();
        clock.SystemOffsetHours = offset;
        for (int i = 1; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            logger.Log();
        }
        byte[] before = File.ReadAllBytes(DayFile("2026-09-28"));

        clock.Advance(TimeSpan.FromSeconds(1));
        logger.Log();

        Assert.Equal(expectedRow, Rows("2026-09-28").Last());
        Assert.Equal(before, File.ReadAllBytes(DayFile("2026-09-28")).Take(before.Length).ToArray());
        Assert.Equal(61, Rows("2026-09-28").Length);
        Assert.Equal(2, clock.RefreshCount); // No process-wide cache flush on every fast logging tick.
    }

    [Fact]
    public void DailyRollover_UsesCorrectedDateAndTimestampTogether()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc), -4);
        Logger logger = CreateLogger(clock);
        logger.Log();
        byte[] previousDay = File.ReadAllBytes(DayFile("2026-09-27"));
        clock.SystemOffsetHours = 2;
        clock.Advance(TimeSpan.FromMinutes(1));

        logger.Log();

        Assert.Equal("09/28/2026 03:01:00.000,42", Rows("2026-09-28").Single());
        Assert.Equal(previousDay, File.ReadAllBytes(DayFile("2026-09-27")));
    }

    [Fact]
    public void BackwardDateCorrection_AppendsToExistingDayWithoutTruncation()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc), 2);
        Logger logger = CreateLogger(clock);
        string priorDay = ",/fixture/temperature/0\r\nTime,\"Fixture\"\r\n09/27/2026 20:00:00.000,41\r\n";
        File.WriteAllText(DayFile("2026-09-27"), priorDay);
        logger.Log();
        byte[] laterDay = File.ReadAllBytes(DayFile("2026-09-28"));
        clock.SystemOffsetHours = -4;
        clock.Advance(TimeSpan.FromMinutes(1));

        logger.Log();

        Assert.StartsWith(priorDay, File.ReadAllText(DayFile("2026-09-27")));
        Assert.Equal("09/27/2026 21:01:00.000,42", Rows("2026-09-27").Last());
        Assert.Equal(laterDay, File.ReadAllBytes(DayFile("2026-09-28")));
    }

    [Fact]
    public void LongLoggingInterval_RefreshesBeforeNextDueRowAfterIdle()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc), 0);
        Logger logger = CreateLogger(clock);
        logger.LoggingInterval = TimeSpan.FromMinutes(5);
        logger.Log();
        clock.SystemOffsetHours = 6;
        clock.Advance(TimeSpan.FromMinutes(1));
        logger.Log();
        Assert.Single(Rows("2026-09-28"));
        Assert.Equal(1, clock.RefreshCount);
        clock.Advance(TimeSpan.FromMinutes(4));

        logger.Log();

        Assert.Equal("09/28/2026 18:05:00.000,42", Rows("2026-09-28").Last());
        Assert.Equal(2, Rows("2026-09-28").Length);
        Assert.Equal(2, clock.RefreshCount);
    }

    [Fact]
    public void BackwardWallClockJump_DoesNotChangeMonotonicLoggingCadence()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc), 0);
        Logger logger = CreateLogger(clock);
        logger.LoggingInterval = TimeSpan.FromSeconds(10);
        logger.Log();
        clock.UtcNow = clock.UtcNow.AddHours(-6);
        clock.Advance(TimeSpan.FromSeconds(1));
        logger.Log();
        clock.Advance(TimeSpan.FromSeconds(8));
        logger.Log();
        Assert.Single(Rows("2026-09-28"));
        clock.Advance(TimeSpan.FromSeconds(1));

        logger.Log();

        Assert.Equal(new[] { "09/28/2026 12:00:00.000,42", "09/28/2026 06:00:10.000,42" }, Rows("2026-09-28"));
    }

    [Fact]
    public void ContinuousPerSessionLog_DoesNotRotateForZoneChangeAcrossMidnight()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 20, 0, 0, DateTimeKind.Utc), 0);
        Logger logger = CreateLogger(clock, LoggerFileRotation.PerSession);
        logger.Log();
        clock.SystemOffsetHours = 6;
        for (int i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            logger.Log();
        }

        string file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal("LibreHardwareMonitorLog-2026-09-28-1.csv", Path.GetFileName(file));
        Assert.Equal("09/29/2026 02:01:00.000,42", File.ReadAllLines(file).Last());
        Assert.Equal(63, File.ReadAllLines(file).Length);
    }

    [Fact]
    public void PerSessionAfterLongPause_UsesRefreshedDateForNewSession()
    {
        TestClock clock = new(new DateTime(2026, 9, 28, 20, 0, 0, DateTimeKind.Utc), 0);
        Logger logger = CreateLogger(clock, LoggerFileRotation.PerSession);
        logger.Log();
        clock.SystemOffsetHours = 2;
        clock.Advance(TimeSpan.FromHours(4));

        logger.Log();

        string nextSession = Path.Combine(_directory, "LibreHardwareMonitorLog-2026-09-29-1.csv");
        Assert.Equal("09/29/2026 02:00:00.000,42", File.ReadAllLines(nextSession).Last());
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    private Logger CreateLogger(TestClock clock, LoggerFileRotation rotation = LoggerFileRotation.Daily) =>
        new(new FixtureComputer(), _directory, clock)
        {
            FileRotationMethod = rotation,
            LoggingInterval = TimeSpan.FromSeconds(1)
        };

    private string DayFile(string date) => Path.Combine(_directory, $"LibreHardwareMonitorLog-{date}.csv");

    private string[] Rows(string date) => File.ReadAllLines(DayFile(date)).Skip(2).ToArray();

    public void Dispose()
    {
        string path = Path.GetFullPath(_directory);
        string prefix = Path.GetFullPath(_testBase) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture path escaped its temporary root.");
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class TestClock : ILoggerClock
    {
        private int _cachedOffsetHours;

        internal TestClock(DateTime utcNow, int offsetHours)
        {
            UtcNow = utcNow;
            SystemOffsetHours = _cachedOffsetHours = offsetHours;
        }

        internal DateTime UtcNow { get; set; }
        internal int SystemOffsetHours { get; set; }
        internal int RefreshCount { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public DateTime LocalNow => DateTime.SpecifyKind(UtcNow.AddHours(_cachedOffsetHours), DateTimeKind.Local);
        public void RefreshTimeZone()
        {
            _cachedOffsetHours = SystemOffsetHours;
            RefreshCount++;
        }
        internal void Advance(TimeSpan elapsed)
        {
            Elapsed += elapsed;
            UtcNow += elapsed;
        }
    }

    private sealed class FixtureComputer : IComputer
    {
        public event HardwareEventHandler HardwareAdded { add { } remove { } }
        public event HardwareEventHandler HardwareRemoved { add { } remove { } }
        public IList<IHardware> Hardware { get; } = new IHardware[] { new FixtureHardware() };
        public bool IsBatteryEnabled => false;
        public bool IsControllerEnabled => false;
        public bool IsCpuEnabled => false;
        public bool IsGpuEnabled => false;
        public bool IsPowerMonitorEnabled => false;
        public bool IsMemoryEnabled => false;
        public bool IsMotherboardEnabled => false;
        public bool IsNetworkEnabled => false;
        public bool IsPsuEnabled => false;
        public bool IsStorageEnabled => false;
        public string GetReport() => string.Empty;
        public void Accept(IVisitor visitor) => visitor.VisitComputer(this);
        public void Traverse(IVisitor visitor)
        {
            foreach (IHardware hardware in Hardware)
                hardware.Accept(visitor);
        }
    }

    private sealed class FixtureHardware : LibreHardwareMonitor.Hardware.Hardware
    {
        internal FixtureHardware() : base("Fixture", new Identifier("fixture"), new FixtureSettings())
        {
            Sensor sensor = new("Fixture", 0, SensorType.Temperature, this, new FixtureSettings()) { Value = 42 };
            ActivateSensor(sensor);
        }
        public override HardwareType HardwareType => HardwareType.Cpu;
        public override void Update() { }
    }

    private sealed class FixtureSettings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void SetValue(string name, string value) { }
        public void Remove(string name) { }
    }
}
