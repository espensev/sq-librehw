# Review - Web Dashboard Slice 1 and Slice 3 Plan

**Date:** 2026-07-06
**Surface:** `feat/web-dashboard-v3-card-first` working tree after Slice 3 pre-flight state merge guard
**Spec source:** [`../feature-web-dashboard-card-truth.md`](../feature-web-dashboard-card-truth.md), [`../superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`](../superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md)
**Standards sources:** maintainer-provided `AGENTS.md` instructions, [`../feature-workflow.md`](../feature-workflow.md)
**Verdict:** PASS WITH NOTES. The original Slice 3 blocker was reproduced and fixed; expansion/drag-drop UI work is still not implemented.

## Findings

### High

No findings.

### Medium

No open findings.

Resolved in this pass:

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/console.js`, `webtests/console.tests.js` - Slice 1's background telemetry persistence could overwrite customization changes from another same-route browser tab.
  Evidence: a new regression test first failed with `TypeError: S.mergeTelemetryState is not a function`. Stable and preview dashboards now normalize `sensorAliases`, `primaryCards`, and `cardOrder`, expose `SQ.mergeTelemetryState` and `SQ.saveTelemetryState`, and call `saveTelemetryDashboard()` only from telemetry accumulator flushes. User-driven `saveDashboard()` still writes full state.
  Impact: same-route passive tabs now preserve fresh aliases, order, overrides, hidden state, card style, card selection, and route-local layout while merging only `observedMax` and `powerLimitSamples`.
  Verification: `node webtests\selftest.node.js` passes `147/147`, including the new state-merge cases.

### Low

No current low findings. Review-time docs drift around the derived-limit sample gate was aligned in this pass: the accepted spec now matches the implemented 5% idle floor, 8-sample minimum, median ratio, and 25 W bucket behavior.

## Verification

- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` - pass
- `node webtests\selftest.node.js` - pass, `SELFTEST PASS 147/147`
- Preview model one-off against `Resources\WebDash\cardtruth\console.js` - pass, `141/141`
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --no-restore` - pass, 42/42; existing xUnit2020 analyzer warning in `DataJsonGoldenTests.cs`
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore` - pass, 0 warnings/errors
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore` - pass after stopping old PID 38804, 0 warnings/errors
- Live app restarted as PID 48116; `GET /`, `/dash/cardtruth`, `/dash/cardtruth/`, `/dash/cardtruth/console.js`, `/data.json`, and `/metrics` returned 200; served stable/preview JS both contain `SQ.saveTelemetryState`
- `git diff --check` - pass; CRLF normalization warnings only
- `git diff --check origin/master...HEAD` - pass
- `git diff --check origin/master` - pass; CRLF normalization warnings only

## Coverage Notes

- Deep-reviewed files: stable and preview `console.js`, `webtests/console.tests.js`, `docs/feature-web-dashboard-card-truth.md`, `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`.
- Sampled files: route and preview docs for storage namespace/lifecycle consistency.
- Browser drag/drop E2E remains out of scope because expansion/drag-drop UI is planned, not implemented.

## Slice 3 Planning Notes

- Slice 3 started with state safety; that pre-flight guard is now implemented.
- User selection and order must be first-class: visible cards/rows/panels get direct drag/drop where reliable, plus keyboard move controls.
- `/dash/cardtruth/` remains a temporary comparison subsite. Accepted row-order behavior must be promoted into stable `/` or discarded; it should not become the only place where the feature works.
- Browser tabs on the same route are the real concurrency problem; stable `/` and preview `/dash/cardtruth/` already use separate storage keys and should keep doing so until explicit promotion/import.
- Side-tree check: `claude-devsev/loving-volhard-66d981` and `worktree-dashboard-templates` both diverge before the current route/menu/preview fixes. Do not merge either tree wholesale; port or cherry-pick selected ideas only after preserving the versioned dashboard routes.
