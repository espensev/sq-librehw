# Text Size slider + high-DPI QoL — Design

- **Date:** 2026-07-01
- **Repo:** `sq-librehw` (fork of LibreHardwareMonitor), WinForms app `LibreHardwareMonitor.Windows.Forms`
- **Status:** Approved for planning (brainstorming complete). Next step: implementation plan (writing-plans).
- **Author:** Espen Severinsen (with Claude)

## 1. Problem & goal

On a large 4K / DSR / supersampled monitor at 100% Windows scaling, Windows reports **96 DPI**, so every DPI code path in the app resolves to ×1.0 and nothing can enlarge the UI. Result (see `ui-needadj-textpng.png` in repo root): the **sensor tree** text and the **plot axis text** (Y-axis numbers, time labels, and the `Power [W]` / `Temperature [°C]` / `Fan [RPM]` axis titles) render physically tiny, with large empty vertical bands in the graphs.

**Goal:** give the user a single, DPI-independent **Text Size** control (a slider hosted inside a menu) that enlarges the sensor-tree text *and* the plot axis text together, persisted across restarts, plus a set of chosen quality-of-life readability improvements.

## 2. Non-goals & contract guards

- **Do not** touch `data.json` or the CSV `Identifier`/`Time` columns — these are external contracts consumed downstream (ThermalTrace). This feature is UI-only.
- **Do not** reassign the form-level `Font`. `AutoScaleMode = AutoScaleMode.Font` (`MainForm.Designer.cs:1107`) means changing the form font cascades a full form/menu re-layout. Scale only the specific surfaces below.
- **Do not** change `AssemblyVersion` (pinned at 0.9.6 to protect the `data.json` "Version" golden test). A new UI-only settings key does not affect that.
- Builds/tests must use `-p:Platform=x64` (CsWin32 requirement; AnyCPU fails). Data-contract tests (7/7) must still pass.

## 3. Core feature — "Text Size" slider

### 3.1 UX & placement

- New **`Text Size`** `ToolStripMenuItem` inserted into the **View** menu next to Compact Mode, mirroring the `compactModeMenuItem` pattern at `MainForm.cs:179-182`.
- Its dropdown hosts a `TrackBar` via `ToolStripControlHost`. As a child of the already-themed `mainMenu` (renderer set at `MainForm.cs:652`) the dropdown inherits `ThemedToolStripRenderer`.
- **TrackBar:** `Minimum=75`, `Maximum=250`, `TickFrequency=25`, `SmallChange=5`, `LargeChange=25`, `AutoSize=false`. Sized for DPI by mirroring `PlotPanel.SetDpi` (`this.DeviceDpi/96.0`): `Size ≈ (Round(150*scale), Round(45*scale))`.
- **Live % readout:** show current percent either as a `ToolStripLabel` in the dropdown or encoded in the submenu text (`Text Size (150%)`). Both theme correctly via `OnRenderItemText`.
- **Default 100% reproduces today's look exactly** on every DPI (verified by construction below).

### 3.2 Central apply method

Add one method, `ApplyUiTextScale(int percent)`, called on TrackBar `ValueChanged` (covers drag + keyboard) and once at startup. It performs, in order:

1. **Tree font.** Build `scaled = new Font(baseFont.FontFamily, baseFont.SizeInPoints * percent/100f, baseFont.Style)` from a captured **base** font (`SystemFonts.MessageBoxFont`, captured once — never multiply the already-scaled font, or scales compound). Assign `treeView.Font = scaled`. This auto-propagates to every text `NodeControl` because the Aga draw/measure pipeline uses `context.Font = treeView.Font` (`TreeViewAdv.Draw.cs:17-19,69-71`; `BaseTextControl.cs:146-157`). Setting per-`NodeControl.Font` is dead code — do not. **Dispose the previously-allocated scaled font** each apply (the `TreeViewAdv.Font` setter does not dispose the old font → GDI handle leak); never dispose the shared `SystemFonts.MessageBoxFont`.
2. **Row height (the load-bearing fix).** `_standardRowHeight` is captured once against the default font at `MainForm.cs:171`, and `ApplySensorTreeLayout`'s normal branch restores it (`MainForm.cs:860`) → clipped rows at any scale. **Change the normal branch to derive height from the live font**, mirroring the ctor formula: `treeView.RowHeight = Math.Max(treeView.Font.Height + 1, 18)`. The compact branch already does `Math.Max(treeView.Font.Height, 16)` (`MainForm.cs:854`). This makes row height order-independent and composes with Compact Mode and the Show Value/Min/Max toggles. (Deliberate side effect: the Unix path normal height becomes font-derived instead of self-referential — an improvement.)
3. **Column widths.** Value/Min/Max columns are fixed (~100px) and only the name column auto-stretches (`TreeView_SizeChanged`, `MainForm.cs:1865-1874`), so at 150–200% real readings (`5593.0 MHz`, `62.3 °C`) would ellipsize. **Scale the Value/Min/Max standard widths and the compact 78px cap (`MainForm.cs:856`) by `percent/100`, clamped to the existing `20..400` bound (`MainForm.cs:135`).** Treat the scaled width as the new baseline so user drags still work; persisted per-column widths continue to load as today, then are scaled relative to the 100% baseline.
4. **Plot axis text.** Call a new `PlotPanel.SetAxisTextScale(percent/100.0)` (§3.3).
5. **Tree glyphs.** Scale + vertically center the hardcoded glyphs in `Theme.cs` (§3.4).
6. **Persist.** `_settings.SetValue("uiTextScale", percent)` (§3.5).

Route steps 1–3 through the existing `ApplySensorTreeLayout()` (recompute `_standardRowHeight`/scaled widths **before** calling it) so there is exactly one relayout; its tail already calls `TreeView_SizeChanged` + `Invalidate`.

### 3.3 Plot axis scaling — `PlotPanel.SetAxisTextScale(double mult)`

Today no axis font is set anywhere, so tick labels **and** titles both render at `PlotModel.DefaultFontSize = 12` (an unset `TitleFontSize` falls back to the axis's own `ActualFontSize`, not the model's 18). `ScaledPlotModel` scales margins/padding by DPI but the default `PlotMargins` is `NaN`, so that margin scaling is a no-op and **axis fonts are provably not DPI-scaled today**.

`SetAxisTextScale(mult)`:
- Guard/clamp `mult` to `[0.5, 4.0]`; reject NaN/Inf/≤0.
- `double fontSize = _model.DefaultFontSize * mult;` — **DPI-independent** (so `mult=1.0` is byte-identical to today at every DPI; folding DPI in would change today's high-DPI look).
- `foreach (Axis axis in _model.Axes)` (this is `_timeAxis` + all `_axes.Values`, populated at `PlotPanel.cs:474-476`): set `axis.FontSize = fontSize` and `axis.TitleFontSize = fontSize` (set titles **explicitly**, don't rely on the fallback).
- **Tracker/tooltip:** the OxyPlot WinForms tracker is a private WinForms `Label` that never sets its own `Font`, so it inherits `PlotView.Font` ambiently. Capture `_plot.Font.Size` once as the base; set `_plot.Font = new Font(family, base*mult, style)`, disposing the previous one.
- Re-render live via the existing `InvalidatePlotCosmetic()` (`PlotPanel.cs:890`), which re-runs the layout pass so auto-margins (`NaN`) re-derive from the larger labels → **no clipping, no manual margin math**.
- The plot is a **single `PlotPanel` instance** reused across docked/bottom/separate-window modes (`MainForm.cs:943/951/958`), so one call covers all three.
- Optional persistence key `plotPanel.AxisTextScale`, or drive purely from `MainForm`'s `uiTextScale`. **Decision: drive from `uiTextScale`** (single source of truth); no separate plot key.

### 3.4 Tree glyph scaling — `Theme.cs`

`CustomPlusMinusRenderFunc` draws at `size = 8`, `y = rect.Top + 5`; `CustomCheckRenderFunc` (the "plot this sensor" checkbox) at `size = 12`, `y = rect.Top + 1` (`Theme.cs:32-57`). These are fixed px pinned near the row top; in taller rows they stay tiny and top-stuck. **Scale both `size` values by the active factor and vertically center within `rect`** (`y = rect.Top + (rect.Height - size)/2`). The renderers need access to the current scale — expose it via a static on `Theme` (or a field the funcs close over) updated by `ApplyUiTextScale`. This is **core**, not polish: without it the scaling looks half-done. Node icons stay 16px and self-center (`NodeIcon` + `VerticalAlign.Center`) — no change needed.

### 3.5 Persistence

- Key **`uiTextScale`**, stored as **int percent**, default **100**. (There is no `SetValue(string,double)` overload — `double` is Get-only, `PersistentSettings.cs:189`. Int percent pairs naturally with `TrackBar.Value`.)
- **Load** once in the ctor after `_settings.Load(...)` (`MainForm.cs:88-89`) and **apply** it (build the control, restore `TrackBar.Value`, call `ApplyUiTextScale`) after the tree/columns and Compact Mode are wired (~`MainForm.cs:265`), so the row-height/width recompute isn't clobbered by the initial `ApplySensorTreeLayout` pass. Clamp the loaded value to `[75,250]`.
- **Save** happens automatically: `SetValue` on change writes the in-memory dict; the existing `SaveConfiguration()` → `_settings.Save()` (`MainForm.cs:1218-1245`, invoked on close `1320` and logoff `585`) flushes to disk. Mirrors the theme/splitter/window-bounds scalar-setting idiom (`MainForm.cs:695/718/1821-1824`).

### 3.6 Risk checkpoint & fallback (menu-hosted slider)

The menu-hosted `TrackBar` is the highest-risk of the UI options considered. Two behaviors cannot be proven from source and get an explicit **run-and-confirm** gate in the plan:

- **(a)** Does a slider **drag** keep the dropdown open? The keep-open guard (`StopFileHardwareMenuFromClosing`, `MainForm.cs:593-599`) fires on `ItemClicked`, and a drag is not an `ItemClicked`. Verify empirically; add a `DropDown.Closing` guard if needed.
- **(b)** The Win32 trackbar channel/thumb won't fully honor dark-theme `BackColor` under visual styles. Set `host.BackColor`/`trackBar.BackColor` explicitly and confirm the result is acceptable in the Dark/Black themes.

**Pre-agreed fallback** if either is unacceptable: move the `TrackBar` into a small themed **`Text Size…` dialog** (the Option-B design) opened from the menu. This is a *switch*, not a re-design — the `ApplyUiTextScale` core is unchanged.

## 4. QoL extras (all four selected; each independently shippable)

### QoL-1 — Remember window/maximized state + MinimumSize + larger default
Today `MainForm_MoveOrResize` persists only `Location/Width/Height` (`MainForm.cs:~1821-1824`) and `MainForm_Load` restores only `Bounds`; `WindowState` is not saved, and there is no `MinimumSize`. Add: persist/restore `FormWindowState` (reopen maximized if it was), set a sensible `MinimumSize`, and raise the small default window size (`~470x640`). Effort S, risk low.

### QoL-2 — Reclaim empty graph vertical space (investigate → default)
The stacked graphs waste large bands (Power spans ≈ -125..500 W for ~75–125 W data). `CreatePlotModel` restores each axis's zoom from persisted `plotPanel.Min<Type>`/`Max<Type>` (`PlotPanel.cs:464`) and never re-fits; `AutoscaleAllYAxes` (`PlotPanel.cs:946`) exists as prior art. **Investigate at runtime** whether the over-wide range is stale persisted zoom vs. autoscale padding, then either tighten the default padding or make fit-to-data the default (and/or auto-fit on first show). Effort M, risk med — this one is *investigate-then-decide*, so the plan carries a decision point.

### QoL-3 — Auto-fit columns to content
Add a menu action and/or column-divider double-click that measures node text (Aga `TreeViewAdv` can) and sizes the tree columns to content, so long hardware/sensor names aren't ellipsized on a wide window. Complements §3.3's width scaling (that scales with text size; this fits on demand). Effort M, risk low.

### QoL-4 — Larger hover tooltips + plot tracker
The sensor-tree hover tooltip (`NodeToolTipProvider`, enabled at `MainForm.cs:184`) and the OxyPlot tracker balloon both use small default fonts. Provide a larger, readable font — owner-drawn `ToolTip` for the tree; the tracker font already scales via §3.3's `_plot.Font`. Effort M, risk med (tree tooltip needs owner-draw).

## 5. Testing & verification

- **Data-contract regression:** `dotnet test LibreHardwareMonitor.Tests\...csproj -p:Platform=x64` — all 7 (`DataJsonGolden` + `CsvTimestampContract`) must pass unchanged (feature is UI-only).
- **Build:** Release `net10.0-windows` and `net472`, `-p:Platform=x64`, clean.
- **Manual checklist (run the app):**
  1. Slider drag enlarges tree text + plot axis text + titles live; % readout updates.
  2. Drag keeps the dropdown open (or fallback dialog engaged) — §3.6(a).
  3. Dark/Black theme: slider legible/acceptable — §3.6(b).
  4. Value/Min/Max readings stay fully visible (not `...`) at 150–200%.
  5. Tree expand/collapse + plot checkbox glyphs scale and are vertically centered.
  6. Toggle Compact Mode at a non-100% scale → rows stay correct (no clipping, no revert).
  7. Restart → scale restored from `uiTextScale`.
  8. Move plot to separate window → axis text still scaled.
  9. QoL-1: maximize, restart → reopens maximized; window can't shrink below MinimumSize.
- **Where unit-testable:** extract the percent→font-size / percent→row-height / percent→column-width math into pure helpers and unit-test the boundaries (75/100/250, clamp to 20..400). UI wiring stays manual.

## 6. Build/versioning notes

Local (non-CI) builds self-stamp git SHA+date into EXE FileVersion/ProductVersion (`StampLocalBuildVersion`); `AssemblyVersion` stays 0.9.6. After implementation, rebuild Release and restart the monitor per the machine's launch path (see issue #22). No change to the version scheme.

## 7. Implementation phasing (for the plan)

1. **Phase 1 — Core, minus risky UI:** `ApplyUiTextScale` + `PlotPanel.SetAxisTextScale` + row-height fix + column-width scaling + `Theme.cs` glyph scale/center + persistence. Wire to a temporary trigger (or the menu item without the hosted TrackBar) to validate the scaling engine.
2. **Phase 2 — Menu-hosted slider + risk checkpoint §3.6** (drag-keeps-open, dark theme). Switch to dialog fallback here if needed.
3. **Phase 3 — QoL-1** (window/maximized state).
4. **Phase 4 — QoL-3** (auto-fit columns).
5. **Phase 5 — QoL-4** (tooltips + tracker).
6. **Phase 6 — QoL-2** (graph space) — investigate-then-decide; last because it carries a runtime decision.

## 8. Backlog (found in sweep, not selected)

- Manual UI scale factor extended to the **menu bar** (`mainMenu.Font`) and **tray/gadget** — a superset of this feature; could extend `ApplyUiTextScale` later.
- Optional visible **plot legend** (`IsLegendVisible=false` today; `ScaledPlotModel` already builds a scaled legend).
- **Larger tray icons** — low confidence (OS dictates 16px at 100% scaling).
- **Comfortable/Large tree preset** — superseded by the Text Size slider.

## 9. Key code anchors

- Menu insert / option pattern: `MainForm.cs:179-182`, startup apply `:265`.
- Tree font: `MainForm.cs:100-101`; row height ctor `:166`, `_standardRowHeight` `:171`; `ApplySensorTreeLayout` `:824-883` (compact `:854`, normal restore `:860`, compact cap `:856`, captures `:835-837`, tail `:881-882`); col0 stretch `:1865-1874`; width clamp `:135`.
- Persistence: `PersistentSettings.cs` int `:153/158`, float `:172/177`, no `SetValue(double)` `:189`; Load `MainForm.cs:88-89`; Save `:1218-1245`, close `:1320`, logoff `:585`; scalar idiom `:695/718/1821-1824`.
- Plot: `PlotPanel.cs` axes `:432-444`, `_timeAxis` `:348-374`, `ApplyTheme` `:150-159`, axes collection `:474-476`, tracker create `:123-124`, `InvalidatePlotCosmetic` `:890-896`; `ScaledPlotModel.cs:10-18`.
- Glyphs: `Theme.cs:32-57`.
- Aga tree: `TreeViewAdv.Draw.cs:17-19,69-71`, `BaseTextControl.cs:146-157`, `FixedRowHeightLayout.cs:25-28`, `TreeViewAdv.cs:587-592` (OnFontChanged→FullUpdate), `NodeIcon.cs:18-31,44-45`.
