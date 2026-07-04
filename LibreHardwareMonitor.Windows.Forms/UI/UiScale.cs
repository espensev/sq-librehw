// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;

namespace LibreHardwareMonitor.Windows.Forms.UI;

/// <summary>
/// Pure, side-effect-free math for the user "Text Size" scale (GH: high-DPI readability).
/// A single source of truth so the tree, plot, glyphs and columns scale consistently and the
/// boundaries are unit-testable without constructing WinForms controls.
/// </summary>
public static class UiScale
{
    public const int MinPercent = 75;
    public const int MaxPercent = 250;
    public const int DefaultPercent = 100;
    public const int MinColumnWidth = 20;
    public const int MaxColumnWidth = 400;

    public static int ClampPercent(int percent) =>
        Math.Max(MinPercent, Math.Min(MaxPercent, percent));

    public static float ScaledFontSize(float basePointSize, int percent) =>
        basePointSize * ClampPercent(percent) / 100f;

    /// <summary>Row height from a font's pixel height, matching MainForm's original formulas
    /// (normal: Max(h+1,18); compact: Max(h,16)).</summary>
    public static int TreeRowHeight(int fontHeight, bool compact) =>
        compact ? Math.Max(fontHeight, 16) : Math.Max(fontHeight + 1, 18);

    public static int ScaledColumnWidth(int baseWidth, int percent) =>
        Math.Max(MinColumnWidth,
                 Math.Min(MaxColumnWidth,
                          (int)Math.Round(baseWidth * ClampPercent(percent) / 100.0)));

    public static double PlotAxisFontSize(double baseFontSize, int percent) =>
        baseFontSize * ClampPercent(percent) / 100.0;

    /// <summary>Recover a 100% base width from a currently-displayed (scaled) width, so a
    /// user drag at scale S persists as a scale-independent base.</summary>
    public static int BaseFromScaled(int scaledWidth, int percent) =>
        Math.Max(MinColumnWidth, (int)Math.Round(scaledWidth * 100.0 / ClampPercent(percent)));
}
