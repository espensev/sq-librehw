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
  if (!window.SQ_NO_BOOT) {
    const $ = s => document.querySelector(s);
    const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
    const state = {
      paused: localStorage.getItem('sq.paused') === '1',
      rate: +localStorage.getItem('sq.rate') || 2,
      last: null, stale: false, timer: null,
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
      $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
      $('#freshdot').className = 'lamp s-ok';
    }

    async function tick(force) {
      if (state.paused && !force) return;
      try {
        const r = await fetch('data.json', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
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
    tick(true); schedule();
  }
})();
