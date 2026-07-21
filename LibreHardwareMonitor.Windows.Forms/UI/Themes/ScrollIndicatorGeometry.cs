using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    internal static class ScrollIndicatorGeometry
    {
        internal const int TrackEndInset = 2;
        internal const int MinimumThumbLength = 24;

        internal static int GetEffectiveMaximum(ScrollBar scrollbar)
        {
            int page = Math.Max(1, scrollbar.LargeChange);
            long effectiveMaximum = (long)scrollbar.Maximum - page + 1;
            return (int)Math.Max(scrollbar.Minimum, effectiveMaximum);
        }

        internal static Rectangle GetThumbBounds(
            ScrollBar scrollbar,
            Rectangle clientRectangle,
            Orientation orientation,
            int crossAxisInset)
        {
            int length = orientation == Orientation.Vertical
                ? clientRectangle.Height
                : clientRectangle.Width;
            int crossAxisLength = orientation == Orientation.Vertical
                ? clientRectangle.Width
                : clientRectangle.Height;
            int trackLength = Math.Max(0, length - (TrackEndInset * 2));
            long totalUnits = Math.Max(1L, (long)scrollbar.Maximum - scrollbar.Minimum + 1);
            long pageUnits = Math.Min(totalUnits, Math.Max(1, scrollbar.LargeChange));
            int thumbLength = Math.Min(
                trackLength,
                Math.Max(MinimumThumbLength, (int)Math.Round(trackLength * (double)pageUnits / totalUnits)));

            int effectiveMaximum = GetEffectiveMaximum(scrollbar);
            long scrollableRange = (long)effectiveMaximum - scrollbar.Minimum;
            int travel = Math.Max(0, trackLength - thumbLength);
            int value = Math.Max(scrollbar.Minimum, Math.Min(effectiveMaximum, scrollbar.Value));
            int offset = scrollableRange > 0
                ? (int)Math.Round(travel * (double)((long)value - scrollbar.Minimum) / scrollableRange)
                : 0;
            int crossAxisSize = Math.Max(1, crossAxisLength - (crossAxisInset * 2));

            return orientation == Orientation.Vertical
                ? new Rectangle(crossAxisInset, TrackEndInset + offset, crossAxisSize, thumbLength)
                : new Rectangle(TrackEndInset + offset, crossAxisInset, thumbLength, crossAxisSize);
        }

        internal static int GetValueFromDrag(
            ScrollBar scrollbar,
            int startValue,
            int delta,
            int trackLength,
            int thumbLength)
        {
            int effectiveMaximum = GetEffectiveMaximum(scrollbar);
            long scrollableRange = (long)effectiveMaximum - scrollbar.Minimum;
            int travel = Math.Max(0, trackLength - (TrackEndInset * 2) - thumbLength);
            if (scrollableRange <= 0)
                return scrollbar.Minimum;

            if (travel <= 0)
                return Math.Max(scrollbar.Minimum, Math.Min(effectiveMaximum, startValue));

            double value = startValue + (delta * (double)scrollableRange / travel);
            if (value <= scrollbar.Minimum)
                return scrollbar.Minimum;

            if (value >= effectiveMaximum)
                return effectiveMaximum;

            return (int)Math.Round(value);
        }
    }
}
