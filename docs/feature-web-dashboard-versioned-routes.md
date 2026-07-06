# Feature Spec: Web Dashboard Versioned Preview Routes

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Verified  
**Updated:** 2026-07-05  
**Related docs:** [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md), [`feature-web-dashboard-customization.md`](feature-web-dashboard-customization.md)  
**Purpose:** allow multiple dashboard UI versions to be served side by side for testing without replacing or damaging the stable dashboard.

## 1. Summary

The web server will keep `/` as the stable dashboard and add explicit preview routes for alternate dashboard builds. Each preview route consumes the same live root APIs (`/data.json`, `/metrics`, `/Sensor`, `/ResetAllMinMax`) but uses its own embedded static assets and browser-local state namespace.

Example routes:

- `/` - stable dashboard, unchanged by experiments.
- `/dash/cardtruth/` - current card-truth preview.
- `/dash/roworder/` - row reorder and pin/collapse preview.
- `/dash/network/` - network activity/latency preview.

## 2. Problem and Motivation

Dashboard work is now visual and interactive enough that testing changes directly on `/` is too risky. The operator needs to compare variants, test ideas like network activity summaries and row pinning, and reject poor UI directions without losing the last usable dashboard.

## 3. Goals and Non-Goals

**Goals**

- Keep `/` stable while experimental dashboards are available under named routes.
- Let multiple preview dashboards run from the same rebuilt executable.
- Prevent preview localStorage/settings from corrupting the stable dashboard layout.
- Keep all preview work client-side unless a separate feature spec explicitly changes server data contracts.
- Make preview routes easy to inspect locally and through the existing proxy/tunnel.

**Non-goals**

- No server-stored dashboard profiles.
- No change to `data.json`, Prometheus, CSV, or sensor identifiers.
- No automatic promotion of a preview route to `/`.
- No new frontend framework requirement.

## 4. Behavior Specification

The server serves `/` as the stable dashboard: `Resources/Web/index.html`, `console.css`, and `console.js` remain the stable dashboard assets. The stable dashboard masthead includes a compact Pages menu with root-absolute links to available preview dashboards and diagnostic read-only endpoints.

The server also recognizes route roots matching `/dash/<version>/`. If the request path is exactly a preview route root, the server serves that version's `index.html`. Requests under the same route serve that version's embedded assets.

Preview route names must be short ASCII slugs without spaces. Prefer names without hyphens because the current embedded-resource resolver has special-case handling for hyphenated names.

Preview pages must call live APIs with root-absolute paths:

- `/data.json`
- `/metrics`
- `/Sensor?...`
- `/ResetAllMinMax`

They must not fetch `data.json` relative to the preview route, because `/dash/<version>/data.json` is a static asset path, not the API endpoint.

Each preview version uses a separate browser storage namespace, for example:

- stable `/`: `sq.dashboard.v1`
- `/dash/cardtruth/`: `sq.dashboard.preview.cardtruth`
- `/dash/roworder/`: `sq.dashboard.preview.roworder`

State can be exported/imported later, but preview routes must not read or write the stable namespace by default.

Promotion is explicit: moving a preview to `/` requires a separate commit that copies or wires the selected assets into `Resources/Web/` and records verification against the relevant feature spec.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI | Adds preview dashboard URLs under `/dash/<version>/`; `/` remains stable and links available preview/diagnostic pages from the masthead Pages menu. |
| Settings/config | Browser-local preview namespaces are separate from stable dashboard state. |
| Remote web/API | Static routing only; root API endpoints are unchanged. |
| Logging/files | None. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Stable dashboard accidentally replaced | `/` assets stay in `Resources/Web/`; preview assets live in a separate embedded folder and route. |
| Preview state corrupts stable layout | Per-version localStorage keys; no default read/write to `sq.dashboard.v1`. |
| Relative API fetches fail under subroutes | Require root-absolute API paths in preview assets and test them. |
| Embedded resource path quirks | Use simple slug names and add route tests for `/dash/<version>/`, CSS, and JS. |
| Upstream sync | Keep routing change small and isolated to `HttpServer.cs`; UI variants stay in static resources. |
| `net472` vs `net10.0-windows` | Route code uses existing `HttpListener` and embedded-resource patterns only. |

## 7. Acceptance Criteria

- [x] `/` still serves the current stable dashboard after the change.
- [x] At least one preview route, `/dash/cardtruth/`, serves a separate dashboard shell.
- [x] Preview CSS and JS are served from the preview route, not from the stable `/` assets.
- [x] The preview dashboard successfully fetches `/data.json` using a root-absolute URL.
- [x] Stable and preview dashboards use different localStorage namespaces.
- [x] `/` exposes a Pages menu linking `/dash/cardtruth/`, `/data.json`, and `/metrics`.
- [x] A broken or missing preview route returns 404 and does not fall back to `/`.
- [x] Existing behavior not in scope remains unchanged: `/data.json`, `/metrics`, `/Sensor`, `/ResetAllMinMax`, and stable `/`.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| JS syntax | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` and preview JS checks | 0 errors |
| Web model self-test | `node webtests\selftest.node.js` | pass |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Stable route smoke | `GET http://localhost:8085/` | stable dashboard HTML |
| Root menu check | `GET http://localhost:8085/` and inspect links | menu includes `/dash/cardtruth/`, `/data.json`, `/metrics` |
| Preview route smoke | `GET http://localhost:8085/dash/cardtruth/` | preview dashboard HTML |
| Preview API smoke | Open preview route and verify network request to `/data.json` | 200, live payload |
| Missing route smoke | `GET http://localhost:8085/dash/missing/` | 404 |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Preview asset folder name | resolved | `Resources\WebDash\<slug>\...` |
| First preview contents | resolved | `/dash/cardtruth/` cloned from stable shell with preview title, root-absolute `/data.json`, and isolated storage key |
| Preview index page | resolved | no gallery page; root Pages menu links named preview URLs |
| Promotion process | release | explicit copy/wire to `/` after operator approval |

## 10. Implementation Notes

Drafted on branch `feat/web-dashboard-versioned-routes` after operator requested testable version subsites that do not destroy the current dashboard.

Implementation adds:

- `HttpServer.TryMapDashboardPreviewResource(...)` for pure, testable `/dash/<slug>/...` mapping.
- Exact root API routing for `GET /data.json`, `/metrics`, `/Sensor`, and `/ResetAllMinMax`.
- Preview assets under `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/`.
- `sq.dashboard.preview.cardtruth` storage key and root-absolute `fetch('/data.json')` in the preview JS.
- Route tests in `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs`.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-05 | Spec drafted only | pass | No product code changed in the first spec pass. |
| 2026-07-05 | Subagent verification pass | pass | Two read-only sidecars reviewed routing risk and verification scope; findings folded into exact API routing and route tests. |
| 2026-07-05 | `node --check .\LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`; `node --check .\LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js`; `node .\webtests\selftest.node.js` | pass | Stable and preview JS parse; model self-test `SELFTEST PASS 100/100`. |
| 2026-07-05 | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | pass | 42/42 tests pass, including new route tests and existing data.json golden tests. |
| 2026-07-05 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`; `dotnet build ... -f net472` | pass | Both Release x64 targets build with 0 warnings and 0 errors after stopping the running task for the net10 deploy build. |
| 2026-07-05 | Live smoke on restarted scheduled task, PID 62056 | pass | `http://127.0.0.1:8085/`, `/dash/cardtruth/`, preview JS/CSS, `/data.json`, and `/metrics` returned 200; `/dash/missing/` returned 404. Proxy `http://127.0.0.1:8095/dash/cardtruth/` and preview JS also returned 200. |
| 2026-07-05 | Preview row-reorder slice on restarted scheduled task, PID 24440 | pass | `/dash/cardtruth/console.js` on both 8085 and 8095 contains `rowOrder`, `rowGroup`, `row-up`, and `row-grip`; preview keeps `sq.dashboard.preview.cardtruth` and does not contain stable `sq.dashboard.v1`. |
| 2026-07-06 | Root Pages menu and live route smoke after rebuild/restart, PID 56304 | pass | `http://localhost:8085/` serves a Pages menu linking `/dash/cardtruth/`, `/data.json`, and `/metrics`; root/preview/assets/API returned 200 and `/dash/missing/` returned 404. |
