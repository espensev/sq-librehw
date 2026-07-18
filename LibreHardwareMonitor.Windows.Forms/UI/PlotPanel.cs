// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using OxyPlot.Series;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class PlotPanel : UserControl
{
    private const double FineGridMajorDivisions = 20;
    private const int DefaultGridDensity = 3;
    private const int TimeAxisLabelModeLocalTime = 0;
    private const int TimeAxisLabelModeElapsed = 1;

    private readonly PersistentSettings _settings;
    private readonly UnitManager _unitManager;
    private readonly PlotView _plot;
    private readonly PlotModel _model;
    private ContextMenuStrip _menu;
    private Button _optionsButton;
    private readonly ToolTip _toolTip = new ToolTip();
    private readonly SessionTimeAxis _timeAxis = new SessionTimeAxis();
    private readonly SortedDictionary<SensorType, LinearAxis> _axes = new SortedDictionary<SensorType, LinearAxis>();
    private readonly Dictionary<SensorType, LineAnnotation> _annotations = new Dictionary<SensorType, LineAnnotation>();
    private UserOption _stackedAxes;
    private UserOption _showAxesLabels;
    private UserOption _timeAxisEnableZoom;
    private UserOption _yAxesEnableZoom;
    private UserRadioGroup _gridDensity;
    private UserRadioGroup _timeAxisLabelMode;
    private float _dpiX;
    private float _dpiY;
    private double _dpiXScale = 1;
    private double _dpiYScale = 1;
    private int _axisTextScalePercent = UiScale.DefaultPercent;
    private Font _trackerBaseFont;   // captured lazily on first apply (MainForm sets _plot.Font first)
    private Font _scaledTrackerFont;  // owned; disposed on replacement
    private Point _rightClickEnter;
    private bool _cancelContextMenu = false;

    // Series X values are seconds since this fixed session origin, so a point's coordinates never
    // change after creation. The visible window pans toward "now" each tick instead of every point
    // being re-aged, which lets OxyPlot reuse the materialized point lists (and render only the
    // visible window, since X is monotonic).
    private readonly DateTime _timeOrigin;
    private readonly PlotPanelHistoryStore _historyStore;
    private double _windowAgeMin;
    private double _windowAgeMax;
    private double _lastNowX;
    private bool _updatingTimeAxis;
    private TemperatureUnit _lastTemperatureUnit;
    private readonly bool _autoFitYOnStart;
    private bool _didAutoFitYAxesOnStart;

    // The default tracker fills its X slot with TimeSpan.FromSeconds(axis value), which since the
    // session-origin rework would read as time-since-launch. Substitute a value that matches the
    // active label mode (wall-clock time, or age before "now" in Elapsed mode).
    private sealed class SessionTimeAxis : TimeSpanAxis
    {
        public Func<double, object> TrackerValue { get; set; }

        public override object GetValue(double x)
        {
            return TrackerValue != null ? TrackerValue(x) : base.GetValue(x);
        }
    }

    public PlotPanel(PersistentSettings settings, UnitManager unitManager)
    {
        _settings = settings;
        _unitManager = unitManager;
        _timeOrigin = DateTime.UtcNow;
        _historyStore = new PlotPanelHistoryStore(_timeOrigin);
        _lastTemperatureUnit = unitManager.TemperatureUnit;

        // One-time gate for the startup Y-axis auto-fit (see InvalidatePlot()): reclaims empty
        // graph bands left over from a stale persisted zoom (axis.Zoom(...) below in
        // CreatePlotModel), without touching later in-session zooms (manual or menu-driven).
        _autoFitYOnStart = _settings.GetValue("plotPanel.AutoFitYOnStart", true);

        SetDpi();
        _model = CreatePlotModel();

        _plot = new PlotView { Dock = DockStyle.Fill, Model = _model, BackColor = Color.Black, ContextMenuStrip = CreateMenu() };
        _plot.MouseDown += (sender, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _rightClickEnter = e.Location;
            }
        };
        _plot.MouseMove += (sender, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                if (!_cancelContextMenu && e.Location.DistanceTo(_rightClickEnter) > 10.0f)
                {
                    _cancelContextMenu = true;
                }
            }
        };

        UpdateAxesPosition();

        SuspendLayout();
        Controls.Add(_plot);
        ResumeLayout(true);
        CreateOptionsButton();
        _plot.ShowTracker(new TrackerHitResult());
        _plot.HideTracker();
        foreach (Control plotControl in _plot.Controls)
        {
            plotControl.BackColor = Theme.Current.PlotBackgroundColor;
            plotControl.ForeColor = Theme.Current.PlotTextColor;
        }
        ApplyTheme();
    }

    /// <summary>
    /// Shared command wired by the owner (MainForm) so the graph-local menu can offer the same
    /// "Reset Graph View" semantics as the main Graph menu without duplicating its logic.
    /// </summary>
    public Action ResetGraphView { get; set; }

    public void ApplyTheme()
    {
        _model.Background = Theme.Current.PlotBackgroundColor.ToOxyColor();
        _model.PlotAreaBorderColor = Theme.Current.PlotBorderColor.ToOxyColor();

        if (_optionsButton != null)
        {
            _optionsButton.BackColor = Theme.Current.PlotBackgroundColor;
            _optionsButton.ForeColor = Theme.Current.PlotTextColor;
            _optionsButton.FlatAppearance.BorderColor = Theme.Current.PlotBorderColor;
        }
        foreach (Axis axis in _model.Axes)
        {
            axis.AxislineColor = Theme.Current.PlotBorderColor.ToOxyColor();
            axis.MajorGridlineColor = Theme.Current.PlotGridMajorColor.ToOxyColor();
            axis.MinorGridlineColor = Theme.Current.PlotGridMinorColor.ToOxyColor();
            axis.TextColor = Theme.Current.PlotTextColor.ToOxyColor();
            axis.TitleColor = Theme.Current.PlotTextColor.ToOxyColor();
            axis.MinorTicklineColor = Theme.Current.PlotBorderColor.ToOxyColor();
            axis.TicklineColor = Theme.Current.PlotBorderColor.ToOxyColor();
        }
        foreach (LineAnnotation annotation in _model.Annotations.Select(x => x as LineAnnotation).Where(x => x != null))
        {
            annotation.Color = Theme.Current.PlotBorderColor.ToOxyColor();
        }

        ApplyGridDensity();
    }

    public void SetCurrentSettings()
    {
        foreach (LinearAxis axis in _axes.Values)
        {
            _settings.SetValue("plotPanel.Min" + axis.Key, (float)axis.ActualMinimum);
            _settings.SetValue("plotPanel.Max" + axis.Key, (float)axis.ActualMaximum);
        }

        // Persist the window in age semantics (seconds before "now"), matching what the
        // age-valued axis used to store under the same keys.
        double ageMin = _windowAgeMin;
        double ageMax = _windowAgeMax;
        if (double.IsNaN(ageMax))
        {
            ageMax = Math.Max(0, _lastNowX - _timeAxis.ActualMinimum);
            ageMin = Math.Max(0, _lastNowX - _timeAxis.ActualMaximum);
        }

        _settings.SetValue("plotPanel.MinTimeSpan", (float)ageMin);
        _settings.SetValue("plotPanel.MaxTimeSpan", (float)ageMax);
    }

    private ContextMenuStrip CreateMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Renderer = new ThemedToolStripRenderer();
        menu.Opening += (sender, e) =>
        {
            if (_cancelContextMenu)
            {
                e.Cancel = true;
                _cancelContextMenu = false;
            }
        };

        ToolStripMenuItem stackedAxesMenuItem = new ToolStripMenuItem("Stacked Axes");
        _stackedAxes = new UserOption("stackedAxes", true, stackedAxesMenuItem, _settings);
        _stackedAxes.Changed += (sender, e) =>
        {
            UpdateAxesPosition();
            InvalidatePlotCosmetic();
        };
        menu.Items.Add(stackedAxesMenuItem);

        ToolStripMenuItem showAxesLabelsMenuItem = new ToolStripMenuItem("Show Axes Labels");
        _showAxesLabels = new UserOption("showAxesLabels", true, showAxesLabelsMenuItem, _settings);
        _showAxesLabels.Changed += (sender, e) =>
        {
            if (_showAxesLabels.Value)
                _model.PlotMargins = new OxyThickness(double.NaN);
            else
                _model.PlotMargins = new OxyThickness(0);
        };
        menu.Items.Add(showAxesLabelsMenuItem);

        ToolStripMenuItem gridDensityMenuItem = new ToolStripMenuItem("Grid Density");
        ToolStripMenuItem[] gridDensityMenuItems =
        {
            new ToolStripMenuItem("Off"),
            new ToolStripMenuItem("Major"),
            new ToolStripMenuItem("Normal"),
            new ToolStripMenuItem("Fine")
        };

        foreach (ToolStripItem mi in gridDensityMenuItems)
            gridDensityMenuItem.DropDownItems.Add(mi);
        menu.Items.Add(gridDensityMenuItem);

        _gridDensity = new UserRadioGroup("plotGridDensity", DefaultGridDensity, gridDensityMenuItems, _settings);
        _gridDensity.Changed += (sender, e) => InvalidatePlotCosmetic();

        ToolStripMenuItem timeAxisMenuItem = new ToolStripMenuItem("Time Axis");
        ToolStripMenuItem[] timeAxisMenuItems =
        { new ToolStripMenuItem("Enable Zoom"),
            new ToolStripMenuItem("Auto", null, (s, e) => { TimeAxisZoom(0, double.NaN); }),
            new ToolStripMenuItem("30 sec", null, (s, e) => { TimeAxisZoom(0, 30); }),
            new ToolStripMenuItem("1 min", null, (s, e) => { TimeAxisZoom(0, 60); }),
            new ToolStripMenuItem("2 min", null, (s, e) => { TimeAxisZoom(0, 2 * 60); }),
            new ToolStripMenuItem("5 min", null, (s, e) => { TimeAxisZoom(0, 5 * 60); }),
            new ToolStripMenuItem("10 min", null, (s, e) => { TimeAxisZoom(0, 10 * 60); }),
            new ToolStripMenuItem("20 min", null, (s, e) => { TimeAxisZoom(0, 20 * 60); }),
            new ToolStripMenuItem("30 min", null, (s, e) => { TimeAxisZoom(0, 30 * 60); }),
            new ToolStripMenuItem("45 min", null, (s, e) => { TimeAxisZoom(0, 45 * 60); }),
            new ToolStripMenuItem("1 h", null, (s, e) => { TimeAxisZoom(0, 60 * 60); }),
            new ToolStripMenuItem("1.5 h", null, (s, e) => { TimeAxisZoom(0, 1.5 * 60 * 60); }),
            new ToolStripMenuItem("2 h", null, (s, e) => { TimeAxisZoom(0, 2 * 60 * 60); }),
            new ToolStripMenuItem("3 h", null, (s, e) => { TimeAxisZoom(0, 3 * 60 * 60); }),
            new ToolStripMenuItem("6 h", null, (s, e) => { TimeAxisZoom(0, 6 * 60 * 60); }),
            new ToolStripMenuItem("12 h", null, (s, e) => { TimeAxisZoom(0, 12 * 60 * 60); }),
            new ToolStripMenuItem("24 h", null, (s, e) => { TimeAxisZoom(0, 24 * 60 * 60); }) };

        foreach (ToolStripItem mi in timeAxisMenuItems)
            timeAxisMenuItem.DropDownItems.Add(mi);
        menu.Items.Add(timeAxisMenuItem);

        ToolStripMenuItem timeAxisLabelModeMenuItem = new ToolStripMenuItem("Label Mode");
        ToolStripMenuItem[] timeAxisLabelModeMenuItems =
        {
            new ToolStripMenuItem("Local Time"),
            new ToolStripMenuItem("Elapsed")
        };

        foreach (ToolStripItem mi in timeAxisLabelModeMenuItems)
            timeAxisLabelModeMenuItem.DropDownItems.Add(mi);
        timeAxisMenuItem.DropDownItems.Add(new ToolStripSeparator());
        timeAxisMenuItem.DropDownItems.Add(timeAxisLabelModeMenuItem);

        _timeAxisLabelMode = new UserRadioGroup("plotTimeAxisLabelMode", TimeAxisLabelModeLocalTime, timeAxisLabelModeMenuItems, _settings);
        _timeAxisLabelMode.Changed += (sender, e) =>
        {
            ApplyTimeAxisLabelMode();
            InvalidatePlotCosmetic();
        };

        _timeAxisEnableZoom = new UserOption("timeAxisEnableZoom", true, timeAxisMenuItems[0], _settings);
        _timeAxisEnableZoom.Changed += (sender, e) =>
        {
            _timeAxis.IsZoomEnabled = _timeAxisEnableZoom.Value;
        };

        ToolStripMenuItem yAxesMenuItem = new ToolStripMenuItem("Value Axes");
        ToolStripMenuItem[] yAxesMenuItems =
        { new ToolStripMenuItem("Enable Zoom"),
            new ToolStripMenuItem("Autoscale All", null, (s, e) => { AutoscaleAllYAxes(); }) };

        foreach (ToolStripItem mi in yAxesMenuItems)
            yAxesMenuItem.DropDownItems.Add(mi);
        menu.Items.Add(yAxesMenuItem);

        _yAxesEnableZoom = new UserOption("yAxesEnableZoom", true, yAxesMenuItems[0], _settings);
        _yAxesEnableZoom.Changed += (sender, e) =>
        {
            foreach (LinearAxis axis in _axes.Values)
                axis.IsZoomEnabled = _yAxesEnableZoom.Value;
        };

        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem resetGraphViewMenuItem = new ToolStripMenuItem("Reset Graph View");
        resetGraphViewMenuItem.Click += (sender, e) => ResetGraphView?.Invoke();
        menu.Items.Add(resetGraphViewMenuItem);

        _menu = menu;
        return menu;
    }

    private void CreateOptionsButton()
    {
        // Graph-local entry point to the same option set as the plot right-click menu.
        // Overlay the button so the plot keeps its footprint; use a fixed glyph size
        // so nothing clips in narrow panel placements.
        int size = (int)Math.Round(24 * _dpiYScale);
        int margin = (int)Math.Round(6 * _dpiXScale);

        _optionsButton = new Button
        {
            Text = "⚙",
            AccessibleName = "Graph options",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(size, size),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            UseVisualStyleBackColor = false,
            TabStop = true
        };
        _optionsButton.FlatAppearance.BorderSize = 1;
        _optionsButton.Location = new Point(ClientSize.Width - size - margin, margin);
        _optionsButton.Click += (sender, e) =>
        {
            // A preceding right-drag sets the suppression flag that the shared menu's Opening
            // handler honors; a deliberate button click must not be eaten by it.
            _cancelContextMenu = false;
            _menu?.Show(_optionsButton, new Point(0, _optionsButton.Height));
        };
        _toolTip.SetToolTip(_optionsButton, "Graph options");

        Controls.Add(_optionsButton);
        _optionsButton.BringToFront();
    }

    private PlotModel CreatePlotModel()
    {
        _timeAxis.Position = AxisPosition.Bottom;
        _timeAxis.MajorGridlineStyle = LineStyle.Solid;
        _timeAxis.MajorGridlineThickness = 1;
        _timeAxis.MajorGridlineColor = OxyColor.FromRgb(192, 192, 192);
        _timeAxis.MinorGridlineStyle = LineStyle.Solid;
        _timeAxis.MinorGridlineThickness = 1;
        _timeAxis.MinorGridlineColor = OxyColor.FromRgb(232, 232, 232);
        _timeAxis.StartPosition = 0;
        _timeAxis.EndPosition = 1;
        _timeAxis.MinimumPadding = 0;
        _timeAxis.MaximumPadding = 0;
        _timeAxis.MinimumRange = 1;
        _timeAxis.StringFormat = "h:mm";
        _timeAxis.TrackerValue = x =>
        {
            if (_timeAxisLabelMode?.Value == TimeAxisLabelModeElapsed)
                return TimeSpan.FromSeconds(Math.Max(0, _lastNowX - x));

            try
            {
                return _timeOrigin.AddSeconds(x).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                return TimeSpan.FromSeconds(x);
            }
        };

        // The persisted window is stored in age semantics (seconds before "now"), exactly as the
        // previous age-valued axis persisted it, so existing configs keep their meaning.
        _windowAgeMin = _settings.GetValue("plotPanel.MinTimeSpan", 0.0f);
        _windowAgeMax = _settings.GetValue("plotPanel.MaxTimeSpan", 10.0f * 60);
        if (!(_windowAgeMax > _windowAgeMin) || _windowAgeMin < 0)
        {
            _windowAgeMin = 0;
            _windowAgeMax = 10.0 * 60;
        }

        UpdateTimeAxisWindow(0);

#pragma warning disable CS0618 //obsolete warning

        _timeAxis.AxisChanged += (sender, args) =>
        {
            // Track user pan/zoom as an age window so the per-tick update keeps the same
            // distance from "now". Programmatic updates set _updatingTimeAxis to avoid feedback.
            if (_updatingTimeAxis)
                return;

            double minimum = _timeAxis.ActualMinimum;
            double maximum = _timeAxis.ActualMaximum;
            if (double.IsNaN(minimum) || double.IsNaN(maximum))
                return;

            _windowAgeMin = Math.Max(0, _lastNowX - maximum);
            _windowAgeMax = Math.Max(_windowAgeMin + 1e-3, _lastNowX - minimum);
        };

#pragma warning restore CS0618 //obsolete warning

        var units = new Dictionary<SensorType, string>
        {
            { SensorType.Voltage, "V" },
            { SensorType.Current, "A" },
            { SensorType.Clock, "MHz" },
            { SensorType.Temperature, "°C" },
            { SensorType.TemperatureRate, "°C/s" },
            { SensorType.Load, "%" },
            { SensorType.Fan, "RPM" },
            { SensorType.Flow, "L/h" },
            { SensorType.Control, "%" },
            { SensorType.Level, "%" },
            { SensorType.Factor, "1" },
            { SensorType.Power, "W" },
            { SensorType.Data, "GB" },
            { SensorType.Frequency, "Hz" },
            { SensorType.Energy, "mWh" },
            { SensorType.Noise, "dBA" },
            { SensorType.Conductivity, "µS/cm" },
            { SensorType.Humidity, "%" }
        };

        foreach (SensorType type in Enum.GetValues(typeof(SensorType)))
        {
            string typeName = type.ToString();
            var axis = new LinearAxis
            {
                Position = AxisPosition.Left,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineThickness = 1,
                MajorGridlineColor = _timeAxis.MajorGridlineColor,
                MinorGridlineStyle = LineStyle.Solid,
                MinorGridlineThickness = 1,
                MinorGridlineColor = _timeAxis.MinorGridlineColor,
                AxislineStyle = LineStyle.Solid,
                Title = typeName,
                Key = typeName,
            };

            var annotation = new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal,
                ClipByXAxis = false,
                ClipByYAxis = false,
                LineStyle = LineStyle.Solid,
                Color = Theme.Current.PlotBorderColor.ToOxyColor(),
                YAxisKey = typeName,
                StrokeThickness = 2,
            };

#pragma warning disable CS0618 //obsolete warning

            axis.AxisChanged += (sender, args) => annotation.Y = axis.ActualMinimum;
            axis.TransformChanged += (sender, args) => annotation.Y = axis.ActualMinimum;

#pragma warning restore CS0618 //obsolete warning

            axis.Zoom(_settings.GetValue("plotPanel.Min" + axis.Key, float.NaN), _settings.GetValue("plotPanel.Max" + axis.Key, float.NaN));

            if (units.ContainsKey(type))
                axis.Unit = units[type];

            _axes.Add(type, axis);
            _annotations.Add(type, annotation);
        }

        var model = new ScaledPlotModel(_dpiXScale, _dpiYScale);
        model.Axes.Add(_timeAxis);
        foreach (LinearAxis axis in _axes.Values)
            model.Axes.Add(axis);
        model.IsLegendVisible = false;

        return model;
    }

    private void ApplyTimeAxisLabelMode()
    {
        if (_timeAxisLabelMode?.Value == TimeAxisLabelModeElapsed)
        {
            _timeAxis.LabelFormatter = FormatElapsedTimeAxisLabel;
            return;
        }

        _timeAxis.LabelFormatter = FormatLocalTimeAxisLabel;
    }

    private string FormatElapsedTimeAxisLabel(double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            return string.Empty;

        // Elapsed mode keeps the historical display semantics: the label is the sample's age
        // relative to "now" (h:mm), even though the axis value is now seconds since session start.
        TimeSpan age = TimeSpan.FromSeconds(Math.Max(0, _lastNowX - elapsedSeconds));
        return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}", (int)age.TotalHours, age.Minutes);
    }

    private string FormatLocalTimeAxisLabel(double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            return string.Empty;

        DateTime labelTime;
        try
        {
            labelTime = _timeOrigin.AddSeconds(elapsedSeconds).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }

        double range = GetVisibleTimeAxisRange();

        // Use second resolution when gridlines are spaced under a minute apart; otherwise adjacent
        // labels repeat the same HH:mm (the Fine grid default targets ~20 divisions, which is
        // sub-minute at common zooms). ActualMajorStep is the rendered tick spacing in seconds.
        double majorStep = _timeAxis.ActualMajorStep;
        bool subMinuteSteps = !double.IsNaN(majorStep) && majorStep > 0 && majorStep < 60;

        if (range <= 2 * 60 || subMinuteSteps)
            return labelTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        if (VisibleTimeAxisCrossesLocalDate())
            return labelTime.ToString("M/d HH:mm", CultureInfo.CurrentCulture);

        return labelTime.ToString("HH:mm", CultureInfo.CurrentCulture);
    }

    private double GetVisibleTimeAxisRange()
    {
        double minimum = !double.IsNaN(_timeAxis.ActualMinimum) ? _timeAxis.ActualMinimum : _timeAxis.Minimum;
        double maximum = !double.IsNaN(_timeAxis.ActualMaximum) ? _timeAxis.ActualMaximum : _timeAxis.Maximum;
        double range = Math.Abs(maximum - minimum);
        return double.IsNaN(range) || double.IsInfinity(range) ? 0 : range;
    }

    private bool VisibleTimeAxisCrossesLocalDate()
    {
        double minimum = !double.IsNaN(_timeAxis.ActualMinimum) ? _timeAxis.ActualMinimum : _timeAxis.Minimum;
        double maximum = !double.IsNaN(_timeAxis.ActualMaximum) ? _timeAxis.ActualMaximum : _timeAxis.Maximum;
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || double.IsInfinity(minimum) || double.IsInfinity(maximum))
            return false;

        try
        {
            DateTime first = _timeOrigin.AddSeconds(minimum).ToLocalTime();
            DateTime second = _timeOrigin.AddSeconds(maximum).ToLocalTime();
            return first.Date != second.Date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private void SetDpi()
    {
        // https://msdn.microsoft.com/en-us/library/windows/desktop/dn469266(v=vs.85).aspx
        const int defaultDpi = 96;
        Graphics g = CreateGraphics();

        try
        {
            _dpiX = g.DpiX;
            _dpiY = g.DpiY;
        }
        finally
        {
            g.Dispose();
        }

        if (_dpiX > 0)
            _dpiXScale = _dpiX / defaultDpi;
        if (_dpiY > 0)
            _dpiYScale = _dpiY / defaultDpi;
    }

    public void SetSensors(List<ISensor> sensors, IDictionary<ISensor, Color> colors, double strokeThickness)
    {
        _model.Series.Clear();
        var types = new HashSet<SensorType>();

        _historyStore.RetainSensors(sensors);

        foreach (ISensor sensor in sensors)
        {
            PlotPanelSeriesState state = _historyStore.GetOrCreateState(sensor);

            var series = new LineSeries
            {
                // A List<DataPoint> ItemsSource takes OxyPlot's zero-copy fast path; the list is
                // owned by PlotPanelSeriesState and refreshed in SyncSeriesPoints only when the sensor's
                // history snapshot actually changed.
                ItemsSource = state.Points,
                Decimator = DecimateIfDense,
                Color = colors[sensor].ToOxyColor(),
                StrokeThickness = strokeThickness,
                YAxisKey = _axes[sensor.SensorType].Key,
                Title = sensor.Hardware.Name + " " + sensor.Name
            };

            _model.Series.Add(series);

            types.Add(sensor.SensorType);
        }

        foreach (KeyValuePair<SensorType, LinearAxis> pair in _axes.Reverse())
        {
            LinearAxis axis = pair.Value;
            SensorType type = pair.Key;
            axis.IsAxisVisible = types.Contains(type);
        }

        UpdateAxesPosition();
        InvalidatePlot();
    }

    private void SyncSeriesPoints()
    {
        bool temperatureUnitChanged = _unitManager.TemperatureUnit != _lastTemperatureUnit;
        if (temperatureUnitChanged)
            _lastTemperatureUnit = _unitManager.TemperatureUnit;

        _historyStore.Synchronize(_unitManager.TemperatureUnit, temperatureUnitChanged);
    }

    private void DecimateIfDense(List<ScreenPoint> input, List<ScreenPoint> output)
    {
        // Decimator.Decimate snaps every vertex to the integer pixel grid, so only engage it when
        // the rendered segment is dense enough that sub-pixel placement is invisible anyway
        // (spec threshold: max(2 * plot pixel width, 2000) points).
        int width = _plot?.Width ?? 0;
        if (input.Count > Math.Max(2 * width, 2000))
            Decimator.Decimate(input, output);
        else
            output.AddRange(input);
    }

    public void UpdateStrokeThickness(double strokeThickness)
    {
        foreach (LineSeries series in _model.Series)
        {
            series.StrokeThickness = strokeThickness;
        }
        InvalidatePlotCosmetic();
    }

    private void ApplyGridDensity()
    {
        int density = _gridDensity?.Value ?? DefaultGridDensity;
        int textScalePercent = _axisTextScalePercent;

        ApplyAxisGrid(_timeAxis, density, true, textScalePercent);

        bool showValueGrid = _stackedAxes?.Value == true;
        foreach (LinearAxis axis in _axes.Values)
            ApplyAxisGrid(axis, density, showValueGrid, textScalePercent);
    }

    private static void ApplyAxisGrid(Axis axis, int density, bool enabled, int textScalePercent)
    {
        // ApplyGridDensity runs on every plot refresh. Assign only when a value actually
        // changes: re-assigning identical MajorStep/MinorStep each frame forces OxyPlot to
        // re-tick and makes the gridlines "pop" during live updates.
        if (axis.MajorGridlineThickness != 1)
            axis.MajorGridlineThickness = 1;

        double minorThickness = density == 3 ? 0.5 : 1;
        if (axis.MinorGridlineThickness != minorThickness)
            axis.MinorGridlineThickness = minorThickness;

        if (!enabled || density == 0)
        {
            SetGridlineStyle(axis, LineStyle.None, LineStyle.None);
            ResetAxisSteps(axis);
            return;
        }

        SetGridlineStyle(axis, LineStyle.Solid, density >= 2 ? LineStyle.Solid : LineStyle.None);

        if (density == 3)
            ApplyFineAxisSteps(axis, textScalePercent);
        else
            ResetAxisSteps(axis);
    }

    private static void SetGridlineStyle(Axis axis, LineStyle major, LineStyle minor)
    {
        if (axis.MajorGridlineStyle != major)
            axis.MajorGridlineStyle = major;
        if (axis.MinorGridlineStyle != minor)
            axis.MinorGridlineStyle = minor;
    }

    private static void ResetAxisSteps(Axis axis)
    {
        if (!double.IsNaN(axis.MajorStep))
            axis.MajorStep = double.NaN;
        if (!double.IsNaN(axis.MinorStep))
            axis.MinorStep = double.NaN;
    }

    private static void ApplyFineAxisSteps(Axis axis, int textScalePercent)
    {
        double minimum = !double.IsNaN(axis.ActualMinimum) ? axis.ActualMinimum : axis.Minimum;
        double maximum = !double.IsNaN(axis.ActualMaximum) ? axis.ActualMaximum : axis.Maximum;
        double range = Math.Abs(maximum - minimum);
        if (double.IsNaN(range) || double.IsInfinity(range) || range <= 0)
            return;

        // Fewer divisions (larger step) as text scale grows past 100%, so bigger tick labels
        // don't crowd; at/below 100% this is unchanged (divisions == FineGridMajorDivisions).
        double divisions = FineGridMajorDivisions * 100.0 / Math.Max(100, textScalePercent);
        double majorStep = GetNiceAxisStep(range, divisions);
        if (double.IsNaN(majorStep) || majorStep <= 0)
            return;

        // The nice-step is piecewise-constant across a band of ranges, so within a band these
        // assignments are no-ops and the grid only changes at genuine band crossings.
        if (axis.MajorStep != majorStep)
            axis.MajorStep = majorStep;

        double minorStep = majorStep / 4;
        if (axis.MinorStep != minorStep)
            axis.MinorStep = minorStep;
    }

    private static readonly double[] NiceFactors = { 1, 2, 2.5, 5, 10 };

    private static double GetNiceAxisStep(double range, double targetDivisions)
    {
        if (double.IsNaN(range) || double.IsInfinity(range) || range <= 0 || targetDivisions <= 0)
            return double.NaN;

        double rawStep = range / targetDivisions;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double bestStep = double.NaN;
        double bestScore = double.PositiveInfinity;
        const double scoreTolerance = 1e-9;

        for (int powerOffset = -1; powerOffset <= 1; powerOffset++)
        {
            double power = magnitude * Math.Pow(10, powerOffset);
            foreach (double factor in NiceFactors)
            {
                double step = factor * power;
                double divisions = range / step;
                double score = Math.Abs(divisions - targetDivisions);

                // Prefer the smaller step on a genuine near-tie. The previous condition used
                // double.Epsilon (smallest denormal) as the tolerance, so that branch was dead;
                // a real tolerance is needed for it to fire. First iteration still initialises
                // because score < (+Infinity - tolerance) is true.
                if (score < bestScore - scoreTolerance || (Math.Abs(score - bestScore) <= scoreTolerance && step < bestStep))
                {
                    bestStep = step;
                    bestScore = score;
                }
            }
        }

        return bestStep;
    }

    private void UpdateAxesPosition()
    {
        if (_stackedAxes.Value)
        {
            int count = _axes.Values.Count(axis => axis.IsAxisVisible);
            double start = 0.0;
            foreach (KeyValuePair<SensorType, LinearAxis> pair in _axes.Reverse())
            {
                LinearAxis axis = pair.Value;
                axis.StartPosition = start;
                double delta = axis.IsAxisVisible ? 1.0 / count : 0;
                start += delta;
                axis.EndPosition = start;
                axis.PositionTier = 0;
                LineAnnotation annotation = _annotations[pair.Key];
                annotation.Y = axis.ActualMinimum;
                if (!_model.Annotations.Contains(annotation)) 
                    _model.Annotations.Add(annotation);
            }
        }
        else
        {
            int tier = 0;

            foreach (KeyValuePair<SensorType, LinearAxis> pair in _axes.Reverse())
            {
                LinearAxis axis = pair.Value;

                if (axis.IsAxisVisible)
                {
                    axis.StartPosition = 0;
                    axis.EndPosition = 1;
                    axis.PositionTier = tier;
                    tier++;
                }
                else
                {
                    axis.StartPosition = 0;
                    axis.EndPosition = 0;
                    axis.PositionTier = 0;
                }
                LineAnnotation annotation = _annotations[pair.Key];
                if (_model.Annotations.Contains(annotation))
                    _model.Annotations.Remove(_annotations[pair.Key]);
            }
        }
    }

    public void InvalidatePlot()
    {
        double nowX = (DateTime.UtcNow - _timeOrigin).TotalSeconds;
        _lastNowX = nowX;

        SyncSeriesPoints();

        // One-shot: reclaim empty Y-axis bands left over from a stale persisted zoom
        // (CreatePlotModel's axis.Zoom(...) restore) once real data exists, then never again
        // this session so a later manual/menu zoom sticks.
        if (_autoFitYOnStart && !_didAutoFitYAxesOnStart && _historyStore.States.Any(state => state.Points.Count > 0))
        {
            _didAutoFitYAxesOnStart = true;
            AutoscaleAllYAxes();
        }

        UpdateTimeAxisWindow(nowX);
        ApplyGridDensity();

        if (_axes != null)
        {
            foreach (KeyValuePair<SensorType, LinearAxis> pair in _axes)
            {
                LinearAxis axis = pair.Value;
                SensorType type = pair.Key;
                if (type == SensorType.Temperature)
                    axis.Unit = _unitManager.TemperatureUnit == TemperatureUnit.Celsius ? "°C" : "°F";
                else if (type == SensorType.TemperatureRate)
                    axis.Unit = _unitManager.TemperatureUnit == TemperatureUnit.Celsius ? "°C/s" : "°F/s";

                if (!_stackedAxes.Value)
                    continue;

                var annotation = _annotations[pair.Key];
                annotation.Y = axis.ActualMaximum;
            }
        }

        _plot?.InvalidatePlot(true);
    }

    private void InvalidatePlotCosmetic()
    {
        // Re-render without re-running the series data update: none of the cosmetic call sites
        // (stroke width, grid density, label mode, axis layout, window pan) change point data.
        ApplyGridDensity();
        _plot?.InvalidatePlot(false);
    }

    private void UpdateTimeAxisWindow(double nowX)
    {
        _updatingTimeAxis = true;
        bool zoomEnabled = _timeAxis.IsZoomEnabled;
        _timeAxis.IsZoomEnabled = true;

        try
        {
            _timeAxis.AbsoluteMaximum = nowX;
            _timeAxis.AbsoluteMinimum = nowX - (24 * 60 * 60);

            // NaN ageMax means "Auto": leave the range to OxyPlot's data-driven autoscale.
            if (!double.IsNaN(_windowAgeMax))
                _timeAxis.Zoom(nowX - _windowAgeMax, nowX - _windowAgeMin);
        }
        finally
        {
            _timeAxis.IsZoomEnabled = zoomEnabled;
            _updatingTimeAxis = false;
        }
    }

    public void TimeAxisZoom(double ageMin, double ageMax)
    {
        _windowAgeMin = ageMin;
        _windowAgeMax = ageMax;

        if (double.IsNaN(ageMax))
        {
            _updatingTimeAxis = true;
            bool zoomEnabled = _timeAxis.IsZoomEnabled;
            _timeAxis.IsZoomEnabled = true;

            try
            {
                _timeAxis.Reset();
            }
            finally
            {
                _timeAxis.IsZoomEnabled = zoomEnabled;
                _updatingTimeAxis = false;
            }
        }

        UpdateTimeAxisWindow(_lastNowX);
        InvalidatePlotCosmetic();
    }

    public void AutoscaleAllYAxes()
    {
        foreach (LinearAxis axis in _axes.Values)
        {
            // Zoom() silently no-ops when IsZoomEnabled is false (persisted via Value Axes >
            // Enable Zoom, default true but can be off from a prior session). Force-enable around
            // the call and restore after, mirroring UpdateTimeAxisWindow's pattern, so this always
            // actually un-zooms the axis regardless of that setting.
            bool zoomEnabled = axis.IsZoomEnabled;
            axis.IsZoomEnabled = true;
            try
            {
                axis.Zoom(double.NaN, double.NaN);
            }
            finally
            {
                axis.IsZoomEnabled = zoomEnabled;
            }
        }

        // Refresh now instead of waiting for the next update tick, so the rescale is visible
        // immediately after the menu action.
        InvalidatePlotCosmetic();
    }

    /// <summary>
    /// Scales all axis tick-label and title fonts (plotTextScale; the hover tracker follows
    /// <see cref="SetTrackerTextScale"/> instead). DPI-independent on purpose: today's axis fonts are
    /// NOT DPI-scaled (ScaledPlotModel scales only the NaN/auto margins, a no-op), so 100% reproduces
    /// the current look at every DPI. Auto-margins absorb larger labels, so no clipping math is needed.
    /// </summary>
    public void SetAxisTextScale(int percent)
    {
        _axisTextScalePercent = UiScale.ClampPercent(percent);
        double fontSize = UiScale.PlotAxisFontSize(_model.DefaultFontSize, _axisTextScalePercent);

        foreach (Axis axis in _model.Axes)
        {
            axis.FontSize = fontSize;        // tick labels
            axis.TitleFontSize = fontSize;   // axis titles (set explicitly; don't rely on the fallback)

            // Grow tick spacing with the font so larger labels don't pack tighter than they render
            // (OxyPlot's default IntervalLength is 60; at percent=100 this is exactly 60.0, so the
            // 100% case is byte-identical to today's tick density).
            axis.IntervalLength = 60.0 * (_axisTextScalePercent / 100.0);
        }

        InvalidatePlotCosmetic();
    }

    /// <summary>Scales the hover tracker/tooltip font (follows the UI text scale, not plotTextScale).</summary>
    public void SetTrackerTextScale(int percent)
    {
        int clamped = UiScale.ClampPercent(percent);

        // Tracker/tooltip is a WinForms Label that inherits PlotView.Font ambiently.
        _trackerBaseFont ??= (Font)_plot.Font.Clone();
        Font old = _scaledTrackerFont;
        _scaledTrackerFont = new Font(
            _trackerBaseFont.FontFamily,
            UiScale.ScaledFontSize(_trackerBaseFont.Size, clamped),
            _trackerBaseFont.Style);
        _plot.Font = _scaledTrackerFont;
        old?.Dispose();

        InvalidatePlotCosmetic();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _historyStore.RetainSensors(Array.Empty<ISensor>());
            _toolTip.Dispose();

            if (_plot != null)
                _plot.ContextMenuStrip = null;

            _menu?.Dispose();
            _menu = null;

            _scaledTrackerFont?.Dispose();
            _scaledTrackerFont = null;
            _trackerBaseFont?.Dispose();
            _trackerBaseFont = null;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Owns the materialized plot histories separately from the WinForms control so history updates
/// can be verified without constructing a window. A state exists only while its sensor is plotted.
/// </summary>
internal sealed class PlotPanelHistoryStore
{
    // Sensor currently caps its retained history at this value. Keeping the consumer request
    // explicit also bounds reset, decimation, expiry, and temperature-unit rebuilds for other
    // implementations of the optional history-reader contract.
    internal const int MaxPlotHistoryValues = 10_000;

    private readonly Dictionary<ISensor, PlotPanelSeriesState> _states = new();
    private readonly DateTime _timeOrigin;

    internal PlotPanelHistoryStore(DateTime timeOrigin)
    {
        _timeOrigin = timeOrigin;
    }

    internal IEnumerable<PlotPanelSeriesState> States => _states.Values;

    internal PlotPanelSeriesState GetOrCreateState(ISensor sensor)
    {
        if (!_states.TryGetValue(sensor, out PlotPanelSeriesState state))
        {
            state = new PlotPanelSeriesState();
            _states.Add(sensor, state);
        }

        return state;
    }

    internal bool TryGetState(ISensor sensor, out PlotPanelSeriesState state)
    {
        return _states.TryGetValue(sensor, out state);
    }

    internal void RetainSensors(IEnumerable<ISensor> sensors)
    {
        if (sensors == null)
            throw new ArgumentNullException(nameof(sensors));

        HashSet<ISensor> retained = sensors as HashSet<ISensor> ?? new HashSet<ISensor>(sensors);
        List<ISensor> stale = _states.Keys.Where(sensor => !retained.Contains(sensor)).ToList();
        foreach (ISensor sensor in stale)
            _states.Remove(sensor);
    }

    internal void Synchronize(TemperatureUnit temperatureUnit, bool temperatureUnitChanged)
    {
        foreach (KeyValuePair<ISensor, PlotPanelSeriesState> pair in _states)
        {
            bool rebuildForUnit = temperatureUnitChanged &&
                                  (pair.Key.SensorType == SensorType.Temperature ||
                                   pair.Key.SensorType == SensorType.TemperatureRate);
            pair.Value.Synchronize(pair.Key, _timeOrigin, temperatureUnit, rebuildForUnit);
        }
    }
}

internal sealed class PlotPanelSeriesState
{
    private IEnumerable<SensorValue> _lastValues;

    internal List<DataPoint> Points { get; } = new();

    internal long LastHistoryVersion { get; private set; }

    internal void Synchronize
    (
        ISensor sensor,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit,
        bool rebuildForTemperatureUnit)
    {
        if (sensor is ISensorHistoryReader reader)
        {
            SynchronizeReader(sensor.SensorType, reader, timeOrigin, temperatureUnit, rebuildForTemperatureUnit);
            return;
        }

        SynchronizeFallback(sensor, timeOrigin, temperatureUnit, rebuildForTemperatureUnit);
    }

    private void SynchronizeReader
    (
        SensorType sensorType,
        ISensorHistoryReader reader,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit,
        bool rebuildForTemperatureUnit)
    {
        // HistoryVersion is an allocation-free dirty check. Expiry can advance it without a new
        // sample; ReadHistory then returns ResetRequired and replaces the plotted tail once.
        long currentVersion = reader.HistoryVersion;
        if (!rebuildForTemperatureUnit && currentVersion == LastHistoryVersion)
            return;

        // Version zero is the documented initial-read request. It also gives a temperature-unit
        // change one bounded current tail even when the reader reports ResetRequired == false.
        long sinceVersion = rebuildForTemperatureUnit ? 0 : LastHistoryVersion;
        SensorHistorySlice history = reader.ReadHistory(sinceVersion, PlotPanelHistoryStore.MaxPlotHistoryValues);
        bool rebuild = rebuildForTemperatureUnit || history.ResetRequired;

        if (rebuild)
            Points.Clear();

        AppendBoundedPoints(history.Values, sensorType, timeOrigin, temperatureUnit);
        LastHistoryVersion = history.Version;
        _lastValues = null;
    }

    private void SynchronizeFallback
    (
        ISensor sensor,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit,
        bool rebuildForTemperatureUnit)
    {
        IEnumerable<SensorValue> values = sensor.Values;
        if (!rebuildForTemperatureUnit && ReferenceEquals(values, _lastValues))
            return;

        _lastValues = values;
        Points.Clear();
        AppendPoints(values, sensor.SensorType, timeOrigin, temperatureUnit);
    }

    private void AppendBoundedPoints
    (
        IReadOnlyList<SensorValue> values,
        SensorType sensorType,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit)
    {
        int excess = Math.Max(0, Points.Count + values.Count - PlotPanelHistoryStore.MaxPlotHistoryValues);
        int removeExisting = Math.Min(Points.Count, excess);
        if (removeExisting > 0)
            Points.RemoveRange(0, removeExisting);

        int skipIncoming = excess - removeExisting;
        int appendCount = values.Count - skipIncoming;
        int requiredCapacity = Points.Count + appendCount;
        if (Points.Capacity < requiredCapacity)
            Points.Capacity = requiredCapacity;

        for (int i = skipIncoming; i < values.Count; i++)
            Points.Add(CreatePoint(values[i], sensorType, timeOrigin, temperatureUnit));
    }

    private void AppendPoints
    (
        IEnumerable<SensorValue> values,
        SensorType sensorType,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit)
    {
        if (values is IReadOnlyList<SensorValue> list)
        {
            int requiredCapacity = Points.Count + list.Count;
            if (Points.Capacity < requiredCapacity)
                Points.Capacity = requiredCapacity;

            for (int i = 0; i < list.Count; i++)
                Points.Add(CreatePoint(list[i], sensorType, timeOrigin, temperatureUnit));

            return;
        }

        foreach (SensorValue value in values)
            Points.Add(CreatePoint(value, sensorType, timeOrigin, temperatureUnit));
    }

    private static DataPoint CreatePoint
    (
        SensorValue value,
        SensorType sensorType,
        DateTime timeOrigin,
        TemperatureUnit temperatureUnit)
    {
        float displayedValue = value.Value;
        if (temperatureUnit == TemperatureUnit.Fahrenheit)
        {
            if (sensorType == SensorType.Temperature)
                displayedValue = UnitManager.CelsiusToFahrenheit(value.Value).Value;
            else if (sensorType == SensorType.TemperatureRate)
                displayedValue = UnitManager.CelsiusRateToFahrenheit(value.Value).Value;
        }

        return new DataPoint((value.Time - timeOrigin).TotalSeconds, displayedValue);
    }
}
