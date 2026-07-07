# Web Dashboard v3 Next Plan - Card-First, Machine-Agnostic

**Plan ID:** web-dashboard-v3-next-2026-07-06
**Date:** 2026-07-06
**Status:** in progress on `master`. Merged: Slice 3 (`4310a8b`), A1/A2 fan + suffix clipping, **B1** masthead Sensors popover (`8291c89`), **B2** explicit primary-card selection (`106f91d`). **B3** Customize drawer removal **merged to `master` (`e7ae6f0`)**, `feat/web-drawer-removal-b3` deleted (`69252b4` panel reorder + `f60fcda` drawer removal + `4004822` no-op guard; plan `2026-07-06-web-drawer-removal-b3.md`). **Next: C1** — network adapter subgroups.
**Authoritative sequence:** the §4 A–F queue below. Where it disagrees with the older Slice numbering in the [continuation handoff](2026-07-06-web-dashboard-v3-continuation-handoff.md) §5/§10, this §4 queue wins (B2 before B3 was chosen deliberately).
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Predecessor plan:** [2026-07-04-web-dashboard-visible-correctness-plan.md](2026-07-04-web-dashboard-visible-correctness-plan.md)
**Recent review:** [../../reviews/review-2026-07-06-web-dashboard-v3-independent-verification.md](../../reviews/review-2026-07-06-web-dashboard-v3-independent-verification.md)
**Continuation handoff:** [2026-07-06-web-dashboard-v3-continuation-handoff.md](2026-07-06-web-dashboard-v3-continuation-handoff.md)

## 1. Current Baseline

The dashboard now has two important guardrails in place:

- `/` exposes a Pages menu that links `/dash/cardtruth/`, `/data.json`, and `/metrics`.
- Peak-derived ranges are not gauge-eligible. `SQ.gaugeRangeFor(...)` only allows semantic bands, explicit overrides, real/derived limits, or paired fan Control percentages to draw arcs.

Those fixes close the immediate screenshot failure, but they are only a partial v3 foundation. The v3 product goal remains: a machine-agnostic, card-first dashboard where trustworthy telemetry is visually clear, customization is local to the card/row/header being used, and normal workflows no longer depend on the old Customize side drawer.

This plan was drafted from the live `http://localhost:8085/` state on 2026-07-06 and now continues from `master` after the Slice 3 branch was merged through PR #25. It intentionally avoids host-specific assumptions. SND-DESK examples are acceptance fixtures only, not hardcoded behavior.

## 2. Non-Negotiables

- No fake gauges. If a sensor has no reliable range, it renders number-only plus explicit detail text such as `no known range`.
- No host-specific labels, limits, or sensor IDs in product code. Current-host labels such as `Fan #7`, RTX 5090, and Radeon are regression examples only.
- Raw LibreHardwareMonitor labels and `SensorId` values remain visible wherever aliases are used.
- `data.json` remains unchanged in this v3 client campaign. Server-side limit sensors are a separate gated feature.
- The dashboard remains read-only. No `/Sensor?action=Set` or hardware write UI is introduced.
- Stable `/` remains usable. Risky UI work can be staged in `/dash/cardtruth/` until promotion is explicit.
- `/dash/cardtruth/` is a temporary dev route only. Once selected changes are synced into `/`, retire the route and expose any surviving visual treatment as a root Theme dropdown/view option.
- Retiring `/dash/cardtruth/` retires a **dev/preview route**, not the deferred **context-dashboard** feature. Main/Gaming/Storage selectable dashboards remain an intended, separate future lane (own `sq.dashboard.{route}` namespaces, hash routing); the "one dashboard" closeout must not foreclose it, and the root view selector must be built route-namespace-ready. See the [context-dashboard spec](../specs/2026-07-04-dashboard-templates.md) and [continuation handoff §3.1](2026-07-06-web-dashboard-v3-continuation-handoff.md).

## 3. Target User Experience

The first viewport is an operational dashboard, not a configuration surface.

- Primary cards show current value, source, status, and only trustworthy visual range.
- A card or row can expand inline for details and actions.
- Common actions are local: pin, hide, rename/alias, style, max override, and move.
- Hidden/offscreen discovery lives in one compact masthead popover.
- The side drawer is removed after parity exists.
- Reordering is visible where the item lives: cards, panels, rows, and network adapter groups.
- Dark and light themes use the same semantic rules: status color communicates health, type color communicates sensor kind, and unknown/estimated states are muted.

## 4. Remaining Execution Queue

This queue was **re-sequenced on 2026-07-06 after the live browser audit** (recorded in the card-truth
verification log). Completed model/identity/range work stays as regression history; the remaining work
is regrouped into dependency-ordered phases that fold in the audit's concrete defects and the preserved
context-dashboard lane (continuation handoff §3.1).

**Completed (through Slice 3) — keep as regression baseline:**

| Done | Coverage to keep |
|---|---|
| Slice 0 — stabilize worktree | Rerun smoke after any rebuild. |
| Slice C1 — remove host-specific hidden-sensor IDs | No host sensor IDs in product code. |
| Slice 1 — range truth + machine-agnostic limit derivation | Range labels, observed peaks, GPU watt+percent derived limits, no peak gauges. |
| Slice 2 — hardware identity + multi-device rendering | Duplicate NVMe/GPU and `hwid`-keyed panels/heroes. |
| Slice 3 — card/row expansion + ordering contract (partial) | Visible expansion/actions, alias, override, keyboard move, `cardOrder`, `rowOrder[panelKey\|type]`, multi-tab save guard. |

**Remaining — re-sequenced phases.** These supersede the old Slice 4–7 numbering; the §5 detail
subsections map on as noted in the "Maps to" column.

| Phase | Item | Maps to | Exit |
|---|---|---|---|
| **A1** ✅ | Fan-card `cmd %` right-edge clipping fix | Slice 6 (pulled fwd) | Done. Full `cmd XX.X %` visible on every fan hero, dark+light. |
| **A2** ✅ | Value/unit/ceiling suffix overflow sweep at ~300px | Slice 6 | Done. No right-edge clip on any card suffix in either theme. |
| **B1** ✅ | Masthead Sensors popover (search/show/hide/pin/reset, hidden count) | Slice 4B | Done (`8291c89`; plan `2026-07-06-web-sensors-popover-b1.md`). Hidden/offscreen found + restored without the drawer. |
| **B2** ✅ | Explicit primary-card selection (`primaryCardsCustomized` boolean sentinel; seed-from-visible; seeded heroes keep curated presentation) | Slice 5A | Done (`106f91d`; plan `2026-07-06-web-primary-card-selection-b2.md`). Auto heroes default; first show-as/remove-from-primary seeds the visible set + switches to custom; Auto reset in PFD header. |
| **B3** ✅ | Remove Customize drawer after B1+B2 parity | Slice 4C | Done (`69252b4` panel reorder + `f60fcda` drawer removal; plan `2026-07-06-web-drawer-removal-b3.md`). Parity re-assessment corrected the stated gate: pinned-card reorder was **already** inline (expanded card `move-left`/`move-right` → `pinnedOrder`); only **panel** keyboard reorder was missing — added as always-visible ▲▼ in the panel header + a Subsystems "Reset order". Drawer DOM/JS/CSS deleted after live parity verification (Sensors popover covers hidden/pin/reset; pinned-card `title` rename subsumed by alias). |
| **C1** ✅ | Network adapter subgroups (per-NIC key, hide/reorder/restore) | Slice 5B | Done (`e48173c..555e7ae`, merge pending; plan `2026-07-06-web-network-subgroups-c1.md`). One panel per **active** adapter keyed by `s.hwid`; ▲▼/drag reorder → `netAdapterOrder` (no-op-guarded), ⊘ hide → `hiddenNetAdapters`, restore from Sensors popover; `panelOrder` stays nic-free; hidden-adapter sensors `offscreen`. selftest 227/227, golden 42/42, both builds 0/0, live-verified dark+light on a 37-NIC host (5 active panels), zero console errors. |
| **D1** ✅ | Card header grid + reserved action gutter | Slice 6 | Done (`e0f1dad` grid + `0e0987a` collapse-at-rest; plan `2026-07-07-web-card-header-gutter-d1.md`). Header is a two-track grid (`.chead{minmax(0,1fr) auto}`) with `.cell-ctl` in column 2 → **structurally** cannot overlap the chip/type-icon (mutually-exclusive track, no `position`/`transform` escape). Cluster collapses `display:none` at rest (default names stay full) and reveals in-flow on hover/focus/touch. Live RED 4→GREEN 0 both themes (desktop/touch-390/narrow-320), selftest 227/227, golden 42/42, clean rebuild 0/0 (`0.9.6+0e0987a`), final review 0C/0I. Controls never overlap chip/icon on hover/focus/touch. |
| **D2** | Expansion multi-column layout (use horizontal space) | audit finding | Expanded detail fills width, not a tall narrow strip. **Spec Accepted 2026-07-07: [`feature-web-dashboard-expansion-layout.md`](../../feature-web-dashboard-expansion-layout.md)** — §9 resolved with the defaults (**B grid-breakout full-row** `.cell.expanded{grid-column:1/-1}`; **keep single `c:` key**). **Plan ready: [`2026-07-07-web-expansion-multicolumn-d2.md`](2026-07-07-web-expansion-multicolumn-d2.md)** — one CSS rule (no JS; `#pfd`+`#pinned` share `.pfd`), controller-owned live column-count/clip gate RED→GREEN + both-theme screenshots. |
| **D3** | Full responsive/theme QA matrix | Slice 6 | 320/390/640/1440/wide × dark/light, zero overlap/clip. **Re-check B3 additions at narrow widths:** always-visible panel-head ▲▼ and the extra `#panelsReset` button in the Subsystems `.sec-head` (which already has mobile tag-clamping). **From the 2026-07-07 D1/D2 review (low, latent):** `.cell .chip-state`'s `text-overflow:ellipsis` is inert (`inline-flex` container — flex items don't ellipsize), so an overflowing chip hard-clips; decide fix-or-accept during the matrix. |
| **E1** | Root `viewTheme: standard \| cardTruth` selector, route-namespace-ready | Slice 7 | Look selector persists; state plumbing ready for `sq.dashboard.{route}`. |
| **E2** | Sync accepted deltas to `/`; retire `cardtruth` route + Pages entry | Slice 7 | One product surface, no dev/preview routes. |
| **F1–F3** | Context dashboards (Main/Gaming/Storage): hash router + per-route state, switcher control, template defaults | new lane (§3.1) | Selectable context dashboards coexisting with `viewTheme`, each honest per card-truth. Separate campaign, gated behind Phase E; build on current baseline, not the stale branch. |
| **X1** | Planning-doc consolidation (one authoritative plan+spec; archive superseded 2026-07-04 set) | new | Sprawl reduced; verification log remains the evidence trail. |

**Critical path:** A → B → **C (done)** → D → E, then F as its own campaign. A and X1 can start immediately and
in parallel with anything; C was independent of B. **D1 done** (card header reserved gutter, merged). **Next: D2** — expansion multi-column layout (use horizontal space).
Re-check the C1 always-visible adapter-head ▲▼/⊘ controls in the D3 narrow-width matrix (they sit in the same
`.panel-move` cluster the D-phase polish touches).

Do not treat the existing `/dash/cardtruth/` preview as a product destination. It is a temporary place
to test unsynced UI work; once a delta is accepted, promote it into stable assets or discard it.
Retiring that route (Phase E2) does **not** retire the context-dashboard lane (Phase F) — see §2 and the
continuation handoff §3.1.

## 5. Implementation Slices

### Slice 0 - Stabilize Current Worktree (done)

Goal: make the repo state explicit before deeper changes.

Tasks:

- Keep the current Pages menu, preview route, and gauge guard changes.
- Decide whether to commit the current route/menu/gauge changes before v3 starts, or continue in one implementation branch.
- Record active process provenance before every live closeout.

Verification:

- `git status --short`
- `node webtests\selftest.node.js`
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- Live `GET /`, `/dash/cardtruth/`, `/data.json`, `/metrics`.

Exit:

- Current baseline is committed on `feat/web-dashboard-v3-card-first`.
- No stale executable is mistaken for the current source.

### Slice 1 - Range Truth and Machine-Agnostic Limit Derivation (done)

Goal: finish the truth model without hardcoding this machine.

Tasks:

- Keep `SQ.gaugeRangeFor(...)` as the single arc eligibility gate.
- Add `SQ.rangeLabelFor(rangeInfo, sensor)` or equivalent display-model helper.
- Persist observed peaks with a throttled `mergeObservedPeaks(...)` path, but keep peaks number/sparkline-only unless the operator sets an override.
- Derive GPU power limits only from matching watt + percent-of-limit sensors under the same `hwid`.
- Use a conservative sample gate for derived GPU limits:
  - same hardware id;
  - watt sensor raw value finite;
  - percent sensor raw value finite and above idle noise;
  - enough samples over time;
  - median ratio;
  - rounded to stable 25 W buckets;
  - rendered as approximate.
- Do not derive CPU power caps from current value or observed peaks.

Tests:

- `rangeFor` returns `peak` for observed peaks, but `gaugeRangeFor` rejects it.
- `rangeLabelFor` distinguishes `override`, `limit`, `derived`, `band`, `peak`, and `unknown`.
- GPU derived limit works from synthetic watt + percent sensors.
- Derived limit refuses idle/noisy/insufficient samples.
- CPU power remains number-only unless explicit override or real limit exists.

Exit:

- No power card can render a bare guessed ceiling.
- Any derived limit is explainable from live input sensors and marked approximate.

### Slice 2 - Hardware Identity and Multi-Device Rendering (done)

Goal: stop merging devices by display text.

Tasks:

- Introduce device grouping helpers keyed by `hwid`, not display name.
- Use display text only as a label.
- Suffix duplicate labels deterministically: `KINGSTON ... #1`, `KINGSTON ... #2`.
- Migrate or fallback old text-keyed `collapsedPanels` and `panelOrder` once.
- Build GPU hero cards by distinct `hwid`.
- Include iGPU policy from the spec: compact temperature and power visibility where those sensors exist.
- Keep hero count bounded after per-device selection, trimming lower-priority fan cards first.

Tests:

- Three same-name NVMe fixture devices produce three panel items.
- Two same-name GPU fixture devices produce separate hero/panel identities.
- Old text-keyed collapse/order state can still be read or reset without crashing.
- Raw `hwid` is stable in row/card details.

Exit:

- Panels and heroes can be traced to one hardware id.
- Duplicate device names are readable, not merged.

### Slice 3 - Card and Row Expansion + Ordering Contract (next)

Goal: move normal details/actions onto the visible item and define the user-owned selection/order contract before drawer removal.

Pre-flight:

- Done: fixed the Slice 1 multi-tab state-write risk. Background telemetry accumulator saves now merge into fresh same-route layout/customization state through `SQ.saveTelemetryState`, so a passive browser tab cannot overwrite active-tab aliases, order, overrides, hidden state, or card selection.
- Audit the current `/dash/cardtruth/` row-order delta. Promote accepted behavior into stable `/` or explicitly discard it; do not leave a product capability available only in the preview route.
- Keep stable `/` as the product target. `/dash/cardtruth/` and any future `/dash/<version>/` route are temporary comparison subsites with separate storage keys, not permanent dashboard tabs. If a visual treatment survives, expose it from the root Theme/view control during promotion.

Tasks:

- Add one expansion state model:
  - expanded card id;
  - expanded row id;
  - expanded panel/network group as needed.
- Add explicit card selection/order state:
  - default to the current auto-selected heroes when no user card selection exists;
  - allow the operator to choose which sensors become visible dashboard cards;
  - persist card order separately from raw sensor order;
  - keep pinned-card behavior compatible with existing `pinnedCards`/`pinnedOrder`.
- Card expansion shows:
  - raw LibreHardwareMonitor label;
  - operator alias input and clear;
  - `SensorId`;
  - hardware label and `hwid`;
  - type and unit;
  - current/min/max raw values;
  - range provenance;
  - style select;
  - max override input and clear;
  - pin/unpin;
  - hide/show;
  - move up/down buttons.
- Row expansion uses the same details and actions, adapted to row layout.
- Pointer drag/drop belongs on the visible surface where reliable:
  - cards drag within their card group;
  - pinned cards keep existing drag behavior;
  - rows drag within their panel/type group;
  - panel drag remains on the panel header;
  - keyboard move controls remain available in expansion even when drag is unavailable.
- Aliases are display-only. Raw labels remain visible in expansion/search.
- Max override validation is strict: numeric, finite, max > min.
- Cross-panel row moves remain pin/promote actions, not raw row migration. Raw `data.json` order and sensor ids are never changed.
- Keyboard behavior:
  - expansion toggles with Enter/Space on the row/card button target;
  - controls have labels;
  - move buttons work without drag.
- Multi-tab/subsite behavior:
  - two browser tabs on `/` must not clobber each other's persisted edits;
  - stable `/` and `/dash/cardtruth/` keep separate localStorage namespaces until promotion;
  - expansion open/closed state is per-tab/transient unless persistence is deliberately chosen.

Tests:

- Alias set/clear normalizes state.
- Override set/clear normalizes state and affects gauge eligibility.
- Invalid override is rejected or ignored without corrupting state.
- Expanded detail contains raw label and `SensorId`.
- Keyboard move helper changes order.
- Drag/drop helper changes order for card, panel, and row containers without crossing row group boundaries.
- Multi-tab save test: a telemetry-only/background save cannot overwrite a fresh alias/order/override edit from another same-route tab.
- Preview namespace test: stable `/` and `/dash/cardtruth/` do not read or write each other's dashboard state.

Exit:

- The old drawer is no longer the only way to rename, style, override, pin, hide, or reorder.
- The operator can choose and reorder visible cards/rows directly, with drag/drop plus keyboard fallback.
- No accepted ordering capability remains only on `/dash/cardtruth/`.

### Slice 4 - Masthead Sensor Popover and Drawer Removal

Goal: delete the normal side drawer only after replacement parity exists.

Tasks:

- Add a compact masthead button: `Sensors` plus hidden/offscreen count.
- Popover supports:
  - search all sensors;
  - show hidden sensor;
  - hide visible sensor;
  - pin sensor;
  - reset hidden;
  - restore hidden network adapters once network grouping lands.
- The popover is anchored and compact, not a side pane.
- Remove:
  - `Customize` button;
  - drawer `<aside>`;
  - scrim;
  - tabs;
  - drawer-only render handlers;
  - drawer CSS.

Tests:

- Hidden sensor can be restored from the popover.
- Pin-anything path still works.
- Reset hidden works.
- DOM/source no longer contains `customizeDrawer`, drawer tabs, or drawer open handlers.

Exit:

- No normal workflow depends on a drawer.
- Hidden/offscreen search still exists.

### Slice 5 - Visible Ordering Everywhere

Goal: every ordered surface can be changed from that surface.

Tasks:

- Primary cards:
  - stable order model;
  - visible drag grip where appropriate;
  - up/down fallback in expansion.
- Pinned cards:
  - keep existing drag behavior;
  - add keyboard up/down parity.
- Panels:
  - panel header move controls;
  - order persists by `hwid`.
- Rows:
  - row-line reorder controls beside existing row actions;
  - drag within type group only;
  - up/down fallback in row expansion;
  - persist as `rowOrder[panelKey + '|' + displayType]`.
- Network:
  - one subgroup per adapter;
  - adapter key derived from stable `/nic/{GUID}` prefix;
  - subgroup hide/show;
  - subgroup reorder;
  - row reorder inside adapter/type group if practical.

Tests:

- `applyOrder` and `reorderByDrop` cover cards, panels, rows, and network groups.
- Row reorder cannot move a row to a different panel/type group.
- Network adapter order normalizes duplicates/missing keys.
- Hidden adapter is restorable.

Exit:

- No reorder action is drawer-only or hidden in an unrelated management surface.

### Slice 6 - Modern UI Polish and Responsive QA

Goal: make the dashboard feel intentional without losing density.

Tasks:

- Card header becomes a grid with a reserved action gutter.
- Controls never absolutely cover chips/icons on hover, focus, or touch.
- Value/unit/ceiling area wraps or reserves enough width.
- Status/type color rules:
  - status rail/chip: health/state;
  - value/icon accent: sensor kind;
  - unknown/estimated: muted;
  - warnings/errors: reserved for actual status, not decoration.
- Reduce noisy competing effects.
- Keep cards at 8px radius unless an existing local style demands otherwise.
- Mobile masthead:
  - no horizontal clipping at 320/390/640px;
  - rate slider and buttons remain reachable;
  - Pages and Sensors popovers fit viewport.
- Light theme gets equal attention, not a dark-theme afterthought.

Visual checks:

- 320px, 390px, 640px, 1440px, and wide desktop.
- Dark and light theme.
- Mouse hover, keyboard focus, touch/no-hover assumptions.
- Long labels, long units, missing values, many duplicate devices.

Exit:

- Default view is readable without opening customization.
- No overlapping text/buttons/icons.
- The page looks like a polished monitoring app, not a debug grid.

### Slice 7 - Preview, Promotion, and Closeout

Goal: avoid breaking the stable dashboard while v3 is still being judged.

Tasks:

- Keep risky UI work available under `/dash/cardtruth/` until accepted.
- When accepted, promote by copying/wiring selected assets into `Resources/Web/`.
- After sync/promotion, remove the temporary `cardtruth` route and Pages-menu entry unless a new active comparison needs it.
- If the card-truth visual treatment survives as an alternate style, expose it from the root Theme dropdown/view selector using stable `sq.dashboard.v1` state instead of a separate route namespace. This `viewTheme` selector is the *look* control only; it is distinct from the deferred **context-dashboard** switcher (Main/Gaming/Storage), which stays a separate future lane with its own `sq.dashboard.{route}` namespaces. Build the selector route-namespace-ready — do not merge the two controls.
- Verify stable `/` and preview route independently.
- Update spec verification logs and review notes.

Gates:

- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` if preview remains active.
- `node webtests\selftest.node.js`
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
- Live `http://localhost:8085/` smoke after restart.

Exit:

- Stable route and preview route both behave intentionally.
- Verification log records exact commands and live URL results.

## 6. Data and State Model

All state remains browser-local under `sq.dashboard.v1` unless a preview route intentionally uses its own namespace. The deferred **context-dashboard** lane (Main/Gaming/Storage; see the [context-dashboard spec](../specs/2026-07-04-dashboard-templates.md)) uses per-route namespaces `sq.dashboard.{route}`. When Slice 7 adds the root view selector, keep the selector and state plumbing **route-namespace-ready** so that lane needs no retrofit — the `viewTheme` look-selector and the context-dashboard switcher are two orthogonal controls that coexist, not one merged control.

Additive fields:

| Field | Owner Slice | Purpose |
|---|---:|---|
| `rangeOverrides` | 1, 3 | User-set true min/max for sensors. |
| `observedMax` | 1 | Historical observed peak, not gauge-eligible by itself. |
| `powerLimitSamples` | 1 | Telemetry-only GPU implied-limit samples, scoped by `hwid`. |
| `sensorAliases` | 3 | Display alias while preserving raw label. |
| `primaryCards` | 3, B2 | Explicit user-selected primary card ids; seeded from the visible hero set on first edit. Missing ids preserved so layout recovers. |
| `primaryCardsCustomized` | B2 | Boolean sentinel: `false` = auto (`SQ.pickHero`); `true` = operator owns the set (even if empty). Distinguishes "chose nothing" from "never chose". |
| `cardOrder` | 3, 5 | Primary/pinned card order if split from existing pinned order. |
| `rowOrder` | 3, 5 | Row order per panel/type group. |
| `netAdapterOrder` | 5 | Network adapter subgroup order. |
| `hiddenNetAdapters` | 5 | Network adapter visibility. |
| `expanded` or equivalent transient state | 3 | Runtime only unless persistence is intentionally chosen. |

Normalizer rules:

- Drop invalid object shapes.
- Deduplicate arrays.
- Preserve missing-sensor references where useful so layout can recover if hardware returns.
- Never let invalid state prevent rendering.
- Background telemetry writes use `SQ.saveTelemetryState` to merge `observedMax`/`powerLimitSamples` into fresh same-route persisted state before saving. User-driven commits still write the intended full dashboard state.
- Stable `/` uses `sq.dashboard.v1`; preview routes use separate namespaces until promotion. Cross-route state import/export is explicit only.

## 7. Component Structure

This is still vanilla HTML/CSS/JS. Do not introduce a frontend framework for v3.

Recommended internal render structure:

- `buildDashboardModel(data, state)`
  - flattens sensors;
  - derives device groups;
  - derives limits/ranges;
  - applies visibility and order.
- `renderMasthead(model, state)`
  - freshness;
  - rate/theme/pause;
  - Pages menu;
  - Sensors popover trigger.
- `renderCardGrid(model, state)`
  - primary cards;
  - pinned cards;
  - expansion host.
- `renderPanelList(model, state)`
  - hardware panels keyed by `hwid`;
  - network panel special grouping.
- `renderSensorPopover(model, state)`
  - hidden/offscreen discovery only.
- `renderExpansion(sensor, context, state)`
  - shared card/row detail/action body.

The goal is not abstraction for its own sake. The goal is to keep state derivation separate from DOM string assembly so tests can cover behavior without a browser.

## 8. Test Plan

Model tests in `webtests/console.tests.js` should grow before or with each slice.

Required fixture classes:

- generic CPU with power but no power limit;
- NVIDIA GPU with watts plus percent-of-limit;
- GPU without percent-of-limit;
- iGPU with temp and power;
- duplicate same-name NVMe devices;
- duplicate same-name GPUs;
- fan with matching Control;
- fan without Control;
- network adapters with stable GUID-like prefixes;
- missing/null sensor values;
- long labels and units.

Browser/live tests:

- root Pages menu links remain present;
- no peak-derived card emits an arc;
- no side drawer after Slice 4;
- card expansion works by mouse and keyboard;
- row expansion works by mouse and keyboard;
- card and row drag/drop persists without crossing row group boundaries;
- two browser tabs on the same route do not overwrite each other's aliases/order/overrides through background telemetry saves;
- stable `/` and `/dash/cardtruth/` state namespaces stay isolated until an explicit promotion/import path exists;
- hidden sensor can be restored;
- reorders persist after reload;
- narrow viewports do not clip controls.

## 9. Branching Recommendation

Preferred for the next product branch:

```powershell
git checkout master
git pull --ff-only origin master
git checkout -b feat/web-dashboard-v3-popover-promotion
```

The baseline route/menu/gauge, hardware-identity, range-truth, state-merge, and visible expansion/action work is merged on `master` through `4310a8b`. If new dirty work appears, commit or park it before starting another route/UI slice.

Avoid parallel branches for `console.js`. The file is too central and the slices depend on one another.

Recommended commit grouping:

1. `feat(web): stabilize v3 range display model`
2. `feat(web): derive gpu power limits from telemetry percentages`
3. `feat(web): key panels and heroes by hardware id`
4. `feat(web): add card and row expansion actions`
5. `feat(web): replace customize drawer with sensor popover`
6. `feat(web): add visible row panel card and network ordering`
7. `fix(web): refine dashboard card layout and responsive controls`
8. `docs(web): record v3 verification and promotion decision`

## 10. Stop Conditions

Stop and review before continuing if any of these happen:

- `data.json` shape changes or golden tests require regeneration.
- A hardware write/control path appears in the web UI.
- A feature requires hardcoding a sensor ID, GPU model, board name, or local host.
- Drawer deletion would remove the only keyboard path for an action.
- Live dashboard cannot be restarted after a build.
- A visual fix makes the default dashboard less readable or more cluttered.

## 11. First Next Step

The multi-tab state merge guard, visible expansion/action patch, masthead Sensors popover (B1), explicit primary-card selection (B2), and **Customize drawer removal (B3)** are all implemented and merged to `master` (B3 at `e7ae6f0`; `feat/web-drawer-removal-b3` deleted). Stable `/` exposes raw label, `SensorId`, hardware id, range provenance, alias, style, max override, pin/hide, keyboard move (cards, rows, pinned cards, **and panels**), hidden/offscreen discovery via the Sensors popover, and operator-chosen primary cards with an Auto reset — **with no side drawer**.

B3 was **not** a clean deletion, but the parity re-assessment found only one real keyboard gap: panel reorder (pinned-card reorder was already inline via the expanded card's `move-left`/`move-right`). Delivered as: (1) inline ▲▼ panel reorder + a Subsystems "Reset order"; (2) live verification that every drawer workflow has a visible/keyboard replacement (hidden/pin/reset → Sensors popover; alias/style/override/pin/hide/move → card/row expansion; pinned-title → alias, which renders on the card); (3) deletion of `#customizeDrawer`/`#customizeScrim`/`#customize`, tabs, `renderCustomize`/`renderPinnedEditor`/`renderLayoutEditor`/`renderSensorRows`/`renamePinned`, drawer handlers, and drawer CSS (shared `.iconbtn`/`.sensor-*` rules split, not deleted).

**C1 — network adapter subgroups is now done** (branch `feat/web-network-subgroups-c1`,
`e48173c..555e7ae`, merge pending; execution record `2026-07-06-web-network-subgroups-c1.md`). One panel per
active adapter keyed by `s.hwid`, ▲▼/drag reorder + ⊘ hide + Sensors-popover restore, `panelOrder` kept
nic-free, hidden-adapter sensors reported `offscreen`. selftest 227/227, golden 42/42, both Release x64 builds
0/0, live-verified in both themes on a 37-NIC host (5 active panels), zero console errors.

**D1 — card header grid + reserved action gutter is now done** (branch `feat/web-card-header-gutter-d1`,
`e0f1dad` grid + `0e0987a` collapse-at-rest refine; execution record `2026-07-07-web-card-header-gutter-d1.md`):
a two-track `.chead` grid makes control↔chip/type-icon overlap structurally impossible, and the control cluster
collapses at rest so default card names stay full. selftest 227/227, golden 42/42, clean `net10.0-windows` x64
rebuild 0/0 (stamp `0.9.6+0e0987a.2026-07-07`), live RED 4→GREEN 0 in both themes across desktop/touch/narrow,
final whole-branch review 0C/0I. The next concrete patch is **D2 — expansion multi-column layout** (see §4 row
D2 / §5 Slice 6 polish).

Do not make `/dash/cardtruth/` a permanent product tab; use it only as a temporary comparison route until accepted behavior is synced into the root dashboard.

Acceptance for the next patch:

- `node webtests\selftest.node.js` adds and passes popover/drawer-removal model tests where practical.
- Hidden/offscreen sensor discovery is available from one compact masthead popover.
- No normal card/row detail workflow remains drawer-only.
- Drawer DOM/CSS/handlers are removed only after parity is verified.
- No product code mentions this machine's sensor IDs or device names.
