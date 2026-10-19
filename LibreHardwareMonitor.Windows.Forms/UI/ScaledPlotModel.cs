using OxyPlot;
using OxyPlot.Legends;

namespace LibreHardwareMonitor.Windows.Forms.UI;

class ScaledPlotModel : PlotModel
{
    public ScaledPlotModel(double dpiXscale, double dpiYscale)
    {
        PlotMargins = new OxyThickness(PlotMargins.Left * dpiXscale,
                                       PlotMargins.Top * dpiYscale,
                                       PlotMargins.Right * dpiXscale,
                                       PlotMargins.Bottom * dpiYscale);

        Padding = new OxyThickness(Padding.Left * dpiXscale,
                                   Padding.Top * dpiYscale,
                                   Padding.Right * dpiXscale,
                                   Padding.Bottom * dpiYscale);

        TitlePadding *= dpiXscale;

        Legend legend = new();

        legend.LegendSymbolLength *= dpiXscale;
        legend.LegendSymbolMargin *= dpiXscale;
        legend.LegendPadding *= dpiXscale;
        legend.LegendColumnSpacing *= dpiXscale;
        legend.LegendItemSpacing *= dpiXscale;
        legend.LegendMargin *= dpiXscale;

        Legends.Add(legend);
    }

    // OxyPlot 2.2 still dispatches through this API; it is removed only in v4.
#pragma warning disable CS0618, CS0672
    public override void HandleMouseDown(object sender, OxyMouseDownEventArgs e)
    {
        // With many unstacked axes the plot area can collapse to zero size, and hit testing
        // the series then passes NaN to the time axis, which throws.
        if (PlotArea.Width <= 0 || PlotArea.Height <= 0)
        {
            e.Handled = true;
            return;
        }

        base.HandleMouseDown(sender, e);
    }
#pragma warning restore CS0618, CS0672
}
