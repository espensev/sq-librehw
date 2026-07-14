// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class PersistentSettings : ISettings
{
    // Upper bound for a persisted "<sensor>/values" history blob. Sensor.SetSensorValuesToSettings
    // (LibreHardwareMonitorLib) trims its payload to stay within this budget; anything larger
    // (written by older builds) is dropped on load/save so sensor history cannot bloat the
    // settings file and destabilize saving user settings.
    internal const int MaxSensorValuesLength = 64 * 1024;

    private const string SensorValuesKeySuffix = "/values";
    private const string PlotKeySuffix = "/plot";

    private enum LoadResult
    {
        Success,
        Missing,
        Corrupt,
        TransientFailure
    }

    internal enum SaveStage
    {
        Entered,
        SerializationAcquired
    }

    private readonly IDictionary<string, string> _settings = new Dictionary<string, string>();

    private readonly Action<string, byte[], bool> _writeFile;
    private readonly Action<SaveStage> _saveStageObserver;

    // "<sensor>/values" keys loaded from disk that no sensor claimed (via Remove) or refreshed
    // (via SetValue) during this session. They belong to hardware that is no longer present and
    // are skipped on Save so stale history cannot accumulate forever.
    private readonly HashSet<string> _unclaimedSensorValues = new HashSet<string>();

    // Guards _settings, _unclaimedSensorValues, _modified and the transient-load write block so
    // periodic autosave (on the UI thread) and SessionEnded (on a system thread) stay consistent.
    private readonly object _sync = new object();

    // Serializes file writes so two overlapping Save calls cannot fight over the temp/backup files.
    private readonly object _ioSync = new object();

    // True when an in-memory change has not yet been persisted. Autosave uses this to skip writing
    // when nothing changed, avoiding needless disk churn while the app sits idle in the tray.
    private bool _modified;

    // A transient read failure leaves it unknown whether the primary or recovery file is newer.
    // Never write that uncertain state back until a later definitive Load clears this block.
    private bool _saveBlockedByTransientLoad;

    // When Load recovered from "<fileName>.backup", the primary is known bad or missing while the
    // backup is the last validated copy. The first repair save must replace only the primary; the
    // normal rotation would otherwise overwrite that good backup with the corrupt primary.
    private long _backupRecoveryGeneration;

    public PersistentSettings()
        : this(WriteFileAtomic, null)
    { }

    internal PersistentSettings(Action<string, byte[]> writeFile, Action<SaveStage> saveStageObserver = null)
        : this(AdaptWriteFile(writeFile), saveStageObserver)
    { }

    private PersistentSettings(Action<string, byte[], bool> writeFile, Action<SaveStage> saveStageObserver)
    {
        _writeFile = writeFile ?? throw new ArgumentNullException(nameof(writeFile));
        _saveStageObserver = saveStageObserver;
    }

    private static Action<string, byte[], bool> AdaptWriteFile(Action<string, byte[]> writeFile)
    {
        if (writeFile == null)
            throw new ArgumentNullException(nameof(writeFile));

        return (fileName, contents, _) => writeFile(fileName, contents);
    }

    // True when at least one setting has changed since the last successful Save (or since Load).
    // Read by the autosave path to avoid rewriting an unchanged configuration file.
    internal bool Modified
    {
        get
        {
            lock (_sync)
                return _modified;
        }
    }

    public void Load(string fileName)
    {
        // Block any Save that starts while this load is unresolved. A successful load or a
        // definitive missing/corrupt result clears the block; transient access failures retain it.
        lock (_sync)
            _saveBlockedByTransientLoad = true;

        // Try the live file first. A missing or malformed primary may recover from the backup left
        // by the previous successful save; a transient access failure is deferred because it is
        // not evidence that an older backup should replace the current configuration.
        bool loadedFromBackup = false;
        LoadResult loadResult = TryLoadSettings(fileName, out Dictionary<string, string> loadedSettings, out HashSet<string> loadedUnclaimedSensorValues, out bool cleanupApplied);
        if (loadResult == LoadResult.TransientFailure)
            return;

        if (loadResult == LoadResult.Missing || loadResult == LoadResult.Corrupt)
        {
            loadResult = TryLoadSettings(fileName + ".backup", out loadedSettings, out loadedUnclaimedSensorValues, out cleanupApplied);
            loadedFromBackup = loadResult == LoadResult.Success;
        }

        if (loadResult == LoadResult.TransientFailure)
            return;

        if (loadResult != LoadResult.Success)
        {
            lock (_sync)
                _saveBlockedByTransientLoad = false;

            return;
        }

        lock (_sync)
        {
            _settings.Clear();
            _unclaimedSensorValues.Clear();

            foreach (KeyValuePair<string, string> setting in loadedSettings)
                _settings[setting.Key] = setting.Value;

            foreach (string key in loadedUnclaimedSensorValues)
                _unclaimedSensorValues.Add(key);

            // If load had to discard stale/noisy data, or had to recover from the backup file,
            // keep the store dirty so the next autosave compacts/restores the live config even
            // when the user does not change a setting.
            _modified = cleanupApplied || loadedFromBackup || loadedUnclaimedSensorValues.Count > 0;
            _backupRecoveryGeneration = loadedFromBackup ? _backupRecoveryGeneration + 1 : 0;
            _saveBlockedByTransientLoad = false;
        }
    }

    // Streams settings from disk instead of loading the whole .config into an XmlDocument. Older
    // builds could write hundreds of megabytes of sensor history, and building a DOM for that file
    // is the startup memory spike this cleanup path is meant to avoid.
    private static LoadResult TryLoadSettings(
        string fileName,
        out Dictionary<string, string> loadedSettings,
        out HashSet<string> loadedUnclaimedSensorValues,
        out bool cleanupApplied)
    {
        loadedSettings = new Dictionary<string, string>();
        loadedUnclaimedSensorValues = new HashSet<string>();
        cleanupApplied = false;

        try
        {
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };

            using XmlReader reader = XmlReader.Create(fileName, readerSettings);

            bool inConfiguration = false;
            bool inAppSettings = false;
            int configurationDepth = -1;
            int appSettingsDepth = -1;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!inConfiguration && reader.Depth == 0 && reader.Name == "configuration")
                    {
                        inConfiguration = true;
                        configurationDepth = reader.Depth;
                        if (reader.IsEmptyElement)
                            inConfiguration = false;

                        continue;
                    }

                    if (!inConfiguration)
                        continue;

                    if (!inAppSettings && reader.Depth == configurationDepth + 1 && reader.Name == "appSettings")
                    {
                        inAppSettings = true;
                        appSettingsDepth = reader.Depth;
                        if (reader.IsEmptyElement)
                            inAppSettings = false;

                        continue;
                    }

                    if (inAppSettings && reader.Depth == appSettingsDepth + 1 && reader.Name == "add")
                    {
                        string key = reader.GetAttribute("key");
                        string value = reader.GetAttribute("value");
                        if (key == null || value == null)
                            continue;

                        // Migration/cleanup: skip oversized sensor history blobs and
                        // default-only noise written by older builds.
                        if (!IsPersistableSetting(key, value))
                        {
                            cleanupApplied = true;
                            continue;
                        }

                        // Last-wins on a duplicate key so a hand-edited or partially corrupt file
                        // cannot throw and abort startup. Saving will normalize duplicates away.
                        if (loadedSettings.ContainsKey(key))
                            cleanupApplied = true;

                        loadedSettings[key] = value;

                        if (IsSensorValuesKey(key))
                            loadedUnclaimedSensorValues.Add(key);
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (inAppSettings && reader.Depth == appSettingsDepth && reader.Name == "appSettings")
                    {
                        inAppSettings = false;
                        appSettingsDepth = -1;
                    }
                    else if (inConfiguration && reader.Depth == configurationDepth && reader.Name == "configuration")
                    {
                        inConfiguration = false;
                        configurationDepth = -1;
                    }
                }
            }

            return LoadResult.Success;
        }
        catch (FileNotFoundException)
        {
            return LoadResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return LoadResult.Missing;
        }
        catch (XmlException)
        {
            return LoadResult.Corrupt;
        }
        catch (Exception exception) when (exception is IOException ||
                                          exception is UnauthorizedAccessException ||
                                          exception is System.Security.SecurityException)
        {
            // Sharing violations, access failures and other I/O errors are not evidence that the
            // primary XML is corrupt. Leave the current in-memory state untouched and defer rather
            // than rolling back to an older backup that could later replace a valid primary file.
            return LoadResult.TransientFailure;
        }
    }

    public void Save(string fileName)
    {
        _saveStageObserver?.Invoke(SaveStage.Entered);

        // Serialize the snapshot as well as the file swap. If two saves overlap, the later caller
        // must snapshot only after the earlier write completes; otherwise it can clear Modified,
        // write first, and then be overwritten by the older snapshot.
        lock (_ioSync)
        {
            _saveStageObserver?.Invoke(SaveStage.SerializationAcquired);

            // Snapshot the entries to persist under the state lock, then release it before doing
            // file I/O so regular setting readers/writers are never blocked on disk. Changes made
            // during the write re-dirty the flag; a failed write also re-arms it for retry.
            List<KeyValuePair<string, string>> entries;
            List<string> staleSensorValues;
            long backupRecoveryGeneration;
            lock (_sync)
            {
                if (_saveBlockedByTransientLoad)
                {
                    throw new IOException(
                        "Saving settings is deferred because the previous load encountered a transient read failure. " +
                        "Call Load again after the configuration files become readable.");
                }

                entries = new List<KeyValuePair<string, string>>(_settings.Count);
                staleSensorValues = new List<string>();
                foreach (KeyValuePair<string, string> keyValuePair in _settings)
                {
                    // Sensor history that was loaded but never claimed by a live sensor this
                    // session belongs to hardware that is gone; omit it from the snapshot.
                    if (_unclaimedSensorValues.Contains(keyValuePair.Key))
                    {
                        staleSensorValues.Add(keyValuePair.Key);
                        continue;
                    }

                    if (!IsPersistableSetting(keyValuePair.Key, keyValuePair.Value))
                        continue;

                    entries.Add(keyValuePair);
                }

                _modified = false;
                backupRecoveryGeneration = _backupRecoveryGeneration;
            }

            XmlDocument doc = new XmlDocument();
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
            XmlElement configuration = doc.CreateElement("configuration");
            doc.AppendChild(configuration);
            XmlElement appSettings = doc.CreateElement("appSettings");
            configuration.AppendChild(appSettings);
            foreach (KeyValuePair<string, string> keyValuePair in entries)
            {
                XmlElement add = doc.CreateElement("add");
                add.SetAttribute("key", keyValuePair.Key);
                add.SetAttribute("value", keyValuePair.Value);
                appSettings.AppendChild(add);
            }

            byte[] file;
            using (var memory = new MemoryStream())
            {
                using (var writer = new StreamWriter(memory, Encoding.UTF8))
                {
                    doc.Save(writer);
                }
                file = memory.ToArray();
            }

            try
            {
                _writeFile(fileName, file, backupRecoveryGeneration != 0);
            }
            catch
            {
                // The bytes did not land on disk. Re-mark the store dirty so a later (auto)save
                // retries rather than assuming the file already reflects the current state.
                lock (_sync)
                    _modified = true;

                throw;
            }

            // The successful snapshot omitted these stale histories. Release their strings from
            // the resident store, but do not remove a key that a live sensor claimed or refreshed
            // while the file write was in progress.
            lock (_sync)
            {
                if (_backupRecoveryGeneration == backupRecoveryGeneration)
                    _backupRecoveryGeneration = 0;

                foreach (string key in staleSensorValues)
                {
                    if (_unclaimedSensorValues.Remove(key))
                        _settings.Remove(key);
                }
            }
        }
    }

    // Writes the settings bytes without ever risking the loss of an existing good file. The content
    // is fully written and flushed to a temporary file first, then swapped into place while the
    // previous file is retained as "<fileName>.backup". A crash or power loss during the swap can
    // therefore never leave the user with only defaults: either the new file or the backup survives.
    private static void WriteFileAtomic(string fileName, byte[] contents, bool preserveExistingBackup)
    {
        string backupFileName = fileName + ".backup";
        string tempFileName = fileName + ".new";

        try
        {
            File.Delete(tempFileName);
        }
        catch { }

        using (var stream = new FileStream(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents, 0, contents.Length);
            // Force the data (not just the OS cache) to the physical disk before the swap.
            stream.Flush(true);
        }

        if (File.Exists(fileName))
        {
            try
            {
                // Atomic on NTFS. During ordinary saves the previous primary becomes the backup.
                // During recovery, keep the already-validated backup in place and replace only the
                // corrupt/missing primary; File.Replace accepts null when no rotation is wanted.
                File.Replace(
                    tempFileName,
                    fileName,
                    preserveExistingBackup ? null : backupFileName,
                    ignoreMetadataErrors: true);
                return;
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
            {
                // File.Replace needs both paths on one volume and is unsupported on some file
                // systems (e.g. certain network shares). Fall back to a copy-based swap that still
                // preserves a backup, at the cost of atomicity on those file systems.
            }

            if (!preserveExistingBackup)
            {
                try
                {
                    File.Delete(backupFileName);
                }
                catch { }

                // If the previous good file cannot be preserved as the backup, fail the save before
                // touching the live file: proceeding into the delete+move below could otherwise leave
                // neither a live config nor a backup. The caller re-marks the store dirty on throw, so
                // a later (auto)save retries.
                File.Copy(fileName, backupFileName, overwrite: true);
            }
        }

        // First save (nothing to preserve) or the copy-based fallback: move the fully written temp
        // file into place. Delete first because File.Move does not overwrite on .NET Framework.
        try
        {
            File.Delete(fileName);
        }
        catch { }

        File.Move(tempFileName, fileName);
    }

    public bool Contains(string name)
    {
        lock (_sync)
            return _settings.ContainsKey(name);
    }

    public void SetValue(string name, string value)
    {
        SetValueInternal(name, value);
    }

    public string GetValue(string name, string value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string result))
                return result;
        }

        return value;
    }

    public void Remove(string name)
    {
        lock (_sync)
        {
            bool removed = _settings.Remove(name);
            _unclaimedSensorValues.Remove(name);
            if (removed)
                _modified = true;
        }
    }

    // Records value under name, marking the store modified only when the stored text actually
    // changes (or when a previously unclaimed sensor-history key becomes claimed). Callers pass the
    // exact on-disk string so the persisted format is unchanged.
    private void SetValueInternal(string name, string value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string existing) && existing == value)
            {
                if (_unclaimedSensorValues.Remove(name))
                    _modified = true;

                return;
            }

            _settings[name] = value;
            _unclaimedSensorValues.Remove(name);
            _modified = true;
        }
    }

    private static bool IsSensorValuesKey(string key)
    {
        return key.EndsWith(SensorValuesKeySuffix, StringComparison.Ordinal);
    }

    private static bool IsPersistableSetting(string key, string value)
    {
        // Sensor history blobs beyond the budget stem from builds without a cap; they make the
        // settings file huge, slow and brittle, so they are not kept.
        if (IsSensorValuesKey(key) && value != null && value.Length > MaxSensorValuesLength)
            return false;

        // "plot" defaults to false; persisted "false" entries are default-only noise that older
        // builds wrote for every sensor ever seen. "hidden" is not filtered because its default
        // is per-sensor (IsDefaultHidden).
        if (key.EndsWith(PlotKeySuffix, StringComparison.Ordinal) && value == "false")
            return false;

        return true;
    }

    public void SetValue(string name, int value)
    {
        SetValueInternal(name, value.ToString());
    }

    public int GetValue(string name, int value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string str))
            {
                if (int.TryParse(str, out int parsedValue))
                    return parsedValue;

                return value;
            }
        }

        return value;
    }

    public void SetValue(string name, float value)
    {
        SetValueInternal(name, value.ToString(CultureInfo.InvariantCulture));
    }

    public float GetValue(string name, float value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string str))
            {
                if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue))
                    return parsedValue;
            }
        }

        return value;
    }

    public double GetValue(string name, double value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string str))
            {
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
                    return parsedValue;
            }
        }

        return value;
    }
        
    public void SetValue(string name, bool value)
    {
        SetValueInternal(name, value ? "true" : "false");
    }

    public bool GetValue(string name, bool value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string str))
                return str == "true";
        }

        return value;
    }

    public void SetValue(string name, Color color)
    {
        SetValueInternal(name, color.ToArgb().ToString("X8"));
    }

    public Color GetValue(string name, Color value)
    {
        lock (_sync)
        {
            if (_settings.TryGetValue(name, out string str))
            {
                if (int.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedValue))
                    return Color.FromArgb(parsedValue);
            }
        }

        return value;
    }
}
