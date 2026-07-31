# Agent Task — Control Plane Wiring

**Scope:** Wire the CI module, gate, smart-test mappings, and conflict zone
into `project.toml`, then update the tracker, roadmap, current-state docs, and
retire the handoff.

**Depends on:** Agent E, Agent F, Agent G, Agent H

**Output files:** `.codex/skills/project.toml`, `docs/README.md`,
`live-tracker.md`, `docs/refactor-roadmap.md`, `docs/HANDOFF.md`

## Exit Criteria

- `.codex/skills/project.toml` declares the `ci` module, the `ci-gates` build
  gate, the `eng/ci` and `.github/workflows` smart-test mappings, and the two
  new conflict zones, and `scripts/Test-AvaloniaSpike.ps1` is claimed by a
  module.
- `python scripts/task_manager.py plan preflight --json` reports `ready` with
  zero errors after the edit.
- `live-tracker.md` carries one row per plan-002 agent, `CIGATE-001` through
  `CIGATE-005`, written only by you.
- `docs/refactor-roadmap.md` marks both open Phase 1 items complete and records
  the deliberate early adoption of the Phase 2 `eng/ci` path.
- `docs/README.md` documents the `eng/ci` verification surface, links
  `docs/campaign-history.md`, and carries the handoff content listed in Part 5.
- `docs/HANDOFF.md` is deleted only after every item in the Part 5 checklist is
  verifiably present in the roadmap or the current-state docs, and no file
  still references it.

---

## Context — read before doing anything

1. `AGENTS.md` — task classification, the source-of-truth map you are updating,
   and the before-handoff docs checklist.
2. `docs/HANDOFF.md` — read all of it. You are retiring it, so nothing in it
   may be lost by accident. Its own lifecycle note is the authority for when it
   may go.
3. `docs/refactor-roadmap.md` — Phase 1's two open checkboxes and its exit
   gate; the Phase 2 `eng/build`, `eng/test`, `eng/ci` target paths.
4. `docs/README.md` — the current-state map, source map, and `Verify` block.
5. `.codex/skills/project.toml` — every section you touch. Read the whole file
   before editing; a malformed edit blocks preflight for every campaign.
6. `docs/architecture/campaign-control-plane.md` — the rule that adding a
   project, moving a fork-only path, or changing a verification command
   requires updating `project.toml` in the same change. Agent H has already
   extended this file; do not edit it.
7. `eng/ci/README.md`, `eng/ci/Invoke-LhmGates.ps1`,
   `eng/ci/Test-LhmCiGates.ps1`, `.github/workflows/non-deploying-gates.yml`,
   and `docs/campaign-history.md` — the delivered artifacts you are wiring in.
8. `live-tracker.md` — the existing `AVSPIKE-001` through `AVSPIKE-004` rows and
   their column set. Match the format.

---

## Task

### Part 1 — `.codex/skills/project.toml`

Make these edits and no others. Preserve formatting, ordering style, and the
existing escaping convention for Windows paths.

- `[modules]`: add `ci = ["eng/ci/", ".github/"]`.
- `[modules]`: add `scripts/Test-AvaloniaSpike.ps1` to the existing
  `avalonia-tests` entry. It is currently claimed by no module, which is the
  only genuine gap behind the standing unassigned-files analyzer warning.
- `[smart-test.mappings]`: add
  - `"eng/ci/*.ps1" = ["eng/ci/Test-LhmCiGates.ps1"]`
  - `"eng/ci/tests/*.ps1" = ["eng/ci/Test-LhmCiGates.ps1"]`
  - `".github/workflows/*.yml" = ["eng/ci/Test-LhmCiGates.ps1"]`
- `[build-gate.ci-gates]`: add with `build = ""` and
  `test = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\\ci\\Test-LhmCiGates.ps1"`.
- `[conflict-zones]`: add two entries in the existing
  `"<files> | <reason>"` form:
  - `eng/ci/Invoke-LhmGates.ps1, .codex/skills/project.toml | gate definition drift between the runner and the configuration`
  - `docs/architecture/campaign-control-plane.md, scripts/task_runtime/test_campaign_history.py | campaign-runtime provenance record and its overlay files`

Do not change `[commands]`. Its `compile`, `build`, `test`, `test_fast`, and
`test_full` entries are the campaign runtime's generic verification, and the
control-plane contract requires them to stay non-mutating.

After editing, confirm the runner now classifies `ci-gates` as an excluded,
present gate rather than reporting it as missing.

### Part 2 — `live-tracker.md`

Add five rows using the existing column set. You are the only writer; every
other agent returned its row text in its result payload, so use that text
rather than inventing an update line.

| ID | Owner | Scope |
|---|---|---|
| `CIGATE-001` | `agent-e` | gate runner and CI README |
| `CIGATE-002` | `agent-f` | test dispatcher and gate-runner suite |
| `CIGATE-003` | `agent-g` | workflow delegation and workflow tests |
| `CIGATE-004` | `agent-h` | campaign-history contract, ledger, and regression |
| `CIGATE-005` | `agent-i` | control-plane wiring and docs |

Keep the tracker's existing honesty convention: record what was verified and
what remains open, and do not claim attended acceptance.

### Part 3 — `docs/refactor-roadmap.md`

- Tick both open Phase 1 checkboxes and update the Phase 1 status line.
- Record the `eng/ci` decision explicitly: the Phase 2 target path was adopted
  early and additively, with no moves and no renames, so the CI entry point
  never has to relocate. Phase 2 now moves the existing engineering entry
  points into an established boundary instead of relocating a fresh one.
- Confirm Phase 1's exit gate is satisfied: a clean baseline passes the
  configured non-live gates and CI cannot deploy or mutate the host. Name the
  evidence.
- Update the `## Next campaign` section, which currently tells the reader to
  pick between the CI campaign and the Avalonia experiment move. The CI
  campaign is done; the isolated Avalonia experiment move is now the next
  candidate.

### Part 4 — `docs/README.md`

- Add an `eng/ci/` entry to the source map describing the non-deploying gate
  runner and where the gate definitions live.
- Add a `docs/campaign-history.md` entry describing it as the durable
  per-criterion acceptance ledger.
- Add the runner to the `Verify` block as the single entry point, keeping the
  individual commands listed beneath it. Do not remove the existing commands;
  the runner delegates to them and readers still need the direct forms.
- Update the `Roadmap` paragraph: Phase 1 is complete, source moves and live
  relocation remain gated.
- Extend the `Repository` section with the `vanilla` remote and the fact that
  `upstream` and `vanilla` pushes are disabled.

### Part 5 — Retire `docs/HANDOFF.md`

Delete it **only** after each of these is verifiably present elsewhere. Check
each one off in your result payload with the destination file and section.

| Handoff content | Destination |
|---|---|
| Non-negotiable architecture decisions | `docs/refactor-roadmap.md` baseline decision and non-negotiable contracts |
| Preserved Plan-001 branch table, all five commits | `docs/refactor-roadmap.md` Phase 0 |
| Do not push unless explicitly requested | `docs/README.md` repository section |
| Do not create a release candidate merely to verify docs or control changes | `docs/README.md` or the roadmap non-negotiable contracts |
| Do not delete the preserved Plan-001 branches | `docs/refactor-roadmap.md` Phase 0 |
| Do not raw-delete Git worktrees or Git administration paths | `docs/refactor-roadmap.md` Phase 0 |
| Live SND-HOST proof method — process, root task, HTTP endpoints, CSV growth | `docs/README.md` verify section |
| Remaining ambiguity list — empty `.agents` placeholders, empty `Monitoring\Active`, foreign `HWiNFO64\logex.txt`, `TerminateWarThunder`, unowned root `docs` and `tests` | `docs/refactor-roadmap.md`, as open quarantine and ownership decisions |
| Local remote configuration including the `vanilla` remote and disabled pushes | `docs/README.md` repository section |
| Do not use `git clean -fdX`; it removes the local execution ledger and analysis cache | `docs/README.md` or the roadmap |
| Plan-001's two intentionally different statuses | already carried by `docs/architecture/campaign-control-plane.md` and `docs/campaign-history.md` — verify, do not duplicate |
| Fast gate re-run commands, including the leading `PYTHONDONTWRITEBYTECODE` line | `docs/README.md` verify section, now fronted by the runner |
| Do not edit a Plan-002 agent's owned files from outside that agent; `live-tracker.md` has exactly one writer | `docs/refactor-roadmap.md`, alongside the campaign-ownership rules |
| Do not hand-edit a generated campaign document; `docs/campaign-plan-*.md` is rendered from `data/plans/*.json` on every plan mutation and manual edits are overwritten | `docs/README.md` docs policy, or `docs/refactor-roadmap.md` |

Then remove every remaining reference to `docs/HANDOFF.md`. At minimum
`AGENTS.md` section 2 and the `docs/README.md` source map name it. Search the
whole repository before deleting:

```powershell
git grep -n "HANDOFF"
```

`AGENTS.md` is not in your ownership set. If the grep shows a reference there,
report it in your result payload with the exact replacement text rather than
editing the file yourself.

---

## Constraints

- Do not edit `eng/ci/**`, `.github/**`,
  `docs/architecture/campaign-control-plane.md`, or
  `docs/campaign-history.md`. Agents E, F, G, and H own them.
- Do not edit `AGENTS.md`; report the needed change instead.
- Do not edit `data/plans/plan-001.json`, `data/plans/plan-002.json`, or either
  campaign document. The campaign documents are rendered from the plan JSON by
  the tooling.
- Do not change `[commands]` in `project.toml`.
- Do not delete `docs/HANDOFF.md` before the Part 5 checklist is complete. A
  partially carried retirement is worse than no retirement.
- Do not claim any attended acceptance, live promotion, or push occurred.

---

## Verification

Order matters here. Run the cheap checks and the ignored-state check **before**
the full sweep, because the sweep legitimately creates build output.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
python -m unittest discover -s scripts -p "test_*.py" -v
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
git grep -n "HANDOFF"
git diff --check
git clean -ndX          # must list only data/tasks.json and data/analysis-cache.json
```

Then the full sweep and its cleanup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
git clean -ndX          # now long: bin/ and obj/ from the three dotnet build gates
.\scripts\local-release\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\scripts\local-release\Clear-LhmRepositoryBuildOutputs.ps1
git clean -ndX          # back to the two data files
git status --short
```

`-List` must now show `ci-gates` as a present but excluded gate.
`git grep -n "HANDOFF"` must return nothing except your own result-payload note
about `AGENTS.md`, if one is needed.

The `-All` sweep runs three `dotnet build` gates, so the `git clean -ndX` list
immediately after it is long. That is expected and is not a defect: build output
is ignored and reproducible, and the structural baseline recorded zero generated
directories precisely because it was cleaned afterwards. Review the `-WhatIf`
output before the real cleanup run. No `__pycache__` directory may appear in any
of the three listings; if one does, report it — the runner's
`PYTHONDONTWRITEBYTECODE` setting is not reaching its Python gate children.

Do not run `git clean -fdX` to do this. It would delete `data/tasks.json` and
`data/analysis-cache.json`. `Clear-LhmRepositoryBuildOutputs.ps1` is the
repository's guarded cleanup tool and is deliberately on the runner's deny-list,
so this has to be your explicit named step, not something the runner does.

---

## Do NOT

- Do not register `ci-gates` as a gate the runner will execute; it is
  self-referential and must stay excluded from `-All`.
- Do not add a separate build gate or command for Agent H's Python test; the
  existing campaign-control gate already discovers it.
- Do not remove the direct verification commands from `docs/README.md` when
  adding the runner.
- Do not run `git clean -fdX`; it would delete the local execution ledger and
  the analysis cache.
- Do not push. `origin` stays deliberately behind.
- Do not silently drop a handoff item you could not find a home for; report it
  instead.
