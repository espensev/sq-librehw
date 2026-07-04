# SQ Telemetry Console — Customize Tier 3 (live drag + inline controls + state consolidation) — Design

**Date:** 2026-07-04
**Status:** Approved (design), pending implementation plan
**Baseline:** commit `095bd69` on `feature/web-dashboard-customization` — the existing customization layer (Customize drawer, hidden sensors, pinned cards, up/down reorder, graphs, `sq.dashboard.v1`). Verified green: **SELFTEST 32/32**.
**Supersedes one prior decision:** the baseline spec (`docs/feature-web-dashboard-customization.md`) deliberately kept `sq.theme`/`sq.rate`/`sq.paused`/`sq.panel.*` as separate loose keys and deferred pointer drag as a "future refinement." This design reverses the persistence decision (consolidates into `sq.dashboard.v1`) and implements the deferred drag.

## Context

The web console's customization is functional but drawer-centric: to pin or hide a sensor you open the drawer and search for it; to reorder you click Up/Down. This design makes customization *first-class and in-place*, the current best-practice pattern for a live dashboard, without changing `data.json` (zero contract risk — same constraint as the console redesign and the customization baseline).

## Goals / Non-goals

**Goals**
- **Inline pin/hide:** pin and hide controls directly on hero cards, pinned cards, and panel rows, revealed on hover **and** keyboard focus.
- **Live drag reorder:** pointer drag-and-drop to reorder pinned cards and subsystem panels on the page itself, via a dedicated drag grip, keeping the gap-free CSS-column masonry. Up/Down buttons stay as the keyboard/accessibility path.
- **Consolidated persistence:** fold `sq.paused`, `sq.rate`, `sq.theme`, and every `sq.panel.<hw>` into the single versioned `sq.dashboard.v1` object, with a one-time migration of existing values, so Reset and a future export capture everything and there is one source of truth.

**Non-goals**
- No change to `data.json`, `/Sensor`, `/metrics`, CSV, or `AssemblyVersion`.
- No export/import of config (deselected by the user for this pass).
- No Tier 1 (in-place DOM diffing) or Tier 2 (theme FOUC, container queries, Page Visibility) work — separate future efforts.
- No drag on the auto-selected Primary Flight Display (it is heuristic and has no stored order); drag applies only to **pinned cards** and **panels**.
- No hardware control writes; the dashboard stays read-only.

## Architecture

Pure client-side, no build step, no framework — extends the existing files only:
- `console.js` — model helpers (`SQ.reorderByDrop`, `SQ.migrateLegacyState`, extended state schema/normalize) + DOM wiring (inline controls, drag, rewired persistence).
- `console.css` — inline-control cluster, drag grip, drag ghost / source-dim / insertion indicator, coarse-pointer reveal.
- `index.html` — expected **unchanged** (controls and drag ghost are injected by JS). If a change is needed it is limited to a static drag-ghost host element.
- `webtests/console.test.html` — new pure-model assertions (target ~32 → ~40 green).

The model/DOM split from the baseline holds: pure logic lives under `window.SQ.*` and is unit-tested; DOM wiring is gated behind `!window.SQ_NO_BOOT`.

## Feature 1 — Inline pin/hide controls

Each card and row gains a control cluster that is visually quiet until the card/row is hovered or a control inside it receives keyboard focus (`:hover`, `:focus-within`). On coarse pointers (`@media (hover:none)`) the cluster stays visible (no hover to reveal it).

Controls per surface:
- **PFD hero card** → `Pin` toggle + `Hide` button.
- **Pinned card** → `Unpin` (the pin toggle in active state) + drag grip (Feature 2).
- **Panel row (a sensor)** → `Pin` toggle + `Hide` button.

Semantics:
- **Pin** is a toggle reflecting `isPinned(sensorId)`: pinning adds `{id, title:''}` to `pinnedCards` and the id to `pinnedOrder` (identical to the drawer's pin); toggling again unpins. Same code path as the drawer, so the drawer and inline controls stay consistent.
- **Hide** hides the sensor everywhere via the existing `hiddenSensorIds` mechanism (same as the drawer). Hiding a hero card removes that sensor from the PFD and from panels on the next render. This is the honest, consistent behavior; to unhide, use the drawer's Hidden tab. Hide is a one-way button on a visible surface (a visible card is by definition not hidden).

Each control is a real `<button>` with an `aria-label` (e.g. `Pin CPU Temp`, `Hide CPU Temp`, `Unpin RAM Used`). Click handling is by **event delegation** on the section containers (`#pfd`, `#pinned`, `#panels`), attached once at init (not per render), reading `data-act` + `data-id` from the pressed control. Row controls live in the panel body (not the header), so they never trigger the header's collapse toggle; the drag grip on the header calls `stopPropagation` so it does not collapse either.

## Feature 2 — Live drag-and-drop reorder

Applies to `#pinned` (pinned-card grid) and `#panels` (subsystem masonry). Pointer-based, no library.

Interaction:
1. A **drag grip** appears (hover/focus) on each panel header and each pinned card.
2. `pointerdown` on a grip starts a drag: set `state.dragging` truthy, `setPointerCapture`, dim the source element (`.dragging`), and create a **compact drag ghost** — a small `position:fixed` chip showing the item's name + class tag (not a full clone; panels can be very tall), following the pointer, `pointer-events:none`.
3. `pointermove` moves the ghost and computes the insertion index by hit-testing the pointer against the bounding rects of the sibling draggable elements in the same container (works regardless of CSS-column flow, because rendered rects are read live). A thin **insertion indicator** marks the target slot.
4. `pointerup` computes the final target index, calls the pure `SQ.reorderByDrop(orderedKeys, movedKey, targetIndex)` to produce the new key order, writes it to `panelOrder` / `pinnedOrder`, saves, and re-renders; the masonry reflows into the new order. Pointer capture released, ghost + indicator removed, `state.dragging` cleared.
5. `Escape` (or `pointercancel`) during a drag cancels: restore, no reorder.

**Re-render safety:** the console re-renders every poll tick. A tick that fires mid-drag would destroy the dragged node. Guard: while `state.dragging` is set, `tick()` skips its render cycle (returns early); the next tick after `pointerup` renders normally. This is the one behavior most likely to break the feature and must be implemented.

**Keys:** `panelOrder` holds panel keys (`SQ.panelKey` = `hwid` or `hw:<name>`); `pinnedOrder` holds sensor ids. `SQ.reorderByDrop` operates on whatever key list it is given and is fully unit-tested; the DOM layer only supplies the current ordered keys and the drop target index.

**Keyboard parity:** the grip is pointer-only; keyboard users reorder with the existing Up/Down buttons in the drawer's Layout and Cards editors, which remain. This is stated so the pointer path is not the sole ordering mechanism.

## Feature 3 — Consolidated persistence

Extend the `sq.dashboard.v1` object with four fields (all defensively defaulted by `normalizeDashboardState`, so old blobs without them load cleanly):

| Field | Type | Default | Replaces |
|---|---|---|---|
| `paused` | boolean | `false` | `sq.paused` (`'1'`/`'0'`) |
| `rate` | number, clamped 1–10 | `2` | `sq.rate` |
| `theme` | `'dark'` \| `'light'` | `'dark'` | `sq.theme` |
| `collapsedPanels` | `{ [hwName]: boolean }` | `{}` | `sq.panel.<hw>` |

`collapsedPanels` is a **map**, not a list, to preserve today's tri-state: key present and `true` = explicitly collapsed, present and `false` = explicitly expanded, **absent** = use the code's default hint (this matters for the Network panel, which defaults collapsed — an expanded choice must be recordable). Panel collapse stays keyed by hardware **name** (matching the legacy `sq.panel.<hw>` scheme); ordering stays keyed by `panelKey`. That pre-existing split is preserved, not unified (out of scope).

**Migration** — `SQ.migrateLegacyState(storage, state)`, run once on load right after `loadDashboardState`:
- If `sq.paused` / `sq.rate` / `sq.theme` exist, fold their values into the state (only when the state has not already stored its own), then `removeItem` them.
- Enumerate `localStorage` keys matching `^sq\.panel\.`; for each, set `collapsedPanels[<hw>] = (value === '1')`, then remove the key.
- Save the merged state. Because the legacy keys are removed, re-running is a no-op (idempotent). Missing `removeItem` on a mock storage is tolerated.

**Runtime rewiring:** `state.paused`/`state.rate` and the theme now read from and write to `state.dashboard` via `saveDashboard()`; the theme toggle, rate slider, pause button, and panel-collapse handler all persist through the one object. All direct `localStorage` reads/writes for `sq.paused`/`sq.rate`/`sq.theme`/`sq.panel.*` are removed. Existing drawer Reset actions (`reset-hidden`, `reset-panels`, `clear-pinned`) are unchanged and continue to leave theme/rate/pause alone.

## Status model & data contract

Unchanged. Status still comes only from temperature bands and SSD life; everything else is info. `data.json`, `/metrics`, CSV, and the desktop tree are untouched — the golden/contract tests stay green because no server or data code changes.

## Verification

- **No-regression gate:** `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` (the 7 data-contract tests + suite stay green — proves `data.json`/CSV untouched).
- **Build:** `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` (embedded assets resolve).
- **Model self-test:** `webtests/console.test.html` gains cases for `reorderByDrop` (move up/down/no-op/out-of-range), `migrateLegacyState` (folds legacy keys + removes them + idempotent), `collapsedPanels` normalize (map of string→bool, tri-state), and pin-toggle state. Target ~40 green, run in-browser and via the Node harness.
- **End-to-end (browser):** hover a hero card → pin/hide controls appear; pin reflects in the drawer and survives reload; hide drops the sensor from PFD + panels while `data.json` still lists it. Drag a panel grip → masonry reflows into the new order, survives reload; a poll tick mid-drag does not disrupt the drag. Toggle theme/rate/pause/collapse → all persist through `sq.dashboard.v1`; legacy `sq.*` keys are gone from `localStorage` after first load.
- **Migration check:** seed old `sq.paused`/`sq.rate`/`sq.theme`/`sq.panel.*` keys, load once, confirm values carried into `sq.dashboard.v1` and the old keys removed.

## Risks

| Risk | Mitigation |
|---|---|
| Poll tick re-renders mid-drag, destroying the dragged node | `state.dragging` guard makes `tick()` skip render while dragging |
| Delegated listeners double-bound across renders | Attach once at init on stable section containers, not in `render()` |
| Drag ghost from a tall panel is huge/janky | Ghost is a compact name+tag chip, not a full clone |
| Inline controls trigger panel collapse | Row controls live in the panel body; header grip calls `stopPropagation` |
| Coarse-pointer users can't reveal hover controls | `@media (hover:none)` keeps the cluster visible |
| Consolidation loses a user's existing collapse/theme/rate/pause | One-time `migrateLegacyState` folds legacy keys before removal; `normalize` defaults cover absent fields |
