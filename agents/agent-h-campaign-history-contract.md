# Agent Task — Campaign History Contract

**Scope:** Define the durable campaign-history transition contract and ledger
with regression coverage that stops open attended criteria from being closed
automatically.

**Depends on:** none

**Output files:** `docs/campaign-history.md`,
`docs/architecture/campaign-control-plane.md`,
`scripts/task_runtime/test_campaign_history.py`

## Exit Criteria

- `docs/campaign-history.md` exists as a tracked ledger with one row per plan
  file in `data/plans/` and a per-criterion detail section for each plan.
- The ledger uses exactly the states `registered`, `implemented`, `accepted`,
  and `closed`, and each criterion is exactly one of `met`, `open`, or
  `waived`.
- Plan-001 is recorded with criteria 1 through 8 `met` and criterion 9 `open`,
  and the campaign is **not** `accepted` or `closed`.
- `docs/architecture/campaign-control-plane.md` defines the transition
  contract, the waiver record fields, who may set each state, and the rule that
  automated verification may never write `accepted`.
- `scripts/task_runtime/test_campaign_history.py` passes under
  `python -m unittest discover -s scripts -p "test_*.py"` and fails on each
  violation listed in Part 3.
- The new overlay test is added to the overlay list and the blob-ID table in
  `docs/architecture/campaign-control-plane.md`, with its blob ID recorded
  after the file content is final.
- No canonical campaign-runtime file is modified and the pinned
  `scripts/task_manager.py` SHA-256 stays valid.

---

## Context — read before doing anything

1. `AGENTS.md` — task classification and baseline commands.
2. `docs/architecture/campaign-control-plane.md` — the file you are extending.
   Read all of it. The three truth layers, the overlay list, the blob-ID table,
   the safe refresh procedure, and the safety rules are all live contracts.
3. `.codex/skills/planning-contract.md` — the lifecycle statuses `draft`,
   `approved`, `rejected`, `executed`, and the legacy/backfill `partial`. Do
   not invent new plan statuses; the ledger states are a **separate** axis.
4. `scripts/task_runtime/verify.py` — the overlay rule that aggregate automated
   verification leaves every exit criterion `not_evaluated`. Your contract
   makes that rule's consequence durable.
5. `scripts/task_runtime/test_verify.py` and
   `scripts/task_runtime/test_execution.py` — the existing repository-owned
   overlay tests. Match their import style, naming, and structure.
6. `data/plans/plan-001.json` and `data/plans/plan-002.json` — the two plans
   the ledger must cover, including their `plan_elements.exit_criteria` arrays.
7. `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` and
   `live-tracker.md` — the existing plan-001 evidence you are recording, not
   re-deriving.

### The distinction this campaign exists to make durable

`executed` in this tooling means **"the tooling registered the agents and
generated the templates"**. It does not mean the campaign is finished. Plan-002
is `executed` from the moment it is registered, with every one of its criteria
still open. Any rule keyed on plan status would therefore be wrong on day one.
That is why the ledger carries its own acceptance axis.

---

## Task

### Part 1 — `docs/campaign-history.md`

Create the durable ledger. It is the tracked home for per-criterion acceptance
evidence, which today lives only in the ignored `data/tasks.json`.

Open with a short statement that the ledger records human acceptance, that
`data/tasks.json` is local execution state and can never substitute for it, and
that a row here is not authority to deploy anything.

A summary table with one row per plan file in `data/plans/`:

| Plan | Campaign | Plan status | Ledger state | Criteria met | Open | Waived | Evidence |
|---|---|---|---|---|---|---|---|

Then one detail section per plan, `## plan-NNN — <campaign title>`, containing a
criterion table:

| # | Criterion | State | Evidence |
|---|---|---|---|

State each criterion's text verbatim from that plan's
`plan_elements.exit_criteria`, in order, so the ledger and the plan JSON cannot
drift silently.

Seed both plans:

- **plan-001**, ledger state `implemented`. Criteria 1 through 8 are `met`;
  cite the durable evidence already recorded — the automated implementation,
  merge, regression, and exact-source candidate work completed at commit
  `9674680`, candidate `0.9.6-20260730-210528120-b466837` passing dual-shell
  promotable and current-source verification, both package inventories
  containing zero Avalonia or spike entries, and the 75/75 Avalonia runner.
  Criterion 9, the attended normal-user smoke and evidence record, is `open`.
  The plan status column reads `partial`.
- **plan-002**, ledger state `registered`, every criterion `open`, evidence
  column referencing this campaign's plan document.

Add a `## Waiver records` section. It is empty at creation; state that
explicitly rather than leaving the heading bare.

### Part 2 — Transition contract in `docs/architecture/campaign-control-plane.md`

Add a `## Campaign history and acceptance` section, placed after
`## Authority` so it sits with the truth-layer discussion. Extend the authority
table with the ledger as a fourth layer: `docs/campaign-history.md`, authority
"human acceptance evidence", persistence "tracked".

Define:

- **The two axes.** Plan lifecycle status answers "has the tooling registered
  and run this campaign". Ledger state answers "has a person accepted the
  result". They move independently, and neither implies the other.
- **The four ledger states**, with who may set each:
  - `registered` — the plan is approved and its agents exist. Set by whoever
    registers the campaign.
  - `implemented` — every automated exit criterion has recorded evidence and
    all agent work is merged. Set by the campaign owner after the gates pass.
  - `accepted` — every criterion is `met`, or `waived` with a complete waiver
    record. Set only by a person. **Automated verification must never write
    `accepted`.**
  - `closed` — accepted, and its campaign document, tracker rows, and agent
    specs have been reconciled. Set only by a person.
- **The transition rule.** A campaign may not be recorded `accepted` or
  `closed` while any criterion is `open`, or `waived` without a complete waiver
  record. Moving to `implemented` with open attended criteria is correct and
  expected; that is precisely plan-001's situation.
- **Waiver record fields**, all five required: criterion number, waived by,
  date as an absolute ISO date, reason, accepted risk, and the condition that
  would reopen it. A waiver with any field missing is treated as `open`.
- **The plan-001 application.** Its automated execution manifest is `verified`
  while its plan contract is `partial` and its ledger state is `implemented`.
  All three are simultaneously correct. Closing criterion 9 requires either the
  attended smoke or a waiver record; this campaign does neither.

Then update the provenance sections in the same file:

- Add `scripts/task_runtime/test_campaign_history.py` to the repository-owned
  overlay list with a one-line description.
- Add its normalization-aware Git blob ID to the blob-ID table. Compute it
  **after** the file content is final:

  ```powershell
  git hash-object scripts\task_runtime\test_campaign_history.py
  ```

  If you edit the file again afterwards, recompute and update the table. A
  stale blob ID is a provenance defect.
- State that this is a repository-only addition, that no canonical file changed,
  and that the count of repository-only overlay tests is now four.

### Part 3 — `scripts/task_runtime/test_campaign_history.py`

A `unittest` module discovered by the existing campaign-control gate. It needs
no `project.toml` change. Resolve the repository root from `__file__`, two
levels up.

Parse `docs/campaign-history.md` with the standard library only — no PyYAML, no
Markdown package. A small Markdown table reader over pipe-delimited lines is
sufficient and is what the existing overlay tests would do.

Required tests:

1. The ledger file exists and is non-empty. A missing ledger is a failure, not
   a skip.
2. The summary table has exactly one row per `data/plans/plan-*.json`, and
   every plan file has a row. Both directions.
3. Every ledger state is one of `registered`, `implemented`, `accepted`,
   `closed`.
4. Every criterion state is one of `met`, `open`, `waived`.
5. For each plan, the criterion count in its detail section equals
   `len(plan_elements["exit_criteria"])` in its JSON, and each criterion's text
   matches the JSON entry at the same index. Normalize whitespace and Markdown
   pipe escaping before comparing.
6. No campaign whose ledger state is `accepted` or `closed` has a criterion in
   state `open`.
7. Every criterion in state `waived` has a waiver record in the waiver section
   carrying all five required fields.
8. The summary row's met, open, and waived counts equal the counts in that
   plan's detail table.
9. Plan-001 specifically: ledger state is not `accepted` and not `closed`, and
   its criterion 9 is `open`. This is a targeted regression against the known
   live case, so it names plan-001 explicitly. If plan-001 is later legitimately
   accepted, this test is the deliberate place where that decision must be made
   consciously.
10. `data/plans/plan-001.json` still has `status` `partial` and an empty
    `executed_at`.

Each test carries a docstring naming the rule it enforces. Prove tests 6, 7,
and 9 actually bite by temporarily corrupting a copy of the ledger in a
`tempfile` directory and asserting the parser and rule functions reject it —
structure the module so the rule checks take the parsed ledger as an argument
and can be exercised against a fixture, not only against the real file.

---

## Constraints

- Do not modify any canonical campaign-runtime file: `scripts/task_manager.py`,
  `scripts/analysis/*.py`, `.codex/skills/planning-contract.md`,
  `.codex/skills/project.toml.template`, or any
  `scripts/task_runtime/*.py` other than your new test module.
- Do not change `data/plans/plan-001.json`, its campaign document, or its
  status. Recording that criterion 9 is open is in scope; closing or waiving it
  is not.
- Do not change `data/plans/plan-002.json`.
- Do not edit `live-tracker.md` or `docs/README.md`. Agent I owns them and adds
  the source-map link to your ledger.
- Do not add a new CLI command, argument, or `[build-gate.*]` entry. The
  existing campaign-control gate already discovers your test.
- Do not add a third-party Python package.

---

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest scripts.task_runtime.test_campaign_history -v
python -m unittest discover -s scripts -p "test_*.py" -v
python scripts\task_manager.py plan preflight --json
git hash-object scripts\task_runtime\test_campaign_history.py
python -c "import json;d=json.load(open('data/plans/plan-001.json',encoding='utf-8'));print(d['status'], repr(d['executed_at']))"
git diff --stat
git clean -ndX
```

The plan-001 check must print `partial ''`. `git diff --stat` must show only
your three owned files. Confirm the blob ID printed by `git hash-object`
matches the value you recorded in the blob-ID table.

Set `PYTHONDONTWRITEBYTECODE` before the first command. Every one of your
verification steps imports `scripts/task_runtime`, and without it the run leaves
`__pycache__` directories in the repository. `git clean -ndX` must end listing
only `data/tasks.json` and `data/analysis-cache.json`.

---

## Do NOT

- Do not mark plan-001 `accepted`, `closed`, or `executed`.
- Do not treat the automated `verified` execution manifest as attended
  acceptance.
- Do not let a missing ledger file downgrade a test to a skip.
- Do not record a blob ID you have not recomputed against the final file.
- Do not add a rule that closes a criterion automatically; the whole point of
  the contract is that a person does it.
- Do not invent plan lifecycle statuses beyond the four in the planning
  contract plus the legacy `partial`.
