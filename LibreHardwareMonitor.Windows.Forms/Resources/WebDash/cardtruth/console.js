// SQ Telemetry Console - pure model layer. Consumes the unchanged data.json.
(function () {
  const SQ = {};
  const DASHBOARD_STORAGE_KEY = 'sq.dashboard.preview.cardtruth';
  const SENSOR_MOTION = new Map();
  const SENSOR_HISTORY = new Map();
  const SMOOTH_FRACTIONS = new Map();
  const MAX_HISTORY_POINTS = 90;
  const TEMPBANDS = { cpu: [85, 95], gpu: [83, 92], igpu: [83, 92], nvme: [70, 80], dimm: [55, 85], mb: null, mem: null };

  SQ.RANK = { crit: 3, warn: 2, ok: 1, info: 0, off: -1 };
  SQ.DASHBOARD_STORAGE_KEY = DASHBOARD_STORAGE_KEY;

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
  function clampRate(n) {
    n = Math.round(Number(n));
    if (!Number.isFinite(n)) return 2;
    return Math.max(1, Math.min(10, n));
  }
  function cleanCollapsedMap(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => { if (typeof k === 'string' && k.length) out[k] = !!value[k]; });
    return out;
  }
  function cleanCardStyleMap(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => {
        if (k && (value[k] === 'gauge' || value[k] === 'number' || value[k] === 'graph')) out[k] = value[k];
      });
    return out;
  }
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
      Object.keys(value).forEach(k => { const l = cleanStringList(value[k]); if (k && l.length) out[k] = l; });
    return out;
  }
  SQ.defaultDashboardState = function () {
    return {
      version: 1,
      hiddenSensorIds: [],
      pinnedCards: [],
      panelOrder: [],
      pinnedOrder: [],
      graphsEnabled: false,
      paused: false,
      rate: 2,
      theme: 'dark',
      collapsedPanels: {},
      cardStyle: {},
      rangeOverrides: {},
      observedMax: {},
      rowOrder: {},
      netAdapterOrder: [],
      hiddenNetAdapters: []
    };
  };
  SQ.normalizeDashboardState = function (value) {
    const base = SQ.defaultDashboardState();
    if (!value || typeof value !== 'object') return base;
    return {
      version: 1,
      hiddenSensorIds: cleanStringList(value.hiddenSensorIds),
      pinnedCards: cleanPinnedCards(value.pinnedCards),
      panelOrder: cleanStringList(value.panelOrder),
      pinnedOrder: cleanStringList(value.pinnedOrder),
      graphsEnabled: value.graphsEnabled === true,
      paused: value.paused === true,
      rate: clampRate(value.rate),
      theme: value.theme === 'light' ? 'light' : 'dark',
      collapsedPanels: cleanCollapsedMap(value.collapsedPanels),
      cardStyle: cleanCardStyleMap(value.cardStyle),
      rangeOverrides: cleanRangeOverrides(value.rangeOverrides),
      observedMax: cleanNumberMap(value.observedMax),
      rowOrder: cleanOrderMap(value.rowOrder),
      netAdapterOrder: cleanStringList(value.netAdapterOrder),
      hiddenNetAdapters: cleanStringList(value.hiddenNetAdapters)
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
  SQ.isPanelCollapsed = function (state, hw, defaultCollapsed) {
    const v = SQ.normalizeDashboardState(state).collapsedPanels[hw];
    return v == null ? !!defaultCollapsed : v === true;
  };
  SQ.isSensorHidden = function (s, state) {
    if (!s || !s.id) return false;
    return SQ.normalizeDashboardState(state).hiddenSensorIds.includes(s.id);
  };
  SQ.visibleSensors = function (sensors, state) {
    const cfg = SQ.normalizeDashboardState(state);
    return sensors.filter(s => !SQ.isSensorHidden(s, cfg) && !SQ.isStaticDriveAuxTemp(s) && !SQ.isStaticMbTemp(s));
  };
  SQ.isDashboardSuppressedSensor = function (s, state) {
    return SQ.isSensorHidden(s, state) || SQ.isStaticDriveAuxTemp(s) || SQ.isStaticMbTemp(s);
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
  SQ.isStaticMbTemp = function (s) {
    if (!s || s.cls !== 'mb' || s.type !== 'Temperature') return false;
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
  SQ.derivedPowerLimit = function () { return null; };   // real implementation lands with power-limit tracking
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
  SQ.fanControlFor = function (fan, sensors) {
    if (!fan || fan.type !== 'Fan' || !Array.isArray(sensors)) return null;
    return sensors.find(s => s.type === 'Control' && s.hwid === fan.hwid && s.text === fan.text && s.raw != null) || null;
  };
  SQ.gaugeRangeFor = function (rangeInfo, sensor, control) {
    if (control) return { lo: 0, hi: 100, source: 'control' };
    if (!rangeInfo || !sensor || sensor.raw == null) return null;
    if (rangeInfo.source === 'peak') return null;
    return rangeInfo;
  };
  SQ.cardStyleFor = function (styleValue, hasRange, graphsEnabled) {
    if (styleValue === 'gauge') return { arc: !!hasRange, spark: !!graphsEnabled };
    if (styleValue === 'number') return { arc: false, spark: !!graphsEnabled };
    if (styleValue === 'graph') return { arc: !!hasRange, spark: true };
    return { arc: !!hasRange, spark: !!graphsEnabled };
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
    sensors.filter(s => s.type === 'Fan' && s.raw > 0).sort((a, b) => b.raw - a.raw).slice(0, 4)
      .forEach(f => add(f, f.text, { unit: 'rpm' }));
    return H.slice(0, 12);
  };

  window.SQ = SQ;
  if (!window.SQ_NO_BOOT) {
    const $ = s => document.querySelector(s);
    const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
    const STGLYPH = { ok:'●', warn:'▲', crit:'✕', info:'·', off:'○' };
    const CLASSLABEL = { cpu:'CPU', gpu:'GPU', igpu:'iGPU', mem:'MEMORY', dimm:'DIMM', nvme:'STORAGE', disk:'DISK', mb:'BOARD', nic:'NET', other:'MISC' };
    const TORDER = ['Temperature','Limits','Load','Power','Clock','Fan','Control','Voltage','Current','Data','SmallData','Throughput','Level','Factor','Timing'];
    const TICONS = {
      temp:  '<path d="M8 3a2 2 0 0 1 4 0v7.3a4.5 4.5 0 1 1-4 0z" fill="none" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="14" r="2" fill="currentColor"/>',
      load:  '<path d="M3 13a7 7 0 0 1 14 0" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 13 13.5 8" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="13" r="1.4" fill="currentColor"/>',
      fan:   '<circle cx="10" cy="10" r="1.8" fill="currentColor"/><path d="M10 8.2C10 4 13 3 15 5c-1 2-3 3-5 3.2M11.8 10c4.2 0 5.2 3 3.2 5-2-1-3-3-3.2-5M10 11.8C10 16 7 17 5 15c1-2 3-3 5-3.2M8.2 10C4 10 3 7 5 5c2 1 3 3 3.2 5" fill="none" stroke="currentColor" stroke-width="1.4"/>',
      power: '<path d="M11 2 4 12h5l-1 6 7-10h-5z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>',
      clock: '<circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 6v4l3 2" fill="none" stroke="currentColor" stroke-width="1.6"/>',
      data:  '<path d="M2 11h4l2-5 4 9 2-5h4" fill="none" stroke="currentColor" stroke-width="1.6"/>'
    };
    const tIcon = kind => `<svg class="ticon" viewBox="0 0 20 20" aria-hidden="true">${TICONS[kind] || TICONS.data}</svg>`;
    const isCoreRow = s => /\bcore\s*#?\d/i.test(s.text) && !/average|max|total/i.test(s.text);
    const dashboard0 = SQ.migrateLegacyState(localStorage, SQ.loadDashboardState(localStorage));
    SQ.saveDashboardState(localStorage, dashboard0);
    const state = {
      paused: dashboard0.paused,
      rate: dashboard0.rate,
      timer: null,
      dragging: false,
      dashboard: dashboard0,
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
    function ctlCluster(id, label, opts) {
      const pinned = SQ.isPinned(state.dashboard, id);
      const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
      const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
      return pin + hide;
    }
    function rootNode(data) {
      return data.Children && data.Children[0] ? data.Children[0] : data;
    }
    function saveDashboard() {
      state.dashboard = SQ.saveDashboardState(localStorage, state.dashboard);
      paintGraphs();
    }
    function rerender() {
      if (state.dragging) return;
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
    function rowGroupKey(panelKey, type) {
      return `${panelKey}|${type}`;
    }
    function orderedRows(panelKey, type, rows) {
      const key = rowGroupKey(panelKey, type);
      return SQ.applyOrder(rows, state.dashboard.rowOrder[key] || [], s => s.id);
    }
    function moveRow(groupKey, id, delta) {
      const group = Array.from(document.querySelectorAll('.row-group')).find(el => el.dataset.rowGroup === groupKey);
      if (!group) return;
      const rows = Array.from(group.querySelectorAll('.row'))
        .map(el => el.dataset.key)
        .filter(k => typeof k === 'string' && k.length);
      state.dashboard.rowOrder[groupKey] = moveKey(mergeOrder(state.dashboard.rowOrder[groupKey], rows), id, delta);
      commitDashboard();
    }
    function setSensorHidden(id, hidden) {
      const cfg = state.dashboard;
      cfg.hiddenSensorIds = cfg.hiddenSensorIds.filter(x => x !== id);
      if (hidden) cfg.hiddenSensorIds.push(id);
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
    function rangeMarkup(s) {
      const rmin = SQ.splitValue(s.min).n, rmax = SQ.splitValue(s.max).n;
      const badTempMin = s.type === 'Temperature' && !(parseFloat(rmin) > 0);
      if (s.min == null || s.min === '') return '';
      return badTempMin ? `<div class="range">peak <b>${esc(rmax)}</b></div>` :
        `<div class="range"><b>${esc(rmin)}</b> &rarr; <b>${esc(rmax)}</b></div>`;
    }
    function cardEl(h, pinned) {
      const { n, unit } = h.s.raw == null ? { n: '—', unit: '' } : SQ.splitValue(h.s.value);
      const u = unit || h.unit || '';
      const st = h.status;
      const kind = SQ.kindOf(h.s.type);
      const styleVal = state.dashboard.cardStyle[h.s.id];
      const rr = h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                           : SQ.rangeFor(h.s, {}, state.dashboard);
      const range = rr ? [rr.lo, rr.hi] : null;
      const ctrl = kind === 'fan' ? SQ.fanControlFor(h.s, state.allSensors) : null;
      const gaugeRange = SQ.gaugeRangeFor(rr, h.s, ctrl);
      const fx = SQ.cardStyleFor(styleVal, !!gaugeRange && h.s.raw != null, state.dashboard.graphsEnabled);
      let arc = '';
      if (fx.arc && ctrl) arc = arcSVG(h.s.id, ctrl.raw / 100);
      else if (fx.arc && gaugeRange) arc = arcSVG(h.s.id, (h.s.raw - gaugeRange.lo) / (gaugeRange.hi - gaugeRange.lo));
      const isHealth = (h.s.type === 'Temperature' && !SQ.isLimitSensor(h.s)) ||
                       (h.s.type === 'Level' && (h.s.text || '').toLowerCase().includes('life'));
      const chip = isHealth && (st === 'ok' || st === 'warn' || st === 'crit')
        ? `<span class="chip-state g-${st}">${STGLYPH[st]} ${STLABEL[st]}</span>` : '';
      const trend = SQ.trendFor(h.s.id, kind);
      const trendHtml = trend
        ? `<span class="trend">${trend.direction === 'rising' ? '&#8599;' : '&#8600;'} ${Math.abs(trend.rate).toFixed(Math.abs(trend.rate) >= 10 ? 0 : 2)} ${esc(trend.rateUnit)}</span>`
        : '<span class="trend"></span>';
      const ceil = fx.arc && !ctrl && gaugeRange && gaugeRange.source !== 'band' ? `<span class="ceil">/ ${esc(String(gaugeRange.hi))}</span>` : '';
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
           <div class="meta">${rangeMarkup(h.s) || '<div class="range"></div>'}${trendHtml}${ctrl ? `<span class="cmd">cmd ${esc(ctrl.value)}</span>` : ''}</div>
         </div></div>${fx.spark ? sparkAreaSVG(h.s, range) : ''}`;
      const showHide = !pinned;
      const ctl = document.createElement('div');
      ctl.className = 'cell-ctl';
      ctl.innerHTML = (pinned ? `<button class="grip" aria-label="Drag to reorder ${esc(h.label)}" title="Drag to reorder">&#8942;&#8942;</button>` : '') + ctlCluster(h.s.id, h.label, { hide: showHide });
      cell.appendChild(ctl);
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
    function rowEl(s, type, groupKey) {
      const st = s.status, showBar = (s.type === 'Load' || s.type === 'Level' || s.type === 'Control') && s.raw != null;
      const mm = (s.min != null && s.min !== '' && type === 'Temperature')
        ? (parseFloat(s.min) > 0 ? `<span class="mm">${esc(s.min)} / ${esc(s.max)}</span>` : `<span class="mm">peak ${esc(s.max)}</span>`)
        : '';
      const r = document.createElement('div'); r.className = `row ${st}`;
      r.dataset.key = s.id;
      r.dataset.rowGroup = groupKey;
      const fanCtl = s.type === 'Fan' ? SQ.fanControlFor(s, state.allSensors) : null;
      r.innerHTML = `<button class="grip row-grip" aria-label="Drag to reorder ${esc(s.text)}" title="Drag to reorder">&#8942;&#8942;</button>
        <span class="glyph-stat g-${st}" title="${STLABEL[st]}">${st === 'info' ? '' : STGLYPH[st]}</span>
        <span class="rn">${esc(s.text)}${mm}</span><span class="rv">${esc(s.raw == null ? '—' : (s.value ?? '-'))}${fanCtl ? ` <small class="rvcmd">· ${esc(fanCtl.value)}</small>` : ''}</span>
        ${showBar ? `<div class="bar ${st==='warn'?'warn':st==='crit'?'crit':''}"><i style="width:${Math.max(0,Math.min(100,s.raw))}%"></i></div>` : ''}`;
      const rctl = document.createElement('span');
      rctl.className = 'row-ctl';
      rctl.innerHTML = `<button class="ctl" data-act="row-up" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(s.text)} up" title="Move up">&#9650;</button>` +
        `<button class="ctl" data-act="row-down" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(s.text)} down" title="Move down">&#9660;</button>` +
        ctlCluster(s.id, s.text, { hide: true });
      r.appendChild(rctl);
      return r;
    }
    function panelEl(item) {
      const { hw, ss, collapsed } = item;
      let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const cls = ss[0].cls;
      const startCollapsed = SQ.isPanelCollapsed(state.dashboard, hw, collapsed);
      const p = document.createElement('div'); p.className = 'panel' + (startCollapsed ? ' collapsed' : '');
      p.dataset.key = item.key;
      const temps = ss.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a,b)=>b.raw-a.raw);
      const head = temps[0] ? temps[0].value : (ss.find(s => s.type === 'Load')?.value || '');
      const h = document.createElement('div'); h.className = 'panel-head';
      h.innerHTML = `<button class="grip" aria-label="Drag to reorder ${esc(hw)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        `<span class="lamp s-${worst}"></span><span class="nm">${esc(hw)}</span>` +
        `<span class="cls">${CLASSLABEL[cls] || ''}</span>` +
        `<span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
      h.onclick = () => {
        p.classList.toggle('collapsed');
        state.dashboard.collapsedPanels[hw] = p.classList.contains('collapsed');
        saveDashboard();
      };
      p.appendChild(h);
      const body = document.createElement('div'); body.className = 'panel-body';
      const byType = new Map(); ss.forEach(s => { const t = SQ.displayType(s); (byType.get(t) || byType.set(t, []).get(t)).push(s); });
      [...byType.entries()].sort((a,b) => TORDER.indexOf(a[0]) - TORDER.indexOf(b[0])).forEach(([type, list]) => {
        body.appendChild(Object.assign(document.createElement('div'), { className: 'tg', textContent: type }));
        list = orderedRows(item.key, type, list);
        const groupKey = rowGroupKey(item.key, type);
        const group = document.createElement('div');
        group.className = 'row-group';
        group.dataset.rowGroup = groupKey;
        const primary = [], extra = [];
        list.forEach(s => (cls === 'cpu' && isCoreRow(s) ? extra : primary).push(s));
        primary.forEach(s => group.appendChild(rowEl(s, type, groupKey)));
        body.appendChild(group);
        if (extra.length) {
          const box = document.createElement('div'); box.className = 'extra';
          extra.forEach(s => box.appendChild(rowEl(s, type, groupKey)));
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
      const ae = document.activeElement;
      if (ae && container.contains(ae) && (ae.tagName === 'INPUT' || ae.tagName === 'SELECT' || ae.tagName === 'TEXTAREA')) return;
      const q = filter.trim().toLowerCase();
      const rows = state.allSensors.filter(s => !q || sensorSearchText(s).includes(q)).slice(0, 220);
      container.innerHTML = rows.map(s => {
        const hidden = SQ.isSensorHidden(s, state.dashboard);
        const pinned = state.dashboard.pinnedCards.some(c => c.id === s.id);
        const action = mode === 'cards' ? (pinned ? 'unpin' : 'pin') : (hidden ? 'show' : 'hide');
        const label = mode === 'cards' ? (pinned ? 'Unpin' : 'Pin') : sensorButtonLabel(s);
        const styleSel = mode === 'cards'
          ? `<select class="style-select" data-action="style" data-id="${esc(s.id)}">
              ${['auto','gauge','number','graph'].map(v =>
                `<option value="${v}"${(state.dashboard.cardStyle[s.id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
            </select>` : '';
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(s.text)}</b><span>${esc(s.hw)} · ${esc(s.type)} · ${esc(s.value ?? '-')}</span><code>${esc(s.id)}</code></div>
          ${styleSel}<button class="iconbtn" data-action="${action}" data-id="${esc(s.id)}">${label}</button>
        </div>`;
      }).join('') || '<div class="empty-note">No sensors</div>';
    }
    function renderPinnedEditor() {
      const box = $('#pinnedList');
      const ae = document.activeElement;
      if (ae && box.contains(ae) && (ae.tagName === 'INPUT' || ae.tagName === 'SELECT' || ae.tagName === 'TEXTAREA')) return;
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
          <select class="style-select" data-action="style" data-id="${esc(id)}">
            ${['auto','gauge','number','graph'].map(v =>
              `<option value="${v}"${(state.dashboard.cardStyle[id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
          </select>
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
      if ((state.paused && !force) || state.dragging) return;
      try {
        const r = await fetch('/data.json', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();
        if (state.dragging) return;   // drag started during the fetch — don't render over it
        render(data);
        document.body.classList.remove('stale');
      } catch (e) {
        $('#freshdot').className = 'lamp s-warn';
        $('#freshtxt').textContent = 'stale - retrying';
        document.body.classList.add('stale');
      }
    }
    function schedule() { clearInterval(state.timer); state.timer = setInterval(tick, state.rate * 1000); }

    document.documentElement.setAttribute('data-theme', state.dashboard.theme);
    $('#theme').onclick = () => {
      const t = state.dashboard.theme === 'dark' ? 'light' : 'dark';
      state.dashboard.theme = t;
      document.documentElement.setAttribute('data-theme', t);
      saveDashboard();
    };
    const rate = $('#rate'); rate.value = state.rate; $('#ratev').textContent = state.rate + 's';
    rate.oninput = e => {
      state.rate = clampRate(e.target.value);
      state.dashboard.rate = state.rate;
      $('#ratev').textContent = state.rate + 's';
      saveDashboard(); schedule();
    };
    const pause = $('#pause');
    function paintPause() { pause.textContent = state.paused ? 'Resume' : 'Pause';
      $('#freshdot').className = 'lamp ' + (state.paused ? 's-off' : 's-ok');
      $('#freshtxt').textContent = state.paused ? 'paused' : 'live'; }
    pause.onclick = () => {
      state.paused = !state.paused;
      state.dashboard.paused = state.paused;
      saveDashboard(); paintPause();
      if (!state.paused) tick();
    };
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
      const sel = e.target.closest('[data-action="style"]');
      if (sel) {
        const v = sel.value;
        if (v === 'auto') delete state.dashboard.cardStyle[sel.dataset.id];
        else state.dashboard.cardStyle[sel.dataset.id] = v;
        commitDashboard();
      }
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

    ['#pfd', '#pinned', '#panels'].forEach(sel => {
      const host = $(sel);
      host && host.addEventListener('click', e => {
        const b = e.target.closest('.ctl');
        if (!b || !host.contains(b)) return;
        if (b.dataset.act === 'row-up' || b.dataset.act === 'row-down') e.preventDefault();
        e.stopPropagation();
        const id = b.dataset.id;
        if (b.dataset.act === 'pin') pinSensor(id);
        else if (b.dataset.act === 'unpin') unpinSensor(id);
        else if (b.dataset.act === 'hide') setSensorHidden(id, true);
        else if (b.dataset.act === 'row-up') moveRow(b.dataset.rowGroup, id, -1);
        else if (b.dataset.act === 'row-down') moveRow(b.dataset.rowGroup, id, 1);
      });
    });

    const drag = { active: null };
    function orderedKeysFor(container) {
      return Array.from(container.children).map(el => el.dataset.key).filter(k => typeof k === 'string' && k.length);
    }
    function dragSiblings(container, movedKey) {
      return Array.from(container.children).filter(el => el.dataset.key && el.dataset.key !== movedKey);
    }
    function dropIndex(sibs, clientX, clientY, columnMajor) {
      for (let i = 0; i < sibs.length; i++) {
        const r = sibs[i].getBoundingClientRect();
        if (columnMajor) {
          // #panels CSS multi-column masonry: columns left->right, items top->bottom.
          if (clientX < r.left) return i;
          if (clientX <= r.right && clientY < r.top + r.height / 2) return i;
        } else {
          // #pinned row-major grid: rows top->bottom, items left->right.
          if (clientY < r.top) return i;
          if (clientY <= r.bottom && clientX < r.left + r.width / 2) return i;
        }
      }
      return sibs.length;
    }
    function placeIndicator(a, sibs) {
      const ind = a.ind;
      if (!sibs.length) { ind.style.display = 'none'; return; }
      const crect = a.container.getBoundingClientRect();
      const before = sibs[a.dropIdx]; // insert-before sibling, or undefined for end-drop
      const anchor = before || sibs[sibs.length - 1];
      const r = anchor.getBoundingClientRect();
      ind.style.display = 'block';
      if (a.isPanel || a.isRow) {
        // horizontal bar above the anchor (below the last panel for end-drop)
        ind.style.left = (r.left - crect.left) + 'px';
        ind.style.width = r.width + 'px';
        ind.style.height = '2px';
        ind.style.top = ((before ? r.top : r.bottom) - crect.top - 1) + 'px';
      } else {
        // vertical bar left of the anchor (right of the last card for end-drop)
        ind.style.top = (r.top - crect.top) + 'px';
        ind.style.height = r.height + 'px';
        ind.style.width = '2px';
        ind.style.left = ((before ? r.left : r.right) - crect.left - 1) + 'px';
      }
    }
    function moveGhost(ev) {
      const a = drag.active; if (!a) return;
      a.ghost.style.left = (ev.clientX + 12) + 'px';
      a.ghost.style.top = (ev.clientY + 12) + 'px';
      const sibs = dragSiblings(a.container, a.key);
      a.dropIdx = dropIndex(sibs, ev.clientX, ev.clientY, a.isPanel || a.isRow);
      placeIndicator(a, sibs);
    }
    function startDrag(grip, ev) {
      if (drag.active) return;
      const el = grip.closest('.row') || grip.closest('.panel') || grip.closest('.cell.pinned');
      if (!el || !el.dataset.key) return;
      ev.preventDefault();
      const nameEl = el.querySelector('.rn') || el.querySelector('.nm') || el.querySelector('.k .name');
      state.dragging = true;
      const ghost = document.createElement('div');
      ghost.className = 'drag-ghost';
      ghost.textContent = nameEl ? nameEl.textContent : el.dataset.key;
      document.body.appendChild(ghost);
      const ind = document.createElement('div');
      ind.className = 'drop-ind';
      el.parentElement.appendChild(ind);
      const isRow = el.classList.contains('row');
      drag.active = { container: el.parentElement, el, key: el.dataset.key,
        isPanel: el.classList.contains('panel'), isRow, rowGroup: el.dataset.rowGroup, ghost, ind, grip, pointerId: ev.pointerId };
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
        if (a.isPanel) state.dashboard.panelOrder = next;
        else if (a.isRow) state.dashboard.rowOrder[a.rowGroup] = next;
        else state.dashboard.pinnedOrder = next;
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

    paintPause();
    paintGraphs();
    window.SQ._STLABEL = STLABEL;
    tick(true); schedule();
  }
})();
