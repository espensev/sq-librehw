using Avalonia;
using Avalonia.Themes.Fluent;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.UI;

public sealed class TestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
