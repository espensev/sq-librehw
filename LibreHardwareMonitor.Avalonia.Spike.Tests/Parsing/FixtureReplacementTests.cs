using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;
using Xunit;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.Parsing;

public sealed class FixtureReplacementTests
{
    [Fact]
    public async Task HotplugReplacement_ProducesIndependentImmutableSnapshots()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorSnapshot before = AssertSuccess(
            await loader.LoadFileAsync(
                FixturePath("hotplug-before.json"),
                SensorLoadLimits.Default,
                TestContext.Current.CancellationToken));
        string[] beforeIds = SensorIds(before);

        SensorSnapshot after = AssertSuccess(
            await loader.LoadFileAsync(
                FixturePath("hotplug-after.json"),
                SensorLoadLimits.Default,
                TestContext.Current.CancellationToken));
        string[] afterIds = SensorIds(after);

        Assert.Equal(
            [
                "/fixture/hotplug/0/temperature/0",
                "/fixture/hotplug/0/fan/0"
            ],
            beforeIds);
        Assert.Equal(["/fixture/hotplug/0/temperature/0"], afterIds);
        Assert.Equal(2, before.SensorCount);
        Assert.Equal(1, after.SensorCount);
        Assert.NotSame(before, after);

        Assert.Equal(beforeIds, SensorIds(before));
        Assert.Equal(47, FindSensor(before, "/fixture/hotplug/0/temperature/0").Current.Raw);
        Assert.Equal(48, FindSensor(after, "/fixture/hotplug/0/temperature/0").Current.Raw);
        Assert.NotNull(FindSensor(before, "/fixture/hotplug/0/fan/0"));
        Assert.DoesNotContain(
            Flatten(after.RootNodes),
            node => node.SensorId == "/fixture/hotplug/0/fan/0");
    }

    [Fact]
    public async Task MalformedReplacement_DoesNotReplacePreviousSuccess()
    {
        BoundedDataJsonFixtureLoader loader = new();
        SensorSnapshot? visibleSnapshot = null;

        SensorLoadResult accepted = await loader.LoadFileAsync(
            FixturePath("normal.json"),
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);
        ApplySuccessfulReplacement(ref visibleSnapshot, accepted);
        SensorSnapshot original = Assert.IsType<SensorSnapshot>(visibleSnapshot);
        string[] originalIds = SensorIds(original);

        SensorLoadResult rejected = await loader.LoadFileAsync(
            FixturePath("malformed.json"),
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);
        ApplySuccessfulReplacement(ref visibleSnapshot, rejected);

        AssertFailure(rejected, SensorLoadErrorCode.InvalidJson);
        Assert.Same(original, visibleSnapshot);
        Assert.Equal(originalIds, SensorIds(Assert.IsType<SensorSnapshot>(visibleSnapshot)));
    }

    [Fact]
    public async Task LimitFailure_DoesNotPublishPartialProjectedTree()
    {
        BoundedDataJsonFixtureLoader loader = new();
        SensorSnapshot? visibleSnapshot = null;

        SensorLoadResult accepted = await loader.LoadFileAsync(
            FixturePath("hotplug-before.json"),
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);
        ApplySuccessfulReplacement(ref visibleSnapshot, accepted);
        SensorSnapshot original = Assert.IsType<SensorSnapshot>(visibleSnapshot);

        SensorLoadLimits oneNodeLimit = new(
            MaxInputBytes: 16_384,
            MaxDepth: 32,
            MaxNodes: 1,
            MaxChildrenPerNode: 64,
            MaxStringCharacters: 1_024);
        SensorLoadResult rejected = await LoadJsonAsync(
            loader,
            GeneratedLimitCases.CreateNodeDocument(2),
            oneNodeLimit);
        ApplySuccessfulReplacement(ref visibleSnapshot, rejected);

        AssertFailure(rejected, SensorLoadErrorCode.ExcessiveNodes);
        Assert.Same(original, visibleSnapshot);
        Assert.Equal(2, original.SensorCount);
        Assert.Equal(
            "/fixture/hotplug/0/fan/0",
            FindSensor(original, "/fixture/hotplug/0/fan/0").SensorId);
    }

    [Fact]
    public async Task NewerLoad_SupersedesOlderInFlightLoad()
    {
        BoundedDataJsonFixtureLoader loader = new();
        using GeneratedLimitCases.BlockingReadStream blockedStream = new();

        Task<SensorLoadResult> olderLoad = loader.LoadAsync(
            blockedStream,
            "older.json",
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);
        await blockedStream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        SensorLoadResult newerResult = await LoadJsonAsync(
            loader,
            GeneratedLimitCases.CreateSensorDocument("/fixture/newer/0"));
        SensorLoadResult olderResult = await olderLoad.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        SensorSnapshot newerSnapshot = AssertSuccess(newerResult);
        Assert.Equal("/fixture/newer/0", Assert.Single(newerSnapshot.RootNodes).SensorId);
        AssertFailure(olderResult, SensorLoadErrorCode.SupersededOrCancelled);
    }

    private static void ApplySuccessfulReplacement(
        ref SensorSnapshot? visibleSnapshot,
        SensorLoadResult result)
    {
        if (result.IsSuccess)
        {
            visibleSnapshot = Assert.IsType<SensorSnapshot>(result.Snapshot);
        }
    }

    private static async Task<SensorLoadResult> LoadJsonAsync(
        BoundedDataJsonFixtureLoader loader,
        string json,
        SensorLoadLimits? limits = null)
    {
        using MemoryStream stream = new(GeneratedLimitCases.Utf8(json), writable: false);
        return await loader.LoadAsync(
            stream,
            "generated.json",
            limits ?? SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);
    }

    private static SensorSnapshot AssertSuccess(SensorLoadResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        return Assert.IsType<SensorSnapshot>(result.Snapshot);
    }

    private static SensorLoadError AssertFailure(
        SensorLoadResult result,
        SensorLoadErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Snapshot);
        SensorLoadError error = Assert.IsType<SensorLoadError>(result.Error);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }

    private static SensorNodeSnapshot FindSensor(
        SensorSnapshot snapshot,
        string sensorId)
    {
        return Assert.Single(
            Flatten(snapshot.RootNodes),
            node => node.SensorId == sensorId);
    }

    private static string[] SensorIds(SensorSnapshot snapshot)
    {
        return Flatten(snapshot.RootNodes)
            .Where(node => node.SensorId is not null)
            .Select(node => node.SensorId!)
            .ToArray();
    }

    private static IEnumerable<SensorNodeSnapshot> Flatten(
        IEnumerable<SensorNodeSnapshot> nodes)
    {
        foreach (SensorNodeSnapshot node in nodes)
        {
            yield return node;

            foreach (SensorNodeSnapshot child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }
}
