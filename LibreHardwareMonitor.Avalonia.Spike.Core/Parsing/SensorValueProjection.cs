using System.Globalization;
using System.Text.Json;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

namespace LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;

internal static class SensorValueProjection
{
    public static void ValidateRawValues(JsonElement node)
    {
        ValidateRawValue(node, "RawMin");
        ValidateRawValue(node, "RawValue");
        ValidateRawValue(node, "RawMax");
    }

    public static SensorValueSnapshot Project(
        JsonElement node,
        string rawPropertyName,
        string displayPropertyName)
    {
        string? display = ReadOptionalDisplay(node, displayPropertyName);

        if (!node.TryGetProperty(rawPropertyName, out JsonElement rawElement) ||
            rawElement.ValueKind == JsonValueKind.Null)
        {
            return new SensorValueSnapshot(null, "Unavailable");
        }

        if (rawElement.ValueKind != JsonValueKind.Number ||
            !rawElement.TryGetDouble(out double raw) ||
            !double.IsFinite(raw))
        {
            throw InvalidShape();
        }

        string effectiveDisplay = string.IsNullOrEmpty(display)
            ? raw.ToString("G17", CultureInfo.InvariantCulture)
            : display;

        return new SensorValueSnapshot(raw, effectiveDisplay);
    }

    public static SensorValueSnapshot Unavailable(
        JsonElement node,
        string displayPropertyName)
    {
        _ = ReadOptionalDisplay(node, displayPropertyName);
        return new SensorValueSnapshot(null, "Unavailable");
    }

    private static string? ReadOptionalDisplay(
        JsonElement node,
        string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out JsonElement displayElement) ||
            displayElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (displayElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidShape();
        }

        return displayElement.GetString();
    }

    private static void ValidateRawValue(
        JsonElement node,
        string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out JsonElement rawElement) ||
            rawElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (rawElement.ValueKind != JsonValueKind.Number ||
            !rawElement.TryGetDouble(out double raw) ||
            !double.IsFinite(raw))
        {
            throw InvalidShape();
        }
    }

    private static SensorProjectionException InvalidShape()
    {
        return new SensorProjectionException(
            SensorLoadErrorCode.InvalidShape,
            "The fixture does not match the expected data.json shape.");
    }
}
