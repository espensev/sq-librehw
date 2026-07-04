# Feature Spec: Web Dashboard Customization

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Implemented for Console v2; superseded by v3 card-truth/card-first follow-up
**Updated:** 2026-07-04
**Related docs:** [`feature-workflow.md`](feature-workflow.md), [`local-ui-customizations.md`](local-ui-customizations.md), [`superpowers/specs/2026-07-04-web-dashboard-telemetry-console-design.md`](superpowers/specs/2026-07-04-web-dashboard-telemetry-console-design.md), [`superpowers/plans/2026-07-04-web-dashboard-customization.md`](superpowers/plans/2026-07-04-web-dashboard-customization.md), follow-up v3 [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md) (card truth & card-first controls — supersedes the drawer direction)
**Purpose:** let the SQ Telemetry Console hide noisy sensors and persist user-arranged cards without changing LibreHardwareMonitor sensor contracts.

**Supersession note (2026-07-04):** the v2 drawer/list UI shipped as a browser-local customization surface, but it is not the desired long-term interaction model. Operator review after the screenshots called for cards, not side panes: cards and rows should carry source, unit, current value, real range/limit, status, raw details, max override, pin/hide/style, rename/alias, and move actions. Ordering must be possible from the visible UI surface for cards, rows, panels, and network groups, not from a drawer. The follow-up authority is [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md).

## 1. Summary

Add dashboard-local customization to the web console served at `GET /`: hide/de-emphasize noisy sensors, pin/create card views from existing sensors, reorder cards and panels, optionally show compact client-side graphs, and persist layout choices per browser. The raw `data.json`, `/metrics`, CSV, and desktop sensor tree remain unchanged.

This v2 spec explains what shipped. New work must not expand the drawer or replace it with another side pane; v3 should move normal details/actions onto cards and rows, keeping only a compact masthead sensor search/restore popover for sensors that are not currently visible. Dashboard-local aliases are allowed for operator labels such as showing raw `Fan #7` as `Pump`, but raw LibreHardwareMonitor labels and `SensorId` values must stay visible in detail/search.

## 2. Problem and Motivation

The current dashboard is useful but fully automatic. On this host the Nuvoton board exposes unlabeled or bogus temperature inputs such as `Temperature #5`; the console also has no way to pin the exact sensors the operator cares about, create extra cards, or rearrange panels for the active monitoring task. The hero gauge arcs can also look jumpy at the 2-second poll interval when fast-moving values change sharply, even though the exact numeric values and row bars are useful.

## 3. Goals and Non-Goals

**Goals**

- Provide a dashboard-local hidden-sensor list so known noisy readings do not dominate cards.
- Let the user create pinned cards from existing sensors and place them ahead of automatic panels.
- Support reordering for pinned cards and subsystem panels, with keyboard-accessible move controls.
- Keep exact numeric values and existing row bars, while smoothing decorative hero gauge movement so fast-changing values do not visually jump too far between polls.
- Provide optional compact client-side graphs/sparklines for users who want trend context.
- Persist customization in browser `localStorage` with a reset path.
- Keep the default automatic dashboard useful with no setup.

**Non-goals**

- Do not remove sensors from `data.json`, `/metrics`, CSV, or the desktop UI.
- Do not add write/control operations to the web dashboard.
- Do not require a new server endpoint or account/profile system in the first version.
- Do not make machine-wide layout changes shared across browsers in the first version.
- Do not replace the existing row bars or remove existing pause/rate/theme/collapse options.

## 4. Behavior Specification

The dashboard starts in the current automatic view. Console v2 exposes a customize drawer for hiding sensors, creating cards, and changing order. Treat that drawer as transitional compatibility, not as the target for more feature growth.

Hidden sensors are omitted from dashboard hero/card/panel rendering but remain available in a hidden-sensors manager and in raw endpoints. Missing hidden sensors are ignored when they are absent from a later `data.json`.

Pinned cards are built from one or more existing `SensorId` values. A single-sensor card shows sensor name, hardware, current value, min/max where meaningful, status glyph, and optional bar/arc only when the sensor type has a real bounded range. A multi-sensor card groups selected rows under a custom title.

Reordering persists card and panel order. The first implementation may use explicit up/down controls instead of pointer drag; pointer drag can be added later if it stays reliable on desktop and touch input. A reset command clears layout keys and returns to the automatic dashboard. V3 extends this to every visible ordered surface: individual sensor rows/cards, subsystem panels, and network adapter groups, with keyboard-accessible move actions attached to the visible card/row/header surface.

The graph option is dashboard-local and client-side only. When enabled, pinned and automatic cards show compact sparklines from the current browser session's recently polled values. When disabled, existing cards and row bars remain unchanged. Hero gauge arcs may be visually damped between polls, but the displayed number always remains the current `data.json` value.

Failure behavior: if `data.json` fetch fails, the dashboard keeps last-good values and shows the existing stale freshness state. Custom layout data that cannot be parsed is ignored and overwritten only after a new valid change.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Web dashboard gets a customize mode, hidden-sensor manager, card creation flow, reorder controls, optional graph toggle, and reset actions. |
| Settings/config | Browser `localStorage` stores versioned dashboard layout, hidden sensor, pinned card, panel order, and graph-toggle state. |
| Remote web/API | No server or contract change; reads existing `data.json` only. |
| Logging/files | No CSV or app-log change. |
| Hardware/admin flow | None; read-only dashboard behavior. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Keep customization isolated to `Resources/Web/console.js` and `console.css`; no `HttpServer.cs` changes. |
| `net472` vs `net10.0-windows` | Static embedded assets should serve identically from both targets; verify at least the primary `net10.0-windows` build. |
| DPI/mobile | Use stable card dimensions, wrapping text, and touch-friendly drag handles; verify desktop and narrow viewport. |
| Hardware/admin rights | No new hardware access. |
| Existing settings/users | Existing `sq.theme`, `sq.rate`, `sq.paused`, and `sq.panel.*` keys continue to work; new keys are versioned and resettable. |

## 7. Acceptance Criteria

- [ ] A known noisy sensor can be hidden from dashboard cards without disappearing from `data.json`.
- [ ] The user can create at least one pinned card from selected existing sensors.
- [ ] Pinned cards and subsystem panels can be reordered and the order persists after reload.
- [ ] Optional card graphs can be toggled without removing row bars or changing raw telemetry.
- [ ] Fast-moving hero gauge arcs are visually damped while exact numeric readouts remain current.
- [ ] Reset returns the dashboard to the automatic layout.
- [ ] Existing behavior not in scope remains unchanged: `data.json`, `/metrics`, CSV logging, desktop tree visibility, theme/rate/pause persistence, and stale fetch handling.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Web model self-test | Serve repo root and open `webtests/console.test.html` | Self-test passes |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Data contract tests | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | 0 failures |
| Runtime smoke | Launch rebuilt app, open `http://localhost:8085/`, hide a sensor, create a card, toggle graphs, reorder, reload, reset | Layout behavior matches acceptance criteria |
| Raw contract check | Open `http://localhost:8085/data.json` after hiding a sensor | Hidden sensor remains present in raw JSON |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| First release scope | Spec acceptance | Ship hide list + pinned single-sensor cards + reorder, defer import/export |
| Storage schema | Implementation | Single versioned localStorage JSON key `sq.dashboard.v1` |
| Card creation UI | Implementation | Search/filter modal over flattened `data.json` sensors |
| Drag library | Future refinement | First implementation uses explicit up/down controls; pointer drag remains optional |
| Multi-browser sharing | Future feature | Out of scope for v1 |

## 10. Implementation Notes

The immediate dashboard cleanup on 2026-07-04 added a small hard-coded suppression hook for known Nuvoton noisy temperature inputs and removed the page grid background. This draft covers the broader user-configurable version.

The staged implementation plan is in [`superpowers/plans/2026-07-04-web-dashboard-customization.md`](superpowers/plans/2026-07-04-web-dashboard-customization.md).

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-04 | Draft created from live dashboard review | pending | Awaiting maintainer acceptance before broader customization implementation |
| 2026-07-04 | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`; Node VM equivalent of `webtests/console.test.html` | pass | Self-test reported `SELFTEST PASS 32/32` |
| 2026-07-04 | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | pass | 27 tests passed |
| 2026-07-04 | Release builds for `net10.0-windows` and `net472` to temp output directories | pass | Default Release output was locked by the running LibreHardwareMonitor process, so the app was not stopped |
| 2026-07-04 | Temporary fixture site with Chrome screenshots at desktop and mobile-width layouts | pass | Verified current source against `webtests/fixture.data.json`; live endpoint was left untouched |
| 2026-07-04 | Post-v2 operator screenshot review | follow-up required | Drawer/list customization should be minimized; wrong gauge ceilings, fan RPM scaling, duplicate hardware identity, two-GPU display, badge/icon overlap, missing-range honesty, all-surface reorder, and dashboard-local alias/rename are tracked in `feature-web-dashboard-card-truth.md`. |
