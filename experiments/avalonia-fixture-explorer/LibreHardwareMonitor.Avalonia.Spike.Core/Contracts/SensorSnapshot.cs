using System.Collections.Immutable;

namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public sealed record SensorSnapshot
{
    public SensorSnapshot(
        string SourceName,
        string? Version,
        ImmutableArray<SensorNodeSnapshot> RootNodes,
        int TotalNodeCount,
        int SensorCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceName);

        if (RootNodes.IsDefault)
        {
            throw new ArgumentException("Root nodes must be initialized.", nameof(RootNodes));
        }

        if (TotalNodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TotalNodeCount));
        }

        if (SensorCount < 0 || SensorCount > TotalNodeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(SensorCount));
        }

        if (RootNodes.Length > TotalNodeCount)
        {
            throw new ArgumentException(
                "The total node count cannot be smaller than the root node count.",
                nameof(TotalNodeCount));
        }

        this.SourceName = SourceName;
        this.Version = Version;
        this.RootNodes = RootNodes;
        this.TotalNodeCount = TotalNodeCount;
        this.SensorCount = SensorCount;
    }

    public string SourceName { get; }

    public string? Version { get; }

    public ImmutableArray<SensorNodeSnapshot> RootNodes { get; }

    public int TotalNodeCount { get; }

    public int SensorCount { get; }
}
