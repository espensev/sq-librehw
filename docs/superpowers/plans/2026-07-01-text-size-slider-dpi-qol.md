# Text Size Slider + High-DPI QoL — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a menu-hosted **Text Size** slider that scales the sensor tree text, plot axis text, tree glyphs and value columns together (persisted), plus four high-DPI quality-of-life improvements.

**Architecture:** A pure static `UiScale` helper holds all scale math (unit-tested). `MainForm.ApplyUiTextScale(int percent)` is the single apply path: it scales `treeView.Font`, recomputes row height + value/min/max column widths, sets `Theme.GlyphScalePercent`, and calls `PlotPanel.SetAxisTextScale`. A `TrackBar` hosted in a View-menu dropdown (`ToolStripControlHost`) drives it live; the value persists as an int percent under `uiTextScale`.

**Tech Stack:** C# / .NET `net10.0-windows`, WinForms, Aga.Controls `TreeViewAdv`, OxyPlot (WinForms), xUnit.

## Global Constraints

- **Platform:** every build/test uses `-p:Platform=x64` (CsWin32 fails on AnyCPU). Verbatim: `-p:Platform=x64`.
- **UI-only:** do not touch `data.json`, the CSV `Identifier`/`Time` columns, or `AssemblyVersion` (pinned 0.9.6 for the data.json golden test). New setting keys are fine.
- **Never reassign the form-level `Font`** (`AutoScaleMode.Font` at `MainForm.Designer.cs:1107` cascades a full re-layout). Scale only `treeView.Font`, `_plot.Font`, and the specific surfaces below.
- **Dispose every scaled `Font` you allocate** on replacement (the `TreeViewAdv.Font` and `PlotView.Font` setters do not); never dispose the shared `SystemFonts.MessageBoxFont`.
- **Regression gate:** the 7 data-contract tests (`DataJsonGoldenTests`, `CsvTimestampContractTests`) must still pass unchanged.
- **Default 100% must reproduce today's look byte-for-byte** at every DPI.
- Follow existing patterns (`UserOption`/`UserRadioGroup`, `ApplySensorTreeLayout`, scalar-setting idiom).

**Base column widths (from `MainForm.Designer.cs:168-192`):** Sensor=250, Value=100, Min=100, Max=100.

---

## Task 1: `UiScale` pure math helper (TDD)

**Files:**
- Create: `LibreHardwareMonitor.Windows.Forms/UI/UiScale.cs`
- Test: `LibreHardwareMonitor.Tests/UiScaleTests.cs`

**Interfaces:**
- Produces:
  - `UiScale.MinPercent=75`, `MaxPercent=250`, `DefaultPercent=100`, `MinColumnWidth=20`, `MaxColumnWidth=400` (const int)
  - `int UiScale.ClampPercent(int percent)`
  - `float UiScale.ScaledFontSize(float basePointSize, int percent)`
  - `int UiScale.TreeRowHeight(int fontHeight, bool compact)`
  - `int UiScale.ScaledColumnWidth(int baseWidth, int percent)`
  - `double UiScale.PlotAxisFontSize(double baseFontSize, int percent)`
  - `int UiScale.BaseFromScaled(int scaledWidth, int percent)`

- [ ] **Step 1: Write the failing test**

`LibreHardwareMonitor.Tests/UiScaleTests.cs`:

```csharp
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public class UiScaleTests
{
    [Theory]
    [InlineData(50, 75)]     // below floor -> floor
    [InlineData(75, 75)]
    [InlineData(100, 100)]
    [InlineData(250, 250)]
    [InlineData(400, 250)]   // above ceiling -> ceiling
    public void ClampPercent_BoundsToRange(int input, int expected)
    {
        Assert.Equal(expected, UiScale.ClampPercent(input));
    }

    [Fact]
    public void ScaledFontSize_At100_ReturnsBase()
    {
        Assert.Equal(9f, UiScale.ScaledFontSize(9f, 100));
    }

    [Fact]
    public void ScaledFontSize_At150_Scales()
    {
        Assert.Equal(13.5f, UiScale.ScaledFontSize(9f, 150));
    }

    [Theory]
    [InlineData(20, false, 21)]  // Font.Height 20 -> Max(20+1,18)=21
    [InlineData(10, false, 18)]  // small font -> floor 18
    [InlineData(20, true, 20)]   // compact -> Max(20,16)=20
    [InlineData(10, true, 16)]   // compact floor 16
    public void TreeRowHeight_NormalAndCompact(int fontHeight, bool compact, int expected)
    {
        Assert.Equal(expected, UiScale.TreeRowHeight(fontHeight, compact));
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(100, 150, 150)]
    [InlineData(100, 250, 250)]
    [InlineData(100, 500, 250)]  // percent clamped to 250 first -> 250
    [InlineData(10, 100, 20)]    // below MinColumnWidth -> 20
    [InlineData(300, 200, 400)]  // 600 -> clamped to MaxColumnWidth 400
    public void ScaledColumnWidth_ScalesAndClamps(int baseWidth, int percent, int expected)
    {
        Assert.Equal(expected, UiScale.ScaledColumnWidth(baseWidth, percent));
    }

    [Fact]
    public void PlotAxisFontSize_ScalesFromTwelve()
    {
        Assert.Equal(18.0, UiScale.PlotAxisFontSize(12.0, 150));
    }

    [Theory]
    [InlineData(150, 150, 100)]  // scaled 150 at 150% -> base 100
    [InlineData(100, 100, 100)]
    public void BaseFromScaled_InvertsScaling(int scaledWidth, int percent, int expectedBase)
    {
        Assert.Equal(expectedBase, UiScale.BaseFromScaled(scaledWidth, percent));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
Expected: FAIL — `UiScale` does not exist (CS0103 / type not found).

- [ ] **Step 3: Write the implementation**

`LibreHardwareMonitor.Windows.Forms/UI/UiScale.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
Expected: PASS — all `UiScaleTests` green, and the 7 existing contract tests still green.

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/UiScale.cs LibreHardwareMonitor.Tests/UiScaleTests.cs
git commit -m "feat(ui): add UiScale pure helper for text-size scaling math

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 2: `PlotPanel.SetAxisTextScale`

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs` (fields near `:47-50`; new method; call in ctor near `:130`)

**Interfaces:**
- Consumes: `UiScale.ClampPercent`, `UiScale.PlotAxisFontSize` (Task 1)
- Produces: `void PlotPanel.SetAxisTextScale(int percent)` — sets `FontSize`+`TitleFontSize` on every axis and the tracker font, then re-renders.

- [ ] **Step 1: Add backing fields**

In the field block (after `private double _dpiYScale = 1;` at `PlotPanel.cs:50`), add:

```csharp
    private int _axisTextScalePercent = UiScale.DefaultPercent;
    private Font _trackerBaseFont;   // captured lazily on first apply (MainForm sets _plot.Font first)
    private Font _scaledTrackerFont;  // owned; disposed on replacement
```

- [ ] **Step 2: Add the public method**

Add after `AutoscaleAllYAxes()` (end of class, before the closing brace ~`PlotPanel.cs:954`):

```csharp
    /// <summary>
    /// Scales all axis tick-label and title fonts, plus the hover tracker font, by <paramref name="percent"/>.
    /// DPI-independent on purpose: today's axis fonts are NOT DPI-scaled (ScaledPlotModel scales only the
    /// NaN/auto margins, a no-op), so 100% reproduces the current look at every DPI. Auto-margins absorb
    /// larger labels, so no clipping math is needed.
    /// </summary>
    public void SetAxisTextScale(int percent)
    {
        _axisTextScalePercent = UiScale.ClampPercent(percent);
        double fontSize = UiScale.PlotAxisFontSize(_model.DefaultFontSize, _axisTextScalePercent);

        foreach (Axis axis in _model.Axes)
        {
            axis.FontSize = fontSize;        // tick labels
            axis.TitleFontSize = fontSize;   // axis titles (set explicitly; don't rely on the fallback)
        }

        // Tracker/tooltip is a WinForms Label that inherits PlotView.Font ambiently.
        _trackerBaseFont ??= (Font)_plot.Font.Clone();
        Font old = _scaledTrackerFont;
        _scaledTrackerFont = new Font(
            _trackerBaseFont.FontFamily,
            UiScale.ScaledFontSize(_trackerBaseFont.Size, _axisTextScalePercent),
            _trackerBaseFont.Style);
        _plot.Font = _scaledTrackerFont;
        old?.Dispose();

        InvalidatePlotCosmetic();
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Manual smoke (temporary)**

Temporarily add `SetAxisTextScale(175);` at the end of the `PlotPanel` constructor, build+run the app, enable the graph, and confirm the Y-axis numbers, time labels and `Power [W]`/`Temperature [°C]` titles are visibly larger with no clipping. Then **remove the temporary line** and rebuild.

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs
git commit -m "feat(plot): add SetAxisTextScale to scale axis + tracker fonts

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 3: `Theme.cs` glyph scaling + centering

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/Theme.cs` (add static property; edit the two render funcs at `:32-75`)

**Interfaces:**
- Produces: `static int Theme.GlyphScalePercent { get; set; }` (default 100), consumed by `CustomPlusMinusRenderFunc`/`CustomCheckRenderFunc`.

- [ ] **Step 1: Add the scale property**

Add to the `Theme` class (near its other static members, above `Init()`):

```csharp
    /// <summary>Percent scale applied to the tree expand/collapse and plot-checkbox glyphs so they
    /// track the Text Size slider and stay centered in taller rows. Set by MainForm.ApplyUiTextScale.</summary>
    public static int GlyphScalePercent { get; set; } = 100;
```

- [ ] **Step 2: Replace the plus/minus render func**

Replace `Theme.cs:32-51` with:

```csharp
            TreeViewAdv.CustomPlusMinusRenderFunc = (g, rect, isExpanded) =>
            {
                int size = Math.Max(6, (int)Math.Round(8 * GlyphScalePercent / 100.0));
                int x = rect.Left;
                int y = rect.Top + Math.Max(0, (rect.Height - size) / 2);
                using (Brush brush = new SolidBrush(Current.BackgroundColor))
                {
                    g.FillRectangle(brush, x - 1, y - 1, size + 4, size + 4);
                }
                using (Pen pen = new Pen(Current.TreeOutlineColor))
                {
                    g.DrawRectangle(pen, x, y, size, size);
                    g.DrawLine(pen, x + 2, y + (size / 2), x + size - 2, y + (size / 2));
                    if (!isExpanded)
                    {
                        g.DrawLine(pen, x + (size / 2), y + 2, x + (size / 2), y + size - 2);
                    }
                }
            };
```

- [ ] **Step 3: Replace the checkbox render func**

Replace `Theme.cs:53-75` with (checkmark redrawn proportionally so it scales cleanly):

```csharp
            TreeViewAdv.CustomCheckRenderFunc = (g, rect, isChecked) =>
            {
                int size = Math.Max(10, (int)Math.Round(12 * GlyphScalePercent / 100.0));
                int x = rect.Left;
                int y = rect.Top + Math.Max(0, (rect.Height - size) / 2);
                using (Brush brush = new SolidBrush(Current.BackgroundColor))
                {
                    g.FillRectangle(brush, x - 1, y - 1, size + 1, size + 1);
                }
                using (Pen pen = new Pen(Current.TreeOutlineColor))
                {
                    g.DrawRectangle(pen, x, y, size, size);
                }
                if (isChecked)
                {
                    using var check = new Pen(Current.TreeOutlineColor, Math.Max(1.5f, size / 6f));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawLines(check, new[]
                    {
                        new PointF(x + size * 0.22f, y + size * 0.55f),
                        new PointF(x + size * 0.42f, y + size * 0.75f),
                        new PointF(x + size * 0.78f, y + size * 0.28f),
                    });
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                }
            };
```

- [ ] **Step 4: Build**

Run: `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Manual smoke (temporary)**

Temporarily set `Theme.GlyphScalePercent = 175;` before the tree first paints (e.g. in `MainForm` ctor after `Theme.Current` is chosen), run, and confirm the expand/collapse boxes and the plot checkboxes are larger and vertically centered. At 100% they should look like today. Remove the temporary line, rebuild.

- [ ] **Step 6: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/Theme.cs
git commit -m "feat(theme): scale + center tree glyphs via GlyphScalePercent

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 4: `MainForm` scaling engine + row-height fix + column scaling + persistence

This is the integration core. No slider UI yet — it is driven by the persisted `uiTextScale` value so it can be validated by editing the `.config` and relaunching.

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`

**Interfaces:**
- Consumes: `UiScale.*` (Task 1), `PlotPanel.SetAxisTextScale` (Task 2), `Theme.GlyphScalePercent` (Task 3)
- Produces: `void ApplyUiTextScale()`, fields `_uiTextScalePercent`, `_scaledTreeFont`, `_baseValueColumnWidth/_baseMinColumnWidth/_baseMaxColumnWidth`.

- [ ] **Step 1: Add fields**

Near the other tree fields (`private int _standardRowHeight;` at `MainForm.cs:79`), add:

```csharp
    private int _uiTextScalePercent = UiScale.DefaultPercent;
    private Font _scaledTreeFont;   // owned; disposed on replacement (never dispose SystemFonts.MessageBoxFont)
    private int _baseValueColumnWidth = 100;
    private int _baseMinColumnWidth = 100;
    private int _baseMaxColumnWidth = 100;
```

- [ ] **Step 2: Load the persisted scale early**

Immediately after `_settings.Load(...)` (`MainForm.cs:89`), add:

```csharp
        _uiTextScalePercent = UiScale.ClampPercent(_settings.GetValue("uiTextScale", UiScale.DefaultPercent));
```

- [ ] **Step 3: Capture base column widths**

Immediately after the `_standardMaxColumnWidth = treeView.Columns[3].Width;` line (`MainForm.cs:175`), add (these loaded widths are the 100% base):

```csharp
        _baseValueColumnWidth = treeView.Columns[1].Width;
        _baseMinColumnWidth = treeView.Columns[2].Width;
        _baseMaxColumnWidth = treeView.Columns[3].Width;
```

- [ ] **Step 4: Make the normal-mode row height font-derived (the clipping fix)**

In `ApplySensorTreeLayout`, replace `MainForm.cs:860`:

```csharp
                treeView.RowHeight = _standardRowHeight;
```

with:

```csharp
                treeView.RowHeight = UiScale.TreeRowHeight(treeView.Font.Height, compact: false);
```

And replace the compact row-height line `MainForm.cs:854`:

```csharp
                treeView.RowHeight = Math.Max(treeView.Font.Height, 16);
```

with:

```csharp
                treeView.RowHeight = UiScale.TreeRowHeight(treeView.Font.Height, compact: true);
```

Also scale the compact value-column cap. Replace `MainForm.cs:856`:

```csharp
                treeView.Columns[1].Width = Math.Min(_standardValueColumnWidth, 78);
```

with:

```csharp
                treeView.Columns[1].Width = Math.Min(_standardValueColumnWidth, UiScale.ScaledColumnWidth(78, _uiTextScalePercent));
```

- [ ] **Step 5: Add the apply method**

Add near `ApplySensorTreeLayout` (e.g. after it, ~`MainForm.cs:883`):

```csharp
    /// <summary>
    /// Single apply path for the Text Size scale: tree font + row height + value/min/max column
    /// widths + tree glyphs + plot axis text. Order-independent and composes with Compact Mode.
    /// </summary>
    private void ApplyUiTextScale()
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

        // Tree glyphs.
        Theme.GlyphScalePercent = _uiTextScalePercent;

        // Row height + column visibility + repaint (recomputes RowHeight from the live font).
        ApplySensorTreeLayout();

        // Plot axis text (single PlotPanel instance covers docked + separate-window modes).
        _plotPanel?.SetAxisTextScale(_uiTextScalePercent);

        _settings.SetValue("uiTextScale", _uiTextScalePercent);
    }
```

- [ ] **Step 6: Apply at startup**

Replace the existing startup call `ApplySensorTreeLayout();` at `MainForm.cs:265` with:

```csharp
        ApplyUiTextScale();
```

(`ApplyUiTextScale` calls `ApplySensorTreeLayout` internally, so column/row options are still applied once.)

- [ ] **Step 7: Preserve user drags as a scale-independent base**

In `TreeView_ColumnWidthChanged` (`MainForm.cs:1947`), after the early-return guard (`if (_updatingSensorTreeLayout) return;`), add:

```csharp
        int changedIndex = treeView.Columns.IndexOf(column);
        if (changedIndex == 1) _baseValueColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);
        else if (changedIndex == 2) _baseMinColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);
        else if (changedIndex == 3) _baseMaxColumnWidth = UiScale.BaseFromScaled(column.Width, _uiTextScalePercent);
```

- [ ] **Step 8: Persist bases (not scaled widths) so restarts don't double-scale**

In `SaveConfiguration`, replace the column-persist loop `MainForm.cs:1225-1226`:

```csharp
        foreach (TreeColumn column in treeView.Columns)
            _settings.SetValue("treeView.Columns." + column.Header + ".Width", column.Width);
```

with (Sensor keeps its displayed width; value/min/max persist their 100% base):

```csharp
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
```

Then delete the now-redundant compact override block `MainForm.cs:1234-1239` (base widths are compact-independent, so the special case is no longer needed):

```csharp
        if (_compactLayoutActive)
        {
            _settings.SetValue("treeView.Columns." + treeView.Columns[1].Header + ".Width", _standardValueColumnWidth);
            _settings.SetValue("treeView.Columns." + treeView.Columns[2].Header + ".Width", _standardMinColumnWidth);
            _settings.SetValue("treeView.Columns." + treeView.Columns[3].Header + ".Width", _standardMaxColumnWidth);
        }
```

- [ ] **Step 9: Build + regression tests**

Run:
```
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
```
Expected: build succeeds; all tests pass (7 contract + Task 1).

- [ ] **Step 10: Manual verify via config**

Edit the built app's `.config`, add `<add key="uiTextScale" value="150" />` under `<appSettings>`, launch. Confirm: tree text larger; rows taller (no clipping); Value/Min/Max readings fully visible (not `...`); glyphs scaled/centered; plot axis text larger. Set value to `100`, relaunch → identical to today. Toggle Compact Mode at 150 → rows stay correct.

- [ ] **Step 11: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs
git commit -m "feat(ui): ApplyUiTextScale engine (tree font/rows/columns/glyphs/plot) + persistence

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 5: Menu-hosted Text Size slider + risk checkpoint

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (View-menu wiring near `:179-182`)

**Interfaces:**
- Consumes: `ApplyUiTextScale`, `_uiTextScalePercent`, `UiScale.*`

- [ ] **Step 1: Build the submenu + hosted TrackBar**

Immediately after the Compact Mode wiring (`MainForm.cs:182`), add:

```csharp
        ToolStripMenuItem textSizeMenuItem = new($"Text Size ({_uiTextScalePercent}%)");
        viewMenuItem.DropDownItems.Insert(5, textSizeMenuItem);

        double dpiScale = DeviceDpi / 96.0;
        TrackBar textSizeTrackBar = new()
        {
            Minimum = UiScale.MinPercent,
            Maximum = UiScale.MaxPercent,
            TickFrequency = 25,
            SmallChange = 5,
            LargeChange = 25,
            AutoSize = false,
            Value = _uiTextScalePercent,
            Size = new Size((int)Math.Round(170 * dpiScale), (int)Math.Round(45 * dpiScale)),
            BackColor = Theme.Current.MenuBackgroundColor
        };
        ToolStripControlHost textSizeHost = new(textSizeTrackBar) { AutoSize = false, BackColor = Theme.Current.MenuBackgroundColor };
        textSizeHost.Size = textSizeTrackBar.Size;
        textSizeMenuItem.DropDownItems.Add(textSizeHost);

        textSizeTrackBar.ValueChanged += (s, e) =>
        {
            _uiTextScalePercent = UiScale.ClampPercent(textSizeTrackBar.Value);
            textSizeMenuItem.Text = $"Text Size ({_uiTextScalePercent}%)";
            ApplyUiTextScale();
        };

        // Keep the dropdown open while dragging the slider (a drag is not an ItemClicked).
        textSizeMenuItem.DropDown.Closing += (s, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                e.Cancel = true;
        };
```

Note: `MenuBackgroundColor` is the property the `ThemedToolStripRenderer` uses; if the exact name differs in `Theme`, use the same member `ThemedToolStripRenderer.cs:63-69` reads. Confirm the member name when implementing.

- [ ] **Step 2: Build**

Run: `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: RISK CHECKPOINT — run and confirm two behaviors**

Launch the app. Verify:
1. **Drag keeps the dropdown open** — open View ▸ Text Size, drag the slider; the menu must stay open and the tree/plot must scale live and the `(NNN%)` label update.
2. **Dark theme acceptable** — switch to Dark/Black theme; the slider must be legible (the Win32 channel may stay light — judge if acceptable).

**If either fails:** invoke the **fallback** — move the `TrackBar` into a small themed `Text Size…` dialog opened from a plain menu item (the ValueChanged handler and `ApplyUiTextScale` are unchanged; only the host changes). Document which path shipped.

- [ ] **Step 4: Verify persistence round-trip**

Set the slider to 150%, close the app (triggers `SaveConfiguration`), relaunch. Confirm it reopens at 150% and the tree/plot are scaled. Set back to 100%, relaunch → identical to today.

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs
git commit -m "feat(ui): menu-hosted Text Size slider driving ApplyUiTextScale

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 6: QoL-1 — remember window/maximized state + MinimumSize + larger default

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (ctor bounds `:105-111`; `MainForm_Load` `:1269-1291`; `MainForm_MoveOrResize` `:1817-1826`)

- [ ] **Step 1: Set a MinimumSize and larger default**

In the ctor `Bounds` block (`MainForm.cs:105-111`), raise the default width/height and add a minimum after it:

```csharp
        Bounds = new Rectangle
        {
            X = _settings.GetValue("mainForm.Location.X", Location.X),
            Y = _settings.GetValue("mainForm.Location.Y", Location.Y),
            Width = _settings.GetValue("mainForm.Width", 720),
            Height = _settings.GetValue("mainForm.Height", 840)
        };
        MinimumSize = new Size(360, 420);
```

(Also bump the matching defaults in `MainForm_Load` `:1275-1276` to `720`/`840` so both read sites agree.)

- [ ] **Step 2: Persist WindowState (use RestoreBounds when maximized)**

Replace `MainForm_MoveOrResize` (`MainForm.cs:1817-1826`) with:

```csharp
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
```

- [ ] **Step 3: Restore WindowState on load**

At the end of `MainForm_Load`, after `Bounds = newBounds;` (`MainForm.cs:1291`), add:

```csharp
        if (_settings.GetValue("mainForm.Maximized", false))
            WindowState = FormWindowState.Maximized;
```

- [ ] **Step 4: Build + manual verify**

Build (Task 2 command). Run: maximize, close, relaunch → reopens maximized. Un-maximize, resize small → cannot shrink below MinimumSize. Move/resize normal, relaunch → position restored.

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs
git commit -m "feat(ui): remember maximized state, add MinimumSize, larger default window

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 7: QoL-3 — auto-fit columns to content

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (new menu item in View; new method)

**Interfaces:**
- Consumes: `UiScale.MinColumnWidth/MaxColumnWidth`, `treeView` node enumeration

- [ ] **Step 1: Add an `Auto-Fit Columns` menu item**

After the Text Size wiring (Task 5), add:

```csharp
        ToolStripMenuItem autoFitColumnsMenuItem = new("Auto-Fit Columns");
        autoFitColumnsMenuItem.Click += delegate { AutoFitTreeColumns(); };
        viewMenuItem.DropDownItems.Insert(6, autoFitColumnsMenuItem);
```

- [ ] **Step 2: Implement `AutoFitTreeColumns`**

Add a method that measures header + visible cell text for the Value/Min/Max columns (the Sensor column already auto-stretches):

```csharp
    private void AutoFitTreeColumns()
    {
        // Columns 1..3 (Value/Min/Max) are fixed-width and ellipsize; size them to the widest
        // currently-visible cell text. Column 0 (Sensor) auto-fills via TreeView_SizeChanged.
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
```

Note: confirm the `SensorNode` property names for value/min/max when implementing (grep `class SensorNode`); adapt `n.Value/n.Min/n.Max` to the actual getters (they are the same strings shown in the tree).

- [ ] **Step 3: Build + manual verify**

Build. Run: give a sensor a long value, choose View ▸ Auto-Fit Columns → the Value/Min/Max columns widen to fit (no ellipsis), clamped to 400px.

- [ ] **Step 4: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs
git commit -m "feat(ui): Auto-Fit Columns menu action sizes value columns to content

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 8: QoL-4 — larger hover tooltips (tracker already scaled)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (tree tooltip)

Note: the OxyPlot **tracker** already scales via `_plot.Font` in Task 2, so this task covers only the **sensor-tree** hover tooltip.

- [ ] **Step 1: Investigate the tooltip surface**

Read `NodeToolTipProvider.cs` and how `treeView.ShowNodeToolTips` (`MainForm.cs:184`) surfaces the string (which `ToolTip` instance renders it). Determine whether the tree exposes a settable tooltip `Font` or needs an owner-drawn `ToolTip`.

- [ ] **Step 2: Apply a scaled tooltip font**

If the tree's tooltip is a reachable `ToolTip`, set `OwnerDraw = true` and draw with a font scaled by `_uiTextScalePercent` (reuse `UiScale.ScaledFontSize(SystemFonts.MessageBoxFont.SizeInPoints, _uiTextScalePercent)`), refreshed inside `ApplyUiTextScale`. If it is not cleanly reachable without patching `Aga.Controls`, **stop and report** — per the spec this is medium-risk; the tracker (Task 2) already covers the plot side, and enlarging the Aga tooltip may not be worth an Aga patch. Get a go/no-go before modifying `Aga.Controls`.

- [ ] **Step 3: Build + manual verify (if implemented)**

Build. Hover a sensor row → tooltip renders in the larger font at scale 150.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ui): scale sensor-tree hover tooltip font with Text Size

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 9: QoL-2 — reclaim empty graph vertical space (investigate → decide)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs`

- [ ] **Step 1: Investigate the cause at runtime**

Run the app with graphs enabled. Determine whether the over-wide Y ranges come from **stale persisted zoom** (`plotPanel.Min<Type>`/`Max<Type>` restored at `PlotPanel.cs:464`) or from **autoscale padding**. Quick test: call `AutoscaleAllYAxes()` (`PlotPanel.cs:946`, already exists) via the existing Value Axes ▸ Autoscale All menu and observe whether the bands tighten. Record the finding in the commit message.

- [ ] **Step 2: Implement the chosen fix**

- **If stale persisted zoom is the cause:** add a setting `plotPanel.AutoFitYOnStart` (default `true`) and, after the first data population, call `AutoscaleAllYAxes()` once so a fresh session fits to data instead of restoring a stale wide window. Gate it so an explicit user zoom during the session still sticks.
- **If autoscale padding is the cause:** reduce the per-axis `MinimumPadding`/`MaximumPadding` on the `LinearAxis` initializer (`PlotPanel.cs:432-444`) to a tighter value (e.g. `0.05`) so autoscaled bands hug the data.

Provide the concrete one-block edit that matches the finding; do not add both.

- [ ] **Step 3: Build + manual verify**

Build. Run: the graphs should use their vertical space with far less empty band, while pan/zoom still works and `Reset Graph View` behaves.

- [ ] **Step 4: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs
git commit -m "feat(plot): tighten default Y-axis fit to reclaim empty graph space

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

---

## Task 10: Final regression, docs, deploy

**Files:**
- Modify: `docs/local-ui-customizations.md` (or the fork's UI-customizations doc)

- [ ] **Step 1: Full regression**

Run:
```
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
```
Expected: both frameworks build; all tests pass.

- [ ] **Step 2: Full manual checklist (spec §5)**

Run the app and confirm all 9 items in the spec's manual checklist, including: drag-keeps-open (or fallback), dark theme, values not truncated at 150–200%, glyph centering, Compact Mode composition, restart persistence, separate plot window scaling, maximized restore.

- [ ] **Step 3: Update docs**

Document the Text Size slider (View menu, `uiTextScale` setting, 75–250%), the four QoL items, and which slider host shipped (menu vs dialog fallback). Note the DPI rationale (96 DPI on DSR panels).

- [ ] **Step 4: Commit + rebuild/redeploy monitor**

```bash
git add docs/
git commit -m "docs: Text Size slider + high-DPI QoL customizations

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Vvh8CP1h2XjNRRTZJ5TjCQ"
```

Then rebuild Release and restart the monitor per issue #22's launch path so the running instance reflects the new build.

---

## Self-review notes

- **Spec coverage:** Core slider (T5) → tree font (T4), row height fix (T4), column widths (T4), plot axis+tracker (T2), glyphs (T3), persistence (T4), risk checkpoint+fallback (T5). QoL-1 (T6), QoL-3 (T7), QoL-4 (T8), QoL-2 (T9). Regression/docs (T10). All spec sections mapped.
- **Type consistency:** `UiScale` signatures in T1 match every call site (T2 `PlotAxisFontSize`/`ClampPercent`/`ScaledFontSize`; T4 `ScaledFontSize`/`TreeRowHeight`/`ScaledColumnWidth`/`BaseFromScaled`/`ClampPercent`; T7 `BaseFromScaled`/`MinColumnWidth`/`MaxColumnWidth`). `SetAxisTextScale(int)` (T2) matches its T4 call. `Theme.GlyphScalePercent` (T3) matches its T4 assignment.
- **Known adapt-on-implement points (flagged inline, not placeholders):** `Theme.MenuBackgroundColor` member name (T5); `SensorNode` value/min/max getter names (T7); tree tooltip reachability go/no-go (T8); QoL-2 branch chosen from runtime finding (T9).
