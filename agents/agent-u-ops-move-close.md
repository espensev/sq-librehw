# Agent Task — Ops Move Close

**Scope:** Integrate results and close the campaign truth surfaces: add the Plan-004
ledger row and criterion evidence in `docs/campaign-history.md` (without automatic
acceptance), record Plan-004 tracker rows in `live-tracker.md`, close the Phase 2
operations-taxonomy roadmap item, and remove the completed Plan-004 section from the
backlog.

**Depends on:** Agent O, Agent P, Agent R, Agent S, Agent T

**Output files:** `docs/campaign-history.md`, `live-tracker.md`,
`docs/refactor-roadmap.md`, `docs/campaign-backlog.md`.

## Exit Criteria

- `docs/campaign-history.md` carries a Plan-004 `## Ledger` summary row and a
  `## plan-004` detail section whose criterion text matches `data/plans/plan-004.json`
  exit criteria verbatim, with state `implemented` or `registered` (whichever the gates
  support) and criterion-specific evidence. It is NOT marked `accepted`.
- `scripts/task_runtime/test_campaign_history.py` passes (one-row-per-plan, count match,
  transition rule).
- `live-tracker.md` carries the Plan-004 rows, written only by you.
- `docs/refactor-roadmap.md` marks the Phase 2 operations-taxonomy item complete.
- `docs/campaign-backlog.md` removes the completed Plan-004 section.

## Task

1. **`docs/campaign-history.md` — Plan-004 row:** add the summary row
   `plan-004 | Separate candidate creation from peer deployment | executed | <state> |
   <met> | <open> | 0 | docs/campaign-plan-004-separate-candidate-creation-from.md`.
   Add a `## plan-004` detail section with a criterion table. Copy each criterion text
   **verbatim** from `data/plans/plan-004.json` `plan_elements.exit_criteria`. Set each
   criterion `met`/`open` honestly from the gate evidence each agent returned. Note
   explicitly that the `snd-desk-local-release-fixture` gate stays blocked by the
   pre-existing Get-FileHash defect (shared with Plan-003 criterion 4) and that the
   fail-closed proof was run under `pwsh`.
2. **`live-tracker.md`:** add one row per Plan-004 agent (o–u).
3. **`docs/refactor-roadmap.md`:** mark the Phase 2 "Separate candidate creation from
   peer-specific deployment semantics" item complete.
4. **`docs/campaign-backlog.md`:** remove the completed Plan-004 section.

## Constraints

- Only you write `live-tracker.md` and `docs/campaign-history.md` for this campaign.
- Do not mark Plan-004 `accepted` or `closed` — that is person-only.
- Do not hand-edit rendered campaign docs; change the plan JSON and let the tooling render.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s scripts -p "test_*.py" -v
python scripts\task_manager.py plan validate plan-004
git diff --check
```

The campaign-history regression must stay green. (Do not push. Do not invent acceptance
evidence; record only what the gates proved.)
