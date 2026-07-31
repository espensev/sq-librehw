namespace LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

public sealed record SensorLoadLimits
{
    private long _maxInputBytes;
    private int _maxDepth;
    private int _maxNodes;
    private int _maxChildrenPerNode;
    private int _maxStringCharacters;

    public SensorLoadLimits(
        long MaxInputBytes,
        int MaxDepth,
        int MaxNodes,
        int MaxChildrenPerNode,
        int MaxStringCharacters)
    {
        this.MaxInputBytes = MaxInputBytes;
        this.MaxDepth = MaxDepth;
        this.MaxNodes = MaxNodes;
        this.MaxChildrenPerNode = MaxChildrenPerNode;
        this.MaxStringCharacters = MaxStringCharacters;
    }

    public static SensorLoadLimits Default { get; } = new(
        4L * 1024 * 1024,
        32,
        16_384,
        4_096,
        1_024);

    public long MaxInputBytes
    {
        get => _maxInputBytes;
        init => _maxInputBytes = EnsurePositive(value, nameof(MaxInputBytes));
    }

    public int MaxDepth
    {
        get => _maxDepth;
        init => _maxDepth = EnsurePositive(value, nameof(MaxDepth));
    }

    public int MaxNodes
    {
        get => _maxNodes;
        init => _maxNodes = EnsurePositive(value, nameof(MaxNodes));
    }

    public int MaxChildrenPerNode
    {
        get => _maxChildrenPerNode;
        init => _maxChildrenPerNode = EnsurePositive(value, nameof(MaxChildrenPerNode));
    }

    public int MaxStringCharacters
    {
        get => _maxStringCharacters;
        init => _maxStringCharacters = EnsurePositive(value, nameof(MaxStringCharacters));
    }

    private static long EnsurePositive(long value, string parameterName)
    {
        return value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static int EnsurePositive(int value, string parameterName)
    {
        return value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);
    }
}
