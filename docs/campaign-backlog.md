# Campaign Backlog

**Status:** active
**Updated:** 2026-07-31
**Read first:** `docs/campaign-playbook.md` for how to run any of these,
`docs/refactor-roadmap.md` for why each phase exists

This is the sequenced queue. `docs/refactor-roadmap.md` holds the *phase
contract* — invariants, prohibitions, exit gates. This holds the *executable
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
| Last campaign | `plan-002`, ledger state `implemented`, all 13 criteria met |
| Next campaign | `plan-003` |
| Next agent letter | `j` |
| Blocking nothing | the working tree needs committing before `plan-003` registers |

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

**Do this before `plan-003`.** The move touches the same three projects, and if
the smoke fails afterwards you will not know whether the move or the original
code caused it. Not absolutely blocking, but the sequencing is cheap and the
ambiguity is not.

A second, smaller question sits in the same place: plan-001 criterion 1 requires
verification *before* the implementation agents launched, and the record shows
it happened afterwards. It is currently `met` with the deviation stated. Confirm
or correct that while you are there.

### A2 — Plan-002 acceptance

Every criterion is met and the ledger state is `implemented`. Moving it to
`accepted` is a person's decision. Plan-002 wrote the rule that forbids
automation from doing it, so it cannot do it to itself.

---

## plan-003 — Move the Avalonia fixture explorer into `experiments/`

**Phase:** 2, fork-only taxonomy
**Risk:** medium — pure path change, but it touches the central project graph
**Status:** ready to register

### Goal

The Avalonia explorer is fixture-only and non-shipping, but it currently sits at
the repository root beside the inherited product projects, where it reads as a
peer of them. Move it behind an `experiments/` boundary so the layout states its
status, and prove the move changed nothing by keeping the 75-test gate green and
both package inventories free of spike entries.

This is also the first real exercise of the plan-002 payoff: because
`eng/ci/Invoke-LhmGates.ps1` reads gate commands from `.codex/skills/project.toml`
at run time rather than hard-coding them, **the runner needs no edit at all**.
Only the configuration moves. If the runner turns out to need changing, that is
a defect in the runner, not in this campaign.

### Entry conditions

- A1 performed or explicitly waived.
- Working tree committed and clean; `plan preflight` ready.
- Full sweep green immediately before starting, so a later failure is
  attributable.

### Target layout

```
experiments/avalonia-fixture-explorer/
  LibreHardwareMonitor.Avalonia.Spike.slnx
  LibreHardwareMonitor.Avalonia.Spike/
  LibreHardwareMonitor.Avalonia.Spike.Core/
  LibreHardwareMonitor.Avalonia.Spike.Tests/
  Test-AvaloniaSpike.ps1
```

**Decision, already taken: move directories only. Do not rename projects or
assemblies.** The release isolation check greps package *contents* for
`Avalonia` and `spike` strings, not source paths — verified: no script under
`ops/release/` mentions either. Keeping assembly names therefore keeps that
check working unchanged and preserves continuity with plan-001's evidence. A
rename is a separate, later decision with its own justification.

Use `git mv` so history follows the files.

### Exact impact surface

Verified by reference sweep. **Must change:**

| File | Why |
|---|---|
| `LibreHardwareMonitor.Avalonia.Spike.slnx` | three `<Project Path=…>` entries |
| `LibreHardwareMonitor.Avalonia.Spike/*.csproj` | project reference to `.Core` |
| `LibreHardwareMonitor.Avalonia.Spike.Tests/*.csproj` | project references to `.Core` and the app |
| `.codex/skills/project.toml` | `avalonia-core`, `avalonia-app`, `avalonia-tests` modules; two smart-test mappings; `[build-gate.avalonia-spike]` build and test commands; the `Directory.Packages.props`/`.slnx` conflict zone; `[smart-test.cross-cutting]` |
| `scripts/Test-AvaloniaSpike.ps1` | internal path resolution, and the script itself moves |
| `scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1` | the six Avalonia `bin`/`obj` entries |
| `docs/README.md` | source map |
| `docs/feature-avalonia-fixture-sensor-explorer.md` | every path claim |
| `live-tracker.md` | campaign rows |

**Must NOT change** — these are immutable campaign evidence, and rewriting them
falsifies the record of what was done:

- `agents/agent-a-…` through `agent-d-…`, and `agent-i-…`
- `docs/campaign-plan-001-*.md` and `docs/campaign-plan-002-*.md` — both are
  rendered from their plan JSON

**Open decision:** `docs/discovery-librehw-structural-audit.md` describes the
current structure and will go stale. The roadmap says to retire it once its
findings are folded into durable contracts. Either update its path claims or
retire it — but decide explicitly, do not leave it half-true.

### Agent roster

Five agents, disjoint ownership.

| Letter | Name | Owns | Deps |
|---|---|---|---|
| `j` | `move-spike-projects` | the three project directories, the `.slnx`, and their `.csproj` files | — |
| `k` | `rewire-spike-config` | `.codex/skills/project.toml`, `scripts/Test-AvaloniaSpike.ps1` (moving it), `scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1` | `j` |
| `l` | `stale-reference-gate` | `eng/ci/tests/Test-NoStaleReferences.ps1` | `j`, `k` |
| `m` | `spike-move-docs` | `docs/README.md`, `docs/feature-avalonia-fixture-sensor-explorer.md`, `docs/discovery-librehw-structural-audit.md` | `j`, `k` |
| `n` | `spike-move-close` | `live-tracker.md`, `docs/refactor-roadmap.md`, `docs/campaign-backlog.md` | `j`, `k`, `l`, `m` |

Agent `l` is the durable payoff. `Test-NoStaleReferences.ps1` becomes a
permanent `eng/ci` gate asserting that no tracked configuration or current
document references a path that no longer exists — with an explicit allow-list
for completed agent specs and rendered campaign documents. Every later
taxonomy campaign then inherits it. Write it so the allow-list is data, not
scattered conditionals.

### Exit criteria

1. The three projects, the `.slnx`, and the test runner live under
   `experiments/avalonia-fixture-explorer/`, moved with `git mv` so
   `git log --follow` resolves through the move.
2. `dotnet build` of the moved `.slnx` succeeds in Release.
3. The Avalonia gate passes 75 of 75, unchanged in count.
4. `eng/ci/Invoke-LhmGates.ps1 -All` passes 8 of 8 **with no edit to the
   runner** — configuration-only change proven.
5. Both WinForms x64 Release targets and the .NET suite pass; `git diff --stat`
   shows no change under the inherited product roots.
6. `Test-NoStaleReferences.ps1` passes and is registered in
   `[build-gate.ci-gates]`'s discovery, i.e. `Test-LhmCiGates.ps1` reports three
   scripts.
7. A release candidate built from the moved source still contains zero Avalonia
   or spike entries in both package inventories.
8. No product source, live runtime, scheduled task, release store, rollback
   packet, or log archive changed.
9. `plan preflight` ready with zero errors; `git clean -ndX` back to the two
   ignored data files.

### Risks

| Risk | Mitigation |
|---|---|
| A relative project reference silently resolves to the old location on one framework only | Build the `.slnx` clean from a deleted `bin`/`obj` state, not incrementally |
| `project.toml` and the moved paths drift, so a gate silently stops running | Agent `l`'s stale-reference gate, plus `-List` showing all gates still classified |
| The `Directory.Packages.props`/`.slnx` conflict zone is edited by two agents | `j` owns the `.slnx`, `k` owns `project.toml`; the zone entry itself is `k`'s |
| Cleanup script keeps stale paths and silently stops cleaning | `k` owns both the move and the cleanup list, in one change |
| History is lost by delete-and-recreate | `git mv` only; exit criterion 1 asserts `--follow` |

---

## plan-004 — Separate candidate creation from peer deployment

**Phase:** 2, fork-only taxonomy
**Risk:** high — touches fail-closed SND-DESK surfaces
**Entry:** `plan-003` landed and accepted

### Goal

`ops/release` (host-neutral candidate creation), `ops/local-release` +
`scripts/local-release` (SND-DESK-only deployment), and one repository
maintenance tool are currently interleaved across two top-level directories.
The names do not say which is safe to run here. Split them so the path states
the blast radius.

### Target layout

```
ops/candidate/          <- from ops/release/           (host-neutral, non-deploying)
ops/deploy/snd-desk/    <- from ops/local-release/ + scripts/local-release/*deploy*
ops/log-management/     <- unchanged
eng/Clear-LhmRepositoryBuildOutputs.ps1  <- from scripts/local-release/
```

That last move matters and is easy to miss: `Clear-LhmRepositoryBuildOutputs.ps1`
lives under `scripts/local-release/` but is **repository maintenance, not
deployment**. It is the tool the playbook tells everyone to run after a gate
sweep. It does not belong behind a peer-deployment boundary.

### Known hazards

- The SND-DESK production paths **fail closed to `snd-desk`**. That behavior is
  a contract, not an implementation detail. Every fail-closed guard must still
  fail closed after the move, proven by the peer-safe fixture on this host.
- `LHM_RELEASE_ROOT` is a user-level environment binding pointing at the
  external release store. Source moves must not touch it, and nothing in this
  campaign may write to that store.
- `ops/log-management` is **installed** on SND-HOST in a separate stable runtime
  with a SYSTEM scheduled task. The installed copy is not the source copy. Do
  not move the installed runtime; this campaign is source-only.
- Four `[build-gate.*]` entries name these paths, plus four smart-test mappings
  and the `candidate-ops` / `snd-desk-deploy-ops` modules.
- `eng/ci/Invoke-LhmGates.ps1`'s deny-list matches on command *content*
  (`New-LhmRelease`, `Publish-LibreHardwareMonitor`, `Install-`, …), not paths,
  so it survives the move. Confirm that rather than assuming it.

### Sketch roster

`move-candidate-ops` → `move-deploy-ops` → `relocate-maintenance-tool` →
`rewire-config-and-gates` → `verify-fail-closed` → `docs-and-close`.

The fail-closed verification deserves its own agent. It is the one property that
a path move could plausibly break in a way no build catches.

---

## plan-005 — Documentation and engineering taxonomy

**Phase:** 2, fork-only taxonomy
**Risk:** medium — wide reference surface, no behavior
**Entry:** `plan-004` landed

### Goal

Group the ~20 flat files in `docs/` under `architecture/`, `features/`,
`operations/`, and `campaigns/`, and consolidate the remaining engineering entry
points under `eng/build` and `eng/test` beside the existing `eng/ci`.

### Known hazards

- `[docs-sync.tier1]` and `[docs-sync.tier2]` glob these paths directly.
- `AGENTS.md`'s source-of-truth map and `docs/README.md`'s source map both
  enumerate most of the tree by hand.
- Rendered campaign documents move but must not be hand-edited — the render
  target is `plan["plan_doc"]` inside each plan JSON, so **the JSON must be
  updated and re-rendered**, not the file moved underneath the tooling. This is
  the subtle one; get it wrong and the next plan mutation recreates the file at
  the old path.
- `plan_doc_path()` in `scripts/task_manager.py` computes the default location.
  If the target layout disagrees with it, either the config or the expectation
  has to change — decide which before starting.
- Completed agent specs and rendered plan documents are historical. Moving them
  is fine; rewriting their contents is not.

Agent `l`'s stale-reference gate from `plan-003` carries most of the
verification load here. If that gate is good, this campaign is mostly mechanical.

---

## plan-006 — Verification suite boundaries

**Phase:** 3
**Risk:** high — first campaign to touch a product project file
**Entry:** `plan-005` landed; Phase 2 exit gate met

### Goal

`LibreHardwareMonitor.Tests` currently mixes library behavior, application
behavior, external-contract golden masters, and anything hardware-dependent.
Split it so each future seam maps to a focused deterministic suite, and so
hardware-dependent and attended tests are explicitly outside deterministic CI.

### Boundaries to establish

1. library-only behavior (`LibreHardwareMonitorLib`);
2. application behavior (`LibreHardwareMonitor.Windows.Forms`);
3. external contracts — `data.json`, HTTP routes, Prometheus, CSV, web assets;
4. hardware-dependent and attended, excluded from CI by construction rather
   than by convention.

### Known hazards

- `DataJsonGoldenTests` embeds the assembly version in `data.golden.json`.
  Regenerating it is a documented procedure in `AGENTS.md` — follow it, review
  the diff, and do not regenerate casually to make a red test green.
- The `data.json` shape, order, and IDs are an external downstream contract.
  This campaign restructures *tests*, never the payload.
- Splitting a test project changes the `[build-gate.winforms-net10]` test
  command and every `[smart-test.mappings]` entry pointing into
  `LibreHardwareMonitor.Tests/`.
- One documented opt-in skip exists in the current 258-test run. Preserve it as
  a skip; do not let a restructure silently drop it.

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

Each of those is a non-negotiable in `docs/refactor-roadmap.md`. A campaign that
needs one of them relaxed is not a campaign — it is a new architectural
decision, and it needs a spec and a maintainer decision first.
