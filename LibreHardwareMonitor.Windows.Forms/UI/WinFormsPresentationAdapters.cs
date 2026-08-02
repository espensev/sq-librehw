// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using Aga.Controls.Tree;
using LibreHardwareMonitor.Hardware;
using WinFormsControl = System.Windows.Forms.Control;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Binds the presentation ports to the already-created WinForms surfaces. Every member is a
/// one-for-one forward to the wrapped instance: no copying, translation, reordering, caching,
/// exception suppression, thread marshalling, or added lifecycle state.
/// </summary>
internal sealed class TreeViewPresentationAdapter : IPresentationTreeSurface
{
    private readonly TreeViewAdv _treeView;

    internal TreeViewPresentationAdapter(TreeViewAdv treeView)
    {
        _treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
    }

    public void Redraw() => _treeView.Invalidate();
}

internal sealed class PlotPanelPresentationAdapter : IPresentationPlotSurface
{
    private readonly PlotPanel _plotPanel;

    internal PlotPanelPresentationAdapter(PlotPanel plotPanel)
    {
        _plotPanel = plotPanel ?? throw new ArgumentNullException(nameof(plotPanel));
    }

    // The same control instance MainForm reparents between the split panel and the plot window.
    public WinFormsControl Control => _plotPanel;

    public Action ResetGraphView
    {
        get => _plotPanel.ResetGraphView;
        set => _plotPanel.ResetGraphView = value;
    }

    public void SetCurrentSettings() => _plotPanel.SetCurrentSettings();

    public void SetSensors(List<ISensor> sensors, IDictionary<ISensor, Color> colors, double strokeThickness) =>
        _plotPanel.SetSensors(sensors, colors, strokeThickness);

    public void UpdateStrokeThickness(double strokeThickness) => _plotPanel.UpdateStrokeThickness(strokeThickness);

    public void SetAxisTextScale(int percent) => _plotPanel.SetAxisTextScale(percent);

    public void SetTrackerTextScale(int percent) => _plotPanel.SetTrackerTextScale(percent);

    public void Redraw() => _plotPanel.InvalidatePlot();
}

internal sealed class SystemTrayPresentationAdapter : IPresentationTraySurface
{
    private readonly SystemTray _systemTray;

    internal SystemTrayPresentationAdapter(SystemTray systemTray)
    {
        _systemTray = systemTray ?? throw new ArgumentNullException(nameof(systemTray));
    }

    // Direct add/remove against the wrapped event keeps the tray as the raising sender, so the
    // coordinator relays the original sender and the tray's established null EventArgs unchanged.
    public event EventHandler HideShowCommand
    {
        add => _systemTray.HideShowCommand += value;
        remove => _systemTray.HideShowCommand -= value;
    }

    public event EventHandler ExitCommand
    {
        add => _systemTray.ExitCommand += value;
        remove => _systemTray.ExitCommand -= value;
    }

    public bool IsMainIconEnabled
    {
        get => _systemTray.IsMainIconEnabled;
        set => _systemTray.IsMainIconEnabled = value;
    }

    public void Redraw() => _systemTray.Redraw();

    public bool Contains(ISensor sensor) => _systemTray.Contains(sensor);

    public void Add(ISensor sensor, bool balloonTip) => _systemTray.Add(sensor, balloonTip);

    public void Remove(ISensor sensor) => _systemTray.Remove(sensor);

    public void Dispose() => _systemTray.Dispose();
}

internal sealed class SensorGadgetPresentationAdapter : IPresentationGadgetSurface
{
    private readonly SensorGadget _gadget;

    internal SensorGadgetPresentationAdapter(SensorGadget gadget)
    {
        _gadget = gadget ?? throw new ArgumentNullException(nameof(gadget));
    }

    public event EventHandler HideShowCommand
    {
        add => _gadget.HideShowCommand += value;
        remove => _gadget.HideShowCommand -= value;
    }

    // Assigning through keeps Gadget's established VisibleChanged-then-Redraw behavior.
    public bool Visible
    {
        get => _gadget.Visible;
        set => _gadget.Visible = value;
    }

    public void Redraw() => _gadget.Redraw();

    public bool Contains(ISensor sensor) => _gadget.Contains(sensor);

    public void Add(ISensor sensor) => _gadget.Add(sensor);

    public void Remove(ISensor sensor) => _gadget.Remove(sensor);

    public void Dispose() => _gadget.Dispose();
}

/// <summary>
/// Creates the presentation ports for the existing surfaces.
/// </summary>
internal static class WinFormsPresentationAdapters
{
    internal static IPresentationTreeSurface ForTree(TreeViewAdv treeView) =>
        new TreeViewPresentationAdapter(treeView);

    internal static IPresentationPlotSurface ForPlot(PlotPanel plotPanel) =>
        new PlotPanelPresentationAdapter(plotPanel);

    internal static IPresentationTraySurface ForTray(SystemTray systemTray) =>
        new SystemTrayPresentationAdapter(systemTray);

    /// <summary>
    /// An absent gadget maps to an absent port. The coordinator then reports the gadget
    /// unavailable, returns false for containment, and performs safe no-ops for visibility,
    /// redraw, membership, and teardown without emitting events. This keeps the established
    /// non-Windows and disabled-gadget behavior in one place instead of a null-object instance
    /// that would otherwise be reported as available.
    /// </summary>
    internal static IPresentationGadgetSurface ForGadget(SensorGadget gadget) =>
        gadget == null ? null : new SensorGadgetPresentationAdapter(gadget);
}
