using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LibreHardwareMonitor.Avalonia.Spike.Services;
using LibreHardwareMonitor.Avalonia.Spike.ViewModels;

namespace LibreHardwareMonitor.Avalonia.Spike.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IFilePickerFixtureSource _filePicker;

    public MainWindow(
        MainWindowViewModel viewModel,
        IFilePickerFixtureSource filePicker)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));

        AvaloniaXamlLoader.Load(this);
        DataContext = _viewModel;
    }

    private async void LoadFixture_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.LoadBundledFixtureAsync();
    }

    private async void OpenDataJson_OnClick(object? sender, RoutedEventArgs e)
    {
        string? selectedPath = await _filePicker.PickFixtureAsync(this);

        if (selectedPath is not null)
        {
            await _viewModel.LoadLocalPathAsync(selectedPath);
        }
    }
}
