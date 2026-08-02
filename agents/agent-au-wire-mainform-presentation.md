# Agent Task — Wire MainForm Through the Presentation Boundary

**Plan:** `plan-012`

**Baseline:** integrated Agents AS and AT

**Depends on:** Agents AS and AT

**Exclusive output:**

- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`

## Goal

Compose the tested presentation coordinator and concrete adapters in `MainForm`, replacing direct operational coupling to `PlotPanel`, `SystemTray`, and `SensorGadget` while preserving exact construction, UI-thread timing, tree interaction/layout, graph parenting/menu policy, lifecycle, settings, shutdown, and user-visible behavior.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-012-winforms-presentation-surface-adapters.md`
3. integrated Agent AS and AT source plus AS's twelve tests
4. `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`
5. `LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs`
6. `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`
7. `LibreHardwareMonitor.Windows.Forms/UI/SettingsPersistenceCoordinator.cs`
8. `LibreHardwareMonitor.Windows.Forms/UI/PlotPanel.cs`
9. `LibreHardwareMonitor.Windows.Forms/UI/SystemTray.cs`
10. `LibreHardwareMonitor.Windows.Forms/UI/SensorGadget.cs`
11. protected Plan-008 snapshot and Plan-009 HTTP files named in the plan
12. focused coordinator, lifetime, plot, settings, lifecycle, and contract test families

Before editing, run AS's `12/12`, the full Application and Contracts suites, and both Release builds. Stop if the integrated baseline is not green.

## Task

### Part 1 — Compose one presentation coordinator

Replace the `_plotPanel`, `_systemTray`, and `_gadget` fields with one readonly `PresentationSurfaceCoordinator` field. Keep `MainForm` as the only composition root:

- construct the existing `PlotPanel`, `SystemTray`, and Windows-only `SensorGadget` with the exact current arguments and in the same initialization phase;
- create the tree, plot, tray, and available/unavailable gadget adapters around those exact instances;
- create one coordinator from those adapters;
- attach `HideShowClick` and `ExitClick` through the coordinator while preserving original sender/event arguments;
- keep `treeView.Model = treeModel`, all `Node` ownership, and all hardware/lifecycle construction in MainForm.

Local concrete variables are permitted only during composition/configuration. Do not retain duplicate concrete fields after the coordinator is assigned.

### Part 2 — Route only the characterized surface operations

Delegate existing operations through the coordinator without changing conditions or order:

- tree invalidation/redraw calls;
- UI-thread post-poll refresh, exactly tree → tray → available gadget → conditional plot;
- plot reset callback, control parenting between split panel and plot window, settings projection, selected sensor sequence/color dictionary/stroke, stroke updates, axis/tracker text scaling, and invalidation;
- tray main-icon state, redraw, membership, exact single-item `balloonTip` versus bulk `false`, hide/show and exit commands;
- gadget availability, visibility, redraw, membership, and hide/show command;
- notification-surface teardown in the exact existing gadget-before-tray position.

Preserve the existing `showPlot` and `showGadget` option behavior, Unix menu visibility, plot-location orientation, plot control object identity, context-menu text/order, mixed-selection idempotence, and every membership settings side effect owned by the wrapped surfaces.

### Part 3 — Deliberately retain direct MainForm/control policy

Do not route or move any of the following:

- `TreeModel`/`Node` construction, canonical node order, hardware add/remove handling, or data.json root ownership;
- tree columns, layout, scaling, auto-fit, selection, keyboard/mouse behavior, drag selection, hit testing, context-menu placement, expand/collapse state, themed scroll indicators, or UI Automation bridge;
- plot show/hide/location menu decisions or WinForms parent-control selection;
- timers, background polling, UI-thread dispatch, lifecycle barriers, server stop, final settings save, error dialogs, or settings keys/values;
- concrete surface internals, hardware enumeration/subscriptions, native icon/gadget resources, or persistence.

The coordinator adds no new asynchronous or disposal phase. `CloseApplicationCoreAsync` must still stop timers/tray icon, quit the server, drain lifecycle work, perform the final save, dispose lifecycle/UI resources, clear nodes, tear down gadget then tray through the coordinator, and close the computer in the same order.

## Exit Criteria

- Exactly `MainForm.cs` changes.
- MainForm owns one presentation coordinator and no longer retains the three concrete presentation fields after construction.
- Every characterized operation goes through the coordinator with exact conditions, order, object identity, event behavior, and arguments; direct tree interaction/layout and all application policy remain in MainForm.
- Coordinator facts pass `12/12`; Application is `179/178/1`; Contracts remain `73/73`; deterministic aggregate is `320/319/1`; both WinForms Release targets are `0W/0E`.
- Agent AS/AT files, existing surface classes, snapshot/HTTP/lifecycle/settings/hardware files, tests, projects/packages, gates, docs, operations, candidates, and live runtime remain unchanged.

## Constraints

- Modify exactly `MainForm.cs`.
- Preserve exact menu text/order, setting keys/values/defaults, event timing, UI thread, redraw gate/order, tray balloon argument, plot control instance, shutdown order, and exception behavior.
- Add no alternate UI, public API, dependency, project edit, timer, task, thread, lock, snapshot conversion, schema, or behavior change.
- If AS/AT cannot express the required exact wiring, report the contract defect instead of editing their files.
- Return proposed tracker-row text; do not edit `live-tracker.md`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~PresentationSurfaceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~WinFormsUiLifetimeTests|FullyQualifiedName~PlotPanelHistoryTests|FullyQualifiedName~PlotPanelTextScaleTests|FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests|FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

Expected: coordinator `12/12`; Application `179/178/1`; Contracts `73/73`; aggregate `320/319/1`; both WinForms targets `0W/0E`.

## Do NOT

- Do not edit outside `MainForm.cs` or alter AS/AT/source tests to make wiring easier.
- Do not abstract the whole Aga tree, rewrite graph/tray/gadget internals, migrate settings, change UI behavior, or take hardware/process ownership away from MainForm.
- Do not edit campaign truth, merge, deploy, promote, push, or clean another checkout.

## Post-completion

Commit with `refactor(ui): wire MainForm presentation surfaces`. Return the commit SHA, focused/full/build results, exact file list, explicit refresh/order/identity/membership/shutdown proof, any concern, and one concise proposed tracker row.
