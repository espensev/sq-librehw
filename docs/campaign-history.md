# Campaign History

**Status:** active
**Authority:** human acceptance evidence

This is the durable, tracked record of what a person has actually accepted.
`data/tasks.json` is local execution state, it is ignored by Git, and it can
never substitute for a row in this file. A row here records acceptance of a
campaign's own exit criteria. It is not authority to deploy, promote, or
change any live runtime.

Plan lifecycle status and ledger state answer different questions. Plan status
answers "has the tooling registered and run this campaign"; `executed` means the
agents were registered and their templates generated, not that the work is
finished. Ledger state answers "has a person accepted the result". The two move
independently and neither implies the other. The full contract, including who
may set each state and the required waiver fields, is in
`docs/architecture/campaign-control-plane.md`.

Criterion text in this file is copied verbatim from each plan's
`plan_elements.exit_criteria`. `scripts/task_runtime/test_campaign_history.py`
fails if the two ever disagree.

## Ledger

| Plan | Campaign | Plan status | Ledger state | Met | Open | Waived | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| plan-001 | Fixture-only Avalonia sensor explorer | partial | implemented | 8 | 1 | 0 | `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` |
| plan-002 | Non-deploying CI gates and campaign history contract | executed | implemented | 13 | 0 | 0 | `docs/campaign-plan-002-non-deploying-ci-gates.md` |

## plan-001 — Fixture-only Avalonia sensor explorer

Its automated execution manifest is `verified`, its tracked plan contract is
`partial`, and its ledger state is `implemented`. All three are simultaneously
correct: the automated work finished, an attended criterion is still open, and
no person has accepted the campaign.

**Open question for the owner.** Criterion 1 requires verification *before* the
implementation agents launch. AVSPIKE-001 records that the independent
dual-shell current-source check happened afterwards, not before. It is recorded
`met` with the deviation stated, matching the position the plan's own backfill
reason already took. Moving it to `open`, or adding a waiver record, is a
one-line edit here and the regression will enforce whichever is chosen.

| # | Criterion | State | Evidence |
| --- | --- | --- | --- |
| 1 | The accepted spec and plan checkpoint is committed and independently verified as a clean promotable current-source candidate before implementation agents launch. | met | Spike spec and plan checkpoint committed. Candidate `0.9.6-20260730-210528120-b466837` passed dual-shell exact-source promotable verification. Ordering deviation: `live-tracker.md` AVSPIKE-001 records that the independent dual-shell current-source verification was not recorded *before* the implementation agents launched, only afterwards. The substance was verified; the timing clause was not. |
| 2 | The separate net10.0 spike solution restores and builds with Avalonia 12.1.0 while LibreHardwareMonitor.sln, global.json, and existing project files remain unchanged. | met | The separate `net10.0` spike solution restores and builds with Avalonia 12.1.0 while `LibreHardwareMonitor.sln`, `global.json`, and the existing project files remain unchanged. AVSPIKE-001. |
| 3 | The parser enforces the 4 MiB, depth 32, 16384-node, 4096-child, 1024-character, unique-ID, single-snapshot, and single-load bounds with no partial state. | met | 49 of 49 parser and replacement cases enforce every byte, depth, node, child, string, and identity bound with atomic immutable projection. AVSPIKE-002. |
| 4 | Recorded and generated tests cover normal, unavailable, hot-plug removal, malformed, oversized, duplicate-ID, and every hard-limit case. | met | Recorded and generated fixtures cover the normal, unavailable, hot-plug removal, malformed, oversized, duplicate-ID, and hard-limit cases. Residual: a reliable Windows ACL-denied test remains unavailable, although typed catches exist, and a non-cooperative caller stream can outlive logical supersession. AVSPIKE-002. |
| 5 | The Avalonia shell renders producer order, stable IDs, honest current/min/max values, and loaded, empty, loading, and rejection states with keyboard navigation. | met | 19 of 19 UI and headless cases cover explicit states, accessible labels, keyboard focus and navigation, cancellation, and safe errors. Residual: the `AVLN3001` source-build warning remains. AVSPIKE-003. |
| 6 | No spike project references WinForms, opens hardware, requests elevation, writes current settings, exposes control requests, joins packaging, or changes live state. | met | Both WinForms package inventories contained zero Avalonia or spike entries. The spike is absent from `LibreHardwareMonitor.sln`, from every scheduled task, and from the live runtime. AVSPIKE-004. |
| 7 | Existing DataJsonGoldenTests, the complete .NET test suite, and both x64 Release WinForms targets pass unchanged. | met | The .NET suite passed 258 with one documented opt-in skip, and both x64 Release WinForms targets, `net10.0-windows` and `net472`, passed. Re-proved by the plan-002 gate sweep on 2026-07-31. |
| 8 | The release-system fixture passes and candidate inspection proves spike outputs are absent from WinForms release packages. | met | The release-system fixture passed 114 of 114 assertions and candidate inspection proved spike outputs absent from the WinForms release packages. AVSPIKE-004. |
| 9 | An attended normal-user smoke and documentation evidence record the feasibility result and any separately gated polling recommendation. | open | The attended normal-user smoke and its documentation evidence record have not been performed. This is the only reason plan-001 remains `partial`. Closing it requires either performing the attended smoke or filing a waiver record in this file. |

## plan-002 — Non-deploying CI gates and campaign history contract

Every criterion has recorded evidence and all agent work is in the working
tree, so the ledger state is `implemented`.

It is deliberately **not** `accepted`. This is the campaign that wrote the
acceptance contract, so it cannot record its own acceptance; `accepted` is a
person-only state and automated verification may never write it.

| # | Criterion | State | Evidence |
| --- | --- | --- | --- |
| 1 | eng/ci/Invoke-LhmGates.ps1 -List prints every [build-gate.*] name defined in .codex/skills/project.toml with its included or excluded classification and reason, and exits 0. | met | `-List` exits 0 and prints all nine configured gates with classification and reason. `Test-GateRunner.ps1` cases 1 and 2 assert the listed set against the configuration parsed by the same `tomllib` call the runner uses, so the assertion cannot go stale when a gate is added. |
| 2 | Every build gate name in .codex/skills/project.toml is explicitly classified by the runner; an unclassified gate name makes the runner exit non-zero with an unclassified-gate error instead of silently skipping it. | met | `Test-GateRunner.ps1` case 3: a fixture declaring `[build-gate.rogue]` exits 2 and names the gate. The case asserts the specific unclassified-gate message rather than the bare exit code, because a configuration parse failure also exits 2. |
| 3 | The runner refuses, without executing, any command matching the deploying or host-mutating deny-list, and the release-candidate build and -RequireCurrentSource verify keys are excluded from the runnable set with a recorded reason while its release-system fixture test remains included. | met | `Test-GateRunner.ps1` case 4: an included gate carrying `New-LhmRelease.ps1` exits 3 and the sentinel that command would have written does not exist, proving refusal before execution. Case 5: `release-candidate` resolves only `Test-LhmReleaseSystem.ps1`, and neither `New-LhmRelease` nor `-RequireCurrentSource` appears. |
| 4 | eng/ci/Invoke-LhmGates.ps1 -All runs the included gate set on identity-verified SND-HOST, reports a per-gate result table, and exits non-zero if any gate fails. | met | Full `-All` sweep run twice on identity-verified `snd-host`: 8 of 8 included gates passed, per-gate result table emitted, `ci-gates` correctly skipped. `Test-GateRunner.ps1` case 10 proves a failing gate exits 1. |
| 5 | powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 discovers and runs every eng/ci/tests/Test-*.ps1, reports an aggregate pass and fail count, and exits non-zero on any failure. | met | `Test-LhmCiGates.ps1` discovers both test scripts and reports 22 passed, 0 failed, 0 skipped under Windows PowerShell 5.1 and PowerShell 7. An empty or missing test root exits 0, so the `ci-gates` gate cannot fail on an unpopulated suite. |
| 6 | .github/workflows/non-deploying-gates.yml parses as valid YAML, declares permissions contents read, references no repository secret and no deployment or publish action, and every gate-running step delegates to eng/ci/Invoke-LhmGates.ps1. | met | `Test-Workflow.ps1`: 11 assertions pass, including YAML parse, `permissions: contents: read` and nothing else, no `secrets.` reference, no deployment or cloud-login action, and every `run` step delegating to the runner. The three safety-critical assertions were negative-tested — broadened permissions, an inline `dotnet test`, and an injected secret each failed the suite — after which the workflow was restored byte-identical. |
| 7 | docs/campaign-history.md exists as a tracked ledger with one row per plan file in data/plans/, uses the ledger states registered, implemented, accepted, and closed, and records plan-001 with its attended exit criterion still open. | met | This file. One row per plan file in `data/plans/`, the four ledger states, and plan-001 recorded with criterion 9 open. |
| 8 | python -m unittest discover -s scripts -p test_*.py passes, including a new campaign-history regression that fails if any campaign is recorded accepted or closed while one of its exit criteria is still open or is waived without a complete waiver record, and that fails if the ledger and data/plans/ disagree on the criterion count. | met | `python -m unittest discover -s scripts -p test_*.py` passes 31 of 31, up from 12. Nineteen are new; eleven of those exercise the rule functions against deliberately broken fixtures, so the rules are proven to reject rather than merely to pass. |
| 9 | docs/architecture/campaign-control-plane.md states the campaign-history transition contract including the waiver record fields, and lists the added repository-owned overlay test with its normalization-aware Git blob ID. | met | `docs/architecture/campaign-control-plane.md` gained the acceptance ledger as a fourth authority layer, the two-axis explanation, the four ledger states with named owners, the transition rule, and the five mandatory waiver fields. `test_campaign_history.py` is listed as the fourth repository-owned overlay test with blob ID `58a37e2c879dd2fce554c5c6ac4efc978572b08e`. |
| 10 | .codex/skills/project.toml declares the ci module, the ci-gates build gate, the eng/ci and .github/workflows smart-test mappings, and the gate-definition-drift conflict zone, and python scripts/task_manager.py plan preflight --json reports ready with zero errors. | met | `.codex/skills/project.toml` declares `ci`, `[build-gate.ci-gates]`, the `eng/ci`, `eng/ci/tests`, `.github/workflows`, and `docs/campaign-history.md` smart-test mappings, and both new conflict zones. `plan preflight --json` reports ready with zero errors. |
| 11 | Both WinForms x64 Release targets and the .NET test suite still pass, and git diff --stat shows no change under Aga.Controls, LibreHardwareMonitorLib, LibreHardwareMonitor.Windows.Forms, LibreHardwareMonitor.Tests, or LibreHardwareMonitor.sln. | met | Both WinForms x64 Release targets and the .NET suite passed in the final sweep, and `git diff --stat` over `Aga.Controls`, `LibreHardwareMonitorLib`, `LibreHardwareMonitor.Windows.Forms`, `LibreHardwareMonitor.Tests`, `LibreHardwareMonitor.sln`, `Directory.Packages.props`, and `global.json` is empty. |
| 12 | data/plans/plan-001.json still has status partial with an empty executed_at, and no scheduled task, live runtime file, release candidate, rollback packet, or log archive changed during the campaign. | met | `data/plans/plan-001.json` is still `partial` with an empty `executed_at`, asserted by `test_campaign_history.py`. Live proof after the sweep: one process at PID 13104 on the live executable, root task `Running`, CSV grown to 78.9 MB, and `/`, `/data.json`, `/metrics` all HTTP 200. No release candidate, rollback packet, or log archive was created or changed. |
| 13 | docs/HANDOFF.md is retired only after docs/refactor-roadmap.md and docs/README.md carry its non-negotiable architecture decisions, do-not list, live-verification method, and remaining-ambiguity list. | met | All thirteen carry-over rows were verified present in `docs/refactor-roadmap.md` or `docs/README.md` before deletion. `AGENTS.md` now names `docs/refactor-roadmap.md` as the continuation checkpoint and additionally links `docs/campaign-history.md` and `eng/ci/README.md`. `docs/HANDOFF.md` was removed with `git rm`; the original 219-line file remains recoverable at `HEAD`. Surviving mentions are in completed agent specs, which are immutable campaign evidence and are deliberately not rewritten. |

## Waiver records

No waiver records exist.

A waiver is the only way a criterion may be treated as closed without being met.
Every waiver needs all five fields below; a waiver missing any field is treated
as `open` by `scripts/task_runtime/test_campaign_history.py`.

| Plan | Criterion | Waived by | Date | Reason | Accepted risk | Reopens if |
| --- | --- | --- | --- | --- | --- | --- |
