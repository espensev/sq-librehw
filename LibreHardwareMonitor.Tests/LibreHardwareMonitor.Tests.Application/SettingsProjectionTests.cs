// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class SettingsProjectionTests
{
    [Fact]
    public void UserOption_ProjectsStoredValueWithoutDirtyingSettings()
    {
        PersistentSettings settings = CreateCleanSettings("option", true);
        using ToolStripMenuItem menuItem = new();

        UserOption option = new("option", false, menuItem, settings);

        Assert.True(option.Value);
        Assert.True(menuItem.Checked);
        Assert.False(settings.Modified);
    }

    [Fact]
    public void UserOption_ChangedInitializesOnlyNewSubscriberAndNotifiesAfterProjection()
    {
        PersistentSettings settings = CreateInMemorySettings();
        using ToolStripMenuItem menuItem = new();
        UserOption option = new("option", false, menuItem, settings);
        List<string> notifications = new();

        option.Changed += (_, _) =>
        {
            notifications.Add("first");
            if (option.Value)
            {
                Assert.True(settings.GetValue("option", false));
                Assert.True(menuItem.Checked);
            }
        };
        option.Changed += (_, _) =>
        {
            notifications.Add("second");
            if (option.Value)
            {
                Assert.True(settings.GetValue("option", false));
                Assert.True(menuItem.Checked);
            }
        };

        Assert.Equal(new[] { "first", "second" }, notifications);

        notifications.Clear();
        option.Value = true;

        Assert.Equal(new[] { "first", "second" }, notifications);
        Assert.True(settings.Modified);
    }

    [Fact]
    public void UserOption_NullNameProjectsExternalStateWithoutPersistence()
    {
        PersistentSettings settings = CreateInMemorySettings();
        using ToolStripMenuItem menuItem = new();
        UserOption option = new(null, true, menuItem, settings);
        int notifications = 0;
        option.Changed += (_, _) => notifications++;

        Assert.True(option.Value);
        Assert.True(menuItem.Checked);
        Assert.Equal(1, notifications);
        Assert.False(settings.Modified);

        menuItem.PerformClick();

        Assert.False(option.Value);
        Assert.False(menuItem.Checked);
        Assert.Equal(2, notifications);
        Assert.False(settings.Contains("autoStart"));
        Assert.False(settings.Modified);
    }

    [Fact]
    public void UserRadioGroup_ClampsStoredSelectionWithoutNormalizingPersistence()
    {
        PersistentSettings settings = CreateCleanSettings("selection", 99);
        using ToolStripMenuItem first = new();
        using ToolStripMenuItem second = new();
        using ToolStripMenuItem third = new();
        ToolStripMenuItem[] menuItems = { first, second, third };

        UserRadioGroup group = new("selection", 0, menuItems, settings);

        Assert.Equal(2, group.Value);
        Assert.False(first.Checked);
        Assert.False(second.Checked);
        Assert.True(third.Checked);
        Assert.Equal(99, settings.GetValue("selection", -1));
        Assert.False(settings.Modified);
    }

    [Fact]
    public void UserRadioGroup_ChangedInitializesOnlyNewSubscriberAndNotifiesAfterProjection()
    {
        PersistentSettings settings = CreateInMemorySettings();
        using ToolStripMenuItem first = new();
        using ToolStripMenuItem second = new();
        using ToolStripMenuItem third = new();
        ToolStripMenuItem[] menuItems = { first, second, third };
        UserRadioGroup group = new("selection", 0, menuItems, settings);
        List<string> notifications = new();

        group.Changed += (_, _) =>
        {
            notifications.Add("first");
            if (group.Value == 2)
            {
                Assert.Equal(2, settings.GetValue("selection", -1));
                Assert.False(first.Checked);
                Assert.False(second.Checked);
                Assert.True(third.Checked);
            }
        };
        group.Changed += (_, _) =>
        {
            notifications.Add("second");
            if (group.Value == 2)
            {
                Assert.Equal(2, settings.GetValue("selection", -1));
                Assert.False(first.Checked);
                Assert.False(second.Checked);
                Assert.True(third.Checked);
            }
        };

        Assert.Equal(new[] { "first", "second" }, notifications);

        notifications.Clear();
        third.PerformClick();

        Assert.Equal(new[] { "first", "second" }, notifications);
        Assert.Equal(2, group.Value);
        Assert.True(settings.Modified);
    }

    [Fact]
    public void PersistentSettings_InvalidTypedTextUsesCurrentPerTypeFallbacks()
    {
        PersistentSettings settings = CreateInMemorySettings();
        settings.SetValue("integer", "not-an-integer");
        settings.SetValue("single", "not-a-single");
        settings.SetValue("double", "not-a-double");
        settings.SetValue("color", "not-a-color");
        settings.SetValue("malformedBoolean", "not-a-boolean");
        settings.SetValue("caseMismatchedBoolean", "True");
        settings.SetValue("exactBoolean", "true");
        Color fallbackColor = Color.FromArgb(255, 12, 34, 56);

        Assert.Equal(17, settings.GetValue("integer", 17));
        Assert.Equal(1.25f, settings.GetValue("single", 1.25f));
        Assert.Equal(2.5d, settings.GetValue("double", 2.5d));
        Assert.Equal(fallbackColor.ToArgb(), settings.GetValue("color", fallbackColor).ToArgb());
        Assert.False(settings.GetValue("malformedBoolean", true));
        Assert.False(settings.GetValue("caseMismatchedBoolean", true));
        Assert.True(settings.GetValue("exactBoolean", false));
    }

    private static PersistentSettings CreateCleanSettings(string name, bool value)
    {
        PersistentSettings settings = CreateInMemorySettings();
        settings.SetValue(name, value);
        settings.Save("unused");
        return settings;
    }

    private static PersistentSettings CreateCleanSettings(string name, int value)
    {
        PersistentSettings settings = CreateInMemorySettings();
        settings.SetValue(name, value);
        settings.Save("unused");
        return settings;
    }

    private static PersistentSettings CreateInMemorySettings()
    {
        return new PersistentSettings((_, _) => { });
    }
}
