# Review - Webserver Dashboard Interaction

**Date:** 2026-07-07
**Surface:** read-only source audit plus live route smoke against PID `38992`
**Spec source:** `AGENTS.md`, `docs/ai-guide.md`, `docs/feature-web-dashboard-customization.md`, `docs/feature-web-dashboard-card-truth.md`, `docs/feature-web-dashboard-versioned-routes.md`
**Standards sources:** `AGENTS.md`, `docs/ai-guide.md`, `docs/feature-workflow.md`
**Verdict:** FAIL for the server/API boundary; root dashboard remains read-only as specified

## Patch List

| Item | Status | Evidence |
|---|---|---|
| Stable JSON failure for GET `/Sensor` invalid requests | Fixed and verified | `feature-webserver-api-hardening.md`; `HttpServer.HandleGetSensorRequest`; `HttpServerSensorApiTests`; live PID `30508` returned HTTP 200 `{"result":"fail","message":"Unknown id /missing specified"}`. |
| HTTP control value validation and range clamp | Fixed and verified | `SetSensorControlValue` rejects blank/malformed/non-finite values and clamps finite values to `IControl.MinSoftwareValue`/`MaxSoftwareValue`; covered by `HttpServerSensorApiTests`. |
| GET `action=Set` stays rejected | Fixed and verified | `HandleGetSensorRequest` returns JSON failure before dispatching the sensor write path; covered by test and live smoke. |
| Require auth or explicit write-enable for POST `action=Set` | Open policy decision | Preserved for compatibility in this patch; tracked in `feature-webserver-api-hardening.md` §9. |
| Public reset-route exposure | Fixed at public edge | `D:\Scripts\tunnels\telemetry-public-proxy.mjs` blocks reset GETs and `Start-SevIQTelemetryPublic.ps1` now self-tests that `/ResetAllMinMax` and `/Sensor?action=ResetMinMax...` return 403 before starting/reporting the public tunnel. Server-side POST-only reset remains optional future defense-in-depth for untrusted direct `:8085` exposure. |
| Root-vs-preview UI divergence and D3 responsive/stat-card polish | Still queued | Remains covered by `reviews/review-2026-07-07-dashboard-d3-user-perspective.md` and `superpowers/plans/2026-07-07-web-responsive-theme-qa-d3.md`. |

## Findings

### High

- [axis: regression/security] `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:323-333`, `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:244-258`, `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:397-418`, `LibreHardwareMonitor.Windows.Forms/UI/InterfacePortForm.cs:40-68` - The webserver is not read-only: `POST /Sensor?action=Set` can write hardware control values while authentication is optional and all-interface binding is selectable.
  Evidence: `HttpServer` sets control values through `sNode.Sensor.Control.SetSoftware(...)`; `MainForm` constructs the server from persisted auth/listener settings; `InterfacePortForm` offers `0.0.0.0` and writes the selected listener IP/port back to the server. `HttpServer.StartHttpListener()` also falls back to `+` when the configured IP is not found (`HttpServer.cs:120-126`).
  Impact: the documented dashboard invariant "read-only dashboard" can be misread as "read-only webserver." A listener bound beyond localhost, a local proxy, or a tunnel can expose a hardware write API if auth/settings are not handled deliberately.
  Recommendation: document this boundary explicitly everywhere web exposure is discussed. Before broader exposure, require auth or an explicit write-enable gate for `Set`, and add HTTP dispatch tests for auth, origin rejection, and write behavior.

### Medium

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:468-483` - `GET /Sensor` does not consistently return the JSON fail shape promised by the server comments when lookup fails.
  Evidence: live smoke on 2026-07-07 returned HTTP 500 with an empty body for `GET /Sensor?action=Get&id=/missing`; the same missing sensor through `POST /Sensor?action=Get&id=/missing` returned JSON `{"result":"fail","message":"Unknown id /missing specified"}`. The comment block documents fail JSON for `/Sensor` responses at `HttpServer.cs:263-276`, but the GET path calls `HandleSensorRequest` outside the `HandlePostRequest` try/catch.
  Impact: clients and diagnostics cannot rely on a stable JSON error contract for GET sensor reads.
  Recommendation: route GET `/Sensor` through the same result/error wrapper as POST, then add tests for missing id, missing action, and invalid action.
  Status 2026-07-07: fixed and verified in `feature-webserver-api-hardening.md`; live rebuilt app returned JSON failure instead of HTTP 500.

- [axis: regression/security] `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:313-318`, `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:487-495` - GET routes mutate telemetry extrema.
  Evidence: `/Sensor?action=ResetMinMax` resets the addressed sensor before returning values, and `/ResetAllMinMax` resets every sensor through `_rootElement.Accept(new SensorVisitor(...))`. The GET guard only blocks `action=Set` at `HttpServer.cs:472-477`. During this audit, a live `GET /ResetAllMinMax` returned 200 and reset the running app's current min/max history.
  Impact: crawlers, bookmarks, browser prefetch, or casual diagnostic checks can reset historical extrema. It is not a hardware control write, but it is still state mutation over GET.
  Recommendation: if compatibility allows, make reset operations POST-only. If compatibility does not allow it, document the legacy behavior and add explicit dispatch tests so future UI/proxy work treats it as mutating.

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:251-258`, `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:1768-1777` - The HTTP `Set` path does not enforce the same range gate as the desktop context menu.
  Evidence: the desktop menu only creates 0-100 percent choices that fit `control.MinSoftwareValue` and `control.MaxSoftwareValue`; HTTP parses the raw query string value and passes it directly to `SetSoftware`.
  Impact: correctness depends on every `IControl` implementation clamping or rejecting invalid values. That may be true in some drivers, but it is not proven at the server boundary.
  Recommendation: either prove and test downstream clamping for all relevant controls, or clamp/validate at the HTTP layer before `SetSoftware`.
  Status 2026-07-07: fixed and verified in `feature-webserver-api-hardening.md`; HTTP-layer helper now validates finite numeric values and clamps to the control range before `SetSoftware`.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:29-63`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html:29-88`, `docs/feature-web-dashboard-versioned-routes.md:11-13` - Root `/` and `/dash/cardtruth/` are no longer equivalent UI acceptance surfaces.
  Evidence: root has the Sensors popover, network restore block, primary reset, panel reset, and network section; preview still has the older Customize drawer. The versioned-routes spec says preview routes are temporary development surfaces.
  Impact: preview smoke can prove routing, but not D3 root-dashboard behavior.
  Recommendation: keep root `/` as the D3 acceptance surface; use preview only for lifecycle comparison until E1/E2 retires or reconciles it.

- [axis: spec] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:130-179`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:339-365`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:445-460`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:578-650` - Browser-local state can intentionally diverge from server truth.
  Evidence: hidden sensors, aliases, explicit range overrides, observed peaks, and derived GPU power samples live in `sq.dashboard.v1`. This is allowed by the v3 spec, but it is a data-trust boundary rather than raw LibreHardwareMonitor state.
  Impact: users can see a friendly label or operator range that is not server truth unless provenance is visible.
  Recommendation: keep raw LibreHardwareMonitor labels, `SensorId`, and range provenance visible in detail/search; treat export/import or cross-tab behavior as display-state handling, not server state.

### Low

- [axis: verification] `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs:8-43`, `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs:30-67`, `webtests/selftest.node.js:6-13` - Current tests cover route mapping, `data.json` golden bytes, and root dashboard model helpers, but not live HTTP dispatch, auth, CORS/origin rejection, `/metrics`, reset routes, or POST `/Sensor`.
  Evidence: `HttpServerRouteTests` exercises only `TryMapDashboardPreviewResource`; `DataJsonGoldenTests` exercises object serialization; `selftest.node.js` evaluates root `console.js` in DOM-less `SQ_NO_BOOT` mode.
  Impact: server behavior can regress while existing tests stay green.
  Recommendation: add focused HTTP dispatch tests before changing server behavior; keep browser layout gates separate from selftest.

## Verification

- `git status --short --branch` - pass; repo already had docs-only D3 worktree changes before this audit.
- Source searches/read-through of `HttpServer.cs`, `MainForm.cs`, `InterfacePortForm.cs`, root/preview dashboard assets, webtests, route tests, and dashboard specs - pass.
- Live smoke on PID `38992` (`ProductVersion 0.9.6+0279333.2026-07-07`) - pass for `/`, `/data.json`, `/metrics`, `/dash/cardtruth`, `/dash/cardtruth/`; `/dash/missing/` returned 404.
- Live data snapshot - pass: `data.json` reported version `0.9.6`, host `SND-DESK`, 50 hardware nodes, 540 sensors, about 158 KB.
- Live JS snapshot - pass: root JS contains `sq.dashboard.v1`, relative `fetch('data.json')`, and no `/Sensor`/`ResetAllMinMax` calls; preview JS contains `sq.dashboard.preview.cardtruth`, root-absolute `fetch('/data.json')`, and no `/Sensor`/`ResetAllMinMax` calls.
- Live sensor API probe - fail: `GET /Sensor?action=Get&id=/missing` returned HTTP 500; `POST /Sensor?action=Get&id=/missing` returned JSON fail.
- Follow-up hardening live probe on rebuilt PID `30508` - pass: `GET /Sensor?action=Get&id=/missing` returned HTTP 200 JSON failure; `GET /Sensor?action=Set&id=/missing&value=55` returned HTTP 200 JSON failure; `/data.json` returned HTTP 200.
- Live reset probe - mutating check: `GET /ResetAllMinMax` returned 200 and reset current min/max history; no hardware control write was sent.
- `git diff --check` - pass; only CRLF conversion warnings on docs.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` - pass.
- `node webtests\selftest.node.js` - pass, `SELFTEST PASS 227/227`.
- Full .NET build/test - not run; this pass wrote docs/review artifacts only and did not change product code.
- Follow-up hardening gates - pass: `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` passed 55/55; Release `net10.0-windows` and `net472` builds passed with 0 warnings and 0 errors; `node E:\SQ_HQ\Monitoring\sq-librehw\webtests\selftest.node.js` passed `SELFTEST PASS 227/227`.

## Coverage Notes

- Files reviewed deeply: `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`, `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`, `LibreHardwareMonitor.Windows.Forms/UI/InterfacePortForm.cs`, `LibreHardwareMonitor.Windows.Forms/UI/Node.cs`, `LibreHardwareMonitor.Windows.Forms/UI/HardwareNode.cs`, `LibreHardwareMonitor.Windows.Forms/UI/SensorNode.cs`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`, `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html`, `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/console.js`, `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs`, `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs`, `webtests/selftest.node.js`.
- Files sampled: related dashboard feature specs and existing D3 review/plan docs.
- Excluded: hardware driver implementations behind `IControl.SetSoftware`; no live hardware write was attempted.

## Open Questions

- Should `POST /Sensor?action=Set` require auth even when read endpoints are anonymous?
- Should reset routes become POST-only, or is GET compatibility required for existing clients?
- Should `HttpServer` clamp control values, or should clamping remain the responsibility of each control implementation?
- Should this fork add an in-process `HttpListener` dispatch harness, or keep server API verification as live smoke?
