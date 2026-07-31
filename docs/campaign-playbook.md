# Campaign Playbook

**Status:** active
**Audience:** any agent or contributor running a campaign in this repository
**Read with:** `AGENTS.md` for lane classification,
`docs/architecture/campaign-control-plane.md` for the authority contract,
`docs/campaign-backlog.md` for what to run next

`AGENTS.md` says *what* the rules are. This says *how the tooling actually
behaves*, including the parts that are not obvious from reading it and that have
already cost real rework. Read this before your first campaign here.

---

## 1. Orientation, in order

1. `docs/README.md` — current state, SND-HOST paths, verification commands.
2. `docs/architecture/refactor-roadmap.md` — **the continuation checkpoint.** Phases,
   invariants, standing prohibitions, preserved branch evidence.
3. `docs/campaign-backlog.md` — the sequenced campaign queue and entry
   conditions.
4. `docs/architecture/campaign-control-plane.md` — truth layers, runtime
   provenance, acceptance contract.
5. `docs/campaign-history.md` — what has actually been accepted.
6. `eng/ci/README.md` — how verification runs.

Do not restart discovery. The deep structural audit is complete and its findings
are folded into the roadmap.

## 2. Machine boundary

This repository is worked on from **SND-HOST**. Before any machine-sensitive
mutation:

```powershell
& 'C:\Users\Dev\OneDrive\common\common_dev\Get-VerifiedMachineIdentity.ps1'
```

Stop unless it returns `VERIFIED` with machine ID `snd-host`.

`ops/deploy/snd-desk`, `ops/deploy`, and everything they install are **SND-DESK**
surfaces. They fail closed here by design. A peer-safe fixture exercising them
on this host is a fixture, not a deployment. Never treat SND-DESK evidence in
the docs as an SND-HOST fact.

## 3. The campaign lifecycle

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'

# 0. Preconditions: verified machine, clean tree, ready preflight.
python scripts\task_manager.py plan preflight --json

# 1. Create the draft, with full traceability. Populate all five metadata
#    fields; plan-001 left four of them empty and it cost traceability.
python scripts\task_manager.py plan create "<title>" `
  --planner-kind planner-refactor `
  --roadmap docs/architecture/refactor-roadmap.md `
  --phase "<phase name>" `
  --behavioral-invariant "<one per invariant>" `
  --rollback-strategy "<how to undo this campaign>"

# 2. Roster. One agent per disjoint file set, 2-6 agents.
python scripts\task_manager.py plan-add-agent plan-00N <letter> <name> `
  --scope "..." --deps "<letters>" --files "<comma,separated>" --complexity <low|medium|high>

# 3. Finalize the four elements the CLI exposes.
python scripts\task_manager.py plan finalize plan-00N `
  --goal "..." --exit-criterion "..." --verification-step "..." --documentation-update "..."

# 4. Patch the five elements the CLI does NOT expose (see 4.1).
# 5. Validate BEFORE approving.
python scripts\task_manager.py plan validate plan-00N

# 6. Author every agent spec in agents/ (see 4.3).
# 7. Approve and execute.
python scripts\task_manager.py plan approve plan-00N
python scripts\task_manager.py plan execute plan-00N

# 8. Launch, per dependency group.
python scripts\task_manager.py run <letters>
python scripts\task_manager.py result <letter> --payload-file <path>

# 9. Record acceptance evidence in docs/campaign-history.md.
```

Use the explicit path. **Do not use `plan go`** — it bundles
preflight, finalize, approve, and execute, which skips your `plan-add-agent`
calls and approves a roster-less plan.

## 4. Tooling behavior that is not obvious

### 4.1 `plan finalize` exposes only 4 of the 13 required plan elements

It writes `goal_statement`, `exit_criteria`, `verification_strategy`, and
`documentation_updates`. The other five — `impact_assessment`,
`risk_assessment`, `conflict_zone_analysis`, `integration_points`, and
`schema_changes` — must be patched into `data/plans/plan-00N.json` directly.

Keep the top-level `plan["conflicts"]` and `plan["integration_steps"]` arrays
**empty** when you do. `refresh_plan_elements` only overwrites the corresponding
elements when those keys are non-empty, so leaving them empty preserves your
patched values. This is the same shape plan-001 and plan-002 use.

### 4.2 The campaign document is rendered, never authored

`docs/campaign-plan-*.md` is regenerated from the plan JSON by
`persist_plan_artifacts` on **every** plan mutation. A manual edit is silently
overwritten. Change the JSON and re-render.

To re-render after patching an already-executed plan, where `plan finalize`
refuses to run:

```python
import sys; sys.path.insert(0, "scripts")
import task_manager as tm, json
plan = json.load(open("data/plans/plan-002.json", encoding="utf-8"))
# ... mutate plan ...
tm._persist_plan_artifacts(plan)
```

### 4.3 Author agent specs *before* `plan execute`

`plan execute` writes a stub template only when
`agents/agent-{letter}-{name}.md` does not already exist. Author the real specs
first and the tool preserves them. Author them afterwards and you are competing
with a generated stub.

The generated stub also tells every agent to update `live-tracker.md`. That
conflicts with single-writer ownership, so **each spec must explicitly override
it**: one agent owns the tracker, everyone else returns their row text in their
result payload.

### 4.4 `executed` does not mean complete

`executed` means "the tooling registered the agents and generated their
templates". A plan is `executed` the moment it is registered, with every exit
criterion still open. Any acceptance rule keyed on plan status is wrong on day
one. Acceptance lives on the separate ledger axis in
`docs/campaign-history.md`, and `accepted` is person-only.

### 4.5 Do not run `merge` after inline execution

`python scripts/task_manager.py merge` restores tracked files from the agent
worktree branches it expects to find. If the agents ran **inline in the primary
checkout** instead of in Git worktrees, it finds no worktrees, reports conflict
sets, merges nothing, and **reverts every tracked modification in the working
tree**. Untracked files survive; tracked edits do not.

It also resolves against `execution_manifest.plan_id` in `data/tasks.json`,
which can still point at an older campaign.

Inline execution needs no merge step. Treat `verify` with the same caution: its
generic verification is a strict subset of what `eng/ci/Invoke-LhmGates.ps1`
already covers.

If you do lose edits this way, the untracked deliverables are still on disk and
the tracked files are recoverable from `HEAD`.

## 5. Environment landmines

| Symptom | Cause | Fix |
|---|---|---|
| `__pycache__` appears in the repo | The configured `exclude-globs` and the baseline both assume none exists | Set `PYTHONDONTWRITEBYTECODE=1` before **any** Python command. The gate runner sets it for its children. |
| A fixture `.toml` fails to parse, runner exits 2 | `Set-Content -Encoding UTF8` writes a BOM under Windows PowerShell 5.1; `tomllib` rejects a BOM | Write fixtures with `[System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding($false)))` |
| A Python `-c` one-liner sees a bare name instead of a string | PowerShell native argument passing strips embedded double quotes | Single-quote every Python string literal: `open(sys.argv[1],''rb'')` |
| A substring assertion against child-process output fails intermittently | Child PowerShell wraps stdout at the console width, mid-command | Assert against the JSON summary, or strip all whitespace first — wrapping only inserts whitespace |
| A TOML fixture path breaks | TOML *basic* strings process backslash escapes | Use literal (single-quoted) strings for Windows paths, or escape as `\\` like the real `project.toml` |
| `localhost:8080` refused while the dashboard is healthy | The listener binds the LAN address, not loopback | Probe the bound address from `Get-NetTCPConnection -State Listen -LocalPort 8080` |
| `Get-NetTCPConnection -OwningProcess <lhm-pid>` returns nothing | `HttpListener` is http.sys-backed; the socket belongs to `System` (PID 4) | Normal. Not evidence of an outage. |

Two configuration traps worth knowing:

- `[analysis] exclude-globs` **replaces** the analyzer defaults rather than
  extending them, and `fnmatch` gives `**` no special meaning. `**/bin/**`
  requires a separator before `bin`, so it misses a repository-root `bin/`. Both
  forms are needed.
- `Clear-LhmRepositoryBuildOutputs.ps1` has a deliberately explicit path list. A
  new project does **not** inherit destructive cleanup by having a `bin`
  directory. Add it by hand.

## 6. Verification ladder

Climb only as far as the change warrants.

**Tier 1 — fast gates.** Any docs, tooling, or configuration change.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python scripts\task_manager.py plan preflight --json
python -m unittest discover -s scripts -p "test_*.py"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
git diff --check
git clean -ndX          # must list only data/tasks.json and data/analysis-cache.json
```

**Tier 2 — full sweep.** Any source, project, or gate-definition change.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
```

Then clean up, because three `dotnet build` gates recreate `bin`/`obj`:

```powershell
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\eng\Clear-LhmRepositoryBuildOutputs.ps1
git clean -ndX          # back to the two data files
```

Never `git clean -fdX`; it deletes the execution ledger and analysis cache.

**Tier 3 — live proof.** Any campaign that could plausibly touch the runtime.
Five checks, all unchanged except CSV growth — see the live SND-HOST proof
section of `docs/README.md`.

**Tier 4 — attended.** A person drives the real UI. No automation may claim
this, and no automated `verified` implies it.

## 7. Ownership rules

- Each file has **at most one** agent owner per campaign, declared in the plan
  JSON and validated on approval.
- `live-tracker.md` has exactly one writer per campaign.
- `docs/README.md`, `live-tracker.md`, and `data/plans/` are a declared conflict
  zone. Assign one owner and say so in the conflict-zone analysis.
- `.codex/skills/project.toml` is a single-owner file. A new module, gate,
  mapping, or conflict zone belongs in the same change as the thing it
  describes.
- An agent that finds a defect in another agent's file **reports it in its
  result payload**; it does not patch it.
- Completed agent specs are immutable campaign evidence. Do not rewrite them to
  tidy a reference; that falsifies the record of what the agent was told.

## 8. Never

- Never push unless explicitly asked. `main` is deliberately ahead of
  `origin/main`.
- Never move or replace the live LHM runtime, its scheduled tasks, its config,
  or its active CSV.
- Never run `ops/deploy/snd-desk` as an SND-HOST deployment path.
- Never create a release candidate to verify a docs or tooling change.
- Never delete the preserved Plan-001 branches, or raw-delete a Git worktree.
- Never mark a criterion met without criterion-specific evidence, and never let
  automation write `accepted`.
- Never register a plan from a dirty tree.
