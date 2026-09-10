// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;
using WinFormsControl = System.Windows.Forms.Control;

namespace LibreHardwareMonitor.Tests;

public sealed class PresentationSurfaceCoordinatorTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly WinFormsControl _plotControl = new();
    private readonly FakeTreeSurface _tree;
    private readonly FakePlotSurface _plot;
    private readonly FakeTraySurface _tray;
    private readonly FakeGadgetSurface _gadget;

    public PresentationSurfaceCoordinatorTests()
    {
        _tree = new FakeTreeSurface(_log);
        _plot = new FakePlotSurface(_log, _plotControl);
        _tray = new FakeTraySurface(_log);
        _gadget = new FakeGadgetSurface(_log);
    }

    public void Dispose()
    {
        _plotControl.Dispose();
    }

    [Fact]
    public void RefreshSurfaces_VisiblePlot_RedrawsTreeThenTrayThenGadgetThenPlot()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);

        coordinator.RefreshSurfaces(plotVisible: true);

        Assert.Equal(new[] { "tree.Redraw", "tray.Redraw", "gadget.Redraw", "plot.Redraw" }, _log);
    }

    [Fact]
    public void RefreshSurfaces_HiddenPlot_SkipsOnlyPlotRedraw()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);

        coordinator.RefreshSurfaces(plotVisible: false);

        Assert.Equal(new[] { "tree.Redraw", "tray.Redraw", "gadget.Redraw" }, _log);
    }

    [Fact]
    public void RefreshSurfaces_UnavailableGadget_ReportsUnavailableAndSkipsGadgetRedraw()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: false);

        Assert.False(coordinator.IsGadgetAvailable);
        Assert.False(coordinator.IsGadgetVisible);

        coordinator.RefreshSurfaces(plotVisible: true);

        Assert.Equal(new[] { "tree.Redraw", "tray.Redraw", "plot.Redraw" }, _log);
        Assert.False(coordinator.TryRedrawGadget());
        Assert.Equal(0, _gadget.RedrawCount);
    }

    [Fact]
    public void Plot_ControlIdentityResetCallbackAndCurrentSettingsForwardUnchanged()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        Action reset = () => { };

        coordinator.PlotResetGraphView = reset;
        coordinator.ApplyPlotCurrentSettings();

        Assert.Same(_plotControl, coordinator.PlotControl);
        Assert.Same(reset, _plot.ResetGraphView);
        Assert.Same(reset, coordinator.PlotResetGraphView);
        Assert.Equal(1, _plot.SetCurrentSettingsCount);
    }

    [Fact]
    public void SetPlotSensors_ForwardsSensorSequenceColorDictionaryInstanceAndStroke()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        FakeSensor sensor = new("first");
        List<ISensor> sensors = new() { sensor };
        Dictionary<ISensor, Color> colors = new() { [sensor] = Color.Red };

        Dictionary<ISensor, string> laneKeys = new() { [sensor] = "Temperature" };

        coordinator.SetPlotSensors(sensors, colors, laneKeys, 2.5d);

        Assert.Same(sensors, _plot.Sensors);
        Assert.Same(colors, _plot.Colors);
        Assert.Same(laneKeys, _plot.LaneKeys);
        Assert.Equal(2.5d, _plot.SetSensorsStrokeThickness);
    }

    [Fact]
    public void Plot_StrokeAxisAndTrackerScalesForwardExactValues()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);

        coordinator.UpdatePlotStrokeThickness(1.75d);
        coordinator.SetPlotAxisTextScale(140);
        coordinator.SetPlotTrackerTextScale(115);

        Assert.Equal(1.75d, _plot.UpdatedStrokeThickness);
        Assert.Equal(140, _plot.AxisTextScalePercent);
        Assert.Equal(115, _plot.TrackerTextScalePercent);
    }

    [Fact]
    public void Tray_MainIconAndMembershipPreserveExactBalloonArgument()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        FakeSensor silent = new("silent");
        FakeSensor announced = new("announced");
        _tray.Members.Add(silent);

        coordinator.IsTrayMainIconEnabled = true;
        coordinator.AddToTray(silent, balloonTip: false);
        coordinator.AddToTray(announced, balloonTip: true);
        coordinator.RemoveFromTray(silent);

        Assert.True(_tray.IsMainIconEnabled);
        Assert.True(coordinator.IsTrayMainIconEnabled);
        Assert.Equal(new[] { false, true }, _tray.BalloonArguments);
        Assert.Equal(new ISensor[] { silent, announced }, _tray.Added);
        Assert.Equal(new ISensor[] { silent }, _tray.Removed);
        Assert.False(coordinator.TrayContains(silent));
        Assert.False(coordinator.TrayContains(announced));
    }

    [Fact]
    public void Gadget_VisibilityAndMembershipForwardUnchanged()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        FakeSensor sensor = new("gadget-member");
        _gadget.Members.Add(sensor);

        Assert.True(coordinator.IsGadgetAvailable);
        Assert.True(coordinator.TrySetGadgetVisible(true));
        Assert.True(coordinator.GadgetContains(sensor));
        Assert.True(coordinator.AddToGadget(sensor));
        Assert.True(coordinator.RemoveFromGadget(sensor));
        Assert.True(coordinator.TryRedrawGadget());

        Assert.True(_gadget.Visible);
        Assert.True(coordinator.IsGadgetVisible);
        Assert.Equal(new ISensor[] { sensor }, _gadget.Added);
        Assert.Equal(new ISensor[] { sensor }, _gadget.Removed);
        Assert.Equal(1, _gadget.RedrawCount);
    }

    [Fact]
    public void Gadget_UnavailableMembershipAndVisibilityAreSafeNoOps()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: false);
        FakeSensor sensor = new("absent-gadget");

        Assert.False(coordinator.GadgetContains(sensor));
        Assert.False(coordinator.AddToGadget(sensor));
        Assert.False(coordinator.RemoveFromGadget(sensor));
        Assert.False(coordinator.TrySetGadgetVisible(true));
        Assert.False(coordinator.IsGadgetVisible);

        Assert.Empty(_gadget.Added);
        Assert.Empty(_gadget.Removed);
        Assert.False(_gadget.Visible);
        Assert.Empty(_log);
    }

    [Fact]
    public void TrayHideShowAndExit_RelayOriginalSenderAndArguments()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        List<(object Sender, EventArgs Args)> hideShow = new();
        List<(object Sender, EventArgs Args)> exit = new();
        coordinator.HideShowRequested += (sender, e) => hideShow.Add((sender, e));
        coordinator.ExitRequested += (sender, e) => exit.Add((sender, e));
        EventArgs hideShowArgs = new();
        EventArgs exitArgs = new();

        _tray.RaiseHideShowCommand(_tray, hideShowArgs);
        _tray.RaiseExitCommand(_tray, exitArgs);

        Assert.Equal(new[] { ((object)_tray, hideShowArgs) }, hideShow);
        Assert.Equal(new[] { ((object)_tray, exitArgs) }, exit);
    }

    [Fact]
    public void GadgetHideShow_RelaysOriginalSenderAndArguments()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        List<(object Sender, EventArgs Args)> hideShow = new();
        coordinator.HideShowRequested += (sender, e) => hideShow.Add((sender, e));
        EventArgs args = new();

        _gadget.RaiseHideShowCommand(_gadget, args);

        Assert.Equal(new[] { ((object)_gadget, args) }, hideShow);
    }

    [Fact]
    public void Dispose_IsIdempotentDetachesRelaysAndDisposesGadgetBeforeTray()
    {
        PresentationSurfaceCoordinator coordinator = CreateCoordinator(withGadget: true);
        int relayed = 0;
        coordinator.HideShowRequested += (_, _) => relayed++;
        coordinator.ExitRequested += (_, _) => relayed++;

        coordinator.Dispose();
        coordinator.Dispose();

        _tray.RaiseHideShowCommand(_tray, EventArgs.Empty);
        _tray.RaiseExitCommand(_tray, EventArgs.Empty);
        _gadget.RaiseHideShowCommand(_gadget, EventArgs.Empty);

        Assert.Equal(new[] { "gadget.Dispose", "tray.Dispose" }, _log);
        Assert.Equal(1, _gadget.DisposeCount);
        Assert.Equal(1, _tray.DisposeCount);
        Assert.Equal(0, relayed);
    }

    private PresentationSurfaceCoordinator CreateCoordinator(bool withGadget)
    {
        return new PresentationSurfaceCoordinator(_tree, _plot, _tray, withGadget ? _gadget : null);
    }

    private sealed class FakeTreeSurface : IPresentationTreeSurface
    {
        private readonly List<string> _log;

        internal FakeTreeSurface(List<string> log) => _log = log;

        public void Redraw() => _log.Add("tree.Redraw");
    }

    private sealed class FakePlotSurface : IPresentationPlotSurface
    {
        private readonly List<string> _log;

        internal FakePlotSurface(List<string> log, WinFormsControl control)
        {
            _log = log;
            Control = control;
        }

        public WinFormsControl Control { get; }

        public Action ResetGraphView { get; set; }

        public Action<string> LaneRemoved { get; set; }

        internal int SetCurrentSettingsCount { get; private set; }
        internal List<ISensor> Sensors { get; private set; }
        internal IDictionary<ISensor, Color> Colors { get; private set; }
        internal IDictionary<ISensor, string> LaneKeys { get; private set; }
        internal double? SetSensorsStrokeThickness { get; private set; }
        internal double? UpdatedStrokeThickness { get; private set; }
        internal int? AxisTextScalePercent { get; private set; }
        internal int? TrackerTextScalePercent { get; private set; }

        public void SetCurrentSettings() => SetCurrentSettingsCount++;

        public void SetSensors(
            List<ISensor> sensors,
            IDictionary<ISensor, Color> colors,
            IDictionary<ISensor, string> laneKeys,
            double strokeThickness)
        {
            Sensors = sensors;
            Colors = colors;
            LaneKeys = laneKeys;
            SetSensorsStrokeThickness = strokeThickness;
        }

        public IReadOnlyList<PlotLane> GetLanes(SensorType type) => Array.Empty<PlotLane>();

        public string GetSuggestedLaneName(SensorType type) => $"{type} lane";

        public string ResolveLaneKey(SensorType type, string persistedKey) => persistedKey;

        public PlotLane CreateLane(SensorType type, string name) => new($"{type}:2", name, type, 1, false);

        public ToolStripMenuItem CreateLanesMenu() => new("Lanes");

        public void UpdateStrokeThickness(double strokeThickness) => UpdatedStrokeThickness = strokeThickness;

        public void SetAxisTextScale(int percent) => AxisTextScalePercent = percent;

        public void SetTrackerTextScale(int percent) => TrackerTextScalePercent = percent;

        public void Redraw() => _log.Add("plot.Redraw");
    }

    private sealed class FakeTraySurface : IPresentationTraySurface
    {
        private readonly List<string> _log;

        internal FakeTraySurface(List<string> log) => _log = log;

        public event EventHandler HideShowCommand;

        public event EventHandler ExitCommand;

        public bool IsMainIconEnabled { get; set; }

        internal List<ISensor> Members { get; } = new();
        internal List<ISensor> Added { get; } = new();
        internal List<ISensor> Removed { get; } = new();
        internal List<bool> BalloonArguments { get; } = new();
        internal int DisposeCount { get; private set; }

        public void Redraw() => _log.Add("tray.Redraw");

        public bool Contains(ISensor sensor) => Members.Contains(sensor);

        public void Add(ISensor sensor, bool balloonTip)
        {
            Added.Add(sensor);
            BalloonArguments.Add(balloonTip);
        }

        public void Remove(ISensor sensor)
        {
            Removed.Add(sensor);
            Members.Remove(sensor);
        }

        public void Dispose()
        {
            DisposeCount++;
            _log.Add("tray.Dispose");
        }

        internal void RaiseHideShowCommand(object sender, EventArgs e) => HideShowCommand?.Invoke(sender, e);

        internal void RaiseExitCommand(object sender, EventArgs e) => ExitCommand?.Invoke(sender, e);
    }

    private sealed class FakeGadgetSurface : IPresentationGadgetSurface
    {
        private readonly List<string> _log;

        internal FakeGadgetSurface(List<string> log) => _log = log;

        public event EventHandler HideShowCommand;

        public bool Visible { get; set; }

        internal List<ISensor> Members { get; } = new();
        internal List<ISensor> Added { get; } = new();
        internal List<ISensor> Removed { get; } = new();
        internal int RedrawCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public void Redraw()
        {
            RedrawCount++;
            _log.Add("gadget.Redraw");
        }

        public bool Contains(ISensor sensor) => Members.Contains(sensor);

        public void Add(ISensor sensor) => Added.Add(sensor);

        public void Remove(ISensor sensor) => Removed.Add(sensor);

        public void Dispose()
        {
            DisposeCount++;
            _log.Add("gadget.Dispose");
        }

        internal void RaiseHideShowCommand(object sender, EventArgs e) => HideShowCommand?.Invoke(sender, e);
    }

    private sealed class FakeSensor : ISensor
    {
        private readonly Identifier _identifier;

        internal FakeSensor(string segment)
        {
            _identifier = new Identifier("fake", "sensor", segment);
        }

        public IControl Control => null;
        public IHardware Hardware => null;
        public Identifier Identifier => _identifier;
        public int Index => 0;
        public bool IsDefaultHidden => false;
        public float? Max => null;
        public float? Min => null;
        public string Name { get; set; } = "fake";
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType => SensorType.Temperature;
        public float? Value => null;
        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }

        public void ResetMin() { }
        public void ResetMax() { }
        public void ClearValues() { }
        public void Accept(IVisitor visitor) { }
        public void Traverse(IVisitor visitor) { }
    }
}
