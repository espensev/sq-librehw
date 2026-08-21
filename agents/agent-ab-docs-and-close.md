# Agent Task — Docs and Close (Plan-006, Agent AB)

**Scope:** Update every current document that names the retired test-project path, close
the campaign in the roadmap and backlog, add the Plan-006 history ledger row with
criterion-specific evidence, and write all Plan-006 tracker rows. **You are the single
`live-tracker.md` writer for Plan-006.**

**Depends on:** y, z, aa — use their result payloads as your evidence source.

---

## Path-claim updates (verified locations at campaign start)

1. `AGENTS.md:97` — baseline command block: the `dotnet test` line becomes the slnf form
   (`dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`).
2. `AGENTS.md:100-104` — the golden-master paragraph: the deletion path becomes
   `LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json`, and
   the targeted re-run project becomes the Contracts csproj. Keep the "after a version
   bump" conditioning — the procedure is not blanket permission to regenerate.
3. `docs/README.md:367` (individual-commands block) — slnf form; add the three per-suite
   csproj commands as the focused variants. If the current-state section names the test
   project, state the four-suite layout in one or two sentences; do not write an essay.
4. `docs/features/feature-host-operator-utilities.md:212` — the targeted
   `--filter FullyQualifiedName~DataJsonGoldenTests` command now targets the Contracts
   csproj.
5. **Maintainer amendment (2026-08-01):** the extended stale-reference gate surfaced
   nine more feature docs with flat old test paths, now in your ownership:
   feature-avalonia-fixture-sensor-explorer, feature-local-release-system,
   feature-memory-ui-reliability, feature-native-ui-modernization,
   feature-sensor-workspace, feature-standard-context-layouts, feature-thermal-trends,
   feature-upstream-sync-2026-07-25, feature-web-dashboard-studio-view (all under
   `docs/features/`). Update each flat `LibreHardwareMonitor.Tests/<file>` claim to the
   file's current suite path (Library/Application/Contracts per the agent-y membership
   tables) and each old csproj test command to the slnf form. These are pointer updates
   to current claims — including inside verification-log sections, where the referenced
   file's identity is unchanged; do not alter any recorded result, count, or date.
   `eng/ci/tests/Test-NoStaleReferences.ps1` must exit 0 when you are done.

## Roadmap — `docs/architecture/refactor-roadmap.md`

- Phase 3: mark the split items done with one-line evidence (library/application split,
  external-contract isolation, attended-outside-CI-by-construction), each tagged
  `(Plan-006, 2026-07-31)`. **Leave the characterization-tests item open** — it is
  plan-007 and explicitly non-optional.
- Phase 3 status line: update from "not started" to reflect the partial completion.
- "Next campaign" checkpoint section: queue becomes A1, A2, then plan-007
  (characterization tests before extraction).

## Backlog — `docs/campaign-backlog.md`

- Delete the completed plan-006 section (the record lives in history + Git).
- Current position: phases complete unchanged (Phase 3 is partial until plan-007), last
  campaign `plan-006` ledger state `implemented`, next campaign `plan-007`, next agent
  letter `ac`, blocking note updated (A1/A2 still person-only).
- Keep the standing acceptance actions A1 and A2 verbatim.

## History ledger — `docs/campaign-history.md`

- Add the Plan-006 ledger row: plan status `executed`, ledger state `implemented`, with
  Met/Open/Waived counts derived from the criterion table, evidence column pointing at
  `docs/campaign-plan-006-verification-suite-boundaries.md`. **Fix the plan-004/005 row
  malformation if it is mechanical (the plan-005 row currently carries plan-004's evidence
  cell as a trailing column); if the fix is not purely mechanical, report it instead.**
- Add the `## plan-006 - Verification suite boundaries` section: two-sentence framing
  (never `accepted`; person-only), then the criterion table. **Criterion text must be
  copied verbatim from `data/plans/plan-006.json` `plan_elements.exit_criteria`** —
  `scripts/task_runtime/test_campaign_history.py` fails on any disagreement. Evidence per
  criterion comes from the y/z/aa payloads and must be criterion-specific, in the style of
  the plan-003/004/005 sections.
- Do not touch the waiver table (no waivers this campaign).

## Tracker — `live-tracker.md`

Append the Plan-006 section following the existing pattern, including the single-writer
statement ("Agent AB is the only writer of this file for Plan-006; agents y, z, and aa
returned their row text in their result payloads"), then one row per agent (SUITESPLIT-001,
SUITEWIRE-001, SUITEVERIFY-001, and your own SUITECLOSE-001) using the row text from the
payloads, edited only for table hygiene.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s scripts -p "test_*.py"      # campaign-history regression must pass
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
python scripts\task_manager.py plan preflight --json        # still ready
git diff --check
```

## Exit Criteria

- `AGENTS.md`, `docs/README.md`, and `docs/features/feature-host-operator-utilities.md`
  carry the slnf / Contracts paths; no current document references the retired csproj.
- Roadmap Phase 3 split items are closed with `(Plan-006, 2026-07-31)` tags, the
  characterization item stays open, and the checkpoint queue lists A1, A2, then plan-007.
- The backlog's plan-006 section is removed and the current position advances to plan-007
  with next agent letter `ac`.
- `docs/campaign-history.md` has the Plan-006 ledger row and a criterion table copied
  verbatim from the plan JSON, with criterion-specific evidence, and
  `python -m unittest discover -s scripts -p "test_*.py"` passes.
- `live-tracker.md` carries the Plan-006 section with the single-writer statement and the
  four agent rows.
- The stale-reference gate passes, `git diff --check` is clean, and preflight is ready.

## Do NOT

- Hand-edit `docs/campaign-plan-006-verification-suite-boundaries.md` (rendered artifact).
- Edit completed agent specs (`agents/*.md`) — immutable campaign evidence.
- Write `accepted` or `closed` anywhere; both are person-only states.
- Edit files outside your ownership (AGENTS.md, docs/README.md,
  docs/features/feature-host-operator-utilities.md, docs/campaign-history.md,
  docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, live-tracker.md).
- Run `python scripts/task_manager.py merge`.

## Result payload

Return: your tracker row (SUITECLOSE-001), the ledger row and criterion states as written,
the roadmap/backlog deltas, the unittest and stale-reference results, and anything you had
to leave open.
