// SQ Telemetry Console - pure model layer. Consumes the unchanged data.json.
(function () {
  const SQ = {};
  const DASHBOARD_STORAGE_KEY = 'sq.dashboard.v1';
  const DEFAULT_HIDDEN_SENSOR_IDS = new Set([
    '/lpc/nct6701d/0/temperature/3',
    '/lpc/nct6701d/0/temperature/5',
    '/lpc/nct6701d/0/temperature/6',
  ]);
  const SENSOR_MOTION = new Map();
  const SENSOR_HISTORY = new Map();
  const SMOOTH_FRACTIONS = new Map();
  const MAX_HISTORY_POINTS = 90;
  const TEMPBANDS = { cpu: [85, 95], gpu: [83, 92], igpu: [83, 92], nvme: [70, 80], dimm: [55, 85], mb: null, mem: null };

  SQ.RANK = { crit: 3, warn: 2, ok: 1, info: 0, off: -1 };
  SQ.DASHBOARD_STORAGE_KEY = DASHBOARD_STORAGE_KEY;
  SQ.DEFAULT_HIDDEN_SENSOR_IDS = Array.from(DEFAULT_HIDDEN_SENSOR_IDS);

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
          value: node.Value, min: node.Min, max: node.Max, raw: node.RawValue,
          rawMin: node.RawMin, rawMax: node.RawMax, id: node.SensorId });
      }
      (node.Children || []).forEach(c => walk(c, hw, hwid));
    })(root, root.Text, undefined);
    return out;
  };

  function cleanStringList(value) {
    return Array.isArray(value) ? [...new Set(value.filter(x => typeof x === 'string' && x.length))] : [];
  }
  function cleanPinnedCards(value) {
    if (!Array.isArray(value)) return [];
    const seen = new Set();
    const out = [];
    value.forEach(card => {
      const id = typeof card === 'string' ? card : card && card.id;
      if (typeof id !== 'string' || !id || seen.has(id)) return;
      seen.add(id);
      out.push({ id, title: typeof card?.title === 'string' ? card.title.slice(0, 80) : '' });
    });
    return out;
  }
  SQ.defaultDashboardState = function () {
    return {
      version: 1,
      hiddenSensorIds: [],
      shownDefaultHiddenSensorIds: [],
      pinnedCards: [],
      panelOrder: [],
      pinnedOrder: [],
      graphsEnabled: false
    };
  };
  SQ.normalizeDashboardState = function (value) {
    const base = SQ.defaultDashboardState();
    if (!value || typeof value !== 'object') return base;
    return {
      version: 1,
      hiddenSensorIds: cleanStringList(value.hiddenSensorIds),
      shownDefaultHiddenSensorIds: cleanStringList(value.shownDefaultHiddenSensorIds),
      pinnedCards: cleanPinnedCards(value.pinnedCards),
      panelOrder: cleanStringList(value.panelOrder),
      pinnedOrder: cleanStringList(value.pinnedOrder),
      graphsEnabled: value.graphsEnabled === true
    };
  };
  SQ.loadDashboardState = function (storage) {
    if (!storage || typeof storage.getItem !== 'function') return SQ.defaultDashboardState();
    try {
      const raw = storage.getItem(DASHBOARD_STORAGE_KEY);
      return raw ? SQ.normalizeDashboardState(JSON.parse(raw)) : SQ.defaultDashboardState();
    } catch {
      return SQ.defaultDashboardState();
    }
  };
  SQ.saveDashboardState = function (storage, value) {
    const state = SQ.normalizeDashboardState(value);
    if (storage && typeof storage.setItem === 'function')
      storage.setItem(DASHBOARD_STORAGE_KEY, JSON.stringify(state));
    return state;
  };
  SQ.isDefaultHiddenSensorId = function (id) {
    return DEFAULT_HIDDEN_SENSOR_IDS.has(id);
  };
  SQ.isSensorHidden = function (s, state) {
    if (!s || !s.id) return false;
    const cfg = SQ.normalizeDashboardState(state);
    if (cfg.hiddenSensorIds.includes(s.id)) return true;
    return DEFAULT_HIDDEN_SENSOR_IDS.has(s.id) && !cfg.shownDefaultHiddenSensorIds.includes(s.id);
  };
  SQ.visibleSensors = function (sensors, state) {
    const cfg = SQ.normalizeDashboardState(state);
    return sensors.filter(s => !SQ.isSensorHidden(s, cfg) && !SQ.isStaticDriveAuxTemp(s));
  };
  SQ.isDashboardSuppressedSensor = function (s, state) {
    return SQ.isSensorHidden(s, state) || SQ.isStaticDriveAuxTemp(s);
  };
  SQ.panelKey = function (hw, sensors) {
    const hwid = sensors && sensors.find(s => s.hwid)?.hwid;
    return hwid || ('hw:' + hw);
  };
  SQ.applyOrder = function (items, order, getKey) {
    const pos = new Map(cleanStringList(order).map((id, i) => [id, i]));
    return items.slice().sort((a, b) => {
      const ak = getKey(a), bk = getKey(b);
      const ai = pos.has(ak) ? pos.get(ak) : Number.MAX_SAFE_INTEGER;
      const bi = pos.has(bk) ? pos.get(bk) : Number.MAX_SAFE_INTEGER;
      if (ai !== bi) return ai - bi;
      return (a.index ?? 0) - (b.index ?? 0);
    });
  };
  SQ.resolvePinnedCards = function (sensors, state, limits) {
    const byId = new Map(sensors.map(s => [s.id, s]));
    const cfg = SQ.normalizeDashboardState(state);
    const ordered = cfg.pinnedOrder.length ? SQ.applyOrder(cfg.pinnedCards, cfg.pinnedOrder, c => c.id) : cfg.pinnedCards;
    return ordered.map(card => {
      const s = byId.get(card.id);
      if (!s) return null;
      return { s, label: card.title || s.text, status: SQ.statusOf(s, limits || {}), bounded: SQ.visualRangeForSensor(s, limits || {}) };
    }).filter(Boolean);
  };

  SQ.isLimitSensor = function (s) {
    const t = (s.text || '').toLowerCase();
    return t.includes('limit') || t.includes('warning temperature') ||
           t.includes('critical temperature') || t.includes('resolution');
  };
  SQ.displayType = function (s) {
    return s.type === 'Temperature' && SQ.isLimitSensor(s) ? 'Limits' : s.type;
  };
  SQ.resetSensorMotion = function () { SENSOR_MOTION.clear(); };
  SQ.trackSensorMotion = function (sensors) {
    sensors.forEach(s => {
      if (s.raw == null || !Number.isFinite(s.raw)) return;
      const m = SENSOR_MOTION.get(s.id) || { count: 0, min: s.raw, max: s.raw };
      m.count++;
      m.min = Math.min(m.min, s.raw);
      m.max = Math.max(m.max, s.raw);
      SENSOR_MOTION.set(s.id, m);
    });
  };
  SQ.isStaticDriveAuxTemp = function (s) {
    if (!s || s.cls !== 'nvme' || s.type !== 'Temperature' || !/^temperature\s+#\d+$/i.test(s.text || '')) return false;
    const m = SENSOR_MOTION.get(s.id);
    return !m || m.count < 5 || (m.max - m.min) <= 1;
  };
  SQ.isPrimaryDriveTemp = function (s) {
    return s && s.cls === 'nvme' && s.type === 'Temperature' && /^temperature$/i.test(s.text || '') && s.raw != null;
  };
  SQ.trackSensorHistory = function (sensors, now) {
    const t = Number.isFinite(now) ? now : Date.now();
    sensors.forEach(s => {
      if (!s.id || s.raw == null || !Number.isFinite(s.raw)) return;
      const h = SENSOR_HISTORY.get(s.id) || [];
      const last = h[h.length - 1];
      if (!last || last.raw !== s.raw || t - last.t > 250)
        h.push({ t, raw: s.raw });
      while (h.length > MAX_HISTORY_POINTS) h.shift();
      SENSOR_HISTORY.set(s.id, h);
    });
  };
  SQ.historyFor = function (id) {
    return SENSOR_HISTORY.get(id) || [];
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
  SQ.visualRangeForSensor = function (s, limits) {
    if (!s) return null;
    if (s.type === 'Load' || s.type === 'Control' || s.type === 'Level') return [0, 100];
    if (s.type !== 'Temperature' || SQ.isLimitSensor(s)) return null;
    const t = (s.text || '').toLowerCase();
    if ((s.cls === 'gpu' || s.cls === 'igpu') && (t.includes('junction') || t.includes('hot'))) return [25, 105];
    if (s.cls === 'cpu') return [30, 95];
    if (s.cls === 'gpu' || s.cls === 'igpu') return [25, 92];
    if (s.cls === 'nvme') return [25, limits?.[s.hwid]?.crit || 80];
    if (s.cls === 'dimm') return [20, limits?.[s.hwid]?.crit || 85];
    return null;
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
    const drives = sensors.filter(SQ.isPrimaryDriveTemp).sort((a, b) => b.raw - a.raw);
    add(drives[0], 'Drive Temp', { bounded: [25, 80], unit: '°C' });
    return H.slice(0, 9);
  };

  window.SQ = SQ;
  if (!window.SQ_NO_BOOT) {
    const $ = s => document.querySelector(s);
    const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
    const STGLYPH = { ok:'●', warn:'▲', crit:'✕', info:'·', off:'○' };
    const CLASSLABEL = { cpu:'CPU', gpu:'GPU', igpu:'iGPU', mem:'MEMORY', dimm:'DIMM', nvme:'STORAGE', disk:'DISK', mb:'BOARD', nic:'NET', other:'MISC' };
    const TORDER = ['Temperature','Limits','Load','Power','Clock','Fan','Control','Voltage','Current','Data','SmallData','Throughput','Level','Factor','Timing'];
    const isCoreRow = s => /\bcore\s*#?\d/i.test(s.text) && !/average|max|total/i.test(s.text);
    const state = {
      paused: localStorage.getItem('sq.paused') === '1',
      rate: +localStorage.getItem('sq.rate') || 2,
      timer: null,
      dashboard: SQ.loadDashboardState(localStorage),
      lastData: null,
      allSensors: [],
      visibleSensors: [],
      panelItems: [],
      customizeOpen: false,
      customizeTab: 'hidden',
      hiddenFilter: '',
      cardFilter: ''
    };

    function esc(v) {
      return String(v ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
    }
    function rootNode(data) {
      return data.Children && data.Children[0] ? data.Children[0] : data;
    }
    function saveDashboard() {
      state.dashboard = SQ.saveDashboardState(localStorage, state.dashboard);
      paintGraphs();
    }
    function rerender() {
      if (state.lastData) render(state.lastData);
    }
    function commitDashboard() {
      saveDashboard();
      rerender();
    }
    function mergeOrder(saved, keys) {
      const set = new Set(keys);
      const merged = cleanStringList(saved).filter(k => set.has(k));
      keys.forEach(k => { if (!merged.includes(k)) merged.push(k); });
      return merged;
    }
    function moveKey(list, key, delta) {
      const i = list.indexOf(key);
      const j = i + delta;
      if (i < 0 || j < 0 || j >= list.length) return list;
      const next = list.slice();
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    }
    function setSensorHidden(id, hidden) {
      const cfg = state.dashboard;
      cfg.hiddenSensorIds = cfg.hiddenSensorIds.filter(x => x !== id);
      cfg.shownDefaultHiddenSensorIds = cfg.shownDefaultHiddenSensorIds.filter(x => x !== id);
      if (hidden) {
        if (!DEFAULT_HIDDEN_SENSOR_IDS.has(id)) cfg.hiddenSensorIds.push(id);
      } else if (DEFAULT_HIDDEN_SENSOR_IDS.has(id)) {
        cfg.shownDefaultHiddenSensorIds.push(id);
      }
      commitDashboard();
    }
    function pinSensor(id) {
      if (!state.dashboard.pinnedCards.some(c => c.id === id))
        state.dashboard.pinnedCards.push({ id, title: '' });
      if (!state.dashboard.pinnedOrder.includes(id))
        state.dashboard.pinnedOrder.push(id);
      commitDashboard();
    }
    function unpinSensor(id) {
      state.dashboard.pinnedCards = state.dashboard.pinnedCards.filter(c => c.id !== id);
      state.dashboard.pinnedOrder = state.dashboard.pinnedOrder.filter(x => x !== id);
      commitDashboard();
    }
    function renamePinned(id, title) {
      const card = state.dashboard.pinnedCards.find(c => c.id === id);
      if (card) card.title = title.slice(0, 80);
      commitDashboard();
    }

    function render(data) {
      state.lastData = data;
      const root = rootNode(data);
      const host = root.Text || 'Sensor';
      const allSensors = SQ.flatten(root);
      SQ.trackSensorMotion(allSensors);
      SQ.trackSensorHistory(allSensors);
      const sensors = SQ.visibleSensors(allSensors, state.dashboard);
      const limits = SQ.deriveLimits(sensors);
      sensors.forEach(s => s.status = SQ.statusOf(s, limits));
      state.allSensors = allSensors;
      state.visibleSensors = sensors;

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
      renderPinnedCards(sensors, limits);
      renderPFD(sensors, limits);
      renderPlacard(alarm);
      renderPanels(sensors);
      renderCustomize();
      $('#host').textContent = host;
      $('#foot-left').textContent = `LibreHardwareMonitor ${data.Version} · host ${host} · GET /data.json · ${state.rate}s poll`;
      if (!state.paused) {
        $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
        $('#freshdot').className = 'lamp s-ok';
      }
    }

    function smoothFraction(id, target) {
      const t = Math.max(0, Math.min(1, Number.isFinite(target) ? target : 0));
      const prev = SMOOTH_FRACTIONS.get(id);
      if (prev == null) { SMOOTH_FRACTIONS.set(id, t); return t; }
      const maxStep = state.rate <= 1 ? 0.14 : 0.18;
      const delta = t - prev;
      const next = prev + Math.sign(delta) * Math.min(Math.abs(delta), maxStep);
      SMOOTH_FRACTIONS.set(id, next);
      return next;
    }
    function arcSVG(id, frac) {
      const R = 30, C = 2 * Math.PI * R, len = C * 0.75;
      const f = smoothFraction(id, frac);
      const off = len * (1 - f);
      return `<svg class="arc" viewBox="0 0 78 78" aria-hidden="true"><g transform="rotate(135 39 39)">
        <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--line-soft)" stroke-width="6"
          stroke-linecap="round" stroke-dasharray="${len} ${C}"/>
        <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--c)" stroke-width="6" stroke-linecap="round"
          stroke-dasharray="${len} ${C}" stroke-dashoffset="${off}"/></g></svg>`;
    }
    function sparklineSVG(sensor, bounded) {
      if (!state.dashboard.graphsEnabled) return '';
      const hist = SQ.historyFor(sensor.id).filter(p => Number.isFinite(p.raw));
      if (hist.length < 2) return '<div class="spark empty"></div>';
      const values = hist.map(p => p.raw);
      let min = bounded ? bounded[0] : Math.min(...values);
      let max = bounded ? bounded[1] : Math.max(...values);
      if (!(max > min)) { min -= 1; max += 1; }
      const w = 120, h = 28;
      const points = hist.map((p, i) => {
        const x = hist.length === 1 ? 0 : (i / (hist.length - 1)) * w;
        const y = h - ((p.raw - min) / (max - min)) * h;
        return `${x.toFixed(1)},${Math.max(0, Math.min(h, y)).toFixed(1)}`;
      }).join(' ');
      return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">
        <polyline points="${points}" fill="none" stroke="var(--c)" stroke-width="2" vector-effect="non-scaling-stroke"/></svg>`;
    }
    function rangeMarkup(s) {
      const rmin = SQ.splitValue(s.min).n, rmax = SQ.splitValue(s.max).n;
      const badTempMin = s.type === 'Temperature' && !(parseFloat(rmin) > 0);
      if (s.min == null || s.min === '') return '';
      return badTempMin ? `<div class="range">peak <b>${esc(rmax)}</b></div>` :
        `<div class="range"><b>${esc(rmin)}</b> &rarr; <b>${esc(rmax)}</b></div>`;
    }
    function cardEl(h, pinned) {
      const { n, unit } = SQ.splitValue(h.s.value);
      const u = unit || h.unit || '';
      const st = h.status;
      const bounded = h.bounded || SQ.visualRangeForSensor(h.s, {});
      let arc = '';
      if (bounded) { const [lo, hi] = bounded; arc = arcSVG(h.s.id, (h.s.raw - lo) / (hi - lo)); }
      const cell = document.createElement('div');
      cell.className = `cell s-${st}${pinned ? ' pinned' : ''}${state.dashboard.graphsEnabled ? ' graph-on' : ''}`;
      const source = (h.s.hw || '').split(' ').slice(0, 3).join(' ');
      cell.innerHTML =
        `<div class="k"><span class="name">${esc(h.label)}</span><span class="src">${esc(source)}</span></div>
         <div class="body">${arc}<div class="readout">
           <div class="big"><span class="v">${esc(n)}</span><span class="u">${esc(u)}</span></div>
           ${rangeMarkup(h.s)}
           <div class="tags"><span class="tag-stat g-${st}">${STGLYPH[st]} ${STLABEL[st]}</span></div>
         </div></div>${sparklineSVG(h.s, bounded)}`;
      return cell;
    }
    function renderPinnedCards(sensors, limits) {
      const cards = SQ.resolvePinnedCards(sensors, state.dashboard, limits);
      const sec = $('#pinnedsec'), grid = $('#pinned');
      grid.innerHTML = '';
      sec.style.display = cards.length ? '' : 'none';
      cards.forEach(h => grid.appendChild(cardEl(h, true)));
      $('#pinnedtag').textContent = `${cards.length} pinned`;
    }
    function renderPFD(sensors, limits) {
      const H = SQ.pickHero(sensors, limits), pfd = $('#pfd');
      pfd.innerHTML = '';
      H.forEach(h => pfd.appendChild(cardEl(h, false)));
      $('#pfdtag').textContent = `${H.length} auto-selected`;
    }
    function renderPlacard(alarm) {
      const flagged = alarm.filter(s => s.status === 'warn' || s.status === 'crit')
        .sort((a, b) => SQ.RANK[b.status] - SQ.RANK[a.status]);
      const ps = $('#placardsec');
      if (!flagged.length) { ps.style.display = 'none'; ps.innerHTML = ''; return; }
      const crit = flagged.some(s => s.status === 'crit');
      ps.style.display = '';
      ps.innerHTML = `<div class="placard ${crit ? 'crit' : ''}">
        <div class="placard-head"><span class="lamp ${crit ? 's-crit' : 's-warn'}"></span>
          <h3>${crit ? 'Thermal Alert' : 'Thermal Watch'}</h3>
          <span class="tag">${flagged.length} over band</span></div>
        <ul>${flagged.map(s => `<li><span class="glyph-stat g-${s.status}">${STGLYPH[s.status]}</span>
          <span class="who">${esc(s.text)} <small>${esc(s.hw)}</small></span>
          <span class="val g-${s.status}">${esc(s.value)}</span></li>`).join('')}</ul></div>`;
    }
    function rowEl(s, type) {
      const st = s.status, showBar = (s.type === 'Load' || s.type === 'Level' || s.type === 'Control') && s.raw != null;
      const mm = (s.min != null && s.min !== '' && type === 'Temperature')
        ? (parseFloat(s.min) > 0 ? `<span class="mm">${esc(s.min)} / ${esc(s.max)}</span>` : `<span class="mm">peak ${esc(s.max)}</span>`)
        : '';
      const r = document.createElement('div'); r.className = `row ${st}`;
      r.innerHTML = `<span class="glyph-stat g-${st}" title="${STLABEL[st]}">${st === 'info' ? '' : STGLYPH[st]}</span>
        <span class="rn">${esc(s.text)}${mm}</span><span class="rv">${esc(s.value ?? '-')}</span>
        ${showBar ? `<div class="bar ${st==='warn'?'warn':st==='crit'?'crit':''}"><i style="width:${Math.max(0,Math.min(100,s.raw))}%"></i></div>` : ''}`;
      return r;
    }
    function panelEl(item) {
      const { hw, ss, collapsed } = item;
      let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const cls = ss[0].cls, collapseKey = 'sq.panel.' + hw;
      const stored = localStorage.getItem(collapseKey);
      const startCollapsed = stored != null ? stored === '1' : !!collapsed;
      const p = document.createElement('div'); p.className = 'panel' + (startCollapsed ? ' collapsed' : '');
      const temps = ss.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a,b)=>b.raw-a.raw);
      const head = temps[0] ? temps[0].value : (ss.find(s => s.type === 'Load')?.value || '');
      const h = document.createElement('div'); h.className = 'panel-head';
      h.innerHTML = `<span class="lamp s-${worst}"></span><span class="nm">${esc(hw)}</span>
        <span class="cls">${CLASSLABEL[cls] || ''}</span>
        <span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
      h.onclick = () => { p.classList.toggle('collapsed'); localStorage.setItem(collapseKey, p.classList.contains('collapsed') ? '1':'0'); };
      p.appendChild(h);
      const body = document.createElement('div'); body.className = 'panel-body';
      const byType = new Map(); ss.forEach(s => { const t = SQ.displayType(s); (byType.get(t) || byType.set(t, []).get(t)).push(s); });
      [...byType.entries()].sort((a,b) => TORDER.indexOf(a[0]) - TORDER.indexOf(b[0])).forEach(([type, list]) => {
        body.appendChild(Object.assign(document.createElement('div'), { className: 'tg', textContent: type }));
        const primary = [], extra = [];
        list.forEach(s => (cls === 'cpu' && isCoreRow(s) ? extra : primary).push(s));
        primary.forEach(s => body.appendChild(rowEl(s, type)));
        if (extra.length) {
          const box = document.createElement('div'); box.className = 'extra';
          extra.forEach(s => box.appendChild(rowEl(s, type)));
          const btn = document.createElement('button'); btn.className = 'morebtn';
          btn.textContent = `+ ${extra.length} per-core ${type.toLowerCase()}`;
          btn.onclick = e => { e.stopPropagation(); box.classList.toggle('open'); btn.textContent =
            box.classList.contains('open') ? `- hide per-core ${type.toLowerCase()}` : `+ ${extra.length} per-core ${type.toLowerCase()}`; };
          body.appendChild(btn); body.appendChild(box);
        }
      });
      p.appendChild(body); return p;
    }
    function buildPanelItems(sensors) {
      const byHw = new Map();
      sensors.forEach(s => { if (s.cls === 'nic') return; (byHw.get(s.hw) || byHw.set(s.hw, []).get(s.hw)).push(s); });
      const order = ['cpu','gpu','igpu','mem','dimm','nvme','disk','mb','other'];
      const items = [...byHw.entries()].map(([hw, ss], index) => ({ hw, ss, key: SQ.panelKey(hw, ss), collapsed: false, index }))
        .sort((a,b) => {
          const ai = order.indexOf(a.ss[0].cls), bi = order.indexOf(b.ss[0].cls);
          return (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.index - b.index;
        }).map((item, index) => Object.assign(item, { index }));
      const nics = sensors.filter(s => s.cls === 'nic');
      const active = new Set(nics.filter(s => s.type === 'Throughput' && s.raw > 0).map(s => s.hw));
      const net = nics.filter(s => active.has(s.hw));
      if (net.length) items.push({ hw: 'Network', ss: net, key: 'panel:network', collapsed: true, index: items.length });
      return items;
    }
    function renderPanels(sensors) {
      const panels = $('#panels');
      panels.innerHTML = '';
      state.panelItems = buildPanelItems(sensors);
      const ordered = SQ.applyOrder(state.panelItems, state.dashboard.panelOrder, item => item.key);
      ordered.forEach(item => panels.appendChild(panelEl(item)));
      $('#subtag').textContent = `${ordered.length} components`;
    }

    function sensorSearchText(s) {
      return `${s.hw} ${s.text} ${s.type} ${s.value} ${s.id}`.toLowerCase();
    }
    function sensorButtonLabel(s) {
      return SQ.isSensorHidden(s, state.dashboard) ? 'Show' : 'Hide';
    }
    function renderSensorRows(container, filter, mode) {
      const q = filter.trim().toLowerCase();
      const rows = state.allSensors.filter(s => !q || sensorSearchText(s).includes(q)).slice(0, 220);
      container.innerHTML = rows.map(s => {
        const hidden = SQ.isSensorHidden(s, state.dashboard);
        const pinned = state.dashboard.pinnedCards.some(c => c.id === s.id);
        const action = mode === 'cards' ? (pinned ? 'unpin' : 'pin') : (hidden ? 'show' : 'hide');
        const label = mode === 'cards' ? (pinned ? 'Unpin' : 'Pin') : sensorButtonLabel(s);
        const badge = DEFAULT_HIDDEN_SENSOR_IDS.has(s.id) ? '<span class="mini-badge">default</span>' : '';
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(s.text)}</b> ${badge}<span>${esc(s.hw)} · ${esc(s.type)} · ${esc(s.value ?? '-')}</span><code>${esc(s.id)}</code></div>
          <button class="iconbtn" data-action="${action}" data-id="${esc(s.id)}">${label}</button>
        </div>`;
      }).join('') || '<div class="empty-note">No sensors</div>';
    }
    function renderPinnedEditor() {
      const box = $('#pinnedList');
      const sensors = state.allSensors;
      const resolved = SQ.resolvePinnedCards(sensors, state.dashboard, {});
      const currentOrder = mergeOrder(state.dashboard.pinnedOrder, state.dashboard.pinnedCards.map(c => c.id));
      box.innerHTML = resolved.map(h => {
        const id = h.s.id;
        const card = state.dashboard.pinnedCards.find(c => c.id === id);
        const i = currentOrder.indexOf(id);
        return `<div class="order-row">
          <div><b>${esc(card?.title || h.s.text)}</b><span>${esc(h.s.hw)} · ${esc(h.s.value ?? '-')}</span></div>
          <input class="title-input" data-action="rename" data-id="${esc(id)}" value="${esc(card?.title || '')}" placeholder="${esc(h.s.text)}">
          <button class="iconbtn" data-action="pin-up" data-id="${esc(id)}" ${i <= 0 ? 'disabled' : ''}>Up</button>
          <button class="iconbtn" data-action="pin-down" data-id="${esc(id)}" ${i >= currentOrder.length - 1 ? 'disabled' : ''}>Down</button>
          <button class="iconbtn" data-action="unpin" data-id="${esc(id)}">Remove</button>
        </div>`;
      }).join('') || '<div class="empty-note">No pinned cards</div>';
    }
    function renderLayoutEditor() {
      const box = $('#panelOrderList');
      const keys = state.panelItems.map(i => i.key);
      const order = mergeOrder(state.dashboard.panelOrder, keys);
      const panels = SQ.applyOrder(state.panelItems, order, item => item.key);
      box.innerHTML = panels.map((item, i) => `<div class="order-row">
        <div><b>${esc(item.hw)}</b><span>${esc(CLASSLABEL[item.ss[0].cls] || 'MISC')} · ${item.ss.length} sensors</span></div>
        <button class="iconbtn" data-action="panel-up" data-id="${esc(item.key)}" ${i <= 0 ? 'disabled' : ''}>Up</button>
        <button class="iconbtn" data-action="panel-down" data-id="${esc(item.key)}" ${i >= panels.length - 1 ? 'disabled' : ''}>Down</button>
      </div>`).join('');
    }
    function renderCustomize() {
      const drawer = $('#customizeDrawer');
      const scrim = $('#customizeScrim');
      if (!drawer || !scrim) return;
      drawer.classList.toggle('open', state.customizeOpen);
      scrim.classList.toggle('open', state.customizeOpen);
      drawer.setAttribute('aria-hidden', state.customizeOpen ? 'false' : 'true');
      document.querySelectorAll('[data-tab]').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === state.customizeTab);
      });
      document.querySelectorAll('[data-pane]').forEach(pane => {
        pane.hidden = pane.dataset.pane !== state.customizeTab;
      });
      if (!state.customizeOpen) return;
      $('#hiddenSearch').value = state.hiddenFilter;
      $('#cardSearch').value = state.cardFilter;
      renderSensorRows($('#hiddenList'), state.hiddenFilter, 'hidden');
      renderSensorRows($('#cardList'), state.cardFilter, 'cards');
      renderPinnedEditor();
      renderLayoutEditor();
    }
    function paintGraphs() {
      const btn = $('#graphs');
      btn.textContent = state.dashboard.graphsEnabled ? 'Graphs On' : 'Graphs';
      btn.setAttribute('aria-pressed', state.dashboard.graphsEnabled ? 'true' : 'false');
      btn.classList.toggle('active', state.dashboard.graphsEnabled);
    }

    async function tick(force) {
      if (state.paused && !force) return;
      try {
        const r = await fetch('data.json', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();
        render(data);
      } catch (e) {
        $('#freshdot').className = 'lamp s-warn';
        $('#freshtxt').textContent = 'stale - retrying';
      }
    }
    function schedule() { clearInterval(state.timer); state.timer = setInterval(tick, state.rate * 1000); }

    document.documentElement.setAttribute('data-theme', localStorage.getItem('sq.theme') || 'dark');
    $('#theme').onclick = () => { const r = document.documentElement;
      const t = r.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      r.setAttribute('data-theme', t); localStorage.setItem('sq.theme', t); };
    const rate = $('#rate'); rate.value = state.rate; $('#ratev').textContent = state.rate + 's';
    rate.oninput = e => { state.rate = +e.target.value; $('#ratev').textContent = state.rate + 's';
      localStorage.setItem('sq.rate', state.rate); schedule(); };
    const pause = $('#pause');
    function paintPause() { pause.textContent = state.paused ? 'Resume' : 'Pause';
      $('#freshdot').className = 'lamp ' + (state.paused ? 's-off' : 's-ok');
      $('#freshtxt').textContent = state.paused ? 'paused' : 'live'; }
    pause.onclick = () => { state.paused = !state.paused; localStorage.setItem('sq.paused', state.paused ? '1':'0');
      paintPause(); if (!state.paused) tick(); };
    $('#graphs').onclick = () => { state.dashboard.graphsEnabled = !state.dashboard.graphsEnabled; commitDashboard(); };
    $('#customize').onclick = () => { state.customizeOpen = true; renderCustomize(); };
    $('#drawerClose').onclick = () => { state.customizeOpen = false; renderCustomize(); };
    $('#customizeScrim').onclick = () => { state.customizeOpen = false; renderCustomize(); };
    $('#hiddenSearch').oninput = e => { state.hiddenFilter = e.target.value; renderCustomize(); };
    $('#cardSearch').oninput = e => { state.cardFilter = e.target.value; renderCustomize(); };
    document.querySelectorAll('[data-tab]').forEach(btn => btn.onclick = () => { state.customizeTab = btn.dataset.tab; renderCustomize(); });
    $('#customizeDrawer').addEventListener('change', e => {
      const input = e.target.closest('[data-action="rename"]');
      if (input) renamePinned(input.dataset.id, input.value);
    });
    $('#customizeDrawer').addEventListener('click', e => {
      const btn = e.target.closest('[data-action]');
      if (!btn) return;
      const id = btn.dataset.id;
      switch (btn.dataset.action) {
        case 'hide': setSensorHidden(id, true); break;
        case 'show': setSensorHidden(id, false); break;
        case 'pin': pinSensor(id); break;
        case 'unpin': unpinSensor(id); break;
        case 'pin-up':
        case 'pin-down': {
          const keys = mergeOrder(state.dashboard.pinnedOrder, state.dashboard.pinnedCards.map(c => c.id));
          state.dashboard.pinnedOrder = moveKey(keys, id, btn.dataset.action === 'pin-up' ? -1 : 1);
          commitDashboard();
          break;
        }
        case 'panel-up':
        case 'panel-down': {
          const keys = mergeOrder(state.dashboard.panelOrder, state.panelItems.map(i => i.key));
          state.dashboard.panelOrder = moveKey(keys, id, btn.dataset.action === 'panel-up' ? -1 : 1);
          commitDashboard();
          break;
        }
        case 'reset-hidden':
          state.dashboard.hiddenSensorIds = [];
          state.dashboard.shownDefaultHiddenSensorIds = [];
          commitDashboard();
          break;
        case 'reset-panels':
          state.dashboard.panelOrder = [];
          commitDashboard();
          break;
        case 'clear-pinned':
          state.dashboard.pinnedCards = [];
          state.dashboard.pinnedOrder = [];
          commitDashboard();
          break;
      }
    });

    paintPause();
    paintGraphs();
    window.SQ._STLABEL = STLABEL;
    tick(true); schedule();
  }
})();
