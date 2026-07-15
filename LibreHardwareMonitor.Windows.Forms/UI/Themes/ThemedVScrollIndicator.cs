using System.Drawing;
using System;
using System.Windows.Forms;
using System.Windows.Automation.Provider;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    public class ThemedVScrollIndicator : Control
    {
        private readonly VScrollBar _scrollbar;
        private readonly Control _scrollbarOwner;
        private int _startValue = 0;
        private int _startPos = 0;
        private bool _isScrolling = false;
        private bool _isHovered = false;
        private ScrollIndicatorAutomationProvider _automationProvider;

        public static void AddToControl(Control control)
        {
            VScrollBar scrollbar = null;
            foreach (Control child in control.Controls)
            {
                if (child is ThemedVScrollIndicator)
                    return;

                if (child is VScrollBar candidate)
                    scrollbar = candidate;
            }

            if (scrollbar == null)
                return;

            var indicator = new ThemedVScrollIndicator(scrollbar);
            control.Controls.Add(indicator);
            indicator.BringToFront();
        }

        public ThemedVScrollIndicator(VScrollBar scrollBar)
        {
            _scrollbar = scrollBar;
            _scrollbarOwner = scrollBar.Parent;

            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            TabStop = false;
            Cursor = Cursors.SizeNS;
            if (string.IsNullOrWhiteSpace(scrollBar.AccessibleName))
                scrollBar.AccessibleName = "Sensor list vertical scrollbar";
            scrollBar.AccessibleRole = AccessibleRole.ScrollBar;
            AccessibleName = scrollBar.AccessibleName;
            AccessibleRole = AccessibleRole.ScrollBar;

            scrollBar.VisibleChanged += ScrollBar_VisibleChanged;
            scrollBar.LocationChanged += ScrollBar_BoundsChanged;
            scrollBar.SizeChanged += ScrollBar_BoundsChanged;
            scrollBar.Scroll += ScrollBar_Scroll;
            scrollBar.ValueChanged += ScrollBar_ValueChanged;
            _scrollbarOwner.Invalidated += ScrollBarOwner_Invalidated;
            _scrollbarOwner.SystemColorsChanged += ScrollBarOwner_SystemColorsChanged;

            SyncBounds();
            SyncVisibility();
        }

        protected override void WndProc(ref Message m)
        {
            const int WmGetObject = 0x003D;
            const int WmDestroy = 0x0002;
            if (m.Msg == WmGetObject && m.LParam.ToInt64() == AutomationInteropProvider.RootObjectId)
            {
                _automationProvider ??= new ScrollIndicatorAutomationProvider(this, _scrollbar, Orientation.Vertical);
                m.Result = AutomationInteropProvider.ReturnRawElementProvider(
                    Handle,
                    m.WParam,
                    m.LParam,
                    _automationProvider);
                return;
            }

            if (m.Msg == WmDestroy && _automationProvider != null)
            {
                AutomationInteropProvider.ReturnRawElementProvider(
                    Handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    null);
                _automationProvider = null;
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_isScrolling || e.Button != MouseButtons.Left)
                return;

            _isScrolling = true;
            Invalidate();

            _startPos = e.Y;
            _startValue = _scrollbar.Value;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
                return;

            _isScrolling = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isScrolling)
                return;

            Rectangle thumbBounds = ScrollIndicatorGeometry.GetThumbBounds(
                _scrollbar,
                ClientRectangle,
                Orientation.Vertical,
                2);
            int value = ScrollIndicatorGeometry.GetValueFromDrag(
                _scrollbar,
                _startValue,
                e.Y - _startPos,
                ClientSize.Height,
                thumbBounds.Height);

            if (_scrollbar.Value != value)
                _scrollbar.Value = value;
        }

        protected override void OnMouseEnter(System.EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(System.EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(System.EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _isScrolling)
            {
                _isScrolling = false;
                Invalidate();
            }
        }

        private void ScrollBar_VisibleChanged(object sender, System.EventArgs e)
        {
            SyncVisibility();
        }

        private void ScrollBar_BoundsChanged(object sender, System.EventArgs e)
        {
            SyncBounds();
        }

        private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            Invalidate();
        }

        private void ScrollBar_ValueChanged(object sender, System.EventArgs e)
        {
            Invalidate();
        }

        private void ScrollBarOwner_Invalidated(object sender, InvalidateEventArgs e)
        {
            Invalidate();
        }

        private void ScrollBarOwner_SystemColorsChanged(object sender, System.EventArgs e)
        {
            SyncVisibility();
        }

        private void SyncBounds()
        {
            Bounds = _scrollbar.Bounds;
        }

        private void SyncVisibility()
        {
            Visible = _scrollbar.Visible && !SystemInformation.HighContrast;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            using (SolidBrush brush = new SolidBrush(Theme.Current.ScrollbarBackground))
                g.FillRectangle(brush, new Rectangle(0, 0, Bounds.Width, Bounds.Height));

            using (Pen pen = new Pen(Theme.Current.ScrollbarBorder))
                g.DrawLine(pen, 0, 0, 0, Bounds.Height - 1);

            if (ScrollIndicatorGeometry.GetEffectiveMaximum(_scrollbar) > _scrollbar.Minimum)
            {
                int inset = _isHovered || _isScrolling ? 2 : 3;
                Color thumbColor = _isScrolling
                    ? Theme.Current.ScrollbarTrackPressed
                    : _isHovered
                        ? Theme.Current.ScrollbarTrackHover
                        : Theme.Current.ScrollbarTrack;
                Rectangle thumbBounds = ScrollIndicatorGeometry.GetThumbBounds(
                    _scrollbar,
                    ClientRectangle,
                    Orientation.Vertical,
                    inset);
                using (SolidBrush brush = new SolidBrush(thumbColor))
                    g.FillRectangle(brush, thumbBounds);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scrollbar.VisibleChanged -= ScrollBar_VisibleChanged;
                _scrollbar.LocationChanged -= ScrollBar_BoundsChanged;
                _scrollbar.SizeChanged -= ScrollBar_BoundsChanged;
                _scrollbar.Scroll -= ScrollBar_Scroll;
                _scrollbar.ValueChanged -= ScrollBar_ValueChanged;
                _scrollbarOwner.Invalidated -= ScrollBarOwner_Invalidated;
                _scrollbarOwner.SystemColorsChanged -= ScrollBarOwner_SystemColorsChanged;
            }

            base.Dispose(disposing);
        }
    }
}
