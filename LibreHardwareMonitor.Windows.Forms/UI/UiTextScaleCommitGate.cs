// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Pure, side-effect-free commit policy for the "Text Size" slider (see <see cref="UiScale"/>).
/// Dragging the slider fires a value change per tick; applying the full UI scale on every tick
/// re-lays out the whole app and — worse — mutates the menu strip the slider lives in, so the
/// open dropdown jitters under the cursor. The gate decides when the debounced heavy pass may
/// run (drag paused) and defers all menu-strip mutations until the dropdown has closed.
/// </summary>
public sealed class UiTextScaleCommitGate
{
    public enum Commit
    {
        /// <summary>Nothing pending; apply nothing.</summary>
        None,

        /// <summary>Apply tree/plot/column scaling now; leave the menu strip untouched.</summary>
        ScaleOnly,

        /// <summary>Apply everything, including menu font and menu item text.</summary>
        Full
    }

    private bool _scalePending;
    private bool _menuRefreshPending;

    /// <summary>The slider produced a new value; the caller restarts its debounce timer.</summary>
    public void OnSliderTick()
    {
        _scalePending = true;
    }

    /// <summary>The debounce timer elapsed (drag paused or ended).</summary>
    public Commit OnDebounceElapsed(bool menuOpen)
    {
        if (!_scalePending)
            return Commit.None;

        _scalePending = false;

        if (menuOpen)
        {
            _menuRefreshPending = true;
            return Commit.ScaleOnly;
        }

        _menuRefreshPending = false;
        return Commit.Full;
    }

    /// <summary>The dropdown hosting the slider closed; the caller stops its debounce timer.</summary>
    public Commit OnMenuClosed()
    {
        if (!_scalePending && !_menuRefreshPending)
            return Commit.None;

        _scalePending = false;
        _menuRefreshPending = false;
        return Commit.Full;
    }
}
