// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class Logger
{
    private const string FileNameFormat = "LibreHardwareMonitorLog-{0:yyyy-MM-dd}{1}.csv";

    private readonly IComputer _computer;
    private readonly object _lock = new object();

    // Monotonic clock for the interval gate and gap detection; DateTime.Now is kept
    // only for the timestamp column text and the file-name date (DST-safe logging).
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private DateTime _day = DateTime.MinValue;
    private string _fileName;
    private string[] _identifiers;
    private ISensor[] _sensors;
    private TimeSpan? _lastLoggedElapsed;
    private TimeSpan? _lastTickElapsed;
    private uint _lastSessionNumber;
    private DateTime _sessionDate = DateTime.MinValue;

    public LoggerFileRotation FileRotationMethod = LoggerFileRotation.PerSession;

    public Logger(IComputer computer)
    {
        _computer = computer;
        _computer.HardwareAdded += HardwareAdded;
        _computer.HardwareRemoved += HardwareRemoved;
    }

    private void HardwareRemoved(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (ISensor sensor in hardware.Sensors)
            SensorRemoved(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareRemoved(subHardware);
    }

    private void HardwareAdded(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        lock (_lock)
        {
            if (_sensors == null || _identifiers == null)
                return;

            for (int i = 0; i < _sensors.Length; i++)
            {
                if (sensor.Identifier.ToString() == _identifiers[i])
                {
                    _sensors[i] = sensor;
                    break; // one sensor maps to one column; stop so a duplicate identifier can't fan it into several
                }
            }
        }
    }

    private void SensorRemoved(ISensor sensor)
    {
        lock (_lock)
        {
            if (_sensors == null)
                return;

            for (int i = 0; i < _sensors.Length; i++)
            {
                if (sensor == _sensors[i])
                    _sensors[i] = null;
            }
        }
    }

    private static string GetFileName(DateTime date, uint sessionNumber = 0)
    {
        return AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar
            + string.Format(FileNameFormat, date, sessionNumber == 0 ? "" : "-" + sessionNumber);
    }

    // Row timestamp format: the historical US-locale layout ("MM/dd/yyyy HH:mm:ss") with milliseconds
    // (.fff) appended. The general "G" specifier has no fractional-seconds field, so it collapsed every
    // sub-second sample onto a duplicate whole second (~25% of rows at faster-than-1 Hz logging),
    // losing their ordering and true sub-second position (GH #9). Only the formatting dropped the
    // resolution; DateTime.Now already carries it. The leading fields are byte-for-byte the legacy
    // form, so a consumer reading second-resolution timestamps still parses unchanged; the downstream
    // ThermalTrace parser also accepts this .fff form. Deliberate local-fork divergence from upstream's
    // second-resolution "G".
    internal const string RowTimestampFormat = "MM/dd/yyyy HH:mm:ss.fff";

    internal static string FormatRowTimestamp(DateTime timestamp)
    {
        return timestamp.ToString(RowTimestampFormat, CultureInfo.InvariantCulture);
    }

    private enum OpenLogResult
    {
        Opened,

        // The file could not be read right now (sharing violation etc.); retrying later may work.
        RetryLater,

        // The file was read but its first line is not a usable identifier header; retrying
        // cannot succeed and the file must not be reused (or truncated).
        BadHeader
    }

    private OpenLogResult TryOpenExistingLogFile()
    {
        if (!File.Exists(_fileName))
            return OpenLogResult.BadHeader;

        string line;
        try
        {
            using (StreamReader reader = new StreamReader(_fileName))
                line = reader.ReadLine();
        }
        catch
        {
            return OpenLogResult.RetryLater;
        }

        if (string.IsNullOrEmpty(line))
            return OpenLogResult.BadHeader;

        string[] identifiers = line.Split(',').Skip(1).ToArray();
        if (identifiers.Length == 0)
            return OpenLogResult.BadHeader;

        // Visit sensors without holding _lock: SensorAdded can fire while the update sweep
        // holds Computer's traversal lock, so holding _lock across VisitComputer would invert
        // the lock order.
        ISensor[] sensors = new ISensor[identifiers.Length];
        SensorVisitor visitor = new SensorVisitor(sensor =>
        {
            for (int i = 0; i < identifiers.Length; i++)
            {
                if (sensor.Identifier.ToString() == identifiers[i])
                {
                    sensors[i] = sensor;
                    break; // stop at the first column match so one sensor can't populate duplicate columns
                }
            }
        });
        visitor.VisitComputer(_computer);

        lock (_lock)
        {
            _identifiers = identifiers;
            _sensors = sensors;
        }

        return OpenLogResult.Opened;
    }

    private void CreateNewLogFile()
    {
        // Visitor runs outside _lock for the same lock-ordering reason as TryOpenExistingLogFile.
        IList<ISensor> list = new List<ISensor>();
        SensorVisitor visitor = new SensorVisitor(sensor =>
        {
            list.Add(sensor);
        });
        visitor.VisitComputer(_computer);
        ISensor[] sensors = list.ToArray();
        string[] identifiers = sensors.Select(s => s.Identifier.ToString()).ToArray();

        using (StreamWriter writer = new StreamWriter(_fileName, false))
        {
            writer.Write(",");
            for (int i = 0; i < sensors.Length; i++)
            {
                writer.Write(sensors[i].Identifier);
                if (i < sensors.Length - 1)
                    writer.Write(",");
                else
                    writer.WriteLine();
            }

            writer.Write("Time,");
            for (int i = 0; i < sensors.Length; i++)
            {
                writer.Write('"');
                writer.Write(sensors[i].Name.Replace("\"", "\"\""));
                writer.Write('"');
                if (i < sensors.Length - 1)
                    writer.Write(",");
                else
                    writer.WriteLine();
            }
        }

        // Publish only after the header was written, so a creation failure leaves the
        // previous column state intact.
        lock (_lock)
        {
            _sensors = sensors;
            _identifiers = identifiers;
        }
    }

    public TimeSpan LoggingInterval { get; set; }

    public void Log()
    {
        DateTime now = DateTime.Now;
        TimeSpan elapsed = _stopwatch.Elapsed;

        // The early-fire margin tolerates timer jitter without over-logging: half the observed
        // tick spacing (so sub-second update intervals cannot pass the gate twice per logging
        // interval), capped at 500 ms.
        TimeSpan tickSpacing = _lastTickElapsed.HasValue ? elapsed - _lastTickElapsed.Value : TimeSpan.Zero;
        _lastTickElapsed = elapsed;

        TimeSpan margin = TimeSpan.FromMilliseconds(Math.Min(500.0, Math.Max(0.0, tickSpacing.TotalMilliseconds / 2)));
        if (_lastLoggedElapsed.HasValue && elapsed - _lastLoggedElapsed.Value < LoggingInterval - margin)
            return;

        try
        {
            switch (FileRotationMethod)
            {
                case LoggerFileRotation.PerSession:
                    // Rotate only on a genuine session break: at least double the logging
                    // interval with a 30 s floor, so a slow update interval or a skipped
                    // tick does not spawn a new file. Also rotate when no column state exists
                    // yet (e.g. after switching from a Daily file this logger never opened).
                    TimeSpan gapThreshold = TimeSpan.FromMilliseconds(Math.Max(LoggingInterval.TotalMilliseconds * 2, 30000));
                    if (!File.Exists(_fileName) || _sensors == null || (_lastLoggedElapsed.HasValue && elapsed - _lastLoggedElapsed.Value > gapThreshold))
                    {
                        if (_sessionDate != now.Date)
                        {
                            _sessionDate = now.Date;
                            _lastSessionNumber = 0;
                        }

                        uint sessionNumber = _lastSessionNumber + 1;
                        do
                        {
                            _fileName = GetFileName(now, sessionNumber);
                            sessionNumber++;
                        } while (File.Exists(_fileName));

                        CreateNewLogFile();

                        // Commit the cache only after creation succeeded, so a failed attempt
                        // does not permanently inflate the session numbering.
                        _lastSessionNumber = sessionNumber - 1;
                    }
                    break;
                case LoggerFileRotation.Daily:
                    // Create a new file if the day has changed or the file does not exist
                    if (_day != now.Date || !File.Exists(_fileName))
                    {
                        _fileName = GetFileName(now.Date);
                        OpenLogResult result = TryOpenExistingLogFile();

                        if (result == OpenLogResult.RetryLater)
                        {
                            // Transient read failure: never truncate; retry at the logging
                            // interval rather than every update tick.
                            _lastLoggedElapsed = elapsed;
                            return;
                        }

                        if (result == OpenLogResult.BadHeader && File.Exists(_fileName) && new FileInfo(_fileName).Length > 0)
                        {
                            // The day file exists but will never parse (corrupt/foreign
                            // header). Preserve it and divert to the first usable suffixed
                            // day file instead of silently never logging again today.
                            for (uint suffix = 1; ; suffix++)
                            {
                                _fileName = GetFileName(now.Date, suffix);
                                if (!File.Exists(_fileName))
                                {
                                    CreateNewLogFile();
                                    break;
                                }

                                OpenLogResult suffixResult = TryOpenExistingLogFile();
                                if (suffixResult == OpenLogResult.Opened)
                                    break;

                                if (suffixResult == OpenLogResult.RetryLater)
                                {
                                    _lastLoggedElapsed = elapsed;
                                    return;
                                }
                            }
                        }
                        else if (result != OpenLogResult.Opened)
                        {
                            CreateNewLogFile();
                        }

                        _day = now.Date;
                    }
                    break;
            }

            StringBuilder row = new StringBuilder();
            row.Append(FormatRowTimestamp(now));
            row.Append(',');

            lock (_lock)
            {
                if (_sensors == null)
                    return;

                for (int i = 0; i < _sensors.Length; i++)
                {
                    ISensor sensor = _sensors[i];
                    if (sensor != null)
                    {
                        float? value = sensor.Value;
                        if (value.HasValue)
                            row.Append(value.Value.ToString("R", CultureInfo.InvariantCulture));
                    }

                    if (i < _sensors.Length - 1)
                        row.Append(',');
                }
            }

            row.Append(Environment.NewLine);

            using (StreamWriter writer = new StreamWriter(new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
            {
                // Single write so a mid-row failure can never leave a torn line
                writer.Write(row.ToString());
            }

            _lastLoggedElapsed = elapsed;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Advance the gate so a persistent failure cannot retry every tick
            // or be mistaken for a session gap.
            _lastLoggedElapsed = elapsed;
            Debug.WriteLine(e);
        }
    }
}
