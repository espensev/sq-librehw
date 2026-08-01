# Campaign — HTTP listener dispatch service extraction

**Plan ID:** plan-009
**Date:** 2026-08-01
**Status:** executed
**Plan file:** data/plans/plan-009.json
**Plan doc:** docs/campaign-plan-009-http-listener-dispatch-service.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Characterize the complete existing HTTP wire contract, then extract System.Net.HttpListener lifecycle, accept-loop, bounded-handler, cancellation, fault, and response-finalization mechanics into one internal sealed adapter while HttpServer remains the stable public facade and sole owner of authentication policy, routes, mutations, payload generation, resources, and serialization.

## 2. Exit Criteria

- AJ creates only HttpServerWireContractTests.cs with exactly six Fact methods and no theories; they cover route/status/header behavior, data.json identity and gzip, mutation method/origin policy, Basic authentication, fault-to-500 response closure, and start/idempotent-start/stop/idempotent-stop/restart, and all six pass against the unextracted planning baseline through temporary loopback listeners that are always stopped.
- Outside campaign/configuration documentation, the integrated diff is exactly three authorized paths: new HttpServerWireContractTests.cs, new Utilities/HttpListenerDispatchService.cs, and HttpServer.cs; MainForm, AuthForm, InterfacePortForm, hardware, snapshot/projector, golden, web-resource, project, package, solution, operations, release, and live-runtime files remain unchanged.
- AK adds one internal sealed net472-compatible HttpListenerDispatchService with the planned dispatch callback, maximum-concurrency default 16, PlatformNotSupported, Start with effective listener-IP output, StopAsync, and Abort surface; it retains no Node, hardware, sensor, settings, UI, or mutable application-model reference.
- Listener construction and IgnoreWriteExceptions, prefix/realm/auth-scheme configuration, lifecycle/session state, NIC fallback propagation, accept loop, bounded request pool, cancellation abort, handler fault-to-500 boundary, final response close, wait and drain logic, and final abort mechanics move into the adapter without semantic rewriting.
- HttpServer remains the public facade with compatible constructor, settings properties, SetPassword, PlatformNotSupported, start/stop/quit methods, endpoint helpers, and MainForm wiring; dynamic credential checks, full route switch, Sensor/reset origin policy, resources, data.json, Prometheus, buffering/gzip, and response writers stay in HttpServer.
- Transport behavior remains exact: concurrency 16; error 50 five-second retry; error 995 stop; five-second listener and handler drains; timed-out session restart refusal; effective invalid-IP fallback persistence; success, cancellation, and fault all terminate the response; every established route, status, header, content type, gzip, authentication, and mutation result passes the wire characterization.
- The six wire facts plus the six existing lifetime/authentication facts pass 12/12; the complete Contracts suite is exactly 73/73 and the complete deterministic solution is exactly 293 discovered, 292 passed, and the one established SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets opt-in skip, with no lost, duplicated, or new skipped baseline fact.
- DataJsonGoldenTests.cs and data.golden.json remain byte-for-byte unchanged; both golden facts pass, the golden file retains Git blob 05113704acc6fefeb4128004b3f523d876fbcec4 and SHA-256 BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3, and the broader HTTP/data/web Contracts regression is green.
- Both WinForms x64 Release targets build with zero warnings and errors, the isolated Avalonia spike remains 75/75 with only established AVLN3001 output, web remains 315/315 plus 18/18, and all eight included non-deploying CI gates pass without changing a gate.
- Exclusive worktree ownership, dependency-first integration, explicit commit/file/patch checks, plan validation/preflight/analyzer, Git hygiene, and guarded output cleanup pass; only proven campaign worktrees/branches and reproducible outputs are removed, while ignored ledgers and unrelated state are preserved.
- Read-only verified SND-HOST evidence confirms the separate live executable, process, task action/working directory, HTTP 200 for root, data.json, and metrics, and current-day CSV growth without restart, reconfiguration, publication, promotion, settings write, or operational-tree mutation.
- Campaign truth records criterion-specific implemented evidence while plan status remains executed rather than accepted; Phase 2 remains partial, Phase 4 item 2 is complete and Phase 4 remains in progress, the backlog advances to Plan-010 and agent am, A1 remains open and person-only, the stale GenerateJsonForNode documentation is corrected, and no candidate, deployment, promotion, or live cutover is claimed.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerWireContractTests.cs | new | create | medium |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs | new | create | high |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs |  | modify | high |
| .codex/skills/project.toml |  | modify | high |
| data/plans/plan-009.json |  | modify | high |
| docs/campaign-plan-009-http-listener-dispatch-service.md |  | modify | high |
| docs/README.md |  | modify | high |
| docs/architecture/refactor-roadmap.md |  | modify | high |
| docs/campaign-backlog.md |  | modify | high |
| docs/campaign-history.md |  | modify | high |
| docs/features/feature-native-ui-modernization.md |  | modify | high |
| live-tracker.md |  | modify | high |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| aj | http-wire-characterization | Add exactly six deterministic pre-extraction wire-contract facts that exercise the existing public HttpServer through a local ephemeral loopback listener, cover the complete route/status/header matrix, data.json identity and gzip, mutation method/origin rules, Basic authentication, fault-to-500 closure, and start/stop/restart lifecycle, and change no production file. |  | LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerWireContractTests.cs | 0 | high |
| ak | extract-http-listener-adapter | After AJ passes against the baseline, add an internal net472-compatible HttpListenerDispatchService adapter and rewire HttpServer as its unchanged public facade; move only listener construction/configuration, lifecycle/session state, accept scheduling, bounded handlers, cancellation/abort, fault containment, drain, and final close while leaving routes, authentication credentials, mutations, payloads, resources, serialization, and UI callers untouched. | aj | LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs, LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs | 1 | high |
| al | verify-document-close | Independently verify the integrated characterization and adapter extraction, exact HTTP/golden contracts, both WinForms targets, spike isolation, web and all non-deploying CI gates, ownership and read-only live separation; then update only authoritative campaign/configuration/documentation truth, record implemented rather than accepted, keep Phase 2 partial and A1 person-only, and advance Phase 4 to Plan-010. | aj, ak | .codex/skills/project.toml, data/plans/plan-009.json, docs/campaign-plan-009-http-listener-dispatch-service.md, docs/README.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, docs/features/feature-native-ui-modernization.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: aj
Group 1: ak
Group 2: al
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerWireContractTests.cs | aj |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs | ak |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs | ak |
| .codex/skills/project.toml | al |
| data/plans/plan-009.json | al |
| docs/campaign-plan-009-http-listener-dispatch-service.md | al |
| docs/README.md | al |
| docs/architecture/refactor-roadmap.md | al |
| docs/campaign-backlog.md | al |
| docs/campaign-history.md | al |
| docs/features/feature-native-ui-modernization.md | al |
| live-tracker.md | al |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| HttpServer listener facade and HTTP adapter extraction | listener lifecycle, accepted-context dispatch envelope, public facade wiring | AK is the sole owner; move mechanics without moving route or payload policy and integrate only after AJ passes against the baseline. |
| HTTP wire characterization and protected route contract | every established route, status, header, authentication, gzip, mutation, and closure result | AJ owns only the new test and proves it on the original baseline; AK cannot edit the characterization file. |
| campaign truth and smart-test ownership | one authoritative campaign closure and new adapter test mapping | AL is the only writer and edits after AJ and AK are integrated and independently verified. |
| ['LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs', 'LibreHardwareMonitorLib/Hardware/Computer.cs'] |  | hardware lifetime and ordered option/reset ownership |
| ['LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs', 'LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/data.golden.json', 'LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs', 'LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs', 'LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs', 'LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs'] |  | external data.json snapshot, projection, and HTTP mutation contract |
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
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/data.golden.json, LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs, LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs, LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs, LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs | conflict-zone | Keep one owner for this external data.json snapshot, projection, and HTTP mutation contract. |
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

- Commit AJ's single test file first and prove all six facts on the a45790c planning baseline before any production extraction.
- Integrate AK only after AJ, then verify its commit touches exactly HttpListenerDispatchService.cs and HttpServer.cs and rerun the unchanged wire facts.
- Keep DispatchRequestAsync and every endpoint/content producer in HttpServer; the adapter receives only Func<HttpListenerContext, CancellationToken, Task> and owns the terminal response envelope.
- Propagate Start's effective listener IP even when listener startup later fails so the current invalid-address fallback and settings persistence behavior do not drift.
- Run AL from the integrated source baseline as the sole documentation/configuration writer and render campaign markdown from JSON, never by hand.
- Integrate dependency-first with explicit file lists and patch checks; no candidate, deployment, promotion, acceptance, or live mutation is an integration step.
- Keep startup project ownership centralized during integration: experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj.
- Route changes for LibreHardwareMonitor.Windows.Forms/Resources/app.net472.manifest through one owner because it is a desktop process manifest surface.
- Route changes for experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml through one owner because it is a desktop shell surface.
- Route changes for experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/App.axaml through one owner because it is a desktop startup surface.
- Assign ownership for the 142 unassigned analysis files before execution to avoid drift.

## 9. Schema Changes

- {'schema': 'No external or persisted schema change', 'compatibility': "HttpServer's public facade, settings keys, routes, payload bytes, headers, authentication, and mutation semantics remain compatible; the new adapter is internal.", 'migration': 'None. The campaign creates no data migration, configuration migration, candidate, or runtime cutover.'}

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A mechanical move changes listener start, stop, retry, drain, or restart timing. | medium | high | Characterize the wire lifecycle first, move the existing code without rewriting it, and rerun the same six facts after integration. |
| Route, authentication, mutation, gzip, or response-header policy leaks into the adapter and duplicates ownership. | low | high | Keep DispatchRequestAsync and all endpoint/content producers in HttpServer and reject any diff outside the three authorized product/test paths. |
| Temporary wire tests collide on a port or leave a listener behind after failure. | medium | medium | Reserve ephemeral loopback ports immediately before start, serialize the test collection, use bounded client timeouts, and always stop and dispose in finally/async-disposal paths. |
| The extracted source accidentally uses a net10-only API. | low | high | Build both net472 and net10.0-windows x64 Release targets and forbid project/package changes. |
| Documentation cleanup overreaches into historical or operational truth. | low | medium | AL owns the exact closure list, leaves Plan-008/AGENTS/operations/deployment records untouched, and records implemented rather than accepted. |

## 11. Verification Strategy

- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- Unset live-config test variables, restore the Contracts project, run the six new wire facts against the pre-extraction baseline, then rerun them after extraction.
- Run HttpServerWireContractTests, HttpServerLifetimeTests, and HttpServerAuthenticationTests together for 12/12, the broader HttpServer/DataJson/WebDashboardRetirement Contracts filter, and the complete Contracts project for 73/73.
- Run both unchanged data.json golden facts; verify the protected Git blob, SHA-256, and baseline-to-HEAD source/golden diff.
- Run the deterministic solution filter and require 293 discovered, 292 passed, and exactly the established one opt-in skip.
- Build WinForms x64 Release for net10.0-windows and net472 with zero warnings/errors; run the isolated Avalonia spike gate, both Node web gates, and eng/ci/Invoke-LhmGates.ps1 -All.
- Run plan validate, plan preflight, task-manager analyze, docs/control-plane consistency checks, protected-surface diffs, git diff --check, status, worktree, and branch audits.
- After verified machine identity, perform only read-only live process/task/HTTP/CSV separation checks with two CSV samples; do not restart or mutate live state.

## 12. Documentation Updates

- Update data/plans/plan-009.json with exact implementation and criterion evidence and regenerate docs/campaign-plan-009-http-listener-dispatch-service.md only through _persist_plan_artifacts.
- Update .codex/skills/project.toml only to add the new adapter and wire-contract file to the HTTP conflict zone and map the new adapter exactly to Contracts plus web tests; do not change commands, modules, or gates.
- Update docs/README.md, docs/architecture/refactor-roadmap.md, and docs/campaign-backlog.md to mark Phase-4 item 2 implemented, keep Phase 2 partial and A1 open, advance Plan-010/agent-am, and preserve the no-deploy boundary.
- Append criterion-specific implemented-not-accepted evidence to docs/campaign-history.md and single-writer AJ/AK/AL rows to live-tracker.md.
- Replace the stale GenerateJsonForNode reference in docs/features/feature-native-ui-modernization.md with the current snapshot/projector/listener boundary; leave AGENTS.md, historical plans, operational manifests, and deployment documentation unchanged.


## R1. Roadmap Phase

Phase: Phase 4 - Application and adapter seams
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- HttpServer remains the public facade and MainForm, AuthForm, and InterfacePortForm remain unchanged; all constructor, property, start, stop, quit, endpoint-helper, and settings-facing behavior remains compatible.
- Authentication credential checks, the complete route switch, Sensor and reset mutation policy, cross-origin rejection, resources, data.json, Prometheus, buffering, gzip, and response writers remain owned by HttpServer and retain their established HTTP behavior.
- The internal HttpListener adapter owns only listener construction and configuration, lifecycle/session state, the accept loop, bounded handler scheduling, cancellation/abort, fault-to-500 containment, drain timing, and final response closure.
- Listener concurrency remains 16; error 50 retries after five seconds, error 995 terminates accept, stop uses the established five-second listener and handler drains, and a timed-out canceled session prevents restart until drained.
- Every GET, POST, and unsupported-method route retains its exact matching, status, Allow, content type, cache, CORS, gzip, authentication, and mutation semantics; no route, payload, setting, port policy, or persisted format is added.
- The detached Plan-008 snapshot, projector, data.json golden source and bytes, Prometheus output, web assets, hardware ownership, and project/package graph remain unchanged.
- Both WinForms x64 Release targets, the deterministic test population, isolated Avalonia spike, web tests, and every non-deploying CI gate remain green with no new warning or skip.
- The SND-HOST live deployment, task, process, listener, settings, logs, candidate, rollback, release, and operations trees remain untouched; any live proof is read-only separation evidence, not deployment or acceptance.

## R3. Rollback Strategy

Revert the Plan-009 characterization, adapter extraction, configuration, and documentation commits and remove only campaign-owned Git worktrees and branches; no runtime, schema, persisted-state, candidate, or deployment rollback is required because the campaign does not mutate live state.
