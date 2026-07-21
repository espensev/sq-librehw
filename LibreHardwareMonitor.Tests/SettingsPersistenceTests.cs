// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Regression coverage for the persistent settings bloat issue: unbounded "&lt;sensor&gt;/values"
/// history blobs (and stale blobs of departed hardware) grew the runtime .config to hundreds of
/// megabytes, making saves slow/brittle so user settings appeared to revert after restart.
/// </summary>
public sealed class SettingsPersistenceTests : IDisposable
{
    private const string NicValuesKey = "/nic/%7B7d5637a8-11d0-4671-b0f8-3a4bcbd4f81b%7D/throughput/1/values";
    private const string LiveConfigEnvironmentVariable = "LHM_LIVE_CONFIG_PATH";
    private const string LiveConfigExpectedLengthEnvironmentVariable = "LHM_LIVE_CONFIG_EXPECTED_LENGTH";
    private const long DefaultExpectedLiveConfigLength = 307_985_688;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lhm-settings-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public SettingsPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
            // Best effort cleanup of temp files.
        }
    }

    [Fact]
    public void Load_DropsOversizedSensorHistory_AndKeepsUserSettings()
    {
        string path = ConfigPath("oversized");
        WriteConfig(path,
                    (NicValuesKey, new string('A', PersistentSettings.MaxSensorValuesLength + 1)),
                    ("listenerIp", "+"),
                    ("listenerPort", "8085"),
                    ("runWebServerMenuItem", "true"));

        PersistentSettings settings = new();
        settings.Load(path);

        Assert.False(settings.Contains(NicValuesKey));
        Assert.True(settings.Modified);
        Assert.Equal("+", settings.GetValue("listenerIp", ""));
        Assert.Equal(8085, settings.GetValue("listenerPort", 0));
        Assert.True(settings.GetValue("runWebServerMenuItem", false));
    }

    [Fact]
    public void Load_KeepsBoundedSensorHistory_AndArmsCompactionAutosave()
    {
        string blob = new('A', 1024);
        string path = ConfigPath("bounded");
        WriteConfig(path, (NicValuesKey, blob));

        PersistentSettings settings = new();
        settings.Load(path);

        Assert.True(settings.Contains(NicValuesKey));
        Assert.True(settings.Modified);
        Assert.Equal(blob, settings.GetValue(NicValuesKey, null));
    }

    [Fact]
    public void Save_DropsSensorHistoryOfHardwareNoLongerPresent()
    {
        string path = ConfigPath("stale");
        WriteConfig(path,
                    (NicValuesKey, "c3RhbGU="),
                    ("/nic/%7B7d5637a8-11d0-4671-b0f8-3a4bcbd4f81b%7D/throughput/1/name", "Uplink"),
                    ("listenerPort", "8085"));

        PersistentSettings settings = new();
        settings.Load(path);

        // No sensor claims the history this session (hardware is gone); save and reload.
        string savedPath = ConfigPath("stale-saved");
        settings.Save(savedPath);

        Assert.False(settings.Contains(NicValuesKey));
        Assert.False(settings.Modified);

        PersistentSettings reloaded = new();
        reloaded.Load(savedPath);

        Assert.False(reloaded.Contains(NicValuesKey));

        // Real user settings for that hardware survive so it is restored if it comes back.
        Assert.Equal("Uplink", reloaded.GetValue("/nic/%7B7d5637a8-11d0-4671-b0f8-3a4bcbd4f81b%7D/throughput/1/name", null));
        Assert.Equal(8085, reloaded.GetValue("listenerPort", 0));
    }

    [Fact]
    public void Save_KeepsSensorHistoryClaimedAndRewrittenThisSession()
    {
        string path = ConfigPath("claimed");
        WriteConfig(path, (NicValuesKey, "b2xk"));

        PersistentSettings settings = new();
        settings.Load(path);

        // Sensor construction reads and removes the entry (Sensor.GetSensorValuesFromSettings).
        Assert.Equal("b2xk", settings.GetValue(NicValuesKey, null));
        settings.Remove(NicValuesKey);

        // Hardware close writes a fresh bounded payload (Sensor.SetSensorValuesToSettings).
        settings.SetValue(NicValuesKey, "bmV3");

        string savedPath = ConfigPath("claimed-saved");
        settings.Save(savedPath);

        PersistentSettings reloaded = new();
        reloaded.Load(savedPath);

        Assert.Equal("bmV3", reloaded.GetValue(NicValuesKey, null));
    }

    [Fact]
    public void Save_DropsOversizedSensorHistoryWrittenThisSession()
    {
        PersistentSettings settings = new();
        settings.SetValue(NicValuesKey, new string('B', PersistentSettings.MaxSensorValuesLength + 1));
        settings.SetValue("listenerPort", 8085);

        string savedPath = ConfigPath("oversized-saved");
        settings.Save(savedPath);

        PersistentSettings reloaded = new();
        reloaded.Load(savedPath);

        Assert.False(reloaded.Contains(NicValuesKey));
        Assert.Equal(8085, reloaded.GetValue("listenerPort", 0));
    }

    [Fact]
    public void LoadAndSave_DropPlotFalseNoise_ButKeepPlotTrueAndHiddenFlags()
    {
        string path = ConfigPath("plot");
        WriteConfig(path,
                    ("/intelcpu/0/temperature/0/plot", "false"),
                    ("/intelcpu/0/temperature/1/plot", "true"),
                    ("/intelcpu/0/temperature/2/hidden", "false"),
                    ("/intelcpu/0/temperature/3/hidden", "true"));

        PersistentSettings settings = new();
        settings.Load(path);

        Assert.False(settings.Contains("/intelcpu/0/temperature/0/plot"));
        Assert.True(settings.Modified);
        Assert.True(settings.GetValue("/intelcpu/0/temperature/1/plot", false));

        // "hidden=false" is meaningful for default-hidden sensors and must not be dropped.
        Assert.True(settings.Contains("/intelcpu/0/temperature/2/hidden"));
        Assert.True(settings.GetValue("/intelcpu/0/temperature/3/hidden", false));

        // A plot toggled on and back off during the session equals the default again.
        settings.SetValue("/gpu-nvidia/0/load/0/plot", false);

        string savedPath = ConfigPath("plot-saved");
        settings.Save(savedPath);

        PersistentSettings reloaded = new();
        reloaded.Load(savedPath);

        Assert.False(reloaded.Contains("/gpu-nvidia/0/load/0/plot"));
        Assert.True(reloaded.GetValue("/intelcpu/0/temperature/1/plot", false));
        Assert.True(reloaded.Contains("/intelcpu/0/temperature/2/hidden"));
        Assert.True(reloaded.GetValue("/intelcpu/0/temperature/3/hidden", false));
    }

    [Fact]
    public void SaveThenLoad_PreservesUserSettingsAcrossRestart()
    {
        PersistentSettings settings = new();
        settings.SetValue("listenerIp", "+");
        settings.SetValue("listenerPort", 8085);
        settings.SetValue("runWebServerMenuItem", true);
        settings.SetValue("authenticationEnabled", true);
        settings.SetValue("authenticationUserName", "admin");
        settings.SetValue("uiTextScale", 150);
        settings.SetValue("mainForm.Location.X", 42);
        settings.SetValue("mainForm.Width", 1280);
        settings.SetValue("treeView.Columns.Value.Width", 100);
        settings.SetValue("/intelcpu/0/temperature/0/name", "Core Custom");
        settings.SetValue("/intelcpu/0/temperature/0/hidden", true);
        settings.SetValue("/intelcpu/0/temperature/0/plot", true);
        settings.SetValue("/intelcpu/0/temperature/0/penColor", Color.FromArgb(255, 12, 34, 56));

        string savedPath = ConfigPath("user-settings");
        settings.Save(savedPath);

        PersistentSettings reloaded = new();
        reloaded.Load(savedPath);

        Assert.False(reloaded.Modified);
        Assert.Equal("+", reloaded.GetValue("listenerIp", ""));
        Assert.Equal(8085, reloaded.GetValue("listenerPort", 0));
        Assert.True(reloaded.GetValue("runWebServerMenuItem", false));
        Assert.True(reloaded.GetValue("authenticationEnabled", false));
        Assert.Equal("admin", reloaded.GetValue("authenticationUserName", ""));
        Assert.Equal(150, reloaded.GetValue("uiTextScale", 100));
        Assert.Equal(42, reloaded.GetValue("mainForm.Location.X", 0));
        Assert.Equal(1280, reloaded.GetValue("mainForm.Width", 0));
        Assert.Equal(100, reloaded.GetValue("treeView.Columns.Value.Width", 0));
        Assert.Equal("Core Custom", reloaded.GetValue("/intelcpu/0/temperature/0/name", ""));
        Assert.True(reloaded.GetValue("/intelcpu/0/temperature/0/hidden", false));
        Assert.True(reloaded.GetValue("/intelcpu/0/temperature/0/plot", false));
        Assert.Equal(Color.FromArgb(255, 12, 34, 56).ToArgb(), reloaded.GetValue("/intelcpu/0/temperature/0/penColor", Color.Black).ToArgb());
    }

    [Fact]
    public void Modified_TracksUnsavedChanges_ForAutosaveSkip()
    {
        // The autosave timer writes only when Modified is true, so this flag both prevents idle
        // disk churn and guarantees a genuine change is never skipped between clean saves.
        PersistentSettings settings = new();
        Assert.False(settings.Modified);

        settings.SetValue("listenerPort", 8085);
        Assert.True(settings.Modified);

        string savedPath = ConfigPath("modified");
        settings.Save(savedPath);
        Assert.False(settings.Modified);

        // Rewriting the identical value is a no-op and must not schedule another autosave.
        settings.SetValue("listenerPort", 8085);
        Assert.False(settings.Modified);

        // A genuine change re-arms autosave; removing a present key does too.
        settings.SetValue("listenerPort", 9090);
        Assert.True(settings.Modified);

        settings.Save(savedPath);
        settings.Remove("listenerPort");
        Assert.True(settings.Modified);
    }

    [Fact]
    public void Load_RecoversFromBackup_WhenPrimaryConfigIsCorrupt()
    {
        string path = ConfigPath("atomic");

        // First save creates the file; the second save rotates the previous good file to
        // "<path>.backup" as part of the atomic swap.
        PersistentSettings first = new();
        first.SetValue("listenerPort", 8085);
        first.Save(path);

        first.SetValue("listenerPort", 9090);
        first.Save(path);

        Assert.True(File.Exists(path + ".backup"));

        // Simulate a torn write / corruption of the live file after the swap.
        File.WriteAllText(path, "<configuration><appSettings><add key");

        PersistentSettings reloaded = new();
        reloaded.Load(path);

        // The previous good configuration (the backup) survives instead of collapsing to defaults.
        Assert.True(reloaded.Modified);
        Assert.Equal(8085, reloaded.GetValue("listenerPort", 0));
        byte[] validatedBackup = File.ReadAllBytes(path + ".backup");

        // The dirty recovery state repairs the primary on the next save.
        reloaded.Save(path);
        Assert.False(reloaded.Modified);
        Assert.Equal(validatedBackup, File.ReadAllBytes(path + ".backup"));

        PersistentSettings repaired = new();
        repaired.Load(path);
        Assert.False(repaired.Modified);
        Assert.Equal(8085, repaired.GetValue("listenerPort", 0));

        // A second primary failure must still recover from the validated backup. Repairing the
        // first failure must never rotate the corrupt primary over the last known-good copy.
        File.WriteAllText(path, "<configuration><appSettings><add key");
        PersistentSettings recoveredAgain = new();
        recoveredAgain.Load(path);
        Assert.True(recoveredAgain.Modified);
        Assert.Equal(8085, recoveredAgain.GetValue("listenerPort", 0));
    }

    [Fact]
    public void Load_RecoversFromBackup_WhenPrimaryConfigIsMissing()
    {
        string path = ConfigPath("missing-primary");
        WriteConfig(path + ".backup", ("listenerPort", "8085"));

        PersistentSettings settings = new();
        settings.Load(path);

        Assert.True(settings.Modified);
        Assert.Equal(8085, settings.GetValue("listenerPort", 0));
    }

    [Fact]
    public void Load_TransientPrimaryFailureBlocksSaveUntilSuccessfulReload()
    {
        string path = ConfigPath("locked-primary");
        WriteConfig(path, ("listenerPort", "9090"));
        WriteConfig(path + ".backup", ("listenerPort", "8085"));
        byte[] primaryBefore = File.ReadAllBytes(path);
        byte[] backupBefore = File.ReadAllBytes(path + ".backup");

        PersistentSettings settings = new();
        Assert.False(settings.Contains("listenerPort"));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Load(path);
            Assert.False(settings.Contains("listenerPort"));

            // Model startup projection after Load returned without a definitive configuration.
            settings.SetValue("listenerPort", 7070);
            IOException exception = Assert.ThrowsAny<IOException>(() => settings.Save(path));

            Assert.Contains("deferred", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(settings.Modified);
            Assert.Equal(7070, settings.GetValue("listenerPort", 0));
        }

        Assert.False(File.Exists(path + ".new"));
        Assert.Equal(primaryBefore, File.ReadAllBytes(path));
        Assert.True(File.Exists(path + ".backup"));
        Assert.Equal(backupBefore, File.ReadAllBytes(path + ".backup"));

        // A later explicit retry reads the still-valid primary once the transient lock is gone.
        settings.Load(path);
        Assert.False(settings.Modified);
        Assert.Equal(9090, settings.GetValue("listenerPort", 0));

        // The successful retry clears the write block.
        settings.SetValue("listenerPort", 10001);
        settings.Save(path);

        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal(10001, reloaded.GetValue("listenerPort", 0));
    }

    [Fact]
    public void Load_TransientBackupFailureBlocksSaveUntilBackupCanBeRead()
    {
        string path = ConfigPath("locked-recovery-backup");
        File.WriteAllText(path, "<configuration><appSettings><add key");
        WriteConfig(path + ".backup", ("listenerPort", "8085"));
        byte[] primaryBefore = File.ReadAllBytes(path);
        byte[] backupBefore = File.ReadAllBytes(path + ".backup");

        PersistentSettings settings = new();
        using (new FileStream(path + ".backup", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Load(path);
            settings.SetValue("listenerPort", 7070);

            IOException exception = Assert.ThrowsAny<IOException>(() => settings.Save(path));
            Assert.Contains("deferred", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(settings.Modified);
        }

        Assert.False(File.Exists(path + ".new"));
        Assert.Equal(primaryBefore, File.ReadAllBytes(path));
        Assert.Equal(backupBefore, File.ReadAllBytes(path + ".backup"));

        settings.Load(path);
        Assert.True(settings.Modified); // backup recovery still needs to repair the corrupt primary
        Assert.Equal(8085, settings.GetValue("listenerPort", 0));

        settings.Save(path);
        PersistentSettings repaired = new();
        repaired.Load(path);
        Assert.Equal(8085, repaired.GetValue("listenerPort", 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_DefinitiveMissingOrCorruptCandidatesClearTransientSaveBlock(bool corruptCandidates)
    {
        string path = ConfigPath(corruptCandidates ? "clear-block-corrupt" : "clear-block-missing");
        WriteConfig(path, ("listenerPort", "9090"));
        WriteConfig(path + ".backup", ("listenerPort", "8085"));

        PersistentSettings settings = new();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Load(path);
            settings.SetValue("listenerPort", 7070);
            Assert.ThrowsAny<IOException>(() => settings.Save(path));
        }

        if (corruptCandidates)
        {
            File.WriteAllText(path, "<configuration><appSettings><add key");
            File.WriteAllText(path + ".backup", "<configuration><appSettings><add key");
        }
        else
        {
            File.Delete(path);
            File.Delete(path + ".backup");
        }

        // Both candidates now have a definitive classification. The failed load retains the
        // in-memory change but clears the safety block so it can become the new configuration.
        settings.Load(path);
        Assert.True(settings.Modified);
        Assert.Equal(7070, settings.GetValue("listenerPort", 0));

        settings.Save(path);
        Assert.False(settings.Modified);

        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal(7070, reloaded.GetValue("listenerPort", 0));
    }

    [Fact]
    public async Task Save_FinalShutdownSaveWaitsForInFlightAutosaveAndPersistsLatestSnapshot()
    {
        string path = ConfigPath("overlapping");
        using ManualResetEventSlim firstWriteStarted = new(false);
        using ManualResetEventSlim releaseFirstWrite = new(false);
        using ManualResetEventSlim secondSaveEntered = new(false);
        using ManualResetEventSlim secondSerializationAcquired = new(false);
        int writeCount = 0;
        int enteredCount = 0;
        int serializationCount = 0;

        PersistentSettings settings = new((fileName, contents) =>
        {
            if (Interlocked.Increment(ref writeCount) == 1)
            {
                firstWriteStarted.Set();
                if (!releaseFirstWrite.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting to release the first settings write.");
            }

            File.WriteAllBytes(fileName, contents);
        }, stage =>
        {
            if (stage == PersistentSettings.SaveStage.Entered && Interlocked.Increment(ref enteredCount) == 2)
                secondSaveEntered.Set();
            else if (stage == PersistentSettings.SaveStage.SerializationAcquired && Interlocked.Increment(ref serializationCount) == 2)
                secondSerializationAcquired.Set();
        });

        settings.SetValue("listenerPort", 8085);
        // Model a periodic autosave that has already snapshotted the old value and is in flight.
        Task firstSave = Task.Run(() => settings.Save(path));
        Assert.True(firstWriteStarted.Wait(TimeSpan.FromSeconds(10)));

        settings.SetValue("listenerPort", 9090);
        // Model final shutdown projection changing a setting before issuing its final save.
        Task secondSave = Task.Run(() => settings.Save(path));
        Assert.True(secondSaveEntered.Wait(TimeSpan.FromSeconds(10)));

        try
        {
            // The internal entry signal proves Save is running; absence of the serialization signal
            // proves it cannot snapshot or clear the newer dirty value while the first write owns
            // the complete snapshot/write critical section.
            Assert.False(secondSerializationAcquired.Wait(TimeSpan.FromMilliseconds(250)));
            Assert.True(settings.Modified);
        }
        finally
        {
            releaseFirstWrite.Set();
            await Task.WhenAll(firstSave, secondSave).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.True(secondSerializationAcquired.IsSet);
        Assert.False(settings.Modified);
        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal(9090, reloaded.GetValue("listenerPort", 0));
    }

    [LiveConfigFact]
    public async Task LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets()
    {
        string sourcePath = Environment.GetEnvironmentVariable(LiveConfigEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(sourcePath));
        Assert.True(File.Exists(sourcePath));

        string copiedPath = ConfigPath("live-copy");
        string compactedPath = ConfigPath("live-compacted");
        File.Copy(sourcePath, copiedPath);
        Assert.Equal(GetExpectedLiveConfigLength(), new FileInfo(copiedPath).Length);

        ForceFullCollection();
        long baselineManagedBytes = GC.GetTotalMemory(false);
        long peakWorkingSetBytes = GetCurrentWorkingSet();
        object peakSync = new();
        using CancellationTokenSource stopSampling = new();
        using ManualResetEventSlim samplerStarted = new(false);
        Task sampler = Task.Run(async () =>
        {
            long initialWorkingSetBytes = GetCurrentWorkingSet();
            lock (peakSync)
                peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, initialWorkingSetBytes);
            samplerStarted.Set();

            while (!stopSampling.IsCancellationRequested)
            {
                long workingSetBytes = GetCurrentWorkingSet();
                lock (peakSync)
                    peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, workingSetBytes);

                await Task.Delay(5);
            }
        });
        Assert.True(samplerStarted.Wait(TimeSpan.FromSeconds(10)));

        PersistentSettings settings = new();
        try
        {
            settings.Load(copiedPath);
        }
        finally
        {
            stopSampling.Cancel();
            await sampler.WaitAsync(TimeSpan.FromSeconds(10));
        }

        ForceFullCollection();
        long postLoadManagedBytes = GC.GetTotalMemory(false);
        lock (peakSync)
            peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, GetCurrentWorkingSet());

        Assert.True(settings.Modified); // bounded histories must arm the real autosave predicate
        Assert.True(peakWorkingSetBytes < 256L * 1024 * 1024,
                    $"Peak working set was {ToMiB(peakWorkingSetBytes):F1} MiB.");
        Assert.True(postLoadManagedBytes < 100L * 1024 * 1024,
                    $"Post-GC managed memory was {ToMiB(postLoadManagedBytes):F1} MiB.");

        settings.Save(compactedPath);
        Assert.False(settings.Modified);
        ForceFullCollection();
        long postCompactionManagedBytes = GC.GetTotalMemory(false);

        Dictionary<string, string> expectedNormalSettings = ReadNormalSettings(copiedPath, out int sourceHistoryCount);
        Dictionary<string, string> compactedNormalSettings = ReadNormalSettings(compactedPath, out int compactedHistoryCount);

        Assert.True(sourceHistoryCount > 0);
        Assert.Equal(0, compactedHistoryCount);
        Assert.True(new FileInfo(compactedPath).Length < 40L * 1024 * 1024);
        Assert.Equal(expectedNormalSettings.Count, compactedNormalSettings.Count);
        foreach (KeyValuePair<string, string> setting in expectedNormalSettings)
        {
            Assert.True(compactedNormalSettings.TryGetValue(setting.Key, out string actualValue),
                        $"Compaction dropped normal setting '{setting.Key}'.");
            Assert.True(string.Equals(setting.Value, actualValue, StringComparison.Ordinal),
                        $"Compaction changed normal setting '{setting.Key}'.");
        }

        _output.WriteLine(
            "Live config harness: source={0:F1} MiB, compacted={1:F1} MiB, peak working set={2:F1} MiB, " +
            "managed baseline={3:F1} MiB, post-load={4:F1} MiB, post-compaction={5:F1} MiB, stale histories removed={6}.",
            ToMiB(new FileInfo(copiedPath).Length),
            ToMiB(new FileInfo(compactedPath).Length),
            ToMiB(peakWorkingSetBytes),
            ToMiB(baselineManagedBytes),
            ToMiB(postLoadManagedBytes),
            ToMiB(postCompactionManagedBytes),
            sourceHistoryCount);
    }

    [Fact]
    public void Save_KeepsLiveConfig_WhenBackupCannotBePreserved()
    {
        string path = ConfigPath("locked-backup");

        PersistentSettings settings = new();
        settings.SetValue("listenerPort", 8085);
        settings.Save(path);

        // Lock the backup path exclusively so File.Replace fails (forcing the copy-based
        // fallback) and the backup copy fails as well. The save must then throw instead of
        // deleting the live file, otherwise a crash here would lose the last good config.
        using (new FileStream(path + ".backup", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            settings.SetValue("listenerPort", 9090);
            Assert.ThrowsAny<IOException>(() => settings.Save(path));
        }

        // The failed save re-arms the dirty flag so a later (auto)save retries.
        Assert.True(settings.Modified);

        // The live file still holds the previous good configuration.
        PersistentSettings reloaded = new();
        reloaded.Load(path);
        Assert.Equal(8085, reloaded.GetValue("listenerPort", 0));
    }

    [Fact]
    public void SensorHistoryEncoding_TrimsOldestSamplesToStayWithinBudget()
    {
        // Poorly compressible data (random floats) models NIC throughput history that used to
        // produce ~800 KB payloads per sensor.
        Random random = new(12345);
        var values = new SensorValue[200_000];
        DateTime start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < values.Length; i++)
            values[i] = new SensorValue((float)(random.NextDouble() * 1_000_000), start.AddSeconds(i));

        string encoded = Sensor.EncodeValuesBounded(values, Sensor.MaxPersistedValuesLength);

        Assert.True(encoded.Length <= Sensor.MaxPersistedValuesLength);
        Assert.True(encoded.Length <= PersistentSettings.MaxSensorValuesLength); // caps stay in sync

        List<(DateTime Time, float Value)> decoded = DecodeValues(encoded);
        Assert.NotEmpty(decoded);
        Assert.Equal(values[values.Length - 1].Time, decoded[decoded.Count - 1].Time); // newest kept
        Assert.True(decoded[0].Time > values[0].Time); // oldest trimmed away
    }

    [Fact]
    public void SensorHistoryEncoding_KeepsAllSamplesWhenWithinBudget()
    {
        var values = new SensorValue[1_000];
        DateTime start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < values.Length; i++)
            values[i] = new SensorValue(42f, start.AddSeconds(i));

        string encoded = Sensor.EncodeValuesBounded(values, Sensor.MaxPersistedValuesLength);

        Assert.True(encoded.Length <= Sensor.MaxPersistedValuesLength);

        List<(DateTime Time, float Value)> decoded = DecodeValues(encoded);
        Assert.Equal(values.Length, decoded.Count);
        Assert.Equal(values[0].Time, decoded[0].Time);
        Assert.All(decoded, v => Assert.Equal(42f, v.Value));
    }

    private string ConfigPath(string name)
    {
        return Path.Combine(_directory, name + ".config");
    }

    private static void WriteConfig(string path, params (string Key, string Value)[] entries)
    {
        XmlDocument doc = new();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
        XmlElement configuration = doc.CreateElement("configuration");
        doc.AppendChild(configuration);
        XmlElement appSettings = doc.CreateElement("appSettings");
        configuration.AppendChild(appSettings);

        foreach ((string key, string value) in entries)
        {
            XmlElement add = doc.CreateElement("add");
            add.SetAttribute("key", key);
            add.SetAttribute("value", value);
            appSettings.AppendChild(add);
        }

        doc.Save(path);
    }

    private static Dictionary<string, string> ReadNormalSettings(string path, out int sensorHistoryCount)
    {
        Dictionary<string, string> result = new();
        sensorHistoryCount = 0;
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

        using XmlReader reader = XmlReader.Create(path, readerSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "add")
                continue;

            string key = reader.GetAttribute("key");
            if (key == null)
                continue;

            if (key.EndsWith("/values", StringComparison.Ordinal))
            {
                sensorHistoryCount++;
                continue;
            }

            string value = reader.GetAttribute("value");
            if (value == null || (key.EndsWith("/plot", StringComparison.Ordinal) && value == "false"))
                continue;

            result[key] = value;
        }

        return result;
    }

    private static long GetCurrentWorkingSet()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double ToMiB(long bytes)
    {
        return bytes / (1024d * 1024d);
    }

    private static long GetExpectedLiveConfigLength()
    {
        string configuredLength = Environment.GetEnvironmentVariable(LiveConfigExpectedLengthEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredLength))
            return DefaultExpectedLiveConfigLength;

        if (!long.TryParse(configuredLength, out long expectedLength) || expectedLength <= 0)
        {
            throw new InvalidOperationException(
                $"{LiveConfigExpectedLengthEnvironmentVariable} must be a positive byte count.");
        }

        return expectedLength;
    }

    private sealed class LiveConfigFactAttribute : FactAttribute
    {
        public LiveConfigFactAttribute()
        {
            string sourcePath = Environment.GetEnvironmentVariable(LiveConfigEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                Skip = $"Set {LiveConfigEnvironmentVariable} to a copied/live config path to run the opt-in loader harness.";
        }
    }

    /// <summary>Mirrors the decoding in Sensor.GetSensorValuesFromSettings.</summary>
    private static List<(DateTime Time, float Value)> DecodeValues(string encoded)
    {
        List<(DateTime, float)> result = new();

        using MemoryStream memory = new(Convert.FromBase64String(encoded));
        using GZipStream gzip = new(memory, CompressionMode.Decompress);
        using MemoryStream destination = new();

        gzip.CopyTo(destination);
        destination.Seek(0, SeekOrigin.Begin);

        using BinaryReader reader = new(destination);
        long t = 0;
        while (destination.Position < destination.Length)
        {
            t += reader.ReadInt64();
            result.Add((DateTime.FromBinary(t), reader.ReadSingle()));
        }

        return result;
    }
}
