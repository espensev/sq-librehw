# Review - Deploy delta: wheel scope and graph menu revisions

**Date:** 2026-11-10
**Surface:** fixed point `1bf2655f...master` (`42015e60`) — everything the next
SND-DESK promotion would install beyond the currently running runtime
**Spec source:** `docs/features/feature-graph-pane-controls.md`
**Standards sources:** `AGENTS.md`, `docs/README.md`, `docs/architecture/upstream-divergence.md`
**Verdict:** PASS — no blocking findings; deployment may proceed with the separately authorized promotion

## Findings

No high or medium findings. Notes only:

- **Low / note:** `UI/PlotPanel.cs:1106` (`UpdateZoomScopeLabel`) assigns
  `_optionsButton.Size = _optionsButton.PreferredSize` while the button has
  `AutoSize = true`; the assignment is redundant under AutoSize and only the
  `Left` correction matters. No behavioral effect; fixture bounds tests pass.
- **Low / note:** pane-section menu items are re-created on every menu open and
  the previous set is dropped by `ToolStripItemCollection.Clear()`, which
  detaches but does not dispose. This matches the pre-change behavior for
  short-lived items, and the persistent option set is disposed explicitly at
  `UI/PlotPanel.cs:2300-2305`. Not a defect; recorded because disposal is
  ownership-critical for the persistent items.
- **Low / spec (open follow-up, not this change):** overlay mode with the
  default `This pane` scope performs no wheel zoom over the shared plot area;
  the spec keeps the `Shift` + wheel overlay alternative as an operator
  decision (`feature-graph-pane-controls.md`, Follow-ups).

## Independent checks

- Wheel contract: `ZoomAtPointer` consumes every modifier combination; only
  plain (values) and Shift (time) act. Normalized anchors are computed from the
  pointed pane's height (values) or the plot's horizontal fraction (time),
  clamped to `[0,1]`; a miss or ambiguous shared label resolves no target.
  `ZoomTargets` snapshots every target range before mutation, deduplicates the
  shared time axis, skips empty and disabled axes, and preserves dormant
  overrides. Age-space anchoring keeps the live edge exact
  (`UI/PlotPanel.cs:1536-1631`).
- Shared Auto: `PlotLineSeries.UpdateAxisMaxMin` includes a detached pane's
  data extent in the shared axis, and the settings-save guard now returns only
  when no visible series has samples, so an all-detached Auto session no longer
  stores the empty-axis fallback (`UI/PlotTimeAxes.cs:362-380`,
  `UI/PlotPanel.cs:877-894`).
- Menus: one persistent set of graph-wide items moves between the root and the
  `Graph Options` submenu without recreation; `Zoom Scope` and the pane section
  bracket it; the Time Axis submenu shape matches the spec sketch including
  separators; `Height` is omitted in overlay mode; `Lanes` reuses the pane
  builder with no duplicate `Auto Range`; keyboard invocation targets the
  focused visible pane and falls back to graph-wide only (`UI/PlotPanel.cs:896-1064`).
- History clearing: both entry points route through
  `MainForm.ConfirmClearGraphHistory`, defaulting to No; real-dialog tests
  verify the default button and that only Yes clears (`UI/MainForm.cs:2461-2476`).
- Prompts: content-sized `PlotPromptDialog` with unit-suffixed labels, wrapped
  validation, OK-validates-first, and owner-screen centering clamped to the
  working area including negative coordinates.
- Renderer: choice dots and scaled checks are vertically centred; state text is
  painted explicitly on submenu parents (WinForms suppresses the native paint);
  separators, headers and blended theme colors derive from existing menu colors.
- Standards: ledger rows updated in the same pass for every touched
  upstream-owned file; new fork-only files are recorded
  (`docs/architecture/upstream-divergence.md`). No generated output in source.
- Regression coverage added in the delta maps one-to-one onto the spec's
  acceptance criteria (menu ownership/state/overlay, scope surface and restart,
  keyboard menu, shared labels, unassigned bindings, double-click/touch reset
  neutrality, tracker recovery, all-pane time/value zoom, Auto stability).

## Verification

- `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf -p:Platform=x64`:
  Application 404 passed / 1 established skip, Library 193 passed, Contracts
  124 passed (721 passed total) — at `42015e60`, fresh run this session.
- `dotnet build ... -c Release -f net10.0-windows -p:Platform=x64 --no-incremental`:
  0 warnings, 0 errors.
- `dotnet build ... -c Release -f net472 -p:Platform=x64 --no-incremental`:
  0 warnings, 0 errors.
- `eng/ci/Test-LhmCiGates.ps1 -Filter Test-UpstreamDivergenceLedger`: 15
  passed, 0 failed.
- `git status`: clean except the pre-existing untracked Application
  `TestResults/` directory; `master`, `origin/master`, `librehw-host/main` all
  at `42015e60`.
- `Get-VerifiedMachineIdentity.ps1`: `VERIFIED` for `snd-desk`, instance
  `ca96d510-7d87-4cec-8e1a-bd8fc3866903`.

## Coverage notes

- Deep-reviewed: `PlotPanel.cs`, `PlotTimeAxes.cs`, `PlotPromptDialog.cs`,
  `ThemedMenuRenderer.cs`, `PlotRangePrompt.cs`, `PlotLaneNamePrompt.cs`,
  `Theme.cs`, `UserRadioGroup.cs`, `MainForm.cs` (full delta), and
  `GraphMenuPresentationTests.cs` (full file).
- Sampled: `PlotPanelPaneControlsTests.cs` (delta test inventory — names and
  targeted behaviors, not every assertion), `PlotPanelLaneTests.cs`,
  docs/spec deltas (status rows, ledger, spec acceptance sections). The
  previous continuation and completion reviews deep-reviewed those suites.
- Not re-reviewed: hardware, HTTP, dashboard, and settings surfaces unchanged
  in this delta.

## Open questions

- None blocking. Installed-runtime acceptance (menus, gestures, removal,
  restart, DPI) remains post-promotion work recorded in the spec.
