# Web Dashboard Card Truth - Implementation Campaign Draft

**Plan ID:** web-card-truth-2026-07-04
**Date:** 2026-07-04
**Status:** draft
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Task recipe:** [2026-07-04-web-dashboard-card-truth-plan.md](2026-07-04-web-dashboard-card-truth-plan.md)

This is the execution-level campaign plan for the v3 web dashboard work. The
repo does not currently contain `.codex/skills/project.toml` or
`scripts/task_manager.py`, so this plan is a manual repo-local draft rather
than a registered task-manager campaign.

## 1. Goal

Implement the accepted v3 dashboard behavior in controlled worktree phases:
honest gauge ranges, fan Control percent gauges with RPM readouts, derived GPU
power limits where possible, clean multi-GPU and duplicate-hardware identity,
and card/row/header based customization with no normal side pane. The campaign
keeps phases A-D client-only so `data.json` and the golden-mastered downstream
contract do not change.

## 2. Exit Criteria

- [ ] Existing `.worktrees/card-truth` is finished through A5, B1, and B2, with `node webtests/selftest.node.js` passing.
- [ ] `master` receives the model-truth work before UI branches are cut.
- [ ] Card-first UI branch removes the Customize drawer and keeps normal details/actions on cards, rows, and headers.
- [ ] The only surviving non-card surface is a compact masthead popover for hidden/offscreen sensor search and restore.
- [ ] Primary cards, pinned cards, panels, sensor rows, and network adapter groups are orderable from their visible surfaces with keyboard/button fallback.
- [ ] Alias/rename works for every sensor/card/row, including displaying raw `Fan #7` as `Pump`, while raw LibreHardwareMonitor label and `SensorId` remain visible.
- [ ] Local and hosted dashboard smoke checks in dark and light themes show no bare `/ 200` power ceilings, no RPM-derived fan gauge ceilings when Control percent exists, and no unit/ceiling clipping.
- [ ] `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` passes.
- [ ] `node webtests\selftest.node.js` passes.
- [ ] `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` passes without regenerating `data.golden.json`.
- [ ] `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` passes, using temp output if the running app locks default output.
- [ ] Docs verification log is updated after live checks.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
|---|---:|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | 1013 | modify | high - central dashboard model, render, state, and drag surface |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | 278 | modify | medium - card layout and responsive UI |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` | 81 | modify | medium - removes drawer, adds masthead popover shell |
| `webtests/console.tests.js` | 162 | modify | medium - main JS model regression tests |
| `webtests/selftest.node.js` | 30 | inspect/modify if needed | low - runner only |
| `docs/feature-web-dashboard-card-truth.md` | 172 | modify | low - spec verification log |
| `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md` | 734 | modify | low - implementation checklist |
| `docs/superpowers/plans/2026-07-04-web-dashboard-implementation-campaign.md` | new | create | low - campaign plan |

## 4. Agent Roster

Letters use the local `sq-` namespace because this repo does not have a task
manager registry. These are work packages, not launched agents yet.

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
|---|---|---|---|---|---:|---|
| `sq-a` | `finish-model-truth` | Finish A4/A5/B1/B2 in `.worktrees/card-truth`: persisted peaks, honest ceilings, derived NVIDIA power limit, hardware identity, duplicate-name panels, per-device heroes. | - | `console.js`, `webtests/console.tests.js`, docs log | 0 | high |
| `sq-b` | `card-surface-ui` | Build card/row/header interaction surface: expansion details, alias, card order, masthead hidden-sensor popover, header/readout clipping fix, delete drawer. | `sq-a` | `console.js`, `console.css`, `index.html`, `webtests/console.tests.js` | 1 | high |
| `sq-c` | `row-network-order` | Add visible ordering for rows and network adapter groups using the card/row detail contract from `sq-b`. | `sq-b` | `console.js`, `console.css`, `webtests/console.tests.js` | 2 | medium |
| `sq-d` | `verify-live-docs` | Run build/test/live dark-light verification, compare local vs hosted, and update spec/plan verification logs. | `sq-c` | docs only, no product source | 3 | medium |

## 5. Dependency Graph

```text
Group 0: [sq-a finish-model-truth]
Group 1: [sq-b card-surface-ui]
Group 2: [sq-c row-network-order]
Group 3: [sq-d verify-live-docs]
```

Default execution is serial because `console.js` is a single high-conflict
entry point. Do not parallelize `sq-b` and `sq-c` unless `sq-b` first lands a
stable row-detail contract and an integrator is assigned to resolve conflicts.

## 6. File Ownership Map

The same files are touched across phases, but ownership is sequential:

```text
console.js        -> sq-a, then sq-b, then sq-c (serial handoff only)
console.css       -> sq-b, then sq-c (serial handoff only)
index.html        -> sq-b
console.tests.js  -> sq-a, then sq-b, then sq-c (serial handoff only)
selftest.node.js  -> inspect only unless runner breaks
feature spec docs -> sq-d verification log after implementation
```

No two work packages should edit these files concurrently.

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
|---|---|---|
| `console.js` state/model/render/drag all in one file | Yes | Serial work packages; each package rebases/merges after predecessor lands. |
| Drawer deletion vs popover reuse classes | Yes | `sq-b` owns `index.html`, drawer JS removal, and shared class reuse in one branch. |
| Row ordering depends on row-detail expansion | Yes | `sq-c` depends on `sq-b`; row buttons use the detail contract created by `sq-b`. |
| `data.json` golden payload | No in A-D | No server or `LibreHardwareMonitorLib` changes; run golden tests to prove. |
| Existing `.claude/worktrees/dashboard-templates` | No | Treat as secondary preview lane; do not edit or merge into main campaign. |

## 8. Integration Points

- `sq-a` replaces raw `speedoRange` behavior with `rangeFor` provenance, derived power tracking, and hardware identity helpers.
- `sq-b` consumes `rangeFor`, fan pairing, and hardware identity to render card details and labels without side UI.
- `sq-b` creates `sensorAliases` and `cardOrder`; `sq-c` must preserve and reuse the same state normalizer style.
- `sq-c` adds `rowOrder` and `netAdapterOrder` UI behavior on top of the card/row expansion and visible header controls.
- `sq-d` consumes the final implementation for local and hosted live review.

## 9. Schema Changes

No database or server schema changes.

Browser-local `sq.dashboard.v1` gains additive fields only:
`rangeOverrides`, `observedMax`, `sensorAliases`, `cardOrder`, `rowOrder`,
`netAdapterOrder`, and `hiddenNetAdapters`. The version remains `1`.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `console.js` merge conflicts | High | High | Serial branch order; no parallel edits to `console.js`. |
| Side drawer recreated as a large popover | Medium | Medium | Plan states popover is hidden/offscreen search only; normal details/actions stay on cards/rows/headers. |
| Derived NVIDIA power limit wrong at idle | Medium | Medium | Gate samples by percent threshold and count; mark derived limits with approximate label; user override wins. |
| Alias hides raw hardware truth | Medium | Medium | Expanded detail/search always show raw LibreHW label and `SensorId`; clearing alias restores raw label. |
| Keyboard reorder lost when drawer is deleted | Medium | High | Delete drawer only after card/row/header up/down controls exist. |
| Unit/ceiling clipping persists in one theme | Medium | Medium | Verify dark and light at desktop plus narrow widths. |
| Golden payload changes accidentally | Low | High | Do not touch server/lib code in A-D; run `dotnet test` golden suite. |

## 11. Verification Strategy

Run these at package boundaries:

- [ ] `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js`
- [ ] `node webtests\selftest.node.js`
- [ ] `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
- [ ] `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`

Live checks after `sq-b` and `sq-c`:

- [ ] Open `http://localhost:8085/` in dark and light themes.
- [ ] Open `https://telemetry.seviq.org/` in dark and light themes when hosted deployment is expected to match.
- [ ] Confirm RTX 5090 power is not bare `/ 200`.
- [ ] Confirm CPU power ceiling is not a fake hardcoded max.
- [ ] Confirm Fan #7 gauge uses paired Control percent and numeric RPM; alias `Pump` shows only as display label.
- [ ] Confirm raw `Fan #7` and `/lpc/nct6701d/0/fan/6` remain visible in detail/search.
- [ ] Confirm no side pane/drawer is available for normal details/actions.
- [ ] Confirm card, panel, row, and network adapter ordering persist after reload.
- [ ] Confirm dark/light card suffixes (`W`, `%`, `RPM`, ceilings) do not clip.

## 12. Documentation Updates

- Update `docs/feature-web-dashboard-card-truth.md` verification log after each live gate.
- Keep `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md` task statuses current.
- If a branch changes scope or ordering, update this campaign draft before implementing the changed behavior.
- Do not change `docs/superpowers/specs/2026-07-04-dashboard-templates.md` unless the operator explicitly resumes the secondary template lane.

## 13. Worktree Commands Draft

```powershell
# Existing model-truth worktree:
git -C .worktrees/card-truth status --short
git -C .worktrees/card-truth log --oneline -5

# After sq-a passes and is merged to master:
git checkout master
git pull --ff-only
git checkout -b feat/web-card-first

# After sq-b lands:
git checkout master
git pull --ff-only
git checkout -b feat/web-row-subgroup-order
```

Use `git worktree add` instead of switching branches in this checkout if the
main working tree remains dirty with docs.
