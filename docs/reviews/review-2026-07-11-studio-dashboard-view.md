# Review — Studio Dashboard View

**Date:** 2026-07-11
**Surface:** working tree (unstaged) — 7 modified files + 2 new docs; untracked `scripts/` cache excluded from review scope
**Spec source:** `docs/feature-web-dashboard-studio-view.md` (with `docs/discovery-studio-distinction.md`)
**Standards sources:** `AGENTS.md`; repo memory (data.json external contract, no-client-derivation, live-visual-gate lesson)
**Verdict:** PASS — no High/Medium findings; 3 Low housekeeping notes.

## Findings

### High
- None.

### Medium
- None.

### Low
- **[standards] `scripts/**/__pycache__/*.pyc` (26 untracked files)** — Python bytecode cache sitting in the worktree.
  AGENTS.md §4: "Keep generated output, logs, and local scratch artifacts out of source." `.gitignore` has no
  `__pycache__/` / `*.pyc` rule, so these are one `git add -A` away from being committed. **Not part of the feature
  diff.** Recommendation: add ignore rules; never commit. (Handled during cleanup below.)
- **[standards] `AGENTS.md` §2 points at `docs/ai-guide.md` ("start here"), which no longer exists** — removed by
  `d74124e docs: collapse documentation to live summary`. Pre-existing staleness, not caused by this work.
  Recommendation: refresh AGENTS.md §2 or restore the target.
- **[standards] `docs/feature-web-dashboard-studio-view.md` is not linked from `docs/feature-workflow.md`'s draft list.**
  Mitigated: `docs/README.md` (the current "live summary" index) was updated to reference the spec, so traceability is
  effectively met. Optional: add to `feature-workflow.md` if that index is still authoritative.

## Spec acceptance ↔ diff evidence

- **Standard unchanged (Acceptance #1):** `index.html` wraps the prior `<main>` in `#standardView`; no Standard node was
  edited. Studio is a separate `<main id="studioView" hidden>`. `render()` branches only on `viewTheme==='cardTruth'`
  (`console.js:1008`); the Standard path (`renderPinnedCards/PFD/Placard/Panels`) is unchanged.
- **Backward compat (Behavior/Compat):** stored value stays `standard | cardTruth`; option relabeled Card Truth→Studio,
  selector View→Dashboard; unknown `viewTheme` → `standard` via `cleanViewTheme`.
- **Warm-only palette (Acceptance #3):** `--studio-*` tokens are coral/rose/amber/plum/gold/sand; `--studio-load`
  overrides the green load token; selftest asserts *absence* of `cyan/blue/green/emerald` options.
- **State model (Acceptance #4, Compat):** 9 `studioX` fields added to `defaultDashboardState` + `normalizeDashboardState`
  with per-field clean fns; opacity clamps 0–100 with 55 fallback; focusCount whitelisted `[4,6,8,12]`; `show*` default
  true unless exactly `false`. Independent normalization, save/load round-trip, and **telemetry-merge preservation** are
  all covered in `console.tests.js`.
- **Read-only (Non-goals, Acceptance #5):** no `fetch(` or `action=Set` added (git-diff grep = NONE_ADDED); star-remove
  uses local `setPrimaryCardState`; "Choose sensors" opens the existing local popover.
- **Honest states (Behavior, Acceptance #6):** unavailable readings render `—`/`unavailable` (`raw==null` guards); empty
  focus deck shows a labelled empty-state action; stale status is preserved on cached rerenders — the fresh indicator is
  only stamped by `tick()`'s `render(data, true)`, never by a settings-driven `rerender()`.
- **Accessibility (Acceptance #6):** `.sr-only` transition-gated live region (announces only on alert-signature change,
  and not on first paint); `aria-label="Dashboard"`; explicit `<label for="studioCanvasOpacity">`; `aria-valuetext` on
  the range; focus is restored to the next action / Choose-sensors when a focused card is removed.
- **Scoping / no leak (Acceptance #2, #7):** Studio visual language is gated under `:root[data-view-theme="cardTruth"]`;
  the shared header/masthead overrides for strata/plain require that prefix (selftest guard asserts
  `data-view-theme="cardTruth"][data-studio-canvas="strata"] header`); `#standardView[hidden]{display:none!important}`.
  `--studio-*` vars are consumed only by `.studio-*` nodes inside the hidden view.
- **Retirement contract intact (Acceptance #8):** `WebDashboardRetirementTests` still asserts `/dash/cardtruth` absent +
  data.json/metrics present; the server route is untouched, so `/dash/cardtruth[/]` stays 404.

## Internals confirmed by source read (not only tests)

- `rerender()` is null-guarded (`if (state.lastData)`) and drag-guarded → switching to Studio before first telemetry
  cannot crash (`console.js:903`).
- Exactly two `render()` call sites: `tick()`→`render(data, true)` (fresh) and `rerender()`→`render(state.lastData)`
  (not fresh). Freshness gating is complete; no other caller lost its indicator update.
- `state.panelItems` is (re)built by the same `SQ.buildPanelItems` in both paths; Studio-only readers cannot observe a
  Standard-stale value, and the panel/adapter movers are Standard-only DOM.

## Verification (commands run this pass)

- `node --check ...console.js` — **PASS**
- `node webtests/selftest.node.js` — **SELFTEST PASS 252/252**
- `dotnet test ...Tests.csproj -p:Platform=x64` — **Passed! 64/64** (0 failed), incl. `DataJsonGoldenTests` (external
  data.json contract) and the updated `WebDashboardRetirementTests`. Build stamp `0.9.6+541b05b-dirty` confirms the
  pinned AssemblyVersion.
- `git diff console.js | grep 'action=Set|fetch('` — **NONE_ADDED**
- Release `net10.0-windows` / `net472` builds not re-run this pass; the spec verification log records them PASS
  (2026-07-11). The `dotnet test` run above already compiled Tests + Forms + Lib for net10.0-windows x64 clean.

## Coverage notes

- **Deep-reviewed:** `console.js` (full diff + render/rerender/commit/save internals), `index.html`, `console.css`
  (scoping + responsive + palette), `WebDashboardRetirementTests.cs`, `console.tests.js`, `selftest.node.js`,
  `docs/README.md`, both new docs.
- **Not independently exercised:** live browser visual QA (dark/light, 375px, reduced-motion, opacity drag, reset). The
  spec's own verification log claims this was done 2026-07-11. Per the repo lesson *"a GREEN measurement gate can still
  hide a visual regression,"* a final live look in both themes + narrow viewport remains the recommended gate before
  treating Studio as fully shipped — source/automated PASS does not by itself establish visual correctness.

## Open question

- **Landing shape:** recent convention is direct `feat(web):` commits to master (e.g. `58e240b feat(web): retire
  cardtruth preview route`); earlier dashboard phases used `--no-ff` merge commits (`0279333`). This review lands as
  direct commits to master. Do **not** rewrite the already-pushed merge commits.
