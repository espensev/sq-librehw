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

    private readonly IDictionary<string, string> _settings = new Dictionary<string, string>();

    // "<sensor>/values" keys loaded from disk that no sensor claimed (via Remove) or refreshed
    // (via SetValue) during this session. They belong to hardware that is no longer present and
    // are skipped on Save so stale history cannot accumulate forever.
    private readonly HashSet<string> _unclaimedSensorValues = new HashSet<string>();

    // Guards _settings, _unclaimedSensorValues and _modified so periodic autosave (on the UI
    // thread) and the SessionEnded handler (on a system thread) cannot corrupt shared state.
    private readonly object _sync = new object();

    // Serializes file writes so two overlapping Save calls cannot fight over the temp/backup files.
    private readonly object _ioSync = new object();

    // True when an in-memory change has not yet been persisted. Autosave uses this to skip writing
    // when nothing changed, avoiding needless disk churn while the app sits idle in the tray.
    private bool _modified;

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
        // Try the live file first, then the backup left by the previous successful save. Neither
        // file is deleted on failure: a torn write or transient read error must never destroy the
        // user's last good configuration and silently fall back to defaults.
        XmlDocument doc = TryLoadXml(fileName) ?? TryLoadXml(fileName + ".backup");
        if (doc == null)
            return;

        lock (_sync)
        {
            _settings.Clear();
            _unclaimedSensorValues.Clear();

            XmlNodeList list = doc.GetElementsByTagName("appSettings");
            foreach (XmlNode node in list)
            {
                XmlNode parent = node.ParentNode;
                if (parent != null && parent.Name == "configuration" && parent.ParentNode is XmlDocument)
                {
                    foreach (XmlNode child in node.ChildNodes)
                    {
                        if (child.Name == "add")
                        {
                            XmlAttributeCollection attributes = child.Attributes;
                            XmlAttribute keyAttribute = attributes["key"];
                            XmlAttribute valueAttribute = attributes["value"];
                            if (keyAttribute != null && valueAttribute != null && keyAttribute.Value != null)
                            {
                                string key = keyAttribute.Value;
                                string value = valueAttribute.Value;

                                // Migration/cleanup: skip oversized sensor history blobs and
                                // default-only noise written by older builds.
                                if (!IsPersistableSetting(key, value))
                                    continue;

                                // Last-wins on a duplicate key so a hand-edited or partially
                                // corrupt file cannot throw and abort startup.
                                _settings[key] = value;

                                if (IsSensorValuesKey(key))
                                    _unclaimedSensorValues.Add(key);
                            }
                        }
                    }
                }
            }

            _modified = false;
        }
    }

    // Loads an XML document, returning null (instead of throwing) when the file is missing or not
    // well-formed, so the caller can fall back to the backup without any destructive cleanup.
    private static XmlDocument TryLoadXml(string fileName)
    {
        try
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(fileName);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string fileName)
    {
        // Snapshot the entries to persist under the lock, then release it before doing file I/O so
        // concurrent readers/writers are never blocked on the disk. Marking the store clean up
        // front means changes made during the write are not lost (they re-dirty the flag); if the
        // write fails we restore the flag so the next save retries.
        List<KeyValuePair<string, string>> entries;
        lock (_sync)
        {
            entries = new List<KeyValuePair<string, string>>(_settings.Count);
            foreach (KeyValuePair<string, string> keyValuePair in _settings)
            {
                // Sensor history that was loaded but never claimed by a live sensor this session
                // belongs to hardware that is gone; drop it instead of carrying it forever.
                if (_unclaimedSensorValues.Contains(keyValuePair.Key))
                    continue;

                if (!IsPersistableSetting(keyValuePair.Key, keyValuePair.Value))
                    continue;

                entries.Add(keyValuePair);
            }

            _modified = false;
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
            lock (_ioSync)
            {
                WriteFileAtomic(fileName, file);
            }
        }
        catch
        {
            // The bytes did not land on disk. Re-mark the store dirty so a later (auto)save retries
            // rather than assuming the file already reflects the current state.
            lock (_sync)
                _modified = true;

            throw;
        }
    }

    // Writes the settings bytes without ever risking the loss of an existing good file. The content
    // is fully written and flushed to a temporary file first, then swapped into place while the
    // previous file is retained as "<fileName>.backup". A crash or power loss during the swap can
    // therefore never leave the user with only defaults: either the new file or the backup survives.
    private static void WriteFileAtomic(string fileName, byte[] contents)
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
                // Atomic on NTFS: the previous file becomes the backup and the temp becomes live.
                File.Replace(tempFileName, fileName, backupFileName, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
            {
                // File.Replace needs both paths on one volume and is unsupported on some file
                // systems (e.g. certain network shares). Fall back to a copy-based swap that still
                // preserves a backup, at the cost of atomicity on those file systems.
            }

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
