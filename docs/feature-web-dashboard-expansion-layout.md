# Feature Spec: Web Dashboard Expansion Multi-Column Layout (D2)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Verified <!-- Draft | Accepted | Implemented | Verified | Done -->
**Updated:** 2026-07-07 (accepted; §9 #1 re-resolved same day to **anchored overlay** per operator UX feedback, #2 = keep single key; implementation plan rev 2 [`superpowers/plans/2026-07-07-web-expansion-multicolumn-d2.md`](superpowers/plans/2026-07-07-web-expansion-multicolumn-d2.md))
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

> **Normative since 2026-07-07.** §9 #1 was resolved twice the same day: first to B grid-breakout (recommended default), then **re-resolved to the anchored overlay** after direct operator UX feedback that in-flow displacement itself is the defect ("it moves all the cards down, creating an ugly transition"). The original candidate list is retained in §9 for the record.

**Normative behavior (anchored overlay):**

- Expanding a card renders the detail body as a **floating overlay panel anchored directly below the card**, spanning the **full width of the card grid** (`.pfd` — both `#pfd` and `#pinned`). It paints **above** the cards/sections beneath it (elevated `z-index`, panel background + border + shadow — popover semantics).
- **Zero displacement:** expanding or collapsing moves no other card and does not change the expanded card's own size. (The pre-D2 in-flow growth — `.cell.expanded{height:auto}` — is removed for cards; `.rowxp` keeps its in-flow behavior.)
- The expanded card is **visibly marked** (accent border) so the overlay reads as attached to it; `aria-expanded` stays on the card and the overlay is its next DOM sibling (tab order stays adjacent).
- The detail content is **unchanged** — the `xpEl` inventory (8 label:value cells, full-width `SensorId` block, `.xp-actions` row) renders inside the overlay; the inner `.xp-grid` auto-fit now gets the grid's full width → **2+ columns** wherever the grid provides ≥314px.
- **Single-open (cards):** opening a card's overlay closes any other open card overlay (row expansions unaffected). The PFD/pinned **twin lockstep** (shared `c:` key, §9 #2) still applies: a sensor present in both grids shows one overlay per grid, each anchored locally.
- **Close paths:** click the card again (toggle, as today), press **Esc** (existing handler, `console.js:1585`), or click empty page space outside any card/overlay/row/control (capture-phase listener; a click on a *different card* is not "outside" — it switches the overlay via single-open). Clicks **inside** the overlay never close it (it hosts alias/range inputs).
- **Entrance:** the overlay fades/slides in once when opened (~140ms). The animation must **not replay on poll-tick rerenders** — gate it on the open action, not on render. `prefers-reduced-motion` disables it via the existing global rule.
- On viewport **resize** while open, the overlay re-anchors to the card immediately (listener), not merely on the next poll tick.
- The `.xp-actions` row remains a wrapped flex row and stays reachable, not clipped or overlapped, at any width; the `SensorId` `<code>` block stays full-width (`grid-column:1/-1`) with `overflow-wrap:anywhere`.
- Collapsing removes the overlay with no residual layout shift.

## 5. UI, Settings, API, and Data Impact

| Surface | Change |
|---|---|
| UI/menu/dialogs | Expanded card detail renders as a full-grid-width **anchored overlay** below its card: zero sibling displacement, single-open, Esc/click-away close, one-shot entrance fade. Rows keep the in-flow `.rowxp`. |
| Settings/config | **None.** Expansion state stays in-memory (`state.expanded`); no new persisted fields. |
| Remote web/API | None. |
| Logging/files | None. |
| Hardware/admin flow | None. |

## 6. Compatibility and Risk

| Risk | Mitigation |
|---|---|
| **Shared expand key ties PFD + pinned twins together** (`console.js:1105,1120,1458`; key `c:<sensorId>`). | Resolved (§9 #2): single key kept. Each grid anchors its own overlay locally, so lockstep costs nothing extra. |
| **Overlay anchor goes stale** — the `top` offset is measured at render; a viewport resize between poll ticks would leave it misaligned | Re-anchor on a `window resize` listener, not just on the next tick. |
| **Poll rebuild replays the entrance animation** — cards (and the overlay) are rebuilt every tick, so a naive CSS animation strobes once per second | Gate the `.enter` animation class on the open *action* (a one-shot state flag cleared after the toggle's own rerender), never on plain renders. Verified at G3. |
| **Capture-phase click-away vs. rebuild race** (the B3/C1 menu lesson: a bubble-phase rebuild detaches `e.target` and makes everything read as "outside") | The click-away listener ignores clicks landing on *any* card/row/control surface and only closes on genuinely empty space; card-to-card switching is owned by `toggleExpand`'s single-open logic, so the capture handler never races the delegated toggle. |
| **Overlay floats over content below the grid** (cards in later rows, next section head) | By design — popover semantics. Judged at the live visual gate in both themes; the overlay carries panel background/border/shadow so it reads as a layer, not a glitch. |
| DOM-less selftest cannot see layout | Same constraint as D1: the node harness is a regression guard, not the gate. Gate is a controller-owned live rect/column-count measurement (§8). |
| Narrow widths (320/390) | At ≤640px the card grid is 1 column (`console.css:244`), so the overlay spans that single column and floats over the next card until closed. Verify no clip, no horizontal scroll, and that toggle/Esc/click-away all work under touch emulation. |
| Upstream sync | Client-only (`Resources/Web/*` + `webtests/*`); same isolation promise as A–D1. |
| `net472` vs `net10.0-windows` | Both targets embed the same web assets; no target-specific behavior. |

## 7. Acceptance Criteria

- [ ] Expanding a card on a wide desktop (≥1280px) renders `.xp-grid` in **2+ columns** inside a full-grid-width overlay, not a single tall strip.
- [ ] **Zero displacement:** the bounding rects of every other card are byte-identical before/during/after expansion, and the expanded card's own rect is unchanged (including `graph-on` cards, whose fixed 172px height must not change on expand).
- [ ] Single-open works (opening card B closes card A's overlay); Esc closes; a click on empty page space closes; clicks inside the overlay (alias input, range inputs, style select, buttons) never close it; clicking the card again toggles.
- [ ] The entrance animation plays once on open and does **not** replay on poll-tick rerenders (no 1 Hz strobe).
- [ ] The overlay re-anchors correctly on viewport resize while open.
- [ ] Row expansion (`.rowxp`) still renders correctly and is not regressed at panel width (~370px) or narrow widths.
- [ ] `.xp-actions` controls remain reachable, not clipped or overlapped, in dark and light, at 320/390/640/1440/wide; no horizontal scroll anywhere.
- [ ] `SensorId` code block stays full-width with `overflow-wrap:anywhere`; long ids do not blow out layout.
- [ ] No `data.json`/server/contract change: `dotnet test` 42/42 untouched, golden untouched.
- [ ] Existing behavior not in scope remains unchanged: card/row content inventory, `state.expanded` in-memory lifecycle, drag/keyboard-move, alias/override/style/pin/hide paths, raw label + `SensorId` visibility, the D1 card-header gutter (its overlap gate re-run stays green).

## 8. Verification Plan

| Check | Command or manual step | Expected result |
|---|---|---|
| Syntax | `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` | clean |
| Model regression | `node webtests\selftest.node.js` | `SELFTEST PASS 227/227` (regression guard — no new assertions; harness is DOM-less) |
| No-contract gate | `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` | 42/42, golden untouched |
| Build modern | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` | 0 errors |
| **Live displacement + overlay gate (the real D2 evidence)** | chrome-devtools `evaluate_script` (full script in the plan): snapshot every card's rect by `data-sid`, toggle a card, re-snapshot → count moved cards; assert overlay exists, spans the grid width, sits below the card, `.xp-grid` columns ≥2 on wide (≥1 at 320), `.xp-actions` unclipped, no horizontal scroll. Then exercise single-open, Esc, click-away, click-inside-stays, resize re-anchor; watch several poll ticks for animation strobe. Run both themes, 320/390/640/1440/wide, plus paired screenshots. | RED baseline (pre-fix) = cards below **move** and cols = 1 at packed width → GREEN: **moved = 0**, overlay spans grid, cols ≥ 2, all close paths work, no strobe, zero clip; both themes. |
| Console clean | `list_console_messages` across several poll ticks with a card expanded | zero errors (catches dangling refs the selftest can't) |

The live gate mirrors D1's controller-owned pattern (the DOM-less harness cannot measure layout).

## 9. Open Decisions

> **Resolved 2026-07-07, twice.** First pass: the operator accepted the recommended defaults ("start plan d2") — #1 = B grid-breakout, #2 = keep single key. **Same-day re-resolution of #1:** direct operator UX feedback identified in-flow displacement itself as the defect ("the dropdown … moves all the cards down, creating an ugly transition"), which rules out all in-flow strategies (B/C/A). **#1 = anchored overlay** (a D-variant that keeps the detail visually attached to its card: full-grid-width floating panel anchored below the card, zero displacement, single-open, Esc/click-away close). **#2 unchanged = keep the single `c:<sensorId>` key.** #3 stays verify-don't-break; #4 stays a separate fix. §4 is normative for the anchored overlay. The options table is retained for the record.

| Decision | Needed before | Options & current default |
|---|---|---|
| **#1 Layout strategy** | spec acceptance | **B — Grid breakout (recommended default)**: most faithful to "detail on the item", minimal DOM change, reuses existing `.xp-grid`; cost = grid reflow on expand. **D — Modal/overlay**: cleanest full-width, zero grid disruption, well-understood focus mgmt; cost = diverges from inline principle. **C — Span-N**: middle ground; finicky with auto-fit. **A — Portal below grid**: most literal horizontal-space use; breaks "on the item" hardest. Recommend **B** unless the reflow on rerender proves unacceptable. |
| **#2 Shared expand key (PFD/pinned twins)** | spec acceptance | The `c:<sensorId>` key + shared `cardEl` path mean both twins expand in lockstep and share the chosen layout. **Keep single key (default)** if twins are rarely adjacent and lockstep expansion is acceptable. **Split namespace** (`c-pfd:`/`c-pinned:`) only if a strategy needs them to differ. Default: **keep single key**. |
| **#3 Row expansion scope** | implementation | Rows already get 2 columns in a 370px panel. **Verify-don't-break (default)**: apply the same rules where free, but D2's acceptance is card-focused. Optionally widen rows if a breakout strategy makes it cheap. |
| **#4 C1 follow-up (c) — stale `· idle` popover marker** | optional fold-in | 1-line fix (`console.js:1286-1288`, append `':'+a.active` to the `|net:` sig term). Can ride along as a freebie commit since it's D-phase, or stay separate. Default: separate. |

## 10. Implementation Notes

Shipped as **anchored overlay** (strategy per §9 #1 re-resolution), commits `e33bd6e` (render/position) + `ab1c930` (interaction semantics), plan `superpowers/plans/2026-07-07-web-expansion-multicolumn-d2.md` rev 2 executed subagent-driven with per-task reviews (both Approved 0C/0I).

- **Render:** `cardEl` no longer appends `.xp` into the cell (only toggles `.expanded`); `placeCardOverlay(grid, cards)` (console.js, above `renderPinnedCards`) portals one `.xp.xp-overlay` per grid as the expanded cell's next sibling, `top = offsetTop + offsetHeight + 6`, `left:0;right:0` (full `.pfd` width, `z-index:6`). `cardRange(h)` extracted for reuse. Resize listener re-anchors both grids.
- **Sizing invariant:** `.cell.expanded,.cell.graph-on.expanded{height:auto}` removed — expanded cards keep their exact size (graph-on stays 172px); `.cell.expanded` now carries only an accent border.
- **Semantics:** single-open for `c:` keys in `toggleExpand` (rows keep multi-open); one-shot `.enter` entrance via `state.xpEnter` set before the synchronous `rerender()` and cleared after (poll ticks never animate); capture-phase click-away listener blind to all card/row/control surfaces; pre-existing Esc handler untouched.
- **Twin-key decision:** single `c:<sensorId>` key kept — live-verified that a sensor in both grids gets one locally-anchored overlay per grid.
- **Accepted tradeoffs (observed live):** the overlay covers the card row(s) beneath it while open (popover semantics — border+shadow make it read as a layer); the pre-existing DevTools autofill audit note on `xpEl` inputs (no id/name) is unchanged by D2.

## 11. Verification Log

| Date | Build/run evidence | Result | Notes |
|---|---|---|---|
| 2026-07-07 | Spec drafted from D-phase audit: grounded expansion-width measurements (`console.css:105,270,275`; `console.js:1021-1065,1122`), `xpEl` inventory, shared-key constraint, responsive-hook inventory. | pending | Draft only; awaiting §9 decisions before acceptance and implementation. Checkpointed as `11312f2` on branch `D2-flyingcircus` (docs-only, on top of master `47690a9`); implementation has not started. |
| 2026-07-07 | Independent review of the D1 merge + this checkpoint: [`docs/reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md`](reviews/review-2026-07-07-dashboard-d1-d2-checkpoint.md). Every grounded line reference in this spec re-verified against source, incl. the §9 #4 sig-omits-`a.active` claim. | pass with notes | Notes fixed in the follow-up commit: stale "uncommitted"/"no commits yet" resume claims, card-truth `Updated:` bump, 300→314px gap arithmetic (§2). Latent cosmetic note parked in the v3-next-plan D3 row: `.cell .chip-state` `text-overflow:ellipsis` is inert on an `inline-flex` container (chips hard-clip if ever too long). |
| 2026-07-07 | §9 #1/#2 resolved by the operator with the recommended defaults (B grid-breakout full-row; keep single `c:` key); spec flipped Draft → **Accepted**. Implementation plan authored: [`superpowers/plans/2026-07-07-web-expansion-multicolumn-d2.md`](superpowers/plans/2026-07-07-web-expansion-multicolumn-d2.md) (single CSS-rule task, `#pfd`+`#pinned` both covered via shared `.pfd`; controller-owned live column-count/clip gate + paired both-theme screenshots). | pending | Superseded same day — see next row. |
| 2026-07-07 | Operator UX feedback on the flight deck re-opened §9 #1: "the dropdown … moves all the cards down, creating an ugly transition" — displacement is the defect, so in-flow B is out. **#1 re-resolved → anchored overlay** (chosen over true modal and keep-B in an explicit 3-way question). §4/§5/§6/§7/§8 rewritten for the overlay; plan rewritten (rev 2: two tasks — overlay render/position + interaction semantics; displacement-based RED→GREEN gate). Companion finding (deck editing buried in `.xp-actions`) queued as **D2a** in the v3-next-plan. | superseded by execution | See next row. |
| 2026-07-07 | Plan rev 2 executed subagent-driven (`e33bd6e` + `ab1c930`, task reviews both Approved 0C/0I) + controller live gate on rebuilt EXE (stamp `0.9.6+ab1c930.2026-07-07`, chrome-devtools, dark+light). **RED** (pre-D2 build, 1440×900, 14 cards): expand → `moved:9, cols:1, overlay:false`. **GREEN**: `moved:0` at 1440/640/320/1920 (1920 initial `moved:2` diagnosed as live-poll rerender noise — 3 paused runs all 0) and 390-touch; `cols` 8/3/1/8/2 respectively (1 col legit ≤320); overlay spans grid, below card, anchor delta 0 (one false reading was a mid-`.enter`-animation rect artifact); `.enter` plays once and is absent on poll rerenders (no strobe); single-open switches with exactly one overlay; click-inside stays; Esc + click-empty-space close; resize 1440→900 re-anchors delta 0; twin pin/unpin round-trip → one locally-anchored overlay per grid, state restored; rows `.rowxp` in-flow 2-col untouched; **D1 gutter gate re-run: 14 checked / 4 chips / violations `[]`**; no hscroll anywhere; both-theme screenshots judged intentional (overlay reads as elevated layer). Console 0 errors/0 warnings whole session; golden `dotnet test` 42/42; selftest 227/227 both tasks. | **pass** | Closes the 2026-07-06 "tall single-column strip" audit finding AND the 2026-07-07 operator displacement complaint. Status → Verified. Next: final whole-branch review → merge. |
