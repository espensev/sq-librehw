# LibreHardwareMonitor Structural Refactor Roadmap

**Status:** active
**Date:** 2026-07-31
**Scope:** repository structure, campaign control, fork-only organization, and
behavior-preserving seams
**Out of scope until separately approved:** live deployment relocation,
runtime/data migration, hardware ownership changes, and Avalonia promotion

This roadmap turns the structural audit into bounded campaigns. It is not
authority to move the live SND-HOST runtime or rename upstream-derived product
roots.

## Baseline decision

The repository is a long-lived fork that still needs low-friction upstream
integration. Therefore:

- keep `Aga.Controls`, `LibreHardwareMonitorLib`,
  `LibreHardwareMonitor.Windows.Forms`, and the solution at their inherited
  roots;
- organize fork-only experiments, operations, tests, documents, and campaign
  tooling around those roots;
- extract contracts before splitting high-coupling classes;
- keep source, release candidate, live runtime, rollback, and data/archive
  authority separate;
- make every live path change a later manifest-backed migration with rollback.

## Non-negotiable contracts

- WinForms remains the sole current hardware, process, and scheduled-task
  owner.
- `data.json` shape/order/IDs, Prometheus output, CSV behavior, settings, and
  hardware behavior do not change during structural phases.
- Both `net10.0-windows` and `net472` WinForms builds remain gates.
- The Avalonia fixture explorer remains non-elevated, fixture-only, and
  non-shipping until a new accepted specification says otherwise.
- `ops/local-release` is SND-DESK-only. It must fail closed on SND-HOST.
- No phase may treat a source build or candidate as a live promotion.

## Phase 0 — Baseline and ambiguity removal

**Status:** complete and committed

- [x] Verify SND-HOST identity before mutation.
- [x] Inventory source, live, candidate, rollback, log, archive, task,
      shortcut, environment, and service ownership.
- [x] Remove four clean completed worktrees after ancestry or patch-equivalence
      proof; preserve their branches.
- [x] Remove only the three verified-empty false Git markers.
- [x] Repair the stale local `vanilla` remote to the verified current checkout.
- [x] Reconcile Plan-001 to `partial` while preserving attended smoke as open.
- [x] Revalidate the untouched live process, task, HTTP endpoints, and CSV
      growth.

**Exit gate:** audit evidence is reviewable and no live consumer changed.

## Phase 1 — Campaign and verification control plane

**Status:** local recovery complete and committed; CI and the campaign-history
transition remain open

- [x] Pin `scripts/task_manager.py`, `scripts/task_runtime`, and
      `scripts/analysis` from the canonical Codex package.
- [x] Define repository paths, modules, conflict zones, docs-sync, smart-test,
      and build gates in `.codex/skills/project.toml`.
- [x] Ignore local execution state and analysis cache.
- [x] Make blocker states override stale success, keep manual exit criteria
      unevaluated without evidence, and add regression tests.
- [x] Keep generic campaign verification non-mutating; external candidate
      creation remains isolated in its named release gate.
- [x] Restore a ready planning preflight and high-confidence seven-project
      analysis.
- [x] Re-run all existing non-live regression/build fixtures and remove their
      generated repository output.
- [x] Review and commit the structural baseline.
- [ ] Add non-deploying CI for campaign-control tests, .NET tests, both WinForms
      targets, web tests, Avalonia fixture tests, and operations contract tests.
- [ ] Define an explicit campaign-history transition for Plan-001 after its
      attended gate is closed or waived.

**Exit gate:** a clean baseline commit passes all configured non-live gates and
CI cannot deploy or mutate the host.

## Phase 2 — Fork-only taxonomy

**Status:** not started

- Move the Avalonia fixture projects into a clearly named
  `experiments/avalonia-fixture-explorer` boundary.
- Separate candidate creation from peer-specific deployment semantics:
  `ops/candidate`, `ops/deploy/snd-desk`, and `ops/log-management`.
- Group current documents under architecture, features, operations, and
  campaigns without retaining completed point-in-time reviews.
- Move general engineering entry points toward `eng/build`, `eng/test`, and
  `eng/ci`.
- Update solutions, scripts, package isolation checks, docs, and test mappings
  in the same atomic change as each move.

**Do not move:** inherited product roots, the live runtime, release store,
rollback store, active logs, or installed operations tooling.

**Exit gate:** every moved path has zero stale current references, both
framework builds pass, the 75-test Avalonia gate passes, and release/log
contract tests pass.

## Phase 3 — Verification suite boundaries

**Status:** not started

- Split library-only behavior tests from WinForms/application tests.
- Isolate external contracts: `data.json`, HTTP routes, Prometheus, CSV, and
  web assets.
- Add characterization tests around lifecycle, settings projection, and
  reset/option ordering before extraction.
- Keep hardware-dependent and attended tests explicitly separate from
  deterministic CI.

**Exit gate:** each future seam maps to a focused deterministic suite, and
cross-cutting changes still trigger the full gate.

## Phase 4 — Application and adapter seams

**Status:** not started

Extract one seam per campaign, in this order:

1. immutable sensor snapshot and `data.json` projection;
2. HTTP listener/dispatch service around the preserved external contract;
3. application lifecycle, polling, option/reset, and shutdown coordination;
4. settings projection and persistence coordination;
5. WinForms presentation adapters for tree, plot, tray, and gadget surfaces.

`MainForm` remains the composition root until each extracted contract is
characterized and accepted. Avoid a wholesale rewrite.

**Exit gate:** external output is byte/semantics compatible where required,
both framework targets pass, and no ownership becomes duplicated.

## Phase 5 — Hardware lifecycle seams

**Status:** not started

- Introduce explicit group/registry lifecycle boundaries around `Computer`.
- Separate discovery/update/close behavior in the NVIDIA and storage groups.
- Reuse the immutable snapshot contract rather than exposing mutable hardware
  trees to new hosts.
- Preserve upstream mergeability and hardware quirks with characterization
  tests.

**Exit gate:** no regression in device discovery, polling, close/dispose,
settings, or downstream sensor identity.

## Phase 6 — Runtime/data packaging and optional path migration

**Status:** gated

- Choose one SND-HOST data-root authority: runtime manifest, explicit
  environment setting, or managed host manifest.
- Produce a complete consumer manifest covering the scheduled tasks, four
  Start Menu shortcuts, SQ shims, environment bindings, Scribe/log management,
  firewall ownership, and rollback.
- Stage a candidate without cutover, verify exact source and package isolation,
  then conduct attended acceptance.
- Migrate one authority boundary at a time with an immediate rollback packet.

**Exit gate:** exact-path process/task proof, `/`, `/data.json`, and `/metrics`
HTTP `200`, settings persistence, CSV/archive growth, and full consumer
rebinding all pass after cutover.

## Next campaign

Do not register Plan-002 from a dirty baseline. After Phase 0/1 changes are
reviewed and committed, make the first campaign one of:

1. non-deploying CI and campaign-history contracts; or
2. the isolated Avalonia experiment move with exact reference/test gates.

The CI/control-plane campaign is lower risk and should normally go first.
