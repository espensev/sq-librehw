using System.Drawing;
using System.Linq;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    public class DarkTheme : LightTheme
    {
        private readonly Color[] _plotColorPalette;
        public override Color ForegroundColor => Color.FromArgb(233, 233, 233);
        public override Color BackgroundColor => Color.FromArgb(30, 30, 30);
        public override Color HyperlinkColor => Color.FromArgb(144, 220, 232);
        public override Color SelectedForegroundColor => ForegroundColor;
        public override Color SelectedBackgroundColor => Color.FromArgb(45, 45, 45);
        public override Color LineColor => Color.FromArgb(38, 38, 38);
        public override Color StrongLineColor => Color.FromArgb(53, 53, 53);
        public override Color[] PlotColorPalette => _plotColorPalette;
        public override Color PlotGridMajorColor => Color.FromArgb(93, 93, 93);
        public override Color PlotGridMinorColor => Color.FromArgb(53, 53, 53);
        public override Color ScrollbarBackground => Color.FromArgb(42, 42, 42);
        public override Color ScrollbarTrack => Color.FromArgb(138, 138, 138);
        public override Color ScrollbarTrackHover => Color.FromArgb(190, 190, 190);
        public override Color ScrollbarTrackPressed => HyperlinkColor;
        public override Color ScrollbarBorder => Color.FromArgb(76, 76, 76);
        public override bool WindowTitlebarFallbackToImmersiveDarkMode => true;

        public DarkTheme() : base("dark", "Dark")
        {
            string[] colors = {
                "#F07178",
                "#82AAFF",
                "#C3E88D",
                "#FFCB6B",
                "#009688",
                "#89DDF3",
                "#FFE082",
                "#7986CB",
                "#C792EA",
                "#FF5370",
                "#73d1c8",
                "#F78C6A"
            };

            _plotColorPalette = colors.Select(color => ColorTranslator.FromHtml(color)).ToArray();
        }
    }
}
