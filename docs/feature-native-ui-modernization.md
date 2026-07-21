# Feature Roadmap: Native UI Modernization

**Status:** roadmap defined; implementation not started
**Updated:** 2026-07-21

## Problem and motivation

The native application has a capable read-only sensor tree, graph, tray, and
desktop gadget, but their presentation has grown unevenly:

- the tree is always ordered by hardware type, sensor type, and sensor index;
- ordinary dragging selects rows, but users cannot deliberately reorder or
  group the sensors they care about;
- the theme/scaling foundations are stronger than the remaining icons, row
  treatment, graph controls, and empty/stale states;
- the Windows gadget is one bitmap-backed, vertically growing list with fixed
  ordering and one global configuration;
- advanced dashboard layout behavior exists in the web Workspace, but the
  native tree and gadget do not yet share a host-neutral presentation model.

The goal is a modern, calm monitoring UI that makes important sensors easier to
find, arrange, compare, and keep visible without changing hardware truth or any
downstream telemetry contract.

## Goals

- Add clear sensor search, Favorites, and user-controlled presentation order to
  the native tree.
- Modernize native graphics across Light, Dark, Black, and high-contrast modes.
- Improve graph selection, legibility, summaries, and honest missing/stale
  states without regressing its bounded update path.
- Replace the single legacy gadget experience incrementally with useful,
  reorderable compact-list, card, gauge, and mini-sparkline layouts.
- Support keyboard, UI Automation, text scaling, DPI, multi-monitor recovery,
  and bounded resource use as acceptance gates rather than follow-up polish.
- Create a read-only presentation seam that a future Avalonia prototype can
  consume without taking hardware or process ownership from WinForms.

## Non-goals

- No change to sensor discovery, polling cadence, hardware access, controls, or
  write policy.
- No mutation of the canonical hardware/type/sensor hierarchy or its ordering.
- No change to `data.json`, CSV, Prometheus, routes, sensor identifiers, raw
  labels, value units, missing-value semantics, or `AssemblyVersion`.
- No full native rewrite, WebView2 shell, or Avalonia cutover in the early
  phases.
- No alert automation or automatic hardware action. Visual thresholds remain
  display-only until a separate feature spec says otherwise.
- No live runtime promotion as part of a source/spec packet.

## Current seams to reuse

| Surface | Current seam | Roadmap use |
|---|---|---|
| Sensor tree | `MainForm`, `TreeModel`, `HardwareNode`, `SensorNode` | Keep the canonical model; add a presentation-only order/filter adapter |
| Drag/drop | `Aga.Controls.Tree.TreeViewAdv` | Reuse drop validation, markers, autoscroll, and selected-node drag |
| Theme and scale | `Theme`, `UiScale`, `TextScaleSliderMenu` | Add semantic tokens and scalable assets without replacing independent text scales |
| Graph | `PlotPanel` / OxyPlot | Add accessible series controls and richer honest presentation while preserving bounded history/render paths |
| Gadget | `SensorGadget`, `GadgetWindow` | Preserve the layered-window compatibility path while extracting state, layout, and formatting |
| Web Workspace | `workspace.js` bounded profiles/panels | Reuse state and honesty semantics only; do not couple browser storage to native settings |
| Persistence | `PersistentSettings` and stable `Sensor.Identifier` | Store a versioned, bounded native layout document with safe fallback |

## Governing decisions

### Canonical truth stays immutable

The native `Node.Nodes` order also drives ordered `data.json` IDs/output and the
existing graph traversal/color assignment. User movement is therefore a
**presentation projection only**. The source tree remains machine → hardware →
sensor type → sensor in canonical order.

Cross-hardware organization starts with Favorites over stable sensor IDs. Named
virtual groups are deferred until a later bounded slice is specified. Neither
mechanism reparents a sensor under false hardware or type.

### Reorder and selection are distinct interactions

Ordinary row dragging continues to support swipe selection. Reordering is
available only through an explicit Organize mode or visible drag handle, with
equivalent `Move Earlier`, `Move Later`, `Move to Top`, `Move to Bottom`, and
`Reset Order` commands accessible from the keyboard and context menu.

### Layout state is bounded and recoverable

Each implementation slice must define a versioned settings document with hard
limits on profiles, groups, sensors, gadgets, strings, and import size. Known
sensor IDs retain their rank; newly discovered sensors use canonical fallback
order; absent sensors never become zero and never make the layout unloadable.
Malformed state falls back to a usable canonical view without corrupting the
last valid settings file.

### Visuals remain honest and efficient

- Colors supplement labels/icons; they never become the only status signal.
- Unlike units remain on separate axes or small multiples.
- Missing, warming, stale, and unsupported values are explicit, never `0`.
- Decorative motion respects reduced-motion/high-contrast behavior and is tied
  to real state changes. There is no permanent 60 FPS animation loop.
- New paints, fonts, icons, menus, bitmaps, handles, timers, and subscriptions
  have deterministic ownership and disposal.

## Phased roadmap

| Phase | Priority | Outcome | Depends on |
|---|---|---|---|
| 0. Foundation and interaction prototype | P0 | Accepted layout/theme contracts, bounded schemas, prototypes, and baselines | Current shipped UI |
| 1. Find and organize sensors | P0 | Search, Favorites, and presentation-only tree ordering | Phase 0 |
| 2. Native visual and graph polish | P1 | Coherent scalable graphics and clearer graph interaction | Phase 0; may overlap late Phase 1 |
| 3. Gadget 2.0 vertical slice | P1 | One modern, accessible, reorderable gadget backed by extracted state/layout | Phases 0–2 |
| 4. Multiple gadgets and portable layouts | P2 | Named gadget profiles, monitor recovery, bounded import/export | Phase 3 |
| 5. Host-neutral presentation prototype | P3 | Read-only contract plus parallel Avalonia feasibility build | Stable Phases 1–4 |

Each phase receives a focused implementation/verification update in this spec
before product code starts. This roadmap is sequencing authority, not blanket
approval for all phases at once.

## Phase 0 — foundation and interaction prototype

Deliverables:

- Define the presentation-order and gadget-profile schemas, bounds, migration,
  malformed-state behavior, and missing-sensor reconciliation.
- Prototype the tree Organize mode/handle and keyboard commands in an isolated,
  non-shipping harness without wiring persistence into the product or canonical
  node model.
- Extend the theme vocabulary for surface, muted text, accent, focus, warning,
  critical, hover, and pressed states.
- Choose a scalable, cached icon strategy that works on both target frameworks.
- Capture current Light/Dark/Black/high-contrast baselines at 100/150/200%
  Windows display scaling and the 75/100/150/200/250% UI/graph text-scale
  range, plus UI update latency and GDI/USER handle baselines for the tree,
  graph, and gadget.

Exit gate:

- [ ] Tree and gadget state contracts have explicit bounds and safe migration.
- [ ] The reorder prototype preserves swipe selection and has keyboard parity.
- [ ] Baselines and the visual/accessibility matrix are reproducible.
- [ ] No product behavior or live runtime changed in this phase.

## Phase 1 — find and organize sensors

User-visible behavior:

- `Ctrl+F` or a visible tree search control filters by hardware, type, displayed
  label, and stable sensor ID without changing hidden/plot/gadget membership.
- `View → Organize Sensors` exposes reorder handles and an explicit exit action.
- Sensors can move within their current type group by drag/drop or equivalent
  keyboard/context-menu commands.
- Favorites is a virtual, ordered view over sensor IDs. Adding a Favorite never
  removes or reparents the canonical row.
- Multi-select bulk hide, graph, tray, and gadget actions remain available.
- Reset restores canonical presentation order without deleting unrelated
  aliases, graph choices, gadget membership, or histories.

Acceptance:

- [ ] Order and Favorites persist across restart by stable sensor ID.
- [ ] Hot-plugged sensors merge deterministically; missing sensors do not crash,
  become zero, or block editing/reset.
- [ ] Search, drag, keyboard movement, focus, expansion, and multi-selection
  compose without lost selection or accidental moves.
- [ ] The fixed-fixture `data.json` golden payload stays byte-identical; runtime
  schema, IDs, canonical order, graph membership, and automatic graph colors
  remain unchanged.
- [ ] Malformed or oversized layout state falls back safely and remains bounded.

## Phase 2 — native visual and graph polish

User-visible behavior:

- Tree headers, rows, selection, focus, hierarchy, icons, density, and
  missing/stale states use one semantic theme system across all modes.
- Icons remain crisp at 100/150/200% and do not allocate per paint.
- Optional density presets compose with the existing independent UI and graph
  text scales.
- The graph gains a keyboard-accessible series list/legend with show, isolate,
  color, and reset actions plus current/min/max summaries.
- Optional point markers, restrained fills, and display-only thresholds are
  disabled by default and preserve separate axes for incompatible units.
- Empty, warming, paused, stale, and no-data graph states are explicit.

Acceptance:

- [ ] Light, Dark, Black, and high-contrast states meet contrast, focus, and
  non-color-cue requirements at supported text scales.
- [ ] Plot history bounds, zero-copy materialization, density decimation, zoom,
  time windows, and cosmetic-only invalidation remain intact.
- [ ] Theme/icon/graph changes do not introduce unbounded GDI/USER handles,
  steady-state allocations, timers, or redraw loops.
- [ ] Keyboard and UI Automation can identify and operate new controls.

## Phase 3 — Gadget 2.0 vertical slice

User-visible behavior:

- `View → Gadgets` opens a normal, accessible WinForms editor; the display
  window stays read-only.
- One migrated gadget supports ordered sensor membership and compact-list,
  number card, gauge, and mini-sparkline presentation.
- A profile controls theme, density, size, opacity, lock, topmost, sensor order,
  and per-sensor style without changing the main tree.
- Values reuse one shared formatter/presentation model with the tree and retain
  correct temperature, temperature-rate, throughput, and missing states.
- The existing layered gadget and custom background assets remain available as
  a compatibility fallback until the replacement clears lifecycle parity.

Acceptance:

- [ ] Existing gadget membership and global settings migrate without loss, or
  roll back to the legacy gadget when migration is invalid.
- [ ] Reorder, style, resize, lock, topmost, opacity, and restart persistence
  work with mouse and keyboard editing.
- [ ] Repeated resize/theme/profile/sensor churn stays within the established
  native-handle envelope and never blocks the hardware update thread.
- [ ] Stale, missing, and unsupported values are explicit; gauges never invent
  an unsafe range or combine incompatible units.

## Phase 4 — multiple gadgets and portable layouts

User-visible behavior:

- Operators can create, name, duplicate, reorder, and remove a bounded number
  of gadgets, each with its own ordered sensors and layout.
- Windows snap cleanly, remember monitor-relative placement, and recover onto a
  visible working area when a monitor disappears.
- Bounded JSON import/export supports backup and transfer. Unknown sensors stay
  as labelled missing entries in the editor until remapped or removed.
- Native and web layouts may share a future normalized document, but neither
  silently reads or overwrites the other's current storage key.

Acceptance:

- [ ] Window/profile counts, sensor counts, names, payloads, and history samples
  have tested hard bounds.
- [ ] Duplicate/import never overwrites an existing profile without an explicit
  choice and never performs hardware writes.
- [ ] Multi-monitor, minimize/restore, startup, lock/topmost, and process-exit
  behavior preserve the existing tray and `MainForm` lifecycle contract.
- [ ] Mixed-DPI monitor moves and runtime DPI changes are exercised on both
  net472 and net10.0-windows; unsupported per-monitor behavior is documented
  and never corrupts gadget size, position, or saved layout.

## Phase 5 — host-neutral presentation prototype

- Extract a normalized, read-only sensor snapshot and bounded presentation
  profile contract from the proven native/web semantics.
- Build an Avalonia prototype in parallel for tree organization, graph summary,
  and one gadget/profile—not hardware collection or control.
- Compare keyboard/UIA, DPI, multi-monitor, packaging, startup, lifetime,
  performance, and feature parity before any cutover proposal.
- WinForms and the existing sanctioned task remain the hardware/process owner
  until a separate migration spec is accepted and every gate passes.

Exit gate:

- [ ] The shared snapshot/profile contracts are versioned, bounded, and proven
  against native and web fixture data without coupling their storage keys.
- [ ] Prototype parity measurements cover keyboard/UIA, DPI, multi-monitor,
  startup, packaging, lifecycle, performance, and the selected feature slice.
- [ ] The prototype performs no hardware writes, opens no independent hardware
  owner, and does not replace WinForms process/task ownership.
- [ ] Any cutover remains blocked behind a separate accepted migration spec.

## Related roadmap boundaries

- `docs/feature-sensor-workspace.md` owns browser Workspace reflow, panel
  density, membership editing, and richer honest small multiples.
- `docs/feature-independent-text-scaling.md` owns the shipped independent UI,
  tracker, and graph-axis text scale contract.
- `docs/feature-memory-ui-reliability.md` owns lifecycle, handle, scrollbar,
  settings durability, and current accessibility baselines.

## Verification plan

Every implementation phase must add red-capable tests at the owning seam and
run the strongest applicable subset before the full gate.

Required automated coverage:

- pure presentation-order, search, Favorites, migration, malformed-state,
  bounds, and hot-plug reconciliation tests;
- unchanged `DataJsonGoldenTests` output and graph color/membership regression
  checks;
- gadget profile/layout/formatter tests plus repeated GDI/USER handle and
  allocation checks;
- graph option/state tests that preserve history and decimation behavior;
- UI Automation/name/role/keyboard checks for reorder controls, graph series,
  and the gadget editor.

Required attended matrix:

- Light, Dark, Black, and Windows high contrast;
- 75%, 100%, 150%, 200%, and 250% text scaling, with independent UI/graph text
  settings;
- mixed-DPI monitor moves and runtime DPI changes on both target frameworks,
  with current system-aware limitations recorded rather than hidden;
- keyboard-only, screen-reader/UI Automation, mouse, and touchpad interaction;
- sensor arrival/removal, hidden/missing/stale values, empty states, reset, and
  malformed settings;
- gadget resize, monitor disconnect/reconnect, minimize/restore, startup, and
  clean exit;
- source build versus live runtime promotion recorded separately.

Required final commands:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

The repository multi-architecture/packaging workflow must also pass for x64,
x86, and ARM64 before a phase is merged for promotion.

Any change near `HttpServer.BuildDataJsonObject`, `GenerateJsonForNode`, or the
canonical node model must also review the golden payload byte-for-byte. Web
Workspace phases continue to run the Node checks listed in `docs/README.md`.
