# Web Telemetry Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the legacy web dashboard with a self-contained, framework-free "SQ Telemetry Console" that consumes the unchanged `data.json` and renders live glass-cockpit-style instrumentation.

**Architecture:** Three static assets under `Resources/Web/` (auto-embedded, served by name) — `index.html` + `console.css` (with embedded Chakra Petch font) + `console.js`. `console.js` splits into pure logic (`window.SQ.*`, unit-tested via an in-browser self-test harness) and DOM render/bootstrap. It polls `GET /data.json` and re-renders each tick. No server, `data.json`, or `AssemblyVersion` change.

**Tech Stack:** Vanilla ES2020 JS, CSS custom properties, embedded woff2 font, `HttpListener`-served embedded resources (C# side untouched). Self-test via a static-served HTML page + browser console.

## Global Constraints

- **Zero contract change:** do NOT edit `data.json` output, `HttpServer.cs`, the `/Sensor` API, `/metrics`, CSV, or `AssemblyVersion` (pinned 0.9.6). The dashboard is read-only and client-side.
- **Regression gate:** `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` — 7 data-contract tests + suite stay green.
- **Build:** every build/test uses `-p:Platform=x64` (CsWin32 fails on AnyCPU).
- **No hyphens in new asset filenames** — `HttpServer.ServeResourceFileAsync` maps `.`→resource segments and special-cases only `custom-theme`; hyphens break resolution. Use `console.css` / `console.js`.
- **Self-contained:** no external CDN/font/script/fetch to other hosts (served over LAN, possibly offline). Font embedded as base64 in `console.css`.
- **Reference implementation (validated, committed):** `docs/superpowers/assets/2026-07-04-console-reference.html` holds the reviewed CSS (`<style>`) and JS (`<script>`) with `/*__FONT__*/` and `/*__DATA__*/null` placeholders. Lift validated bulk from it; this plan shows the deltas (3 review fixes + live fetch) as concrete code.
- **Status model:** only Temperature (per-hardware-class bands, honoring drive/DIMM self-reported limits) and SSD `Life` drive alarm color; everything else is `info`. Motherboard (`/lpc`) temps and limit/metadata sensors are `info`, never alarmed.
- **Status is triple-encoded:** color + glyph (● ok, ▲ warn, ✕ crit, · info, ○ idle) + text label.

---

### Task 1: Pure model layer (`window.SQ`) + self-test harness

**Files:**
- Create: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (logic half only in this task)
- Create: `webtests/console.test.html`
- Exists (fixture, committed): `webtests/fixture.data.json` (a real trimmed `data.json` snapshot)

**Interfaces:**
- Produces (all on `window.SQ`, and the bootstrap is gated so the test page can load the file without side effects):
  - `SQ.classOf(sid: string) -> 'cpu'|'gpu'|'igpu'|'mem'|'dimm'|'nvme'|'disk'|'mb'|'nic'|'other'`
  - `SQ.flatten(root: object) -> Sensor[]` where `Sensor = {hw, hwid, cls, type, text, value, min, max, raw, id}`
  - `SQ.isLimitSensor(s: Sensor) -> boolean`
  - `SQ.deriveLimits(sensors: Sensor[]) -> { [hwid]: {warn?:number, crit?:number} }`
  - `SQ.statusOf(s: Sensor, limits) -> 'ok'|'warn'|'crit'|'info'|'off'`
  - `SQ.pickHero(sensors: Sensor[], limits) -> Hero[]` where `Hero = {s:Sensor, label, status, bounded?:[lo,hi], unit?}`
  - `SQ.splitValue(v: string|null) -> {n:string, unit:string}`
  - `SQ.RANK = {crit:3,warn:2,ok:1,info:0,off:-1}`

- [ ] **Step 1: Write the failing self-test page**

Create `webtests/console.test.html`:

```html
<!doctype html><meta charset="utf-8"><title>SQ console self-test</title>
<body><pre id="out">running…</pre>
<script>window.SQ_NO_BOOT = true;</script>
<script src="/LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js"></script>
<script>
(async () => {
  const out = document.getElementById('out'); let pass = 0, fail = 0, log = [];
  const eq = (name, got, want) => {
    const ok = JSON.stringify(got) === JSON.stringify(want);
    log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${JSON.stringify(got)} want=${JSON.stringify(want)}`);
    ok ? pass++ : fail++;
  };
  const data = await (await fetch('/webtests/fixture.data.json', {cache:'no-store'})).json();
  const S = window.SQ;

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

  // cpu Tctl 65.5 -> ok (< 85)
  eq('cpu Tctl ok', st('/amdcpu/0/temperature/2'), 'ok');
  // motherboard stray 89°C -> info (never alarmed)
  eq('mb stray info', st('/lpc/nct6701d/0/temperature/5'), 'info');
  // nvme working temp 52.9 with self-limits present -> ok (warn 84)
  eq('nvme temp ok', st('/nvme/2/temperature/2'), 'ok');
  // nvme "Warning Temperature" is a limit sensor -> info
  eq('nvme limit info', st('/nvme/2/temperature/10'), 'info');
  // load is info, not alarm
  eq('cpu load info', st('/amdcpu/0/load/0'), 'info');

  const hero = S.pickHero(sensors, limits);
  eq('hero has CPU Temp', hero.some(h => h.label === 'CPU Temp'), true);
  eq('CPU Power unbounded', !!hero.find(h => h.label === 'CPU Power')?.bounded, false);
  eq('CPU Temp bounded', !!hero.find(h => h.label === 'CPU Temp')?.bounded, true);

  console.log(`SELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass+fail}`);
  out.textContent = log.join('\n') + `\n\nSELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass+fail}`;
})();
</script>
```

- [ ] **Step 2: Run the test to verify it fails**

From repo root, serve statically and open the page:
```bash
python -m http.server 8791 --bind 127.0.0.1
```
Open `http://127.0.0.1:8791/webtests/console.test.html`. Expected: FAIL — `console.js` 404 / `window.SQ` undefined, `<pre>` shows an error or stays "running…". (If driving via Chrome MCP: navigate there, then `read_console_messages` pattern `SELFTEST|Error`.)

- [ ] **Step 3: Write the logic half of `console.js`**

Create `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` starting from the `<script>` in `docs/superpowers/assets/2026-07-04-console-reference.html`, but (a) remove the `const DATA = …` line and the jitter/`setInterval` bootstrap (added in Task 3), (b) attach the pure functions to `window.SQ`, (c) gate any bootstrap behind `SQ_NO_BOOT`. Paste exactly:

```js
// SQ Telemetry Console — pure model layer. Consumes the unchanged data.json.
(function () {
  const SQ = {};
  SQ.RANK = { crit: 3, warn: 2, ok: 1, info: 0, off: -1 };

  SQ.classOf = function (sid) {
    if (!sid) return 'other';
    if (sid.startsWith('/amdcpu') || sid.startsWith('/intelcpu')) return 'cpu';
    if (sid.startsWith('/gpu-nvidia')) return 'gpu';
    if (sid.startsWith('/gpu-amd') || sid.startsWith('/gpu-intel')) return 'igpu';
    if (sid.startsWith('/ram') || sid.startsWith('/vram')) return 'mem';
    if (sid.startsWith('/memory/dimm')) return 'dimm';
    if (sid.startsWith('/nvme') || sid.startsWith('/hdd')) return 'nvme';
    if (sid.startsWith('/usb')) return 'disk';
    if (sid.startsWith('/lpc')) return 'mb';
    if (sid.startsWith('/nic')) return 'nic';
    return 'other';
  };

  SQ.flatten = function (root) {
    const out = [];
    (function walk(node, hw, hwid) {
      if (node.HardwareId !== undefined) { hw = node.Text; hwid = node.HardwareId; }
      if (node.SensorId !== undefined) {
        out.push({ hw, hwid, cls: SQ.classOf(node.SensorId), type: node.Type, text: node.Text,
          value: node.Value, min: node.Min, max: node.Max, raw: node.RawValue, id: node.SensorId });
      }
      (node.Children || []).forEach(c => walk(c, hw, hwid));
    })(root, root.Text, undefined);
    return out;
  };

  SQ.isLimitSensor = function (s) {
    const t = (s.text || '').toLowerCase();
    return t.includes('limit') || t.includes('warning temperature') ||
           t.includes('critical temperature') || t.includes('resolution');
  };

  SQ.deriveLimits = function (sensors) {
    const m = {};
    sensors.forEach(s => {
      if (s.type !== 'Temperature' || s.raw == null) return;
      const t = (s.text || '').toLowerCase();
      m[s.hwid] = m[s.hwid] || {};
      if (t.includes('critical') && (t.includes('high') || t.includes('temperature'))) m[s.hwid].crit = s.raw;
      else if (t.includes('warning') || (t.includes('high') && t.includes('limit'))) m[s.hwid].warn = s.raw;
    });
    return m;
  };

  const TEMPBANDS = { cpu: [85, 95], gpu: [83, 92], igpu: [83, 92], nvme: [70, 80], dimm: [55, 85], mb: null, mem: null };
  function tempStatus(s, limits) {
    const t = (s.text || '').toLowerCase(), lim = limits[s.hwid];
    let warn, crit;
    if (s.cls === 'gpu' && (t.includes('junction') || t.includes('hot'))) { warn = 95; crit = 105; }
    else if ((s.cls === 'nvme' || s.cls === 'dimm') && lim) { warn = lim.warn ?? TEMPBANDS[s.cls][0]; crit = lim.crit ?? TEMPBANDS[s.cls][1]; }
    else { const b = TEMPBANDS[s.cls]; if (!b) return 'info'; warn = b[0]; crit = b[1]; }
    if (s.raw >= crit) return 'crit';
    if (s.raw >= warn) return 'warn';
    return 'ok';
  }
  SQ.statusOf = function (s, limits) {
    if (s.raw == null) return 'off';
    if (s.type === 'Temperature') { if (SQ.isLimitSensor(s)) return 'info'; return tempStatus(s, limits); }
    if (s.type === 'Level' && (s.text || '').toLowerCase().includes('life')) {
      if (s.raw < 5) return 'crit'; if (s.raw < 20) return 'warn'; return 'ok';
    }
    return 'info';
  };

  SQ.splitValue = function (v) {
    if (v == null) return { n: '—', unit: '' };
    const m = String(v).match(/^([\-\d.,]+)\s*(.*)$/);
    return m ? { n: m[1], unit: m[2] } : { n: String(v), unit: '' };
  };

  SQ.pickHero = function (sensors, limits) {
    const H = [], find = p => sensors.find(p);
    const add = (s, label, opts) => { if (s) H.push(Object.assign({ s, label, status: SQ.statusOf(s, limits) }, opts || {})); };
    if (sensors.some(s => s.cls === 'cpu')) {
      const c = sensors.filter(s => s.cls === 'cpu');
      add(c.find(s => s.type === 'Temperature' && s.text.includes('Tctl')), 'CPU Temp', { bounded: [30, 95], unit: '°C' });
      add(c.find(s => s.type === 'Load' && /CPU Total/i.test(s.text)), 'CPU Load', { bounded: [0, 100], unit: '%' });
      add(c.find(s => s.type === 'Power' && /^Package/i.test(s.text)), 'CPU Power', { unit: 'W' });
    }
    if (sensors.some(s => s.cls === 'gpu')) {
      const g = sensors.filter(s => s.cls === 'gpu');
      add(g.find(s => s.type === 'Temperature' && /^GPU Core/i.test(s.text)), 'GPU Temp', { bounded: [25, 92], unit: '°C' });
      add(g.find(s => s.type === 'Temperature' && /Junction/i.test(s.text)), 'GPU Mem Jct', { bounded: [25, 105], unit: '°C' });
      add(g.find(s => s.type === 'Load' && /^GPU Core/i.test(s.text)), 'GPU Load', { bounded: [0, 100], unit: '%' });
      add(g.find(s => s.type === 'Power' && /Package/i.test(s.text)), 'GPU Power', { unit: 'W' });
    }
    add(find(s => s.cls === 'mem' && s.hw === 'Total Memory' && s.type === 'Load'), 'RAM Used', { bounded: [0, 100], unit: '%' });
    const drives = sensors.filter(s => s.cls === 'nvme' && s.type === 'Temperature' && !SQ.isLimitSensor(s) && s.raw != null).sort((a, b) => b.raw - a.raw);
    add(drives[0], 'Drive Temp', { bounded: [25, 80], unit: '°C' });
    return H.slice(0, 9);
  };

  window.SQ = SQ;
  if (!window.SQ_NO_BOOT) { /* Task 3 installs the bootstrap here */ }
})();
```

- [ ] **Step 4: Run the test to verify it passes**

Reload `http://127.0.0.1:8791/webtests/console.test.html`. Expected: page shows `SELFTEST PASS 16/16` and console logs `SELFTEST PASS 16/16`. (Chrome MCP: `read_console_messages` pattern `SELFTEST` → `SELFTEST PASS 16/16`.)

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.test.html webtests/fixture.data.json
git commit -m "feat(web): SQ console pure model layer + browser self-test

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `console.css` — design system + embedded font

**Files:**
- Create: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css`

**Interfaces:**
- Produces: the class contract the render layer (Tasks 3–5) targets — `.mast .brand .sigil .wordmark .verdict .census .chip .controls`, `.sec-head`, `.pfd .cell .arc .readout`, `.placard`, `.grid .panel .panel-head .panel-body .tg .row .bar`, status classes `.s-ok/.s-warn/.s-crit/.s-info/.s-off` and `.g-*`, and CSS vars `--ok/--warn/--crit/--off/--cy/--lime`.

- [ ] **Step 1: Create `console.css` from the validated reference**

Copy the entire contents of the `<style>…</style>` block in `docs/superpowers/assets/2026-07-04-console-reference.html` into `console.css` (drop the surrounding `<style>` tags). This is the reviewed, blended-palette, theme-aware system (dark default via `:root`, light via `@media (prefers-color-scheme:light)`, manual override via `:root[data-theme=…]`).

- [ ] **Step 2: Splice the embedded Chakra Petch font at the top of `console.css`**

The reference used a `/*__FONT__*/` placeholder. Replace it with the two `@font-face` blocks (weights 500 + 600) from the reference dashboard:
```bash
cd LibreHardwareMonitor.Windows.Forms/Resources/Web
python - <<'PY'
import re
css=open('console.css',encoding='utf-8').read()
src=open(r'D:/Development/Thermals/SQ-control/docs/status/readiness-dashboard.html',encoding='utf-8').read()
faces=re.findall(r"@font-face\{.*?format\('woff2'\);\}", src, re.S)[:2]
open('console.css','w',encoding='utf-8').write(css.replace('/*__FONT__*/', '\n'.join(faces)))
print('font faces spliced:', len(faces))
PY
```
Expected: `font faces spliced: 2`. (If that source path is unavailable, extract the same two `@font-face` blocks from `docs/superpowers/assets/2026-07-04-console-reference.html` once the mockup has been rendered — they are identical.)

- [ ] **Step 3: Apply review-fix #1 CSS (PFD readout typography)**

Replace the `.readout` block in `console.css` with this (value+unit share a baseline; range is one compact mono line; fixed min-height equalizes cells):
```css
.cell{min-height:118px}
.readout{min-width:0}
.readout .big{display:flex;align-items:baseline;gap:4px;font-family:var(--disp);font-weight:600;line-height:1}
.readout .big .v{font-size:30px;letter-spacing:.01em}
.readout .big .u{font-size:14px;font-weight:400;color:var(--muted)}
.readout .range{margin-top:5px;font-family:var(--mono);font-size:10.5px;color:var(--muted);white-space:nowrap}
.readout .range b{color:var(--ink);font-weight:600}
.readout .tags{margin-top:7px;display:flex;gap:6px;align-items:center;flex-wrap:wrap}
```

- [ ] **Step 4: Apply review-fix #2 CSS (masonry columns for subsystems)**

Replace the `.grid{…}` rule with a balanced multi-column (masonry) layout so tall panels don't leave gaps:
```css
.grid{column-width:370px;column-gap:14px}
.panel{break-inside:avoid;-webkit-column-break-inside:avoid;margin:0 0 14px;display:inline-block;width:100%;
  border:1px solid var(--line);border-radius:13px;overflow:hidden;
  background:linear-gradient(180deg,var(--panel),var(--panel-2));box-shadow:var(--shadow)}
```
Add the "show more" affordance styles used by fix #3:
```css
.morebtn{display:block;width:calc(100% - 30px);margin:6px 15px 8px;padding:6px;font-family:var(--mono);
  font-size:10.5px;letter-spacing:.08em;color:var(--muted);background:transparent;border:1px dashed var(--line-soft);
  border-radius:7px;cursor:pointer}
.morebtn:hover{color:var(--cy);border-color:var(--cy)}
.extra{display:none} .extra.open{display:block}
```

- [ ] **Step 5: Verify it loads with no CSS errors and font resolves**

Temporarily reference `console.css` from the test page (`<link rel="stylesheet" href="/LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css">`) and reload `http://127.0.0.1:8791/webtests/console.test.html`. Expected: page background goes dark, Chakra Petch renders (headings switch to the condensed display face), no 404 for the css. Remove the temporary `<link>` after checking.

- [ ] **Step 6: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): SQ console CSS system + embedded font (fixes 1-2: readout typography, masonry)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: `index.html` + fetch/poll bootstrap + controls + persistence

**Files:**
- Create/replace: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html`
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add render bootstrap after the `window.SQ = SQ;` line)

**Interfaces:**
- Consumes: `window.SQ.*` (Task 1). Depends on the DOM ids in `index.html`.
- Produces: `render(data)` and a running poll loop; `renderMasthead`/`renderPFD`/`renderPanels` seams filled in Tasks 4–5. This task delivers a working masthead + verdict + census + freshness + controls against the live server; PFD/panels may be empty until Tasks 4–5.

- [ ] **Step 1: Create `index.html` from the reference `<body>`**

Copy the `<body>…</body>` markup from `docs/superpowers/assets/2026-07-04-console-reference.html` into a new `index.html`, and in `<head>` load the split assets (no inline `<style>`/`<script>`):
```html
<!doctype html><html lang="en" data-theme="dark"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>SQ Telemetry Console</title>
<link rel="shortcut icon" href="favicon.ico">
<link rel="stylesheet" href="console.css">
</head>
<body>
<!-- paste the reference <body> inner markup here (masthead, main, sections, footer) -->
<script src="console.js"></script>
</body></html>
```

- [ ] **Step 2: Add the live bootstrap to `console.js`**

Replace the `if (!window.SQ_NO_BOOT) { /* Task 3 installs the bootstrap here */ }` line with the bootstrap (render seams `renderPFD`/`renderPanels` are no-ops until Tasks 4–5):
```js
  if (!window.SQ_NO_BOOT) {
    const $ = s => document.querySelector(s);
    const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
    const state = {
      paused: localStorage.getItem('sq.paused') === '1',
      rate: +localStorage.getItem('sq.rate') || 2,
      last: null, stale: false, timer: null,
    };

    function render(data) {
      const sensors = SQ.flatten(data.Children[0]);
      const limits = SQ.deriveLimits(sensors);
      sensors.forEach(s => s.status = SQ.statusOf(s, limits));
      const alarm = sensors.filter(s => s.status !== 'info' && s.status !== 'off');
      let worst = 'ok'; alarm.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const vmap = { ok:['GO','s-ok','ok'], warn:['WATCH','s-warn','warn'], crit:['CRITICAL','s-crit','crit'] };
      const [vt, vc, vk] = vmap[worst];
      $('#vlamp').className = 'lamp big ' + vc;
      $('#vstate').textContent = vt; $('#vstate').style.color = `var(--${vk})`;
      const counts = { ok:0, warn:0, crit:0 }; alarm.forEach(s => counts[s.status] != null && counts[s.status]++);
      $('#census').innerHTML =
        `<span class="chip"><span class="lamp s-ok"></span>OK <b>${counts.ok}</b></span>` +
        `<span class="chip"><span class="lamp s-warn"></span>WATCH <b>${counts.warn}</b></span>` +
        `<span class="chip"><span class="lamp s-crit"></span>CRIT <b>${counts.crit}</b></span>`;
      if (window.renderPFD) window.renderPFD(sensors, limits);
      if (window.renderPlacard) window.renderPlacard(alarm);
      if (window.renderPanels) window.renderPanels(sensors);
      $('#foot-left').textContent = `LibreHardwareMonitor ${data.Version} · host ${data.Text} · GET /data.json · ${state.rate}s poll`;
      $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
      $('#freshdot').className = 'lamp s-ok';
    }

    async function tick() {
      if (state.paused) return;
      try {
        const r = await fetch('data.json', { cache: 'no-store' });
        const data = await r.json();
        state.last = data; state.stale = false; render(data);
      } catch (e) {
        state.stale = true; $('#freshdot').className = 'lamp s-warn';
        $('#freshtxt').textContent = 'stale — retrying';
      }
    }
    function schedule() { clearInterval(state.timer); state.timer = setInterval(tick, state.rate * 1000); }

    // controls
    document.documentElement.setAttribute('data-theme', localStorage.getItem('sq.theme') || 'dark');
    $('#theme').onclick = () => { const r = document.documentElement;
      const t = r.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      r.setAttribute('data-theme', t); localStorage.setItem('sq.theme', t); };
    const rate = $('#rate'); rate.value = state.rate; $('#ratev').textContent = state.rate + 's';
    rate.oninput = e => { state.rate = +e.target.value; $('#ratev').textContent = state.rate + 's';
      localStorage.setItem('sq.rate', state.rate); schedule(); };
    const pause = $('#pause');
    function paintPause() { pause.textContent = state.paused ? '▶ Resume' : '❚❚ Pause';
      $('#freshdot').className = 'lamp ' + (state.paused ? 's-off' : 's-ok');
      $('#freshtxt').textContent = state.paused ? 'paused' : 'live'; }
    pause.onclick = () => { state.paused = !state.paused; localStorage.setItem('sq.paused', state.paused ? '1':'0');
      paintPause(); if (!state.paused) tick(); };
    paintPause();

    window.SQ._STLABEL = STLABEL;      // shared with render tasks
    tick(); schedule();
  }
```

- [ ] **Step 3: Build the app**

```
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```
Expected: Build succeeded, 0 errors (new resources embed; nothing references deleted files yet).

- [ ] **Step 4: Verify against the live server**

Run the monitor with the web server enabled, open `http://<host>:<port>/`. Expected: dark console loads; masthead shows host + "Hardware Telemetry Console"; Thermal Verdict + census populate from live temps; freshness clock ticks every 2 s; Pause freezes it and flips the dot; the rate slider changes cadence; theme toggle persists across reload. PFD/Subsystems may be empty (Tasks 4–5).

- [ ] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): SQ console shell — index.html, live poll, masthead/verdict/controls

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Primary Flight Display (hero gauges, fix #1 applied)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add `window.renderPFD` + `arcSVG`)

**Interfaces:**
- Consumes: `SQ.pickHero`, `SQ.splitValue`, `SQ.statusOf`, `SQ._STLABEL`; DOM `#pfd`, `#pfdtag`.
- Produces: `window.renderPFD(sensors, limits)`.

- [ ] **Step 1: Add `arcSVG` + `renderPFD` to `console.js`** (inside the `!SQ_NO_BOOT` block, before `tick()`):

```js
    const STGLYPH = { ok:'●', warn:'▲', crit:'✕', info:'·', off:'○' };
    function arcSVG(frac) {
      const R = 30, C = 2 * Math.PI * R, len = C * 0.75;
      const off = len * (1 - Math.max(0, Math.min(1, frac)));
      return `<svg class="arc" viewBox="0 0 78 78"><g transform="rotate(135 39 39)">
        <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--line-soft)" stroke-width="6"
          stroke-linecap="round" stroke-dasharray="${len} ${C}"/>
        <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--c)" stroke-width="6" stroke-linecap="round"
          stroke-dasharray="${len} ${C}" stroke-dashoffset="${off}"
          style="transition:stroke-dashoffset .5s ease"/></g></svg>`;
    }
    window.renderPFD = function (sensors, limits) {
      const H = SQ.pickHero(sensors, limits), pfd = document.querySelector('#pfd');
      pfd.innerHTML = '';
      H.forEach(h => {
        const { n, unit } = SQ.splitValue(h.s.value);
        const u = unit || h.unit || '';
        const st = h.status;
        let arc = '';
        if (h.bounded) { const [lo, hi] = h.bounded; arc = arcSVG((h.s.raw - lo) / (hi - lo)); }
        const rmin = SQ.splitValue(h.s.min).n, rmax = SQ.splitValue(h.s.max).n;
        const range = (h.s.min != null && h.s.min !== '')
          ? `<div class="range">min <b>${rmin}</b> &rarr; max <b>${rmax}</b> ${u}</div>` : '';
        const cell = document.createElement('div');
        cell.className = `cell s-${st}`;
        cell.innerHTML =
          `<div class="k"><span class="name">${h.label}</span><span class="src">${h.s.hw.split(' ').slice(0,2).join(' ')}</span></div>
           <div class="body">${arc}<div class="readout">
             <div class="big"><span class="v">${n}</span><span class="u">${u}</span></div>
             ${range}
             <div class="tags"><span class="tag-stat g-${st}">${STGLYPH[st]} ${(window.SQ._STLABEL)[st]}</span></div>
           </div></div>`;
        pfd.appendChild(cell);
      });
      document.querySelector('#pfdtag').textContent = `${H.length} auto-selected`;
    };
```

- [ ] **Step 2: Build + verify visually**

Rebuild (Task 3 Step 3 command), reload `http://<host>:<port>/`. Expected: hero row fills with CPU Temp/Load/Power, GPU Temp/Mem Jct/Load/Power, RAM Used, Drive Temp. Confirm the three review-fix behaviors:
  1. **value + unit share one baseline** (`65.5 °C`, not `°C` wrapping below);
  2. **min→max is one compact line**, unit shown once;
  3. **CPU Power / GPU Power have NO arc** (big number only); temps/loads/RAM DO have arcs colored by status.

- [ ] **Step 3: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): SQ console PFD hero gauges (arc bounded-only, one-baseline readout)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Subsystem panels + Network collapse + placard (fixes #2, #3 applied)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (add `window.renderPanels`, `window.renderPlacard`, `panelEl`)

**Interfaces:**
- Consumes: `SQ.statusOf`, `SQ.isLimitSensor`, `SQ.RANK`, `SQ._STLABEL`; DOM `#panels`, `#placardsec`, `#subtag`.
- Produces: `window.renderPanels(sensors)`, `window.renderPlacard(alarm)`.

- [ ] **Step 1: Add `renderPlacard` to `console.js`** (inside the boot block):

```js
    window.renderPlacard = function (alarm) {
      const flagged = alarm.filter(s => s.status === 'warn' || s.status === 'crit')
        .sort((a, b) => SQ.RANK[b.status] - SQ.RANK[a.status]);
      const ps = document.querySelector('#placardsec');
      if (!flagged.length) { ps.style.display = 'none'; ps.innerHTML = ''; return; }
      const crit = flagged.some(s => s.status === 'crit');
      ps.style.display = '';
      ps.innerHTML = `<div class="placard ${crit ? 'crit' : ''}">
        <div class="placard-head"><span class="lamp ${crit ? 's-crit' : 's-warn'}"></span>
          <h3>${crit ? 'Thermal Alert' : 'Thermal Watch'}</h3>
          <span class="tag" style="margin-left:auto;font-family:var(--mono);color:var(--muted)">${flagged.length} over band</span></div>
        <ul>${flagged.map(s => `<li><span class="glyph-stat g-${s.status}">${STGLYPH[s.status]}</span>
          <span class="who">${s.text} <small>${s.hw}</small></span>
          <span class="val g-${s.status}">${s.value}</span></li>`).join('')}</ul></div>`;
    };
```

- [ ] **Step 2: Add `panelEl` with per-core collapse (fix #3) + `renderPanels` with network collapse**

```js
    const CLASSLABEL = { cpu:'CPU', gpu:'GPU', igpu:'iGPU', mem:'MEMORY', dimm:'DIMM', nvme:'STORAGE', disk:'DISK', mb:'BOARD', nic:'NET', other:'MISC' };
    const TORDER = ['Temperature','Load','Power','Clock','Fan','Control','Voltage','Current','Data','SmallData','Throughput','Level','Factor','Timing'];
    const isCoreRow = s => /(^|\s)(Core|CPU Core)\s*#?\d/i.test(s.text) && !/Average|Max|Total/i.test(s.text);

    function panelEl(hw, ss, collapsed) {
      let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const cls = ss[0].cls, key = 'sq.panel.' + hw;
      const startCollapsed = localStorage.getItem(key) === '1' || !!collapsed;
      const p = document.createElement('div'); p.className = 'panel' + (startCollapsed ? ' collapsed' : '');
      const temps = ss.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a,b)=>b.raw-a.raw);
      const head = temps[0] ? temps[0].value : (ss.find(s => s.type === 'Load')?.value || '');
      const h = document.createElement('div'); h.className = 'panel-head';
      h.innerHTML = `<span class="lamp s-${worst}"></span><span class="nm">${hw}</span>
        <span class="cls">${CLASSLABEL[cls] || ''}</span>
        <span class="head-stat">${head}<span class="chev">&#9656;</span></span>`;
      h.onclick = () => { p.classList.toggle('collapsed'); localStorage.setItem(key, p.classList.contains('collapsed') ? '1':'0'); };
      p.appendChild(h);
      const body = document.createElement('div'); body.className = 'panel-body';
      const byType = new Map(); ss.forEach(s => { (byType.get(s.type) || byType.set(s.type, []).get(s.type)).push(s); });
      [...byType.entries()].sort((a,b) => TORDER.indexOf(a[0]) - TORDER.indexOf(b[0])).forEach(([type, list]) => {
        body.appendChild(Object.assign(document.createElement('div'), { className: 'tg', textContent: type }));
        // fix #3: on CPU, split per-core rows into a collapsed "show N more"
        const primary = [], extra = [];
        list.forEach(s => (cls === 'cpu' && isCoreRow(s) ? extra : primary).push(s));
        primary.forEach(s => body.appendChild(rowEl(s, type)));
        if (extra.length) {
          const box = document.createElement('div'); box.className = 'extra';
          extra.forEach(s => box.appendChild(rowEl(s, type)));
          const btn = document.createElement('button'); btn.className = 'morebtn';
          btn.textContent = `+ ${extra.length} per-core ${type.toLowerCase()}`;
          btn.onclick = () => { box.classList.toggle('open'); btn.textContent =
            box.classList.contains('open') ? `− hide per-core ${type.toLowerCase()}` : `+ ${extra.length} per-core ${type.toLowerCase()}`; };
          body.appendChild(btn); body.appendChild(box);
        }
      });
      p.appendChild(body); return p;
    }
    function rowEl(s, type) {
      const st = s.status, showBar = (s.type === 'Load' || s.type === 'Level' || s.type === 'Control') && s.raw != null;
      const mm = (s.min != null && s.min !== '' && type === 'Temperature') ? `<span class="mm">${s.min} / ${s.max}</span>` : '';
      const r = document.createElement('div'); r.className = `row ${st}`;
      r.innerHTML = `<span class="glyph-stat g-${st}" title="${SQ._STLABEL[st]}">${st === 'info' ? '' : STGLYPH[st]}</span>
        <span class="rn">${s.text}${mm}</span><span class="rv">${s.value ?? '—'}</span>
        ${showBar ? `<div class="bar ${st==='warn'?'warn':st==='crit'?'crit':''}"><i style="width:${Math.max(0,Math.min(100,s.raw))}%"></i></div>` : ''}`;
      return r;
    }
    window.renderPanels = function (sensors) {
      const panels = document.querySelector('#panels'); panels.innerHTML = '';
      const byHw = new Map();
      sensors.forEach(s => { if (s.cls === 'nic') return; (byHw.get(s.hw) || byHw.set(s.hw, []).get(s.hw)).push(s); });
      const order = ['cpu','gpu','igpu','mem','dimm','nvme','disk','mb','other'];
      [...byHw.entries()].sort((a,b) => order.indexOf(a[1][0].cls) - order.indexOf(b[1][0].cls))
        .forEach(([hw, ss]) => panels.appendChild(panelEl(hw, ss, false)));
      // network collapse: one panel, active interfaces only
      const nics = sensors.filter(s => s.cls === 'nic');
      const active = new Set(nics.filter(s => s.type === 'Throughput' && s.raw > 0).map(s => s.hw));
      const net = nics.filter(s => active.has(s.hw));
      if (net.length) panels.appendChild(panelEl('Network', net, true));
      document.querySelector('#subtag').textContent = `${byHw.size + (net.length ? 1 : 0)} components`;
    };
```

- [ ] **Step 3: Build + verify visually**

Rebuild, reload. Expected:
  - Per-hardware panels appear (CPU, GPU, iGPU, Memory, DIMMs, Storage, Board), status lamp + class tag + headline temp, collapsible (collapse state persists on reload — fix #2 masonry keeps columns balanced, no big gaps).
  - CPU Load/Clock/Power show only summary rows (Average/Max/Total/Package) with a **"+ N per-core …"** button that expands the rest (fix #3).
  - A single **Network** panel (collapsed) lists only interfaces with real traffic; WFP/QoS/idle adapters absent.
  - Force a WATCH (e.g. temporarily lower a temp band, or run a GPU load) and confirm the **placard** appears listing the sensor, then disappears when clear.

- [ ] **Step 4: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): SQ console subsystem panels, network collapse, thermal placard (fixes 2-3)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Delete legacy assets, final regression, docs

**Files:**
- Delete: legacy `Resources/Web/js/*`, `Resources/Web/css/jquery.treeTable.css`, `Resources/Web/css/ohm_web.css`, `Resources/Web/css/custom-theme/**`
- Modify/Create: `docs/local-ui-customizations.md` (or the fork's UI-customizations doc)

- [ ] **Step 1: Delete the now-unused legacy dashboard assets**

```bash
cd LibreHardwareMonitor.Windows.Forms/Resources/Web
git rm js/jquery-1.7.2.js js/jquery-1.7.2.min.js js/jquery.tmpl.js js/jquery.tmpl.min.js \
  js/jquery-ui-1.8.16.custom.min.js js/jquery.treeTable.min.js js/knockout-2.1.0.js js/knockout-2.1.0.min.js \
  js/knockout.mapping-latest.js js/knockout.mapping-latest.min.js js/ohm_web.js \
  css/jquery.treeTable.css css/ohm_web.css
git rm -r css/custom-theme
```
Keep `favicon.ico` and `images/` (referenced by `data.json` `ImageURL`s).

- [ ] **Step 2: Build both target frameworks + run the regression gate**

```
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
```
Expected: both frameworks build (no dangling references to deleted files); all tests pass — the 7 data-contract tests prove `data.json`/CSV are byte-unchanged.

- [ ] **Step 3: Final end-to-end pass**

Run the monitor, open `http://<host>:<port>/` in a fresh browser profile. Walk the spec's verification checklist: hero gauges populate; arcs only on bounded metrics; power/clock as plain readouts; panels group correctly; network collapsed to active interfaces; verdict + census reflect real temps; placard on a warmed sensor; theme/rate/pause/collapse persist across reload; the bare URL serves the new page (legacy assets gone). Compare against `docs/superpowers/assets/2026-07-04-console-reference.html`.

- [ ] **Step 4: Update docs**

Document the new dashboard in `docs/local-ui-customizations.md`: the SQ Telemetry Console (route `/`), that it consumes the unchanged `data.json` client-side, the status model (per-class temp bands + SSD life; everything else info), the auto-heuristic hero selection, the network collapse, theme/rate/pause/collapse persistence, and that the legacy jQuery/Knockout stack was removed. Note the `webtests/console.test.html` self-test and how to run it.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(web): remove legacy jQuery/Knockout dashboard + document SQ console

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** architecture/self-contained/embedded-resources (T2,T3,T6); data.json read-only contract (all tasks, gated by T6 regression); IA masthead/PFD/placard/panels/footer (T3,T4,T5); status model per-class + self-limits + mb-info + triple-encoding (T1 logic + T4/T5 render); hero heuristic bounded-arc rule (T1,T4); network collapse (T5); refresh/controls/persistence (T3); review fixes #1 readout (T2 CSS + T4 markup), #2 masonry (T2), #3 per-core collapse (T5); delete legacy + docs + dual-framework regression (T6). All spec sections mapped.
- **Placeholder scan:** none — every code step shows complete code; the only intentional deferrals (`renderPFD`/`renderPanels` no-op guards in T3) are filled in T4/T5 and named explicitly.
- **Type consistency:** `Sensor` shape {hw,hwid,cls,type,text,value,min,max,raw,id} defined in T1 `flatten`, consumed unchanged in T3–T5. `SQ.pickHero` `Hero` fields {s,label,status,bounded,unit} produced in T1, consumed in T4. `SQ._STLABEL` set in T3 boot, read in T4/T5. `STGLYPH` defined in T4 before T5 uses it (both inside the same boot block; T5 steps are added after T4's). Status strings `ok|warn|crit|info|off` and CSS classes `s-*`/`g-*` consistent across T1/T2/T4/T5.
