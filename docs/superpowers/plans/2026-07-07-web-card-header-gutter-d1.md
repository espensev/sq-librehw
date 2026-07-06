# Web Dashboard D1 — Card Header Grid + Reserved Action Gutter

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Plan ID:** web-dashboard-d1-2026-07-07
**Goal:** Make the card header an owned two-row grid with a reserved trailing control gutter so the `.cell-ctl` cluster (grip/pin/hide) sits in-flow and never paints over the state chip or type icon — on hover, focus, or touch.

**Architecture:** Restructure `cardEl`'s header DOM into `.chead` (a `minmax(0,1fr) auto` grid): column 1 holds the two text rows (`.ktext` → `.k` name+chip, `.k2` src+icon); column 2 is the gutter holding `.cell-ctl` in-flow. The cluster stops being `position:absolute` and instead toggles `visibility` (reserving its natural width so the gutter self-sizes per card and never reflows on hover). Name/src/chip truncate inside column 1. Pure card change — rows (`.row-ctl`) and panels are untouched.

**Tech Stack:** Vanilla JS/CSS/HTML embedded resources in `LibreHardwareMonitor.Windows.Forms.exe`, served at `http://localhost:8085/`. No framework.

## Global Constraints (campaign non-negotiables — every task inherits these)

- **No `data.json` / server / contract change.** Client-only. Golden `dotnet test` must stay 42/42 with zero server diffs.
- **No host-specific labels, limits, or sensor IDs in product code.**
- **Read-only dashboard.** No `/Sensor?action=Set` or write UI.
- **Raw LibreHardwareMonitor labels + `SensorId` stay visible** wherever aliases are used (unchanged here — expansion already shows them).
- **Both dark and light themes are first-class**, verified equally.
- **Build requires `-p:Platform=x64`** (AnyCPU breaks CsWin32). Stop the running EXE before building (it locks the DLL/EXE); restart after.
- **Scope is cards only.** Rows' `.row-ctl` uses a deliberate gradient-scrim pattern with no chip/icon and is out of scope. The full width×theme matrix is D3. Readout/suffix width was A2 (done) — do not reopen it.
- **The DOM-less selftest must stay green** (`node webtests/selftest.node.js`, currently 227/227). It cannot see `cardEl`/CSS, so it is a regression guard here, not the D1 gate.

---

## File Structure

| File | Responsibility for D1 |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | `cardEl` (~:1099–1126): restructure header DOM; render `.cell-ctl` in-flow inside `.chead`; drop the `document.createElement`/`appendChild` cluster tail. |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | Add `.chead`/`.ktext`/truncation rules; split the shared `.cell-ctl,.row-ctl` rules so `.cell-ctl` becomes in-flow + `visibility`-toggled while `.row-ctl` is unchanged; keep a stable gutter by reserving the card grip's width. |

No `index.html` change (the card header is 100% runtime-generated). No test-file change — see "Testing note".

## Testing note — why there is no new node unit test

`cardEl` is boot-block DOM and its layout lives in `console.css`. **Both** `webtests/selftest.node.js` and `webtests/console.tests.js` are DOM-less (they exercise `SQ.*` pure functions and `index.html` structure strings). A "cardEl output string contains `chead`" assertion would assert nothing about the actual overlap and is exactly the test-theater the SDD review rubric flags. The **honest, objective gate is a live browser rect-intersection measurement** (below), run by the controller red→green. The node selftest stays a regression guard (must remain 227/227).

---

### Task 1: Card header reserved-gutter grid

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:1099-1126` (`cardEl`)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` (card anatomy ~:134-145 and inline-customize ~:253-259)

**Interfaces:**
- Consumes: existing `ctlCluster(id, label, {hide})`, `tIcon(kind)`, `esc`, `rangeMarkup`, `sparkAreaSVG`, `SQ.sensorDisplayText`, and the drag machinery in `startDrag` (`:1542` resolves the card via `grip.closest('.cell')`, so moving the grip inside `.chead` is safe — it stays a `.cell` descendant).
- Produces: DOM class `.chead` (header grid) with `.ktext` (text column) and in-flow `.cell-ctl` (gutter column). No new JS functions, no state-shape change.

- [ ] **Step 1: CSS — add the header grid + truncation.** In `console.css`, immediately after the `.cell .k2{...}` rule (`:138`), add:

```css
.cell .chead{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:8px;align-items:start}
.cell .ktext{min-width:0}
.cell .k .name{flex:1 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.cell .k2 .src{flex:1 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.cell .chip-state{flex:none;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
```

Rationale: column 1 (`minmax(0,1fr)`) holds the two text rows and truncates; column 2 (`auto`) is the gutter, self-sized to the cluster. `.name`/`.src` become the flexible truncating elements; `.chip-state` and `.ticon` (already `flex:none`, `:139`) keep their size and sit at the right of column 1, left of the gutter.

- [ ] **Step 2: CSS — make `.cell-ctl` in-flow with a stable, reserved gutter.** Replace the shared absolute rule (`console.css:253-254`):

```css
.cell-ctl,.row-ctl{position:absolute;display:none;gap:4px;z-index:2}
.cell-ctl{top:10px;right:11px}
```

with (row rule preserved verbatim; card cluster becomes in-flow, `visibility`-reserved; card grip forced into layout so the gutter width does not change on hover):

```css
.row-ctl{position:absolute;display:none;gap:4px;z-index:2}
.cell-ctl{display:flex;gap:4px;visibility:hidden;align-self:center}
.cell .cell-ctl .grip{display:inline-block}
```

- [ ] **Step 3: CSS — split the reveal rules so cards toggle `visibility`, rows keep `display`.** Replace the combined hover/focus rule (`console.css:257-258`):

```css
.cell:hover .cell-ctl,.cell:focus-within .cell-ctl,
.row:hover .row-ctl,.row:focus-within .row-ctl{display:flex}
```

with:

```css
.cell:hover .cell-ctl,.cell:focus-within .cell-ctl{visibility:visible}
.row:hover .row-ctl,.row:focus-within .row-ctl{display:flex}
```

Then replace the touch rule (`console.css:259`):

```css
@media (hover:none){.cell-ctl,.row-ctl{display:flex}}
```

with:

```css
@media (hover:none){.cell-ctl{visibility:visible}.row-ctl{display:flex}}
```

(The card grip's own `display:none`→`inline-block` hover rules at `:284-286` now no-op for cards because `.cell .cell-ctl .grip` forces `inline-block`; they still govern panel/row grips. Leave them unchanged.)

- [ ] **Step 4: JS — restructure `cardEl`'s header DOM.** In `console.js`, replace the `cell.innerHTML = …` assignment and the `document.createElement('div')` cluster tail (`:1108-1120`) with:

```js
      const showHide = !pinned;
      const ctlHtml = `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        ctlCluster(h.s.id, label, { hide: showHide });
      cell.innerHTML =
        `<div class="chead"><div class="ktext">
           <div class="k"><span class="name">${esc(label)}</span>${chip}</div>
           <div class="k2"><span class="src">${esc(source)}</span>${tIcon(kind)}</div>
         </div><div class="cell-ctl">${ctlHtml}</div></div>
         <div class="body">${arc}<div class="readout">
           <div class="big"><span class="v">${esc(n)}</span><span class="u">${esc(u)}</span>${ceil}${ctrl ? `<span class="vcmd" title="commanded ${esc(ctrl.value)}">· ${esc(ctrl.value)}</span>` : ''}</div>
           <div class="meta">${rangeMarkup(h.s) || '<div class="range"></div>'}${trendHtml}</div>
         </div></div>${fx.spark ? sparkAreaSVG(h.s, range) : ''}`;
```

This hoists `showHide`/`ctlHtml` above `innerHTML`, inlines the cluster into `.chead`'s gutter column (the HTML from `ctlCluster`/the grip is already `esc`-escaped, same as `chip`/`tIcon`), and removes the now-dead `const ctl = document.createElement('div'); … cell.appendChild(ctl);` block. The expansion append (`:1121-1124`) is unchanged.

- [ ] **Step 5: Static gates.**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
```
Expected: `--check` clean; selftest `SELFTEST PASS 227/227` (regression guard — no new assertions here).

- [ ] **Step 6: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "fix(web): give card header a reserved control gutter so controls never overlap chip/icon (D1)"
```

---

## Controller-owned live gate (the real D1 verification — not a subagent task)

The implementer does static gates + commit only (no rebuild/browser; avoids app-lock and MCP-profile-lock cycles). The controller runs the live red→green measurement, because the gate is the sole objective evidence and must be validated against the known-bad state first.

**Acceptance script** (chrome-devtools `evaluate_script`; returns `violations: []` on pass). Run it under **touch emulation** (`emulate` `hover:none` so `.cell-ctl` is visibly reserved-and-shown) and again after focusing a chip-bearing cell:

```js
(() => {
  const rect = el => el.getBoundingClientRect();
  const hit = (a,b) => a && b && !(a.right<=b.left||a.left>=b.right||a.bottom<=b.top||a.top>=b.bottom);
  const bad = []; let checked = 0, withChip = 0;
  document.querySelectorAll('.cell').forEach(cell => {
    const ctl = cell.querySelector('.cell-ctl'); if (!ctl) return;
    const cr = rect(ctl); if (cr.width === 0 || cr.height === 0) return; // cluster not shown
    checked++;
    const chip = cell.querySelector('.chip-state');
    const icon = cell.querySelector('.ticon');
    if (chip) { withChip++; if (hit(cr, rect(chip))) bad.push({id: cell.dataset.sid, what: 'chip'}); }
    if (icon && hit(cr, rect(icon))) bad.push({id: cell.dataset.sid, what: 'icon'});
  });
  return { checked, withChip, violations: bad };
})();
```

- [ ] **G0 — RED baseline (before rebuild).** On the currently-running C1 build (pre-fix), emulate `hover:none`, run the script. **Expect `withChip > 0` and `violations.length > 0`** (the absolute cluster overlaps chip/icon today). If it returns `violations: []` here, the script or selectors are broken — fix the gate before trusting any green. Record the number.
- [ ] **G1 — Rebuild.** Stop the app, then:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```
Restart the app; confirm `GET /` serves the rebuilt asset (served HTML/JS contains `class="chead"`), close stale pre-rebuild tabs (multi-tab version skew).

- [ ] **G2 — GREEN.** Re-run the script under `hover:none`: expect **`checked > 0`, `withChip > 0`, `violations: []`**. Then in normal (hover) mode, focus a chip-bearing cell (`document.querySelector('.cell:has(.chip-state)').focus()`), re-run: expect `violations: []`. Repeat in **both dark and light** themes and at **320px, 390px, and wide** widths (`resize_page`). Confirm the cluster is fully inside the card (no clip) and the gutter does not visibly reflow between resting and hover (screenshot both states).
- [ ] **G3 — Console clean.** Across several poll ticks with a card expanded, `list_console_messages` shows zero errors (catches any dangling ref the selftest can't).

## Closeout (standard campaign procedure, after Task 1 review is clean)

- [ ] Verification-log entry in `docs/feature-web-dashboard-card-truth.md` §11 (D1 row): exact commands, `checked`/`withChip`/RED-count→GREEN, themes/widths, closes audit finding #9 (line 61).
- [ ] Advance the queue: `2026-07-06-web-dashboard-v3-next-plan.md` §4 row **D1 ✅** + critical path → D2; handoff §0/§11 D1-done row.
- [ ] Final whole-branch review (superpowers:requesting-code-review) on the branch range — model scaled to a small presentational diff.
- [ ] Merge via superpowers:finishing-a-development-branch; post-merge selftest 227/227; leave rebuilt app running.

## Stop conditions (from the campaign)

Stop and review if: a fix makes the default view less readable/more cluttered; the gutter forces name truncation so severe the card is unreadable at the 190px floor (if so, reconsider reserving the grip vs. accepting a hover reflow, or a narrower gutter); any drag/expand/keyboard-move path breaks; the app cannot restart after build.
