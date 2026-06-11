# Local UI Customizations (beyond the Graph Menu spec)

This fork ("Sev IQ") carries local changes on top of upstream LibreHardwareMonitor that are
**not** covered by [`feature-graph-menu.md`](feature-graph-menu.md). They were delivered in the
same commits (`7b0e079`, `1f4225c`, `bb432e1`, `dc424c5`) and are recorded here for traceability so future
upstream merges and reviewers know they are intentional.

## Sensor tree

- **Compact Mode** (`View > Compact Mode`, `MainForm.cs` `ApplySensorTreeLayout`). Opt-in, default
  off. When on: reduces row height, sets `GridLineStyle.None`, narrows the value column, and hides
  the Min/Max columns. The `Show Min` / `Show Max` items are disabled while compact is active so the
  checkmark cannot lie about visibility. On save, the pre-compact column widths are written back so
  the narrowed widths are not persisted.
- **Sensor-list bulk selection and keyboard access** (`treeView.SelectionMode =
  TreeSelectionMode.Multi`; spec:
  [`feature-sensor-list-bulk-selection.md`](feature-sensor-list-bulk-selection.md)). Multi-select
  context menus provide hide/unhide, graph, pen-color, tray, and gadget actions; type rows provide
  group visibility/plot actions; Del and Apps/Shift+F10 are supported; Graph Inputs supports
  multi-row toggling. Visibility changes persist `/hidden` only: they do not clear `Plot` or raise
  `PlotSelectionChanged`. Post-implementation corrections and ranked follow-ups are recorded in
  [`review-sensor-list-bulk-selection-follow-up.md`](review-sensor-list-bulk-selection-follow-up.md).

## Plot panel

- **Grid Density** (plot context menu: Off / Major / Normal / Fine). Default is **Fine**, which is a
  visible change from upstream's default grid. Fine uses a custom "nice step" algorithm
  (`GetNiceAxisStep`, factors {1, 2, 2.5, 5, 10}) targeting ~20 major divisions. This only changes
  gridline/tick rendering — it never touches sensor data, so it does not smooth, average, or
  downsample anything. Steps are recomputed per refresh but only re-assigned when they actually
  change, so the grid does not "pop" between frames.
- **Time-axis presets**: added `30 sec`, `1 min`, `2 min` to the plot's right-click Time Axis menu.
- **Time-axis label mode**: `Time Axis > Label Mode > Local Time / Elapsed` in the plot
  right-click menu. Local Time is the default and maps the existing relative X values back to local
  wall-clock labels; Elapsed preserves the prior label behavior.

## Library (`LibreHardwareMonitorLib`)

- **Averaging-accumulator reset** (`Sensor.cs`). The pre-existing 4-sample averaging
  (`_sum` / `_count`) is now reset wherever the `_values` graph buffer is cleared
  (`ValuesTimeWindow == Zero`, `ClearValues()`). This is a correctness fix: previously a clear left
  a stale partial sum that skewed the next averaged graph point. Exposed `Value` / `Min` / `Max`
  come from the raw value and are unaffected. This is a shared-library change, so it applies to all
  consumers of `ISensor.Values` (e.g. the Prometheus HTTP endpoint), not just the WinForms graph.
- **Unique NVIDIA GPU sensor identifiers** (`Hardware/Gpu/NvidiaGpu.cs`, spec:
  [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md), GH #4). On ASUS RTX
  "Astral" cards the 12VHPWR per-pin **voltage** sensors were created at indices 0–5, colliding with
  `GPU Core Voltage` at index 0 — two sensors shared `/gpu-nvidia/<n>/voltage/0` across `data.json`,
  Prometheus, CSV logging, and persisted plot state. Shifted the pins to 1–6 (matching the Current
  block) so every `Identifier` is unique. **Contract change:** `12VHPWR Pin 1..6` voltage ids move
  from `/voltage/0..5` to `/voltage/1..6`; `GPU Core Voltage` and the Current/Power pin ids are
  unchanged. Shared-lib edit — it's an upstream defect, so it's a candidate for an upstream report.

## CSV logger (`Logger.cs`)

- **One-sensor-one-column guard.** `SensorAdded` and `OpenExistingLogFile` now `break` on the first
  identifier match, so a single sensor can never be fanned into multiple columns. This fixes a latent
  Daily-rotation/hot-plug bug where a duplicated identifier silently overwrote one real sensor's data;
  with the NVIDIA fix above it is also defence-in-depth against any future identifier collision.

## Remote Web Server (JSON endpoints)

- **Non-finite sensor handling** (`HttpServer.cs`, spec: [`feature-webserver-json-stream.md`](feature-webserver-json-stream.md)).
  `GET /data.json` and `GET`/`POST /Sensor?action=Get` previously **hung the client** whenever any
  sensor reported `NaN`/`Infinity` (97 such sensors on the maintainer's board): `System.Text.Json`
  rejects non-finite floats by default, and the throw escaped a fire-and-forget handler so the
  response was never closed. Fixed by mapping non-finite raw readings to JSON `null` at the source
  (`SanitizeFloat` used in `GenerateJsonForNode` `RawMin/RawValue/RawMax` and in `HandleSensorRequest`'s
  `Get` case), plus a handler-level backstop (`HandleContextAsync` wraps `DispatchRequestAsync` in
  try/catch → `500` / finally → `response.Close()`). **API contract change:** non-finite readings now
  serialize as `null` instead of crashing the response. `/metrics` was already NaN-safe and is
  unchanged; the formatted `Value/Min/Max` strings are unchanged. The `GET /` web UI `404`
  (`index.html` resource lookup) is a separate, untouched issue.

## Modernization (traceable to `discovery-librehw-sync-upgrade.md`)

- **High DPI**: `Program.cs` `Application.SetHighDpiMode(SystemAware)` (under `NETCOREAPP`),
  `ApplicationHighDpiMode=SystemAware` in the csproj for non-`net472`, and a separate
  `Resources/app.net472.manifest` with the legacy `dpiAware` block for the .NET Framework target.
  The modern `app.manifest` stays free of manifest DPI settings so the `net10.0-windows` build
  remains warning-free while `net472` stays system-DPI aware.
- **`System.Web` removal**: `HttpServer.cs` now uses `request.QueryString` instead of
  `HttpUtility.ParseQueryString`, and the `System.Web` / `System.Configuration.Install` references
  are dropped. Behavior-preserving for the sensor API; clears the MSB3245 warning.
- **`Aga.Controls` multi-targeting** to `net472;net10.0-windows`, with `#if NETFRAMEWORK` guards
  around `Thread.Abort` (`AbortableThreadPool`), `SecurityPermission` attributes, and a
  `SYSLIB0050` pragma. **Behavior change on `net10.0-windows`:** `AbortableThreadPool` can no longer
  forcibly abort work items — `Cancel`/`CancelAll` with `allowAbort` become no-ops that report the
  item as still executing (`Thread.Abort` is unsupported on modern .NET). This is an accepted
  consequence of modernization, not a regression to fix.

## Branding

- **Window title and tray tooltip** changed from `Libre Hardware Monitor` to
  `Libre Hardware Monitor - Sev IQ` (`MainForm.Designer.cs`, `SystemTray.cs`).

## Verification

- 2026-06-06: `net10.0-windows` and `net472` Release x64 app builds passed with 0 warnings and 0 errors using redirected temp `OutDir` paths. The ordinary `net10.0-windows` release output path was locked by a running `Libre Hardware Monitor` process, so it was not used for the compile check.
- 2026-06-06: Re-verified at the **normal** output path after closing the running app — `net10.0-windows` and `net472` (Release x64) both built with 0 warnings / 0 errors. Confirms the per-target manifest split (`app.manifest` vs `app.net472.manifest`) embeds cleanly on both frameworks (no `WFO0003`), and `requireAdministrator` remains in both manifests so hardware access is preserved on each target.
- 2026-06-07: Graph/sensor-tree UI review fixes implemented (see [`feature-graph-ui-review-fixes.md`](feature-graph-ui-review-fixes.md)): column-width persistence regression, GraphInputsForm BindingList subscription leak, plot recompute double-fire/fan-out, sub-minute time-axis label resolution, and the GetNiceAxisStep tie-break. `net10.0-windows` + `net472` Release x64 build 0/0; GUI-interaction paths verified by code reasoning (advisor-reviewed), manual checklist outstanding — not runtime-verified to the curl/CSV standard used for the web-server/identifier fixes.
- 2026-06-07: NVIDIA unique-identifier + CSV logger guard verified end-to-end (see [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md)). `net10.0-windows` + `net472` Release x64 built 0/0; after relaunch `data.json` had 0 duplicate `SensorId` (12VHPWR Pin 1 = `/voltage/1`, GPU Core Voltage = `/voltage/0`) and a fresh CSV header was 533/533 unique (was 453/452 with the `/voltage/0` collision).
- 2026-06-07: Remote Web Server JSON NaN/Infinity fix verified end-to-end (see [`feature-webserver-json-stream.md`](feature-webserver-json-stream.md)). `net10.0-windows` + `net472` Release x64 built 0/0; after relaunch `GET /data.json` returned HTTP 200 valid JSON (533 sensors) instead of hanging, NaN sensors (NIC "Network Utilization") serialized as `RawValue: null`, `GET /Sensor?action=Get` on a NaN sensor returned `value:null` with no hang, and `GET /metrics` stayed HTTP 200. Server auto-starts via persisted `runWebServerMenuItem=true`.
