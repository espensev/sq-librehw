# Feature Spec: Web Dashboard Card Truth & Card-First Controls (v3)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Draft — planning accepted scope, implementation NOT started (stop-point per operator request 2026-07-04)
**Updated:** 2026-07-04
**Related docs:** [`feature-web-dashboard-customization.md`](feature-web-dashboard-customization.md) (v2, shipped on this branch), [`superpowers/specs/2026-07-04-web-dashboard-telemetry-console-design.md`](superpowers/specs/2026-07-04-web-dashboard-telemetry-console-design.md), implementation plan [`superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md`](superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md)
**Evidence:** operator-annotated screenshot [`docs/Screenshot 2026-07-04 082637_edited.png`](Screenshot%202026-07-04%20082637_edited.png) (Customize drawer crossed out: "VERY LIMITED USE OF THIS TYPE OF MENU. DO IT ON CARDS"), desktop sensor tree screenshot `ui-needadj-textpng.png` (repo root, untracked), live `data.json` walk on SND-DESK 2026-07-04 (575 leaf sensors).

## 1. Summary

Operator feedback on the shipped Console v2 boils down to one theme: **the dashboard must stop implying precision it does not have, and put control where the data is (the card), not in a side drawer.** This spec covers: honest gauge ranges (real limit > user override > semantic band > clearly-labeled estimate), fan gauges driven by their paired Control % sensor, clean multi-GPU / duplicate-hardware identity, card-carried detail and actions replacing the Customize drawer, movable individual sensor rows and network subgroups, and the state-chip/icon overlap fix.

## 2. Operator feedback → code reality (grounded evaluation)

| # | Feedback | What the code actually does | Verdict |
|---|---|---|---|
| 1 | "No hardcoded max values; RTX 5090 max is not 200 W" | Not a constant: `SQ.speedoRange` (`console.js:342`) takes `max(rawMax, session motion peak, raw)` and rounds up a 1/2/5/10 ladder via `niceCeil` (`:335`). 5090 session peak 122 W → "/ 200" ceiling rendered with no qualifier (`:579`), so it *reads* as a hardware max. | Real defect in presentation, not a literal constant. Fix = range provenance + labeling, not deleting a number. |
| 2 | "Get max/range from sensors where possible" | Already done for NVMe/DIMM temps (`deriveLimits` `:285` reads `Warning/Critical Temperature`, `Thermal Sensor High/Critical High Limit` — live values 84/89 °C, 55/85 °C). NOT done for power/fan/clock. data.json has **no** GPU/CPU power-limit sensor. But the 5090 exposes `Load/GPU Power` and `Load/GPU Board Power` = **% of power limit** (live: 81.2 W @ 13.5 % ⇒ limit ≈ 575–600 W) — a real, derivable limit with zero hardcoding. | Agree. Extend the existing limit-derivation pattern; add W÷(%/100) derivation for NVIDIA; everything else falls to override/estimate. |
| 3 | "Support 2 GPUs cleanly" | This box: RTX 5090 (`/gpu-nvidia/0`, cls `gpu`) + AMD Radeon iGPU (`/gpu-amd/0`, cls `igpu`, real 35–65 W core power). `pickHero` (`:364`) uses `find` on the merged class list → with two discrete GPUs only GPU #0 would ever surface; hero labels say just "GPU Temp" with a truncated source. iGPU never gets heroes. | Agree, and it's worse than reported — see #3b. |
| 3b | *(found during review)* duplicate-name hardware merges | Panels group by **display text** (`buildPanelItems` `:684` keys `s.hw`): the three NVMe drives all named "KINGSTON SKC3000D2048G" (`/nvme/0..2`) merge into ONE panel with 3× indistinguishable "Temperature"/"Used Space" rows. Two identical GPUs would collide identically. `collapsedPanels` is also text-keyed. | Must-fix. Identity moves to `HardwareId`; duplicate display names get `#1/#2` suffixes. |
| 4 | "Fix gauge scaling; must accept real max/limit, or mark estimated" | Ranges come from 3 places today: semantic bands (temps/loads — fine), `speedoRange` session-peak ceilings (unlabeled — the complaint), nothing user-settable. | Agree. Introduce one resolver with provenance: `override > sensor/derived limit > semantic band > session-peak (labeled "est")`, else number-only card. |
| 5 | "Fans are special: % for the gauge, RPM as the number" | Every fan on this host has a paired Control sensor with identical text under the same hardware node (NCT `fan/0..6`↔`control/0..6`; GPU `fan/1,2`↔`control/1,2`). The console ignores Controls for fan cards and draws arcs against `niceCeil(peak RPM)` (e.g. 3020 → 5000). | Agree, cleanly implementable by pairing on `(hwid, text)`. No pair → RPM number, no arc (or est-labeled arc). |
| 6 | "Drop the right pane unless 100 % needed; do it on cards" | Customize drawer (`index.html:48-79`, `renderCustomize` `console.js:769`) holds: hide/show list, pin list + rename + style + up/down, panel order list. Cards/rows already have hover pin/hide (`ctlCluster`), pinned cards have drag grips. | Agree with one carve-out: acting on **invisible** sensors (hidden ones, idle NICs, suppressed aux temps) needs *some* surface. Proposal: kill the drawer; add card/row **expansion** for detail+actions; add one masthead "Sensors (N hidden)" popover for search/restore/pin-anything. That popover is the 100 %-needed remainder. |
| 7 | "Move individual sensor rows" | Only pinned cards and whole panels are draggable. Rows render in fixed `TORDER` type groups. | Agree. Row drag within its type group, persisted per panel. Cross-panel row moves = pinning (already exists); don't invent a second mechanism. |
| 8 | "Move subgroups — network really needs this" | The Network panel merges all active NICs' sensors then groups by **type**, so "Upload Speed" rows from different adapters interleave with identical labels (`:691-694`). Adapters can't be told apart, reordered, or hidden individually. | Agree. Network panel gets per-adapter subgroups (header = adapter name, keyed by stable `/nic/{GUID}` prefix), each hideable and reorderable. |
| 9 | "OK badge overlaps the thermometer etc." | `.cell-ctl` (pin/hide cluster) is absolutely positioned `top:10px;right:11px` (`console.css:258-264`) — the same corner as the state chip in the `.k` row and the type icon in `.k2`. On `hover:none` (touch) it is **always** visible. | Confirmed. Header needs an owned gutter: controls in-flow (or a reserved padding column), never painted over chip/icon. |
| 10 | "Accept missing data gracefully" | Null raw already renders "—"; `cardStyleFor` falls back to number-only when no range. | Mostly there. The gap is *labeling*: est ceilings must say est; no-range sensors must say "no known range" in the expansion, not silently differ. |

## 3. Opinions (what I'd do and why)

1. **Range provenance is the core fix.** One resolver, `SQ.rangeFor(sensor) → {lo, hi, source}` with `source ∈ override | limit | band | peak`, and the card ceiling rendered differently per source (`/ 575 W` plain for override/limit; `/ ≈200 W` muted+tooltip for peak). This satisfies "we can and must accept" estimates while never disguising them. Temps keep their semantic bands — a CPU arc drawn against 30–95 °C is *meaningful*, and no sensor exposes TjMax here; I would not churn that.
2. **Session peaks should persist.** `niceCeil(session peak)` resets every reload, so ceilings wobble between visits. Persist per-sensor observed peaks in `sq.dashboard.v1` so estimates only ratchet up. Cheap, and makes the "est" ceiling stable and honest ("highest this browser has ever seen").
3. **Fan pairing via Control is strictly better** than any RPM ceiling: arc = commanded %, number = measured RPM — you see both intent and effect (that is exactly the SQ-Control mental model: `control/N` is the command lane, `fan/N` the tach). I'd also show the paired % as a small secondary line on fan rows.
4. **Derived NVIDIA power limit is worth one small task** (W ÷ %-of-limit, median over samples where % > 10, rounded to 25 W, labeled "≈ derived"). It removes the single most misleading gauge (5090 power) with zero hardcoding and zero server work. Accuracy caveat at idle (±1 % quantization) is handled by the sample gate.
5. **Do NOT rush server-side limit sensors.** Adding NVML `EnforcedPowerLimit` / temp-threshold sensors upstreams real limits properly, but touches `LibreHardwareMonitorLib`, changes `data.json` content, and forces a `DataJsonGoldenTests` regen on a payload documented as a downstream contract (ThermalTrace). Keep it as an optional, separately-gated branch (E) after the client-side work proves what's actually missing.
6. **Hardware identity by `HardwareId` everywhere** (panels, collapse state, order keys, hero iteration). Display names become labels only, with `#1/#2` suffixes on duplicates and short device names in hero labels when >1 GPU ("RTX 5090 Temp" / "Radeon Temp"). This fixes the NVMe merge today and is the actual "support 2 GPUs" requirement.
7. **iGPU policy:** give the Radeon a compact hero pair (hottest temp + core power) — it draws a real 35–65 W and deserves visibility — and raise the hero cap to 14 with fans trimmed first (the cap-overflow wording landed in `749f386` already admits the 12 cap bites).
8. **Card expansion is the drawer replacement,** not a redesign: click card/row → inline detail (full sensor id, hardware, raw min/max, range + provenance line, style select, rename-if-pinned, max override, hide/pin, keyboard ▲▼ move). Keyboard reorder must survive the drawer's death — the drawer's Up/Down buttons are currently the only accessible path; expansion buttons take that role.
9. **Branch strategy for UI testing** (operator asked for testable branches): keep model-truth work on the current `feature/web-dashboard-customization` trunk (phases A+B — it changes *what* is shown, not the interaction shell), then two UI branches off it the operator can A/B live: `feat/web-card-first` (drawer removal + expansion + overlap fix) and `feat/web-row-subgroup-order` (row/adapter arranging). Optional `feat/web-limit-sensors` (server) stays parked until explicitly green-lit.
10. **Small enhancements worth folding in** (cheap, aligned): masthead "N hidden" honesty count on the popover trigger; warn/crit tick marks on temp arcs (design doc nice-to-have #4); layout export/import (copy JSON) for moving a tuned layout between browsers — v2 declared multi-browser sync out of scope, export/import is the 90 % answer for 1 % effort. Deliberately NOT proposing: WebSocket streaming, server-stored profiles, per-core charts (YAGNI; poll+localStorage are fine at 1–2 s).

## 4. Goals and Non-goals

**Goals**

- Every arc/gauge is backed by a range with known provenance; estimated ceilings are visibly marked and stable across reloads.
- Fan cards/heroes show commanded % (arc) + measured RPM (number) when a paired Control exists.
- Two GPUs (and any duplicate-named hardware) render as distinct, correctly-labeled panels and heroes.
- Per-sensor max override, editable on the card, persisted browser-locally.
- Card/row expansion carries detail + all per-sensor actions; the Customize drawer is deleted; one compact masthead popover covers search/restore/pin of not-currently-visible sensors.
- Individual rows reorderable within their type group; Network panel gets per-adapter subgroups (label, hide, reorder).
- State chip, type icon, and hover controls never overlap at any width or on touch.
- Missing/unknown data keeps rendering gracefully ("—", number-only cards, explicit "no known range").

**Non-goals**

- No server or `data.json` change in phases A–D (golden tests untouched). Server-side limit sensors are a separate, optional phase E behind its own decision gate.
- No control writes from the dashboard; read-only stays read-only.
- No cross-browser/machine layout sync service (export/import file only).
- No replacement of the poll model, row bars, theme/rate/pause behavior, or the v2 card anatomy.

## 5. Behavior specification

**Range resolution** (per sensor, evaluated in order; first hit wins):
1. `rangeOverrides[id]` from dashboard state (user-set max, optional min; validated numeric, max > min).
2. Sensor-provided or derived limit: existing `deriveLimits` temps; NVIDIA power limit derived from the `(Power W, Load %-of-limit)` pair — gated on ≥10 samples with % > 10, median ratio, rounded to nearest 25 W, tagged `limit` with `derived: true`.
3. Semantic band: existing `visualRangeForSensor` (temps per class incl. junction/hot-spot, Load/Control/Level 0–100).
4. Persisted observed peak: `max(rawMax, session motion, observedMax[id])` → `niceCeil` → tagged `peak`; `observedMax[id]` is updated (throttled) in dashboard state. Applies to Power/Clock, and to Fan only when no Control pair exists.
5. Otherwise `null` → number-only card; expansion shows "no known range".

**Rendering per source:** `override`/`limit` ceilings render plain (`/ 575 W`, tooltip states origin, derived limits prefix `≈`); `peak` renders muted `≈` ceiling with tooltip "estimated from observed peak — click to set true max"; `band` shows no ceiling text (unchanged).

**Fan pairing:** a Fan sensor with a Control sensor of identical `text` under the same `hwid` renders: arc = Control raw (0–100), big value = RPM, secondary meta line shows the %. Hero fan cells and pinned fan cards behave identically. Unpaired fans: RPM number card (fallback per range rule 4/5).

**Hardware identity:** panels group by `hwid`; `panelKey = hwid`; duplicate display texts get ` #2`, ` #3` suffixes in tree order; `collapsedPanels`/`panelOrder` keys migrate from text keys on first load (best-effort, text key read as fallback once). Heroes iterate distinct GPU `hwid`s: discrete first (Temp, Mem-Jct, Load, Power each), then iGPU (hottest temp + core power); when >1 GPU device exists, hero labels prefix a short device name derived from the hardware text ("RTX 5090", "Radeon"). Hero cap 14, fans trimmed first.

**Card-first controls:** clicking a card/row (not its buttons/grip) toggles an inline expansion: full `SensorId`, hardware, type, current/min/max raw values, range line with provenance, style select (auto/gauge/number/graph), rename (pinned only), max override input + clear, hide, pin/unpin, ▲▼ move buttons (keyboard-accessible reorder). Masthead gains a `Sensors — N hidden` button opening an anchored popover: search all sensors, Show/Hide/Pin each, reset-hidden; this is the only surviving list UI. The drawer (`<aside>`, scrim, tabs, its CSS and handlers) is deleted in the same branch once expansion + popover reach action parity.

**Row & subgroup arranging:** rows gain grips; drag reorders within the row's type group only; order persisted as `rowOrder[panelKey + '|' + displayType] = [sensorIds]`. Network panel renders one subgroup per adapter (key = `/nic/{GUID}` id prefix, header = adapter display name), each subgroup collapsible/hideable/drag-reorderable; hidden adapters listed in the popover for restore. Idle-adapter filtering (Throughput > 0) unchanged.

**Overlap fix:** the card header becomes a fixed two-row grid with a reserved trailing control gutter; `.cell-ctl` occupies the gutter in-flow (visible on hover/focus/touch), never absolutely painted over the chip or icon; state chip truncates its label rather than colliding at narrow widths.

**Failure/edge behavior:** unparseable new state fields are dropped by normalization (existing pattern); overrides referencing missing sensors are ignored but retained; derived limits never *lower* an explicit override; est ceilings never shrink below the current raw value.

## 6. UI, Settings, API, and Data impact

| Surface | Change |
|---|---|
| UI | Card/row expansion; masthead sensors popover; drawer removed; fan cards re-ranged; per-device hero labels; network subgroups; header layout fix. |
| Settings (`sq.dashboard.v1`, additive, version stays 1) | `rangeOverrides {id:{max,min?}}`, `observedMax {id:number}`, `rowOrder {groupKey:[ids]}`, `netAdapterOrder [nicKey]`, `hiddenNetAdapters [nicKey]`; `collapsedPanels`/`panelOrder` re-keyed to `hwid` with legacy fallback. |
| Remote web/API | None in A–D. Phase E (optional, gated): new limit sensors in `data.json` ⇒ golden regen per AGENTS §4. |
| Logging/files | None. |

## 7. Compatibility and risk

| Risk | Mitigation |
|---|---|
| `data.json` downstream contract (ThermalTrace, golden tests) | Phases A–D are client-only; `dotnet test` gate must stay green untouched. Phase E explicitly parked. |
| Drawer removal loses keyboard reorder | Expansion ▲▼ buttons land in the same branch, before drawer deletion; deletion task is last in the branch. |
| Text-keyed saved state (collapse/order) breaks on re-key | One-time migration + legacy-text fallback read; reset actions already exist. |
| Derived power limit wrong at idle | Sample gate (% > 10, n ≥ 10, median), `≈` labeling, override always wins. |
| Upstream sync | All changes stay in `Resources/Web/*` + `webtests/*` for A–D (same isolation promise as v2). |
| Touch devices | Controls in-flow (gutter) fixes the permanent-overlap case; drag paths already pointer-event based. |

## 8. Acceptance criteria

- [ ] No gauge anywhere renders an unlabeled invented ceiling: every arc is `band`, `limit`, `override`, or visibly `≈ est`.
- [ ] 5090 power card: shows real/derived/override limit (≈575–600 W) or a marked estimate — never a bare "/ 200".
- [ ] Every fan with a Control pair: arc = %, number = RPM (heroes, pinned, rows).
- [ ] Both GPUs appear as distinct panels and correctly-labeled heroes; three same-name NVMe drives render as three panels.
- [ ] A user can set/clear a max override from the card itself and the gauge respects it after reload.
- [ ] All drawer capabilities reachable from cards/rows/popover; drawer DOM/CSS gone; keyboard reorder still possible.
- [ ] Individual rows and network adapters can be reordered (and adapters hidden) with persistence.
- [ ] No overlap between state chip / type icon / control cluster at 320 px–4 K, mouse or touch.
- [ ] Sensors with no reliable range render number-only with explicit "no known range" in expansion.
- [ ] `node webtests/selftest.node.js` green with new model tests; `dotnet test` (golden) green with zero server diffs.

## 9. Verification plan

| Check | Command / step | Expected |
|---|---|---|
| Model self-test | `node --check ...\console.js` then `node webtests\selftest.node.js` | PASS, count grows from 32 with new cases |
| No-contract-change gate | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | all pass, golden untouched (A–D) |
| Build | `dotnet build LibreHardwareMonitor.Windows.Forms\...csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| Live smoke per branch | rebuild, relaunch, open `http://localhost:8085/` | acceptance items above, checked against live 5090/Radeon/NCT data |
| UI A/B | operator runs each UI branch build, compares | operator verdict recorded in §11 |

## 10. Open decisions

| Decision | Needed before | Current default |
|---|---|---|
| iGPU hero policy | Phase B | Always show temp+power pair; cap 14, trim fans first |
| Derived-limit rounding/labeling | Phase A task 5 | Median ratio, round 25 W, display `≈` |
| Drawer deletion timing | End of card-first branch | Delete only after expansion+popover parity in same branch |
| Row drag scope | Phase D | Within type group only; cross-panel = pinning |
| Phase E (server limit sensors) go/no-go | After A–D evaluated | Parked; requires explicit operator green-light (golden regen + downstream review) |
| State version bump | Phase A | Keep `version:1`, additive fields (normalizer drops unknowns on old builds — acceptable browser-local) |

## 11. Verification log

| Date | Evidence | Result | Notes |
|---|---|---|---|
| 2026-07-04 | Spec drafted from operator feedback + annotated screenshot + live data.json walk + console.js/css review | pending | Implementation intentionally not started; awaiting operator branch go |
