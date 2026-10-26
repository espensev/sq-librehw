// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal sealed class SettingsPersistenceException : Exception
{
    internal SettingsPersistenceException(Exception innerException)
        : base("Settings persistence failed.", innerException)
    { }
}

internal sealed class SettingsPersistenceCoordinator
{
    private readonly string _fileName;
    private readonly Action _projectCurrentState;
    private readonly Action<Exception> _reportSuppressedAutoSaveFailure;
    private readonly PersistentSettings _settings;

    internal SettingsPersistenceCoordinator(
        PersistentSettings settings,
        string fileName,
        Action projectCurrentState,
        Action<Exception> reportSuppressedAutoSaveFailure)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _projectCurrentState = projectCurrentState ?? throw new ArgumentNullException(nameof(projectCurrentState));
        _reportSuppressedAutoSaveFailure = reportSuppressedAutoSaveFailure ?? throw new ArgumentNullException(nameof(reportSuppressedAutoSaveFailure));
    }

    internal void Save(bool autoSave)
    {
        SaveCore(autoSave, retainUnclaimedSensorValues: false);
    }

    /// <summary>
    /// Forced save for the Windows session-end checkpoint. Session end can arrive before hardware
    /// discovery has constructed every sensor, so this save keeps loaded sensor history that no
    /// sensor has claimed yet instead of dropping it as departed hardware. Failures are thrown as
    /// for a final save.
    /// </summary>
    internal void SaveCheckpoint()
    {
        SaveCore(autoSave: false, retainUnclaimedSensorValues: true);
    }

    private void SaveCore(bool autoSave, bool retainUnclaimedSensorValues)
    {
        _projectCurrentState();

        if (autoSave && !_settings.Modified)
            return;

        try
        {
            RuntimePaths.EnsureSafeMutableFile(_fileName, "The runtime settings file");
            RuntimePaths.EnsureSafeMutableFile(_fileName + ".backup", "The runtime settings backup");
            RuntimePaths.EnsureSafeMutableFile(_fileName + ".new", "The runtime settings staging file");
            _settings.Save(_fileName, retainUnclaimedSensorValues);
        }
        catch (Exception exception) when (exception is IOException ||
                                           exception is UnauthorizedAccessException)
        {
            if (autoSave)
            {
                _reportSuppressedAutoSaveFailure(exception);
                return;
            }

            throw new SettingsPersistenceException(exception);
        }
    }
}
