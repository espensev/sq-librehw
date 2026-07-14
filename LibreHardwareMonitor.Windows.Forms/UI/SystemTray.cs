// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class SystemTray : IDisposable
{
    private IComputer _computer;
    private readonly PersistentSettings _settings;
    private readonly UnitManager _unitManager;
    private readonly Control _uiMarshaller;
    private readonly List<SensorNotifyIcon> _sensorList = new List<SensorNotifyIcon>();
    private readonly SynchronizationContext _uiContext;
    private readonly int _uiThreadId;
    private bool _mainIconEnabled;
    private readonly NotifyIconAdv _mainIcon;
    private readonly ContextMenuStrip _mainContextMenu;
    private readonly System.Drawing.Icon _mainIconImage;
    private bool _disposed;

    public SystemTray(IComputer computer, PersistentSettings settings, UnitManager unitManager, Control uiMarshaller = null)
    {
        _computer = computer;
        _settings = settings;
        _unitManager = unitManager;
        _uiMarshaller = uiMarshaller;
        _uiContext = SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;
        computer.HardwareAdded += HardwareAdded;
        computer.HardwareRemoved += HardwareRemoved;

        _mainIcon = new NotifyIconAdv();

        _mainContextMenu = new ContextMenuStrip();
        ToolStripItem hideShowItem = new ToolStripMenuItem("Hide/Show");
        hideShowItem.Click += delegate
        {
            SendHideShowCommand();
        };
        _mainContextMenu.Items.Add(hideShowItem);
        _mainContextMenu.Items.Add(new ToolStripSeparator());
        ToolStripItem exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += delegate
        {
            SendExitCommand();
        };
        _mainContextMenu.Items.Add(exitItem);
        _mainIcon.ContextMenuStrip = _mainContextMenu;
        _mainIcon.DoubleClick += delegate
        {
            SendHideShowCommand();
        };
        _mainIconImage = EmbeddedResources.GetIcon("smallicon.ico");
        _mainIcon.Icon = _mainIconImage;
        _mainIcon.Text = "Libre Hardware Monitor - Sev IQ";
    }

    // Hardware/sensor events fire on whatever thread detected the change (background updater,
    // NetworkChange pool threads); _sensorList, settings, and the native tray icons are
    // UI-thread state, so handler bodies hop threads here. Subscriptions stay method groups so
    // the -= in HardwareRemoved keeps working.
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
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }

            return;
        }

        if (_uiMarshaller == null && _uiContext != null)
        {
            _uiContext.Post(_ =>
            {
                if (!_disposed)
                    action();
            }, null);
        }
    }

    private void HardwareRemoved(IHardware hardware)
    {
        if (_disposed)
            return;

        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (ISensor sensor in hardware.Sensors)
            SensorRemoved(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareRemoved(subHardware);
    }

    private void HardwareAdded(IHardware hardware)
    {
        if (_disposed)
            return;

        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        RunOnUiThread(() =>
        {
            if (_settings.GetValue(new Identifier(sensor.Identifier, "tray").ToString(), false))
                Add(sensor, false);
        });
    }

    private void SensorRemoved(ISensor sensor)
    {
        RunOnUiThread(() =>
        {
            if (Contains(sensor))
                Remove(sensor, false);
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _computer.HardwareAdded -= HardwareAdded;
        _computer.HardwareRemoved -= HardwareRemoved;
        foreach (IHardware hardware in _computer.Hardware)
            UnsubscribeHardware(hardware);

        foreach (SensorNotifyIcon icon in _sensorList)
            icon.Dispose();
        _sensorList.Clear();

        _mainIcon.ContextMenuStrip = null;
        _mainIcon.Icon = null;
        _mainIcon.Dispose();
        _mainContextMenu.Dispose();
        _mainIconImage?.Dispose();

        HideShowCommand = null;
        ExitCommand = null;
        _computer = null;
        GC.SuppressFinalize(this);
    }

    private void UnsubscribeHardware(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            UnsubscribeHardware(subHardware);
    }

    public void Redraw()
    {
        foreach (SensorNotifyIcon icon in _sensorList)
            icon.Update();
    }

    public bool Contains(ISensor sensor)
    {
        foreach (SensorNotifyIcon icon in _sensorList)
            if (icon.Sensor == sensor)
                return true;
        return false;
    }

    public void Add(ISensor sensor, bool balloonTip)
    {
        if (Contains(sensor))
            return;


        _sensorList.Add(new SensorNotifyIcon(this, sensor, _settings, _unitManager));
        UpdateMainIconVisibility();
        _settings.SetValue(new Identifier(sensor.Identifier, "tray").ToString(), true);
    }

    public void Remove(ISensor sensor)
    {
        Remove(sensor, true);
    }

    private void Remove(ISensor sensor, bool deleteConfig)
    {
        if (deleteConfig)
        {
            _settings.Remove(new Identifier(sensor.Identifier, "tray").ToString());
            _settings.Remove(new Identifier(sensor.Identifier, "traycolor").ToString());
        }
        SensorNotifyIcon instance = null;
        foreach (SensorNotifyIcon icon in _sensorList)
        {
            if (icon.Sensor == sensor)
                instance = icon;
        }
        if (instance != null)
        {
            _sensorList.Remove(instance);
            UpdateMainIconVisibility();
            instance.Dispose();
        }
    }

    public event EventHandler HideShowCommand;

    public void SendHideShowCommand()
    {
        HideShowCommand?.Invoke(this, null);
    }

    public event EventHandler ExitCommand;

    public void SendExitCommand()
    {
        ExitCommand?.Invoke(this, null);
    }

    private void UpdateMainIconVisibility()
    {
        if (_mainIconEnabled)
            _mainIcon.Visible = _sensorList.Count == 0;
        else
            _mainIcon.Visible = false;
    }

    public bool IsMainIconEnabled
    {
        get { return _mainIconEnabled; }
        set
        {
            if (_mainIconEnabled != value)
            {
                _mainIconEnabled = value;
                UpdateMainIconVisibility();
            }
        }
    }
}
