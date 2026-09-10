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
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotPanelLaneTests
{
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
        internal FakeSensor(SensorType sensorType, string name = "Test sensor")
        {
            SensorType = sensorType;
            Name = name;
            Identifier = new Identifier("plot-lane-test", sensorType.ToString().ToLowerInvariant(), name.ToLowerInvariant());
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
        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }
        public void ResetMax() { }
        public void ClearValues() { }
        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);
        public void Traverse(IVisitor visitor) { }
    }
}
