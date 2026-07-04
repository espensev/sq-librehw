# Web Dashboard Customization Implementation Plan

**Feature spec:** [`../../feature-web-dashboard-customization.md`](../../feature-web-dashboard-customization.md)
**Status:** Draft plan
**Updated:** 2026-07-04
**Scope:** `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`, `console.css`, `console.js`, `webtests/console.test.html`, and docs only.

## Goal

Make the SQ Telemetry Console configurable without changing LibreHardwareMonitor's raw sensor contracts. The dashboard should let the operator hide noisy sensors, create pinned cards from existing readings, reorder cards/panels, optionally show client-side card graphs, reset layout state, and keep the automatic dashboard usable by default.

## Constraints

- Do not edit `HttpServer.cs`, `BuildDataJsonObject`, `GenerateJsonForNode`, `/Sensor`, `/metrics`, CSV logging, or `AssemblyVersion`.
- Keep dashboard customization browser-local in `localStorage`; no server persistence in this pass.
- Keep the dashboard read-only. No hardware control writes.
- Keep the current hard-coded Nuvoton noisy-temp suppression as the default behavior until the user-configurable hidden list exists.
- Keep exact numeric values and existing row bars; optional graphs and smoothed hero arcs must not replace those readouts.
- Every product-code slice must update `webtests/console.test.html` or add equivalent model checks.

## Phase 0: Commit-Safe Baseline

**Purpose:** make sure the current dashboard cleanup is a stable base before adding larger layout state.

- [ ] Review the current diff for the grid removal and Nuvoton dashboard suppression.
- [ ] Run `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`.
- [ ] Run `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`.
- [ ] Runtime-check `http://localhost:8085/`, `console.js`, `console.css`, and `data.json`.

**Acceptance:** current dashboard still loads; raw `data.json` still contains the suppressed Nuvoton sensor IDs; served CSS has no page grid.

## Phase 1: Versioned Dashboard State

**Purpose:** add the safe storage layer before adding UI controls.

Files:
- `console.js`
- `webtests/console.test.html`

Tasks:
- [ ] Add `SQ.DEFAULT_HIDDEN_SENSOR_IDS` for the current Nuvoton noisy inputs.
- [ ] Add `SQ.loadDashboardState(storage)` and `SQ.saveDashboardState(storage, state)`.
- [ ] Use one versioned key, recommended `sq.dashboard.v1`, with shape:

```json
{
  "hiddenSensorIds": [],
  "pinnedCards": [],
  "panelOrder": [],
  "pinnedOrder": [],
  "graphsEnabled": false
}
```

- [ ] Validate loaded state defensively: bad JSON, wrong types, unknown fields, or missing keys fall back to defaults.
- [ ] Make `SQ.visibleSensors(sensors, state)` use default hidden IDs plus user hidden IDs.
- [ ] Add self-test cases for bad state, default hidden IDs, and explicit user hidden IDs.

**Acceptance:** hidden-sensor behavior is state-driven, but the visible UI still behaves like today before any controls are added.

## Phase 1.5: Motion Damping and Optional Graphs

**Purpose:** address QA feedback that rapid card/gauge movement looks off at the 2-second poll interval while keeping bars and exact values.

Files:
- `index.html`
- `console.css`
- `console.js`
- `webtests/console.test.html`

Tasks:
- [ ] Add a persisted `Graphs` toggle.
- [ ] Keep short client-side history for numeric sensors in the current browser session only.
- [ ] Render compact card sparklines only when the toggle is enabled.
- [ ] Smooth decorative hero arc movement between polls while keeping displayed numbers exact.
- [ ] Do not remove row bars, pause/rate/theme, or panel collapse controls.

**Acceptance:** graphs can be enabled/disabled without changing raw telemetry; fast-changing card arcs no longer visually jump as hard between polls.

## Phase 2: Hidden Sensor Manager

**Purpose:** let the user remove stale/fake/noisy sensors from the dashboard view.

Files:
- `index.html`
- `console.css`
- `console.js`
- `webtests/console.test.html`
- `local-ui-customizations.md`

Tasks:
- [ ] Add a compact `Customize` button in the masthead.
- [ ] Add a non-card drawer/dialog with tabs or segmented controls for `Hidden`, `Cards`, and `Layout`.
- [ ] In `Hidden`, show searchable flattened sensors with hardware, type, value, and ID.
- [ ] Let the user hide/unhide sensors; changes persist in `sq.dashboard.v1`.
- [ ] Show default-hidden sensors as hidden by default, with an explicit "show anyway" action if needed.
- [ ] Add `Reset hidden sensors` to restore defaults.
- [ ] Ensure hidden sensors remain discoverable in the manager even though they do not render in cards/panels.

**Acceptance:** hiding `Temperature #5` removes it from dashboard cards/panels while `http://localhost:8085/data.json` still contains `/lpc/nct6701d/0/temperature/5`.

## Phase 3: Pinned Cards

**Purpose:** let the operator create more cards for the sensors they care about.

Files:
- `index.html`
- `console.css`
- `console.js`
- `webtests/console.test.html`

Tasks:
- [ ] Add `SQ.cardFromSensor(sensor, options)` model helper.
- [ ] Add `SQ.resolvePinnedCards(sensors, state)` that ignores missing sensor IDs without breaking render.
- [ ] Add a pinned-card strip before the automatic PFD, or make pinned cards appear first inside the PFD area with a clear visual distinction.
- [ ] In the `Cards` customize tab, add search/filter over flattened sensors and a `Pin` action.
- [ ] Support single-sensor cards first: current value, unit, hardware source, min/max when meaningful, status label/glyph.
- [ ] Allow card title override in state, but default to the sensor text.
- [ ] Add remove/unpin action.

**Acceptance:** user can create a pinned card for an existing sensor, reload the page, and see the card restored; missing sensors do not create blank broken cards.

## Phase 4: Reorder and Reset

**Purpose:** make layout adjustable without making the UI fragile.

Files:
- `console.css`
- `console.js`
- `webtests/console.test.html`

Tasks:
- [ ] Add stable IDs for rendered panels based on `hwid` where available, falling back to hardware name.
- [ ] Persist `panelOrder` and `pinnedOrder`.
- [ ] Add keyboard-accessible move up/down buttons in customize mode for pinned cards and panels.
- [ ] Add reset actions for panel order, hidden sensors, and pinned cards while leaving theme/rate/pause alone.
- [ ] Keep pointer drag as an optional future refinement unless testing shows it is reliable.

**Acceptance:** reorder survives reload; reset returns automatic order; panel collapse persistence still works.

## Phase 5: Visual Polish and Fit

**Purpose:** keep the dashboard operationally dense and distinct from the user's site.

Files:
- `console.css`
- `index.html`
- `console.js`

Tasks:
- [ ] Check desktop and narrow viewport: no clipped button text, overlapping header controls, or card text overflow.
- [ ] Keep cards at 8px radius or less if revising component styling; avoid adding new page-level grid or site-like background.
- [ ] Use compact icons or glyphs for move/remove/hide controls with accessible labels.
- [ ] Ensure customize controls do not look like hardware write controls.
- [ ] Keep the default first viewport focused on live telemetry, not a landing/help page.

**Acceptance:** dashboard remains scan-friendly and dense; customization controls are available but not visually dominant.

## Phase 6: Verification and Handoff

Run:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Manual checks:
- [ ] Start/restart LibreHardwareMonitor and open `http://localhost:8085/`.
- [ ] Hide a live sensor and confirm it disappears from dashboard cards/panels.
- [ ] Confirm the same sensor still exists in `http://localhost:8085/data.json`.
- [ ] Pin a sensor card, reload, and confirm persistence.
- [ ] Toggle graphs, reload, and confirm persistence.
- [ ] Reorder pinned cards and panels, reload, and confirm persistence.
- [ ] Reset layout and confirm automatic dashboard order returns.
- [ ] Toggle theme, rate, pause, and panel collapse to confirm existing `sq.*` keys still work.

## Recommended First Implementation Slice

Start with **Phase 1 + Phase 2 only**. That directly handles stale/fake sensors and builds the state foundation. Pinned cards and drag reorder should follow after the hidden-sensor manager is stable, because both depend on the same state loader and sensor lookup model.
