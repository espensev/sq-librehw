# Web Dashboard Card Truth & Card-First Controls — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** honest gauge ranges (override > limit > band > labeled estimate), fan gauges from paired Control %, clean multi-GPU/duplicate-hardware identity, card-carried detail/actions replacing the Customize drawer, movable rows and network subgroups, and the header overlap fix.

**Architecture:** all changes live in the client console (`Resources/Web/console.js` model + boot, `console.css`, `index.html`) plus `webtests/`. Model logic is added as pure `SQ.*` functions tested by the Node self-test; DOM wiring follows the existing render/delegate patterns. No server, no `data.json` change, golden tests must stay green untouched (phases A–D).

**Tech Stack:** vanilla JS (no framework/build step), embedded resources, Node for self-tests, .NET 10 build for smoke.

**Spec:** [`docs/feature-web-dashboard-card-truth.md`](../../feature-web-dashboard-card-truth.md)

## Global Constraints

- Phases A–D touch ONLY: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`, `console.css`, `index.html`, `webtests/*`, `docs/*`. Nothing under `LibreHardwareMonitorLib/` or `Utilities/`.
- `data.json` contract untouched: `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` must pass with NO golden regen in A–D.
- Web asset filenames must not contain hyphens (`ServeResourceFileAsync` constraint).
- After every task: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` and `node webtests\selftest.node.js` → `SELFTEST PASS n/n` (n grows; 0 FAIL).
- `sq.dashboard.v1` stays `version: 1`; new fields are additive and cleaned by a `cleanX` normalizer like the existing ones.
- Keyboard reorder must exist at all times: the drawer may only be deleted (Task C5) after expansion ▲▼ buttons work (C1).
- Do NOT kill the operator's running LibreHardwareMonitor instance for builds — build to a temp output directory (`-o`) when the default output is locked; live relaunch is an operator step.
- Commits: `feat(web): …` / `fix(web): …` / `docs(web): …`, one per task.
- Phase A+B commit to `master` — the trunk for this work since the concurrent 2026-07-04 merge `2128e33` (the former `feature/web-dashboard-customization` branch was merged in and deleted). Phase C on branch `feat/web-card-first`, Phase D on branch `feat/web-row-subgroup-order`, both cut from master after B2. Phase E does not exist in this plan (parked pending operator go — needs its own spec+plan because it regenerates the data.json golden).

---

## Phase A — true ranges (trunk)

### Task A0: Preflight — confirm live symptoms on a current build

**Files:** none (verification only)

- [ ] **Step 1:** `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 -o %TEMP%\lhm-verify` (temp output; the default may be locked by the running app). Expected: 0 errors.
- [ ] **Step 2:** `node webtests\selftest.node.js` → note the current baseline count (last known `SELFTEST PASS 32/32`; later commits may have raised it — record actual).
- [ ] **Step 3:** With the operator's running instance (or a temp-run on another port if the operator declines a restart), verify on `http://localhost:8085/`: (a) GPU/CPU power hero shows an unlabeled `/ 200`-style ceiling, (b) fan cards arc against an RPM ceiling, (c) the pin/hide cluster can paint over the state chip (hover a card with an OK chip; or narrow window). Record which still reproduce in the spec §11 log.
- [ ] **Step 4:** Commit any log-only doc update: `git commit -m "docs(web): card-truth preflight — symptom verification log"`

### Task A1: Dashboard state — `rangeOverrides` + `observedMax` (+ later-phase keys)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (state section, ~lines 48–115)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: state fields `rangeOverrides {id:{max:number,min?:number}}`, `observedMax {id:number}`, `rowOrder {groupKey:[ids]}`, `netAdapterOrder [string]`, `hiddenNetAdapters [string]` on `SQ.defaultDashboardState()` / `SQ.normalizeDashboardState()`.

- [ ] **Step 1: failing tests** — append to `webtests/console.tests.js` before the `return`:

```js
// --- v3: range/override/order schema ---
eq('normalize rangeOverrides', S.normalizeDashboardState({rangeOverrides:{'/a':{max:575},'/b':{max:-1},'/c':{max:200,min:50},'':{max:5},'/d':'x'}}).rangeOverrides,
  {'/a':{max:575},'/c':{max:200,min:50}});
eq('normalize observedMax', S.normalizeDashboardState({observedMax:{'/a':150.9,'/b':'nope'}}).observedMax, {'/a':150.9});
eq('normalize rowOrder', S.normalizeDashboardState({rowOrder:{'k|Fan':['/f1','/f2'],'bad':[],7:'x'}}).rowOrder, {'k|Fan':['/f1','/f2']});
eq('normalize net lists', (() => { const d = S.normalizeDashboardState({netAdapterOrder:['/nic/a','/nic/a'], hiddenNetAdapters:['/nic/b']});
  return [d.netAdapterOrder, d.hiddenNetAdapters]; })(), [['/nic/a'], ['/nic/b']]);
eq('default has v3 fields', (() => { const d = S.defaultDashboardState();
  return [d.rangeOverrides, d.observedMax, d.rowOrder, d.netAdapterOrder, d.hiddenNetAdapters]; })(), [{}, {}, {}, [], []]);
```

- [ ] **Step 2:** `node webtests\selftest.node.js` → FAIL (fields undefined).
- [ ] **Step 3: implement** — in `console.js` add next to the other cleaners:

```js
function cleanRangeOverrides(value) {
  const out = {};
  if (value && typeof value === 'object' && !Array.isArray(value))
    Object.keys(value).forEach(k => {
      const v = value[k];
      if (!k || !v || typeof v !== 'object') return;
      const max = Number(v.max), min = Number(v.min);
      if (!Number.isFinite(max) || max <= 0) return;
      const o = { max };
      if (Number.isFinite(min) && min < max) o.min = min;
      out[k] = o;
    });
  return out;
}
function cleanNumberMap(value) {
  const out = {};
  if (value && typeof value === 'object' && !Array.isArray(value))
    Object.keys(value).forEach(k => { const n = Number(value[k]); if (k && Number.isFinite(n)) out[k] = n; });
  return out;
}
function cleanOrderMap(value) {
  const out = {};
  if (value && typeof value === 'object' && !Array.isArray(value))
    Object.keys(value).forEach(k => { const l = cleanStringList(value[k]); if (k && typeof k === 'string' && l.length) out[k] = l; });
  return out;
}
```

Extend `defaultDashboardState` return with `rangeOverrides: {}, observedMax: {}, rowOrder: {}, netAdapterOrder: [], hiddenNetAdapters: []` and `normalizeDashboardState` with `rangeOverrides: cleanRangeOverrides(value.rangeOverrides), observedMax: cleanNumberMap(value.observedMax), rowOrder: cleanOrderMap(value.rowOrder), netAdapterOrder: cleanStringList(value.netAdapterOrder), hiddenNetAdapters: cleanStringList(value.hiddenNetAdapters)`.

- [ ] **Step 4:** self-test → PASS.
- [ ] **Step 5:** `git add … && git commit -m "feat(web): v3 dashboard state - range overrides, observed peaks, row/net order maps"`

### Task A2: `SQ.rangeFor` — one resolver with provenance

**Files:**
- Modify: `console.js` (replace `SQ.speedoRange` internals ~line 342; keep `visualRangeForSensor` as-is)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.rangeFor(s, limits, state) → {lo:number, hi:number, source:'override'|'limit'|'band'|'peak', derived?:true} | null`; `SQ.derivedPowerLimit(hwid) → number|null` (stub returning null until A5); `SQ.speedoRange` becomes a thin `[lo,hi]` wrapper (existing tests keep passing).
- Consumes: A1 state fields.

- [ ] **Step 1: failing tests**

```js
// --- v3: rangeFor provenance ---
S.resetSensorMotion();
eq('rangeFor override wins', S.rangeFor({id:'/p', type:'Power', raw:80, rawMax:122}, {}, {rangeOverrides:{'/p':{max:575}}}),
  {lo:0, hi:575, source:'override'});
eq('rangeFor band for temp', S.rangeFor({cls:'cpu', type:'Temperature', text:'Tctl', raw:60, id:'/t'}, {}, {}),
  {lo:30, hi:95, source:'band'});
eq('rangeFor peak est', S.rangeFor({id:'/p2', type:'Power', raw:87, rawMax:122}, {}, {}), {lo:0, hi:200, source:'peak'});
eq('rangeFor honors persisted peak', S.rangeFor({id:'/p3', type:'Power', raw:10, rawMax:12}, {}, {observedMax:{'/p3':480}}),
  {lo:0, hi:500, source:'peak'});
eq('rangeFor null for voltage', S.rangeFor({id:'/v', type:'Voltage', raw:1.02}, {}, {}), null);
eq('speedoRange still [lo,hi]', S.speedoRange({type:'Power', raw:87, rawMax:null, id:'/p4'}, {}), [0, 100]);
```

- [ ] **Step 2:** self-test → FAIL (`rangeFor` undefined).
- [ ] **Step 3: implement** — replace the `SQ.speedoRange` block with:

```js
SQ.derivedPowerLimit = function () { return null; };   // implemented in Task A5
SQ.rangeFor = function (s, limits, state) {
  if (!s) return null;
  const cfg = SQ.normalizeDashboardState(state);
  const ov = cfg.rangeOverrides[s.id];
  if (ov) return { lo: ov.min ?? 0, hi: ov.max, source: 'override' };
  if (s.type === 'Power' && /^GPU Package/i.test(s.text || '')) {
    const d = SQ.derivedPowerLimit(s.hwid);
    if (d) return { lo: 0, hi: d, source: 'limit', derived: true };
  }
  const band = SQ.visualRangeForSensor(s, limits || {});
  if (band) return { lo: band[0], hi: band[1], source: 'band' };
  if (s.type !== 'Fan' && s.type !== 'Power' && s.type !== 'Clock') return null;
  const motion = SENSOR_MOTION.get(s.id);
  const peak = Math.max(s.rawMax ?? 0, motion ? motion.max : 0, s.raw ?? 0, cfg.observedMax[s.id] ?? 0);
  const hi = SQ.niceCeil(peak);
  return hi ? { lo: 0, hi, source: 'peak' } : null;
};
SQ.speedoRange = function (s, limits) {
  const r = SQ.rangeFor(s, limits, undefined);
  return r ? [r.lo, r.hi] : null;
};
```

In `cardEl`, replace `const range = h.bounded || SQ.speedoRange(h.s, {});` with:

```js
const rr = h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                     : SQ.rangeFor(h.s, {}, state.dashboard);
const range = rr ? [rr.lo, rr.hi] : null;
```

(`ceil` markup keeps using `range[1]` for now; A4 switches it to `rr.source`-aware rendering.)

- [ ] **Step 4:** self-test → PASS (including the four pre-existing `speedoRange` cases).
- [ ] **Step 5:** `git commit -m "feat(web): SQ.rangeFor - range provenance resolver (override>limit>band>peak)"`

### Task A3: Fan cards — arc from paired Control %, RPM as the number

**Files:**
- Modify: `console.js` (`SQ.fanControlFor` in model; `cardEl`, `rowEl` in boot)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.fanControlFor(fan, sensors) → control sensor | null` (pairs on same `hwid` + identical `text`).
- Consumes: `SQ.rangeFor` (fans with a pair bypass it for the arc).

- [ ] **Step 1: failing tests**

```js
// --- v3: fan/control pairing ---
const fanPairSensors = [
  {hwid:'/lpc/nct6701d/0', type:'Fan',     text:'Fan #2', raw:642,  id:'/lpc/nct6701d/0/fan/1'},
  {hwid:'/lpc/nct6701d/0', type:'Control', text:'Fan #2', raw:29.8, id:'/lpc/nct6701d/0/control/1'},
  {hwid:'/gpu-nvidia/0',   type:'Control', text:'Fan #2', raw:50,   id:'/gpu-nvidia/0/control/9'}
];
eq('fanControlFor pairs hwid+text', S.fanControlFor(fanPairSensors[0], fanPairSensors)?.id, '/lpc/nct6701d/0/control/1');
eq('fanControlFor null when unpaired', S.fanControlFor({hwid:'/z', type:'Fan', text:'Pump', raw:100, id:'/z/fan/0'}, fanPairSensors), null);
eq('fanControlFor null for non-fan', S.fanControlFor(fanPairSensors[1], fanPairSensors), null);
eq('fanControlFor live fixture', (() => { const f = sensors.find(s => s.id === '/lpc/nct6701d/0/fan/1');
  return f ? S.fanControlFor(f, sensors)?.id : '/lpc/nct6701d/0/control/1'; })(), '/lpc/nct6701d/0/control/1');
```

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement** — model:

```js
SQ.fanControlFor = function (fan, sensors) {
  if (!fan || fan.type !== 'Fan' || !Array.isArray(sensors)) return null;
  return sensors.find(s => s.type === 'Control' && s.hwid === fan.hwid && s.text === fan.text && s.raw != null) || null;
};
```

`cardEl` — after computing `kind`, before the arc:

```js
const ctrl = kind === 'fan' ? SQ.fanControlFor(h.s, state.allSensors) : null;
```

then arc/ceil selection becomes: if `ctrl` → `arc = arcSVG(h.s.id, ctrl.raw / 100)` and no `ceil`; the meta line appends `<span class="cmd">cmd ${esc(ctrl.value)}</span>` after the trend span. Fans without `ctrl` keep the A2 path (peak-est arc).
`rowEl` — for `s.type === 'Fan'`, look up `SQ.fanControlFor(s, state.allSensors)` and render the value cell as `640 RPM · 29.8 %` when paired.

- [ ] **Step 4:** self-test → PASS. Manual: fixture page `webtests/console.test.html` still loads.
- [ ] **Step 5:** `git commit -m "feat(web): fan cards - arc from paired Control %, RPM stays the number"`

### Task A4: Persisted peaks + honest `≈` ceilings

**Files:**
- Modify: `console.js` (render loop + `cardEl` ceil markup), `console.css`

**Interfaces:**
- Consumes: `observedMax` (A1), `rr.source` (A2).
- Produces: est ceilings render `/ ≈ N` with tooltip; peaks persist (throttled ≥30 s, ≥2 % growth).

- [ ] **Step 1: failing test** (model-level: peak merge helper)

```js
eq('mergeObservedPeaks ratchets up only', (() => {
  const st = S.normalizeDashboardState({observedMax:{'/p':100}});
  const changed = S.mergeObservedPeaks(st, [{id:'/p', type:'Power', raw:90, rawMax:150.9}, {id:'/t', type:'Temperature', raw:60}]);
  return [changed, st.observedMax['/p'], st.observedMax['/t']];
})(), [true, 150.9, undefined]);
eq('mergeObservedPeaks no-op within 2%', (() => {
  const st = S.normalizeDashboardState({observedMax:{'/p':150}});
  return S.mergeObservedPeaks(st, [{id:'/p', type:'Power', raw:151, rawMax:151}]);
})(), false);
```

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement**

```js
SQ.mergeObservedPeaks = function (state, sensors) {
  let changed = false;
  sensors.forEach(s => {
    if (s.type !== 'Power' && s.type !== 'Clock' && s.type !== 'Fan') return;
    const motion = SENSOR_MOTION.get(s.id);
    const peak = Math.max(s.rawMax ?? 0, motion ? motion.max : 0, s.raw ?? 0);
    if (!Number.isFinite(peak) || peak <= 0) return;
    if (peak > (state.observedMax[s.id] ?? 0) * 1.02) { state.observedMax[s.id] = Math.round(peak * 10) / 10; changed = true; }
  });
  return changed;
};
```

Boot: add `let lastPeakSave = 0;` and in `render()` after `trackSensorHistory`: `if (SQ.mergeObservedPeaks(state.dashboard, allSensors) && Date.now() - lastPeakSave > 30000) { lastPeakSave = Date.now(); saveDashboard(); }`.
`cardEl` ceil markup (replaces the A2 interim):

```js
const ceil = !fx.arc || !rr || rr.source === 'band' ? '' :
  rr.source === 'peak'
    ? `<span class="ceil est" title="Ceiling estimated from the highest value this browser has observed. Set a true max from the card detail.">/ &asymp; ${esc(String(rr.hi))}</span>`
    : `<span class="ceil" title="${rr.derived ? 'Limit derived from the sensor\'s %-of-limit reading' : 'User-set maximum'}">/ ${rr.derived ? '&asymp; ' : ''}${esc(String(rr.hi))}</span>`;
```

`console.css`: `.ceil.est{font-style:italic;opacity:.85}`.

- [ ] **Step 4:** self-test → PASS. Manual fixture check: power card ceiling shows `/ ≈ …`.
- [ ] **Step 5:** `git commit -m "feat(web): persisted observed peaks + estimated ceilings labeled with ≈"`

### Task A5: Derived NVIDIA power limit (client-only, no hardcoding)

**Files:**
- Modify: `console.js` (model), `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.trackPowerLimits(sensors)` (call each render), real `SQ.derivedPowerLimit(hwid) → W|null` replacing the A2 stub; `SQ.resetPowerLimits()` for tests.
- Grounding: live 5090 exposes `Powers/GPU Package` (W) and `Load/GPU Power` (% of limit) — 81.2 W @ 13.5 % ⇒ ≈575–600 W.

- [ ] **Step 1: failing tests**

```js
// --- v3: derived power limit ---
S.resetPowerLimits();
for (let i = 0; i < 12; i++) S.trackPowerLimits([
  {hwid:'/gpu-nvidia/0', type:'Power', text:'GPU Package', raw:300, id:'/gp'},
  {hwid:'/gpu-nvidia/0', type:'Load',  text:'GPU Power',   raw:52.17, id:'/gl'}]);
eq('derived limit ~575 (25W rounding)', S.derivedPowerLimit('/gpu-nvidia/0'), 575);
S.resetPowerLimits();
for (let i = 0; i < 12; i++) S.trackPowerLimits([
  {hwid:'/gpu-nvidia/0', type:'Power', text:'GPU Package', raw:30, id:'/gp'},
  {hwid:'/gpu-nvidia/0', type:'Load',  text:'GPU Power',   raw:5, id:'/gl'}]);
eq('derived limit gated below 10%', S.derivedPowerLimit('/gpu-nvidia/0'), null);
S.resetPowerLimits();
S.trackPowerLimits([{hwid:'/gpu-nvidia/0', type:'Power', text:'GPU Package', raw:300, id:'/gp'},
  {hwid:'/gpu-nvidia/0', type:'Load', text:'GPU Power', raw:52.17, id:'/gl'}]);
eq('derived limit needs 10 samples', S.derivedPowerLimit('/gpu-nvidia/0'), null);
S.resetPowerLimits();
eq('rangeFor uses derived limit', (() => {
  for (let i = 0; i < 12; i++) S.trackPowerLimits([
    {hwid:'/gpu-nvidia/0', type:'Power', text:'GPU Package', raw:300, id:'/gp'},
    {hwid:'/gpu-nvidia/0', type:'Load',  text:'GPU Power',   raw:52.17, id:'/gl'}]);
  return S.rangeFor({hwid:'/gpu-nvidia/0', type:'Power', text:'GPU Package', raw:300, rawMax:400, id:'/gp'}, {}, {});
})(), {lo:0, hi:575, source:'limit', derived:true});
S.resetPowerLimits();
```

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement** — model:

```js
const POWER_LIMIT_SAMPLES = new Map();
SQ.resetPowerLimits = function () { POWER_LIMIT_SAMPLES.clear(); };
SQ.trackPowerLimits = function (sensors) {
  const byHw = new Map();
  sensors.forEach(s => {
    if (s.raw == null || !s.hwid) return;
    const e = byHw.get(s.hwid) || byHw.set(s.hwid, {}).get(s.hwid);
    if (s.type === 'Power' && /^GPU Package/i.test(s.text || '')) e.w = s.raw;
    if (s.type === 'Load' && /^GPU Power$/i.test(s.text || '')) e.pct = s.raw;
  });
  byHw.forEach((e, hwid) => {
    if (!(e.w > 0) || !(e.pct > 10)) return;
    const arr = POWER_LIMIT_SAMPLES.get(hwid) || [];
    arr.push(e.w / (e.pct / 100));
    if (arr.length > 60) arr.shift();
    POWER_LIMIT_SAMPLES.set(hwid, arr);
  });
};
SQ.derivedPowerLimit = function (hwid) {   // replaces the A2 stub
  const arr = POWER_LIMIT_SAMPLES.get(hwid);
  if (!arr || arr.length < 10) return null;
  const sorted = arr.slice().sort((a, b) => a - b);
  return Math.round(sorted[Math.floor(sorted.length / 2)] / 25) * 25;
};
```

Boot `render()`: add `SQ.trackPowerLimits(allSensors);` right after `SQ.trackSensorHistory(allSensors);`.

- [ ] **Step 4:** self-test → PASS.
- [ ] **Step 5:** `git commit -m "feat(web): derive NVIDIA power limit from W ÷ %-of-limit pair (median, ≈-labeled)"`

**Phase A gate:** `dotnet build … -f net10.0-windows -p:Platform=x64` (temp `-o` if locked) → 0 errors; `dotnet test … -p:Platform=x64` → all pass, golden untouched. Live check (operator relaunch): 5090 power ceiling ≈575–600 labeled `≈`, fans arc on %, CPU power ceiling `≈ 200` italic.

---

## Phase B — multi-device identity (trunk)

### Task B1: Panels keyed by HardwareId; duplicate names disambiguated

**Files:**
- Modify: `console.js` (extract grouping to model `SQ.groupPanels`; boot `buildPanelItems` delegates; collapse-key migration)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.groupPanels(sensors) → [{hw, ss, key, collapsed, index}]` — `key` = `hwid` (or `'hw:'+text` fallback); duplicate display texts become `Name #1`, `Name #2` in tree order; the Network aggregate keeps `key:'panel:network'`.
- Consumes: nothing new; `applyOrder`/`panelOrder` unchanged (keys change value → one-time migration below).

- [ ] **Step 1: failing tests**

```js
// --- v3: hwid panel identity ---
const dupPanels = S.groupPanels([
  {hw:'KINGSTON SKC3000D2048G', hwid:'/nvme/0', cls:'nvme', type:'Temperature', text:'Temperature', raw:51, id:'/nvme/0/temperature/0'},
  {hw:'KINGSTON SKC3000D2048G', hwid:'/nvme/1', cls:'nvme', type:'Temperature', text:'Temperature', raw:44, id:'/nvme/1/temperature/0'},
  {hw:'AMD Ryzen 9 9950X3D', hwid:'/amdcpu/0', cls:'cpu', type:'Load', text:'CPU Total', raw:12, id:'/amdcpu/0/load/0'}
]);
eq('dup names suffixed, keyed by hwid', dupPanels.map(p => [p.hw, p.key]),
  [['AMD Ryzen 9 9950X3D', '/amdcpu/0'], ['KINGSTON SKC3000D2048G #1', '/nvme/0'], ['KINGSTON SKC3000D2048G #2', '/nvme/1']]);
eq('fixture drives split into distinct panels', (() => {
  const nv = S.groupPanels(sensors).filter(p => p.key.startsWith('/nvme/'));
  return [nv.length, new Set(nv.map(p => p.hw)).size];
})(), [3, 3]);
```

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement** — move the body of boot `buildPanelItems` into the model as `SQ.groupPanels(sensors)` with grouping changed to:

```js
SQ.groupPanels = function (sensors) {
  const byKey = new Map();
  sensors.forEach(s => {
    if (s.cls === 'nic') return;
    const key = s.hwid || ('hw:' + s.hw);
    (byKey.get(key) || byKey.set(key, []).get(key)).push(s);
  });
  const order = ['cpu','gpu','igpu','mem','dimm','nvme','disk','mb','other'];
  const entries = [...byKey.entries()].map(([key, ss], index) => ({ key, ss, index }))
    .sort((a, b) => {
      const ai = order.indexOf(a.ss[0].cls), bi = order.indexOf(b.ss[0].cls);
      return (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.index - b.index;
    });
  const totals = new Map();
  entries.forEach(e => totals.set(e.ss[0].hw, (totals.get(e.ss[0].hw) || 0) + 1));
  const seen = new Map();
  const items = entries.map((e, index) => {
    const base = e.ss[0].hw;
    const n = (seen.get(base) || 0) + 1; seen.set(base, n);
    return { hw: totals.get(base) > 1 ? `${base} #${n}` : base, ss: e.ss, key: e.key, collapsed: false, index };
  });
  const nics = sensors.filter(s => s.cls === 'nic');
  const active = new Set(nics.filter(s => s.type === 'Throughput' && s.raw > 0).map(s => s.hw));
  const net = nics.filter(s => active.has(s.hw));
  if (net.length) items.push({ hw: 'Network', ss: net, key: 'panel:network', collapsed: true, index: items.length });
  return items;
};
```

Boot: `state.panelItems = SQ.groupPanels(sensors);`. Collapse persistence in `panelEl`: read `SQ.isPanelCollapsed(state.dashboard, item.key, collapsed)` and, when `collapsedPanels[item.key]` is absent, fall back once to the legacy text key (`state.dashboard.collapsedPanels[hwBaseText]`); the toggle handler writes `collapsedPanels[item.key]` (pass `item.key` into `panelEl`, keep the display `hw` for the label only). Delete the now-unused boot `buildPanelItems` and `SQ.panelKey` call sites if fully superseded (keep `SQ.panelKey` exported — tests may reference it; verify with a grep before removing).

- [ ] **Step 4:** self-test → PASS (`apply panel order` legacy test unaffected).
- [ ] **Step 5:** `git commit -m "feat(web): panels keyed by HardwareId - duplicate hardware names get #n suffixes"`

### Task B2: Heroes per GPU device + short device labels + iGPU pair + cap 14

**Files:**
- Modify: `console.js` (`SQ.deviceShortName`, `pickHero`), `webtests/console.tests.js`, hero-cap wording doc (locate with `git show 749f386 --name-only`)

**Interfaces:**
- Produces: `SQ.deviceShortName(text) → string` ("NVIDIA GeForce RTX 5090"→"RTX 5090", "AMD Radeon(TM) Graphics"→"Radeon"); `pickHero` iterates distinct gpu/igpu `hwid`s, prefixes labels when >1 GPU device, caps at 14 (fans added last → trimmed first).

- [ ] **Step 1: failing tests**

```js
// --- v3: per-device heroes ---
eq('deviceShortName rtx', S.deviceShortName('NVIDIA GeForce RTX 5090'), 'RTX 5090');
eq('deviceShortName radeon', S.deviceShortName('AMD Radeon(TM) Graphics'), 'Radeon');
const twoGpuHero = S.pickHero([
  {hw:'NVIDIA GeForce RTX 5090', hwid:'/gpu-nvidia/0', cls:'gpu', type:'Temperature', text:'GPU Core', raw:40, value:'40.0 °C', id:'/g0/t'},
  {hw:'NVIDIA GeForce RTX 5090', hwid:'/gpu-nvidia/1', cls:'gpu', type:'Temperature', text:'GPU Core', raw:45, value:'45.0 °C', id:'/g1/t'},
  {hw:'AMD Radeon(TM) Graphics', hwid:'/gpu-amd/0', cls:'igpu', type:'Temperature', text:'GPU VR SoC', raw:48, value:'48.0 °C', id:'/ig/t'},
  {hw:'AMD Radeon(TM) Graphics', hwid:'/gpu-amd/0', cls:'igpu', type:'Power', text:'GPU Core', raw:35, value:'35.0 W', id:'/ig/p'}
], {});
eq('two dGPUs both surface', twoGpuHero.filter(h => /Temp$/.test(h.label)).map(h => h.s.id), ['/g0/t', '/g1/t', '/ig/t']);
eq('labels carry device names', twoGpuHero.map(h => h.label).slice(0, 3), ['RTX 5090 Temp', 'RTX 5090 Temp', 'Radeon Temp']);
eq('igpu power hero present', twoGpuHero.some(h => h.label === 'Radeon Power'), true);
```

(Note: two identical dGPUs both shorten to "RTX 5090" — acceptable; the card source line + panel `#n` disambiguate. Do not over-engineer.)

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement**

```js
SQ.deviceShortName = function (text) {
  const t = String(text || '').trim();
  const m = t.match(/\b(RTX|GTX|RX|Arc)\s*[\w-]+/i);
  if (m) return m[0].replace(/\s+/g, ' ');
  if (/radeon/i.test(t)) return 'Radeon';
  if (/intel/i.test(t)) return 'Intel GPU';
  return t.split(/\s+/).slice(-2).join(' ') || t;
};
```

In `pickHero`, replace the single `cls === 'gpu'` block with per-device iteration (discrete then integrated), exactly:

```js
const gpuIds = [...new Set(sensors.filter(s => s.cls === 'gpu').map(s => s.hwid))];
const igpuIds = [...new Set(sensors.filter(s => s.cls === 'igpu').map(s => s.hwid))];
const multiGpu = gpuIds.length + igpuIds.length > 1;
gpuIds.forEach(hwid => {
  const g = sensors.filter(s => s.hwid === hwid);
  const nm = multiGpu ? SQ.deviceShortName(g[0].hw) + ' ' : 'GPU ';
  add(g.find(s => s.type === 'Temperature' && /^GPU Core/i.test(s.text)), nm + 'Temp', { bounded: [25, 92], unit: '°C' });
  add(g.find(s => s.type === 'Temperature' && /Junction/i.test(s.text)), nm + 'Mem Jct', { bounded: [25, 105], unit: '°C' });
  add(g.find(s => s.type === 'Load' && /^GPU Core/i.test(s.text)), nm + 'Load', { bounded: [0, 100], unit: '%' });
  add(g.find(s => s.type === 'Power' && /Package/i.test(s.text)), nm + 'Power', { unit: 'W' });
});
igpuIds.forEach(hwid => {
  const g = sensors.filter(s => s.hwid === hwid);
  const nm = multiGpu ? SQ.deviceShortName(g[0].hw) + ' ' : 'iGPU ';
  const temps = g.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a, b) => b.raw - a.raw);
  add(temps[0], nm + 'Temp', { bounded: [25, 92], unit: '°C' });
  add(g.find(s => s.type === 'Power' && /^GPU Core/i.test(s.text)), nm + 'Power', { unit: 'W' });
});
```

Change the final `return H.slice(0, 12);` to `return H.slice(0, 14);` and update the hero-cap sentence in the doc that `749f386` edited (now "11 base heroes + up to 4 fans, capped at 14, fans trimmed first").

- [ ] **Step 4:** self-test → PASS (existing hero tests: `hero has CPU Temp`, fan tests — confirm intact; fixture single-dGPU labels become `RTX 5090 …`/`Radeon …` because the fixture has both devices — update the two existing label-based assertions if they assumed `GPU Temp`).
- [ ] **Step 5:** `git commit -m "feat(web): per-device GPU heroes with short names; iGPU temp+power; hero cap 14"`

**Phase B gate:** same build/test/live gate as Phase A. Live: both GPUs visible as distinct heroes + panels; three NVMe panels.

---

## Phase C — card-first controls (branch `feat/web-card-first`)

Cut: `git checkout -b feat/web-card-first` from trunk after B2.

### Task C1: Card expansion — detail + actions on the card

**Files:**
- Modify: `console.js` (boot: `detailEl`, `cardEl` click-to-expand, delegated actions incl. `override-max`), `console.css`

**Interfaces:**
- Produces: clicking a card toggles `.cell-detail` showing: sensor id, hardware, type, now/min/max, gauge-range provenance line, style select, max-override input (+ clear), rename (pinned), hide/pin, ▲▼ move (pinned). Transient `state.openDetails = new Set()`.
- Consumes: `SQ.rangeFor` provenance (A2/A4), `rangeOverrides` (A1).

- [ ] **Step 1:** add `openDetails: new Set()` to boot `state`; in `rerender()` add a focus guard: `if (document.activeElement && document.activeElement.closest && document.activeElement.closest('.cell-detail')) return;`.
- [ ] **Step 2: implement `detailEl`** (boot, after `cardEl`):

```js
function detailEl(s, opts) {
  const rr = opts.range;
  const rangeLine = !rr ? 'no known range'
    : `${rr.lo} – ${rr.hi} · ${rr.source === 'peak' ? 'estimated from observed peak'
        : rr.derived ? 'derived limit' : rr.source === 'override' ? 'user override' : rr.source}`;
  const ov = state.dashboard.rangeOverrides[s.id];
  const d = document.createElement('div');
  d.className = 'cell-detail';
  d.innerHTML =
    `<div class="detail-facts"><code>${esc(s.id)}</code>
      <span>${esc(s.hw)} · ${esc(s.type)}</span>
      <span>now ${esc(s.value ?? '—')} · min ${esc(s.min ?? '—')} · max ${esc(s.max ?? '—')}</span>
      <span>gauge: ${esc(rangeLine)}</span></div>
     <div class="detail-controls">
      <label>style <select data-action="style" data-id="${esc(s.id)}">${['auto','gauge','number','graph'].map(v =>
        `<option value="${v}"${(state.dashboard.cardStyle[s.id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}</select></label>
      <label>max <input type="number" step="any" inputmode="decimal" data-action="override-max" data-id="${esc(s.id)}"
        value="${ov ? esc(String(ov.max)) : ''}" placeholder="auto"></label>
      ${opts.pinned ? `<label>name <input class="title-input" data-action="rename" data-id="${esc(s.id)}"
          value="${esc(opts.title || '')}" placeholder="${esc(s.text)}"></label>
        <button class="iconbtn" data-action="pin-up" data-id="${esc(s.id)}" aria-label="Move earlier">&#9650;</button>
        <button class="iconbtn" data-action="pin-down" data-id="${esc(s.id)}" aria-label="Move later">&#9660;</button>` : ''}
      ${ctlCluster(s.id, s.text, { hide: !opts.pinned })}
     </div>`;
  return d;
}
```

- [ ] **Step 3:** in `cardEl`, after building `cell`: if `state.openDetails.has(h.s.id)` → `cell.classList.add('open'); cell.appendChild(detailEl(h.s, { pinned, title: …pinned card title…, range: rr }));` and add the toggle listener:

```js
cell.addEventListener('click', e => {
  if (e.target.closest('.ctl,.grip,.cell-detail,button,input,select,a')) return;
  state.openDetails.has(h.s.id) ? state.openDetails.delete(h.s.id) : state.openDetails.add(h.s.id);
  rerender();
});
```

Move the drawer's `change` handling for `rename`/`style` to a `document`-level delegated listener (works for both drawer and detail while both exist), and add:

```js
if (e.target.matches('[data-action="override-max"]')) {
  const id = e.target.dataset.id, n = Number(e.target.value);
  if (e.target.value === '' || !Number.isFinite(n) || n <= 0) delete state.dashboard.rangeOverrides[id];
  else state.dashboard.rangeOverrides[id] = { max: n };
  commitDashboard();
}
```

`pin-up`/`pin-down` clicks are already handled by the drawer listener — move that `switch` to the same document-level delegate so detail buttons share it.
- [ ] **Step 4:** CSS: `.cell.open{height:auto}` (v2 fixed heights must yield), `.cell-detail{margin-top:8px;border-top:1px solid var(--line-soft);padding-top:8px;font:11px var(--mono);display:flex;flex-direction:column;gap:6px}`, `.detail-controls{display:flex;flex-wrap:wrap;gap:8px;align-items:center}`.
- [ ] **Step 5:** self-test (unchanged model) + manual fixture: open card, set max 575 on GPU power → arc ceiling flips to `/ 575` plain; clear → back to `≈`. Reload keeps it.
- [ ] **Step 6:** `git commit -m "feat(web): card expansion - detail, provenance, style, rename, max override, move on the card"`

### Task C2: Row expansion parity

**Files:** `console.js` (`rowEl`), `console.css`

- [ ] **Step 1:** `rowEl` gains the same toggle: clicking the row (not `.ctl`/buttons) toggles `state.openDetails` and, when open, appends `detailEl(s, { pinned:false, range: SQ.rangeFor(s, {}, state.dashboard) })` after the row inside a `.row-detail` wrapper (`display:block`, spans full width, same inner layout, class on the row `open`).
- [ ] **Step 2:** CSS `.row.open{background:var(--panel-2)}.row-detail{padding:6px 12px 8px;border-bottom:1px solid var(--line-soft)}`.
- [ ] **Step 3:** manual fixture check (keyboard: row focusable `tabindex="0"`, Enter toggles — add `keydown` Enter/Space handler alongside click).
- [ ] **Step 4:** `git commit -m "feat(web): row expansion with same detail/actions as cards"`

### Task C3: Masthead "Sensors — N hidden" popover (search / restore / pin anything)

**Files:** `index.html`, `console.js`, `console.css`

**Interfaces:**
- Produces: `#sensorsBtn` in `.controls` + anchored `#sensorsPop` panel: search input over ALL sensors (incl. hidden/suppressed/idle-NIC), each row shows Hide/Show + Pin/Unpin, plus `Reset hidden`. This is the only list UI that survives C5.

- [ ] **Step 1:** `index.html`: before the Customize button add `<button class="btn" id="sensorsBtn" aria-haspopup="true" aria-expanded="false">Sensors</button>` and after `</header>` add `<div class="pop" id="sensorsPop" hidden><div class="drawer-tools"><input id="popSearch" type="search" placeholder="Search all sensors"><button class="iconbtn" data-action="reset-hidden">Reset hidden</button></div><div class="sensor-list" id="popList"></div></div>`.
- [ ] **Step 2:** `console.js`: extend `renderSensorRows(container, filter, mode)` with mode `'all'` that renders BOTH the pin/unpin and hide/show buttons per row (reuse the existing per-mode button builders side by side). Boot state gains `popOpen:false, popFilter:''`; `#sensorsBtn.onclick` toggles, button text painted each render: `` `Sensors${hidden ? ` — ${hidden} hidden` : ''}` `` where `hidden = state.allSensors.filter(s => SQ.isSensorHidden(s, state.dashboard)).length``. Outside-click + Escape close it.
- [ ] **Step 3:** CSS: `.pop{position:fixed;top:64px;right:18px;width:min(420px,92vw);max-height:70vh;overflow:auto;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px;z-index:40;box-shadow:0 12px 40px rgba(0,0,0,.45)}`.
- [ ] **Step 4:** manual: hide a sensor from a row → count ticks up; restore from popover; pin an idle NIC sensor from the popover → pinned card appears.
- [ ] **Step 5:** `git commit -m "feat(web): masthead sensors popover - search all, restore hidden, pin anything"`

### Task C4: Header layout — kill the chip/icon/controls overlap

**Files:** `console.js` (`cardEl` markup order), `console.css` (`.k`, `.k2`, `.cell-ctl`, `.row-ctl`)

- [ ] **Step 1:** `cardEl`: render the control cluster inside the `.k2` row as its last child (in-flow), grip included for pinned:
   `.k` = name + chip (grid `1fr auto`, name ellipsizes); `.k2` = src + spacer + ticon + ctl cluster.
- [ ] **Step 2:** CSS replace the absolute rules:

```css
.cell .k{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:8px}
.cell .k .name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.cell .k2{display:flex;align-items:center;gap:8px}
.cell .k2 .src{margin-right:auto;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.cell-ctl{position:static;display:none;gap:4px}
.cell:hover .cell-ctl,.cell:focus-within .cell-ctl{display:flex}
@media (hover:none){.cell-ctl{display:flex}}
.row{position:relative;padding-right:64px}   /* reserved gutter so hover controls never cover the value */
```

(`.row-ctl` keeps its right-anchored position but now floats over reserved padding, not content.)
- [ ] **Step 3:** verify at 320 px, 768 px, desktop, and with DevTools touch emulation: chip, ticon, and controls never overlap; long names ellipsize.
- [ ] **Step 4:** `git commit -m "fix(web): card header grid + reserved control gutter - no chip/icon/control overlap"`

### Task C5: Delete the Customize drawer

**Files:** `index.html` (drop `<aside>`, scrim, `#customize` button), `console.js` (drop `renderCustomize`, tab state/handlers, drawer listeners, `renderPinnedEditor`, `renderLayoutEditor`, scrim/close handlers), `console.css` (drawer/tabs/order-list blocks — keep `.sensor-list`, `.sensor-choice`, `.drawer-tools`, `.iconbtn`, `.style-select`, `.title-input` which the popover/detail reuse)

- [ ] **Step 1: parity checklist first (blocker):** hide ✓ rows/cards + popover; show ✓ popover; pin/unpin ✓ everywhere; rename ✓ card detail; style ✓ card/row detail; pinned reorder ✓ drag + ▲▼ in detail; panel reorder ✓ drag (keyboard: panel-head gets ▲▼ in a mini-detail — add small `panel-up`/`panel-down` buttons next to the grip, reusing the existing switch actions); reset-hidden ✓ popover; clear-pinned + reset-panels → add both as small buttons at the bottom of the popover (`data-action` already handled).
- [ ] **Step 2:** delete the DOM/JS/CSS listed above; keep the shared classes; `panel-up/pin-up/...` switch moves intact to the document-level delegate (done in C1).
- [ ] **Step 3:** `node --check` + self-test PASS; manual keyboard-only pass: Tab to a pinned card → open detail (Enter) → ▲▼ reorders; panel head buttons reorder panels.
- [ ] **Step 4:** `git commit -m "feat(web): remove Customize drawer - cards, rows, and sensors popover carry all controls"`

**Phase C gate:** build + dotnet test green; live A/B by operator vs trunk build.

---

## Phase D — row & subgroup arranging (branch `feat/web-row-subgroup-order`, cut from trunk after B2, parallel to C)

### Task D1: Drag-reorder individual rows within their type group

**Files:**
- Modify: `console.js` (model `SQ.rowGroupKey`; `panelEl` group wrappers + ordered rows; drag machinery row mode), `console.css`
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.rowGroupKey(panelKey, displayType) → panelKey + '|' + displayType`; rows ordered by `state.rowOrder[groupKey]` via existing `applyOrder`; row grips drag within the group only; drop persists via existing `reorderByDrop`.

- [ ] **Step 1: failing tests**

```js
// --- v3: row order ---
eq('rowGroupKey', S.rowGroupKey('/nvme/0', 'Temperature'), '/nvme/0|Temperature');
eq('row order applies within group', S.applyOrder(
  [{key:'/a',index:0},{key:'/b',index:1},{key:'/c',index:2}], ['/c','/a'], x => x.key).map(x => x.key), ['/c','/a','/b']);
```

- [ ] **Step 2:** self-test → FAIL on `rowGroupKey`.
- [ ] **Step 3: implement** — model: `SQ.rowGroupKey = function (panelKey, displayType) { return panelKey + '|' + displayType; };`
  `panelEl`: wrap each type group's rows in `const wrap = document.createElement('div'); wrap.className = 'tgroup'; wrap.dataset.group = SQ.rowGroupKey(item.key, type);`; order rows first:

```js
const gk = SQ.rowGroupKey(item.key, type);
const orderedRows = SQ.applyOrder(list.map((s, i) => ({ s, key: s.id, index: i })),
  state.dashboard.rowOrder[gk] || [], x => x.key).map(x => x.s);
```

  rows get `r.dataset.key = s.id` and a leading grip button (same `.grip` class). Drag machinery: in `startDrag`, add `|| grip.closest('.row')`; set `a.mode = el.classList.contains('row') ? 'list' : (el.classList.contains('panel') ? 'panel' : 'cards')`; in `dropIndex`, `list` mode uses the vertical midpoint rule: `if (clientY < r.top + r.height / 2) return i;`. In `endDrag`, `list` mode writes `state.dashboard.rowOrder[a.container.dataset.group] = next` (container = the `.tgroup`). The per-core `extra` box keeps its own group key (`… + '|core'`) or is excluded from dragging — exclude (no grips inside `.extra`) to stay simple.
- [ ] **Step 4:** self-test PASS; manual: drag a fan row above another inside Board panel Fans group, reload → order kept; dragging cannot cross into another group.
- [ ] **Step 5:** `git commit -m "feat(web): drag-reorder individual sensor rows within their type group, persisted"`

### Task D2: Network panel — per-adapter subgroups (label, hide, reorder)

**Files:**
- Modify: `console.js` (model `SQ.nicKey` + `SQ.networkSubgroups`; `panelEl` nic branch), `console.css`
- Test: `webtests/console.tests.js`

**Interfaces:**
- Produces: `SQ.nicKey(sensorId) → '/nic/{GUID}' | null`; `SQ.networkSubgroups(ss, state) → [{key, name, rows, index}]` (hidden adapters filtered by `hiddenNetAdapters`, ordered by `netAdapterOrder`); Network panel renders one labeled subgroup per adapter with hide button + grip; an in-panel `n adapters hidden — restore` chip lists hidden ones (keeps D independent of C's popover).

- [ ] **Step 1: failing tests**

```js
// --- v3: network subgroups ---
eq('nicKey extracts prefix', S.nicKey('/nic/%7BAAA%7D/throughput/7'), '/nic/%7BAAA%7D');
eq('nicKey null for non-nic', S.nicKey('/amdcpu/0/load/0'), null);
const nicSS = [
  {hw:'Ethernet 2', id:'/nic/%7BA%7D/throughput/7', type:'Throughput', raw:100},
  {hw:'Ethernet 2', id:'/nic/%7BA%7D/load/1', type:'Load', raw:1},
  {hw:'Wi-Fi', id:'/nic/%7BB%7D/throughput/7', type:'Throughput', raw:5}
];
eq('networkSubgroups groups+orders', S.networkSubgroups(nicSS, {netAdapterOrder:['/nic/%7BB%7D']}).map(g => [g.key, g.name, g.rows.length]),
  [['/nic/%7BB%7D', 'Wi-Fi', 1], ['/nic/%7BA%7D', 'Ethernet 2', 2]]);
eq('networkSubgroups hides adapters', S.networkSubgroups(nicSS, {hiddenNetAdapters:['/nic/%7BA%7D']}).map(g => g.key), ['/nic/%7BB%7D']);
```

- [ ] **Step 2:** self-test → FAIL.
- [ ] **Step 3: implement** — model:

```js
SQ.nicKey = function (id) { const m = String(id || '').match(/^\/nic\/[^/]+/); return m ? m[0] : null; };
SQ.networkSubgroups = function (ss, state) {
  const cfg = SQ.normalizeDashboardState(state);
  const by = new Map();
  ss.forEach(s => {
    const k = SQ.nicKey(s.id);
    if (!k || cfg.hiddenNetAdapters.includes(k)) return;
    (by.get(k) || by.set(k, []).get(k)).push(s);
  });
  const groups = [...by.entries()].map(([key, rows], index) => ({ key, name: rows[0].hw, rows, index }));
  return SQ.applyOrder(groups, cfg.netAdapterOrder, g => g.key);
};
```

  `panelEl`: when `item.key === 'panel:network'`, skip the type-group loop; instead for each subgroup render `<div class="subgrp" data-key>` with a head (grip + name + hide button `data-action="nic-hide"`) and its rows (typed order inside: Load, Throughput, Data). Hidden count chip when `hiddenNetAdapters` non-empty: click renders an inline restore list (`data-action="nic-show"`). Delegate actions in the existing document-level switch: `nic-hide` pushes the key, `nic-show` removes it, both `commitDashboard()`. Drag: subgroup grips reuse the `list` mode from D1 with container `.panel-body` and commit target `netAdapterOrder`; distinguish via `el.classList.contains('subgrp')`.
- [ ] **Step 4:** self-test PASS; manual: adapters labeled, hide one, reorder others, reload persists; idle-adapter auto-filter unchanged.
- [ ] **Step 5:** `git commit -m "feat(web): network panel per-adapter subgroups - label, hide, drag order"`

**Phase D gate:** build + dotnet test green; live check on the real ~30-NIC host.

---

## Phase E — server-side limit sensors (PARKED — not in this plan)

Real NVML `EnforcedPowerLimit` / temperature-threshold sensors would put true limits into `data.json` for any client. It changes `LibreHardwareMonitorLib`, adds sensors to the golden-mastered payload (regen + downstream ThermalTrace review per `AGENTS.md` §4), and needs its own spec + plan + operator go. Do not start it from this document.

## Self-review (writing-plans checklist)

- Spec coverage: feedback items 1/2/4 → A1–A5; 5 → A3; 3/3b → B1–B2; 6 → C1–C3+C5; 9 → C4; 7 → D1; 8 → D2; 10 → A2 (`null` range) + C1 (provenance line "no known range"). Enhancement opinions #2/#4/#5 → A4/A5/C3. Export/import + arc band-ticks intentionally deferred (listed in spec §3.10, not tasked — cut line, revisit after operator A/B).
- Placeholders: none — every code step carries the code; C-phase DOM steps include exact markup/CSS; the only deliberate stub is `derivedPowerLimit → null` (A2) with its real body in A5.
- Type consistency: `rangeFor` return shape `{lo,hi,source,derived?}` used in A4 ceil, C1 detail; state keys `rangeOverrides/observedMax/rowOrder/netAdapterOrder/hiddenNetAdapters` consistent across A1/C1/D1/D2; `SQ.groupPanels` item shape `{hw,ss,key,collapsed,index}` matches existing `panelEl`/`applyOrder` consumers.
