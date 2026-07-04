# Web Dashboard → SQ Telemetry Console — Design

**Date:** 2026-07-04
**Status:** Approved (design), pending implementation plan
**Visual target:** `D:\Development\Thermals\SQ-control\docs\status\sq-telemetry-console-mockup.html` (built from a live `data.json` snapshot; rendered and reviewed in dark + light)

## Context

The monitor's built-in web server (`LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`) serves a legacy dashboard (`Resources/Web/index.html` + `ohm_web.js`) — the 2012 OpenHardwareMonitor UI: jQuery 1.7.2 + Knockout 2.1 + jQuery-UI + `jquery.treeTable`, which fetches `data.json` and renders the raw sensor tree as a plain 4-column table. It is functional but dated and hard to read at a glance.

This project replaces it with a **massively upgraded, purpose-built telemetry console** styled after two reference mockups the user authored (`readiness-dashboard.html` "flight readiness console" and `atlas-mockup.html` "glass cockpit"), blending both plus original elements. The goal is a live, glanceable, instrument-grade dashboard for viewing the machine's state over the LAN.

**Why this is low-risk:** the new dashboard is a pure client-side redesign that consumes the **existing, unchanged** `data.json`. No server code changes, no `data.json` schema change, no `AssemblyVersion` change — so the downstream ThermalTrace contract and the `DataJsonGoldenTests`/`CsvTimestampContractTests` are untouched.

## Goals / Non-goals

**Goals**
- Replace the legacy dashboard with a self-contained, framework-free console served from embedded resources.
- Auto-surface the machine's key metrics as hero gauges; group everything else into per-hardware panels.
- Theme-aware (dark default + light), blended glass-cockpit/readiness aesthetic, "cooler" than the reference admin look.
- Honest, per-hardware-class status coloring that does not cry wolf on stray/unlabeled sensors.
- Live auto-refresh with user controls, remembering preferences in `localStorage`.

**Non-goals**
- No change to `data.json`, the `/Sensor` API, `/metrics`, CSV, or `AssemblyVersion`.
- No server-side history/sparkline contract in v1. Optional card sparklines may use only the current browser session's client-side poll history.
- No hardware **control** writes from the dashboard (read-only view; the `/Sensor` Set CSRF surface is untouched).
- Sensor pinning/customization is handled by the follow-up dashboard customization spec and must remain browser-local/read-only.

## Architecture

Pure client-side, no build step, no framework:

- **Files** (all under `Resources/Web/`, auto-embedded via the existing `<EmbeddedResource Include="Resources\**" />` at `LibreHardwareMonitor.Windows.Forms.csproj:86`, served by name through `HttpServer.ServeResourceFileAsync`):
  - `index.html` — replaces the legacy file (default served on `/`).
  - `console.css` — all styles incl. the embedded Chakra Petch woff2 (base64, two weights) reused from the reference mockups. No external CDN — must work offline over LAN.
  - `console.js` — vanilla JS: fetch, model, heuristic, status, render.
  - **Delete** the now-unused legacy assets: `js/jquery-*.js`, `js/knockout-*.js`, `js/knockout.mapping-*.js`, `js/jquery.tmpl*.js`, `js/jquery.treeTable*.js`, `js/jquery-ui-*.js`, `js/ohm_web.js`, `css/jquery.treeTable.css`, `css/custom-theme/**`, `css/ohm_web.css`. Keep `favicon.ico` and `images/` (still referenced by `data.json` `ImageURL`s if used).
- **Asset filenames must not contain hyphens** — `ServeResourceFileAsync` maps `.`→resource segments and special-cases only `custom-theme`; hyphens otherwise break resolution. Use `console.css`/`console.js`.
- **Data source:** `GET /data.json` (gzip already supported by the server). Poll on an interval; render the whole DOM from the parsed tree each tick (the tree is small, ~140 KB; full re-render is simplest and fast enough).
- **Persistence (`localStorage`):** theme override, poll rate, paused state, per-panel collapsed state.

### `data.json` shape consumed (read-only contract)

Nested `Children` tree: computer → hardware nodes (`HardwareId`) → type-group nodes → sensor nodes. Sensor nodes carry: `Text`, `Type` (SensorType, e.g. `Temperature`/`Load`/`Power`/`Fan`/`Level`…), formatted `Value`/`Min`/`Max` strings, numeric `RawValue`/`RawMin`/`RawMax` (or `null` for NaN/no-reading), and `SensorId`. Root carries `Version` and the host `Text` (e.g. `SND-DESK`).

**Hardware class is derived from the `SensorId` path prefix** (robust across machines):

| prefix | class | | prefix | class |
|---|---|---|---|---|
| `/amdcpu`, `/intelcpu` | cpu | | `/nvme`, `/hdd` | nvme (storage) |
| `/gpu-nvidia` | gpu (discrete) | | `/usb` | disk |
| `/gpu-amd`, `/gpu-intel` | igpu | | `/lpc` | mb (board/SuperIO) |
| `/ram`, `/vram` | mem | | `/nic` | nic (network) |
| `/memory/dimm` | dimm | | (else) | other |

## Information architecture

1. **Sticky masthead** — brand sigil + host wordmark (`SND-DESK` / "Hardware Telemetry Console"); **Thermal Verdict** pill (GO / WATCH / CRITICAL + big lamp); census chips (OK / WATCH / CRIT counts); freshness dot + "updated HH:MM:SS"; rate slider (1–10 s), Pause, optional Graphs toggle, Theme toggle, Customize entry point.
2. **Primary Flight Display** — the auto-picked hero row of gauge cells.
3. **Range-safety placard** — shown only when ≥1 sensor is WATCH/CRIT; lists offending sensors (name, hardware, value) with status glyph.
4. **Subsystems** — per-hardware panels in a responsive grid: CPU, GPU(s), Memory, DIMMs, Storage, Disk, Board/Fans, then a single collapsed **Network** section. Each panel: status lamp + name + class tag + headline stat, collapsible; body groups rows by SensorType; rows show glyph · name (min/max on temps) · value · optional fill bar (Load/Level/Control).
5. **Footer** — version, host, endpoint, poll rate, and a status-color legend.

Background: glass-cockpit radial glow without the site-style page grid. `prefers-reduced-motion`
disables animation/jitter.

## Status model (the crux — grounded in live data)

Only **two sensor classes drive alarm color**, so stray/unlabeled readings never cry wolf; everything else is **informational**.

- **Temperature** — banded *per hardware class*; NaN/`null` → `idle`:
  - cpu: warn ≥ 85, crit ≥ 95
  - gpu core: warn ≥ 83, crit ≥ 92; gpu **memory-junction / hot-spot** (name match): warn ≥ 95, crit ≥ 105
  - nvme / dimm: **prefer the hardware's own published limits** when present as sibling sensors — NVMe `Warning Temperature` / `Critical Temperature`; DIMM `Thermal Sensor High Limit` / `Critical High Limit` — else defaults nvme warn 70 / crit 80, dimm warn 55 / crit 85
  - mb (`/lpc`): **informational only** (no red) — e.g. the observed stray `Temperature #5 = 89 °C` on the NCT6701D must not alarm
  - **limit/metadata sensors** (names containing `Limit`, `Warning Temperature`, `Critical Temperature`, `Resolution`) are themselves rendered as **info**, never alarmed. In subsystem panels they are grouped separately as `Limits` rather than live `Temperature` rows, so drive warning/critical thresholds do not appear to be fake current temperatures.
  - Known static/noisy dashboard readings are hidden client-side only before hero/card rendering; on this host that includes the Nuvoton noisy board temps and numbered NVMe aux temperature rows until dashboard-observed motion proves they move by more than 1 C across at least five poll samples. Raw `data.json`, CSV, `/metrics`, and the desktop tree remain unchanged.
- **SSD "Life" `Level`** — inverted: warn < 20 %, crit < 5 %.
- **Everything else** (Load, Power, Voltage, Current, Clock, Fan, Control, Data, SmallData, Throughput, Factor, Timing) — **info**; shown as readouts / fill bars, never alarm-red.
- **Thermal Verdict** = worst status across the alarm-eligible set (temps + life). Labeled "Thermal Verdict" so it doesn't over-claim whole-system health.

**Status is triple-encoded** everywhere — color **+** glyph (● ok, ▲ warn, ✕ crit, · info, ○ idle) **+** text label (OK/WATCH/CRIT/INFO/IDLE) — for colorblind/grayscale safety.

## Hero heuristic (auto-selected gauges)

Keyed off `SensorId` prefix + sensor name; cap ~9; discrete GPU prioritized over integrated:
- per CPU: `Tctl/Tdie` temp · `CPU Total` load · `Package` power
- per discrete GPU: `GPU Core` temp · `GPU Memory Junction` temp · `GPU Core` load · `GPU Package`/board power
- memory: `Total Memory` load (used %)
- storage: hottest real drive `Temperature` (excluding limit sensors)

**Arc gauge only where a real min→max range exists** (temperatures against their band; load/RAM % against 0–100). **Unbounded metrics — power, clock, voltage — render as a big-number readout with no arc** (an arc against an invented ceiling is meaningless). Each hero shows value, unit (once), status tag, and min·max.

## Design refinements folded in from the mockup review

Baked into the mockup's known gaps; the implementation must address:
1. **PFD readout typography** — number + unit on one baseline; min·max as a single compact mono line (`54.5 → 69.0`, unit shown once, not repeated 3×); equalize cell heights. *(must-fix)*
2. **Panel height balance** — masonry-style columns (CSS `columns` or JS balancing) so tall panels (CPU ~123 sensors, Storage 3 drives) don't leave large grid gaps; per-type row caps with a "show N more" expander.
3. **Per-core signal-over-noise** — default to `Cores (Average)` + `CPU Core Max` (and equivalents); collapse the full per-core clock/load/power lists behind the expander (same discipline as the NIC collapse).
4. *(nice-to-have)* faint warn/crit zone tick on the arc track so the fill is quantitatively meaningful; census may also show INFO/IDLE counts.
5. **Fast-value motion** — exact values remain current, but decorative hero arcs should be visually damped so 2-second poll jumps do not feel broken. Existing row bars are retained.
6. **Optional graph context** — compact card sparklines are a user option, not a replacement for bars, cards, or subsystem rows.

## Network handling

Collapse all `/nic` nodes into a **single "Network" panel**, defaulted collapsed, showing only interfaces with real traffic (a `Throughput` `RawValue > 0`); hide idle/`NaN` adapters and the WFP/QoS/LightWeight-filter pseudo-adapters entirely. (Live data has ~30 NIC nodes, nearly all zero/NaN duplicates.)

## Refresh & controls

- Default **2 s** auto-poll; rate slider 1–10 s; Pause toggle; optional Graphs toggle; freshness dot (live/paused/stale). All persisted.
- On fetch failure, keep last-good values and flip the freshness dot to "stale" rather than blanking the UI.

## Verification

- **No-regression gate:** `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` — the 7 data-contract tests + existing suite stay green (proves `data.json`/CSV untouched).
- **Build:** `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` (embedded resources resolve; deleted assets don't break the build).
- **End-to-end:** run the monitor with the web server enabled, open `http://<host>:<port>/` in a browser; confirm: hero gauges populate from live sensors; arcs only on bounded metrics; power/clock as plain readouts; per-hardware panels group correctly; Network collapses to active interfaces; thermal verdict + census reflect real temps; a deliberately warmed sensor (e.g. GPU load) surfaces the placard; theme toggle + rate + pause + collapse persist across reload; legacy URL still returns the new page.
- **Cross-check** against the approved mockup `sq-telemetry-console-mockup.html` for visual fidelity.
