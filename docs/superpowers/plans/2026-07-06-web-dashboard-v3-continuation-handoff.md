# Web Dashboard v3 Continuation Plan and Handoff

**Date:** 2026-07-07 (updated after D1 merge; D2 brainstorm draft in progress)
**Status:** B1, B2, B3, **C1 (network adapter subgroups)**, and **D1 (card header grid + reserved action gutter)** are all MERGED to `master`. **D2 (expansion multi-column layout) has been brainstormed into a Draft spec** ([`feature-web-dashboard-expansion-layout.md`](../../feature-web-dashboard-expansion-layout.md)) but is **not yet accepted** — two acceptance-blocking decisions are open (that spec's §9). See the §0 Resume Brief, the [v3-next-plan §4](2026-07-06-web-dashboard-v3-next-plan.md) queue, and §10–§12 below. **The v3-next-plan §4 A–F queue is the authoritative sequence** wherever it disagrees with the Slice numbering in §5 below.
**Baseline:** `master` / `origin/master` at `47690a9` (`Merge card header reserved gutter (Phase D1)`); was `7130748` after C1, `e7ae6f0` after B3, `106f91d` after B2, and `4310a8b` when this handoff was first written.
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Active plan:** [2026-07-06-web-dashboard-v3-next-plan.md](2026-07-06-web-dashboard-v3-next-plan.md)
**Versioned-route spec:** [../../feature-web-dashboard-versioned-routes.md](../../feature-web-dashboard-versioned-routes.md)
**Context-dashboard spec:** [../specs/2026-07-04-dashboard-templates.md](../specs/2026-07-04-dashboard-templates.md) (Main/Gaming/Storage — preserved coexist lane, see §3.1)

## 0. Resume Brief — Start Here

*Read this section alone to resume. §1–§12 below are reference detail.*

**State (2026-07-07):**

- `master` = `origin/master` = `47690a9` (`Merge card header reserved gutter (Phase D1)`), on top of the C1
  baseline `4416db5` (pushed). **D1 is the current shipped product baseline.** No open PRs.
- **D2 brainstorm is checkpointed on branch `D2-flyingcircus`** (`11312f2`, one docs-only commit on top of
  `47690a9`): the Draft D2 spec `feature-web-dashboard-expansion-layout.md` plus cross-reference edits
  to `feature-web-dashboard-card-truth.md`, `feature-workflow.md`, and the D2 row of
  `2026-07-06-web-dashboard-v3-next-plan.md`. An independent review of the D1 merge + this checkpoint is
  recorded in [`docs/reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md`](../../reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md)
  (PASS WITH NOTES; the notes are fixed by the commit that carries this line). The design was **Accepted and
  then IMPLEMENTED + VERIFIED the same day**: §9 **#1 = anchored overlay** (re-resolved from the B
  grid-breakout default after operator UX feedback that in-flow expansion "moves all the cards down, creating
  an ugly transition") and **#2 = keep the single `c:<sensorId>` expand key**. Shipped as `e33bd6e` +
  `ab1c930` per plan rev 2 [`2026-07-07-web-expansion-multicolumn-d2.md`](2026-07-07-web-expansion-multicolumn-d2.md);
  live gate RED `moved:9` → GREEN `moved:0` across widths × themes (full evidence: expansion spec §11). The
  same feedback session queued **D2a — direct flight-deck edit controls** (v3-next-plan §4) as the next item.
- The two old experimental branches (`claude-devsev/loving-volhard-66d981`, `worktree-dashboard-templates`)
  remain reference-only (§2).
- Done + merged, keep as regression baseline: A1/A2 (suffix/fan clipping), **B1** masthead Sensors popover
  (`8291c89`), **B2** explicit primary-card selection (`106f91d`), **B3** Customize drawer removal
  (`e7ae6f0`), **C1** network adapter subgroups (`7130748`), **D1** card header reserved gutter
  (`e0f1dad`+`0e0987a`). See §11 for the log.
- App: `LibreHardwareMonitor.Windows.Forms.exe` serves `http://localhost:8085/` (stable) and
  `/dash/cardtruth/` (temporary preview). Web assets are **embedded resources** in
  `LibreHardwareMonitor.Windows.Forms/Resources/Web/{index.html,console.js,console.css}` —
  **rebuild the EXE for served changes to take effect, and stop the running EXE first (it locks the DLL/EXE).**
  As of this handoff the app is up and serving `/` and `/data.json` (both 200), built from the D1 code-HEAD
  (stamp `0.9.6+05f609c.2026-07-07`, an ancestor of `47690a9` with byte-identical D1 web assets).

**D1 — Card header grid + reserved action gutter is DONE** (branch `feat/web-card-header-gutter-d1`,
`e0f1dad` grid + `0e0987a` collapse-at-rest refine; execution record
[`2026-07-07-web-card-header-gutter-d1.md`](2026-07-07-web-card-header-gutter-d1.md)). The card header is a
two-track grid (`.chead{grid-template-columns:minmax(0,1fr) auto}`) with the `.cell-ctl` cluster in column 2,
so the controls **structurally cannot** overlap the state chip or type-icon (mutually-exclusive grid track, no
`position`/`transform`/negative-margin escape). The cluster collapses `display:none` at rest (default card names
stay full) and reveals `display:flex` in-flow on hover/focus/touch. selftest 227/227, golden 42/42, clean
`net10.0-windows` x64 rebuild of committed `0e0987a` 0/0 (stamp `0.9.6+0e0987a.2026-07-07`), live RED 4→GREEN 0
in both themes across desktop/touch/narrow, zero console errors; final whole-branch review (opus) READY TO MERGE
0C/0I. **Accepted tradeoffs:** desktop hover/focus reflows the name; touch permanently occupies the gutter
(graceful ellipsis, full raw label in expansion). **Next task: D2 — expansion multi-column layout**
(v3-next-plan §4 row D2 / §5 Slice 6 polish). Its Draft spec is already authored (see the D2 bullet above and
§10) and now needs the two spec-§9 decisions resolved before it can be planned.

<details><summary>C1 original brief (retained for reference)</summary>

Break the single merged Network panel into one subgroup per NIC.

- **Where:** `SQ.buildPanelItems` in `console.js:712`. It currently excludes NICs at `:716`
  (`if (s.cls === 'nic') return;`), then re-adds *all* active NIC sensors as one bucket at `:740`
  (`{ hw:'Network', key:'panel:network', ss: net }`); "active" = adapters that have a `Throughput` sensor
  with `raw > 0` (`:738`).
- **Do:** emit one panel item per adapter keyed by **`s.hwid`** — the stable per-NIC hardware id already
  used at `:738-739`, *not* a re-parsed `/nic/{GUID}` string (this supersedes the older §5 "id prefix"
  wording). Label from `s.hw`; dedupe duplicate adapter labels with the same `#N` pattern as `:727`. Apply
  `netAdapterOrder` (order) and `hiddenNetAdapters` (hide) — both already normalized in state (`:151-152`
  init, `:178-179` via `cleanStringList`). Hidden adapters must be restorable from the **Sensors popover**.
- **Reuse, don't reinvent:** mirror the existing panel reorder plumbing — `moveKey`/`mergeOrder` +
  `movePanel` (`console.js:846`) / `moveRow` (`:835`). **Carry the B3 no-op guard**
  (`if (next === merged) return;`) into the adapter reorder mutator — per §12.2 this bug recurs on every new
  reorder surface.
- **Keep it testable:** `SQ.buildPanelItems` is a pure, DOM-free helper (in the `SQ.*` block above
  `window.SQ = SQ` at `:744`; the selftest loads it with `SQ_NO_BOOT`). Add adapter-keying / order / hide as
  pure `SQ.*` helpers and TDD them in `webtests/selftest.node.js`. Full C1 contract + acceptance: **§5 Slice 5B**.

**Start:**

```powershell
git checkout master
git pull --ff-only origin master
git checkout -b feat/web-network-subgroups-c1
```

**Verify before closeout:**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js                 # keep green (currently 192)
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -c Release -p:Platform=x64   # 42 golden
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

Then **stop the app → rebuild → restart** and live-smoke in a **real browser across poll ticks, in BOTH dark
and light themes** — the light theme is a first-class constraint, and the DOM-less selftest cannot catch a
dangling reference or a theme regression (§12.4–12.5).

**Non-negotiables (§4 / §12.6):** no `data.json`/server/contract change (state stays browser-local under
`sq.dashboard.v1`, multi-tab-safe via `SQ.saveTelemetryState`); vanilla JS/CSS/HTML only; no host-specific
labels/limits/sensor IDs in product code; raw LHM label + `SensorId` visible wherever an alias shows; golden
(42) + selftest (192) stay green; both themes deliberately styled.

**Traps that will bite C1 specifically:** §12.2 reorder no-op guard (directly reusable), §12.3 inline-control
keyboard reachability (adapter-header controls must be focusable or always-visible), §12.4 live-only
ReferenceError gate, §12.5 the async-`<details>` / shared-expand-key / multi-tab-skew / MCP-lock gotchas.

</details>

---

## 1. Purpose

This document expands the remaining v3 work into implementation-ready detail and records a handoff
state evaluation after the Slice 3 PR was merged. It is intentionally docs/planning only: no product
code was changed while writing this handoff.

The next product work should start from a fresh branch off `master`. Do not resume the old
`feat/web-dashboard-v3-card-first` branch; it was merged and deleted after PR #25.

Recommended branch:

```powershell
git checkout master
git pull --ff-only origin master
git checkout -b feat/web-dashboard-v3-popover-promotion
```

## 2. Current State Evaluation

### Repository and GitHub

Verified on 2026-07-06:

| Surface | State |
|---|---|
| Current branch | `master`, tracking `origin/master` |
| Current commit | `4310a8b83a031f0877a5b14787c1d4377b467356` |
| Worktree status | clean before this handoff was written |
| Active worktrees | one main worktree: `E:/SQ_HQ/Monitoring/sq-librehw` |
| Open PRs | none from `gh pr list -R espensev/sq-librehw --state open` |
| Merged local branch refs | `feat/web-dashboard-versioned-routes` remains local and is merged into `master`; it can be deleted later as cleanup |
| Unmerged local branch refs | `claude-devsev/loving-volhard-66d981`, `worktree-dashboard-templates`; do not merge either wholesale |

The two unmerged local branches contain older experimental dashboard ideas. Treat them as reference
material only. Their commits predate the current route/menu, state-merge, hardware-identity, and
Slice 3 expansion work, so a full merge would reintroduce stale assumptions.

### Runtime

Verified on 2026-07-06 without stopping the app:

| Surface | State |
|---|---|
| Live process | PID `12204`, `LibreHardwareMonitor.Windows.Forms.exe` |
| Executable path | `bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe` |
| `GET /` | 200 |
| `GET /dash/cardtruth` | 200 |
| `GET /dash/cardtruth/` | 200 |
| `GET /dash/cardtruth/console.js` | 200 |
| `GET /data.json` | 200 |
| `GET /metrics` | 200 |
| Model test | `node webtests\selftest.node.js` -> `SELFTEST PASS 156/156` |

This proves the merged app is still serving the stable dashboard, the temporary card-truth preview,
and the read-only telemetry endpoints. It does not replace a full rebuild/browser smoke for the next
product change.

### Product Capabilities Already Merged

Stable `/` and preview `/dash/cardtruth/` both currently include:

- Pages menu linking the preview route and diagnostic endpoints.
- Route-root-absolute preview assets, so `/dash/cardtruth` and `/dash/cardtruth/` are both browser-viable.
- Separate localStorage namespaces: stable `sq.dashboard.v1`; preview `sq.dashboard.preview.cardtruth`.
- Honest range gating: peak-only observed maxima are not gauge-eligible.
- Range provenance labels for override, real limit, derived limit, semantic band, observed peak, and unknown.
- Machine-agnostic GPU power-limit derivation from same-`hwid` watt + percent-of-limit sensors.
- Fan Control pairing where a matching Control percent sensor exists.
- Hardware panels and GPU heroes keyed by `hwid`, not display text.
- Duplicate-name hardware handling in model tests.
- Background telemetry persistence via `SQ.saveTelemetryState`, so passive same-route tabs cannot overwrite fresh aliases, order, overrides, hidden state, card style, or card selection.
- Visible card and row expansion with raw label, `SensorId`, hardware id, current/min/max, range provenance, alias, style, max/min override, pin/hide, and keyboard move controls.
- Primary-card drag/order through `cardOrder`.
- Stable row ordering through `rowOrder[panelKey|type]`.
- Inline-edit protection so polling does not clobber active alias/range inputs.

### Remaining Product Gaps

The main remaining gaps are now narrower and more concrete:

| Gap | Current evidence | Required outcome |
|---|---|---|
| Customize drawer still exists | `index.html` still has `#customize`, `#customizeDrawer`, drawer scrim, drawer tabs; `console.js` still has `renderCustomize` and drawer event handlers | Replace with compact masthead Sensors popover, then delete drawer DOM/CSS/JS |
| Hidden/offscreen discovery is still drawer-centric | Hidden/search/card management lives in drawer render paths | Masthead `Sensors` popover supports search, show/hide, pin, reset hidden, and later hidden network adapter restore |
| Explicit primary-card selection is normalized but not fully surfaced | `primaryCards` exists in state but auto heroes remain the practical default path | Add direct "show as primary card" / "remove from primary cards" actions and keep drag order |
| Network subgroups are not implemented | `netAdapterOrder` and `hiddenNetAdapters` normalize, but Network panel still needs adapter subgroups and UI | One subgroup per adapter, keyed from stable NIC id prefix, reorderable/hideable/restorable |
| Preview route remains visible | Pages menu still links `/dash/cardtruth/` | After accepted behavior is synced, remove the preview route/menu entry unless a new active comparison exists |
| Card-truth visual treatment is still a dev route concept | `cardtruth` exists as a separate subsite namespace | If any visual treatment survives, expose it as a root dashboard Theme/view option, not as another page |
| Visual polish is not complete | Card controls and compact layouts still need systematic viewport/theme QA | No overlap/clipping at 320/390/640/wide; dark and light both polished |

## 3. Decision: What to Do With "Truth Cards"

The original intent of `/dash/cardtruth/` was a temporary development place, not a permanent product
page. That is still the correct decision.

Current evaluation:

- The accepted behavioral pieces from the card-truth work have already been merged into stable `/`
  through the Slice 3 PR: expansion/actions, aliasing, overrides, row ordering, range truth, and
  hardware identity are no longer preview-only.
- The route is still useful only as a comparison safety net while the drawer/popup/theme promotion
  work is underway.
- Keeping it as a permanent page would split operator state, create two places to fix every UI bug,
  and make the Pages menu look like product navigation when it is really a dev switch.

Target outcome:

- Retire `/dash/cardtruth/` after Slice 4-7 promotion closes.
- Remove the Pages-menu entry for Card Truth Preview at the same time.
- If the card-truth styling or density mode remains useful, add it as a selectable root dashboard
  view/theme field under stable `sq.dashboard.v1`.
- Keep color theme (`dark`/`light`) separate from layout/view mode. A practical shape is:
  - `theme`: `dark | light`
  - `viewTheme` or `dashboardView`: `standard | cardTruth`
- Do not import preview localStorage automatically. If state transfer is needed, make it an explicit
  operator action or one-time migration with tests.

## 3.1 Related Decision: Context Dashboards (Main / Gaming / Storage) — Coexist

Separate from the cardtruth question above, the operator confirmed on 2026-07-06 that the deferred
**context-dashboard** direction (selectable Main / Gaming / Storage views) is **preserved** and will
**coexist** with the v3 view-style selector as a second, orthogonal control — not be subsumed into it.
Spec: [`../specs/2026-07-04-dashboard-templates.md`](../specs/2026-07-04-dashboard-templates.md).

- **Two controls, two meanings.** View-style selector = *look/density* (`viewTheme: standard | cardTruth`)
  over one shared state. Context-dashboard switcher = *which sensors / cards / poll rate* per named
  dashboard, each with its own `sq.dashboard.{route}` namespace and client-side hash route (`/#/gaming`).
- **Orthogonal to route retirement.** Hash routing is independent of the `/dash/` server routes, so
  Slice 7 retiring `cardtruth` does not touch this lane.
- **Build the v3 selector route-namespace-ready now.** When Slice 7 adds the root selector and touches
  state, do not hardwire single-`sq.dashboard.v1` assumptions that would block `sq.dashboard.{route}`,
  so the later feature needs no architectural retrofit.
- **Preserve the spec, not the branch.** Keep the context-dashboard spec a live roadmap item and port
  its ideas onto the current baseline. Do not revive `worktree-dashboard-templates` (`4137cee`)
  wholesale — it predates hwid identity, range truth, and the state-merge work.

Sequence: this stays **deferred behind the v3 card-first baseline** (Slices 4–7). It is a future
campaign, not part of the popover/drawer/promotion work — recorded here only so that work does not
foreclose it.

## 4. Planning Principles for the Remaining Work

1. Stable `/` is the product target.
2. Preview routes are temporary comparison surfaces.
3. No fake gauges, no guessed ceilings, no host-specific sensor ids.
4. Raw labels and `SensorId` remain visible anywhere aliases are used.
5. Every visible order should be user-owned and changeable from the visible surface.
6. Drawer deletion must be parity-driven, not cosmetic. Remove it only after hidden/offscreen search,
   restore, pin, alias, style, override, and order workflows have replacement paths.
7. User-owned state writes must keep using the multi-tab-safe save pattern.
8. Avoid parallel product edits to `Resources/Web/console.js`. It is the central integration file.
9. Keep `data.json` unchanged unless a separate server/data feature is explicitly accepted.
10. Every slice updates the active spec verification log or this handoff with exact evidence.

## 5. Proposed Execution Campaign

### Slice 4A - Preflight and Preview Delta Audit

Goal: prove the stable and preview routes are synchronized enough to remove the preview later.

Tasks:

- Create a fresh branch from `master`.
- Diff stable and preview assets:
  - `Resources/Web/index.html` vs `Resources/WebDash/cardtruth/index.html`
  - `Resources/Web/console.css` vs `Resources/WebDash/cardtruth/console.css`
  - `Resources/Web/console.js` vs `Resources/WebDash/cardtruth/console.js`
- Classify every remaining delta:
  - route namespace delta that must stay while preview exists;
  - visual treatment delta that should become root Theme/view state;
  - stale duplicate code that should be synced;
  - behavior delta that should be promoted or discarded.
- Record the delta list in the active plan before deleting anything.

Acceptance:

- There is no unknown preview-only product behavior.
- Any visual difference worth keeping has a named root-dashboard option.
- The next slices know whether they must touch preview assets in lockstep or can retire them soon.

Recommended tests:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js
node webtests\selftest.node.js
```

### Slice 4B - Masthead Sensors Popover

Goal: replace drawer-only discovery/restore workflows with one compact masthead popover.

UI contract:

- Add a masthead button labelled `Sensors` with a count, for example `Sensors 12`.
- The count should be hidden/offscreen sensors, not total sensors.
- The popover is anchored near the masthead button and constrained to the viewport.
- It is not a side pane and does not carry full card detail bodies.
- Search is the primary interaction. A user should be able to type raw label, alias, hardware name,
  type, unit, or `SensorId`.

Popover row content:

- Alias or raw display label.
- Raw label when an alias is active.
- Hardware label.
- Type and current value.
- Visibility state: visible, hidden, offscreen/available.
- Compact actions:
  - show;
  - hide;
  - pin/unpin;
  - show as primary card / remove from primary cards, if Slice 5A is folded into the same patch;
  - reset hidden, as a global footer action.

State and model helpers:

- Keep `hiddenSensors` as the sensor-level hide list.
- Use existing `sensorAliases` for display.
- Add helpers that are easy to test without the DOM:
  - `SQ.sensorSearchText(sensor, state)` if not already pure enough;
  - `SQ.sensorVisibility(sensor, state, model)` -> `visible | hidden | offscreen`;
  - `SQ.hiddenSensorCount(allSensors, state, model)`;
  - `SQ.sensorPopoverRows(allSensors, state, query, model)`.
- Offscreen should mean "available in `data.json`, not currently visible as a row/card because of
  dashboard projection rules", not "hidden from raw telemetry".

Event behavior:

- Opening the popover should not pause polling.
- Typing into the search input should use the same inline-edit guard pattern as alias/override inputs.
- Escape closes the popover.
- Clicking outside closes the popover.
- Keyboard tab order stays inside normal document flow; no trap is needed for a small popover unless
  it becomes modal, which it should not.

Tests:

- Hidden count counts explicit hidden sensors.
- Search matches alias, raw label, hardware, type, and `SensorId`.
- Show removes a sensor id from hidden state.
- Hide adds a sensor id to hidden state.
- Pin/unpin uses existing pinned-card state.
- Reset hidden clears hidden sensor state without touching aliases/order/overrides.
- Popover rows preserve raw label and `SensorId` where aliases are active.

Exit:

- A hidden sensor can be found and restored without opening the drawer.
- A not-currently-carded sensor can be pinned from the masthead.
- No new server endpoint or data contract change.

### Slice 4C - Drawer Removal

Goal: delete the old Customize drawer after parity exists.

Delete from stable and preview if preview remains:

- `#customize` button.
- `#customizeDrawer` aside.
- `#customizeScrim`.
- drawer tabs and drawer search inputs.
- `renderCustomize`.
- drawer-specific event handlers.
- drawer-only CSS selectors.

Keep or migrate:

- Style select: already exists in card/row expansion.
- Alias set/clear: already exists in card/row expansion.
- Max/min override set/clear: already exists in card/row expansion.
- Pin/hide: already exists on cards/rows; popover covers hidden restoration.
- Panel order: panel headers already support visible ordering; verify keyboard fallback remains.
- Pinned order: existing drag plus keyboard controls should remain.
- Card order: existing drag plus expansion buttons should remain.

Tests:

- Source/DOM string checks do not find `customizeDrawer`, `renderCustomize`, drawer tabs, or drawer
  open handlers.
- Hidden sensor restore still works through Sensors popover.
- Card/row alias, style, override, pin/hide, and move tests stay green.
- Keyboard move controls still work without drawer.

Exit:

- No normal dashboard workflow requires a side drawer.
- Removing the drawer does not remove the only keyboard-accessible path for any ordering action.

### Slice 5A - Explicit Primary Card Selection

Goal: let the operator choose which sensors are primary cards instead of relying only on auto heroes.

State contract:

- `primaryCards: []` remains normalized.
- Empty or absent `primaryCards` means "auto mode": use `SQ.pickHero(...)`.
- The first deliberate add/remove switches to explicit mode. To avoid ambiguity, add either:
  - `primaryCardsMode: 'auto' | 'custom'`, or
  - a sentinel field such as `primaryCardsCustomized: true`.
- `cardOrder` remains the order list for primary cards. It should preserve ids that are temporarily
  missing so a device can return without losing layout.

UI contract:

- Card expansion:
  - If in primary cards: action says remove from primary.
  - If not in primary cards: action says show as primary.
  - If still in auto mode: explain through button state, not long helper text.
- Row expansion:
  - Add show/remove primary action for any card-worthy sensor.
- Sensors popover:
  - Add show/remove primary action if it fits without turning the popover into a full editor.
- Primary card grid:
  - Drag/drop order remains.
  - Keyboard move controls remain.

Model helpers:

- `SQ.primaryCardIds(allSensors, state)` -> selected ids or auto hero ids.
- `SQ.setPrimaryCard(state, id, enabled)` -> normalized state.
- `SQ.resetPrimaryCards(state)` -> returns to auto mode.
- `SQ.isPrimaryCard(state, id, allSensors)` -> boolean for UI.

Tests:

- Absent/empty primary state uses auto heroes.
- Adding a row sensor switches to explicit mode and includes the id.
- Removing an id keeps explicit mode unless reset is chosen.
- Reset returns to auto heroes.
- Card order applies to explicit selected cards.
- Missing selected sensor id is preserved in state but ignored in render.
- Same-route telemetry save does not clobber primary card edits.

Exit:

- The operator can choose and reorder visible primary cards without drawer state.

### Slice 5B - Network Adapter Subgroups

Goal: make the Network panel readable and orderable by adapter.

Identity:

- Derive adapter key from the stable sensor id prefix. For current LHM shapes this should be the
  `/nic/{GUID}` or equivalent hardware prefix, not the display label alone.
- Adapter display label is human-facing only.
- Duplicate or missing labels get deterministic suffixes.

Render model:

- Network panel contains adapter subgroups.
- Each subgroup header shows adapter name, activity summary, and compact controls.
- Inside each adapter, rows remain grouped by type where useful.
- Idle-adapter filtering can stay, but hidden adapters must be restorable from the Sensors popover.

State:

- `netAdapterOrder: [adapterKey]`
- `hiddenNetAdapters: [adapterKey]`
- Existing `rowOrder[adapterKey|type]` can order rows inside adapter/type groups.

UI actions:

- Move adapter earlier/later.
- Drag adapter subgroup where reliable.
- Hide adapter.
- Restore adapter from Sensors popover.
- Collapse/expand adapter if the row count is high.

Tests:

- Adapter key extraction is stable for GUID-like NIC ids.
- Duplicate adapter labels are readable and do not collide.
- Adapter order normalizes duplicates and preserves missing keys.
- Hidden adapter is absent from Network render and present in restore popover.
- Row order cannot move a row to another adapter/type group.

Exit:

- Network is no longer one merged bucket of identical upload/download rows.

### Slice 6 - UI Polish and Responsive QA

Goal: make the dashboard look intentional after the control migration.

Layout fixes:

- Card header has an explicit grid with a reserved action gutter.
- Control clusters are in-flow, not painted over chips/icons.
- Long labels and source lines truncate or wrap predictably.
- Value, unit, and range label reserve width so suffixes do not clip.
- Expansion bodies have stable spacing and do not change card width.
- Popovers fit 320 px and 390 px mobile widths.

Theme/view work:

- Keep color theme and view theme separate.
- Dark and light both need deliberate color decisions.
- Status color means health.
- Type color means sensor kind.
- Unknown/estimated is muted.
- Warning/error colors are not decorative.

Visual verification:

- Desktop 1365x768.
- Mobile 390x844.
- Narrow 320 px if Playwright can run it without excessive setup.
- Wide desktop if time allows.
- Dark and light.
- Hover-capable and touch/no-hover assumptions.

Tests and checks:

- Use Playwright/Chrome screenshots for stable `/`.
- If preview remains, also check `/dash/cardtruth/`.
- Inspect console errors.
- Check long labels, missing values, duplicate devices, and expanded rows/cards.

Exit:

- No visible overlap between chip, icon, controls, value, unit, or ceiling labels.
- The default dashboard feels like an operational monitoring app, not a debug sheet.

### Slice 7 - Preview Retirement and Theme Promotion

Goal: close the temporary dev subsite once accepted behavior is in stable `/`.

Preconditions:

- Sensors popover is in stable `/`.
- Drawer is gone from stable `/`.
- Explicit primary card selection is in stable `/`.
- Network subgroups are either implemented or explicitly deferred.
- Visual treatment decision is made: keep as root view/theme, or discard.

If keeping the visual treatment:

- Add a root dashboard view selector. Prefer a dropdown or segmented control near the existing Theme
  button rather than a Pages-menu entry.
- Persist under stable `sq.dashboard.v1`, for example:
  - `theme: dark | light`
  - `viewTheme: standard | cardTruth`
- Apply view classes on the root element.
- Do not read preview localStorage by default.

If retiring the route fully:

- Remove Card Truth Preview from the Pages menu.
- Remove `Resources/WebDash/cardtruth` embedded assets if no other test needs them.
- Remove or update route tests that require `/dash/cardtruth/`.
- Update `feature-web-dashboard-versioned-routes.md` to mark cardtruth retired but keep the general
  preview-route capability if the server still supports future routes.
- Keep `/data.json` and `/metrics` links.

Tests:

- Stable `/` still serves.
- Pages menu no longer links a retired route.
- If route removed, `/dash/cardtruth/` returns 404.
- If preview support remains for future versions, missing preview routes still return 404.
- Root Theme/view selector persists and reloads.
- Stable state namespace remains `sq.dashboard.v1`.

Exit:

- There is one product dashboard **surface** with no permanent dev/preview routes; the temporary `cardtruth` route is retired.
- The cardtruth *visual treatment* is a root-dashboard **view option** (`viewTheme`), not a second dashboard page.
- This closeout retires **dev/preview routes only**. It does **not** foreclose the separate, intended **context-dashboard** lane (Main/Gaming/Storage; see §3.1 and the context-dashboard spec), which is a distinct control with its own `sq.dashboard.{route}` namespaces and hash routing — deferred but preserved.

## 6. Verification Matrix for the Next Implementation Branch

Minimum closeout after any product-code slice:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

Add preview JS syntax while `/dash/cardtruth/` exists:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js
```

Live smoke after a rebuild/restart:

- `GET http://localhost:8085/`
- `GET http://localhost:8085/data.json`
- `GET http://localhost:8085/metrics`
- `GET http://localhost:8085/dash/cardtruth/` only while the preview route remains active

Browser smoke:

- stable `/`, desktop and mobile;
- preview route if still active;
- expansion, alias save, override save, primary-card add/remove, hidden restore, network reorder if
  those surfaces changed;
- console/page errors.

Before closeout:

- `git diff --check`
- update the feature spec verification log;
- update this handoff or supersede it with a new handoff;
- if the live app was stopped, restart it unless the maintainer explicitly wants it left stopped.

## 7. Subagent / Independent Review Plan

Use subagents for review and verification, not parallel edits to the same central JS file unless the
work is split very carefully.

Recommended independent passes before merge:

| Pass | Scope | What it should prove |
|---|---|---|
| Product-state review | `console.js`, state normalizers, localStorage writes | No user-owned state clobbering; no preview/stable namespace leak |
| UI/accessibility review | `index.html`, `console.css`, interaction handlers | Drawer parity exists; keyboard paths survived; no obvious overlap/clipping |
| Runtime/browser review | rebuilt app on `localhost:8085` | Stable route works; preview route behavior matches decision; no console errors |
| Docs/spec review | feature spec, next plan, versioned-route spec | Status, acceptance, and verification logs match actual code |

For implementation, prefer one integrator-owned branch. If parallelism is needed:

- Agent A: model tests/helpers only.
- Agent B: CSS/layout only after selectors/classes are agreed.
- Agent C: read-only reviewer.
- Integrator: owns `index.html` and `console.js` final wiring.

Do not let two agents independently rewrite `Resources/Web/console.js`.

## 8. Risks and Stop Conditions

Stop and review before merging if:

- Any change touches `HttpServer.BuildDataJsonObject`, `GenerateJsonForNode`, or data serialization.
- `data.golden.json` changes unexpectedly.
- A web UI path writes to hardware or calls `/Sensor?action=Set`.
- Drawer deletion removes the only keyboard path for ordering.
- Preview and stable localStorage namespaces begin reading/writing each other implicitly.
- Any product code hardcodes this machine's device name, sensor id, or GPU model.
- CPU/GPU power starts rendering a peak-derived gauge again.
- Browser visual checks show clipped controls or overlapping text at mobile widths.
- The app cannot be restarted after a build.

## 9. Suggested Commit Grouping

1. `docs(web): record v3 continuation handoff`
2. `feat(web): add masthead sensors popover`
3. `feat(web): remove customize drawer after parity`
4. `feat(web): add explicit primary card selection`
5. `feat(web): group network sensors by adapter`
6. `fix(web): polish dashboard responsive controls`
7. `feat(web): promote card truth view and retire preview route`
8. `docs(web): record v3 closeout verification`

Depending on risk, commits 2 and 3 can be separate PRs. Do not combine preview retirement with the
first popover implementation unless the route delta audit proves the preview is already redundant.

## 10. Immediate Next Step

**Superseded by progress.** B1 (Slice 4B, `8291c89`), B2 (Slice 5A, `106f91d`), B3 (Slice 4C, merge
`e7ae6f0`), **C1 (Slice 5B network adapter subgroups, merge `7130748`)**, and **D1 (Slice 6 card-header
reserved gutter, merge `47690a9`)** are all done and merged; their feature branches are deleted. Execution
records: [`2026-07-06-web-sensors-popover-b1.md`](2026-07-06-web-sensors-popover-b1.md),
[`2026-07-06-web-primary-card-selection-b2.md`](2026-07-06-web-primary-card-selection-b2.md),
[`2026-07-06-web-drawer-removal-b3.md`](2026-07-06-web-drawer-removal-b3.md), and
[`2026-07-07-web-card-header-gutter-d1.md`](2026-07-07-web-card-header-gutter-d1.md). See the §0 Resume Brief
for the current one-screen state and §11 for the full merge log.

**D2 — expansion multi-column layout (v3-next-plan §4 row D2 / §5 Slice 6) is mid-brainstorm.** A Draft spec
[`feature-web-dashboard-expansion-layout.md`](../../feature-web-dashboard-expansion-layout.md) is authored and
grounded — it diagnoses the ~190px `.pfd` card-column constraint (`console.css:105/270/275`) that forces the
already-`auto-fit` `.xp-grid` into a tall single-column strip, and inventories `xpEl` (`console.js:1021-1065`).
It is committed as checkpoint `11312f2` on branch `D2-flyingcircus`, together with its cross-reference edits. Exit
criterion: *expanded detail fills width, not a tall narrow strip* — spec §7 = `.xp-grid` renders 2+ columns on
wide, no `.xp-actions` clip, `SensorId` stays full-width, across 320/390/640/1440/wide in both themes.

**The §9 decisions are RESOLVED (2026-07-07) and the spec is Accepted:** #1 was first resolved to the B
grid-breakout default, then **re-resolved the same day to the anchored overlay** after operator UX feedback
(in-flow expansion "moves all the cards down, creating an ugly transition" — displacement is the defect, so
every in-flow strategy is out); **#2 = keep the single `c:<sensorId>` key** (twins each get a locally-anchored
overlay). Implementation plan **rev 2**:
[`2026-07-07-web-expansion-multicolumn-d2.md`](2026-07-07-web-expansion-multicolumn-d2.md) — Task 1 renders the
detail as a full-grid-width overlay anchored below the card (`placeCardOverlay`, zero displacement); Task 2
adds single-open, race-safe click-away close, and a one-shot entrance animation that cannot strobe on poll
rebuilds. Static gates for the implementer; **controller-owned live displacement gate** (RED `moved>0` →
GREEN `moved=0`, overlay spans grid, `.xp-grid` ≥2 columns), paired with screenshots in both themes. The same
feedback queued **D2a — direct flight-deck edit controls** (primary add/remove on the visible item; see
v3-next-plan §4 row D2a) to run after D2, before D3.

**That plan is now EXECUTED and live-gated** (see §0 and the expansion spec §11; merge is the remaining step
if this handoff still shows the branch unmerged). **Next work item after D2 lands: D2a — direct flight-deck
edit controls** (v3-next-plan §4 row D2a; needs its own brainstorm-lite + mini-plan), then D3. Carry §12
forward — especially the **live-only column-count / visual gate** (§12.4–12.5, mirrored in the spec's §8): D2 is
a layout change with **no node unit-test surface**, and a green measurement gate can still hide a visual
regression (D1's Option A passed every overlap check yet truncated names at rest). After D2: **D3** (full
responsive/theme QA matrix), then **E** (viewTheme selector + `cardtruth` retirement, §5 Slice 7), then **F**
(context dashboards — separate campaign, §3.1).

## 11. Progress Log (A → B, on `master`)

Reverse chronological; each phase has its own execution record in this folder.

| Phase | What landed | Merge / commit |
|---|---|---|
| **D2** | Expansion anchored overlay (Slice 6 polish / closes the "tall single-column strip" audit finding + the 2026-07-07 operator displacement complaint). Expanded card detail renders as a full-grid-width floating panel anchored below the card (`placeCardOverlay` portals `.xp.xp-overlay` into the `.pfd` grid as the cell's next sibling; `cardRange` extracted; resize re-anchor listener). **Zero displacement** — cards keep exact size (`height:auto` growth removed, graph-on stays 172px); live RED `moved:9, cols:1` → GREEN `moved:0`, cols≤8, across 320/390-touch/640/1440/1920 × dark/light. Single-open for `c:` keys (rows multi-open untouched); one-shot `.enter` entrance gated on `state.xpEnter` (never replays on poll ticks); capture-phase click-away blind to card/control surfaces; Esc handler reused. Twins (single `c:` key): one locally-anchored overlay per grid, live pin/unpin round-trip. D1 gutter gate re-run `violations:[]`. selftest 227/227, golden 42/42, rebuild 0/0 (`0.9.6+ab1c930.2026-07-07`), console 0 errors, task reviews 2× Approved 0C/0I, both-theme screenshots. Full evidence: expansion spec §11 + `.superpowers/sdd/progress.md`. | `e33bd6e` render + `ab1c930` semantics on `D2-flyingcircus` (plan rev 2, subagent-driven) |
| **D1** | Card header grid + reserved action gutter (Slice 6 / closes card-truth finding #9). Header restructured into a two-track grid `.chead{grid-template-columns:minmax(0,1fr) auto}` — name+chip (`.k`) / source+type-icon (`.k2`) in column 1, the `.cell-ctl` cluster (grip/pin/hide) in column 2 — so the control cluster **structurally cannot** overlap the state chip or type-icon (mutually-exclusive grid track, no `position`/`transform`/negative-margin escape). Design evolved A→B under the controller live gate: A reserved the gutter permanently (`visibility:hidden`) and passed every overlap check but **truncated chip-card names at rest** (`CPU Temp`→`CPU…`, a readability stop-condition); B (shipped) collapses `.cell-ctl` to `display:none` at rest (full names) and reveals it `display:flex` in-flow on `:hover`/`:focus-within`/`@media(hover:none)`, never overlaying. Grip reverts to shared `.grip` reveal rules; `.row-ctl`/panel heads/`index.html` byte-unchanged; cards-only. No node unit-test surface (both harnesses DOM-less) → controller-owned live rect-intersection gate: RED 4→GREEN 0 both themes across desktop/touch-390/narrow-320, 0 truncated names at rest, focus clears chip 8px, zero console errors. selftest 227/227, golden 42/42, clean `net10.0-windows` x64 rebuild 0/0 (stamp `0.9.6+0e0987a.2026-07-07`). Final whole-branch review (opus): READY TO MERGE, 0C/0I. **Accepted tradeoffs (non-blocking):** desktop hover/focus name reflow; touch permanently occupies the gutter (graceful ellipsis, full raw label in expansion); ~8px residual grid-gap at rest. | `e0f1dad` grid + `0e0987a` collapse-at-rest; merged to master via finishing-a-development-branch |
| **C1** | Network adapter subgroups (Slice 5B). One subsystem panel per **active** adapter keyed by `s.hwid` (`SQ.netAdapterKey`/`SQ.buildNetAdapters`; `SQ.buildPanelItems` gained a `state` arg), deduped `#N` labels, own `#netsec`/`#netPanels` section. Always-visible ▲▼ (`moveAdapter`, no-op-guarded §12.2) + ⊘ hide; drag → `netAdapterOrder` via a new `endDrag` `#netPanels` branch; `movePanel` filtered to non-nic so `panelOrder` never absorbs adapter keys; hidden-adapter sensors report `offscreen`; hidden adapters restore from the Sensors popover (`showAdapter`, sig-gated). selftest 227/227 (+35), golden 42/42, both builds 0/0. Live-verified dark+light on a 37-NIC host (5 active panels), zero console errors. **D-phase follow-ups (non-blocking, from the final whole-branch review):** (1) *idle-Show* — an adapter hidden while active then gone idle is un-hidden by Show but excluded from panels by the active filter until it transmits again (sensors stay in the popover; not a regression — idle adapters never rendered panels pre-C1). (2) *pinned-nic × adapter-hide asymmetry* — `SQ.visibleSensors` does not drop adapter-hidden nic sensors, so a nic sensor pinned via the B1 popover keeps rendering as a card after its adapter is ⊘-hidden, while the masthead badge counts it `offscreen`; arguably-correct (a pin is a deliberate override) → **product decision for D-phase**. (3) *stale `· idle` marker* — the popover restore-row rebuild signature omits `a.active`, so a hidden adapter flipping active↔idle while the popover is open keeps a stale marker until the sig bumps; 1-line fix (append `':'+a.active` to the `|net:` sig term). | `7130748` (merge Phase C1); `9443348` helpers + `cc156a5` grouping + `dfd0ddd` per-adapter items + `5b1dceb` offscreen + `b110b1a` render + `555e7ae` popover restore + `b65501b` docs |
| **B3** | Customize drawer removed after inline+popover parity. Parity re-assessment corrected the plan's gate: pinned-card reorder was **already** inline (expanded card `move-left`/`move-right` → `pinnedOrder`); only **panel** reorder was a real gap → added always-visible ▲▼ in the panel head + Subsystems "Reset order". Deleted `#customizeDrawer`/`#customizeScrim`/`#customize`, tabs, `renderCustomize`/`renderPinnedEditor`/`renderLayoutEditor`/`renderSensorRows`/`renamePinned`, drawer handlers, drawer CSS (shared `.iconbtn`/`.sensor-*` rules **split**, not deleted). | `e7ae6f0` (merge); `69252b4`+`f60fcda`+`4004822` |
| **B2** | Explicit primary-card selection: `primaryCardsCustomized` boolean sentinel, seed-from-visible on first add, seeded heroes keep curated presentation, Auto reset in PFD header. | `106f91d` |
| **B1** | Masthead Sensors popover: search (label/alias/hardware/type/`SensorId`), show/hide/pin/reset-hidden, hidden-count badge. Replaced drawer-only hidden discovery. | `8291c89` |
| **A1/A2** | Fan-card `cmd %` right-edge clip fix + value/unit/ceiling suffix overflow sweep (~300px), both themes. | merged pre-B1 |

## 12. Lessons & Gotchas Carried Forward

Cross-cutting lessons from A→B3 that the next phases (C1 first) should apply. These are also mirrored in
the maintainer's `~/.claude` auto-memory; they live here so the repo carries them too.

1. **Re-verify parity gates against the code, not the plan.** The plan asserted pinned-card *and* panel
   reorder were drawer-only; reading the actual handlers showed pinned-card reorder was already inline, so
   B3's real scope was "add panel reorder, then delete." Before scoping any deletion, read the live handler
   code and confirm each claimed gap.
2. **Reorder no-op guard (directly relevant to C1's adapter reorder).** `moveKey(list, key, delta)` returns
   the **same array reference** on out-of-bounds, and `mergeOrder([], keys)` materializes the *full* order
   from an empty saved order. So clicking ▲ on the top item (or ▼ on the bottom) from the default empty
   order would otherwise dirty the order list to a full N-key array and spuriously show a "Reset order"
   affordance. Guard every reorder mutator with reference-equality: `if (next === merged) return;`. Test the
   **no-op** path (top-▲ / bottom-▼ from default), not just a real move.
3. **Inline-control reachability.** `.cell-ctl`/`.row-ctl`/`.grip` are `display:none`, revealed on
   `:hover`/`:focus-within`, and are keyboard-reachable **only** because the parent `.cell`/`.row` carries
   `tabindex=0`. Panel heads have **no** tabindex, which is why panel reorder buttons had to be
   always-visible. **C1:** if an adapter subgroup header is not focusable, its reorder/hide controls must be
   always-visible, or add `tabindex=0` to the header.
4. **The ReferenceError gate — live-only.** After deleting functions, `node --check` + zero-residual greps +
   `selftest` passing do **not** prove the app runs; only a live browser console-clean check across several
   poll ticks catches a dangling call to a deleted function. The DOM-less node selftest cannot see it.
5. **Live-verification gotchas** (see the per-topic `~/.claude` memories): `<details>` `toggle` fires
   **async** (a synchronous eval right after `menu.open = true` reads the popover empty — re-inspect in a
   later call); a PFD card and its pinned twin **share** the expand key `c:<sensorId>` (toggling one toggles
   both); stale pre-rebuild browser tabs run OLD `console.js` and **strip** newly-added `sq.dashboard.v1`
   fields on save (multi-tab version skew — close/reload other tabs before trusting a persistence failure);
   the chrome-devtools MCP browser can lock mid-session (`already running for chrome-profile`) — kill the
   stale MCP chrome procs and reopen.
6. **Standing constraints (unchanged — re-apply to C1 and every later phase):** no `data.json`/server/
   contract change (state stays browser-local under `sq.dashboard.v1`); C# golden tests green (42/42) and
   web `selftest` green (currently 192); vanilla JS/CSS/HTML only, no framework; no host-specific labels/
   limits/sensor IDs in product code, and the raw LibreHardwareMonitor label + `SensorId` stay visible
   wherever an alias shows; build requires `-p:Platform=x64`; the running EXE locks the DLL/EXE so **stop the
   app before rebuilding**; all user-owned state writes go through the multi-tab-safe `SQ.saveTelemetryState`
   path; build stamp format `0.9.6+<sha>.<date>`.
