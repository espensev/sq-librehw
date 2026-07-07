# Review - Latest Dashboard Work: D1 Card-Header Gutter (merged) + D2 Draft Checkpoint

**Date:** 2026-07-07
**Surface:** fixed point `4416db5` (pre-D1 master) → `D2-flyingcircus` (`11312f2`). Covers the D1 implementation merged to master at `47690a9` (code: `console.css`, `console.js`) and the D2 docs-only checkpoint commit on top.
**Spec source:** `docs/superpowers/plans/2026-07-07-web-card-header-gutter-d1.md` (D1 plan), `docs/feature-web-dashboard-card-truth.md` (parent v3 spec, finding #9), `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md` §4 rows D1/D2
**Standards sources:** none found at repo root (no CLAUDE.md/CONTRIBUTING.md; AGENTS.md present but process-oriented). Conventions taken from the v3 campaign non-negotiables stated in the D1 plan (client-only, no contract change, both themes, x64 build, DOM-less selftest as regression guard) and from nearby doc practice.
**Verdict:** PASS WITH NOTES

## Findings

### Medium

- [axis: spec] `docs/superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md` (§0 State, §10) and `docs/feature-web-dashboard-expansion-layout.md` (§11) — the checkpoint commit `11312f2` froze stale state claims into the resume surface.
  Evidence: handoff §0 says "D2 brainstorm is in progress on branch `D2-flyingcircus` (at `47690a9`, no commits yet). The working tree holds an **uncommitted** Draft D2 spec…"; §10 says the spec "is currently **uncommitted** on branch `D2-flyingcircus`"; the D2 spec §11 log says "Branch `D2-flyingcircus` == `master` (`47690a9`); D2 has not started." All three were true when written but false the moment they were committed: `git rev-parse D2-flyingcircus` = `11312f2` ≠ master `47690a9`, and the branch worktree is clean.
  Impact: the handoff §0 is explicitly the read-this-alone resume brief. A next session following it will look for uncommitted working-tree files, find a clean tree, and may conclude the draft was lost or that it must recreate it. If `D2-flyingcircus` were merged as-is, master's handoff would permanently misstate its own history.
  Recommendation: one small follow-up commit on `D2-flyingcircus` updating the three spots to "committed as checkpoint `11312f2`". Must land before this branch merges; does not affect D1, which is already on master.

### Low

- [axis: standards] `docs/feature-web-dashboard-card-truth.md:7` — header still reads `**Updated:** 2026-07-06` although this diff adds a 2026-07-07 D1 verification row and edits the related-docs line. The sibling D2 spec stamps `Updated: 2026-07-07`, so the convention exists. Recommendation: bump the date in the same follow-up commit.
- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:140` — new rule `.cell .chip-state{flex:none;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}`: `text-overflow:ellipsis` is inert here because `.chip-state` is `display:inline-flex` (console.css:142) and text-overflow only ellipsizes inline content of a block container, not flex items. If a chip ever overflows it will hard-clip instead of showing `…`. No overlap risk (`overflow:hidden` + the grid track still contain it) and current chip texts are short state words, so this is cosmetic-only and latent.
- [axis: spec] `docs/feature-web-dashboard-expansion-layout.md` §2 — "The inner detail grid … needs **300px for 2 columns / 450px for 3**" ignores the 14px column gap (`gap:7px 14px`, console.css:275): real thresholds are 314px / 478px. The conclusion (1 column inside a ~160px card interior) is unaffected; fix opportunistically when the spec goes Draft→Accepted.

## Verified claims (D1 implementation — no findings)

Independent re-derivation of the merged D1 change (`e0f1dad` + `0e0987a`), beyond what the docs assert:

- **Structural non-overlap holds.** `.chead{grid-template-columns:minmax(0,1fr) auto}` puts `.cell-ctl` in its own grid track with no `position`/`transform`/negative-margin escape; the cluster cannot paint over the chip/type-icon in column 1 at any width. Confirmed no other card-header generator exists: `class="k"` appears only in `cardEl` (console.js:1113-1114), and both PFD (`:1143`) and pinned (`:1131`) cards route through it; `index.html` contains no card header markup.
- **Reveal semantics are coherent.** `.cell-ctl{display:none}` at rest, `display:flex` on `.cell:hover/.cell:focus-within/@media(hover:none)` (console.css:255-260); the grip's own `display:none` → `inline-block` reveal rules (console.css:288-291) fire on exactly the same triggers, so the grip is never a visible-cluster/invisible-grip mismatch, including touch.
- **Keyboard reveal works.** `cell.tabIndex = 0` (console.js:1104), so `:focus-within` matches when the cell itself is focused; once revealed, the buttons are tabbable. Same reveal model as pre-D1 (rest state was `display:none` before too) — no a11y regression.
- **Drag/click paths unaffected.** Drag resolves the card via `grip.closest('.cell')` and the ghost label via descendant selector `.k .name` (console.js:1544) — both are depth-agnostic and still match under `.chead > .ktext`. The expand-toggle delegate ignores clicks on `button` (console.js:1456), so grip/pin/hide clicks don't toggle expansion.
- **Escaping safe in the new attribute context.** `esc()` (console.js:821) escapes `&<>"'`, so `aria-label="Drag to reorder ${esc(label)}"` cannot break out of the attribute.
- **Rows untouched.** The split `.row-ctl` rules are property-for-property identical to the old shared rules (position:absolute/display:none/gap/z-index preserved).
- **Docs' grounded line references are accurate.** Spot-checked every citation in the D2 draft spec: `.pfd` minmax(190px) grid (console.css:104-105), `.cell.expanded{height:auto}` (:270), `.xp-grid` minmax(150px) (:275), `.cell` padding `14px 15px 13px` (:106 → ~160px usable interior on a 190px card: correct), `--maxw:1520px` (:16,51), panel `column-width:370px` (:173), mobile `.pfd{grid-template-columns:1fr}` (:244), `xpEl` (console.js:1021), expansion append (:1120-1122), `state.expanded` (:816), shared `c:` key (:1105,1120,1458), and the §9 #4 claim that the Sensors-popover sig omits `a.active` (console.js:1286-1288 — confirmed, the `|net:` term joins only `a.key`).
- **Known documented tradeoffs re-confirmed as real but accepted:** hover/focus reveal reflows the name in-flow (by design, Option B); touch permanently occupies the gutter; the empty `auto` track leaves the 8px `gap` at rest.

## Verification

- `git rev-parse D2-flyingcircus / master / origin/master` — pass (`11312f2` / `47690a9` / `47690a9`; "pushed" claim in handoff confirmed)
- `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — pass
- `node webtests/selftest.node.js` — pass, `SELFTEST PASS 227/227` (worktree code == D2 tip code, since D2 adds only docs on top of master)
- `dotnet test` / x64 builds — not run (docs record 42/42 + 0/0 on `0e0987a`; no code changed since, and this is a lightweight review, not full QA)
- Live browser rect-intersection gate — not re-run (needs rebuilt EXE + chrome-devtools session; the D1 execution record documents RED 4 → GREEN 0 in both themes at 320/390/desktop with zero console errors)

## Coverage Notes

- Files reviewed deeply: `console.css` (full changed regions + surrounding rules), `console.js` (full changed hunk + all header/drag/click consumers), `feature-web-dashboard-expansion-layout.md` (full), `2026-07-07-web-card-header-gutter-d1.md` (full), `feature-web-dashboard-card-truth.md` (diff + D1 row), `feature-workflow.md` (diff), `2026-07-06-web-dashboard-v3-next-plan.md` (diff)
- Files reviewed via diff hunks only: `2026-07-06-web-dashboard-v3-continuation-handoff.md` (700+ lines; unchanged sections not re-read)

## Open Questions

- D2 §9 #1 (layout strategy, default B grid-breakout) and #2 (shared `c:` expand key) remain the operator's acceptance-blocking decisions — the spec correctly refuses to be normative until they're resolved. Nothing in this review changes the recommended defaults.
