// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public sealed class TypeNode : Node, IExpandPersistNode, IDisposable
{
    private readonly PersistentSettings _settings;
    private readonly string _expandedIdentifier;
    private bool _expanded;

    public TypeNode(SensorType sensorType, Identifier parentId, PersistentSettings settings)
    {
        SensorType = sensorType;
        _expandedIdentifier = new Identifier(parentId, SensorType.ToString(), ".expanded").ToString();
        _settings = settings;

        switch (sensorType)
        {
            case SensorType.Voltage:
                Image = SensorTypeImageCache.Get("voltage.png");
                Text = "Voltages";
                break;
            case SensorType.Current:
                Image = SensorTypeImageCache.Get("voltage.png");
                Text = "Currents";
                break;
            case SensorType.Energy:
                Image = SensorTypeImageCache.Get("battery.png");
                Text = "Capacities";
                break;
            case SensorType.Clock:
                Image = SensorTypeImageCache.Get("clock.png");
                Text = "Clocks";
                break;
            case SensorType.Load:
                Image = SensorTypeImageCache.Get("load.png");
                Text = "Load";
                break;
            case SensorType.Temperature:
                Image = SensorTypeImageCache.Get("temperature.png");
                Text = "Temperatures";
                break;
            case SensorType.TemperatureRate:
                Image = SensorTypeImageCache.Get("temperature.png");
                Text = "Temperature Rates";
                break;
            case SensorType.Fan:
                Image = SensorTypeImageCache.Get("fan.png");
                Text = "Fans";
                break;
            case SensorType.Flow:
                Image = SensorTypeImageCache.Get("flow.png");
                Text = "Flows";
                break;
            case SensorType.Control:
                Image = SensorTypeImageCache.Get("control.png");
                Text = "Controls";
                break;
            case SensorType.Level:
                Image = SensorTypeImageCache.Get("level.png");
                Text = "Levels";
                break;
            case SensorType.Power:
                Image = SensorTypeImageCache.Get("power.png");
                Text = "Powers";
                break;
            case SensorType.Data:
                Image = SensorTypeImageCache.Get("data.png");
                Text = "Data";
                break;
            case SensorType.SmallData:
                Image = SensorTypeImageCache.Get("data.png");
                Text = "Data";
                break;
            case SensorType.Factor:
                Image = SensorTypeImageCache.Get("factor.png");
                Text = "Factors";
                break;
            case SensorType.Frequency:
                Image = SensorTypeImageCache.Get("clock.png");
                Text = "Frequencies";
                break;
            case SensorType.Throughput:
                Image = SensorTypeImageCache.Get("throughput.png");
                Text = "Throughput";
                break;
            case SensorType.TimeSpan:
                Image = SensorTypeImageCache.Get("time.png");
                Text = "Times";
                break;
            case SensorType.Timing:
                Image = SensorTypeImageCache.Get("time.png");
                Text = "Timings";
                break;
            case SensorType.Noise:
                Image = SensorTypeImageCache.Get("loudspeaker.png");
                Text = "Noise Levels";
                break;
            case SensorType.Conductivity:
                Image = SensorTypeImageCache.Get("voltage.png");
                Text = "Conductivities";
                break;
            case SensorType.Humidity:
                Image = SensorTypeImageCache.Get("humidity.png");
                Text = "Humidity Levels";
                break;
        }

        NodeAdded += TypeNode_NodeAdded;
        NodeRemoved += TypeNode_NodeRemoved;
        _expanded = settings.GetValue(_expandedIdentifier, true);
    }

    private void TypeNode_NodeRemoved(Node node)
    {
        node.IsVisibleChanged -= Node_IsVisibleChanged;
        Node_IsVisibleChanged(null);
    }

    private void TypeNode_NodeAdded(Node node)
    {
        node.IsVisibleChanged += Node_IsVisibleChanged;
        Node_IsVisibleChanged(null);
    }

    private void Node_IsVisibleChanged(Node node)
    {
        foreach (Node n in Nodes)
        {
            if (n.IsVisible)
            {
                IsVisible = true;
                return;
            }
        }
        IsVisible = false;
    }

    public SensorType SensorType { get; }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            _settings.SetValue(_expandedIdentifier, _expanded);
        }
    }

    public void Dispose()
    {
        NodeAdded -= TypeNode_NodeAdded;
        NodeRemoved -= TypeNode_NodeRemoved;
        foreach (Node node in Nodes)
            node.IsVisibleChanged -= Node_IsVisibleChanged;
    }
}

internal static class SensorTypeImageCache
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Image> Images = new(StringComparer.Ordinal);

    internal static Image Get(string resourceName)
    {
        lock (SyncRoot)
        {
            if (!Images.TryGetValue(resourceName, out Image image))
            {
                image = EmbeddedResources.GetImage(resourceName);
                Images.Add(resourceName, image);
            }

            return image;
        }
    }

    internal static void DisposeAll()
    {
        lock (SyncRoot)
        {
            foreach (Image image in Images.Values)
                image.Dispose();

            Images.Clear();
        }
    }
}
