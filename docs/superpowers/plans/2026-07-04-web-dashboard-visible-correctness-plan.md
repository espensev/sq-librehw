# Web Dashboard Visible Correctness and Modern UX - Campaign

**Plan ID:** web-dashboard-visible-correctness-2026-07-04
**Date:** 2026-07-04
**Status:** superseded by [2026-07-06-web-dashboard-v3-next-plan.md](2026-07-06-web-dashboard-v3-next-plan.md)
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Earlier recipe:** [2026-07-04-web-dashboard-card-truth-plan.md](2026-07-04-web-dashboard-card-truth-plan.md)
**Blocking review:** [../../reviews/review-2026-07-04-web-dashboard-ui.md](../../reviews/review-2026-07-04-web-dashboard-ui.md)
**Next plan:** [2026-07-06-web-dashboard-v3-next-plan.md](2026-07-06-web-dashboard-v3-next-plan.md)
**Why this exists:** The model-truth base was merged to `master` in `34e1f09`, but the current visible dashboard review still shows the side Customize panel, bare or clipped power ceilings such as `/ 200`, questionable gauge scaling, inconsistent card color treatment, missing card-carried detail, and no visible reorder/alias workflow. This campaign is the corrective implementation plan from current `master`, not a resume of the removed `.worktrees/card-truth` checkout. It is not scoped only to defect cleanup: the product goal is a good-looking, modern, user-friendly telemetry UI with useful customization built into the surfaces operators already use.

**2026-07-06 update:** the immediate Pages menu and peak-derived gauge guard are now implemented and verified. Continue remaining v3 work from the machine-agnostic next plan linked above.

The repo does not contain `.codex/skills/project.toml` or `scripts/task_manager.py`, so this is a manual repo-local campaign plan.

## 1. Goal

Make the shipped web dashboard visibly satisfy the card-truth requirements while also becoming a polished modern monitoring UI: clear visual hierarchy, restrained but attractive card styling, deterministic status/type color language, ergonomic customization, and fast card/row workflows. The dashboard must keep the hardware truth honest: no hardcoded or unlabeled max values, gauge scaling from real/derived/override ranges or clearly marked estimates, fan gauges driven by paired Control percent with RPM as the numeric readout, clean two-GPU and duplicate-hardware identity, no normal side pane, detail/actions on cards and rows, dashboard-local aliases, visible sensor/card/row ordering, stable status/type colors, and graceful unknown-range rendering.

## 1.1 Review-to-Action Map

The action order is deliberately truth-first, then identity, then interaction, then movement, then polish. Do not spend polish time before the visible truth failures are closed.

| Review Finding | Action Owner | Required Fix | Acceptance Check |
|---|---|---|---|
| Bare guessed power ceilings | `sq-vc-a` | Render source-aware range labels, persist observed peaks, add derived RTX limit when inputs exist, and avoid fake CPU caps. | CPU/GPU power cards never show a bare `/ N`; they show derived/override/estimated/unknown provenance. |
| Side Customize drawer remains | `sq-vc-c` | Replace normal drawer workflows with card/row expansion and a compact hidden-sensor popover, then delete drawer DOM/CSS/handlers. | No `Customize` side drawer or scrim is present in the normal UI; card/row expansion has action parity. |
| Duplicate hardware merges | `sq-vc-b` | Group panels/heroes/order keys by `HardwareId`, suffix duplicate display names, and add duplicate NVMe/GPU tests. | Same-name NVMe devices render as separate panels; two GPUs have separate labels and cards. |
| Overlay controls collide with headers | `sq-vc-c` then `sq-vc-e` | Move card controls into a reserved in-flow gutter and prove hover/focus/touch do not overlap chips/icons. | Screenshots at narrow and desktop widths show no control/chip/icon collision. |
| GPU heroes collapse device identity | `sq-vc-b` | Build heroes per hardware group and include the iGPU policy from the spec. | RTX and Radeon/iGPU surfaces are visible and named distinctly. |
| 390px masthead clips controls | `sq-vc-e` | Redesign masthead mobile layout and constrain range/buttons so no horizontal overflow is hidden. | 320px, 390px, 640px screenshots show all controls fully reachable. |
| `observedMax` never updates | `sq-vc-a` | Add throttled observed peak merging during render and regression tests. | Reloaded dashboard keeps observed peak estimates stable and never shrinks below current raw value. |
| Cockpit skin feels noisy | `sq-vc-e` | Define a calmer status/type/unknown color hierarchy, refine cards, and reduce competing signals. | Default dashboard is readable without opening customization; color roles are explainable and deterministic. |

## 2. Exit Criteria

- [ ] The active local dashboard build has been verified against current `master` or the implementation branch, not a stale running executable.
- [ ] No power gauge renders a bare `/ 200` or any other invented ceiling; every ceiling shows `limit`, `override`, `band`, `derived`, or `estimated` provenance.
- [ ] RTX 5090 power uses a real/derived power limit when the watt sensor and percent-of-limit sensors are available; otherwise it is explicitly estimated/unknown.
- [ ] CPU power never uses a guessed hard cap; it uses observed peak estimate, explicit override, or no arc.
- [ ] Every fan with a paired Control sensor uses Control percent for the arc and RPM for the big value; unpaired fans do not pretend an RPM ceiling is exact.
- [ ] Two GPUs render as separate panels/heroes with distinct names, sensors, labels, limits, and card identities.
- [ ] Duplicate same-name hardware, including identical NVMe models, is keyed by `HardwareId` and not merged by display text.
- [ ] The Customize side drawer/pane DOM, opening button, scrim, drawer CSS, and drawer-only handlers are removed from normal workflow.
- [ ] The only non-card management surface is a compact masthead sensor popover for hidden/offscreen search, show/hide, reset-hidden, and pin-anything.
- [ ] Cards and rows expose detail directly: source hardware, raw LibreHardwareMonitor label, alias, unit, current/min/max, range provenance, status, raw `SensorId`, style, pin/hide, max override, and move controls.
- [ ] A sensor/card alias can be set and cleared for every sensor; raw LibreHardwareMonitor label and `SensorId` remain visible in expansion/search. Current-host example: `Fan #7` can display as `Pump` while detail still shows raw `Fan #7` and `/lpc/nct6701d/0/fan/6`.
- [ ] Primary cards, pinned cards, panels, individual sensor rows/lines, and network adapter groups are reorderable from the visible UI with keyboard/button fallback and persistence.
- [ ] Badge, icon, hover controls, value units, and ceiling suffixes never overlap or clip in dark/light themes at narrow and desktop widths.
- [ ] Card color treatment is deterministic: status rail/chip communicates state; type color/icon communicates kind; unknown/estimated states are visually muted instead of random-looking.
- [ ] The dashboard reads as a cohesive modern app in both dark and light themes: balanced spacing, readable type, refined cards, clear hierarchy, no random color drift, no novelty decoration, and no dense control clutter.
- [ ] Customization is user-friendly and local to context: common actions are one click away on cards/rows/headers; advanced controls are available on expansion without overwhelming the default scan view.
- [ ] Sensors without a reliable range render number-only or explicitly marked unknown/estimated; no fake exact range is shown.
- [ ] `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` passes.
- [ ] `node webtests\selftest.node.js` passes with new regression coverage.
- [ ] `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` passes without regenerating `LibreHardwareMonitor.Tests\data.golden.json`.
- [ ] `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` passes, using temp output if the live monitor locks `bin\Release`.
- [ ] Live local `http://localhost:8085/` and hosted `https://telemetry.seviq.org/` dark/light reviews are recorded in the spec verification log when deployment is expected to match.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
|---|---:|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | 1050 | modify | high - central state, model, render, drawer, card, row, panel, and drag logic |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | 267 | modify | high - card layout, drawer removal, popover, readout clipping, colors |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` | 80 | modify | medium - remove drawer DOM, add compact sensors popover |
| `webtests/console.tests.js` | 181 | modify | medium - model and state regression tests |
| `webtests/selftest.node.js` | 30 | inspect/modify only if runner needs new fixture plumbing | low |
| `docs/feature-web-dashboard-card-truth.md` | 134 | modify | low - verification log and current status |
| `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md` | 598 | modify | low - older recipe status and links |
| `docs/superpowers/plans/2026-07-04-web-dashboard-implementation-campaign.md` | 137 | modify | low - superseded campaign note |

## 4. Agent Roster

Default execution is serial. These are work packages, not launched agents.

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
|---|---|---|---|---|---:|---|
| `sq-vc-a` | `range-gauge-truth` | Finish range truth from current `master`: observed peaks, source-aware ceiling labels, derived RTX power limit, CPU power unknown/estimate behavior, and unknown-range rendering. | - | `console.js`, `webtests/console.tests.js`, docs log | 0 | high |
| `sq-vc-b` | `hardware-identity-gpus` | Re-key panels/cards/heroes by stable hardware identity and render two GPUs plus duplicate same-name devices separately. | `sq-vc-a` | `console.js`, `webtests/console.tests.js` | 1 | high |
| `sq-vc-c` | `card-first-controls` | Add card/row expansion with detail/actions, alias and max override controls, compact masthead sensor popover, then remove the Customize side drawer. | `sq-vc-b` | `console.js`, `console.css`, `index.html`, `webtests/console.tests.js` | 2 | high |
| `sq-vc-d` | `visible-ordering` | Add visible and keyboard-accessible ordering for primary cards, pinned cards, panels, sensor rows/lines, and network adapter groups directly from those surfaces. | `sq-vc-c` | `console.js`, `console.css`, `webtests/console.tests.js` | 3 | high |
| `sq-vc-e` | `modern-ui-customization-polish` | Polish the UI as a modern app: card composition, color system, theme balance, default scan ergonomics, customization discoverability, overlap/clipping fixes, and live verification. | `sq-vc-d` | `console.css`, `console.js`, docs log | 4 | high |

## 5. Dependency Graph

```text
Group 0: [sq-vc-a range-gauge-truth]
Group 1: [sq-vc-b hardware-identity-gpus]
Group 2: [sq-vc-c card-first-controls]
Group 3: [sq-vc-d visible-ordering]
Group 4: [sq-vc-e modern-ui-customization-polish]
```

All groups touch `console.js` directly or depend on its render/state contracts, so parallel work is intentionally avoided.

## 6. File Ownership Map

```text
console.js        -> sq-vc-a, then sq-vc-b, then sq-vc-c, then sq-vc-d, then sq-vc-e (serial only)
console.css       -> sq-vc-c, then sq-vc-d, then sq-vc-e (serial only)
index.html        -> sq-vc-c
console.tests.js  -> sq-vc-a, then sq-vc-b, then sq-vc-c, then sq-vc-d
selftest.node.js  -> inspect only unless test runner support is required
feature spec docs -> sq-vc-a/e verification log updates
older plan docs   -> sq-vc-e status/link cleanup
```

No file is assigned to two work packages in the same dependency group.

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
|---|---|---|
| `console.js` model/render/state/drag in one file | Yes | One implementation branch or strictly serial task commits; no parallel edits. |
| Drawer removal vs replacement controls | Yes | `sq-vc-c` must land expansion + popover parity before deleting drawer code. |
| Range model vs card rendering | Yes | `sq-vc-a` owns the `SQ.rangeFor` contract and card ceiling semantics before later UI consumes it. |
| Hardware identity vs ordering keys | Yes | `sq-vc-b` establishes `HardwareId`/panel keys before `sq-vc-d` writes row/network order maps. |
| Card detail controls vs random-looking colors/overlap | Yes | `sq-vc-e` is a full modern UI polish pass after controls/order surfaces exist. |
| Row line pin/hide without reorder | Yes | `sq-vc-d` adds row/line reorder controls beside the existing row actions, plus keyboard move buttons in row expansion. |
| `data.json` golden payload | No for this campaign | Do not touch `LibreHardwareMonitorLib` or server serialization. Run golden tests to prove. |
| Secondary `worktree-dashboard-templates` branch | No | Parked. Do not merge route/template tabs into this corrective branch unless explicitly requested. |

## 8. Integration Points

- `sq-vc-a` produces the range provenance contract: `override`, `limit`, `derived`, `band`, `peak`, and `unknown`.
- `sq-vc-b` consumes the range contract while changing panel/card identity keys to `HardwareId`; all later persisted order keys must use the new stable keys.
- `sq-vc-c` consumes range provenance and stable identity in card/row detail. It creates or finalizes `sensorAliases`, `cardOrder`, range override actions, and the masthead popover.
- `sq-vc-d` consumes the card/row detail contract for keyboard move controls and writes `rowOrder`, `netAdapterOrder`, and any panel/card order maps. Rows/lines must support reorder from the row itself; pinning alone is not sufficient.
- `sq-vc-e` consumes the final DOM structure to verify modern app quality: colors, spacing, card styling, customization ergonomics, suffix clipping, local/hosted parity, and docs.

## 9. Schema Changes

No database, server, or `data.json` schema changes.

Browser-local `sq.dashboard.v1` may add or finalize additive fields only:

- `rangeOverrides { [sensorId]: { max:number, min?:number } }`
- `observedMax { [sensorId]: number }`
- `sensorAliases { [sensorId]: string }`
- `cardOrder string[]`
- `rowOrder { [panelKey|type]: sensorId[] }`
- `netAdapterOrder string[]`
- `hiddenNetAdapters string[]`

The state version remains `1`; invalid fields are dropped by normalizers.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Live screenshots are from a stale executable, not current `master` | Medium | Medium | First task records active process/build provenance and verifies against current build before closing defects. |
| Side pane survives because hidden-sensor search still needs a list | Medium | High | Only a compact masthead popover may survive, and it is limited to hidden/offscreen search/show/pin/reset. |
| Card expansion becomes too dense | Medium | Medium | Show current/source/range/status by default, keep advanced controls compact, and ensure keyboard access. |
| Derived RTX power limit is noisy at idle | Medium | Medium | Use percent threshold and sample count; label derived approximate; override wins. |
| CPU power still looks capped | Medium | High | For CPU power, use override or observed/estimated provenance only; no semantic fixed cap. |
| Two GPUs or same-name drives still merge through display labels | Medium | High | Tests must include duplicate hardware text with distinct `HardwareId`. |
| Removing drawer loses keyboard reorder | Medium | High | Drawer deletion is blocked until card/row/panel up/down controls exist. |
| Colors continue to look arbitrary | Medium | Medium | Define stable mapping: state rail/chip = status, value/icon = sensor kind, unknown/estimated = muted. |
| Modern polish gets treated as cosmetic afterthought | Medium | Medium | Make visual hierarchy, spacing, theme quality, and customization ergonomics explicit exit criteria. |
| Row reorder remains hidden or drawer-only | Medium | High | Add reorder affordances to the row line next to existing pin/hide controls and to row expansion for keyboard access. |
| Unit/ceiling clipping remains in hosted/theme variant | Medium | Medium | Verify local and hosted, dark and light, narrow and desktop widths. |
| Accidental golden payload drift | Low | High | Restrict source files; run `dotnet test` with no golden regen. |

## 11. Verification Strategy

Run after each work package:

- [ ] `git diff --check`
- [ ] `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`
- [ ] `node webtests\selftest.node.js`

Run at campaign gate:

- [ ] `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- [ ] `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`, or temp output if default `bin\Release` is locked by the running monitor.

Manual/live smoke:

- [ ] Confirm active build provenance for `http://localhost:8085/`.
- [ ] Dark and light local review: no side Customize panel, no clipped units/ceilings, no overlap.
- [ ] Dark and light hosted review: `https://telemetry.seviq.org/` matches expected deployment state.
- [ ] GPU power card: not bare `/ 200`; derived/override/estimated state visible.
- [ ] CPU power card: no guessed fixed ceiling.
- [ ] Fan #7 card: arc uses Control percent, big value is RPM, alias `Pump` can be set/cleared.
- [ ] Two GPUs and duplicate NVMe devices remain separate.
- [ ] Reorder cards, panel, row/line, and network subgroup from their visible surfaces; reload and confirm persistence.
- [ ] Inspect the default dashboard without opening expansions: it should look modern, intentional, and readable, not like a random-color debug grid.

## 12. Documentation Updates

- Update `docs/feature-web-dashboard-card-truth.md` verification log with this corrective campaign and final live checks.
- Mark the older implementation campaign as continued/superseded by this plan because `.worktrees/card-truth` was merged and removed.
- Keep `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md` as the detailed recipe for known tasks, but update its current-status block to point at this corrective campaign from `master`.
- Do not change `docs/superpowers/specs/2026-07-04-dashboard-templates.md` or merge `worktree-dashboard-templates` during this campaign.

## 13. Branch and Execution Draft

Recommended branch:

```powershell
git checkout master
git checkout -b feat/web-visible-correctness
```

Do not resume `.worktrees/card-truth`; it was merged into `master` and removed. Keep the parked `worktree-dashboard-templates` branch separate.

If `master` has dirty docs only, either commit/stash the docs first or use a new worktree for implementation:

```powershell
git worktree add .worktrees/web-visible-correctness -b feat/web-visible-correctness master
```

Task order:

1. `sq-vc-a`: range/gauge truth.
2. `sq-vc-b`: hardware identity and GPU/device separation.
3. `sq-vc-c`: card-first controls and drawer deletion.
4. `sq-vc-d`: visible ordering surfaces, including row/line reorder from the row itself.
5. `sq-vc-e`: modern UI polish, customization ergonomics, live verification, docs log.

### 13.1 First Action Slice

Start with a single implementation branch and finish these commits before touching drawer deletion or broad visual polish:

1. **Range labels and observed peaks**
   - Add `SQ.mergeObservedPeaks(sensors, state)` or equivalent.
   - Update it from `render()` with throttled `saveDashboard()` behavior.
   - Change card ceiling markup to include provenance: `derived`, `override`, `estimated`, or no ceiling for semantic bands.
   - Add model tests for ratcheting observed peaks and source-aware label data.

2. **Derived RTX power limit**
   - Pair `Power/GPU Package` watts with `Load/GPU Power` or `Load/GPU Board Power` percent-of-limit under the same `hwid`.
   - Gate derivation to useful samples only: percent above idle noise, enough samples, median ratio, rounded to a stable display value.
   - Render derived values as approximate and overrideable; do not apply this rule to CPU power.

3. **Hardware identity baseline**
   - Introduce panel/device grouping helpers keyed by `HardwareId`.
   - Keep raw LibreHardwareMonitor labels visible, but suffix duplicate display names for UI identity.
   - Add duplicate-device fixture tests before changing the panel renderer.

Exit this first slice only when:

- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` passes.
- `node webtests\selftest.node.js` passes with new range and identity tests.
- A live screenshot shows no bare power ceiling on CPU/GPU cards.
- Same-name NVMe devices no longer merge into one visible panel.

Only after that first slice should the work move to card/row expansion, drawer removal, visible ordering, and full modern UI polish.

Implementation commits should stay small and reviewable, for example:

- `feat(web): label gauge range provenance and persist observed peaks`
- `feat(web): derive RTX power limits from percent-of-limit sensors`
- `feat(web): key panels and heroes by hardware identity`
- `feat(web): card and row expansion with alias and range override controls`
- `feat(web): replace Customize drawer with masthead sensor popover`
- `feat(web): visible ordering for cards rows and network groups`
- `fix(web): stabilize card color semantics and readout spacing`
- `feat(web): polish dashboard cards themes and customization ergonomics`
