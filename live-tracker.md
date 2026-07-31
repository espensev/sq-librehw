# Campaign tracker

This tracker records source-campaign work. It does not describe or authorize a
live deployment, and a `Done` row is agent-level completion, not campaign
acceptance. Acceptance lives in `docs/campaign-history.md`.

## Plan-001 — fixture-only Avalonia sensor explorer

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-001 | Done | agent-a | package/project bootstrap and contracts | Fixture-only Avalonia explorer | Restored three isolated projects, passed the Core Release build, froze loader/snapshot interfaces, and passed 114/114 release-system assertions. Launch candidate creation internally verified clean/promotable state, but independent dual-shell current-source verification was not recorded before launch. |
| AVSPIKE-002 | Done | agent-b | bounded parser and fixtures | Fixture-only Avalonia explorer | Enforced every byte/depth/node/child/string/identity bound with atomic immutable projection; 49/49 parser and replacement cases passed. A reliable Windows ACL-denied test remains unavailable, although typed catches exist. A non-cooperative caller stream can outlive logical supersession; the app uses a bounded `FileStream`, and view-model request identity prevents stale publication. |
| AVSPIKE-003 | Done | agent-c | Avalonia shell and headless UI | Fixture-only Avalonia explorer | Covered explicit states, accessible labels, keyboard focus/navigation, cancellation, and safe errors; 19/19 UI/headless cases passed. Known `AVLN3001` source-build warning remains; attended smoke is pending. |
| AVSPIKE-004 | Done | agent-d | integration, verification, docs | Fixture-only Avalonia explorer | Added seven real parser-to-view-model cases (68 pre-D, 75/75 final) and passed 10/10 source/regression gates. Manager candidate `0.9.6-20260730-210528120-b466837` passed dual-shell exact-source verification and both package inventories contained zero Avalonia/spike entries. Attended smoke remains pending; read-only polling is worth a separate spec only. |

## Plan-002 — non-deploying CI gates and campaign history contract

Agent I is the only writer of this file. Every other Plan-002 agent returned its
row text in its result payload rather than editing here, because
`docs/README.md, live-tracker.md, data/plans/` is a declared conflict zone.

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| CIGATE-001 | Done | agent-e | non-deploying gate runner | Non-deploying CI gates | `eng/ci/Invoke-LhmGates.ps1` reads gate commands from `.codex/skills/project.toml` at run time and hard-codes none. Every configured gate must be explicitly classified or the run fails closed; a 27-pattern deny-list is applied to each resolved command immediately before execution, so a later configuration edit that adds a deploying command is still refused. Verified `-List`, `-All -DryRun`, single-gate, unknown-gate, excluded-gate, unclassified-gate, deny-list refusal, failing-gate, and `-FailFast` paths under both PowerShell engines. The full `-All` sweep passed 8/8 with the live process, root task, and CSV untouched. |
| CIGATE-002 | Done | agent-f | dispatcher and gate-runner suite | Non-deploying CI gates | `eng/ci/Test-LhmCiGates.ps1` runs every `eng/ci/tests/Test-*.ps1` in a child process of the same engine and exits 0 on an empty test root, so the `ci-gates` gate cannot fail on an unpopulated suite. `Test-GateRunner.ps1` covers all eleven required cases. Two initially passed for the wrong reason: `Set-Content -Encoding UTF8` writes a BOM that `tomllib` rejects, so fixtures failed to parse and the runner exited 2 before reaching the behavior under test, and a double-quoted Python key was stripped by native argument passing, leaving an empty expected set. Both are now asserted specifically rather than by bare exit code. |
| CIGATE-003 | Done | agent-g | read-only workflow delegation | Non-deploying CI gates | `.github/workflows/non-deploying-gates.yml` declares `permissions: contents: read` and nothing else, references no repository secret and no deployment, publish, or cloud-login action, and its single gate step delegates to the runner rather than duplicating any gate command. Eleven assertions pass. The three safety-critical ones were negative-tested: broadened permissions, an inline `dotnet test`, and an injected secret reference each failed the suite, after which the workflow was restored byte-identical. The workflow has never executed on a hosted runner because `origin` is deliberately unpushed; it is verified by YAML parse and structural inspection only. |
| CIGATE-004 | Done | agent-h | campaign-history transition contract | Non-deploying CI gates | Separated two axes that were previously conflated: plan lifecycle status answers whether the tooling registered and ran a campaign, ledger state answers whether a person accepted it. `executed` arrives the moment a plan is registered, with every criterion open, so no acceptance rule may key on plan status. `docs/campaign-history.md` adds four ledger states with named owners, a rule barring `accepted` or `closed` while any criterion is open, and five mandatory waiver fields. 31/31 campaign-control tests pass, nineteen new, eleven of which prove the rules reject broken fixtures. Plan-001 stays `partial` with criterion 9 open. Open question recorded there: criterion 1's pre-launch ordering clause was not satisfied, though its substance was verified afterwards. |
| CIGATE-005 | Done | agent-i | control-plane wiring and docs | Non-deploying CI gates | Registered the `ci` module, the `ci-gates` build gate, four smart-test mappings, and two new conflict zones in `.codex/skills/project.toml`, and claimed `scripts/Test-AvaloniaSpike.ps1` under `avalonia-tests` — the only real module-ownership gap behind the standing unassigned-files analyzer warning. Closed both open roadmap Phase 1 items, recorded the deliberate early adoption of the Phase 2 `eng/ci` path and the campaign ownership rules, and folded every still-live item from `docs/HANDOFF.md` into the roadmap and current-state docs. Retirement was initially held because `AGENTS.md` was outside this agent's ownership set; the maintainer then authorized that edit, so `AGENTS.md` now points at the roadmap and `docs/HANDOFF.md` was removed with `git rm`, the original staying recoverable at `HEAD`. |
| CIGATE-006 | Done | maintainer | post-campaign gap closure | Non-deploying CI gates | Three gaps reported by agents E, F, and I were closed under maintainer authorization. `AGENTS.md` source-of-truth map now names the roadmap as the continuation checkpoint and adds `docs/campaign-history.md` and `eng/ci/README.md`; `docs/HANDOFF.md` deleted. `Clear-LhmRepositoryBuildOutputs.ps1` gained the six Avalonia spike `bin`/`obj` paths, keeping its deliberately explicit list and passing its reparse-point and runtime-state guard suite unchanged. The analyzer `exclude-globs` gained root-relative `bin/**`, `obj/**`, `__pycache__/**`, and `.git/**`: the configured list replaces the defaults rather than extending them, and `fnmatch` gives `**` no special meaning, so `**/bin/**` never matched a repository-root `bin/`. Unassigned inventory fell 112 to 103 with zero build output remaining; the residue is three cross-cutting entry points already declared in `[smart-test.cross-cutting]`. |

## Plan-003 — Avalonia fixture explorer relocation

Agent N is the only writer of this file for Plan-003; agents J-M returned their
row text in their result payloads.

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVMOVE-001 | Done | agent-j | move spike projects | Avalonia fixture explorer relocation | Moved the three spike projects and the `.slnx` under `experiments/avalonia-fixture-explorer/` with `git mv`; the cluster's internal relative references resolved unchanged and the moved solution built clean in Release (0 errors). No inherited product root changed. |
| AVMOVE-002 | Done | agent-k | rewire spike config | Avalonia fixture explorer relocation | Rewired `.codex/skills/project.toml` (modules, smart-test mappings, gate commands, conflict zone, cross-cutting), relocated `Test-AvaloniaSpike.ps1` into the experiment root with fixed repository-root resolution, and updated the six Avalonia `bin`/`obj` paths in `Clear-LhmRepositoryBuildOutputs.ps1`. `Invoke-LhmGates.ps1` stayed byte-identical, proving configuration-only rewiring. |
| AVMOVE-003 | Done | agent-l | stale-reference gate | Avalonia fixture explorer relocation | Added the permanent `eng/ci/tests/Test-NoStaleReferences.ps1` with a data-driven move map and a centralized allow-list for immutable historical evidence. The CI dispatcher now discovers three test scripts. |
| AVMOVE-004 | Done | agent-m | spike-move docs | Avalonia fixture explorer relocation | Updated current path claims in `docs/README.md` and the Avalonia feature spec, and the one spike path in the structural discovery audit, so no current document references the old location. |
| AVMOVE-005 | Done | agent-n | spike-move close | Avalonia fixture explorer relocation | Added the Plan-003 ledger row and criterion evidence to `docs/campaign-history.md` without automatic acceptance, recorded the maintainer's honest deferral of the plan-001 attended smoke, closed the Phase 2 Avalonia roadmap item, and removed the completed Plan-003 section from the backlog. |

## Plan-004 — operations taxonomy

Agent U is the only writer of this file for Plan-004; agents O-T returned their
row text in their result payloads.

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVOPS-001 | Done | agent-o | move candidate ops | Operations taxonomy | Moved `ops/release/` to `ops/candidate/` with `git mv` (4 files, history preserved) and updated the synthetic `ops\release` mirror in `Test-LhmReleaseSystem.ps1` to `ops\candidate`. |
| AVOPS-002 | Done | agent-p | dissolve local-release | Operations taxonomy | Dissolved `scripts/local-release/` and relocated `ops/local-release/` (9 files to `ops/deploy/snd-desk/`), moved `Clear-LhmRepositoryBuildOutputs.ps1` to `eng/`, and fixed every internal `$PSScriptRoot`/repo-root/cleanup reference broken by the split. |
| AVOPS-003 | Done | agent-r | rewire config and gates | Operations taxonomy | Rewired `.codex/skills/project.toml` (modules, mappings, 4 gate commands), the spike gate's `ops\release` reference to `ops\candidate`, and extended the permanent stale-reference gate with the ops move-map and dissolved-path checks. `Invoke-LhmGates.ps1` stayed byte-identical. |
| AVOPS-004 | Done | agent-s | verify fail-closed | Operations taxonomy | Verified the relocated peer-safe fixture passes under `pwsh` (proving fail-closed guards hold on snd-host), the content-based deny-list still refuses deploying commands, and the extended stale-reference gate passes. |
| AVOPS-005 | Done | agent-t | ops-move docs | Operations taxonomy | Updated 45 path claims across 9 doc/eng files so no current document references a dissolved ops path; the stale-reference gate passes. |
| AVOPS-006 | Done | agent-u | ops-move close | Operations taxonomy | Added the Plan-004 ledger row and criterion evidence to `docs/campaign-history.md` without automatic acceptance, closed the Phase 2 operations-taxonomy roadmap item, advanced the backlog current position, and removed the completed Plan-004 section. |

## Plan-005 — documentation taxonomy

Agent X is the only writer of this file for Plan-005; agents V-W returned their
row text in their result payloads.

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVDOCS-001 | Done | agent-v | reorganize docs | Documentation taxonomy | Moved 13 feature specs to `docs/features/`, refactor-roadmap and repository-build-output-cleanup to `docs/architecture/`, and retired the 2 completed discovery reviews (findings folded into the roadmap). Tooling-coupled docs stay at `docs/` root. |
| AVDOCS-002 | Done | agent-w | rewire doc references | Documentation taxonomy | Updated 45 path references across AGENTS.md, docs/README, campaign-playbook, campaign-backlog, inter-doc links, the 2 deploy scripts, and extended the stale-reference gate with the doc move-map and a retired/bare-doc-path check. `scripts/task_manager.py` unchanged. |
| AVDOCS-003 | Done | agent-x | docs-move close | Documentation taxonomy | Added the Plan-005 ledger row and criterion evidence to `docs/campaign-history.md` without automatic acceptance, closed the Phase 2 documentation-grouping roadmap item (at its new `docs/architecture/` path), and removed the completed Plan-005 section from the backlog. |
