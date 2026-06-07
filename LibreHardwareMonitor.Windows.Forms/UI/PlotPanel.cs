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
    private readonly TimeSpanAxis _timeAxis = new TimeSpanAxis();
    private readonly SortedDictionary<SensorType, LinearAxis> _axes = new SortedDictionary<SensorType, LinearAxis>();
    private readonly Dictionary<SensorType, LineAnnotation> _annotations = new Dictionary<SensorType, LineAnnotation>();
    private UserOption _stackedAxes;
    private UserOption _showAxesLabels;
    private UserOption _timeAxisEnableZoom;
    private UserOption _yAxesEnableZoom;
    private UserRadioGroup _gridDensity;
    private UserRadioGroup _timeAxisLabelMode;
    private DateTime _now;
    private float _dpiX;
    private float _dpiY;
    private double _dpiXScale = 1;
    private double _dpiYScale = 1;
    private Point _rightClickEnter;
    private bool _cancelContextMenu = false;

    public PlotPanel(PersistentSettings settings, UnitManager unitManager)
    {
        _settings = settings;
        _unitManager = unitManager;
        _now = DateTime.UtcNow;

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
        _plot.ShowTracker(new TrackerHitResult());
        _plot.HideTracker();
        foreach (Control plotControl in _plot.Controls)
        {
            plotControl.BackColor = Theme.Current.PlotBackgroundColor;
            plotControl.ForeColor = Theme.Current.PlotTextColor;
        }
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        _model.Background = Theme.Current.PlotBackgroundColor.ToOxyColor();
        _model.PlotAreaBorderColor = Theme.Current.PlotBorderColor.ToOxyColor();
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
        _settings.SetValue("plotPanel.MinTimeSpan", (float)_timeAxis.ActualMinimum);
        _settings.SetValue("plotPanel.MaxTimeSpan", (float)_timeAxis.ActualMaximum);
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
            InvalidatePlot();
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
        _gridDensity.Changed += (sender, e) =>
        {
            ApplyGridDensity();
            InvalidatePlot();
        };

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
            InvalidatePlot();
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

        return menu;
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
        _timeAxis.StartPosition = 1;
        _timeAxis.EndPosition = 0;
        _timeAxis.MinimumPadding = 0;
        _timeAxis.MaximumPadding = 0;
        _timeAxis.AbsoluteMinimum = 0;
        _timeAxis.Minimum = 0;
        _timeAxis.AbsoluteMaximum = 24 * 60 * 60;
        _timeAxis.Zoom(
                       _settings.GetValue("plotPanel.MinTimeSpan", 0.0f),
                       _settings.GetValue("plotPanel.MaxTimeSpan", 10.0f * 60));
        _timeAxis.StringFormat = "h:mm";

        var units = new Dictionary<SensorType, string>
        {
            { SensorType.Voltage, "V" },
            { SensorType.Current, "A" },
            { SensorType.Clock, "MHz" },
            { SensorType.Temperature, "°C" },
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
            _timeAxis.LabelFormatter = null;
            _timeAxis.StringFormat = "h:mm";
            return;
        }

        _timeAxis.LabelFormatter = FormatLocalTimeAxisLabel;
    }

    private string FormatLocalTimeAxisLabel(double secondsFromNow)
    {
        if (double.IsNaN(secondsFromNow) || double.IsInfinity(secondsFromNow))
            return string.Empty;

        DateTime anchor = _now == default ? DateTime.UtcNow : _now;
        DateTime labelTime;
        try
        {
            labelTime = anchor.AddSeconds(-secondsFromNow).ToLocalTime();
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

        DateTime anchor = _now == default ? DateTime.UtcNow : _now;
        try
        {
            DateTime first = anchor.AddSeconds(-minimum).ToLocalTime();
            DateTime second = anchor.AddSeconds(-maximum).ToLocalTime();
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


        DataPoint CreateDataPoint(SensorType type, SensorValue value)
        {
            float displayedValue;

            if (type == SensorType.Temperature && _unitManager.TemperatureUnit == TemperatureUnit.Fahrenheit)
            {
                displayedValue = UnitManager.CelsiusToFahrenheit(value.Value).Value;
            }
            else
            {
                displayedValue = value.Value;
            }

            return new DataPoint((_now - value.Time).TotalSeconds, displayedValue);
        }


        foreach (ISensor sensor in sensors)
        {
            var series = new LineSeries
            {
                ItemsSource = sensor.Values.Select(value => CreateDataPoint(sensor.SensorType, value)),
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

    public void UpdateStrokeThickness(double strokeThickness)
    {
        foreach (LineSeries series in _model.Series)
        {
            series.StrokeThickness = strokeThickness;
        }
        InvalidatePlot();
    }

    private void ApplyGridDensity()
    {
        int density = _gridDensity?.Value ?? DefaultGridDensity;

        ApplyAxisGrid(_timeAxis, density, true);

        bool showValueGrid = _stackedAxes?.Value == true;
        foreach (LinearAxis axis in _axes.Values)
            ApplyAxisGrid(axis, density, showValueGrid);
    }

    private static void ApplyAxisGrid(Axis axis, int density, bool enabled)
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
            ApplyFineAxisSteps(axis);
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

    private static void ApplyFineAxisSteps(Axis axis)
    {
        double minimum = !double.IsNaN(axis.ActualMinimum) ? axis.ActualMinimum : axis.Minimum;
        double maximum = !double.IsNaN(axis.ActualMaximum) ? axis.ActualMaximum : axis.Maximum;
        double range = Math.Abs(maximum - minimum);
        if (double.IsNaN(range) || double.IsInfinity(range) || range <= 0)
            return;

        double majorStep = GetNiceAxisStep(range, FineGridMajorDivisions);
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

    private static double GetNiceAxisStep(double range, double targetDivisions)
    {
        if (double.IsNaN(range) || double.IsInfinity(range) || range <= 0 || targetDivisions <= 0)
            return double.NaN;

        double rawStep = range / targetDivisions;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double bestStep = double.NaN;
        double bestScore = double.PositiveInfinity;
        double[] niceFactors = { 1, 2, 2.5, 5, 10 };
        const double scoreTolerance = 1e-9;

        for (int powerOffset = -1; powerOffset <= 1; powerOffset++)
        {
            double power = magnitude * Math.Pow(10, powerOffset);
            foreach (double factor in niceFactors)
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

        ApplyGridDensity();
    }

    public void InvalidatePlot()
    {
        _now = DateTime.UtcNow;
        ApplyGridDensity();

        if (_axes != null)
        {
            foreach (KeyValuePair<SensorType, LinearAxis> pair in _axes)
            {
                LinearAxis axis = pair.Value;
                SensorType type = pair.Key;
                if (type == SensorType.Temperature)
                    axis.Unit = _unitManager.TemperatureUnit == TemperatureUnit.Celsius ? "°C" : "°F";
                    
                if (!_stackedAxes.Value) 
                    continue;

                var annotation = _annotations[pair.Key];
                annotation.Y = axis.ActualMaximum;
            }
        }

        _plot?.InvalidatePlot(true);
    }

    public void TimeAxisZoom(double min, double max)
    {
        bool timeAxisIsZoomEnabled = _timeAxis.IsZoomEnabled;

        _timeAxis.IsZoomEnabled = true;
        _timeAxis.Zoom(min, max);
        InvalidatePlot();
        _timeAxis.IsZoomEnabled = timeAxisIsZoomEnabled;
    }

    public void AutoscaleAllYAxes()
    {
        foreach (LinearAxis axis in _axes.Values)
            axis.Zoom(double.NaN, double.NaN);
    }
}
