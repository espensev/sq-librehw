# Review - Dashboard Menu and Gauge Correctness

**Date:** 2026-07-06  
**Surface:** working tree plus live `http://localhost:8085/` after rebuild/restart  
**Spec source:** user request, `docs/feature-web-dashboard-versioned-routes.md`, `docs/feature-web-dashboard-card-truth.md`  
**Standards sources:** `AGENTS.md`  
**Verdict:** PASS WITH NOTES

## Findings

### High

No high-severity findings for this narrow pass after the fix.

### Medium

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:20` - Broader v3 dashboard cleanup is still incomplete.
  Evidence: the stable page still carries the existing `Customize` drawer entry and drawer DOM. This pass only added a Pages menu and blocked inaccurate peak-derived gauges.
  Impact: the dashboard is improved for discoverability and gauge truth, but it is not yet the full card-first v3 interaction model.
  Recommendation: continue the v3 visible-correctness plan separately: replace drawer-centered normal actions with card/row/header actions, finish alias/override from the card surface, and verify narrow/mobile layouts.

### Low

- [axis: verification] Chrome headless DOM dumping was unavailable in this shell.
  Evidence: Chrome 0-exited with no dumped DOM output. Live HTTP checks still verified the served HTML/JS/CSS and route status, and JS/model tests cover the gauge guard.
  Impact: no visual screenshot was captured in this turn.
  Recommendation: use the interactive browser or a working Playwright install for the next visual layout pass.

## Verification

- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` - pass.
- `node webtests\selftest.node.js` - pass, `SELFTEST PASS 106/106`.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` - pass, 42/42 tests.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` - pass, 0 warnings, 0 errors.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` - pass, 0 warnings, 0 errors.
- Live smoke after restart - pass: `/`, `/console.js`, `/console.css`, `/dash/cardtruth/`, `/dash/cardtruth/console.js`, `/data.json`, and `/metrics` returned 200; `/dash/missing/` returned 404.

## Coverage Notes

- Files reviewed deeply: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`, preview dashboard assets under `Resources/WebDash/cardtruth`, and `webtests/*`.
- Existing route code/tests were exercised but not redesigned in this pass.

## Open Questions

- Which preview pages beyond `/dash/cardtruth/` should become real embedded routes next? The menu currently links only existing live pages plus read-only diagnostics.
