namespace LibreHardwareMonitor.Avalonia.Spike.ViewModels;

public sealed record FixtureChoice
{
    public FixtureChoice(string DisplayName, string RelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);

        this.DisplayName = DisplayName;
        this.RelativePath = RelativePath;
    }

    public string DisplayName { get; }

    public string RelativePath { get; }

    public override string ToString()
    {
        return DisplayName;
    }
}
