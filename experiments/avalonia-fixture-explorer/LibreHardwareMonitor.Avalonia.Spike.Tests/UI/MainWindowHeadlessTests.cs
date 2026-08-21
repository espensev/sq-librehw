using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.Services;
using LibreHardwareMonitor.Avalonia.Spike.ViewModels;
using LibreHardwareMonitor.Avalonia.Spike.Views;
using Xunit;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.UI;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Window_identifies_itself_as_fixture_only_and_non_shipping()
    {
        MainWindow window = CreateWindow(out _);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                "LibreHardwareMonitor Avalonia Fixture Explorer",
                window.Title);
            TextBlock label = RequireControl<TextBlock>(window, "FixtureOnlyLabel");
            Assert.Equal("Fixture-only, non-shipping", label.Text);
            Assert.Equal(
                "Fixture-only, non-shipping",
                AutomationProperties.GetName(label));
            Assert.True(RequireControl<Button>(window, "LoadFixtureButton").IsVisible);
            Assert.True(RequireControl<Button>(window, "OpenDataJsonButton").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Source_controls_status_tree_and_headers_have_accessible_names()
    {
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);

        try
        {
            await viewModel.LoadLocalPathAsync("headless.json");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                "Bundled fixture selector",
                AutomationProperties.GetName(
                    RequireControl<ComboBox>(window, "FixtureSelector")));
            Assert.Equal(
                "Load selected bundled fixture",
                AutomationProperties.GetName(
                    RequireControl<Button>(window, "LoadFixtureButton")));
            Assert.Equal(
                "Open one local data.json file read-only",
                AutomationProperties.GetName(
                    RequireControl<Button>(window, "OpenDataJsonButton")));
            Assert.Equal(
                "Fixture load status",
                AutomationProperties.GetName(
                    RequireControl<Border>(window, "StatusPanel")));
            TextBlock loadStatus = RequireControl<TextBlock>(window, "LoadingStatus");
            Assert.True(loadStatus.IsVisible);
            Assert.Equal("Fixture loaded.", loadStatus.Text);
            Assert.Equal(
                "Fixture loaded.",
                AutomationProperties.GetName(loadStatus));

            TreeView tree = RequireControl<TreeView>(window, "SensorTree");
            Assert.Equal(
                "Read-only sensor hierarchy",
                AutomationProperties.GetName(tree));
            Assert.Single(viewModel.Roots);

            AssertHeader(window, "NameHeader", "Name column");
            AssertHeader(window, "TypeHeader", "Type column");
            AssertHeader(window, "CurrentHeader", "Current value column");
            AssertHeader(window, "MinimumHeader", "Minimum value column");
            AssertHeader(window, "MaximumHeader", "Maximum value column");
            AssertHeader(window, "StableIdHeader", "Stable ID column");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Empty_loading_loaded_and_retained_rejection_states_are_visible()
    {
        QueueFixtureLoader loader = new();
        TaskCompletionSource<SensorLoadResult> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Enqueue((_, _) => pending.Task);
        loader.EnqueueResult(
            SensorLoadResult.Failure(
                new SensorLoadError(
                    SensorLoadErrorCode.InvalidJson,
                    "The document is truncated.")));
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel, loader);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBlock emptyState =
                RequireControl<TextBlock>(window, "InitialEmptyState");
            TextBlock errorState =
                RequireControl<TextBlock>(window, "InitialErrorState");
            Border rejectionBanner =
                RequireControl<Border>(window, "RejectionBanner");
            TextBlock loadingStatus =
                RequireControl<TextBlock>(window, "LoadingStatus");
            TreeView tree = RequireControl<TreeView>(window, "SensorTree");

            Assert.True(emptyState.IsVisible);
            Assert.False(errorState.IsVisible);
            Assert.False(rejectionBanner.IsVisible);
            Assert.False(tree.IsVisible);
            Assert.Equal("No fixture loaded.", loadingStatus.Text);

            Task load = viewModel.LoadLocalPathAsync("pending.json");
            Dispatcher.UIThread.RunJobs();

            Assert.False(emptyState.IsVisible);
            Assert.False(errorState.IsVisible);
            Assert.False(rejectionBanner.IsVisible);
            Assert.False(tree.IsVisible);
            Assert.Equal("Loading fixture.", loadingStatus.Text);

            pending.SetResult(SensorLoadResult.Success(CreateSnapshot()));
            await load;
            Dispatcher.UIThread.RunJobs();

            Assert.False(emptyState.IsVisible);
            Assert.False(errorState.IsVisible);
            Assert.False(rejectionBanner.IsVisible);
            Assert.True(tree.IsVisible);
            Assert.Equal("Fixture loaded.", loadingStatus.Text);
            AssertRenderedSensorValues(tree);

            await viewModel.LoadLocalPathAsync("rejected.json");
            Dispatcher.UIThread.RunJobs();

            Assert.False(emptyState.IsVisible);
            Assert.False(errorState.IsVisible);
            Assert.True(rejectionBanner.IsVisible);
            Assert.True(tree.IsVisible);
            Assert.Contains(
                "last accepted hierarchy",
                loadingStatus.Text,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Closing_window_cancels_and_invalidates_active_load()
    {
        QueueFixtureLoader loader = new();
        TaskCompletionSource<SensorLoadResult> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Enqueue((_, _) => pending.Task);
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel, loader);

        window.Show();
        Task load = viewModel.LoadLocalPathAsync("closing.json");
        CancellationToken cancellationToken =
            Assert.Single(loader.CancellationTokens);

        window.Close();

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsLoading);

        pending.SetResult(SensorLoadResult.Success(CreateSnapshot()));
        await load;

        Assert.False(viewModel.HasSnapshot);
        Assert.Empty(viewModel.Roots);
    }

    [AvaloniaFact]
    public void Picker_failure_is_sanitized_into_visible_initial_error_state()
    {
        DelegatingFilePickerFixtureSource picker = new(
            _ => Task.FromException<string?>(
                new InvalidOperationException(
                    @"Picker failed at C:\private\operator-name\data.json")));
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel, filePicker: picker);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Click(RequireControl<Button>(window, "OpenDataJsonButton"));

            Assert.True(viewModel.HasError);
            Assert.Equal(
                "IoFailure: The fixture source could not be opened.",
                viewModel.RejectionMessage);
            Assert.DoesNotContain("operator-name", viewModel.RejectionMessage);
            Assert.DoesNotContain(@"C:\private", viewModel.RejectionMessage);
            Assert.True(
                RequireControl<Border>(window, "RejectionBanner").IsVisible);
            Assert.True(
                RequireControl<TextBlock>(window, "InitialErrorState").IsVisible);
            Assert.False(RequireControl<TreeView>(window, "SensorTree").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Picker_cancellation_remains_a_normal_empty_state()
    {
        DelegatingFilePickerFixtureSource picker = new(
            _ => Task.FromCanceled<string?>(new CancellationToken(canceled: true)));
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel, filePicker: picker);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Click(RequireControl<Button>(window, "OpenDataJsonButton"));

            Assert.False(viewModel.HasError);
            Assert.True(viewModel.ShowInitialEmptyState);
            Assert.True(
                RequireControl<TextBlock>(window, "InitialEmptyState").IsVisible);
            Assert.False(
                RequireControl<Border>(window, "RejectionBanner").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_focus_order_and_tree_expand_collapse_are_operable()
    {
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);

        try
        {
            await viewModel.LoadLocalPathAsync("keyboard.json");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ComboBox selector = RequireControl<ComboBox>(window, "FixtureSelector");
            Button loadButton = RequireControl<Button>(window, "LoadFixtureButton");
            Button openButton = RequireControl<Button>(window, "OpenDataJsonButton");
            TreeView tree = RequireControl<TreeView>(window, "SensorTree");
            Assert.True(selector.Focus());
            Assert.Same(selector, window.FocusManager?.GetFocusedElement());

            PressTab(window);
            Assert.Same(loadButton, window.FocusManager?.GetFocusedElement());

            PressTab(window);
            Assert.Same(openButton, window.FocusManager?.GetFocusedElement());

            PressTab(window);
            TreeViewItem rootItem = Assert.IsType<TreeViewItem>(
                tree.ContainerFromIndex(0));
            Assert.Same(rootItem, window.FocusManager?.GetFocusedElement());

            tree.SelectedItem = viewModel.Roots[0];
            Dispatcher.UIThread.RunJobs();
            Assert.True(rootItem.Focus());
            Assert.False(rootItem.IsExpanded);

            window.KeyPress(
                Key.Right,
                RawInputModifiers.None,
                PhysicalKey.ArrowRight,
                string.Empty);
            window.KeyRelease(
                Key.Right,
                RawInputModifiers.None,
                PhysicalKey.ArrowRight,
                string.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.True(rootItem.IsExpanded);

            window.KeyPress(
                Key.Left,
                RawInputModifiers.None,
                PhysicalKey.ArrowLeft,
                string.Empty);
            window.KeyRelease(
                Key.Left,
                RawInputModifiers.None,
                PhysicalKey.ArrowLeft,
                string.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.False(rootItem.IsExpanded);
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateWindow(out MainWindowViewModel viewModel)
    {
        return CreateWindow(out viewModel, loader: null, filePicker: null);
    }

    private static MainWindow CreateWindow(
        out MainWindowViewModel viewModel,
        ISensorFixtureLoader? loader = null,
        IFilePickerFixtureSource? filePicker = null)
    {
        viewModel = new MainWindowViewModel(
            loader ?? new ImmediateFixtureLoader(CreateSnapshot()),
            SensorLoadLimits.Default);
        return new MainWindow(
            viewModel,
            filePicker ?? new NullFilePickerFixtureSource());
    }

    private static void AssertRenderedSensorValues(TreeView tree)
    {
        TreeViewItem rootItem = Assert.IsType<TreeViewItem>(
            tree.ContainerFromIndex(0));
        rootItem.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        TreeViewItem hardwareItem = Assert.Single(
            rootItem
                .GetVisualDescendants()
                .OfType<TreeViewItem>());
        hardwareItem.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        string?[] renderedText = tree
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .ToArray();

        Assert.Contains("CPU Package", renderedText);
        Assert.Contains("Temperature", renderedText);
        Assert.Contains("40.0 °C", renderedText);
        Assert.Contains("42.5 °C", renderedText);
        Assert.Contains("45.0 °C", renderedText);
        Assert.Contains("/sensor/cpu/temperature/0", renderedText);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressTab(MainWindow window)
    {
        window.KeyPress(
            Key.Tab,
            RawInputModifiers.None,
            PhysicalKey.Tab,
            string.Empty);
        window.KeyRelease(
            Key.Tab,
            RawInputModifiers.None,
            PhysicalKey.Tab,
            string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertHeader(
        MainWindow window,
        string controlName,
        string automationName)
    {
        TextBlock header = RequireControl<TextBlock>(window, controlName);
        Assert.Equal(automationName, AutomationProperties.GetName(header));
        Assert.True(AutomationProperties.GetIsColumnHeader(header));
    }

    private static T RequireControl<T>(MainWindow window, string name)
        where T : Control
    {
        return window.FindControl<T>(name) ??
            throw new InvalidOperationException($"Control '{name}' was not found.");
    }

    private static SensorSnapshot CreateSnapshot()
    {
        SensorNodeSnapshot sensor = new(
            SensorNodeKind.Sensor,
            "CPU Package",
            "/sensor/cpu/temperature/0",
            null,
            "Temperature",
            null,
            new SensorValueSnapshot(40.0, "40.0 °C"),
            new SensorValueSnapshot(42.5, "42.5 °C"),
            new SensorValueSnapshot(45.0, "45.0 °C"),
            ImmutableArray<SensorNodeSnapshot>.Empty);
        SensorNodeSnapshot hardware = new(
            SensorNodeKind.Hardware,
            "CPU",
            null,
            "/hardware/cpu/0",
            null,
            null,
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            ImmutableArray.Create(sensor));
        SensorNodeSnapshot root = new(
            SensorNodeKind.Group,
            "Computer",
            null,
            null,
            null,
            null,
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            new SensorValueSnapshot(null, string.Empty),
            ImmutableArray.Create(hardware));

        return new SensorSnapshot(
            "headless.json",
            "0.9.6",
            ImmutableArray.Create(root),
            3,
            1);
    }

    private sealed class ImmediateFixtureLoader : ISensorFixtureLoader
    {
        private readonly SensorSnapshot _snapshot;

        public ImmediateFixtureLoader(SensorSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<SensorLoadResult> LoadAsync(
            Stream stream,
            string sourceName,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SensorLoadResult.Success(_snapshot));
        }

        public Task<SensorLoadResult> LoadFileAsync(
            string filePath,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SensorLoadResult.Success(_snapshot));
        }
    }

    private sealed class NullFilePickerFixtureSource : IFilePickerFixtureSource
    {
        public Task<string?> PickFixtureAsync(Window owner)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class DelegatingFilePickerFixtureSource :
        IFilePickerFixtureSource
    {
        private readonly Func<Window, Task<string?>> _pickFixture;

        public DelegatingFilePickerFixtureSource(
            Func<Window, Task<string?>> pickFixture)
        {
            _pickFixture = pickFixture;
        }

        public Task<string?> PickFixtureAsync(Window owner)
        {
            return _pickFixture(owner);
        }
    }

    private sealed class QueueFixtureLoader : ISensorFixtureLoader
    {
        private readonly Queue<
            Func<string, CancellationToken, Task<SensorLoadResult>>> _responses = new();

        public List<CancellationToken> CancellationTokens { get; } = [];

        public void Enqueue(
            Func<string, CancellationToken, Task<SensorLoadResult>> response)
        {
            _responses.Enqueue(response);
        }

        public void EnqueueResult(SensorLoadResult result)
        {
            Enqueue((_, _) => Task.FromResult(result));
        }

        public Task<SensorLoadResult> LoadAsync(
            Stream stream,
            string sourceName,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "Headless tests exercise the frozen file-loader contract only.");
        }

        public Task<SensorLoadResult> LoadFileAsync(
            string filePath,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return _responses.Dequeue()(filePath, cancellationToken);
        }
    }
}
