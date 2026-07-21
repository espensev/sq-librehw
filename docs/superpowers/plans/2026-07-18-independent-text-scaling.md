# Independent Text Scaling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decouple sensor-pane text scaling from graph text scaling: a new persisted `plotTextScale` (Graph → Graph Text Size slider, default 100%) drives plot axis text, while the existing `uiTextScale` keeps driving tree/menu/columns/glyphs plus the plot hover tracker.

**Architecture:** Split `PlotPanel.SetAxisTextScale` into axis-only and tracker-only methods; extract the debounced slider-dropdown machinery from `MainForm` into a reusable `TextScaleSliderMenu` class; instantiate it twice (View → Text Size, Graph → Graph Text Size). Spec: `docs/feature-independent-text-scaling.md`.

**Tech Stack:** .NET 10 WinForms, OxyPlot, xUnit. Existing pure helpers `UiScale` and `UiTextScaleCommitGate` are reused unchanged.

## Global Constraints

- Build/test always with `-p:Platform=x64` (CsWin32 fails under AnyCPU).
- Both scales clamp via `UiScale.ClampPercent` (75–250); `UiScale.DefaultPercent` = 100.
- Settings keys: existing `uiTextScale`; new `plotTextScale` (default 100 on first run).
- Never mutate an open menu strip per slider tick — only the fixed-size readout label changes per tick (this is the shipped anti-glitch invariant; commit `0f9476b`).
- No changes to `lib/` or the `data.json` contract.
- Test command: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- Line numbers cited as ≈N drift as edits land; anchor on the quoted code, not the number.

---

### Task 1: Split PlotPanel axis scaling from tracker scaling

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs` (≈948–975, method `SetAxisTextScale`)
- Test (create): `LibreHardwareMonitor.Tests/PlotPanelTextScaleTests.cs`

**Interfaces:**
- Consumes: `UiScale.ClampPercent(int)`, `UiScale.PlotAxisFontSize(double,int)`, `UiScale.ScaledFontSize(float,int)` (all existing).
- Produces: `public void SetAxisTextScale(int percent)` (narrowed: axes only) and `public void SetTrackerTextScale(int percent)` (new: tracker font only) on `PlotPanel`. Task 3 calls both.

Note: `MainForm.ApplyUiTextScale` (≈1213) keeps calling `SetAxisTextScale(_uiTextScalePercent)` until Task 3 rewires it — it still compiles; interim behavior (tracker temporarily unscaled) is acceptable between commits.

- [ ] **Step 1: Write the failing tests**

Create `LibreHardwareMonitor.Tests/PlotPanelTextScaleTests.cs`:

```csharp
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class PlotPanelTextScaleTests
{
    private static (PlotPanel Panel, PlotView View) CreatePanel()
    {
        var settings = new PersistentSettings();
        var unitManager = new UnitManager(settings);
        var panel = new PlotPanel(settings, unitManager);
        return (panel, panel.Controls.OfType<PlotView>().Single());
    }

    [Fact]
    public void SetAxisTextScale_ScalesAxisFontsAndTickSpacing_NotTrackerFont()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            float trackerSizeBefore = view.Font.Size;

            panel.SetAxisTextScale(150);

            double expected = UiScale.PlotAxisFontSize(view.Model.DefaultFontSize, 150);
            Assert.NotEmpty(view.Model.Axes);
            foreach (Axis axis in view.Model.Axes)
            {
                Assert.Equal(expected, axis.FontSize);
                Assert.Equal(expected, axis.TitleFontSize);
                Assert.Equal(90.0, axis.IntervalLength); // 60 * 150%
            }

            Assert.Equal(trackerSizeBefore, view.Font.Size);
        }
    }

    [Fact]
    public void SetTrackerTextScale_ScalesTrackerFont_NotAxes()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            float baseSize = view.Font.Size;
            double[] axisSizesBefore = view.Model.Axes.Select(a => a.FontSize).ToArray();
            double[] intervalsBefore = view.Model.Axes.Select(a => a.IntervalLength).ToArray();

            panel.SetTrackerTextScale(200);

            Assert.Equal(UiScale.ScaledFontSize(baseSize, 200), view.Font.Size, precision: 2);
            Assert.Equal(axisSizesBefore, view.Model.Axes.Select(a => a.FontSize).ToArray());
            Assert.Equal(intervalsBefore, view.Model.Axes.Select(a => a.IntervalLength).ToArray());
        }
    }

    [Fact]
    public void BothSetters_ClampToUiScaleRange()
    {
        (PlotPanel panel, PlotView view) = CreatePanel();
        using (panel)
        {
            panel.SetAxisTextScale(400);  // clamps to 250
            double expected = UiScale.PlotAxisFontSize(view.Model.DefaultFontSize, 250);
            Assert.All(view.Model.Axes, axis => Assert.Equal(expected, axis.FontSize));

            float baseSize = 0; // recompute from a fresh panel: tracker base is captured lazily
            (PlotPanel panel2, PlotView view2) = CreatePanel();
            using (panel2)
            {
                baseSize = view2.Font.Size;
                panel2.SetTrackerTextScale(10); // clamps to 75
                Assert.Equal(UiScale.ScaledFontSize(baseSize, 75), view2.Font.Size, precision: 2);
            }
        }
    }
}
```

If `new PlotPanel(...)` throws on the xUnit MTA thread, wrap creation in an STA helper thread exactly as `WinFormsUiLifetimeTests` does for its window tests; plain control construction without a shown window is expected to work.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlotPanelTextScaleTests"`
Expected: compile error `'PlotPanel' does not contain a definition for 'SetTrackerTextScale'`. Add a temporary stub `public void SetTrackerTextScale(int percent) { }` to `PlotPanel`, re-run, and confirm assertion failures: `SetTrackerTextScale_ScalesTrackerFont_NotAxes` fails (font unchanged) and `SetAxisTextScale_ScalesAxisFontsAndTickSpacing_NotTrackerFont` fails (tracker font changed by the current combined `SetAxisTextScale`).

- [ ] **Step 3: Implement the split**

In `PlotPanel.cs`, replace the whole `SetAxisTextScale` method (≈948–975) with:

```csharp
    public void SetAxisTextScale(int percent)
    {
        _axisTextScalePercent = UiScale.ClampPercent(percent);
        double fontSize = UiScale.PlotAxisFontSize(_model.DefaultFontSize, _axisTextScalePercent);

        foreach (Axis axis in _model.Axes)
        {
            axis.FontSize = fontSize;        // tick labels
            axis.TitleFontSize = fontSize;   // axis titles (set explicitly; don't rely on the fallback)

            // Grow tick spacing with the font so larger labels don't pack tighter than they render
            // (OxyPlot's default IntervalLength is 60; at percent=100 this is exactly 60.0, so the
            // 100% case is byte-identical to today's tick density).
            axis.IntervalLength = 60.0 * (_axisTextScalePercent / 100.0);
        }

        InvalidatePlotCosmetic();
    }

    public void SetTrackerTextScale(int percent)
    {
        int clamped = UiScale.ClampPercent(percent);

        // Tracker/tooltip is a WinForms Label that inherits PlotView.Font ambiently.
        _trackerBaseFont ??= (Font)_plot.Font.Clone();
        Font old = _scaledTrackerFont;
        _scaledTrackerFont = new Font(
            _trackerBaseFont.FontFamily,
            UiScale.ScaledFontSize(_trackerBaseFont.Size, clamped),
            _trackerBaseFont.Style);
        _plot.Font = _scaledTrackerFont;
        old?.Dispose();

        InvalidatePlotCosmetic();
    }
```

Remove the temporary stub from Step 2. Keep the `_axisTextScalePercent` field (ApplyGridDensity reads it every refresh so late-added axes pick up the scale); the tracker needs no percent field.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlotPanelTextScaleTests"`
Expected: 3/3 PASS. Then run the full suite (same command without `--filter`): expected 141 total, 0 failed (1 skipped is normal).

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs LibreHardwareMonitor.Tests/PlotPanelTextScaleTests.cs
git commit -m "refactor(plot): split axis text scale from tracker text scale"
```

---

### Task 2: Extract TextScaleSliderMenu and refactor the View slider onto it

**Files:**
- Create: `LibreHardwareMonitor.Windows.Forms/UI/TextScaleSliderMenu.cs`
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (fields ≈89–96; slider ctor block ≈219–283; `ApplyUiTextScale` menu-refresh section ≈1195–1201; dispose ≈1850)
- Test (create): `LibreHardwareMonitor.Tests/TextScaleSliderMenuTests.cs`

**Interfaces:**
- Consumes: `UiScale` constants/clamp, `UiTextScaleCommitGate` (`OnSliderTick()`, `Commit OnDebounceElapsed(bool menuOpen)`, `Commit OnMenuClosed()`).
- Produces (Task 3 relies on these exact members): `internal sealed class TextScaleSliderMenu : IDisposable` with
  `TextScaleSliderMenu(string titlePrefix, int initialPercent, double dpiScale, Font menuFont, Color menuBackColor)`,
  `ToolStripMenuItem MenuItem { get; }`, `ToolStripLabel Readout { get; }`, `TrackBar Slider { get; }`,
  `event Action<int> PercentChanged`, `event Action<bool> CommitRequested` (arg = deferMenuRefresh),
  `void InstallAt(ToolStripMenuItem parentMenuItem, int index)`, `void RefreshMenuText(int percent, Font menuFont)`, `void Dispose()`.

- [ ] **Step 1: Write the failing tests**

Create `LibreHardwareMonitor.Tests/TextScaleSliderMenuTests.cs`:

```csharp
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Drawing;
using LibreHardwareMonitor.Windows.Forms.UI;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class TextScaleSliderMenuTests
{
    private static TextScaleSliderMenu Create(int initialPercent = 100) =>
        new("Text Size", initialPercent, dpiScale: 1.0, SystemFonts.MenuFont, Color.Black);

    [Fact]
    public void Construction_SetsTitleReadoutAndSliderFromInitialPercent()
    {
        using TextScaleSliderMenu menu = Create(125);

        Assert.Equal("Text Size (125%)", menu.MenuItem.Text);
        Assert.Equal("125%", menu.Readout.Text);
        Assert.Equal(125, menu.Slider.Value);
        Assert.False(menu.Readout.AutoSize); // fixed-size: per-tick text can never re-layout the dropdown
    }

    [Fact]
    public void SliderTick_UpdatesReadoutAndRaisesPercentChanged_WithoutTouchingMenuText()
    {
        using TextScaleSliderMenu menu = Create(100);
        int observed = 0;
        menu.PercentChanged += p => observed = p;
        Size readoutSizeBefore = menu.Readout.Size;

        menu.Slider.Value = 150;

        Assert.Equal(150, observed);
        Assert.Equal("150%", menu.Readout.Text);
        Assert.Equal("Text Size (100%)", menu.MenuItem.Text); // parent label untouched mid-drag
        Assert.Equal(readoutSizeBefore, menu.Readout.Size);
    }

    [Fact]
    public void RefreshMenuText_UpdatesTitleAndReadoutHeightOnly()
    {
        using TextScaleSliderMenu menu = Create(100);
        int widthBefore = menu.Readout.Width;

        menu.RefreshMenuText(180, SystemFonts.CaptionFont);

        Assert.Equal("Text Size (180%)", menu.MenuItem.Text);
        Assert.Equal(widthBefore, menu.Readout.Width);
        Assert.Equal(SystemFonts.CaptionFont.Height + 6, menu.Readout.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~TextScaleSliderMenuTests"`
Expected: compile error `The type or namespace name 'TextScaleSliderMenu' could not be found` (class doesn't exist yet — this is the RED for a new type; no stub needed because the whole class is the unit under test and Step 3 implements it directly from these tests).

- [ ] **Step 3: Implement TextScaleSliderMenu**

Create `LibreHardwareMonitor.Windows.Forms/UI/TextScaleSliderMenu.cs`:

```csharp
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
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~TextScaleSliderMenuTests"`
Expected: 3/3 PASS.

- [ ] **Step 5: Refactor MainForm onto the builder (no behavior change)**

In `MainForm.cs`:

(a) Replace the four slider fields (≈89–96)

```csharp
    // Text Size slider: heavy scaling is debounced to drag pauses and menu-strip mutations are
    // deferred to menu close (see UiTextScaleCommitGate); per-tick feedback is the readout label.
    private const int UiTextScaleCommitDelayMilliseconds = 150;
    private readonly UiTextScaleCommitGate _uiTextScaleCommitGate = new();
    private readonly System.Windows.Forms.Timer _uiTextScaleCommitTimer;
    private ToolStripMenuItem _textSizeMenuItem;
    private ToolStripLabel _textSizeReadout;
```

with

```csharp
    // Debounced percent sliders (see TextScaleSliderMenu / UiTextScaleCommitGate).
    private TextScaleSliderMenu _textSizeSlider;
```

(b) Replace the whole ctor slider block — from `_textSizeMenuItem = new ToolStripMenuItem(...)` (≈219) through the `viewMenuItem.DropDown.Closed += ...;` handler's closing `};` (≈277) — with:

```csharp
        double dpiScale = DeviceDpi / 96.0;
        _textSizeSlider = new TextScaleSliderMenu("Text Size", _uiTextScalePercent, dpiScale,
            mainMenu.Font, Theme.Current.MenuBackgroundColor);
        _textSizeSlider.PercentChanged += percent => _uiTextScalePercent = percent;
        _textSizeSlider.CommitRequested += deferMenuRefresh => ApplyUiTextScale(deferMenuRefresh);
        _textSizeSlider.InstallAt(viewMenuItem, 5);
```

(c) In `ApplyUiTextScale`, inside the `if (!deferMenuRefresh)` block, replace the `_textSizeMenuItem` / `_textSizeReadout` updates (the two trailing `if (... != null)` statements) with:

```csharp
            _textSizeSlider?.RefreshMenuText(_uiTextScalePercent, mainMenu.Font);
```

(d) In the shutdown path, replace `_uiTextScaleCommitTimer.Dispose();` with `_textSizeSlider?.Dispose();`.

- [ ] **Step 6: Build + full suite to verify the refactor holds**

Run: `dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -p:Platform=x64` then the full test suite.
Expected: build 0 warnings/0 errors; suite 0 failed (1 skipped is normal).

- [ ] **Step 7: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/TextScaleSliderMenu.cs LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs LibreHardwareMonitor.Tests/TextScaleSliderMenuTests.cs
git commit -m "refactor(ui): extract reusable debounced text-scale slider menu"
```

---

### Task 3: Graph Text Size slider, persistence, tracker rewire

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` (settings load ≈120; fields; `InitializeGraphMenu` ≈981; initial apply call site — the `ApplyUiTextScale();` call after the column UserOptions are wired; `ApplyUiTextScale` plot call ≈1213; `SaveConfiguration`'s `uiTextScale` save; dispose)

**Interfaces:**
- Consumes: `TextScaleSliderMenu` (Task 2 exact members), `PlotPanel.SetAxisTextScale(int)` / `PlotPanel.SetTrackerTextScale(int)` (Task 1).
- Produces: `private void ApplyPlotTextScale(bool deferMenuRefresh = false)`; settings key `"plotTextScale"`.

No new unit test: every moving part (gate policy, slider widget, PlotPanel setters, UiScale math) is already unit-covered by Tasks 1–2 and existing suites; this task is pure composition in `MainForm`, which has no test harness. Verification is Step 3's build+suite and Task 4's live gate.

- [ ] **Step 1: Wire the new scale end-to-end**

(a) Fields — extend the Task 2 field block:

```csharp
    // Debounced percent sliders (see TextScaleSliderMenu / UiTextScaleCommitGate).
    private TextScaleSliderMenu _textSizeSlider;
    private TextScaleSliderMenu _plotTextSlider;
    private int _plotTextScalePercent = UiScale.DefaultPercent;
```

(b) Settings load — directly under the `uiTextScale` load (≈120):

```csharp
        _plotTextScalePercent = UiScale.ClampPercent(_settings.GetValue("plotTextScale", UiScale.DefaultPercent));
```

(c) In `InitializeGraphMenu`, after the `strokeThicknessMenuItem.Text = "&Stroke Thickness";` line:

```csharp
        _plotTextSlider = new TextScaleSliderMenu("Graph Text Size", _plotTextScalePercent, DeviceDpi / 96.0,
            mainMenu.Font, Theme.Current.MenuBackgroundColor);
        _plotTextSlider.PercentChanged += percent => _plotTextScalePercent = percent;
        _plotTextSlider.CommitRequested += deferMenuRefresh => ApplyPlotTextScale(deferMenuRefresh);
        _plotTextSlider.InstallAt(graphMenuItem, graphMenuItem.DropDownItems.IndexOf(strokeThicknessMenuItem) + 1);
```

If `strokeThicknessMenuItem` is not yet in `graphMenuItem.DropDownItems` at that point in the method (items may be added below), place the `InstallAt` call after the line that adds it and use the same IndexOf+1 expression.

(d) New method next to `ApplyUiTextScale`:

```csharp
    private void ApplyPlotTextScale(bool deferMenuRefresh = false)
    {
        _plotTextScalePercent = UiScale.ClampPercent(_plotTextScalePercent);
        _plotPanel?.SetAxisTextScale(_plotTextScalePercent);

        if (!deferMenuRefresh)
            _plotTextSlider?.RefreshMenuText(_plotTextScalePercent, mainMenu.Font);

        _settings.SetValue("plotTextScale", _plotTextScalePercent);
    }
```

(e) In `ApplyUiTextScale` (≈1213), change

```csharp
        _plotPanel?.SetAxisTextScale(_uiTextScalePercent);
```

to

```csharp
        _plotPanel?.SetTrackerTextScale(_uiTextScalePercent);
```

(f) Immediately after the existing initial `ApplyUiTextScale();` call in the ctor (the one under the "Apply once now that all column options..." comment), add:

```csharp
        ApplyPlotTextScale();
```

(g) In `SaveConfiguration`, next to `_settings.SetValue("uiTextScale", _uiTextScalePercent);`, add:

```csharp
        _settings.SetValue("plotTextScale", _plotTextScalePercent);
```

(h) In the shutdown path, next to `_textSizeSlider?.Dispose();`, add `_plotTextSlider?.Dispose();`.

- [ ] **Step 2: Build + full suite**

Run: `dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -p:Platform=x64` then the full test suite.
Expected: build clean; 0 failed.

- [ ] **Step 3: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs
git commit -m "feat(ui): independent Graph Text Size scale for plot axis text"
```

---

### Task 4: Deploy swap and live visual gate

**Files:**
- Modify: `docs/feature-independent-text-scaling.md` (status line only, after the gate passes)

- [ ] **Step 1: Rebuild Release and swap the running instance**

The app runs from `bin\Release\net10.0-windows\` (repo-root `bin`, per the csproj `OutputPath`); the exe file-locks while running, so stop → build → start:

```powershell
$p = Get-Process -Name "LibreHardwareMonitor.Windows.Forms" -ErrorAction SilentlyContinue
if ($p) { $null = $p.CloseMainWindow(); if (-not $p.WaitForExit(8000)) { Stop-Process -Id $p.Id -Confirm:$false } }
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -p:Platform=x64
$dir = "E:\SQ_HQ\Monitoring\sq-librehw\bin\Release\net10.0-windows"
Start-Process -FilePath "$dir\LibreHardwareMonitor.Windows.Forms.exe" -WorkingDirectory $dir
```

Verify the running exe's `ProductVersion` contains today's SHA/date and `http://localhost:8085/data.json` returns 200.

- [ ] **Step 2: Operator visual gate (per memory: live visual gate beats measurement)**

Ask the operator to confirm, in both themes if convenient:
1. View → Text Size at e.g. 175%: tree/menu text large; graph axis text and tick density unchanged.
2. Graph → Graph Text Size drag: axis text/tick spacing scale live on pauses; sensor tree untouched; no menu jitter mid-drag; % readout ticks inside the dropdown.
3. Hover the plot: tracker tooltip size follows the main Text Size, not the graph slider.
4. Restart the app: both percents persist; fresh config defaults graph scale to 100%.

- [ ] **Step 3: Close out docs and commit**

After the operator confirms, change the spec status line in `docs/feature-independent-text-scaling.md` from `Status: DRAFT (design approved pending operator review, 2026-07-18)` to `Status: SHIPPED (live-verified 2026-07-18)`, then:

```bash
git add docs/feature-independent-text-scaling.md docs/superpowers/plans/2026-07-18-independent-text-scaling.md
git commit -m "docs: close out independent text scaling spec"
```
