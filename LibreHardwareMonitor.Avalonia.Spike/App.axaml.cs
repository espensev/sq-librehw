using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;
using LibreHardwareMonitor.Avalonia.Spike.Services;
using LibreHardwareMonitor.Avalonia.Spike.ViewModels;
using LibreHardwareMonitor.Avalonia.Spike.Views;

namespace LibreHardwareMonitor.Avalonia.Spike;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel viewModel = new MainWindowViewModel(
                new BoundedDataJsonFixtureLoader(),
                SensorLoadLimits.Default);

            desktop.MainWindow = new MainWindow(
                viewModel,
                new FilePickerFixtureSource());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
