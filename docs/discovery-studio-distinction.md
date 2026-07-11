# Discovery — Studio Visual Distinction

**Goal:** Continue the dashboard work, make Studio look different from Standard,
and add more customization.
**Date:** 2026-07-11
**Status:** complete
**Recommended next:** direct implementation in the existing Studio feature slice

---

## Questions

1. Is the current Studio work ready to extend?
2. How is Studio already separated from Standard?
3. Which preferences already persist?
4. What additions create the most distinction with the smallest blast radius?
5. Which checks protect the change?

---

## Findings

### Q1: Is the current Studio work ready to extend?

**Answer:** Yes. The uncommitted slice has a complete feature spec and recorded
verification, with no open blocker.

**Evidence:**
- `docs/feature-web-dashboard-studio-view.md:79` — acceptance is explicit.
- `docs/feature-web-dashboard-studio-view.md:107` — verification is logged.

**Implications:** Extend the existing spec and WIP rather than starting another
route, branch, or dashboard implementation.

### Q2: How is Studio already separated from Standard?

**Answer:** The views have separate root markup and render paths, but share the
global masthead, typography tokens, telemetry model, and theme.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:49` — Standard root.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:77` — Studio root.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1008` — render branch.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:376` — Studio-only body styling.

**Implications:** Studio-only data attributes and CSS can change its visual
language without risking Standard or the telemetry contract.

### Q3: Which preferences already persist?

**Answer:** Accent, density, focus-card count, and Systems/Network visibility.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:160` — defaults.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:195` — normalization.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:85` — controls.

**Implications:** New settings should use the same normalized `sq.dashboard.v1`
path and reset behavior.

### Q4: What additions create the most distinction with the smallest blast radius?

**Answer:** Use warm Studio-only accents, replace the grid canvas with layered
Strata, add atmosphere opacity, a focus layout selector, and a sparkline toggle.
Make the default Studio use an editorial system type treatment and an asymmetric
spotlight deck. Keep alternate canvas treatments CSS-driven.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:418` — Studio layout seam.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:487` — focus-layout seam.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1247` — sparkline render seam.

**Implications:** No API, route, sensor, or server change is required.

### Q5: Which checks protect the change?

**Answer:** State normalization tests, embedded-control assertions, the web
selftest, .NET tests/builds, and live desktop/narrow visual checks.

**Evidence:**
- `webtests/console.tests.js:142` — schema tests.
- `webtests/selftest.node.js:12` — embedded markup checks.
- `docs/feature-web-dashboard-studio-view.md:71` — verification commands.

**Implications:** Visual distinction still needs browser proof; source tests alone
cannot establish it.

---

## Cross-Cutting Analysis

### Constraints

- Keep `viewTheme` stored as `standard | cardTruth` for compatibility.
- Keep Studio read-only and on the root route.
- Preserve Standard markup and behavior.
- The optional `http://127.0.0.1:8799/` reference was unavailable during this
  pass, so no layout or assets were copied from it.

### Risks

| Risk | Likelihood | Impact | Notes |
|---|---|---|---|
| Preset contrast regresses in light mode | M | M | Check every canvas in both themes. |
| Spotlight creates narrow overflow | M | M | Collapse to one column under 640px. |
| Telemetry saves drop new preferences | L | M | Extend merge-preservation coverage. |

### Open Questions

All questions answered.

---

## Recommendation

This is a small extension of the current feature slice. Update the Studio spec,
implement directly, and re-run the existing verification stack plus live visual QA.
