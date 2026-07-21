# Feature Spec: Standard Dashboard Context Layouts

**Status:** planned; implementation plan at
`docs/superpowers/plans/2026-07-21-standard-context-layouts.md`; delivery lane is
PR #29 (`worktree-dashboard-templates`)
**Updated:** 2026-07-21

## Problem

Standard keeps one global trim. Operators who alternate activities (desktop
monitoring, gaming, storage triage) either re-hide and re-pin sensors by hand on
every change or leave the union of everything visible.

The 2026-07-04 spike on `worktree-dashboard-templates` (PR #29) prototyped
per-route dashboard layouts, but it predates the `sq.dashboard.v1` state model,
the shipped Workspace view, and the current masthead. Its code stays archived in
branch history and is not merged; this spec defines the proper rebuild.

## Relationship to shipped Workspace profiles

Workspace already ships host-neutral `Main`, `Gaming`, `Storage`, and `Thermal`
profiles. These are different mechanisms and both remain:

| Surface | Mechanism | What it holds |
|---------|-----------|---------------|
| Workspace profiles | Curated panel documents in their own root view | Named panels with exact SensorId membership |
| Standard contexts (this spec) | Alternate trims of the full Standard console | Hidden set, pins, primaries, order, styles |

The masthead copy is `Context` and applies only to Standard; the spec reuses the
`Main`/`Gaming`/`Storage` names deliberately so both features share one mental
model of the operator's current activity.

## Decisions

| Choice | Decision | Reason |
|--------|----------|--------|
| Control | Dropdown beside the `Dashboard` selector, not the spike's tab bar | Matches the two-orthogonal-selectors masthead design; avoids a second switcher aesthetic |
| Context set | Fixed `main` / `gaming` / `storage` | Matches the deferred lane; free-form named layouts are already served by Workspace |
| Storage | Materialize-swap: `sq.dashboard.v1` stays the single live authority; parked trims live in a new `sq.dashboard.contexts.v1` key | Zero change to every existing read/write/telemetry path; old tabs and old builds never touch the new key |
| Per-context fields | Exactly the 13 curation fields listed below | Telemetry caches, device prefs, and shared overrides must not fork |
| First entry to a context | Seed by copying the current trim | The feature is variant trims of the console, not rebuilds from scratch |
| Scope | Standard view only; selector disabled under Studio/Workspace | Honest control state; no implied effect where none exists |

## Goal

Add a `Context` selector to the masthead that switches the Standard dashboard
between three independently persisted trims (`Main`, `Gaming`, `Storage`)
without touching telemetry, global preferences, Studio, or Workspace.

Per-context fields (the curation subset of `sq.dashboard.v1`):
`hiddenSensorIds`, `pinnedCards`, `panelOrder`, `pinnedOrder`, `graphsEnabled`,
`collapsedPanels`, `cardStyle`, `primaryCards`, `primaryCardsCustomized`,
`cardOrder`, `rowOrder`, `netAdapterOrder`, `hiddenNetAdapters`.

Global fields (never forked): `paused`, `rate`, `theme`, `viewTheme`, all
`studio*` preferences, `sensorAliases`, `rangeOverrides`, and the telemetry
caches `observedMax` and `powerLimitSamples`.

## Non-goals

- No hash routing or deep links; the spike's `#/main` router is not rebuilt.
- No cross-tab `storage`-event synchronization in this slice; the existing
  last-write-wins multi-tab caveat applies unchanged and stays documented.
- No contexts for Studio or Workspace, and no changes to shipped Workspace
  behavior, markup, or state.
- No renameable or user-defined contexts.
- No schema change to `sq.dashboard.v1` itself: no new fields, no version bump.
- No changes to `data.json`, CSV, Prometheus, routes, WMI, hardware access, or
  `AssemblyVersion`.

## State and behavior

New storage key `sq.dashboard.contexts.v1`:

```json
{
  "version": 1,
  "active": "main",
  "saved": {
    "gaming":  { "hiddenSensorIds": [], "...": "curation subset only" },
    "storage": { "hiddenSensorIds": [], "...": "curation subset only" }
  }
}
```

Invariants and behavior:

- `sq.dashboard.v1` always holds the **active** context's trim; `saved` holds
  only inactive contexts (normalization drops `saved[active]`), each entry
  normalized through the same cleaners as the live state and containing only the
  13 curation fields.
- Switching `A -> B`: park `A`'s curation subset under `saved[A]`; take
  `saved[B]` if present, otherwise seed from the current subset; apply onto the
  live state through `normalizeDashboardState`; persist the contexts key first,
  then `sq.dashboard.v1`; repaint and rerender. Same-context selection is a
  no-op with no storage writes.
- If a persist partially fails, the outgoing trim is never lost (it is parked
  before the live key is overwritten); the safe-storage memory fallback keeps
  the session consistent within the session.
- The selector is disabled (with an explanatory title) whenever
  `viewTheme !== 'standard'` and repainted on every view change.
- Multi-tab: a tab running pre-context code keeps operating on `sq.dashboard.v1`
  (whatever trim is materialized) and cannot corrupt the contexts key. Two
  context-aware tabs share one active pointer last-write-wins, same class as the
  existing dashboard-state caveat: close stale tabs after deploying.

## Compatibility

Absent contexts key = exactly today's behavior (active `main`, live state
untouched). Old builds and old tabs ignore the new key. Deleting the key loses
parked trims only — never the live `sq.dashboard.v1` state. The change is
confined to embedded HTML/CSS/JS and browser-local storage; `net472` and
`net10.0-windows` hosts behave identically.

## Acceptance

- [x] Masthead has a labelled `Context` selector with stable option values
  `main` / `gaming` / `storage`, placed beside the `Dashboard` selector.
- [x] Selector is disabled under Studio and Workspace and re-enables on return
  to Standard.
- [x] The 13 curation fields isolate per context; hiding/pinning/starring in one
  context never leaks into another.
- [x] `theme`, `viewTheme`, `paused`, `rate`, Studio preferences, aliases,
  range overrides, `observedMax`, and `powerLimitSamples` survive every switch
  unchanged.
- [x] Trims persist across reload; first entry to a context seeds from the
  current trim; same-context selection writes nothing.
- [x] Malformed or throwing storage degrades to defaults without breaking the
  session (safe-storage path).
- [x] Node model tests cover normalize/extract/apply/switch including telemetry
  preservation; markup self-test asserts the selector; full self-test suite
  green (record the new total; was 285/285).
- [x] .NET suite passes and both x64 Release targets build clean from isolated
  outputs.
- [x] Live served-fixture browser matrix passes in dark/light at desktop and
  390 px: switch isolation, reload persistence, disabled gating, no console
  errors — with at-rest screenshots in both themes.

## Roadmap (deferred)

1. `storage`-event adoption of context switches across same-origin tabs.
2. Optional `#ctx=` deep link once cross-tab semantics are settled.
3. Evaluate per-context Studio preferences only if operators ask.

## Verification

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Live matrix (served fixture, both themes, desktop + 390 px): hide a sensor and
star a card in `Gaming`; verify `Main` unaffected; reload and verify both trims
persist; verify globals (theme, pause, rate, Studio prefs) survive switches;
verify the selector disables under Studio/Workspace; verify an empty console.

## Verification Log

- 2026-07-21 SND-DESK: spec and implementation plan authored; implementation
  pending on the PR #29 lane.
- 2026-07-21 SND-DESK: implemented and verified at `14f39c0`. Deterministic
  gate: `node --check` clean on console.js/workspace.js; selftest
  **304/304** (285 baseline + 14 model + 4 markup + 1 wiring; the plan's
  "306" projection was an arithmetic slip — its own test block holds 14
  assertions, not 16); `node --test` console+workspace suites green; .NET
  suite 150 passed / 1 skipped / 151 total; both x64 Release builds
  (net10.0-windows, net472) 0 warnings 0 errors. Live served-fixture matrix
  (chrome-devtools, fixture.data.json on :8123): Main enabled under
  Standard; hide+star in Main → switch seeds Gaming identically and parks
  Main; second hide in Gaming stays out of Main and returns on re-entry;
  reload restores active context and both trims; theme/pause/rate survive
  switches (parked entries hold exactly 13 fields, `observedMax` 193 entries
  intact); selector disabled with explanatory title under Studio AND
  Workspace, re-enabled on Standard; console clean (zero errors/warnings);
  at-rest screenshots dark+light at 1600px and dark at true 390px emulated
  viewport (no horizontal overflow, selector usable — verified switch at
  390px). Same-context no-op and throwing-storage safety covered by model
  assertions t13/t14.
- 2026-07-21 SND-DESK: post-review hardening — exact-13-key allowlist pin and
  throwing-storage degradation assertion added (selftest 306/306);
  partial-failure invariant wording tightened to the outgoing-trim guarantee.
