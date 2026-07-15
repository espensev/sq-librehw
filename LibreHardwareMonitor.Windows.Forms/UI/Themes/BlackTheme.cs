using System.Drawing;
using System.Linq;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    public class BlackTheme : LightTheme
    {
        private readonly Color[] _plotColorPalette;
        public override Color ForegroundColor => Color.FromArgb(218, 218, 218);
        public override Color BackgroundColor => Color.FromArgb(0, 0, 0);
        public override Color HyperlinkColor => Color.FromArgb(144, 220, 232);
        public override Color SelectedForegroundColor => ForegroundColor;
        public override Color SelectedBackgroundColor => ColorTranslator.FromHtml("#090A17");
        public override Color LineColor => ColorTranslator.FromHtml("#070A12");
        public override Color StrongLineColor => ColorTranslator.FromHtml("#091217");
        public override Color[] PlotColorPalette => _plotColorPalette;
        public override Color PlotGridMajorColor => Color.FromArgb(73, 73, 73);
        public override Color PlotGridMinorColor => Color.FromArgb(33, 33, 33);
        public override Color ScrollbarBackground => Color.FromArgb(18, 18, 18);
        public override Color ScrollbarTrack => Color.FromArgb(112, 112, 112);
        public override Color ScrollbarTrackHover => Color.FromArgb(176, 176, 176);
        public override Color ScrollbarTrackPressed => HyperlinkColor;
        public override Color ScrollbarBorder => Color.FromArgb(66, 66, 66);
        public override bool WindowTitlebarFallbackToImmersiveDarkMode => true;

        public BlackTheme() : base("black", "Black")
        {
            string[] colors = {
                "#FF2525",
                "#1200FF",
                "#00FF5B",
                "#FFE53B",
                "#00FFFF",
                "#FF0A6C",
                "#2D27FF",
                "#FF2CDF",
                "#00E1FD",
                "#0A5057"
            };

            _plotColorPalette = colors.Select(color => ColorTranslator.FromHtml(color)).ToArray();
        }
    }
}
