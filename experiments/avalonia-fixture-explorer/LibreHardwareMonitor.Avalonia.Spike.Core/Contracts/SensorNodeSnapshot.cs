using System.Collections.Immutable;

namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public enum SensorNodeKind
{
    Group,
    Hardware,
    Sensor
}

public sealed record SensorValueSnapshot
{
    public SensorValueSnapshot(double? Raw, string Display)
    {
        ArgumentNullException.ThrowIfNull(Display);

        this.Raw = Raw;
        this.Display = Display;
    }

    public double? Raw { get; }

    public string Display { get; }

    public bool IsAvailable => Raw is double raw && double.IsFinite(raw);
}

public sealed record SensorNodeSnapshot
{
    public SensorNodeSnapshot(
        SensorNodeKind Kind,
        string Name,
        string? SensorId,
        string? HardwareId,
        string? SensorType,
        string? ImageUrl,
        SensorValueSnapshot Minimum,
        SensorValueSnapshot Current,
        SensorValueSnapshot Maximum,
        ImmutableArray<SensorNodeSnapshot> Children)
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind));
        }

        ArgumentNullException.ThrowIfNull(Name);
        ArgumentNullException.ThrowIfNull(Minimum);
        ArgumentNullException.ThrowIfNull(Current);
        ArgumentNullException.ThrowIfNull(Maximum);

        if (Children.IsDefault)
        {
            throw new ArgumentException("Children must be initialized.", nameof(Children));
        }

        this.Kind = Kind;
        this.Name = Name;
        this.SensorId = SensorId;
        this.HardwareId = HardwareId;
        this.SensorType = SensorType;
        this.ImageUrl = ImageUrl;
        this.Minimum = Minimum;
        this.Current = Current;
        this.Maximum = Maximum;
        this.Children = Children;
    }

    public SensorNodeKind Kind { get; }

    public string Name { get; }

    public string? SensorId { get; }

    public string? HardwareId { get; }

    public string? SensorType { get; }

    public string? ImageUrl { get; }

    public SensorValueSnapshot Minimum { get; }

    public SensorValueSnapshot Current { get; }

    public SensorValueSnapshot Maximum { get; }

    public ImmutableArray<SensorNodeSnapshot> Children { get; }

    public string? StableId => SensorId ?? HardwareId;
}
