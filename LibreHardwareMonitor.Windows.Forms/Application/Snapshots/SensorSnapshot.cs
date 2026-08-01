// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;

internal enum SensorSnapshotNodeKind
{
    Group,
    Hardware,
    Sensor
}

internal sealed class SensorValueSnapshot
{
    internal SensorValueSnapshot(float? raw, string display)
    {
        Raw = raw;
        Display = display ?? throw new ArgumentNullException(nameof(display));
    }

    internal float? Raw { get; }

    internal string Display { get; }
}

internal sealed class SensorNodeSnapshot
{
    private readonly ReadOnlyCollection<SensorNodeSnapshot> _children;

    internal SensorNodeSnapshot(
        SensorSnapshotNodeKind kind,
        string text,
        string sensorId,
        string hardwareId,
        string sensorType,
        string imageUrl,
        SensorValueSnapshot minimum,
        SensorValueSnapshot current,
        SensorValueSnapshot maximum,
        IEnumerable<SensorNodeSnapshot> children)
    {
        Kind = kind;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        SensorId = sensorId;
        HardwareId = hardwareId;
        SensorType = sensorType;
        ImageUrl = imageUrl ?? throw new ArgumentNullException(nameof(imageUrl));
        Minimum = minimum ?? throw new ArgumentNullException(nameof(minimum));
        Current = current ?? throw new ArgumentNullException(nameof(current));
        Maximum = maximum ?? throw new ArgumentNullException(nameof(maximum));

        if (children == null)
            throw new ArgumentNullException(nameof(children));

        SensorNodeSnapshot[] childrenCopy = new List<SensorNodeSnapshot>(children).ToArray();
        _children = new ReadOnlyCollection<SensorNodeSnapshot>(childrenCopy);
    }

    internal SensorSnapshotNodeKind Kind { get; }

    internal string Text { get; }

    internal string SensorId { get; }

    internal string HardwareId { get; }

    internal string SensorType { get; }

    internal string ImageUrl { get; }

    internal SensorValueSnapshot Minimum { get; }

    internal SensorValueSnapshot Current { get; }

    internal SensorValueSnapshot Maximum { get; }

    internal IReadOnlyList<SensorNodeSnapshot> Children => _children;
}

internal sealed class SensorSnapshot
{
    internal SensorSnapshot(SensorNodeSnapshot root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    internal SensorNodeSnapshot Root { get; }
}
