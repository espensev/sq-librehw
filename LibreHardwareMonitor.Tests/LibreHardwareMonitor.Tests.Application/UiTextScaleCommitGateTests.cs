// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Policy tests for the Text Size slider commit gate: heavy UI scaling is debounced
/// (committed when the drag pauses), and menu-strip mutations are deferred until the
/// dropdown hosting the slider has closed, so the open menu never re-lays out mid-drag.
/// </summary>
public class UiTextScaleCommitGateTests
{
    [Fact]
    public void DebounceElapsed_MenuClosed_CommitsFull()
    {
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();

        Assert.Equal(UiTextScaleCommitGate.Commit.Full, gate.OnDebounceElapsed(menuOpen: false));
    }

    [Fact]
    public void DebounceElapsed_MenuOpen_CommitsScaleOnly_AndDefersMenuRefresh()
    {
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();

        Assert.Equal(UiTextScaleCommitGate.Commit.ScaleOnly, gate.OnDebounceElapsed(menuOpen: true));
        Assert.Equal(UiTextScaleCommitGate.Commit.Full, gate.OnMenuClosed());
    }

    [Fact]
    public void MenuClosed_BeforeDebounceElapsed_CommitsFull()
    {
        // User drags and immediately closes the menu: the pending scale must not be lost.
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();

        Assert.Equal(UiTextScaleCommitGate.Commit.Full, gate.OnMenuClosed());
    }

    [Fact]
    public void MenuClosed_WithNothingPending_CommitsNothing()
    {
        var gate = new UiTextScaleCommitGate();

        Assert.Equal(UiTextScaleCommitGate.Commit.None, gate.OnMenuClosed());
    }

    [Fact]
    public void DebounceElapsed_WithNothingPending_CommitsNothing()
    {
        var gate = new UiTextScaleCommitGate();

        Assert.Equal(UiTextScaleCommitGate.Commit.None, gate.OnDebounceElapsed(menuOpen: false));
    }

    [Fact]
    public void DebounceElapsed_Twice_WithoutNewTick_SecondIsNone()
    {
        // A spurious timer fire after a ScaleOnly commit must not re-run the heavy pass;
        // the deferred menu refresh stays pending until the menu closes.
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();

        Assert.Equal(UiTextScaleCommitGate.Commit.ScaleOnly, gate.OnDebounceElapsed(menuOpen: true));
        Assert.Equal(UiTextScaleCommitGate.Commit.None, gate.OnDebounceElapsed(menuOpen: true));
        Assert.Equal(UiTextScaleCommitGate.Commit.Full, gate.OnMenuClosed());
    }

    [Fact]
    public void FullCommit_ClearsAllPendingState()
    {
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();
        gate.OnDebounceElapsed(menuOpen: false);

        Assert.Equal(UiTextScaleCommitGate.Commit.None, gate.OnMenuClosed());
        Assert.Equal(UiTextScaleCommitGate.Commit.None, gate.OnDebounceElapsed(menuOpen: false));
    }

    [Fact]
    public void NewTick_AfterScaleOnlyCommit_DebouncesAgain()
    {
        // Drag, pause (ScaleOnly), drag again, pause again: a second live preview commit.
        var gate = new UiTextScaleCommitGate();
        gate.OnSliderTick();
        gate.OnDebounceElapsed(menuOpen: true);
        gate.OnSliderTick();

        Assert.Equal(UiTextScaleCommitGate.Commit.ScaleOnly, gate.OnDebounceElapsed(menuOpen: true));
    }
}
