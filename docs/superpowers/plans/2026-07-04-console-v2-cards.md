# Console v2 — Honest Cards, Speedos, Fans-First — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the console's cards as honest, configurable instruments (state rail/chip vs type icon/color, trend arrows with deadband, fixed heights, filled sparklines), add speedometer arcs to power/fan/clock via a nice-ceiling of session peak, promote active fans into the hero row, and remove the Thermal Verdict pill + census chips.

**Architecture:** Pure client-side on the existing three web files. All new logic that can be pure lives under `window.SQ.*` with tests in `webtests/console.tests.js` (run headless via `node webtests/selftest.node.js`); DOM/CSS work is regression-gated (`node --check` + self-test stays green) and browser-E2E'd by the user. Executes on `feature/web-dashboard-customization` AFTER the Tier 3 final whole-branch review.

**Tech Stack:** Vanilla ES2020, CSS custom properties, inline SVG (`currentColor`), `localStorage` (`sq.dashboard.v1`).

**Spec:** `docs/superpowers/specs/2026-07-04-console-v2-cards-design.md` (honesty rules are spec-level law).

## Global Constraints

- Do NOT edit `HttpServer.cs`, `data.json` generation, `/Sensor`, `/metrics`, CSV, or `AssemblyVersion`.
- Honesty rules (spec §"Non-negotiable"): `raw == null` renders "—", never 0; context lines are measured-or-absent (slot reserved, no reflow); no derived/invented verdicts or ceilings presented as hardware limits; freshness stays global (no per-card freshness line).
- Rail + chip = STATE; icon + value color = TYPE. Chips ONLY on health-judged sensors (non-limit Temperature, Life Level); info-class sensors get no chip.
- Trend arrows: omitted inside the per-kind deadband; direction flips only when the opposite-signed rate exceeds the deadband (hysteresis keeps same-sign arrows alive down to half the deadband).
- Speedo ceilings: `niceCeil(max(RawMax, observed max))` on the 1-2-5 ladder; the ceiling is labeled small+muted on the card.
- Fixed card heights per mode (compact 132px / graph 172px); the grid must never be ragged.
- Self-test grows 60 → target 78; every code task ends with `node --check` clean + `node webtests/selftest.node.js` green before commit.
- Asset filenames keep no hyphens; only `console.js` / `console.css` / `index.html` under `Resources/Web/` plus `webtests/console.tests.js` and docs change.

## File Structure

- `console.js` — model helpers (Task 1–2) + card render rewrite, masthead removal, drawer style select (Task 3–5).
- `console.css` — type-color vars, chip/icon/trend/fixed-height/sparkline-fill styles, verdict/census removal.
- `index.html` — masthead verdict/census removal only.
- `webtests/console.tests.js` — new cases inserted before the `// === Tier 3 cases` marker (works for v2 cases too).
- `docs/local-ui-customizations.md` — Task 6.

---

### Task 1: Model — kinds, nice-ceiling, speedo ranges, per-card style

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (model section)
- Test: `webtests/console.tests.js`

**Interfaces (Produces):**
- `SQ.kindOf(type) -> 'temp'|'load'|'fan'|'power'|'clock'|'data'`
- `SQ.niceCeil(x) -> number|null` (1-2-5 ladder round-up; null for non-finite/≤0)
- `SQ.speedoRange(s, limits) -> [lo,hi]|null` — bounded types as today (delegates to `visualRangeForSensor`), plus Fan/Power/Clock → `[0, niceCeil(peak)]` where `peak = max(s.rawMax ?? 0, SENSOR_MOTION max ?? 0, s.raw ?? 0)`; null when peak ≤ 0.
- `normalizeDashboardState` gains `cardStyle: {[id]: 'gauge'|'number'|'graph'}` (map; unknown values dropped; `'auto'` never stored).
- `SQ.cardStyleFor(styleValue, hasRange, graphsEnabled) -> {arc:boolean, spark:boolean}` — pure precedence: `'gauge'`→`{arc:hasRange, spark:graphsEnabled}`; `'number'`→`{arc:false, spark:graphsEnabled}`; `'graph'`→`{arc:hasRange, spark:true}`; else (auto)→`{arc:hasRange, spark:graphsEnabled}`.

- [ ] **Step 1: Failing tests** — insert before the `// === Tier 3 cases` marker:

```js
    // --- v2: kinds, niceCeil, speedoRange, cardStyle ---
    eq('kindOf temp', S.kindOf('Temperature'), 'temp');
    eq('kindOf load family', [S.kindOf('Load'), S.kindOf('Level'), S.kindOf('Control')], ['load','load','load']);
    eq('kindOf fan', S.kindOf('Fan'), 'fan');
    eq('kindOf power family', [S.kindOf('Power'), S.kindOf('Voltage'), S.kindOf('Current')], ['power','power','power']);
    eq('kindOf clock', S.kindOf('Clock'), 'clock');
    eq('kindOf data fallback', [S.kindOf('Throughput'), S.kindOf('Factor'), S.kindOf('Nope')], ['data','data','data']);
    eq('niceCeil ladder', [S.niceCeil(87), S.niceCeil(1740), S.niceCeil(0.7), S.niceCeil(100), S.niceCeil(101)], [100, 2000, 1, 100, 200]);
    eq('niceCeil junk', [S.niceCeil(0), S.niceCeil(-5), S.niceCeil(NaN)], [null, null, null]);
    S.resetSensorMotion();
    eq('speedoRange fan from rawMax', S.speedoRange({type:'Fan', raw:900, rawMax:1740, id:'/f'}, {}), [0, 2000]);
    eq('speedoRange power current-peak', S.speedoRange({type:'Power', raw:87, rawMax:null, id:'/p'}, {}), [0, 100]);
    eq('speedoRange null when no peak', S.speedoRange({type:'Fan', raw:null, rawMax:null, id:'/f2'}, {}), null);
    eq('speedoRange temp delegates', S.speedoRange({cls:'cpu', type:'Temperature', text:'Tctl', raw:60, id:'/t'}, {}), [30, 95]);
    eq('cardStyle map normalized', S.normalizeDashboardState({cardStyle:{'/a':'gauge','/b':'nope','/c':'graph', '':'gauge'}}).cardStyle, {'/a':'gauge','/c':'graph'});
    eq('cardStyleFor gauge', S.cardStyleFor('gauge', true, false), {arc:true, spark:false});
    eq('cardStyleFor number keeps global spark', S.cardStyleFor('number', true, true), {arc:false, spark:true});
    eq('cardStyleFor graph forces spark', S.cardStyleFor('graph', false, false), {arc:false, spark:true});
    eq('cardStyleFor auto', S.cardStyleFor(undefined, true, true), {arc:true, spark:true});
```

- [ ] **Step 2: RED** — `node webtests/selftest.node.js` → FAIL (`S.kindOf is not a function`).

- [ ] **Step 3: Implement** — in the model section (near `SQ.visualRangeForSensor`):

```js
  SQ.kindOf = function (type) {
    if (type === 'Temperature') return 'temp';
    if (type === 'Load' || type === 'Level' || type === 'Control') return 'load';
    if (type === 'Fan') return 'fan';
    if (type === 'Power' || type === 'Voltage' || type === 'Current') return 'power';
    if (type === 'Clock') return 'clock';
    return 'data';
  };
  SQ.niceCeil = function (x) {
    x = Number(x);
    if (!Number.isFinite(x) || x <= 0) return null;
    const m = Math.pow(10, Math.floor(Math.log10(x)));
    for (const f of [1, 2, 5, 10]) { if (x <= f * m + 1e-9) return f * m; }
    return 10 * m;
  };
  SQ.speedoRange = function (s, limits) {
    const bounded = SQ.visualRangeForSensor(s, limits || {});
    if (bounded) return bounded;
    if (s.type !== 'Fan' && s.type !== 'Power' && s.type !== 'Clock') return null;
    const motion = SENSOR_MOTION.get(s.id);
    const peak = Math.max(s.rawMax ?? 0, motion ? motion.max : 0, s.raw ?? 0);
    const hi = SQ.niceCeil(peak);
    return hi ? [0, hi] : null;
  };
  SQ.cardStyleFor = function (styleValue, hasRange, graphsEnabled) {
    if (styleValue === 'gauge') return { arc: !!hasRange, spark: !!graphsEnabled };
    if (styleValue === 'number') return { arc: false, spark: !!graphsEnabled };
    if (styleValue === 'graph') return { arc: !!hasRange, spark: true };
    return { arc: !!hasRange, spark: !!graphsEnabled };
  };
```

In `defaultDashboardState` add `cardStyle: {}` after `collapsedPanels: {}`. In `normalizeDashboardState` add after `collapsedPanels: ...`:

```js
      collapsedPanels: cleanCollapsedMap(value.collapsedPanels),
      cardStyle: cleanCardStyleMap(value.cardStyle)
```

and next to `cleanCollapsedMap` add:

```js
  function cleanCardStyleMap(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => {
        if (k && (value[k] === 'gauge' || value[k] === 'number' || value[k] === 'graph')) out[k] = value[k];
      });
    return out;
  }
```

- [ ] **Step 4: GREEN** — `node webtests/selftest.node.js` → `SELFTEST PASS 77/77` (60 + 17). `node --check` clean.
- [ ] **Step 5: Commit** — `git add` console.js + console.tests.js; message `feat(web): v2 model — kindOf/niceCeil/speedoRange/cardStyle`.

---

### Task 2: Model — trend with deadband + hysteresis; fans join pickHero

**Files:**
- Modify: `console.js` (model section)
- Test: `webtests/console.tests.js`

**Interfaces (Produces):**
- `SQ.TRENDBANDS = { temp:{unit:'°C/s', db:0.05, scale:1}, fan:{unit:'rpm/min', db:30, scale:60}, power:{unit:'W/s', db:1.5, scale:1}, load:{unit:'%/s', db:0.5, scale:1}, clock:{unit:'MHz/s', db:15, scale:1} }` (data kind → no trend).
- `SQ.trendFor(id, kind, now) -> {direction:'rising'|'falling', rate:number, rateUnit:string}|null` — rate from `SENSOR_HISTORY` over the last 30 s: mean(second half) − mean(first half) divided by half-window seconds, × scale. Hysteresis via module map `TREND_DIRS` (per id): |rate| > db → arrow + store direction; |rate| ≥ db/2 AND same sign as stored → keep stored direction; else null + clear stored.
- `SQ.resetSensorTrends()` — clears `TREND_DIRS` (tests).
- `SQ.pickHero` extension: after drives, active fans (`type==='Fan' && raw > 0`) sorted rpm-desc, capped 4, hero cap 9 → 12.

- [ ] **Step 1: Failing tests** — insert before the marker (history is seeded with explicit timestamps; `trackSensorHistory(sensors, now)` already accepts `now`):

```js
    // --- v2: trend + hero fans ---
    S.resetSensorTrends();
    const seedHist = (id, pts) => pts.forEach(([t, raw]) => S.trackSensorHistory([{id, raw}], t));
    seedHist('/tr1', [[0,50],[5000,50.5],[10000,51],[15000,51.5],[20000,52],[25000,52.5],[30000,53]]); // windowed rate ~ +0.12 °C/s
    eq('trend rising past deadband', S.trendFor('/tr1', 'temp', 30000)?.direction, 'rising');
    eq('trend unit', S.trendFor('/tr1', 'temp', 30000)?.rateUnit, '°C/s');
    S.resetSensorTrends();
    seedHist('/tr2', [[0,50],[10000,50.01],[20000,50.02],[30000,50.03]]); // ~0.001 °C/s, inside band
    eq('trend inside deadband -> null', S.trendFor('/tr2', 'temp', 30000), null);
    S.resetSensorTrends();
    seedHist('/tr3', [[0,50],[15000,50.45],[30000,50.9]]); // windowed rate ~ +0.045: within [db/2, db), no prior -> null
    eq('hysteresis: weak same-sign w/o prior -> null', S.trendFor('/tr3', 'temp', 30000), null);
    S.resetSensorTrends();
    seedHist('/tr4', [[0,50],[15000,50.9],[30000,51.8]]); // windowed rate +0.09 °C/s -> rising stored
    eq('hysteresis: arm rising', S.trendFor('/tr4', 'temp', 30000)?.direction, 'rising');
    seedHist('/tr4', [[35000,51.85],[45000,52.3],[60000,52.4]]); // 30s-window rate ~ +0.035: within [db/2, db)
    eq('hysteresis: weak same-sign keeps arrow', S.trendFor('/tr4', 'temp', 60000)?.direction, 'rising');
    eq('trend data kind -> null', S.trendFor('/tr1', 'data', 30000), null);
    S.resetSensorTrends();
    const fanHero = S.pickHero([
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #1', raw:900, value:'900 RPM', id:'/fan1'},
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #2', raw:1400, value:'1400 RPM', id:'/fan2'},
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #3', raw:0, value:'0 RPM', id:'/fan3'}
    ], {});
    eq('hero fans active only, rpm-desc', fanHero.filter(h => h.s.type === 'Fan').map(h => h.s.id), ['/fan2','/fan1']);
```

- [ ] **Step 2: RED** — `node webtests/selftest.node.js` → FAIL (`S.trendFor is not a function`).

- [ ] **Step 3: Implement** — near `SQ.historyFor`:

```js
  const TREND_DIRS = new Map();
  SQ.TRENDBANDS = {
    temp:  { unit: '°C/s',    db: 0.05, scale: 1 },
    fan:   { unit: 'rpm/min', db: 30,   scale: 60 },
    power: { unit: 'W/s',     db: 1.5,  scale: 1 },
    load:  { unit: '%/s',     db: 0.5,  scale: 1 },
    clock: { unit: 'MHz/s',   db: 15,   scale: 1 }
  };
  SQ.resetSensorTrends = function () { TREND_DIRS.clear(); };
  SQ.trendFor = function (id, kind, now) {
    const band = SQ.TRENDBANDS[kind];
    if (!band) return null;
    const t = Number.isFinite(now) ? now : Date.now();
    const win = SENSOR_HISTORY.get(id)?.filter(p => t - p.t <= 30000 && Number.isFinite(p.raw)) || [];
    if (win.length < 3) { TREND_DIRS.delete(id); return null; }
    const mid = Math.floor(win.length / 2);
    const mean = a => a.reduce((s, p) => s + p.raw, 0) / a.length;
    const tMid = (win[win.length - 1].t - win[0].t) / 2 / 1000;
    if (tMid <= 0) { TREND_DIRS.delete(id); return null; }
    const rate = ((mean(win.slice(mid)) - mean(win.slice(0, mid))) / tMid) * band.scale;
    const prev = TREND_DIRS.get(id);
    let direction = null;
    if (rate > band.db) direction = 'rising';
    else if (rate < -band.db) direction = 'falling';
    else if (prev && Math.abs(rate) >= band.db / 2 &&
             ((prev === 'rising' && rate > 0) || (prev === 'falling' && rate < 0))) direction = prev;
    if (direction) TREND_DIRS.set(id, direction); else TREND_DIRS.delete(id);
    return direction ? { direction, rate, rateUnit: band.unit } : null;
  };
```

In `SQ.pickHero`, before `return H.slice(0, 9);`:

```js
    sensors.filter(s => s.type === 'Fan' && s.raw > 0).sort((a, b) => b.raw - a.raw).slice(0, 4)
      .forEach(f => add(f, f.text, { unit: 'rpm' }));
    return H.slice(0, 12);
```

- [ ] **Step 4: GREEN** — `node webtests/selftest.node.js` → `SELFTEST PASS 85/85` (77 + 8). `node --check` clean.
- [ ] **Step 5: Commit** — `feat(web): v2 model — trend deadband/hysteresis + fans in pickHero`.

---

### Task 3: De-opinionate the masthead

**Files:**
- Modify: `index.html` (masthead), `console.js` (`render()`), `console.css`

- [ ] **Step 1: `index.html`** — delete the verdict block and census div:

```html
  <div class="verdict"><span class="lamp big" id="vlamp"></span><div>
    <div class="lab">Thermal Verdict</div><div class="st" id="vstate">—</div></div></div>
  <div class="census" id="census"></div>
```

- [ ] **Step 2: `console.js` `render()`** — delete the verdict/census computation and DOM writes (the `let worst`/`vmap`/`$('#vlamp')`/`$('#vstate')`/`counts`/`$('#census')` lines). KEEP `const alarm = sensors.filter(...)` — the placard still consumes it via `renderPlacard(alarm)`.

- [ ] **Step 3: `console.css`** — remove the `.verdict`, `.verdict .lab`, `.verdict .st`, `.census`, `.chip`, `.chip b` rules and the `@media (max-width:640px)` `.verdict{...}` / `.census{...}` lines.

- [ ] **Step 4: Verify** — `node --check` clean; `node webtests/selftest.node.js` → 86/86 (regression); grep `vlamp|vstate|census|vmap` in console.js/index.html → no matches.
- [ ] **Step 5: Commit** — `feat(web): remove Thermal Verdict pill + census chips (placard stays)`.

---

### Task 4: Card anatomy v2 (rail/chip=state, icon/color=type, trend, fixed heights)

**Files:**
- Modify: `console.js` (`cardEl`, `ctlCluster` untouched), `console.css`

- [ ] **Step 1: Type colors + icons.** In `console.css` `:root` (and both `[data-theme]` blocks + light media block) add:

```css
  --t-temp:#F5A524; --t-load:#34D399; --t-fan:#00e5ff; --t-power:#c084fc; --t-clock:#60a5fa; --t-data:#8794a1;
```

light variants: `--t-temp:#b8790a; --t-load:#0a9d55; --t-fan:#0091a8; --t-power:#7c3aed; --t-clock:#2563eb; --t-data:#4f5b67;`

In `console.js` bootstrap add an icon map (stroke-based, `currentColor`, 20×20 viewBox):

```js
    const TICONS = {
      temp:  '<path d="M8 3a2 2 0 0 1 4 0v7.3a4.5 4.5 0 1 1-4 0z" fill="none" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="14" r="2" fill="currentColor"/>',
      load:  '<path d="M3 13a7 7 0 0 1 14 0" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 13 13.5 8" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="13" r="1.4" fill="currentColor"/>',
      fan:   '<circle cx="10" cy="10" r="1.8" fill="currentColor"/><path d="M10 8.2C10 4 13 3 15 5c-1 2-3 3-5 3.2M11.8 10c4.2 0 5.2 3 3.2 5-2-1-3-3-3.2-5M10 11.8C10 16 7 17 5 15c1-2 3-3 5-3.2M8.2 10C4 10 3 7 5 5c2 1 3 3 3.2 5" fill="none" stroke="currentColor" stroke-width="1.4"/>',
      power: '<path d="M11 2 4 12h5l-1 6 7-10h-5z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>',
      clock: '<circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 6v4l3 2" fill="none" stroke="currentColor" stroke-width="1.6"/>',
      data:  '<path d="M2 11h4l2-5 4 9 2-5h4" fill="none" stroke="currentColor" stroke-width="1.6"/>'
    };
    const tIcon = kind => `<svg class="ticon" viewBox="0 0 20 20" aria-hidden="true">${TICONS[kind] || TICONS.data}</svg>`;
```

- [ ] **Step 2: Rewrite `cardEl`** (keeps the Task-5/6 control cluster + grip logic verbatim at the end):

```js
    function cardEl(h, pinned) {
      const { n, unit } = SQ.splitValue(h.s.value);
      const u = unit || h.unit || '';
      const st = h.status;
      const kind = SQ.kindOf(h.s.type);
      const styleVal = state.dashboard.cardStyle[h.s.id];
      const range = h.bounded || SQ.speedoRange(h.s, {});
      const fx = SQ.cardStyleFor(styleVal, !!range && h.s.raw != null, state.dashboard.graphsEnabled);
      let arc = '';
      if (fx.arc) { const [lo, hi] = range; arc = arcSVG(h.s.id, (h.s.raw - lo) / (hi - lo)); }
      const isHealth = (h.s.type === 'Temperature' && !SQ.isLimitSensor(h.s)) ||
                       (h.s.type === 'Level' && (h.s.text || '').toLowerCase().includes('life'));
      const chip = isHealth && (st === 'ok' || st === 'warn' || st === 'crit')
        ? `<span class="chip-state g-${st}">${STGLYPH[st]} ${STLABEL[st]}</span>` : '';
      const trend = SQ.trendFor(h.s.id, kind);
      const trendHtml = trend
        ? `<span class="trend">${trend.direction === 'rising' ? '&#8599;' : '&#8600;'} ${Math.abs(trend.rate).toFixed(trend.rate >= 10 ? 0 : 2)} ${esc(trend.rateUnit)}</span>`
        : '<span class="trend"></span>';
      const ceil = fx.arc && !h.bounded ? `<span class="ceil">/ ${esc(String(range[1]))}</span>` : '';
      const cell = document.createElement('div');
      cell.className = `cell s-${st}${pinned ? ' pinned' : ''}${fx.spark ? ' graph-on' : ''}`;
      cell.style.setProperty('--tc', `var(--t-${kind})`);
      if (pinned) cell.dataset.key = h.s.id;
      const source = (h.s.hw || '').split(' ').slice(0, 3).join(' ');
      cell.innerHTML =
        `<div class="k"><span class="name">${esc(h.label)}</span>${chip}</div>
         <div class="k2"><span class="src">${esc(source)}</span>${tIcon(kind)}</div>
         <div class="body">${arc}<div class="readout">
           <div class="big"><span class="v">${esc(n)}</span><span class="u">${esc(u)}</span>${ceil}</div>
           <div class="meta">${rangeMarkup(h.s) || '<div class="range"></div>'}${trendHtml}</div>
         </div></div>${fx.spark ? sparkAreaSVG(h.s, range) : ''}`;
      const showHide = !pinned;
      const ctl = document.createElement('div');
      ctl.className = 'cell-ctl';
      ctl.innerHTML = (pinned ? `<button class="grip" aria-label="Drag to reorder ${esc(h.label)}" title="Drag to reorder">&#8942;&#8942;</button>` : '') + ctlCluster(h.s.id, h.label, { hide: showHide });
      cell.appendChild(ctl);
      return cell;
    }
```

Notes: `raw == null` keeps rendering "—" via `splitValue` (`fx.arc` is forced off because `hasRange` is passed as false). The empty `.range`/`.trend` spans keep the context slot reserved (fixed height, no reflow).

- [ ] **Step 3: Replace `sparklineSVG` with `sparkAreaSVG`** (filled area, type-colored; forced-on per-card via `fx.spark` rather than reading the global toggle inside):

```js
    function sparkAreaSVG(sensor, bounded) {
      const hist = SQ.historyFor(sensor.id).filter(p => Number.isFinite(p.raw));
      if (hist.length < 2) return '<div class="spark empty"></div>';
      const values = hist.map(p => p.raw);
      let min = bounded ? bounded[0] : Math.min(...values);
      let max = bounded ? bounded[1] : Math.max(...values);
      if (!(max > min)) { min -= 1; max += 1; }
      const w = 120, h = 28;
      const pts = hist.map((p, i) => {
        const x = (i / (hist.length - 1)) * w;
        const y = h - ((p.raw - min) / (max - min)) * h;
        return `${x.toFixed(1)},${Math.max(0, Math.min(h, y)).toFixed(1)}`;
      });
      return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">
        <polygon points="0,${h} ${pts.join(' ')} ${w},${h}" fill="var(--tc)" opacity="0.18"/>
        <polyline points="${pts.join(' ')}" fill="none" stroke="var(--tc)" stroke-width="1.6" vector-effect="non-scaling-stroke"/></svg>`;
    }
```

Delete the old `sparklineSVG` and its call sites (cardEl is the only one).

- [ ] **Step 4: CSS** — update card styles (replace the old `.cell .k`/`.readout` block additions; keep rail `::before` as-is — it is already state-colored):

```css
/* v2 card anatomy */
.cell{height:132px;display:flex;flex-direction:column}
.cell.graph-on{height:172px}
.cell .k{display:flex;justify-content:space-between;align-items:center;gap:8px}
.cell .k2{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-top:1px}
.ticon{width:14px;height:14px;color:var(--tc);flex:none;opacity:.9}
.chip-state{display:inline-flex;align-items:center;gap:4px;padding:1px 7px;border-radius:999px;font-family:var(--mono);
  font-size:9px;font-weight:700;letter-spacing:.09em;border:1px solid currentColor}
.readout .big .v{color:var(--tc)}
.readout .big .ceil{font-family:var(--mono);font-size:10px;color:var(--dim);margin-left:6px}
.readout .meta{display:flex;gap:10px;align-items:baseline;min-height:16px;margin-top:5px}
.readout .meta .range{margin-top:0}
.trend{font-family:var(--mono);font-size:10.5px;color:var(--muted);white-space:nowrap}
.spark{margin-top:auto}
body.stale main{opacity:.55;filter:saturate(.6);transition:opacity .3s}
```

And in `tick()`'s catch/success paths: `document.body.classList.add('stale')` on failure, `document.body.classList.remove('stale')` on success (global stale treatment — all cards together).

- [ ] **Step 5: Verify** — `node --check` clean; self-test 85/85 (regression). Grep: `sparklineSVG` gone; `--tc` set in cardEl.
- [ ] **Step 6: Commit** — `feat(web): v2 card anatomy — state chip, type icon/color, trend, fixed heights, filled sparkline`.

---

### Task 5: Per-card style select in Customize (Cards tab)

**Files:**
- Modify: `console.js` (`renderPinnedEditor` + a hero-style list, drawer click/change handling)

- [ ] **Step 1:** In the drawer's Cards pane, each pinned-editor row gains a style `<select>`; hero cards get style controls via the existing sensor rows when pinned/unpinned — simplest complete approach: add the select to `renderPinnedEditor` rows AND to `renderSensorRows(container, filter, 'cards')` rows for sensors currently shown as heroes. Implement in `renderPinnedEditor` row template (after the title input):

```js
          <select class="style-select" data-action="style" data-id="${esc(id)}">
            ${['auto','gauge','number','graph'].map(v =>
              `<option value="${v}"${(state.dashboard.cardStyle[id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
          </select>
```

and in `renderSensorRows`, insert a style select for 'cards' mode — change the row template to:

```js
      container.innerHTML = rows.map(s => {
        const hidden = SQ.isSensorHidden(s, state.dashboard);
        const pinned = state.dashboard.pinnedCards.some(c => c.id === s.id);
        const action = mode === 'cards' ? (pinned ? 'unpin' : 'pin') : (hidden ? 'show' : 'hide');
        const label = mode === 'cards' ? (pinned ? 'Unpin' : 'Pin') : sensorButtonLabel(s);
        const badge = DEFAULT_HIDDEN_SENSOR_IDS.has(s.id) ? '<span class="mini-badge">default</span>' : '';
        const styleSel = mode === 'cards'
          ? `<select class="style-select" data-action="style" data-id="${esc(s.id)}">
              ${['auto','gauge','number','graph'].map(v =>
                `<option value="${v}"${(state.dashboard.cardStyle[s.id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
            </select>` : '';
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(s.text)}</b> ${badge}<span>${esc(s.hw)} · ${esc(s.type)} · ${esc(s.value ?? '-')}</span><code>${esc(s.id)}</code></div>
          ${styleSel}<button class="iconbtn" data-action="${action}" data-id="${esc(s.id)}">${label}</button>
        </div>`;
      }).join('') || '<div class="empty-note">No sensors</div>';
```

and widen the choice row for the extra column: `.sensor-choice{grid-template-columns:minmax(0,1fr) auto auto}` (add alongside the existing `.sensor-choice,.order-row` rule; the two-column drop for hidden-mode rows without a select is handled by the empty `styleSel` string).

- [ ] **Step 2:** In the drawer `change` listener (currently handling `rename`), add:

```js
      const sel = e.target.closest('[data-action="style"]');
      if (sel) {
        const v = sel.value;
        if (v === 'auto') delete state.dashboard.cardStyle[sel.dataset.id];
        else state.dashboard.cardStyle[sel.dataset.id] = v;
        commitDashboard();
      }
```

- [ ] **Step 3: CSS** — `.style-select{font:11px var(--mono);color:var(--ink);background:var(--panel-2);border:1px solid var(--line-soft);border-radius:6px;padding:5px 6px}`

- [ ] **Step 4: Verify** — `node --check` clean; self-test 85/85; manual note for user E2E (style select persists, `graph` forces a sparkline with global Graphs off).
- [ ] **Step 5: Commit** — `feat(web): per-card style override (auto/gauge/number/graph) in Customize`.

---

### Task 6: Docs + full verification

**Files:**
- Modify: `docs/local-ui-customizations.md`

- [ ] **Step 1:** Update the customization section: verdict/census removal (placard retained), card anatomy (rail/chip=state vs icon/value=type; chips only on health-judged sensors), honesty rules (— never 0; measured-or-absent; global-only freshness + body.stale), trend deadband/hysteresis table, speedo nice-ceiling rule (labeled ceiling, not a hardware limit), fans-in-hero, per-card style override + `cardStyle` in `sq.dashboard.v1`.
- [ ] **Step 2:** `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` → 0 failures.
- [ ] **Step 3:** `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` (redirect `-p:BaseOutputPath=` if the running monitor locks the output) → 0 errors.
- [ ] **Step 4:** `node webtests/selftest.node.js` → 85/85.
- [ ] **Step 5: Commit** — `docs(web): document Console v2 cards, trend, speedos, fans-first, de-opinionated masthead`.

---

## Notes for the implementer

- Line numbers have drifted across Tier 3 — always match on surrounding code.
- `SENSOR_MOTION`, `SENSOR_HISTORY`, `trackSensorHistory(sensors, now)`, `STGLYPH`, `STLABEL`, `arcSVG`, `rangeMarkup`, `ctlCluster` already exist — reuse, don't duplicate.
- The honesty rules are review-blocking: any code path that could print a number not derived from measured data is a defect.
- `webtests/console.tests.js`: insert all new cases before the `// === Tier 3 cases are appended below by later tasks ===` marker.
