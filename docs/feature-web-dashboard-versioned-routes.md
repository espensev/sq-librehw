# Feature Spec: Web Dashboard Versioned Preview Routes

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Verified
**Updated:** 2026-07-06
**Related docs:** [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md), [`feature-web-dashboard-customization.md`](feature-web-dashboard-customization.md)
**Purpose:** allow multiple dashboard UI versions to be served side by side for testing without replacing or damaging the stable dashboard.

## 1. Summary

The web server keeps `/` as the stable dashboard and adds explicit preview routes for alternate dashboard builds. Each preview route consumes the same live root APIs (`/data.json`, `/metrics`, `/Sensor`, `/ResetAllMinMax`) but uses its own embedded static assets and browser-local state namespace.

Preview routes are temporary development surfaces, not permanent product navigation. The `cardtruth` route exists only while the v3 card-first work is being compared and promoted. Once the selected behavior is synced into `/`, the separate `cardtruth` route and Pages-menu entry should be retired; any surviving visual treatment belongs as a selectable root-dashboard theme/view option from the Theme dropdown, not as a second dashboard page.

Example routes:

- `/` - stable dashboard, unchanged by experiments.
- `/dash/cardtruth/` - current card-truth preview.
- Future examples, if needed: `/dash/roworder/` for row reorder testing, `/dash/network/` for network activity/latency testing.

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

The server also recognizes route roots matching `/dash/<version>/`. If the request path is exactly a preview route root, with or without the trailing slash, the server serves that version's `index.html`. Requests under the same route serve that version's embedded assets. Preview shells must use route-root-absolute asset links such as `/dash/cardtruth/console.css` and `/dash/cardtruth/console.js` so direct no-slash URLs remain browser-viable.

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
- future `/dash/roworder/`: `sq.dashboard.preview.roworder`

State can be exported/imported later, but preview routes must not read or write the stable namespace by default. When a preview is promoted, selected state fields should move into the stable `sq.dashboard.v1` model deliberately. Visual variants should be represented by a stable dashboard theme/view field, not by keeping the preview route alive.

Promotion is explicit: moving a preview to `/` requires a separate commit that copies or wires the selected assets into `Resources/Web/` and records verification against the relevant feature spec. After promotion, remove the temporary preview route unless a new active comparison needs it.

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
| Direct no-slash preview URL breaks CSS/JS | Preview shells use route-root-absolute asset links; live smoke checks `/dash/cardtruth` as well as `/dash/cardtruth/`. |
| Embedded resource path quirks | Use simple slug names and add route tests for `/dash/<version>/`, CSS, and JS. |
| Upstream sync | Keep routing change small and isolated to `HttpServer.cs`; UI variants stay in static resources. |
| `net472` vs `net10.0-windows` | Route code uses existing `HttpListener` and embedded-resource patterns only. |

## 7. Acceptance Criteria

- [x] `/` still serves the current stable dashboard after the change.
- [x] At least one preview route, `/dash/cardtruth/`, serves a separate dashboard shell.
- [x] Direct `/dash/cardtruth` loads a browser-viable shell with CSS/JS from the preview route.
- [x] Preview CSS and JS are served from the preview route, not from the stable `/` assets.
- [x] The preview dashboard successfully fetches `/data.json` using a root-absolute URL.
- [x] Stable and preview dashboards use different localStorage namespaces.
- [x] `/` exposes a Pages menu linking `/dash/cardtruth/`, `/data.json`, and `/metrics`.
- [x] A broken or missing preview route returns 404 and does not fall back to `/`.
- [x] Existing behavior not in scope remains unchanged: `/data.json`, `/metrics`, `/Sensor`, `/ResetAllMinMax`, and stable `/`.
- [x] `cardtruth` is documented as a temporary development route; after promotion, visual variants move to the root Theme dropdown instead of remaining separate pages.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| JS syntax | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` and preview JS checks | 0 errors |
| Web model self-test | `node webtests\selftest.node.js` | pass |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Stable route smoke | `GET http://localhost:8085/` | stable dashboard HTML |
| Root menu check | `GET http://localhost:8085/` and inspect links | menu includes `/dash/cardtruth/`, `/data.json`, `/metrics` |
| Preview route smoke | `GET http://localhost:8085/dash/cardtruth/` | preview dashboard HTML |
| Preview no-slash smoke | `GET http://localhost:8085/dash/cardtruth` and resolve shell assets | preview dashboard HTML; CSS and JS resolve under `/dash/cardtruth/` |
| Preview API smoke | Open preview route and verify network request to `/data.json` | 200, live payload |
| Missing route smoke | `GET http://localhost:8085/dash/missing/` | 404 |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Preview asset folder name | resolved | `Resources\WebDash\<slug>\...` |
| First preview contents | resolved | `/dash/cardtruth/` cloned from stable shell with preview title, root-absolute `/data.json`, and isolated storage key |
| Preview index page | resolved | no gallery page; root Pages menu links named preview URLs |
| Promotion process | release | explicit copy/wire to `/` after operator approval; retire temporary route once synced |
| Long-term card-truth access | release | selected visual treatment becomes a root Theme dropdown/view choice, not a permanent extra page |

## 10. Implementation Notes

Drafted on branch `feat/web-dashboard-versioned-routes` after operator requested testable version subsites that do not destroy the current dashboard.

Implementation adds:

- `HttpServer.TryMapDashboardPreviewResource(...)` for pure, testable `/dash/<slug>/...` mapping.
- Exact root API routing for `GET /data.json`, `/metrics`, `/Sensor`, and `/ResetAllMinMax`.
- Preview assets under `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/`.
- `sq.dashboard.preview.cardtruth` storage key, root-absolute shell assets, and root-absolute `fetch('/data.json')` in the preview JS.
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
| 2026-07-06 | No-slash preview-route fix + preview lifecycle decision | pass | Preview shell assets are route-root-absolute so `/dash/cardtruth` resolves CSS/JS correctly. `cardtruth` remains a temporary dev route only; after sync/promotion, the remaining visual treatment moves under the root Theme dropdown and the separate preview route should be removed. |
| 2026-07-06 | Rebuilt/restarted live app, PID 38804 | pass | `node webtests\selftest.node.js` passed 120/120. `dotnet test` passed 42/42. `net472` and `net10.0-windows` Release x64 builds passed 0/0. Live `GET /dash/cardtruth` and `/dash/cardtruth/` returned 200; shell HTML points at `/dash/cardtruth/console.css` and `/dash/cardtruth/console.js`, both 200. |
