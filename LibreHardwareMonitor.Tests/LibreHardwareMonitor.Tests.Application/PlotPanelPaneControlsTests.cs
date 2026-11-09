// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Xunit;
using ToolStripItem = System.Windows.Forms.ToolStripItem;
using ToolStripItemCollection = System.Windows.Forms.ToolStripItemCollection;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace LibreHardwareMonitor.Tests;

#pragma warning disable CS0618 // Exercise the input API used by pinned OxyPlot 2.2.

public sealed class PlotPanelPaneControlsTests
{
    private const int RenderWidth = 1000;
    private const int RenderHeight = 400;

    public enum WheelRegion
    {
        StackedPane,
        ValueLabels,
        TimeLabels,
        OverlayPlot,
        Outside
    }

    public enum WheelTarget
    {
        Nothing,
        LaneValue,
        SharedTime,
        DetachedPaneTime
    }

    /// <summary>In cycle order: each gesture is followed by one that a stale transform would misplace.</summary>
    public enum PaneGesture
    {
        Wheel,
        ZoomRectangle,
        Pan
    }

    [Theory]
    [InlineData(WheelRegion.StackedPane, OxyModifierKeys.None, WheelTarget.LaneValue)]
    [InlineData(WheelRegion.StackedPane, OxyModifierKeys.Shift, WheelTarget.LaneValue)]
    [InlineData(WheelRegion.StackedPane, OxyModifierKeys.Control, WheelTarget.DetachedPaneTime)]
    [InlineData(WheelRegion.ValueLabels, OxyModifierKeys.None, WheelTarget.LaneValue)]
    [InlineData(WheelRegion.ValueLabels, OxyModifierKeys.Shift, WheelTarget.LaneValue)]
    [InlineData(WheelRegion.ValueLabels, OxyModifierKeys.Control, WheelTarget.Nothing)]
    [InlineData(WheelRegion.TimeLabels, OxyModifierKeys.None, WheelTarget.SharedTime)]
    [InlineData(WheelRegion.TimeLabels, OxyModifierKeys.Shift, WheelTarget.Nothing)]
    [InlineData(WheelRegion.TimeLabels, OxyModifierKeys.Control, WheelTarget.SharedTime)]
    [InlineData(WheelRegion.OverlayPlot, OxyModifierKeys.None, WheelTarget.SharedTime)]
    [InlineData(WheelRegion.OverlayPlot, OxyModifierKeys.Shift, WheelTarget.Nothing)]
    [InlineData(WheelRegion.OverlayPlot, OxyModifierKeys.Control, WheelTarget.SharedTime)]
    [InlineData(WheelRegion.Outside, OxyModifierKeys.None, WheelTarget.Nothing)]
    [InlineData(WheelRegion.Outside, OxyModifierKeys.Shift, WheelTarget.Nothing)]
    [InlineData(WheelRegion.Outside, OxyModifierKeys.Control, WheelTarget.Nothing)]
    public void Wheel_EachGestureChangesExactlyTheTableTarget(WheelRegion region, OxyModifierKeys modifiers, WheelTarget expected)
    {
        using Fixture f = Fixture.Create(stacked: region != WheelRegion.OverlayPlot);
        ScreenPoint point = f.PointIn(region);
        if (expected == WheelTarget.DetachedPaneTime)
        {
            AssertCtrlWheelDetaches(f, point, f.LaneKey);
            Assert.Null(f.SeriesOf(f.Gpu).XAxisKey);
            return;
        }

        Axis target = expected switch
        {
            WheelTarget.LaneValue => f.LaneAxis,
            WheelTarget.SharedTime => f.SharedAxis,
            _ => null
        };

        AssertWheelChangesOnly(f, point, modifiers, target);
    }

    [Theory]
    [InlineData(OxyModifierKeys.None, WheelRegion.StackedPane, false)]
    [InlineData(OxyModifierKeys.Shift, WheelRegion.ValueLabels, false)]
    [InlineData(OxyModifierKeys.Control, WheelRegion.StackedPane, true)]
    [InlineData(OxyModifierKeys.None, WheelRegion.TimeLabels, true)]
    [InlineData(OxyModifierKeys.Control, WheelRegion.OverlayPlot, true)]
    public void Wheel_DisabledZoomIsConsumedAndChangesNothing(OxyModifierKeys modifiers, WheelRegion region, bool timeGesture)
    {
        using Fixture f = Fixture.Create(
            stacked: region != WheelRegion.OverlayPlot,
            configure: settings =>
            {
                settings.SetValue("yAxesEnableZoom", timeGesture);
                settings.SetValue("timeAxisEnableZoom", !timeGesture);
            });

        AssertWheelChangesOnly(f, f.PointIn(region), modifiers, null);
    }

    [Fact]
    public void NoOverride_ModelKeepsSharedAxisFirstAndEverySeriesOnIt()
    {
        using Fixture f = Fixture.Create();
        RecordingRenderContext recorder = Render(f.View);

        Assert.IsAssignableFrom<TimeSpanAxis>(f.View.Model.Axes[0]);
        Assert.Equal(1, f.View.Model.Axes.Count(axis => axis.IsHorizontal()));
        Assert.All(f.View.Model.Axes.Skip(1), axis => Assert.IsType<LinearAxis>(axis));
        Assert.All(f.View.Model.Annotations, annotation => Assert.IsType<LineAnnotation>(annotation));
        Assert.All(f.View.Model.Series.Cast<LineSeries>(), series => Assert.Null(series.XAxisKey));

        // The shared axis took the stock render path: each tick label is drawn exactly once,
        // below the plot area.
        string[] labels = LabelTexts(f.SharedAxis);
        List<RecordedText> texts = recorder.Texts.Where(text => labels.Contains(text.Text)).ToList();
        Assert.Equal(labels.OrderBy(text => text), texts.Select(text => text.Text).OrderBy(text => text));
        Assert.All(texts, text => Assert.True(text.Position.Y > f.View.Model.PlotArea.Bottom));
    }

    [Theory]
    [InlineData(OxyModifierKeys.None, false)]
    [InlineData(OxyModifierKeys.Shift, false)]
    [InlineData(OxyModifierKeys.Control, true)]
    public void Wheel_InOverridingPaneZoomsItsValueOrItsOwnTimeAxis(OxyModifierKeys modifiers, bool paneTime)
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));
        Render(f.View);

        AssertWheelChangesOnly(f, f.PointIn(WheelRegion.StackedPane), modifiers, paneTime ? f.PaneAxis : f.LaneAxis);

        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal(paneTime, ageMax - ageMin < 120 - 1e-6);
    }

    [Fact]
    public void Wheel_CtrlOverFollowingPaneDetachesOnlyThatPaneWhileAnotherPaneOverrides()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));
        Render(f.View);
        OxyRect band = BandRect(f.View.Model, f.DefaultAxis);

        AssertCtrlWheelDetaches(f, new ScreenPoint(f.View.Model.PlotArea.Center.X, band.Center.Y), "Power");

        Assert.Equal("time:" + f.LaneKey, f.SeriesOf(f.Cpu).XAxisKey);
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal((0d, 120d), (ageMin, ageMax));
    }

    [Fact]
    public void PaneTimePreset_BindsOnlyThatLanesSeriesToItsOwnAxisAndFollowsNow()
    {
        using Fixture f = Fixture.Create();
        double sharedRange = f.SharedAxis.ActualMaximum - f.SharedAxis.ActualMinimum;

        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 60));
        Render(f.View);

        PlotModel model = f.View.Model;
        PlotPaneTimeAxis pane = f.PaneAxis;
        Assert.Equal("time:" + f.LaneKey, pane.Key);
        Assert.IsNotType<PlotPaneTimeAxis>(model.Axes[0]);
        Assert.Same(pane, model.Axes.Last());
        Assert.Equal(pane.Key, f.SeriesOf(f.Cpu).XAxisKey);
        Assert.Same(pane, f.SeriesOf(f.Cpu).XAxis);
        Assert.Null(f.SeriesOf(f.Gpu).XAxisKey);
        Assert.Single(model.Annotations, annotation => annotation.XAxisKey == pane.Key && annotation.YAxisKey == f.LaneKey);
        Assert.Equal(60, pane.ActualMaximum - pane.ActualMinimum, 6);
        Assert.Equal(sharedRange, f.SharedAxis.ActualMaximum - f.SharedAxis.ActualMinimum, 6);

        double firstNow = pane.ActualMaximum;
        Thread.Sleep(50);
        f.Panel.InvalidatePlot();
        Render(f.View);

        Assert.True(pane.ActualMaximum > firstNow);
        Assert.Equal(f.SharedAxis.ActualMaximum, pane.ActualMaximum, 6);
        Assert.Equal(60, pane.ActualMaximum - pane.ActualMinimum, 6);
        Assert.Equal(sharedRange, f.SharedAxis.ActualMaximum - f.SharedAxis.ActualMinimum, 6);

        // The tracker of a pane series reads the same session time as the shared axis.
        double x = pane.ActualMinimum + 10;
        Assert.IsType<DateTime>(pane.GetValue(x));
        Assert.Equal(f.SharedAxis.GetValue(x), pane.GetValue(x));
    }

    [Fact]
    public void PaneTimeWindow_SurvivesRestartAndFollowSharedRemovesAxisAndKey()
    {
        var settings = new PersistentSettings();
        string laneKey;
        string settingKey;
        using (Fixture f = Fixture.Create(settings: settings))
        {
            laneKey = f.LaneKey;
            settingKey = "plotPanel.laneTime." + laneKey;
            Assert.True(f.Panel.SetLaneTimeWindow(laneKey, 0, 3600));
            Assert.Equal("0:3600", settings.GetValue(settingKey, (string)null));

            // A pane pan is saved in age semantics on the next settings write.
            Render(f.View);
            f.PaneAxis.Pan(f.PaneAxis.Scale * 30);
            Assert.True(f.Panel.TryGetLaneTimeWindow(laneKey, out double pannedMin, out double pannedMax));
            Assert.Equal(30, pannedMin, 3);
            Assert.Equal(3630, pannedMax, 3);
            f.Panel.SetCurrentSettings();
            Assert.StartsWith("30", settings.GetValue(settingKey, (string)null));
            Assert.True(f.Panel.SetLaneTimeWindow(laneKey, 0, 3600));
        }

        using Fixture restored = Fixture.Create(settings: settings, laneKey: laneKey);
        Assert.True(restored.Panel.TryGetLaneTimeWindow(laneKey, out double ageMin, out double ageMax));
        Assert.Equal((0d, 3600d), (ageMin, ageMax));
        Assert.Equal(3600, restored.PaneAxis.ActualMaximum - restored.PaneAxis.ActualMinimum, 6);

        ToolStripMenuItem timeScale = Item(restored.Panel.CreatePaneMenuItems(restored.PointIn(WheelRegion.StackedPane)), "Time Scale");
        Assert.True(Item(timeScale.DropDownItems, "1 h").Checked);
        ToolStripMenuItem follow = Item(timeScale.DropDownItems, "Follow Shared");
        Assert.False(follow.Checked);
        follow.PerformClick();

        AssertNoPaneAxes(restored);
        Assert.False(settings.Contains(settingKey));
        Assert.False(restored.Panel.TryGetLaneTimeWindow(laneKey, out _, out _));
    }

    [Fact]
    public void PaneZoomedOutToTheFullDay_StaysWithin24HoursAndSurvivesRestart()
    {
        var settings = new PersistentSettings();
        string laneKey;
        using (Fixture f = Fixture.Create(settings: settings))
        {
            laneKey = f.LaneKey;
            Assert.True(f.Panel.SetLaneTimeWindow(laneKey, 0, 3600));
            Render(f.View);

            // Ctrl + wheel out in the overriding pane until its axis stops at its absolute minimum.
            for (int i = 0; i < 40; i++)
            {
                f.View.ActualController.HandleMouseWheel(f.View, new OxyMouseWheelEventArgs
                {
                    Position = f.PointIn(WheelRegion.StackedPane), Delta = -120, ModifierKeys = OxyModifierKeys.Control
                });
                Render(f.View);
            }

            Assert.True(f.Panel.TryGetLaneTimeWindow(laneKey, out _, out double zoomedMax));
            Assert.True(zoomedMax <= 24 * 3600, $"ageMax {zoomedMax:R}");
            f.Panel.SetCurrentSettings();
        }

        using Fixture restored = Fixture.Create(settings: settings, laneKey: laneKey);
        Assert.True(restored.Panel.TryGetLaneTimeWindow(laneKey, out _, out double ageMax));
        Assert.Equal(24 * 3600, ageMax, 1e-3);
    }

    [Fact]
    public void AutoTimeWindow_KeepsStoredSharedWindowWhileEveryVisiblePaneOverrides()
    {
        using Fixture f = Fixture.Create();
        f.Panel.TimeAxisZoom(0, 300);
        f.Panel.SetCurrentSettings();
        Assert.Equal((0f, 300f), StoredSharedWindow(f.Settings));

        // The default TimeSpan lane's range keys are the shared window's keys, so auto-ranging that
        // lane (Autoscale All, which the startup auto-fit also runs, or its pane Auto Range) must
        // leave them. In Auto nothing rewrites them below.
        f.Panel.AutoscaleAllYAxes();
        Assert.Equal((0f, 300f), StoredSharedWindow(f.Settings));
        Assert.True(f.Panel.AutoRangeLane("TimeSpan"));
        Assert.Equal((0f, 300f), StoredSharedWindow(f.Settings));

        // In Auto the shared range comes from its series. With every visible pane overriding it has
        // none, so its range is OxyPlot's empty-axis fallback and the stored window must stay. The
        // auto-range default TimeSpan lane shares these keys, so the lane loop must leave them too.
        Item(Item(f.View.ContextMenuStrip.Items, "Time Axis").DropDownItems, "Auto").PerformClick();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 60));
        Assert.True(f.Panel.SetLaneTimeWindow("Power", 0, 120));
        f.Panel.InvalidatePlot();
        Render(f.View);
        f.Panel.SetCurrentSettings();
        Assert.Equal((0f, 300f), StoredSharedWindow(f.Settings));

        // A following pane puts data back on the shared axis; Auto saves its data-driven window again.
        Assert.True(f.Panel.ClearLaneTimeWindow("Power"));
        f.Panel.InvalidatePlot();
        Render(f.View);
        f.Panel.SetCurrentSettings();
        Assert.InRange(StoredSharedWindow(f.Settings).Max, 110f, 130f);
    }

    [Fact]
    public void AutoSharedWindow_RefitsToTheRemainingDataWhenTheLongestPaneDetaches()
    {
        // Under Time Axis > Auto the shared range is the autoscale of the series still on the shared
        // axis, so a detach can shift the other following panes; a fixed shared window cannot move.
        using Fixture f = Fixture.Create(cpuSamples: 600);
        Item(Item(f.View.ContextMenuStrip.Items, "Time Axis").DropDownItems, "Auto").PerformClick();
        f.Panel.InvalidatePlot();
        Render(f.View);
        Axis shared = f.SharedAxis;
        Assert.InRange(shared.ActualMaximum - shared.ActualMinimum, 590, 620);

        Assert.True(f.View.ActualController.HandleMouseWheel(f.View, new OxyMouseWheelEventArgs
        {
            Position = f.PointIn(WheelRegion.StackedPane), Delta = 120, ModifierKeys = OxyModifierKeys.Control
        }));
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        f.Panel.InvalidatePlot();
        Render(f.View);

        // Only the 120 s GPU series is left on the shared axis; the detached pane keeps its window.
        Assert.Null(f.SeriesOf(f.Gpu).XAxisKey);
        Assert.InRange(shared.ActualMaximum - shared.ActualMinimum, 110, 130);
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double keptMin, out double keptMax));
        Assert.Equal((ageMin, ageMax), (keptMin, keptMax));
        Assert.Equal(ageMax - ageMin, f.PaneAxis.ActualMaximum - f.PaneAxis.ActualMinimum, 1e-6);
    }

    [Fact]
    public void AllPanesFollowShared_ClearsEveryOverride()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 60));
        Assert.True(f.Panel.SetLaneTimeWindow("Power", 0, 1800));
        Assert.Equal(2, f.View.Model.Axes.OfType<PlotPaneTimeAxis>().Count());

        Item(Item(f.View.ContextMenuStrip.Items, "Time Axis").DropDownItems, "All Panes Follow Shared").PerformClick();

        AssertNoPaneAxes(f);
        Assert.False(f.Settings.Contains("plotPanel.laneTime." + f.LaneKey));
        Assert.False(f.Settings.Contains("plotPanel.laneTime.Power"));
        Render(f.View);
    }

    [Fact]
    public void OverlayIgnoresOverridesAndStackedRestoresThem()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));
        ToolStripMenuItem stacked = Item(f.View.ContextMenuStrip.Items, "Stacked Axes");

        stacked.PerformClick();
        Render(f.View);

        AssertNoPaneAxes(f);
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out _, out _));
        Assert.True(f.Settings.Contains("plotPanel.laneTime." + f.LaneKey));
        Assert.False(f.Panel.ZoomLaneTime(f.LaneKey, 1.25));
        ToolStripItem[] labelSection = f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.ValueLabels)).ToArray();
        Assert.False(Item(labelSection, "Time Scale").Enabled);

        stacked.PerformClick();
        Render(f.View);

        Assert.Equal(f.PaneAxis.Key, f.SeriesOf(f.Cpu).XAxisKey);
        Assert.Equal(120, f.PaneAxis.ActualMaximum - f.PaneAxis.ActualMinimum, 6);
    }

    [Fact]
    public void HiddenLaneKeepsDormantOverrideAndRebindsWhenVisibleAgain()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));

        f.Bind(f.Gpu);
        Render(f.View);
        AssertNoPaneAxes(f);
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out _, out _));

        f.Bind();
        Render(f.View);
        Assert.Equal(f.PaneAxis.Key, f.SeriesOf(f.Cpu).XAxisKey);
        Assert.Equal(120, f.PaneAxis.ActualMaximum - f.PaneAxis.ActualMinimum, 6);
    }

    [Fact]
    public void RemoveLane_DropsItsOverrideAxisAndSetting()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));

        Assert.True(f.Panel.RemoveLane(f.LaneKey));
        Render(f.View);

        AssertNoPaneAxes(f);
        Assert.False(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out _, out _));
        Assert.False(f.Settings.Contains("plotPanel.laneTime." + f.LaneKey));
    }

    [Fact]
    public void SetThenClearOverride_RestoresTheModelExactly()
    {
        using Fixture f = Fixture.Create();
        PlotModel model = f.View.Model;
        Axis[] axes = model.Axes.ToArray();
        Annotation[] annotations = model.Annotations.ToArray();
        OxyRect plotArea = model.PlotArea;

        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 60));
        Render(f.View);
        AssertRectEqual(plotArea, model.PlotArea);
        Assert.True(f.Panel.ClearLaneTimeWindow(f.LaneKey));
        Render(f.View);

        Assert.Equal(axes, model.Axes.ToArray());
        Assert.Equal(annotations, model.Annotations.ToArray());
        Assert.All(model.Series.Cast<LineSeries>(), series => Assert.Null(series.XAxisKey));
        AssertRectEqual(plotArea, model.PlotArea);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverridingPane_ConfinesGridAndLabelsToItsBandAndKeepsPlotArea(bool overrideTopLane)
    {
        using Fixture f = Fixture.Create();
        OxyRect plotBefore = f.View.Model.PlotArea;
        LinearAxis band = overrideTopLane ? f.DefaultAxis : f.LaneAxis;
        LinearAxis other = overrideTopLane ? f.LaneAxis : f.DefaultAxis;
        Assert.True(f.Panel.SetLaneTimeWindow(band.Key, 0, 60));

        // Distinct colors so the recording can tell the two time axes apart.
        Axis shared = f.SharedAxis;
        PlotPaneTimeAxis pane = f.PaneAxis;
        shared.MajorGridlineColor = SharedMajor;
        shared.MinorGridlineColor = SharedMinor;
        shared.TextColor = SharedText;
        pane.MajorGridlineColor = PaneMajor;
        pane.MinorGridlineColor = PaneMinor;
        pane.TextColor = PaneText;
        LineSeries paneSeries = f.SeriesOf(overrideTopLane ? f.Gpu : f.Cpu);
        LineSeries otherSeries = f.SeriesOf(overrideTopLane ? f.Cpu : f.Gpu);
        paneSeries.Color = PaneSeriesColor;
        otherSeries.Color = OtherSeriesColor;

        RecordingRenderContext recorder = Render(f.View);
        PlotModel model = f.View.Model;
        AssertRectEqual(plotBefore, model.PlotArea);
        OxyRect paneBand = BandRect(model, band);
        OxyRect otherBand = BandRect(model, other);

        // The overriding lane's line is drawn through its own window and clipped to its band; the
        // other lane's line still uses the shared window.
        DataPoint sample = Assert.IsAssignableFrom<IList<DataPoint>>(paneSeries.ItemsSource).Last(point => point.X <= pane.ActualMaximum - 5);
        RecordedLine paneLine = Assert.Single(recorder.Lines, line => !line.IsSegments && line.Stroke.Equals(PaneSeriesColor));
        Assert.Contains(paneLine.Points, point =>
            Math.Abs(point.X - pane.Transform(sample.X)) < 0.01 && Math.Abs(point.Y - band.Transform(sample.Y)) < 0.01);
        Assert.True(Math.Abs(pane.Transform(sample.X) - shared.Transform(sample.X)) > 50);
        AssertRectEqual(paneBand, paneLine.Clip);
        RecordedLine otherLine = Assert.Single(recorder.Lines, line => !line.IsSegments && line.Stroke.Equals(OtherSeriesColor));
        AssertRectEqual(otherBand, otherLine.Clip);

        var sharedGrid = VisibleVerticalSegments(recorder, SharedMajor).Concat(VisibleVerticalSegments(recorder, SharedMinor)).ToList();
        var paneGrid = VisibleVerticalSegments(recorder, PaneMajor).Concat(VisibleVerticalSegments(recorder, PaneMinor)).ToList();
        Assert.NotEmpty(sharedGrid);
        Assert.NotEmpty(paneGrid);
        Assert.All(sharedGrid, segment => Assert.True(Overlap(segment.Top, segment.Bottom, paneBand.Top, paneBand.Bottom) < 1e-6,
            $"shared gridline x={segment.X:F1} {segment.Top:F1}..{segment.Bottom:F1} inside band {paneBand.Top:F1}..{paneBand.Bottom:F1}"));
        Assert.Contains(sharedGrid, segment => segment.Top <= otherBand.Center.Y && segment.Bottom >= otherBand.Center.Y);
        Assert.All(paneGrid, segment => Assert.True(segment.Top >= paneBand.Top - 1e-6 && segment.Bottom <= paneBand.Bottom + 1e-6,
            $"pane gridline x={segment.X:F1} {segment.Top:F1}..{segment.Bottom:F1} outside band {paneBand.Top:F1}..{paneBand.Bottom:F1}"));

        // Shared labels: each tick once, below the plot area. Pane labels: inside the band, on a
        // semi-opaque strip, and never cut at the band's left or right edge.
        List<RecordedText> sharedTexts = recorder.Texts.Where(text => text.Fill.Equals(SharedText)).ToList();
        Assert.Equal(LabelTexts(shared).OrderBy(text => text), sharedTexts.Select(text => text.Text).OrderBy(text => text));
        Assert.All(sharedTexts, text => Assert.True(text.Position.Y > model.PlotArea.Bottom));

        List<RecordedText> paneTexts = recorder.Texts.Where(text => text.Fill.Equals(PaneText)).OrderBy(text => text.Position.X).ToList();
        Assert.NotEmpty(paneTexts);
        double fontSize = double.IsNaN(pane.FontSize) ? model.DefaultFontSize : pane.FontSize;
        for (int i = 1; i < paneTexts.Count; i++)
        {
            double previousRight = paneTexts[i - 1].Position.X + MeasureText(model, paneTexts[i - 1].Text, fontSize).Width / 2;
            double left = paneTexts[i].Position.X - MeasureText(model, paneTexts[i].Text, fontSize).Width / 2;
            Assert.True(left > previousRight, $"pane labels '{paneTexts[i - 1].Text}' and '{paneTexts[i].Text}' overlap");
        }

        Assert.Subset(new HashSet<string>(LabelTexts(pane)), new HashSet<string>(paneTexts.Select(text => text.Text)));
        Assert.All(paneTexts, text =>
        {
            Assert.True(text.Position.Y > paneBand.Top && text.Position.Y <= paneBand.Bottom, $"pane label y={text.Position.Y:F1}");
            AssertRectEqual(paneBand, text.Clip);
            OxySize size = MeasureText(f.View.Model, text.Text, double.IsNaN(pane.FontSize) ? model.DefaultFontSize : pane.FontSize);
            Assert.True(text.Position.X - size.Width / 2 >= paneBand.Left && text.Position.X + size.Width / 2 <= paneBand.Right,
                $"pane label '{text.Text}' at x={text.Position.X:F1} is cut by the band edge");
        });
        Assert.Contains(recorder.Rectangles, rect => rect.Fill.A < 255 && rect.Fill.A > 0 &&
            Math.Abs(rect.Rect.Bottom - paneBand.Bottom) < 2 && Math.Abs(rect.Rect.Width - paneBand.Width) < 1e-6);
    }

    [Fact]
    public void ShowAxesLabelsOff_HidesPaneLabelsAndStripAndKeepsItsBandGrid()
    {
        // A persisted override creates the pane axis while the panel builds its menu, before the
        // Show Axes Labels option exists, so the option's handler has to reach that axis.
        var settings = new PersistentSettings();
        string laneKey;
        using (var panel = new PlotPanel(settings, new UnitManager(settings)))
            laneKey = panel.CreateLane(SensorType.Power, "CPU").Key;
        settings.SetValue("plotPanel.laneTime." + laneKey, "0:60");
        settings.SetValue("showAxesLabels", false);

        using Fixture f = Fixture.Create(settings: settings, laneKey: laneKey);
        ToolStripMenuItem showLabels = Item(f.View.ContextMenuStrip.Items, "Show Axes Labels");
        AssertPaneLabels(f, visible: false);

        showLabels.PerformClick();
        AssertPaneLabels(f, visible: true);

        showLabels.PerformClick();
        AssertPaneLabels(f, visible: false);

        // A pane axis created while the option is off starts hidden too.
        Assert.True(f.Panel.ClearLaneTimeWindow(laneKey));
        Assert.True(f.Panel.SetLaneTimeWindow(laneKey, 0, 60));
        AssertPaneLabels(f, visible: false);
    }

    [Fact]
    public void RightDragInOverridingPanePansOnlyItsWindowAndFollowingPanePansShared()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));
        Render(f.View);
        PlotPaneTimeAxis pane = f.PaneAxis;
        Axis shared = f.SharedAxis;
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(f.View.Model);
        ScreenPoint start = f.PointIn(WheelRegion.StackedPane);
        double paneScale = pane.Scale;

        Drag(f.View, OxyMouseButton.Right, OxyModifierKeys.None, start, new ScreenPoint(start.X + 100, start.Y));

        Assert.Equal(before[pane].Min - 100 / paneScale, pane.ActualMinimum, 1e-6);
        Assert.Equal(before[shared], (shared.ActualMinimum, shared.ActualMaximum));
        Assert.Equal(before[f.DefaultAxis], (f.DefaultAxis.ActualMinimum, f.DefaultAxis.ActualMaximum));
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal(100 / paneScale, ageMin, 3);
        Assert.Equal(120 + 100 / paneScale, ageMax, 3);

        Render(f.View);
        before = Snapshot(f.View.Model);
        OxyRect followingBand = BandRect(f.View.Model, f.DefaultAxis);
        start = new ScreenPoint(f.View.Model.PlotArea.Center.X, followingBand.Center.Y);
        Drag(f.View, OxyMouseButton.Left, OxyModifierKeys.Alt, start, new ScreenPoint(start.X + 100, start.Y));

        Assert.NotEqual(before[shared], (shared.ActualMinimum, shared.ActualMaximum));
        Assert.Equal(before[pane], (pane.ActualMinimum, pane.ActualMaximum));
    }

    [Theory]
    [InlineData(OxyMouseButton.Middle, OxyModifierKeys.None)]
    [InlineData(OxyMouseButton.Right, OxyModifierKeys.Control)]
    public void ZoomRectangleInOverridingPaneZoomsOnlyItsWindow(OxyMouseButton button, OxyModifierKeys modifiers)
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 120));
        Render(f.View);
        PlotPaneTimeAxis pane = f.PaneAxis;
        Axis shared = f.SharedAxis;
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(f.View.Model);
        OxyRect band = BandRect(f.View.Model, f.LaneAxis);
        var start = new ScreenPoint(band.Left + band.Width * 0.3, band.Center.Y - 20);
        var end = new ScreenPoint(band.Left + band.Width * 0.6, band.Center.Y + 20);
        double expectedMin = pane.InverseTransform(start.X);
        double expectedMax = pane.InverseTransform(end.X);

        Drag(f.View, button, modifiers, start, end);

        Assert.Equal(expectedMin, pane.ActualMinimum, 1e-6);
        Assert.Equal(expectedMax, pane.ActualMaximum, 1e-6);
        Assert.Equal(before[shared], (shared.ActualMinimum, shared.ActualMaximum));
        Assert.Equal(before[f.DefaultAxis], (f.DefaultAxis.ActualMinimum, f.DefaultAxis.ActualMaximum));
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal(expectedMax - expectedMin, ageMax - ageMin, 3);
    }

    [Theory]
    [InlineData(OxyMouseButton.Middle, OxyModifierKeys.None)]
    [InlineData(OxyMouseButton.Right, OxyModifierKeys.Control)]
    [InlineData(OxyMouseButton.Left, OxyModifierKeys.Control | OxyModifierKeys.Alt)]
    public void ZoomRectangleInFollowingPaneDetachesItWithTheRectangleTimeRange(OxyMouseButton button, OxyModifierKeys modifiers)
    {
        using Fixture f = Fixture.Create();
        Axis shared = f.SharedAxis;
        LinearAxis lane = f.LaneAxis;
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(f.View.Model);
        OxyRect band = BandRect(f.View.Model, lane);
        var start = new ScreenPoint(band.Left + band.Width * 0.3, band.Center.Y - 20);
        var end = new ScreenPoint(band.Left + band.Width * 0.6, band.Center.Y + 20);
        double expectedMin = shared.InverseTransform(start.X);
        double expectedMax = shared.InverseTransform(end.X);
        double valueMin = lane.InverseTransform(end.Y);
        double valueMax = lane.InverseTransform(start.Y);

        Drag(f.View, button, modifiers, start, end);
        Render(f.View);

        Assert.Equal(before[shared], (shared.ActualMinimum, shared.ActualMaximum));
        Assert.Equal(before[f.DefaultAxis], (f.DefaultAxis.ActualMinimum, f.DefaultAxis.ActualMaximum));
        PlotPaneTimeAxis pane = f.PaneAxis;
        Assert.Equal("time:" + f.LaneKey, pane.Key);
        Assert.Equal(pane.Key, f.SeriesOf(f.Cpu).XAxisKey);
        Assert.Null(f.SeriesOf(f.Gpu).XAxisKey);
        Assert.Equal(expectedMin, pane.ActualMinimum, 1e-6);
        Assert.Equal(expectedMax, pane.ActualMaximum, 1e-6);
        Assert.Equal(valueMin, lane.ActualMinimum, 1e-6);
        Assert.Equal(valueMax, lane.ActualMaximum, 1e-6);
    }

    [Fact]
    public void ZoomRectangleInFollowingPane_BelowThresholdOrWithTimeZoomOffDoesNotDetach()
    {
        using Fixture f = Fixture.Create();
        PlotModel model = f.View.Model;
        OxyRect band = BandRect(model, f.LaneAxis);
        var start = new ScreenPoint(band.Center.X, band.Center.Y - 20);
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(model);

        // OxyPlot zooms nothing for a rectangle 10 px or less on either side.
        Drag(f.View, OxyMouseButton.Middle, OxyModifierKeys.None, start, new ScreenPoint(start.X + 8, start.Y + 40));
        Render(f.View);

        Assert.Equal(before.Count, model.Axes.Count);
        Assert.All(before, pair => Assert.Equal(pair.Value, (pair.Key.ActualMinimum, pair.Key.ActualMaximum)));
        Assert.False(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out _, out _));

        // With time zoom off the rectangle spans the plot width and zooms only the value axis.
        Item(Item(f.View.ContextMenuStrip.Items, "Time Axis").DropDownItems, "Enable Zoom").PerformClick();
        Drag(f.View, OxyMouseButton.Middle, OxyModifierKeys.None, start, new ScreenPoint(start.X + 100, start.Y + 40));
        Render(f.View);

        Assert.Equal(before.Count, model.Axes.Count);
        Assert.False(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out _, out _));
        Assert.Equal(before[f.SharedAxis], (f.SharedAxis.ActualMinimum, f.SharedAxis.ActualMaximum));
        Assert.NotEqual(before[f.LaneAxis], (f.LaneAxis.ActualMinimum, f.LaneAxis.ActualMaximum));
    }

    [Theory]
    [InlineData(PaneGesture.Wheel)]
    [InlineData(PaneGesture.ZoomRectangle)]
    [InlineData(PaneGesture.Pan)]
    public void DetachedPane_MapsInputThatArrivesBeforeItIsRendered(PaneGesture first)
    {
        // Wheel bursts, a touchpad pinch (Ctrl + wheel), and queued input reach a pane before the next
        // paint. After a Ctrl + wheel detach, each case runs all three gestures in cycle order, starting
        // with a different one, and never renders. Expectations come from the pane's window and the
        // plot area, not from the pane's transform.
        using Fixture f = Fixture.Create();
        OxyRect plot = f.View.Model.PlotArea;
        ScreenPoint point = f.PointIn(WheelRegion.StackedPane);
        Axis shared = f.SharedAxis;
        (double Min, double Max) sharedWindow = (shared.ActualMinimum, shared.ActualMaximum);
        double sharedSpan = sharedWindow.Max - sharedWindow.Min;
        double anchor = shared.InverseTransform(point.X);
        string settingKey = "plotPanel.laneTime." + f.LaneKey;
        var notch = new OxyMouseWheelEventArgs { Position = point, Delta = 120, ModifierKeys = OxyModifierKeys.Control };

        Assert.True(f.View.ActualController.HandleMouseWheel(f.View, notch));
        PlotPaneTimeAxis pane = f.PaneAxis;
        Assert.Equal(sharedSpan / 1.2, pane.ActualMaximum - pane.ActualMinimum, 1e-6);
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double detachedAgeMin, out _));
        double now = pane.ActualMaximum + detachedAgeMin;

        for (int i = 0; i < 3; i++)
        {
            double min = pane.ActualMinimum;
            double max = pane.ActualMaximum;
            double TimeAt(double x) => min + (x - plot.Left) / plot.Width * (max - min);
            (double Min, double Max) expected;
            switch ((PaneGesture)(((int)first + i) % 3))
            {
                case PaneGesture.Wheel:
                    double at = TimeAt(point.X);
                    Assert.True(f.View.ActualController.HandleMouseWheel(f.View, notch));
                    expected = (at + (min - at) / 1.2, at + (max - at) / 1.2);
                    if (i == 0)
                    {
                        // Straight after the detach: the shared span / 1.44 about the same time.
                        Assert.Equal(anchor, at, 1e-6);
                        Assert.Equal(sharedSpan / 1.44, expected.Max - expected.Min, 1e-6);
                    }

                    break;
                case PaneGesture.ZoomRectangle:
                    var start = new ScreenPoint(plot.Left + plot.Width * 0.3, point.Y - 20);
                    var end = new ScreenPoint(plot.Left + plot.Width * 0.6, point.Y + 20);
                    Drag(f.View, OxyMouseButton.Middle, OxyModifierKeys.None, start, end);
                    expected = (TimeAt(start.X), TimeAt(end.X));
                    break;
                default:
                    Drag(f.View, OxyMouseButton.Right, OxyModifierKeys.None, point, new ScreenPoint(point.X + 100, point.Y));
                    double shift = 100 / plot.Width * (max - min);
                    expected = (min - shift, max - shift);
                    break;
            }

            Assert.Equal(expected.Min, pane.ActualMinimum, 1e-6);
            Assert.Equal(expected.Max, pane.ActualMaximum, 1e-6);
            Assert.Equal(sharedWindow, (shared.ActualMinimum, shared.ActualMaximum));
            Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
            Assert.Equal(now - expected.Max, ageMin, 1e-6);
            Assert.Equal(now - expected.Min, ageMax, 1e-6);
            f.Panel.SetCurrentSettings();
            Assert.Equal(
                ageMin.ToString("R", CultureInfo.InvariantCulture) + ":" + ageMax.ToString("R", CultureInfo.InvariantCulture),
                f.Settings.GetValue(settingKey, (string)null));
        }
    }

    [Fact]
    public void PaneMenu_SectionForPaneOrLabelsButNotForGearOrOverlayPlotArea()
    {
        using Fixture f = Fixture.Create();

        IReadOnlyList<ToolStripItem> section = f.Panel.CreatePaneMenuItems(PointAwayFromLines(f, f.LaneAxis));
        Assert.Equal(
            new[] { "── CPU ──", "Value Scale", "Time Scale", "Height", "Remove from Graph", "Rename...", "Remove Lane", string.Empty },
            section.Select(item => item is ToolStripSeparator ? string.Empty : item.Text));
        Assert.False(section[0].Enabled);
        Assert.IsType<ToolStripSeparator>(section.Last());

        IReadOnlyList<ToolStripItem> defaultSection = f.Panel.CreatePaneMenuItems(PointAwayFromLines(f, f.DefaultAxis));
        Assert.Equal("── Power ──", defaultSection[0].Text);
        Assert.DoesNotContain(defaultSection, item => item.Text == "Rename..." || item.Text == "Remove Lane");

        Assert.Empty(f.Panel.CreatePaneMenuItems(null));
        Assert.Empty(f.Panel.CreatePaneMenuItems(new ScreenPoint(-10, -10)));

        using Fixture overlay = Fixture.Create(stacked: false);
        Assert.Empty(overlay.Panel.CreatePaneMenuItems(PointAwayFromLines(overlay, overlay.LaneAxis)));
        Assert.Equal("── CPU ──", overlay.Panel.CreatePaneMenuItems(overlay.PointIn(WheelRegion.ValueLabels))[0].Text);
    }

    [Fact]
    public void PaneMenu_SectionIsPrependedAndDisposedOnlyWhenTheNextOpeningRebuildsIt()
    {
        using Fixture f = Fixture.Create();
        ToolStripItem[] fixedItems = f.View.ContextMenuStrip.Items.Cast<ToolStripItem>().ToArray();

        f.Panel.RebuildPaneMenuSection(PointAwayFromLines(f, f.LaneAxis));
        ToolStripItem[] firstSection = f.View.ContextMenuStrip.Items.Cast<ToolStripItem>().Take(8).ToArray();
        Assert.Equal("── CPU ──", firstSection[0].Text);
        Assert.Equal(fixedItems, f.View.ContextMenuStrip.Items.Cast<ToolStripItem>().Skip(8));
        var disposed = new HashSet<ToolStripItem>();
        foreach (ToolStripItem item in firstSection)
            item.Disposed += (sender, args) => disposed.Add((ToolStripItem)sender);

        f.Panel.RebuildPaneMenuSection(null);

        Assert.Equal(fixedItems, f.View.ContextMenuStrip.Items.Cast<ToolStripItem>());
        Assert.Equal(firstSection.Length, disposed.Count);
        Assert.All(fixedItems, item => Assert.DoesNotContain(item, disposed));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PaneMenu_HitLineAddsRemoveItemThatRequestsExactlyThatSensor(bool stacked, bool overriding)
    {
        using Fixture f = Fixture.Create(stacked: stacked);
        if (overriding)
        {
            Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 90));
            Render(f.View);
        }

        IReadOnlyList<ISensor> requested = null;
        f.Panel.SensorsRemoveRequested = sensors => requested = sensors;
        ScreenPoint onLine = PointOnLine(f, f.Cpu);

        // The hit test and the tracker map the line through the series' own time axis.
        TrackerHitResult tracker = f.SeriesOf(f.Cpu).GetNearestPoint(onLine, true);
        Assert.Same(overriding ? f.PaneAxis : f.SharedAxis, f.SeriesOf(f.Cpu).XAxis);
        Assert.Equal(f.SeriesOf(f.Cpu).XAxis.Transform(tracker.DataPoint.X), tracker.Position.X, 1e-6);
        Assert.Same(f.Cpu, f.Panel.GetSensorAt(onLine));
        IReadOnlyList<ToolStripItem> section = f.Panel.CreatePaneMenuItems(onLine);
        Assert.Equal("Remove \"Test hardware CPU\" from Graph", section[0].Text);
        Assert.Equal(stacked ? "── CPU ──" : null, section.Count > 2 ? section[1].Text : null);
        Assert.IsType<ToolStripSeparator>(section.Last());

        section[0].PerformClick();
        Assert.Same(f.Cpu, Assert.Single(requested));
    }

    [Fact]
    public void RemoveFromGraph_OffersEachSensorAndConfirmsAllInPane()
    {
        using Fixture f = Fixture.Create();
        var soc = new FakeSensor(SensorType.Power, "SoC", 120, index => 20);
        f.Panel.SetSensors(
            new List<ISensor> { f.Gpu, f.Cpu, soc },
            new Dictionary<ISensor, Color> { [f.Gpu] = Color.Red, [f.Cpu] = Color.Blue, [soc] = Color.Green },
            new Dictionary<ISensor, string> { [f.Gpu] = null, [f.Cpu] = f.LaneKey, [soc] = f.LaneKey },
            2);
        Render(f.View);
        var requests = new List<IReadOnlyList<ISensor>>();
        f.Panel.SensorsRemoveRequested = sensors => requests.Add(sensors);
        Assert.Equal(new ISensor[] { f.Cpu, soc }, f.Panel.GetLaneSensors(f.LaneKey));

        ToolStripMenuItem remove = Item(f.Panel.CreatePaneMenuItems(PointAwayFromLines(f, f.LaneAxis)), "Remove from Graph");
        Assert.Equal(
            new[] { "Test hardware CPU", "Test hardware SoC", string.Empty, "All 2 in This Pane..." },
            remove.DropDownItems.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? string.Empty : item.Text));

        Item(remove.DropDownItems, "Test hardware SoC").PerformClick();
        Assert.Same(soc, Assert.Single(Assert.Single(requests)));

        string question = null;
        f.Panel.ConfirmRemoval = message => { question = message; return false; };
        Item(remove.DropDownItems, "All 2 in This Pane...").PerformClick();
        Assert.Contains("CPU", question);
        Assert.Single(requests);

        f.Panel.ConfirmRemoval = _ => true;
        Item(remove.DropDownItems, "All 2 in This Pane...").PerformClick();
        Assert.Equal(new ISensor[] { f.Cpu, soc }, requests[1]);
    }

    [Fact]
    public void ValueScale_ZoomSetAndAutoRangeAffectOnlyThePaneAndPersist()
    {
        using Fixture f = Fixture.Create();
        string minKey = "plotPanel.Min" + f.LaneKey;
        string maxKey = "plotPanel.Max" + f.LaneKey;
        LinearAxis lane = f.LaneAxis;
        double min = lane.ActualMinimum;
        double max = lane.ActualMaximum;
        double center = (min + max) / 2;
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(f.View.Model);
        ToolStripMenuItem valueScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Value Scale");
        Assert.True(Item(valueScale.DropDownItems, "Auto Range").Checked);

        Item(valueScale.DropDownItems, "Zoom In").PerformClick();

        Assert.Equal(center - (max - min) / 2.5, lane.ActualMinimum, 6);
        Assert.Equal(center + (max - min) / 2.5, lane.ActualMaximum, 6);
        Assert.All(f.View.Model.Axes.Where(axis => axis != lane), axis =>
            Assert.Equal(before[axis], (axis.ActualMinimum, axis.ActualMaximum)));
        f.Panel.SetCurrentSettings();
        Assert.Equal(lane.ActualMinimum, f.Settings.GetValue(minKey, float.NaN), 3);
        Assert.Equal(lane.ActualMaximum, f.Settings.GetValue(maxKey, float.NaN), 3);
        Assert.False(f.Settings.Contains("plotPanel.MinPower"));

        Item(valueScale.DropDownItems, "Zoom Out").PerformClick();
        Assert.Equal(min, lane.ActualMinimum, 6);
        Assert.Equal(max, lane.ActualMaximum, 6);

        Assert.True(f.Panel.SetLaneValueRange(f.LaneKey, 10, 90));
        Assert.Equal((10d, 90d), (lane.ActualMinimum, lane.ActualMaximum));
        f.Panel.SetCurrentSettings();
        Assert.Equal(10f, f.Settings.GetValue(minKey, float.NaN));
        Assert.Equal(90f, f.Settings.GetValue(maxKey, float.NaN));
        Assert.False(f.Panel.SetLaneValueRange(f.LaneKey, 90, 10));
        Assert.False(f.Panel.SetLaneValueRange(f.LaneKey, double.NaN, 10));
        Assert.False(f.Panel.SetLaneValueRange(f.LaneKey, 0, double.PositiveInfinity));
        Assert.Equal((10d, 90d), (lane.ActualMinimum, lane.ActualMaximum));

        valueScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Value Scale");
        Assert.False(Item(valueScale.DropDownItems, "Auto Range").Checked);
        Item(valueScale.DropDownItems, "Auto Range").PerformClick();
        Assert.False(f.Settings.Contains(minKey));
        Assert.False(f.Settings.Contains(maxKey));
    }

    [Fact]
    public void ValueZoomRespectsEnableZoomButSetRangeStillApplies()
    {
        using Fixture f = Fixture.Create(configure: settings => settings.SetValue("yAxesEnableZoom", false));
        ToolStripMenuItem valueScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Value Scale");

        Assert.False(Item(valueScale.DropDownItems, "Zoom In").Enabled);
        Assert.False(Item(valueScale.DropDownItems, "Zoom Out").Enabled);
        Assert.False(f.Panel.ZoomLaneValue(f.LaneKey, 1.25));
        Assert.True(f.Panel.SetLaneValueRange(f.LaneKey, 5, 25));
        Assert.Equal((5d, 25d), (f.LaneAxis.ActualMinimum, f.LaneAxis.ActualMaximum));
        Assert.False(f.LaneAxis.IsZoomEnabled);
    }

    [Fact]
    public void ZoomLaneTime_ScalesTheOverrideWithItsNewestEdgeFixed()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 30, 130));

        Assert.True(f.Panel.ZoomLaneTime(f.LaneKey, 1.25));
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal((30d, 110d), (ageMin, ageMax));

        ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
        Item(timeScale.DropDownItems, "Zoom Out").PerformClick();
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out ageMin, out ageMax));
        Assert.Equal(30, ageMin);
        Assert.Equal(130, ageMax, 1e-9);

        // Zoom Out stops at 24 h of age.
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 80000));
        Assert.True(f.Panel.ZoomLaneTime(f.LaneKey, 1 / 1.25));
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out ageMin, out ageMax));
        Assert.Equal((0d, 86400d), (ageMin, ageMax));
    }

    [Fact]
    public void TimeScaleZoomIn_OnFollowingPaneDetachesItAtTheLiveEdge()
    {
        using Fixture f = Fixture.Create();
        Axis shared = f.SharedAxis;
        (double Min, double Max) sharedBefore = (shared.ActualMinimum, shared.ActualMaximum);
        double sharedSpan = sharedBefore.Max - sharedBefore.Min;
        OxyRect band = BandRect(f.View.Model, f.DefaultAxis);
        IReadOnlyList<ToolStripItem> section = f.Panel.CreatePaneMenuItems(new ScreenPoint(f.View.Model.PlotArea.Center.X, band.Center.Y));
        Assert.Equal("── Power ──", section.First(item => item.Text.StartsWith("──", StringComparison.Ordinal)).Text);

        Item(Item(section, "Time Scale").DropDownItems, "Zoom In").PerformClick();

        Assert.True(f.Panel.TryGetLaneTimeWindow("Power", out double ageMin, out double ageMax));
        Assert.Equal(0d, ageMin);
        Assert.Equal(sharedSpan / 1.25, ageMax - ageMin, 1e-6);
        Assert.Equal(sharedBefore, (shared.ActualMinimum, shared.ActualMaximum));
        Assert.Equal("time:Power", f.SeriesOf(f.Gpu).XAxisKey);
        Assert.Null(f.SeriesOf(f.Cpu).XAxisKey);

        Render(f.View);
        PlotPaneTimeAxis pane = f.PaneAxis;
        Assert.Equal(shared.ActualMaximum, pane.ActualMaximum, 1e-6);
        Assert.Equal(sharedSpan / 1.25, pane.ActualMaximum - pane.ActualMinimum, 1e-6);
        Assert.Equal(sharedBefore, (shared.ActualMinimum, shared.ActualMaximum));
    }

    [Fact]
    public void TimeScaleZoomIn_OnLivePaneKeepsItsNewestEdgeAndStopsAtMinimumRange()
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 100));
        double minimumRange = f.SharedAxis.MinimumRange;
        double ageMin = double.NaN;
        double ageMax = double.NaN;

        for (int i = 0; i < 25; i++)
        {
            ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
            Item(timeScale.DropDownItems, "Zoom In").PerformClick();
            Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out ageMin, out ageMax));
            Assert.Equal(0d, ageMin);
            Assert.True(ageMax - ageMin >= minimumRange, $"click {i + 1}: span {ageMax - ageMin} below {minimumRange}");
        }

        Assert.Equal(minimumRange, ageMax - ageMin, 1e-9);
        Render(f.View);
        Assert.Equal(f.SharedAxis.ActualMaximum, f.PaneAxis.ActualMaximum, 1e-6);
        Assert.Equal(minimumRange, f.PaneAxis.ActualMaximum - f.PaneAxis.ActualMinimum, 1e-6);
    }

    [Fact]
    public void TimeScaleMenu_WithTimeZoomOffKeepsFollowAndPresetsButDisablesZoom()
    {
        using Fixture f = Fixture.Create(configure: settings => settings.SetValue("timeAxisEnableZoom", false));
        ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");

        Assert.True(timeScale.Enabled);
        Assert.True(Item(timeScale.DropDownItems, "Follow Shared").Enabled);
        Assert.Equal(15, timeScale.DropDownItems.OfType<ToolStripRadioButtonMenuItem>().Count(item => item.Text != "Follow Shared"));
        Assert.All(timeScale.DropDownItems.OfType<ToolStripRadioButtonMenuItem>(), item => Assert.True(item.Enabled, item.Text));
        Assert.False(Item(timeScale.DropDownItems, "Zoom In").Enabled);
        Assert.False(Item(timeScale.DropDownItems, "Zoom Out").Enabled);
        Assert.False(f.Panel.ZoomLaneTime(f.LaneKey, 1.25));

        Item(timeScale.DropDownItems, "5 min").PerformClick();
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal((0d, 300d), (ageMin, ageMax));
    }

    [Theory]
    [InlineData(0d, 250d, "Custom (4 min 10 s)")]
    [InlineData(30d, 75d, "Custom (45 s, ending 30 s ago)")]
    [InlineData(0.4, 3900.2, "Custom (1 h 5 min)")]
    [InlineData(120d, 3720d, "Custom (1 h, ending 2 min ago)")]
    [InlineData(0d, 59.6, "Custom (1 min)")]
    public void TimeScaleMenu_NonPresetWindowShowsACheckedDisabledCustomItem(double ageMin, double ageMax, string text)
    {
        using Fixture f = Fixture.Create();
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, ageMin, ageMax));

        ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
        ToolStripItem[] items = timeScale.DropDownItems.Cast<ToolStripItem>().ToArray();

        Assert.False(Assert.IsAssignableFrom<ToolStripMenuItem>(items[0]).Checked);
        Assert.Equal("Follow Shared", items[0].Text);
        ToolStripRadioButtonMenuItem custom = Assert.IsType<ToolStripRadioButtonMenuItem>(items[1]);
        Assert.Equal(text, custom.Text);
        Assert.True(custom.Checked);
        Assert.False(custom.Enabled);
        Assert.IsType<ToolStripSeparator>(items[2]);
        Assert.Single(items.OfType<ToolStripMenuItem>(), item => item.Checked);
    }

    [Fact]
    public void TimeScaleMenu_FollowingOrPresetWindowHasNoCustomItem()
    {
        using Fixture f = Fixture.Create();
        ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
        Assert.DoesNotContain(timeScale.DropDownItems.Cast<ToolStripItem>(), item => item.Text.StartsWith("Custom", StringComparison.Ordinal));
        Assert.True(Item(timeScale.DropDownItems, "Follow Shared").Checked);

        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 600));
        timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
        Assert.DoesNotContain(timeScale.DropDownItems.Cast<ToolStripItem>(), item => item.Text.StartsWith("Custom", StringComparison.Ordinal));
        Assert.Equal("10 min", Assert.Single(timeScale.DropDownItems.OfType<ToolStripMenuItem>(), item => item.Checked).Text);
    }

    [Fact]
    public void PaneAxes_FollowTextScaleGridDensityLabelModeAndZoomOption()
    {
        using Fixture f = Fixture.Create();
        f.Panel.SetAxisTextScale(150);
        Assert.True(f.Panel.SetLaneTimeWindow(f.LaneKey, 0, 60));
        f.Panel.TimeAxisZoom(0, 3600);
        f.Panel.InvalidatePlot();
        Render(f.View);
        Axis shared = f.SharedAxis;
        PlotPaneTimeAxis pane = f.PaneAxis;

        Assert.Equal(shared.FontSize, pane.FontSize);
        Assert.Equal(shared.IntervalLength, pane.IntervalLength);
        Assert.Equal(shared.TextColor, pane.TextColor);
        Assert.Equal(shared.MajorGridlineColor, pane.MajorGridlineColor);
        Assert.False(double.IsNaN(pane.MajorStep));
        Assert.True(pane.MajorStep < shared.MajorStep);

        // Each axis formats from its own range and step: seconds for the 60 s pane, minutes for
        // the shared hour.
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", pane.FormatValue(pane.ActualMaximum));
        Assert.Matches(@"^(\d+/\d+ )?\d{2}:\d{2}$", shared.FormatValue(shared.ActualMaximum));

        ToolStripMenuItem timeAxis = Item(f.View.ContextMenuStrip.Items, "Time Axis");
        Item(Item(timeAxis.DropDownItems, "Label Mode").DropDownItems, "Elapsed").PerformClick();
        Assert.Equal("0:00", pane.FormatValue(pane.ActualMaximum));
        Assert.IsType<TimeSpan>(pane.GetValue(pane.ActualMinimum));

        Item(timeAxis.DropDownItems, "Enable Zoom").PerformClick();
        Assert.False(shared.IsZoomEnabled);
        Assert.False(pane.IsZoomEnabled);
        ToolStripMenuItem timeScale = Item(f.Panel.CreatePaneMenuItems(f.PointIn(WheelRegion.StackedPane)), "Time Scale");
        Assert.True(timeScale.Enabled);
        Assert.False(Item(timeScale.DropDownItems, "Zoom In").Enabled);
        Assert.False(f.Panel.ZoomLaneTime(f.LaneKey, 1.25));

        // Window follow does not depend on the zoom option.
        f.Panel.InvalidatePlot();
        Assert.Equal(60, pane.ActualMaximum - pane.ActualMinimum, 6);
    }

    [Fact]
    public void LanesMenu_LaneEntriesOfferValueTimeAndRemoveSubmenus()
    {
        using Fixture f = Fixture.Create();
        ToolStripMenuItem lanes = f.Panel.CreateLanesMenu();
        f.Panel.PopulateLanesMenu(lanes.DropDownItems);

        foreach (string lane in new[] { "Power", "CPU" })
        {
            ToolStripMenuItem entry = Item(lanes.DropDownItems, lane);
            Assert.NotNull(Item(entry.DropDownItems, "Value Scale"));
            Assert.NotNull(Item(entry.DropDownItems, "Time Scale"));
            Assert.NotNull(Item(entry.DropDownItems, "Remove from Graph"));
        }

        Item(Item(Item(lanes.DropDownItems, "CPU").DropDownItems, "Time Scale").DropDownItems, "5 min").PerformClick();
        Assert.True(f.Panel.TryGetLaneTimeWindow(f.LaneKey, out double ageMin, out double ageMax));
        Assert.Equal((0d, 300d), (ageMin, ageMax));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1:10")]
    [InlineData("10:5")]
    [InlineData("5:5")]
    [InlineData("1:2:3")]
    [InlineData("NaN:5")]
    [InlineData("0:Infinity")]
    [InlineData("")]
    [InlineData(":")]
    [InlineData("0,5:10")]
    [InlineData("90000:100000")]
    [InlineData("0:86400.5")]
    public void MalformedLaneTimeSettingIsIgnoredAndRemovedOnSave(string value)
    {
        var settings = new PersistentSettings();
        string laneKey;
        using (var panel = new PlotPanel(settings, new UnitManager(settings)))
            laneKey = panel.CreateLane(SensorType.Power, "CPU").Key;
        settings.SetValue("plotPanel.laneTime." + laneKey, value);
        settings.SetValue("plotPanel.laneTime.Power", "0:60");

        using Fixture f = Fixture.Create(settings: settings, laneKey: laneKey);

        Assert.False(f.Panel.TryGetLaneTimeWindow(laneKey, out _, out _));
        Assert.Equal("time:Power", Assert.Single(f.View.Model.Axes.OfType<PlotPaneTimeAxis>()).Key);
        f.Panel.SetCurrentSettings();
        Assert.False(settings.Contains("plotPanel.laneTime." + laneKey));
        Assert.Equal("0:60", settings.GetValue("plotPanel.laneTime.Power", (string)null));
    }

    [Theory]
    [InlineData("10", "20", "", true, 10, 20)]
    [InlineData(" -5 ", "2.5e1", "", true, -5, 25)]
    [InlineData("1,5", "2,5", "de-DE", true, 1.5, 2.5)]
    [InlineData("1.5", "2.5", "de-DE", false, 0, 0)] // A group separator is not silently dropped.
    [InlineData("1,000", "2,000", "", false, 0, 0)]
    [InlineData("20", "10", "", false, 0, 0)]
    [InlineData("10", "10", "", false, 0, 0)]
    [InlineData("abc", "10", "", false, 0, 0)]
    [InlineData("", "10", "", false, 0, 0)]
    [InlineData(null, "10", "", false, 0, 0)]
    [InlineData("NaN", "10", "", false, 0, 0)]
    [InlineData("-Infinity", "10", "", false, 0, 0)]
    [InlineData("1e309", "1e310", "", false, 0, 0)]
    public void TryParseRange_AcceptsOnlyFiniteIncreasingCultureNumbers(string minimum, string maximum, string culture, bool valid, double expectedMinimum, double expectedMaximum)
    {
        bool parsed = PlotRangePrompt.TryParseRange(minimum, maximum, CultureInfo.GetCultureInfo(culture), out double min, out double max);

        Assert.Equal(valid, parsed);
        if (valid)
            Assert.Equal((expectedMinimum, expectedMaximum), (min, max));
    }

    [Theory]
    [InlineData(10d, 20d, "", "10", "20")]
    [InlineData(-5.5, 1234567d, "", "-5.5", "1.23457E+06")]
    [InlineData(1.5, 2.5, "de-DE", "1,5", "2,5")]
    [InlineData(double.NaN, 20d, "", "", "20")]
    [InlineData(10d, double.PositiveInfinity, "", "10", "")]
    [InlineData(double.NegativeInfinity, double.NaN, "", "", "")]
    [InlineData(1.0000001, 1.0000002, "", "1.0000001", "1.0000002")]
    [InlineData(1.0000001, 1.0000002, "de-DE", "1,0000001", "1,0000002")]
    [InlineData(123456.7, 123456.8, "", "123456.7", "123456.8")]
    public void RangePromptPrefill_UsesG6UnlessABoundIsNotFiniteOrG6CollapsesTheRange(double minimum, double maximum, string culture, string expectedMinimum, string expectedMaximum)
    {
        CultureInfo cultureInfo = CultureInfo.GetCultureInfo(culture);

        (string minimumText, string maximumText) = PlotRangePrompt.FormatPrefill(minimum, maximum, cultureInfo);

        Assert.Equal(expectedMinimum, minimumText);
        Assert.Equal(expectedMaximum, maximumText);
        if (minimumText.Length > 0 && maximumText.Length > 0)
            Assert.True(PlotRangePrompt.TryParseRange(minimumText, maximumText, cultureInfo, out _, out _));
    }

    private static readonly OxyColor SharedMajor = OxyColor.FromRgb(255, 0, 0);
    private static readonly OxyColor SharedMinor = OxyColor.FromRgb(140, 0, 0);
    private static readonly OxyColor SharedText = OxyColor.FromRgb(255, 255, 0);
    private static readonly OxyColor PaneMajor = OxyColor.FromRgb(0, 255, 0);
    private static readonly OxyColor PaneMinor = OxyColor.FromRgb(0, 110, 0);
    private static readonly OxyColor PaneText = OxyColor.FromRgb(0, 255, 255);
    private static readonly OxyColor PaneSeriesColor = OxyColor.FromRgb(0, 0, 255);
    private static readonly OxyColor OtherSeriesColor = OxyColor.FromRgb(255, 0, 255);

    private static void AssertNoPaneAxes(Fixture f)
    {
        PlotModel model = f.View.Model;
        Assert.DoesNotContain(model.Axes, axis => axis is PlotPaneTimeAxis);
        Assert.All(model.Annotations, annotation => Assert.IsType<LineAnnotation>(annotation));
        Assert.All(model.Series.Cast<LineSeries>(), series => Assert.Null(series.XAxisKey));
    }

    private static (float Min, float Max) StoredSharedWindow(PersistentSettings settings) =>
        (settings.GetValue("plotPanel.MinTimeSpan", float.NaN), settings.GetValue("plotPanel.MaxTimeSpan", float.NaN));

    /// <summary>Renders and checks the pane labels and their strip are drawn or absent; the pane grid stays in its band either way.</summary>
    private static void AssertPaneLabels(Fixture f, bool visible)
    {
        PlotPaneTimeAxis pane = f.PaneAxis;
        pane.MajorGridlineColor = PaneMajor;
        pane.MinorGridlineColor = PaneMinor;
        pane.TextColor = PaneText;
        RecordingRenderContext recorder = Render(f.View);
        OxyRect band = BandRect(f.View.Model, f.LaneAxis);

        Assert.Equal(visible, recorder.Texts.Any(text => text.Fill.Equals(PaneText)));
        Assert.Equal(visible, recorder.Rectangles.Any(rect => rect.Fill.A < 255 && rect.Fill.A > 0 &&
            Math.Abs(rect.Rect.Bottom - band.Bottom) < 2 && Math.Abs(rect.Rect.Width - band.Width) < 1e-6));

        var grid = VisibleVerticalSegments(recorder, PaneMajor).Concat(VisibleVerticalSegments(recorder, PaneMinor)).ToList();
        Assert.NotEmpty(grid);
        Assert.All(grid, segment => Assert.True(segment.Top >= band.Top - 1e-6 && segment.Bottom <= band.Bottom + 1e-6,
            $"pane gridline x={segment.X:F1} {segment.Top:F1}..{segment.Bottom:F1} outside band {band.Top:F1}..{band.Bottom:F1}"));
    }

    private static ToolStripMenuItem Item(IEnumerable<ToolStripItem> items, string text) =>
        Assert.IsAssignableFrom<ToolStripMenuItem>(Assert.Single(items, item => item.Text == text));

    private static ToolStripMenuItem Item(ToolStripItemCollection items, string text) =>
        Item(items.Cast<ToolStripItem>(), text);

    private static ScreenPoint PointOnLine(Fixture f, ISensor sensor)
    {
        LineSeries series = f.SeriesOf(sensor);
        var points = Assert.IsAssignableFrom<IList<DataPoint>>(series.ItemsSource);
        ScreenPoint point = series.Transform(points[points.Count / 2]);
        Assert.True(f.View.Model.PlotArea.Contains(point));
        return point;
    }

    private static ScreenPoint PointAwayFromLines(Fixture f, Axis band)
    {
        OxyRect rect = BandRect(f.View.Model, band);
        double x = rect.Center.X;
        for (double y = rect.Top + 4; y < rect.Bottom - 4; y++)
        {
            var point = new ScreenPoint(x, y);
            bool nearLine = f.View.Model.Series.Cast<LineSeries>().Any(series =>
                series.GetNearestPoint(point, true) is TrackerHitResult hit && hit.Position.DistanceTo(point) < 30);
            if (!nearLine)
                return point;
        }

        throw new InvalidOperationException("No point away from the plotted lines.");
    }

    private static void Drag(PlotView view, OxyMouseButton button, OxyModifierKeys modifiers, ScreenPoint start, ScreenPoint end)
    {
        IPlotController controller = view.ActualController;
        controller.HandleMouseDown(view, new OxyMouseDownEventArgs
        {
            ChangedButton = button, ClickCount = 1, Position = start, ModifierKeys = modifiers
        });
        controller.HandleMouseMove(view, new OxyMouseEventArgs { Position = end, ModifierKeys = modifiers });
        controller.HandleMouseUp(view, new OxyMouseEventArgs { Position = end, ModifierKeys = modifiers });
    }

    private static IEnumerable<(double X, double Top, double Bottom)> VisibleVerticalSegments(RecordingRenderContext recorder, OxyColor color)
    {
        foreach (RecordedLine line in recorder.Lines.Where(line => line.IsSegments && line.Stroke.Equals(color)))
        {
            for (int i = 0; i + 1 < line.Points.Length; i += 2)
            {
                ScreenPoint a = line.Points[i];
                ScreenPoint b = line.Points[i + 1];
                if (Math.Abs(a.X - b.X) > 1e-9 || a.X < line.Clip.Left || a.X > line.Clip.Right)
                    continue;

                double top = Math.Max(Math.Min(a.Y, b.Y), line.Clip.Top);
                double bottom = Math.Min(Math.Max(a.Y, b.Y), line.Clip.Bottom);
                if (bottom - top > 1e-9)
                    yield return (a.X, top, bottom);
            }
        }
    }

    private static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    private static OxySize MeasureText(PlotModel model, string text, double fontSize)
    {
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using var context = new GraphicsRenderContext(graphics);
        return context.MeasureText(text, model.DefaultFont, fontSize, FontWeights.Normal);
    }

    private static void AssertRectEqual(OxyRect expected, OxyRect actual)
    {
        Assert.Equal(expected.Left, actual.Left, 6);
        Assert.Equal(expected.Top, actual.Top, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    /// <summary>
    /// Ctrl + wheel over a following pane: that lane gets its own window, seeded from the shared window
    /// and zoomed 1.2x about the time under the pointer. Every existing axis and every other series stays.
    /// </summary>
    private static void AssertCtrlWheelDetaches(Fixture f, ScreenPoint point, string laneKey)
    {
        PlotModel model = f.View.Model;
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(model);
        Dictionary<LineSeries, string> seriesKeys = model.Series.Cast<LineSeries>().ToDictionary(series => series, series => series.XAxisKey);
        Axis shared = f.SharedAxis;
        double sharedSpan = shared.ActualMaximum - shared.ActualMinimum;
        double anchor = shared.InverseTransform(point.X);
        Assert.False(f.Panel.TryGetLaneTimeWindow(laneKey, out _, out _));

        bool handled = f.View.ActualController.HandleMouseWheel(f.View, new OxyMouseWheelEventArgs
        {
            Position = point, Delta = 120, ModifierKeys = OxyModifierKeys.Control
        });

        // The new axis is already laid out as the next render lays it out, so input before that paint
        // maps through it.
        PlotPaneTimeAxis pane = Assert.Single(model.Axes.OfType<PlotPaneTimeAxis>(), axis => !before.ContainsKey(axis));
        (double Scale, double Offset) laidOut = (pane.Scale, pane.Offset);
        Render(f.View);
        Assert.Equal(laidOut.Scale, pane.Scale, 1e-9);
        Assert.Equal(laidOut.Offset, pane.Offset, 1e-9);

        Assert.True(handled);
        Assert.All(before, pair => Assert.Equal(pair.Value, (pair.Key.ActualMinimum, pair.Key.ActualMaximum)));
        Assert.Equal("time:" + laneKey, pane.Key);
        Assert.Equal(before.Count + 1, model.Axes.Count);
        Assert.All(seriesKeys, pair => Assert.Equal(pair.Key.YAxisKey == laneKey ? pane.Key : pair.Value, pair.Key.XAxisKey));
        Assert.Equal(sharedSpan / 1.2, pane.ActualMaximum - pane.ActualMinimum, 1e-6);
        Assert.Equal(anchor, pane.InverseTransform(point.X), 1e-6);
        Assert.True(f.Panel.TryGetLaneTimeWindow(laneKey, out double ageMin, out double ageMax));
        Assert.Equal(sharedSpan / 1.2, ageMax - ageMin, 1e-6);
    }

    private static void AssertWheelChangesOnly(Fixture f, ScreenPoint point, OxyModifierKeys modifiers, Axis target)
    {
        Dictionary<Axis, (double Min, double Max)> before = Snapshot(f.View.Model);
        double anchor = target == null ? double.NaN : AxisValueAt(target, point);

        bool handled = f.View.ActualController.HandleMouseWheel(f.View, new OxyMouseWheelEventArgs
        {
            Position = point, Delta = 120, ModifierKeys = modifiers
        });
        Render(f.View);

        // No gesture outside a following pane's Ctrl + wheel creates a pane axis.
        Assert.Equal(before.Count, f.View.Model.Axes.Count);
        List<Axis> changed = f.View.Model.Axes
            .Where(axis => before[axis] != (axis.ActualMinimum, axis.ActualMaximum))
            .ToList();
        Assert.True(handled);
        if (target == null)
        {
            Assert.Empty(changed);
            return;
        }

        Assert.Same(target, Assert.Single(changed));
        Assert.True(target.ActualMaximum - target.ActualMinimum < before[target].Max - before[target].Min);
        // A tolerance, not 6-decimal rounding: time anchors carry nowX's 7th decimal, and when it is
        // 5 the zoom's ~1e-13 noise decides which way each side rounds.
        Assert.Equal(anchor, AxisValueAt(target, point), 1e-6);
    }

    private static double AxisValueAt(Axis axis, ScreenPoint point) =>
        axis.IsHorizontal() ? axis.InverseTransform(point.X) : axis.InverseTransform(point.Y);

    private static Dictionary<Axis, (double Min, double Max)> Snapshot(PlotModel model) =>
        model.Axes.ToDictionary(axis => axis, axis => (axis.ActualMinimum, axis.ActualMaximum));

    private static string[] LabelTexts(Axis axis)
    {
        axis.GetTickValues(out IList<double> labels, out _, out _);
        double tolerance = Math.Abs(axis.ActualMinorStep) * 1e-3;
        return labels
            .Where(value => value >= axis.ClipMinimum - tolerance && value <= axis.ClipMaximum + tolerance)
            .Select(axis.FormatValue)
            .ToArray();
    }

    private static RecordingRenderContext Render(PlotView view, int height = RenderHeight)
    {
        RecordingRenderContext recorder = null;
        view.Model.RenderingDecorator = inner => recorder = new RecordingRenderContext(inner);
        try
        {
            using Bitmap bitmap = new PngExporter { Width = RenderWidth, Height = height }.ExportToBitmap(view.Model);
            Assert.Null(view.Model.GetLastPlotException());
            return recorder;
        }
        finally
        {
            view.Model.RenderingDecorator = null;
        }
    }

    private static OxyRect BandRect(PlotModel model, Axis band)
    {
        double top = Math.Min(band.ScreenMin.Y, band.ScreenMax.Y);
        double bottom = Math.Max(band.ScreenMin.Y, band.ScreenMax.Y);
        return new OxyRect(model.PlotArea.Left, top, model.PlotArea.Width, bottom - top);
    }

    /// <summary>
    /// A panel with the default Power lane (GPU) above nothing and a user lane "CPU" (CPU sensor),
    /// both visible, rendered once at 1000x400 so plot geometry exists.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private Fixture(PersistentSettings settings, PlotPanel panel, string laneKey, FakeSensor gpu, FakeSensor cpu)
        {
            Settings = settings;
            Panel = panel;
            LaneKey = laneKey;
            Gpu = gpu;
            Cpu = cpu;
        }

        internal PersistentSettings Settings { get; }
        internal PlotPanel Panel { get; }
        internal string LaneKey { get; }
        internal FakeSensor Gpu { get; }
        internal FakeSensor Cpu { get; }
        internal PlotView View => Panel.Controls.OfType<PlotView>().Single();
        internal Axis SharedAxis => View.Model.Axes[0];
        internal LinearAxis LaneAxis => GetLinearAxis(LaneKey);
        internal LinearAxis DefaultAxis => GetLinearAxis("Power");
        internal PlotPaneTimeAxis PaneAxis => Assert.Single(View.Model.Axes.OfType<PlotPaneTimeAxis>());

        internal static Fixture Create(bool stacked = true, Action<PersistentSettings> configure = null, PersistentSettings settings = null, string laneKey = null, int cpuSamples = 120)
        {
            settings ??= new PersistentSettings();
            settings.SetValue("stackedAxes", stacked);
            configure?.Invoke(settings);
            var panel = new PlotPanel(settings, new UnitManager(settings));
            laneKey ??= panel.CreateLane(SensorType.Power, "CPU").Key;
            var fixture = new Fixture(
                settings,
                panel,
                laneKey,
                new FakeSensor(SensorType.Power, "GPU", 120, index => 150 + 20 * (float)Math.Sin(index / 7.0)),
                new FakeSensor(SensorType.Power, "CPU", cpuSamples, index => 40 + 10 * (float)Math.Cos(index / 5.0)));
            fixture.Bind();
            Render(fixture.View);
            return fixture;
        }

        internal void Bind(params ISensor[] sensors)
        {
            if (sensors.Length == 0)
                sensors = new ISensor[] { Gpu, Cpu };

            Panel.SetSensors(
                sensors.ToList(),
                sensors.ToDictionary(sensor => sensor, _ => Color.Red),
                sensors.ToDictionary(sensor => sensor, sensor => ReferenceEquals(sensor, Cpu) ? LaneKey : null),
                2);
        }

        internal LinearAxis GetLinearAxis(string key) =>
            Assert.IsType<LinearAxis>(View.Model.Axes.Single(axis => axis.Key == key));

        internal LineSeries SeriesOf(ISensor sensor) =>
            View.Model.Series.Cast<LineSeries>().Single(series => series.Title == sensor.Hardware.Name + " " + sensor.Name);

        internal ScreenPoint PointIn(WheelRegion region)
        {
            PlotModel model = View.Model;
            OxyRect plot = model.PlotArea;
            OxyRect band = BandRect(model, LaneAxis);
            return region switch
            {
                WheelRegion.StackedPane => new ScreenPoint(plot.Center.X, band.Center.Y),
                WheelRegion.ValueLabels => new ScreenPoint(plot.Left - 10, LaneAxis.Transform(
                    LaneAxis.ActualMinimum + (LaneAxis.ActualMaximum - LaneAxis.ActualMinimum) * 0.3)),
                WheelRegion.TimeLabels => new ScreenPoint(plot.Center.X, plot.Bottom + 6),
                WheelRegion.OverlayPlot => plot.Center,
                _ => new ScreenPoint(-10, -10)
            };
        }

        public void Dispose() => Panel.Dispose();
    }

    internal sealed record RecordedLine(ScreenPoint[] Points, OxyColor Stroke, bool IsSegments, OxyRect Clip);

    internal sealed record RecordedText(ScreenPoint Position, string Text, OxyColor Fill, OxyRect Clip);

    /// <summary>Records draw calls with the effective clip (intersect-on-push, like OxyPlot's ClippingRenderContext).</summary>
    internal sealed class RecordingRenderContext : IRenderContext
    {
        private readonly IRenderContext _inner;
        private readonly Stack<OxyRect> _clips = new();

        public RecordingRenderContext(IRenderContext inner)
        {
            _inner = inner;
        }

        public List<RecordedLine> Lines { get; } = new();
        public List<RecordedText> Texts { get; } = new();
        public List<(OxyRect Rect, OxyColor Fill)> Rectangles { get; } = new();
        private OxyRect CurrentClip => _clips.Count == 0 ? OxyRect.Everything : _clips.Peek();

        public bool RendersToScreen => _inner.RendersToScreen;
        public int ClipCount => _inner.ClipCount;

        public void PushClip(OxyRect clippingRectangle)
        {
            _clips.Push(_clips.Count == 0 ? clippingRectangle : clippingRectangle.Intersect(_clips.Peek()));
            _inner.PushClip(clippingRectangle);
        }

        public void PopClip()
        {
            _clips.Pop();
            _inner.PopClip();
        }

        public void DrawLine(IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode mode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter)
        {
            Lines.Add(new RecordedLine(points.ToArray(), stroke, false, CurrentClip));
            _inner.DrawLine(points, stroke, thickness, mode, dashArray, lineJoin);
        }

        public void DrawLineSegments(IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode mode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter)
        {
            Lines.Add(new RecordedLine(points.ToArray(), stroke, true, CurrentClip));
            _inner.DrawLineSegments(points, stroke, thickness, mode, dashArray, lineJoin);
        }

        public void DrawText(ScreenPoint p, string text, OxyColor fill, string fontFamily = null, double fontSize = 10, double fontWeight = FontWeights.Normal, double rotation = 0, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left, VerticalAlignment verticalAlignment = VerticalAlignment.Top, OxySize? maxSize = null)
        {
            Texts.Add(new RecordedText(p, text, fill, CurrentClip));
            _inner.DrawText(p, text, fill, fontFamily, fontSize, fontWeight, rotation, horizontalAlignment, verticalAlignment, maxSize);
        }

        public void DrawRectangle(OxyRect rectangle, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode)
        {
            Rectangles.Add((rectangle, fill));
            _inner.DrawRectangle(rectangle, fill, stroke, thickness, mode);
        }

        public void DrawEllipse(OxyRect extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode) => _inner.DrawEllipse(extents, fill, stroke, thickness, mode);
        public void DrawEllipses(IList<OxyRect> extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode) => _inner.DrawEllipses(extents, fill, stroke, thickness, mode);
        public void DrawPolygon(IList<ScreenPoint> points, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawPolygon(points, fill, stroke, thickness, mode, dashArray, lineJoin);
        public void DrawPolygons(IList<IList<ScreenPoint>> polygons, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawPolygons(polygons, fill, stroke, thickness, mode, dashArray, lineJoin);
        public void DrawRectangles(IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode mode) => _inner.DrawRectangles(rectangles, fill, stroke, thickness, mode);
        public OxySize MeasureText(string text, string fontFamily = null, double fontSize = 10, double fontWeight = 500) => _inner.MeasureText(text, fontFamily, fontSize, fontWeight);
        public void SetToolTip(string text) => _inner.SetToolTip(text);
        public void CleanUp() => _inner.CleanUp();
        public void DrawImage(OxyImage source, double srcX, double srcY, double srcWidth, double srcHeight, double destX, double destY, double destWidth, double destHeight, double opacity, bool interpolate) => _inner.DrawImage(source, srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight, opacity, interpolate);
    }

#pragma warning disable CS0067 // Events required by IHardware are not raised by this test fake.

    private sealed class FakeHardware : IHardware
    {
        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;

        public HardwareType HardwareType => HardwareType.Cpu;
        public Identifier Identifier { get; } = new("plot-pane-test");
        public string Name { get; set; } = "Test hardware";
        public IHardware Parent => null;
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public ISensor[] Sensors => Array.Empty<ISensor>();
        public IHardware[] SubHardware => Array.Empty<IHardware>();

        public string GetReport() => string.Empty;
        public void Update() { }
        public void Accept(IVisitor visitor) => visitor.VisitHardware(this);
        public void Traverse(IVisitor visitor) { }
    }

#pragma warning restore CS0067

    /// <summary>One sample per second ending at construction time.</summary>
    private sealed class FakeSensor : ISensor
    {
        internal FakeSensor(SensorType sensorType, string name, int count, Func<int, float> value)
        {
            SensorType = sensorType;
            Name = name;
            Identifier = new Identifier("plot-pane-test", sensorType.ToString().ToLowerInvariant(), name.ToLowerInvariant());
            DateTime first = DateTime.UtcNow.AddSeconds(-count);
            Values = Enumerable.Range(0, count).Select(index => new SensorValue(value(index), first.AddSeconds(index))).ToArray();
        }

        public IControl Control => null;
        public IHardware Hardware { get; } = new FakeHardware();
        public Identifier Identifier { get; }
        public int Index => 0;
        public bool IsDefaultHidden => false;
        public float? Max => null;
        public float? Min => null;
        public string Name { get; set; }
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType { get; }
        public float? Value => null;
        public IEnumerable<SensorValue> Values { get; }
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }
        public void ResetMax() { }
        public void ClearValues() { }
        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);
        public void Traverse(IVisitor visitor) { }
    }
}

#pragma warning restore CS0618
