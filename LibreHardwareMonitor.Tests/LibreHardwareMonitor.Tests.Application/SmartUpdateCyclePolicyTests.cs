// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class SmartUpdateCyclePolicyTests
{
    [Fact]
    public void ResolveSelectionDefaultsToFollowUpdateInterval()
    {
        PersistentSettings settings = new();

        Assert.Equal(0, SmartUpdateCyclePolicy.ResolveSelection(settings));
    }

    [Fact]
    public void ResolveSelectionKeepsDisabledLegacySettingAtFollowUpdateInterval()
    {
        PersistentSettings settings = new();
        settings.SetValue("throttleAtaUpdateMenuItem", false);

        Assert.Equal(0, SmartUpdateCyclePolicy.ResolveSelection(settings));
    }

    [Fact]
    public void ResolveSelectionMigratesEnabledLegacySettingToTwentyFiveCycles()
    {
        PersistentSettings settings = new();
        settings.SetValue("throttleAtaUpdateMenuItem", true);

        Assert.Equal(2, SmartUpdateCyclePolicy.ResolveSelection(settings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveSelectionPrefersExplicitSmartUpdateCycle(bool legacySetting)
    {
        PersistentSettings settings = new();
        settings.SetValue("throttleAtaUpdateMenuItem", legacySetting);
        settings.SetValue("smartUpdateCycle", 4);

        Assert.Equal(4, SmartUpdateCyclePolicy.ResolveSelection(settings));
    }

    [Theory]
    [InlineData(0, 1u)]
    [InlineData(1, 10u)]
    [InlineData(2, 25u)]
    [InlineData(3, 50u)]
    [InlineData(4, 100u)]
    [InlineData(-1, 1u)]
    [InlineData(5, 1u)]
    public void ToCycleCountMapsSelectionsAndFallsBackSafely(int selection, uint expected)
    {
        Assert.Equal(expected, SmartUpdateCyclePolicy.ToCycleCount(selection));
    }
}
