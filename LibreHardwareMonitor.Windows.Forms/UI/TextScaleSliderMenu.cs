// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// A "{Title} (N%)" menu item hosting a debounced percent slider with a fixed-size readout.
/// Per tick only the readout text changes; heavy work is requested via <see cref="CommitRequested"/>
/// after a drag pause (menu open → deferMenuRefresh=true) or when the owning dropdown closes
/// (deferMenuRefresh=false). Policy lives in <see cref="UiTextScaleCommitGate"/>.
/// </summary>
internal sealed class TextScaleSliderMenu : IDisposable
{
    private const int CommitDelayMilliseconds = 150;

    private readonly string _titlePrefix;
    private readonly UiTextScaleCommitGate _gate = new();
    private readonly System.Windows.Forms.Timer _commitTimer;
    private ToolStripDropDown _parentDropDown;

    public ToolStripMenuItem MenuItem { get; }
    public ToolStripLabel Readout { get; }
    public TrackBar Slider { get; }

    /// <summary>Fires per slider tick with the clamped percent; keep handlers cheap.</summary>
    public event Action<int> PercentChanged;

    /// <summary>Fires when the debounce policy wants the heavy scale applied (arg = deferMenuRefresh).</summary>
    public event Action<bool> CommitRequested;

    public TextScaleSliderMenu(string titlePrefix, int initialPercent, double dpiScale, Font menuFont, Color menuBackColor)
    {
        _titlePrefix = titlePrefix;
        int percent = UiScale.ClampPercent(initialPercent);
        MenuItem = new ToolStripMenuItem($"{titlePrefix} ({percent}%)");

        Slider = new TrackBar
        {
            Minimum = UiScale.MinPercent,
            Maximum = UiScale.MaxPercent,
            TickFrequency = 25,
            SmallChange = 5,
            LargeChange = 25,
            AutoSize = false,
            Value = percent,
            Size = new Size((int)Math.Round(170 * dpiScale), (int)Math.Round(45 * dpiScale)),
            BackColor = menuBackColor
        };

        // Fixed-size so per-tick text updates never re-layout the open dropdown.
        Readout = new ToolStripLabel($"{percent}%")
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(Slider.Width, menuFont.Height + 6)
        };
        MenuItem.DropDownItems.Add(Readout);

        ToolStripControlHost host = new(Slider) { AutoSize = false, BackColor = menuBackColor };
        host.Size = Slider.Size;
        MenuItem.DropDownItems.Add(host);

        _commitTimer = new System.Windows.Forms.Timer { Interval = CommitDelayMilliseconds };
        _commitTimer.Tick += (s, e) =>
        {
            _commitTimer.Stop();
            Raise(_gate.OnDebounceElapsed(menuOpen: _parentDropDown?.Visible == true));
        };

        Slider.ValueChanged += (s, e) =>
        {
            int value = UiScale.ClampPercent(Slider.Value);
            Readout.Text = $"{value}%";
            PercentChanged?.Invoke(value);
            _gate.OnSliderTick();
            _commitTimer.Stop();
            _commitTimer.Start();
        };

        // Keep the dropdown open while dragging the slider (a drag is not an ItemClicked).
        MenuItem.DropDown.Closing += (s, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                e.Cancel = true;
        };
    }

    /// <summary>Insert into the parent menu and wire the deferred menu refresh to its dropdown closing.</summary>
    public void InstallAt(ToolStripMenuItem parentMenuItem, int index)
    {
        _parentDropDown = parentMenuItem.DropDown;
        parentMenuItem.DropDownItems.Insert(index, MenuItem);
        _parentDropDown.Closed += (s, e) =>
        {
            _commitTimer.Stop();
            Raise(_gate.OnMenuClosed());
        };
    }

    /// <summary>Menu-strip half of a full commit: parent label text + readout height for the new menu font.</summary>
    public void RefreshMenuText(int percent, Font menuFont)
    {
        MenuItem.Text = $"{_titlePrefix} ({UiScale.ClampPercent(percent)}%)";
        Readout.Size = new Size(Readout.Width, menuFont.Height + 6);
    }

    private void Raise(UiTextScaleCommitGate.Commit commit)
    {
        if (commit == UiTextScaleCommitGate.Commit.None)
            return;

        CommitRequested?.Invoke(commit == UiTextScaleCommitGate.Commit.ScaleOnly);
    }

    public void Dispose()
    {
        _commitTimer.Dispose();
        Slider.Dispose();
        MenuItem.Dispose();
    }
}
