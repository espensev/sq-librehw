using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(
    typeof(LibreHardwareMonitor.Avalonia.Spike.Tests.UI.TestAppBuilder))]

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.UI;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
