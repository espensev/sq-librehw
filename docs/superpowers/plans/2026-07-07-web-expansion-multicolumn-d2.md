# Web Dashboard D2 — Anchored-Overlay Expansion Implementation Plan (rev 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Plan ID:** web-dashboard-d2-2026-07-07 (rev 2 — supersedes the B grid-breakout rev 1 after operator UX feedback; see spec §9)
**Spec:** [`../../feature-web-dashboard-expansion-layout.md`](../../feature-web-dashboard-expansion-layout.md) (Accepted; §9 #1 = **anchored overlay**, §9 #2 = **keep single `c:` key**, §9 #3 = rows verify-don't-break, §9 #4 = stays separate)
**Goal:** An expanded card's detail renders as a floating panel anchored below the card, spanning the full card-grid width — multi-column detail with **zero displacement of other cards**.

**Architecture:** The expansion element leaves the card's flow. `cardEl` stops appending `.xp` into the cell; instead each card-grid renderer (`renderPFD`, `renderPinnedCards`) places one absolutely-positioned `.xp.xp-overlay` into its `.pfd` container (`position:relative`, console.css:105), anchored at `top = cell.offsetTop + cell.offsetHeight + 6` and `left:0;right:0` (full grid width). The overlay is inserted as the expanded cell's next DOM sibling so tab order stays adjacent. Cards keep their exact size (`height:auto` growth removed), so nothing below moves. Interaction hardening: single-open for cards, click-empty-space close (capture phase, race-safe), one-shot entrance animation gated on the open action so the 1 s poll rebuild cannot strobe it. Esc-close already exists (console.js:1585).

**Tech Stack:** Vanilla JS/CSS/HTML embedded resources in `LibreHardwareMonitor.Windows.Forms.exe`, served at `http://localhost:8085/`. No framework.

## Global Constraints (campaign non-negotiables — every task inherits these)

- **No `data.json` / server / contract change.** Client-only. Golden `dotnet test` must stay 42/42 with zero server diffs.
- **No host-specific labels, limits, or sensor IDs in product code.**
- **Read-only dashboard.** No `/Sensor?action=Set` or write UI.
- **Raw LibreHardwareMonitor labels + `SensorId` stay visible** (the `xpEl` inventory is not touched — it just renders inside the overlay).
- **Both dark and light themes are first-class**, verified equally.
- **Build requires `-p:Platform=x64`** (AnyCPU breaks CsWin32). Stop the running EXE before building (it locks the DLL/EXE); restart after.
- **Scope is card expansion only.** Row expansion (`.rowxp`) is verify-don't-break (spec §9 #3). The full width×theme matrix is D3. The D1 header gutter is done — do not reopen it. The D2a flight-deck edit controls are a separate queued item — do not fold them in.
- **The DOM-less selftest must stay green** (`node webtests/selftest.node.js`, currently 227/227). It cannot see `cardEl`/CSS, so it is a regression guard here, not the D2 gate.

---

## File Structure

| File | Responsibility for D2 |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | Task 1: extract `cardRange(h)`; stop in-cell `.xp` append in `cardEl` (~:1120); add `placeCardOverlay(grid, cards)` and call it from `renderPinnedCards`/`renderPFD`; resize re-anchor listener. Task 2: single-open + one-shot-enter flag in `toggleExpand` (~:1396); `xpEnter` state field (~:816); capture-phase click-away listener (pattern at :1390). |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | Task 1: remove the in-flow `height:auto` growth (:270); add `.cell.expanded` accent + `.xp-overlay` block (after the `.xp-actions` rules ~:283 — must come *after* `.xp` so equal-specificity overrides win). Task 2: `.enter` animation + keyframes. |

No `index.html` change. No test-file change — see "Testing note".

## Testing note — why there is no new node unit test

Same constraint as D1: `cardEl`/`placeCardOverlay`/`toggleExpand` are boot-block DOM code and the layout lives in CSS; both node harnesses are DOM-less. A string assertion would be test-theater. The honest gate is the **live displacement + overlay measurement** below, run by the controller red→green and **paired with both-theme screenshots** (campaign lesson: a green measurement can still hide a visual regression).

---

### Task 1: Anchored overlay — render and position

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1066-1148` (`cardEl`, `renderPinnedCards`, `renderPFD`) and the global-listener region (~:1583, next to the existing pointer/keydown listeners)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css:268-271` and ~:283

**Interfaces:**
- Consumes: `xpEl(s, rr, opts)` (console.js:1021 — unchanged), `state.expanded` (`c:<id>` keys), `.pfd{position:relative}` (console.css:105), `$` helper.
- Produces: `function cardRange(h)` → `{lo, hi, source}` (used by `cardEl` and `placeCardOverlay`); `function placeCardOverlay(grid, cards)` — `grid` is the `.pfd` element, `cards` is the renderer's resolved card list (objects with `.s`, `.label`, `.bounded`); DOM class `.xp-overlay` (always together with `.xp`); reads optional `state.xpEnter` (set by Task 2; harmless `undefined` until then).

- [ ] **Step 1: JS — extract `cardRange` and stop the in-cell append.** In `cardEl` (console.js:1066), the range is currently computed inline (~:1072):

```js
      const rr = h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                           : SQ.rangeFor(h.s, {}, state.dashboard);
```

Add this function directly **above** `function cardEl(h, pinned)`:

```js
    function cardRange(h) {
      return h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                       : SQ.rangeFor(h.s, {}, state.dashboard);
    }
```

and change the line in `cardEl` to:

```js
      const rr = cardRange(h);
```

Then replace `cardEl`'s expansion tail (console.js:1120-1124):

```js
      if (state.expanded.has('c:' + h.s.id)) {
        cell.classList.add('expanded');
        cell.appendChild(xpEl(h.s, rr, { cls: 'xp', style: true, movable: true, fallbackLabel: h.label }));
      }
      return cell;
```

with:

```js
      if (state.expanded.has('c:' + h.s.id)) cell.classList.add('expanded');
      return cell;
```

- [ ] **Step 2: JS — add `placeCardOverlay` and call it from both grid renderers.** Add directly above `function renderPinnedCards(sensors, limits)` (console.js:1127):

```js
    function placeCardOverlay(grid, cards) {
      const old = grid.querySelector('.xp-overlay');
      if (old) old.remove();
      const h = cards.find(c => state.expanded.has('c:' + c.s.id));
      if (!h) return;
      const cell = grid.querySelector(`.cell[data-sid="${CSS.escape(h.s.id)}"]`);
      if (!cell) return;
      const ov = xpEl(h.s, cardRange(h), { cls: 'xp xp-overlay', style: true, movable: true, fallbackLabel: h.label });
      if (state.xpEnter === 'c:' + h.s.id) ov.classList.add('enter');
      cell.after(ov);
      ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
    }
```

In `renderPinnedCards`, after `cards.forEach(h => grid.appendChild(cardEl(h, true)));` add:

```js
      placeCardOverlay(grid, cards);
```

In `renderPFD`, after `H.forEach(h => pfd.appendChild(cardEl(h, false)));` add:

```js
      placeCardOverlay(pfd, H);
```

(`cell.offsetTop` is relative to `.pfd` because `.pfd` is the nearest positioned ancestor; inserting via `cell.after(ov)` keeps keyboard/tab order adjacent to the card. The `cls: 'xp xp-overlay'` keeps both the click-toggle guard `closest('… .xp, .rowxp')` at console.js:1456 and the inline-edit guard `isInlineEditTarget` at console.js:825 working unchanged.)

- [ ] **Step 3: JS — re-anchor on viewport resize.** Next to the existing document-level listeners (console.js:1583), add:

```js
    window.addEventListener('resize', () => {
      ['#pfd', '#pinned'].forEach(sel => {
        const grid = $(sel);
        const ov = grid && grid.querySelector('.xp-overlay');
        const cell = grid && grid.querySelector('.cell.expanded');
        if (ov && cell) ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
      });
    });
```

- [ ] **Step 4: CSS — remove in-flow growth, add the overlay + expanded affordance.** In `console.css`, the expansion block (:268-271) currently reads:

```css
/* card & row expansion - detail and actions live on the visible item */
.cell{cursor:pointer}
.cell.expanded,.cell.graph-on.expanded{height:auto}
.row{cursor:pointer}
```

Replace the `height:auto` line so the block becomes (expanded cards keep their exact size — a `graph-on` card must stay 172px, otherwise the card itself shifts on expand):

```css
/* card & row expansion - detail lives in a full-width overlay anchored to the visible item */
.cell{cursor:pointer}
.cell.expanded{border-color:color-mix(in srgb,var(--cy) 45%,var(--line))}
.row{cursor:pointer}
```

Then, **after** the last `.xp-actions` rule (`.xp-actions .alias-input{width:130px}`, ~:283), add:

```css
.xp-overlay{position:absolute;left:0;right:0;z-index:6;margin:0;padding:12px 15px 13px;
  border:1px solid var(--line);border-radius:13px;
  background:linear-gradient(180deg,var(--panel),var(--panel-2));box-shadow:var(--shadow)}
```

(This must sit after the `.xp{border-top…;margin-top:10px;padding-top:10px}` rule at :273 — same specificity, later wins; the `border` shorthand also replaces `.xp`'s `border-top`. `z-index:6` floats above cards, below the drag ghost at z 80.)

- [ ] **Step 5: Static gates.**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
git diff --check
```

Expected: `--check` clean; `SELFTEST PASS 227/227` (regression guard); no whitespace errors.

- [ ] **Step 6: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): render card expansion as a full-width overlay anchored below the card (D2)"
```

---

### Task 2: Interaction semantics — single-open, click-away, one-shot entrance

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:812-818` (state object), `:1396-1399` (`toggleExpand`), global-listener region (~:1583)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` (append after the `.xp-overlay` rule from Task 1)

**Interfaces:**
- Consumes: `placeCardOverlay`'s `.enter`-class hook (`state.xpEnter === 'c:' + id`, Task 1 Step 2), `rerender()` (console.js:848 — synchronous), `state.expanded`.
- Produces: `state.xpEnter` (string key or `null`); single-open invariant: at most one `c:` key in `state.expanded` at any time; capture-phase click-away close for card overlays.

- [ ] **Step 1: JS — state field.** In the state object (console.js:812-818), directly after `expanded: new Set(),` add:

```js
      xpEnter: null,
```

- [ ] **Step 2: JS — single-open + one-shot flag in `toggleExpand`.** Replace (console.js:1396-1399):

```js
    function toggleExpand(key) {
      if (state.expanded.has(key)) state.expanded.delete(key); else state.expanded.add(key);
      rerender();
    }
```

with:

```js
    function toggleExpand(key) {
      if (state.expanded.has(key)) state.expanded.delete(key);
      else {
        if (key.startsWith('c:')) {
          [...state.expanded].filter(k => k.startsWith('c:')).forEach(k => state.expanded.delete(k));
          state.xpEnter = key;
        }
        state.expanded.add(key);
      }
      rerender();
      state.xpEnter = null;
    }
```

(`rerender()` renders synchronously, so the flag is visible to both `placeCardOverlay` calls of exactly one render pass, then cleared — poll-tick renders never see it, which is what prevents the entrance animation from strobing at 1 Hz. Row keys `r:` are untouched: rows keep multi-open.)

- [ ] **Step 3: JS — capture-phase click-away close.** Directly after the existing menu click-outside listener (console.js:1390-1394 — keep that one unchanged), add:

```js
    // Capture phase, and deliberately blind to clicks on any card/row/control
    // surface: card-to-card switching belongs to toggleExpand's single-open
    // logic, and a bubble-phase rebuild must never make a control click read
    // as "outside" (same race as the sensors-popover lesson above).
    document.addEventListener('click', e => {
      const open = [...state.expanded].filter(k => k.startsWith('c:'));
      if (!open.length) return;
      if (e.target.closest('.cell, .xp-overlay, .row, .rowxp, .panel-head, button, input, select, a, code, label, details')) return;
      open.forEach(k => state.expanded.delete(k));
      rerender();
    }, true);
```

(Esc-close needs no work: the existing handler at console.js:1585 already clears `state.expanded` and rerenders.)

- [ ] **Step 4: CSS — one-shot entrance.** Append after the `.xp-overlay` rule added in Task 1:

```css
.xp-overlay.enter{animation:xpin .14s ease-out}
@keyframes xpin{from{opacity:0;transform:translateY(-4px)}to{opacity:1;transform:none}}
```

(`prefers-reduced-motion` is already covered by the global `animation:none!important` rule at console.css:250.)

- [ ] **Step 5: Static gates.**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
git diff --check
```

Expected: `--check` clean; `SELFTEST PASS 227/227`; no whitespace errors.

- [ ] **Step 6: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): single-open card overlay with click-away close and one-shot entrance (D2)"
```

---

## Controller-owned live gate (the real D2 verification — not a subagent task)

The implementer does static gates + commits only (no rebuild/browser; avoids app-lock and MCP-profile-lock cycles). The controller runs the live red→green measurement and the paired visual screenshots.

**Acceptance script** (chrome-devtools `evaluate_script`; expands a card, measures, collapses it again):

```js
(() => {
  const grid = document.querySelector('#pfd');
  const ids = [...grid.querySelectorAll('.cell')].map(c => c.dataset.sid);
  const snap = () => ids.map(id => {
    const c = grid.querySelector(`.cell[data-sid="${CSS.escape(id)}"]`);
    if (!c) return 'gone';
    const r = c.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height].map(v => Math.round(v)).join(',');
  });
  const before = snap();
  const target = grid.querySelector(`.cell[data-sid="${CSS.escape(ids[0])}"]`);
  target.click();                                   // expand (delegated toggle)
  const after = snap();
  const moved = before.filter((b, i) => b !== after[i]).length;
  const ov = grid.querySelector('.xp-overlay');
  const xpg = ov && ov.querySelector('.xp-grid');
  const cols = xpg ? getComputedStyle(xpg).gridTemplateColumns.split(' ').filter(w => parseFloat(w) > 0).length
                   : (() => { const g = grid.querySelector('.cell.expanded .xp-grid'); // RED path: inline xp
                       return g ? getComputedStyle(g).gridTemplateColumns.split(' ').filter(w => parseFloat(w) > 0).length : 0; })();
  const gr = grid.getBoundingClientRect(), or = ov ? ov.getBoundingClientRect() : null;
  const tr = grid.querySelector(`.cell[data-sid="${CSS.escape(ids[0])}"]`).getBoundingClientRect();
  const acts = (ov || grid).querySelector('.xp-actions');
  const res = {
    moved, cols, overlay: !!ov,
    spansGrid: !!or && Math.abs(or.width - gr.width) < 2,
    belowCard: !!or && or.top >= tr.bottom - 1,
    actionsClipped: !!acts && acts.scrollWidth > acts.clientWidth + 1,
    hscroll: document.documentElement.scrollWidth > window.innerWidth
  };
  grid.querySelector(`.cell[data-sid="${CSS.escape(ids[0])}"]`).click();   // collapse
  return res;
})();
```

- [ ] **G0 — RED baseline (before rebuild).** On the currently-running D1 build at 1440×900 (grid packed enough that cards sit near their 190px minimum — narrow the window if only a few heroes render), run the script. Expect **`moved > 0`** (cards below shift down), **`overlay: false`**, **`cols: 1`**. If it returns `moved: 0` here, the script or setup is broken — fix the gate before trusting any green. Record the numbers.
- [ ] **G1 — Rebuild.** Stop the app, then:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

Restart the app; confirm `GET /` serves the rebuilt asset (served JS contains `placeCardOverlay`); close stale pre-rebuild tabs (multi-tab version skew).
- [ ] **G2 — GREEN.** At 1440×900 run the script: expect **`moved: 0`, `overlay: true`, `spansGrid: true`, `belowCard: true`, `cols ≥ 2`, `actionsClipped: false`, `hscroll: false`**. Then exercise the semantics manually (chrome-devtools):
  - **Single-open:** expand card A, then click card B → A's overlay gone, B's present (one `.xp-overlay` per grid).
  - **Twin lockstep:** pin an expanded sensor → both `#pfd` and `#pinned` show an overlay anchored in their own grid.
  - **Close paths:** Esc closes; a click on empty page space (e.g. a section margin) closes; clicking the alias input, style select, and range inputs inside the overlay does **not** close it; clicking the card again toggles.
  - **No strobe:** leave a card expanded across ≥3 poll ticks — the overlay must not re-animate (watch it; the `.enter` class must be absent on tick renders).
  - **Resize re-anchor:** with an overlay open, resize 1440→900 wide — the overlay stays glued under its card.
  - **Rows unchanged:** expand a panel row → `.rowxp` renders in-flow as before, 2 columns at panel width.
  - **D1 non-regression:** re-run the D1 rect-intersection gate (in `2026-07-07-web-card-header-gutter-d1.md`) under `hover:none` — expect `violations: []`.
  - **Widths × themes + paired visual check:** repeat the script at **320, 390, 640, 1440, ≥1900** in **both dark and light**; at 320/390 `cols` may be 1–2 and the overlay floats over the next card — screenshot expanded state at wide and at 390 in both themes and judge that the overlay reads as an intentional layer (shadow/border visible, not a glitch). A green measurement alone does not close D2.
- [ ] **G3 — Console + contract clean.** Across several poll ticks with a card expanded: `list_console_messages` → zero errors. Then `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` → 42/42, golden untouched.

## Closeout (standard campaign procedure, after Task 2 review is clean)

- [ ] Spec `feature-web-dashboard-expansion-layout.md`: fill §10 implementation notes (anchored overlay shipped, twin-key kept, tradeoffs observed live), add the §11 verification row (exact commands, RED→GREEN numbers, themes/widths), flip Status → Implemented/Verified per outcome.
- [ ] Verification-log entry in `docs/feature-web-dashboard-card-truth.md` §11 (D2 row): closes the 2026-07-06 audit's "tall single-column strip" finding **and** the 2026-07-07 operator displacement complaint.
- [ ] Advance the queue: `2026-07-06-web-dashboard-v3-next-plan.md` §4 row **D2 ✅** + critical path → **D2a** (direct flight-deck edit controls) then D3; handoff §0/§10/§11 D2-done rows.
- [ ] Final whole-branch review (superpowers:requesting-code-review) on the branch range — model scaled to a two-commit presentational+interaction diff.
- [ ] Merge via superpowers:finishing-a-development-branch; post-merge selftest 227/227; leave rebuilt app running.

## Stop conditions (from the campaign)

Stop and review if: the default (non-expanded) view becomes less readable; the overlay misanchors (wrong card, wrong grid, drifts on ticks) in any tested configuration; the overlay floating over content below reads as broken in the paired visual check (fallback candidates: dim-scrim under the overlay, or revisit true modal — operator call, do not improvise); any drag/expand/keyboard-move/alias/range path breaks; row expansion regresses; the app cannot restart after build.
