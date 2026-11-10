# Review - Pane zoom continuation

**Date:** 2026-11-10
**Surface:** working tree over `f793f738`, including the resumed wheel/scope draft
**Spec source:** `docs/features/feature-graph-pane-controls.md`, confirmed revision
**Standards sources:** `AGENTS.md`, `docs/README.md`, upstream divergence ledger
**Verdict:** PASS WITH NOTES - source findings corrected; installed acceptance remains open

## Findings

- **Medium / regression:** `UI/PlotPanel.cs` guards only its own `GetSensorAt`
  caller. OxyPlot controller mouse-down hit-testing calls the replacement
  series directly before its first update. Plain, Ctrl and Shift left-click
  each throw `ArgumentNullException` from null `ActualPoints`.
  `Tracker_AfterSeriesReplacementBeforePaintSkipsUnpreparedLinesAndRecovers`
  reproduced all three failures. **Resolved:** the typed `PlotLineSeries`
  override now guards all three callers; tracking resumes after render.
- **Low / standards:** the feature status and documentation map still describe
  this draft as unimplemented, and the divergence row still advertises the
  removed rectangle binding. **Resolved:** source status, acceptance evidence,
  input bindings and divergence rows now describe this revision separately
  from historical runtime evidence.

## Verification

- The two previous hosted runs, `42737670234` and `42737673215`, completed
  successfully for `f793f738`; both remote tips still matched that commit.
- The new tracker regression ran against the resumed draft: 3 failed, 0 passed,
  all with the same null-source exception. Evidence is `tracker-red.trx` under
  the workload-root `LibreHW/audits/2026-11-10-pane-zoom-continuation` directory.
- Graph tests: 155 passed. Full suite: Application 389 passed / one established
  opt-in skip, Library 193 passed, Contracts 124 passed. Both Release frameworks
  built with zero warnings/errors. Upstream-divergence ledger: 15 passed,
  zero failed. `git diff --check` passed.
- Twelve hidden-form fixture captures show both scope labels without clipping
  in light/dark themes at 144 device DPI and three font sizes (100/150/200%).
  These are source-rendering evidence, not installed UI or monitor-switch tests.

## Coverage notes

- Deep review: both changed UI files, both changed graph test files, confirmed
  feature contract, input bindings, menu scope and settings behavior.
- Documentation review: `AGENTS.md`, `docs/README.md`, feature status and
  verification, both divergence rows and this report; no new machine literals.
- Existing unrelated audit and test-result files are preserved and excluded
  from this delivery. No installed runtime or hardware acceptance is claimed.

## Open questions

- Live gesture, real monitor-DPI and theme acceptance remain separate from
  deterministic controller and rendering verification.
