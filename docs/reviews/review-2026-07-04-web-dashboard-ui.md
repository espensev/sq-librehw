# Review - Web Dashboard UI

**Date:** 2026-07-04
**Surface:** `origin/master...HEAD` branch review, with dirty working-tree docs noted separately
**Spec source:** `docs/feature-web-dashboard-card-truth.md`, `docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md`
**Standards sources:** `AGENTS.md`
**Verdict:** FAIL

## Findings

### High

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:378` - Power range truth is still only scaffolded, so power cards can keep rendering guessed ceilings as authoritative-looking UI.
  Evidence: `SQ.derivedPowerLimit` returns `null`, while `SQ.rangeFor` falls through to `source: 'peak'` from `rawMax` / motion / `observedMax` at `console.js:392-394`. The visible card renderer then prints only `/ ${rr.hi}` at `console.js:636`, with no `estimated`, `peak`, `derived`, or override provenance. The accepted spec requires no unlabeled invented ceilings and explicitly requires RTX 5090 power to show a real/derived/override limit or a marked estimate (`docs/feature-web-dashboard-card-truth.md:85-91`, `docs/feature-web-dashboard-card-truth.md:131-132`). A live 1440px screenshot of `http://localhost:8085/` during this review showed CPU power `/ 500` and GPU power `/ 1000` as bare suffixes.
  Impact: This is the core trust issue the dashboard work was meant to fix. The UI still implies a hardware max where it only has an observed/session peak.
  Recommendation: Finish the range renderer contract before polishing visuals: add source-aware ceiling markup/tooltips, implement or defer derived NVIDIA power with explicit labels, and make peak estimates visibly approximate. Add a regression test for rendered ceiling text, not just `SQ.rangeFor` return values.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:20` - Normal customization is still centered on the side drawer, not card/row surfaces.
  Evidence: The masthead still exposes `Customize`, the drawer DOM remains at `index.html:47-79`, drawer CSS remains at `console.css:188-207`, and runtime handlers still open/close/populate it at `console.js:827-844` and `console.js:897-902`. The accepted spec says no right pane/side drawer for normal dashboard work and requires card/row expansion plus a compact masthead popover only for hidden/offscreen search (`docs/feature-web-dashboard-card-truth.md:25-27`, `docs/feature-web-dashboard-card-truth.md:99`, `docs/feature-web-dashboard-card-truth.md:138`).
  Impact: The current interaction model still feels like a control panel bolted onto the dashboard rather than a modern card-first monitoring app. Alias, max override, range provenance, raw sensor detail, and move controls are not where the operator is looking.
  Recommendation: Do not call this UI pass complete until card/row expansion reaches action parity, hidden/offscreen search moves to a compact masthead popover, and the drawer button/DOM/CSS/handlers are removed.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:740` - Hardware panels are still grouped by display text, so duplicate-name devices remain merged.
  Evidence: `buildPanelItems` groups sensors with `byHw.get(s.hw)` at `console.js:741-742`, then derives the panel key from that grouped display name path at `console.js:744`. The spec requires panels to group by `hwid`, duplicate display names to get suffixes, and three same-name NVMe drives to render as three panels (`docs/feature-web-dashboard-card-truth.md:97`, `docs/feature-web-dashboard-card-truth.md:134`). The desktop screenshot during this review showed a single `KINGSTON SKC3000D2048G` panel containing repeated `Temperature` and `Used Space` rows.
  Impact: This breaks scanability and identity. Operators cannot reliably tell which same-model drive or duplicate GPU a row belongs to.
  Recommendation: Re-key panel grouping, collapse state, and panel order to stable `HardwareId`; add duplicate-name fixture tests covering three same-name NVMe devices and two GPUs.

### Medium

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:260` - Card action controls still occupy an absolute overlay that can collide with status chips and type icons.
  Evidence: `.cell-ctl` is `position:absolute` at `top:10px;right:11px` (`console.css:260-261`), and on hoverless devices it is always displayed (`console.css:266`). The card header still lays out chip and icon independently in `.k` / `.k2` (`console.js:643-644`). The spec requires a reserved trailing control gutter and explicitly says controls must never paint over chip/icon (`docs/feature-web-dashboard-card-truth.md:105`, `docs/feature-web-dashboard-card-truth.md:140`).
  Impact: On narrow cards and touch devices the UI can become visually incoherent and hard to operate. This is also why the existing cockpit styling feels less polished than it could.
  Recommendation: Make the card header a grid with an owned action column; keep controls in-flow for hover, focus, and touch states; truncate chip/name text inside reserved tracks.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:417` - Hero selection still collapses GPU identity and excludes the iGPU policy in the spec.
  Evidence: `SQ.pickHero` builds a single `gpu` list, then uses `find` for one GPU temp, memory junction, load, and power card at `console.js:426-431`; it never iterates GPU hardware IDs and never adds iGPU hero cards. The spec requires distinct GPU panels/heroes, short device labels when multiple GPUs exist, and iGPU temp+power visibility (`docs/feature-web-dashboard-card-truth.md:20`, `docs/feature-web-dashboard-card-truth.md:97`, `docs/feature-web-dashboard-card-truth.md:134`).
  Impact: Multi-GPU systems remain visually underrepresented; the dashboard can look clean while silently hiding a device class the operator asked to see.
  Recommendation: Drive hero selection from hardware groups keyed by `hwid`, then apply cap/trim rules after per-device cards are selected.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:234` - The masthead controls clip in a 390px viewport despite the mobile media query.
  Evidence: A live 390x1200 screenshot of `http://localhost:8085/` showed the right-column `Graphs` and `Customize` buttons clipped off the viewport. The mobile CSS switches `.controls` to a two-column grid at `console.css:234-239`, but `body` hides horizontal overflow (`console.css:44`), so the user gets clipped controls instead of a usable layout. The spec requires no overlap or clipping from 320px to 4K (`docs/feature-web-dashboard-card-truth.md:140`) and the corrective plan repeats this as an exit criterion (`docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md:30`).
  Impact: Narrow windows/mobile screenshots do not meet the operator-ready bar; key controls can become partially unreachable.
  Recommendation: Make the masthead controls a single-column or auto-fit grid below a safe breakpoint, constrain the range input explicitly, and verify at 320/390/640px with screenshots.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:122` - Persisted observed peaks are normalized and read, but never updated from live samples.
  Evidence: `observedMax` exists in default/normalized state (`console.js:122-146`) and `SQ.rangeFor` consumes it (`console.js:392`), but the only code references are normalization, tests, and reads. The spec requires observed peaks to be updated throttled in dashboard state (`docs/feature-web-dashboard-card-truth.md:88`).
  Impact: Estimated ceilings are not stable across reloads unless manually seeded by previous state. This undercuts the "highest this browser has seen" behavior described in the spec.
  Recommendation: Add a `mergeObservedPeaks` or equivalent update path during render, throttle localStorage writes, and test that power/clock estimates ratchet up but do not shrink below the current raw value.

### Low

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:4` - The visual direction is distinctive but still reads more like a dense cockpit skin than a polished, user-friendly monitoring app.
  Evidence: The current palette and typography are deterministic (`console.css:4-16`), and the desktop screenshot is visually coherent at a glance. However, the same view mixes large colored rails, type-colored values, status chips, unlabeled ceiling suffixes, per-card icons, and hidden hover controls without a clear priority hierarchy. The corrective plan calls out deterministic status/type color treatment and modern customization polish as exit criteria (`docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md:14`, `docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md:31`, `docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md:64`).
  Impact: This is not a correctness blocker by itself, but it affects the "look and feel" goal: the UI is attractive in a technical way, but not yet calm or self-explanatory.
  Recommendation: After the truth and interaction fixes, reduce competing signals: reserve status color for rails/chips, type color for icons/value accents, and use muted styling for estimates/unknowns.

## Verification

- `git diff --stat origin/master...HEAD` - pass; reviewed 10 changed files.
- `git diff --name-status origin/master...HEAD` - pass; branch surface includes `console.js`, `console.css`, specs/plans, and `webtests/console.tests.js`.
- `git status --short --branch` - pass; branch is `master...origin/master [ahead 6]`; working tree has separate dirty docs/plans.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node webtests\selftest.node.js` - pass, `SELFTEST PASS 100/100`.
- `Invoke-WebRequest http://localhost:8085/` - pass, status 200.
- `Invoke-WebRequest http://localhost:8085/data.json` - pass, status 200, live payload available.
- Chrome headless screenshots - pass; captured desktop `1440x1100` and narrow `390x1200` from `http://localhost:8085/`.

## Coverage Notes

- Files reviewed deeply: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`, `webtests/console.tests.js`, `docs/feature-web-dashboard-card-truth.md`, `docs/superpowers/plans/2026-07-04-web-dashboard-visible-correctness-plan.md`.
- Files reviewed for standards/spec linkage: `AGENTS.md`, `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md`, `docs/superpowers/plans/2026-07-04-web-dashboard-implementation-campaign.md`, `docs/superpowers/specs/2026-07-04-dashboard-templates.md`.
- Working-tree docs edits were not treated as product implementation. They were considered as current planning context only.
- I did not run the full `dotnet build` or `dotnet test` gates because this was a UI review pass and the main defects are visible/spec mismatches already confirmed by source and browser smoke. The JS syntax/model tests did run.

## Open Questions

- Should the next implementation pass finish the existing corrective plan on `master`, or should it cut `feat/web-visible-correctness` first to keep the dirty planning docs separate?
- Should the current cockpit visual language remain the brand direction, with hierarchy cleanup only, or should the polish pass move toward a calmer operational dashboard style?
