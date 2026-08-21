using System.Collections.Immutable;
using System.Text.Json;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

namespace LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;

internal static class DataJsonProjection
{
    public static SensorSnapshot Project(
        JsonElement root,
        string sourceName,
        SensorLoadLimits limits,
        CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidShape();
        }

        string? version = ReadOptionalString(root, "Version");
        JsonElement rootChildren = ReadRequiredChildren(root);
        EnsureChildLimit(rootChildren, limits);

        ProjectionContext context = new(limits);
        ImmutableArray<SensorNodeSnapshot>.Builder rootNodes =
            ImmutableArray.CreateBuilder<SensorNodeSnapshot>(rootChildren.GetArrayLength());

        foreach (JsonElement child in rootChildren.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rootNodes.Add(ProjectNode(child, context, cancellationToken));
        }

        return new SensorSnapshot(
            sourceName,
            version,
            rootNodes.MoveToImmutable(),
            context.NodeCount,
            context.SensorCount);
    }

    private static SensorNodeSnapshot ProjectNode(
        JsonElement element,
        ProjectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidShape();
        }

        context.CountNode();

        bool hasSensorId = element.TryGetProperty("SensorId", out JsonElement sensorIdElement);
        bool hasHardwareId = element.TryGetProperty("HardwareId", out JsonElement hardwareIdElement);

        SensorNodeKind kind;
        string? sensorId = null;
        string? hardwareId = null;
        string? sensorType = null;

        if (hasSensorId)
        {
            sensorId = ReadSensorId(sensorIdElement);
            context.CountSensor(sensorId);
            kind = SensorNodeKind.Sensor;

            sensorType = ReadOptionalString(element, "Type");
            if (string.IsNullOrEmpty(sensorType))
            {
                sensorType = "Unknown";
            }
        }
        else if (hasHardwareId)
        {
            hardwareId = ReadHardwareId(hardwareIdElement);
            kind = SensorNodeKind.Hardware;
        }
        else
        {
            kind = SensorNodeKind.Group;
        }

        string? text = ReadOptionalString(element, "Text");
        string name = string.IsNullOrEmpty(text)
            ? "(unnamed)"
            : text;
        string? imageUrl = ReadOptionalString(element, "ImageURL");

        SensorValueSnapshot minimum;
        SensorValueSnapshot current;
        SensorValueSnapshot maximum;

        SensorValueProjection.ValidateRawValues(element);

        if (kind == SensorNodeKind.Sensor)
        {
            minimum = SensorValueProjection.Project(element, "RawMin", "Min");
            current = SensorValueProjection.Project(element, "RawValue", "Value");
            maximum = SensorValueProjection.Project(element, "RawMax", "Max");
        }
        else
        {
            minimum = SensorValueProjection.Unavailable(element, "Min");
            current = SensorValueProjection.Unavailable(element, "Value");
            maximum = SensorValueProjection.Unavailable(element, "Max");
        }

        JsonElement childrenElement = ReadRequiredChildren(element);
        EnsureChildLimit(childrenElement, context.Limits);

        ImmutableArray<SensorNodeSnapshot>.Builder children =
            ImmutableArray.CreateBuilder<SensorNodeSnapshot>(childrenElement.GetArrayLength());
        foreach (JsonElement child in childrenElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            children.Add(ProjectNode(child, context, cancellationToken));
        }

        return new SensorNodeSnapshot(
            kind,
            name,
            sensorId,
            hardwareId,
            sensorType,
            imageUrl,
            minimum,
            current,
            maximum,
            children.MoveToImmutable());
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw InvalidShape();
        }

        return property.GetString();
    }

    private static JsonElement ReadRequiredChildren(JsonElement element)
    {
        if (!element.TryGetProperty("Children", out JsonElement children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            throw InvalidShape();
        }

        return children;
    }

    private static string ReadSensorId(JsonElement sensorIdElement)
    {
        if (sensorIdElement.ValueKind == JsonValueKind.Null)
        {
            throw new SensorProjectionException(
                SensorLoadErrorCode.MissingSensorId,
                "A sensor node is missing a non-empty stable sensor ID.");
        }

        if (sensorIdElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidShape();
        }

        string? sensorId = sensorIdElement.GetString();
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            throw new SensorProjectionException(
                SensorLoadErrorCode.MissingSensorId,
                "A sensor node is missing a non-empty stable sensor ID.");
        }

        return sensorId;
    }

    private static string ReadHardwareId(JsonElement hardwareIdElement)
    {
        if (hardwareIdElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidShape();
        }

        string? hardwareId = hardwareIdElement.GetString();
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            throw InvalidShape();
        }

        return hardwareId;
    }

    private static void EnsureChildLimit(
        JsonElement children,
        SensorLoadLimits limits)
    {
        if (children.GetArrayLength() > limits.MaxChildrenPerNode)
        {
            throw new SensorProjectionException(
                SensorLoadErrorCode.ExcessiveChildren,
                "A fixture node exceeds the configured direct-child limit.");
        }
    }

    private static SensorProjectionException InvalidShape()
    {
        return new SensorProjectionException(
            SensorLoadErrorCode.InvalidShape,
            "The fixture does not match the expected data.json shape.");
    }

    private sealed class ProjectionContext
    {
        private readonly HashSet<string> _sensorIds = new(StringComparer.Ordinal);

        public ProjectionContext(SensorLoadLimits limits)
        {
            Limits = limits;
        }

        public SensorLoadLimits Limits { get; }

        public int NodeCount { get; private set; }

        public int SensorCount { get; private set; }

        public void CountNode()
        {
            if (NodeCount >= Limits.MaxNodes)
            {
                throw new SensorProjectionException(
                    SensorLoadErrorCode.ExcessiveNodes,
                    "The fixture exceeds the configured total-node limit.");
            }

            NodeCount++;
        }

        public void CountSensor(string sensorId)
        {
            if (!_sensorIds.Add(sensorId))
            {
                throw new SensorProjectionException(
                    SensorLoadErrorCode.DuplicateSensorId,
                    "The fixture contains a duplicate stable sensor ID.");
            }

            SensorCount++;
        }
    }
}

internal sealed class SensorProjectionException : Exception
{
    public SensorProjectionException(
        SensorLoadErrorCode code,
        string operatorMessage)
        : base(operatorMessage)
    {
        Code = code;
        OperatorMessage = operatorMessage;
    }

    public SensorLoadErrorCode Code { get; }

    public string OperatorMessage { get; }
}
