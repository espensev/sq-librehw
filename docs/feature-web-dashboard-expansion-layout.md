# Feature Spec: Web Dashboard Expansion Multi-Column Layout (D2)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Draft <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-07-07
**Related docs:** [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md) (parent v3 spec), [`superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`](superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md) §4 row D2 / §5 Slice 6, [`superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md`](superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md) §0/§11/§12
**Purpose:** when a card or row is expanded, the detail/action body fills the available horizontal space instead of stacking into a tall narrow strip constrained to one card's column width.

## 1. Summary

Expanded card/row detail renders as a readable multi-column layout that uses the dashboard's horizontal space. Today the card expansion (`.xp`) is hard-constrained to a single ~190px card column, so its 8 label:value detail cells collapse into one tall single-column strip even though the inner grid is already declared `repeat(auto-fit,minmax(150px,1fr))`. D2 removes that constraint so the existing multi-column grid finally gets room, and verifies the result across the responsive/theme matrix that D3 will formally close.

## 2. Problem and Motivation

The 2026-07-06 live browser audit (recorded in the parent spec §11) flagged as a Slice 6 polish item: *"expansion renders as a tall single-column strip leaving large empty horizontal space."* Grounded measurements (all `Resources/Web/`):

- The primary/pinned card grid is `.pfd{grid-template-columns:repeat(auto-fit,minmax(min(100%,190px),1fr))}` (`console.css:105`), so each `.cell` is a **~190px-min grid item**.
- `.cell.expanded` only releases **height**, never width: `.cell.expanded,.cell.graph-on.expanded{height:auto}` (`console.css:270`).
- `.xp` is appended as a normal flex child of `.cell` (`console.js:1122`) with **no `grid-column` breakout** — confirmed by grepping `grid-column` in `console.css` (only `.bar`, mobile `.fresh/.rate`, and `.xp-id` use it).
- The inner detail grid `.xp-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr))}` (`console.css:275`) needs **314px for 2 columns / 478px for 3** (150px tracks + 14px column gaps). A card's usable interior is ~160px after `.cell` padding (`14px 15px 13px`, `console.css:106`), so `auto-fit` collapses to **1 column** and the 8 label:value cells (`label`, `raw label`, `source`, `hardware id`, `type`, `current`, `raw min/max`, `range`) stack vertically.
- Row expansion (`.rowxp`) is **less bad**: panels are `column-width:370px` (`console.css:173`), giving ~340px usable → `.xp-grid` already yields **2 columns**. So D2 is primarily a card problem; rows are a verify-don't-break case.

The dashboard's container caps at `--maxw:1520px` (`console.css:16,51`), so on a normal desktop there is ample horizontal space the expanded detail is currently unable to reach.

## 3. Goals and Non-Goals

**Goals**

- An expanded card's detail/action body uses the dashboard's horizontal space: the 8 detail cells flow into 2+ columns rather than one tall strip.
- The expanded detail stays associated with its card and remains keyboard/discoverable in place (faithful to the v3 principle *"detail and actions live on the visible item"*).
- Row expansion (already 2-column in a 370px panel) is not regressed; it may benefit from the same treatment where cheap.
- The expansion works at 320/390/640/1440/wide in dark and light (this is the D3 matrix; D2 must not make it worse and ideally closes the worst case).
- No `data.json`/server/contract change; no new persisted state; raw labels and `SensorId` stay visible; both themes first-class.

**Non-goals**

- No redesign of what the expansion *contains* — the cell/control inventory (`xpEl`, `console.js:1021-1065`) is unchanged; only its layout/width changes.
- No persistence of expansion state (stays in-memory `state.expanded`, `console.js:816`).
- No server-side work, no Phase E limit sensors, no context-dashboard (F) work.
- D3's full responsive QA matrix is **closed by D3**, not D2; D2 only needs to not regress narrow widths and ideally resolve the card-expansion worst case.

## 4. Behavior Specification

> **This section is normative only after the Open Decisions (§9) are resolved.** The layout strategy (breakout vs. modal vs. span-N) is an acceptance-blocking decision. The cell/control inventory and the *outcome* (multi-column detail using horizontal space) are normative regardless of strategy.

**Normative outcome (strategy-independent):**

- Expanding a card renders the detail body (`.xp-grid`'s 8 label:value cells + the full-width `SensorId` block) across **2 or more columns** wherever the container can provide ≥300px, instead of a single column.
- The `.xp-actions` row (alias, alias-clear, pin/unpin, primary add/remove, hide, style select on cards, max/min override, set-range, clear-range, move buttons) remains a wrapped flex row and stays reachable; it is not clipped or overlapped at any width.
- The `SensorId` `<code>` block stays full-width (`grid-column:1/-1`) and `overflow-wrap:anywhere` so long ids do not blow out the layout.
- Collapsing restores the card to its in-grid size and position with no residual layout shift beyond the normal rerender.

**Strategy candidates (decision in §9):**

- **B — Grid breakout (recommended default).** `.cell.expanded{grid-column:1/-1}` (full row) or a span value. The card widens within its `.pfd` grid when expanded; detail stays in the card's DOM. Minimal DOM change; reuses the existing `.xp-grid` auto-fit, which finally gets room.
- **D — Modal/overlay.** Expanding opens a centered, focus-managed overlay using full viewport width. Cleanest full-width and zero grid disruption; diverges from the inline principle the v3 campaign is built on.
- **C — Span-N.** `.cell.expanded{grid-column:span N}` middle ground between full-width and single-card.
- **A — Portal below grid.** Render the expansion as a full-width panel below the card grid row, not inside the card. Most literal "use horizontal space"; breaks "on the item" hardest.

See §9 for the tradeoffs that make this a maintainer decision.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Expanded card/row detail renders multi-column using available width. Strategy-dependent: in-grid breakout (B/C) reflows the grid on expand; modal (D) opens an overlay; portal (A) renders below the grid. |
| Settings/config | **None.** Expansion state stays in-memory (`state.expanded`); no new persisted fields. |
| Remote web/API | None. |
| Logging/files | None. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| **Shared expand key ties PFD + pinned twins together** (`console.js:1105,1120,1458`; key `c:<sensorId>`). Any layout applies identically to both instances because they share key *and* `cardEl` render path. | Decision needed (§9): keep single key (twins expand in lockstep — acceptable if they never render adjacent) or split namespace (`c-pfd:`/`c-pinned:`). Default: keep single key unless a strategy makes adjacency common. |
| Grid breakout (B/C) reflows sibling cards on expand | Acceptable if transient; verify it does not jump on every poll tick (rerender). If it does, scope breakout to a one-shot class toggle that survives rerender via `state.expanded`. |
| Modal (D) diverges from "inline" v3 principle | Only choose D if the maintainer explicitly accepts the divergence; document it as an accepted tradeoff in the parent spec. |
| DOM-less selftest cannot see layout | Same constraint as D1: the node harness is a regression guard, not the gate. Gate is a controller-owned live rect/column-count measurement (§8). |
| Narrow widths (320/390) | At 320px the card grid is already 1 column (`console.css:244 @media max-width:640px`), so a full-width breakout collapses to the viewport anyway — acceptable. Verify no clip. |
| Upstream sync | Client-only (`Resources/Web/*` + `webtests/*`); same isolation promise as A–D1. |
| `net472` vs `net10.0-windows` | Both targets embed the same web assets; no target-specific behavior. |

## 7. Acceptance Criteria

- [ ] Expanding a card on a wide desktop (≥1280px) renders `.xp-grid` in **2+ columns** (detail cells flow horizontally), not a single tall strip.
- [ ] Row expansion (`.rowxp`) still renders correctly and is not regressed at panel width (~370px) or narrow widths.
- [ ] `.xp-actions` controls remain reachable, not clipped or overlapped, in dark and light, at 320/390/640/1440/wide.
- [ ] `SensorId` code block stays full-width with `overflow-wrap:anywhere`; long ids do not blow out layout.
- [ ] Collapsing restores the card to its in-grid position with no residual shift.
- [ ] No `data.json`/server/contract change: `dotnet test` 42/42 untouched, golden untouched.
- [ ] Existing behavior not in scope remains unchanged: card/row content inventory, `state.expanded` in-memory lifecycle, drag/keyboard-move, alias/override/style/pin/hide paths, raw label + `SensorId` visibility, the D1 card-header gutter.

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Syntax | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` | clean |
| Model regression | `node webtests\selftest.node.js` | `SELFTEST PASS 227/227` (regression guard — no new assertions; harness is DOM-less) |
| No-contract gate | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | 42/42, golden untouched |
| Build modern | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| **Live column-count gate (the real D2 evidence)** | chrome-devtools `evaluate_script` on an expanded `.xp`/`.rowxp`: count rendered columns of `.xp-grid` via `getComputedStyle(el).gridTemplateColumns.split(' ').length`; confirm ≥2 on wide, ≥1 at 320; assert `.xp-actions` right edge ≤ card right edge (no clip); assert `SensorId` `<code>` `scrollWidth ≤ clientWidth` or wraps. Run both themes, 320/390/640/1440/wide. | RED baseline (pre-fix) = 1 column on cards at wide → GREEN ≥2; zero clip; both themes. |
| Console clean | `list_console_messages` across several poll ticks with a card expanded | zero errors (catches dangling refs the selftest can't) |

The live gate mirrors D1's controller-owned pattern (the DOM-less harness cannot measure layout).

## 9. Open Decisions

> This spec stays **Draft** until the maintainer resolves #1 and #2. Do not hide these in Implementation Notes.

| Decision | Needed before | Options & current default |
|---|---|---|
| **#1 Layout strategy** | spec acceptance | **B — Grid breakout (recommended default)**: most faithful to "detail on the item", minimal DOM change, reuses existing `.xp-grid`; cost = grid reflow on expand. **D — Modal/overlay**: cleanest full-width, zero grid disruption, well-understood focus mgmt; cost = diverges from inline principle. **C — Span-N**: middle ground; finicky with auto-fit. **A — Portal below grid**: most literal horizontal-space use; breaks "on the item" hardest. Recommend **B** unless the reflow on rerender proves unacceptable. |
| **#2 Shared expand key (PFD/pinned twins)** | spec acceptance | The `c:<sensorId>` key + shared `cardEl` path mean both twins expand in lockstep and share the chosen layout. **Keep single key (default)** if twins are rarely adjacent and lockstep expansion is acceptable. **Split namespace** (`c-pfd:`/`c-pinned:`) only if a strategy needs them to differ. Default: **keep single key**. |
| **#3 Row expansion scope** | implementation | Rows already get 2 columns in a 370px panel. **Verify-don't-break (default)**: apply the same rules where free, but D2's acceptance is card-focused. Optionally widen rows if a breakout strategy makes it cheap. |
| **#4 C1 follow-up (c) — stale `· idle` popover marker** | optional fold-in | 1-line fix (`console.js:1286-1288`, append `':'+a.active` to the `|net:` sig term). Can ride along as a freebie commit since it's D-phase, or stay separate. Default: separate. |

## 10. Implementation Notes

<Fill during/after implementation. Likely file paths: `Resources/Web/console.js:1021-1065` (`xpEl`) and `:1099-1124` (`cardEl` expansion append); `Resources/Web/console.css:268-283` (`.xp`/`.rowxp`/`.xp-grid`/`.xp-actions`) and `:105`/`:270` (`.pfd` grid / `.cell.expanded`). Record the chosen strategy, the twin-key decision, and any accepted tradeoff.>

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-07 | Spec drafted from D-phase audit: grounded expansion-width measurements (`console.css:105,270,275`; `console.js:1021-1065,1122`), `xpEl` inventory, shared-key constraint, responsive-hook inventory. | pending | Draft only; awaiting §9 decisions before acceptance and implementation. Checkpointed as `11312f2` on branch `D2-flyingcircus` (docs-only, on top of master `47690a9`); implementation has not started. |
| 2026-07-07 | Independent review of the D1 merge + this checkpoint: [`docs/reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md`](reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md). Every grounded line reference in this spec re-verified against source, incl. the §9 #4 sig-omits-`a.active` claim. | pass with notes | Notes fixed in the follow-up commit: stale "uncommitted"/"no commits yet" resume claims, card-truth `Updated:` bump, 300→314px gap arithmetic (§2). Latent cosmetic note parked in the v3-next-plan D3 row: `.cell .chip-state` `text-overflow:ellipsis` is inert on an `inline-flex` container (chips hard-clip if ever too long). |
