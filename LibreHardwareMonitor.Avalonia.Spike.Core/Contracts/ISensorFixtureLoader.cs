namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public interface ISensorFixtureLoader
{
    Task<SensorLoadResult> LoadAsync(
        Stream stream,
        string sourceName,
        SensorLoadLimits limits,
        CancellationToken cancellationToken = default);

    Task<SensorLoadResult> LoadFileAsync(
        string filePath,
        SensorLoadLimits limits,
        CancellationToken cancellationToken = default);
}
