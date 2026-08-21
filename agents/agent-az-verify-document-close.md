# Agent Task — Verify, Document, and Close Plan-013

**Plan:** `plan-013`

**Baseline:** integrated Agents AW, AX, and AY

**Depends on:** Agents AW, AX, and AY

**Exclusive output:**

- `.codex/skills/project.toml` (only if the new files genuinely require a mapping change; otherwise byte-identical)
- `data/plans/plan-013.json` (render-sourced mutations only)
- `docs/campaign-plan-013-hardware-lifecycle-seams.md` (rendered, never hand-edited)
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/features/feature-hardware-lifecycle-seams.md` (new)
- `live-tracker.md` (you are the only writer this campaign)

## Goal

Independently verify the integrated hardware lifecycle seams and every protected contract, then write the campaign truth surfaces once. Record `implemented`, never `accepted`; acceptance is person-only.

## Context — read before doing anything

1. `AGENTS.md`
2. `docs/campaign-plan-013-hardware-lifecycle-seams.md` and `data/plans/plan-013.json`
3. `docs/architecture/campaign-control-plane.md` (two-axis truth, transition rule)
4. AW/AX/AY result payloads: commits, patch-ids, blobs, counts, concerns
5. `docs/campaign-history.md` (existing plan-012 section is the format model)
6. `docs/architecture/refactor-roadmap.md` Phase 5, `docs/campaign-backlog.md`, `docs/README.md`
7. `scripts/task_runtime/test_campaign_history.py` (ledger rules your edits must keep green)

## Task

### Part 1 — Independent verification

Start from a clean integrated tree. Verify, do not trust payloads:

1. **Diff scope.** `ce33b82..HEAD` name-status outside campaign/config/documentation truth is exactly: modified `Computer.cs`, `NvidiaGroup.cs`, `StorageGroup.cs`; new `HardwareGroupRegistry.cs`, `NvidiaGroupLifecycle.cs`, `StorageGroupLifecycle.cs`, and the three new Library test files. Explicit protected diff over `Aga.Controls`, the rest of `LibreHardwareMonitorLib` (including `IComputer.cs`, `AmdGpuGroup.cs` — confirm its Latin-1 encoding was not rewritten, `IntelGpuGroup.cs`, `NvidiaML.cs`, `StorageDIT*`), all WinForms source, existing test files, `Directory.Packages.props`, both solutions/slnf, `webtests`, `eng/ci`, and `experiments` is empty.
2. **Public API.** `IComputer.cs` and every public Lib type are byte-identical; all new types are `internal`.
3. **Counts.** New-fact totals from each agent pass exactly; existing Plan-007 characterization files are unmodified and green; Library is `68 + N_aw + N_ax + N_ay` with zero failures; Application `178+1/179`; Contracts `73/73`; deterministic aggregate `319 + N_aw + N_ax + N_ay` passed plus the one established live-config skip.
4. **Builds and gates.** Both WinForms x64 Release targets `0W/0E`; Avalonia `75/75` with only established AVLN3001; web `315/315` plus `18/18`; `eng\ci\Invoke-LhmGates.ps1 -All` passes all included gates with no gate definition or runner change; release fixture `114/114`.
5. **Golden.** data.json golden remains blob `05113704acc6fefeb4128004b3f523d876fbcec4`, SHA-256 `BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.
6. **Control plane.** `plan validate plan-013` no errors (standing unassigned-inventory warnings only); `plan preflight --json` ready; analyzer high confidence; campaign-runtime suite `35/35`; `git diff --check` clean; render the plan doc twice and prove byte-identical output.
7. **Live separation (read-only).** Run the identity verifier, then the five read-only SND-HOST checks from `docs/README.md`: exact-path single process, root task `\LibreHardwareMonitor` Running with exact action/working directory, proxy-bypassed HTTP 200 for `/`, `/data.json`, `/metrics` on the configured bound address, and current-day CSV growth across two samples. No restarts, no writes.
8. **Cleanup.** `eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf`, review, then run it; `git clean -ndX` afterwards lists only `data/tasks.json` and `data/analysis-cache.json`.

If any check fails, stop and report; do not patch another agent's files.

### Part 2 — Campaign truth (single writer)

1. `live-tracker.md`: add the Plan-013 section with one row per agent using their returned row text plus your own verification/close row.
2. `docs/campaign-history.md`: fill the plan-013 detail table criterion-by-criterion with `met`/`open` and criterion-specific evidence; update the summary row (plan status `executed`, ledger state `implemented`, exact Met/Open/Waived counts). Never `accepted`. Note the inline execution deviation as plan-012 did.
3. `docs/architecture/refactor-roadmap.md`: mark the four Phase 5 items complete with `(Plan-013, <date>)` tags; update the checkpoint queue (A1 still person-only and open; plan-014 next and still gated behind explicit maintainer authorization).
4. `docs/campaign-backlog.md`: remove the plan-013 section; set current position (last campaign plan-013 `implemented`, next campaign `plan-014` — gated, do not start without separate approval, next agent letter `ba`); keep the plan-014 section and its gate text intact.
5. `docs/README.md`: update current-state/source-map text so the new seams are discoverable.
6. `docs/features/feature-hardware-lifecycle-seams.md`: new feature doc recording the registry boundary, the NVIDIA and storage collaborator splits, the pinned quirks, and what remains in `Computer`/`NvidiaGroup`/`StorageGroup`.
7. `.codex/skills/project.toml`: leave byte-identical unless a module or smart-test mapping genuinely fails to cover the new files; if a change is required, make it in this same close and re-run the stale-reference and suite-boundary gates.
8. Re-run the ledger/control-plane tests after every truth edit (`python -m unittest scripts.task_runtime.test_campaign_history` and the full `python -m unittest discover -s scripts -p "test_*.py"`).

## Exit Criteria

- Every Part-1 check passes with recorded evidence; every plan-013 criterion is `met` with criterion-specific evidence in the ledger.
- Truth surfaces agree with each other and with the plan contract; the ledger regression suite and full campaign-runtime suite pass after your edits.
- Plan lifecycle stays `executed`; ledger state is `implemented` with all 12 criteria met, never `accepted`; A1 remains open and person-only; plan-014 remains gated.
- Guarded cleanup done; final `git status --short` shows only intended files; `git clean -ndX` lists only the two ignored data files.

## Constraints

- You own no product source. A defect in AW/AX/AY work is reported, not patched.
- Do not hand-edit the rendered campaign plan doc; mutate `data/plans/plan-013.json` only through the sanctioned render procedure and prove idempotence.
- Do not commit acceptance, push, merge, deploy, promote, create a candidate, or touch live state.
- Keep `docs/campaign-history.md` CRLF line endings and its table shapes exactly parseable.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
python -m unittest discover -s scripts -p "test_*.py"
python scripts\task_manager.py plan validate plan-013
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\eng\Clear-LhmRepositoryBuildOutputs.ps1
git diff --check
git clean -ndX
git status --short
```

## Do NOT

- Do not write `accepted`, claim the attended A1 smoke, start or register plan-014, or relax its gate.
- Do not modify AW/AX/AY files, existing tests, gates, the runner, or any live/operational path.
- Do not push.

## Post-completion

Commit the close with `docs(campaign): close plan-013 hardware lifecycle seams`. Return the close commit SHA(s), every verification result with exact counts, the filled ledger counts, final `git status --short` and `git clean -ndX` output, remaining risks, and the next unregistered gated campaign.
