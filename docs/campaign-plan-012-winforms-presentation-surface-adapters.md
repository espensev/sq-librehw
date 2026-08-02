# Campaign — WinForms presentation surface adapters

**Plan ID:** plan-012
**Date:** 2026-08-02
**Status:** executed
**Plan file:** data/plans/plan-012.json
**Plan doc:** docs/campaign-plan-012-winforms-presentation-surface-adapters.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Extract MainForm presentation calls behind one internal coordinator and four concrete WinForms adapters so refresh, plot projection, tray/gadget membership and visibility, and presentation teardown have explicit replaceable contracts while MainForm retains composition, UI-thread, tree interaction/layout, lifecycle, settings, hardware, and user-policy ownership.

## 2. Exit Criteria

- Outside campaign/configuration/documentation truth, the exact diff is the two new AS files, the new AT adapter file, and AU changes only to MainForm.cs; PlotPanel.cs, SystemTray.cs, SensorGadget.cs, Gadget.cs, GadgetWindow.cs, TreeModel.cs, TreeViewAdv, projects, packages, gates, golden, HTTP, lifecycle, settings, hardware, web, and operations files remain unchanged.
- AS adds one internal net472-compatible coordinator/contract file and exactly twelve deterministic Fact tests covering refresh order and plot gating; tree, plot, tray, and gadget forwarding; absent gadget behavior; membership and balloon semantics; exact sensor/color/stroke forwarding; visibility/main-icon forwarding; event forwarding; and gadget-before-tray teardown.
- AT adds only thin internal concrete adapters over the existing TreeViewAdv, PlotPanel, SystemTray, and optional SensorGadget instances; the adapters introduce no new timer, worker, thread, synchronization context, hardware enumeration, settings key, persistence, or public API and do not modify the wrapped surface classes.
- AU makes MainForm the sole composition root for the coordinator and adapters, removes direct operational calls to PlotPanel/SystemTray/SensorGadget where covered by the contract, and preserves direct tree input/layout/hit-testing/UI Automation and plot docking/control-parenting policy.
- The UI-thread post-poll refresh remains tree then tray then gadget then conditional plot; hidden plot skips only plot refresh; shutdown still disables the tray icon and later disposes gadget before tray after lifecycle drain and final settings save.
- Canonical Node/TreeModel order, multi-select and bulk actions, scrollbar geometry/hit targets, UI Automation range/value bridge, plot history/decimation/zoom/settings, tray balloon behavior and membership keys, gadget membership/settings/assets, command events, and native resources remain semantics-compatible.
- The twelve new coordinator facts pass 12/12; Application is exactly 179 discovered, 178 passed, and the one established live-config opt-in skip; Contracts remain 73/73; deterministic aggregate is exactly 320 discovered, 319 passed, and that same skip.
- Both WinForms x64 Release targets build with zero warnings/errors; Avalonia remains 75/75 with only established AVLN3001; web remains 315/315 plus 18/18; and all eight non-deploying CI gates pass without changing a gate.
- Plan-008 immutable snapshot and protected data.json golden, Plan-009 HTTP wire contract, Plan-010 lifecycle/polling/shutdown, Plan-011 persistence projection/save boundary, CSV, Prometheus, hardware identity, settings schema, runtime paths, project graph, and package graph remain unchanged.
- Exclusive worktree ownership, dependency-first native fast-forward integration, source commit/blob/patch checks, plan validation/preflight/analyzer, documentation synchronization, Git checks, generated-plan render idempotence, and guarded output cleanup pass.
- Read-only VERIFIED SND-HOST proof confirms the separate live process/task, HTTP 200 root/data.json/metrics, and growing current-day CSV without any candidate, deployment, promotion, task, configuration, log, or operational-tree mutation.
- Plan status remains executed and ledger state becomes implemented, never automatic acceptance; Phase 4 completes, Plan-013/agent aw becomes next, A1 remains person-only, and the HTML brief clearly distinguishes completed Plans 008-011, planned/then implemented Plan-012 evidence, and no deployment.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/PresentationSurfaceCoordinator.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PresentationSurfaceCoordinatorTests.cs | new | create | medium |
| LibreHardwareMonitor.Windows.Forms/UI/WinFormsPresentationAdapters.cs | new | create | high |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | 2521 | modify | high |
| .codex/skills/project.toml | 131 | modify | medium |
| data/plans/plan-012.json | new | create | medium |
| docs/campaign-plan-012-winforms-presentation-surface-adapters.md | new | create | low |
| docs/plan-012-senior-developer-brief.html | new | create | low |
| docs/README.md | 449 | modify | low |
| docs/architecture/refactor-roadmap.md | 335 | modify | medium |
| docs/campaign-backlog.md | 140 | modify | low |
| docs/campaign-history.md | 284 | modify | medium |
| docs/features/feature-native-ui-modernization.md | 449 | modify | medium |
| live-tracker.md | 140 | modify | low |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| as | presentation-surface-contract | Define and deterministically test the internal net472-compatible presentation-port contracts and coordinator for tree refresh, plot projection, tray/gadget membership and visibility, refresh fan-out, and gadget-before-tray teardown without owning controls, hardware, timers, settings, or UI-thread policy. |  | LibreHardwareMonitor.Windows.Forms/UI/PresentationSurfaceCoordinator.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PresentationSurfaceCoordinatorTests.cs | 0 | high |
| at | winforms-surface-adapters | Implement the concrete internal adapters over the existing TreeViewAdv, PlotPanel, SystemTray, and optional SensorGadget instances without changing those surface classes, their public APIs, their settings keys, or their native lifetime behavior. | as | LibreHardwareMonitor.Windows.Forms/UI/WinFormsPresentationAdapters.cs | 1 | high |
| au | wire-mainform-presentation | Wire MainForm to the tested coordinator and concrete presentation adapters while retaining construction, UI-thread timing, tree layout/input/hit-testing/UI Automation, plot docking/menu policy, lifecycle, settings, and exact shutdown order. | as, at | LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | 2 | high |
| av | verify-document-close | Independently verify the integrated presentation seam, protected external/lifecycle/persistence contracts, dual-target builds, non-deploying gates, source ownership, and read-only live separation; then update the sole campaign/documentation/tracker truth and refresh the visual senior-developer brief as implemented rather than accepted. | as, at, au | .codex/skills/project.toml, data/plans/plan-012.json, docs/campaign-plan-012-winforms-presentation-surface-adapters.md, docs/plan-012-senior-developer-brief.html, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-native-ui-modernization.md, live-tracker.md | 3 | high |

## 5. Dependency Graph

```text
Group 0: as
Group 1: at
Group 2: au
Group 3: av
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Windows.Forms/UI/PresentationSurfaceCoordinator.cs | as |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/PresentationSurfaceCoordinatorTests.cs | as |
| LibreHardwareMonitor.Windows.Forms/UI/WinFormsPresentationAdapters.cs | at |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs | au |
| .codex/skills/project.toml | av |
| data/plans/plan-012.json | av |
| docs/campaign-plan-012-winforms-presentation-surface-adapters.md | av |
| docs/plan-012-senior-developer-brief.html | av |
| docs/README.md | av |
| docs/architecture/refactor-roadmap.md | av |
| docs/campaign-backlog.md | av |
| docs/campaign-history.md | av |
| docs/features/feature-native-ui-modernization.md | av |
| live-tracker.md | av |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| presentation adapter contract and concrete binding | New coordinator, tests, and concrete adapter file only | AS exclusively owns the contract/coordinator and twelve facts; AT starts after AS and owns the concrete adapter file. Existing PlotPanel, SystemTray, SensorGadget, Gadget, GadgetWindow, and TreeModel files remain byte-identical. |
| MainForm composition, post-poll refresh, and shutdown | MainForm wiring only | AU is the sole MainForm owner and starts after AS/AT integration. It preserves the UI-thread refresh order, plot visibility gate, lifecycle drain, final settings save, tray disable, and gadget-before-tray disposal order. |
| canonical tree model, hit testing, scrollbars, and UI Automation | Adapter invalidation/model attachment boundary only; control-level interaction remains in MainForm | Do not proxy or rewrite TreeViewAdv selection, columns, layout, keyboard/mouse hit testing, themed scrollbar indicators, accessibility objects, Node/TreeModel order, or expand/collapse persistence. Re-run the WinForms lifetime/accessibility facts. |
| plot projection, history, docking, and settings | Adapter forwarding only | Forward the existing PlotPanel instance, control identity, ResetGraphView callback, selected ISensor sequence, color dictionary, stroke thickness, axis/tracker scales, current-settings projection, and conditional invalidation without copying history or creating a second plot. |
| tray and gadget membership, commands, settings, and native resources | Adapter forwarding only | Preserve exact Contains/Add/Remove and tray balloon arguments, optional gadget behavior on Unix, Visible and IsMainIconEnabled semantics, event forwarding, settings keys, redraw timing, and disposal of each existing concrete instance exactly once. |
| Plans 008-011 protected snapshot, HTTP, lifecycle, and persistence contracts | Verification only | Snapshot/projector, data.json golden, HTTP listener/facade, lifecycle coordinators, SettingsPersistenceCoordinator, PersistentSettings, RuntimePaths, Computer, CSV, and Prometheus files stay unchanged; focused and full suites must remain green. |
| project graph, gates, campaign truth, and live operations | AV owns one project.toml inventory edit and all Plan-012 documentation/tracker truth | Projects, packages, solution, manifests, CI runner, operations tree, candidates, deployments, rollback, tasks, configuration, and logs remain unchanged. AV is the sole tracker/doc writer and records implemented, not accepted or deployed. |

## 8. Integration Points

- AS commits the internal contract/coordinator and exactly twelve deterministic facts first; the interfaces describe only presentation operations and accept existing ISensor references without taking hardware ownership.
- AT starts only after AS is integrated and binds the four ports to the already-created TreeViewAdv, PlotPanel, SystemTray, and optional SensorGadget instances; it changes no wrapped surface class.
- AU starts only after AS and AT are integrated; MainForm constructs the concrete surfaces and adapters, remains the UI-thread policy/composition root, and delegates only the operations named by the contract.
- Tree layout, input, selection, context-menu placement, scrollbar overlays, UI Automation, canonical Node/TreeModel order, and plot control parenting remain direct MainForm/control responsibilities in this campaign.
- The post-poll call order is tree redraw, tray redraw, optional gadget redraw, then plot redraw only when visible; membership and command events preserve exact arguments and sender behavior.
- Shutdown retains Plan-010 and Plan-011 ordering: stop UI timers and tray icon, quit server, drain lifecycle, final-save settings, dispose lifecycle/UI resources, then presentation teardown in the existing gadget-before-tray order.
- AS, AT, and AU each return proposed tracker evidence to AV. AV starts from a clean integrated tree, independently verifies exact diffs, counts, hashes, dual targets, non-deploying gates, and read-only live separation, then writes truth once.
- Native Git worktrees and dependency-order fast-forward integration are required; no task_manager merge against stale campaign state, candidate creation, deployment, promotion, acceptance, scheduled-task, configuration, log, CSV, rollback, or operational mutation is an integration step.

## 9. Schema Changes

- {'migration': 'None. No data, configuration, presentation-order, candidate, deployment, or live-runtime migration.', 'schema': 'No persisted, external, or public schema change.', 'compatibility': 'No settings key/value, XML layout, data.json bytes, HTTP route, CSV, Prometheus, hardware identifier, Node/TreeModel order, package graph, project graph, or public API change.'}

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A broad facade becomes a second MainForm and duplicates user or lifecycle policy. | medium | high | Keep the coordinator internal and operation-oriented; MainForm retains construction, UI-thread timing, menu policy, control layout/input, hardware, settings, and lifecycle decisions. |
| Tree abstraction changes canonical order, selection, hit targets, or UI Automation behavior. | medium | high | Limit the tree adapter to the characterized presentation boundary and leave TreeViewAdv, Node/TreeModel, themed scroll indicators, hit testing, and accessibility bridge unchanged. |
| Plot forwarding copies sensor/history state or changes graph identity and docking. | medium | high | Forward the existing PlotPanel instance and exact sensor sequence/color dictionary/stroke values; preserve the same control identity across docked and separate-window parents and run plot history/text-scale facts. |
| Tray or gadget wrappers alter membership persistence, balloon behavior, command events, or native resource disposal. | medium | high | Adapters delegate one-for-one to existing methods/events, keep optional gadget semantics, add no ownership of settings or hardware events, and test forwarding plus teardown order. |
| Refresh or teardown moves off the UI thread or changes Plan-010/011 shutdown ordering. | medium | high | Coordinator adds no thread, timer, task, or marshal; AU calls it only from existing MainForm UI paths and preserves lifecycle drain and final-save placement. |
| New adapter contracts use APIs unavailable to net472 or leak a public extensibility promise. | low | high | All types remain internal, use established BCL/WinForms APIs, add no dependency/project edit, and build both x64 Release targets. |
| Protected external or persisted contracts drift even though the campaign is presentation-only. | low | high | Reject diffs in snapshot, golden, HTTP, settings, lifecycle, Computer, project/package, web, and operations surfaces; run Contracts, aggregate, web, Avalonia, and all eight non-deploying gates. |
| The visual brief is mistaken for execution authority or deployment proof. | medium | medium | Label the HTML as a non-authoritative presentation brief, link canonical plan JSON/roadmap, separate completed versus planned evidence, and state that no candidate/deployment is authorized. |

## 11. Verification Strategy

- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --filter FullyQualifiedName~PresentationSurfaceCoordinatorTests
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
- node webtests\selftest.node.js
- node --test webtests\console.tests.js webtests\workspace.tests.js
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- python scripts\task_manager.py plan validate plan-012 --json; python scripts\task_manager.py plan preflight --json; python scripts\task_manager.py analyze --json
- git diff --check; git status --short

## 12. Documentation Updates

- Regenerate docs/campaign-plan-012-winforms-presentation-surface-adapters.md only from the canonical plan JSON and prove byte-identical second render.
- Update docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-native-ui-modernization.md, and live-tracker.md once through AV after integrated verification.
- Refresh docs/plan-012-senior-developer-brief.html with final commits, counts, architecture result, risks, and explicit non-authoritative/no-deployment labels; keep it standalone and offline.


## R1. Roadmap Phase

Phase: Phase 4 - Application and adapter seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- MainForm remains the sole composition root, UI-thread policy owner, and hardware/process owner.
- Canonical Node/TreeModel order, TreeViewAdv input and layout behavior, themed scrollbar hit targets, and the UI Automation bridge remain unchanged.
- Plot history, graph settings, stroke/text scales, docking identity, and sensor/color selection semantics remain unchanged.
- Tray and gadget membership keys, balloon behavior, visibility, command events, redraw timing, native resource lifetime, and gadget-before-tray shutdown order remain unchanged.
- Plan-008 snapshot, Plan-009 data.json and HTTP, Plan-010 lifecycle, and Plan-011 persistence contracts remain byte or semantics compatible as applicable.
- No new timer, thread, worker, public API, package, schema, candidate, deployment, promotion, or live-runtime mutation is introduced.

## R3. Rollback Strategy

Revert the Plan-012 source commits in reverse dependency order (MainForm wiring, concrete adapters, coordinator contract) while preserving the pre-Plan-012 commit 9d4786c; closure truth is reverted separately. No operational rollback packet is involved because the campaign creates no candidate or deployment.
