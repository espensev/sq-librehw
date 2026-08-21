# Campaign — Non-deploying CI gates and campaign history contract

**Plan ID:** plan-002
**Date:** 2026-07-31
**Status:** executed
**Plan file:** data/plans/plan-002.json
**Plan doc:** docs/campaign-plan-002-non-deploying-ci-gates.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/refactor-roadmap.md
**Source discovery docs:** docs/discovery-librehw-structural-audit.md

---

## 1. Goal

Close the two open Phase 1 items of the structural refactor roadmap by giving the repository a named, non-deploying verification entry point and a durable campaign-history contract. Today every gate is a separate ad-hoc command, the deploying and non-deploying halves of the release tooling are only separated by convention, and per-criterion campaign acceptance evidence lives in the ignored local execution ledger. This campaign adds eng/ci/Invoke-LhmGates.ps1, which reads the named build gates from .codex/skills/project.toml, classifies every one of them as included or excluded with a recorded reason, and refuses any deploying or host-mutating command before it runs; a read-only GitHub Actions workflow that only delegates to that runner; and a tracked docs/campaign-history.md ledger plus regression coverage so a campaign can never be recorded accepted while an attended exit criterion is still open, on a ledger state axis kept separate from the plan lifecycle status. It touches no product source, no live SND-HOST runtime, and no release, rollback, log, or task surface, so the structural baseline stays behavior-identical while Phase 1 gains an exit gate that can actually be run and inspected.

## 2. Exit Criteria

- eng/ci/Invoke-LhmGates.ps1 -List prints every [build-gate.*] name defined in .codex/skills/project.toml with its included or excluded classification and reason, and exits 0.
- Every build gate name in .codex/skills/project.toml is explicitly classified by the runner; an unclassified gate name makes the runner exit non-zero with an unclassified-gate error instead of silently skipping it.
- The runner refuses, without executing, any command matching the deploying or host-mutating deny-list, and the release-candidate build and -RequireCurrentSource verify keys are excluded from the runnable set with a recorded reason while its release-system fixture test remains included.
- eng/ci/Invoke-LhmGates.ps1 -All runs the included gate set on identity-verified SND-HOST, reports a per-gate result table, and exits non-zero if any gate fails.
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 discovers and runs every eng/ci/tests/Test-*.ps1, reports an aggregate pass and fail count, and exits non-zero on any failure.
- .github/workflows/non-deploying-gates.yml parses as valid YAML, declares permissions contents read, references no repository secret and no deployment or publish action, and every gate-running step delegates to eng/ci/Invoke-LhmGates.ps1.
- docs/campaign-history.md exists as a tracked ledger with one row per plan file in data/plans/, uses the ledger states registered, implemented, accepted, and closed, and records plan-001 with its attended exit criterion still open.
- python -m unittest discover -s scripts -p test_*.py passes, including a new campaign-history regression that fails if any campaign is recorded accepted or closed while one of its exit criteria is still open or is waived without a complete waiver record, and that fails if the ledger and data/plans/ disagree on the criterion count.
- docs/architecture/campaign-control-plane.md states the campaign-history transition contract including the waiver record fields, and lists the added repository-owned overlay test with its normalization-aware Git blob ID.
- .codex/skills/project.toml declares the ci module, the ci-gates build gate, the eng/ci and .github/workflows smart-test mappings, and the gate-definition-drift conflict zone, and python scripts/task_manager.py plan preflight --json reports ready with zero errors.
- Both WinForms x64 Release targets and the .NET test suite still pass, and git diff --stat shows no change under Aga.Controls, LibreHardwareMonitorLib, LibreHardwareMonitor.Windows.Forms, LibreHardwareMonitor.Tests, or LibreHardwareMonitor.sln.
- data/plans/plan-001.json still has status partial with an empty executed_at, and no scheduled task, live runtime file, release candidate, rollback packet, or log archive changed during the campaign.
- docs/HANDOFF.md is retired only after docs/refactor-roadmap.md and docs/README.md carry its non-negotiable architecture decisions, do-not list, live-verification method, and remaining-ambiguity list.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| eng/ci/Invoke-LhmGates.ps1 | new | create | medium - owner E; new non-deploying entry point; the only place gate commands are resolved and executed |
| eng/ci/README.md | new | create | low - owner E; documents included and excluded gates and the deny-list rationale |
| eng/ci/Test-LhmCiGates.ps1 | new | create | low - owner F; single command registered as the ci-gates build gate |
| eng/ci/tests/Test-GateRunner.ps1 | new | create | medium - owner F; proves classification, deny-list, selection, and exit-code behavior |
| eng/ci/tests/Test-Workflow.ps1 | new | create | low - owner G; proves the workflow parses and stays read-only |
| .github/workflows/non-deploying-gates.yml | new | create | medium - owner G; outward-facing surface; must stay read-only and secret-free |
| docs/campaign-history.md | new | create | medium - owner H; becomes the durable per-criterion acceptance authority |
| docs/architecture/campaign-control-plane.md | 126 | modify | high - owner H; control-plane authority, provenance, and safety contract |
| scripts/task_runtime/test_campaign_history.py | new | create | medium - owner H; sixth repository-owned overlay file; provenance record must be updated in the same change |
| .codex/skills/project.toml | 107 | modify | high - owner I; sole owner of paths, modules, gates, and conflict zones; a bad edit blocks every campaign preflight |
| docs/README.md | 314 | modify | high - owner I; tier-1 docs-sync file inside the declared campaign-truth conflict zone |
| live-tracker.md | 11 | modify | high - owner I; declared campaign-truth conflict zone; single writer required |
| docs/refactor-roadmap.md | 179 | modify | medium - owner I; active phase contract and exit gates |
| docs/HANDOFF.md | 275 | delete | medium - owner I; retire only after its still-live content is carried elsewhere |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| e | ci-gate-runner | Add the non-deploying gate runner that executes named project.toml build gates and refuses every deploying or host-mutating command. |  | eng/ci/Invoke-LhmGates.ps1, eng/ci/README.md | 0 | high |
| h | campaign-history-contract | Define the durable campaign-history transition contract and ledger with regression coverage that stops open attended criteria from being closed automatically. |  | docs/campaign-history.md, docs/architecture/campaign-control-plane.md, scripts/task_runtime/test_campaign_history.py | 0 | high |
| f | ci-gate-runner-tests | Add the eng/ci test dispatcher and the gate-runner regression suite covering gate classification, the deny-list, selection, and exit codes. | e | eng/ci/Test-LhmCiGates.ps1, eng/ci/tests/Test-GateRunner.ps1 | 1 | medium |
| g | ci-workflow-delegation | Add the read-only GitHub Actions workflow that delegates to the gate runner, plus its structural and YAML-parse regression tests. | e, f | .github/workflows/non-deploying-gates.yml, eng/ci/tests/Test-Workflow.ps1 | 2 | medium |
| i | control-plane-wiring | Wire the CI module, gate, smart-test mappings, and conflict zone into project.toml, then update the tracker, roadmap, current-state docs, and retire the handoff. | e, f, g, h | .codex/skills/project.toml, docs/README.md, live-tracker.md, docs/refactor-roadmap.md, docs/HANDOFF.md | 3 | medium |

## 5. Dependency Graph

```text
Group 0: e, h
Group 1: f
Group 2: g
Group 3: i
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| eng/ci/Invoke-LhmGates.ps1 | e |
| eng/ci/README.md | e |
| docs/campaign-history.md | h |
| docs/architecture/campaign-control-plane.md | h |
| scripts/task_runtime/test_campaign_history.py | h |
| eng/ci/Test-LhmCiGates.ps1 | f |
| eng/ci/tests/Test-GateRunner.ps1 | f |
| .github/workflows/non-deploying-gates.yml | g |
| eng/ci/tests/Test-Workflow.ps1 | g |
| .codex/skills/project.toml | i |
| docs/README.md | i |
| live-tracker.md | i |
| docs/refactor-roadmap.md | i |
| docs/HANDOFF.md | i |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| docs/README.md, live-tracker.md, data/plans/ | yes | Agent I is the single owner of docs/README.md and live-tracker.md. data/plans/plan-002.json is written only by the campaign tooling, and no agent edits data/plans/plan-001.json or its campaign document. |
| eng/ci/Invoke-LhmGates.ps1, .codex/skills/project.toml | yes - new zone introduced by this campaign | Gate commands live only in project.toml. Agent E reads them at run time and hard-codes none, agent I declares the new module, gate, mappings, and this zone, and agent F asserts that every configured gate name is classified so the two files cannot drift apart silently. |
| docs/architecture/campaign-control-plane.md, scripts/task_runtime overlay files | yes - new zone introduced by this campaign | Agent H solely owns both the new overlay test and the provenance record that lists it, and updates them in one change. No canonical runtime file is touched, so the pinned scripts/task_manager.py hash and the five existing overlay blob IDs stay valid. |
| Directory.Packages.props, LibreHardwareMonitor.sln, LibreHardwareMonitor.Avalonia.Spike.slnx | no | No package version, project file, or solution is edited and no .NET project is added. The campaign adds only PowerShell, YAML, Markdown, and one Python test. |
| LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs, LibreHardwareMonitorLib/Hardware/Computer.cs | no | Hardware lifetime and ordered option/reset ownership are untouched; no product source file is in any agent's ownership set. |
| LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs, LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs, LibreHardwareMonitor.Tests/data.golden.json | no | The external data.json and HTTP mutation contract is unchanged; the golden master is only executed as an existing gate, never regenerated. |
| LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs, LibreHardwareMonitor.Windows.Forms/Utilities/RuntimePaths.cs, LibreHardwareMonitor.Windows.Forms/UI/StartupManager.cs | no | Settings, runtime paths, and startup ownership are untouched. The campaign introduces no runtime manifest and no data-root change; that remains gated to roadmap Phase 6. |

## 8. Integration Points

- Agent E freezes the runner surface - the -List, -Gate, -All, -FailFast, -JsonSummary, and -ConfigPath parameters and the JSON summary schema - and agents F and G assert against that surface without adding parameters of their own.
- Agent E defines the eng/ci/tests/Test-*.ps1 convention: each test script is self-contained, exits 0 only when every assertion passes, prints one line per assertion, and writes nothing outside a temporary directory. Agent F implements the dispatcher over that convention and agent G adds a second script under it.
- Agent F's dispatcher eng/ci/Test-LhmCiGates.ps1 is the single command that agent I registers as the [build-gate.ci-gates] test key; agent I registers no per-test command.
- Agent G's workflow references only the runner path and parameters published by agent E, and agent I adds the .github/workflows smart-test mapping that points back at agent F's dispatcher.
- Agent H creates docs/campaign-history.md and agent I links it from the docs/README.md source map and closes the matching roadmap Phase 1 item.
- Agent H's scripts/task_runtime/test_campaign_history.py is discovered by the existing campaign-control gate command with no project.toml change, so agent I adds no test command for it and only records the new overlay file where the roadmap or docs reference the overlay set.
- Every agent returns its own tracker row text in its result payload; agent I is the only writer of live-tracker.md and composes all five rows in one edit.

## 9. Schema Changes

- No schema changes required. The plan artifact stays at schema_version 1, data/tasks.json keeps its current shape, and no database or serialized state contract is introduced.
- docs/campaign-history.md is a new tracked Markdown ledger with a fixed column set, not a state schema. Its shape is enforced by scripts/task_runtime/test_campaign_history.py rather than by the plan schema.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| The runner's gate list drifts from .codex/skills/project.toml and an excluded deploying gate silently becomes runnable. | medium | high | The runner never hard-codes commands: it parses project.toml at run time, requires every discovered [build-gate.*] name to be explicitly classified, and exits non-zero on an unclassified gate. Test-GateRunner.ps1 asserts the classification table against the parsed configuration. |
| A later project.toml edit puts a deploying command inside an already-included gate. | medium | high | The deny-list is applied to the resolved command string immediately before execution, not only to gate names, so an included gate carrying a denied command is refused and reported. Fixture gate tables in the runner tests cover this case. |
| Running the full included gate set on SND-HOST disturbs live monitoring state. | low | high | Included gates are exactly the existing non-live fixtures already run by hand. The runner never elevates, never starts or stops a process, service, or scheduled task, and never writes outside the repository or a temporary directory. The campaign re-proves the live process, root task, HTTP endpoints, and CSV growth after the sweep. |
| The GitHub Actions workflow implies a push, publish, or deploy capability the repository deliberately does not have. | medium | medium | The workflow only delegates to eng/ci/Invoke-LhmGates.ps1, declares permissions contents read, references no repository secret and no deployment or publish action, and is verified by YAML parse plus structural assertions. The plan records that origin is deliberately unpushed, so the workflow is verified by inspection rather than by a hosted run. |
| Adding a sixth repository-owned overlay file invalidates the pinned campaign-runtime provenance check. | medium | medium | Agent H owns both the new overlay test and the provenance record and updates the overlay list and blob-ID table in docs/architecture/campaign-control-plane.md in the same change, recording the blob ID after the file content is final. No canonical package file is modified and the pinned scripts/task_manager.py SHA-256 stays valid. |
| The campaign-history ledger is read as permission to close plan-001's attended smoke gate. | low | high | The transition contract requires criterion-specific evidence, or a waiver record carrying owner, date, reason, and accepted risk, before a plan may move off partial. The regression test fails on any plan recorded executed with an unevaluated or open criterion, and plan-001 staying partial is an explicit non-goal of this campaign. |
| Creating eng/ci before Phase 2 begins fragments the repository layout into scripts, ops, and eng. | medium | low | eng/ci is the Phase 2 target path adopted additively, with no moves and no renames, and the decision is recorded in the roadmap. Phase 2 then moves the existing engineering entry points into an established boundary instead of relocating a freshly created one. |
| The standing analyzer warning about 89 unassigned files masks a real module-ownership gap. | low | medium | The 89 paths contain zero .NET source and 85 are already covered by declared [modules] globs; the warning is a .NET project-graph signal, not module ownership. The four remaining are LibreHardwareMonitor.sln, LibreHardwareMonitor.Avalonia.Spike.slnx, global.json - all three already declared in [smart-test.cross-cutting] - and scripts/Test-AvaloniaSpike.ps1, which agent I adds to the avalonia-tests module. |
| Multiple agents write live-tracker.md and collide in the declared campaign-truth conflict zone. | medium | medium | live-tracker.md has one owner, agent I. Every other agent returns its tracker row text in its result payload and is explicitly forbidden from editing the file, overriding the default tracker instruction in the generated spec template. |
| The agent letters e through i carry no project namespace prefix, contrary to the planning contract. | low | low | planning-contract.md mandates a namespace prefix and points at a central agent registry at D:\Development\plans\agent-registry.json for collision detection. That registry does not exist on SND-HOST, whose primary development directory is D:\DevHome, and the contract's prefix table has no librehw-host entry. Unprefixed letters continue plan-001's a-d sequence and the next_letter counter, so collision risk inside this repository is zero. The deviation is recorded here rather than by editing the shared canonical contract. |
| The full gate sweep recreates bin and obj trees, so a post-sweep ignored-state check reads like a regression against the baseline record of zero generated directories. | high | low | Agents E and I run git clean -ndX both before and after the sweep, and agent I runs the repository's guarded scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 as an explicit named step after reviewing its -WhatIf output. Build output is ignored and reproducible; only a __pycache__ directory would be a real defect, and the runner sets PYTHONDONTWRITEBYTECODE=1 in its own process so Python gate children never create one. The cleanup script stays on the runner's deny-list because it mutates, so cleanup is never something the runner does to itself. |

## 11. Verification Strategy

- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
- python scripts\task_manager.py plan preflight --json
- python -m unittest discover -s scripts -p test_*.py -v
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
- node webtests\selftest.node.js
- node --test webtests\console.tests.js webtests\workspace.tests.js
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Test-AvaloniaSpike.ps1
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\local-release\Test-LhmLocalRelease.ps1
- git diff --check and git diff --stat to prove no product-source or live-path change
- git clean -ndX to confirm data/tasks.json and data/analysis-cache.json remain the only ignored local state

## 12. Documentation Updates

- Owner I updates docs/refactor-roadmap.md Phase 1 to close the CI and campaign-history items and records the deliberate early adoption of the Phase 2 eng/ci path.
- Owner I updates docs/README.md with the eng/ci verification surface and a docs/campaign-history.md source-map entry, and folds the retired handoff content into the roadmap and current-state docs.
- Owner I writes every plan-002 tracker row in live-tracker.md; no other agent edits that file.
- Owner H documents the campaign-history transition contract, waiver record fields, and the new overlay test blob ID in docs/architecture/campaign-control-plane.md.
- Owner E adds eng/ci/README.md describing the included gates, the excluded gates with reasons, and the deny-list rationale.
- Owner I retires docs/HANDOFF.md only after its still-live content is carried by the roadmap and current-state docs.


## R1. Roadmap Phase

Phase: Phase 1 - Campaign and verification control plane
Roadmap reference: docs/refactor-roadmap.md

## R2. Behavioral Invariants

- No campaign step deploys, promotes, installs, or creates an external release candidate.
- The live SND-HOST runtime, its scheduled tasks, configuration, active CSV, release store, and rollback store stay untouched.
- No product source changes: data.json shape/order/IDs, Prometheus output, CSV behavior, settings, and hardware behavior are unchanged.
- Both net10.0-windows and net472 WinForms x64 Release builds remain gates and keep passing.
- The pinned canonical campaign runtime is not modified; the recorded scripts/task_manager.py SHA-256 stays valid and only documented repository-owned overlay files may be added.
- Plan-001 keeps status partial; this campaign defines the campaign-history transition but does not close or waive the attended smoke gate.
- Inherited product roots Aga.Controls, LibreHardwareMonitorLib, LibreHardwareMonitor.Windows.Forms, and LibreHardwareMonitor.sln are not moved or renamed.

## R3. Rollback Strategy

Every deliverable is either a new fork-only path or an additive edit to tracked configuration and documentation. No product source, live runtime, scheduled task, release store, rollback packet, or external artifact is touched, so rollback needs no unwinding step. Revert by deleting the new eng/ci and .github trees and reverting the .codex/skills/project.toml, docs, and scripts/task_runtime overlay-test additions, or by git revert of the campaign commits. Each agent owns a disjoint file set, so a single failing agent can be reverted without disturbing the others.
