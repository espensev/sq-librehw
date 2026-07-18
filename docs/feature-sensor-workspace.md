# Feature Spec: Sensor Workspace

**Status:** deployed on SND-HOST; deterministic and live served-asset smoke verified
**Updated:** 2026-07-18

## Problem

Standard is a dense hardware console and Studio is a curated overview. Neither
lets an operator assemble named, task-specific sensor layouts without changing
global visibility, primary-card, or panel settings.

## Goal

Add `Workspace` as a third root dashboard view. It provides named profiles and
ordered sensor panels while preserving the existing telemetry and hardware
boundaries.

The first slice must:

- ship host-neutral `Main`, `Gaming`, `Storage`, and `Thermal` profiles;
- support card, table, and honest small-multiple graph panels;
- let operators add, rename, reorder, retarget, and remove panels;
- let each panel select and order exact live `SensorId` values;
- create, rename, duplicate, and delete user profiles;
- import and export bounded, versioned JSON profile documents;
- reuse shared aliases, ranges, values, status rules, and history;
- provide clear loading, empty, missing-sensor, import-error, and success states;
- remain keyboard usable and responsive in dark and light themes.

## Non-goals

- No hardware writes, control sliders, or `/Sensor?action=Set` calls.
- No change to `data.json`, CSV, Prometheus, routing, or assembly version.
- Workspace does not calculate derived sensors or render cross-unit multi-series
  overlays. It may present an additive derived sensor supplied by the backend.
- No replacement of Standard or Studio and no migration of their saved state.
- No WebView2, native Sensor Manager, or Avalonia cutover in this slice. Those
  remain parallel frontends over the same future presentation contract.

## State and behavior

The existing `sq.dashboard.v1` state remains the authority for shared dashboard
preferences and telemetry caches; Workspace contributes only
`viewTheme=workspace` there. Its model is isolated in `sq.workspace.v1` and
normalizes independently. It contains a schema version, the active profile,
ordered profiles, ordered panels, panel type, and exact sensor membership.

Built-in profile panels begin with semantic presets instead of machine-specific
IDs. `Main` chooses useful primary telemetry, `Gaming` emphasizes CPU/GPU
telemetry, `Storage` emphasizes storage devices, and `Thermal` combines
temperature, temperature-rate, fan, control, load, and power semantics. Editing
a preset panel materializes its current ordered SensorIds. Export resolves presets into an
explicit document without changing the local adaptive profile, and waits for a
live snapshot rather than exporting an accidental empty preset. Raw labels and
SensorIds remain available even when an alias is displayed.

Workspace membership is independent of Standard/Studio hidden state. Shared
sensor aliases and range overrides still apply. Missing imported SensorIds are
retained and reported rather than silently deleted, allowing a temporarily
unavailable device to return.

Graph panels render labelled small multiples with an honest per-sensor range.
They do not overlay unlike units on a shared scale. Missing values render as an
em dash and never as zero.

Import/export operates on the active profile with schema
`sq.workspace.profile`, version `1`. State is bounded to 10 profiles, 12 panels
per profile, and 24 SensorIds per panel; names are at most 80 characters,
SensorIds at most 192 characters, profile documents 256 KiB, and stored state
3 MiB. Invalid JSON/schema/version, empty profiles, and over-limit collections
are rejected. Safe field errors such as an unknown panel type or duplicate ID
normalize to bounded defaults. ID collisions receive deterministic numeric
suffixes and existing profiles are never overwritten. Imported panels never
receive authority to activate internal adaptive presets: import clears `preset`
and preserves only the document's normalized explicit SensorIds.

Workspace state schema version `2` adds `Thermal` once while loading a version
`1` state, provided the 10-profile safety bound has room. A migrated version `2`
state never resurrects a Thermal profile that the operator later deletes.
Portable profile documents remain version `1`.

Built-in profiles are editable and deletable like other profiles. `Reset
workspace` restores the four current built-ins and removes local profile
changes after confirmation. A single remaining profile cannot be deleted.

## Compatibility

The change is confined to embedded HTML/CSS/JS and browser-local storage. It
adds no admin requirement and behaves the same in the `net472` and
`net10.0-windows` hosts. Unknown root view values still fall back to Standard;
old dashboard state loads unchanged.

The Workspace module must stay presentation-only so a later WebView2 shell or
parallel Avalonia frontend can consume an equivalent profile document without
owning hardware collection or the deployed `LibreHW-No-UAC` task.

## Acceptance

- [x] Standard and Studio retain their current markup, state, and behavior.
- [x] Workspace persists independently and survives malformed local storage.
- [x] Main, Gaming, Storage, and Thermal resolve without hard-coded host IDs or
  labels.
- [x] Version 1 state gains Thermal once; version 2 deletion remains durable.
- [x] Profile and panel operations persist and preserve ordered SensorIds.
- [x] Import/export round-trips and rejects incompatible or unbounded input.
- [x] Card, table, and graph panels show honest values and missing states.
- [x] Empty, missing-sensor, import-error, and success feedback is explicit.
- [x] Controls are labelled, keyboard usable, and responsive in dark/light
  desktop and narrow layouts.
- [x] Pause/polling, stale state, telemetry history, and retired routes keep
  their existing behavior.

## Roadmap

1. Continue hands-on three-view browser and native accessibility inspection
   against the live task path; the deployed served assets, schema migration,
   Thermal profile, telemetry, and polling endpoints are already verified.
2. Specify a flexibility slice for resizable/reflowing layouts, density and
   appearance controls, sensor search/grouping, bulk membership edits, and
   graph presentation that remains honest about units and missing values.
3. Extract the bounded profile document plus normalized read-only sensor model
   behind a host-neutral presentation contract.
4. Use that contract for a parallel Avalonia prototype. Do not cut over hardware
   collection or `LibreHW-No-UAC` ownership until packaging, DPI, accessibility,
   lifecycle, performance, and feature-parity acceptance gates are defined and
   passed.

## Verification

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Also smoke all three root views against served fixture data in dark/light and
desktop/narrow layouts. Exercise profile/panel edits, import/export, missing
SensorIds, pause, stale telemetry, keyboard focus, and browser-console output.

## Verification Log

- 2026-07-18 SND-DESK -> SND-HOST deployment: both machine identities were
  verified before mutation. Product version
  `0.9.6+ebedd8b-dirty.2026-07-18` was installed under the recreated
  LibreHardwareMonitor runtime and launched by the elevated interactive
  `\LibreHardwareMonitor` task. The live index, `data.json`, and Prometheus
  endpoints returned 200; the served Workspace module reported state version 2
  and the Thermal built-in. The target exposed 468 sensors, and the dashboard
  remained reachable from SND-DESK through the scoped TCP 20000 rule.
- 2026-07-18 Thermal extension: Workspace state moved to version 2 with a
  one-time v1 migration while portable profile documents stayed at version 1.
  The 15-test Node model/polling suite and 285/285 dashboard self-test passed,
  including durable deletion and semantic TemperatureRate selection. Both
  Release targets built from isolated outputs with zero warnings/errors; the
  full .NET suite passed 150 tests with the existing opt-in test skipped.

- 2026-07-15 integrated closeout: `node --check` passed for `workspace.js` and
  `console.js`; the model/polling suites passed 14/14; the console/markup
  self-test passed 285/285; and the .NET suite passed 129 tests with one
  existing opt-in test skipped. Both Release targets built from isolated
  outputs with zero warnings and zero errors. Coverage includes exact size
  limits, durable-storage fallback, preset materialization, honest graph-scale
  derivation, import hardening, and Standard/Studio state isolation.
- 2026-07-15: served-fixture browser acceptance passed against the real DOM.
  Standard, Studio, and Workspace isolated correctly; Main, Gaming, and
  Storage rendered; profile/panel creation, preset materialization, exact
  SensorId selection, and graph panels worked; keyboard focus survived polls,
  top/bottom reorder boundaries, membership changes, and the disabled-action
  limits at 10 profiles and 12 panels; pause/resume worked;
  auxiliary limit readings stayed out of adaptive presets; plotted range
  labels matched semantic and history scales; dark/light desktop and 390 px
  narrow layouts
  had no horizontal overflow; and the browser console reported no warnings or
  errors. Import/export bounds, collision handling, malformed input, missing
  SensorId reconnection, and adaptive-preset export immutability are covered by
  the Node model suite.
- The 2026-07-15 verification used a local fixture and isolated build outputs.
  No source was copied into a deployed runtime and the `LibreHW-No-UAC` task was
  not stopped, restarted, or modified during that earlier verification.
