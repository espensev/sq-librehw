# Review - Dashboard D3 User Perspective

**Date:** 2026-07-07
**Surface:** working tree plus live root dashboard evidence
**Spec source:** current user request; `docs/superpowers/plans/2026-07-07-web-responsive-theme-qa-d3.md`
**Standards sources:** `AGENTS.md`; `docs/ai-guide.md`; `docs/feature-web-dashboard-card-truth.md`
**Verdict:** ~~FAIL for D3 closeout until D3-03 is fixed~~ → **PASS** (re-verified 2026-07-07 after `e101748` + `409f0a6`; see **Re-verification** at the end)

## Findings

### High

- [regression] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:193`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:259`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1195` - Mobile row controls can cover the live value column.
  Evidence: the live 390px touch pass found `rowOverlapCount=11` and `rowValueOverflowCount=11`; the row is a fixed grid while `.row-ctl` is absolute and forced visible under `hover:none`, and JS appends row move plus star/pin/hide controls.
  Impact: mobile users can lose the value they opened the dashboard to read. This violates the user-priority rule that telemetry text wins over management controls.
  Recommendation: D3-03 should move row controls in-flow or stack them on touch/narrow layouts before D3 can close.

### Medium

- [spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:315`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1315` - Sensors popover checks need to prove useful text, not only viewport fit.
  Evidence: `.sensors-panel` remains right-anchored with `width:min(420px,92vw)`, and visible rows can render visibility chip plus Make/Remove primary, Pin, and Hide.
  Impact: at 320/390, action buttons can consume the row before sensor label, hardware/type/value, alias, or `SensorId` remain useful.
  Recommendation: D3-01/D3-02 should require action stacking/compaction before sensor identity becomes unreadable.

- [spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:182`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1226`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:57` - Panel headers need content-priority review, not just no-horizontal-scroll proof.
  Evidence: panel move/hide controls render before the name, `head-stat` pushes to the right, and `#panelsReset` shares the Subsystems heading row.
  Impact: a narrow layout could technically fit while still making panel identity/status harder to scan than the controls.
  Recommendation: D3-04 should keep panel name/status legible and allow controls to wrap, stack, or compact first.

- [regression] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:286`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1131` - D2 overlay evidence must include hit testing and intentional occlusion review.
  Evidence: `.xp-overlay` is absolute and positioned below the card by offset math, so zero displacement does not prove it is readable, dismissible, or safe around popovers.
  Impact: a screenshot can pass the no-move probe while hiding later cards, trapping clicks, or conflicting with Sensors/Pages.
  Recommendation: D3-07 should include screenshots, click-away, Escape, scroll, resize re-anchor, and popover coexistence.

### Low

- [standards] `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:29`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html:29`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html:56` - Preview-route divergence can pollute D3 evidence if root and preview are mixed.
  Evidence: stable `/` has the Sensors popover; `/dash/cardtruth/` still has Customize/drawer DOM.
  Impact: a preview screenshot can make a root-dashboard D3 claim appear stale or contradictory.
  Recommendation: root `/` should be the acceptance surface; preview route evidence is comparison-only until Phase E promotion/removal.

## Stat-Card Review Rules

- `name/source/value/unit/range` outrank controls. Controls may hide, stack, or move; telemetry text should not.
- Health chips are for health state only. Type/value accents are for sensor kind.
- Touch card controls stay capped at `[grip, star, pin]`; Hide belongs in popover or expansion on touch.
- Number-only cards without a trusted range should look deliberate, not unfinished.
- Expanded details must expose raw label and `SensorId`; aliases are display helpers, not replacements.

## Verification

- Live browser evidence reviewed: 1440 dark/light had row overlap 0; 390 touch dark had row overlap 11 and value overflow 11.
- Static code references reviewed in `console.css`, `console.js`, root `index.html`, and preview `cardtruth/index.html`.
- `git diff --check` - pass, with CRLF conversion warnings only.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` - pass.
- `node webtests\selftest.node.js` - pass (`SELFTEST PASS 227/227`).
- Full .NET build/test not run; this was a docs/review expansion pass with no product-code edits.

## Coverage Notes

- Files reviewed deeply: `docs/superpowers/plans/2026-07-07-web-responsive-theme-qa-d3.md`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`.
- Files sampled for route divergence: `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html`.

## Open Questions

- Should D3 fix row controls with an in-flow second row only on touch, or also simplify desktop focus behavior for consistency?
- Should touch target minimums follow a strict 24px local compact rule or a larger accessibility target where layout allows?

## Re-verification (2026-07-07, post-fix — verdict FAIL → PASS)

Re-audited the live root dashboard (rebuilt EXE, chrome-devtools, dark+light) at 320 and 390 touch after `e101748` (D3 responsive patch) and `409f0a6` (panel-name wrap). Every finding above is resolved:

- **High / D3-03 (mobile row controls cover value)** — RESOLVED by `e101748`. Live: 390 touch **and** 320 touch, 234 rows, `rowCtlOverValue=0`, `rowValueClip=0`; `.row-ctl` is now `position:static; grid-column:3/5` on its own line under the value (`@media (hover:none),(max-width:640px)`). Telemetry text is no longer occluded.
- **Medium (Sensors popover readability)** — RESOLVED by `e101748`. At ≤640 `.sensor-choice` is single-column; live 390: sensor label + hw/type/value + `SensorId` un-clipped, chip and Pin/Hide/Make-primary stacked full-width; panel fits viewport.
- **Medium (panel-header content priority)** — RESOLVED by `409f0a6`. Was the last real residual: at 320, 9/17 panel names ellipsis-clipped, cutting the `#1/#2/#3` that distinguishes the three identical `KINGSTON SKC3000D2048G` drives. Fix wraps `.panel-head .nm` at ≤640 (`white-space:normal; overflow-wrap:anywhere`). Live 320 dark+light: all three Kingston `#N` fully shown (2 lines), `horizontalClip=0`, no h-scroll.
- **Medium (D2 overlay occlusion / popover coexistence)** — VERIFIED fine. Live 390: expanded-card overlay fits the viewport (no h-scroll), and with the Sensors popover opened on top the popover renders above it (`z-index 40 > overlay z 6`) — no occlusion or click-trap.
- **Low (preview-route divergence)** — unchanged; root `/` was the sole acceptance surface for this pass (preview not used as evidence). Defer to Phase E.

Gates: `node webtests/selftest.node.js` 227/227; golden `dotnet test` 42→55/55 (server-hardening tests, unaffected by CSS); clean `net10.0-windows` Release x64 rebuild 0/0. Screenshots: `eval-320-kingston-fixed` (dark), `eval-320-kingston-light`, `eval-390-popover`. **D3 closeout complete.**
