# Local UI Customizations (beyond the Graph Menu spec)

This fork ("Sev IQ") carries local changes on top of upstream LibreHardwareMonitor that are
**not** covered by [`feature-graph-menu.md`](feature-graph-menu.md). They were delivered in the
initial graph-menu series (`7b0e079`, `1f4225c`, `bb432e1`, `dc424c5`) and later fork work. They
are recorded here for traceability so future upstream merges and reviewers know they are
intentional. This catalog was re-audited on 2026-07-06 against `upstream/master` `9837983`; a later
dashboard branch commit moved the preview-route work out of the dirty working tree.

## Current audit snapshot (2026-07-06)

- `git rev-list --left-right --count HEAD...upstream/master` reports `108 11` from merge-base
  `abfc4f5705419d62cd6000f45a92563415c165fc`: this fork has a large intentional local feature/fix
  stack, and upstream has 11 commits not literally present in this checkout.
- Upstream #2382, #2384, #2386, #2390, and #2411 are content-covered or superseded locally even
  though `git cherry -v HEAD upstream/master` shows them as not patch-identical. Treat the next
  upstream sync as a conflict-reviewed merge/cherry-pick, not a blind "missing fixes" import.
- Sync-sensitive local areas are: `HttpServer.cs` (JSON contract, resource serving, preview routes),
  `MainForm.cs` (PawnIO resource extraction and UI wiring), `.github/workflows/master.yml` (fork-safe
  publish behavior), project/package files (`Directory.Packages.props` central package management),
  web dashboard assets/tests, and the local feature-spec docs.

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
  addition beyond upstream #2382. As of upstream `9837983` (#2411), upstream has also fixed the
  PawnIO manifest-resource lookup; the local fork already had equivalent content. No
  `data.json`/contract change.

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
  touched to build it. The data.json/CSV golden contract tests (`LibreHardwareMonitor.Tests`) stay
  green — see Verification below.
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
    live reading. In the subsystem panels these metadata rows are grouped under `Limits`, not
    `Temperature`, so drive warning/critical thresholds do not read as fake live drive temperatures.
  - **SSD/NVMe "Life" level** sensors (`type === 'Level'`, inverted thresholds): `< 5` -> `crit`,
    `< 20` -> `warn`, else `ok`.
  - Everything else (Load, Power, Clock, Voltage, Current, Fan, Data throughput, …) is `info` —
    displayed on its card/row but never health-judged: `info` sensors carry no state chip (see
    "Card anatomy v2" below) and never populate the placard.
  - `SQ.RANK` (`crit > warn > ok > info/off`) exists to *rank* statuses for sorting/worst-of
    purposes only — e.g. the placard's worst-first ordering, a subsystem panel header's worst-child
    lamp — not to aggregate them into one dashboard-wide verdict (that aggregate verdict/census was
    removed in v2; see "De-opinionated masthead" below).
- **Dashboard-only noisy sensor suppression** (`console.js` `SQ.isDashboardSuppressedSensor`):
  the live Nuvoton NCT6701D board exposes known-bad local temperature inputs
  `/lpc/nct6701d/0/temperature/3`, `/temperature/5`, and `/temperature/6`; the web dashboard hides
  them before hero/card rendering so they do not headline the Board panel. Numbered NVMe aux
  temperature rows such as `Temperature #2` are hidden until dashboard-observed motion proves they
  move by more than 1 C across at least five poll samples, because runtime CSV evidence showed these
  static rows can otherwise masquerade as the hottest drive temperature. This is intentionally
  client-side only: `data.json`, `/metrics`, CSV logging, and the desktop sensor tree still expose
  the raw LibreHardwareMonitor readings for auditability.
- **Background treatment** (`console.css`): the web console keeps the subtle radial cockpit glow but
  no longer draws the page-level grid background.
- **Auto-heuristic hero gauges** (`SQ.pickHero`): the top Primary Flight Display strip
  auto-selects headline metrics with no per-machine config — CPU package temp (AMD
  `Tctl/Tdie` or Intel `CPU Package`)/Total Load/Package Power when a `cpu`-class sensor is present,
  GPU Core Temp/Junction/Load/Package Power when a `gpu`-class (NVIDIA) sensor is present, overall
  RAM Load (the `Total Memory` node), the single hottest non-limit NVMe temperature, and — **fans
  first** (v2) — up to 4 active fans (`Type === 'Fan'`, `raw > 0`, sorted rpm-descending), with the
  hero cap raised from 9 to **12**; on a maximal host (all 9 base heroes present) the slowest
  active fan can still fall past the cap — pin it if you want it guaranteed. Selection keys on standard LHM sensor names; a gauge
  is simply omitted (no error) if a given host names that sensor differently. A fan that spun
  earlier this session but currently reads 0 rpm is a real reading and stays visible at 0 if already
  selected (that's signal, not noise, worth keeping); a `null` fan reading is never counted as
  active. Bounded metrics (CPU/GPU/drive temps, Load) get an arc gauge from their real range
  (`h.bounded = [lo, hi]`, e.g. CPU Temp `[30, 95]`, GPU Temp `[25, 92]`): the SVG arc fraction is
  `(raw - lo) / (hi - lo)`, clamped and guarded against non-finite values (`arcSVG`) so a missing
  reading renders an empty arc, not a `NaN`-driven full one. Power, Fan, and Clock readouts have no
  natural hardware bound and instead get a **speedometer arc** — see the next bullet.
- **Speedometer arcs on unbounded metrics (v2)** (`SQ.speedoRange`, `SQ.niceCeil`): this is
  card-rendering behavior, not hero-selection behavior — it applies to *any* card showing a Fan,
  Power, or Clock sensor (hero, pinned, or a sensor forced to `gauge` style; see "Per-card style
  override" below), not only the auto-selected heroes. The arc's ceiling is
  `niceCeil(max(RawMax from data.json, client-observed session max, current raw))`, rounded **up**
  the 1-2-5 ladder (e.g. 87 W -> 100, 1740 rpm -> 2000); the ladder is coarse for some inputs (e.g.
  clock speeds) but is applied consistently rather than hand-tuned per sensor type. The ceiling is
  shown as a small, muted `/ N` label next to the value (`.ceil`) so the arc can never be mistaken
  for a hardware-rated maximum — it's an honestly-derived display scale. Every number on the card
  still comes from measured `data.json`/session history; nothing is invented.
- **De-opinionated masthead (v2)**: the masthead's **Thermal Verdict pill** (`GO`/`WATCH`/`CRITICAL`
  lamp + label) and the **OK/WATCH/CRIT census** chip row were removed from `index.html` and from
  `console.js` `render()` — the dashboard no longer computes or displays one aggregate judgement over
  every sensor. The warn/crit **placard** (`renderPlacard`) is unchanged in spirit and stays: it
  renders only when at least one sensor is genuinely over its warn/crit band ("Thermal Watch"/
  "Thermal Alert", worst status first) and is hidden the rest of the time — a report of measured
  over-band sensors, not an opinion. Masthead controls are now: freshness dot/text, rate slider,
  Pause, Graphs, Theme, Customize.
- **Card anatomy v2 — two channels, two meanings** (`cardEl`, card rules in `console.css`): a card's
  **rail + chip communicate health STATE**; its **icon + value color communicate metric TYPE** — two
  independent channels on the same card.
  - **State** (`s-ok`/`s-warn`/`s-crit`/`s-off`; `info` is not a health state): a 3px left rail
    (`.cell::before`, state-colored) plus, only on sensors the status model actually health-judges
    (non-limit Temperature, and Level sensors whose text contains "life"), a small `OK`/`WATCH`/
    `CRIT` chip (`STGLYPH`/`STLABEL`). Info-class cards (power, clock, fan, load, …) render no chip
    at all; `off` (raw `null`) shows no chip either.
  - **Type** (`SQ.kindOf`: `temp`/`load`/`fan`/`power`/`clock`/`data`): a small inline `currentColor`
    SVG icon plus the big numeric value, both colored via a per-type CSS custom property set on the
    card (`--tc: var(--t-<kind>)`) — `--t-temp` amber, `--t-load` green, `--t-fan` cyan, `--t-power`
    violet, `--t-clock` blue, `--t-data` a neutral muted grey — each defined for both themes.
  - **Fixed heights**: every card is `132px` tall in compact mode and `172px` once its sparkline is
    on (`.cell` / `.cell.graph-on`), so the card grid never goes ragged; an empty context slot stays
    blank rather than reflowing the card.
  - **Graph-mode sparkline**: a filled, type-colored area chart (`sparkAreaSVG` — a translucent
    `<polygon>` fill plus a `<polyline>` stroke, both `var(--tc)`) built only from the session's own
    polled history (`SQ.historyFor`), drawn across the card's bottom edge; the exact numeric value
    stays the primary readout and the graph is supporting context.
- **Honesty rules (v2)**: a `null` `RawValue` always renders as an em dash ("—"), never `0` and
  never a stale-looking formatted string — `cardEl` (and the panel-row equivalent, `rowEl`) checks
  `raw == null` before any value formatting. Context lines (the min/max, or "peak", range under the
  value) show only measured values: `rangeMarkup` omits the line entirely when nothing was measured,
  and the card's context row reserves its vertical space regardless (`min-height`), so its absence
  never reflows the card. Freshness stays a single, global signal — the masthead freshness dot/text
  is the only freshness indicator; there is no per-card "updated Ns ago" line implying any one sensor
  was measured more recently than its neighbors. When a poll fails, `tick()` adds a `stale` class to
  `<body>`, which dims and desaturates the entire console at once (`body.stale main`) — every card
  enters the stale look together, because every sensor really did arrive (or fail to arrive)
  together in the same poll.
- **Trend arrows (v2)** (`SQ.trendFor`, `SQ.TRENDBANDS`): each card computes a rate of change from
  the last ~30 s of the session's own polled history (mean of the newer half minus mean of the older
  half, divided by half the window), against a per-kind deadband below which no arrow is drawn at all
  (never a "stable" label — omission is the honest signal): Temperature `0.05 °C/s`, Fan
  `30 rpm/min`, Power `1.5 W/s`, Load `0.5 %/s`, Clock `15 MHz/s`. Outside the deadband, an up/down
  glyph plus the signed rate and unit appear next to the value. A one-sided hysteresis stops the
  arrow from strobing on sensor noise: once armed, a direction survives as long as the rate stays
  same-signed down to **half** the deadband, and only flips once the opposite-signed rate itself
  clears the **full** deadband. Sensors with no `TRENDBANDS` entry (kind `data`) never show a trend.
- **Per-card style override (v2)** (`cardStyle` map inside `sq.dashboard.v1`, `SQ.cardStyleFor`):
  any sensor id can be pinned to `auto` (default; absent from the map) / `gauge` / `number` /
  `graph`. `auto` keeps the existing heuristic — an arc when a real or speedometer range exists,
  sparkline following the global Graphs toggle; `gauge`/`number` force the arc on/off; `graph` forces
  *this* card's sparkline on even while the global Graphs toggle is off. Precedence: explicit
  per-card style > global Graphs toggle > auto heuristic. Editable via a `<select>` next to each
  sensor in the Customize drawer's **Cards** tab (both the pinned-card reorder list and the sensor
  picker below it); setting it back to `auto` deletes the key rather than storing the word `"auto"`
  (`cleanCardStyleMap` also drops anything not one of the three explicit values on load).
- **Network panel collapse** (`renderPanels`): NIC sensors are excluded from the normal
  one-panel-per-hardware grouping and instead folded into a single collapsed-by-default "Network"
  panel containing only interfaces with nonzero `Throughput` (`active = nics with Throughput > 0`)
  — idle/virtual adapters (common on multi-NIC boards and VM hosts) don't clutter the panel grid.
- **Per-core row collapse** (CPU panels only): individual `Core #N` / hybrid-Intel `P-Core #N` /
  `E-Core #N` rows (`isCoreRow`, excluding Average/Max/Total summaries) are tucked behind a
  "+ N per-core …" toggle per sensor type, so a 16-core CPU doesn't dominate the panel.
- **Persistence** (all via `localStorage`): theme (`dark`/`light`, default dark), poll rate
  (seconds, default 2), pause state, and each hardware panel's collapsed/expanded state persist
  across page reloads as fields of the single versioned `sq.dashboard.v1` object — see the
  "Consolidated state" bullet below for the one-time migration off the legacy loose `sq.*` keys.
  A stored per-panel choice always wins over the code's default-collapsed hint (e.g. the Network
  panel defaults collapsed only until the user expands it once; tri-state map, absent = default).
- **Dashboard customization state** (`sq.dashboard.v1`): hidden-sensor choices, default-hidden
  overrides, pinned cards, pinned/panel order, per-card style overrides (`cardStyle`), and the
  optional card-graph toggle are browser-local. This is intentionally separate from raw telemetry:
  hiding, pinning, or restyling affects only the web dashboard projection, not `data.json`,
  `/metrics`, CSV logging, or the desktop sensor tree.
- **Inline pin/hide**: hovering (or keyboard-focusing) a hero card, pinned card, or panel row
  reveals compact pin and hide controls. Pin mirrors the drawer's Cards tab; hide adds the sensor
  to the browser-local hidden list (reversible from the drawer's Hidden tab). Raw endpoints are
  unaffected.
- **Live drag reorder**: a drag grip on panel headers and pinned cards reorders panels and pinned
  cards directly on the page; the CSS-column masonry reflows on drop and the order persists in
  `sq.dashboard.v1`. Keyboard users reorder with the drawer's Up/Down buttons. Polling is
  suppressed for the duration of a drag.
- **Consolidated state**: theme, poll rate, paused, and per-panel collapse now persist inside the
  single versioned `sq.dashboard.v1` object; legacy `sq.theme`/`sq.rate`/`sq.paused`/`sq.panel.*`
  keys are migrated into it once on load and then removed.
- **Optional graphs and smoothed card motion**: the row bars remain the dense exact readout. Card
  sparklines are opt-in — via the global Graphs toggle, or forced on one card at a time by its
  per-card style override (v2; see "Per-card style override" above) — and use only the current
  browser session's recent poll history. Gauge arcs (hero and pinned cards alike) are visually
  damped between polls so fast-moving values do not jump as hard at a 2-second refresh cadence,
  while the displayed numeric value remains the latest `data.json` value.
- **Self-test**: `webtests/console.test.html` loads `console.js` in a `window.SQ_NO_BOOT = true`
  harness (so it exposes the pure `SQ` model functions without booting the live poller/DOM
  renderer against a real page), fetches the fixture `webtests/fixture.data.json`, and asserts
  `classOf`, `splitValue`, `statusOf` (including the GPU junction-band override and inverted SSD
  Life thresholds), `pickHero` (bounded vs. unbounded hero selection, plus v2 fan promotion), and
  the v2 model helpers `niceCeil`/`speedoRange`/`cardStyleFor`/`trendFor` (deadband + hysteresis)
  against known-good values. To run it: serve the repo root over HTTP (e.g. `python -m http.server`
  or any static server so the absolute `/LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`
  and `/webtests/fixture.data.json` paths resolve) and open `webtests/console.test.html` in a
  browser; it prints `SELFTEST PASS n/n` (or `FAIL`) plus a per-assertion log to the page. The same
  assertions (`webtests/console.tests.js`) also run headlessly via `node webtests/selftest.node.js`,
  which `eval`s `console.js` under a minimal `window.SQ_NO_BOOT` shim — this is the entry point used
  for command-line/agent verification.

## Web dashboard: versioned preview routes (temporary dev surface, 2026-07-06)

- **Preview route server support** (`HttpServer.cs`, spec:
  [`feature-web-dashboard-versioned-routes.md`](feature-web-dashboard-versioned-routes.md)). The
  stable dashboard remains at `GET /`; preview dashboards are served under explicit roots such as
  `GET /dash/cardtruth/`. Missing preview roots return `404` instead of falling back to `/`.
- **Separate preview assets** live under
  `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/`, with the stable dashboard still
  using `Resources/Web/`. The root dashboard now exposes a compact Pages menu linking the preview,
  `/data.json`, and `/metrics`.
- **No telemetry contract change.** Preview pages must fetch root-absolute APIs such as
  `/data.json`; `/dash/<version>/data.json` is a static-asset path, not a new API. Stable dashboard
  state remains `sq.dashboard.v1`; the current preview uses `sq.dashboard.preview.cardtruth` so test
  layouts cannot corrupt the stable dashboard.
- **Route regression tests** are in `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs`, covering
  root, preview HTML/CSS/JS, root API paths, and missing preview routes. This route work was present
  in the dirty working tree during the 2026-07-06 audit and was then committed on
  `feat/web-dashboard-v3-card-first`.
- **Temporary route lifecycle.** `cardtruth` is not intended to remain as a permanent extra page once
  the selected card-first behavior is synced into `/`. At promotion time, retire the separate route
  and Pages-menu entry; any surviving visual treatment should become a root dashboard Theme
  dropdown/view option under stable `sq.dashboard.v1` state.

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
- 2026-07-04: Console v2 (honest cards, speedometers, fans-first, de-opinionated masthead) shipped —
  see [`2026-07-04-console-v2-cards-design.md`](superpowers/specs/2026-07-04-console-v2-cards-design.md)
  and this file's updated web-dashboard section. `dotnet test LibreHardwareMonitor.Tests
  -p:Platform=x64` **27/27 passed, 0 failures** (contract untouched — v2 is a pure client change).
  `dotnet build LibreHardwareMonitor.Windows.Forms -c Release -f net10.0-windows -p:Platform=x64`
  built **0 warnings / 0 errors**; the normal output path was locked by the running `Libre Hardware
  Monitor` instance (left running, per instruction), so the redirected `-p:OutDir=` path was used
  (`-p:BaseOutputPath=` does not work in this repo — the csproj hardcodes `OutputPath`). `node
  webtests/selftest.node.js` → **SELFTEST PASS 85/85**, including all new v2 model-helper cases
  (`kindOf`, `niceCeil`, `speedoRange`, `cardStyleFor`, `trendFor` deadband/hysteresis, hero fan
  promotion). DOM/CSS card-anatomy rendering (rail/chip vs icon/color, fixed heights, filled
  sparkline, masthead verdict/census removal) is self-test-adjacent but not itself unit-tested —
  confirmed by direct reading of the shipped `console.js`/`console.css`/`index.html` against the
  spec; live-browser E2E remains a user follow-up per the spec's Verification section.
- 2026-07-06: Upstream/customization audit updated this catalog and
  [`discovery-librehw-sync-upgrade.md`](discovery-librehw-sync-upgrade.md). No product build/test was
  run for the docs-only audit. Evidence commands: `git fetch upstream --prune`, `git fetch origin
  --prune`, `git rev-list --left-right --count HEAD...upstream/master` (`108 11`), `git diff --stat
  upstream/master...HEAD` (109 committed local-change paths), `git diff --stat` (10 dirty tracked
  paths), and `git ls-files --others --exclude-standard` (9 untracked working-tree paths). Existing
  route/gauge verification for the in-flight dashboard work is recorded in
  [`feature-web-dashboard-versioned-routes.md`](feature-web-dashboard-versioned-routes.md) and
  [`reviews/review-2026-07-06-dashboard-menu-gauge-correctness.md`](reviews/review-2026-07-06-dashboard-menu-gauge-correctness.md).
- 2026-07-06: Follow-up on the merged v3 dashboard branch resolved the stale dirty-state notes
  and clarified that `/dash/cardtruth/` is a temporary dev surface. Stable `/` now carries the
  route/menu, gauge guard, host-ID cleanup, hardware-ID panel/hero work, range truth, multi-tab
  telemetry save guard, visible card/row expansion actions, primary-card order, and stable row
  ordering. The preview copy remains only as an isolated `sq.dashboard.preview.cardtruth` comparison
  namespace until it is retired or any surviving visual treatment is promoted into the root Theme/view
  selector.
