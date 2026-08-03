# Campaign Backlog

**Status:** active
**Updated:** 2026-08-03
**Read first:** `docs/campaign-playbook.md` for how to run any of these,
`docs/architecture/refactor-roadmap.md` for why each phase exists

This is the sequenced queue. `docs/architecture/refactor-roadmap.md` holds the *phase
contract* — invariants, prohibitions, exit gates. This holds the *executable
order*: what to run next, what must be true before starting it, and what each
campaign owns.

## How to use this file

- Campaigns run **one at a time, in order**. Each is planned from the state the
  previous one left behind, which is why later entries are outlines rather than
  full specs. Do not pre-register them.
- Every campaign starts from a **clean, committed tree** with `plan preflight`
  reporting ready. Registering from a dirty tree is a standing prohibition.
- Detail level is deliberate. `plan-011` onward are outlines whose shape
  depends on results you cannot see yet.
- When a campaign lands, delete its section here and leave the record in
  `docs/campaign-history.md` and Git history.

## Current position

| | |
|---|---|
| Phases complete | 0 (baseline and ambiguity removal), 1 (campaign and verification control plane), 3 (verification suite boundaries), 4 (application and adapter seams), 5 (hardware lifecycle seams); Phase 2 remains partially complete with general engineering grouping open |
| Last campaign | `plan-013`, hardware lifecycle seams, ledger state `implemented` |
| Next campaign | `plan-014` — **gated, do not start without separate approval** |
| Next agent letter | `ba` |
| Closing now | A1 is person-only and still open; A2 was accepted by the maintainer on 2026-08-01; Plan-013 is implemented with all 12 criteria met, Phase 5 is complete, and Plan-014 remains gated behind explicit maintainer authorization |

---

## Standing acceptance actions

These are not campaigns. They are person-only actions that no automation may
perform, and both are recorded in `docs/campaign-history.md`.

### A1 — Plan-001 attended normal-user smoke

The only open criterion in the repository. A person launches the Avalonia
fixture explorer as a normal user, exercises the loaded, empty, loading, and
rejection states plus keyboard navigation, and records the result.

Three possible outcomes, all legitimate:

1. it passes → criterion 9 moves to `met` with the evidence;
2. it fails → the finding becomes a bugfix, and plan-001 stays `implemented`;
3. it is waived → a waiver record with all five required fields.

Plan-003 landed before this attended smoke. Exercise the fixture at its current
`experiments/avalonia-fixture-explorer` path. If it fails, compare the failure
with Plan-001 and Plan-003 evidence before assigning it to the original work or
the path-only move.

A second, smaller question sits in the same place: plan-001 criterion 1 requires
verification *before* the implementation agents launched, and the record shows
it happened afterwards. It is currently `met` with the deviation stated. Confirm
or correct that while you are there.

A2 (Plan-002 acceptance) completed 2026-08-01: the maintainer accepted the
campaign and the ledger records it. A1 remains the only standing action.

---

## plan-014 — Runtime and data authority

**Phase:** 6 — **gated, do not start without separate approval**
**Entry:** everything above, plus explicit maintainer authorization

The maintainer explicitly reprioritized the physical workspace/path migration
on 2026-07-31. It completed out-of-band with a Libre-specific consumer
manifest, rollback packet, Git/artifact preservation, exact-path task/process
proof, HTTP checks, settings persistence, and CSV/archive growth. It did not
promote a candidate or change product bytes.

Plan-014 is now limited to an optional **data-root authority** change. There is
still no `librehw.runtime.json` and no explicit
`LIBREHARDWAREMONITOR_DATA_ROOT`; configuration and active CSV remain
co-located with the executable under `deployments\current`. Starting that
separate change still requires explicit maintainer authorization, a staged
candidate, rollback, and attended acceptance.

---

## What invalidates this backlog

Re-plan from the roadmap, rather than following this file, if any of these
change:

- the decision to keep inherited product roots at their current paths;
- WinForms as the sole hardware, process, and scheduled-task owner;
- the Avalonia explorer's fixture-only, non-shipping status;
- `ops/deploy` remaining SND-DESK-only and failing closed here;
- the `data.json` external contract.

Each of those is a non-negotiable in `docs/architecture/refactor-roadmap.md`. A campaign that
needs one of them relaxed is not a campaign — it is a new architectural
decision, and it needs a spec and a maintainer decision first.
