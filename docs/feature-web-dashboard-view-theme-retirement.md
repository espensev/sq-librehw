# Feature Spec: Web Dashboard View Theme and Preview Retirement

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Verified  
**Updated:** 2026-07-07  
**Related docs:** [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md), [`feature-web-dashboard-versioned-routes.md`](feature-web-dashboard-versioned-routes.md), [`superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`](superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md)  
**Purpose:** finish Phase E by making the root dashboard the only product surface while preserving a root look selector for the accepted card-truth treatment.

## 1. Summary

The stable root dashboard now carries the accepted card-first/card-truth behavior. Phase E adds a root `viewTheme` selector with `standard` and `cardTruth` values, persists it in `sq.dashboard.v1`, and retires the stale `/dash/cardtruth/` preview route, menu link, route helper, route tests, and embedded preview assets.

## 2. Problem and Motivation

`/dash/cardtruth/` started as a safe comparison route, but it now lags root behavior: root has the Sensors popover, network adapter groups, primary-card controls, D2 overlay behavior, and D3 responsive fixes, while the preview still carried legacy drawer DOM. Keeping it online makes the product surface ambiguous and risks testing the wrong dashboard.

## 3. Goals and Non-Goals

**Goals**

- Keep `/` as the single product dashboard surface.
- Persist `viewTheme: standard | cardTruth` in stable browser-local dashboard state.
- Keep `viewTheme` as a look selector, separate from future Main/Gaming/Storage context dashboards.
- Remove the stale `/dash/cardtruth/` route, Pages link, embedded assets, and tests that required the preview to exist.
- Keep `/data.json`, `/metrics`, `/Sensor`, `/ResetAllMinMax`, and the read-only root dashboard behavior unchanged.

**Non-goals**

- No new server-side dashboard profiles.
- No hash router or context-dashboard switcher in Phase E.
- No `data.json`, CSV, Prometheus, hardware access, or control-write changes.
- No second product page for the card-truth dashboard.

## 4. Behavior Specification

The root dashboard masthead exposes a compact `View` selector. It offers:

- `standard`
- `cardTruth`

The selector writes only `state.dashboard.viewTheme`; it does not change the current route, server endpoint, or hardware data. The root HTML also exposes `data-view-theme` so a future visual treatment can be applied without another server route. Unknown or malformed values normalize to `standard`.

`viewTheme` lives under the same root dashboard namespace, `sq.dashboard.v1`. This keeps the current dashboard stable while remaining compatible with the future context-dashboard lane, where each hash route can own its own `sq.dashboard.{route}` state and still carry an independent `viewTheme` field.

The Pages menu keeps diagnostic links to `/data.json` and `/metrics`. It no longer links `/dash/cardtruth/`.

Requests for `/dash/cardtruth/` are no longer mapped to embedded preview assets. They should return 404 through normal static-resource handling after rebuild. The root dashboard remains available at `/`.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Root masthead gains `View`; Pages drops `Card Truth Preview`. |
| Settings/config | Adds normalized `viewTheme` to `sq.dashboard.v1`; legacy states default to `standard`. |
| Remote web/API | Removes active `/dash/cardtruth/` static preview mapping; APIs unchanged. |
| Logging/files | Deletes stale preview embedded assets under `Resources/WebDash/cardtruth/`. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Existing root state loses fields | Normalization adds one field and keeps telemetry merge preserving user-owned state. |
| `cardTruth` selector looks like a second dashboard | It is a local look field only; no route switch or context dashboard behavior is introduced. |
| Future context dashboards get blocked | `viewTheme` remains orthogonal and can live inside future `sq.dashboard.{route}` state. |
| Stale preview links survive | Selftest asserts the root menu no longer links `/dash/cardtruth/`. |
| Deleted assets break root dashboard | Root uses only `Resources/Web/*`; static checks and rebuild verify embedded resources. |

## 7. Acceptance Criteria

- [x] Root dashboard has a keyboard-accessible `viewTheme` selector with `standard` and `cardTruth`.
- [x] `viewTheme` persists through `sq.dashboard.v1`, normalizes bad values to `standard`, and is preserved by telemetry-only saves.
- [x] Root Pages menu keeps `/data.json` and `/metrics` but no longer links `/dash/cardtruth/`.
- [x] `Resources/WebDash/cardtruth/` assets are removed from source.
- [x] `HttpServer` no longer maps `/dash/cardtruth/` to preview embedded resources.
- [x] Rebuilt live app returns 200 for `/`, `/data.json`, and `/metrics`, and 404 for `/dash/cardtruth/`.
- [x] `node --check`, selftest, golden tests, and `net10.0-windows` x64 build pass.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| JS syntax | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` | 0 errors |
| Web model self-test | `node webtests\selftest.node.js` | pass |
| Golden/server tests | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | pass |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Runtime smoke | `GET /`, `/data.json`, `/metrics`, `/dash/cardtruth/` | root/API 200; retired preview 404 |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Whether `cardTruth` gets distinct CSS later | future visual polish | no visual fork in Phase E; current D3 root styling is the accepted product surface |

## 10. Implementation Notes

- `Resources/Web/index.html` owns the root selector and drops the preview Pages link.
- `Resources/Web/console.js` normalizes and persists `viewTheme`, sets `data-view-theme`, and preserves it through telemetry merges.
- `Resources/Web/console.css` styles the masthead selector using the existing compact control language.
- `HttpServer.cs` no longer contains the preview route mapper or dispatch branch.
- `Resources/WebDash/cardtruth/*` and the preview route unit tests are removed.
- Preview-only drawer conveniences were audited before deletion. Pinned-card title editing and the drawer's `Clear pinned` shortcut were not promoted: root aliases are the accepted naming mechanism, and visible card controls already remove pinned cards one at a time without reintroducing a drawer.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-07 | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`; `node webtests\selftest.node.js` | pass | Root JS parses; selftest passes 229/229 with `viewTheme` normalization, telemetry preservation, root selector, and retired preview-link checks. |
| 2026-07-07 | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`; `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`; live smoke on PID `74624` | pass | Tests pass 44/44 with only the pre-existing `DataJsonGoldenTests.cs:61` xUnit2020 analyzer warning. Release build is 0 warnings/0 errors. Live `GET /`, `/console.css`, `/console.js`, `/data.json`, and `/metrics` returned 200; root contains `#viewTheme` and no `/dash/cardtruth/` link; `/dash/cardtruth/` and `/dash/cardtruth` returned 404; embedded CSS includes the mobile `viewTheme` full-row rule. |
