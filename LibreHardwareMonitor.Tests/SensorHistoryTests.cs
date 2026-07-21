// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Xunit;
using HardwareBase = LibreHardwareMonitor.Hardware.Hardware;

namespace LibreHardwareMonitor.Tests;

public sealed class SensorHistoryTests
{
    private const string SensorValuesKey = "/test/temperature/0/values";

    [Fact]
    public void TwentyFourHoursAt250Milliseconds_IsBoundedAndPreservesWindowExtremaAndNewest()
    {
        DateTime start = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorFixture fixture = CreateFixture(start);
        const int secondsPerDay = 24 * 60 * 60;

        for (int second = 0; second < secondsPerDay; second++)
        {
            float value = (second % 251) - 125;
            if (second == 60)
                value = -1_000;
            else if (second == 61)
                value = 1_000;

            RecordAveragedValue(fixture, value);
        }

        SensorValue[] values = fixture.Sensor.Values.ToArray();
        float expectedNewest = ((secondsPerDay - 1) % 251) - 125;
        DateTime expectedOldestTime = start.AddMilliseconds(750);
        DateTime expectedNewestTime = start.AddDays(1).AddMilliseconds(-250);

        Assert.InRange(values.Length, 1, Sensor.MaxHistoryValues);
        Assert.Equal(expectedOldestTime, values[0].Time);
        Assert.Equal(expectedNewestTime, values[values.Length - 1].Time);
        Assert.Equal(fixture.Sensor.ValuesTimeWindow - TimeSpan.FromSeconds(1), values[values.Length - 1].Time - values[0].Time);
        Assert.Contains(values, value => value.Value == -1_000f);
        Assert.Contains(values, value => value.Value == 1_000f);
        Assert.Equal(expectedNewest, fixture.Sensor.Value);
        Assert.Equal(-1_000f, fixture.Sensor.Min);
        Assert.Equal(1_000f, fixture.Sensor.Max);
    }

    [Fact]
    public void CompressedLegacyHistoryOverBudget_IsRejectedWithoutKeepingPartialValues()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string encoded = CreateHighlyCompressibleHistoryPayload(4_194_304, now.AddSeconds(-1), 0);
        TestSettings settings = new();
        settings.SetValue(SensorValuesKey, encoded);

        Assert.InRange(encoded.Length, 1, Sensor.MaxPersistedValuesLength);

        int acceptedRecordCount = 0;
        bool decoded = Sensor.TryDecodeSensorValues(Convert.FromBase64String(encoded),
                                                    now,
                                                    TimeSpan.FromDays(1),
                                                    (value, time) => acceptedRecordCount++,
                                                    out int decodedRecordCount,
                                                    out int decodedByteCount);

        Assert.False(decoded);
        Assert.Equal(0, decodedRecordCount);
        Assert.Equal(0, acceptedRecordCount);
        Assert.Equal(0, decodedByteCount);
        Assert.InRange(decodedByteCount, 0, Sensor.MaxDecodedHistoryBytes);

        byte[] forgedSmallTrailer = Convert.FromBase64String(encoded);
        Array.Clear(forgedSmallTrailer, forgedSmallTrailer.Length - 8, 8);
        bool forgedDecoded = Sensor.TryDecodeSensorValues(forgedSmallTrailer,
                                                          now,
                                                          TimeSpan.FromDays(1),
                                                          (value, time) => acceptedRecordCount++,
                                                          out int forgedRecordCount,
                                                          out int forgedByteCount);

        Assert.False(forgedDecoded);
        Assert.Equal(0, forgedRecordCount);
        Assert.Equal(0, acceptedRecordCount);
        Assert.Equal(1, forgedByteCount);
        Assert.InRange(forgedByteCount, 0, Sensor.MaxDecodedHistoryBytes);

        SensorFixture fixture = CreateFixture(now, settings);

        Assert.Empty(fixture.Sensor.Values);
        Assert.False(settings.Contains(SensorValuesKey));
    }

    [Fact]
    public void LegacyHistoryAtDecodeBudget_RemainsAccepted()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string encoded = CreateHighlyCompressibleHistoryPayload(Sensor.MaxDecodedHistoryRecords, now.AddSeconds(-1), 0);
        int acceptedRecordCount = 0;

        Assert.InRange(encoded.Length, 1, Sensor.MaxPersistedValuesLength);

        bool decoded = Sensor.TryDecodeSensorValues(Convert.FromBase64String(encoded),
                                                    now,
                                                    TimeSpan.FromDays(1),
                                                    (value, time) => acceptedRecordCount++,
                                                    out int decodedRecordCount,
                                                    out int decodedByteCount);

        Assert.True(decoded);
        Assert.Equal(Sensor.MaxDecodedHistoryRecords, decodedRecordCount);
        Assert.Equal(Sensor.MaxDecodedHistoryRecords, acceptedRecordCount);
        Assert.Equal(Sensor.MaxDecodedHistoryBytes - 1, decodedByteCount);
    }

    [Fact]
    public void MalformedOrTruncatedPersistedHistory_IsDiscardedWithoutPartialSession()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        byte[] completeRecord = CreatePersistedRecord(now.AddSeconds(-1).ToBinary(), 42);
        byte[] truncatedPayload = new byte[completeRecord.Length + 5];
        Buffer.BlockCopy(completeRecord, 0, truncatedPayload, 0, completeRecord.Length);
        Buffer.BlockCopy(completeRecord, 0, truncatedPayload, completeRecord.Length, 5);

        TestSettings truncatedSettings = new();
        truncatedSettings.SetValue(SensorValuesKey, CompressHistoryBytes(truncatedPayload));
        SensorFixture truncatedFixture = CreateFixture(now, truncatedSettings);

        Assert.Empty(truncatedFixture.Sensor.Values);
        Assert.False(truncatedSettings.Contains(SensorValuesKey));

        byte[] truncatedGzip = Convert.FromBase64String(CompressHistoryBytes(completeRecord));
        Array.Resize(ref truncatedGzip, truncatedGzip.Length - 4);
        TestSettings truncatedGzipSettings = new();
        truncatedGzipSettings.SetValue(SensorValuesKey, Convert.ToBase64String(truncatedGzip));
        SensorFixture truncatedGzipFixture = CreateFixture(now, truncatedGzipSettings);

        Assert.Empty(truncatedGzipFixture.Sensor.Values);
        Assert.False(truncatedGzipSettings.Contains(SensorValuesKey));

        TestSettings malformedSettings = new();
        malformedSettings.SetValue(SensorValuesKey, "not-valid-base64");
        SensorFixture malformedFixture = CreateFixture(now, malformedSettings);

        Assert.Empty(malformedFixture.Sensor.Values);
        Assert.False(malformedSettings.Contains(SensorValuesKey));
    }

    [Fact]
    public void ConcatenatedMemberOrTrailingJunkWithCopiedTrailer_IsRejected()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        byte[] firstMember = Convert.FromBase64String(CompressHistoryBytes(CreatePersistedRecord(now.AddSeconds(-2).ToBinary(), 10)));
        byte[] secondMember = (byte[])firstMember.Clone();
        byte[] concatenated = Concatenate(firstMember, secondMember);
        byte[] trailingJunk = AppendJunkAndCopiedTrailer(firstMember, new byte[] { 0xde, 0xad, 0xbe, 0xef });

        int acceptedRecordCount = 0;
        Assert.True(TryDecodeForTest(firstMember, now, () => acceptedRecordCount++));
        Assert.Equal(1, acceptedRecordCount);

        acceptedRecordCount = 0;
        Assert.False(TryDecodeForTest(concatenated, now, () => acceptedRecordCount++));
        Assert.InRange(acceptedRecordCount, 0, 1);

        acceptedRecordCount = 0;
        Assert.False(TryDecodeForTest(trailingJunk, now, () => acceptedRecordCount++));
        Assert.InRange(acceptedRecordCount, 0, 1);
    }

    [Fact]
    public void CorruptCrc_RollsBackPartiallyDecodedHistoryAndReleasesCapacity()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorValue[] persisted =
        {
            new(10, now.AddSeconds(-2)),
            new(20, now.AddSeconds(-1))
        };
        byte[] corruptCrc = Convert.FromBase64String(Sensor.EncodeValuesBounded(persisted, Sensor.MaxPersistedValuesLength));
        corruptCrc[corruptCrc.Length - 8] ^= 0xff;
        int acceptedRecordCount = 0;

        bool decoded = Sensor.TryDecodeSensorValues(corruptCrc,
                                                    now,
                                                    TimeSpan.FromDays(1),
                                                    (value, time) => acceptedRecordCount++,
                                                    out int decodedRecordCount,
                                                    out int decodedByteCount);

        Assert.False(decoded);
        Assert.InRange(decodedRecordCount, 1, persisted.Length);
        Assert.Equal(decodedRecordCount, acceptedRecordCount);
        Assert.InRange(decodedByteCount, sizeof(long) + sizeof(float), persisted.Length * (sizeof(long) + sizeof(float)));

        TestSettings settings = new();
        settings.SetValue(SensorValuesKey, Convert.ToBase64String(corruptCrc));
        SensorFixture fixture = CreateFixture(now, settings);

        Assert.Empty(fixture.Sensor.Values);
        Assert.Equal(0, GetHistoryCapacity(fixture.Sensor));
        Assert.False(settings.Contains(SensorValuesKey));
    }

    [Fact]
    public void BackwardClockCorrection_KeepsHistoryTimestampsNondecreasing()
    {
        DateTime start = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorFixture fixture = CreateFixture(start);
        ISensorHistoryReader reader = fixture.Sensor;

        RecordAveragedValue(fixture, 10);
        RecordAveragedValue(fixture, 20);
        SensorHistorySlice beforeRollback = reader.ReadHistory(0, 100);
        DateTime previousNewest = beforeRollback.Values[beforeRollback.Values.Count - 1].Time;

        fixture.Clock.SetUtcNow(start.AddHours(-1));
        RecordAveragedValue(fixture, 30);

        SensorHistorySlice delta = reader.ReadHistory(beforeRollback.Version, 100);
        SensorValue appended = Assert.Single(delta.Values);
        Assert.False(delta.ResetRequired);
        Assert.Equal(30f, appended.Value);
        Assert.Equal(previousNewest, appended.Time);
        SensorValue[] history = fixture.Sensor.Values.ToArray();
        Assert.True(history.Zip(history.Skip(1), (left, right) => left.Time <= right.Time).All(ordered => ordered));
    }

    [Fact]
    public void QuietSensor_ExpiresOnReadAndPersistenceAndRequiresReset()
    {
        SensorFixture readFixture = CreateFixture();
        ISensorHistoryReader reader = readFixture.Sensor;
        RecordAveragedValue(readFixture, 10);
        SensorHistorySlice beforeExpiry = reader.ReadHistory(0, 100);

        readFixture.Clock.Advance(readFixture.Sensor.ValuesTimeWindow + TimeSpan.FromSeconds(1));
        SensorHistorySlice afterExpiry = reader.ReadHistory(beforeExpiry.Version, 100);

        Assert.True(afterExpiry.ResetRequired);
        Assert.True(afterExpiry.Version > beforeExpiry.Version);
        Assert.Empty(afterExpiry.Values);

        SensorFixture valuesFixture = CreateFixture();
        RecordAveragedValue(valuesFixture, 20);
        valuesFixture.Clock.Advance(valuesFixture.Sensor.ValuesTimeWindow + TimeSpan.FromSeconds(1));
        Assert.Empty(valuesFixture.Sensor.Values);

        SensorFixture persistenceFixture = CreateFixture();
        RecordAveragedValue(persistenceFixture, 30);
        persistenceFixture.Clock.Advance(persistenceFixture.Sensor.ValuesTimeWindow + TimeSpan.FromSeconds(1));
        persistenceFixture.Hardware.Close();
        Assert.False(persistenceFixture.Settings.Contains(SensorValuesKey));
    }

    [Fact]
    public void PersistedHistory_LoadsAndKeepsSessionMarkerThroughDecimation()
    {
        DateTime now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorValue[] persisted =
        {
            new(10, now.AddSeconds(-2)),
            new(20, now.AddSeconds(-1)),
            new(float.NaN, now.AddMilliseconds(-500))
        };
        TestSettings settings = new();
        settings.SetValue(SensorValuesKey, Sensor.EncodeValuesBounded(persisted, Sensor.MaxPersistedValuesLength));

        SensorFixture fixture = CreateFixture(now, settings);
        SensorValue[] loaded = fixture.Sensor.Values.ToArray();

        Assert.Equal(new[] { 10f, 20f }, loaded.Take(2).Select(value => value.Value));
        Assert.True(float.IsNaN(loaded[loaded.Length - 1].Value));
        Assert.False(settings.Contains(SensorValuesKey));

        for (int i = 0; i < Sensor.MaxHistoryValues; i++)
            RecordAveragedValue(fixture, 1_000 + i);

        SensorValue[] decimated = fixture.Sensor.Values.ToArray();
        Assert.InRange(decimated.Length, 1, Sensor.MaxHistoryValues);
        Assert.Contains(decimated, value => float.IsNaN(value.Value));
        Assert.Equal(1_000f + Sensor.MaxHistoryValues - 1, decimated[decimated.Length - 1].Value);
    }

    [Fact]
    public void ReadHistory_ReturnsOnlyChronologicalAppendsSinceVersion()
    {
        SensorFixture fixture = CreateFixture();
        ISensorHistoryReader reader = fixture.Sensor;

        RecordAveragedValue(fixture, 10);
        RecordAveragedValue(fixture, 20);

        SensorHistorySlice initial = reader.ReadHistory(0, 100);
        Assert.False(initial.ResetRequired);
        Assert.Equal(new[] { 10f, 20f }, initial.Values.Select(value => value.Value));
        Assert.True(initial.Values[0].Time < initial.Values[1].Time);

        RecordAveragedValue(fixture, 30);
        RecordAveragedValue(fixture, 40);

        SensorHistorySlice delta = reader.ReadHistory(initial.Version, 100);
        Assert.False(delta.ResetRequired);
        Assert.Equal(new[] { 30f, 40f }, delta.Values.Select(value => value.Value));
        Assert.True(delta.Version > initial.Version);

        SensorHistorySlice unchanged = reader.ReadHistory(delta.Version, 100);
        Assert.False(unchanged.ResetRequired);
        Assert.Empty(unchanged.Values);
        Assert.Equal(delta.Version, unchanged.Version);
        Assert.Equal(delta.Version, reader.HistoryVersion);
    }

    [Fact]
    public void ReadHistory_WhenResultExceedsLimit_ReturnsNewestBoundedResetSlice()
    {
        SensorFixture fixture = CreateFixture();
        ISensorHistoryReader reader = fixture.Sensor;

        for (int value = 0; value < 20; value++)
            RecordAveragedValue(fixture, value);

        SensorHistorySlice slice = reader.ReadHistory(0, 3);

        Assert.True(slice.ResetRequired);
        Assert.Equal(new[] { 17f, 18f, 19f }, slice.Values.Select(value => value.Value));
        Assert.Equal(reader.HistoryVersion, slice.Version);

        SensorHistorySlice futureVersion = reader.ReadHistory(long.MaxValue, 3);
        Assert.True(futureVersion.ResetRequired);
        Assert.Equal(new[] { 17f, 18f, 19f }, futureVersion.Values.Select(value => value.Value));
    }

    [Fact]
    public void SensorHistorySlice_CopiesInputAndAlwaysExposesImmutableNonNullValues()
    {
        DateTime time = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorValue[] source = { new(1, time) };
        SensorHistorySlice slice = new(7, false, source);

        source[0] = new SensorValue(2, time.AddSeconds(1));

        Assert.NotNull(slice.Values);
        Assert.Equal(1f, Assert.Single(slice.Values).Value);
        Assert.False(slice.Values is IList<SensorValue>);

        SensorHistorySlice empty = new(0, false, Array.Empty<SensorValue>());
        Assert.NotNull(empty.Values);
        Assert.Empty(empty.Values);
    }

    [Fact]
    public void ReadHistory_AfterDecimationOrTailReplacement_RequiresReset()
    {
        SensorFixture fixture = CreateFixture();
        ISensorHistoryReader reader = fixture.Sensor;

        for (int value = 0; value < Sensor.MaxHistoryValues - 1; value++)
            RecordAveragedValue(fixture, value);

        SensorHistorySlice beforeDecimation = reader.ReadHistory(0, Sensor.MaxHistoryValues);
        Assert.False(beforeDecimation.ResetRequired);

        RecordAveragedValue(fixture, Sensor.MaxHistoryValues - 1);
        RecordAveragedValue(fixture, Sensor.MaxHistoryValues);

        SensorHistorySlice afterDecimation = reader.ReadHistory(beforeDecimation.Version, Sensor.MaxHistoryValues);
        Assert.True(afterDecimation.ResetRequired);
        Assert.InRange(afterDecimation.Values.Count, 1, Sensor.MaxHistoryValues);
        Assert.Equal((float)Sensor.MaxHistoryValues, afterDecimation.Values[afterDecimation.Values.Count - 1].Value);

        SensorFixture stableFixture = CreateFixture();
        ISensorHistoryReader stableReader = stableFixture.Sensor;
        RecordAveragedValue(stableFixture, 42);
        RecordAveragedValue(stableFixture, 42);
        SensorHistorySlice beforeReplacement = stableReader.ReadHistory(0, 10);

        RecordAveragedValue(stableFixture, 42);
        SensorHistorySlice afterReplacement = stableReader.ReadHistory(beforeReplacement.Version, 10);

        Assert.True(afterReplacement.ResetRequired);
        Assert.Equal(2, afterReplacement.Values.Count);
        Assert.True(afterReplacement.Values[1].Time > beforeReplacement.Values[1].Time);
    }

    [Fact]
    public void Decimation_PreservesEveryOlderBucketExtremumAndNewestHalfAtFullDetail()
    {
        DateTime start = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SensorFixture fixture = CreateFixture(start);
        float[] original = new float[Sensor.MaxHistoryValues + 1];
        float[] offsets = { 2, -5, 7, 0 };
        original[0] = -50_000;

        for (int i = 1; i < Sensor.MaxHistoryValues / 2; i++)
        {
            int bucket = (i - 1) / 4;
            original[i] = (bucket * 20) + offsets[(i - 1) % offsets.Length];
        }

        for (int i = Sensor.MaxHistoryValues / 2; i < original.Length; i++)
            original[i] = 100_000 + i;

        for (int i = 0; i < original.Length; i++)
            RecordAveragedValue(fixture, original[i]);

        SensorValue[] retained = fixture.Sensor.Values.ToArray();
        HashSet<float> retainedValues = retained.Select(value => value.Value).ToHashSet();
        int recentStart = Sensor.MaxHistoryValues / 2;

        Assert.Contains(original[0], retainedValues);
        for (int bucketStart = 1; bucketStart < recentStart; bucketStart += 4)
        {
            float[] bucket = original.Skip(bucketStart).Take(Math.Min(4, recentStart - bucketStart)).ToArray();
            Assert.Contains(bucket.Min(), retainedValues);
            Assert.Contains(bucket.Max(), retainedValues);
        }

        DateTime recentStartTime = start.AddMilliseconds(750).AddSeconds(recentStart);
        SensorValue[] retainedRecent = retained.Where(value => value.Time >= recentStartTime).ToArray();
        Assert.Equal(original.Skip(recentStart), retainedRecent.Select(value => value.Value));
        Assert.Equal(original.Length - recentStart, retainedRecent.Length);
        Assert.Equal(original[original.Length - 1], retained[retained.Length - 1].Value);
    }

    [Fact]
    public void ValuesTimeWindow_TrimAndDisableResetHistoryWithoutChangingCurrentMinMax()
    {
        SensorFixture fixture = CreateFixture();
        ISensorHistoryReader reader = fixture.Sensor;

        RecordAveragedValue(fixture, -10);
        RecordAveragedValue(fixture, 10);
        SensorHistorySlice beforeTrim = reader.ReadHistory(0, 100);

        fixture.Clock.Advance(TimeSpan.FromHours(2));
        fixture.Sensor.ValuesTimeWindow = TimeSpan.FromHours(1);

        SensorHistorySlice afterTrim = reader.ReadHistory(beforeTrim.Version, 100);
        Assert.True(afterTrim.ResetRequired);
        Assert.Empty(afterTrim.Values);
        Assert.Equal(10f, fixture.Sensor.Value);
        Assert.Equal(-10f, fixture.Sensor.Min);
        Assert.Equal(10f, fixture.Sensor.Max);

        RecordAveragedValue(fixture, 20);
        SensorHistorySlice appendAfterReset = reader.ReadHistory(afterTrim.Version, 100);
        Assert.False(appendAfterReset.ResetRequired);
        Assert.Single(appendAfterReset.Values);
        Assert.Equal(20f, appendAfterReset.Values[0].Value);

        long beforeDisableVersion = reader.HistoryVersion;
        fixture.Sensor.ValuesTimeWindow = TimeSpan.Zero;

        SensorHistorySlice afterDisable = reader.ReadHistory(beforeDisableVersion, 100);
        Assert.True(afterDisable.ResetRequired);
        Assert.Empty(afterDisable.Values);

        fixture.Sensor.Value = 30;
        Assert.Empty(fixture.Sensor.Values);
        Assert.Equal(30f, fixture.Sensor.Value);
        Assert.Equal(-10f, fixture.Sensor.Min);
        Assert.Equal(30f, fixture.Sensor.Max);
    }

    [Fact]
    public void CurrentMinAndMax_RemainExactWhenHistoryAveragesAndRejectsDropouts()
    {
        SensorFixture fixture = CreateFixture();

        SetValueAndAdvance(fixture, 10);
        SetValueAndAdvance(fixture, 20);
        SetValueAndAdvance(fixture, -5);
        SetValueAndAdvance(fixture, 15);

        SensorValue historyValue = Assert.Single(fixture.Sensor.Values);
        Assert.Equal(10f, historyValue.Value);
        Assert.Equal(15f, fixture.Sensor.Value);
        Assert.Equal(-5f, fixture.Sensor.Min);
        Assert.Equal(20f, fixture.Sensor.Max);

        fixture.Sensor.Value = float.NaN;
        Assert.True(float.IsNaN(fixture.Sensor.Value.Value));
        Assert.Equal(-5f, fixture.Sensor.Min);
        Assert.Equal(20f, fixture.Sensor.Max);

        fixture.Sensor.Value = float.PositiveInfinity;
        Assert.True(float.IsPositiveInfinity(fixture.Sensor.Value.Value));
        Assert.Equal(-5f, fixture.Sensor.Min);
        Assert.Equal(20f, fixture.Sensor.Max);

        fixture.Sensor.Value = null;
        Assert.Null(fixture.Sensor.Value);
        Assert.Equal(-5f, fixture.Sensor.Min);
        Assert.Equal(20f, fixture.Sensor.Max);

        fixture.Sensor.ResetMin();
        fixture.Sensor.ResetMax();
        Assert.Null(fixture.Sensor.Min);
        Assert.Null(fixture.Sensor.Max);

        fixture.Sensor.Value = 7;
        Assert.Equal(7f, fixture.Sensor.Min);
        Assert.Equal(7f, fixture.Sensor.Max);
    }

    [Fact]
    public void ReadHistory_RejectsNonPositiveMaximum()
    {
        SensorFixture fixture = CreateFixture();
        ISensorHistoryReader reader = fixture.Sensor;

        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadHistory(0, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadHistory(0, -1); });
    }

    [Fact]
    public async Task ConcurrentUpdatesAndReads_KeepVersionsOrderedAndHistoryBounded()
    {
        DateTime start = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long ticks = start.Ticks;
        Sensor sensor = CreateSensor(() => new DateTime(Interlocked.Add(ref ticks, TimeSpan.TicksPerMillisecond * 250), DateTimeKind.Utc));
        ISensorHistoryReader reader = sensor;
        const int writerCount = 4;
        const int valuesPerWriter = 12_000;
        using ManualResetEventSlim gate = new(false);
        using ManualResetEventSlim valuesWritten = new(false);
        using ManualResetEventSlim overlappingRead = new(false);
        int remainingWriters = writerCount;
        int readCount = 0;
        int overlappingReadCount = 0;
        List<SensorValue> reconstructed = new();

        Task[] writers = Enumerable.Range(0, writerCount).Select(writer => Task.Run(() =>
        {
            gate.Wait();
            try
            {
                int offset = writer * valuesPerWriter;
                for (int i = 0; i < valuesPerWriter; i++)
                {
                    sensor.Value = offset + i;

                    if (i == 100)
                    {
                        valuesWritten.Set();
                        overlappingRead.Wait();
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref remainingWriters);
                valuesWritten.Set();
                overlappingRead.Set();
            }
        })).ToArray();

        Task readerTask = Task.Run(() =>
        {
            gate.Wait();
            valuesWritten.Wait();
            long version = 0;
            do
            {
                SensorHistorySlice slice = reader.ReadHistory(version, Sensor.MaxHistoryValues);
                Assert.True(slice.Version >= version);
                Assert.InRange(slice.Values.Count, 0, Sensor.MaxHistoryValues);
                ApplySlice(reconstructed, slice);
                version = slice.Version;
                Interlocked.Increment(ref readCount);

                if (slice.Version > 0 && Volatile.Read(ref remainingWriters) > 0)
                {
                    Interlocked.Increment(ref overlappingReadCount);
                    overlappingRead.Set();
                }

                Thread.Yield();
            }
            while (Volatile.Read(ref remainingWriters) > 0);

            SensorHistorySlice finalSlice = reader.ReadHistory(version, Sensor.MaxHistoryValues);
            ApplySlice(reconstructed, finalSlice);
        });

        gate.Set();
        await Task.WhenAll(writers);
        await readerTask;

        SensorValue[] values = sensor.Values.ToArray();
        Assert.InRange(values.Length, 1, Sensor.MaxHistoryValues);
        Assert.True(values.Zip(values.Skip(1), (left, right) => left.Time <= right.Time).All(ordered => ordered));
        Assert.True(readCount > 0);
        Assert.True(overlappingReadCount > 0);
        Assert.Equal(values.Select(value => (value.Time, value.Value)), reconstructed.Select(value => (value.Time, value.Value)));
        Assert.Equal(0f, sensor.Min);
        Assert.Equal((float)((writerCount * valuesPerWriter) - 1), sensor.Max);
    }

    private static void ApplySlice(List<SensorValue> values, SensorHistorySlice slice)
    {
        if (slice.ResetRequired)
            values.Clear();

        values.AddRange(slice.Values);
    }

    private static byte[] AppendJunkAndCopiedTrailer(byte[] member, byte[] junk)
    {
        byte[] payload = new byte[member.Length + junk.Length + 8];
        Buffer.BlockCopy(member, 0, payload, 0, member.Length);
        Buffer.BlockCopy(junk, 0, payload, member.Length, junk.Length);
        Buffer.BlockCopy(member, member.Length - 8, payload, member.Length + junk.Length, 8);
        return payload;
    }

    private static byte[] Concatenate(byte[] first, byte[] second)
    {
        byte[] combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static int GetHistoryCapacity(Sensor sensor)
    {
        FieldInfo valuesField = typeof(Sensor).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(valuesField);
        List<SensorValue> values = Assert.IsType<List<SensorValue>>(valuesField.GetValue(sensor));
        return values.Capacity;
    }

    private static bool TryDecodeForTest(byte[] compressedData, DateTime now, Action acceptedValue)
    {
        return Sensor.TryDecodeSensorValues(compressedData,
                                            now,
                                            TimeSpan.FromDays(1),
                                            (value, time) => acceptedValue(),
                                            out _,
                                            out _);
    }

    private static string CreateHighlyCompressibleHistoryPayload(int recordCount, DateTime time, float value)
    {
        int blockRecordCount = Math.Min(262_144, Math.Max(1, recordCount - 1));
        byte[] repeatedRecord = CreatePersistedRecord(0, value);
        byte[] repeatedBlock = new byte[repeatedRecord.Length * blockRecordCount];
        for (int offset = 0; offset < repeatedBlock.Length; offset += repeatedRecord.Length)
            Buffer.BlockCopy(repeatedRecord, 0, repeatedBlock, offset, repeatedRecord.Length);

        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.SmallestSize, true))
        {
            byte[] firstRecord = CreatePersistedRecord(time.ToBinary(), value);
            gzip.Write(firstRecord, 0, firstRecord.Length);

            int remainingRecords = recordCount - 1;
            while (remainingRecords > 0)
            {
                int recordsToWrite = Math.Min(blockRecordCount, remainingRecords);
                gzip.Write(repeatedBlock, 0, recordsToWrite * repeatedRecord.Length);
                remainingRecords -= recordsToWrite;
            }
        }

        return Convert.ToBase64String(compressed.ToArray());
    }

    private static byte[] CreatePersistedRecord(long timeDelta, float value)
    {
        byte[] record = new byte[sizeof(long) + sizeof(float)];
        Buffer.BlockCopy(BitConverter.GetBytes(timeDelta), 0, record, 0, sizeof(long));
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, record, sizeof(long), sizeof(float));
        return record;
    }

    private static string CompressHistoryBytes(byte[] bytes)
    {
        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.Optimal, true))
            gzip.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(compressed.ToArray());
    }

    private static SensorFixture CreateFixture(DateTime? start = null, TestSettings settings = null)
    {
        SimulatedClock clock = new(start ?? new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        settings ??= new TestSettings();
        TestHardware hardware = new(settings);
        Sensor sensor = CreateSensor(clock.GetUtcNow, settings, hardware);
        return new SensorFixture(sensor, clock, hardware, settings);
    }

    private static Sensor CreateSensor(Func<DateTime> utcNow)
    {
        TestSettings settings = new();
        TestHardware hardware = new(settings);
        return CreateSensor(utcNow, settings, hardware);
    }

    private static Sensor CreateSensor(Func<DateTime> utcNow, TestSettings settings, TestHardware hardware)
    {
        return new Sensor("Test Sensor",
                          0,
                          false,
                          SensorType.Temperature,
                          hardware,
                          Array.Empty<ParameterDescription>(),
                          settings,
                          false,
                          utcNow);
    }

    private static void RecordAveragedValue(SensorFixture fixture, float value)
    {
        for (int i = 0; i < 4; i++)
            SetValueAndAdvance(fixture, value);
    }

    private static void SetValueAndAdvance(SensorFixture fixture, float value)
    {
        fixture.Sensor.Value = value;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
    }

    private sealed class SensorFixture
    {
        public SensorFixture(Sensor sensor, SimulatedClock clock, TestHardware hardware, TestSettings settings)
        {
            Sensor = sensor;
            Clock = clock;
            Hardware = hardware;
            Settings = settings;
        }

        public Sensor Sensor { get; }

        public SimulatedClock Clock { get; }

        public TestHardware Hardware { get; }

        public TestSettings Settings { get; }
    }

    private sealed class SimulatedClock
    {
        private DateTime _utcNow;

        public SimulatedClock(DateTime utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTime GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }

        public void SetUtcNow(DateTime utcNow)
        {
            _utcNow = utcNow;
        }
    }

    private sealed class TestHardware : HardwareBase
    {
        public TestHardware(ISettings settings) : base("Test Hardware", new Identifier("test"), settings)
        { }

        public override HardwareType HardwareType => HardwareType.Cpu;

        public override void Update()
        { }
    }

    private sealed class TestSettings : ISettings
    {
        private readonly Dictionary<string, string> _values = new();

        public bool Contains(string name)
        {
            return _values.ContainsKey(name);
        }

        public void SetValue(string name, string value)
        {
            _values[name] = value;
        }

        public string GetValue(string name, string value)
        {
            return _values.TryGetValue(name, out string stored) ? stored : value;
        }

        public void Remove(string name)
        {
            _values.Remove(name);
        }
    }
}
