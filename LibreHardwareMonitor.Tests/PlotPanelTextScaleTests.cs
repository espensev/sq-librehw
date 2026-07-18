// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotPanelTextScaleTests
{
    private static (PlotPanel Panel, PlotView View) CreatePanel()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var panel = new PlotPanel(settings, unitManager);
        return (panel, panel.Controls.OfType<PlotView>().Single());
    }

    [Fact]
    public void SetAxisTextScale_ScalesAxisFontsAndTickSpacing_NotTrackerFont()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            float trackerSizeBefore = view.Font.Size;

            panel.SetAxisTextScale(150);

            double expected = UiScale.PlotAxisFontSize(view.Model.DefaultFontSize, 150);
            Assert.NotEmpty(view.Model.Axes);
            foreach (Axis axis in view.Model.Axes)
            {
                Assert.Equal(expected, axis.FontSize);
                Assert.Equal(expected, axis.TitleFontSize);
                Assert.Equal(90.0, axis.IntervalLength); // 60 * 150%
            }

            Assert.Equal(trackerSizeBefore, view.Font.Size);
        }
    }

    [Fact]
    public void SetTrackerTextScale_ScalesTrackerFont_NotAxes()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            float baseSize = view.Font.Size;
            double[] axisSizesBefore = view.Model.Axes.Select(a => a.FontSize).ToArray();
            double[] intervalsBefore = view.Model.Axes.Select(a => a.IntervalLength).ToArray();

            panel.SetTrackerTextScale(200);

            Assert.Equal(UiScale.ScaledFontSize(baseSize, 200), view.Font.Size, precision: 2);
            Assert.Equal(axisSizesBefore, view.Model.Axes.Select(a => a.FontSize).ToArray());
            Assert.Equal(intervalsBefore, view.Model.Axes.Select(a => a.IntervalLength).ToArray());
        }
    }

    [Fact]
    public void BothSetters_ClampToUiScaleRange()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            panel.SetAxisTextScale(400);  // clamps to 250
            double expected = UiScale.PlotAxisFontSize(view.Model.DefaultFontSize, 250);
            Assert.All(view.Model.Axes, axis => Assert.Equal(expected, axis.FontSize));

            float baseSize = 0; // recompute from a fresh panel: tracker base is captured lazily
            (PlotPanel panel2, PlotView view2) = CreatePanel();
            using (panel2)
            {
                baseSize = view2.Font.Size;
                panel2.SetTrackerTextScale(10); // clamps to 75
                Assert.Equal(UiScale.ScaledFontSize(baseSize, 75), view2.Font.Size, precision: 2);
            }
        }
    }
}
