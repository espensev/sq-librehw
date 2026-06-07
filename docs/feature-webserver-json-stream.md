# Feature Spec: Remote Web Server JSON stream — non-finite (NaN/Infinity) sensor handling

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Verified <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-06-07
**Related docs:** [`feature-workflow.md`](feature-workflow.md), [`local-ui-customizations.md`](local-ui-customizations.md)
**Purpose:** make the remote web server's JSON endpoints return valid JSON instead of hanging when sensors report non-finite values.

## 1. Summary

The Remote Web Server's JSON endpoints (`GET /data.json` and `GET`/`POST /Sensor?action=Get`) hung
indefinitely whenever any sensor reported a non-finite reading (`NaN`/`Infinity`). The fix maps
non-finite raw readings to JSON `null` at the data source and adds a handler-level backstop so no
request can ever leave the connection open. The web server is enabled via
**Options → Remote Web Server → Run** (default off).

## 2. Problem and Motivation

On the maintainer's machine (ASUS ROG STRIX X870-F, Ryzen 9 9950X3D, RTX 5090) **97 sensors report
`NaN`** — unwired motherboard voltages (AVCC `/voltage/2`, +3V Standby `/voltage/7`, CPU Termination
`/voltage/9`, Voltage #11–#15), some temperatures, idle GPU clocks/loads, etc. This was corroborated
three independent ways:

- `GET /metrics` emits 97 `# HELP ... has an invalid value and was skipped.` markers;
- the `Log Sensors` CSV logs contain thousands of literal `NaN` tokens for those same columns;
- `GET /data.json` and `GET /` were compared against the same running server: `/` returned an
  instant `404`, `/metrics` returned `200` with ~131 KB of live data, but `/data.json` timed out
  every time, gzip or not.

Root cause: `System.Text.Json` rejects `NaN`/`+Infinity`/`-Infinity` for `float`/`double` by default.
`SendJsonAsync` and the `Sensor` GET path serialized raw `float?` readings directly, so the serializer
threw mid-response. The throw escaped an un-`try`/caught, fire-and-forget handler
(`ProcessRequestsAsync` does `_ = Task.Run(() => HandleContextAsync(...))`), so `response.Close()` was
never reached and the socket hung until the client timed out. `GET /metrics` was unaffected only
because `GeneratePrometheusResponse` already skips `float.IsNaN` values explicitly.

## 3. Goals and Non-Goals

**Goals**

- `GET /data.json` returns valid JSON promptly even when sensors report non-finite values.
- `GET`/`POST /Sensor?action=Get` behaves the same way for non-finite single-sensor reads.
- No request path can leave the HTTP response open on an unhandled exception.

**Non-goals**

- Changing the web UI (`GET /` currently returns `404` for `index.html`; that is a separate embedded
  resource-name issue and is out of scope here).
- Changing `/metrics` output (already NaN-safe).
- Changing the formatted display strings (`Text`/`Value`/`Min`/`Max`), control/set semantics, auth,
  gzip, or the enable/port/interface workflow.

## 4. Behavior Specification

- **Non-finite mapping.** Any raw sensor reading that is `NaN`, `+Infinity`, or `-Infinity` is
  serialized as JSON `null`. A `null` (no value) reading also serializes as `null`. Finite values are
  unchanged (numeric).
- **`/data.json`.** The per-sensor `RawMin`, `RawValue`, `RawMax` fields use this mapping. The
  formatted string fields (`Value`, `Min`, `Max`) are unchanged — they were already strings and never
  caused the failure.
- **`/Sensor?action=Get`.** The `value`, `min`, `max` fields use this mapping. The single sanitize
  site (`HandleSensorRequest`) is shared by both GET and POST, so both methods are covered. `format`
  is unchanged.
- **Backstop.** Request dispatch is wrapped so that on any unhandled exception the response status is
  set to `500` (best effort) and the response is always closed in a `finally`. Result: a handler bug
  degrades to a closed/`500` connection, never an indefinite hang.
- **Failure states.** Unknown sensor id, missing `action`/`value`, etc. continue to return the
  existing `{"result":"fail",...}` JSON for the POST path; on the GET `/Sensor` path such errors are
  now caught by the backstop and close the connection instead of hanging.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | None. |
| Settings/config | None. (`runWebServerMenuItem`, `listenerIp`, `listenerPort`, auth keys unchanged.) |
| Remote web/API | `data.json` `RawMin/RawValue/RawMax` and `Sensor` `value/min/max` now emit JSON `null` for non-finite readings instead of throwing. All finite values unchanged. `/metrics`, `/`, image, and POST-set paths unchanged. |
| Logging/files | None. CSV `Log Sensors` output is unchanged (still writes literal `NaN`). |
| Hardware/admin flow | None. App still runs elevated (`requireAdministrator`). |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| Upstream sync | Localized to `HttpServer.cs` (a fork-local file already diverged for `System.Web` removal). `SanitizeFloat` is a small private helper; the dispatch split is additive. |
| `net472` vs `net10.0-windows` | Fix is framework-agnostic (`float.IsNaN`/`IsInfinity`, `System.Text.Json`). Both targets built clean. |
| DPI/multi-monitor | Not UI-affecting. |
| Hardware/admin rights | Unchanged. |
| Existing settings/users | Consumers that previously parsed `RawValue` now must tolerate `null` for absent readings — this is the standard "no value" representation and replaces a response that previously never arrived. |

## 7. Acceptance Criteria

- [x] `GET /data.json` returns HTTP 200 and a complete, valid JSON body (no hang) while NaN sensors are present.
- [x] A known-NaN sensor appears in `data.json` with `RawValue: null` (verified: NIC "Network Utilization" `.../load/1`; note AVCC `/voltage/2` is only intermittently NaN).
- [x] A finite sensor retains a numeric `RawValue` (verified: NIC throughput, etc.).
- [x] `GET /Sensor?action=Get&id=<NaN sensor>` returns `value: null` (no hang).
- [x] `GET /metrics` still returns HTTP 200 with live data (unchanged surface).
- [x] Both `net10.0-windows` and `net472` Release x64 build with 0 errors.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Build modern app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Build legacy app | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` | 0 errors |
| JSON validity | `Invoke-WebRequest http://localhost:8085/data.json | %{ $_.Content } | ConvertFrom-Json` | parses without error |
| NaN → null | inspect AVCC `/voltage/2` node in parsed `data.json` | `RawValue` is `null` |
| No regression | `GET /metrics` | HTTP 200, live values |

## 9. Open Decisions

| Decision | Needed before | Current default |
|---|---|---|
| Represent non-finite as `null` vs `"NaN"` string (`JsonNumberHandling.AllowNamedFloatingPointLiterals`) | implementation | `null` — standard JSON, parseable by any client; `"NaN"` is non-standard |

## 10. Implementation Notes

- File: `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`.
- `SanitizeFloat(float?)` helper maps `NaN`/`Infinity`/no-value → `null`, finite → boxed `float`.
- Sanitize sites: `GenerateJsonForNode` (`RawMin/RawValue/RawMax`) and `HandleSensorRequest`'s `Get`
  case (`value/min/max`, shared by GET and POST).
- `HandleContextAsync` was split into a thin wrapper (try/catch → 500 / finally → `response.Close()`)
  plus `DispatchRequestAsync` (the former body, with its trailing self-close removed). This is the
  backstop for the whole handler class, not just JSON.
- Chose per-field `SanitizeFloat` over a shared `JsonConverter<float>` because the dictionary values
  are boxed as `object`, where converter dispatch on the boxed runtime type is easy to get subtly
  wrong; the explicit helper is unambiguous and there are only six call sites.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-06-07 | `-f net10.0-windows -c Release -p:Platform=x64` to redirected `OutDir` (app still running) | pass | 0 warnings / 0 errors (compile-check before deploy) |
| 2026-06-07 | `-f net10.0-windows` and `-f net472` Release x64 to real output path | pass | both 0 warnings / 0 errors |
| 2026-06-07 | `GET /data.json` after relaunch (was: hung indefinitely) | pass | HTTP 200, 155 KB, 533 sensors, `ConvertFrom-Json` valid |
| 2026-06-07 | NaN → null spot-check | pass | NIC "Network Utilization" (`.../load/1`) `RawValue: null`, formatted `Value:"NaN %"`; finite sensors numeric |
| 2026-06-07 | `GET /Sensor?action=Get&id=<NaN sensor>` | pass | HTTP 200 `{"value":null,"min":null,"max":null,"format":"{0:F1} %"}`, no hang |
| 2026-06-07 | `GET /metrics` regression check | pass | HTTP 200, live data (unchanged) |
| 2026-06-07 | Server auto-start | pass | `runWebServerMenuItem=true` in config → listener up ~5 s after launch, no manual toggle |
