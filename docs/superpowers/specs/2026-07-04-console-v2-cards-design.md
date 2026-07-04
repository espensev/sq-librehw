# SQ Telemetry Console v2 — Honest Cards, Speedos, Fans-First — Design

**Date:** 2026-07-04
**Status:** Approved direction (user), pending implementation plan
**Builds on:** Customize Tier 3 (`2026-07-04-console-customize-tier3-design.md`), baseline `095bd69`+
**Design input:** user's card-design review (state-vs-type channels, honesty rules, trend deadband, fixed heights) — adopted as principles for a best-quality web-native implementation; the referenced WinUI/XAML template is NOT copied.

## Intent

Turn the console's gauge cells into real, configurable instrument cards; remove the dashboard's editorial voice; add speedometer gauges to more metrics; and surface fans by default. Visual quality and user clarity are the priority. Still a pure client-side projection of the unchanged `data.json` (no `HttpServer.cs` / schema / `AssemblyVersion` change).

## Non-negotiable honesty rules (spec-level guardrails)

1. **No fabricated readings.** Every number on a card is a measured value from `data.json` or client-observed history of those values. No derived verdicts ("expected ~2300 rpm"), no placeholder numerics.
2. **`null` renders as "—", never 0.** A sensor with `RawValue: null` shows an em-dash and the `off` treatment. The renderer must never substitute zero for missing (the F01/F02 rule; same spirit as the shipped bogus-0.0-min suppression).
3. **Context facts are measured or absent.** min/max lines show only real observed values (keep the existing "peak X" form when the min is a bogus 0-init). If a fact isn't measured, the line is omitted — the layout reserves the slot, it does not reflow.
4. **Freshness is global and stays global.** All sensors arrive in one poll; the masthead freshness dot/time is the single honest freshness indicator. No per-card "fresh 1.3s" line implying per-sensor measurement (deliberate deviation from the reference design). When the poll goes stale, ALL cards visibly enter the stale treatment together.

## Card anatomy (two meanings, two channels)

**Rule: rail + chip = STATE (health). Icon + value color = TYPE (metric identity).** This resolves the "amber temperature reads as a warning" ambiguity.

```
╭──────────────────────────────╮
│▌ CPU PACKAGE          ▲WATCH │   ▌ 3px state rail (ok/watch/crit/stale/off)
│▌ AMD Ryzen 9 9950X3D     [🌡]│   type icon, type-colored
│▌   ◔  65.0 °C     ↗ 0.2 °C/s │   speedo arc (state-colored) · type-colored value · trend
│▌   45.2 → 87.1               │   measured context only (or "peak 87.1"); slot reserved
│▌ ▂▃▅▆▅▃▂▁▂▄▆▇▆▄▂ (filled)    │   sparkline only in graph mode
╰──────────────────────────────╯
```

- **State set:** `ok / watch / crit / stale / off`. `info` is not a health state — cards for info-class sensors (power, clocks, fans, …) show **no chip** at all; their rail is neutral. Chips appear only where the status model actually judges health (temperature bands, SSD life). `stale` = poll failure (global, all cards). `off` = `raw == null` → value "—".
- **Type identity:** small inline-SVG icon (self-contained, `currentColor`) + a per-type value color: temp=amber, load=green, fan=cyan, power=violet, clock=blue, data/other=ink. Defined as CSS custom properties in both themes. (State-green chip vs load-green value can't collide in practice: chips only appear on temp/life cards, whose value color is amber.)
- **Fixed heights.** Cards are uniform per render mode: compact mode one height, graph mode one taller height. Empty context slots stay blank; the grid is never ragged.
- **Graph mode look:** filled area sparkline (type-colored, gradient fade) across the card bottom — the screenshot style. Exact numeric value stays primary; the graph is context.

## Trend indicator (deadband + hysteresis, per-kind units)

Computed client-side from the existing `SENSOR_HISTORY` (last ~60 s window):

| kind | rate unit | deadband (|rate| below → no arrow) |
|---|---|---|
| Temperature | °C/s | 0.05 |
| Fan | rpm/min | 30 |
| Power | W (windowed avg delta) | 1.5 |
| Load | %/s | 0.5 |
| Clock | MHz/s | 15 |

- Inside the deadband: **no arrow at all** (omit, don't print "stable").
- Hysteresis: the displayed direction flips only when the opposite-signed rate exceeds the deadband; leaving the band in the same direction keeps the arrow. Prevents ↗/↘ strobing on sensor noise.
- The rate shown is measured history math — allowed under rule 1.

## Speedometers on unbounded metrics

- Bounded metrics keep their real ranges (temps vs their band ceiling, Load/Level/Control 0–100).
- Power, Fan, Clock gain arcs scaled **0 → niceCeil(session peak)**: the peak is `max(RawMax from data.json, client-observed max)`, rounded UP the 1-2-5 ladder (87 W → 100 W, 1740 rpm → 2000 rpm, 5.6 GHz → 10? no — 5.6 GHz → 10 is wrong; ladder includes 6/8? No: 1-2-5 ladder only → 5.6→10 overshoots; acceptable? For clocks the ladder yields coarse scales — accept: consistency beats cleverness; the number is still exact).
- The arc is a *visual aid*; the ceiling label is shown (small, muted) so the scale is never implied to be a hardware limit. No invented "max RPM" — the scale is honestly derived from observed data (rule 3).

## Fans first

`SQ.pickHero` additionally selects **active fans** (`Type === 'Fan'`, `raw > 0`), sorted by rpm desc, capped at 4, after the existing CPU/GPU/RAM/drive heroes (overall hero cap raised to fit). A stopped fan (`raw == 0`) is a real reading and shows 0 rpm if selected by history (a fan that WAS spinning this session stays visible at 0 — that's signal); a `null` fan is "—"/off.

## De-opinionating the masthead

- **Removed:** the Thermal Verdict pill (`GO/WATCH/CRITICAL` lamp + label) and the OK/WATCH/CRIT census chips, plus their render code.
- **Kept:** the warn/crit placard (appears only when a sensor is actually over its band — information, not opinion), freshness, rate slider, Pause, Graphs, Theme, Customize.
- Docs updated accordingly (`local-ui-customizations.md` describes the verdict/census today).

## Per-card configuration

- New `cardStyle` map in `sq.dashboard.v1`: `{[sensorId]: 'auto' | 'gauge' | 'number' | 'graph'}` (default absent = `auto`).
  - `auto`: current heuristic — gauge when a real range exists, else big number; sparkline follows the global Graphs toggle.
  - `gauge` / `number`: force the visual.
  - `graph`: force the filled sparkline on this card even when global Graphs is off ("not everyone needs that" — but you can want it per-card).
- Editable in the Customize drawer's Cards tab (style select per pinned/hero card) alongside the existing rename; inline pin/hide/drag from Tier 3 unchanged.
- Precedence: explicit per-card style > global Graphs toggle > auto heuristic.

## Model/testing surface (pure, self-tested)

New `window.SQ.*` helpers, covered in `webtests/console.tests.js` (target ~60 → ~75):
- `SQ.niceCeil(x)` — 1-2-5 ladder round-up.
- `SQ.trendOf(history, kind, prevDirection)` — `{direction, rate, rateUnit} | null` implementing the deadband/hysteresis table.
- `SQ.speedoRange(sensor, limits, observedMax)` — bounded ranges as today + unbounded 0→niceCeil rule.
- `SQ.pickHero` fan extension (active fans, sorted, capped).
- `SQ.cardStyleFor(state, id, bounded)` — style precedence resolution.
- `normalizeDashboardState` gains `cardStyle` (map, defensively cleaned like `collapsedPanels`).
DOM/CSS (card layout, icons, chips, sparkline fill, fixed heights) verified by `node --check` + self-test regression + user browser E2E.

## Out of scope

- Tier 1 (in-place DOM diffing) and Tier 2 (FOUC, container queries, visibility-gated polling) — unchanged future efforts.
- No export/import; no server persistence; no data.json/CSV/metrics/AssemblyVersion change (verbatim constraint).
- The reference XAML/WinUI template and its `StatCard` type are inspiration only — no per-card freshness line, no `paused` state name (ours is `off`), no fabricated context rows.

## Verification

- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` stays green (contract untouched).
- `dotnet build ... -c Release -f net10.0-windows -p:Platform=x64` clean.
- `node webtests/selftest.node.js` green at the grown count.
- Browser E2E (user): card anatomy renders per this spec in both themes; verdict/census gone; fans appear as speedo cards; trend arrows appear only on genuinely moving sensors and don't strobe; per-card style overrides apply and persist; stale poll dims all cards + masthead dot; no layout raggedness at any width.
