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
    private bool _isClosed;

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
        await RunOperatorActionAsync(_viewModel.LoadBundledFixtureAsync);
    }

    private async void OpenDataJson_OnClick(object? sender, RoutedEventArgs e)
    {
        await RunOperatorActionAsync(
            async () =>
            {
                string? selectedPath = await _filePicker.PickFixtureAsync(this);

                if (!_isClosed && selectedPath is not null)
                {
                    await _viewModel.LoadLocalPathAsync(selectedPath);
                }
            });
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _viewModel.CancelActiveLoad();
        base.OnClosed(e);
    }

    private async Task RunOperatorActionAsync(Func<Task> operation)
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            // Closing a picker or cancelling a provider operation is not a
            // fixture rejection.
        }
        catch (Exception)
        {
            if (!_isClosed)
            {
                _viewModel.ReportFixtureSourceFailure();
            }
        }
    }
}
