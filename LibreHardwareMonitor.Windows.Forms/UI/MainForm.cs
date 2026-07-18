// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public sealed partial class MainForm : Form
{
    private ToolStripMenuItem _autoThemeMenuItem;
    private bool _closing;
    private readonly UserOption _autoStart;
    private readonly Computer _computer;
    private readonly SensorGadget _gadget;
    private readonly Logger _logger;
    private readonly UserRadioGroup _loggingInterval;
    private readonly UserRadioGroup _updateInterval;
    private readonly UserOption _throttleAtaUpdate;
    private readonly UserOption _logSensors;
    private readonly UserOption _forceDriveWakeup;
    private readonly UserOption _minimizeOnClose;
    private readonly UserOption _minimizeToTray;
    private readonly PlotPanel _plotPanel;
    private readonly UserOption _readBatterySensors;
    private readonly UserOption _readCpuSensors;
    private readonly UserOption _readFanControllersSensors;
    private readonly UserOption _readGpuSensors;
    private readonly UserOption _readPowerMonitorSensors;
    private readonly UserOption _readHddSensors;
    private readonly UserOption _readMainboardSensors;
    private readonly UserOption _readNicSensors;
    private readonly UserOption _readPsuSensors;
    private readonly UserOption _readRamSensors;
    private readonly Node _root;
    private readonly UserOption _runWebServer;
    private readonly UserRadioGroup _sensorValuesTimeWindow;
    private readonly PersistentSettings _settings;
    private readonly UserOption _showGadget;
    private readonly UserOption _showValue;
    private readonly UserOption _showMin;
    private readonly UserOption _showMax;
    private readonly UserOption _compactMode;
    private readonly StartupManager _startupManager = new();
    private readonly SystemTray _systemTray;
    private readonly UnitManager _unitManager;
    private readonly UpdateVisitor _updateVisitor = new();

    private int _delayCount;
    private Form _plotForm;
    private UserRadioGroup _plotLocation;
    private UserRadioGroup _splitPanelScalingSetting;
    private bool _selectionDragging;
    private IDictionary<ISensor, Color> _sensorPlotColors = new Dictionary<ISensor, Color>();
    private UserOption _showPlot;
    private UserRadioGroup _strokeThickness;
    private double _plotStrokeThickness = 2;
    private bool _compactLayoutActive;
    private bool _updatingSensorTreeLayout;
    private int _plotEventSuspendDepth;
    private bool _plotRebuildPending;
    private GridLineStyle _standardGridLineStyle;
    private int _standardRowHeight;
    private int _standardValueColumnWidth;
    private int _standardMinColumnWidth;
    private int _standardMaxColumnWidth;
    private int _uiTextScalePercent = UiScale.DefaultPercent;
    private Font _scaledTreeFont;   // owned; disposed on replacement (never dispose SystemFonts.MessageBoxFont)
    private Font _baseMenuFont;     // owned clone captured once from mainMenu.Font
    private Font _scaledMenuFont;   // owned; disposed on replacement

    // Debounced percent sliders (see TextScaleSliderMenu / UiTextScaleCommitGate).
    private TextScaleSliderMenu _textSizeSlider;
    private TextScaleSliderMenu _plotTextSlider;
    private int _plotTextScalePercent = UiScale.DefaultPercent;
    private int _baseValueColumnWidth = 100;
    private int _baseMinColumnWidth = 100;
    private int _baseMaxColumnWidth = 100;

    // Persist settings on this cadence so a crash, forced kill or power loss cannot revert
    // everything changed since launch; the app otherwise saves only on clean exit/log-off.
    private const int AutoSaveIntervalMilliseconds = 5 * 60 * 1000;
    private readonly System.Windows.Forms.Timer _autoSaveTimer;
    private readonly UiShutdownCoordinator _shutdownCoordinator;
    private readonly int _uiThreadId;
    private readonly SemaphoreSlim _hardwareLifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _hardwareLifecycleCancellation = new();
    private Task _hardwareInitializationTask;

    private bool IsShutdownPending => _closing || (_shutdownCoordinator?.IsShutdownRequested ?? false);

    public MainForm()
    {
        InitializeComponent();
        _uiThreadId = Environment.CurrentManagedThreadId;

        _settings = new PersistentSettings();
        _settings.Load(Path.ChangeExtension(Application.ExecutablePath, ".config"));
        _uiTextScalePercent = UiScale.ClampPercent(_settings.GetValue("uiTextScale", UiScale.DefaultPercent));
        _plotTextScalePercent = UiScale.ClampPercent(_settings.GetValue("plotTextScale", UiScale.DefaultPercent));

        _unitManager = new UnitManager(_settings);

        // make sure the buffers used for double buffering are not disposed
        // after each draw call
        BufferedGraphicsManager.Current.MaximumBuffer = Screen.PrimaryScreen.Bounds.Size;

        // set the DockStyle here, to avoid conflicts with the MainMenu
        splitContainer.Dock = DockStyle.Fill;

        Font = SystemFonts.MessageBoxFont;
        treeView.Font = SystemFonts.MessageBoxFont;

        // Set the bounds immediately, so that our child components can be
        // properly placed.
        Bounds = new Rectangle
        {
            X = _settings.GetValue("mainForm.Location.X", Location.X),
            Y = _settings.GetValue("mainForm.Location.Y", Location.Y),
            Width = _settings.GetValue("mainForm.Width", 720),
            Height = _settings.GetValue("mainForm.Height", 840)
        };
        MinimumSize = new Size(360, 420);

        Theme setTheme = Theme.All.FirstOrDefault(theme => _settings.GetValue("theme", "auto") == theme.Id);
        if (setTheme != null)
        {
            Theme.Current = setTheme;
        }
        else
        {
            Theme.SetAutoTheme();
        }

        _plotPanel = new PlotPanel(_settings, _unitManager) { Font = SystemFonts.MessageBoxFont, Dock = DockStyle.Fill };

        nodeCheckBox.IsVisibleValueNeeded += NodeCheckBox_IsVisibleValueNeeded;
        nodeTextBoxText.DrawText += NodeTextBoxText_DrawText;
        nodeTextBoxValue.DrawText += NodeTextBoxText_DrawText;
        nodeTextBoxMin.DrawText += NodeTextBoxText_DrawText;
        nodeTextBoxMax.DrawText += NodeTextBoxText_DrawText;
        nodeTextBoxText.EditorShowing += NodeTextBoxText_EditorShowing;

        for (int i = 1; i < treeView.Columns.Count; i++)
        {
            TreeColumn column = treeView.Columns[i];
            column.Width = Math.Max(20, Math.Min(400, _settings.GetValue("treeView.Columns." + column.Header + ".Width", column.Width)));
        }

        TreeModel treeModel = new();
        _root = new Node(Environment.MachineName) { Image = EmbeddedResources.GetImage("computer.png") };

        treeModel.Nodes.Add(_root);
        treeView.Model = treeModel;

        _computer = new Computer(_settings);

        _systemTray = new SystemTray(_computer, _settings, _unitManager, this);
        _systemTray.HideShowCommand += HideShowClick;
        _systemTray.ExitCommand += ExitClick;

        if (Software.OperatingSystem.IsUnix)
        {
            // Unix
            treeView.RowHeight = Math.Max(treeView.RowHeight, 18);
            splitContainer.BorderStyle = BorderStyle.None;
            splitContainer.SplitterWidth = 4;
            treeView.BorderStyle = BorderStyle.Fixed3D;
            _plotPanel.BorderStyle = BorderStyle.Fixed3D;
            gadgetMenuItem.Visible = false;
            minCloseMenuItem.Visible = false;
            minTrayMenuItem.Visible = false;
            startMinMenuItem.Visible = false;
        }
        else
        {
            // Windows
            treeView.RowHeight = Math.Max(treeView.Font.Height + 1, 18);
            _gadget = new SensorGadget(_computer, _settings, _unitManager, this);
            _gadget.HideShowCommand += HideShowClick;
        }

        _standardRowHeight = treeView.RowHeight;
        _standardGridLineStyle = treeView.GridLineStyle;
        _standardValueColumnWidth = treeView.Columns[1].Width;
        _standardMinColumnWidth = treeView.Columns[2].Width;
        _standardMaxColumnWidth = treeView.Columns[3].Width;
        _baseValueColumnWidth = treeView.Columns[1].Width;
        _baseMinColumnWidth = treeView.Columns[2].Width;
        _baseMaxColumnWidth = treeView.Columns[3].Width;

        InitializeGraphMenu();

        ToolStripMenuItem compactModeMenuItem = new("Compact Mode");
        viewMenuItem.DropDownItems.Insert(4, compactModeMenuItem);
        _compactMode = new UserOption("compactMode", false, compactModeMenuItem, _settings);
        _compactMode.Changed += delegate { ApplySensorTreeLayout(); };

        double dpiScale = DeviceDpi / 96.0;
        _textSizeSlider = new TextScaleSliderMenu("Text Size", _uiTextScalePercent, dpiScale,
            mainMenu.Font, Theme.Current.MenuBackgroundColor);
        _textSizeSlider.PercentChanged += percent => _uiTextScalePercent = percent;
        _textSizeSlider.CommitRequested += deferMenuRefresh => ApplyUiTextScale(deferMenuRefresh);
        _textSizeSlider.InstallAt(viewMenuItem, 5);

        ToolStripMenuItem autoFitColumnsMenuItem = new("Auto-Fit Columns");
        autoFitColumnsMenuItem.Click += delegate { AutoFitTreeColumns(); };
        viewMenuItem.DropDownItems.Insert(6, autoFitColumnsMenuItem);

        treeView.ShowNodeToolTips = true;
        treeView.SelectionMode = TreeSelectionMode.Multi;
        NodeToolTipProvider tooltipProvider = new();
        nodeTextBoxText.ToolTipProvider = tooltipProvider;
        nodeTextBoxValue.ToolTipProvider = tooltipProvider;
        _logger = new Logger(_computer);
        var saved = _settings.GetValue("logger.fileRotation", 0); // 0 = PerSession, 1 = Daily.
        _logger.FileRotationMethod = (LoggerFileRotation)Math.Max(0, Math.Min(saved, 1));
        perSessionFileRotationMenuItem.Checked = _logger.FileRotationMethod == LoggerFileRotation.PerSession;
        dailyFileRotationMenuItem.Checked = _logger.FileRotationMethod == LoggerFileRotation.Daily;

        _computer.HardwareAdded += HardwareAdded;
        _computer.HardwareRemoved += HardwareRemoved;

        backgroundUpdater.DoWork += BackgroundUpdater_DoWork;
        backgroundUpdater.RunWorkerCompleted += BackgroundUpdater_RunWorkerCompleted;
        timer.Enabled = false;

        UserOption showHiddenSensors = new("hiddenMenuItem", false, hiddenMenuItem, _settings);
        showHiddenSensors.Changed += delegate { treeModel.ForceVisible = showHiddenSensors.Value; };

        _showValue = new UserOption("valueMenuItem", true, valueMenuItem, _settings);
        _showValue.Changed += delegate { ApplySensorTreeLayout(); };

        _showMin = new UserOption("minMenuItem", false, minMenuItem, _settings);
        _showMin.Changed += delegate { ApplySensorTreeLayout(); };

        _showMax = new UserOption("maxMenuItem", true, maxMenuItem, _settings);
        _showMax.Changed += delegate { ApplySensorTreeLayout(); };

        // Apply once now that all column options (and compact mode) are wired, so the initial
        // layout does not depend on the eager Changed callback of whichever option subscribed last.
        ApplyUiTextScale();
        ApplyPlotTextScale();

        _ = new UserOption("startMinMenuItem", false, startMinMenuItem, _settings);
        _minimizeToTray = new UserOption("minTrayMenuItem", true, minTrayMenuItem, _settings);
        _minimizeToTray.Changed += delegate { _systemTray.IsMainIconEnabled = _minimizeToTray.Value; };

        _minimizeOnClose = new UserOption("minCloseMenuItem", false, minCloseMenuItem, _settings);

        _autoStart = new UserOption(null, _startupManager.Startup, startupMenuItem, _settings);
        _autoStart.Changed += delegate
        {
            try
            {
                _startupManager.Startup = _autoStart.Value;
            }
            catch (InvalidOperationException)
            {
                MessageBox.Show("Updating the auto-startup option failed.",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                _autoStart.Value = _startupManager.Startup;
            }
        };

        _readMainboardSensors = new UserOption("mainboardMenuItem", true, mainboardMenuItem, _settings);
        _readMainboardSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsMotherboardEnabled = _readMainboardSensors.Value); };

        _readCpuSensors = new UserOption("cpuMenuItem", true, cpuMenuItem, _settings);
        _readCpuSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsCpuEnabled = _readCpuSensors.Value); };

        _readRamSensors = new UserOption("ramMenuItem", true, ramMenuItem, _settings);
        _readRamSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsMemoryEnabled = _readRamSensors.Value); };

        _readGpuSensors = new UserOption("gpuMenuItem", true, gpuMenuItem, _settings);
        _readGpuSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsGpuEnabled = _readGpuSensors.Value); };

        _readPowerMonitorSensors = new UserOption("powerMonitorMenuItem", true, powerMonitorMenuItem, _settings);
        _readPowerMonitorSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsPowerMonitorEnabled = _readPowerMonitorSensors.Value); };

        _readFanControllersSensors = new UserOption("fanControllerMenuItem", true, fanControllerMenuItem, _settings);
        _readFanControllersSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsControllerEnabled = _readFanControllersSensors.Value); };

        _readHddSensors = new UserOption("hddMenuItem", true, hddMenuItem, _settings);
        _readHddSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsStorageEnabled = _readHddSensors.Value); };

        _readNicSensors = new UserOption("nicMenuItem", true, nicMenuItem, _settings);
        _readNicSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsNetworkEnabled = _readNicSensors.Value); };

        _readPsuSensors = new UserOption("psuMenuItem", true, psuMenuItem, _settings);
        _readPsuSensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsPsuEnabled = _readPsuSensors.Value); };

        _readBatterySensors = new UserOption("batteryMenuItem", true, batteryMenuItem, _settings);
        _readBatterySensors.Changed += delegate { ApplyHardwareOption(() => _computer.IsBatteryEnabled = _readBatterySensors.Value); };

        _showGadget = new UserOption("gadgetMenuItem", false, gadgetMenuItem, _settings);

        _forceDriveWakeup = new UserOption("forceDriveWakeupItem", false, forceDriveWakeupItem, _settings);
        _forceDriveWakeup.Changed += delegate
        {
            _computer.Hardware
                .OfType<StorageDevice>()
                .ToList()
                .ForEach(sd =>
            {
                sd.ForceWakeup = _forceDriveWakeup.Value;
            });
        };

        // Prevent Menu From Closing When UnClicking Hardware Items
        menuItemFileHardware.DropDown.Closing += StopFileHardwareMenuFromClosing;

        _showGadget.Changed += delegate
        {
            if (_gadget != null)
                _gadget.Visible = _showGadget.Value;
        };

        celsiusMenuItem.Checked = _unitManager.TemperatureUnit == TemperatureUnit.Celsius;
        fahrenheitMenuItem.Checked = !celsiusMenuItem.Checked;

        Server = new HttpServer(_root,
                                _computer,
                                _settings.GetValue("listenerIp", "?"),
                                _settings.GetValue("listenerPort", 8085),
                                _settings.GetValue("authenticationEnabled", false),
                                _settings.GetValue("authenticationUserName", ""),
                                _settings.GetValue("authenticationPassword", ""));

        if (Server.PlatformNotSupported)
        {
            webMenuItemSeparator.Visible = false;
            webMenuItem.Visible = false;
        }

        _runWebServer = new UserOption("runWebServerMenuItem", false, runWebServerMenuItem, _settings);
        _runWebServer.Changed += async delegate
        {
            try
            {
                if (_runWebServer.Value)
                {
                    Server.StartHttpListener();
                }
                else
                {
                    await Server.StopHttpListenerAsync().ConfigureAwait(true);

                    // A quick off/on toggle can race the asynchronous drain. Reassert the latest
                    // requested state after the old listener has fully stopped.
                    if (_runWebServer.Value && !IsShutdownPending)
                        Server.StartHttpListener();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Web server state change failed: " + ex);
            }
        };

        authWebServerMenuItem.Checked = _settings.GetValue("authenticationEnabled", false);

        _logSensors = new UserOption("logSensorsMenuItem", false, logSensorsMenuItem, _settings);

        _loggingInterval = new UserRadioGroup("loggingInterval",
                                              0,
                                              new[]
                                              {
                                                  log1sMenuItem,
                                                  log2sMenuItem,
                                                  log5sMenuItem,
                                                  log10sMenuItem,
                                                  log30sMenuItem,
                                                  log1minMenuItem,
                                                  log2minMenuItem,
                                                  log5minMenuItem,
                                                  log10minMenuItem,
                                                  log30minMenuItem,
                                                  log1hMenuItem,
                                                  log2hMenuItem,
                                                  log6hMenuItem
                                              },
                                              _settings);

        _loggingInterval.Changed += (sender, e) =>
        {
            switch (_loggingInterval.Value)
            {
                case 0:
                    _logger.LoggingInterval = new TimeSpan(0, 0, 1);
                    break;
                case 1:
                    _logger.LoggingInterval = new TimeSpan(0, 0, 2);
                    break;
                case 2:
                    _logger.LoggingInterval = new TimeSpan(0, 0, 5);
                    break;
                case 3:
                    _logger.LoggingInterval = new TimeSpan(0, 0, 10);
                    break;
                case 4:
                    _logger.LoggingInterval = new TimeSpan(0, 0, 30);
                    break;
                case 5:
                    _logger.LoggingInterval = new TimeSpan(0, 1, 0);
                    break;
                case 6:
                    _logger.LoggingInterval = new TimeSpan(0, 2, 0);
                    break;
                case 7:
                    _logger.LoggingInterval = new TimeSpan(0, 5, 0);
                    break;
                case 8:
                    _logger.LoggingInterval = new TimeSpan(0, 10, 0);
                    break;
                case 9:
                    _logger.LoggingInterval = new TimeSpan(0, 30, 0);
                    break;
                case 10:
                    _logger.LoggingInterval = new TimeSpan(1, 0, 0);
                    break;
                case 11:
                    _logger.LoggingInterval = new TimeSpan(2, 0, 0);
                    break;
                case 12:
                    _logger.LoggingInterval = new TimeSpan(6, 0, 0);
                    break;
            }
        };

        _updateInterval = new UserRadioGroup("updateIntervalMenuItem",
                                             2,
                                             new[]
                                             {
                                                 updateInterval250msMenuItem,
                                                 updateInterval500msMenuItem,
                                                 updateInterval1sMenuItem,
                                                 updateInterval2sMenuItem,
                                                 updateInterval5sMenuItem,
                                                 updateInterval10sMenuItem
                                             },
                                             _settings);

        _updateInterval.Changed += (sender, e) =>
        {
            switch (_updateInterval.Value)
            {
                case 0:
                    timer.Interval = 250;
                    break;
                case 1:
                    timer.Interval = 500;
                    break;
                case 2:
                    timer.Interval = 1000;
                    break;
                case 3:
                    timer.Interval = 2000;
                    break;
                case 4:
                    timer.Interval = 5000;
                    break;
                case 5:
                    timer.Interval = 10000;
                    break;
            }
        };

        _throttleAtaUpdate = new UserOption("throttleAtaUpdateMenuItem", false, throttleAtaUpdateMenuItem, _settings);
        _throttleAtaUpdate.Changed += (sender, e) =>
        {
            switch (_throttleAtaUpdate.Value)
            {
                case true:
                    StorageDevice.ThrottleInterval = TimeSpan.FromSeconds(30);
                    break;

                case false:
                    StorageDevice.ThrottleInterval = TimeSpan.Zero;
                    break;
            }
        };

        _sensorValuesTimeWindow = new UserRadioGroup("sensorValuesTimeWindow",
                                                     10,
                                                     new[]
                                                     {
                                                         timeWindow30sMenuItem,
                                                         timeWindow1minMenuItem,
                                                         timeWindow2minMenuItem,
                                                         timeWindow5minMenuItem,
                                                         timeWindow10minMenuItem,
                                                         timeWindow30minMenuItem,
                                                         timeWindow1hMenuItem,
                                                         timeWindow2hMenuItem,
                                                         timeWindow6hMenuItem,
                                                         timeWindow12hMenuItem,
                                                         timeWindow24hMenuItem
                                                     },
                                                     _settings);

        perSessionFileRotationMenuItem.Checked = _logger.FileRotationMethod == LoggerFileRotation.PerSession;
        dailyFileRotationMenuItem.Checked = _logger.FileRotationMethod == LoggerFileRotation.Daily;

        _sensorValuesTimeWindow.Changed += (sender, e) =>
        {
            TimeSpan timeWindow = GetSensorValuesTimeWindow();
            _computer.Accept(new SensorVisitor(delegate(ISensor sensor) { sensor.ValuesTimeWindow = timeWindow; }));
        };

        InitializeTheme();
        InitializePlotForm();
        InitializeSplitter();

        startupMenuItem.Visible = _startupManager.IsAvailable;

        // Create a handle before background discovery starts. Hardware event dispatch must never
        // infer UI-thread ownership merely because a handle has not been created yet.
        // event marshaling (HardwareAdded/SensorAdded BeginInvoke) also requires the handle to
        // exist even when starting minimized to tray, so do not rely on side effects for this.
        _ = Handle;

        _autoSaveTimer = new System.Windows.Forms.Timer { Interval = AutoSaveIntervalMilliseconds };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        // SystemEvents invokes SessionEnded on its monitoring thread. Invoke the request on the form
        // thread so settings projection never reads controls concurrently with FormClosing or
        // autosave, and so the logoff handler does not return before the final save completes. The
        // coordinator still allows FormClosing to win if it starts before the callback is dispatched.
        _shutdownCoordinator = new UiShutdownCoordinator(
            () => Environment.CurrentManagedThreadId == _uiThreadId,
            action => BeginInvoke(action),
            CloseApplicationCoreAsync);

        Microsoft.Win32.SystemEvents.SessionEnded += SystemEvents_SessionEnded;

        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerModeChanged;

        _autoSaveTimer.Start();

        if (startMinMenuItem.Checked)
        {
            if (!minTrayMenuItem.Checked)
            {
                WindowState = FormWindowState.Minimized;
                Show();
            }
        }
        else
        {
            Show();
        }

        menuItemFileHardware.Enabled = false;
        _hardwareInitializationTask = InitializeHardwareAsync();
    }

    private void StopFileHardwareMenuFromClosing(object sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
        {
            e.Cancel = true;
        }
    }

    private async Task InitializeHardwareAsync()
    {
        CancellationToken cancellationToken = _hardwareLifecycleCancellation.Token;
        try
        {
            bool installPawnIo = false;
            if (PawnIo.PawnIo.IsInstalled)
            {
                if (PawnIo.PawnIo.Version < new Version(2, 0, 0, 0))
                {
                    installPawnIo = MessageBox.Show(
                        this,
                        "PawnIO is outdated, do you want to update it?",
                        nameof(LibreHardwareMonitor),
                        MessageBoxButtons.OKCancel) == DialogResult.OK;
                }
            }
            else
            {
                installPawnIo = MessageBox.Show(
                    this,
                    "PawnIO is not installed, do you want to install it?",
                    nameof(LibreHardwareMonitor),
                    MessageBoxButtons.OKCancel) == DialogResult.OK;
            }

            if (installPawnIo)
                await Task.Run(InstallPawnIO).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested || IsShutdownPending || IsDisposed)
                return;

            // Hardware discovery can probe firmware, buses, storage and drivers for several
            // seconds. Computer events are marshalled through the live form dispatcher.
            await _hardwareLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => _computer.Open()).ConfigureAwait(true);
            }
            finally
            {
                _hardwareLifecycleGate.Release();
            }

            if (!IsShutdownPending && !IsDisposed)
                timer.Enabled = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal close while installation/discovery is queued.
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Hardware initialization failed: " + ex);
            if (!IsShutdownPending && !IsDisposed)
            {
                MessageBox.Show(
                    this,
                    "Hardware discovery could not be completed.\n\n" + ex.GetBaseException().Message,
                    "Libre Hardware Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!IsShutdownPending && !IsDisposed)
            {
                _systemTray.IsMainIconEnabled = _minimizeToTray.Value;
                menuItemFileHardware.Enabled = true;
            }
        }
    }

    private void BeginHardwareReset()
    {
        if (IsShutdownPending || !menuItemFileHardware.Enabled)
            return;

        menuItemFileHardware.Enabled = false;
        _systemTray.IsMainIconEnabled = false;
        _ = ResetHardwareAsync();
    }

    private void ApplyHardwareOption(Action change)
    {
        // UserOption invokes each handler once during construction. Before Open starts, applying
        // the flags synchronously is cheap and lets Computer.Open discover the selected groups in
        // one pass. Later toggles can construct/close drivers and therefore use the lifecycle gate.
        if (_hardwareInitializationTask == null)
        {
            change();
            return;
        }

        if (IsShutdownPending || !menuItemFileHardware.Enabled)
            return;

        menuItemFileHardware.Enabled = false;
        _ = ApplyHardwareOptionAsync(change);
    }

    private async Task ApplyHardwareOptionAsync(Action change)
    {
        CancellationToken cancellationToken = _hardwareLifecycleCancellation.Token;
        try
        {
            await _hardwareLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(change).ConfigureAwait(true);
            }
            finally
            {
                _hardwareLifecycleGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns the next lifecycle turn.
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Hardware option change failed: " + ex);
        }
        finally
        {
            if (!IsShutdownPending && !IsDisposed)
                menuItemFileHardware.Enabled = true;
        }
    }

    private async Task ResetHardwareAsync()
    {
        CancellationToken cancellationToken = _hardwareLifecycleCancellation.Token;
        try
        {
            await _hardwareLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => _computer.Reset()).ConfigureAwait(true);
            }
            finally
            {
                _hardwareLifecycleGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns the next lifecycle turn.
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Hardware reset failed: " + ex);
        }
        finally
        {
            if (!IsShutdownPending && !IsDisposed)
            {
                _systemTray.IsMainIconEnabled = _minimizeToTray.Value;
                menuItemFileHardware.Enabled = true;
            }
        }
    }

    private static void InstallPawnIO()
    {
        string path = ExtractPawnIO();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            using Process process = Process.Start(new ProcessStartInfo(path, "-install"));
            process?.WaitForExit();
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ExtractPawnIO()
    {
        string destination = Path.Combine(Directory.GetCurrentDirectory(), "PawnIO_setup.exe");

        try
        {
            using Stream resourceStream = typeof(MainForm).Assembly.GetManifestResourceStream(
                typeof(MainForm).Assembly.GetName().Name + ".Resources.PawnIO_setup.exe");
            if (resourceStream == null)
                return null;

            using FileStream fileStream = new(destination, FileMode.Create, FileAccess.Write);
            resourceStream.CopyTo(fileStream);
            return destination;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool AuthWebServerMenuItemChecked
    {
        get { return authWebServerMenuItem.Checked; }
        set { authWebServerMenuItem.Checked = value; }
    }

    public HttpServer Server { get; }

    private void BackgroundUpdater_DoWork(object sender, DoWorkEventArgs e)
    {
        _computer.Accept(_updateVisitor);

        if (_logSensors != null && _logSensors.Value && _delayCount >= 4)
            _logger.Log();

        if (_delayCount < 4)
            _delayCount++;
    }

    private void BackgroundUpdater_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        // Runs on the UI thread. All post-update redraws live here so they (a) never mutate
        // UI/OxyPlot state from the worker thread and (b) only run when a tick actually
        // produced fresh data, instead of unconditionally from Timer_Tick.
        if (e.Error != null)
            Debug.WriteLine("Background hardware update failed: " + e.Error);

        // _closing covers the window between CloseApplication disposing the tray/timer and the
        // form actually being disposed after Application.Run returns: a worker that was in
        // flight at exit still posts its completion to the message queue.
        if (IsShutdownPending || IsDisposed)
            return;

        treeView.Invalidate();
        _systemTray.Redraw();
        _gadget?.Redraw();

        if (_showPlot == null || _showPlot.Value)
            _plotPanel.InvalidatePlot();
    }

    private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            RunOnUiThreadOrDrop(() =>
            {
                // Reset can re-enumerate every hardware group. Keep that work off the message
                // loop; the resulting hardware/sensor callbacks marshal back through this form.
                BeginHardwareReset();
            });
        }
    }

    private void InitializeTheme()
    {
        mainMenu.Renderer = new ThemedToolStripRenderer();
        treeContextMenu.Renderer = new ThemedToolStripRenderer();
        ThemedVScrollIndicator.AddToControl(treeView);
        ThemedHScrollIndicator.AddToControl(treeView);

        string themeSetting = _settings.GetValue("theme", "auto");
        bool themeSelected = false;

        void ClearThemeMenu()
        {
            foreach (ToolStripItem x in themeMenuItem.DropDownItems)
            {
                if (x is ToolStripMenuItem tmi)
                {
                    tmi.Checked = false;
                }
            }
        }

        if (Theme.SupportsAutoThemeSwitching())
        {
            _autoThemeMenuItem = new ToolStripMenuItem();
            _autoThemeMenuItem.Text = "Auto";
            _autoThemeMenuItem.Click += (o, e) =>
            {
                ClearThemeMenu();
                _autoThemeMenuItem.Checked = true;
                Theme.SetAutoTheme();
                _settings.SetValue("theme", "auto");
                PlotSelectionChanged(o, e);
            };
            themeMenuItem.DropDownItems.Add(_autoThemeMenuItem);
        }

        foreach (Theme theme in Theme.All)
        {
            ToolStripMenuItem item = new ToolStripMenuItem();
            item.Text = theme.DisplayName;
            item.Click += (o, e) =>
            {
                ClearThemeMenu();
                item.Checked = true;
                Theme.Current = theme;
                _settings.SetValue("theme", theme.Id);
                PlotSelectionChanged(o, e);
            };
            themeMenuItem.DropDownItems.Add(item);

            if (themeSetting == theme.Id)
            {
                item.PerformClick();
                themeSelected = true;
            }
        }

        if (!themeSelected)
        {
            themeMenuItem.DropDownItems[0].PerformClick();
        }

        Theme.Current.Apply(this);
    }

    private void InitializeSplitter()
    {
        splitContainer.SplitterDistance = _settings.GetValue("splitContainer.SplitterDistance", 400);
        splitContainer.SplitterMoved += delegate { _settings.SetValue("splitContainer.SplitterDistance", splitContainer.SplitterDistance); };
    }

    private void InitializeGraphMenu()
    {
        ToolStripMenuItem graphMenuItem = new("&Graph") { Name = "graphMenuItem" };
        int optionsMenuIndex = mainMenu.Items.IndexOf(optionsMenuItem);
        mainMenu.Items.Insert(optionsMenuIndex >= 0 ? optionsMenuIndex : mainMenu.Items.Count, graphMenuItem);

        plotMenuItem.Text = "&Show Graph";
        resetPlotMenuItem.Text = "&Reset Graph View";

        // The graph-local options menu offers the same reset command as this menu item.
        _plotPanel.ResetGraphView = () => resetPlotMenuItem_Click(this, EventArgs.Empty);
        sensorValuesTimeWindowMenuItem.Text = "&Time Window";
        plotLocationMenuItem.Text = "Graph &Location";
        strokeThicknessMenuItem.Text = "&Stroke Thickness";

        _plotTextSlider = new TextScaleSliderMenu("Graph Text Size", _plotTextScalePercent, DeviceDpi / 96.0,
            mainMenu.Font, Theme.Current.MenuBackgroundColor);
        _plotTextSlider.PercentChanged += percent => _plotTextScalePercent = percent;
        _plotTextSlider.CommitRequested += deferMenuRefresh => ApplyPlotTextScale(deferMenuRefresh);

        plotWindowMenuItem.Text = "Separate Window";
        plotBottomMenuItem.Text = "Bottom";
        plotRightMenuItem.Text = "Right";
        plotLocationMenuItem.DropDownItems.Clear();
        plotLocationMenuItem.DropDownItems.AddRange(new ToolStripItem[] { plotRightMenuItem, plotBottomMenuItem, plotWindowMenuItem });

        timeWindow30sMenuItem.Text = "30 seconds";
        timeWindow1minMenuItem.Text = "1 minute";
        timeWindow2minMenuItem.Text = "2 minutes";
        timeWindow5minMenuItem.Text = "5 minutes";
        timeWindow10minMenuItem.Text = "10 minutes";
        timeWindow30minMenuItem.Text = "30 minutes";
        timeWindow1hMenuItem.Text = "1 hour";
        timeWindow2hMenuItem.Text = "2 hours";
        timeWindow6hMenuItem.Text = "6 hours";
        timeWindow12hMenuItem.Text = "12 hours";
        timeWindow24hMenuItem.Text = "24 hours";

        strokeThickness1ptMenuItem.Text = "1 pt";
        strokeThickness2ptMenuItem.Text = "2 pt";
        strokeThickness3ptMenuItem.Text = "3 pt";
        strokeThickness4ptMenuItem.Text = "4 pt";

        ToolStripMenuItem graphInputsMenuItem = new("Graph &Inputs...");
        graphInputsMenuItem.Click += delegate { ShowGraphInputsForm(); };
        ToolStripMenuItem togglePlotSelectionMenuItem = new("Toggle &Plot for Selected Sensors") { ShortcutKeyDisplayString = "Space" };
        togglePlotSelectionMenuItem.Click += delegate { TogglePlotForSelectedSensors(); };
        ToolStripMenuItem clearGraphInputsMenuItem = new("&Clear Graph Inputs");
        clearGraphInputsMenuItem.Click += delegate { ClearGraphInputs(); };

        MoveMenuItem(graphMenuItem.DropDownItems, plotMenuItem);
        graphMenuItem.DropDownItems.Add(graphInputsMenuItem);
        graphMenuItem.DropDownItems.Add(togglePlotSelectionMenuItem);
        graphMenuItem.DropDownItems.Add(clearGraphInputsMenuItem);
        MoveMenuItem(graphMenuItem.DropDownItems, resetPlotMenuItem);
        graphMenuItem.DropDownItems.Add(new ToolStripSeparator());
        MoveMenuItem(graphMenuItem.DropDownItems, sensorValuesTimeWindowMenuItem);
        MoveMenuItem(graphMenuItem.DropDownItems, plotLocationMenuItem);
        MoveMenuItem(graphMenuItem.DropDownItems, strokeThicknessMenuItem);
        _plotTextSlider.InstallAt(graphMenuItem, graphMenuItem.DropDownItems.IndexOf(strokeThicknessMenuItem) + 1);
    }

    private static void MoveMenuItem(ToolStripItemCollection target, ToolStripItem item)
    {
        item.Owner?.Items.Remove(item);
        target.Add(item);
    }

    private void ShowGraphInputsForm()
    {
        // The dialog routes every plot mutation (single checkbox edit or bulk action) through
        // SetSensorNodesPlot, so each user action batches into exactly one rebuild. No suppression
        // is held while the dialog is open: unrelated plot events (sensor/hardware add/remove)
        // keep rebuilding the graph normally.
        using GraphInputsForm form = new(GetAllSensorNodes(), SetSensorNodesPlot);
        form.ShowDialog(this);
    }

    private void ClearGraphInputs()
    {
        SetSensorNodesPlot(GetAllSensorNodes(), false);
    }

    private void TogglePlotForSelectedSensors()
    {
        List<SensorNode> selectedSensorNodes = GetSelectedSensorNodes(null);
        if (selectedSensorNodes.Count == 0)
            return;

        SetSensorNodesPlot(selectedSensorNodes, selectedSensorNodes.Any(node => !node.Plot));
    }

    private List<SensorNode> GetAllSensorNodes()
    {
        static IEnumerable<SensorNode> Traverse(Node node)
        {
            if (node is SensorNode sensorNode)
                yield return sensorNode;

            foreach (Node child in node.Nodes)
            {
                foreach (SensorNode childSensorNode in Traverse(child))
                    yield return childSensorNode;
            }
        }

        return Traverse(_root).ToList();
    }

    private void ApplySensorTreeLayout()
    {
        if (_showValue == null || _showMin == null || _showMax == null || treeView.Columns.Count < 4)
            return;

        bool compact = _compactMode?.Value == true;

        if (compact && !_compactLayoutActive)
        {
            _standardRowHeight = treeView.RowHeight;
            _standardGridLineStyle = treeView.GridLineStyle;
            _standardValueColumnWidth = treeView.Columns[1].Width;
            _standardMinColumnWidth = treeView.Columns[2].Width;
            _standardMaxColumnWidth = treeView.Columns[3].Width;
        }

        _updatingSensorTreeLayout = true;
        try
        {
            treeView.Columns[1].IsVisible = _showValue.Value;
            treeView.Columns[2].IsVisible = !compact && _showMin.Value;
            treeView.Columns[3].IsVisible = !compact && _showMax.Value;

            // Compact mode force-hides Min/Max regardless of the Show Min/Show Max toggles;
            // disable those items so their checkmark cannot misrepresent the actual column state.
            minMenuItem.Enabled = !compact;
            maxMenuItem.Enabled = !compact;

            if (compact)
            {
                treeView.RowHeight = UiScale.TreeRowHeight(treeView.Font.Height, compact: true);
                treeView.GridLineStyle = GridLineStyle.None;
                treeView.Columns[1].Width = Math.Min(_standardValueColumnWidth, UiScale.ScaledColumnWidth(78, _uiTextScalePercent));
            }
            else
            {
                treeView.RowHeight = UiScale.TreeRowHeight(treeView.Font.Height, compact: false);
                treeView.GridLineStyle = _standardGridLineStyle;

                // Restore the saved column widths only when actually leaving compact mode
                // (_compactLayoutActive still holds the previous state here; it is updated below).
                // Re-applying them on every Show Value/Min/Max toggle would clobber widths the user
                // dragged in normal mode, since _standard* is only refreshed when entering compact mode.
                if (_compactLayoutActive)
                {
                    treeView.Columns[1].Width = UiScale.ScaledColumnWidth(_baseValueColumnWidth, _uiTextScalePercent);
                    treeView.Columns[2].Width = UiScale.ScaledColumnWidth(_baseMinColumnWidth, _uiTextScalePercent);
                    treeView.Columns[3].Width = UiScale.ScaledColumnWidth(_baseMaxColumnWidth, _uiTextScalePercent);
                }
            }
        }
        finally
        {
            _updatingSensorTreeLayout = false;
        }

        _compactLayoutActive = compact;
        TreeView_SizeChanged(treeView, EventArgs.Empty);
        treeView.Invalidate();
    }

    /// <summary>
    /// Single apply path for the Text Size scale: tree font + row height + value/min/max column
    /// widths + tree glyphs + plot axis text. Order-independent and composes with Compact Mode.
    /// </summary>
    private void ApplyUiTextScale(bool deferMenuRefresh = false)
    {
        _uiTextScalePercent = UiScale.ClampPercent(_uiTextScalePercent);

        // Tree font (scaled from the shared base; dispose the previous scaled font).
        Font previous = _scaledTreeFont;
        _scaledTreeFont = new Font(
            SystemFonts.MessageBoxFont.FontFamily,
            UiScale.ScaledFontSize(SystemFonts.MessageBoxFont.SizeInPoints, _uiTextScalePercent),
            SystemFonts.MessageBoxFont.Style);
        treeView.Font = _scaledTreeFont;   // propagates to all text NodeControls; fires FullUpdate
        previous?.Dispose();

        // Top menu-bar font (scaled from its captured base). Scaling a child MenuStrip's font does
        // NOT trigger the form's AutoScaleMode.Font cascade — that keys off the form's own Font.
        // Deferred while the View dropdown is open: re-fonting or re-labeling the open menu makes
        // it re-layout under the cursor mid-drag (the reported slider jitter).
        if (!deferMenuRefresh)
        {
            _baseMenuFont ??= (Font)mainMenu.Font.Clone();
            Font previousMenu = _scaledMenuFont;
            _scaledMenuFont = new Font(
                _baseMenuFont.FontFamily,
                UiScale.ScaledFontSize(_baseMenuFont.SizeInPoints, _uiTextScalePercent),
                _baseMenuFont.Style);
            mainMenu.Font = _scaledMenuFont;
            previousMenu?.Dispose();

            _textSizeSlider?.RefreshMenuText(_uiTextScalePercent, mainMenu.Font);
        }

        // Value/Min/Max column widths from their 100% base (guarded so our sets don't churn the base).
        _updatingSensorTreeLayout = true;
        try
        {
            treeView.Columns[1].Width = UiScale.ScaledColumnWidth(_baseValueColumnWidth, _uiTextScalePercent);
            treeView.Columns[2].Width = UiScale.ScaledColumnWidth(_baseMinColumnWidth, _uiTextScalePercent);
            treeView.Columns[3].Width = UiScale.ScaledColumnWidth(_baseMaxColumnWidth, _uiTextScalePercent);
        }
        finally
        {
            _updatingSensorTreeLayout = false;
        }

        // Tree glyphs (expand/collapse + plot-select checkbox footprint).
        Theme.GlyphScalePercent = _uiTextScalePercent;
        NodeCheckBox.GlyphScalePercent = _uiTextScalePercent;

        // Row height + column visibility + repaint (recomputes RowHeight from the live font).
        ApplySensorTreeLayout();

        // Plot tracker text (single PlotPanel instance covers docked + separate-window modes).
        _plotPanel?.SetTrackerTextScale(_uiTextScalePercent);

        _settings.SetValue("uiTextScale", _uiTextScalePercent);
    }

    private void ApplyPlotTextScale(bool deferMenuRefresh = false)
    {
        _plotTextScalePercent = UiScale.ClampPercent(_plotTextScalePercent);
        _plotPanel?.SetAxisTextScale(_plotTextScalePercent);

        if (!deferMenuRefresh)
            _plotTextSlider?.RefreshMenuText(_plotTextScalePercent, mainMenu.Font);

        _settings.SetValue("plotTextScale", _plotTextScalePercent);
    }

    /// <summary>
    /// Sizes the Value/Min/Max tree columns to their widest currently-visible header/cell text
    /// (clamped to <see cref="UiScale.MinColumnWidth"/>/<see cref="UiScale.MaxColumnWidth"/>).
    /// Column 0 (Sensor) already auto-fills via <see cref="TreeView_SizeChanged"/>.
    /// </summary>
    private void AutoFitTreeColumns()
    {
        (int col, Func<SensorNode, string> text)[] fitters =
        {
            (1, n => n.Value),
            (2, n => n.Min),
            (3, n => n.Max),
        };

        using Graphics g = treeView.CreateGraphics();
        foreach ((int col, Func<SensorNode, string> text) in fitters)
        {
            int widest = TextRenderer.MeasureText(g, treeView.Columns[col].Header, treeView.Font).Width;
            foreach (TreeNodeAdv node in treeView.AllNodes)
            {
                if (node.Tag is SensorNode sensorNode)
                {
                    string s = text(sensorNode);
                    if (!string.IsNullOrEmpty(s))
                        widest = Math.Max(widest, TextRenderer.MeasureText(g, s, treeView.Font).Width);
                }
            }

            _updatingSensorTreeLayout = true;
            try { treeView.Columns[col].Width = Math.Max(UiScale.MinColumnWidth, Math.Min(UiScale.MaxColumnWidth, widest + 12)); }
            finally { _updatingSensorTreeLayout = false; }

            // Keep the scale-independent base in sync so a later slider move preserves the fit.
            int baseWidth = UiScale.BaseFromScaled(treeView.Columns[col].Width, _uiTextScalePercent);
            if (col == 1) _baseValueColumnWidth = baseWidth;
            else if (col == 2) _baseMinColumnWidth = baseWidth;
            else _baseMaxColumnWidth = baseWidth;
        }

        TreeView_SizeChanged(treeView, EventArgs.Empty);
        treeView.Invalidate();
    }

    private void InitializePlotForm()
    {
        _plotForm = new Form { FormBorderStyle = FormBorderStyle.SizableToolWindow, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual };
        AddOwnedForm(_plotForm);
        _plotForm.Bounds = new Rectangle
        {
            X = _settings.GetValue("plotForm.Location.X", -100000),
            Y = _settings.GetValue("plotForm.Location.Y", 100),
            Width = _settings.GetValue("plotForm.Width", 600),
            Height = _settings.GetValue("plotForm.Height", 400)
        };

        _showPlot = new UserOption("plotMenuItem", false, plotMenuItem, _settings);
        _plotLocation = new UserRadioGroup("plotLocation", 0, new[] { plotWindowMenuItem, plotBottomMenuItem, plotRightMenuItem }, _settings);
        _splitPanelScalingSetting = new UserRadioGroup("splitPanelScalingSetting", 0, new[] { splitPanelPercentageScalingMenuItem, splitPanelFixedPlotScalingMenuItem, splitPanelFixedSensorScalingMenuItem }, _settings);

        _showPlot.Changed += delegate
        {
            if (_plotLocation.Value == 0)
            {
                if (_showPlot.Value && Visible)
                {
                    Theme.Current.Apply(_plotForm);
                    _plotForm.Show();
                }
                else
                    _plotForm.Hide();
            }
            else
            {
                splitContainer.Panel2Collapsed = !_showPlot.Value;
            }

            treeView.Invalidate();

            // The per-tick refresh is gated on _showPlot, so the model froze while hidden;
            // bring points and the time window current immediately on re-show.
            if (_showPlot.Value)
                _plotPanel.InvalidatePlot();
        };

        _strokeThickness = new UserRadioGroup("plotStroke", 1, new[] { strokeThickness1ptMenuItem, strokeThickness2ptMenuItem, strokeThickness3ptMenuItem, strokeThickness4ptMenuItem }, _settings);

        _strokeThickness.Changed += (sender, e) =>
        {
            _plotStrokeThickness = (_strokeThickness.Value >= 0 && _strokeThickness.Value <= 3)
                                                   ? _strokeThickness.Value + 1
                                                   : 4;
            _plotPanel.UpdateStrokeThickness(_plotStrokeThickness);
        };

        _plotLocation.Changed += delegate
        {
            switch (_plotLocation.Value)
            {
                case 0:
                    splitContainer.Panel2.Controls.Clear();
                    splitContainer.Panel2Collapsed = true;
                    _plotForm.Controls.Add(_plotPanel);
                    if (_showPlot.Value && Visible)
                        _plotForm.Show();
                    break;
                case 1:
                    _plotForm.Controls.Clear();
                    _plotForm.Hide();
                    splitContainer.Orientation = Orientation.Horizontal;
                    splitContainer.Panel2.Controls.Add(_plotPanel);
                    splitContainer.Panel2Collapsed = !_showPlot.Value;
                    break;
                case 2:
                    _plotForm.Controls.Clear();
                    _plotForm.Hide();
                    splitContainer.Orientation = Orientation.Vertical;
                    splitContainer.Panel2.Controls.Add(_plotPanel);
                    splitContainer.Panel2Collapsed = !_showPlot.Value;
                    break;
            }
        };

        _splitPanelScalingSetting.Changed += delegate
        {
            switch (_splitPanelScalingSetting.Value)
            {
                case 0:
                    splitContainer.FixedPanel = FixedPanel.None;
                    break;
                case 1:
                    splitContainer.FixedPanel = FixedPanel.Panel2;
                    break;
                case 2:
                    splitContainer.FixedPanel = FixedPanel.Panel1;
                    break;
            }
        };

        _plotForm.FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // just switch off the plotting when the user closes the form
                if (_plotLocation.Value == 0)
                {
                    _showPlot.Value = false;
                }

                e.Cancel = true;
            }
        };

        void MoveOrResizePlotForm(object sender, EventArgs e)
        {
            if (_plotForm.WindowState != FormWindowState.Minimized)
            {
                _settings.SetValue("plotForm.Location.X", _plotForm.Bounds.X);
                _settings.SetValue("plotForm.Location.Y", _plotForm.Bounds.Y);
                _settings.SetValue("plotForm.Width", _plotForm.Bounds.Width);
                _settings.SetValue("plotForm.Height", _plotForm.Bounds.Height);
            }
        }

        _plotForm.Move += MoveOrResizePlotForm;
        _plotForm.Resize += MoveOrResizePlotForm;

        _plotForm.VisibleChanged += delegate
        {
            Rectangle bounds = new(_plotForm.Location, _plotForm.Size);
            Screen screen = Screen.FromRectangle(bounds);
            Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, bounds);
            if (intersection.Width < Math.Min(16, bounds.Width) ||
                intersection.Height < Math.Min(16, bounds.Height))
            {
                _plotForm.Location = new Point(screen.WorkingArea.Width / 2 - bounds.Width / 2,
                                               screen.WorkingArea.Height / 2 - bounds.Height / 2);
            }
        };

        VisibleChanged += delegate
        {
            if (Visible && _showPlot.Value && _plotLocation.Value == 0)
                _plotForm.Show();
            else
                _plotForm.Hide();
        };

        Theme.Current.Apply(_plotForm);
    }

    private void InsertSorted(IList<Node> nodes, HardwareNode node)
    {
        int i = 0;
        while (i < nodes.Count && nodes[i] is HardwareNode && ((HardwareNode)nodes[i]).Hardware.HardwareType <= node.Hardware.HardwareType)
            i++;

        nodes.Insert(i, node);
    }

    private void SubHardwareAdded(IHardware hardware, Node node)
    {
        if (hardware is StorageDevice storageDevice && _forceDriveWakeup != null)
            storageDevice.ForceWakeup = _forceDriveWakeup.Value;

        // Subscribe before HardwareNode takes its UI snapshot so any sensor activated during
        // hot-plug still receives the selected window through the dynamic event path.
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorAdded += SensorAdded;

        HardwareNode hardwareNode = new(hardware, _settings, _unitManager, this);
        ApplySensorValuesTimeWindow(hardwareNode);
        hardwareNode.PlotSelectionChanged += PlotSelectionChanged;
        InsertSorted(node.Nodes, hardwareNode);
        foreach (IHardware subHardware in hardware.SubHardware)
            SubHardwareAdded(subHardware, hardwareNode);
    }

    private void SystemEvents_SessionEnded(object sender, Microsoft.Win32.SessionEndedEventArgs eventArgs)
    {
        try
        {
            Task shutdown = _shutdownCoordinator.RequestAsync();
            if (Environment.CurrentManagedThreadId != _uiThreadId)
                shutdown.GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
            // FormClosing already completed while the system event was being dispatched.
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
        {
            // The control handle was destroyed before the synchronous UI handoff completed.
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Session-end shutdown failed: " + ex);
        }
    }

    private void ApplySensorValuesTimeWindow(HardwareNode hardwareNode)
    {
        TimeSpan timeWindow = GetSensorValuesTimeWindow();
        foreach (TypeNode typeNode in hardwareNode.Nodes.OfType<TypeNode>())
        {
            foreach (SensorNode sensorNode in typeNode.Nodes.OfType<SensorNode>())
                sensorNode.Sensor.ValuesTimeWindow = timeWindow;
        }
    }

    private TimeSpan GetSensorValuesTimeWindow()
    {
        // The menu-backed setting is ready before asynchronous hardware discovery begins. Keep the
        // fallback for tests and early construction paths that have not created the group yet.
        return _sensorValuesTimeWindow?.Value switch
        {
            0 => TimeSpan.FromSeconds(30),
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(2),
            3 => TimeSpan.FromMinutes(5),
            4 => TimeSpan.FromMinutes(10),
            5 => TimeSpan.FromMinutes(30),
            6 => TimeSpan.FromHours(1),
            7 => TimeSpan.FromHours(2),
            8 => TimeSpan.FromHours(6),
            9 => TimeSpan.FromHours(12),
            _ => TimeSpan.FromHours(24)
        };
    }

    private void SensorAdded(ISensor sensor)
    {
        RunOnUiThreadOrDrop(() => sensor.ValuesTimeWindow = GetSensorValuesTimeWindow());
    }

    private void UnsubscribeSensorAdded(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;

        foreach (IHardware subHardware in hardware.SubHardware)
            UnsubscribeSensorAdded(subHardware);
    }

    private void HardwareAdded(IHardware hardware)
    {
        RunOnUiThreadOrDrop(() =>
        {
            SubHardwareAdded(hardware, _root);
            PlotSelectionChanged(this, null);
        });
    }

    private void HardwareRemoved(IHardware hardware)
    {
        RunOnUiThreadOrDrop(() =>
        {
            UnsubscribeSensorAdded(hardware);

            List<HardwareNode> nodesToRemove = new();
            foreach (Node node in _root.Nodes)
            {
                if (node is HardwareNode hardwareNode && hardwareNode.Hardware == hardware)
                    nodesToRemove.Add(hardwareNode);
            }

            foreach (HardwareNode hardwareNode in nodesToRemove)
            {
                _root.Nodes.Remove(hardwareNode);
                hardwareNode.PlotSelectionChanged -= PlotSelectionChanged;
                hardwareNode.Dispose();
            }

            PlotSelectionChanged(this, null);
        });
    }

    private void RunOnUiThreadOrDrop(Action action)
    {
        if (action == null || IsShutdownPending || IsDisposed)
            return;

        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return;
        }

        if (!IsHandleCreated)
            return;

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!IsShutdownPending && !IsDisposed)
                    action();
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void NodeTextBoxText_DrawText(object sender, DrawEventArgs e)
    {
        if (e.Node.Tag is Node node)
        {
            if (node.IsVisible)
            {
                if (plotMenuItem.Checked && node is SensorNode sensorNode && _sensorPlotColors.TryGetValue(sensorNode.Sensor, out Color color))
                    e.TextColor = color;
            }
            else
                e.TextColor = Color.DarkGray;
        }
    }

    private void PlotSelectionChanged(object sender, EventArgs e)
    {
        // While a batching scope is active (RunBatchedPlotChange), coalesce the per-node Plot and
        // PenColor events into one pending rebuild performed when the outermost scope exits, so a
        // bulk change does not fan out into N full recomputes.
        if (_plotEventSuspendDepth > 0)
        {
            _plotRebuildPending = true;
            return;
        }

        RebuildPlotSelection();
    }

    private void RebuildPlotSelection()
    {
        // Graph membership is defined by SensorNode.Plot over the full sensor model, not the tree's
        // filtered presentation: a hidden sensor keeps its plot membership and color slot, and the
        // "Show Hidden Sensors" option cannot change graph output or automatic color assignment.
        List<SensorNode> sensorNodes = GetAllSensorNodes();

        List<ISensor> selected = new();
        IDictionary<ISensor, Color> colors = new Dictionary<ISensor, Color>();
        int colorIndex = 0;

        foreach (SensorNode sensorNode in sensorNodes)
        {
            if (sensorNode.Plot)
            {
                if (!sensorNode.PenColor.HasValue)
                {
                    colors.Add(sensorNode.Sensor,
                               Theme.Current.PlotColorPalette[colorIndex % Theme.Current.PlotColorPalette.Length]);
                }

                selected.Add(sensorNode.Sensor);
            }

            colorIndex++;
        }

        // if a sensor is assigned a color that's already being used by another
        // sensor, try to assign it a new color. This is done only after the
        // previous loop sets an unchanging default color for all sensors, so that
        // colors jump around as little as possible as sensors get added/removed
        // from the plot
        var usedColors = new List<Color>();
        foreach (ISensor curSelectedSensor in selected)
        {
            if (!colors.ContainsKey(curSelectedSensor))
                continue;

            Color curColor = colors[curSelectedSensor];
            if (usedColors.Contains(curColor))
            {
                foreach (Color potentialNewColor in Theme.Current.PlotColorPalette)
                {
                    if (!colors.Values.Contains(potentialNewColor))
                    {
                        colors[curSelectedSensor] = potentialNewColor;
                        usedColors.Add(potentialNewColor);
                        break;
                    }
                }
            }
            else
            {
                usedColors.Add(curColor);
            }
        }

        foreach (SensorNode sensorNode in sensorNodes)
        {
            if (sensorNode.Plot && sensorNode.PenColor.HasValue)
                colors.Add(sensorNode.Sensor, sensorNode.PenColor.Value);
        }

        _sensorPlotColors = colors;
        _plotPanel.SetSensors(selected, colors, _plotStrokeThickness);
    }

    private void NodeTextBoxText_EditorShowing(object sender, CancelEventArgs e)
    {
        e.Cancel = !(treeView.CurrentNode != null && (treeView.CurrentNode.Tag is SensorNode || treeView.CurrentNode.Tag is HardwareNode));
    }

    private void NodeCheckBox_IsVisibleValueNeeded(object sender, NodeControlValueEventArgs e)
    {
        e.Value = e.Node.Tag is SensorNode && plotMenuItem.Checked;
    }

    private void ExitClick(object sender, EventArgs e)
    {
        CloseApplication();
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (!IsShutdownPending && !backgroundUpdater.IsBusy)
            backgroundUpdater.RunWorkerAsync();
    }

    private void AutoSaveTimer_Tick(object sender, EventArgs e)
    {
        if (IsShutdownPending)
            return;

        SaveConfiguration(autoSave: true);
    }

    private void SaveConfiguration(bool autoSave = false)
    {
        if (_plotPanel == null || _settings == null)
            return;

        _plotPanel.SetCurrentSettings();

        foreach (TreeColumn column in treeView.Columns)
        {
            int index = treeView.Columns.IndexOf(column);
            int widthToSave = index switch
            {
                1 => _baseValueColumnWidth,
                2 => _baseMinColumnWidth,
                3 => _baseMaxColumnWidth,
                _ => column.Width
            };
            _settings.SetValue("treeView.Columns." + column.Header + ".Width", widthToSave);
        }

        _settings.SetValue("uiTextScale", _uiTextScalePercent);
        _settings.SetValue("plotTextScale", _plotTextScalePercent);

        _settings.SetValue("listenerIp", Server.ListenerIp);
        _settings.SetValue("listenerPort", Server.ListenerPort);
        _settings.SetValue("authenticationEnabled", Server.AuthEnabled);
        _settings.SetValue("authenticationUserName", Server.UserName);
        _settings.SetValue("authenticationPassword", Server.PasswordSHA256);

        // Nothing changed since the last save; skip the periodic write to avoid needless disk
        // churn while the app sits idle in the tray.
        if (autoSave && !_settings.Modified)
            return;

        string fileName = Path.ChangeExtension(Application.ExecutablePath, ".config");

        try
        {
            _settings.Save(fileName);
        }
        catch (Exception ex) when (autoSave && (ex is UnauthorizedAccessException || ex is IOException))
        {
            // A periodic save must never interrupt the user with a modal dialog every cycle. The
            // atomic write preserved the previous file/backup, so just log and retry next tick.
            Debug.WriteLine("Autosave of settings failed: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Access to the path '" +
                            fileName +
                            "' is denied. " +
                            "The current settings could not be saved.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
        }
        catch (IOException)
        {
            MessageBox.Show("The path '" +
                            fileName +
                            "' is not writeable. " +
                            "The current settings could not be saved.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
        }
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        Rectangle newBounds = new()
        {
            X = _settings.GetValue("mainForm.Location.X", Location.X),
            Y = _settings.GetValue("mainForm.Location.Y", Location.Y),
            Width = _settings.GetValue("mainForm.Width", 720),
            Height = _settings.GetValue("mainForm.Height", 840)
        };

        Rectangle fullWorkingArea = new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

        foreach (Screen screen in Screen.AllScreens)
            fullWorkingArea = Rectangle.Union(fullWorkingArea, screen.Bounds);

        Rectangle intersection = Rectangle.Intersect(fullWorkingArea, newBounds);
        if (intersection.Width < 20 || intersection.Height < 20 || !_settings.Contains("mainForm.Location.X"))
        {
            newBounds.X = (Screen.PrimaryScreen.WorkingArea.Width / 2) - (newBounds.Width / 2);
            newBounds.Y = (Screen.PrimaryScreen.WorkingArea.Height / 2) - (newBounds.Height / 2);
        }

        Bounds = newBounds;

        if (_settings.GetValue("mainForm.Maximized", false))
            WindowState = FormWindowState.Maximized;

        RestoreCollapsedNodeState(treeView);

        FormClosing += MainForm_FormClosing;
    }

    private void RestoreCollapsedNodeState(TreeViewAdv treeViewAdv)
    {
        var collapsedHwNodes = treeViewAdv.AllNodes
                                          .Where(n => n.IsExpanded && n.Tag is IExpandPersistNode expandPersistNode && !expandPersistNode.Expanded)
                                          .OrderByDescending(n => n.Level)
                                          .ToList();

        foreach (TreeNodeAdv node in collapsedHwNodes)
        {
            node.IsExpanded = false;
        }
    }

    private async void CloseApplication()
    {
        try
        {
            await _shutdownCoordinator.RequestAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Application shutdown failed: " + ex);
        }
    }

    private async Task CloseApplicationCoreAsync()
    {
        if (_closing)
            return;

        _closing = true;
        FormClosing -= MainForm_FormClosing;
        Microsoft.Win32.SystemEvents.SessionEnded -= SystemEvents_SessionEnded;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerModeChanged;
        _computer.HardwareAdded -= HardwareAdded;
        _computer.HardwareRemoved -= HardwareRemoved;

        try
        {
            Visible = false;
            _systemTray.IsMainIconEnabled = false;
            timer.Enabled = false;
            _autoSaveTimer.Stop();

            _hardwareLifecycleCancellation.Cancel();
            await Server.QuitAsync().ConfigureAwait(true);

            if (_hardwareInitializationTask != null)
                await _hardwareInitializationTask.ConfigureAwait(true);

            await _hardwareLifecycleGate.WaitAsync().ConfigureAwait(true);
            try
            {
                foreach (HardwareNode hardwareNode in _root.Nodes.OfType<HardwareNode>().ToList())
                {
                    hardwareNode.PlotSelectionChanged -= PlotSelectionChanged;
                    hardwareNode.Dispose();
                }

                _root.Nodes.Clear();
                _gadget?.Dispose();
                _systemTray.Dispose();
                await Task.Run(() => _computer.Close()).ConfigureAwait(true);
            }
            finally
            {
                _hardwareLifecycleGate.Release();
            }

            SaveConfiguration();

            timer.Dispose();
            _autoSaveTimer.Dispose();
            _textSizeSlider?.Dispose();
            _plotTextSlider?.Dispose();
            backgroundUpdater.Dispose();
            _hardwareLifecycleCancellation.Dispose();
            _hardwareLifecycleGate.Dispose();

            _scaledTreeFont?.Dispose();
            _scaledTreeFont = null;
            _scaledMenuFont?.Dispose();
            _scaledMenuFont = null;
            _baseMenuFont?.Dispose();
            _baseMenuFont = null;

            _root.Image?.Dispose();
            _root.Image = null;
            SensorTypeImageCache.DisposeAll();
            HardwareTypeImage.Instance.DisposeAll();
        }
        finally
        {
            Application.Exit();
        }
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        e.Cancel = true;
        CloseApplication();
    }

    private void AboutMenuItem_Click(object sender, EventArgs e)
    {
        using AboutBox aboutBox = new();
        aboutBox.ShowDialog(this);
    }

    private List<SensorNode> GetSelectedSensorNodes(TreeNodeAdv clickedNode)
    {
        List<SensorNode> nodes = treeView.SelectedNodes
                                         .Select(node => node.Tag as SensorNode)
                                         .Where(node => node?.Sensor != null)
                                         .Distinct()
                                         .ToList();

        if (clickedNode?.Tag is SensorNode clickedSensorNode && clickedSensorNode.Sensor != null && !nodes.Contains(clickedSensorNode))
            nodes.Add(clickedSensorNode);

        return nodes;
    }

    private void SetSensorNodesVisible(IEnumerable<SensorNode> sensorNodes, bool visible)
    {
        List<SensorNode> nodes = sensorNodes.Distinct().ToList();

        treeView.BeginUpdate();
        try
        {
            foreach (SensorNode node in nodes)
                node.IsVisible = visible;
        }
        finally
        {
            treeView.EndUpdate();
        }
    }

    private void SetSensorNodesPlot(IEnumerable<SensorNode> sensorNodes, bool plot)
    {
        RunBatchedPlotChange(sensorNodes, node => node.Plot = plot);
    }

    private void SetSensorNodesPenColor(IEnumerable<SensorNode> sensorNodes, Color? color)
    {
        RunBatchedPlotChange(sensorNodes, node => node.PenColor = color);
    }

    private void RunBatchedPlotChange(IEnumerable<SensorNode> sensorNodes, Action<SensorNode> change)
    {
        List<SensorNode> nodes = sensorNodes.Distinct().ToList();

        // The Plot and PenColor setters each raise PlotSelectionChanged; suspend the per-node
        // events so a bulk change recomputes the plot once instead of once per sensor. The depth
        // counter makes nested batches compose: only the outermost scope performs the rebuild,
        // and a mutation that throws mid-batch still rebuilds from the changes already applied.
        _plotEventSuspendDepth++;
        try
        {
            foreach (SensorNode node in nodes)
                change(node);
        }
        finally
        {
            _plotEventSuspendDepth--;
            if (_plotEventSuspendDepth == 0 && _plotRebuildPending)
            {
                _plotRebuildPending = false;
                RebuildPlotSelection();
            }
        }

        treeView.Invalidate();
    }

    private void AddTreeContextMenuSeparator()
    {
        // Insert a separator only between two non-empty sections: never as the first item and
        // never directly after another separator, even if a future section contributes no items.
        if (treeContextMenu.Items.Count > 0 && treeContextMenu.Items[treeContextMenu.Items.Count - 1] is not ToolStripSeparator)
            treeContextMenu.Items.Add(new ToolStripSeparator());
    }

    private static void ClearAndDisposeMenuItems(ToolStripItemCollection items)
    {
        while (items.Count > 0)
        {
            ToolStripItem item = items[0];
            items.RemoveAt(0);
            item.Dispose();
        }
    }

    private void AddBulkMembershipMenuItems(string addText, string removeText, List<SensorNode> sensorNodes, Func<ISensor, bool> contains, Action<ISensor> add, Action<ISensor> remove)
    {
        // The add/remove items appear only when at least one selected sensor is missing/present,
        // and the per-sensor membership check keeps mixed selections idempotent on click.
        if (sensorNodes.Any(node => !contains(node.Sensor)))
        {
            ToolStripItem item = new ToolStripMenuItem(addText);
            item.Click += delegate
            {
                foreach (SensorNode node in sensorNodes)
                {
                    if (!contains(node.Sensor))
                        add(node.Sensor);
                }
            };
            treeContextMenu.Items.Add(item);
        }

        if (sensorNodes.Any(node => contains(node.Sensor)))
        {
            ToolStripItem item = new ToolStripMenuItem(removeText);
            item.Click += delegate
            {
                foreach (SensorNode node in sensorNodes)
                {
                    if (contains(node.Sensor))
                        remove(node.Sensor);
                }
            };
            treeContextMenu.Items.Add(item);
        }
    }

    private void TreeView_Click(object sender, EventArgs e)
    {
        if (!(e is MouseEventArgs m) || (m.Button != MouseButtons.Left && m.Button != MouseButtons.Right))
            return;

        NodeControlInfo info = treeView.GetNodeControlInfoAt(new Point(m.X, m.Y));
        if (m.Button == MouseButtons.Left && info.Node != null)
        {
            if (info.Node.Tag is IExpandPersistNode expandPersistNode)
            {
                expandPersistNode.Expanded = info.Node.IsExpanded;
            }
            return;
        }

        if (info.Node == null || !info.Node.IsSelected)
            treeView.SelectedNode = info.Node;

        if (info.Node != null)
            ShowNodeContextMenu(info.Node, new Point(m.X, m.Y));
    }

    private void ShowNodeContextMenu(TreeNodeAdv viewNode, Point location)
    {
        if (viewNode.Tag is SensorNode node && node.Sensor != null)
        {
            List<SensorNode> selectedSensorNodes = GetSelectedSensorNodes(viewNode);
            bool multipleSensorsSelected = selectedSensorNodes.Count > 1;
            int count = selectedSensorNodes.Count;

            ClearAndDisposeMenuItems(treeContextMenu.Items);
            if (!multipleSensorsSelected && node.Sensor.Parameters.Count > 0)
            {
                ToolStripItem item = new ToolStripMenuItem("Parameters...");
                item.Click += delegate { ShowParameterForm(node.Sensor); };
                treeContextMenu.Items.Add(item);
            }

            if (!multipleSensorsSelected && nodeTextBoxText.EditEnabled)
            {
                ToolStripItem item = new ToolStripMenuItem("Rename") { ShortcutKeyDisplayString = "F2" };
                item.Click += delegate { nodeTextBoxText.BeginEdit(); };
                treeContextMenu.Items.Add(item);
            }

            if (selectedSensorNodes.Any(selectedNode => selectedNode.IsVisible))
            {
                string text = multipleSensorsSelected ? $"Hide Selected Sensors ({count})" : "Hide";
                ToolStripItem item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = "Del" };
                item.Click += delegate { SetSensorNodesVisible(selectedSensorNodes, false); };
                treeContextMenu.Items.Add(item);
            }

            if (selectedSensorNodes.Any(selectedNode => !selectedNode.IsVisible))
            {
                string text = multipleSensorsSelected ? $"Unhide Selected Sensors ({count})" : "Unhide";
                ToolStripItem item = new ToolStripMenuItem(text);
                item.Click += delegate { SetSensorNodesVisible(selectedSensorNodes, true); };
                treeContextMenu.Items.Add(item);
            }

            AddTreeContextMenuSeparator();

            if (selectedSensorNodes.Any(selectedNode => !selectedNode.Plot))
            {
                string text = multipleSensorsSelected ? $"Add Selected to Graph ({count})" : "Add to Graph";
                ToolStripItem item = new ToolStripMenuItem(text);
                item.Click += delegate { SetSensorNodesPlot(selectedSensorNodes, true); };
                treeContextMenu.Items.Add(item);
            }

            if (selectedSensorNodes.Any(selectedNode => selectedNode.Plot))
            {
                string text = multipleSensorsSelected ? $"Remove Selected from Graph ({count})" : "Remove from Graph";
                ToolStripItem item = new ToolStripMenuItem(text);
                item.Click += delegate { SetSensorNodesPlot(selectedSensorNodes, false); };
                treeContextMenu.Items.Add(item);
            }

            {
                ToolStripItem item = new ToolStripMenuItem(multipleSensorsSelected ? $"Pen Color... ({count})" : "Pen Color...");
                item.Click += delegate
                {
                    using ColorDialog dialog = new() { Color = node.PenColor.GetValueOrDefault() };
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        SetSensorNodesPenColor(selectedSensorNodes, dialog.Color);
                };

                treeContextMenu.Items.Add(item);
            }

            {
                ToolStripItem item = new ToolStripMenuItem(multipleSensorsSelected ? $"Reset Pen Colors ({count})" : "Reset Pen Color");
                item.Click += delegate { SetSensorNodesPenColor(selectedSensorNodes, null); };
                treeContextMenu.Items.Add(item);
            }

            AddTreeContextMenuSeparator();

            if (multipleSensorsSelected)
            {
                AddBulkMembershipMenuItems($"Show Selected in Tray ({count})",
                                           $"Remove Selected from Tray ({count})",
                                           selectedSensorNodes,
                                           sensor => _systemTray.Contains(sensor),
                                           sensor => _systemTray.Add(sensor, false),
                                           sensor => _systemTray.Remove(sensor));

                if (_gadget != null)
                {
                    AddBulkMembershipMenuItems($"Show Selected in Gadget ({count})",
                                               $"Remove Selected from Gadget ({count})",
                                               selectedSensorNodes,
                                               sensor => _gadget.Contains(sensor),
                                               sensor => _gadget.Add(sensor),
                                               sensor => _gadget.Remove(sensor));
                }

                treeContextMenu.Show(treeView, location);
                return;
            }

            {
                ToolStripMenuItem item = new("Show in Tray") { Checked = _systemTray.Contains(node.Sensor) };
                item.Click += delegate
                {
                    if (item.Checked)
                        _systemTray.Remove(node.Sensor);
                    else
                        _systemTray.Add(node.Sensor, true);
                };

                treeContextMenu.Items.Add(item);
            }

            if (_gadget != null)
            {
                ToolStripMenuItem item = new("Show in Gadget") { Checked = _gadget.Contains(node.Sensor) };
                item.Click += delegate
                {
                    if (item.Checked)
                    {
                        _gadget.Remove(node.Sensor);
                    }
                    else
                    {
                        _gadget.Add(node.Sensor);
                    }
                };

                treeContextMenu.Items.Add(item);
            }

            if (node.Sensor.Control != null)
            {
                AddTreeContextMenuSeparator();
                IControl control = node.Sensor.Control;
                ToolStripMenuItem controlItem = new("Control");
                ToolStripItem defaultItem = new ToolStripMenuItem("Default") { Checked = control.ControlMode == ControlMode.Default };
                controlItem.DropDownItems.Add(defaultItem);
                defaultItem.Click += delegate { control.SetDefault(); };
                ToolStripMenuItem manualItem = new("Manual");
                controlItem.DropDownItems.Add(manualItem);
                manualItem.Checked = control.ControlMode == ControlMode.Software;
                for (int i = 0; i <= 100; i += 5)
                {
                    if (i <= control.MaxSoftwareValue &&
                        i >= control.MinSoftwareValue)
                    {
                        ToolStripMenuItem item = new ToolStripRadioButtonMenuItem(i + " %");
                        manualItem.DropDownItems.Add(item);
                        item.Checked = control.ControlMode == ControlMode.Software && Math.Round(control.SoftwareValue) == i;
                        int softwareValue = i;
                        item.Click += delegate { control.SetSoftware(softwareValue); };
                    }
                }

                treeContextMenu.Items.Add(controlItem);
            }

            treeContextMenu.Show(treeView, location);
        }

        if (viewNode.Tag is HardwareNode hardwareNode && hardwareNode.Hardware != null)
        {
            ClearAndDisposeMenuItems(treeContextMenu.Items);

            if (nodeTextBoxText.EditEnabled)
            {
                ToolStripItem item = new ToolStripMenuItem("Rename") { ShortcutKeyDisplayString = "F2" };
                item.Click += delegate { nodeTextBoxText.BeginEdit(); };
                treeContextMenu.Items.Add(item);
            }

            treeContextMenu.Show(treeView, location);
        }

        if (viewNode.Tag is TypeNode typeNode)
        {
            List<SensorNode> groupSensorNodes = typeNode.Nodes.OfType<SensorNode>()
                                                        .Where(groupNode => groupNode.Sensor != null)
                                                        .ToList();
            if (groupSensorNodes.Count == 0)
                return;

            int count = groupSensorNodes.Count;
            ClearAndDisposeMenuItems(treeContextMenu.Items);

            if (groupSensorNodes.Any(groupNode => groupNode.IsVisible))
            {
                ToolStripItem item = new ToolStripMenuItem($"Hide All in Group ({count})");
                item.Click += delegate { SetSensorNodesVisible(groupSensorNodes, false); };
                treeContextMenu.Items.Add(item);
            }

            if (groupSensorNodes.Any(groupNode => !groupNode.IsVisible))
            {
                ToolStripItem item = new ToolStripMenuItem($"Unhide All in Group ({count})");
                item.Click += delegate { SetSensorNodesVisible(groupSensorNodes, true); };
                treeContextMenu.Items.Add(item);
            }

            AddTreeContextMenuSeparator();

            if (groupSensorNodes.Any(groupNode => !groupNode.Plot))
            {
                ToolStripItem item = new ToolStripMenuItem($"Add Group to Graph ({count})");
                item.Click += delegate { SetSensorNodesPlot(groupSensorNodes, true); };
                treeContextMenu.Items.Add(item);
            }

            if (groupSensorNodes.Any(groupNode => groupNode.Plot))
            {
                ToolStripItem item = new ToolStripMenuItem($"Remove Group from Graph ({count})");
                item.Click += delegate { SetSensorNodesPlot(groupSensorNodes, false); };
                treeContextMenu.Items.Add(item);
            }

            treeContextMenu.Show(treeView, location);
        }
    }

    private void SaveReportMenuItem_Click(object sender, EventArgs e)
    {
        string report = _computer.GetReport();
        if (saveFileDialog.ShowDialog() == DialogResult.OK)
        {
            using (TextWriter w = new StreamWriter(saveFileDialog.FileName))
            {
                w.Write(report);
            }
        }
    }

    private void SysTrayHideShow()
    {
        Visible = !Visible;
        if (Visible)
            Activate();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCOMMAND = 0x112;
        const int WM_WININICHANGE = 0x001A;
        const int SC_MINIMIZE = 0xF020;
        const int SC_CLOSE = 0xF060;

        if (_minimizeToTray.Value && m.Msg == WM_SYSCOMMAND && m.WParam.ToInt64() == SC_MINIMIZE)
        {
            SysTrayHideShow();
        }
        else if (m.Msg == WM_WININICHANGE && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet" && _autoThemeMenuItem?.Checked == true)
        {
            Theme.SetAutoTheme();
        }
        else if (_minimizeOnClose.Value && m.Msg == WM_SYSCOMMAND && m.WParam.ToInt64() == SC_CLOSE)
        {
            //Apparently the user wants to minimize rather than close
            //Now we still need to check if we're going to the tray or not
            //Note: the correct way to do this would be to send out SC_MINIMIZE,
            //but since the code here is so simple,
            //that would just be a waste of time.
            if (_minimizeToTray.Value)
                SysTrayHideShow();
            else
                WindowState = FormWindowState.Minimized;
        }
        else
        {
            base.WndProc(ref m);
        }
    }

    private void HideShowClick(object sender, EventArgs e)
    {
        SysTrayHideShow();
    }

    private void ShowParameterForm(ISensor sensorForm)
    {
        using ParameterForm form = new() { Parameters = sensorForm.Parameters, captionLabel = { Text = sensorForm.Name } };
        form.ShowDialog(this);
    }

    private void TreeView_NodeMouseDoubleClick(object sender, TreeNodeAdvMouseEventArgs e)
    {
        if (e.Node.Tag is SensorNode node && node.Sensor != null && node.Sensor.Parameters.Count > 0)
            ShowParameterForm(node.Sensor);
    }

    private void CelsiusMenuItem_Click(object sender, EventArgs e)
    {
        celsiusMenuItem.Checked = true;
        fahrenheitMenuItem.Checked = false;
        _unitManager.TemperatureUnit = TemperatureUnit.Celsius;
    }

    private void FahrenheitMenuItem_Click(object sender, EventArgs e)
    {
        celsiusMenuItem.Checked = false;
        fahrenheitMenuItem.Checked = true;
        _unitManager.TemperatureUnit = TemperatureUnit.Fahrenheit;
    }

    private void ResetMinMaxMenuItem_Click(object sender, EventArgs e)
    {
        _computer.Accept(new SensorVisitor(delegate(ISensor sensorClick)
        {
            sensorClick.ResetMin();
            sensorClick.ResetMax();
        }));
    }

    private void ExpandAllNodes_Click(object sender, EventArgs e)
    {
        treeView.ExpandAll();

        foreach (var node in treeView.AllNodes)
        {
            if (node.Tag is IExpandPersistNode expandPersistNode)
            {
                expandPersistNode.Expanded = true;
            }
        }
    }

    private void CollapseAllNodes_Click(object sender, EventArgs e)
    {
        treeView.CollapseAll();

        foreach (var node in treeView.AllNodes)
        {
            if (node.Tag is IExpandPersistNode expandPersistNode)
            {
                expandPersistNode.Expanded = false;
            }
        }
    }

    private void resetPlotMenuItem_Click(object sender, EventArgs e)
    {
        _computer.Accept(new SensorVisitor(delegate (ISensor sensorClick)
        {
            sensorClick.ClearValues();
        }));
    }

    private void MainForm_MoveOrResize(object sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
            return;

        Rectangle b = WindowState == FormWindowState.Maximized ? RestoreBounds : Bounds;
        _settings.SetValue("mainForm.Location.X", b.X);
        _settings.SetValue("mainForm.Location.Y", b.Y);
        _settings.SetValue("mainForm.Width", b.Width);
        _settings.SetValue("mainForm.Height", b.Height);
        _settings.SetValue("mainForm.Maximized", WindowState == FormWindowState.Maximized);
    }

    private void ResetClick(object sender, EventArgs e)
    {
        BeginHardwareReset();
    }

    private void TreeView_MouseMove(object sender, MouseEventArgs e)
    {
        _selectionDragging &= (e.Button & MouseButtons.Left) > 0;
        if (_selectionDragging)
        {
            // Over empty area GetNodeAt returns null and assigning it would clear the whole
            // selection mid-drag; keep the last hovered row selected instead.
            TreeNodeAdv hit = treeView.GetNodeAt(e.Location);
            if (hit != null)
                treeView.SelectedNode = hit;
        }
    }

    private void TreeView_MouseDown(object sender, MouseEventArgs e)
    {
        // Swipe-select must not start on Ctrl/Shift-modified presses or on a press over an
        // already-selected row, otherwise a 1-pixel drag collapses a multi-selection to one node.
        _selectionDragging = e.Button == MouseButtons.Left &&
                             (ModifierKeys & (Keys.Control | Keys.Shift)) == Keys.None &&
                             treeView.GetNodeAt(e.Location)?.IsSelected != true;
    }

    private void TreeView_MouseUp(object sender, MouseEventArgs e)
    {
        _selectionDragging = false;
    }

    private void TreeView_SizeChanged(object sender, EventArgs e)
    {
        // Keep a stable native-width gutter for the vertical scrollbar. The themed indicator now
        // mirrors the real scrollbar instead of collapsing it to zero, so sizing columns against
        // the full control width would create a horizontal scrollbar as soon as vertical overflow
        // appears. Reserving the gutter at all times also prevents a visible column-width jump.
        int newWidth = GetSensorTreeColumnViewportWidth();
        for (int i = 1; i < treeView.Columns.Count; i++)
        {
            if (treeView.Columns[i].IsVisible)
                newWidth -= treeView.Columns[i].Width;
        }
        treeView.Columns[0].Width = newWidth;
    }

    private int GetSensorTreeColumnViewportWidth()
    {
        return Math.Max(0, treeView.ClientSize.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth);
    }

    private void TreeView_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space:
                // Only a plain Space is the plot-toggle verb. Leave Alt+Space (window system menu),
                // Ctrl+Space and Shift+Space (selection) to the framework so they are neither
                // suppressed nor mistaken for a toggle.
                if (e.Modifiers != Keys.None)
                    return;

                // Plain Space is always consumed here so Aga's NodeCheckBox never sees it: with the
                // graph hidden that blocks an invisible selection-wide Plot change, and with it shown
                // the batched toggle below replaces N per-node toggles (and N recomputes) with one.
                if (plotMenuItem.Checked)
                    TogglePlotForSelectedSensors();

                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            case Keys.Delete:
            {
                List<SensorNode> selectedSensorNodes = GetSelectedSensorNodes(null);
                if (selectedSensorNodes.Count > 0)
                {
                    SetSensorNodesVisible(selectedSensorNodes, false);
                    e.Handled = true;
                }

                return;
            }
            case Keys.Apps:
            case Keys.F10 when e.Shift:
            {
                TreeNodeAdv selectedNode = treeView.SelectedNode;
                if (selectedNode != null)
                {
                    // Scroll a selection that was navigated off-screen back into view first, so the
                    // menu anchors to the actual row instead of clamped client-area coordinates.
                    treeView.EnsureVisible(selectedNode);
                    Rectangle bounds = treeView.GetNodeBoundsInClient(selectedNode);
                    int x = Math.Max(0, Math.Min(bounds.Left, treeView.ClientSize.Width));
                    int y = Math.Max(0, Math.Min(bounds.Bottom, treeView.ClientSize.Height));
                    ShowNodeContextMenu(selectedNode, new Point(x, y));
                    e.Handled = true;
                }

                return;
            }
        }

        if (treeView.SelectedNode != null)
        {
            switch (e.KeyCode)
            {
                case Keys.Right:
                    if (treeView.SelectedNode.Tag is IExpandPersistNode expandPersistNodeR)
                    {
                        expandPersistNodeR.Expanded = true;
                    }
                    return;
                case Keys.Left:
                    if (treeView.SelectedNode.Tag is IExpandPersistNode expandPersistNodeL)
                    {
                        expandPersistNodeL.Expanded = false;
                    }
                    return;
            }
        }
    }

    private void TreeView_ColumnWidthChanged(TreeColumn column)
    {
        if (_updatingSensorTreeLayout)
            return;

        int changedIndex = treeView.Columns.IndexOf(column);
        if (changedIndex == 1) _baseValueColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);
        else if (changedIndex == 2) _baseMinColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);
        else if (changedIndex == 3) _baseMaxColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);

        int index = treeView.Columns.IndexOf(column);
        int columnsWidth = 0;
        foreach (TreeColumn treeColumn in treeView.Columns)
        {
            if (treeColumn.IsVisible)
                columnsWidth += treeColumn.Width;
        }

        int nextColumnIndex = index + 1;
        while (nextColumnIndex < treeView.Columns.Count && treeView.Columns[nextColumnIndex].IsVisible == false)
            nextColumnIndex++;

        if (nextColumnIndex < treeView.Columns.Count) {
            int diff = GetSensorTreeColumnViewportWidth() - columnsWidth;
            treeView.Columns[nextColumnIndex].Width = Math.Max(20, treeView.Columns[nextColumnIndex].Width + diff);
        }
    }

    private void ServerInterfacePortMenuItem_Click(object sender, EventArgs e)
    {
        using InterfacePortForm form = new(this);
        form.ShowDialog(this);
    }

    private void AuthWebServerMenuItem_Click(object sender, EventArgs e)
    {
        using AuthForm form = new(this);
        form.ShowDialog(this);
    }

    private void perSessionFileRotationMenuItem_Click(object sender, EventArgs e)
    {
        dailyFileRotationMenuItem.Checked = false;
        perSessionFileRotationMenuItem.Checked = true;
        _logger.FileRotationMethod = LoggerFileRotation.PerSession;
        _settings.SetValue("logger.fileRotation", (int)LoggerFileRotation.PerSession);
    }

    private void dailyFileRotationMenuItem_Click(object sender, EventArgs e)
    {
        dailyFileRotationMenuItem.Checked = true;
        perSessionFileRotationMenuItem.Checked = false;
        _logger.FileRotationMethod = LoggerFileRotation.Daily;
        _settings.SetValue("logger.fileRotation", (int)LoggerFileRotation.Daily);
    }
}
