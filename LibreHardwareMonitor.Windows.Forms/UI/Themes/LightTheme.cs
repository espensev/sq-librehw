using System.Drawing;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    public class LightTheme : Theme
    {
        private readonly Color[] _plotColorPalette = new Color[] {
            Color.Blue,
            Color.OrangeRed,
            Color.Green,
            Color.LightSeaGreen,
            Color.Goldenrod,
            Color.DarkViolet,
            Color.YellowGreen,
            Color.SaddleBrown,
            Color.RoyalBlue,
            Color.DeepPink,
            Color.MediumSeaGreen,
            Color.Olive,
            Color.Firebrick
        };

        public LightTheme(string id, string displayName) : base(id, displayName) { }
        public override Color ForegroundColor => Color.FromArgb(0, 0, 0);
        public override Color BackgroundColor => Color.FromArgb(255, 255, 255);
        public override Color HyperlinkColor => Color.FromArgb(0, 0, 255);
        public override Color SelectedForegroundColor => ForegroundColor;
        public override Color SelectedBackgroundColor => Color.FromArgb(240, 240, 240);
        public override Color LineColor => Color.FromArgb(247, 247, 247);
        public override Color StrongLineColor => Color.FromArgb(209, 209, 209);
        public override Color[] PlotColorPalette => _plotColorPalette;
        public override Color PlotGridMajorColor => Color.FromArgb(192, 192, 192);
        public override Color PlotGridMinorColor => Color.FromArgb(232, 232, 232);
        public override Color ScrollbarBackground => Color.FromArgb(242, 242, 242);
        public override Color ScrollbarTrack => Color.FromArgb(112, 112, 112);
        public override Color ScrollbarTrackHover => Color.FromArgb(72, 72, 72);
        public override Color ScrollbarTrackPressed => Color.FromArgb(0, 102, 192);
        public override Color ScrollbarBorder => Color.FromArgb(190, 190, 190);
        public override bool WindowTitlebarFallbackToImmersiveDarkMode => false;

        public LightTheme() : base("light", "Light") { }
    }
}
