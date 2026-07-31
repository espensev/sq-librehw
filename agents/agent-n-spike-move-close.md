# Agent Task — Spike Move Close

**Scope:** Integrate results and close the campaign truth surfaces: add the
Plan-003 ledger row and criterion evidence in `docs/campaign-history.md` (without
automatic acceptance), record Plan-003 tracker rows in `live-tracker.md`, close
the roadmap item, and remove the completed Plan-003 section from the backlog.
Also record the maintainer's honest deferral of the plan-001 attended smoke.

**Depends on:** Agent J, Agent K, Agent L, Agent M

**Output files:** `docs/campaign-history.md`, `live-tracker.md`,
`docs/refactor-roadmap.md`, `docs/campaign-backlog.md`.

## Exit Criteria

- `docs/campaign-history.md` carries a Plan-003 `## Ledger` summary row and a
  `## plan-003` detail section whose criterion text matches
  `data/plans/plan-003.json` exit criteria verbatim, with state `implemented`
  (9 met, 0 open, 0 waived) and criterion-specific evidence. It is NOT marked
  `accepted`.
- `scripts/task_runtime/test_campaign_history.py` passes (one-row-per-plan,
  count match, transition rule).
- `live-tracker.md` carries the Plan-003 rows, written only by you.
- `docs/refactor-roadmap.md` marks the Phase 2 Avalonia move complete and
  advances the current position.
- `docs/campaign-backlog.md` removes the completed Plan-003 section.
- The plan-001 attended smoke (criterion 9) is recorded as honestly deferred by
  the maintainer, kept `open` (not met, not waived).

## Task

1. **`docs/campaign-history.md` — Plan-003 row:** add the summary row
   `plan-003 | Move the Avalonia Fixture Explorer into experiments/ | executed |
   implemented | 9 | 0 | 0 | docs/campaign-plan-003-move-the-avalonia-fixture.md`.
   Add a `## plan-003` detail section with a 9-row criterion table. Copy each
   criterion text **verbatim** from `data/plans/plan-003.json`
   `plan_elements.exit_criteria`; set each `met` with the concrete evidence
   (move + git log --follow; clean Release build; 75/75 gate; 8/8 gates; WinForms
   builds + empty product diff; stale-ref gate + three discovered scripts;
   candidate isolation inventories; unchanged live proof; preflight ready + clean
   -ndX).
2. **A1 deferral (honest):** under the plan-001 section, add a note that the
   maintainer deferred the attended normal-user smoke on 2026-07-31 so Plan-003
   could proceed; criterion 9 stays `open`. Do not convert it to `met` or add an
   incomplete waiver.
3. **`live-tracker.md`:** add one row per Plan-003 agent (j-n), matching the
   existing column set and ID convention (e.g. `AVMOVE-001`..`AVMOVE-005`).
4. **`docs/refactor-roadmap.md`:** mark the Phase 2 Avalonia taxonomy move
   complete and advance the current position/next-campaign pointer.
5. **`docs/campaign-backlog.md`:** remove the completed Plan-003 section; keep
   the durable record in Git/campaign history.

## Constraints

- Only you write `live-tracker.md` and `docs/campaign-history.md` for this
  campaign.
- Do not mark Plan-003 `accepted` or `closed` — that is person-only.
- Do not hand-edit rendered campaign docs; change the plan JSON and let the
  tooling render.
- Do not edit specs, scripts, project.toml, or the inherited product roots.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s scripts -p "test_*.py" -v
python scripts\task_manager.py plan validate plan-003
git diff --check
```

The campaign-history regression must stay green.

## Do NOT

- Do not run `git clean -fdX` or push.
- Do not invent acceptance evidence; record only what the gates proved.
