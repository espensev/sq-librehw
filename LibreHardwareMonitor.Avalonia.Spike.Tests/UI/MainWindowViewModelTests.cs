using System.Collections.Immutable;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.ViewModels;
using Xunit;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.UI;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Initial_state_is_explicitly_empty()
    {
        QueueFixtureLoader loader = new();
        MainWindowViewModel viewModel = CreateViewModel(loader);

        Assert.Empty(viewModel.Roots);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasSnapshot);
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.ShowInitialEmptyState);
        Assert.False(viewModel.ShowInitialErrorState);
        Assert.Equal("None", viewModel.SourceName);
        Assert.Equal("Unavailable", viewModel.Version);
        Assert.Equal(0, viewModel.TotalNodeCount);
        Assert.Equal(0, viewModel.SensorCount);
        Assert.Equal(6, viewModel.FixtureChoices.Count);
        Assert.Equal("normal.json", viewModel.SelectedFixtureChoice.DisplayName);
        Assert.Equal("No fixture loaded.", viewModel.LoadStateStatus);
    }

    [Fact]
    public async Task Successful_load_publishes_hierarchy_counts_source_version_and_stable_ids()
    {
        QueueFixtureLoader loader = new();
        SensorSnapshot snapshot = CreateSnapshot("accepted.json", "0.9.6", "/sensor/cpu/temperature/0");
        loader.EnqueueResult(SensorLoadResult.Success(snapshot));
        MainWindowViewModel viewModel = CreateViewModel(loader);

        await viewModel.LoadLocalPathAsync(@"C:\fixtures\accepted.json");

        Assert.True(viewModel.HasSnapshot);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("accepted.json", viewModel.SourceName);
        Assert.Equal("0.9.6", viewModel.Version);
        Assert.Equal(3, viewModel.TotalNodeCount);
        Assert.Equal(1, viewModel.SensorCount);
        SensorNodeViewModel sensor = viewModel.Roots[0].Children[0].Children[0];
        Assert.Equal("/sensor/cpu/temperature/0", sensor.StableId);
        Assert.Equal("42.5 °C", sensor.Current);
        Assert.Equal(@"C:\fixtures\accepted.json", Assert.Single(loader.FilePaths));
    }

    [Fact]
    public void Unavailable_values_are_textual_and_accessible()
    {
        SensorNodeSnapshot snapshot = new(
            SensorNodeKind.Sensor,
            "CPU Package",
            "/sensor/cpu/temperature/0",
            null,
            null,
            null,
            new SensorValueSnapshot(null, "-"),
            new SensorValueSnapshot(double.NaN, "NaN"),
            new SensorValueSnapshot(null, string.Empty),
            ImmutableArray<SensorNodeSnapshot>.Empty);

        SensorNodeViewModel viewModel = new(snapshot);

        Assert.Equal("Unknown", viewModel.SensorType);
        Assert.Equal("Unavailable", viewModel.Current);
        Assert.Equal("Unavailable", viewModel.Minimum);
        Assert.Equal("Unavailable", viewModel.Maximum);
        Assert.Contains("current Unavailable", viewModel.AccessibleSummary);
        Assert.Contains("minimum Unavailable", viewModel.AccessibleSummary);
        Assert.Equal("Current: Unavailable", viewModel.CurrentAutomationName);
    }

    [Fact]
    public void Available_values_preserve_non_empty_producer_display_text()
    {
        SensorNodeSnapshot snapshot = new(
            SensorNodeKind.Sensor,
            "Memory Clock",
            "/sensor/memory/clock/0",
            null,
            "Clock",
            null,
            new SensorValueSnapshot(1.0, "-"),
            new SensorValueSnapshot(1_800.0, "Infinity Fabric 1800 MHz"),
            new SensorValueSnapshot(2_000.0, "2,000 MHz"),
            ImmutableArray<SensorNodeSnapshot>.Empty);

        SensorNodeViewModel viewModel = new(snapshot);

        Assert.Equal("-", viewModel.Minimum);
        Assert.Equal("Infinity Fabric 1800 MHz", viewModel.Current);
        Assert.Equal("2,000 MHz", viewModel.Maximum);
    }

    [Fact]
    public async Task Loading_transition_is_explicit()
    {
        QueueFixtureLoader loader = new();
        TaskCompletionSource<SensorLoadResult> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Enqueue((_, _) => pending.Task);
        MainWindowViewModel viewModel = CreateViewModel(loader);

        Task load = viewModel.LoadLocalPathAsync("slow.json");

        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.HasSnapshot);
        Assert.Contains("Loading fixture", viewModel.LoadStateStatus);

        pending.SetResult(SensorLoadResult.Success(CreateSnapshot("slow.json", "1.0")));
        await load;

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasSnapshot);
        Assert.Equal("Fixture loaded.", viewModel.LoadStateStatus);
    }

    [Fact]
    public async Task Later_rejection_retains_last_good_hierarchy_and_counts()
    {
        QueueFixtureLoader loader = new();
        loader.EnqueueResult(
            SensorLoadResult.Success(
                CreateSnapshot("last-good.json", "1.0", "/sensor/last-good")));
        loader.EnqueueResult(
            SensorLoadResult.Failure(
                new SensorLoadError(
                    SensorLoadErrorCode.InvalidJson,
                    "The document is truncated.")));
        MainWindowViewModel viewModel = CreateViewModel(loader);

        await viewModel.LoadLocalPathAsync("last-good.json");
        IReadOnlyList<SensorNodeViewModel> acceptedRoots = viewModel.Roots;

        await viewModel.LoadLocalPathAsync("rejected.json");

        Assert.Same(acceptedRoots, viewModel.Roots);
        Assert.True(viewModel.HasSnapshot);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasRetainedSnapshotAfterError);
        Assert.Equal("last-good.json", viewModel.SourceName);
        Assert.Equal(3, viewModel.TotalNodeCount);
        Assert.Equal(1, viewModel.SensorCount);
        Assert.Contains("InvalidJson", viewModel.RejectionMessage);
        Assert.Contains("last accepted hierarchy", viewModel.LoadStateStatus);
    }

    [Fact]
    public async Task Initial_rejection_is_an_empty_error_state()
    {
        QueueFixtureLoader loader = new();
        loader.EnqueueResult(
            SensorLoadResult.Failure(
                new SensorLoadError(
                    SensorLoadErrorCode.InputTooLarge,
                    "The document exceeds the configured input limit.")));
        MainWindowViewModel viewModel = CreateViewModel(loader);

        await viewModel.LoadLocalPathAsync("too-large.json");

        Assert.Empty(viewModel.Roots);
        Assert.False(viewModel.HasSnapshot);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.ShowInitialErrorState);
        Assert.False(viewModel.ShowInitialEmptyState);
        Assert.Contains("InputTooLarge", viewModel.RejectionMessage);
        Assert.Equal(0, viewModel.TotalNodeCount);
        Assert.Equal(0, viewModel.SensorCount);
    }

    [Fact]
    public async Task Unexpected_loader_exception_uses_operator_safe_message()
    {
        QueueFixtureLoader loader = new();
        loader.Enqueue(
            (_, _) => throw new InvalidOperationException(
                @"Internal failure at C:\private\operator-name\fixture.json"));
        MainWindowViewModel viewModel = CreateViewModel(loader);

        await viewModel.LoadLocalPathAsync("rejected.json");

        Assert.True(viewModel.HasError);
        Assert.Contains("IoFailure", viewModel.RejectionMessage);
        Assert.Contains("The fixture could not be loaded.", viewModel.RejectionMessage);
        Assert.DoesNotContain("operator-name", viewModel.RejectionMessage);
        Assert.DoesNotContain(@"C:\private", viewModel.RejectionMessage);
    }

    [Fact]
    public async Task Cancelling_active_load_invalidates_late_publication()
    {
        QueueFixtureLoader loader = new();
        TaskCompletionSource<SensorLoadResult> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Enqueue((_, _) => pending.Task);
        MainWindowViewModel viewModel = CreateViewModel(loader);

        Task load = viewModel.LoadLocalPathAsync("closing.json");

        Assert.True(viewModel.IsLoading);
        CancellationToken cancellationToken =
            Assert.Single(loader.CancellationTokens);

        viewModel.CancelActiveLoad();

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.ShowInitialEmptyState);

        pending.SetResult(
            SensorLoadResult.Success(
                CreateSnapshot("too-late.json", "1.0", "/sensor/too-late")));
        await load;

        Assert.False(viewModel.HasSnapshot);
        Assert.Empty(viewModel.Roots);
        Assert.Equal("None", viewModel.SourceName);
    }

    [Fact]
    public async Task Fixture_source_failure_retains_snapshot_and_uses_safe_message()
    {
        QueueFixtureLoader loader = new();
        loader.EnqueueResult(
            SensorLoadResult.Success(
                CreateSnapshot("last-good.json", "1.0", "/sensor/last-good")));
        MainWindowViewModel viewModel = CreateViewModel(loader);
        await viewModel.LoadLocalPathAsync("last-good.json");
        IReadOnlyList<SensorNodeViewModel> acceptedRoots = viewModel.Roots;

        viewModel.ReportFixtureSourceFailure();

        Assert.Same(acceptedRoots, viewModel.Roots);
        Assert.True(viewModel.HasSnapshot);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasRetainedSnapshotAfterError);
        Assert.Equal(
            "IoFailure: The fixture source could not be opened.",
            viewModel.RejectionMessage);
    }

    [Fact]
    public async Task Slow_old_request_cannot_overwrite_newer_result()
    {
        QueueFixtureLoader loader = new();
        TaskCompletionSource<SensorLoadResult> oldPending = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Enqueue((_, _) => oldPending.Task);
        loader.EnqueueResult(
            SensorLoadResult.Success(
                CreateSnapshot("newer.json", "2.0", "/sensor/newer")));
        MainWindowViewModel viewModel = CreateViewModel(loader);

        Task oldLoad = viewModel.LoadLocalPathAsync("old.json");
        Task newLoad = viewModel.LoadLocalPathAsync("newer.json");

        await newLoad;
        Assert.True(loader.CancellationTokens[0].IsCancellationRequested);
        Assert.Equal("newer.json", viewModel.SourceName);

        oldPending.SetResult(
            SensorLoadResult.Success(
                CreateSnapshot("old.json", "1.0", "/sensor/old")));
        await oldLoad;

        Assert.Equal("newer.json", viewModel.SourceName);
        Assert.Equal("2.0", viewModel.Version);
        Assert.Equal(
            "/sensor/newer",
            viewModel.Roots[0].Children[0].Children[0].StableId);
    }

    [Fact]
    public async Task Bundled_selection_forwards_expected_fixture_path_and_name()
    {
        QueueFixtureLoader loader = new();
        loader.EnqueueResult(
            SensorLoadResult.Success(
                CreateSnapshot("hotplug-after.json", "1.0")));
        MainWindowViewModel viewModel = CreateViewModel(loader);
        FixtureChoice choice = Assert.Single(
            viewModel.FixtureChoices,
            item => item.DisplayName == "hotplug-after.json");
        viewModel.SelectedFixtureChoice = choice;

        await viewModel.LoadBundledFixtureAsync();

        string path = Assert.Single(loader.FilePaths);
        Assert.EndsWith(
            Path.Combine("Fixtures", "hotplug-after.json"),
            path,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("hotplug-after.json", Path.GetFileName(path));
        Assert.Equal("hotplug-after.json", viewModel.SelectedFixtureChoice.DisplayName);
    }

    private static MainWindowViewModel CreateViewModel(ISensorFixtureLoader loader)
    {
        return new MainWindowViewModel(loader, SensorLoadLimits.Default);
    }

    private static SensorSnapshot CreateSnapshot(
        string sourceName,
        string? version,
        string sensorId = "/sensor/cpu/temperature/0")
    {
        SensorNodeSnapshot sensor = new(
            SensorNodeKind.Sensor,
            "CPU Package",
            sensorId,
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
            sourceName,
            version,
            ImmutableArray.Create(root),
            3,
            1);
    }

    private sealed class QueueFixtureLoader : ISensorFixtureLoader
    {
        private readonly Queue<
            Func<string, CancellationToken, Task<SensorLoadResult>>> _responses = new();

        public List<string> FilePaths { get; } = [];

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
                "UI tests exercise the frozen file-loader contract only.");
        }

        public Task<SensorLoadResult> LoadFileAsync(
            string filePath,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            FilePaths.Add(filePath);
            CancellationTokens.Add(cancellationToken);
            return _responses.Dequeue()(filePath, cancellationToken);
        }
    }
}
