# Campaign — Immutable sensor snapshot and data.json projection

**Plan ID:** plan-008
**Date:** 2026-08-01
**Status:** executed
**Plan file:** data/plans/plan-008.json
**Plan doc:** docs/campaign-plan-008-immutable-sensor-snapshot-and.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Extract the first Phase-4 seam by copying the existing WinForms Node hierarchy into a detached immutable sensor snapshot and projecting that snapshot through a pure data.json adapter, so downstream consumers no longer depend on a live mutable tree during serialization while every established payload and runtime behavior remains unchanged.

## 2. Exit Criteria

- Outside campaign/configuration documentation, the integrated diff is limited to six exclusively owned files: two new internal snapshot/capture sources, one new Application test file, one new pure projection source, HttpServer.cs, and one new Contracts test file; no MainForm, library, project, package, solution, golden, spike, web, operations, release, or live-runtime file changes.
- AG adds ordinary sealed net472-compatible snapshot/value/node types whose children are defensively copied and read-only and whose retained values contain no Node, HardwareNode, SensorNode, IHardware, ISensor, mutable collection, or Avalonia type reference.
- AG captures the tree recursively under Node.SyncRoot in existing producer order, includes hidden nodes, preserves opaque case-sensitive identifiers, exact formatted/raw triples, kinds and legacy image URLs, releases the lock before returning, documents that capture is detached rather than an atomic all-sensor sample, and adds exactly five passing Application facts.
- AH adds a pure projector that assigns envelope id zero and consecutive depth-first preorder node ids, recreates the exact existing root and node property insertion order and optional-field presence, preserves display strings and finite boxed float values, maps non-finite raw readings to null, is deterministic without mutating the snapshot, and adds exactly three passing Contracts facts.
- HttpServer.BuildDataJsonObject and WriteDataJson retain their signatures; only the data.json tree-walk is replaced by capture then projection, while Sensor and Prometheus traversal, listener dispatch, headers, buffer gate, pooled copy, gzip, cancellation, response closure, and MainForm construction remain unchanged and serialization/I/O occur outside Node.SyncRoot.
- DataJsonGoldenTests.cs and data.golden.json remain byte-for-byte unchanged; both existing golden facts pass and the golden file retains Git blob 05113704acc6fefeb4128004b3f523d876fbcec4 and SHA-256 BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3.
- The deterministic solution discovers exactly 287 cases with 286 passed and exactly the existing SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets opt-in skip; all eight new facts pass and no baseline fact is lost, duplicated, or newly skipped.
- The focused snapshot/projection/data.json tests, complete deterministic solution, both WinForms x64 Release framework builds, 75-test isolated Avalonia spike gate, web tests, and all eight included non-deploying CI gates pass with no new warnings or errors.
- Exclusive worktree ownership is preserved, implementation commits are integrated dependency-first with explicit file and patch checks, protected/unrelated dirty state is neither merged nor removed, and final campaign worktrees/branches and reproducible build outputs are guard-cleaned only after integration proof.
- Read-only SND-HOST proof confirms the live deployment remains separate and healthy; campaign truth records criterion-specific implemented evidence without automatic acceptance, Phase 4 item 1 is complete and Phase 4 is in progress, the backlog advances to Plan-009 and agent aj, stale data.json/roadmap guidance is corrected, and A1 remains open and person-only.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs | new | create | medium |
| LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs | new | create | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SensorSnapshotTests.cs | new | create | low |
| LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs | new | create | high |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs |  | modify | high |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonProjectionTests.cs | new | create | low |
| .codex/skills/project.toml |  | modify | high |
| AGENTS.md |  | modify | high |
| data/plans/plan-008.json |  | modify | high |
| docs/campaign-plan-008-immutable-sensor-snapshot-and.md |  | modify | high |
| docs/README.md |  | modify | high |
| docs/architecture/refactor-roadmap.md |  | modify | high |
| docs/campaign-backlog.md |  | modify | high |
| docs/campaign-history.md |  | modify | high |
| live-tracker.md |  | modify | high |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| ag | immutable-snapshot-capture | Add internal net472-compatible immutable sensor snapshot contracts and a WinForms Node-tree capture adapter that preserves producer order, hidden nodes, opaque identities, exact display/raw values, and legacy image URLs without retaining mutable source references; add exactly five focused Application facts. |  | LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs, LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SensorSnapshotTests.cs | 0 | high |
| ah | data-json-projection | Add a pure ordered data.json projector, rewire only HttpServer.BuildDataJsonObject to capture then project outside Node.SyncRoot, remove the superseded recursive data.json walk and icon helpers, preserve all other HTTP paths, and add exactly three focused Contracts facts while keeping the golden source and bytes unchanged. | ag | LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs, LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonProjectionTests.cs | 1 | high |
| ai | verify-document-close | Independently verify the integrated Plan-008 source and exact external contract, both framework targets, spike isolation, full non-deploying CI, repository ownership, and read-only live separation; then update only the authoritative campaign/configuration/documentation surfaces, mark Phase 4 in progress, advance the backlog to Plan-009, and record implemented rather than accepted evidence. | ag, ah | .codex/skills/project.toml, AGENTS.md, data/plans/plan-008.json, docs/campaign-plan-008-immutable-sensor-snapshot-and.md, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: ag
Group 1: ah
Group 2: ai
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs | ag |
| LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs | ag |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SensorSnapshotTests.cs | ag |
| LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs | ah |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs | ah |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonProjectionTests.cs | ah |
| .codex/skills/project.toml | ai |
| AGENTS.md | ai |
| data/plans/plan-008.json | ai |
| docs/campaign-plan-008-immutable-sensor-snapshot-and.md | ai |
| docs/README.md | ai |
| docs/architecture/refactor-roadmap.md | ai |
| docs/campaign-backlog.md | ai |
| docs/campaign-history.md | ai |
| live-tracker.md | ai |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| Sensor snapshot capture, data.json projection, HttpServer.BuildDataJsonObject, and the protected golden master | Yes | AG exclusively owns snapshot and capture, AH depends on AG and exclusively owns projection plus HttpServer, and the existing golden test source and blob are protected read-only evidence. |
| MainForm composition and hardware lifetime | No | Both files are prohibited; HttpServer retains its constructor and mutable root for the later HTTP and hardware routes. |
| Shipping project, package, solution, and Avalonia spike graph | No | SDK source globbing includes the new internal files; project, package, solution, and spike inputs remain byte-identical. |
| ['LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs', 'LibreHardwareMonitorLib/Hardware/Computer.cs'] |  | hardware lifetime and ordered option/reset ownership |
| ['LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs', 'LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/data.golden.json', 'LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs'] |  | external data.json and HTTP mutation contract |
| ['LibreHardwareMonitor.Windows.Forms/UI/StartupManager.cs', 'LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs', 'LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs'] |  | settings, runtime paths, and startup ownership |
| ['Directory.Packages.props', 'LibreHardwareMonitor.sln', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx'] |  | central package and project graph |
| ['data/plans/', 'docs/README.md', 'live-tracker.md'] |  | current status and campaign truth |
| ['.codex/skills/project.toml', 'eng/ci/Invoke-LhmGates.ps1'] |  | gate definition drift between the runner and the configuration |
| ['docs/architecture/campaign-control-plane.md', 'scripts/task_runtime/test_campaign_history.py'] |  | campaign-runtime provenance record and its overlay files |
| ['experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs'] |  | xaml-code-behind pair |
| ['experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs'] |  | xaml-code-behind pair |
| ['experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj'] |  | desktop app startup surface |
| ['experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs'] |  | desktop shell surface |
| ['LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj', 'LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest'] |  | desktop process manifest |
| LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest | LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj | Keep one owner for this desktop process manifest surface. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml | experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj (startup) | Keep one owner for this desktop shell surface. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml | experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj (startup) | Keep one owner for this desktop startup surface. |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs, LibreHardwareMonitorLib/Hardware/Computer.cs | conflict-zone | Keep one owner for this hardware lifetime and ordered option/reset ownership. |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/data.golden.json, LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs | conflict-zone | Keep one owner for this external data.json and HTTP mutation contract. |
| LibreHardwareMonitor.Windows.Forms/UI/StartupManager.cs, LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs, LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs | conflict-zone | Keep one owner for this settings, runtime paths, and startup ownership. |
| Directory.Packages.props, LibreHardwareMonitor.sln, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx | conflict-zone | Keep one owner for this central package and project graph. |
| data/plans/, docs/README.md, live-tracker.md | conflict-zone | Keep one owner for this current status and campaign truth. |
| .codex/skills/project.toml, eng/ci/Invoke-LhmGates.ps1 | conflict-zone | Keep one owner for this gate definition drift between the runner and the configuration. |
| docs/architecture/campaign-control-plane.md, scripts/task_runtime/test_campaign_history.py | conflict-zone | Keep one owner for this campaign-runtime provenance record and its overlay files. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs | conflict-zone | Keep one owner for this xaml-code-behind pair. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs | conflict-zone | Keep one owner for this xaml-code-behind pair. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj | conflict-zone | Keep one owner for this desktop app startup surface. |
| experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml, experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs | conflict-zone | Keep one owner for this desktop shell surface. |
| LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj, LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest | conflict-zone | Keep one owner for this desktop process manifest. |

## 8. Integration Points

- AG freezes the internal SensorSnapshot, SensorNodeSnapshot, SensorValueSnapshot, node-kind, and WinFormsNodeSensorSnapshotSource capture API; AH consumes that exact committed API.
- AH converts the detached snapshot into ordered Dictionary/List payloads and rewires HttpServer.BuildDataJsonObject without changing its signature; the existing golden tests are the integration oracle.
- AI starts only after AG and AH are integrated, independently reproduces the complete gate, then writes the single-owner campaign/configuration/documentation truth.
- The coordinator integrates AG before AH, validates exact owned-file sets and patch equivalence, then launches AI from that integrated commit; no task_manager merge is used for inline state.
- The analyzer's unrelated unassigned inventory is explicitly outside Plan-008; only the fifteen mapped implementation and closure paths are authorized.

## 9. Schema Changes

- No database, settings, HTTP route, wire-schema, project-graph, package, or persisted-file schema change is authorized; data.json is an exact compatibility-preserving projection of the new internal snapshot.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| The extracted projector changes data.json property order, optional fields, numeric IDs, formatting, image URLs, or float serialization. | medium | high | Keep Dictionary insertion explicit, retain float?, run focused projection assertions, and require the unchanged golden blob and SHA-256. |
| The snapshot leaks mutable Node, hardware, sensor, array, or list references and changes after capture. | medium | high | Use sealed plain classes, private defensive copies and ReadOnlyCollection exposure, plus mutation-after-capture and cast-based immutability tests. |
| Capture is described or implemented as an atomic all-sensor sample although existing formatted and raw reads can interleave with hardware updates. | medium | high | Preserve the existing read order and structural lock semantics, explicitly document detached-after-capture behavior, and make no stronger sampling guarantee. |
| Node.SyncRoot is held during projection, JSON serialization, compression, or network I/O. | low | high | The capture adapter owns the only lock; HttpServer invokes the pure projector only after Capture returns, and tests/review inspect the call boundary. |
| Moving icon or non-finite normalization logic breaks the separate Sensor API or hidden-node behavior. | medium | high | Keep HttpServer.SanitizeFloat for Sensor API, pin legacy mappings and hidden inclusion in AG/AH facts, and run all HttpServer contract and web tests. |
| A premature shared/public contract expands LibreHardwareMonitorLib or links the net10-only Avalonia prototype into shipping. | low | high | Keep ordinary internal types in the existing WinForms assembly; prohibit library, project, package, solution, and spike changes. |
| Parallel or cleanup operations import or remove unrelated repository, ignored campaign-state, or live runtime data. | low | high | Use isolated exact-path worktrees, exclusive ownership, dependency-first cherry-picks, guarded cleanup previews, and preserve tasks.json plus analysis-cache.json. |

## 11. Verification Strategy

- Restore the deterministic solution filter, then run the AG SensorSnapshotTests filter and complete Application project with both LHM_LIVE_CONFIG variables unset.
- Run the AH DataJsonProjectionTests filter, existing DataJsonGoldenTests filter, and complete Contracts project; compare data.golden.json with Git and verify blob 05113704acc6fefeb4128004b3f523d876fbcec4 plus SHA-256 BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3.
- Run dotnet test LibreHardwareMonitor.Tests\\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger console;verbosity=minimal --tl:off and prove exactly 287 discovered, 286 passed, and the one established opt-in skip.
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64; require zero errors and no new warnings.
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64; require zero errors and no new warnings.
- Run powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\\avalonia-fixture-explorer\\Test-AvaloniaSpike.ps1 and prove 75/75 while shipping project/solution references contain no Avalonia or spike dependency.
- Run node webtests\\selftest.node.js and node --test webtests\\console.tests.js webtests\\workspace.tests.js, including dashboard consumption of the preserved data.json contract.
- Run powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\\ci\\Invoke-LhmGates.ps1 -All and require all eight included gates to pass, with ci-gates excluded only as the documented self-reference.
- Run plan validate, plan preflight, task-manager analyze, git diff --check, tracked/ignored inventory, ownership checks, and exact commit/file comparisons for every integrated lane.
- Perform the standard read-only SND-HOST five-point separation sample: verified identity, exact live process, task action/working directory, proxy-bypassed HTTP 200 for / /data.json /metrics, and current-day CSV growth.

## 12. Documentation Updates

- data/plans/plan-008.json and its rendered docs/campaign-plan-008-immutable-sensor-snapshot-and.md: retain the full executable plan, exact counts, per-criterion evidence, and rollback boundary.
- AGENTS.md and .codex/skills/project.toml: replace the removed GenerateJsonForNode guidance and widen the data.json conflict zone to cover the new snapshot/capture/projection seam without changing gate commands.
- docs/README.md: replace the stale Phase-1/Plan-003 roadmap position with the current Phase-4/Plan-009 position while keeping A1 and live boundaries explicit.
- docs/architecture/refactor-roadmap.md and docs/campaign-backlog.md: mark Phase-4 item 1 complete, Phase 4 in progress, remove Plan-008 from the active table, and advance to Plan-009 and agent aj.
- docs/campaign-history.md and live-tracker.md: add criterion-specific Plan-008 implemented evidence and one row per AG, AH, and AI lane; never auto-accept the campaign.


## R1. Roadmap Phase

Phase: Phase 4 - Application and adapter seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- The external data.json payload retains its exact property names, insertion order, preorder numeric IDs, stable SensorId and HardwareId values, formatting, escaping, image URLs, and golden-master bytes.
- The snapshot is detached and immutable after capture while preserving the current Node tree order and hidden-node inclusion; it does not claim atomic raw/display sampling beyond current behavior.
- MainForm remains the composition root and WinForms remains the sole hardware, HTTP, settings, task, and release owner; no new host, project, package, listener, polling, or hardware ownership is introduced.
- Node.SyncRoot is held only while copying the mutable tree; projection, serialization, compression, and network I/O occur after the lock is released.
- Both WinForms x64 Release targets, the deterministic test population, the Avalonia spike isolation gate, and all non-deploying CI gates remain green.
- The live SND-HOST deployment, scheduled task, configuration, logs, candidate and rollback stores, and operations tree remain untouched.

## R3. Rollback Strategy

Revert the Plan-008 source, test, configuration, and documentation commits and remove only the campaign-owned Git worktrees and branches; no runtime rollback is required because the campaign does not create a candidate, deploy, promote, or mutate live state.
