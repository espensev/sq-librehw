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
    private readonly string _logDirectory;
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
        : this(computer, RuntimePaths.Current.LogDirectory)
    { }

    internal Logger(IComputer computer, string logDirectory)
    {
        _computer = computer ?? throw new ArgumentNullException(nameof(computer));
        if (string.IsNullOrWhiteSpace(logDirectory) || !Path.IsPathRooted(logDirectory))
            throw new ArgumentException("The log directory must be an absolute path.", nameof(logDirectory));

        _logDirectory = Path.GetFullPath(logDirectory);
        RuntimePaths.EnsureSafeMutableDirectoryCreationPath(
            _logDirectory,
            "The log directory");
        Directory.CreateDirectory(_logDirectory);
        RuntimePaths.EnsureSafeMutableDirectory(_logDirectory, "The log directory");
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

    private string GetFileName(DateTime date, uint sessionNumber = 0)
    {
        string fileName = Path.Combine(
            _logDirectory,
            string.Format(FileNameFormat, date, sessionNumber == 0 ? "" : "-" + sessionNumber));
        RuntimePaths.EnsureSafeMutableDirectory(_logDirectory, "The log directory");
        RuntimePaths.EnsureSafeMutableFile(fileName, "The log file");
        return fileName;
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

    // Minimum decimal places kept per sensor unit before the value is written to the CSV.
    //
    // The round-trip "R" specifier emits the shortest string that reproduces the exact binary
    // float, which is far more precision than any of this hardware resolves: a temperature read
    // as 37.771072 °C or a fan at 1250.4471 RPM is measurement noise past the second decimal.
    // Those trailing digits are effectively random, so they compress badly. Replaying all 9,173
    // rows of a real 424-column day (2026-08-04, SND-HOST) through this exact method reduced raw
    // bytes from 19,327,926 to 16,023,769 (17.10%) and the identically compressed archive from
    // 3,974,710 to 2,732,768 bytes (31.25%), without formatting any non-zero sample as zero.
    //
    // Voltage, Current, Factor and Timing keep three places. Their existing native presentation
    // contracts already retain that resolution, and dropping to two would quantise them visibly.
    // Every other current sensor type keeps at least two decimal places.
    //
    // Fixed decimal places alone are unsafe for scaled or near-zero telemetry. Data is expressed
    // in GB, so 0.01 GB is roughly 10.7 MB, and real NIC counters around 0.0017 GB would become
    // zero. Small Load and TemperatureRate values have the same problem. Keep at least four
    // significant digits by increasing the decimal count for magnitudes below the unit minimum.
    // On a 600-row sample of the 424-column SND-HOST log, the former fixed two-place policy would
    // have turned 35% of non-zero Data samples and 12% of non-zero TemperatureRate samples into
    // zero. The significant-digit floor preserves those values while still removing noisy float
    // tails from normal-magnitude readings.
    private const int DefaultValueDecimals = 2;
    private const int FineValueDecimals = 3;
    private const int MinimumSignificantDigits = 4;
    private const int MaximumValueDecimals = 15;

    internal static int? GetMinimumValueDecimals(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Voltage or
            SensorType.Current or
            SensorType.Factor or
            SensorType.Timing => FineValueDecimals,
            SensorType.Power or
            SensorType.Clock or
            SensorType.Temperature or
            SensorType.Load or
            SensorType.Frequency or
            SensorType.Fan or
            SensorType.Flow or
            SensorType.Control or
            SensorType.Level or
            SensorType.Data or
            SensorType.SmallData or
            SensorType.Throughput or
            SensorType.TimeSpan or
            SensorType.Energy or
            SensorType.Noise or
            SensorType.Conductivity or
            SensorType.Humidity or
            SensorType.TemperatureRate => DefaultValueDecimals,
            // Preserve a future or unknown sensor type losslessly until its precision contract is
            // explicitly chosen. The exhaustive test over SensorType makes additions visible in CI.
            _ => null
        };
    }

    internal static int? GetValueDecimals(float value, SensorType sensorType)
    {
        int? minimumDecimals = GetMinimumValueDecimals(sensorType);
        if (!minimumDecimals.HasValue)
            return null;

        if (float.IsNaN(value) || float.IsInfinity(value) || value == 0f)
            return minimumDecimals;

        double magnitude = Math.Floor(Math.Log10(Math.Abs((double)value)));
        double significantDecimals = MinimumSignificantDigits - 1 - magnitude;
        if (significantDecimals <= minimumDecimals.Value)
            return minimumDecimals;

        // Math.Round accepts no more than 15 decimal places. If four significant digits need
        // more than that, retain the original float instead of applying a coarse 15-place round.
        return significantDecimals > MaximumValueDecimals
            ? null
            : (int)significantDecimals;
    }

    // Round first, then still format with "R" rather than with "F2"/"F3".
    //
    // A fixed-point specifier expands large magnitudes instead of keeping the compact exponent
    // form: float.MaxValue under "F2" is a 41-character digit run, and this machine already logs
    // Throughput values around 3.5e9, which "F2" would write as 3497850112.00. Rounding to the
    // target precision and letting "R" pick the shortest representation keeps normal values short
    // (37.77), leaves genuine extremes in exponent form, and preserves NaN/Infinity unchanged.
    //
    // Rounding goes through double because MathF is not available on net472, which this project
    // still targets. The input float converts exactly to double; the rounded result is then
    // narrowed back to the float value that "R" will serialize. Midpoint-to-even is an explicit,
    // stable policy for the new decimal compaction step.
    //
    // NaN and both infinities fall through untouched: Math.Round returns them unchanged and the
    // equality test below is false for NaN, so they still reach "R" and print as before.
    //
    // Unknown future sensor types stay on the historical lossless "R" path. A finite non-zero
    // value needing more than Math.Round's 15-decimal limit to retain four significant digits is
    // also written losslessly. The zero fallback is defense in depth, while a genuine negative
    // zero is normalized to "0".
    internal static string FormatRowValue(float value, SensorType sensorType)
    {
        if (value == 0f)
            return "0";

        int? decimals = GetValueDecimals(value, sensorType);
        if (!decimals.HasValue)
            return value.ToString("R", CultureInfo.InvariantCulture);

        float rounded = (float)Math.Round(
            (double)value,
            decimals.Value,
            MidpointRounding.ToEven);
        if (rounded == 0f && value != 0f)
            return value.ToString("R", CultureInfo.InvariantCulture);

        return rounded.ToString("R", CultureInfo.InvariantCulture);
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
        RuntimePaths.EnsureSafeMutableDirectory(_logDirectory, "The log directory");
        RuntimePaths.EnsureSafeMutableFile(_fileName, "The log file");
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
        RuntimePaths.EnsureSafeMutableDirectory(_logDirectory, "The log directory");
        RuntimePaths.EnsureSafeMutableFile(_fileName, "The log file");
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
                            row.Append(FormatRowValue(value.Value, sensor.SensorType));
                    }

                    if (i < _sensors.Length - 1)
                        row.Append(',');
                }
            }

            row.Append(Environment.NewLine);

            RuntimePaths.EnsureSafeMutableDirectory(_logDirectory, "The log directory");
            RuntimePaths.EnsureSafeMutableFile(_fileName, "The log file");
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
