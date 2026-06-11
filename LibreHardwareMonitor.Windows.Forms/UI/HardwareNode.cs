// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class HardwareNode : Node, IExpandPersistNode
{
    private readonly PersistentSettings _settings;
    private readonly UnitManager _unitManager;
    private readonly Control _uiMarshaller;
    private readonly List<TypeNode> _typeNodes = new List<TypeNode>();
    private readonly string _expandedIdentifier;
    private bool _expanded;

    public event EventHandler PlotSelectionChanged;

    public HardwareNode(IHardware hardware, PersistentSettings settings, UnitManager unitManager, Control uiMarshaller = null)
    {
        _settings = settings;
        _unitManager = unitManager;
        _uiMarshaller = uiMarshaller;
        _expandedIdentifier = new Identifier(hardware.Identifier, "expanded").ToString();
        Hardware = hardware;
        Image = HardwareTypeImage.Instance.GetImage(hardware.HardwareType);

        foreach (SensorType sensorType in Enum.GetValues(typeof(SensorType)))
            _typeNodes.Add(new TypeNode(sensorType, hardware.Identifier, _settings));

        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        // Drivers activate/deactivate sensors inside hardware.Update(), which runs on the
        // background updater thread, so these events must hop to the UI thread before they
        // mutate the node tree (and, through it, TreeViewAdv and the plot model).
        hardware.SensorAdded += sensor => RunOnUiThread(() => SensorAdded(sensor));
        hardware.SensorRemoved += sensor => RunOnUiThread(() => SensorRemoved(sensor));

        _expanded = settings.GetValue(_expandedIdentifier, true);
    }

    private void RunOnUiThread(Action action)
    {
        Control marshaller = _uiMarshaller;
        if (marshaller is { IsHandleCreated: true, IsDisposed: false } && marshaller.InvokeRequired)
        {
            try
            {
                marshaller.BeginInvoke(action);
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // Handle destroyed between the guard and the call (shutdown race).
                return;
            }
        }

        action();
    }


    public override string Text
    {
        get { return Hardware.Name; }
        set { Hardware.Name = value; }
    }

    public override string ToolTip
    {
        get
        {
            IDictionary<string, string> properties = Hardware.Properties;

            if (properties.Count > 0)
            {
                StringBuilder stringBuilder = new();
                stringBuilder.AppendLine("Hardware properties:");
                    
                foreach (KeyValuePair<string, string> property in properties)
                    stringBuilder.AppendFormat(" • {0}: {1}\n", property.Key, property.Value);

                return stringBuilder.ToString();
            }

            return null;
        }
    }

    public IHardware Hardware { get; }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            _settings.SetValue(_expandedIdentifier, _expanded);
        }
    }

    private void UpdateNode(TypeNode node)
    {
        if (node.Nodes.Count > 0)
        {
            if (!Nodes.Contains(node))
            {
                int i = 0;
                while (i < Nodes.Count && ((TypeNode)Nodes[i]).SensorType < node.SensorType)
                    i++;

                Nodes.Insert(i, node);
            }
        }
        else
        {
            if (Nodes.Contains(node))
                Nodes.Remove(node);
        }
    }

    private void SensorRemoved(ISensor sensor)
    {
        foreach (TypeNode typeNode in _typeNodes)
        {
            if (typeNode.SensorType == sensor.SensorType)
            {
                SensorNode sensorNode = null;
                foreach (Node node in typeNode.Nodes)
                {
                    if (node is SensorNode n && n.Sensor == sensor)
                        sensorNode = n;
                }
                if (sensorNode != null)
                {
                    sensorNode.PlotSelectionChanged -= SensorPlotSelectionChanged;
                    typeNode.Nodes.Remove(sensorNode);
                    UpdateNode(typeNode);
                }
            }
        }
        PlotSelectionChanged?.Invoke(this, null);
    }

    private void InsertSorted(Node node, ISensor sensor)
    {
        int i = 0;
        while (i < node.Nodes.Count && ((SensorNode)node.Nodes[i]).Sensor.Index < sensor.Index)
            i++;

        SensorNode sensorNode = new SensorNode(sensor, _settings, _unitManager);
        sensorNode.PlotSelectionChanged += SensorPlotSelectionChanged;
        node.Nodes.Insert(i, sensorNode);
    }

    private void SensorPlotSelectionChanged(object sender, EventArgs e)
    {
        PlotSelectionChanged?.Invoke(this, null);
    }

    private void SensorAdded(ISensor sensor)
    {
        foreach (TypeNode typeNode in _typeNodes)
        {
            if (typeNode.SensorType == sensor.SensorType)
            {
                InsertSorted(typeNode, sensor);
                UpdateNode(typeNode);
            }
        }

        PlotSelectionChanged?.Invoke(this, null);
    }
}
