# Discovery - Webserver and Dashboard Interaction

**Goal:** Write down and audit how the webserver/dashboard works and how it interacts with LibreHardwareMonitor.
**Date:** 2026-07-07
**Status:** complete
**Recommended next:** decide the remaining legacy API policy items (auth/write-enable for POST `Set`, POST-only reset compatibility) before exposing the server beyond the trusted local operator path; keep the F context-dashboard lane separate from the root `viewTheme` selector.

---

## Questions

1. How is the webserver started and connected to the LibreHardwareMonitor hardware model?
2. Which HTTP routes are served, and which routes are static, read-only, or mutating?
3. How is `data.json` produced from the live hardware tree?
4. How does the root dashboard consume data and persist browser-local state?
5. How does `/dash/cardtruth/` differ from `/` today?
6. Where are the main trust, data-contract, and verification risks?

---

## Findings

### Q1: How is the webserver started and connected to the LibreHardwareMonitor hardware model?

**Answer:** `MainForm` owns the live hardware model and passes the WinForms node tree into `HttpServer`. The server is embedded in `LibreHardwareMonitor.Windows.Forms.exe`, not a separate process.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:147-153` - creates the root UI node from `Environment.MachineName` and constructs `Computer`.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:245-264` - hooks hardware add/remove events and opens the computer.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:397-403` - constructs `HttpServer` with `_root` and `_computer`, listener IP/port, and authentication settings.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:411-418` - `runWebServerMenuItem` starts and stops the listener.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs:659-662` and `LibreHardwareMonitor.Windows.Forms/UI/UpdateVisitor.cs:18-22` - a timer-driven background worker updates hardware readings via `_computer.Accept(_updateVisitor)`.

**Implications:**
- The webserver sees the same hardware tree as the desktop UI.
- Serving changed dashboard assets requires rebuilding the WinForms executable because the assets are embedded resources.
- Live webserver state can differ from source if the running EXE was built from an older commit.

### Q2: Which HTTP routes are served, and which routes are static, read-only, or mutating?

**Answer:** The server has three route classes: static embedded dashboard assets, read endpoints, and legacy mutating sensor/reset endpoints. The dashboard code currently uses only `data.json`, but the webserver is not globally read-only.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:456-530` - dispatch order: `POST`, `/data.json`, `/images_icon/`, `/metrics`, `/Sensor`, `/ResetAllMinMax`, then stable `Web.*` assets. There is no active preview static route before `/data.json`.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:489-492` - `GET /data.json` returns the JSON telemetry contract.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:501-504` - `GET /metrics` returns the Prometheus text surface.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:507-510` - `GET /Sensor` handles sensor reads, rejects `action=Set`, and returns JSON failure payloads for invalid requests.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:513-521` - `GET /ResetAllMinMax` resets all sensor extrema and returns `data.json`.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:524-530` and `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:555-590` - other GET paths map to stable embedded `Web.*` resources and return 404 when no matching resource exists; this is what retires `/dash/cardtruth/`.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:323-333` - `POST /Sensor?action=Set` can write `ISensor.Control` values after a same-origin browser check and server-side value validation/range clamping.

**Implications:**
- "Read-only dashboard" is a dashboard invariant, not a server-wide property.
- Any proxy/tunnel or all-interface bind must be treated as exposing read APIs plus legacy write/reset APIs.
- Future dashboard code must continue to avoid `/Sensor?action=Set` unless a separate accepted spec explicitly changes the read-only invariant.

### Q3: How is `data.json` produced from the live hardware tree?

**Answer:** `HttpServer.BuildDataJsonObject()` snapshots the WinForms node tree under `Node.SyncRoot`, recursively serializes nodes, and emits both formatted display strings and raw numeric fields. The payload is an external downstream contract locked by golden tests.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:689-692` - comments state that the object graph and serialization are the external downstream contract.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:698-710` - root object fields and locked tree traversal.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:1001-1051` - recursive node serialization.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:1015-1027` - sensor nodes include `SensorId`, `Type`, formatted `Min`/`Value`/`Max`, and raw values.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs:1054-1064` - NaN and Infinity raw values are serialized as `null`.
- `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs:19-27` - the golden test documents exact property names, ordering, value formatting, and version behavior.

**Implications:**
- Dashboard work should derive presentation from existing fields rather than changing server payload shape.
- Any `HttpServer.BuildDataJsonObject`, `GenerateJsonForNode`, or serialization change needs the golden test and downstream contract review.
- `Node.SyncRoot` protects structural tree traversal, but sensor values remain live readings owned by hardware implementations.

### Q4: How does the root dashboard consume data and persist browser-local state?

**Answer:** The root dashboard is a client-side app. It loads `sq.dashboard.v1`, polls `data.json`, transforms the raw tree into a flat sensor model, renders cards/panels/popovers, and stores only browser-local presentation state.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1-4` - root JS describes itself as consuming unchanged `data.json` and uses `sq.dashboard.v1`.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:35-47` - flattens server nodes into sensor objects carrying `hw`, `hwid`, `type`, labels, raw values, and `SensorId`.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:130-153` - default dashboard state includes hidden sensors, pinned cards, ordering, theme, aliases, range overrides, and network adapter state.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:182-195` - loads and saves normalized dashboard state in localStorage.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:931-972` - each render tick flattens sensors, tracks motion/history, derives limits, renders the visible UI, and throttles telemetry persistence.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1338-1352` - polls `fetch('data.json', { cache: 'no-store' })`.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1383-1518` - user actions such as hide/show, pin, primary, alias, range override, style, and order mutate local dashboard state only.

**Implications:**
- Root UI customization changes do not remove sensors from `data.json`, `/metrics`, CSV, or the desktop sensor tree.
- Local aliases/range overrides can intentionally differ from server truth, so raw labels and `SensorId` must stay visible wherever friendly labels are shown.
- Stale browser tabs remain a real persistence risk after rebuilds because older JS can save older normalized state.

### Q5: How does `/dash/cardtruth/` differ from `/` today?

**Answer:** As of Phase E on 2026-07-07, `/dash/cardtruth/` is retired and should return 404. Before retirement it was a comparison/preview surface, not an equivalent acceptance surface: it used separate embedded assets and a separate storage namespace, but lagged root behavior.

**Evidence:**
- `docs/feature-web-dashboard-view-theme-retirement.md` - Phase E retires `/dash/cardtruth/`, removes its assets/menu entry/tests, and adds root `viewTheme`.
- `docs/feature-web-dashboard-versioned-routes.md` - the route spec now records `cardtruth` as historical/retired, with current builds returning 404.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` - root has the Sensors popover, network restore, primary reset, subsystem reset, network section, and `#viewTheme`.
- `LibreHardwareMonitor.Tests/WebDashboardRetirementTests.cs` - tests assert the root selector/menu and absence of embedded `WebDash.cardtruth` resources.

**Implications:**
- Root `/` is the only product dashboard surface.
- Future preview routes require a new accepted feature spec and must not accidentally resurrect stale `cardtruth` assets.
- The future context-dashboard lane is separate from this retired server preview route.

### Q6: Where are the main trust, data-contract, and verification risks?

**Answer:** The main risks are boundary confusion, weak HTTP API coverage outside route mapping/data golden tests, UI layout gaps that selftest cannot see, and locally persisted display state that can diverge from server truth.

**Evidence:**
- `docs/ai-guide.md:26-33` - hard invariants: no `data.json`/HTTP/CSV changes, read-only dashboard, label honesty, DOM-less selftest is not a UI/layout gate.
- `LibreHardwareMonitor.Tests/WebDashboardRetirementTests.cs` - tests cover root view-theme/menu state and absence of embedded retired preview assets.
- `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs:30-67` - tests cover `data.json` streaming and golden bytes.
- `webtests/selftest.node.js:6-13` - selftest evaluates only root `console.js` in `SQ_NO_BOOT` mode with a fixture.
- `docs/reviews/review-2026-07-07-dashboard-d3-user-perspective.md:13-16` - existing D3 review records the live 390-touch row-control overlap blocker.
- Initial live audit on 2026-07-07: `GET /Sensor?action=Get&id=/missing` returned HTTP 500, while `POST /Sensor?action=Get&id=/missing` returned JSON `{"result":"fail",...}`.
- Follow-up hardening on 2026-07-07: rebuilt PID `30508` returned JSON failure for missing GET `/Sensor`, kept GET `action=Set` rejected, and served `/data.json` successfully.

**Implications:**
- HTTP dispatch behavior needs its own tests if it is changed or relied on.
- The dashboard's model tests are valuable, but D3 and later UI phases need browser geometry plus screenshot gates.
- Server write/reset APIs should be documented and constrained before any public/tunnel exposure is treated as safe.

---

## Cross-Cutting Analysis

### Constraints

- `data.json` is an external contract. Changes to object shape, property order, value formatting, assembly version, or raw-value sanitization must keep golden tests green and be reviewed as downstream-impacting.
- Root dashboard state is browser-local. There is no server-stored dashboard profile today.
- The root dashboard must remain read-only: no `/Sensor?action=Set` calls and no hardware write UI.
- Preview routes are temporary route/version surfaces. They are not an alternate permanent product page.
- Static assets are embedded in the Windows Forms executable; source edits require rebuild/restart before live verification.

### Risks

| Risk | Likelihood | Impact | Notes |
|---|---:|---:|---|
| Server write API exposed through broad listener/proxy | Medium | High | `POST /Sensor?action=Set` can reach `IControl.SetSoftware`; auth is optional and `0.0.0.0` is selectable. |
| GET reset route accidentally resets extrema | Medium | Medium | `/ResetAllMinMax` and `/Sensor?action=ResetMinMax` mutate state over GET. The audit's live smoke of `/ResetAllMinMax` reset current min/max history. |
| HTTP Set values not clamped at server boundary | Low | Medium | Fixed in `feature-webserver-api-hardening.md`; keep tests green if future `Set` behavior changes. |
| Route/test confidence overstates runtime dispatch coverage | Medium | Medium | Sensor GET failures and control value handling now have focused tests; auth/CORS/reset behavior still lacks dispatch coverage. |
| Preview evidence used as root evidence | Medium | Medium | Root has newer Sensors/network/primary UI; preview still has the drawer. |
| Local display state mistaken for server truth | Medium | Medium | Aliases, hidden state, overrides, observed peaks, and derived limits are browser-local. |
| UI layout bugs missed by selftest | High | Medium | Selftest is DOM-less; D3 already has a live mobile overlap blocker. |

### Open Questions

- Should server-side `Set` require authentication even when the rest of the webserver remains anonymous?
- Should min/max reset endpoints move to POST-only, or remain legacy-compatible with stronger warnings/tests?
- Should `HttpServer` keep clamping silently, or should future API versions report clamped values explicitly?
- Should HTTP dispatch behavior get an in-process integration test harness, or is live smoke sufficient for this fork?

---

## Recommendation

This discovery is ready to feed the remaining policy planning. The dashboard can continue through D3 as a read-only client-side surface, but do not describe the whole webserver as read-only. Treat POST `Set` write policy and mutating reset routes as separate server API behavior that needs an accepted compatibility decision before broader exposure.
