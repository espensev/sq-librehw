// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;
using LibreHardwareMonitor.Windows.Forms.UI;

namespace LibreHardwareMonitor.Windows.Forms.Adapters;

internal static class WinFormsNodeSensorSnapshotSource
{
    internal static SensorSnapshot Capture(Node root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        // Node.SyncRoot makes membership and producer order coherent for the complete copy.
        // Formatted and raw sensor getters can still interleave with hardware updates, so this is
        // a detached-after-capture snapshot rather than an atomic one-instant sensor sample.
        lock (Node.SyncRoot)
        {
            return new SensorSnapshot(CaptureNode(root));
        }
    }

    private static SensorNodeSnapshot CaptureNode(Node node)
    {
        string text = node.Text;
        SensorSnapshotNodeKind kind = SensorSnapshotNodeKind.Group;
        string sensorId = null;
        string hardwareId = null;
        string sensorType = null;
        string minimumDisplay = string.Empty;
        string currentDisplay = string.Empty;
        string maximumDisplay = string.Empty;
        float? minimumRaw = null;
        float? currentRaw = null;
        float? maximumRaw = null;
        string imageUrl;

        switch (node)
        {
            case SensorNode sensorNode:
                kind = SensorSnapshotNodeKind.Sensor;
                ISensor sensor = sensorNode.Sensor;

                sensorId = sensor.Identifier.ToString();
                sensorType = sensor.SensorType.ToString();

                minimumDisplay = sensorNode.Min;
                currentDisplay = sensorNode.Value;
                maximumDisplay = sensorNode.Max;

                minimumRaw = sensor.Min;
                currentRaw = sensor.Value;
                maximumRaw = sensor.Max;

                imageUrl = "images/transparent.png";
                break;
            case HardwareNode hardwareNode:
                kind = SensorSnapshotNodeKind.Hardware;
                IHardware hardware = hardwareNode.Hardware;

                hardwareId = hardware.Identifier.ToString();

                imageUrl = GetHardwareImageUrl(hardware.HardwareType);
                break;
            case TypeNode typeNode:
                imageUrl = GetTypeImageUrl(typeNode.SensorType);
                break;
            default:
                imageUrl = "images_icon/computer.png";
                break;
        }

        SensorValueSnapshot minimum = new(minimumRaw, minimumDisplay);
        SensorValueSnapshot current = new(currentRaw, currentDisplay);
        SensorValueSnapshot maximum = new(maximumRaw, maximumDisplay);

        SensorNodeSnapshot[] children = new SensorNodeSnapshot[node.Nodes.Count];
        for (int i = 0; i < node.Nodes.Count; i++)
            children[i] = CaptureNode(node.Nodes[i]);

        return new SensorNodeSnapshot(
            kind,
            text,
            sensorId,
            hardwareId,
            sensorType,
            imageUrl,
            minimum,
            current,
            maximum,
            children);
    }

    private static string GetHardwareImageUrl(HardwareType hardwareType)
    {
        string fileName;
        switch (hardwareType)
        {
            case HardwareType.Cpu:
                fileName = "cpu.png";
                break;
            case HardwareType.GpuNvidia:
                fileName = "nvidia.png";
                break;
            case HardwareType.GpuAmd:
                fileName = "ati.png";
                break;
            case HardwareType.GpuIntel:
                fileName = "intel.png";
                break;
            case HardwareType.Storage:
                fileName = "hdd.png";
                break;
            case HardwareType.Motherboard:
                fileName = "mainboard.png";
                break;
            case HardwareType.SuperIO:
                fileName = "chip.png";
                break;
            case HardwareType.Memory:
                fileName = "ram.png";
                break;
            case HardwareType.Cooler:
                fileName = "fan.png";
                break;
            case HardwareType.Network:
                fileName = "nic.png";
                break;
            case HardwareType.Psu:
                fileName = "power-supply.png";
                break;
            case HardwareType.Battery:
                fileName = "battery.png";
                break;
            case HardwareType.PowerMonitor:
                fileName = "powermonitor.png";
                break;
            default:
                fileName = "cpu.png";
                break;
        }

        return "images_icon/" + fileName;
    }

    private static string GetTypeImageUrl(SensorType sensorType)
    {
        string fileName;
        switch (sensorType)
        {
            case SensorType.Voltage:
            case SensorType.Current:
                fileName = "voltage.png";
                break;
            case SensorType.Clock:
            case SensorType.Timing:
                fileName = "clock.png";
                break;
            case SensorType.Load:
                fileName = "load.png";
                break;
            case SensorType.Temperature:
            case SensorType.TemperatureRate:
                fileName = "temperature.png";
                break;
            case SensorType.Fan:
                fileName = "fan.png";
                break;
            case SensorType.Flow:
                fileName = "flow.png";
                break;
            case SensorType.Control:
                fileName = "control.png";
                break;
            case SensorType.Level:
                fileName = "level.png";
                break;
            case SensorType.Power:
                fileName = "power.png";
                break;
            case SensorType.Noise:
                fileName = "loudspeaker.png";
                break;
            case SensorType.Conductivity:
                fileName = "voltage.png";
                break;
            case SensorType.Throughput:
                fileName = "throughput.png";
                break;
            case SensorType.Humidity:
                fileName = "flow.png";
                break;
            default:
                fileName = "power.png";
                break;
        }

        return "images_icon/" + fileName;
    }
}
