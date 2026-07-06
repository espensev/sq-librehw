# B3 — Customize Drawer Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the Customize side drawer from the SQ Telemetry Console after proving every drawer workflow has an inline/popover replacement — adding the one missing piece (inline keyboard panel reorder) first.

**Architecture:** Vanilla JS/CSS/HTML dashboard (`Resources/Web/`). B1 (Sensors popover) and B2 (primary-card selection) already moved hidden-discovery, pin/hide, alias, style, override, and card/row/pinned reorder onto visible surfaces. This plan closes the last parity gap (panel keyboard reorder), then deletes the drawer DOM/JS/CSS.

**Tech Stack:** Embedded web resources served by `LibreHardwareMonitor.Windows.Forms`. Node self-test harness (`webtests/selftest.node.js`, pure `SQ.*` helpers, no DOM). Live verification via browser at `http://localhost:8085/`.

## Global Constraints (verbatim from v3 non-negotiables)

- No `data.json` / server / contract change. State stays browser-local under `sq.dashboard.v1`.
- C# golden tests (`DataJsonGoldenTests`, 42) must stay green; web self-test must stay green (currently 192/192).
- Vanilla JS/CSS/HTML only — no framework.
- No host-specific labels, limits, or sensor IDs in product code.
- Raw LibreHardwareMonitor label + `SensorId` remain visible wherever an alias shows.
- Build requires `-p:Platform=x64`. Running EXE locks the DLL/EXE — stop the app before rebuild.
- **Light (white) theme is a first-class gate:** every new/changed control is styled and live-verified in **both** dark and light. (User: "don't ignore the white/light either since it looks stylish.")
- Multi-tab-safe save: user edits via `commitDashboard`; do not add fields telemetry saves would clobber (no new persisted fields here).
- **Stop condition (from v3 plan §10):** drawer deletion must not remove the only keyboard path for any action. Panel reorder is the one such path — it is added in Task 1 *before* Task 2 deletes anything.

---

## Parity Re-assessment (supersedes the plan's stale gate)

The v3-next-plan §4 B3 row and §11 claim pinned-card **and** panel keyboard reorder are drawer-only. That is **outdated**. Verified in code:

| Drawer capability | Inline replacement | Status |
|---|---|---|
| Hidden tab: hide/show/search/**reset-hidden** | Sensors popover (B1) — `#sensorsList` click handler (console.js:1377) + dedicated `reset-hidden` handler (console.js:1389), both drawer-independent | ✅ parity |
| Pin / unpin any sensor | Sensors popover + card/row expansion | ✅ |
| Card style select | Card expansion `.style-select` (`xpEl`, console.js:986) | ✅ |
| **Pinned-card reorder** (`pin-up`/`pin-down`) | **Already inline:** expanded pinned card's `move-left`/`move-right` routes to `pinnedOrder` (handler `container.id === 'pinned'`, console.js:1493-1496) | ✅ |
| Unpin | Card expansion `Unpin` / popover | ✅ |
| Pinned-card `title` rename | **Alias** field in card expansion — renders on the card (`cardEl` → `sensorDisplayText` applies alias over the `card.title \|\| s.text` fallback, console.js:1063 / 371) | ⚠️ subsumed by alias (see note) |
| **Panel reorder** (`panel-up`/`panel-down`) | Panels have a drag grip only — **no keyboard path** | ❌ **true gap → Task 1** |
| `clear-pinned`, `reset-panels` (bulk) | unpin-each / move-each; `reset-panels` re-added as a Subsystems header button (Task 1) | ⚠️ convenience |

**Pinned-title note:** `title` was a pinned-card-local rename. Alias is the inline analog and already renders on the card, so removing the drawer's rename UI keeps the *capability* (rename a card inline) — this is parity via alias, not a silent loss. The one lost nuance: alias renames the sensor **everywhere** (card + row + popover), whereas `title` was pinned-card-only. Marginal for a personal tool. `title` **rendering** is kept for back-compat so any existing pinned titles still display; only the editor UI goes. Verify live that an alias set on a pinned card renders (guards against a B2-style silent relabel).

**Deletion reference map (verified complete — zero external references):**
- `renderCustomize` — def console.js:1299; **call at console.js:911 (must be removed — else ReferenceError every poll tick; the node self-test cannot catch this, it exercises `SQ.*` not `render()`)**; wiring at 1369/1371/1372/1373/1374/1405.
- `renderPinnedEditor` (1232), `renderLayoutEditor` (1256), `renderSensorRows` (1211) — called **only** inside `renderCustomize`.
- `renamePinned` (876) — called **only** by the drawer `change` handler (1408).
- state fields `customizeOpen`/`customizeTab`/`hiddenFilter`/`cardFilter` (777-780) — used **only** in the drawer surface.
- Shared mutators the drawer handler calls (`setSensorHidden`, `pinSensor`, `unpinSensor`, `moveKey`, `mergeOrder`) are used elsewhere — **keep them**; delete only the drawer *handlers*.

---

## File Structure

- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — add `movePanel`/`resetPanelOrder` + panel-head ▲▼ + reset wiring (Task 1); delete drawer functions/handlers/state (Task 2).
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` — add `#panelsReset` to Subsystems header (Task 1); remove `#customize`, `#customizeScrim`, `#customizeDrawer` (Task 2).
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` — style panel ▲▼ + `#panelsReset` (Task 1); remove drawer-only rules, split six shared rules (Task 2).
- `webtests/console.tests.js` — no new pure logic to unit-test (panel reorder reuses the already-exercised `moveKey`/`mergeOrder`/`applyOrder`; the risk is DOM/keyboard/theme, which the DOM-less harness cannot test). Self-test stays a **regression guard** at 192/192. The real gate is live browser verification (consistent with B1/B2 DOM-glue).

---

### Task 1: Inline panel keyboard reorder + Reset order

Adds the one missing keyboard path **while the drawer is still present**, so there is no intermediate capability loss and no deletion risk yet. Verified live (keyboard + both themes), not via the DOM-less harness.

**Files:**
- Modify: `console.js` — new `movePanel`/`resetPanelOrder`; `panelEl` head buttons; `renderPanels` reset visibility; `#panelsReset` wiring.
- Modify: `index.html` — Subsystems `.sec-head` gains `#panelsReset`.
- Modify: `console.css` — `.panel-move` buttons (both themes); `#panelsReset`.

**Interfaces:**
- Consumes: existing `moveKey(list,key,delta)` (console.js:824), `mergeOrder(saved,keys)` (818), `state.panelItems`, `state.dashboard.panelOrder`, `commitDashboard()`.
- Produces: `movePanel(key, delta)`, `resetPanelOrder()` (local functions).

- [ ] **Step 1: Add `movePanel` + `resetPanelOrder` (mirror the drawer logic being retired).**

Insert after `moveRow` (console.js:~849):

```javascript
    function movePanel(key, delta) {
      const keys = state.panelItems.map(i => i.key);
      state.dashboard.panelOrder = moveKey(mergeOrder(state.dashboard.panelOrder, keys), key, delta);
      commitDashboard();
    }
    function resetPanelOrder() {
      state.dashboard.panelOrder = [];
      commitDashboard();
    }
```

- [ ] **Step 2: Add always-visible ▲▼ to the panel head with `stopPropagation`.**

In `panelEl` (console.js:1160-1170), the head is a clickable `<div>` (`h.onclick` toggles collapse). Add a move-button pair and attach **direct** `onclick` handlers that `stopPropagation` (so collapse does not toggle — matches the local `morebtn` pattern at 1189; a delegated handler cannot prevent the deeper `h.onclick`). Buttons are always rendered (real, focusable) because the head is not tabbable, so a hover/focus-within reveal would be keyboard-unreachable.

Replace the head `innerHTML` assignment + `h.onclick` block so the head starts with a reorder cluster:

```javascript
      const h = document.createElement('div'); h.className = 'panel-head';
      h.innerHTML = `<span class="panel-move"><button class="ctl" data-mv="up" aria-label="Move ${esc(label)} up" title="Move up">&#9650;</button><button class="ctl" data-mv="down" aria-label="Move ${esc(label)} down" title="Move down">&#9660;</button></span>` +
        `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        `<span class="lamp s-${worst}"></span><span class="nm">${esc(label)}</span>` +
        `<span class="cls">${CLASSLABEL[cls] || ''}</span>` +
        `<span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
      h.querySelectorAll('.panel-move .ctl').forEach(b => b.onclick = e => {
        e.stopPropagation();
        movePanel(item.key, b.dataset.mv === 'up' ? -1 : 1);
      });
      h.onclick = () => {
```

(The `h.onclick` collapse body below is unchanged.)

- [ ] **Step 3: Add `#panelsReset` to the Subsystems header (mirror `#pfdReset`).**

In `index.html` (~line 54), change the Subsystems `.sec-head` to include a conditionally-shown reset button:

```html
    <div class="sec-head"><h2>Subsystems</h2><div class="rule"></div><span class="tag" id="subtag"></span><button class="iconbtn" id="panelsReset" style="display:none" title="Return panels to default order">Reset order</button></div>
```

- [ ] **Step 4: Toggle `#panelsReset` visibility in `renderPanels`; wire its click.**

In `renderPanels` (console.js:1196), after setting `#subtag`, show the reset button only when the user has a custom panel order:

```javascript
      $('#subtag').textContent = `${ordered.length} components`;
      const preset = $('#panelsReset');
      if (preset) preset.style.display = state.dashboard.panelOrder.length ? '' : 'none';
```

Add wiring next to the `#pfdReset` wiring (console.js:1370):

```javascript
    $('#panelsReset').onclick = resetPanelOrder;
```

- [ ] **Step 5: Style panel ▲▼ + `#panelsReset` (both themes).**

Append to the "inline customize controls" area of `console.css` (after line ~278) — reuse the existing `.ctl` look (muted, cyan-on-hover, theme-driven via CSS vars, so both themes inherit correctly):

```css
.panel-move{display:inline-flex;gap:3px;margin-right:2px}
.panel-move .ctl{padding:2px 4px;font-size:10px}
```

Extend the B2 reset rule (console.css:333):

```css
#pfdReset,#panelsReset{margin-left:8px}
```

- [ ] **Step 6: Rebuild EXE, verify live in BOTH themes.**

Stop the running app, then:
`dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
Relaunch. At `http://localhost:8085/`:
- ▲▼ visible on each panel head, both themes; not clipped; reads as intentional.
- Click ▲ / ▼ → panel order changes; **collapse does NOT toggle**.
- Keyboard: Tab reaches a panel's ▲▼; Enter/Space reorders.
- After a reorder, `#panelsReset` ("Reset order") appears in the Subsystems header; click → order returns to default, button hides.
- Reorder persists across reload.
- `node webtests/selftest.node.js` → 192/192 (regression guard).

- [ ] **Step 7: Commit.**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): inline keyboard panel reorder + reset order (B3 parity gate)"
```

---

### Task 2: Remove the Customize drawer (JS + HTML + CSS)

With panel reorder now inline, every drawer workflow has a visible replacement. Delete the drawer atomically (JS wiring + DOM together to avoid a broken intermediate), then CSS. Gate on grep-completeness + `node --check` + a live console-clean load in both themes + the regression suites — **not** the self-test alone.

**Files:**
- Modify: `console.js` — remove call/defs/handlers/state listed in the reference map.
- Modify: `index.html` — remove `#customize`, `#customizeScrim`, `#customizeDrawer`.
- Modify: `console.css` — remove drawer-only rules; split six shared rules.

- [ ] **Step 1: Remove the tick call site (the ReferenceError trap).**

Delete `renderCustomize();` at console.js:911.

- [ ] **Step 2: Remove drawer function definitions.**

Delete: `renderSensorRows` (1211-1231), `renderPinnedEditor` (1232-1255), `renderLayoutEditor` (1256-1266), `renderCustomize` (1299-1319), `renamePinned` (876-880). Keep `renderSensorsPopover` (1267-1298) and all shared mutators.

- [ ] **Step 3: Remove drawer wiring.**

Delete these lines: `$('#customize').onclick` (1369), `$('#drawerClose').onclick` (1371), `$('#customizeScrim').onclick` (1372), `$('#hiddenSearch').oninput` (1373), `$('#cardSearch').oninput` (1374), the `[data-tab]` loop (1405), the `$('#customizeDrawer').addEventListener('change'…)` block (1406-1416), and the `$('#customizeDrawer').addEventListener('click'…)` block (1417-1454). Keep the popover handlers (1375-1393), the Escape/click-outside menu handlers (1395-1404), and the `#pfd/#pinned/#panels` delegated host handlers (1506+).

- [ ] **Step 4: Remove drawer state fields.**

In the `state` object (console.js:777-780) delete `customizeOpen`, `customizeTab`, `hiddenFilter`, `cardFilter` (fix the trailing comma so the object stays valid — the preceding field is `inlineEditingUntil: 0`).

- [ ] **Step 5: Remove drawer DOM from `index.html`.**

Delete the `#customize` button (line 39), the `#customizeScrim` div (66), and the entire `#customizeDrawer` `<aside>` (67-98).

- [ ] **Step 6: Remove drawer-only CSS + split shared rules.**

In `console.css`:
- Delete the `/* customization drawer */` block that is drawer-only: `.scrim`, `.scrim.open`, `.drawer`, `.drawer.open`, `.drawer-head`, `.drawer-head b`, `.drawer-head span`, `.tabs`, `.pane`, `.drawer-tools`, `.drawer-tools input`, and the `.drawer-tools input,.title-input` rule (199-217, minus the shared pieces below).
- **Split** (keep the shared selector, drop the drawer one):
  - `.tab,.iconbtn{…}` → `.iconbtn{…}`
  - `.tab.active,.tab:hover,.iconbtn:hover{…}` → `.iconbtn:hover{…}`
  - `.sensor-list,.order-list{…}` → `.sensor-list{…}`
  - `.sensor-choice,.order-row{…}` → `.sensor-choice{…}`
  - `.sensor-choice b,.order-row b{…}` → `.sensor-choice b{…}`
  - `.sensor-choice span,.order-row span{…}` → `.sensor-choice span{…}`
- Delete drawer-only: `.order-row{grid-template-columns…}` (230), `.order-row .iconbtn` (231), and in the `@media (max-width:640px)` block the `.order-row` (261) and `.order-row .title-input` (262) rules.
- **Keep** (used by popover / card expansion): `.iconbtn`, `.iconbtn:disabled`, `.sensor-list`, `.sensor-choice` (+ `.is-hidden`, `b`, `span`, `code`), `.style-select`, `.mini-badge`, `.empty-note`.

- [ ] **Step 7: Static verification — syntax + zero residual references.**

- `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` → OK (catches syntax breakage; does **not** catch dangling refs).
- Grep the source for every removed symbol; expect **no** matches:
  `renderCustomize|renderPinnedEditor|renderLayoutEditor|renderSensorRows|renamePinned|customizeOpen|customizeTab|hiddenFilter|cardFilter|customizeDrawer|customizeScrim|drawerClose|#customize\b|data-tab|panelOrderList|#pinnedList|#cardList|#hiddenList` across `console.js` + `index.html`.
- Grep `console.css` for `.drawer|\.scrim|\.tabs|\.tab\b|\.pane\b|order-row|order-list|title-input|drawer-tools` → no matches.
- `node webtests/selftest.node.js` → 192/192.

- [ ] **Step 8: Rebuild + live verification in BOTH themes (the real gate).**

Stop app, rebuild (x64), relaunch. At `http://localhost:8085/`, in **dark and light**:
- **Console is clean across ≥3 poll ticks** (no `renderCustomize is not defined`, no null-element errors). This is the gate the self-test cannot provide.
- No `Customize` button; no drawer; no scrim.
- Sensors popover: search, Hide, Show, Pin, Unpin, **Reset hidden** all work.
- Card expansion: alias set/clear, style, max override set/clear, pin/unpin, show-as/remove-primary, hide, move ▲▼ all work.
- Row expansion: alias/override/pin/hide/move all work.
- Reorder on all four surfaces: PFD cards, pinned cards, panels (Task 1), rows.
- **Alias set on a pinned card renders on the card** (pinned-title parity via alias).
- Popover list still styled correctly (shared `.sensor-choice`/`.sensor-list`/`.iconbtn` survived the CSS split).
- `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -c Release -p:Platform=x64` → 42/42.

- [ ] **Step 9: Commit.**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): remove customize drawer after inline+popover parity (B3)"
```

---

## Completion

After both tasks: use superpowers:finishing-a-development-branch (verify tests → present merge options). Then update `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md` §4 (mark B3 ✅ with commit ref + this plan) and §11, and the continuation handoff, as the X1-style closeout.

## Execution notes

**Branch:** `feat/web-drawer-removal-b3` (off `master` @ `2698d0f`). Plan `2781fb4`.

**Task 1 — panel reorder + reset order** (`69252b4`): `movePanel`/`resetPanelOrder` added; always-visible ▲▼ in `panel-head` (real focusable buttons, direct `onclick` + `stopPropagation`); `#panelsReset` in Subsystems header (shown only when `panelOrder` non-empty); `.panel-move` CSS reusing the `.ctl` look. Live-verified (chrome-devtools): 13 panels × 2 = 26 keyboard-focusable buttons; ▲ reorder swaps order **and collapse did not toggle** (`collapseUnchanged:true`); persists to `sq.dashboard.v1`; reset restores default + re-hides; console clean; **both themes** stylish (screenshots `b3-task1-panels-{dark,light}.png`). selftest 192/192.

**Task 2 — drawer removal** (`f60fcda`): removed the `renderCustomize()` tick call (the ReferenceError trap), `renderCustomize`/`renderPinnedEditor`/`renderLayoutEditor`/`renderSensorRows`/`renamePinned` + the now-dead `sensorSearchText`/`sensorButtonLabel` locals, all drawer wiring + both `#customizeDrawer` listeners, and the `customizeOpen`/`customizeTab`/`hiddenFilter`/`cardFilter` state fields; removed `#customize`/`#customizeScrim`/`#customizeDrawer` DOM; deleted drawer-only CSS and **split six shared rules** (`.iconbtn`, `.sensor-list`, `.sensor-choice` + `b`/`span`, keeping the popover/expansion selectors). Verified: `node --check` OK; **zero residual references** (JS/HTML/CSS greps empty); selftest 192/192; **live console clean across many ticks + interactions** (the gate the DOM-less harness can't provide); Sensors popover hide/show/pin/**reset-hidden** all work (200 rows); **pinned-card alias renders as the card label** (`ZZ_PINNED_ALIAS` → confirms pinned-title parity via alias); both themes clean with no Customize button (screenshots `b3-task2-top-{dark,light}.png`); C# golden 42/42.

**Corrections to the plan's stated gate (verified in code, not assumed):** pinned-card keyboard reorder was **already** inline (expanded pinned card's `move-left`/`move-right` → `pinnedOrder` at the `container.id === 'pinned'` branch), so only **panel** reorder was a real keyboard gap. The Sensors popover's `reset-hidden` has its own dedicated handler independent of the drawer. Pinned-card `title` rename was subsumed by alias (kept `title` rendering for back-compat; alias is global vs the old pinned-local scope — a marginal, explicitly-noted trade-off).

**Live-testing gotchas hit:** (1) the chrome-devtools MCP browser lost its profile lock twice mid-session; recovery = kill `*chrome-devtools-mcp*` chrome procs + reopen. (2) `<details>` `toggle` fires async, so a synchronous eval reads the popover list empty — re-inspect in a later call. (3) PFD and pinned cards for the same sensor share the expand key `c:<id>`, so toggling one toggles both — account for it when driving expansion in tests.
