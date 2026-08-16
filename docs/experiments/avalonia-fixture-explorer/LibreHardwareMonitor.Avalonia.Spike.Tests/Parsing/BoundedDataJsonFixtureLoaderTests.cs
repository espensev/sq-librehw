using System.Text;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;
using LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;
using Xunit;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.Parsing;

public sealed class BoundedDataJsonFixtureLoaderTests
{
    [Fact]
    public async Task LoadFileAsync_NormalFixture_PreservesSourceVersionCountsAndOrder()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await loader.LoadFileAsync(
            FixturePath("normal.json"),
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        SensorSnapshot snapshot = AssertSuccess(result);
        Assert.Equal("normal.json", snapshot.SourceName);
        Assert.Equal("0.9.6", snapshot.Version);
        Assert.Equal(12, snapshot.TotalNodeCount);
        Assert.Equal(5, snapshot.SensorCount);

        SensorNodeSnapshot computer = Assert.Single(snapshot.RootNodes);
        Assert.Equal(SensorNodeKind.Group, computer.Kind);
        Assert.Equal("GOLDEN-PC", computer.Name);

        SensorNodeSnapshot hardware = Assert.Single(computer.Children);
        Assert.Equal(SensorNodeKind.Hardware, hardware.Kind);
        Assert.Equal("/golden/0", hardware.HardwareId);
        Assert.Equal("images_icon/cpu.png", hardware.ImageUrl);
        Assert.Equal(
            ["Voltages", "Temperatures", "Load", "Fans", "Throughput"],
            hardware.Children.Select(node => node.Name).ToArray());
        Assert.Equal(
            [
                "/golden/0/voltage/3",
                "/golden/0/temperature/0",
                "/golden/0/load/0",
                "/golden/0/fan/0",
                "/golden/0/throughput/0"
            ],
            hardware.Children
                .SelectMany(group => group.Children)
                .Select(sensor => sensor.SensorId!)
                .ToArray());
        Assert.Equal(
            "Fan \"Æøå\" #1",
            hardware.Children[3].Children[0].Name);
        Assert.Equal(
            "Fan",
            hardware.Children[3].Children[0].SensorType);
    }

    [Fact]
    public async Task LoadAsync_NormalFixture_UsesSuppliedStreamSourceName()
    {
        BoundedDataJsonFixtureLoader loader = new();
        await using FileStream stream = File.OpenRead(FixturePath("normal.json"));

        SensorLoadResult result = await loader.LoadAsync(
            stream,
            "recorded-normal",
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        SensorSnapshot snapshot = AssertSuccess(result);
        Assert.Equal("recorded-normal", snapshot.SourceName);
        Assert.Equal("0.9.6", snapshot.Version);
        Assert.Equal(12, snapshot.TotalNodeCount);
        Assert.Equal(5, snapshot.SensorCount);
    }

    [Fact]
    public async Task FiniteAndUnavailableValues_FollowRawValueSemantics()
    {
        BoundedDataJsonFixtureLoader loader = new();
        SensorSnapshot snapshot = AssertSuccess(
            await loader.LoadFileAsync(
                FixturePath("normal.json"),
                SensorLoadLimits.Default,
                TestContext.Current.CancellationToken));

        SensorNodeSnapshot hardware = snapshot.RootNodes[0].Children[0];
        SensorNodeSnapshot voltage = hardware.Children[0].Children[0];
        SensorNodeSnapshot temperature = hardware.Children[1].Children[0];
        SensorNodeSnapshot load = hardware.Children[2].Children[0];
        SensorNodeSnapshot throughput = hardware.Children[4].Children[0];

        Assert.Null(voltage.Current.Raw);
        Assert.Equal("Unavailable", voltage.Current.Display);

        Assert.Equal(49.5, temperature.Current.Raw);
        Assert.Equal("49.5 °C", temperature.Current.Display);
        Assert.Equal(30.25, temperature.Minimum.Raw);
        Assert.Equal("30.2 °C", temperature.Minimum.Display);

        Assert.Equal(0, load.Minimum.Raw);
        Assert.Equal("0.0 %", load.Minimum.Display);
        Assert.Null(load.Current.Raw);
        Assert.Equal("Unavailable", load.Current.Display);
        Assert.Null(load.Maximum.Raw);
        Assert.Equal("Unavailable", load.Maximum.Display);

        Assert.Null(throughput.Minimum.Raw);
        Assert.Equal("Unavailable", throughput.Minimum.Display);
        Assert.Equal(2_621_440, throughput.Current.Raw);
        Assert.Equal("2.5 MB/s", throughput.Current.Display);
    }

    [Fact]
    public async Task MissingLabelsTypeAndRawValues_UseHonestFallbacks()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorSnapshot snapshot = AssertSuccess(
            await loader.LoadFileAsync(
                FixturePath("unavailable.json"),
                SensorLoadLimits.Default,
                TestContext.Current.CancellationToken));

        Assert.Equal(4, snapshot.TotalNodeCount);
        Assert.Equal(1, snapshot.SensorCount);

        SensorNodeSnapshot group = snapshot.RootNodes[0];
        SensorNodeSnapshot hardware = group.Children[0];
        SensorNodeSnapshot sensor = hardware.Children[0].Children[0];

        Assert.Equal("(unnamed)", group.Name);
        Assert.Equal("(unnamed)", hardware.Name);
        Assert.Equal("(unnamed)", sensor.Name);
        Assert.Equal("Unknown", sensor.SensorType);
        Assert.Equal("/fixture/unavailable/0/temperature/0", sensor.SensorId);
        Assert.All(
            new[] { sensor.Minimum, sensor.Current, sensor.Maximum },
            value =>
            {
                Assert.False(value.IsAvailable);
                Assert.Null(value.Raw);
                Assert.Equal("Unavailable", value.Display);
            });
    }

    [Fact]
    public async Task EmptySensorType_UsesUnknownFallback()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorSnapshot snapshot = AssertSuccess(
            await LoadJsonAsync(
                loader,
                """
                {
                  "Children": [
                    {
                      "SensorId": "/sensor/empty-type",
                      "Type": "",
                      "Children": []
                    }
                  ]
                }
                """));

        Assert.Equal("Unknown", Assert.Single(snapshot.RootNodes).SensorType);
    }

    [Fact]
    public async Task GeneratedNumericIds_AreIgnoredForIdentityAndOrder()
    {
        BoundedDataJsonFixtureLoader loader = new();
        string json = GeneratedLimitCases.CreateSensorDocument(
            "/fixture/sensor/z",
            "/fixture/sensor/a");

        SensorSnapshot snapshot = AssertSuccess(
            await LoadJsonAsync(loader, json));

        Assert.Equal(
            ["/fixture/sensor/z", "/fixture/sensor/a"],
            snapshot.RootNodes.Select(node => node.StableId!).ToArray());
        Assert.Equal(["Sensor 0", "Sensor 1"], snapshot.RootNodes.Select(node => node.Name).ToArray());
    }

    [Theory]
    [InlineData("malformed.json", SensorLoadErrorCode.InvalidJson)]
    [InlineData("oversized-string.json", SensorLoadErrorCode.ExcessiveStringLength)]
    public async Task InvalidRecordedFixtures_ReturnTypedFailure(
        string fixtureName,
        SensorLoadErrorCode expectedCode)
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await loader.LoadFileAsync(
            FixturePath(fixtureName),
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        AssertFailure(result, expectedCode);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Children":{}}""")]
    [InlineData("""{"Children":[0]}""")]
    [InlineData("""{"Children":[{"Text":"missing children"}]}""")]
    [InlineData("""{"Children":[{"Text":42,"Children":[]}]}""")]
    [InlineData("""{"Children":[{"HardwareId":"","Children":[]}]}""")]
    [InlineData("""{"Children":[{"SensorId":42,"Children":[]}]}""")]
    [InlineData("""{"Children":[{"SensorId":"/sensor/0","Type":42,"Children":[]}]}""")]
    [InlineData("""{"Children":[{"SensorId":"/sensor/0","RawValue":"1","Children":[]}]}""")]
    [InlineData("""{"Children":[{"SensorId":"/sensor/0","RawValue":1e400,"Children":[]}]}""")]
    [InlineData("""{"Children":[{"RawValue":"1","Children":[]}]}""")]
    [InlineData("""{"Children":[{"RawMin":true,"Children":[]}]}""")]
    [InlineData("""{"Children":[{"HardwareId":"/hardware/0","RawMax":{},"Children":[]}]}""")]
    [InlineData("""{"Children":[{"HardwareId":"/hardware/0","RawValue":1e400,"Children":[]}]}""")]
    public async Task WrongShapes_AreRejected(string json)
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await LoadJsonAsync(loader, json);

        AssertFailure(result, SensorLoadErrorCode.InvalidShape);
    }

    [Theory]
    [InlineData("""{"Children":[],}""")]
    [InlineData("{\"Children\":[]// comment\n}")]
    [InlineData("""{"Children":[]/* comment */}""")]
    public async Task CommentsAndTrailingCommas_AreRejected(string json)
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await LoadJsonAsync(loader, json);

        AssertFailure(result, SensorLoadErrorCode.InvalidJson);
    }

    [Fact]
    public async Task EmptySensorId_IsRejected()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await LoadJsonAsync(
            loader,
            GeneratedLimitCases.CreateSensorDocument(string.Empty));

        AssertFailure(result, SensorLoadErrorCode.MissingSensorId);
    }

    [Fact]
    public async Task DuplicateSensorId_IsRejectedCaseSensitively()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult duplicate = await LoadJsonAsync(
            loader,
            GeneratedLimitCases.CreateSensorDocument("/sensor/A", "/sensor/A"));
        AssertFailure(duplicate, SensorLoadErrorCode.DuplicateSensorId);

        SensorSnapshot caseDistinct = AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateSensorDocument("/sensor/A", "/sensor/a")));
        Assert.Equal(2, caseDistinct.SensorCount);
    }

    [Fact]
    public async Task ByteLimit_AllowsExactBoundAndRejectsBoundPlusOne()
    {
        const int maximumBytes = 128;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: maximumBytes);
        string exact = GeneratedLimitCases.CreateExactByteDocument(maximumBytes);
        string excessive = GeneratedLimitCases.CreateExactByteDocument(maximumBytes + 1);

        Assert.Equal(maximumBytes, Encoding.UTF8.GetByteCount(exact));
        AssertSuccess(await LoadJsonAsync(loader, exact, limits));
        AssertFailure(
            await LoadJsonAsync(loader, excessive, limits),
            SensorLoadErrorCode.InputTooLarge);
    }

    [Fact]
    public async Task ByteLimit_RejectsBeforeInvalidJsonParsing()
    {
        const int maximumBytes = 128;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: maximumBytes);
        string invalidOversizedJson = new('x', maximumBytes + 1);

        SensorLoadResult result = await LoadJsonAsync(
            loader,
            invalidOversizedJson,
            limits);

        AssertFailure(result, SensorLoadErrorCode.InputTooLarge);
    }

    [Fact]
    public async Task LongMaximumByteLimit_DoesNotOverflowProbe()
    {
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: long.MaxValue);

        SensorLoadResult result = await LoadJsonAsync(
            loader,
            """{"Children":[]}""",
            limits);

        AssertSuccess(result);
    }

    [Fact]
    public async Task NonSeekableStream_ReadsOnlyBoundPlusOneBeforeRejecting()
    {
        const int maximumBytes = 128;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: maximumBytes);
        byte[] payload = GeneratedLimitCases.Utf8(
            GeneratedLimitCases.CreateExactByteDocument(maximumBytes + 64));
        using GeneratedLimitCases.NonSeekableReadStream stream = new(payload);

        SensorLoadResult result = await loader.LoadAsync(
            stream,
            "non-seekable",
            limits,
            TestContext.Current.CancellationToken);

        AssertFailure(result, SensorLoadErrorCode.InputTooLarge);
        Assert.Equal(maximumBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task NonSeekableStream_AllowsExactByteBound()
    {
        const int maximumBytes = 128;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: maximumBytes);
        byte[] payload = GeneratedLimitCases.Utf8(
            GeneratedLimitCases.CreateExactByteDocument(maximumBytes));
        using GeneratedLimitCases.NonSeekableReadStream stream = new(payload);

        SensorLoadResult result = await loader.LoadAsync(
            stream,
            "non-seekable-exact",
            limits,
            TestContext.Current.CancellationToken);

        AssertSuccess(result);
        Assert.Equal(maximumBytes, stream.BytesRead);
    }

    [Fact]
    public async Task DepthLimit_AllowsExactBoundAndRejectsBoundPlusOne()
    {
        const int maximumDepth = 6;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxDepth: maximumDepth);

        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateDepthDocument(maximumDepth),
                limits));
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateDepthDocument(maximumDepth + 1),
                limits),
            SensorLoadErrorCode.ExcessiveDepth);
    }

    [Fact]
    public async Task NodeLimit_AllowsExactBoundAndRejectsBoundPlusOne()
    {
        const int maximumNodes = 3;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(
            maxNodes: maximumNodes,
            maxChildrenPerNode: maximumNodes + 1);

        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNodeDocument(maximumNodes),
                limits));
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNodeDocument(maximumNodes + 1),
                limits),
            SensorLoadErrorCode.ExcessiveNodes);
    }

    [Fact]
    public async Task ChildLimit_AllowsExactBoundAndRejectsBoundPlusOne()
    {
        const int maximumChildren = 3;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(
            maxNodes: maximumChildren + 1,
            maxChildrenPerNode: maximumChildren);

        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNodeDocument(maximumChildren),
                limits));
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNodeDocument(maximumChildren + 1),
                limits),
            SensorLoadErrorCode.ExcessiveChildren);
    }

    [Fact]
    public async Task ChildLimit_IsEnforcedOnNestedProjectedNodes()
    {
        const int maximumChildren = 3;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(
            maxNodes: maximumChildren + 2,
            maxChildrenPerNode: maximumChildren);

        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNestedChildDocument(maximumChildren),
                limits));
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateNestedChildDocument(maximumChildren + 1),
                limits),
            SensorLoadErrorCode.ExcessiveChildren);
    }

    [Fact]
    public async Task StringLimit_AppliesToValuesAndUnknownPropertyNames()
    {
        const int maximumCharacters = 16;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxStringCharacters: maximumCharacters);

        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateStringValueDocument(maximumCharacters),
                limits));
        AssertSuccess(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreatePropertyNameDocument(maximumCharacters),
                limits));
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreateStringValueDocument(maximumCharacters + 1),
                limits),
            SensorLoadErrorCode.ExcessiveStringLength);
        AssertFailure(
            await LoadJsonAsync(
                loader,
                GeneratedLimitCases.CreatePropertyNameDocument(maximumCharacters + 1),
                limits),
            SensorLoadErrorCode.ExcessiveStringLength);
    }

    [Fact]
    public async Task FileLengthOverBound_IsRejectedBeforeParsing()
    {
        string fixturePath = FixturePath("normal.json");
        long fixtureLength = new FileInfo(fixturePath).Length;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: fixtureLength - 1);

        SensorLoadResult result = await loader.LoadFileAsync(
            fixturePath,
            limits,
            TestContext.Current.CancellationToken);

        AssertFailure(result, SensorLoadErrorCode.InputTooLarge);
    }

    [Fact]
    public async Task FileLengthAtExactBound_IsAccepted()
    {
        string fixturePath = FixturePath("normal.json");
        long fixtureLength = new FileInfo(fixturePath).Length;
        BoundedDataJsonFixtureLoader loader = new();
        SensorLoadLimits limits = Limits(maxInputBytes: fixtureLength);

        SensorLoadResult result = await loader.LoadFileAsync(
            fixturePath,
            limits,
            TestContext.Current.CancellationToken);

        AssertSuccess(result);
    }

    [Fact]
    public async Task MissingFile_ReturnsOperatorSafeFileNotFoundError()
    {
        const string sensitiveLeaf = "operator-private-missing-fixture.json";
        string missingPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            sensitiveLeaf);
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await loader.LoadFileAsync(
            missingPath,
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        SensorLoadError error = AssertFailure(result, SensorLoadErrorCode.FileNotFound);
        Assert.False(
            error.Message.Contains(sensitiveLeaf, StringComparison.Ordinal),
            "The operator message must not echo the selected path.");
    }

    [Fact]
    public async Task InvalidFilePath_ReturnsOperatorSafeIoFailure()
    {
        const string sensitiveSentinel = "operator-private-invalid-fixture";
        string invalidPath = sensitiveSentinel + "\0.json";
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await loader.LoadFileAsync(
            invalidPath,
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        SensorLoadError error = AssertFailure(result, SensorLoadErrorCode.IoFailure);
        Assert.DoesNotContain(sensitiveSentinel, error.Message);
    }

    [Fact]
    public async Task DirectoryPath_ReturnsIoFailure()
    {
        BoundedDataJsonFixtureLoader loader = new();

        SensorLoadResult result = await loader.LoadFileAsync(
            AppContext.BaseDirectory,
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        AssertFailure(result, SensorLoadErrorCode.IoFailure);
    }

    [Fact]
    public async Task InFlightCancellation_ReturnsTypedCancellation()
    {
        BoundedDataJsonFixtureLoader loader = new();
        using GeneratedLimitCases.BlockingReadStream stream = new();
        using CancellationTokenSource cancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        Task<SensorLoadResult> load = loader.LoadAsync(
            stream,
            "blocked",
            SensorLoadLimits.Default,
            cancellationSource.Token);
        await stream.ReadStarted.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        cancellationSource.Cancel();

        SensorLoadResult result = await load.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        AssertFailure(result, SensorLoadErrorCode.SupersededOrCancelled);
    }

    [Fact]
    public async Task StreamIoFailure_DoesNotExposeImplementationDetails()
    {
        BoundedDataJsonFixtureLoader loader = new();
        using GeneratedLimitCases.ThrowingReadStream stream = new();

        SensorLoadResult result = await loader.LoadAsync(
            stream,
            "throwing",
            SensorLoadLimits.Default,
            TestContext.Current.CancellationToken);

        SensorLoadError error = AssertFailure(result, SensorLoadErrorCode.IoFailure);
        Assert.False(
            error.Message.Contains("Sensitive", StringComparison.Ordinal),
            "The operator message must not expose the underlying exception.");
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

    private static SensorLoadLimits Limits(
        long maxInputBytes = 16_384,
        int maxDepth = 32,
        int maxNodes = 64,
        int maxChildrenPerNode = 64,
        int maxStringCharacters = 1_024)
    {
        return new SensorLoadLimits(
            maxInputBytes,
            maxDepth,
            maxNodes,
            maxChildrenPerNode,
            maxStringCharacters);
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }
}
