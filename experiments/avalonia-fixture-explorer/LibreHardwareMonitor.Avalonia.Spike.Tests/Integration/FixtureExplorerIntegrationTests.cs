using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;
using LibreHardwareMonitor.Avalonia.Spike.ViewModels;
using Xunit;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.Integration;

public sealed class FixtureExplorerIntegrationTests
{
    [Fact]
    public async Task NormalFixture_PublishesExactHierarchyMetadataValuesAndStableIds()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadLocalPathAsync(FixturePath("normal.json"));

        Assert.True(viewModel.HasSnapshot);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("normal.json", viewModel.SourceName);
        Assert.Equal("0.9.6", viewModel.Version);
        Assert.Equal(12, viewModel.TotalNodeCount);
        Assert.Equal(5, viewModel.SensorCount);

        SensorNodeViewModel computer = Assert.Single(viewModel.Roots);
        Assert.Equal("GOLDEN-PC", computer.Name);
        Assert.Equal(SensorNodeKind.Group, computer.Kind);

        SensorNodeViewModel hardware = Assert.Single(computer.Children);
        Assert.Equal("Golden CPU", hardware.Name);
        Assert.Equal(SensorNodeKind.Hardware, hardware.Kind);
        Assert.Equal("/golden/0", hardware.StableId);
        Assert.Equal(
            ["Voltages", "Temperatures", "Load", "Fans", "Throughput"],
            hardware.Children.Select(node => node.Name).ToArray());

        SensorNodeViewModel temperature = Assert.Single(hardware.Children[1].Children);
        Assert.Equal("CPU Core #1", temperature.Name);
        Assert.Equal("Temperature", temperature.SensorType);
        Assert.Equal("30.2 °C", temperature.Minimum);
        Assert.Equal("49.5 °C", temperature.Current);
        Assert.Equal("80.0 °C", temperature.Maximum);
        Assert.Equal("/golden/0/temperature/0", temperature.StableId);

        SensorNodeViewModel voltage = Assert.Single(hardware.Children[0].Children);
        Assert.Equal("Unavailable", voltage.Minimum);
        Assert.Equal("Unavailable", voltage.Current);
        Assert.Equal("Unavailable", voltage.Maximum);

        SensorNodeViewModel fan = Assert.Single(hardware.Children[3].Children);
        Assert.Equal("Fan \"Æøå\" #1", fan.Name);
        Assert.Equal("1234 RPM", fan.Current);
        Assert.Equal("/golden/0/fan/0", fan.StableId);
    }

    [Fact]
    public async Task UnavailableFixture_PublishesHonestFallbacks()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadLocalPathAsync(FixturePath("unavailable.json"));

        Assert.True(viewModel.HasSnapshot);
        Assert.False(viewModel.HasError);
        Assert.Equal("unavailable.json", viewModel.SourceName);
        Assert.Equal("0.9.6", viewModel.Version);
        Assert.Equal(4, viewModel.TotalNodeCount);
        Assert.Equal(1, viewModel.SensorCount);

        SensorNodeViewModel group = Assert.Single(viewModel.Roots);
        SensorNodeViewModel hardware = Assert.Single(group.Children);
        SensorNodeViewModel typeGroup = Assert.Single(hardware.Children);
        SensorNodeViewModel sensor = Assert.Single(typeGroup.Children);

        Assert.Equal("(unnamed)", group.Name);
        Assert.Equal("(unnamed)", hardware.Name);
        Assert.Equal("(unnamed)", sensor.Name);
        Assert.Equal("Unknown", sensor.SensorType);
        Assert.Equal("Unavailable", sensor.Minimum);
        Assert.Equal("Unavailable", sensor.Current);
        Assert.Equal("Unavailable", sensor.Maximum);
        Assert.Equal(
            "/fixture/unavailable/0/temperature/0",
            sensor.StableId);
    }

    [Fact]
    public async Task HotplugReplacement_AtomicallyRemovesAbsentSensor()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.LoadLocalPathAsync(FixturePath("hotplug-before.json"));
        IReadOnlyList<SensorNodeViewModel> beforeRoots = viewModel.Roots;
        string[] beforeIds = SensorIds(beforeRoots);
        Assert.Equal(
            [
                "/fixture/hotplug/0/temperature/0",
                "/fixture/hotplug/0/fan/0"
            ],
            beforeIds);
        Assert.Equal(2, viewModel.SensorCount);

        await viewModel.LoadLocalPathAsync(FixturePath("hotplug-after.json"));

        Assert.NotSame(beforeRoots, viewModel.Roots);
        Assert.Equal(beforeIds, SensorIds(beforeRoots));
        Assert.Equal(
            "47.0 °C",
            FindSensor(
                beforeRoots,
                "/fixture/hotplug/0/temperature/0").Current);
        Assert.Equal(
            "1100 RPM",
            FindSensor(beforeRoots, "/fixture/hotplug/0/fan/0").Current);
        Assert.Equal(
            ["/fixture/hotplug/0/temperature/0"],
            SensorIds(viewModel.Roots));
        Assert.DoesNotContain(
            Flatten(viewModel.Roots),
            node => node.StableId == "/fixture/hotplug/0/fan/0");
        Assert.Equal("48.0 °C", FindSensor(
            viewModel.Roots,
            "/fixture/hotplug/0/temperature/0").Current);
        Assert.Equal(1, viewModel.SensorCount);
        Assert.False(viewModel.HasError);
    }

    [Theory]
    [InlineData("malformed.json", SensorLoadErrorCode.InvalidJson)]
    [InlineData(
        "oversized-string.json",
        SensorLoadErrorCode.ExcessiveStringLength)]
    public async Task RejectedReplacement_RetainsLastValidSnapshotAndTypedError(
        string rejectedFixture,
        SensorLoadErrorCode expectedCode)
    {
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.LoadLocalPathAsync(FixturePath("normal.json"));
        IReadOnlyList<SensorNodeViewModel> acceptedRoots = viewModel.Roots;
        string[] acceptedIds = SensorIds(acceptedRoots);

        await viewModel.LoadLocalPathAsync(FixturePath(rejectedFixture));

        Assert.Same(acceptedRoots, viewModel.Roots);
        Assert.Equal(acceptedIds, SensorIds(viewModel.Roots));
        Assert.True(viewModel.HasSnapshot);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasRetainedSnapshotAfterError);
        Assert.Equal("normal.json", viewModel.SourceName);
        Assert.Equal("0.9.6", viewModel.Version);
        Assert.Equal(12, viewModel.TotalNodeCount);
        Assert.Equal(5, viewModel.SensorCount);
        Assert.StartsWith(
            $"{expectedCode}: ",
            viewModel.RejectionMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupersededRealParserRequest_CannotPublishLate()
    {
        DelayedFirstCompletionFixtureLoader loader = new(
            new BoundedDataJsonFixtureLoader());
        MainWindowViewModel viewModel = new(loader, SensorLoadLimits.Default);

        Task olderLoad =
            viewModel.LoadLocalPathAsync(FixturePath("normal.json"));
        await loader.FirstCompletionHeld.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        try
        {
            await viewModel.LoadLocalPathAsync(FixturePath("hotplug-after.json"));

            Assert.Equal("hotplug-after.json", viewModel.SourceName);
            Assert.Equal(
                ["/fixture/hotplug/0/temperature/0"],
                SensorIds(viewModel.Roots));
        }
        finally
        {
            loader.ReleaseFirstCompletion();
        }

        await olderLoad.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal("hotplug-after.json", viewModel.SourceName);
        Assert.Equal(1, viewModel.SensorCount);
        Assert.Equal(
            ["/fixture/hotplug/0/temperature/0"],
            SensorIds(viewModel.Roots));
    }

    [Fact]
    public async Task GeneratedNumericIds_AreNeverProjectedAsUiIdentity()
    {
        string normalFixture = FixturePath("normal.json");
        string fixtureText = await File.ReadAllTextAsync(
            normalFixture,
            TestContext.Current.CancellationToken);
        Assert.Contains("\"id\": 12", fixtureText, StringComparison.Ordinal);

        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.LoadLocalPathAsync(normalFixture);

        SensorNodeViewModel[] nodes = Flatten(viewModel.Roots).ToArray();
        Assert.DoesNotContain(
            nodes,
            node => Enumerable.Range(0, 13)
                .Select(value => value.ToString())
                .Contains(node.StableId, StringComparer.Ordinal));
        Assert.All(
            nodes.Where(node => node.Kind == SensorNodeKind.Group),
            node => Assert.Equal("Unavailable", node.StableId));
        Assert.Equal(
            [
                "/golden/0",
                "/golden/0/voltage/3",
                "/golden/0/temperature/0",
                "/golden/0/load/0",
                "/golden/0/fan/0",
                "/golden/0/throughput/0"
            ],
            nodes
                .Where(node => node.Kind is SensorNodeKind.Hardware or SensorNodeKind.Sensor)
                .Select(node => node.StableId)
                .ToArray());
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            new BoundedDataJsonFixtureLoader(),
            SensorLoadLimits.Default);
    }

    private static SensorNodeViewModel FindSensor(
        IEnumerable<SensorNodeViewModel> roots,
        string sensorId)
    {
        return Assert.Single(
            Flatten(roots),
            node => node.Kind == SensorNodeKind.Sensor &&
                node.StableId == sensorId);
    }

    private static string[] SensorIds(
        IEnumerable<SensorNodeViewModel> roots)
    {
        return Flatten(roots)
            .Where(node => node.Kind == SensorNodeKind.Sensor)
            .Select(node => node.StableId)
            .ToArray();
    }

    private static IEnumerable<SensorNodeViewModel> Flatten(
        IEnumerable<SensorNodeViewModel> nodes)
    {
        foreach (SensorNodeViewModel node in nodes)
        {
            yield return node;

            foreach (SensorNodeViewModel child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }

    private sealed class DelayedFirstCompletionFixtureLoader :
        ISensorFixtureLoader
    {
        private readonly ISensorFixtureLoader _inner;
        private readonly TaskCompletionSource _firstCompletionHeld =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public DelayedFirstCompletionFixtureLoader(
            ISensorFixtureLoader inner)
        {
            _inner = inner;
        }

        public Task FirstCompletionHeld => _firstCompletionHeld.Task;

        public Task<SensorLoadResult> LoadAsync(
            Stream stream,
            string sourceName,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            return _inner.LoadAsync(
                stream,
                sourceName,
                limits,
                cancellationToken);
        }

        public async Task<SensorLoadResult> LoadFileAsync(
            string filePath,
            SensorLoadLimits limits,
            CancellationToken cancellationToken = default)
        {
            int callNumber = Interlocked.Increment(ref _callCount);
            SensorLoadResult result = await _inner.LoadFileAsync(
                filePath,
                limits,
                cancellationToken);

            if (callNumber == 1)
            {
                _firstCompletionHeld.TrySetResult();
                await _releaseFirstCompletion.Task;
            }

            return result;
        }

        public void ReleaseFirstCompletion()
        {
            _releaseFirstCompletion.TrySetResult();
        }
    }
}
