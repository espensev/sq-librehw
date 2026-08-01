// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class DataJsonProjectionTests
{
    [Fact]
    public void Project_AssignsEnvelopeAndDepthFirstPreorderIdsWithoutMutatingSnapshot()
    {
        SensorNodeSnapshot sensor = Sensor(
            "Core #1",
            "/CPU/Case/temperature/0",
            "Temperature",
            Value(20.5f, "20.5 °C"),
            Value(23.5f, "23.5 °C"),
            Value(30.5f, "30.5 °C"));
        SensorNodeSnapshot type = Group("Temperatures", "images_icon/temperature.png", sensor);
        SensorNodeSnapshot hardware = Hardware("Case CPU", "/CPU/Case", "images_icon/cpu.png", type);
        SensorNodeSnapshot emptyGroup = Group("Empty Group", "images_icon/power.png");
        SensorNodeSnapshot root = Group("CASE-HOST", "images_icon/computer.png", hardware, emptyGroup);
        SensorSnapshot snapshot = new(root);

        SensorNodeSnapshot[] rootChildrenBefore = snapshot.Root.Children.ToArray();
        SensorNodeSnapshot[] hardwareChildrenBefore = hardware.Children.ToArray();
        string currentDisplayBefore = sensor.Current.Display;
        float? currentRawBefore = sensor.Current.Raw;

        Dictionary<string, object> projection = DataJsonProjection.Project(snapshot, new Version(10, 20, 30, 40));

        AssertKeys(projection, "id", "Version", "Text", "Min", "Value", "Max", "ImageURL", "Children");
        Assert.Equal(0, Assert.IsType<int>(projection["id"]));
        Assert.Equal("10.20.30", Assert.IsType<string>(projection["Version"]));
        Assert.Equal("Sensor", Assert.IsType<string>(projection["Text"]));
        Assert.Equal("Min", Assert.IsType<string>(projection["Min"]));
        Assert.Equal("Value", Assert.IsType<string>(projection["Value"]));
        Assert.Equal("Max", Assert.IsType<string>(projection["Max"]));
        Assert.Equal(string.Empty, Assert.IsType<string>(projection["ImageURL"]));

        List<object> envelopeChildren = Assert.IsType<List<object>>(projection["Children"]);
        Dictionary<string, object> projectedRoot = Assert.IsType<Dictionary<string, object>>(Assert.Single(envelopeChildren));
        Dictionary<string, object>[] nodes = EnumerateNodes(projectedRoot).ToArray();

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, nodes.Select(node => Assert.IsType<int>(node["id"])).ToArray());
        Assert.Equal(new[] { "CASE-HOST", "Case CPU", "Temperatures", "Core #1", "Empty Group" },
            nodes.Select(node => Assert.IsType<string>(node["Text"])).ToArray());
        Assert.Empty(Assert.IsType<List<object>>(nodes[3]["Children"]));
        Assert.Empty(Assert.IsType<List<object>>(nodes[4]["Children"]));

        Assert.Equal(rootChildrenBefore, snapshot.Root.Children.ToArray());
        Assert.Equal(hardwareChildrenBefore, hardware.Children.ToArray());
        Assert.Same(hardware, snapshot.Root.Children[0]);
        Assert.Same(emptyGroup, snapshot.Root.Children[1]);
        Assert.Same(sensor, type.Children[0]);
        Assert.Equal(currentDisplayBefore, sensor.Current.Display);
        Assert.Equal(currentRawBefore, sensor.Current.Raw);
    }

    [Fact]
    public void Project_PreservesExactPropertyOrderOptionalFieldsAndLegacyValues()
    {
        SensorNodeSnapshot sensor = Sensor(
            "GPU Hotspot",
            "/GPU/CaseSensitive/0",
            "TeMpErAtUrE",
            Value(12.5f, "-"),
            Value(13.25f, "NaN %"),
            Value(14.75f, "Infinity %"),
            "images/transparent.png");
        SensorNodeSnapshot type = Group("Temperatures", "images_icon/temperature.png", sensor);
        SensorNodeSnapshot hardware = Hardware("GPU Æ", "/GPU/CaseSensitive", "images_icon/nvidia.png", type);
        SensorSnapshot snapshot = new(Group("CaseHost", "images_icon/computer.png", hardware));

        Dictionary<string, object> projection = DataJsonProjection.Project(snapshot, new Version(1, 2, 3));
        Dictionary<string, object> groupNode = OnlyChild(projection);
        Dictionary<string, object> hardwareNode = OnlyChild(groupNode);
        Dictionary<string, object> typeNode = OnlyChild(hardwareNode);
        Dictionary<string, object> sensorNode = OnlyChild(typeNode);

        AssertKeys(groupNode, "id", "Text", "Min", "Value", "Max", "ImageURL", "Children");
        AssertKeys(hardwareNode, "id", "Text", "Min", "Value", "Max", "HardwareId", "ImageURL", "Children");
        AssertKeys(typeNode, "id", "Text", "Min", "Value", "Max", "ImageURL", "Children");
        AssertKeys(sensorNode, "id", "Text", "Min", "Value", "Max", "SensorId", "Type", "RawMin", "RawValue", "RawMax", "ImageURL", "Children");

        AssertMissingKeys(groupNode, "HardwareId", "SensorId", "Type", "RawMin", "RawValue", "RawMax");
        AssertMissingKeys(hardwareNode, "SensorId", "Type", "RawMin", "RawValue", "RawMax");
        AssertMissingKeys(typeNode, "HardwareId", "SensorId", "Type", "RawMin", "RawValue", "RawMax");
        AssertMissingKeys(sensorNode, "HardwareId");

        Assert.Equal("images_icon/computer.png", Assert.IsType<string>(groupNode["ImageURL"]));
        Assert.Equal("/GPU/CaseSensitive", Assert.IsType<string>(hardwareNode["HardwareId"]));
        Assert.Equal("images_icon/nvidia.png", Assert.IsType<string>(hardwareNode["ImageURL"]));
        Assert.Equal("images_icon/temperature.png", Assert.IsType<string>(typeNode["ImageURL"]));
        Assert.Equal("/GPU/CaseSensitive/0", Assert.IsType<string>(sensorNode["SensorId"]));
        Assert.Equal("TeMpErAtUrE", Assert.IsType<string>(sensorNode["Type"]));
        Assert.Equal("-", Assert.IsType<string>(sensorNode["Min"]));
        Assert.Equal("NaN %", Assert.IsType<string>(sensorNode["Value"]));
        Assert.Equal("Infinity %", Assert.IsType<string>(sensorNode["Max"]));
        Assert.Equal(12.5f, Assert.IsType<float>(sensorNode["RawMin"]));
        Assert.Equal(13.25f, Assert.IsType<float>(sensorNode["RawValue"]));
        Assert.Equal(14.75f, Assert.IsType<float>(sensorNode["RawMax"]));
        Assert.Equal("images/transparent.png", Assert.IsType<string>(sensorNode["ImageURL"]));
        Assert.Empty(Assert.IsType<List<object>>(sensorNode["Children"]));
    }

    [Fact]
    public void Project_MapsNonFiniteRawValuesToNullAndIsDeterministic()
    {
        SensorNodeSnapshot unavailable = Sensor(
            "Unavailable",
            "/Opaque/Sensor/A",
            "Load",
            Value(null, "-"),
            Value(float.NaN, "NaN %"),
            Value(float.PositiveInfinity, "Infinity %"));
        SensorNodeSnapshot mixed = Sensor(
            "Mixed",
            "/Opaque/Sensor/B",
            "Temperature",
            Value(float.NegativeInfinity, "-Infinity °C"),
            Value(42.25f, "42.25 °C"),
            Value(null, "-"));
        SensorSnapshot snapshot = new(Group("Deterministic", "images_icon/computer.png", unavailable, mixed));

        Dictionary<string, object> first = DataJsonProjection.Project(snapshot, new Version(4, 5, 6, 7));
        Dictionary<string, object> second = DataJsonProjection.Project(snapshot, new Version(4, 5, 6, 7));
        byte[] firstBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(first);
        byte[] secondBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(second);

        Assert.Equal(firstBytes, secondBytes);
        Dictionary<string, object> firstRoot = OnlyChild(first);
        List<object> firstChildren = Assert.IsType<List<object>>(firstRoot["Children"]);
        Dictionary<string, object> unavailableNode = Assert.IsType<Dictionary<string, object>>(firstChildren[0]);
        Dictionary<string, object> mixedNode = Assert.IsType<Dictionary<string, object>>(firstChildren[1]);

        Assert.Equal(1, Assert.IsType<int>(firstRoot["id"]));
        Assert.Equal(2, Assert.IsType<int>(unavailableNode["id"]));
        Assert.Equal(3, Assert.IsType<int>(mixedNode["id"]));
        Assert.Null(unavailableNode["RawMin"]);
        Assert.Null(unavailableNode["RawValue"]);
        Assert.Null(unavailableNode["RawMax"]);
        Assert.Null(mixedNode["RawMin"]);
        Assert.Equal(42.25f, Assert.IsType<float>(mixedNode["RawValue"]));
        Assert.Null(mixedNode["RawMax"]);

        Assert.Null(unavailable.Minimum.Raw);
        Assert.True(float.IsNaN(unavailable.Current.Raw.Value));
        Assert.True(float.IsPositiveInfinity(unavailable.Maximum.Raw.Value));
        Assert.True(float.IsNegativeInfinity(mixed.Minimum.Raw.Value));
        Assert.Equal(42.25f, mixed.Current.Raw);
        Assert.Null(mixed.Maximum.Raw);
        Assert.Equal(new[] { unavailable, mixed }, snapshot.Root.Children.ToArray());
    }

    private static SensorNodeSnapshot Group(string text, string imageUrl, params SensorNodeSnapshot[] children)
    {
        return new SensorNodeSnapshot(
            SensorSnapshotNodeKind.Group,
            text,
            null,
            null,
            null,
            imageUrl,
            EmptyValue(),
            EmptyValue(),
            EmptyValue(),
            children);
    }

    private static SensorNodeSnapshot Hardware(string text, string hardwareId, string imageUrl, params SensorNodeSnapshot[] children)
    {
        return new SensorNodeSnapshot(
            SensorSnapshotNodeKind.Hardware,
            text,
            null,
            hardwareId,
            null,
            imageUrl,
            EmptyValue(),
            EmptyValue(),
            EmptyValue(),
            children);
    }

    private static SensorNodeSnapshot Sensor(
        string text,
        string sensorId,
        string sensorType,
        SensorValueSnapshot minimum,
        SensorValueSnapshot current,
        SensorValueSnapshot maximum,
        string imageUrl = "images/transparent.png")
    {
        return new SensorNodeSnapshot(
            SensorSnapshotNodeKind.Sensor,
            text,
            sensorId,
            null,
            sensorType,
            imageUrl,
            minimum,
            current,
            maximum,
            Array.Empty<SensorNodeSnapshot>());
    }

    private static SensorValueSnapshot EmptyValue() => Value(null, string.Empty);

    private static SensorValueSnapshot Value(float? raw, string display) => new(raw, display);

    private static Dictionary<string, object> OnlyChild(Dictionary<string, object> parent)
    {
        List<object> children = Assert.IsType<List<object>>(parent["Children"]);
        return Assert.IsType<Dictionary<string, object>>(Assert.Single(children));
    }

    private static IEnumerable<Dictionary<string, object>> EnumerateNodes(Dictionary<string, object> node)
    {
        yield return node;
        foreach (object child in Assert.IsType<List<object>>(node["Children"]))
        {
            foreach (Dictionary<string, object> descendant in EnumerateNodes(Assert.IsType<Dictionary<string, object>>(child)))
                yield return descendant;
        }
    }

    private static void AssertKeys(Dictionary<string, object> node, params string[] expected)
    {
        Assert.Equal(expected, node.Keys.ToArray());
    }

    private static void AssertMissingKeys(Dictionary<string, object> node, params string[] keys)
    {
        foreach (string key in keys)
            Assert.False(node.ContainsKey(key), $"Did not expect key '{key}'.");
    }
}
