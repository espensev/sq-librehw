# Console Customize Tier 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the SQ Telemetry Console's customization first-class — inline hover/focus pin+hide on cards/rows, live-dashboard drag reorder for pinned cards and panels, and consolidation of the loose `sq.*` localStorage keys into the versioned `sq.dashboard.v1` object.

**Architecture:** Pure client-side, no build step, extends the three existing web files only. New pure logic lives under `window.SQ.*` and is unit-tested; DOM wiring (delegated click handlers, pointer-drag) is gated behind `!window.SQ_NO_BOOT` and verified by the model self-test regression + `node --check` + manual browser E2E. Baseline is commit `095bd69` (SELFTEST 32/32 green).

**Tech Stack:** Vanilla ES2020 JS, CSS custom properties, `localStorage`, Pointer Events. No framework, no dependencies, no CDN.

**Spec:** `docs/superpowers/specs/2026-07-04-console-customize-tier3-design.md`

## Global Constraints

- Do NOT edit `HttpServer.cs`, `data.json` generation, `/Sensor`, `/metrics`, CSV, or `AssemblyVersion` — the ThermalTrace contract and golden tests must stay untouched.
- Asset filenames must have no hyphens (`ServeResourceFileAsync` can't serve them) — only `console.js` / `console.css` / `index.html` under `Resources/Web/` change. `webtests/` is a repo test dir (not embedded), so adding test files there is safe.
- Keep exact numeric readouts and the existing row fill bars; customization is a projection only.
- The model self-test must stay green and grow (baseline 32 → target 60). After Task 1 it is runnable headless as `node webtests/selftest.node.js` (the browser extension is unavailable this session).
- Drag applies only to pinned cards (`#pinned`) and panels (`#panels`), never the auto PFD (`#pfd`).
- Existing drawer Reset actions (`reset-hidden`, `reset-panels`, `clear-pinned`) and up/down reorder stay working; Up/Down is the keyboard path for reordering.
- Every code task ends with `node --check` on `console.js` clean and `node webtests/selftest.node.js` green before commit.

## File Structure

- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — model helpers + DOM wiring (all three features).
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` — control-cluster, grip, ghost, indicator styles.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` — expected unchanged (controls/ghost injected by JS).
- `webtests/console.tests.js` — NEW shared assertion module (single source of test truth).
- `webtests/console.test.html` — refactored to load the shared module (browser runner).
- `webtests/selftest.node.js` — NEW headless runner (evals console.js with a `window` shim, runs the shared module).

---

### Task 1: Shared test harness (extract assertions + headless Node runner)

**Why first:** subsequent tasks are TDD; fresh subagents need one committed, runnable test. This extracts today's inline assertions into a module both the browser page and a Node runner consume, so later tasks add cases in exactly one place.

**Files:**
- Create: `webtests/console.tests.js`
- Create: `webtests/selftest.node.js`
- Modify: `webtests/console.test.html`

**Interfaces:**
- Produces: `runConsoleTests(S, data, makeStorage) -> {pass, fail, log}` where `S` is `window.SQ`, `data` is the parsed fixture, `makeStorage` is `value => storageMock`. Exposed as `module.exports` (Node) and `window.runConsoleTests` (browser).

- [ ] **Step 1: Create `webtests/console.tests.js`** with the exact assertions currently inline in `console.test.html`, wrapped in the shared function (UMD tail so it loads in both Node and browser):

```js
(function (root) {
  function runConsoleTests(S, data, makeStorage) {
    let pass = 0, fail = 0; const log = [];
    const eq = (name, got, want) => {
      const ok = JSON.stringify(got) === JSON.stringify(want);
      log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${JSON.stringify(got)} want=${JSON.stringify(want)}`);
      ok ? pass++ : fail++;
    };

    eq('classOf amdcpu', S.classOf('/amdcpu/0/temperature/2'), 'cpu');
    eq('classOf gpu-nvidia', S.classOf('/gpu-nvidia/0/temperature/0'), 'gpu');
    eq('classOf gpu-amd', S.classOf('/gpu-amd/0/temperature/4'), 'igpu');
    eq('classOf nvme', S.classOf('/nvme/1/temperature/0'), 'nvme');
    eq('classOf lpc', S.classOf('/lpc/nct6701d/0/temperature/5'), 'mb');
    eq('classOf nic', S.classOf('/nic/%7BX%7D/load/1'), 'nic');
    eq('splitValue temp', S.splitValue('65.5 °C'), {n:'65.5', unit:'°C'});
    eq('splitValue pct', S.splitValue('27.1 %'), {n:'27.1', unit:'%'});
    eq('splitValue null', S.splitValue(null), {n:'—', unit:''});

    const sensors = S.flatten(data.Children[0]);
    const limits = S.deriveLimits(sensors);
    const byId = id => sensors.find(s => s.id === id);
    const st = id => S.statusOf(byId(id), limits);
    const storage = makeStorage;

    eq('cpu Tctl ok', st('/amdcpu/0/temperature/2'), 'ok');
    eq('mb stray info', st('/lpc/nct6701d/0/temperature/5'), 'info');
    eq('mb stray suppressed from dashboard', S.isDashboardSuppressedSensor(byId('/lpc/nct6701d/0/temperature/5')), true);
    eq('visible sensors hide mb stray', S.visibleSensors(sensors).some(s => s.id === '/lpc/nct6701d/0/temperature/5'), false);
    eq('nvme temp ok', st('/nvme/2/temperature/2'), 'ok');
    S.resetSensorMotion();
    eq('nvme static aux temp suppressed from dashboard',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', rawMin:52.85, rawMax:52.85, id:'/nvme/2/temperature/2'}), true);
    S.trackSensorMotion([52.4, 52.5, 52.6, 52.4, 52.7].map(raw => ({id:'/nvme/2/temperature/2', raw})));
    eq('nvme low-motion aux temp remains suppressed',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', id:'/nvme/2/temperature/2'}), true);
    S.resetSensorMotion();
    S.trackSensorMotion([45, 46, 47, 50, 52.85].map(raw => ({id:'/nvme/2/temperature/2', raw})));
    eq('nvme moving aux temp remains visible',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', rawMin:45, rawMax:52.85, id:'/nvme/2/temperature/2'}), false);
    S.resetSensorMotion();
    eq('nvme limit info', st('/nvme/2/temperature/10'), 'info');
    eq('nvme limit display type', S.displayType(byId('/nvme/2/temperature/10')), 'Limits');
    eq('nvme real temp display type', S.displayType(byId('/nvme/2/temperature/2')), 'Temperature');
    eq('cpu load info', st('/amdcpu/0/load/0'), 'info');
    eq('amd hotspot junction band', S.statusOf({cls:'igpu', type:'Temperature', text:'GPU Hot Spot', raw:94, hwid:'x'}, {}), 'ok');
    eq('ssd life crit', S.statusOf({type:'Level', text:'Life', raw:3}, {}), 'crit');
    eq('ssd life warn', S.statusOf({type:'Level', text:'Life', raw:15}, {}), 'warn');

    const hero = S.pickHero(sensors, limits);
    eq('hero has CPU Temp', hero.some(h => h.label === 'CPU Temp'), true);
    eq('CPU Power unbounded', !!hero.find(h => h.label === 'CPU Power')?.bounded, false);
    eq('CPU Temp bounded', !!hero.find(h => h.label === 'CPU Temp')?.bounded, true);

    eq('dashboard state bad json falls back', S.loadDashboardState(storage('{bad')).hiddenSensorIds, []);
    const showDefault = S.normalizeDashboardState({shownDefaultHiddenSensorIds:['/lpc/nct6701d/0/temperature/5']});
    eq('default hidden can be shown', S.visibleSensors(sensors, showDefault).some(s => s.id === '/lpc/nct6701d/0/temperature/5'), true);
    const explicitHidden = S.normalizeDashboardState({hiddenSensorIds:['/amdcpu/0/load/0']});
    eq('explicit hidden sensor removed', S.visibleSensors(sensors, explicitHidden).some(s => s.id === '/amdcpu/0/load/0'), false);
    const pinned = S.normalizeDashboardState({pinnedCards:[
      {id:'/amdcpu/0/load/0', title:'CPU Work'},
      {id:'/missing/sensor', title:'Missing'}
    ], pinnedOrder:['/missing/sensor','/amdcpu/0/load/0']});
    const cards = S.resolvePinnedCards(sensors, pinned, limits);
    eq('resolve pinned ignores missing', cards.map(c => c.label), ['CPU Work']);
    eq('apply panel order', S.applyOrder([{key:'a',index:0},{key:'b',index:1}], ['b'], x => x.key).map(x => x.key), ['b','a']);

    // === Tier 3 cases are appended below by later tasks ===

    return { pass, fail, log };
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = runConsoleTests;
  else root.runConsoleTests = runConsoleTests;
})(typeof window !== 'undefined' ? window : globalThis);
```

- [ ] **Step 2: Create `webtests/selftest.node.js`**:

```js
// Headless mirror of console.test.html. Evals console.js with a window shim
// (SQ_NO_BOOT skips the DOM bootstrap) and runs the shared assertion module.
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..');
global.window = { SQ_NO_BOOT: true };
eval(fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js'), 'utf8'));
const runConsoleTests = require('./console.tests.js');
const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json'), 'utf8'));
const storage = value => { let slot = value; return { getItem: () => slot, setItem: (k, v) => { slot = v; } }; };
const { pass, fail, log } = runConsoleTests(global.window.SQ, data, storage);
console.log(log.join('\n'));
console.log(`\nSELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass + fail}`);
process.exit(fail === 0 ? 0 : 1);
```

- [ ] **Step 3: Refactor `webtests/console.test.html`** to load the shared module — replace the whole `<script> (async () => { ... })(); </script>` body with:

```html
<script>window.SQ_NO_BOOT = true;</script>
<script src="/LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js"></script>
<script src="/webtests/console.tests.js"></script>
<script>
(async () => {
  const out = document.getElementById('out');
  const data = await (await fetch('/webtests/fixture.data.json', {cache:'no-store'})).json();
  const storage = value => { let slot = value; return { getItem: () => slot, setItem: (k, v) => { slot = v; } }; };
  const { pass, fail, log } = window.runConsoleTests(window.SQ, data, storage);
  console.log(`SELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass+fail}`);
  out.textContent = log.join('\n') + `\n\nSELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass+fail}`;
})();
</script>
```

- [ ] **Step 4: Run the headless runner to verify the refactor is faithful**

Run: `node webtests/selftest.node.js`
Expected: `SELFTEST PASS 32/32`.

- [ ] **Step 5: Commit**

```bash
git add webtests/console.tests.js webtests/selftest.node.js webtests/console.test.html
git commit -m "test(web): extract shared console test module + headless node runner"
```

---

### Task 2: Extend `sq.dashboard.v1` schema + legacy-key migration

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (helpers near lines 48-101)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: existing `cleanStringList`, `cleanPinnedCards`, `SQ.normalizeDashboardState`, `SQ.defaultDashboardState`.
- Produces:
  - `clampRate(n) -> number` (module-private): rounds, clamps to `[1,10]`, default `2` for non-finite.
  - `cleanCollapsedMap(value) -> {[hw:string]:boolean}` (module-private).
  - Extended state adds `paused:boolean`, `rate:number`, `theme:'dark'|'light'`, `collapsedPanels:{[hw]:boolean}`.
  - `SQ.migrateLegacyState(storage, state) -> normalizedState` — folds legacy keys into state and `removeItem`s them; idempotent.

- [ ] **Step 1: Write the failing tests** — insert before the `// === Tier 3 cases` marker in `webtests/console.tests.js`:

```js
    // --- Tier 3: schema + migration ---
    eq('default has consolidated fields', (() => { const d = S.defaultDashboardState();
      return [d.paused, d.rate, d.theme, JSON.stringify(d.collapsedPanels)]; })(), [false, 2, 'dark', '{}']);
    eq('normalize clamps rate high', S.normalizeDashboardState({rate: 99}).rate, 10);
    eq('normalize clamps rate low', S.normalizeDashboardState({rate: 0}).rate, 1);
    eq('normalize rate default', S.normalizeDashboardState({}).rate, 2);
    eq('normalize theme light', S.normalizeDashboardState({theme:'light'}).theme, 'light');
    eq('normalize theme junk -> dark', S.normalizeDashboardState({theme:'x'}).theme, 'dark');
    eq('normalize paused bool', S.normalizeDashboardState({paused:true}).paused, true);
    eq('normalize collapsed map coerces', S.normalizeDashboardState({collapsedPanels:{CPU:1,GPU:false,'':true}}).collapsedPanels, {CPU:true, GPU:false});
    eq('normalize collapsed rejects array', S.normalizeDashboardState({collapsedPanels:['CPU']}).collapsedPanels, {});
    const legacyStore = (() => {
      const m = {'sq.paused':'1','sq.rate':'5','sq.theme':'light','sq.panel.CPU':'1','sq.panel.Network':'0','other':'keep'};
      return { get length(){return Object.keys(m).length;}, key:i=>Object.keys(m)[i],
        getItem:k=>k in m?m[k]:null, setItem:(k,v)=>{m[k]=String(v);}, removeItem:k=>{delete m[k];}, _m:m };
    })();
    const migrated = S.migrateLegacyState(legacyStore, S.defaultDashboardState());
    eq('migrate folds paused', migrated.paused, true);
    eq('migrate folds rate', migrated.rate, 5);
    eq('migrate folds theme', migrated.theme, 'light');
    eq('migrate folds collapsed map', migrated.collapsedPanels, {CPU:true, Network:false});
    eq('migrate removes legacy keys', [legacyStore._m['sq.paused'], legacyStore._m['sq.rate'], legacyStore._m['sq.theme'], legacyStore._m['sq.panel.CPU']], [undefined, undefined, undefined, undefined]);
    eq('migrate keeps unrelated key', legacyStore._m['other'], 'keep');
    eq('migrate idempotent (2nd pass)', (() => { const again = S.migrateLegacyState(legacyStore, migrated);
      return [again.paused, again.rate, again.theme]; })(), [true, 5, 'light']);
```

- [ ] **Step 2: Run to verify FAIL** — `node webtests/selftest.node.js` → FAIL (`S.migrateLegacyState is not a function`; `default has consolidated fields` mismatch).

- [ ] **Step 3: Add private helpers** — in `console.js`, after `cleanPinnedCards` (ends line 62), add:

```js
  function clampRate(n) {
    n = Math.round(Number(n));
    if (!Number.isFinite(n)) return 2;
    return Math.max(1, Math.min(10, n));
  }
  function cleanCollapsedMap(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => { if (typeof k === 'string' && k.length) out[k] = value[k] === true; });
    return out;
  }
```

- [ ] **Step 4: Extend `defaultDashboardState`** — add after `graphsEnabled: false`:

```js
      graphsEnabled: false,
      paused: false,
      rate: 2,
      theme: 'dark',
      collapsedPanels: {}
```

- [ ] **Step 5: Extend `normalizeDashboardState`** — add after `graphsEnabled: value.graphsEnabled === true`:

```js
      graphsEnabled: value.graphsEnabled === true,
      paused: value.paused === true,
      rate: clampRate(value.rate),
      theme: value.theme === 'light' ? 'light' : 'dark',
      collapsedPanels: cleanCollapsedMap(value.collapsedPanels)
```

- [ ] **Step 6: Add `SQ.migrateLegacyState`** — after `SQ.saveDashboardState` (ends line 101):

```js
  SQ.migrateLegacyState = function (storage, state) {
    const cfg = SQ.normalizeDashboardState(state);
    if (!storage || typeof storage.getItem !== 'function') return cfg;
    const paused = storage.getItem('sq.paused');
    if (paused != null) cfg.paused = paused === '1';
    const rate = storage.getItem('sq.rate');
    if (rate != null && rate !== '') cfg.rate = clampRate(rate);
    const theme = storage.getItem('sq.theme');
    if (theme === 'dark' || theme === 'light') cfg.theme = theme;
    const panelKeys = [];
    if (typeof storage.length === 'number' && typeof storage.key === 'function') {
      for (let i = 0; i < storage.length; i++) {
        const k = storage.key(i);
        if (typeof k === 'string' && k.indexOf('sq.panel.') === 0) panelKeys.push(k);
      }
    }
    panelKeys.forEach(k => { cfg.collapsedPanels[k.slice('sq.panel.'.length)] = storage.getItem(k) === '1'; });
    if (typeof storage.removeItem === 'function') {
      ['sq.paused', 'sq.rate', 'sq.theme'].forEach(k => storage.removeItem(k));
      panelKeys.forEach(k => storage.removeItem(k));
    }
    return cfg;
  };
```

- [ ] **Step 7: Verify PASS** — `node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` clean; `node webtests/selftest.node.js` → `SELFTEST PASS 48/48`.

- [ ] **Step 8: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): extend sq.dashboard.v1 with paused/rate/theme/collapsedPanels + legacy migration"
```

---

### Task 3: Rewire runtime persistence to the consolidated state

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (bootstrap: state init ~265, `panelEl` collapse ~484-493, theme ~630-633, rate ~634-636, pause ~641-642)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: `SQ.migrateLegacyState`, `SQ.saveDashboardState`, `clampRate` (all in the same IIFE scope), extended state fields from Task 2.
- Produces: `SQ.isPanelCollapsed(state, hw, defaultCollapsed) -> boolean` (tri-state: stored wins, else default). Runtime reads/writes `paused`/`rate`/`theme`/panel-collapse through `state.dashboard`; no direct `localStorage` use for `sq.paused`/`sq.rate`/`sq.theme`/`sq.panel.*`.

- [ ] **Step 1: Write the failing tests** — insert before the `// === Tier 3 cases` marker:

```js
    // --- Tier 3: panel collapse tri-state ---
    eq('collapse stored true wins', S.isPanelCollapsed({collapsedPanels:{CPU:true}}, 'CPU', false), true);
    eq('collapse stored false wins over default-collapsed', S.isPanelCollapsed({collapsedPanels:{Network:false}}, 'Network', true), false);
    eq('collapse absent uses default true', S.isPanelCollapsed({collapsedPanels:{}}, 'Network', true), true);
    eq('collapse absent uses default false', S.isPanelCollapsed({collapsedPanels:{}}, 'CPU', false), false);
```

- [ ] **Step 2: Run to verify FAIL** — `node webtests/selftest.node.js` → FAIL (`S.isPanelCollapsed is not a function`).

- [ ] **Step 3: Add `SQ.isPanelCollapsed`** — after `SQ.migrateLegacyState`:

```js
  SQ.isPanelCollapsed = function (state, hw, defaultCollapsed) {
    const v = SQ.normalizeDashboardState(state).collapsedPanels[hw];
    return v == null ? !!defaultCollapsed : v === true;
  };
```

- [ ] **Step 4: Run to verify PASS** — `node webtests/selftest.node.js` → `SELFTEST PASS 52/52`.

- [ ] **Step 5: Migrate on load + source paused/rate from state** — replace the head of the `state` object literal (currently lines 265-269, `const state = { paused: ..., rate: ..., timer: null, dashboard: ..., lastData: null,`) with:

```js
    const dashboard0 = SQ.migrateLegacyState(localStorage, SQ.loadDashboardState(localStorage));
    SQ.saveDashboardState(localStorage, dashboard0);
    const state = {
      paused: dashboard0.paused,
      rate: dashboard0.rate,
      timer: null,
      dragging: false,
      dashboard: dashboard0,
      lastData: null,
```

(Leave every remaining field of the literal — `allSensors`, `visibleSensors`, `panelItems`, `customizeOpen`, etc. — exactly as it was.)

- [ ] **Step 6: Rewire theme** — replace lines 630-633:

```js
    document.documentElement.setAttribute('data-theme', state.dashboard.theme);
    $('#theme').onclick = () => {
      const t = state.dashboard.theme === 'dark' ? 'light' : 'dark';
      state.dashboard.theme = t;
      document.documentElement.setAttribute('data-theme', t);
      saveDashboard();
    };
```

- [ ] **Step 7: Rewire rate** — replace lines 634-636 (`clampRate` is in scope — call it directly):

```js
    const rate = $('#rate'); rate.value = state.rate; $('#ratev').textContent = state.rate + 's';
    rate.oninput = e => {
      state.rate = clampRate(e.target.value);
      state.dashboard.rate = state.rate;
      $('#ratev').textContent = state.rate + 's';
      saveDashboard(); schedule();
    };
```

- [ ] **Step 8: Rewire pause** — replace the `pause.onclick` (lines 641-642):

```js
    pause.onclick = () => {
      state.paused = !state.paused;
      state.dashboard.paused = state.paused;
      saveDashboard(); paintPause();
      if (!state.paused) tick();
    };
```

- [ ] **Step 9: Rewire panel collapse** — in `panelEl`, replace `const cls = ss[0].cls, collapseKey = 'sq.panel.' + hw;` + the two following `stored`/`startCollapsed` lines (484-485) with:

```js
      const cls = ss[0].cls;
      const startCollapsed = SQ.isPanelCollapsed(state.dashboard, hw, collapsed);
```

and replace the header `h.onclick` (line 493) with:

```js
      h.onclick = () => {
        p.classList.toggle('collapsed');
        state.dashboard.collapsedPanels[hw] = p.classList.contains('collapsed');
        saveDashboard();
      };
```

- [ ] **Step 10: Verify no legacy keys remain outside migration**

Run: `grep -nE "localStorage\.(get|set|remove)Item\('sq\.(paused|rate|theme|panel)" LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`
Expected: no matches (all such access now goes through `state.dashboard` + `saveDashboard`; the only `sq.panel.` / `sq.paused` string literals left are inside `SQ.migrateLegacyState`).

- [ ] **Step 11: Verify** — `node --check ... console.js` clean; `node webtests/selftest.node.js` → `SELFTEST PASS 52/52`. Manual browser: toggle theme/rate/pause/collapse, reload, all persist; `localStorage` has `sq.dashboard.v1` and no `sq.paused`/`sq.rate`/`sq.theme`/`sq.panel.*`.

- [ ] **Step 12: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): route theme/rate/pause/panel-collapse through sq.dashboard.v1 (+migrate legacy keys)"
```

---

### Task 4: Pure reorder + isPinned helpers (drag foundation)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (near `SQ.applyOrder` ~122-131)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces:
  - `SQ.reorderByDrop(orderedKeys, movedKey, targetIndex) -> string[]` — removes `movedKey`, re-inserts at clamped `targetIndex`; returns cleaned keys unchanged if `movedKey` absent.
  - `SQ.isPinned(state, id) -> boolean`.

- [ ] **Step 1: Write the failing tests** — insert before the `// === Tier 3 cases` marker:

```js
    // --- Tier 3: reorder + isPinned ---
    eq('reorder move to end', S.reorderByDrop(['a','b','c'], 'a', 2), ['b','c','a']);
    eq('reorder move to front', S.reorderByDrop(['a','b','c'], 'c', 0), ['c','a','b']);
    eq('reorder no-op index', S.reorderByDrop(['a','b','c'], 'b', 1), ['a','b','c']);
    eq('reorder clamps high index', S.reorderByDrop(['a','b','c'], 'a', 99), ['b','c','a']);
    eq('reorder clamps low index', S.reorderByDrop(['a','b','c'], 'c', -5), ['c','a','b']);
    eq('reorder missing key unchanged', S.reorderByDrop(['a','b','c'], 'z', 0), ['a','b','c']);
    eq('isPinned true', S.isPinned({pinnedCards:[{id:'/x',title:''}]}, '/x'), true);
    eq('isPinned false', S.isPinned({pinnedCards:[]}, '/x'), false);
```

- [ ] **Step 2: Run to verify FAIL** — `node webtests/selftest.node.js` → FAIL (`S.reorderByDrop is not a function`).

- [ ] **Step 3: Implement** — after `SQ.applyOrder` (ends line 131):

```js
  SQ.reorderByDrop = function (orderedKeys, movedKey, targetIndex) {
    const keys = cleanStringList(orderedKeys);
    const from = keys.indexOf(movedKey);
    if (from < 0) return keys;
    keys.splice(from, 1);
    const n = Math.trunc(Number(targetIndex));
    const to = Math.max(0, Math.min(keys.length, Number.isFinite(n) ? n : 0));
    keys.splice(to, 0, movedKey);
    return keys;
  };
  SQ.isPinned = function (state, id) {
    return SQ.normalizeDashboardState(state).pinnedCards.some(c => c.id === id);
  };
```

- [ ] **Step 4: Verify PASS** — `node webtests/selftest.node.js` → `SELFTEST PASS 60/60`; `node --check` clean.

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): add pure SQ.reorderByDrop + SQ.isPinned helpers"
```

---

### Task 5: Inline pin/hide controls on cards and rows

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (`esc` area ~280, `cardEl` ~421-439, `rowEl` ~469-479, bootstrap init after other handlers ~649)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`

**Interfaces:**
- Consumes: `SQ.isPinned` (Task 4); existing `pinSensor`, `unpinSensor`, `setSensorHidden`, `esc`.
- Produces: `ctlCluster(id, label, opts) -> htmlString`; a single delegated click handler on `#pfd`/`#pinned`/`#panels` dispatching `data-act` in `{pin, unpin, hide}`.

- [ ] **Step 1: Add the cluster builder** — in the bootstrap, right after `esc` (ends line 282):

```js
    function ctlCluster(id, label, opts) {
      const pinned = SQ.isPinned(state.dashboard, id);
      const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
      const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
      return pin + hide;
    }
```

- [ ] **Step 2: Add the cluster to `cardEl`** — insert right before `return cell;`:

```js
      const showHide = !pinned; // hero cards get hide; the dedicated pinned strip gets unpin only
      const ctl = document.createElement('div');
      ctl.className = 'cell-ctl';
      ctl.innerHTML = ctlCluster(h.s.id, h.label, { hide: showHide });
      cell.appendChild(ctl);
      return cell;
```

- [ ] **Step 3: Add the cluster to `rowEl`** — insert right before `return r;`:

```js
      const rctl = document.createElement('span');
      rctl.className = 'row-ctl';
      rctl.innerHTML = ctlCluster(s.id, s.text, { hide: true });
      r.appendChild(rctl);
      return r;
```

- [ ] **Step 4: Add the delegated click handler** — in the bootstrap init block, after the drawer handlers (~line 649, before `paintPause();`):

```js
    ['#pfd', '#pinned', '#panels'].forEach(sel => {
      const host = $(sel);
      host && host.addEventListener('click', e => {
        const b = e.target.closest('.ctl');
        if (!b || !host.contains(b)) return;
        e.stopPropagation();
        const id = b.dataset.id;
        if (b.dataset.act === 'pin') pinSensor(id);
        else if (b.dataset.act === 'unpin') unpinSensor(id);
        else if (b.dataset.act === 'hide') setSensorHidden(id, true);
      });
    });
```

- [ ] **Step 5: Add CSS** — append to `console.css`:

```css
/* inline customize controls */
.cell,.row{position:relative}
.ctl{font:12px var(--mono);line-height:1;padding:3px 5px;border:1px solid var(--line-soft);border-radius:6px;
  background:var(--panel-2);color:var(--muted);cursor:pointer}
.ctl:hover{color:var(--cy);border-color:var(--cy)}
.ctl.pin.on{color:var(--lime);border-color:color-mix(in srgb,var(--lime) 45%,var(--line))}
.cell-ctl,.row-ctl{position:absolute;display:none;gap:4px;z-index:2}
.cell-ctl{top:10px;right:11px}
.row-ctl{top:50%;right:12px;transform:translateY(-50%);align-items:center;
  background:linear-gradient(90deg,transparent,var(--panel) 22%);padding-left:22px}
.cell:hover .cell-ctl,.cell:focus-within .cell-ctl,
.row:hover .row-ctl,.row:focus-within .row-ctl{display:flex}
@media (hover:none){.cell-ctl,.row-ctl{display:flex}}
```

- [ ] **Step 6: Verify** — `node --check ... console.js` clean; `node webtests/selftest.node.js` → `SELFTEST PASS 60/60` (regression only). Manual browser: hover a hero card and a panel row → pin + hide appear; pin toggles and mirrors the drawer Cards tab; hide removes the sensor from PFD + panels while it remains in `data.json`; pinned-strip cards show an active unpin and no hide.

- [ ] **Step 7: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): inline hover/focus pin+hide controls on cards and rows"
```

---

### Task 6: Live drag-and-drop reorder for panels and pinned cards

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (`panelEl` header ~486-493, `cardEl` ~428-429 + the Task 5 `ctl.innerHTML` line, `tick` guard ~617, bootstrap: add drag module after the Task 5 handler)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`

**Interfaces:**
- Consumes: `SQ.reorderByDrop` (Task 4); `state.dashboard.panelOrder`/`pinnedOrder`; `state.dragging`; `commitDashboard`, `rerender`.
- Produces: a `.grip` on panel headers and pinned cards; `state.dragging` suppresses the poll re-render during a drag; drop writes `panelOrder`/`pinnedOrder` via `SQ.reorderByDrop`.

- [ ] **Step 1: Tag draggable elements with `data-key`.** In `panelEl`, after `const p = document.createElement('div'); p.className = 'panel'...` (line 486) add:

```js
      p.dataset.key = item.key;
```

In `cardEl`, after `cell.className = ...` (line 429) add:

```js
      if (pinned) cell.dataset.key = h.s.id;
```

- [ ] **Step 2: Add the grip to the panel header.** In `panelEl`, replace the `h.innerHTML = ...` assignment (lines 490-492) with a version that prepends a grip button:

```js
      h.innerHTML = `<button class="grip" aria-label="Drag to reorder ${esc(hw)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        `<span class="lamp s-${worst}"></span><span class="nm">${esc(hw)}</span>` +
        `<span class="cls">${CLASSLABEL[cls] || ''}</span>` +
        `<span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
```

- [ ] **Step 3: Add the grip to pinned cards.** In `cardEl`, change the Task 5 `ctl.innerHTML = ctlCluster(...)` line to prepend a grip for pinned cards:

```js
      ctl.innerHTML = (pinned ? `<button class="grip" aria-label="Drag to reorder ${esc(h.label)}" title="Drag to reorder">&#8942;&#8942;</button>` : '') + ctlCluster(h.s.id, h.label, { hide: showHide });
```

- [ ] **Step 4: Add the drag module.** In the bootstrap, after the Task 5 delegated `.ctl` handler:

```js
    const drag = { active: null };
    function orderedKeysFor(container) {
      return Array.from(container.children).map(el => el.dataset.key).filter(k => typeof k === 'string' && k.length);
    }
    function dropIndex(container, movedKey, clientY) {
      const sibs = Array.from(container.children).filter(el => el.dataset.key && el.dataset.key !== movedKey);
      for (let i = 0; i < sibs.length; i++) {
        const r = sibs[i].getBoundingClientRect();
        if (clientY < r.top + r.height / 2) return i;
      }
      return sibs.length;
    }
    function moveGhost(ev) {
      const a = drag.active; if (!a) return;
      a.ghost.style.left = (ev.clientX + 12) + 'px';
      a.ghost.style.top = (ev.clientY + 12) + 'px';
      a.dropIdx = dropIndex(a.container, a.key, ev.clientY);
      const sibs = Array.from(a.container.children).filter(el => el.dataset.key && el.dataset.key !== a.key);
      const ref = sibs[a.dropIdx];
      if (ref) a.container.insertBefore(a.ind, ref); else a.container.appendChild(a.ind);
    }
    function startDrag(grip, ev) {
      const el = grip.closest('.panel') || grip.closest('.cell.pinned');
      if (!el || !el.dataset.key) return;
      ev.preventDefault();
      const nameEl = el.querySelector('.nm') || el.querySelector('.k .name');
      state.dragging = true;
      const ghost = document.createElement('div');
      ghost.className = 'drag-ghost';
      ghost.textContent = nameEl ? nameEl.textContent : el.dataset.key;
      document.body.appendChild(ghost);
      const ind = document.createElement('div');
      ind.className = 'drop-ind';
      drag.active = { container: el.parentElement, el, key: el.dataset.key,
        isPanel: el.classList.contains('panel'), ghost, ind, grip, pointerId: ev.pointerId };
      el.classList.add('dragging');
      moveGhost(ev);
      try { grip.setPointerCapture(ev.pointerId); } catch (e) {}
    }
    function endDrag(commit) {
      const a = drag.active; if (!a) return;
      drag.active = null; state.dragging = false;
      a.el.classList.remove('dragging');
      a.ghost.remove(); a.ind.remove();
      try { a.grip.releasePointerCapture(a.pointerId); } catch (e) {}
      if (commit && typeof a.dropIdx === 'number') {
        const next = SQ.reorderByDrop(orderedKeysFor(a.container), a.key, a.dropIdx);
        if (a.isPanel) state.dashboard.panelOrder = next; else state.dashboard.pinnedOrder = next;
        commitDashboard();
      } else {
        rerender();
      }
    }
    document.addEventListener('pointerdown', ev => {
      const grip = ev.target.closest && ev.target.closest('.grip');
      if (grip) { ev.stopPropagation(); startDrag(grip, ev); }
    });
    document.addEventListener('pointermove', ev => { if (drag.active) moveGhost(ev); });
    document.addEventListener('pointerup', () => { if (drag.active) endDrag(true); });
    document.addEventListener('pointercancel', () => { if (drag.active) endDrag(false); });
    document.addEventListener('keydown', ev => { if (ev.key === 'Escape' && drag.active) endDrag(false); });
    document.addEventListener('click', ev => {
      const grip = ev.target.closest && ev.target.closest('.grip');
      if (grip) { ev.preventDefault(); ev.stopPropagation(); }
    }, true);
```

- [ ] **Step 5: Guard the poll render during a drag.** In `tick` (line 617), change the guard:

```js
    async function tick(force) {
      if ((state.paused && !force) || state.dragging) return;
```

- [ ] **Step 6: Add CSS** — append to `console.css`:

```css
/* drag reorder */
.grip{font:11px var(--mono);letter-spacing:-2px;line-height:1;padding:2px 4px;margin-right:2px;
  border:1px solid var(--line-soft);border-radius:5px;background:var(--panel-2);color:var(--dim);
  cursor:grab;display:none;touch-action:none}
.panel-head:hover .grip,.panel-head:focus-within .grip,.cell:hover .grip,.cell:focus-within .grip{display:inline-block}
@media (hover:none){.grip{display:inline-block}}
.grip:hover{color:var(--cy);border-color:var(--cy)}
.grip:active{cursor:grabbing}
.dragging{opacity:.4}
.drag-ghost{position:fixed;z-index:80;pointer-events:none;font:600 12px var(--disp);letter-spacing:.06em;
  text-transform:uppercase;color:var(--ink);background:linear-gradient(180deg,var(--panel),var(--panel-2));
  border:1px solid var(--cy);border-radius:7px;padding:7px 11px;box-shadow:0 12px 30px -14px rgba(0,0,0,.9)}
.drop-ind{height:0;border-top:2px solid var(--cy);margin:0 0 12px;box-shadow:0 0 8px var(--cy);break-inside:avoid;width:100%}
```

- [ ] **Step 7: Verify** — `node --check ... console.js` clean; `node webtests/selftest.node.js` → `SELFTEST PASS 60/60`. Manual browser: hover a panel header / pinned card → grip appears; drag a panel → compact ghost follows, insertion line shows, drop reflows the masonry into the new order and persists across reload; a poll tick during the drag does not disrupt it; Escape cancels; the grip never toggles panel collapse; pin/hide clicks still work.

- [ ] **Step 8: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): live drag-and-drop reorder for panels and pinned cards"
```

---

### Task 7: Docs + full verification

**Files:**
- Modify: `docs/local-ui-customizations.md`
- Verify only: `.NET` contract tests + Release build.

- [ ] **Step 1: Document Tier 3** — in `docs/local-ui-customizations.md`, under the dashboard customization section, add:

```markdown
- **Inline pin/hide**: hovering (or keyboard-focusing) a hero card, pinned card, or panel row
  reveals compact pin and hide controls. Pin mirrors the drawer's Cards tab; hide adds the sensor
  to the browser-local hidden list (reversible from the drawer's Hidden tab). Raw endpoints are
  unaffected.
- **Live drag reorder**: a drag grip on panel headers and pinned cards reorders panels and pinned
  cards directly on the page; the CSS-column masonry reflows on drop and the order persists in
  `sq.dashboard.v1`. Keyboard users reorder with the drawer's Up/Down buttons. Polling is
  suppressed for the duration of a drag.
- **Consolidated state**: theme, poll rate, paused, and per-panel collapse now persist inside the
  single versioned `sq.dashboard.v1` object; legacy `sq.theme`/`sq.rate`/`sq.paused`/`sq.panel.*`
  keys are migrated into it once on load and then removed.
```

- [ ] **Step 2: Data-contract tests** (proves `data.json`/CSV untouched)

Run: `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
Expected: 0 failures.

- [ ] **Step 3: Release build** (embedded assets resolve). If the Release output dir is locked by a running monitor, either stop it or redirect with `-p:BaseOutputPath=<temp>`.

Run: `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`
Expected: 0 errors.

- [ ] **Step 4: Final self-test** — `node webtests/selftest.node.js` → `SELFTEST PASS 60/60`.

- [ ] **Step 5: Commit**

```bash
git add docs/local-ui-customizations.md
git commit -m "docs(web): document Tier 3 inline controls, live drag, consolidated state"
```

---

## Notes for the implementer

- `webtests/console.tests.js` is the single source of test assertions; append Tier 3 cases there (before the `// === Tier 3 cases` marker) and they run in both the browser page and `node webtests/selftest.node.js`.
- Line numbers reference baseline `095bd69` and drift as tasks land — match on surrounding code, not line numbers alone.
- `clampRate`, `cleanCollapsedMap`, `cleanStringList`, and the bootstrap are all inside the one file-level IIFE, so bootstrap code may call the module-private helpers directly.
- Do not introduce hyphenated asset filenames; do not touch `HttpServer.cs`, `data.json` generation, or `AssemblyVersion`.
```

