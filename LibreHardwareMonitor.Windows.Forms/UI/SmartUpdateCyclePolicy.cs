// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

internal static class SmartUpdateCyclePolicy
{
    private const string LegacySettingName = "throttleAtaUpdateMenuItem";
    private const string SettingName = "smartUpdateCycle";

    internal static int ResolveSelection(PersistentSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (settings.Contains(SettingName))
            return settings.GetValue(SettingName, 0);

        return settings.GetValue(LegacySettingName, false) ? 2 : 0;
    }

    internal static uint ToCycleCount(int selection)
    {
        return selection switch
        {
            1 => 10,
            2 => 25,
            3 => 50,
            4 => 100,
            _ => 1
        };
    }
}
