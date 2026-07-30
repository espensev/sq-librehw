using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

namespace LibreHardwareMonitor.Avalonia.Spike.ViewModels;

public sealed class SensorNodeViewModel
{
    private const string Unavailable = "Unavailable";

    public SensorNodeViewModel(SensorNodeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Name = string.IsNullOrWhiteSpace(snapshot.Name) ? "(unnamed)" : snapshot.Name;
        Kind = snapshot.Kind;
        SensorType = string.IsNullOrWhiteSpace(snapshot.SensorType) ? "Unknown" : snapshot.SensorType;
        StableId = string.IsNullOrWhiteSpace(snapshot.StableId) ? Unavailable : snapshot.StableId;
        Current = FormatValue(snapshot.Current);
        Minimum = FormatValue(snapshot.Minimum);
        Maximum = FormatValue(snapshot.Maximum);
        Children = Array.AsReadOnly(
            snapshot.Children
                .Select(child => new SensorNodeViewModel(child))
                .ToArray());

        AccessibleSummary =
            $"{Kind} {Name}; type {SensorType}; current {Current}; minimum {Minimum}; " +
            $"maximum {Maximum}; stable ID {StableId}.";
        NameAutomationName = $"Name: {Name}";
        TypeAutomationName = $"Type: {SensorType}";
        CurrentAutomationName = $"Current: {Current}";
        MinimumAutomationName = $"Minimum: {Minimum}";
        MaximumAutomationName = $"Maximum: {Maximum}";
        StableIdAutomationName = $"Stable ID: {StableId}";
    }

    public string Name { get; }

    public SensorNodeKind Kind { get; }

    public string SensorType { get; }

    public string StableId { get; }

    public string Current { get; }

    public string Minimum { get; }

    public string Maximum { get; }

    public IReadOnlyList<SensorNodeViewModel> Children { get; }

    public string AccessibleSummary { get; }

    public string NameAutomationName { get; }

    public string TypeAutomationName { get; }

    public string CurrentAutomationName { get; }

    public string MinimumAutomationName { get; }

    public string MaximumAutomationName { get; }

    public string StableIdAutomationName { get; }

    private static string FormatValue(SensorValueSnapshot value)
    {
        if (!value.IsAvailable)
        {
            return Unavailable;
        }

        string normalizedDisplay = value.Display.Trim();
        return normalizedDisplay.Length == 0 ||
            normalizedDisplay.Equals("NaN", StringComparison.OrdinalIgnoreCase) ||
            normalizedDisplay.Contains("Infinity", StringComparison.OrdinalIgnoreCase) ||
            normalizedDisplay == "-"
                ? Unavailable
                : value.Display;
    }
}
