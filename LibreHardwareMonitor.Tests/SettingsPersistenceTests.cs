// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Xml;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Regression coverage for the persistent settings bloat issue: unbounded "&lt;sensor&gt;/values"
/// history blobs (and stale blobs of departed hardware) grew the runtime .config to hundreds of
/// megabytes, making saves slow/brittle so user settings appeared to revert after restart.
/// </summary>
public sealed class SettingsPersistenceTests : IDisposable
{
    private const string NicValuesKey = "/nic/%7B7d5637a8-11d0-4671-b0f8-3a4bcbd4f81b%7D/throughput/1/values";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lhm-settings-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsPersistenceTests()
    {
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
        Assert.Equal("+", settings.GetValue("listenerIp", ""));
        Assert.Equal(8085, settings.GetValue("listenerPort", 0));
        Assert.True(settings.GetValue("runWebServerMenuItem", false));
    }

    [Fact]
    public void Load_KeepsBoundedSensorHistory()
    {
        string blob = new('A', 1024);
        string path = ConfigPath("bounded");
        WriteConfig(path, (NicValuesKey, blob));

        PersistentSettings settings = new();
        settings.Load(path);

        Assert.True(settings.Contains(NicValuesKey));
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
