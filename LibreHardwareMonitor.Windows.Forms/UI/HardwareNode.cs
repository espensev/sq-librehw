// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class HardwareNode : Node, IExpandPersistNode, IDisposable
{
    private readonly PersistentSettings _settings;
    private readonly UnitManager _unitManager;
    private readonly Control _uiMarshaller;
    private readonly Dictionary<SensorType, TypeNode> _typeNodes = new Dictionary<SensorType, TypeNode>();
    private readonly string _expandedIdentifier;
    private readonly int _uiThreadId;
    private bool _expanded;
    private bool _disposed;

    public event EventHandler PlotSelectionChanged;

    public HardwareNode(IHardware hardware, PersistentSettings settings, UnitManager unitManager, Control uiMarshaller = null)
    {
        _settings = settings;
        _unitManager = unitManager;
        _uiMarshaller = uiMarshaller;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _expandedIdentifier = new Identifier(hardware.Identifier, "expanded").ToString();
        Hardware = hardware;
        Image = HardwareTypeImage.Instance.GetImage(hardware.HardwareType);

        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        // Drivers activate/deactivate sensors inside hardware.Update(), which runs on the
        // background updater thread, so these events must hop to the UI thread before they
        // mutate the node tree (and, through it, TreeViewAdv and the plot model).
        hardware.SensorAdded += Hardware_SensorAdded;
        hardware.SensorRemoved += Hardware_SensorRemoved;

        _expanded = settings.GetValue(_expandedIdentifier, true);
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed)
            return;

        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return;
        }

        Control marshaller = _uiMarshaller;
        if (marshaller is { IsHandleCreated: true, IsDisposed: false })
        {
            try
            {
                marshaller.BeginInvoke((Action)(() =>
                {
                    if (!_disposed)
                        action();
                }));
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

        // A missing or destroyed handle is not evidence that this worker owns the UI. Drop the
        // callback; a later hardware snapshot/reset will recreate the visible tree safely.
    }

    private void Hardware_SensorAdded(ISensor sensor)
    {
        RunOnUiThread(() => SensorAdded(sensor));
    }

    private void Hardware_SensorRemoved(ISensor sensor)
    {
        RunOnUiThread(() => SensorRemoved(sensor));
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

    internal int MaterializedTypeNodeCount => _typeNodes.Count;

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
        if (_typeNodes.TryGetValue(sensor.SensorType, out TypeNode typeNode))
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
        if (_disposed)
            return;

        if (!_typeNodes.TryGetValue(sensor.SensorType, out TypeNode typeNode))
        {
            typeNode = new TypeNode(sensor.SensorType, Hardware.Identifier, _settings);
            _typeNodes.Add(sensor.SensorType, typeNode);
        }

        InsertSorted(typeNode, sensor);
        UpdateNode(typeNode);

        PlotSelectionChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Hardware.SensorAdded -= Hardware_SensorAdded;
        Hardware.SensorRemoved -= Hardware_SensorRemoved;

        foreach (HardwareNode child in Nodes.OfType<HardwareNode>().ToList())
            child.Dispose();

        foreach (TypeNode typeNode in _typeNodes.Values)
        {
            foreach (SensorNode sensorNode in typeNode.Nodes.OfType<SensorNode>())
                sensorNode.PlotSelectionChanged -= SensorPlotSelectionChanged;

            typeNode.Dispose();
        }

        Nodes.Clear();
        _typeNodes.Clear();
        PlotSelectionChanged = null;
        GC.SuppressFinalize(this);
    }
}
