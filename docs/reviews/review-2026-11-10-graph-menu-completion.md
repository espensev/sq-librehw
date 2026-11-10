# Review - Graph menu continuation

**Date:** 2026-11-10
**Surface:** `e890693d..dd5b30bb` intake, then the completion diff over `dd5b30bb`
**Spec source:** `docs/features/feature-graph-pane-controls.md`
**Standards sources:** `AGENTS.md`, `docs/README.md`, upstream divergence ledger
**Verdict:** PASS WITH NOTES - intake findings resolved; installed acceptance remains separate

## Findings

- **Medium / spec:** `UI/PlotPanel.cs:363-449` still repeats the pane controls
  in Lanes and appends every graph-wide option to pane context menus. Overlay
  mode still offers Height. The latest commit adds names but changes none of
  these behaviors. Complete the shared menu layout and preserve item ownership
  when moving persistent options between the root and Graph Options.
  **Resolved:** shared items move without recreation; identity, handlers and
  disposal are regression-tested. Lanes reuses the pane builder.
- **Medium / regression:** `UI/MainForm.cs:2462` clears every sensor history
  without confirmation under the Reset Graph View label. Implement the approved
  Clear Graph History label and a shared confirmation defaulting to No.
  **Resolved:** both entry points use the same confirmed command; real-dialog
  tests verify the default and both outcomes against fixture history.
- **Medium / spec:** `UI/PlotRangePrompt.cs:24-42` and
  `UI/PlotLaneNamePrompt.cs:17-31` combine fixed bounds with a scaled system
  font. Replace their geometry with content layout and owner-screen positioning.
  **Resolved:** shared content layout, units, wrapped validation and owner-screen
  clamping; three font sizes and negative monitor coordinates tested.
- **Low / spec:** the inherited renderer still uses fixed check geometry,
  invisible separators and disabled pane headers. Implement the specified
  fork-only renderer, keeping the upstream renderer unchanged.
  **Resolved:** themed checks, dots, headers, separators and state columns.
  Visual review caught missing parent-state paint and a bitmap regression now
  guards it. The inherited renderer and radio-item source remain unchanged.

## Verification and coverage

- Intake `git rev-parse` and both `git ls-remote` checks: local `master` was
  `dd5b30bb`; both remotes are `aa93cb23`. The design and naming commits are
  local and will be included in the verified delivery.
- The installed `Runtime/release.json` still declares `1bf2655f`.
- Read the menu contract, both earlier reviews, the naming diff, menu builders,
  input targeting, renderer, radio group, both prompts and history handler.
- Existing gesture semantics, hardware, release promotion and the broader
  repository-hygiene audit are outside this implementation pass.
- Related stale installed/rollback claims in three feature specs now point to
  manifests; historical graph behavior is consolidated into the current
  contract while dated verification evidence is retained.
- The operator confirmed the spec defaults during this review. Overlay wheel
  targeting remains the existing explicit-scope contract.

## Completion evidence

The owning feature spec records the full evidence: 721 tests passed, one
established skip, both Release frameworks warning-free and 15 ledger assertions
passed. Visual fixtures cover three themes and three font sizes at 144 DPI.

Deep-reviewed: menu ownership/rebuild/disposal, state and choice rendering,
both prompt paths, the shared history confirmation, all changed tests, spec
acceptance and divergence rows. Other source areas were not re-reviewed.
Live gestures, real monitor switching and restart persistence remain open.
