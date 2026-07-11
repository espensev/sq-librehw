# Feature Spec: Studio Dashboard View

**Status:** verified locally.  
**Updated:** 2026-07-11.

Design recovery and extension findings are recorded in
`docs/discovery-studio-distinction.md`.

## Problem

The root dashboard survived and remains the trusted dense console.
Its `View` dropdown also exposes `cardTruth`, but that value currently changes
only a saved HTML attribute. It does not produce a second visual composition.

## Goal

Keep the existing dashboard unchanged as `Standard` and turn the existing
second dropdown value into `Studio`: a modern, responsive monitoring view with
browser-local appearance and layout controls.

Studio must:

- use the same read-only `data.json` telemetry and truth/range helpers;
- show a distinct overview, focus-card deck, and compact system summaries;
- use a Studio-specific editorial type treatment and asymmetric default layout;
- use a warm Studio palette that does not borrow cyan, blue, or green branding;
- let the operator choose accent, canvas treatment and opacity, density, focus
  layout, focus-card count, sparkline visibility, and visible sections;
- reuse existing sensor aliases, hidden state, primary selection, pins, and order;
- persist preferences in the existing normalized `sq.dashboard.v1` state;
- work with keyboard controls, dark/light themes, and reduced motion.

## Non-goals

- No new server route or restored `/dash/cardtruth/` preview.
- No hardware-specific sensor IDs, guessed limits, or missing-as-zero values.
- No write controls or dashboard calls to `/Sensor?action=Set`.
- No replacement of the existing Standard markup or customization behavior.

## Behavior

The root dropdown is labelled `Dashboard` and offers `Standard` and `Studio`.
The stored values remain `standard | cardTruth` for backward compatibility.
Unknown values fall back to Standard.

Standard keeps the current PFD, subsystem, network, and expansion surfaces.
Studio renders from the same normalized sensor model and provides:

- a health summary with current alert counts;
- a configurable number of primary focus cards with live sparklines;
- optional compact system and network summary grids;
- a grouped Customize disclosure for atmosphere, layout, content, and
  reset-to-defaults;
- an atmosphere opacity control from `0%` to `100%` that saves immediately.

The accents are `Coral`, `Rose`, `Amber`, and `Plum`. The canvas presets are
`Ember`, `Strata`, and `Plain`: `Ember` is the default warm ambient treatment,
`Strata` uses broad layered bands rather than a grid, and `Plain` keeps only a
subtle vignette. Focus layout offers the asymmetric `Spotlight` default and an
even `Grid`. These choices affect Studio only.

Changing a Studio setting saves immediately and rerenders without a page reload.
If telemetry is stale, the existing stale state remains visible in both views.
If no primary cards resolve, Studio shows a clear empty-state action instead of
inventing readings.

## Compatibility

The additive Studio fields normalize independently and default safely when old,
malformed, or partially written local state is loaded. Telemetry-only saves must
preserve them. `data.json`, Prometheus, CSV, server routing, assembly version,
and the retired-route contract remain unchanged.

The change is confined to embedded HTML/CSS/JS. It adds no admin or hardware
access requirement, behaves identically in the `net472` and `net10.0-windows`
hosts, and relies on responsive browser layout rather than fixed-DPI geometry.
Upstream-sync risk stays limited to this fork's existing web-resource surface.

## Acceptance

- [x] Live Standard matches the pre-change structure and behavior.
- [x] Studio is visibly distinct from Standard through typography, composition,
  and surfaces, and remains selected through the root dropdown.
- [x] Studio uses no cyan, blue, or green branding accents; warm Studio-only
  sensor/status colors remain readable in dark and light themes.
- [x] Studio preferences persist and malformed values normalize safely,
  including canvas, opacity, focus layout, and sparkline visibility.
- [x] Both views remain read-only and consume identical telemetry truth.
- [x] Studio has labelled controls, usable focus order, and honest empty/error states.
- [x] Ember, Strata, and Plain pass dark/light and desktop/mobile checks;
  reduced-motion behavior remains intact.
- [x] `/dash/cardtruth` and `/dash/cardtruth/` remain 404.

## Verification

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Also smoke both dropdown values against live telemetry in dark/light and at a
desktop plus narrow viewport. Record results below before handoff.

## Verification Log

- 2026-07-11: JS syntax and web selftest passed `252/252`; .NET tests passed
  `64/64`; Release x64 builds passed for `net10.0-windows` and `net472` with no
  build warnings or errors.
- 2026-07-11: rebuilt net10 app runs as PID `58740`. Root, assets, data, metrics,
  and local proxy returned 200; both retired preview paths returned 404.
- 2026-07-11: Coral, Rose, Amber, and Plum resolved to warm accent values.
  Studio focus/status values used gold, coral, rose, plum, and sand tokens in
  dark/light; cyan, blue, and green branding options are absent.
- 2026-07-11: Ember, two-layer non-grid Strata, and Plain passed dark/light.
  Atmosphere opacity was exercised at `0%`, `35%`, and `100%`; `35%` survived
  reload and Reset restored Coral/Ember/`55%`/Spotlight/sparklines.
- 2026-07-11: grouped Customize controls passed desktop and 375px checks with
  one-column mobile groups, correct overlay stacking, and zero x-overflow. The
  existing reduced-motion rule covers the new transition; browser errors were empty.
- 2026-07-11: with Strata stored, Standard hid the atmosphere, restored its
  base `9px` sigil and `blur(10px)` masthead, kept Graphs visible, and had zero
  x-overflow.
- 2026-07-11: final live smoke confirmed the opacity range has an explicit
  label, customization preserves `stale - retrying`, Studio replaces the shared
  green active-control token, and removing a focused card moves focus to the
  next card. Defaults were restored afterward.
