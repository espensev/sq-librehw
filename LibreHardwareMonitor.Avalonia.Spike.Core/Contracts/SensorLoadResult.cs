namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public sealed record SensorLoadResult
{
    private SensorLoadResult(SensorSnapshot? snapshot, SensorLoadError? error)
    {
        if ((snapshot is null) == (error is null))
        {
            throw new ArgumentException("Exactly one of snapshot or error must be supplied.");
        }

        Snapshot = snapshot;
        Error = error;
    }

    public SensorSnapshot? Snapshot { get; }

    public SensorLoadError? Error { get; }

    public bool IsSuccess => Snapshot is not null;

    public static SensorLoadResult Success(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SensorLoadResult(snapshot, null);
    }

    public static SensorLoadResult Failure(SensorLoadError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SensorLoadResult(null, error);
    }
}
