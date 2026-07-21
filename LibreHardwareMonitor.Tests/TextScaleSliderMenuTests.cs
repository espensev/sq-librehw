// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Drawing;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class TextScaleSliderMenuTests
{
    private static TextScaleSliderMenu Create(int initialPercent = 100) =>
        new("Text Size", initialPercent, dpiScale: 1.0, SystemFonts.MenuFont, Color.Black);

    [Fact]
    public void Construction_SetsTitleReadoutAndSliderFromInitialPercent()
    {
        using TextScaleSliderMenu menu = Create(125);

        Assert.Equal("Text Size (125%)", menu.MenuItem.Text);
        Assert.Equal("125%", menu.Readout.Text);
        Assert.Equal(125, menu.Slider.Value);
        Assert.False(menu.Readout.AutoSize); // fixed-size: per-tick text can never re-layout the dropdown
    }

    [Fact]
    public void SliderTick_UpdatesReadoutAndRaisesPercentChanged_WithoutTouchingMenuText()
    {
        using TextScaleSliderMenu menu = Create(100);
        int observed = 0;
        menu.PercentChanged += p => observed = p;
        Size readoutSizeBefore = menu.Readout.Size;

        menu.Slider.Value = 150;

        Assert.Equal(150, observed);
        Assert.Equal("150%", menu.Readout.Text);
        Assert.Equal("Text Size (100%)", menu.MenuItem.Text); // parent label untouched mid-drag
        Assert.Equal(readoutSizeBefore, menu.Readout.Size);
    }

    [Fact]
    public void RefreshMenuText_UpdatesTitleAndReadoutHeightOnly()
    {
        using TextScaleSliderMenu menu = Create(100);
        int widthBefore = menu.Readout.Width;

        menu.RefreshMenuText(180, SystemFonts.CaptionFont);

        Assert.Equal("Text Size (180%)", menu.MenuItem.Text);
        Assert.Equal(widthBefore, menu.Readout.Width);
        Assert.Equal(SystemFonts.CaptionFont.Height + 6, menu.Readout.Height);
    }
}
