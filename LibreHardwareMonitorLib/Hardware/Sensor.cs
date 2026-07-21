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

internal class Sensor : ISensor, ISensorHistoryReader
{
    // Upper bound for the base64 payload persisted per sensor under "<sensor>/values".
    // Keep in sync with PersistentSettings.MaxSensorValuesLength in the Windows Forms app,
    // which drops any larger entry so sensor history cannot bloat the settings file.
    internal const int MaxPersistedValuesLength = 64 * 1024;
    internal const int MaxHistoryValues = 10_000;

    // Legacy builds could record one averaged point per second across the longest selectable
    // 24-hour window, plus a session marker. Permit that full payload while bounding work from
    // highly compressible gzip bombs. The byte budget includes one trailing EOF probe byte.
    internal const int MaxDecodedHistoryRecords = (24 * 60 * 60) + 1;
    internal const int MaxDecodedHistoryBytes = (MaxDecodedHistoryRecords * (sizeof(long) + sizeof(float))) + 1;

    private const int PersistedValueSize = sizeof(long) + sizeof(float);

    private const int HistoryBucketSize = 4;
    private const int RecentHistoryValues = MaxHistoryValues / 2;

    private static readonly uint[] _crc32Table = CreateCrc32Table();

    private readonly string _defaultName;
    private readonly Hardware _hardware;
    private readonly ISettings _settings;
    private readonly bool _trackMinMax;
    private readonly Func<DateTime> _utcNow;
    private readonly List<SensorValue> _values = new();
    private readonly object _valuesLock = new();
    private int _count;
    private float? _currentValue;
    private long _historyResetVersion;
    private long _historyVersion;
    private float? _max;
    private float? _min;
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
        : this(name, index, defaultHidden, sensorType, hardware, parameterDescriptions, settings, disableHistory, () => DateTime.UtcNow)
    { }

    internal Sensor
    (
        string name,
        int index,
        bool defaultHidden,
        SensorType sensorType,
        Hardware hardware,
        ParameterDescription[] parameterDescriptions,
        ISettings settings,
        bool disableHistory,
        Func<DateTime> utcNow)
    {
        if (utcNow == null)
            throw new ArgumentNullException(nameof(utcNow));

        Index = index;
        IsDefaultHidden = defaultHidden;
        SensorType = sensorType;
        _hardware = hardware;
        _utcNow = utcNow;

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

    public long HistoryVersion
    {
        get
        {
            lock (_valuesLock)
            {
                RemoveExpiredValuesForRead();
                return _historyVersion;
            }
        }
    }

    public float? Max
    {
        get
        {
            lock (_valuesLock)
            {
                return _max;
            }
        }
    }

    public float? Min
    {
        get
        {
            lock (_valuesLock)
            {
                return _min;
            }
        }
    }

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
        get
        {
            lock (_valuesLock)
            {
                return _currentValue;
            }
        }
        set
        {
            lock (_valuesLock)
            {
                if (_valuesTimeWindow != TimeSpan.Zero)
                {
                    DateTime now = _utcNow();
                    RemoveExpiredValues(now);

                    if (value.HasValue)
                    {
                        if (IsFinite(value.Value))
                        {
                            _sum += value.Value;
                            _count++;
                            if (_count == 4)
                            {
                                AppendValue(_sum / _count, now);
                                _sum = 0;
                                _count = 0;
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

                _currentValue = value;
                if (_trackMinMax && value.HasValue && IsFinite(value.Value))
                {
                    if (!_min.HasValue || _min > value)
                        _min = value;

                    if (!_max.HasValue || _max < value)
                        _max = value;
                }
            }
        }
    }

    protected void ResetPendingHistoryBucket()
    {
        lock (_valuesLock)
        {
            _sum = 0;
            _count = 0;
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
                RemoveExpiredValuesForRead();
                return _valuesSnapshot ??= _values.ToArray();
            }
        }
    }

    public SensorHistorySlice ReadHistory(long sinceVersion, int maxValues)
    {
        if (maxValues <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValues), maxValues, "The maximum history value count must be positive.");

        lock (_valuesLock)
        {
            RemoveExpiredValuesForRead();
            bool resetRequired = sinceVersion < 0 || sinceVersion > _historyVersion || sinceVersion < _historyResetVersion;
            int startIndex;

            if (resetRequired)
            {
                startIndex = Math.Max(0, _values.Count - maxValues);
            }
            else
            {
                // Every version after the latest reset represents exactly one append. This
                // invariant avoids a per-point version sidecar while still making delta reads
                // O(maxValues). Any replacement/removal/decimation advances the reset version.
                long deltaCount = _historyVersion - sinceVersion;
                if (deltaCount > _values.Count || deltaCount > maxValues)
                {
                    resetRequired = true;
                    startIndex = Math.Max(0, _values.Count - maxValues);
                }
                else
                {
                    startIndex = _values.Count - (int)deltaCount;
                }
            }

            int count = _values.Count - startIndex;
            SensorValue[] values = count == 0 ? Array.Empty<SensorValue>() : new SensorValue[count];
            if (count > 0)
                _values.CopyTo(startIndex, values, 0, count);

            return new SensorHistorySlice(_historyVersion, resetRequired, values, true);
        }
    }

    public TimeSpan ValuesTimeWindow
    {
        get
        {
            lock (_valuesLock)
            {
                return _valuesTimeWindow;
            }
        }
        set
        {
            lock (_valuesLock)
            {
                _valuesTimeWindow = value;
                if (value == TimeSpan.Zero)
                {
                    ClearValuesCore();
                }
                else
                {
                    RemoveExpiredValues(_utcNow());
                }
            }
        }
    }

    public void ResetMin()
    {
        lock (_valuesLock)
        {
            _min = null;
        }
    }

    public void ResetMax()
    {
        lock (_valuesLock)
        {
            _max = null;
        }
    }

    public void ClearValues()
    {
        lock (_valuesLock)
        {
            ClearValuesCore();
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
            RemoveExpiredValuesForRead();
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
        DateTime now = _utcNow();
        bool loadedSuccessfully = false;

        if (!string.IsNullOrEmpty(s) && s.Length <= MaxPersistedValuesLength)
        {
            lock (_valuesLock)
            {
                try
                {
                    byte[] array = Convert.FromBase64String(s);
                    loadedSuccessfully = TryDecodeSensorValues(array,
                                                               now,
                                                               _valuesTimeWindow,
                                                               AppendValue,
                                                               out _,
                                                               out _);
                }
                catch (Exception exception) when (exception is FormatException ||
                                                  exception is InvalidDataException ||
                                                  exception is IOException ||
                                                  exception is ArgumentException ||
                                                  exception is OverflowException)
                {
                    loadedSuccessfully = false;
                }

                if (!loadedSuccessfully)
                {
                    ClearValuesCore();
                    _values.Capacity = 0;
                }
            }
        }

        lock (_valuesLock)
        {
            if (loadedSuccessfully && _values.Count > 0)
                AppendValue(float.NaN, now);
        }

        //remove the value string from the settings to reduce memory usage
        _settings.Remove(name);
    }

    internal static bool TryDecodeSensorValues
    (
        byte[] compressedData,
        DateTime now,
        TimeSpan valuesTimeWindow,
        Action<float, DateTime> appendValue,
        out int decodedRecordCount,
        out int decodedByteCount)
    {
        if (compressedData == null)
            throw new ArgumentNullException(nameof(compressedData));

        if (appendValue == null)
            throw new ArgumentNullException(nameof(appendValue));

        decodedRecordCount = 0;
        decodedByteCount = 0;

        // RFC 1952 requires a ten-byte header and an eight-byte trailer. ISIZE lets us reject
        // ordinary gzip bombs before decompression; CRC and a one-byte EOF probe below also
        // protect against forged/truncated trailers.
        if (compressedData.Length < 18 || compressedData[0] != 0x1f || compressedData[1] != 0x8b)
            return false;

        uint expectedCrc32 = ReadUInt32LittleEndian(compressedData, compressedData.Length - 8);
        uint expectedDecodedSize = ReadUInt32LittleEndian(compressedData, compressedData.Length - 4);
        int maxDecodedDataBytes = MaxDecodedHistoryRecords * PersistedValueSize;
        if (expectedDecodedSize % PersistedValueSize != 0 || expectedDecodedSize > (uint)maxDecodedDataBytes)
            return false;

        int expectedRecordCount = (int)(expectedDecodedSize / PersistedValueSize);
        byte[] record = new byte[PersistedValueSize];
        long binaryTime = 0;
        uint crc32 = uint.MaxValue;
        bool acceptValues = true;

        try
        {
            // GZipStream treats concatenated members and trailing bytes differently across the
            // supported runtimes. Limit compressed reads to one byte so read-ahead cannot hide
            // bytes after the one history member that this format permits.
            using SingleByteReadStream compressedStream = new(compressedData);
            using GZipStream decompressedStream = new(compressedStream, CompressionMode.Decompress);

            while (decodedRecordCount < expectedRecordCount)
            {
                int bytesRead = ReadPersistedRecord(decompressedStream, record);
                decodedByteCount += bytesRead;
                crc32 = UpdateCrc32(crc32, record, bytesRead);

                if (bytesRead != PersistedValueSize)
                    return false;

                decodedRecordCount++;
                binaryTime = checked(binaryTime + BitConverter.ToInt64(record, 0));
                DateTime time = DateTime.FromBinary(binaryTime);
                float value = BitConverter.ToSingle(record, sizeof(long));

                // Histories persisted by older builds may contain non-finite samples. Drop
                // those and append one deliberate session marker only after the whole payload
                // passes validation.
                if (acceptValues && time > now)
                    acceptValues = false;
                else if (acceptValues && valuesTimeWindow != TimeSpan.Zero && now - time <= valuesTimeWindow && IsFinite(value))
                    appendValue(value, time);
            }

            // A forged small ISIZE cannot bypass the budget: only one extra decompressed byte is
            // read before the payload is rejected.
            int trailingByte = decompressedStream.ReadByte();
            if (trailingByte >= 0)
            {
                decodedByteCount++;
                return false;
            }

            return compressedStream.IsFullyConsumed &&
                   (uint)decodedByteCount == expectedDecodedSize &&
                   ~crc32 == expectedCrc32;
        }
        catch (Exception exception) when (exception is InvalidDataException ||
                                          exception is IOException ||
                                          exception is ArgumentException ||
                                          exception is OverflowException)
        {
            return false;
        }
    }

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
    {
        return (uint)bytes[offset] |
               ((uint)bytes[offset + 1] << 8) |
               ((uint)bytes[offset + 2] << 16) |
               ((uint)bytes[offset + 3] << 24);
    }

    private sealed class SingleByteReadStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public SingleByteReadStream(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public bool IsFullyConsumed => _position == _data.Length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (buffer.Length - offset < count)
                throw new ArgumentException("Offset and count exceed the buffer length.");

            if (count == 0 || _position == _data.Length)
                return 0;

            buffer[offset] = _data[_position++];
            return 1;
        }

        public override int ReadByte()
        {
            return _position == _data.Length ? -1 : _data[_position++];
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private static uint[] CreateCrc32Table()
    {
        uint[] table = new uint[256];
        for (int i = 0; i < table.Length; i++)
        {
            uint value = (uint)i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ 0xedb88320u : value >> 1;

            table[i] = value;
        }

        return table;
    }

    private static int ReadPersistedRecord(Stream stream, byte[] record)
    {
        int totalBytesRead = 0;
        while (totalBytesRead < record.Length)
        {
            int bytesRead = stream.Read(record, totalBytesRead, record.Length - totalBytesRead);
            if (bytesRead == 0)
                break;

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    private static uint UpdateCrc32(uint crc32, byte[] bytes, int count)
    {
        for (int i = 0; i < count; i++)
            crc32 = (crc32 >> 8) ^ _crc32Table[(crc32 ^ bytes[i]) & 0xff];

        return crc32;
    }

    private void AppendValue(float value, DateTime time)
    {
        // UTC can move backward after a wall-clock correction. Clamp recording timestamps so
        // history stays chronological and prefix-based expiry remains valid.
        if (_values.Count > 0 && time < _values[_values.Count - 1].Time)
            time = _values[_values.Count - 1].Time;

        if (_values.Count >= 2 && _values[_values.Count - 1].Value == value && _values[_values.Count - 2].Value == value)
        {
            long version = NextHistoryVersion();
            _values[_values.Count - 1] = new SensorValue(value, time);
            _historyResetVersion = version;
            _valuesSnapshot = null;
            return;
        }

        // Compact before adding so List<T> never has to grow beyond the public point cap.
        // Intercept the final geometric growth step for the same reason.
        if (_values.Count == MaxHistoryValues)
            CompactHistory();
        else if (_values.Count == _values.Capacity && _values.Capacity > MaxHistoryValues / 2)
            _values.Capacity = MaxHistoryValues;

        NextHistoryVersion();
        _values.Add(new SensorValue(value, time));
        _valuesSnapshot = null;
    }

    private void CopyBucketRepresentatives(int[] indices, ref int writeIndex, int start, int end)
    {
        int minimumIndex = -1;
        int maximumIndex = -1;
        int nonFiniteIndex = -1;

        for (int i = start; i < end; i++)
        {
            float value = _values[i].Value;
            if (!IsFinite(value))
            {
                if (nonFiniteIndex < 0)
                    nonFiniteIndex = i;

                continue;
            }

            if (minimumIndex < 0 || value < _values[minimumIndex].Value)
                minimumIndex = i;

            if (maximumIndex < 0 || value > _values[maximumIndex].Value)
                maximumIndex = i;
        }

        int count = 0;
        AddUniqueIndex(indices, ref count, minimumIndex);
        AddUniqueIndex(indices, ref count, maximumIndex);
        AddUniqueIndex(indices, ref count, nonFiniteIndex);
        Array.Sort(indices, 0, count);

        for (int i = 0; i < count; i++)
            CopyHistoryValue(indices[i], ref writeIndex);
    }

    private static void AddUniqueIndex(int[] indices, ref int count, int index)
    {
        if (index < 0)
            return;

        for (int i = 0; i < count; i++)
        {
            if (indices[i] == index)
                return;
        }

        indices[count++] = index;
    }

    private void CopyHistoryValue(int sourceIndex, ref int writeIndex)
    {
        if (sourceIndex != writeIndex)
            _values[writeIndex] = _values[sourceIndex];

        writeIndex++;
    }

    private void ClearValuesCore()
    {
        bool historyChanged = _values.Count > 0;
        _values.Clear();
        _sum = 0;
        _count = 0;
        _valuesSnapshot = null;

        if (historyChanged)
            MarkHistoryReset();
    }

    private void CompactHistory()
    {
        int recentStart = _values.Count - RecentHistoryValues;
        int originalCount = _values.Count;
        int writeIndex = 0;
        int[] indices = new int[3];

        // Retain the exact time-window boundary. The remaining older region is reduced to
        // chronological bucket extrema while the newest half stays at full resolution. Source
        // indices always remain ahead of the in-place write cursor, so no unread point is lost.
        CopyHistoryValue(0, ref writeIndex);
        for (int start = 1; start < recentStart; start += HistoryBucketSize)
            CopyBucketRepresentatives(indices, ref writeIndex, start, Math.Min(start + HistoryBucketSize, recentStart));

        for (int i = recentStart; i < originalCount; i++)
            CopyHistoryValue(i, ref writeIndex);

        _values.RemoveRange(writeIndex, originalCount - writeIndex);
        _valuesSnapshot = null;
        MarkHistoryReset();
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void MarkHistoryReset()
    {
        _historyResetVersion = NextHistoryVersion();
    }

    private long NextHistoryVersion()
    {
        return ++_historyVersion;
    }

    private void RemoveExpiredValues(DateTime now)
    {
        int expiredCount = 0;
        while (expiredCount < _values.Count && now - _values[expiredCount].Time > _valuesTimeWindow)
            expiredCount++;

        if (expiredCount == 0)
            return;

        _values.RemoveRange(0, expiredCount);
        _valuesSnapshot = null;
        MarkHistoryReset();
    }

    private void RemoveExpiredValuesForRead()
    {
        if (_valuesTimeWindow != TimeSpan.Zero)
            RemoveExpiredValues(_utcNow());
    }
}
