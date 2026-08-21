# Agent Task — Define and Test the Presentation Surface Contract

**Plan:** `plan-012`

**Baseline:** `9d4786c15e7c800fb07ba2eeecd6a0e112b80eb0`

**Depends on:** none

**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/UI/PresentationSurfaceCoordinator.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PresentationSurfaceCoordinatorTests.cs`

## Goal

Add one internal, deterministic presentation coordination boundary for the existing WinForms tree, plot, tray, and optional gadget surfaces. The boundary owns only operation forwarding, ordered post-poll refresh, event relay, and gadget-before-tray teardown. It must not own controls, hardware, timers, UI-thread dispatch, settings, persistence, or user policy.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-012-winforms-presentation-surface-adapters.md`
3. `docs/architecture/refactor-roadmap.md`
4. `docs/features/feature-native-ui-modernization.md`
5. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
6. `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs`
7. `LibreHardwareMonitor.Windows.Forms/UI/SystemTray.cs`
8. `LibreHardwareMonitor.Windows.Forms/UI/SensorGadget.cs`
9. `LibreHardwareMonitor.Windows.Forms/UI/Gadget.cs`
10. `LibreHardwareMonitor.Windows.Forms/UI/TreeModel.cs`
11. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/WinFormsUiLifetimeTests.cs`
12. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PlotPanelHistoryTests.cs`
13. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PlotPanelTextScaleTests.cs`

Before editing, prove the baseline is clean and run the current Application suite. Do not continue from a different source commit without coordinator approval.

## Task

### Part 1 — Define narrow internal ports

In `PresentationSurfaceCoordinator.cs`, define internal net472-compatible contracts for exactly these existing surface capabilities:

- tree redraw/invalidation only;
- plot control identity, reset callback, current-settings projection, selected sensors and color map, stroke thickness, axis/tracker text scale, and redraw;
- tray redraw, main-icon enablement, sensor membership with the exact `balloonTip` argument, hide/show command, exit command, and disposal;
- optional gadget availability, visibility, redraw, sensor membership, hide/show command, and disposal.

Use the existing `ISensor`, `Color`, and WinForms control types where fidelity requires them. Never snapshot, copy, enumerate, sort, or mutate hardware. The contract is an internal adapter boundary for the legacy WinForms surfaces, not a new public UI SDK and not the future host-neutral immutable presentation model.

### Part 2 — Implement the coordinator

Add one internal sealed `PresentationSurfaceCoordinator` that composes one instance of each port and exposes MainForm-oriented operations without exposing `PlotPanel`, `SystemTray`, or `SensorGadget` concrete types.

The coordinator must:

- preserve the plot control object identity used when moving the graph between the split panel and tool window;
- relay tray/gadget hide-show and tray exit events with the original sender and event arguments;
- refresh in the exact order tree, tray, available gadget, then plot when `plotVisible` is true;
- skip only plot redraw when `plotVisible` is false;
- return `false` and perform no membership/visibility action for an unavailable gadget;
- forward sensor sequences, color dictionaries, stroke values, scale percentages, membership calls, and tray balloon flags without normalization or copying;
- dispose the available gadget before the tray, exactly once, and detach event relays;
- leave tree/plot WinForms controls to normal Form/control disposal; coordinator disposal covers only the existing notification surfaces represented by tray and gadget.

Add no timers, tasks, locks, synchronization contexts, retries, settings access, hardware subscriptions, or UI-thread checks. MainForm is the only caller responsible for invoking these operations on the UI thread.

### Part 3 — Add exactly twelve deterministic facts

Create `PresentationSurfaceCoordinatorTests.cs` with exactly twelve `[Fact]` tests using instrumented fake ports. Cover:

1. visible-plot refresh order: tree, tray, gadget, plot;
2. hidden-plot refresh skips only plot;
3. unavailable gadget is reported unavailable and is not redrawn;
4. plot control identity, reset callback, and current-settings projection forward unchanged;
5. plot sensor sequence, color dictionary instance, and stroke value forward unchanged;
6. plot stroke, axis scale, and tracker scale forward exact values;
7. tray main-icon and membership operations preserve the exact balloon argument;
8. available gadget visibility and membership operations forward unchanged;
9. unavailable gadget membership/visibility operations are safe no-ops and containment is false;
10. tray hide/show and exit events relay the original sender/arguments;
11. gadget hide/show relays the original sender/arguments;
12. disposal is idempotent, detaches relays, and disposes gadget before tray.

Tests must not construct `MainForm`, real notification icons, real gadget windows, hardware, listeners, files, or background workers. They must be stable in headless CI.

## Exit Criteria

- Exactly the two exclusive new files change and the test file contains exactly twelve `[Fact]` tests.
- All presentation contracts and the coordinator are internal, net472-compatible, and free of concrete MainForm, settings, persistence, hardware ownership, timing, and threading policy.
- The twelve facts pass `12/12`; Application is exactly `179` discovered / `178` passed / one established live-config opt-in skip.
- Both WinForms x64 Release targets build with zero warnings and errors.
- No existing product, test, project, package, gate, documentation, candidate, operations, or live-runtime file changes.

## Constraints

- Modify exactly the two exclusive output files.
- Do not modify or implement the concrete adapters; Agent AT owns that file.
- Preserve object identity and arguments; do not introduce DTO conversion, batching, sorting, caching, or defensive copies.
- Keep every new type internal and add no package, project, solution, manifest, public API, or persisted schema.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~PresentationSurfaceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected after this agent: coordinator facts `12/12`; Application `179/178/1`; both targets `0W/0E`. Stop and report a contract/count discrepancy instead of weakening the gate.

## Do NOT

- Do not wire `MainForm`, add concrete surface adapters, or edit existing WinForms surface classes/tests.
- Do not move tree layout/input, plot history, tray/gadget settings, native resource ownership, lifecycle, or persistence policy into the coordinator.
- Do not create a candidate, deploy, promote, mutate the operational tree, merge, push, or clean another checkout.

## Post-completion

Commit with `refactor(ui): add presentation surface contract`. Return the commit SHA, exact `12/12` results, full Application/build results, exact file list, proof of refresh/disposal/event semantics, any concern, and one concise proposed tracker row.
