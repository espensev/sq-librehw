# Agent Task — Bind the Existing WinForms Presentation Surfaces

**Plan:** `plan-012`

**Baseline:** integrated Agent AS

**Depends on:** Agent AS

**Exclusive output:**

- `LibreHardwareMonitor.Windows.Forms/UI/WinFormsPresentationAdapters.cs`

## Goal

Implement thin internal adapters from Agent AS's presentation ports to the already-existing `TreeViewAdv`, `PlotPanel`, `SystemTray`, and optional `SensorGadget` instances. Preserve exact object identity, arguments, events, settings behavior, threading assumptions, and native lifetime behavior without modifying the wrapped classes.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-012-winforms-presentation-surface-adapters.md`
3. Agent AS's committed coordinator/contracts and twelve tests
4. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
5. `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs`
6. `LibreHardwareMonitor.Windows.Forms/UI/SystemTray.cs`
7. `LibreHardwareMonitor.Windows.Forms/UI/SensorGadget.cs`
8. `LibreHardwareMonitor.Windows.Forms/UI/Gadget.cs`
9. `LibreHardwareMonitor.Windows.Forms/UI/GadgetWindow.cs`
10. `Aga.Controls/Tree/TreeViewAdv.cs`
11. `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/WinFormsUiLifetimeTests.cs`
12. all plot/lifetime/runtime-path tests named in the campaign plan

Before editing, run Agent AS's focused `12/12` slice, the full Application suite, and both Release builds. Stop if the integrated contract is not green.

## Task

Add one source file containing the concrete internal adapter implementations required by Agent AS. Keep each adapter one-for-one and intentionally unremarkable:

- the tree adapter wraps one `TreeViewAdv` and invalidates that same instance;
- the plot adapter wraps one `PlotPanel`, returns that exact control instance, and forwards reset callback, settings projection, sensor/color/stroke selection, stroke updates, text-scale updates, and invalidation;
- the tray adapter wraps one `SystemTray` and forwards redraw, main-icon state, membership, the exact `balloonTip` argument, hide/show and exit events, and disposal;
- the gadget adapter wraps one `SensorGadget` and forwards availability, visibility, redraw, membership, hide/show events, and disposal;
- the unavailable-gadget adapter/null object reports unavailable, returns false for containment, performs safe no-ops for visibility/redraw/membership/disposal, and emits no events.

Adapters may validate constructor dependencies once. After construction they must not copy collections, translate identifiers, reorder sensors, cache display values, suppress exceptions, marshal threads, or add lifecycle state. Event add/remove must attach and detach directly enough that the coordinator can preserve original sender and event arguments.

The file must compile under net472 and net10.0-windows using only existing dependencies. Do not add extension points or make any adapter public.

## Exit Criteria

- Exactly `WinFormsPresentationAdapters.cs` changes.
- The four concrete adapters and unavailable-gadget behavior satisfy Agent AS's internal ports using one-for-one forwarding and the existing object instances.
- Agent AS's twelve facts pass `12/12`; Application is exactly `179/178/1` and both WinForms Release targets are `0W/0E`.
- `PlotPanel.cs`, `SystemTray.cs`, `SensorGadget.cs`, `Gadget.cs`, `GadgetWindow.cs`, `TreeViewAdv`, MainForm, tests, projects/packages, gates, docs, operations, candidates, and live runtime remain unchanged.

## Constraints

- Modify exactly the exclusive output file.
- Keep every new type internal and net472-compatible.
- Preserve exact event sender/arguments, plot control identity, sensor/color collection identity, membership arguments, and disposal delegation.
- Add no tests by editing Agent AS's test file; report a contract defect if its ports cannot represent exact forwarding.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~PresentationSurfaceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~WinFormsUiLifetimeTests|FullyQualifiedName~PlotPanelHistoryTests|FullyQualifiedName~PlotPanelTextScaleTests|FullyQualifiedName~RuntimePathsTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected: coordinator `12/12`; Application `179/178/1`; both WinForms targets `0W/0E`.

## Do NOT

- Do not edit Agent AS files, `MainForm`, or any wrapped surface class.
- Do not introduce DTOs, immutable snapshots, alternate renderers, tree ordering, settings migration, UI modernization behavior, or public interfaces in this campaign.
- Do not create a candidate, deploy, promote, mutate the operational tree, merge, push, or clean another checkout.

## Post-completion

Commit with `refactor(ui): bind WinForms presentation adapters`. Return the commit SHA, exact focused/full/build results, exact file list, proof of one-for-one forwarding and absent-gadget behavior, any concern, and one concise proposed tracker row.
