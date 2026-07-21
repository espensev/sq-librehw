# Feature Spec: Studio Dashboard View

**Status:** shipped and verified
**Updated:** 2026-07-11.

## Problem

The root dashboard remains the trusted dense console. Before this feature, its
`cardTruth` value changed only a saved HTML attribute and did not produce a
second visual composition.

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

- Automated: JS/selftest passed `252/252`, .NET passed `64/64`, and both x64
  Release targets built without warnings/errors.
- Live: root/assets/data/metrics/proxy returned 200 and retired preview paths
  returned 404. Dark/light, desktop/375px, reduced motion, focus, empty/stale
  states, and zero horizontal overflow passed.
- Compatibility: Standard remained isolated; warm Studio palettes, canvas
  modes, opacity persistence/reset, grouped controls, and focus restoration
  passed with a clean browser console.
