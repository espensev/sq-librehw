// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotPanelLaneTests
{
    [Theory]
    [InlineData(120, false, false)]
    [InlineData(-120, false, false)]
    [InlineData(120, true, false)]
    [InlineData(120, true, true)]
    public void ShiftWheel_ZoomsOnlyHoveredValueAxis(int delta, bool overLabels, bool overlay)
    {
        var settings = new PersistentSettings();
        settings.SetValue("stackedAxes", !overlay);
        using var panel = new PlotPanel(settings, new UnitManager(settings));
        string laneKey = panel.CreateLane(SensorType.Power, "CPU").Key;
        BindHistory(panel, (new FakeSensor(SensorType.Power, "GPU", 40, 60), null),
            (new FakeSensor(SensorType.Power, "CPU", 100, 200), laneKey));
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        RenderPlot(view);
        Axis target = GetAxis(panel, laneKey);
        double min = target.ActualMinimum;
        double max = target.ActualMaximum;
        double anchor = min + (max - min) * 0.3;
        var others = view.Model.Axes.Where(a => a != target)
            .Select(a => (Axis: a, Min: a.ActualMinimum, Max: a.ActualMaximum)).ToArray();
        double x = overLabels ? view.Model.PlotArea.Left - 10 : view.Model.PlotArea.Center.X;
        var point = new ScreenPoint(x, target.Transform(anchor));

        view.ActualController.HandleMouseWheel(view, new OxyMouseWheelEventArgs
        {
            Position = point, Delta = delta, ModifierKeys = OxyModifierKeys.Shift
        });
        RenderPlot(view);

        double span = target.ActualMaximum - target.ActualMinimum;
        Assert.True(delta > 0 ? span < max - min : span > max - min);
        Assert.Equal(anchor, target.InverseTransform(point.Y), 6);
        foreach (var other in others)
        {
            Assert.Equal(other.Min, other.Axis.ActualMinimum);
            Assert.Equal(other.Max, other.Axis.ActualMaximum);
        }
        panel.SetCurrentSettings();
        Assert.True(settings.Contains("plotPanel.Min" + laneKey));
        Assert.False(settings.Contains("plotPanel.MinPower"));
    }

    [Theory]
    [InlineData(false, false, false)] // Zoom disabled.
    [InlineData(true, true, false)]   // Outside the lanes.
    [InlineData(true, false, true)]   // Ambiguous overlay plot area.
    public void ShiftWheel_InvalidTargetLeavesEveryAxisUnchanged(bool zoomEnabled, bool outside, bool overlay)
    {
        var settings = new PersistentSettings();
        settings.SetValue("yAxesEnableZoom", zoomEnabled);
        settings.SetValue("stackedAxes", !overlay);
        using var panel = new PlotPanel(settings, new UnitManager(settings));
        BindHistory(panel, (new FakeSensor(SensorType.Power, "CPU", 40, 60), null));
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        RenderPlot(view);
        var before = view.Model.Axes.Select(a => (a.ActualMinimum, a.ActualMaximum)).ToArray();
        view.ActualController.HandleMouseWheel(view, new OxyMouseWheelEventArgs
        {
            Position = outside ? new ScreenPoint(0, 0) : view.Model.PlotArea.Center,
            Delta = 120, ModifierKeys = OxyModifierKeys.Shift
        });
        RenderPlot(view);
        Assert.Equal(before, view.Model.Axes.Select(a => (a.ActualMinimum, a.ActualMaximum)).ToArray());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void FineValueGrid_LabelsFitShortLanesAfterResizeAndWeightChange(int textScale)
    {
        var settings = new PersistentSettings();
        using var panel = new PlotPanel(settings, new UnitManager(settings));
        string laneKey = panel.CreateLane(SensorType.Power, "CPU").Key;
        BindHistory(panel, (new FakeSensor(SensorType.Power, "GPU", 40, 60), null),
            (new FakeSensor(SensorType.Power, "CPU", 100, 200), laneKey));
        panel.SetAxisTextScale(textScale);
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        foreach (int height in new[] { 300, 600 })
        {
            panel.SetLaneWeight(laneKey, height == 300 ? 1 : 3);
            panel.InvalidatePlot();
            RenderPlot(view, height);
            foreach (Axis axis in view.Model.Axes.Where(a => a.IsAxisVisible && a.IsVertical()))
            {
                axis.GetTickValues(out var labels, out _, out _);
                var pixels = labels.Select(axis.Transform).OrderBy(y => y).ToArray();
                Assert.True(pixels.Length >= 2);
                for (int i = 1; i < pixels.Length; i++)
                    Assert.True(pixels[i] - pixels[i - 1] >= axis.FontSize * 1.5,
                        $"{axis.Key}: labels only {pixels[i] - pixels[i - 1]:F1}px apart at {axis.FontSize}px font");
            }
        }
    }

    private static void RenderPlot(PlotView view, int height = 400)
    {
        using Bitmap bitmap = new PngExporter { Width = 1000, Height = height }.ExportToBitmap(view.Model);
        Assert.Null(view.Model.GetLastPlotException());
    }

    [Fact]
    public void CreateLane_BindsSameTypeSeriesAndPreservesAxisPresentation()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        using var panel = new PlotPanel(settings, unitManager);
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        panel.SetAxisTextScale(150);

        PlotLane lane = panel.CreateLane(SensorType.Voltage, "Vcore");
        var sensor = new FakeSensor(SensorType.Voltage);
        panel.SetSensors(
            new List<ISensor> { sensor },
            new Dictionary<ISensor, Color> { [sensor] = Color.Red },
            new Dictionary<ISensor, string> { [sensor] = lane.Key },
            2);

        LineSeries series = Assert.IsType<LineSeries>(Assert.Single(view.Model.Series));
        LinearAxis axis = Assert.IsType<LinearAxis>(view.Model.Axes.Single(candidate => candidate.Key == lane.Key));
        Assert.Equal(lane.Key, series.YAxisKey);
        Assert.Equal("Vcore", axis.Title);
        Assert.Equal("V", axis.Unit);
        Assert.True(axis.IsAxisVisible);
        Assert.Equal(view.Model.Axes[0].FontSize, axis.FontSize);
        Assert.Equal(view.Model.Axes[0].TextColor, axis.TextColor);
    }

    [Fact]
    public void LaneZoom_AutoRangeAndSubsequentManualZoomPersistTheirModes()
    {
        var settings = new PersistentSettings();
        settings.SetValue("plotPanel.AutoFitYOnStart", false);
        var unitManager = new UnitManager(settings);
        string laneKey;
        string minKey;
        string maxKey;

        using (var panel = new PlotPanel(settings, unitManager))
        {
            PlotView view = panel.Controls.OfType<PlotView>().Single();
            PlotLane lane = panel.CreateLane(SensorType.Power, "CPU Power");
            laneKey = lane.Key;
            minKey = "plotPanel.Min" + lane.Key;
            maxKey = "plotPanel.Max" + lane.Key;
            LinearAxis axis = Assert.IsType<LinearAxis>(view.Model.Axes.Single(candidate => candidate.Key == lane.Key));

            axis.Zoom(20, 120);
            panel.SetCurrentSettings();

            Assert.True(settings.Contains(minKey));
            Assert.True(settings.Contains(maxKey));
            Assert.Equal(20, settings.GetValue(minKey, float.NaN), 3);
            Assert.Equal(120, settings.GetValue(maxKey, float.NaN), 3);

            Assert.True(panel.AutoRangeLane(lane.Key));
            panel.SetCurrentSettings();
            Assert.False(settings.Contains(minKey));
            Assert.False(settings.Contains(maxKey));

            axis.Zoom(30, 90);
            panel.SetCurrentSettings();
            Assert.Equal(30, settings.GetValue(minKey, float.NaN), 3);
            Assert.Equal(90, settings.GetValue(maxKey, float.NaN), 3);
        }

        using var restored = new PlotPanel(settings, unitManager);
        PlotView restoredView = restored.Controls.OfType<PlotView>().Single();
        LinearAxis restoredAxis = Assert.IsType<LinearAxis>(restoredView.Model.Axes.Single(axis => axis.Key == laneKey));
        Assert.Equal(30, restoredAxis.ActualMinimum, 3);
        Assert.Equal(90, restoredAxis.ActualMaximum, 3);
    }

    [Fact]
    public void CustomizedLaneZoom_SurvivesRestartAndFirstHistoryIncludingDefaultLane()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var cpu = new FakeSensor(SensorType.Power, "CPU", 40, 60);
        var gpu = new FakeSensor(SensorType.Power, "GPU", 150, 180);
        string laneKey;

        using (var panel = new PlotPanel(settings, unitManager))
        {
            laneKey = panel.CreateLane(SensorType.Power, "GPU Power").Key;
            BindHistory(panel, (cpu, null), (gpu, laneKey));
            GetAxis(panel, "Power").Zoom(20, 100);
            GetAxis(panel, laneKey).Zoom(120, 220);
            panel.SetCurrentSettings();
        }

        Assert.False(settings.Contains("plotPanel.AutoFitYOnStart"));
        using var restored = new PlotPanel(settings, unitManager);
        Assert.Equal(20, GetAxis(restored, "Power").ActualMinimum);
        Assert.Equal(120, GetAxis(restored, laneKey).ActualMinimum);

        BindHistory(restored, (cpu, null), (gpu, laneKey));
        restored.InvalidatePlot();
        restored.SetCurrentSettings();

        Assert.Equal(20, GetAxis(restored, "Power").ActualMinimum);
        Assert.Equal(100, GetAxis(restored, "Power").ActualMaximum);
        Assert.Equal(120, GetAxis(restored, laneKey).ActualMinimum);
        Assert.Equal(220, GetAxis(restored, laneKey).ActualMaximum);
        Assert.Equal(20, settings.GetValue("plotPanel.MinPower", float.NaN));
        Assert.Equal(120, settings.GetValue("plotPanel.Min" + laneKey, float.NaN));

        Assert.True(restored.RemoveLane(laneKey));
        restored.InvalidatePlot();
        Assert.Equal(20, GetAxis(restored, "Power").ActualMinimum);
        Assert.Equal(100, GetAxis(restored, "Power").ActualMaximum);
    }

    [Fact]
    public void WeightedDefaultLaneZoom_SurvivesRestartAndFirstHistory()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var sensor = new FakeSensor(SensorType.Voltage, "Vcore", 1.1f, 1.2f);

        using (var panel = new PlotPanel(settings, unitManager))
        {
            Assert.True(panel.SetLaneWeight("Voltage", 3));
            BindHistory(panel, (sensor, null));
            GetAxis(panel, "Voltage").Zoom(0.5, 1.5);
            panel.SetCurrentSettings();
        }

        Assert.False(settings.Contains("plotPanel.AutoFitYOnStart"));
        using var restored = new PlotPanel(settings, unitManager);
        Assert.Equal(3, Assert.Single(restored.GetLanes(SensorType.Voltage)).Weight);
        BindHistory(restored, (sensor, null));
        restored.InvalidatePlot();
        restored.SetCurrentSettings();

        Assert.Equal(0.5, GetAxis(restored, "Voltage").ActualMinimum);
        Assert.Equal(1.5, GetAxis(restored, "Voltage").ActualMaximum);
        Assert.Equal(0.5f, settings.GetValue("plotPanel.MinVoltage", float.NaN));
        Assert.Equal(1.5f, settings.GetValue("plotPanel.MaxVoltage", float.NaN));
    }

    [Fact]
    public void LegacyLayout_AutoFitsStaleZoomOnceAndPreservesLaterManualZoom()
    {
        var settings = new PersistentSettings();
        settings.SetValue("plotPanel.MinPower", 1000f);
        settings.SetValue("plotPanel.MaxPower", 2000f);
        var unitManager = new UnitManager(settings);
        var sensor = new FakeSensor(SensorType.Power, "CPU", 40, 60);
        using var panel = new PlotPanel(settings, unitManager);
        LinearAxis axis = GetAxis(panel, "Power");
        Assert.Equal(1000, axis.ActualMinimum);
        Assert.False(settings.Contains("plotPanel.AutoFitYOnStart"));

        BindHistory(panel, (sensor, null));
        panel.SetCurrentSettings();

        Assert.NotEqual(1000, axis.ActualMinimum);
        Assert.NotEqual(2000, axis.ActualMaximum);
        Assert.False(settings.Contains("plotPanel.MinPower"));
        Assert.False(settings.Contains("plotPanel.MaxPower"));

        axis.Zoom(20, 100);
        BindHistory(panel, (sensor, null));
        panel.InvalidatePlot();
        panel.SetCurrentSettings();

        Assert.Equal(20, axis.ActualMinimum);
        Assert.Equal(100, axis.ActualMaximum);
        Assert.Equal(20, settings.GetValue("plotPanel.MinPower", float.NaN));
        Assert.Equal(100, settings.GetValue("plotPanel.MaxPower", float.NaN));
    }

    [Fact]
    public void CustomizedLayout_ExplicitAutoRangeAndAutoscaleAllStillClearManualZoom()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var cpu = new FakeSensor(SensorType.Power, "CPU", 40, 60);
        var gpu = new FakeSensor(SensorType.Power, "GPU", 150, 180);
        using var panel = new PlotPanel(settings, unitManager);
        string laneKey = panel.CreateLane(SensorType.Power, "GPU Power").Key;
        BindHistory(panel, (cpu, null), (gpu, laneKey));
        LinearAxis defaultAxis = GetAxis(panel, "Power");
        LinearAxis userAxis = GetAxis(panel, laneKey);
        defaultAxis.Zoom(20, 100);
        userAxis.Zoom(120, 220);
        panel.SetCurrentSettings();

        Assert.True(panel.AutoRangeLane(laneKey));
        BindHistory(panel, (cpu, null), (gpu, laneKey));
        panel.SetCurrentSettings();

        Assert.Equal(20, defaultAxis.ActualMinimum);
        Assert.Equal(100, defaultAxis.ActualMaximum);
        Assert.NotEqual(120, userAxis.ActualMinimum);
        Assert.NotEqual(220, userAxis.ActualMaximum);
        Assert.False(settings.Contains("plotPanel.Min" + laneKey));
        Assert.False(settings.Contains("plotPanel.Max" + laneKey));

        userAxis.Zoom(130, 210);
        panel.AutoscaleAllYAxes();
        BindHistory(panel, (cpu, null), (gpu, laneKey));
        panel.SetCurrentSettings();

        foreach (string key in new[] { "Power", laneKey })
        {
            Assert.False(settings.Contains("plotPanel.Min" + key));
            Assert.False(settings.Contains("plotPanel.Max" + key));
        }
        Assert.NotEqual(20, defaultAxis.ActualMinimum);
        Assert.NotEqual(100, defaultAxis.ActualMaximum);
        Assert.NotEqual(130, userAxis.ActualMinimum);
        Assert.NotEqual(210, userAxis.ActualMaximum);
    }

    [Fact]
    public void RemovedLaneNumberIsNotReusedAcrossPanelRestart()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);

        using (var panel = new PlotPanel(settings, unitManager))
        {
            PlotLane removed = panel.CreateLane(SensorType.Fan, "Pump");
            Assert.True(panel.RemoveLane(removed.Key));
            Assert.Equal("Fan#3", panel.CreateLane(SensorType.Fan, "Radiator").Key);
            panel.SetCurrentSettings();
        }

        using var restored = new PlotPanel(settings, unitManager);
        Assert.Equal("Fan 4", restored.GetSuggestedLaneName(SensorType.Fan));
        Assert.Equal("Fan#4", restored.CreateLane(SensorType.Fan, "Case").Key);
    }

    [Fact]
    public void StackedLanePositionsUseVisibleWeightsOnly()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        using var panel = new PlotPanel(settings, unitManager);
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        PlotLane extra = panel.CreateLane(SensorType.Temperature, "GPU");
        Assert.True(panel.SetLaneWeight("Temperature", 1));
        Assert.True(panel.SetLaneWeight(extra.Key, 3));
        var cpu = new FakeSensor(SensorType.Temperature, "CPU");
        var gpu = new FakeSensor(SensorType.Temperature, "GPU");

        panel.SetSensors(
            new List<ISensor> { cpu, gpu },
            new Dictionary<ISensor, Color> { [cpu] = Color.Red, [gpu] = Color.Blue },
            new Dictionary<ISensor, string> { [cpu] = null, [gpu] = extra.Key },
            2);

        LinearAxis defaultAxis = Assert.IsType<LinearAxis>(view.Model.Axes.Single(axis => axis.Key == "Temperature"));
        LinearAxis extraAxis = Assert.IsType<LinearAxis>(view.Model.Axes.Single(axis => axis.Key == extra.Key));
        Assert.Equal(0.25, defaultAxis.EndPosition - defaultAxis.StartPosition, 10);
        Assert.Equal(0.75, extraAxis.EndPosition - extraAxis.StartPosition, 10);
    }

    [Fact]
    public void OverlayLanePositionsIgnoreWeights()
    {
        var settings = new PersistentSettings();
        settings.SetValue("stackedAxes", false);
        var unitManager = new UnitManager(settings);
        using var panel = new PlotPanel(settings, unitManager);
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        PlotLane extra = panel.CreateLane(SensorType.Fan, "Pump");
        Assert.True(panel.SetLaneWeight(extra.Key, 3));
        var radiator = new FakeSensor(SensorType.Fan, "Radiator");
        var pump = new FakeSensor(SensorType.Fan, "Pump");

        panel.SetSensors(
            new List<ISensor> { radiator, pump },
            new Dictionary<ISensor, Color> { [radiator] = Color.Red, [pump] = Color.Blue },
            new Dictionary<ISensor, string> { [radiator] = null, [pump] = extra.Key },
            2);

        foreach (LinearAxis axis in view.Model.Axes.OfType<LinearAxis>().Where(axis => axis.IsAxisVisible))
        {
            Assert.Equal(0, axis.StartPosition);
            Assert.Equal(1, axis.EndPosition);
        }
    }

    [Fact]
    public void RemoveLaneReturnsItsCurrentSeriesToDefaultAndPublishesTheRemovedKey()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        using var panel = new PlotPanel(settings, unitManager);
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        PlotLane lane = panel.CreateLane(SensorType.Power, "GPU Power");
        var sensor = new FakeSensor(SensorType.Power);
        string removedKey = null;
        panel.LaneRemoved = key => removedKey = key;
        panel.SetSensors(
            new List<ISensor> { sensor },
            new Dictionary<ISensor, Color> { [sensor] = Color.Red },
            new Dictionary<ISensor, string> { [sensor] = lane.Key },
            2);

        Assert.True(panel.RemoveLane(lane.Key));

        LineSeries series = Assert.IsType<LineSeries>(Assert.Single(view.Model.Series));
        Assert.Equal(lane.Key, removedKey);
        Assert.Equal("Power", series.YAxisKey);
        Assert.DoesNotContain(view.Model.Axes, axis => axis.Key == lane.Key);
    }

    [Fact]
    public void SensorNodeGraphLaneKeyPersistsAndDefaultRemovesTheSetting()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var sensor = new FakeSensor(SensorType.Power);
        var node = new SensorNode(sensor, settings, unitManager);
        string key = new Identifier(sensor.Identifier, "graphLane").ToString();
        int changes = 0;
        node.PlotSelectionChanged += (sender, args) => changes++;

        node.GraphLaneKey = "Power#2";

        Assert.Equal("Power#2", settings.GetValue(key, (string)null));
        Assert.Equal(1, changes);
        Assert.Equal("Power#2", new SensorNode(sensor, settings, unitManager).GraphLaneKey);

        node.GraphLaneKey = null;

        Assert.False(settings.Contains(key));
        Assert.Equal(2, changes);
    }

    private static LinearAxis GetAxis(PlotPanel panel, string key)
    {
        PlotView view = panel.Controls.OfType<PlotView>().Single();
        return Assert.IsType<LinearAxis>(view.Model.Axes.Single(axis => axis.Key == key));
    }

    private static void BindHistory(PlotPanel panel, params (ISensor Sensor, string LaneKey)[] bindings)
    {
        panel.SetSensors(
            bindings.Select(binding => binding.Sensor).ToList(),
            bindings.ToDictionary(binding => binding.Sensor, _ => Color.Red),
            bindings.ToDictionary(binding => binding.Sensor, binding => binding.LaneKey),
            2);

        PlotView view = panel.Controls.OfType<PlotView>().Single();
        Assert.All(view.Model.Series.Cast<LineSeries>(), series =>
            Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<DataPoint>>(series.ItemsSource)));
        ((IPlotModel)view.Model).Update(true);
    }

#pragma warning disable CS0067 // Events required by IHardware are not raised by this test fake.

    private sealed class FakeHardware : IHardware
    {
        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;

        public HardwareType HardwareType => HardwareType.Cpu;
        public Identifier Identifier { get; } = new("plot-lane-test");
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

    private sealed class FakeSensor : ISensor
    {
        internal FakeSensor(SensorType sensorType, string name = "Test sensor", params float[] history)
        {
            SensorType = sensorType;
            Name = name;
            Identifier = new Identifier("plot-lane-test", sensorType.ToString().ToLowerInvariant(), name.ToLowerInvariant());
            DateTime timeOrigin = DateTime.UtcNow.AddSeconds(-history.Length);
            Values = history.Select((value, index) => new SensorValue(value, timeOrigin.AddSeconds(index))).ToArray();
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
