# LibreHardwareMonitor Structural Refactor Roadmap

**Status:** active
**Date:** 2026-07-31
**Scope:** repository structure, campaign control, fork-only organization, and
behavior-preserving seams
**Out of scope until separately approved:** live deployment relocation,
runtime/data migration, hardware ownership changes, and Avalonia promotion

This roadmap turns the structural audit into bounded campaigns. It is not
authority to move the live SND-HOST runtime or rename upstream-derived product
roots.

## Baseline decision

The repository is a long-lived fork that still needs low-friction upstream
integration. Therefore:

- keep `Aga.Controls`, `LibreHardwareMonitorLib`,
  `LibreHardwareMonitor.Windows.Forms`, and the solution at their inherited
  roots;
- organize fork-only experiments, operations, tests, documents, and campaign
  tooling around those roots;
- extract contracts before splitting high-coupling classes;
- keep source, release candidate, live runtime, rollback, and data/archive
  authority separate;
- make every live path change a later manifest-backed migration with rollback.

## Non-negotiable contracts

- WinForms remains the sole current hardware, process, and scheduled-task
  owner.
- `data.json` shape/order/IDs, Prometheus output, CSV behavior, settings, and
  hardware behavior do not change during structural phases.
- Both `net10.0-windows` and `net472` WinForms builds remain gates.
- The Avalonia fixture explorer remains non-elevated, fixture-only, and
  non-shipping until a new accepted specification says otherwise.
- `ops/deploy/snd-desk` is SND-DESK-only. It must fail closed on SND-HOST.
- No phase may treat a source build or candidate as a live promotion.

## Phase 0 — Baseline and ambiguity removal

**Status:** complete and committed

- [x] Verify SND-HOST identity before mutation.
- [x] Inventory source, live, candidate, rollback, log, archive, task,
      shortcut, environment, and service ownership.
- [x] Remove four clean completed worktrees after ancestry or patch-equivalence
      proof; preserve their branches.
- [x] Remove only the three verified-empty false Git markers.
- [x] Repair the stale local `vanilla` remote to the verified current checkout.
- [x] Reconcile Plan-001 to `partial` while preserving attended smoke as open.
- [x] Revalidate the untouched live process, task, HTTP endpoints, and CSV
      growth.

**Exit gate:** audit evidence is reviewable and no live consumer changed.

### Preserved Plan-001 branch evidence

Four completed Plan-001 worktrees were removed after ancestry or
patch-equivalence proof. **No branch was deleted**, and none may be:

| Branch | Commit |
|---|---|
| `main` | `96746807a41e186e1404e3f4220985db5dd6c276` |
| `agent/plan-001-b-parser` | `1d40e04f3b61aa35a4cc72f2ae6fa92295728621` |
| `integration/plan-001-bc-review` | `33cc19b50263184db4bd8ea6734d1f4d0bf8d522` |
| `agent/plan-001-c-shell` | `59cb5489ff65f64e2c4f3b3d5fc674421994c082` |
| `agent/plan-001-d-integration` | `b466837b20051862f1268b284fa06ec4bedd1f48` |

Only three verified-empty false Git markers were removed: `Monitoring\.git`,
`HWiNFO64\.git`, and `libre-dev\.git`.

### Standing prohibitions from Phase 0

- Do not delete the preserved Plan-001 branches.
- Do not raw-delete a Git worktree or any Git administration path. Worktree
  cleanup requires exact path validation plus clean-state and
  ancestry/patch-equivalence evidence; branch deletion is a separate decision.
- Do not create a release candidate merely to verify a documentation or
  control-plane change.
- Do not register a plan from a dirty tree.
- Do not run `git clean -fdX`. It removes `data/tasks.json` and
  `data/analysis-cache.json`, the local execution ledger and analysis cache.
  After a gate sweep, use `eng/Clear-LhmRepositoryBuildOutputs.ps1`
  instead, reviewing its `-WhatIf` output first. Its path list is deliberately
  explicit, so **a new project must be added to it by hand** — a project does
  not inherit destructive cleanup merely by having a `bin` or `obj` directory.
- Do not infer that an automated `verified` execution manifest means attended
  acceptance completed.
- **Do not run `python scripts/task_manager.py merge` for a campaign whose
  agents ran in the primary checkout rather than in worktrees.** `merge` restores
  tracked files from the agent branches it expects to find; with no worktrees it
  reports conflict sets and reverts every tracked modification in the working
  tree. Untracked files survive, tracked edits do not. Inline execution needs no
  merge step.

### Open quarantine and ownership decisions

Deliberately untouched. These are ownership questions for a later phase, not
cleanup permission:

- empty `.agents` placeholders in several Monitoring and source directories;
- empty `Monitoring\Active`;
- foreign `HWiNFO64\logex.txt` scratch transcript;
- `TerminateWarThunder`, pending registry-alert ownership inspection;
- root operational `docs` and `tests` under `Monitoring`, which still lack
  declared ownership.

## Phase 1 — Campaign and verification control plane

**Status:** complete

- [x] Pin `scripts/task_manager.py`, `scripts/task_runtime`, and
      `scripts/analysis` from the canonical Codex package.
- [x] Define repository paths, modules, conflict zones, docs-sync, smart-test,
      and build gates in `.codex/skills/project.toml`.
- [x] Ignore local execution state and analysis cache.
- [x] Make blocker states override stale success, keep manual exit criteria
      unevaluated without evidence, and add regression tests.
- [x] Keep generic campaign verification non-mutating; external candidate
      creation remains isolated in its named release gate.
- [x] Restore a ready planning preflight and high-confidence seven-project
      analysis.
- [x] Re-run all existing non-live regression/build fixtures and remove their
      generated repository output.
- [x] Review and commit the structural baseline.
- [x] Add non-deploying CI for campaign-control tests, .NET tests, both WinForms
      targets, web tests, Avalonia fixture tests, and operations contract tests.
- [x] Define an explicit campaign-history transition for Plan-001 after its
      attended gate is closed or waived.

**Exit gate:** satisfied by Plan-002. `eng/ci/Invoke-LhmGates.ps1 -All` passed
8 of 8 included gates from the clean baseline, and CI cannot deploy or mutate
the host: gate commands are read from `.codex/skills/project.toml` at run time
rather than hard-coded, every configured gate must be explicitly classified or
the run fails closed, and a deny-list is applied to each resolved command
immediately before execution so a later configuration edit cannot smuggle a
deploying command into an included gate. The GitHub Actions workflow only
delegates to that runner and duplicates no gate command.

### Decision — `eng/ci` adopted early, additively

`eng/ci` is a Phase 2 target path, taken here ahead of Phase 2 deliberately.
The adoption is purely additive: nothing moved, nothing was renamed, and no
existing entry point changed. The reason is that a CI entry point is exactly
the kind of path whose later relocation breaks things silently, so it was worth
creating in its final home once rather than moving it later. Phase 2 therefore
moves the existing engineering entry points into an established boundary
instead of relocating a freshly created one.

### Campaign ownership rules

These apply to every campaign, not just Plan-002:

- Do not edit an agent's owned files from outside that agent. Ownership is
  declared per file in the plan JSON and validated on approval.
- `live-tracker.md` has exactly one writer per campaign. Other agents return
  their tracker row text in their result payload.
- Do not hand-edit `docs/campaign-plan-*.md`. It is rendered from
  `data/plans/*.json` on every plan mutation and manual edits are overwritten.
- A `Done` tracker row is agent-level completion. Campaign acceptance lives in
  `docs/campaign-history.md` and is a person-only action.

## Phase 2 — Fork-only taxonomy

**Status:** not started

- [x] Move the Avalonia fixture projects into `experiments/avalonia-fixture-explorer/` (Plan-003, 2026-07-31): moved with history preserved, configuration-only rewiring proven by the non-deploying gate runner, and a permanent stale-reference gate added.
- [x] Separate candidate creation from peer-specific deployment semantics (Plan-004, 2026-07-31): `ops/candidate` (host-neutral), `ops/deploy/snd-desk` (SND-DESK-only), `ops/log-management` (unchanged), and the repository cleanup tool moved to `eng/`. Fail-closed guards proven under `pwsh`; configuration-only rewiring proven by the runner staying byte-identical.
- [x] Group current documents under architecture, features, operations, and campaigns (Plan-005, 2026-07-31): 13 feature specs under `docs/features/`, refactor-roadmap and repository-build-output-cleanup under `docs/architecture/`, and the completed point-in-time discovery reviews retired. Tooling-coupled docs stay at `docs/` root; `scripts/task_manager.py` unmodified.
- Move general engineering entry points toward `eng/build`, `eng/test`, and
  `eng/ci`.
- Update solutions, scripts, package isolation checks, docs, and test mappings
  in the same atomic change as each move.

**Do not move:** inherited product roots, the live runtime, release store,
rollback store, active logs, or installed operations tooling.

**Exit gate:** every moved path has zero stale current references, both
framework builds pass, the 75-test Avalonia gate passes, and release/log
contract tests pass.

## Phase 3 — Verification suite boundaries

**Status:** partially complete — the suite split landed with Plan-006; the
characterization-tests item remains open and is plan-007

- [x] Split library-only behavior tests from WinForms/application tests
  (Plan-006, 2026-08-01): `LibreHardwareMonitor.Tests.Library` (references
  `LibreHardwareMonitorLib` only, no WinForms assembly in its built dependency
  set) and `LibreHardwareMonitor.Tests.Application` created via pure `git mv`
  renames with the 259/258/1 population preserved.
- [x] Isolate external contracts: `data.json`, HTTP routes, Prometheus, CSV, and
  web assets (Plan-006, 2026-08-01): `LibreHardwareMonitor.Tests.Contracts`
  holds the byte-identical golden master and the HTTP, Prometheus, CSV
  timestamp, and web-dashboard-retirement tests.
- [ ] Add characterization tests around lifecycle, settings projection, and
  reset/option ordering before extraction. **Open — this is plan-007 and it is
  non-optional.**
- [x] Keep hardware-dependent and attended tests explicitly separate from
  deterministic CI (Plan-006, 2026-08-01): the Attended suite exists in the sln
  but is excluded from `LibreHardwareMonitor.Tests.slnf` and from every gate
  command by construction, pinned by the permanent
  `eng/ci/tests/Test-SuiteBoundaries.ps1`.

**Exit gate:** each future seam maps to a focused deterministic suite, and
cross-cutting changes still trigger the full gate.

## Phase 4 — Application and adapter seams

**Status:** not started

Extract one seam per campaign, in this order:

1. immutable sensor snapshot and `data.json` projection;
2. HTTP listener/dispatch service around the preserved external contract;
3. application lifecycle, polling, option/reset, and shutdown coordination;
4. settings projection and persistence coordination;
5. WinForms presentation adapters for tree, plot, tray, and gadget surfaces.

`MainForm` remains the composition root until each extracted contract is
characterized and accepted. Avoid a wholesale rewrite.

**Exit gate:** external output is byte/semantics compatible where required,
both framework targets pass, and no ownership becomes duplicated.

## Phase 5 — Hardware lifecycle seams

**Status:** not started

- Introduce explicit group/registry lifecycle boundaries around `Computer`.
- Separate discovery/update/close behavior in the NVIDIA and storage groups.
- Reuse the immutable snapshot contract rather than exposing mutable hardware
  trees to new hosts.
- Preserve upstream mergeability and hardware quirks with characterization
  tests.

**Exit gate:** no regression in device discovery, polling, close/dispose,
settings, or downstream sensor identity.

## Phase 6 — Runtime/data packaging and optional path migration

**Status:** physical workspace/path migration completed 2026-07-31; separate
data-root authority remains gated

- [x] Produce a complete Libre-specific consumer manifest covering scheduled
  tasks, shortcut, SQ shims, environment, Git reference, log management,
  firewall/service absence, shell cache, and rollback.
- [x] Move development to `D:\DevHome` and consolidate candidates, deployment,
  rollback, installed operations, and archive under the physical
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack` authority.
- [x] Preserve source refs/stash/ignored state, artifact hashes, live executable
  bytes, all baseline settings, HTTP behavior, and CSV/archive growth.
- [ ] Choose and implement a separate SND-HOST data-root authority if active
  CSV/config should later leave `deployments\current`; no
  `LIBREHARDWAREMONITOR_DATA_ROOT` or `librehw.runtime.json` was introduced.

**Exit gate:** exact-path process/task proof, `/`, `/data.json`, and `/metrics`
HTTP `200`, settings persistence, CSV/archive growth, and full consumer
rebinding all passed for the physical path migration. A later data-root change
must pass the same gate again.

## Next campaign

**The sequenced queue lives in `docs/campaign-backlog.md`.** That file holds the
executable order — next campaign, entry conditions, ownership, exit criteria.
This file holds the phase contract those campaigns must satisfy. Keep the two in
step: when a campaign lands, close its roadmap item here and delete its section
there.

At this checkpoint the queue is:

1. **A1** — Plan-001's attended normal-user smoke. Person-only, still open.
2. **plan-007** — characterization tests before extraction. Re-read
   `docs/campaign-backlog.md` before starting; it is an outline, not a spec.

A2 (Plan-002 acceptance) was completed by the maintainer on 2026-08-01 and is
recorded in `docs/campaign-history.md`.

Plan-003, Plan-004, Plan-005, and Plan-006 have landed: the Avalonia fixture
explorer lives under `experiments/avalonia-fixture-explorer/`, operations are
split into `ops/candidate`, `ops/deploy/snd-desk`, and `eng/`, current docs are
grouped under `docs/features/` and `docs/architecture/`, and the flat test
project is split into the four `LibreHardwareMonitor.Tests` boundary suites
behind the deterministic `LibreHardwareMonitor.Tests.slnf`. All moves preserved
history and proved configuration-only rewiring with the runner byte-identical.

Do not register a plan from a dirty tree; check `plan preflight` first, and see
`docs/campaign-playbook.md` for the full lifecycle.
