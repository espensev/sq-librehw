# Feature Spec: Standard Dashboard Context Layouts

**Status:** source merged and browser-fixture-verified; PR #29 merged to master
2026-07-21 (merge `04a04aa`, selftest 306/306). No live runtime promotion was
performed in this packet.
**Updated:** 2026-07-21

## Problem

Standard keeps one global trim. Operators who alternate activities (desktop
monitoring, gaming, storage triage) either re-hide and re-pin sensors by hand on
every change or leave the union of everything visible.

The 2026-07-04 spike on `worktree-dashboard-templates` (PR #29) prototyped
per-route dashboard layouts, but it predates the `sq.dashboard.v1` state model,
the shipped Workspace view, and the current masthead. The original spike remains
only in the merged lineage: `0fde201` restored all three spike files to the
master baseline before the proper rebuild, so none of the spike content shipped.

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
- [x] Browser-served fixture matrix passes in dark/light at desktop and 390 px:
  switch isolation, reload persistence, disabled gating, no console errors, and
  screenshots inspected during the verification session.

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

Browser-served fixture matrix (both themes, desktop + 390 px): hide a sensor and
star a card in `Gaming`; verify `Main` unaffected; reload and verify both trims
persist; verify globals (theme, pause, rate, Studio prefs) survive switches;
verify the selector disables under Studio/Workspace; verify an empty console.

## Verification Log

- 2026-07-21 SND-DESK: PR #29 merged as `04a04aa` after exact-13-field and
  throwing-storage hardening. Node syntax passed, selftest passed 306/306, Node
  suites passed 18/18, .NET passed 150 with 1 skipped, and isolated x64 Release
  builds for net10.0-windows and net472 completed with 0 warnings and 0 errors.
  The dark/light desktop and 390 px browser-fixture matrix passed; screenshots
  were session-only. Final master multi-architecture/packaging run
  `29852462788` passed at `0b427c8`. This verifies source, not live promotion.
