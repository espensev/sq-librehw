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
- **Millisecond row timestamps** (spec: [`feature-csv-millisecond-timestamps.md`](feature-csv-millisecond-timestamps.md), GH #9).
  The row timestamp used the general `"G"` specifier (`MM/dd/yyyy HH:mm:ss`, no fractional seconds), so
  at faster-than-1 Hz logging ~25% of samples collapsed onto a duplicate whole second and lost their
  ordering. Now formatted via `FormatRowTimestamp`/`RowTimestampFormat` as `MM/dd/yyyy HH:mm:ss.fff`.
  **Contract change:** the CSV `Time` column gains `.fff` milliseconds; the leading fields are
  unchanged, so second-resolution readers are unaffected and the downstream ThermalTrace parser
  (already updated) accepts both forms. Deliberate local-fork divergence from upstream's `"G"`.

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
  (`index.html` resource lookup) was a separate issue, **fixed 2026-06-25** (next bullet).
- **Web UI resource lookup for the renamed assembly** (`HttpServer.cs`; upstream #2382 backport).
  `ServeResourceFileAsync`/`ServeResourceImageAsync` hardcoded the `LibreHardwareMonitor.Resources.`
  manifest-resource prefix, but this fork's assembly is `LibreHardwareMonitor.Windows.Forms`, so the
  real prefix is `LibreHardwareMonitor.Windows.Forms.Resources.*` — every static asset (`index.html`,
  css, js, icons) returned `404`; only `data.json` (a separate path) worked. Switched to
  `Assembly.GetExecutingAssembly().GetName().Name + ".Resources."`. The **same latent hardcoded prefix
  in `MainForm.ExtractPawnIO`** (PawnIO_setup.exe extraction) was fixed too — that twin is a local
  addition beyond upstream #2382. No `data.json`/contract change.

## Web dashboard: SQ Telemetry Console (replaces the legacy jQuery/Knockout UI)

The stock Open Hardware Monitor–era web dashboard (jQuery 1.7.2, jQuery UI, jQuery `tmpl`,
Knockout 2.1 + `knockout.mapping`, `jquery.treeTable`, `ohm_web.js`/`.css`, `css/custom-theme/`)
has been **removed** and replaced with a self-contained dashboard: `index.html`, `console.css`,
`console.js` under `LibreHardwareMonitor.Windows.Forms/Resources/Web/`. It is served at the same
route (bare `GET /`) via the existing `HttpServer.cs` embedded-resource lookup — no server-side
change. `favicon.ico` and `images/` (referenced by `data.json` `ImageURL`s) are kept as-is.

- **Zero contract change.** The console is a pure client that polls the existing
  `GET /data.json` endpoint (`fetch('data.json', {cache:'no-store'})`) on an interval; it does not
  read or depend on any new server endpoint, and nothing in `HttpServer.cs`/`AssemblyVersion` was
  touched to build it. The 7 data-contract tests (`LibreHardwareMonitor.Tests`) stay green — see
  Verification below.
- **Status model** (`console.js` `SQ.statusOf`/`tempStatus`/`SQ.deriveLimits`): every sensor is
  classified `ok` / `warn` / `crit` / `info` / `off` (`raw == null` -> `off`). Only two sensor
  shapes are ever alarmed:
  - **Temperature**, banded per hardware class (`TEMPBANDS`): CPU `[85, 95]`, GPU/iGPU core
    `[83, 92]` (GPU/iGPU "Junction"/"Hot Spot" rows use a separate `[95, 105]` band), NVMe
    `[70, 80]`, DIMM `[55, 85]`, motherboard/RAM sensors have no band (`info`). NVMe/DIMM prefer
    the drive/module's own self-reported `Warning`/`Critical Temperature` limit sensors
    (`SQ.deriveLimits`, keyed by `hwid`) over the static band when present. Any sensor whose text
    looks like a limit/threshold readout itself (`isLimitSensor`: "limit", "warning temperature",
    "critical temperature", "resolution") is always `info`, never alarmed — it is metadata, not a
    live reading.
  - **SSD/NVMe "Life" level** sensors (`type === 'Level'`, inverted thresholds): `< 5` -> `crit`,
    `< 20` -> `warn`, else `ok`.
  - Everything else (Load, Power, Clock, Voltage, Current, Fan, Data throughput, …) is `info` —
    displayed but never drives the verdict lamp or census.
  - The masthead verdict lamp and OK/WATCH/CRIT census are derived from the worst non-`info`,
    non-`off` status across all sensors (`SQ.RANK` ordering `crit > warn > ok > info/off`).
- **Auto-heuristic hero gauges** (`SQ.pickHero`): the top Primary Flight Display strip
  auto-selects up to 9 headline metrics with no per-machine config — CPU Tctl/Total Load/Package
  Power when a `cpu`-class sensor is present, GPU Core Temp/Junction/Load/Package Power when a
  `gpu`-class (NVIDIA) sensor is present, overall RAM Load, and the single hottest non-limit NVMe
  temperature. **Only bounded metrics get an arc gauge** (`h.bounded = [lo, hi]`, e.g. CPU Temp
  `[30, 95]`, GPU Temp `[25, 92]`): the SVG arc fraction is `(raw - lo) / (hi - lo)`, clamped and
  guarded against non-finite values (`arcSVG`) so a missing reading renders an empty arc, not a
  `NaN`-driven full one. Power and Clock readouts have no natural upper bound, so they render as
  plain numeric readouts with no arc — this was a deliberate correctness fix, not an oversight.
- **Network panel collapse** (`renderPanels`): NIC sensors are excluded from the normal
  one-panel-per-hardware grouping and instead folded into a single collapsed-by-default "Network"
  panel containing only interfaces with nonzero `Throughput` (`active = nics with Throughput > 0`)
  — idle/virtual adapters (common on multi-NIC boards and VM hosts) don't clutter the panel grid.
- **Per-core row collapse** (CPU panels only): individual `Core #N` / hybrid-Intel `P-Core #N` /
  `E-Core #N` rows (`isCoreRow`, excluding Average/Max/Total summaries) are tucked behind a
  "+ N per-core …" toggle per sensor type, so a 16-core CPU doesn't dominate the panel.
- **Persistence** (all via `localStorage`, keys prefixed `sq.`): theme (`sq.theme`, `dark`/`light`,
  default dark), poll rate (`sq.rate`, seconds, default 2), pause state (`sq.paused`), and each
  hardware panel's collapsed/expanded state (`sq.panel.<hardware name>`) all persist across page
  reloads. A stored per-panel choice always wins over the code's default-collapsed hint (e.g. the
  Network panel defaults collapsed only until the user expands it once).
- **Self-test**: `webtests/console.test.html` loads `console.js` in a `window.SQ_NO_BOOT = true`
  harness (so it exposes the pure `SQ` model functions without booting the live poller/DOM
  renderer against a real page), fetches the fixture `webtests/fixture.data.json`, and asserts
  `classOf`, `splitValue`, `statusOf` (including the GPU junction-band override and inverted SSD
  Life thresholds), and `pickHero` (bounded vs. unbounded hero selection) against known-good
  values. To run it: serve the repo root over HTTP (e.g. `python -m http.server` or any static
  server so the absolute `/LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` and
  `/webtests/fixture.data.json` paths resolve) and open `webtests/console.test.html` in a browser;
  it prints `SELFTEST PASS n/n` (or `FAIL`) plus a per-assertion log to the page.

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

## Build / versioning

- **Local build version stamping** (`LibreHardwareMonitor.Windows.Forms.csproj`,
  `StampLocalBuildVersion` target). Local (non-CI) builds embed a build-identifying `FileVersion`
  (`0.9.6.<dateRev>`, `dateRev = (Year-2020)*366 + DayOfYear`) and
  `ProductVersion`/`InformationalVersion` (`0.9.6+<shortsha>[-dirty].<date>`), so a freshly built exe
  is distinguishable from a stale one in Explorer's Details tab. **`AssemblyVersion` is deliberately
  left at `$(Version)` (0.9.6)** so the data.json `"Version"` field (`Assembly.GetName().Version`) and
  its golden contract test stay byte-stable. The target and the
  `IncludeSourceRevisionInInformationalVersion=false` toggle are guarded by
  `'$(GITHUB_ACTIONS)' != 'true'`, so CI keeps its `-ci<run_number>` scheme.
- **NuGet publish fork guard** (`.github/workflows/master.yml`; upstream #2386 backport). The
  `Publish to NuGet` step is guarded by
  `if: github.repository == 'LibreHardwareMonitor/LibreHardwareMonitor'`, so merges to this fork's
  `master` no longer fail there (`Missing -ApiKey`) and cannot publish the upstream-named package
  from a fork.

## Branding

- **Window title and tray tooltip** changed from `Libre Hardware Monitor` to
  `Libre Hardware Monitor - Sev IQ` (`MainForm.Designer.cs`, `SystemTray.cs`).

## Verification

- 2026-06-06: `net10.0-windows` and `net472` Release x64 app builds passed with 0 warnings and 0 errors using redirected temp `OutDir` paths. The ordinary `net10.0-windows` release output path was locked by a running `Libre Hardware Monitor` process, so it was not used for the compile check.
- 2026-06-06: Re-verified at the **normal** output path after closing the running app — `net10.0-windows` and `net472` (Release x64) both built with 0 warnings / 0 errors. Confirms the per-target manifest split (`app.manifest` vs `app.net472.manifest`) embeds cleanly on both frameworks (no `WFO0003`), and `requireAdministrator` remains in both manifests so hardware access is preserved on each target.
- 2026-06-07: Graph/sensor-tree UI review fixes implemented (see [`feature-graph-ui-review-fixes.md`](feature-graph-ui-review-fixes.md)): column-width persistence regression, GraphInputsForm BindingList subscription leak, plot recompute double-fire/fan-out, sub-minute time-axis label resolution, and the GetNiceAxisStep tie-break. `net10.0-windows` + `net472` Release x64 build 0/0; GUI-interaction paths verified by code reasoning (advisor-reviewed), manual checklist outstanding — not runtime-verified to the curl/CSV standard used for the web-server/identifier fixes.
- 2026-06-07: NVIDIA unique-identifier + CSV logger guard verified end-to-end (see [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md)). `net10.0-windows` + `net472` Release x64 built 0/0; after relaunch `data.json` had 0 duplicate `SensorId` (12VHPWR Pin 1 = `/voltage/1`, GPU Core Voltage = `/voltage/0`) and a fresh CSV header was 533/533 unique (was 453/452 with the `/voltage/0` collision).
- 2026-06-07: Remote Web Server JSON NaN/Infinity fix verified end-to-end (see [`feature-webserver-json-stream.md`](feature-webserver-json-stream.md)). `net10.0-windows` + `net472` Release x64 built 0/0; after relaunch `GET /data.json` returned HTTP 200 valid JSON (533 sensors) instead of hanging, NaN sensors (NIC "Network Utilization") serialized as `RawValue: null`, `GET /Sensor?action=Get` on a NaN sensor returned `value:null` with no hang, and `GET /metrics` stayed HTTP 200. Server auto-starts via persisted `runWebServerMenuItem=true`.
- 2026-06-13: CSV millisecond-timestamp fix implemented (see [`feature-csv-millisecond-timestamps.md`](feature-csv-millisecond-timestamps.md), GH #9). `dotnet test` 7/7 (5 new CSV-timestamp contract tests + 2 data.json golden); `net10.0-windows` + `net472` Release x64 built 0/0 (redirected temp `OutDir` — normal output path locked by the running app). Row `Time` column now emits `MM/dd/yyyy HH:mm:ss.fff`. Runtime CSV capture of `.fff` rows is the outstanding maintainer-launch step; the emitted format is the unit-pinned helper, so launch confirms wiring rather than format.
- 2026-06-25: Backported upstream auth fix #2390 (web-server stored-password double-encoding); added local build version stamping; backported web UI resource-prefix fix #2382 (+ `MainForm.ExtractPawnIO` twin) and NuGet fork guard #2386. `dotnet test ...Tests... -p:Platform=x64` 7/7; `net10.0-windows` Release x64 built 0/0. Web UI verified end-to-end: `GET /` and `GET /index.html` now HTTP 200 (were 404), `data.json` still 200. `master` CI now green (previously failed at "Publish to NuGet" on every merge). EXE Details show `FileVersion 0.9.6.<dateRev>` / `ProductVersion 0.9.6+<sha>.<date>`.
- 2026-07-04: Legacy jQuery/Knockout/`jquery.treeTable`/jQuery-UI dashboard assets deleted (`js/*`,
  `css/jquery.treeTable.css`, `css/ohm_web.css`, `css/custom-theme/**`) now that the SQ Telemetry
  Console fully replaces them; `favicon.ico`/`images/`/`index.html`/`console.css`/`console.js` kept.
  No references to the deleted files remained in `HttpServer.cs` or any `.csproj` (the one
  `custom-theme` hit in `HttpServer.cs` is a generic hyphen-to-underscore resource-name sanitizer,
  not a path reference). Builds used redirected `OutDir` (the normal `bin\Release\...` path was
  locked by a running `Libre Hardware Monitor` instance): `net10.0-windows` Release x64 built 0
  warnings / 0 errors; `net472` Release x64 built 0 warnings / 0 errors. `dotnet test
  LibreHardwareMonitor.Tests -p:Platform=x64` (normal Debug output path, unaffected by the lock):
  **7/7 passed**, including the data.json/CSV golden contract tests — proving the deletion made no
  contract change.
