// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace LibreHardwareMonitor.Hardware;

internal class Sensor : ISensor
{
    // Upper bound for the base64 payload persisted per sensor under "<sensor>/values".
    // Keep in sync with PersistentSettings.MaxSensorValuesLength in the Windows Forms app,
    // which drops any larger entry so sensor history cannot bloat the settings file.
    internal const int MaxPersistedValuesLength = 64 * 1024;

    private readonly string _defaultName;
    private readonly Hardware _hardware;
    private readonly ISettings _settings;
    private readonly bool _trackMinMax;
    private readonly List<SensorValue> _values = new();
    private readonly object _valuesLock = new();
    private int _count;
    private float? _currentValue;
    private string _name;
    private float _sum;
    private SensorValue[] _valuesSnapshot;
    private TimeSpan _valuesTimeWindow = TimeSpan.FromDays(1.0);

    public Sensor(string name, int index, SensorType sensorType, Hardware hardware, ISettings settings) :
        this(name, index, sensorType, hardware, null, settings)
    { }

    public Sensor(string name, int index, SensorType sensorType, Hardware hardware, ParameterDescription[] parameterDescriptions, ISettings settings) :
        this(name, index, false, sensorType, hardware, parameterDescriptions, settings)
    { }

    public Sensor
    (
        string name,
        int index,
        bool defaultHidden,
        SensorType sensorType,
        Hardware hardware,
        ParameterDescription[] parameterDescriptions,
        ISettings settings,
        bool disableHistory = false)
    {
        Index = index;
        IsDefaultHidden = defaultHidden;
        SensorType = sensorType;
        _hardware = hardware;

        Parameter[] parameters = new Parameter[parameterDescriptions?.Length ?? 0];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameterDescriptions != null)
                parameters[i] = new Parameter(parameterDescriptions[i], this, settings);
        }

        Parameters = parameters;

        _settings = settings;
        _defaultName = name;
        _name = settings.GetValue(new Identifier(Identifier, "name").ToString(), name);
        _trackMinMax = !disableHistory;
        if (disableHistory)
        {
            _valuesTimeWindow = TimeSpan.Zero;
        }

        GetSensorValuesFromSettings();

        hardware.Closing += delegate { SetSensorValuesToSettings(); };
    }

    public IControl Control { get; internal set; }

    public IHardware Hardware
    {
        get { return _hardware; }
    }

    public Identifier Identifier => field ??= new Identifier(_hardware.Identifier, SensorType.ToString().ToLowerInvariant(), Index.ToString(CultureInfo.InvariantCulture));

    public int Index { get; }

    public bool IsDefaultHidden { get; }

    public float? Max { get; private set; }

    public float? Min { get; private set; }

    public string Name
    {
        get { return _name; }
        set
        {
            _name = !string.IsNullOrEmpty(value) ? value : _defaultName;

            _settings.SetValue(new Identifier(Identifier, "name").ToString(), _name);
        }
    }

    public IReadOnlyList<IParameter> Parameters { get; }

    public SensorType SensorType { get; }

    public virtual float? Value
    {
        get { return _currentValue; }
        set
        {
            if (_valuesTimeWindow != TimeSpan.Zero)
            {
                DateTime now = DateTime.UtcNow;

                lock (_valuesLock)
                {
                    int expiredCount = 0;
                    while (expiredCount < _values.Count && now - _values[expiredCount].Time > _valuesTimeWindow)
                        expiredCount++;

                    if (expiredCount > 0)
                    {
                        _values.RemoveRange(0, expiredCount);
                        _valuesSnapshot = null;
                    }

                    if (value.HasValue)
                    {
                        if (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
                        {
                            _sum += value.Value;
                            _count++;
                            if (_count == 4)
                            {
                                AppendValue(_sum / _count, now);
                                _sum = 0;
                                _count = 0;
                                _valuesSnapshot = null;
                            }
                        }
                        else
                        {
                            // A non-finite reading marks a sensor dropout: discard the partial
                            // bucket so pre-dropout samples never blend into the first average
                            // produced after the sensor recovers.
                            _sum = 0;
                            _count = 0;
                        }
                    }
                }
            }

            _currentValue = value;
            if (_trackMinMax && value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
            {
                lock (_valuesLock)
                {
                    if (!Min.HasValue || Min > value)
                        Min = value;

                    if (!Max.HasValue || Max < value)
                        Max = value;
                }
            }
        }
    }

    public IEnumerable<SensorValue> Values
    {
        get
        {
            // Cached snapshot: the returned array reference changes only when the data changed,
            // so callers can use ReferenceEquals for O(1) change detection.
            lock (_valuesLock)
            {
                return _valuesSnapshot ??= _values.ToArray();
            }
        }
    }

    public TimeSpan ValuesTimeWindow
    {
        get { return _valuesTimeWindow; }
        set
        {
            _valuesTimeWindow = value;
            if (value == TimeSpan.Zero)
            {
                lock (_valuesLock)
                {
                    _values.Clear();
                    _sum = 0;
                    _count = 0;
                    _valuesSnapshot = null;
                }
            }
        }
    }

    public void ResetMin()
    {
        lock (_valuesLock)
        {
            Min = null;
        }
    }

    public void ResetMax()
    {
        lock (_valuesLock)
        {
            Max = null;
        }
    }

    public void ClearValues()
    {
        lock (_valuesLock)
        {
            _values.Clear();
            _sum = 0;
            _count = 0;
            _valuesSnapshot = null;
        }
    }

    public void Accept(IVisitor visitor)
    {
        if (visitor == null)
            throw new ArgumentNullException(nameof(visitor));

        visitor.VisitSensor(this);
    }

    public void Traverse(IVisitor visitor)
    {
        foreach (IParameter parameter in Parameters)
            parameter.Accept(visitor);
    }

    private void SetSensorValuesToSettings()
    {
        string name = new Identifier(Identifier, "values").ToString();

        SensorValue[] values;
        lock (_valuesLock)
        {
            values = _values.ToArray();
        }

        if (values.Length == 0)
        {
            // No history to persist (e.g. history disabled): drop any leftover entry instead
            // of storing an empty blob for every sensor.
            _settings.Remove(name);
            return;
        }

        _settings.SetValue(name, EncodeValuesBounded(values, MaxPersistedValuesLength));
    }

    /// <summary>
    /// Encodes sensor values for persistence, dropping the oldest samples as needed so the
    /// resulting base64 payload does not exceed <paramref name="maxEncodedLength" /> characters.
    /// </summary>
    internal static string EncodeValuesBounded(SensorValue[] values, int maxEncodedLength)
    {
        int start = 0;
        string encoded = EncodeValues(values, start);

        while (encoded.Length > maxEncodedLength && start < values.Length)
        {
            // Drop the oldest half of the remaining samples until the payload fits.
            start += (values.Length - start + 1) / 2;
            encoded = EncodeValues(values, start);
        }

        return encoded;
    }

    private static string EncodeValues(SensorValue[] values, int start)
    {
        using MemoryStream memoryStream = new();
        using (GZipStream gZipStream = new(memoryStream, CompressionMode.Compress))
        using (BufferedStream outputStream = new(gZipStream, 65536))
        using (BinaryWriter binaryWriter = new(outputStream))
        {
            long t = 0;

            for (int i = start; i < values.Length; i++)
            {
                long v = values[i].Time.ToBinary();
                binaryWriter.Write(v - t);
                t = v;
                binaryWriter.Write(values[i].Value);
            }

            binaryWriter.Flush();
        }

        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private void GetSensorValuesFromSettings()
    {
        string name = new Identifier(Identifier, "values").ToString();
        string s = _settings.GetValue(name, null);

        if (!string.IsNullOrEmpty(s))
        {
            try
            {
                byte[] array = Convert.FromBase64String(s);
                DateTime now = DateTime.UtcNow;

                using MemoryStream memoryStream = new(array);
                using GZipStream gZipStream = new(memoryStream, CompressionMode.Decompress);
                using MemoryStream destination = new();

                gZipStream.CopyTo(destination);
                destination.Seek(0, SeekOrigin.Begin);

                using BinaryReader reader = new(destination);
                lock (_valuesLock)
                {
                    try
                    {
                        long t = 0;
                        long readLen = reader.BaseStream.Length - reader.BaseStream.Position;
                        while (readLen > 0)
                        {
                            t += reader.ReadInt64();
                            DateTime time = DateTime.FromBinary(t);
                            if (time > now)
                                break;

                            float value = reader.ReadSingle();

                            // Histories persisted by older builds may contain non-finite samples
                            // (the live path now filters them); drop them on reload so the
                            // finite-history invariant holds. The deliberate NaN session marker
                            // is appended separately below.
                            if (!float.IsNaN(value) && !float.IsInfinity(value))
                                AppendValue(value, time);

                            readLen = reader.BaseStream.Length - reader.BaseStream.Position;
                        }
                    }
                    catch (EndOfStreamException)
                    { }

                    _valuesSnapshot = null;
                }
            }
            catch
            {
                // Ignored.
            }
        }

        lock (_valuesLock)
        {
            if (_values.Count > 0)
            {
                AppendValue(float.NaN, DateTime.UtcNow);
                _valuesSnapshot = null;
            }
        }

        //remove the value string from the settings to reduce memory usage
        _settings.Remove(name);
    }

    private void AppendValue(float value, DateTime time)
    {
        if (_values.Count >= 2 && _values[_values.Count - 1].Value == value && _values[_values.Count - 2].Value == value)
        {
            _values[_values.Count - 1] = new SensorValue(value, time);
            return;
        }

        _values.Add(new SensorValue(value, time));
    }
}
