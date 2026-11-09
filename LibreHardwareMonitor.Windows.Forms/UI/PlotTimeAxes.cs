// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Graph time axis. Series X values are seconds since the panel's session origin, so the default
/// tracker value (TimeSpan.FromSeconds) would read as time since launch; <see cref="TrackerValue" />
/// substitutes the active label mode's value. When stacked bands own a <see cref="PlotPaneTimeAxis" />,
/// this axis leaves those bands unpainted and still draws each tick label once below the plot.
/// </summary>
internal class PlotTimeAxis : TimeSpanAxis
{
    public Func<double, object> TrackerValue { get; set; }

    /// <summary>Vertical axes whose screen bands this axis must not paint; null or empty keeps the stock rendering.</summary>
    public Func<IEnumerable<Axis>> ExcludedBands { get; set; }

    public override object GetValue(double x)
    {
        return TrackerValue != null ? TrackerValue(x) : base.GetValue(x);
    }

    public override void Render(IRenderContext rc, int pass)
    {
        List<OxyRect> strips = GetPaintedStrips();
        if (strips == null)
        {
            base.Render(rc, pass);
            return;
        }

        double plotBottom = PlotModel.PlotArea.Bottom;
        foreach (OxyRect strip in strips)
        {
            // Only the strip reaching below the plot area carries the tick labels; the others repeat
            // the gridlines with text suppressed so every label is issued exactly once.
            rc.PushClip(strip);
            try
            {
                base.Render(strip.Bottom > plotBottom ? rc : new TextSuppressingRenderContext(rc), pass);
            }
            finally
            {
                rc.PopClip();
            }
        }
    }

    /// <summary>
    /// The full-width complement of the excluded bands from the top of the plot area, with the last
    /// strip extended through the label area below it; adjacent bands merge into one gap.
    /// </summary>
    private List<OxyRect> GetPaintedStrips()
    {
        IEnumerable<Axis> excluded = ExcludedBands?.Invoke();
        if (excluded == null || PlotModel == null)
            return null;

        List<(double Top, double Bottom)> bands = excluded
            .Where(axis => axis != null && axis.IsAxisVisible && axis.IsVertical())
            .Select(axis => (Top: Math.Min(axis.ScreenMin.Y, axis.ScreenMax.Y), Bottom: Math.Max(axis.ScreenMin.Y, axis.ScreenMax.Y)))
            .Where(band => band.Bottom > band.Top)
            .OrderBy(band => band.Top)
            .ToList();
        if (bands.Count == 0)
            return null;

        OxyRect bounds = PlotModel.PlotBounds;
        var strips = new List<OxyRect>();
        double y = PlotModel.PlotArea.Top;
        foreach ((double top, double bottom) in bands)
        {
            if (top > y)
                strips.Add(new OxyRect(bounds.Left, y, bounds.Width, top - y));

            y = Math.Max(y, bottom);
        }

        if (bounds.Bottom > y)
            strips.Add(new OxyRect(bounds.Left, y, bounds.Width, bounds.Bottom - y));

        return strips;
    }
}

/// <summary>
/// The independent time window of one stacked pane. It shares the bottom position and tier of the
/// shared axis but measures no margin, so the plot area is unchanged; its gridlines are clipped to the
/// pane's band and its tick labels are drawn inside the band by <see cref="Labels" />.
/// </summary>
internal sealed class PlotPaneTimeAxis : PlotTimeAxis
{
    private const double LabelPadding = 2;
    private const byte LabelBackgroundAlpha = 192;

    internal PlotPaneTimeAxis(Axis bandAxis)
    {
        BandAxis = bandAxis ?? throw new ArgumentNullException(nameof(bandAxis));
        Key = KeyFor(bandAxis.Key);
        Position = AxisPosition.Bottom;
        // A separate tier would reserve AxisTierDistance below the plot even at zero size.
        PositionTier = 0;
        TickStyle = TickStyle.None;
        AxislineStyle = LineStyle.None;
        MinimumPadding = 0;
        MaximumPadding = 0;
        Labels = new PlotPaneTimeLabels(this);
    }

    internal Axis BandAxis { get; }

    /// <summary>Mirrors Show Axes Labels; when false neither the labels nor their strip are drawn.</summary>
    internal bool ShowLabels { get; set; } = true;

    /// <summary>Draws this axis's tick labels above the series, inside the band.</summary>
    internal Annotation Labels { get; }

    internal OxyRect BandRect
    {
        get
        {
            if (PlotModel == null || !BandAxis.IsAxisVisible)
                return new OxyRect();

            OxyRect plotArea = PlotModel.PlotArea;
            double top = Math.Min(BandAxis.ScreenMin.Y, BandAxis.ScreenMax.Y);
            double bottom = Math.Max(BandAxis.ScreenMin.Y, BandAxis.ScreenMax.Y);
            return new OxyRect(plotArea.Left, top, plotArea.Width, Math.Max(0, bottom - top));
        }
    }

    internal static string KeyFor(string laneKey) => "time:" + laneKey;

    public override void Measure(IRenderContext rc)
    {
        DesiredMargin = new OxyThickness(0);
    }

    /// <summary>
    /// Lays the axis out after every change of its window. OxyPlot updates a transform only when it
    /// renders, so input that reaches the pane before the next paint (a fast wheel spin, a touchpad
    /// pinch, queued input) would otherwise map through a new axis's zero scale or the stale offset a
    /// wheel zoom leaves.
    /// </summary>
    protected override void OnAxisChanged(AxisChangedEventArgs args)
    {
        base.OnAxisChanged(args);
        LayOutOverPlotArea();
    }

    /// <summary>
    /// The horizontal branch of OxyPlot's <c>UpdateTransform</c> over the plot area, which is what the
    /// next render computes for this bottom axis with no margins. Does nothing before the first render
    /// (no plot area yet) or for an empty window.
    /// </summary>
    private void LayOutOverPlotArea()
    {
        OxyRect plotArea = PlotModel?.PlotArea ?? new OxyRect();
        double min = ActualMinimum;
        double max = ActualMaximum;
        if (!(plotArea.Width > 0) || !(max - min > 0))
            return;

        double a0 = plotArea.Left;
        double a1 = plotArea.Right;
        double da = a0 - a1;
        ScreenMin = new ScreenPoint(a0, plotArea.Top);
        ScreenMax = new ScreenPoint(a1, plotArea.Bottom);
        SetTransform((a1 - a0) / (max - min), (a0 / da * max) - (a1 / da * min));
        ClipMinimum = min;
        ClipMaximum = max;
    }

    public override void Render(IRenderContext rc, int pass)
    {
        OxyRect band = BandRect;
        if (band.Width <= 0 || band.Height <= 0)
            return;

        rc.PushClip(band);
        try
        {
            // Gridlines only: the stock labels would sit below the plot area.
            base.Render(new TextSuppressingRenderContext(rc), pass);
        }
        finally
        {
            rc.PopClip();
        }
    }

    /// <summary>
    /// Draws the tick labels on a semi-opaque plot-background strip at the bottom of the band. A label
    /// that would cross the band's left or right edge, or touch the previous label, is skipped instead
    /// of being drawn half clipped or overprinted.
    /// </summary>
    internal void RenderBandLabels(IRenderContext rc)
    {
        if (!ShowLabels)
            return;

        OxyRect band = BandRect;
        OxySize lineSize = rc.MeasureText("0", ActualFont, ActualFontSize, ActualFontWeight);
        double stripHeight = lineSize.Height + 2 * LabelPadding;
        if (band.Width <= 0 || stripHeight >= band.Height || double.IsNaN(ActualMinimum) || double.IsNaN(ActualMaximum))
            return;

        // Stay 1 px above the band bottom so the stacked-lane separator remains visible.
        double bottom = band.Bottom - 1;
        OxyColor background = PlotModel.PlotAreaBackground.IsVisible() ? PlotModel.PlotAreaBackground : PlotModel.Background;
        if (background.IsVisible())
        {
            rc.DrawRectangle(
                new OxyRect(band.Left, bottom - stripHeight, band.Width, stripHeight),
                OxyColor.FromAColor(LabelBackgroundAlpha, background),
                OxyColors.Undefined,
                0,
                EdgeRenderingMode.Automatic);
        }

        GetTickValues(out IList<double> labels, out _, out _);
        double tolerance = Math.Abs(ActualMinorStep) * 1e-3;
        double previousRight = double.NegativeInfinity;
        foreach (double value in labels.OrderBy(label => label))
        {
            if (value < ClipMinimum - tolerance || value > ClipMaximum + tolerance)
                continue;

            string text = FormatValue(value);
            if (string.IsNullOrEmpty(text))
                continue;

            OxySize size = rc.MeasureText(text, ActualFont, ActualFontSize, ActualFontWeight);
            double x = Transform(value);
            double left = x - size.Width / 2;
            double right = x + size.Width / 2;
            if (left < band.Left || right > band.Right || left <= previousRight + 2 * LabelPadding)
                continue;

            previousRight = right;

            rc.DrawText(
                new ScreenPoint(x, bottom - LabelPadding),
                text,
                ActualTextColor,
                ActualFont,
                ActualFontSize,
                ActualFontWeight,
                0,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom);
        }
    }

    /// <summary>Above-series layer for the pane labels; its axis keys make OxyPlot clip it to the band.</summary>
    private sealed class PlotPaneTimeLabels : Annotation
    {
        private readonly PlotPaneTimeAxis _axis;

        internal PlotPaneTimeLabels(PlotPaneTimeAxis axis)
        {
            _axis = axis;
            XAxisKey = axis.Key;
            YAxisKey = axis.BandAxis.Key;
            Layer = AnnotationLayer.AboveSeries;
        }

        public override void Render(IRenderContext rc)
        {
            _axis.RenderBandLabels(rc);
        }
    }
}

/// <summary>
/// Right-drag / Alt+left-drag pan that targets the pointer's pane time axis. OxyPlot assigns the first
/// horizontal axis (the shared one) wherever the pointer is, so the pane is resolved by geometry after
/// the base assignment. The base takes its enable flag from the shared axis; pane axes mirror it.
/// </summary>
internal sealed class PlotPanePanManipulator : PanManipulator
{
    private readonly Func<ScreenPoint, Axis> _resolveTimeAxis;

    internal PlotPanePanManipulator(IPlotView plotView, Func<ScreenPoint, Axis> resolveTimeAxis)
        : base(plotView)
    {
        _resolveTimeAxis = resolveTimeAxis;
    }

    public override void Started(OxyMouseEventArgs e)
    {
        base.Started(e);
        if (XAxis != null && _resolveTimeAxis(e.Position) is Axis timeAxis)
            XAxis = timeAxis;
    }
}

/// <summary>
/// Middle-drag / Ctrl+right-drag / Ctrl+Alt+left-drag zoom rectangle that targets the pointer's pane
/// time axis. Started in a following pane, its time part detaches that pane instead of zooming the
/// shared window: the base zooms only the value axis, and the detach callback receives the
/// rectangle's range on the shared axis. The base keeps its rectangle private, so this class tracks
/// the same rectangle with the same clamps and the same 10 px threshold (OxyPlot 2.2).
/// </summary>
internal sealed class PlotPaneZoomRectangleManipulator : ZoomRectangleManipulator
{
    private const double MinimumZoomSize = 10;

    private readonly Func<ScreenPoint, Axis> _resolveTimeAxis;
    private readonly Func<ScreenPoint, Action<double, double>> _resolveTimeDetach;
    private Action<double, double> _timeDetach;
    private bool _isZoomEnabled;
    private OxyRect _zoomRectangle;

    internal PlotPaneZoomRectangleManipulator(
        IPlotView plotView,
        Func<ScreenPoint, Axis> resolveTimeAxis,
        Func<ScreenPoint, Action<double, double>> resolveTimeDetach)
        : base(plotView)
    {
        _resolveTimeAxis = resolveTimeAxis;
        _resolveTimeDetach = resolveTimeDetach;
    }

    public override void Started(OxyMouseEventArgs e)
    {
        base.Started(e);
        _isZoomEnabled = (XAxis != null && XAxis.IsZoomEnabled) || (YAxis != null && YAxis.IsZoomEnabled);
        _zoomRectangle = new OxyRect(StartPosition.X, StartPosition.Y, 0, 0);
        _timeDetach = XAxis != null ? _resolveTimeDetach?.Invoke(e.Position) : null;
        if (XAxis != null && _resolveTimeAxis(e.Position) is Axis timeAxis)
            XAxis = timeAxis;
    }

    public override void Delta(OxyMouseEventArgs e)
    {
        base.Delta(e);
        if (!_isZoomEnabled)
            return;

        OxyRect plotArea = PlotView.ActualModel.PlotArea;
        double x = Math.Min(StartPosition.X, e.Position.X);
        double width = Math.Abs(StartPosition.X - e.Position.X);
        double y = Math.Min(StartPosition.Y, e.Position.Y);
        double height = Math.Abs(StartPosition.Y - e.Position.Y);
        if (XAxis == null || !XAxis.IsZoomEnabled)
        {
            x = plotArea.Left;
            width = plotArea.Width;
        }

        if (YAxis == null || !YAxis.IsZoomEnabled)
        {
            y = plotArea.Top;
            height = plotArea.Height;
        }

        _zoomRectangle = new OxyRect(x, y, width, height);
    }

    public override void Completed(OxyMouseEventArgs e)
    {
        Action<double, double> timeDetach = _timeDetach;
        if (timeDetach == null || !_isZoomEnabled || XAxis == null || !XAxis.IsZoomEnabled ||
            !(_zoomRectangle.Width > MinimumZoomSize && _zoomRectangle.Height > MinimumZoomSize))
        {
            base.Completed(e);
            return;
        }

        double minimum = XAxis.InverseTransform(_zoomRectangle.Left);
        double maximum = XAxis.InverseTransform(_zoomRectangle.Right);
        XAxis = null;
        base.Completed(e);
        timeDetach(minimum, maximum);
    }
}

/// <summary>Forwards every call except text drawing, so a repeated axis pass paints gridlines only.</summary>
internal sealed class TextSuppressingRenderContext : IRenderContext
{
    private readonly IRenderContext _inner;

    internal TextSuppressingRenderContext(IRenderContext inner)
    {
        _inner = inner;
    }

    public bool RendersToScreen => _inner.RendersToScreen;

    public int ClipCount => _inner.ClipCount;

    public void DrawText(ScreenPoint p, string text, OxyColor fill, string fontFamily = null, double fontSize = 10, double fontWeight = FontWeights.Normal, double rotation = 0, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left, VerticalAlignment verticalAlignment = VerticalAlignment.Top, OxySize? maxSize = null)
    {
    }

    public OxySize MeasureText(string text, string fontFamily = null, double fontSize = 10, double fontWeight = 500) => _inner.MeasureText(text, fontFamily, fontSize, fontWeight);

    public void DrawEllipse(OxyRect extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) => _inner.DrawEllipse(extents, fill, stroke, thickness, edgeRenderingMode);

    public void DrawEllipses(IList<OxyRect> extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) => _inner.DrawEllipses(extents, fill, stroke, thickness, edgeRenderingMode);

    public void DrawLine(IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawLine(points, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawLineSegments(IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawLineSegments(points, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawPolygon(IList<ScreenPoint> points, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawPolygon(points, fill, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawPolygons(IList<IList<ScreenPoint>> polygons, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode, double[] dashArray = null, LineJoin lineJoin = LineJoin.Miter) => _inner.DrawPolygons(polygons, fill, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawRectangle(OxyRect rectangle, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) => _inner.DrawRectangle(rectangle, fill, stroke, thickness, edgeRenderingMode);

    public void DrawRectangles(IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) => _inner.DrawRectangles(rectangles, fill, stroke, thickness, edgeRenderingMode);

    public void DrawImage(OxyImage source, double srcX, double srcY, double srcWidth, double srcHeight, double destX, double destY, double destWidth, double destHeight, double opacity, bool interpolate) => _inner.DrawImage(source, srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight, opacity, interpolate);

    public void SetToolTip(string text) => _inner.SetToolTip(text);

    public void CleanUp() => _inner.CleanUp();

    public void PushClip(OxyRect clippingRectangle) => _inner.PushClip(clippingRectangle);

    public void PopClip() => _inner.PopClip();
}
