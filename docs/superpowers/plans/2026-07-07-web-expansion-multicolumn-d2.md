# Web Dashboard D2 — Expansion Multi-Column Breakout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Plan ID:** web-dashboard-d2-2026-07-07
**Spec:** [`../../feature-web-dashboard-expansion-layout.md`](../../feature-web-dashboard-expansion-layout.md) (Accepted 2026-07-07; §9 #1 = **B grid breakout, full row**, §9 #2 = **keep single `c:` key**, §9 #3 = rows verify-don't-break, §9 #4 = stays separate)
**Goal:** An expanded card breaks out to span the full row of its card grid so the existing `auto-fit` detail grid (`.xp-grid`) renders 2+ columns instead of a tall single-column strip.

**Architecture:** Strategy B — one CSS rule, `.cell.expanded{grid-column:1/-1}`. Both card containers (`#pfd` and `#pinned`, index.html:49) are the same `.pfd` grid (`repeat(auto-fit,minmax(min(100%,190px),1fr))`, console.css:105), and `grid-column:1/-1` resolves against the explicit auto-fit tracks, so the expanded cell spans every column at any viewport. The detail body `.xp` is an unconstrained child of `.cell`, so its inner `.xp-grid{repeat(auto-fit,minmax(150px,1fr))}` (console.css:275) immediately uses the released width (needs 314px for 2 columns — full row provides that at every desktop width). **Zero JS change:** `cardEl` already applies `.expanded` from `state.expanded` on every render tick, and the shared `c:<sensorId>` key means a PFD hero and its pinned twin break out identically.

**Tech Stack:** Vanilla JS/CSS/HTML embedded resources in `LibreHardwareMonitor.Windows.Forms.exe`, served at `http://localhost:8085/`. No framework.

## Global Constraints (campaign non-negotiables — every task inherits these)

- **No `data.json` / server / contract change.** Client-only. Golden `dotnet test` must stay 42/42 with zero server diffs.
- **No host-specific labels, limits, or sensor IDs in product code.**
- **Read-only dashboard.** No `/Sensor?action=Set` or write UI.
- **Raw LibreHardwareMonitor labels + `SensorId` stay visible** wherever aliases are used (unchanged here — `xpEl`'s cell inventory is not touched).
- **Both dark and light themes are first-class**, verified equally.
- **Build requires `-p:Platform=x64`** (AnyCPU breaks CsWin32). Stop the running EXE before building (it locks the DLL/EXE); restart after.
- **Scope is card expansion layout only.** Row expansion (`.rowxp`, already 2-column inside 370px panels) is verify-don't-break (spec §9 #3). The full width×theme matrix is D3. The D1 header gutter is done — do not reopen it.
- **The DOM-less selftest must stay green** (`node webtests/selftest.node.js`, currently 227/227). It cannot see `.cell`/CSS, so it is a regression guard here, not the D2 gate.
- **Do NOT add `grid-auto-flow:dense`** to `.pfd`: dense packing visually reorders cards, which breaks the drag/keyboard reorder mental model. The empty slot left mid-row by a breakout card is normal accordion-in-grid behavior and is judged at the live gate.

---

## File Structure

| File | Responsibility for D2 |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | Add the single breakout rule next to the existing expansion rules (~:270). |

No `console.js` change (`.expanded` is already applied per tick from `state.expanded`). No `index.html` change. No test-file change — see "Testing note".

## Testing note — why there is no new node unit test

Same constraint as D1: both `webtests/selftest.node.js` and `webtests/console.tests.js` are DOM-less and cannot measure layout. A "css file contains `grid-column:1/-1`" assertion would be test-theater. The honest, objective gate is a **live browser column-count + clip measurement**, run by the controller red→green (below). Per the campaign lesson from D1: a green measurement gate can still hide a visual regression, so the gate is **paired with live screenshots in both themes** before declaring done.

---

### Task 1: Expanded-card grid breakout

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:268-271` (card/row expansion block)

**Interfaces:**
- Consumes: the `.expanded` class that `cardEl` already toggles from `state.expanded` (`console.js:1120-1123`); the `.pfd` grid definition (`console.css:105`).
- Produces: CSS behavior only — an expanded `.cell` spans `grid-column:1/-1` in `#pfd` and `#pinned`. No new classes, no JS surface, no state-shape change.

- [ ] **Step 1: CSS — add the breakout rule.** In `console.css`, the expansion block currently reads (`:268-271`):

```css
/* card & row expansion - detail and actions live on the visible item */
.cell{cursor:pointer}
.cell.expanded,.cell.graph-on.expanded{height:auto}
.row{cursor:pointer}
```

Insert one rule directly after the `height:auto` line:

```css
.cell.expanded{grid-column:1/-1}
```

so the block becomes:

```css
/* card & row expansion - detail and actions live on the visible item */
.cell{cursor:pointer}
.cell.expanded,.cell.graph-on.expanded{height:auto}
.cell.expanded{grid-column:1/-1}
.row{cursor:pointer}
```

Rationale: `1/-1` spans the full explicit grid that `repeat(auto-fit,…)` creates, at every viewport (at ≤640px `.pfd` is `grid-template-columns:1fr`, console.css:244, so the rule is a no-op there — correct). No other selector in `console.css` sets `grid-column` on `.cell` (grep `grid-column` → only `.bar`, mobile `.fresh/.rate`, `.xp-id`).

- [ ] **Step 2: Static gates.**

```powershell
node webtests\selftest.node.js
git diff --check
```

Expected: `SELFTEST PASS 227/227` (regression guard — no new assertions); `git diff --check` clean. (`node --check console.js` is unnecessary — no JS change — but harmless if run.)

- [ ] **Step 3: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): break expanded cards out to the full grid row so detail renders multi-column (D2)"
```

---

## Controller-owned live gate (the real D2 verification — not a subagent task)

The implementer does static gates + commit only (no rebuild/browser; avoids app-lock and MCP-profile-lock cycles). The controller runs the live red→green measurement and the paired visual screenshots.

**Acceptance script** (chrome-devtools `evaluate_script`; run with a card expanded — click a card body, or `document.querySelector('.cell').click()` outside a button/`.xp`):

```js
(() => {
  const cell = document.querySelector('.cell.expanded');
  if (!cell) return { error: 'no expanded card — click one first' };
  const grid = cell.parentElement;                       // .pfd (#pfd or #pinned)
  const xp = cell.querySelector('.xp-grid');
  const acts = cell.querySelector('.xp-actions');
  const idEl = cell.querySelector('.xp-id code');
  const cols = getComputedStyle(xp).gridTemplateColumns.split(' ').filter(w => parseFloat(w) > 0).length;
  const cr = cell.getBoundingClientRect(), gr = grid.getBoundingClientRect(), ar = acts.getBoundingClientRect();
  return {
    cols,
    spansGrid: Math.abs(cr.width - gr.width) < 2,
    actionsClipped: ar.right > cr.right + 1 || acts.scrollWidth > acts.clientWidth + 1,
    idOverflow: idEl.scrollWidth > idEl.clientWidth + 1,
    hscroll: document.documentElement.scrollWidth > window.innerWidth
  };
})();
```

- [ ] **G0 — RED baseline (before rebuild).** On the currently-running D1 build at 1440×900, expand a card **in a full row** (the PFD/pinned grid must have enough cards that the row is packed to ~190–200px columns; if only a handful of heroes render, expand a *pinned* card in a fuller grid or narrow the window until columns hit minimum). Expect **`spansGrid:false` and `cols:1`**. If it returns `spansGrid:true` here, the script or setup is broken — fix the gate before trusting any green. Record the numbers.
- [ ] **G1 — Rebuild.** Stop the app, then:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

Restart the app; confirm `GET /` serves the rebuilt asset (served CSS contains `.cell.expanded{grid-column:1/-1}`); close stale pre-rebuild tabs (multi-tab version skew).
- [ ] **G2 — GREEN.** At 1440×900, expand the same card: expect **`spansGrid:true`, `cols ≥ 2`** (expect ~8–9 at full width), **`actionsClipped:false`, `idOverflow:false`, `hscroll:false`**. Then:
  - **Collapse restores:** click the card again; its width returns to a normal grid column (re-measure `getBoundingClientRect().width` ≈ pre-expand width; no residual span).
  - **D1 non-regression:** re-run the D1 rect-intersection gate (in `2026-07-07-web-card-header-gutter-d1.md`) on the expanded card under `hover:none` — expect `violations: []`.
  - **Rows verify-don't-break:** expand a panel row; `.rowxp`'s `.xp-grid` still reports `cols ≥ 2` at panel width and nothing clips.
  - **Widths × themes:** repeat the script at **320, 390, 640, 1440, and ≥1900** via `resize_page`, in **both dark and light**. At 320/390 `cols` may legitimately be 1–2; `hscroll` must stay `false` everywhere.
  - **Paired visual check (campaign lesson):** screenshot the expanded card at rest and mid-grid in **both themes** — judge that the mid-row empty slot and the widened body read as intentional, not broken. A green measurement alone does not close D2.
- [ ] **G3 — Console + contract clean.** Across several poll ticks with a card expanded: `list_console_messages` → zero errors. Then `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` → 42/42, golden untouched.

## Closeout (standard campaign procedure, after Task 1 review is clean)

- [ ] Spec `feature-web-dashboard-expansion-layout.md`: fill §10 implementation notes (strategy B full-row, twin-key kept, accepted tradeoffs observed live), add the §11 verification row (exact commands, RED→GREEN numbers, themes/widths), flip Status → Implemented/Verified per outcome.
- [ ] Verification-log entry in `docs/feature-web-dashboard-card-truth.md` §11 (D2 row): closes the 2026-07-06 audit's "tall single-column strip" finding.
- [ ] Advance the queue: `2026-07-06-web-dashboard-v3-next-plan.md` §4 row **D2 ✅** + critical path → D3; handoff §0/§10/§11 D2-done rows.
- [ ] Final whole-branch review (superpowers:requesting-code-review) on the branch range — model scaled to a one-rule presentational diff.
- [ ] Merge via superpowers:finishing-a-development-branch; post-merge selftest 227/227; leave rebuilt app running.

## Stop conditions (from the campaign)

Stop and review if: the default (non-expanded) view becomes less readable or reflows on poll ticks; the mid-row empty slot left by a broken-out card reads as a rendering bug in the paired visual check (fallback candidates: `span N` instead of full row, or accepting the hole — operator call, do not improvise); expanding/collapsing jumps scroll position disruptively; any drag/expand/keyboard-move path breaks; row expansion regresses; the app cannot restart after build.
