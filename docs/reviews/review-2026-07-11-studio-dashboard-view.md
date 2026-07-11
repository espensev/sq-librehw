# Review - Studio Dashboard View

**Date:** 2026-07-11
**Surface:** working tree (unstaged) - 7 modified files + 2 new docs; untracked `scripts/` cache excluded from scope.
**Spec source:** `docs/feature-web-dashboard-studio-view.md` (+ `docs/discovery-studio-distinction.md`).
**Standards sources:** `AGENTS.md`; repo memory (data.json external contract, no-client-derivation, live-visual-gate).
**Verdict:** PASS - no High/Medium; 3 Low housekeeping notes.

## Findings

- High: none.
- Medium: none.
- Low [standards] `scripts/**/__pycache__/*.pyc` (26 untracked) - pyc cache in worktree; AGENTS.md §4 says keep generated output out of source; no `__pycache__/`/`*.pyc` ignore rule, so one `git add -A` from being committed. Not part of feature diff. Fix: add ignore rules (handled during cleanup).
- Low [standards] AGENTS.md §2 points at `docs/ai-guide.md` ("start here"), which no longer exists (removed by `d74124e`). Pre-existing staleness, not this work. Fix: refresh §2 or restore target.
- Low [standards] `feature-web-dashboard-studio-view.md` not linked from `feature-workflow.md` draft list. Mitigated: `docs/README.md` (live index) references the spec, so traceability effectively met. Optional: add to `feature-workflow.md` if still authoritative.

## Spec Acceptance <-> Diff Evidence

- Standard unchanged (Acc#1): `index.html` wraps prior `<main>` in `#standardView`; no Standard node edited; Studio is separate `<main id="studioView" hidden>`; `render()` branches only on `viewTheme==='cardTruth'` (`console.js:1008`); Standard path (`renderPinnedCards/PFD/Placard/Panels`) unchanged.
- Backward compat: stored value stays `standard | cardTruth`; option relabeled Card Truth->Studio; selector View->Dashboard; unknown `viewTheme` -> `standard` via `cleanViewTheme`.
- Warm-only palette (Acc#3): `--studio-*` = coral/rose/amber/plum/gold/sand; `--studio-load` overrides green load token; selftest asserts *absence* of cyan/blue/green/emerald options.
- State model (Acc#4): 9 `studioX` fields added to `defaultDashboardState` + `normalizeDashboardState` with per-field clean fns; opacity clamps 0-100 (55 fallback); focusCount whitelisted `[4,6,8,12]`; `show*` default true unless exactly `false`. Independent normalization, save/load round-trip, and telemetry-merge preservation all covered in `console.tests.js`.
- Read-only (Acc#5): no `fetch(` or `action=Set` added (git-diff grep NONE_ADDED); star-remove uses local `setPrimaryCardState`; "Choose sensors" opens existing local popover.
- Honest states (Acc#6): unavailable readings -> `—`/`unavailable` (`raw==null` guards); empty focus deck -> labelled empty-state action; stale status preserved on cached rerenders - fresh indicator only stamped by `tick()`'s `render(data, true)`, never by settings-driven `rerender()`.
- Accessibility (Acc#6): `.sr-only` transition-gated live region (announces only on alert-signature change, not first paint); `aria-label="Dashboard"`; explicit `<label for="studioCanvasOpacity">`; `aria-valuetext` on range; focus restored to next action / Choose-sensors when a focused card is removed.
- Scoping / no leak (Acc#2,#7): Studio visuals gated under `:root[data-view-theme="cardTruth"]`; shared header/masthead strata/plain overrides require that prefix (selftest guard asserts `...[data-studio-canvas="strata"] header`); `#standardView[hidden]{display:none!important}`; `--studio-*` consumed only by `.studio-*` nodes inside the hidden view.
- Retirement intact (Acc#8): `WebDashboardRetirementTests` still asserts `/dash/cardtruth` absent + data.json/metrics present; server route untouched -> `/dash/cardtruth[/]` stays 404.

## Internals Confirmed by Source Read (not only tests)

- `rerender()` null-guarded (`if (state.lastData)`) + drag-guarded -> switching to Studio before first telemetry cannot crash (`console.js:903`).
- Exactly two `render()` call sites: `tick()`->`render(data, true)` (fresh), `rerender()`->`render(state.lastData)` (not fresh); freshness gating complete; no other caller lost its indicator update.
- `state.panelItems` (re)built by the same `SQ.buildPanelItems` in both paths; Studio-only readers cannot observe a Standard-stale value; panel/adapter movers are Standard-only DOM.

## Verification

- `node --check ...console.js` - PASS.
- `node webtests/selftest.node.js` - SELFTEST PASS 252/252.
- `dotnet test ...Tests.csproj -p:Platform=x64` - Passed 64/64 (0 failed), incl. `DataJsonGoldenTests` (external data.json contract) + updated `WebDashboardRetirementTests`; build stamp `0.9.6+541b05b-dirty` confirms pinned AssemblyVersion.
- `git diff console.js | grep 'action=Set|fetch('` - NONE_ADDED.
- Release `net10.0-windows` / `net472` not re-run this pass; spec verification log records them PASS (2026-07-11); the `dotnet test` run already compiled Tests + Forms + Lib for net10.0-windows x64 clean.

## Coverage

- Deep-reviewed: `console.js` (full diff + render/rerender/commit/save internals), `index.html`, `console.css` (scoping + responsive + palette), `WebDashboardRetirementTests.cs`, `console.tests.js`, `selftest.node.js`, `docs/README.md`, both new docs.
- Not independently exercised: live browser visual QA (dark/light, 375px, reduced-motion, opacity drag, reset). Spec's verification log claims done 2026-07-11. Per repo lesson *"a GREEN measurement gate can still hide a visual regression,"* a final live look in both themes + narrow viewport remains the recommended gate before treating Studio as fully shipped - source/automated PASS does not by itself establish visual correctness.

## Open Question

- Landing shape: recent convention is direct `feat(web):` commits to master (e.g. `58e240b`); earlier dashboard phases used `--no-ff` merge commits (`0279333`). This review lands as direct commits. Do NOT rewrite already-pushed merge commits.
