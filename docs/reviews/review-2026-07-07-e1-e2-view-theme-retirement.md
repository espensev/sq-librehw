# Review - E1/E2 View Theme and Preview Retirement

**Date:** 2026-07-07
**Surface:** working tree after E1/E2 implementation
**Spec source:** `docs/feature-web-dashboard-view-theme-retirement.md`
**Standards sources:** `AGENTS.md`, `docs/ai-guide.md`
**Review mode:** subagent-assisted, then controller re-audit
**Verdict:** PASS after fixes

## Findings

1. **Medium - route spec still described active preview-route gates after retirement.**
   - Fixed in `docs/feature-web-dashboard-versioned-routes.md` by separating the historical preview-route requirements from the current Phase E retirement contract and removing active preview JS/API smoke expectations.

2. **Low - webserver/dashboard discovery still implied an active preview static route.**
   - Fixed in `docs/discovery-webserver-dashboard-interaction.md` by recording the current dispatch order: product API routes, then stable `Web.*` assets, with no preview route before `/data.json`.

3. **Low - retirement lacked a focused regression test for route status.**
   - Fixed by replacing `HttpServerRouteTests` with `WebDashboardRetirementTests`, covering root `viewTheme`, no embedded `WebDash.cardtruth` resources, and `/dash/cardtruth` plus `/dash/cardtruth/` mapping only to missing stable resources.

## Verification

- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node webtests\selftest.node.js` - pass, 229/229.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` - pass, 44/44; existing `xUnit2020` analyzer warning in `DataJsonGoldenTests.cs:61`.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` - pass, 0 warnings/0 errors.
- Live smoke on Release app PID `74624` - pass: `/`, `/console.css`, `/console.js`, `/data.json`, and `/metrics` return 200; root contains `#viewTheme` and no `/dash/cardtruth/` link; `/dash/cardtruth/` and `/dash/cardtruth` return 404.
- `git diff --check` - pass; only normal CRLF conversion warnings were reported by other diff/status commands.

## User-Perspective Rules Applied

- Root `/` is the product dashboard; preview routes are temporary scaffolding and should disappear once accepted deltas are stable.
- A view selector may change presentation, but it must not fork routes, telemetry contracts, or local storage keys.
- Navigation should not advertise retired or diagnostic-only surfaces.
- State migration must be boring: unknown `viewTheme` values normalize to `standard`, and telemetry-only persistence must preserve operator preferences.
- Mobile masthead controls may wrap, but they must remain readable and avoid squeezing telemetry cards.

## Closeout

E1/E2 is ready to ship. The next queue item is F1-F3 context dashboards (Main/Gaming/Storage), which should remain separate from the E-phase `viewTheme` look selector.
