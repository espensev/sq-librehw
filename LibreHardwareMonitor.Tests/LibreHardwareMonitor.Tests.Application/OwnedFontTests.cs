// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

/// <summary>
/// Regression tests for the owned-font swap used by the Text Size scales.
/// <see cref="Control.Font"/> ignores a value-equal font and keeps the instance it already holds;
/// the naive "assign new, dispose previous" idiom then disposes the font the control is still
/// using, and the next <see cref="Font.Height"/> read throws "Parameter is not valid"
/// (the View → Text Size crash of 2026-08-27).
/// </summary>
public sealed class OwnedFontTests
{
    private static Font Make(float points) => new(FontFamily.GenericSansSerif, points, FontStyle.Regular);

    private static void AssertDisposed(Font font) =>
        Assert.Throws<ArgumentException>(() => font.Height);

    [Fact]
    public void Apply_SameValueTwice_KeepsControlFontUsable_AndDisposesTheCandidate()
    {
        // ScaleOnly commit then Full commit at the same percent (drag, pause, close menu).
        using Control control = new();
        Font owned = null;
        Font first = Make(12f);
        OwnedFont.Apply(control, first, ref owned);
        Font second = Make(12f);

        OwnedFont.Apply(control, second, ref owned);

        Assert.Same(first, control.Font);
        Assert.Same(first, owned);
        Assert.True(control.Font.Height > 0);     // this is the exact read that crashed
        AssertDisposed(second);
        owned.Dispose();
    }

    [Fact]
    public void Apply_DifferentValue_ReplacesControlFont_AndDisposesPrevious()
    {
        using Control control = new();
        Font owned = null;
        Font first = Make(12f);
        OwnedFont.Apply(control, first, ref owned);
        Font second = Make(15f);

        OwnedFont.Apply(control, second, ref owned);

        Assert.Same(second, control.Font);
        Assert.Same(second, owned);
        Assert.True(control.Font.Height > 0);
        AssertDisposed(first);
        owned.Dispose();
    }

    [Fact]
    public void Apply_ControlHoldsForeignEqualFont_LeavesForeignFontAlone_AndOwnsNothing()
    {
        // Startup case: the control still holds a system font we do not own, and the 100% candidate
        // is value-equal to it. Nothing we own may be disposed, and the foreign font must survive.
        using Control control = new();
        using Font foreign = Make(12f);
        control.Font = foreign;
        Font owned = null;
        Font candidate = Make(12f);

        OwnedFont.Apply(control, candidate, ref owned);

        Assert.Same(foreign, control.Font);
        Assert.Null(owned);
        Assert.True(foreign.Height > 0);
        AssertDisposed(candidate);
    }
}
