// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Tree redraw boundary. Layout, input, hit testing, selection, column state, and the
/// UI Automation bridge stay with the control and MainForm.
/// </summary>
internal interface IPresentationTreeSurface
{
    void Redraw();
}

/// <summary>
/// Plot projection boundary. History, decimation, zoom, docking, and control parenting stay
/// with the panel and MainForm; the control is exposed only so MainForm can reparent the
/// same instance between the split panel and the separate plot window.
/// </summary>
internal interface IPresentationPlotSurface
{
    Control Control { get; }

    Action ResetGraphView { get; set; }

    void SetCurrentSettings();

    void SetSensors(List<ISensor> sensors, IDictionary<ISensor, Color> colors, double strokeThickness);

    void UpdateStrokeThickness(double strokeThickness);

    void SetAxisTextScale(int percent);

    void SetTrackerTextScale(int percent);

    void Redraw();
}

/// <summary>
/// Tray notification boundary. Membership settings keys, balloon behavior, and native icon
/// lifetime stay with the concrete tray.
/// </summary>
internal interface IPresentationTraySurface : IDisposable
{
    bool IsMainIconEnabled { get; set; }

    event EventHandler HideShowCommand;

    event EventHandler ExitCommand;

    void Redraw();

    bool Contains(ISensor sensor);

    void Add(ISensor sensor, bool balloonTip);

    void Remove(ISensor sensor);
}

/// <summary>
/// Optional gadget boundary. A null port means the gadget was never created, which is the
/// established non-Windows and disabled-gadget behavior.
/// </summary>
internal interface IPresentationGadgetSurface : IDisposable
{
    bool Visible { get; set; }

    event EventHandler HideShowCommand;

    void Redraw();

    bool Contains(ISensor sensor);

    void Add(ISensor sensor);

    void Remove(ISensor sensor);
}

/// <summary>
/// Forwards MainForm presentation operations to the tree, plot, tray, and optional gadget
/// surfaces, keeps the post-poll redraw order, relays tray/gadget commands with their original
/// sender and arguments, and tears the notification surfaces down gadget before tray.
/// It owns no control, hardware, timer, settings, persistence, or UI-thread policy; MainForm
/// remains responsible for calling these operations on the UI thread.
/// </summary>
internal sealed class PresentationSurfaceCoordinator : IDisposable
{
    private readonly IPresentationTreeSurface _tree;
    private readonly IPresentationPlotSurface _plot;
    private readonly IPresentationTraySurface _tray;
    private readonly IPresentationGadgetSurface _gadget;
    private readonly EventHandler _trayHideShowRelay;
    private readonly EventHandler _trayExitRelay;
    private readonly EventHandler _gadgetHideShowRelay;
    private bool _disposed;

    internal PresentationSurfaceCoordinator(
        IPresentationTreeSurface tree,
        IPresentationPlotSurface plot,
        IPresentationTraySurface tray,
        IPresentationGadgetSurface gadget)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _plot = plot ?? throw new ArgumentNullException(nameof(plot));
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _gadget = gadget;

        // Relays are fields so disposal can detach exactly the delegates that were attached.
        _trayHideShowRelay = (sender, e) => HideShowRequested?.Invoke(sender, e);
        _trayExitRelay = (sender, e) => ExitRequested?.Invoke(sender, e);
        _gadgetHideShowRelay = (sender, e) => HideShowRequested?.Invoke(sender, e);

        _tray.HideShowCommand += _trayHideShowRelay;
        _tray.ExitCommand += _trayExitRelay;

        if (_gadget != null)
            _gadget.HideShowCommand += _gadgetHideShowRelay;
    }

    internal event EventHandler HideShowRequested;

    internal event EventHandler ExitRequested;

    internal bool IsGadgetAvailable => _gadget != null;

    internal bool IsGadgetVisible => _gadget != null && _gadget.Visible;

    internal Control PlotControl => _plot.Control;

    internal Action PlotResetGraphView
    {
        get => _plot.ResetGraphView;
        set => _plot.ResetGraphView = value;
    }

    internal bool IsTrayMainIconEnabled
    {
        get => _tray.IsMainIconEnabled;
        set => _tray.IsMainIconEnabled = value;
    }

    /// <summary>
    /// Post-poll redraw. The order is tree, tray, gadget, then plot; a hidden plot skips only
    /// the plot redraw. MainForm owns the visibility decision and the UI thread.
    /// </summary>
    internal void RefreshSurfaces(bool plotVisible)
    {
        _tree.Redraw();
        _tray.Redraw();
        _gadget?.Redraw();

        if (plotVisible)
            _plot.Redraw();
    }

    internal void RedrawTree() => _tree.Redraw();

    internal void RedrawPlot() => _plot.Redraw();

    internal void ApplyPlotCurrentSettings() => _plot.SetCurrentSettings();

    internal void SetPlotSensors(List<ISensor> sensors, IDictionary<ISensor, Color> colors, double strokeThickness) =>
        _plot.SetSensors(sensors, colors, strokeThickness);

    internal void UpdatePlotStrokeThickness(double strokeThickness) => _plot.UpdateStrokeThickness(strokeThickness);

    internal void SetPlotAxisTextScale(int percent) => _plot.SetAxisTextScale(percent);

    internal void SetPlotTrackerTextScale(int percent) => _plot.SetTrackerTextScale(percent);

    internal void RedrawTray() => _tray.Redraw();

    internal bool TrayContains(ISensor sensor) => _tray.Contains(sensor);

    internal void AddToTray(ISensor sensor, bool balloonTip) => _tray.Add(sensor, balloonTip);

    internal void RemoveFromTray(ISensor sensor) => _tray.Remove(sensor);

    internal bool GadgetContains(ISensor sensor) => _gadget != null && _gadget.Contains(sensor);

    internal bool AddToGadget(ISensor sensor)
    {
        if (_gadget == null)
            return false;

        _gadget.Add(sensor);
        return true;
    }

    internal bool RemoveFromGadget(ISensor sensor)
    {
        if (_gadget == null)
            return false;

        _gadget.Remove(sensor);
        return true;
    }

    internal bool TrySetGadgetVisible(bool visible)
    {
        if (_gadget == null)
            return false;

        _gadget.Visible = visible;
        return true;
    }

    internal bool TryRedrawGadget()
    {
        if (_gadget == null)
            return false;

        _gadget.Redraw();
        return true;
    }

    /// <summary>
    /// Detaches the command relays and disposes the notification surfaces, gadget before tray,
    /// exactly once. The tree and plot controls stay with normal Form/control disposal.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _tray.HideShowCommand -= _trayHideShowRelay;
        _tray.ExitCommand -= _trayExitRelay;

        if (_gadget != null)
        {
            _gadget.HideShowCommand -= _gadgetHideShowRelay;
            _gadget.Dispose();
        }

        _tray.Dispose();
    }
}
