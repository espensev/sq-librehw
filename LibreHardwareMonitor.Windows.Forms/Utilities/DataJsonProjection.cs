// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

internal static class DataJsonProjection
{
    internal static Dictionary<string, object> Project(SensorSnapshot snapshot, Version version)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        if (version == null)
            throw new ArgumentNullException(nameof(version));

        int nodeIndex = 1;
        Dictionary<string, object> json = new()
        {
            ["id"] = 0,
            ["Version"] = $"{version.Major}.{version.Minor}.{version.Build}",
            ["Text"] = "Sensor",
            ["Min"] = "Min",
            ["Value"] = "Value",
            ["Max"] = "Max",
            ["ImageURL"] = string.Empty,
            ["Children"] = new List<object> { ProjectNode(snapshot.Root, ref nodeIndex) }
        };

        return json;
    }

    private static Dictionary<string, object> ProjectNode(SensorNodeSnapshot node, ref int nodeIndex)
    {
        Dictionary<string, object> jsonNode = new()
        {
            ["id"] = nodeIndex++,
            ["Text"] = node.Text,
            ["Min"] = string.Empty,
            ["Value"] = string.Empty,
            ["Max"] = string.Empty
        };

        switch (node.Kind)
        {
            case SensorSnapshotNodeKind.Sensor:
                jsonNode["SensorId"] = node.SensorId;
                jsonNode["Type"] = node.SensorType;
                jsonNode["Min"] = node.Minimum.Display;
                jsonNode["Value"] = node.Current.Display;
                jsonNode["Max"] = node.Maximum.Display;
                jsonNode["RawMin"] = SanitizeFloat(node.Minimum.Raw);
                jsonNode["RawValue"] = SanitizeFloat(node.Current.Raw);
                jsonNode["RawMax"] = SanitizeFloat(node.Maximum.Raw);
                jsonNode["ImageURL"] = node.ImageUrl;
                break;
            case SensorSnapshotNodeKind.Hardware:
                jsonNode["HardwareId"] = node.HardwareId;
                jsonNode["ImageURL"] = node.ImageUrl;
                break;
            case SensorSnapshotNodeKind.Group:
                jsonNode["ImageURL"] = node.ImageUrl;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node.Kind), node.Kind, null);
        }

        List<object> children = new(node.Children.Count);
        foreach (SensorNodeSnapshot child in node.Children)
            children.Add(ProjectNode(child, ref nodeIndex));

        jsonNode["Children"] = children;
        return jsonNode;
    }

    private static object SanitizeFloat(float? value)
    {
        if (value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
            return value.Value;

        return null;
    }
}
