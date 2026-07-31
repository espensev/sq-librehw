# Campaign Backlog

**Status:** active
**Updated:** 2026-07-31
**Read first:** `docs/campaign-playbook.md` for how to run any of these,
`docs/architecture/refactor-roadmap.md` for why each phase exists

This is the sequenced queue. `docs/architecture/refactor-roadmap.md` holds the *phase
contract* ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â invariants, prohibitions, exit gates. This holds the *executable
order*: what to run next, what must be true before starting it, and what each
campaign owns.

## How to use this file

- Campaigns run **one at a time, in order**. Each is planned from the state the
  previous one left behind, which is why later entries are outlines rather than
  full specs. Do not pre-register them.
- Every campaign starts from a **clean, committed tree** with `plan preflight`
  reporting ready. Registering from a dirty tree is a standing prohibition.
- Detail level is deliberate. `plan-003` is spec-complete and ready to register.
  `plan-004` through `plan-006` are scoped with known impact. `plan-007` onward
  are outlines whose shape depends on results you cannot see yet.
- When a campaign lands, delete its section here and leave the record in
  `docs/campaign-history.md` and Git history.

## Current position

| | |
|---|---|
| Phases complete | 0 (baseline and ambiguity removal), 1 (campaign and verification control plane) |
| Last campaign | `plan-005`, documentation taxonomy, ledger state `implemented` |
| Next campaign | `plan-006` |
| Next agent letter | `y` |
| Blocking nothing | A1 and A2 are person-only; the Get-FileHash defect keeps the configured `snd-desk-local-release-fixture` gate red (shared with Plan-003/004) |

---

## Standing acceptance actions

These are not campaigns. They are person-only actions that no automation may
perform, and both are recorded in `docs/campaign-history.md`.

### A1 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Plan-001 attended normal-user smoke

The only open criterion in the repository. A person launches the Avalonia
fixture explorer as a normal user, exercises the loaded, empty, loading, and
rejection states plus keyboard navigation, and records the result.

Three possible outcomes, all legitimate:

1. it passes ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ criterion 9 moves to `met` with the evidence;
2. it fails ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ the finding becomes a bugfix, and plan-001 stays `implemented`;
3. it is waived ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ a waiver record with all five required fields.

**Do this before `plan-003`.** The move touches the same three projects, and if
the smoke fails afterwards you will not know whether the move or the original
code caused it. Not absolutely blocking, but the sequencing is cheap and the
ambiguity is not.

A second, smaller question sits in the same place: plan-001 criterion 1 requires
verification *before* the implementation agents launched, and the record shows
it happened afterwards. It is currently `met` with the deviation stated. Confirm
or correct that while you are there.

### A2 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Plan-002 acceptance

Every criterion is met and the ledger state is `implemented`. Moving it to
`accepted` is a person's decision. Plan-002 wrote the rule that forbids
automation from doing it, so it cannot do it to itself.

---

## plan-006 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Verification suite boundaries

**Phase:** 3
**Risk:** high ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â first campaign to touch a product project file
**Entry:** `plan-005` landed; Phase 2 exit gate met

### Goal

`LibreHardwareMonitor.Tests` currently mixes library behavior, application
behavior, external-contract golden masters, and anything hardware-dependent.
Split it so each future seam maps to a focused deterministic suite, and so
hardware-dependent and attended tests are explicitly outside deterministic CI.

### Boundaries to establish

1. library-only behavior (`LibreHardwareMonitorLib`);
2. application behavior (`LibreHardwareMonitor.Windows.Forms`);
3. external contracts ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â `data.json`, HTTP routes, Prometheus, CSV, web assets;
4. hardware-dependent and attended, excluded from CI by construction rather
   than by convention.

### Known hazards

- `DataJsonGoldenTests` embeds the assembly version in `data.golden.json`.
  Regenerating it is a documented procedure in `AGENTS.md` ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â follow it, review
  the diff, and do not regenerate casually to make a red test green.
- The `data.json` shape, order, and IDs are an external downstream contract.
  This campaign restructures *tests*, never the payload.
- Splitting a test project changes the `[build-gate.winforms-net10]` test
  command and every `[smart-test.mappings]` entry pointing into
  `LibreHardwareMonitor.Tests/`.
- One documented opt-in skip exists in the current 258-test run. Preserve it as
  a skip; do not let a restructure silently drop it.

---

## plan-007 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Characterization tests before extraction

**Phase:** 3
**Risk:** medium
**Entry:** `plan-006` landed

The safety net Phase 4 depends on. Before any seam is extracted, pin the current
behavior of hardware lifetime, ordered option/reset, settings projection, and
shutdown coordination with characterization tests ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â tests that assert what the
code *does*, not what it should do.

Extraction without this is a rewrite with extra steps. Treat this campaign as
non-optional even though the roadmap lists it inside Phase 3.

---

## plan-008 ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ plan-012 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Application and adapter seams

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
| `plan-012` | WinForms presentation adapters ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â tree, plot, tray, gadget | canonical node order, scrollbar hit targets, UI Automation bridge |

Each must keep both framework targets green and must not duplicate ownership.
`plan-008` is the natural first: the snapshot contract already exists in
prototype form in the Avalonia spike's `SensorSnapshot`, which was designed
against this exact payload.

---

## plan-013 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Hardware lifecycle seams

**Phase:** 5
**Risk:** high
**Entry:** Phase 4 complete

Explicit group and registry lifecycle boundaries around `Computer`; separated
discovery, update, and close behavior in the NVIDIA and storage groups; reuse of
the immutable snapshot rather than exposing mutable hardware trees to new hosts.
Upstream mergeability and hardware quirks are preserved with characterization
tests. WinForms remains the sole hardware owner throughout.

---

## plan-014 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Runtime and data authority

**Phase:** 6 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â **gated, do not start without separate approval**
**Entry:** everything above, plus explicit maintainer authorization

The only campaign that touches live paths. It requires a complete consumer
manifest covering 16 scheduled-task bindings, four Start Menu shortcuts, SQ
shims, environment bindings, Scribe and log-management ownership, firewall
ownership, and rollback; a staged candidate without cutover; and attended
acceptance.

There is still no `librehw.runtime.json` and no explicit
`LIBREHARDWAREMONITOR_DATA_ROOT`. Configuration and the active CSV remain
co-located with the live executable, which is exactly why the live directory
must never be raw-moved.

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
needs one of them relaxed is not a campaign ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â it is a new architectural
decision, and it needs a spec and a maintainer decision first.
