# Web Dashboard v3 Next Plan - Card-First, Machine-Agnostic

**Plan ID:** web-dashboard-v3-next-2026-07-06
**Date:** 2026-07-06
**Status:** in progress (Slice 0, C1 host-ID removal, and Slice 2 hardware identity done on `feat/web-dashboard-v3-card-first`)
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Predecessor plan:** [2026-07-04-web-dashboard-visible-correctness-plan.md](2026-07-04-web-dashboard-visible-correctness-plan.md)
**Recent review:** [../../reviews/review-2026-07-06-web-dashboard-v3-independent-verification.md](../../reviews/review-2026-07-06-web-dashboard-v3-independent-verification.md)

## 1. Current Baseline

The dashboard now has two important guardrails in place:

- `/` exposes a Pages menu that links `/dash/cardtruth/`, `/data.json`, and `/metrics`.
- Peak-derived ranges are not gauge-eligible. `SQ.gaugeRangeFor(...)` only allows semantic bands, explicit overrides, real/derived limits, or paired fan Control percentages to draw arcs.

Those fixes close the immediate screenshot failure, but they are only a partial v3 foundation. The v3 product goal remains: a machine-agnostic, card-first dashboard where trustworthy telemetry is visually clear, customization is local to the card/row/header being used, and normal workflows no longer depend on the old Customize side drawer.

This plan was drafted from the live `http://localhost:8085/` state on 2026-07-06 and now continues from the committed `feat/web-dashboard-v3-card-first` branch. It intentionally avoids host-specific assumptions. SND-DESK examples are acceptance fixtures only, not hardcoded behavior.

## 2. Non-Negotiables

- No fake gauges. If a sensor has no reliable range, it renders number-only plus explicit detail text such as `no known range`.
- No host-specific labels, limits, or sensor IDs in product code. Current-host labels such as `Fan #7`, RTX 5090, and Radeon are regression examples only.
- Raw LibreHardwareMonitor labels and `SensorId` values remain visible wherever aliases are used.
- `data.json` remains unchanged in this v3 client campaign. Server-side limit sensors are a separate gated feature.
- The dashboard remains read-only. No `/Sensor?action=Set` or hardware write UI is introduced.
- Stable `/` remains usable. Risky UI work can be staged in `/dash/cardtruth/` until promotion is explicit.
- `/dash/cardtruth/` is a temporary dev route only. Once selected changes are synced into `/`, retire the route and expose any surviving visual treatment as a root Theme dropdown/view option.

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

This is the current queue after the 2026-07-06 alignment pass.

| Order | Slice | Status | Work to post/execute next |
|---:|---|---|---|
| 0 | Stabilize current worktree | done | Keep as baseline; rerun smoke after any rebuild. |
| C1 | Remove host-specific hidden-sensor IDs | done | Keep regression coverage; no host sensor IDs in product code. |
| 2 | Hardware identity and multi-device rendering | done | Keep tests for duplicate NVMe/GPU and `hwid` keyed panels/heroes. |
| 1 | Range truth and machine-agnostic limit derivation | next | Add range display/provenance helper, observed peaks, and GPU watt + percent derived limits without drawing peak gauges. |
| 3 | Card and row expansion | remaining | Move alias, raw label, `SensorId`, style, override, pin/hide, and move controls onto cards/rows. |
| 4 | Masthead sensor popover and drawer removal | remaining | Add compact Sensors popover for hidden/offscreen discovery, then remove the Customize drawer after parity. |
| 5 | Visible ordering everywhere | remaining | Promote row ordering from the preview where accepted; finish card, panel, row, and network subgroup ordering from visible surfaces. |
| 6 | Modern UI polish and responsive QA | remaining | Fix overlap/clipping and theme quality across dark/light and narrow/wide viewports. |
| 7 | Preview promotion and closeout | remaining | Sync accepted changes into `/`; retire `/dash/cardtruth/`; expose surviving visual treatment via root Theme dropdown/view selector. |

Do not treat the existing `/dash/cardtruth/` preview as a product destination. It is a temporary place to test unsynced UI work. Today its extra delta over stable `/` is mainly row-reorder behavior plus isolated preview state; once a delta is accepted, promote it into stable assets or discard it.

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

### Slice 1 - Range Truth and Machine-Agnostic Limit Derivation

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

### Slice 3 - Card and Row Expansion

Goal: move normal details/actions onto the visible item.

Tasks:

- Add one expansion state model:
  - expanded card id;
  - expanded row id;
  - expanded panel/network group as needed.
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
- Aliases are display-only. Raw labels remain visible in expansion/search.
- Max override validation is strict: numeric, finite, max > min.
- Keyboard behavior:
  - expansion toggles with Enter/Space on the row/card button target;
  - controls have labels;
  - move buttons work without drag.

Tests:

- Alias set/clear normalizes state.
- Override set/clear normalizes state and affects gauge eligibility.
- Invalid override is rejected or ignored without corrupting state.
- Expanded detail contains raw label and `SensorId`.
- Keyboard move helper changes order.

Exit:

- The old drawer is no longer the only way to rename, style, override, pin, hide, or reorder.

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
- If the card-truth visual treatment survives as an alternate style, expose it from the root Theme dropdown/view selector using stable `sq.dashboard.v1` state instead of a separate route namespace.
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

All state remains browser-local under `sq.dashboard.v1` unless a preview route intentionally uses its own namespace.

Additive fields:

| Field | Owner Slice | Purpose |
|---|---:|---|
| `rangeOverrides` | 1, 3 | User-set true min/max for sensors. |
| `observedMax` | 1 | Historical observed peak, not gauge-eligible by itself. |
| `sensorAliases` | 3 | Display alias while preserving raw label. |
| `cardOrder` | 5 | Primary/pinned card order if split from existing pinned order. |
| `rowOrder` | 5 | Row order per panel/type group. |
| `netAdapterOrder` | 5 | Network adapter subgroup order. |
| `hiddenNetAdapters` | 5 | Network adapter visibility. |
| `expanded` or equivalent transient state | 3 | Runtime only unless persistence is intentionally chosen. |

Normalizer rules:

- Drop invalid object shapes.
- Deduplicate arrays.
- Preserve missing-sensor references where useful so layout can recover if hardware returns.
- Never let invalid state prevent rendering.

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
- hidden sensor can be restored;
- reorders persist after reload;
- narrow viewports do not clip controls.

## 9. Branching Recommendation

Preferred:

```powershell
git checkout -b feat/web-dashboard-v3-card-first
```

The baseline route/menu/gauge and hardware-identity work is committed on `feat/web-dashboard-v3-card-first`. If new dirty work appears, commit or park that baseline before starting another parallel route/UI slice.

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

Start with Slice 1 in `/dash/cardtruth/` or a branch that can be served as a preview. The first concrete patch should add a tested display-model helper for range provenance and observed peaks, not a broad UI rewrite.

Acceptance for that first patch:

- `node webtests\selftest.node.js` adds and passes range display tests.
- CPU power remains number-only without override/limit.
- GPU power with valid watt+percent fixture gets an approximate derived limit.
- GPU power without percent fixture remains number-only or explicitly unknown.
- No product code mentions this machine's sensor IDs or device names.
