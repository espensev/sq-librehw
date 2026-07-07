# Feature Spec: Webserver API Hardening

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Verified
**Updated:** 2026-07-07
**Related docs:** `docs/discovery-webserver-dashboard-interaction.md`, `docs/reviews/review-2026-07-07-webserver-dashboard-interaction.md`, `docs/feature-web-dashboard-card-truth.md`
**Purpose:** make the legacy sensor API fail predictably and apply the same control-value safety bounds as the desktop UI without changing the dashboard's read-only contract.

## 1. Summary

This pass hardens the existing `HttpServer` sensor API while preserving legacy compatibility. GET `/Sensor` failures return the documented JSON failure shape instead of falling through to a generic HTTP 500. Hardware control writes through POST `/Sensor?action=Set` accept only finite numeric software values, keep `value=null` as the default-control path, and clamp numeric software values to the target `IControl` range before calling `SetSoftware`.

## 2. Problem and Motivation

The 2026-07-07 server/dashboard audit found that:

- the dashboard is read-only, but the webserver still exposes legacy mutating APIs;
- GET `/Sensor?action=Get&id=/missing` returns HTTP 500 with an empty body while POST returns JSON failure;
- the HTTP control-write path sends raw parsed floats to `IControl.SetSoftware`, while the desktop menu only presents values inside the control's advertised range;
- reset routes mutate min/max over GET.

The first safe implementation step is to fix unstable error handling and unsafe value intake without breaking known legacy API shape or moving larger write-policy choices into an implicit code change.

## 3. Goals and Non-Goals

**Goals**

- Return stable JSON failure payloads from GET `/Sensor` for missing ids, missing actions, and invalid actions.
- Keep GET `action=Set` rejected with JSON failure.
- Reject non-numeric and non-finite control values before any control write is attempted.
- Clamp accepted numeric control values to `IControl.MinSoftwareValue` and `IControl.MaxSoftwareValue`.
- Add focused regression tests for the fixed sensor API behavior.

**Non-goals**

- Do not change the `data.json` object graph, property order, value formatting, or golden contract.
- Do not make reset routes POST-only in this compatibility pass.
- Do not require authentication or a new write-enable setting for POST `action=Set` in this pass.
- Do not add dashboard UI that writes hardware controls.

## 4. Behavior Specification

- `GET /Sensor?action=Get&id=<missing>` returns JSON like `{"result":"fail","message":"Unknown id <id> specified"}` instead of HTTP 500.
- `GET /Sensor?action=Set...` returns JSON failure with `Set requires a POST request` and does not call `SetSoftware`.
- `POST /Sensor?action=Set&id=<sensor>&value=null` calls `IControl.SetDefault()`.
- `POST /Sensor?action=Set&id=<sensor>&value=<number>` parses with invariant culture.
- Parsed values that are `NaN`, positive infinity, negative infinity, blank, or otherwise invalid return JSON failure and do not call `SetSoftware`.
- Parsed finite values below `MinSoftwareValue` are written as `MinSoftwareValue`; values above `MaxSoftwareValue` are written as `MaxSoftwareValue`; values inside the range are preserved.
- Existing same-origin rejection for browser-originated POST `Set` remains in force.
- Existing GET reset behavior remains legacy mutating behavior and must stay documented until a separate accepted spec changes it.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | None. The browser dashboard remains read-only and does not call `/Sensor` writes. |
| Settings/config | None. |
| Remote web/API | GET `/Sensor` failure shape becomes stable JSON; POST `Set` validates and clamps control values before writing. |
| Logging/files | None. |
| Hardware/admin flow | No new hardware access path. Existing write path receives safer values. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Small isolated changes in `HttpServer`; tests pin local behavior. |
| `net472` vs `net10.0-windows` | Avoid framework-specific helpers; use existing `float` APIs and simple comparisons. |
| DPI/multi-monitor | Not UI-affecting. |
| Hardware/admin rights | No new hardware capability; values are constrained before existing control write. |
| Existing settings/users | No setting migration. Legacy POST `Set`, `value=null`, and reset URLs remain available. |

## 7. Acceptance Criteria

- [x] GET `/Sensor` missing-id and invalid-request failures return JSON fail payloads instead of unhandled server errors.
- [x] GET `action=Set` remains rejected and does not call `SetSoftware`.
- [x] POST/control write helper rejects blank, malformed, `NaN`, and infinite values.
- [x] POST/control write helper clamps finite values to the control's declared software range.
- [x] `value=null` still restores default control mode.
- [x] Existing `data.json` golden contract remains green.
- [x] The app builds for `net10.0-windows` and `net472`.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Test sensor API and data contract | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | 0 failures |
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| Dashboard regression guard | `node webtests\selftest.node.js` | `SELFTEST PASS 227/227` |
| Optional live smoke | Launch rebuilt app; probe `/Sensor?action=Get&id=/missing` | JSON failure response |

## 9. Open Decisions — grounded in the live tunnel deployment (2026-07-07)

The server is exposed to the public internet on demand via Cloudflare Tunnel — but **never directly**. The public edge is a middle proxy, not `:8085`:

`telemetry.seviq.org` (public HTTPS) → `cloudflared` → `127.0.0.1:8095` (`d:\scripts\tunnels\telemetry-public-proxy.mjs`, the mid proxy) → `127.0.0.1:8085` (this server, reached only from localhost by the proxy).

Public reads are **intended** while the tunnel runs. Because every public request is mediated by the `:8095` proxy, that proxy — not `:8085` — is the surface a fix must target first. This moves the decisions below from hypothetical to active.

**Exposure trace (primary-source):**

- **Hardware writes are not publicly reachable — but only incidentally.** The proxy issues `fetch(path)` with no method, body, or `Authorization` forwarded (`telemetry-public-proxy.mjs:6-14`), so every public request reaches `:8085` as a header-less **GET**. `POST /Sensor?action=Set` cannot traverse it, and the query-string write vector (`?action=Set&…&value=…`) is caught by the recently-added `GET action=Set` rejection (`HttpServer.cs:282-286`). Belt-and-suspenders — but pointing any tunnel at `:8085` directly, or teaching the proxy to forward POST, re-exposes `IControl.SetSoftware` instantly.
- **Reset routes ARE publicly reachable right now.** `GET /ResetAllMinMax` (`HttpServer.cs:519-528`) and `GET /Sensor?action=ResetMinMax` (`HttpServer.cs:358-364`) are GETs, so the proxy forwards them; they run unauthenticated and wipe min/max history. Low severity (telemetry history, not hardware/exfil), live whenever the tunnel is up.

**Confirmed live config + trust model (2026-07-07):** the persisted `<exe>.config` has `listenerIp="+"` (all interfaces), `authenticationEnabled="false"`, `runWebServerMenuItem="true"` — so `:8085` is currently bound LAN-wide with no auth. The operator has **accepted the LAN risk**: this is a single-occupant, single-user subnet, so the direct-`:8085` surface is trusted. That narrows the *untrusted* surface to exactly one thing: **what the `:8095` proxy forwards to the public internet.** Under that trust model:

- The only live untrusted gap is **public reset-over-GET** (above).
- Public hardware writes stay closed by the proxy's GET-downgrade **and** the `GET action=Set` rejection; the `Set` write-enable setting and a loopback bind become **optional defense-in-depth**, not required.

**Why app-level Basic auth is the wrong control for the tunnel path:** auth today is all-routes-or-nothing (`HttpServer.cs:131`, `HttpServer.cs:461-472`), and the proxy does not forward `Authorization`, so enabling it would 401 the proxy and break the intended public reads. Remote auth belongs at the edge (Cloudflare Access), not the origin leg.

**Resolved direction — minimum public-edge guard:**

| Layer | Action | Why |
|---|---|---|
| Edge (proxy / Cloudflare) — implemented minimum | Keep `telemetry-public-proxy.mjs` GET-only, block reset routes, and make `Start-SevIQTelemetryPublic.ps1` self-test the public proxy before starting/reporting the tunnel. | Matches the accepted trust model: LAN `:8085` is trusted; public reads are intended; public mutations must fail closed. |
| Server (this fork) — optional future hardening | Reset → POST-only; `Set` write-enable setting (default off); per-route auth via `AuthenticationSchemeSelectorDelegate`, only if `:8085` is ever fronted directly. | Defense-in-depth if the origin is exposed directly or the proxy later forwards POST. Not required for the current `:8095` proxy path. |

| Decision | Status | Direction |
|---|---|---|
| Should POST `action=Set` require Basic auth while reads stay anonymous? | Trigger fired (tunnel is live); app Basic auth rejected for the tunnel path (breaks public reads via proxy). | Edge auth (Cloudflare Access) for remote; per-route `AuthenticationSchemeSelectorDelegate` only for a direct `:8085` exposure. Never the global gate. |
| Should reset routes become POST-only? | Public reset-over-GET is now blocked by the proxy and launcher guard. | Defer server breakage. Keep as optional future defense-in-depth unless direct `:8085` exposure becomes untrusted. |
| Should writes have a separate explicit enable setting? | Optional (defense-in-depth): LAN is trusted and writes are not publicly reachable, so this only guards a future proxy change that forwards POST. | Deferred. If added later: default off; failure JSON must name the setting so `LiquidCool.py` authors see why `Set` returns fail. |

*Scope: based on the tunnel scripts in `d:\scripts\tunnels` and the persisted `<exe>.config`. LAN/direct-`:8085` access is operator-accepted (trusted single-user subnet); the only untrusted path is the `:8095` proxy. An ad-hoc tunnel pointed straight at `:8085` would bypass the proxy and expose writes — out of scope unless such a tunnel is created.*

## 10. Implementation Notes

- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` now routes GET `/Sensor` through `HandleGetSensorRequest`, which catches failures and returns JSON failure dictionaries.
- `SetSensorControlValue` now validates finite numeric input and clamps to the target `IControl` software range before calling `SetSoftware`.
- `LibreHardwareMonitor.Tests/HttpServerSensorApiTests.cs` covers GET failure shape, GET write rejection, invalid control values, default restore, and clamping.
- `D:\Scripts\tunnels\Start-SevIQTelemetryPublic.ps1` now asserts the public proxy contract before starting/reporting Cloudflare Tunnel: `/data.json` must return 200, `/ResetAllMinMax` and `/Sensor?action=ResetMinMax...` must return 403, and GET `action=Set` must return the server-side no-write failure.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-07 | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | PASS | 55/55 tests passed; includes `DataJsonGoldenTests` and new `HttpServerSensorApiTests`. Existing xUnit2020 warning in `DataJsonGoldenTests.cs:61`. |
| 2026-07-07 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | PASS | 0 warnings, 0 errors. |
| 2026-07-07 | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | PASS | 0 warnings, 0 errors. |
| 2026-07-07 | `node E:\SQ_HQ\Monitoring\sq-librehw\webtests\selftest.node.js` | PASS | `SELFTEST PASS 227/227`. |
| 2026-07-07 | Live rebuilt app PID `30508`; `GET /Sensor?action=Get&id=/missing`; `GET /Sensor?action=Set&id=/missing&value=55`; `GET /data.json` | PASS | Missing sensor and GET Set both returned HTTP 200 JSON failure; `data.json` returned HTTP 200. |
| 2026-07-07 | `pwsh -NoProfile -Command '$null = [scriptblock]::Create((Get-Content -Raw "D:\Scripts\tunnels\Start-SevIQTelemetryPublic.ps1"))'`; `node --check D:\Scripts\tunnels\telemetry-public-proxy.mjs` | PASS | Launcher parses; proxy JS parses. |
| 2026-07-07 | `pwsh -NoProfile -File D:\Scripts\tunnels\Start-SevIQTelemetryPublic.ps1 -SkipDns` | PASS | Started/confirmed proxy PID `62280`, tunnel PID `10580`; guard reported `/data.json=200`, reset routes `403`, GET Set `200` fail. |
| 2026-07-07 | Public edge probes for `https://telemetry.seviq.org/data.json`, `/ResetAllMinMax`, `/Sensor?action=ResetMinMax&id=/missing`, `/Sensor?action=Set&id=/missing&value=55` | PASS | Public data read returned 200; reset routes returned 403; GET Set returned JSON failure without write. |
