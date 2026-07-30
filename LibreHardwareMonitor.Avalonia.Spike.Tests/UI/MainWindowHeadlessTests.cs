using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
            Assert.True(selector.Focus());
            Assert.Same(selector, window.FocusManager?.GetFocusedElement());

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
            Assert.Same(loadButton, window.FocusManager?.GetFocusedElement());

            TreeView tree = RequireControl<TreeView>(window, "SensorTree");
            tree.SelectedItem = viewModel.Roots[0];
            Dispatcher.UIThread.RunJobs();
            TreeViewItem rootItem = Assert.IsType<TreeViewItem>(
                tree.ContainerFromIndex(0));
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
        viewModel = new MainWindowViewModel(
            new ImmediateFixtureLoader(CreateSnapshot()),
            SensorLoadLimits.Default);
        return new MainWindow(viewModel, new NullFilePickerFixtureSource());
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
}
