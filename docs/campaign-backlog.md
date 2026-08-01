# Campaign Backlog

**Status:** active
**Updated:** 2026-08-01
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
- Detail level is deliberate. `plan-007` onward are outlines whose shape
  depends on results you cannot see yet.
- When a campaign lands, delete its section here and leave the record in
  `docs/campaign-history.md` and Git history.

## Current position

| | |
|---|---|
| Phases complete | 0 (baseline and ambiguity removal), 1 (campaign and verification control plane) |
| Last campaign | `plan-006`, verification suite boundaries, ledger state `implemented` |
| Next campaign | `plan-007` |
| Next agent letter | `ac` |
| Blocking nothing | A1 is person-only and still open; A2 was accepted by the maintainer on 2026-08-01; every plan-006 criterion is met |

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

## plan-007 — Characterization tests before extraction

**Phase:** 3
**Risk:** medium
**Entry:** `plan-006` landed

The safety net Phase 4 depends on. Before any seam is extracted, pin the current
behavior of hardware lifetime, ordered option/reset, settings projection, and
shutdown coordination with characterization tests — tests that assert what the
code *does*, not what it should do.

Extraction without this is a rewrite with extra steps. Treat this campaign as
non-optional even though the roadmap lists it inside Phase 3.

---

## plan-008 … plan-012 — Application and adapter seams

**Phase:** 4
**Risk:** high
**Entry:** `plan-007` landed, with characterization coverage green

One seam per campaign, in this order. `MainForm` stays the composition root
until each extracted contract is characterized and accepted. No wholesale
rewrite.

| Plan | Seam | Preserves |
|---|---|---|
| `plan-008` | immutable sensor snapshot and `data.json` projection | payload shape, order, IDs, byte-compatibility where required |
| `plan-009` | HTTP listener and dispatch service | every route, the GET/POST mutation contract, cross-origin rejection |
| `plan-010` | application lifecycle, polling, option/reset, shutdown | ordered hardware-operation coordinator, transactional open/cleanup |
| `plan-011` | settings projection and persistence | ordered, atomic, backup-aware writes; stale-history compaction |
| `plan-012` | WinForms presentation adapters — tree, plot, tray, gadget | canonical node order, scrollbar hit targets, UI Automation bridge |

Each must keep both framework targets green and must not duplicate ownership.
`plan-008` is the natural first: the snapshot contract already exists in
prototype form in the Avalonia spike's `SensorSnapshot`, which was designed
against this exact payload.

---

## plan-013 — Hardware lifecycle seams

**Phase:** 5
**Risk:** high
**Entry:** Phase 4 complete

Explicit group and registry lifecycle boundaries around `Computer`; separated
discovery, update, and close behavior in the NVIDIA and storage groups; reuse of
the immutable snapshot rather than exposing mutable hardware trees to new hosts.
Upstream mergeability and hardware quirks are preserved with characterization
tests. WinForms remains the sole hardware owner throughout.

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
