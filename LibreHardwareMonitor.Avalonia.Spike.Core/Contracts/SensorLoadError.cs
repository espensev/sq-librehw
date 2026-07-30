namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public enum SensorLoadErrorCode
{
    FileNotFound,
    AccessDenied,
    IoFailure,
    InputTooLarge,
    InvalidJson,
    InvalidShape,
    ExcessiveDepth,
    ExcessiveNodes,
    ExcessiveChildren,
    ExcessiveStringLength,
    MissingSensorId,
    DuplicateSensorId,
    SupersededOrCancelled
}

public sealed record SensorLoadError
{
    public SensorLoadError(SensorLoadErrorCode Code, string Message)
    {
        if (!Enum.IsDefined(Code))
        {
            throw new ArgumentOutOfRangeException(nameof(Code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Message);

        this.Code = Code;
        this.Message = Message;
    }

    public SensorLoadErrorCode Code { get; }

    public string Message { get; }
}
