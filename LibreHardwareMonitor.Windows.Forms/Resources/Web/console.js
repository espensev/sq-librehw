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
    if ((s.cls === 'gpu' || s.cls === 'igpu') && (t.includes('junction') || t.includes('hot'))) { warn = 95; crit = 105; }
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
      add(c.find(s => s.type === 'Temperature' && (s.text.includes('Tctl') || /package/i.test(s.text))), 'CPU Temp', { bounded: [30, 95], unit: '°C' });
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
  if (!window.SQ_NO_BOOT) {
    const $ = s => document.querySelector(s);
    const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
    const state = {
      paused: localStorage.getItem('sq.paused') === '1',
      rate: +localStorage.getItem('sq.rate') || 2,
      timer: null,
    };

    function render(data) {
      const host = data.Children[0].Text;
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
      $('#host').textContent = host;
      $('#foot-left').textContent = `LibreHardwareMonitor ${data.Version} · host ${host} · GET /data.json · ${state.rate}s poll`;
      if (!state.paused) {   // a forced initial render while paused must not overwrite the "paused" freshness chip
        $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
        $('#freshdot').className = 'lamp s-ok';
      }
    }

    const STGLYPH = { ok:'●', warn:'▲', crit:'✕', info:'·', off:'○' };
    function arcSVG(frac) {
      const R = 30, C = 2 * Math.PI * R, len = C * 0.75;
      const f = isFinite(frac) ? Math.max(0, Math.min(1, frac)) : 0;  // no-reading -> empty arc, not a NaN offset (full arc)
      const off = len * (1 - f);
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
        // NVMe/limit temps report 0 at init and LHM keeps that 0 as Min forever; a component temp is never <=0 in operation, so treat it as no valid min
        const badTempMin = h.s.type === 'Temperature' && !(parseFloat(rmin) > 0);
        const range = (h.s.min == null || h.s.min === '') ? ''
          : badTempMin ? `<div class="range">peak <b>${rmax}</b></div>`
          : `<div class="range"><b>${rmin}</b> &rarr; <b>${rmax}</b></div>`;
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

    const CLASSLABEL = { cpu:'CPU', gpu:'GPU', igpu:'iGPU', mem:'MEMORY', dimm:'DIMM', nvme:'STORAGE', disk:'DISK', mb:'BOARD', nic:'NET', other:'MISC' };
    const TORDER = ['Temperature','Load','Power','Clock','Fan','Control','Voltage','Current','Data','SmallData','Throughput','Level','Factor','Timing'];
    // matches "Core #1", "CPU Core #1", and hybrid-Intel "P-Core #1"/"E-Core #1" (\b handles the hyphen); excludes Average/Max/Total summaries
    const isCoreRow = s => /\bcore\s*#?\d/i.test(s.text) && !/average|max|total/i.test(s.text);

    function panelEl(hw, ss, collapsed) {
      let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const cls = ss[0].cls, key = 'sq.panel.' + hw;
      const stored = localStorage.getItem(key);   // stored choice wins; `collapsed` is only the default when unset (else a re-render reverts the user's toggle)
      const startCollapsed = stored != null ? stored === '1' : !!collapsed;
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
      const mm = (s.min != null && s.min !== '' && type === 'Temperature')
        ? (parseFloat(s.min) > 0 ? `<span class="mm">${s.min} / ${s.max}</span>` : `<span class="mm">peak ${s.max}</span>`)  // hide bogus 0-init temp min
        : '';
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

    async function tick(force) {
      if (state.paused && !force) return;
      try {
        const r = await fetch('data.json', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();
        render(data);
      } catch (e) {
        $('#freshdot').className = 'lamp s-warn';
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
    tick(true); schedule();
  }
})();
