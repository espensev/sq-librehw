# Sensors Popover (Phase B1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a compact masthead "Sensors N" popover that lets the operator search all sensors and show/hide/pin them (and reset hidden), so hidden/offscreen discovery no longer depends on the Customize drawer.

**Architecture:** Pure model helpers (already partly landed) produce the popover's rows; a native `<details>` disclosure in the masthead (mirroring the existing Pages menu) hosts a search box + a rendered list; a small delegated click handler reuses the drawer's existing `setSensorHidden` / `pinSensor` / `unpinSensor` / reset-hidden logic. No server or `data.json` change; state stays `sq.dashboard.v1`.

**Tech Stack:** Vanilla HTML/CSS/JS (no framework). Model tests via `node webtests/selftest.node.js`. WinForms embeds these as resources; live verify at `http://localhost:8085/` after a Release/x64 rebuild.

## Global Constraints

- No framework; keep it vanilla JS/CSS (v3 non-negotiable).
- No `data.json` / server change; golden tests stay green. State stays browser-local under `sq.dashboard.v1`.
- Raw LibreHardwareMonitor label and `SensorId` remain visible wherever an alias is shown.
- The popover is for hidden/offscreen discovery + show/hide/pin/reset only — it is **not** a side pane and must not carry full card detail/actions.
- The Customize drawer stays in place until B1 parity is verified; **drawer removal is B3, a separate later slice**, not part of this plan.
- Build requires `-p:Platform=x64`. Verify commands: `node --check`, `node webtests/selftest.node.js`, then rebuild + live browser check.
- Files touched are `Resources/Web/{console.js,console.css,index.html}` + `webtests/console.tests.js` only.

## Current State (2026-07-06)

- Branch `feat/web-sensors-popover`, 1 commit ahead of `master`: `59cc506` — foundation model helpers already landed **with tests (164/164)**:
  - `SQ.sensorSearchText(s, state)` → lowercased search text.
  - `SQ.sensorVisibility(s, state)` → `'visible' | 'hidden' | 'offscreen'`.
  - `SQ.hiddenSensorCount(sensors, state)` → count of not-currently-shown sensors.
- The live app (PID from the A1/A2 relaunch) runs `master` (A1+A2). It does **not** yet serve this branch's changes until a rebuild.

### Branch & merge strategy

Complete B1 (Tasks 1–6) on `feat/web-sensors-popover`, then merge the whole popover to `master` as **one reviewable unit** (helpers + UI together). Do **not** merge the helpers-only foundation to master on its own — it would land code with no consumer. (If master must stay current for another reason, the foundation commit is additive/tested and safe to merge early, but the default is merge-once-B1-is-verified.)

## File Structure

| File | Responsibility in B1 |
|---|---|
| `webtests/console.tests.js` | Add tests for `SQ.sensorPopoverRows` (Task 1). Existing helper tests stay. |
| `Resources/Web/console.js` | Add `SQ.sensorPopoverRows` (Task 1); add `renderSensorsPopover()` + count wiring (Task 3); wire search/actions/close (Task 4). |
| `Resources/Web/index.html` | Add the masthead `<details class="sensors-menu">` button + panel (Task 2). |
| `Resources/Web/console.css` | Add `.sensors-menu` / `.sensors-panel` / `.vis-chip` / count styles (Task 5). |

---

### Task 1: `SQ.sensorPopoverRows` model helper

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add after `SQ.hiddenSensorCount`, ~line 277)
- Test: `webtests/console.tests.js` (add after the existing popover-helper tests, ~line 82)

**Interfaces:**
- Consumes: `SQ.sensorSearchText(s, state)`, `SQ.sensorVisibility(s, state)`, `SQ.sensorDisplayText(s, state, fallback)` (all exist).
- Produces: `SQ.sensorPopoverRows(sensors, state, query)` → `Array<{id, label, rawLabel, hw, type, value, visibility}>`, filtered by `query` (empty = all), stable-sorted with `hidden` then `offscreen` then `visible`, capped at 200.

- [ ] **Step 1: Write the failing tests**

In `webtests/console.tests.js`, immediately after the line `eq('hiddenSensorCount ignores plainly-visible-only list', S.hiddenSensorCount([byId('/amdcpu/0/temperature/2')], popState), 0);` add:

```javascript
    const popRows = S.sensorPopoverRows(sensors, popState, '');
    eq('sensorPopoverRows returns rows', popRows.length > 0, true);
    eq('sensorPopoverRows row shape', Object.keys(popRows[0]).sort(),
      ['hw','id','label','rawLabel','type','value','visibility']);
    eq('sensorPopoverRows hidden sorted before visible',
      popRows.findIndex(r => r.visibility === 'hidden') < popRows.findIndex(r => r.visibility === 'visible'), true);
    const loadRows = S.sensorPopoverRows(sensors, popState, 'load');
    eq('sensorPopoverRows query filters to matches',
      loadRows.length > 0 && loadRows.every(r => r.id.includes('load') || r.type.toLowerCase().includes('load')), true);
    eq('sensorPopoverRows keeps raw label under alias',
      S.sensorPopoverRows([byId('/amdcpu/0/temperature/2')], popState, '')[0].rawLabel, byId('/amdcpu/0/temperature/2').text);
    eq('sensorPopoverRows non-array safe', S.sensorPopoverRows(null, popState, ''), []);
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node webtests/selftest.node.js`
Expected: crashes/FAILs with `TypeError: S.sensorPopoverRows is not a function`.

- [ ] **Step 3: Implement the helper**

In `console.js`, after the `SQ.hiddenSensorCount` function (the closing `};` at ~line 277), add:

```javascript
  SQ.sensorPopoverRows = function (sensors, state, query) {
    if (!Array.isArray(sensors)) return [];
    const q = (query || '').trim().toLowerCase();
    const rank = { hidden: 0, offscreen: 1, visible: 2 };
    return sensors
      .filter(s => s && s.id && (!q || SQ.sensorSearchText(s, state).includes(q)))
      .map((s, i) => ({ s, i, vis: SQ.sensorVisibility(s, state) }))
      .sort((a, b) => (rank[a.vis] - rank[b.vis]) || (a.i - b.i))
      .slice(0, 200)
      .map(({ s, vis }) => ({
        id: s.id,
        label: SQ.sensorDisplayText(s, state, s.text),
        rawLabel: s.text || '',
        hw: s.hw || '',
        type: s.type || '',
        value: s.value != null ? s.value : '—',
        visibility: vis
      }));
  };
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js && node webtests/selftest.node.js`
Expected: `console.js` parses; `SELFTEST PASS 170/170` (164 prior + 6 new).

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): add sensorPopoverRows model helper (B1)"
```

---

### Task 2: Masthead Sensors button + popover DOM

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` (insert between the Pages `</details>` at line 28 and the Customize button at line 29)

**Interfaces:**
- Produces: DOM ids `#sensorsMenu` (the `<details>`), `#sensorsCount` (count badge span), `#sensorsSearch` (search input), `#sensorsList` (row container), and a `[data-action="reset-hidden"]` button — all consumed by Tasks 3–4.

- [ ] **Step 1: Add the button + panel**

In `index.html`, replace the line `    <button class="btn" id="customize">Customize</button>` with:

```html
    <details class="sensors-menu" id="sensorsMenu">
      <summary class="btn" id="sensorsSummary">Sensors <span class="count" id="sensorsCount"></span></summary>
      <div class="sensors-panel" aria-label="Sensor visibility">
        <div class="sensors-tools">
          <input id="sensorsSearch" type="search" placeholder="Search all sensors" autocomplete="off" aria-label="Search all sensors">
          <button class="iconbtn" data-action="reset-hidden">Reset hidden</button>
        </div>
        <div class="sensor-list" id="sensorsList"></div>
      </div>
    </details>
    <button class="btn" id="customize">Customize</button>
```

(The Customize button stays — it is removed only in B3, after parity.)

- [ ] **Step 2: Verify the page still loads**

Rebuild is deferred to Task 6; for now confirm the HTML is well-formed by eye and that no id collides (`grep -n 'id="sensors' index.html` shows the four new ids once each).

Run: `grep -c 'id="sensors' LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`
Expected: `4`

- [ ] **Step 3: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html
git commit -m "feat(web): add masthead Sensors popover shell (B1)"
```

---

### Task 3: `renderSensorsPopover()` + count wiring

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add the function near `renderCustomize`, ~line 1179; call it from the render loop next to `renderCustomize()`, line 831)

**Interfaces:**
- Consumes: `SQ.hiddenSensorCount`, `SQ.sensorPopoverRows` (Task 1), `state.allSensors`, `state.dashboard`, `state.sensorsFilter`, `$`, `esc`.
- Produces: `renderSensorsPopover()` (no args); reads `state.sensorsFilter` (a string, defaulting to `''`).

- [ ] **Step 1: Add the render function**

In `console.js`, immediately before `function renderCustomize() {` (line 1179), add:

```javascript
    function renderSensorsPopover() {
      const countEl = $('#sensorsCount');
      if (countEl) {
        const n = SQ.hiddenSensorCount(state.allSensors, state.dashboard);
        countEl.textContent = n ? String(n) : '';
      }
      const list = $('#sensorsList');
      const menu = $('#sensorsMenu');
      if (!list || !menu || !menu.open) return; // only render the list while the popover is open
      const ae = document.activeElement;
      if (ae && list.contains(ae) && (ae.tagName === 'INPUT' || ae.tagName === 'SELECT')) return;
      const rows = SQ.sensorPopoverRows(state.allSensors, state.dashboard, state.sensorsFilter || '');
      list.innerHTML = rows.map(r => {
        const hidden = r.visibility === 'hidden';
        const pinned = state.dashboard.pinnedCards.some(c => c.id === r.id);
        const alias = r.label !== r.rawLabel ? ` · ${esc(r.rawLabel)}` : '';
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(r.label)}</b><span>${esc(r.hw)} · ${esc(r.type)} · ${esc(r.value)}${alias}</span><code>${esc(r.id)}</code></div>
          <span class="vis-chip vis-${r.visibility}">${r.visibility}</span>
          <button class="iconbtn" data-action="${pinned ? 'unpin' : 'pin'}" data-id="${esc(r.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
          <button class="iconbtn" data-action="${hidden ? 'show' : 'hide'}" data-id="${esc(r.id)}">${hidden ? 'Show' : 'Hide'}</button>
        </div>`;
      }).join('') || '<div class="empty-note">No sensors</div>';
    }
```

- [ ] **Step 2: Call it from the render loop**

In `console.js`, find `renderCustomize();` (line 831) and add on the next line:

```javascript
      renderSensorsPopover();
```

- [ ] **Step 3: Verify JS integrity**

Run: `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js && node webtests/selftest.node.js`
Expected: parses; `SELFTEST PASS 170/170` (no new tests — DOM render is not unit-tested; verified live in Task 6).

- [ ] **Step 4: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): render sensors popover list and count (B1)"
```

---

### Task 4: Wire search, actions, and close behavior

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add near the drawer wiring, after line 1270)

**Interfaces:**
- Consumes: `setSensorHidden(id, hidden)`, `pinSensor(id)`, `unpinSensor(id)`, `commitDashboard()`, `renderSensorsPopover`, `$`.
- Produces: no new symbols; wires `#sensorsSearch`, `#sensorsMenu` click/toggle, Escape/click-outside close.

- [ ] **Step 1: Add search + action + close handlers**

In `console.js`, immediately after the line `$('#cardSearch').oninput = e => { state.cardFilter = e.target.value; renderCustomize(); };` (line 1269), add:

```javascript
    $('#sensorsSearch').oninput = e => { state.sensorsFilter = e.target.value; renderSensorsPopover(); };
    $('#sensorsMenu').addEventListener('toggle', () => { if ($('#sensorsMenu').open) renderSensorsPopover(); });
    $('#sensorsList').addEventListener('click', e => {
      const btn = e.target.closest('[data-action]');
      if (!btn) return;
      const id = btn.dataset.id;
      switch (btn.dataset.action) {
        case 'hide': setSensorHidden(id, true); break;
        case 'show': setSensorHidden(id, false); break;
        case 'pin': pinSensor(id); break;
        case 'unpin': unpinSensor(id); break;
      }
      renderSensorsPopover();
    });
    $('#sensorsMenu').querySelector('[data-action="reset-hidden"]').onclick = () => {
      state.dashboard.hiddenSensorIds = [];
      commitDashboard();
      renderSensorsPopover();
    };
    // Escape / click-outside close for masthead disclosure menus (Pages + Sensors)
    document.addEventListener('keydown', e => {
      if (e.key === 'Escape') document.querySelectorAll('details.page-menu[open], details.sensors-menu[open]').forEach(d => { d.open = false; });
    });
    document.addEventListener('click', e => {
      document.querySelectorAll('details.page-menu[open], details.sensors-menu[open]').forEach(d => { if (!d.contains(e.target)) d.open = false; });
    });
```

Note: `setSensorHidden`, `pinSensor`, `unpinSensor`, and `commitDashboard` already re-render the dashboard; the trailing `renderSensorsPopover()` refreshes the popover row's own Show/Hide/Pin label immediately.

- [ ] **Step 2: Verify JS integrity**

Run: `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js && node webtests/selftest.node.js`
Expected: parses; `SELFTEST PASS 170/170`.

- [ ] **Step 3: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): wire sensors popover search, actions, and close (B1)"
```

---

### Task 5: Popover CSS

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` (add near the `.page-menu` rules; confirm their exact location with `grep -n 'page-menu' console.css` and place the new block adjacent)

**Interfaces:**
- Consumes: existing CSS vars (`--panel`, `--panel-2`, `--line`, `--line-soft`, `--cy`, `--muted`, `--ink`, `--mono`) and the `.sensor-choice` / `.iconbtn` styles reused from the drawer.
- Produces: `.sensors-menu`, `.sensors-panel`, `.sensors-tools`, `.vis-chip` + variants, `.count` styles.

- [ ] **Step 1: Add the styles**

Append to `console.css` (or place next to `.page-menu`):

```css
/* --- Masthead Sensors popover (B1) --- */
.sensors-menu{position:relative}
.sensors-menu>summary{list-style:none;cursor:pointer}
.sensors-menu>summary::-webkit-details-marker{display:none}
.sensors-menu .count{display:inline-block;min-width:16px;padding:0 5px;margin-left:5px;border-radius:999px;
  background:color-mix(in srgb,var(--cy) 18%,var(--panel-2));color:var(--cy);font:600 10px var(--mono);text-align:center}
.sensors-panel{position:absolute;top:calc(100% + 8px);right:0;z-index:40;width:min(420px,92vw);
  max-height:min(70vh,560px);overflow:auto;padding:10px;border:1px solid var(--line);border-radius:10px;
  background:var(--panel);box-shadow:0 12px 32px -8px rgba(0,0,0,.5)}
.sensors-tools{display:flex;gap:8px;margin-bottom:8px;position:sticky;top:0}
.sensors-tools input{flex:1 1 auto;min-width:0}
.vis-chip{font:700 9px var(--mono);letter-spacing:.06em;text-transform:uppercase;padding:2px 6px;border-radius:999px;
  border:1px solid var(--line-soft);color:var(--muted)}
.vis-chip.vis-hidden{color:var(--cy);border-color:color-mix(in srgb,var(--cy) 30%,var(--line-soft))}
.vis-chip.vis-offscreen{color:var(--muted)}
.vis-chip.vis-visible{opacity:.6}
```

- [ ] **Step 2: Verify CSS is syntactically intact**

Run: `grep -c 'sensors-panel' LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`
Expected: `1` (and eyeball the block for balanced braces).

- [ ] **Step 3: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): style the sensors popover (B1)"
```

---

### Task 6: Rebuild + live verification

**Files:** none (build + browser).

- [ ] **Step 1: Stop the running app, rebuild Release/x64**

```bash
# PowerShell:
Get-Process -Name "LibreHardwareMonitor.Windows.Forms" -EA SilentlyContinue | Stop-Process -Force
dotnet build "LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj" -c Release -f net10.0-windows -p:Platform=x64
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Relaunch + confirm served assets**

```bash
Start-Process "E:\SQ_HQ\Monitoring\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe"
# after ~5s:
(Invoke-WebRequest http://localhost:8085/index.html -UseBasicParsing).Content -match 'id="sensorsMenu"'  # True
(Invoke-WebRequest http://localhost:8085/console.js -UseBasicParsing).Content -match 'renderSensorsPopover'  # True
```

- [ ] **Step 3: Live browser check (chrome-devtools or manual)**

Confirm at `http://localhost:8085/`:
- Masthead shows `Sensors N` with N = hidden/offscreen count.
- Clicking it opens an anchored panel; typing filters; a hidden sensor appears with a `hidden` chip and a `Show` button that restores it; a visible sensor shows `Hide`; `Pin` pins it; `Reset hidden` clears hidden state.
- Escape and click-outside close the panel; no console errors; dark + light both readable; panel fits at 390px width.

- [ ] **Step 4: Merge B1 to master**

```bash
git checkout master
git merge --no-ff feat/web-sensors-popover -m "Merge sensors popover (Phase B1)"
node webtests/selftest.node.js   # 170/170
git push origin master
git branch -d feat/web-sensors-popover
```

---

## Follow-on (not in this plan)

- **B2 — explicit primary-card selection** (`primaryCardsMode` sentinel; "show as / remove from primary" on card/row/popover).
- **B3 — drawer removal** (delete `#customizeDrawer`, `#customizeScrim`, tabs/panes, `renderCustomize`, drawer handlers, drawer CSS) — only after B1 (this plan) and B2 reach parity; keep the keyboard-accessible paths.

## Self-Review

1. **Spec coverage (handoff §4B):** search all sensors ✓ (Task 1 query + Task 2 input), show/hide ✓ (Task 4 actions), pin ✓ (Task 4), reset hidden ✓ (Task 2 button + Task 4 handler), hidden count badge ✓ (`hiddenSensorCount`, Task 3), anchored/compact/not-a-side-pane ✓ (Task 5), raw label + SensorId preserved ✓ (Task 3 row shape shows `rawLabel` + `id`), escape/click-outside close ✓ (Task 4), does-not-pause-polling ✓ (render loop unaffected; the list rebuilds **only on structural change** — a signature of filter + each row's id/visibility/pin — not every poll tick, so values freeze while open and text selection/typing survive; the count badge still updates live). Offscreen restore of network adapters is deferred to Phase C (network subgroups) — noted, not in scope.
2. **Placeholder scan:** none — every code step shows complete code.
3. **Type consistency:** `SQ.sensorPopoverRows` row keys `{id,label,rawLabel,hw,type,value,visibility}` are produced in Task 1 and consumed verbatim in Task 3; `state.sensorsFilter` set in Task 4, read in Task 3; reused functions (`setSensorHidden`, `pinSensor`, `unpinSensor`, `commitDashboard`) verified present at `console.js:778/1289-1290/commit`.

## Execution notes (2026-07-06, inline)

Executed inline via executing-plans. Two deviations from the written plan, both driven by evidence during execution:

- **Task 1 test split (170→171 selftests):** the planned `'load'` filter assertion wrongly assumed the query could only match `id`/`type`; substring search legitimately matches `up`**`load`**`ed`/`down`**`load`**`ed`. Replaced with two clearer assertions (narrowed-subset + includes-known-`/amdcpu/0/load/0`).
- **Task 5 CSS:** the drawer's `.sensor-choice` is a 3-column grid; the popover adds a 4th item (the `vis-chip`), so added a scoped `.sensors-panel .sensor-choice{grid-template-columns:minmax(0,1fr) auto auto auto}` override + a background on the sticky `.sensors-tools`.

Two bugs found in Task 6 live verification (neither unit-testable via the DOM-less node harness), fixed and re-verified live:

1. **Action clicks closed the popover** — the bubble-phase click-outside handler saw the clicked button *after* the list rebuild detached it, so it read as "outside." Fixed by moving the handler to the **capture phase** (`3233a19`'s parent `f7a9a7d`).
2. **Every-tick rebuild destroyed text selection** — the focus guard was dead code (`#sensorsList` has no INPUT/SELECT), so the list rebuilt each 1s tick, wiping an in-progress SensorId selection. Fixed by **signature-gating** the rebuild (`3233a19`).
