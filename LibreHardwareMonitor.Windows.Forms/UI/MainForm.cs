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
    private bool _suspendPlotSelectionChanged;
    private GridLineStyle _standardGridLineStyle;
    private int _standardRowHeight;
    private int _standardValueColumnWidth;
    private int _standardMinColumnWidth;
    private int _standardMaxColumnWidth;

    public MainForm()
    {
        InitializeComponent();

        _settings = new PersistentSettings();
        _settings.Load(Path.ChangeExtension(Application.ExecutablePath, ".config"));

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
            Width = _settings.GetValue("mainForm.Width", 470),
            Height = _settings.GetValue("mainForm.Height", 640)
        };

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

        _systemTray = new SystemTray(_computer, _settings, _unitManager);
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
            _gadget = new SensorGadget(_computer, _settings, _unitManager);
            _gadget.HideShowCommand += HideShowClick;
        }

        _standardRowHeight = treeView.RowHeight;
        _standardGridLineStyle = treeView.GridLineStyle;
        _standardValueColumnWidth = treeView.Columns[1].Width;
        _standardMinColumnWidth = treeView.Columns[2].Width;
        _standardMaxColumnWidth = treeView.Columns[3].Width;

        InitializeGraphMenu();

        ToolStripMenuItem compactModeMenuItem = new("Compact Mode");
        viewMenuItem.DropDownItems.Insert(4, compactModeMenuItem);
        _compactMode = new UserOption("compactMode", false, compactModeMenuItem, _settings);
        _compactMode.Changed += delegate { ApplySensorTreeLayout(); };

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

        if (PawnIo.PawnIo.IsInstalled)
        {
            if (PawnIo.PawnIo.Version < new Version(2, 0, 0, 0))
            {
                DialogResult result = MessageBox.Show("PawnIO is outdated, do you want to update it?", nameof(LibreHardwareMonitor), MessageBoxButtons.OKCancel);
                if (result == DialogResult.OK)
                    InstallPawnIO();
            }
        }
        else
        {
            DialogResult result = MessageBox.Show("PawnIO is not installed, do you want to install it?", nameof(LibreHardwareMonitor), MessageBoxButtons.OKCancel);
            if (result == DialogResult.OK)
                InstallPawnIO();
        }

        _computer.Open();

        static void InstallPawnIO()
        {
            string path = ExtractPawnIO();
            if (!string.IsNullOrEmpty(path))
            {
                var process = Process.Start(new ProcessStartInfo(path, "-install"));
                process?.WaitForExit();

                File.Delete(path);
            }
        }

        static string ExtractPawnIO()
        {
            string destination = Path.Combine(Directory.GetCurrentDirectory(), "PawnIO_setup.exe");

            try
            {

                using Stream resourceStream = typeof(MainForm).Assembly.GetManifestResourceStream("LibreHardwareMonitor.Resources.PawnIO_setup.exe");
                using FileStream fileStream = new(destination, FileMode.Create, FileAccess.Write);
                resourceStream.CopyTo(fileStream);

                return destination;
            }
            catch
            {
                return null;
            }
        }

        backgroundUpdater.DoWork += BackgroundUpdater_DoWork;
        timer.Enabled = true;

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
        ApplySensorTreeLayout();

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
        _readMainboardSensors.Changed += delegate { _computer.IsMotherboardEnabled = _readMainboardSensors.Value; };

        _readCpuSensors = new UserOption("cpuMenuItem", true, cpuMenuItem, _settings);
        _readCpuSensors.Changed += delegate { _computer.IsCpuEnabled = _readCpuSensors.Value; };

        _readRamSensors = new UserOption("ramMenuItem", true, ramMenuItem, _settings);
        _readRamSensors.Changed += delegate { _computer.IsMemoryEnabled = _readRamSensors.Value; };

        _readGpuSensors = new UserOption("gpuMenuItem", true, gpuMenuItem, _settings);
        _readGpuSensors.Changed += delegate { _computer.IsGpuEnabled = _readGpuSensors.Value; };

        _readPowerMonitorSensors = new UserOption("powerMonitorMenuItem", true, powerMonitorMenuItem, _settings);
        _readPowerMonitorSensors.Changed += delegate { _computer.IsPowerMonitorEnabled = _readPowerMonitorSensors.Value; };

        _readFanControllersSensors = new UserOption("fanControllerMenuItem", true, fanControllerMenuItem, _settings);
        _readFanControllersSensors.Changed += delegate { _computer.IsControllerEnabled = _readFanControllersSensors.Value; };

        _readHddSensors = new UserOption("hddMenuItem", true, hddMenuItem, _settings);
        _readHddSensors.Changed += delegate { _computer.IsStorageEnabled = _readHddSensors.Value; };

        _readNicSensors = new UserOption("nicMenuItem", true, nicMenuItem, _settings);
        _readNicSensors.Changed += delegate { _computer.IsNetworkEnabled = _readNicSensors.Value; };

        _readPsuSensors = new UserOption("psuMenuItem", true, psuMenuItem, _settings);
        _readPsuSensors.Changed += delegate { _computer.IsPsuEnabled = _readPsuSensors.Value; };

        _readBatterySensors = new UserOption("batteryMenuItem", true, batteryMenuItem, _settings);
        _readBatterySensors.Changed += delegate { _computer.IsBatteryEnabled = _readBatterySensors.Value; };

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
        _runWebServer.Changed += delegate
        {
            if (_runWebServer.Value)
                Server.StartHttpListener();
            else
                Server.StopHttpListener();
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
            TimeSpan timeWindow = TimeSpan.Zero;
            switch (_sensorValuesTimeWindow.Value)
            {
                case 0:
                    timeWindow = new TimeSpan(0, 0, 30);
                    break;
                case 1:
                    timeWindow = new TimeSpan(0, 1, 0);
                    break;
                case 2:
                    timeWindow = new TimeSpan(0, 2, 0);
                    break;
                case 3:
                    timeWindow = new TimeSpan(0, 5, 0);
                    break;
                case 4:
                    timeWindow = new TimeSpan(0, 10, 0);
                    break;
                case 5:
                    timeWindow = new TimeSpan(0, 30, 0);
                    break;
                case 6:
                    timeWindow = new TimeSpan(1, 0, 0);
                    break;
                case 7:
                    timeWindow = new TimeSpan(2, 0, 0);
                    break;
                case 8:
                    timeWindow = new TimeSpan(6, 0, 0);
                    break;
                case 9:
                    timeWindow = new TimeSpan(12, 0, 0);
                    break;
                case 10:
                    timeWindow = new TimeSpan(24, 0, 0);
                    break;
            }

            _computer.Accept(new SensorVisitor(delegate(ISensor sensor) { sensor.ValuesTimeWindow = timeWindow; }));
        };

        InitializeTheme();
        InitializePlotForm();
        InitializeSplitter();

        startupMenuItem.Visible = _startupManager.IsAvailable;

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

        // Create a handle, otherwise calling Close() does not fire FormClosed

        // Make sure the settings are saved when the user logs off
        Microsoft.Win32.SystemEvents.SessionEnded += delegate
        {
            _computer.Close();
            SaveConfiguration();
            if (_runWebServer.Value)
                Server.Quit();
        };

        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerModeChanged;
    }

    private void StopFileHardwareMenuFromClosing(object sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
        {
            e.Cancel = true;
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

        _plotPanel.InvalidatePlot();
    }

    private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            _computer.Reset();
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
        sensorValuesTimeWindowMenuItem.Text = "&Time Window";
        plotLocationMenuItem.Text = "Graph &Location";
        strokeThicknessMenuItem.Text = "&Stroke Thickness";

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
    }

    private static void MoveMenuItem(ToolStripItemCollection target, ToolStripItem item)
    {
        item.Owner?.Items.Remove(item);
        target.Add(item);
    }

    private void ShowGraphInputsForm()
    {
        // The dialog drives many SensorNode.Plot changes and each one raises PlotSelectionChanged.
        // Suppress those per-node events for the dialog's lifetime and let its callback below trigger
        // exactly one recompute per user action (it temporarily lifts the suppression to do the work).
        _suspendPlotSelectionChanged = true;
        try
        {
            using GraphInputsForm form = new(GetAllSensorNodes(), delegate
            {
                bool previous = _suspendPlotSelectionChanged;
                _suspendPlotSelectionChanged = false;
                try
                {
                    PlotSelectionChanged(this, EventArgs.Empty);
                }
                finally
                {
                    _suspendPlotSelectionChanged = previous;
                }

                treeView.Invalidate();
            });
            form.ShowDialog(this);
        }
        finally
        {
            _suspendPlotSelectionChanged = false;
        }
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

    private IEnumerable<SensorNode> GetAllSensorNodes()
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
                treeView.RowHeight = Math.Max(treeView.Font.Height, 16);
                treeView.GridLineStyle = GridLineStyle.None;
                treeView.Columns[1].Width = Math.Min(_standardValueColumnWidth, 78);
            }
            else
            {
                treeView.RowHeight = _standardRowHeight;
                treeView.GridLineStyle = _standardGridLineStyle;

                // Restore the saved column widths only when actually leaving compact mode
                // (_compactLayoutActive still holds the previous state here; it is updated below).
                // Re-applying them on every Show Value/Min/Max toggle would clobber widths the user
                // dragged in normal mode, since _standard* is only refreshed when entering compact mode.
                if (_compactLayoutActive)
                {
                    treeView.Columns[1].Width = _standardValueColumnWidth;
                    treeView.Columns[2].Width = _standardMinColumnWidth;
                    treeView.Columns[3].Width = _standardMaxColumnWidth;
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
        HardwareNode hardwareNode = new(hardware, _settings, _unitManager);
        hardwareNode.PlotSelectionChanged += PlotSelectionChanged;
        InsertSorted(node.Nodes, hardwareNode);
        foreach (IHardware subHardware in hardware.SubHardware)
            SubHardwareAdded(subHardware, hardwareNode);
    }

    private void HardwareAdded(IHardware hardware)
    {
        SubHardwareAdded(hardware, _root);
        PlotSelectionChanged(this, null);
    }

    private void HardwareRemoved(IHardware hardware)
    {
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
        }

        PlotSelectionChanged(this, null);
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
        // While the Graph Inputs dialog is open it batches changes and triggers exactly one recompute
        // per user action (see ShowGraphInputsForm); suppress the per-node Plot events it generates so
        // a single toggle or bulk Select/Clear does not fan out into N full recomputes.
        if (_suspendPlotSelectionChanged)
            return;

        List<ISensor> selected = new();
        IDictionary<ISensor, Color> colors = new Dictionary<ISensor, Color>();
        int colorIndex = 0;

        foreach (TreeNodeAdv node in treeView.AllNodes)
        {
            if (node.Tag is SensorNode sensorNode)
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

        foreach (TreeNodeAdv node in treeView.AllNodes)
        {
            if (node.Tag is SensorNode sensorNode && sensorNode.Plot && sensorNode.PenColor.HasValue)
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
        treeView.Invalidate();
        _systemTray.Redraw();
        _gadget?.Redraw();

        if (!backgroundUpdater.IsBusy)
            backgroundUpdater.RunWorkerAsync();
    }

    private void SaveConfiguration()
    {
        if (_plotPanel == null || _settings == null)
            return;

        _plotPanel.SetCurrentSettings();

        foreach (TreeColumn column in treeView.Columns)
            _settings.SetValue("treeView.Columns." + column.Header + ".Width", column.Width);

        _settings.SetValue("listenerIp", Server.ListenerIp);
        _settings.SetValue("listenerPort", Server.ListenerPort);
        _settings.SetValue("authenticationEnabled", Server.AuthEnabled);
        _settings.SetValue("authenticationUserName", Server.UserName);
        _settings.SetValue("authenticationPassword", Server.Password);

        if (_compactLayoutActive)
        {
            _settings.SetValue("treeView.Columns." + treeView.Columns[1].Header + ".Width", _standardValueColumnWidth);
            _settings.SetValue("treeView.Columns." + treeView.Columns[2].Header + ".Width", _standardMinColumnWidth);
            _settings.SetValue("treeView.Columns." + treeView.Columns[3].Header + ".Width", _standardMaxColumnWidth);
        }

        string fileName = Path.ChangeExtension(Application.ExecutablePath, ".config");

        try
        {
            _settings.Save(fileName);
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
            Width = _settings.GetValue("mainForm.Width", 470),
            Height = _settings.GetValue("mainForm.Height", 640)
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

        RestoreCollapsedNodeState(treeView);

        FormClosed += MainForm_FormClosed;
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

    private void CloseApplication()
    {
        FormClosed -= MainForm_FormClosed;

        Visible = false;
        _systemTray.IsMainIconEnabled = false;
        timer.Enabled = false;
        _computer.Close();
        SaveConfiguration();
        if (_runWebServer.Value)
            Server.Quit();

        _systemTray.Dispose();
        timer.Dispose();
        backgroundUpdater.Dispose();

        Application.Exit();
    }

    private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        CloseApplication();
    }

    private void AboutMenuItem_Click(object sender, EventArgs e)
    {
        _ = new AboutBox().ShowDialog();
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
        // events so a bulk change recomputes the plot once instead of once per sensor.
        _suspendPlotSelectionChanged = true;
        try
        {
            foreach (SensorNode node in nodes)
                change(node);
        }
        finally
        {
            _suspendPlotSelectionChanged = false;
        }

        PlotSelectionChanged(this, EventArgs.Empty);
        treeView.Invalidate();
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
        {
            if (viewNode.Tag is SensorNode node && node.Sensor != null)
            {
                List<SensorNode> selectedSensorNodes = GetSelectedSensorNodes(viewNode);
                bool multipleSensorsSelected = selectedSensorNodes.Count > 1;
                int count = selectedSensorNodes.Count;

                treeContextMenu.Items.Clear();
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

                treeContextMenu.Items.Add(new ToolStripSeparator());

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
                        ColorDialog dialog = new() { Color = node.PenColor.GetValueOrDefault() };
                        if (dialog.ShowDialog() == DialogResult.OK)
                            SetSensorNodesPenColor(selectedSensorNodes, dialog.Color);
                    };

                    treeContextMenu.Items.Add(item);
                }

                {
                    ToolStripItem item = new ToolStripMenuItem(multipleSensorsSelected ? $"Reset Pen Colors ({count})" : "Reset Pen Color");
                    item.Click += delegate { SetSensorNodesPenColor(selectedSensorNodes, null); };
                    treeContextMenu.Items.Add(item);
                }

                treeContextMenu.Items.Add(new ToolStripSeparator());

                if (multipleSensorsSelected)
                {
                    if (selectedSensorNodes.Any(selectedNode => !_systemTray.Contains(selectedNode.Sensor)))
                    {
                        ToolStripItem item = new ToolStripMenuItem($"Show Selected in Tray ({count})");
                        item.Click += delegate
                        {
                            foreach (SensorNode selectedNode in selectedSensorNodes)
                            {
                                if (!_systemTray.Contains(selectedNode.Sensor))
                                    _systemTray.Add(selectedNode.Sensor, false);
                            }
                        };
                        treeContextMenu.Items.Add(item);
                    }

                    if (selectedSensorNodes.Any(selectedNode => _systemTray.Contains(selectedNode.Sensor)))
                    {
                        ToolStripItem item = new ToolStripMenuItem($"Remove Selected from Tray ({count})");
                        item.Click += delegate
                        {
                            foreach (SensorNode selectedNode in selectedSensorNodes)
                            {
                                if (_systemTray.Contains(selectedNode.Sensor))
                                    _systemTray.Remove(selectedNode.Sensor);
                            }
                        };
                        treeContextMenu.Items.Add(item);
                    }

                    if (_gadget != null)
                    {
                        if (selectedSensorNodes.Any(selectedNode => !_gadget.Contains(selectedNode.Sensor)))
                        {
                            ToolStripItem item = new ToolStripMenuItem($"Show Selected in Gadget ({count})");
                            item.Click += delegate
                            {
                                foreach (SensorNode selectedNode in selectedSensorNodes)
                                {
                                    if (!_gadget.Contains(selectedNode.Sensor))
                                        _gadget.Add(selectedNode.Sensor);
                                }
                            };
                            treeContextMenu.Items.Add(item);
                        }

                        if (selectedSensorNodes.Any(selectedNode => _gadget.Contains(selectedNode.Sensor)))
                        {
                            ToolStripItem item = new ToolStripMenuItem($"Remove Selected from Gadget ({count})");
                            item.Click += delegate
                            {
                                foreach (SensorNode selectedNode in selectedSensorNodes)
                                {
                                    if (_gadget.Contains(selectedNode.Sensor))
                                        _gadget.Remove(selectedNode.Sensor);
                                }
                            };
                            treeContextMenu.Items.Add(item);
                        }
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
                    treeContextMenu.Items.Add(new ToolStripSeparator());
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
                treeContextMenu.Items.Clear();

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
                treeContextMenu.Items.Clear();

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

                treeContextMenu.Items.Add(new ToolStripSeparator());

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
        ParameterForm form = new() { Parameters = sensorForm.Parameters, captionLabel = { Text = sensorForm.Name } };
        form.ShowDialog();
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
        if (WindowState != FormWindowState.Minimized)
        {
            _settings.SetValue("mainForm.Location.X", Bounds.X);
            _settings.SetValue("mainForm.Location.Y", Bounds.Y);
            _settings.SetValue("mainForm.Width", Bounds.Width);
            _settings.SetValue("mainForm.Height", Bounds.Height);
        }
    }

    private void ResetClick(object sender, EventArgs e)
    {
        // disable the fallback MainIcon during reset, otherwise icon visibility
        // might be lost
        _systemTray.IsMainIconEnabled = false;
        _computer.Reset();
        // restore the MainIcon setting
        _systemTray.IsMainIconEnabled = _minimizeToTray.Value;
    }

    private void TreeView_MouseMove(object sender, MouseEventArgs e)
    {
        _selectionDragging &= (e.Button & MouseButtons.Left) > 0;
        if (_selectionDragging)
            treeView.SelectedNode = treeView.GetNodeAt(e.Location);
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
        int newWidth = treeView.Width;
        for (int i = 1; i < treeView.Columns.Count; i++)
        {
            if (treeView.Columns[i].IsVisible)
                newWidth -= treeView.Columns[i].Width;
        }
        treeView.Columns[0].Width = newWidth;
    }

    private void TreeView_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space:
                // The plot checkbox toggles Plot for the whole selection on Space even while the
                // checkbox column is hidden (graph off); block the invisible state change.
                if (!plotMenuItem.Checked)
                    e.Handled = true;
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
                    Rectangle bounds = treeView.GetNodeBounds(selectedNode);
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
            int diff = treeView.Width - columnsWidth;
            treeView.Columns[nextColumnIndex].Width = Math.Max(20, treeView.Columns[nextColumnIndex].Width + diff);
        }
    }

    private void ServerInterfacePortMenuItem_Click(object sender, EventArgs e)
    {
        new InterfacePortForm(this).ShowDialog();
    }

    private void AuthWebServerMenuItem_Click(object sender, EventArgs e)
    {
        new AuthForm(this).ShowDialog();
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
