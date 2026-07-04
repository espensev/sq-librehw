// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class UiScaleTests
{
    [Theory]
    [InlineData(50, 75)]     // below floor -> floor
    [InlineData(75, 75)]
    [InlineData(100, 100)]
    [InlineData(250, 250)]
    [InlineData(400, 250)]   // above ceiling -> ceiling
    public void ClampPercent_BoundsToRange(int input, int expected)
    {
        Assert.Equal(expected, UiScale.ClampPercent(input));
    }

    [Fact]
    public void ScaledFontSize_At100_ReturnsBase()
    {
        Assert.Equal(9f, UiScale.ScaledFontSize(9f, 100));
    }

    [Fact]
    public void ScaledFontSize_At150_Scales()
    {
        Assert.Equal(13.5f, UiScale.ScaledFontSize(9f, 150));
    }

    [Theory]
    [InlineData(20, false, 21)]  // Font.Height 20 -> Max(20+1,18)=21
    [InlineData(10, false, 18)]  // small font -> floor 18
    [InlineData(20, true, 20)]   // compact -> Max(20,16)=20
    [InlineData(10, true, 16)]   // compact floor 16
    public void TreeRowHeight_NormalAndCompact(int fontHeight, bool compact, int expected)
    {
        Assert.Equal(expected, UiScale.TreeRowHeight(fontHeight, compact));
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(100, 150, 150)]
    [InlineData(100, 250, 250)]
    [InlineData(100, 500, 250)]  // percent clamped to 250 first -> 250
    [InlineData(10, 100, 20)]    // below MinColumnWidth -> 20
    [InlineData(300, 200, 400)]  // 600 -> clamped to MaxColumnWidth 400
    public void ScaledColumnWidth_ScalesAndClamps(int baseWidth, int percent, int expected)
    {
        Assert.Equal(expected, UiScale.ScaledColumnWidth(baseWidth, percent));
    }

    [Fact]
    public void PlotAxisFontSize_ScalesFromTwelve()
    {
        Assert.Equal(18.0, UiScale.PlotAxisFontSize(12.0, 150));
    }

    [Theory]
    [InlineData(150, 150, 100)]  // scaled 150 at 150% -> base 100
    [InlineData(100, 100, 100)]
    public void BaseFromScaled_InvertsScaling(int scaledWidth, int percent, int expectedBase)
    {
        Assert.Equal(expectedBase, UiScale.BaseFromScaled(scaledWidth, percent));
    }
}
