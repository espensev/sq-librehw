// SQ Telemetry Console - pure model layer. Consumes the unchanged data.json.
(function () {
  const SQ = {};
  const DASHBOARD_STORAGE_KEY = 'sq.dashboard.v1';
  const SENSOR_MOTION = new Map();
  const SENSOR_HISTORY = new Map();
  const SMOOTH_FRACTIONS = new Map();
  const MAX_HISTORY_POINTS = 90;
  const TEMPBANDS = { cpu: [85, 95], gpu: [83, 92], igpu: [83, 92], nvme: [70, 80], dimm: [55, 85], mb: null, mem: null };
  // Derived GPU power-limit knobs. Conservative on purpose: peaks/spikes must
  // not produce a guessed ceiling, so a card only earns an approximate limit
  // after enough idle-gated watt/percent samples converge on a stable ratio.
  SQ.POWER_LIMIT_MAX_SAMPLES = 30;     // ring buffer per hwid
  SQ.POWER_LIMIT_MIN_SAMPLES = 8;      // below this no limit is derived
  SQ.POWER_LIMIT_IDLE_FLOOR = 5;       // percent-of-limit below which a sample is ignored as idle noise
  SQ.POWER_LIMIT_BUCKET = 25;          // watt rounding for "approximate" ceilings

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
  function cleanAliasMap(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => {
        const v = typeof value[k] === 'string' ? value[k].trim().slice(0, 80) : '';
        if (k && v) out[k] = v;
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
  // Persisted watt/percent ratios used to derive GPU power limits. Each hwid
  // keeps a bounded ring of finite, idle-gated ratios; never blocks rendering.
  function cleanPowerSamples(value) {
    const out = {};
    if (value && typeof value === 'object' && !Array.isArray(value))
      Object.keys(value).forEach(k => {
        if (!k || !Array.isArray(value[k])) return;
        const xs = value[k].filter(x => Number.isFinite(x) && x > 0).slice(-SQ.POWER_LIMIT_MAX_SAMPLES);
        if (xs.length) out[k] = xs;
      });
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
      powerLimitSamples: {},
      sensorAliases: {},
      primaryCards: [],
      primaryCardsCustomized: false,
      cardOrder: [],
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
      powerLimitSamples: cleanPowerSamples(value.powerLimitSamples),
      sensorAliases: cleanAliasMap(value.sensorAliases),
      primaryCards: cleanStringList(value.primaryCards),
      primaryCardsCustomized: value.primaryCardsCustomized === true,
      cardOrder: cleanStringList(value.cardOrder),
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
  function mergeNumberMaxMap(a, b) {
    const out = Object.assign({}, cleanNumberMap(a));
    const next = cleanNumberMap(b);
    Object.keys(next).forEach(k => { if (!Number.isFinite(out[k]) || next[k] > out[k]) out[k] = next[k]; });
    return out;
  }
  function mergePowerSampleMap(a, b) {
    const aa = cleanPowerSamples(a), bb = cleanPowerSamples(b), out = Object.assign({}, aa);
    Object.keys(bb).forEach(k => {
      const current = out[k] || [];
      out[k] = (bb[k].length > current.length ? bb[k] : current).slice(-SQ.POWER_LIMIT_MAX_SAMPLES);
    });
    return out;
  }
  SQ.mergeTelemetryState = function (persisted, telemetry) {
    const base = SQ.normalizeDashboardState(persisted);
    const t = SQ.normalizeDashboardState(telemetry);
    const merged = SQ.normalizeDashboardState(base);
    merged.observedMax = mergeNumberMaxMap(base.observedMax, t.observedMax);
    merged.powerLimitSamples = mergePowerSampleMap(base.powerLimitSamples, t.powerLimitSamples);
    return merged;
  };
  SQ.saveTelemetryState = function (storage, telemetry) {
    const persisted = SQ.loadDashboardState(storage);
    return SQ.saveDashboardState(storage, SQ.mergeTelemetryState(persisted, telemetry));
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
  SQ.isPanelCollapsed = function (state, key, fallbackKey, defaultCollapsed) {
    const cp = SQ.normalizeDashboardState(state).collapsedPanels;
    let v = cp[key];
    if (v == null && typeof fallbackKey === 'string') v = cp[fallbackKey];
    if (typeof fallbackKey === 'boolean') defaultCollapsed = fallbackKey;
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
  // --- Sensors popover model (Slice 4B): pure, DOM-free helpers ---
  SQ.sensorSearchText = function (s, state) {
    if (!s) return '';
    const alias = SQ.sensorAlias(state, s.id);
    const val = s.value != null ? s.value : '';
    return `${s.hw || ''} ${s.text || ''} ${alias} ${s.type || ''} ${val} ${s.id || ''}`.toLowerCase();
  };
  SQ.sensorVisibility = function (s, state) {
    if (SQ.isSensorHidden(s, state)) return 'hidden';
    if (s && s.cls === 'nic' &&
        SQ.normalizeDashboardState(state).hiddenNetAdapters.includes(SQ.netAdapterKey(s))) return 'offscreen';
    if (SQ.isStaticDriveAuxTemp(s) || SQ.isStaticMbTemp(s)) return 'offscreen';
    return 'visible';
  };
  SQ.hiddenSensorCount = function (sensors, state) {
    if (!Array.isArray(sensors)) return 0;
    return sensors.reduce((n, s) => n + (SQ.sensorVisibility(s, state) !== 'visible' ? 1 : 0), 0);
  };
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
  SQ.panelKey = function (hw, sensors) {
    const hwid = sensors && sensors.find(s => s.hwid)?.hwid;
    return hwid || ('hw:' + hw);
  };
  SQ.mergeOrder = function (saved, keys) {
    const set = new Set(keys);
    const merged = cleanStringList(saved).filter(k => set.has(k));
    keys.forEach(k => { if (!merged.includes(k)) merged.push(k); });
    return merged;
  };
  SQ.moveKey = function (list, key, delta) {
    const i = list.indexOf(key);
    const j = i + delta;
    if (i < 0 || j < 0 || j >= list.length) return list;
    const next = list.slice();
    [next[i], next[j]] = [next[j], next[i]];
    return next;
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
  SQ.sensorAlias = function (state, id) {
    if (!id) return '';
    return SQ.normalizeDashboardState(state).sensorAliases[id] || '';
  };
  SQ.sensorDisplayText = function (sensor, state, fallback) {
    if (!sensor) return fallback || '';
    return SQ.sensorAlias(state, sensor.id) || fallback || sensor.text || '';
  };
  SQ.updateSensorAlias = function (state, id, alias) {
    const cfg = SQ.normalizeDashboardState(state);
    if (!id) return cfg;
    const aliases = Object.assign({}, cfg.sensorAliases);
    const value = typeof alias === 'string' ? alias.trim().slice(0, 80) : '';
    if (value) aliases[id] = value; else delete aliases[id];
    cfg.sensorAliases = aliases;
    return cfg;
  };
  // Edit the override map from raw input strings. Empty or unparseable max
  // clears the override; min applies only when it is below max.
  SQ.updateRangeOverride = function (overrides, id, maxStr, minStr) {
    const next = Object.assign({}, cleanRangeOverrides(overrides));
    delete next[id];
    const max = parseFloat(maxStr), min = parseFloat(minStr);
    if (id && Number.isFinite(max) && max > 0)
      next[id] = Number.isFinite(min) && min < max ? { max, min } : { max };
    return cleanRangeOverrides(next);
  };
  SQ.rangeSourceLabel = function (rangeInfo) {
    if (!rangeInfo) return 'no known range';
    if (rangeInfo.source === 'limit' && rangeInfo.derived) return 'derived hardware limit';
    return {
      override: 'operator override',
      limit: 'hardware limit',
      band: 'semantic band',
      control: 'paired control %',
      peak: 'observed peak'
    }[rangeInfo.source] || rangeInfo.source || 'no known range';
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
  SQ.primaryCardIds = function (sensors, state) {
    const cfg = SQ.normalizeDashboardState(state);
    if (cfg.primaryCardsCustomized) return cfg.primaryCards.slice();
    return Array.isArray(sensors) ? SQ.pickHero(sensors, {}).map(h => h.s.id) : [];
  };
  SQ.isPrimaryCard = function (state, id, sensors) {
    return SQ.primaryCardIds(sensors, state).includes(id);
  };
  SQ.setPrimaryCard = function (state, id, enabled, sensors) {
    const cfg = SQ.normalizeDashboardState(state);
    const ids = SQ.primaryCardIds(sensors, cfg).filter(x => x !== id);
    if (enabled) ids.push(id);
    cfg.primaryCardsCustomized = true;
    cfg.primaryCards = ids;
    return cfg;
  };
  SQ.resetPrimaryCards = function (state) {
    const cfg = SQ.normalizeDashboardState(state);
    cfg.primaryCardsCustomized = false;
    cfg.primaryCards = [];
    return cfg;
  };
  SQ.resolvePrimaryCards = function (sensors, state, limits) {
    const byId = new Map(sensors.map(s => [s.id, s]));
    // Seeded heroes keep their curated pickHero presentation (label/band/unit); only
    // genuine non-hero promotions fall back to raw sensor text.
    const heroById = new Map(SQ.pickHero(sensors, limits || {}).map(h => [h.s.id, h]));
    const cfg = SQ.normalizeDashboardState(state);
    return cfg.primaryCards.map(id => {
      const s = byId.get(id);
      if (!s) return null;
      return heroById.get(id) ||
        { s, label: s.text, status: SQ.statusOf(s, limits || {}), bounded: SQ.visualRangeForSensor(s, limits || {}) };
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
  // Pure merge of observed peaks into a persisted observedMax map. Returns a new
  // object; only finite values that exceed the prior peak are recorded. Used by
  // the throttled persist path so peaks survive reloads without per-tick writes.
  SQ.mergeObservedPeaks = function (sensors, observedMax) {
    const out = {};
    const src = observedMax && typeof observedMax === 'object' ? observedMax : {};
    Object.keys(src).forEach(k => { if (Number.isFinite(src[k])) out[k] = src[k]; });
    if (Array.isArray(sensors)) {
      sensors.forEach(s => {
        if (!s || s.raw == null || !Number.isFinite(s.raw) || s.raw <= 0) return;
        if (s.type === 'Temperature' || s.type === 'Level') return;   // health, not a peak metric
        const prev = out[s.id] ?? -Infinity;
        if (s.raw > prev) out[s.id] = s.raw;
      });
    }
    return out;
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
  // --- Derived GPU power limit (machine-agnostic) -------------------------
  // Pair a GPU watt sensor with a percent-of-limit sensor on the same hwid and
  // record watt/(pct/100) as one observed implied-limit sample. Conservative:
  // idle samples (< POWER_LIMIT_IDLE_FLOOR %) and non-finite values are dropped,
  // and only POWER_LIMIT_MAX_SAMPLES most recent ratios are kept per hwid.
  // Pure: returns a new samples object; never throws, never blocks rendering.
  SQ.trackPowerSamples = function (sensors, samples) {
    if (!Array.isArray(sensors)) return samples || {};
    const next = {};
    Object.keys(samples || {}).forEach(k => { if (Array.isArray(samples[k])) next[k] = samples[k].slice(); });
    const gpus = sensors.filter(s => (s.cls === 'gpu' || s.cls === 'igpu') && s.hwid);
    const seen = new Set();
    gpus.forEach(s => {
      if (seen.has(s.hwid)) return;
      seen.add(s.hwid);
      const fam = sensors.filter(x => x.hwid === s.hwid);
      const watt = fam.find(x => x.type === 'Power' && /^GPU Package/i.test(x.text || '') && Number.isFinite(x.raw) && x.raw > 0);
      if (!watt) return;
      const pct = fam.find(x => x.type === 'Load' && /^(GPU Power|GPU Board Power)$/i.test(x.text || '') && Number.isFinite(x.raw));
      if (!pct) return;
      if (pct.raw < SQ.POWER_LIMIT_IDLE_FLOOR) return;   // idle noise — skip
      const implied = watt.raw / (pct.raw / 100);
      if (!Number.isFinite(implied) || implied <= 0) return;
      const arr = next[s.hwid] || [];
      arr.push(implied);
      if (arr.length > SQ.POWER_LIMIT_MAX_SAMPLES) arr.shift();
      next[s.hwid] = arr;
    });
    return next;
  };
  SQ.median = function (xs) {
    if (!Array.isArray(xs) || !xs.length) return null;
    const a = xs.slice().sort((p, q) => p - q), n = a.length;
    return n % 2 ? a[(n - 1) / 2] : (a[n / 2 - 1] + a[n / 2]) / 2;
  };
  // Shallow compare two {key: number[]} sample maps (length + contents).
  SQ.shallowEqualArrays = function (a, b) {
    const ak = Object.keys(a || {}), bk = Object.keys(b || {});
    if (ak.length !== bk.length) return false;
    for (const k of ak) {
      const xa = a[k], xb = b[k];
      if (!Array.isArray(xa) || !Array.isArray(xb) || xa.length !== xb.length) return false;
      for (let i = 0; i < xa.length; i++) if (xa[i] !== xb[i]) return false;
    }
    return true;
  };
  // Derive an approximate power-limit ceiling for a GPU hwid from accumulated
  // samples. Returns a finite watt ceiling (already bucketed to 25 W) or null.
  SQ.derivedPowerLimit = function (hwid, state) {
    if (!hwid) return null;
    const cfg = SQ.normalizeDashboardState(state);
    const xs = cfg.powerLimitSamples[hwid];
    if (!Array.isArray(xs) || xs.length < SQ.POWER_LIMIT_MIN_SAMPLES) return null;
    const med = SQ.median(xs);
    if (!Number.isFinite(med) || med <= 0) return null;
    return SQ.roundPowerBucket(med);
  };
  SQ.rangeFor = function (s, limits, state) {
    if (!s) return null;
    const cfg = SQ.normalizeDashboardState(state);
    const ov = cfg.rangeOverrides[s.id];
    if (ov) return { lo: ov.min ?? 0, hi: ov.max, source: 'override' };
    if (s.type === 'Power' && /^GPU Package/i.test(s.text || '')) {
      const d = SQ.derivedPowerLimit(s.hwid, cfg);
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
  // Display-model helper: maps a rangeFor result to a short ceiling label.
  // Peak/unknown return null (no label) so peak-derived cards stay number/sparkline-only.
  SQ.rangeLabelFor = function (rangeInfo, sensor) {
    if (!rangeInfo || !sensor || sensor.raw == null) return null;
    const hi = rangeInfo.hi;
    if (!Number.isFinite(hi)) return null;
    switch (rangeInfo.source) {
      case 'override': return String(hi);
      case 'limit':    return rangeInfo.derived ? `~${SQ.roundPowerBucket(hi)}` : String(hi);
      case 'band':     return String(hi);
      default:         return null;   // peak/unknown: no ceiling label
    }
  };
  // Round a watt value down to a stable 25 W bucket for "approximate" derived ceilings.
  SQ.roundPowerBucket = function (w) {
    if (!Number.isFinite(w) || w <= 0) return null;
    return Math.max(25, Math.floor(w / 25) * 25);
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
    if (sensors.some(s => s.cls === 'gpu' || s.cls === 'igpu')) {
      const gpuSensors = sensors.filter(s => s.cls === 'gpu' || s.cls === 'igpu');
      const gpuHwids = [];
      gpuSensors.forEach(s => { if (s.hwid && !gpuHwids.includes(s.hwid)) gpuHwids.push(s.hwid); });
      const multi = gpuHwids.length > 1;
      gpuHwids.forEach((hwid, gi) => {
        const g = gpuSensors.filter(s => s.hwid === hwid);
        const tag = multi ? ` ${gi + 1}` : '';
        add(g.find(s => s.type === 'Temperature' && /^GPU Core/i.test(s.text)), `GPU Temp${tag}`, { bounded: [25, 92], unit: '°C' });
        add(g.find(s => s.type === 'Temperature' && /Junction/i.test(s.text)), `GPU Mem Jct${tag}`, { bounded: [25, 105], unit: '°C' });
        add(g.find(s => s.type === 'Load' && /^GPU Core/i.test(s.text)), `GPU Load${tag}`, { bounded: [0, 100], unit: '%' });
        add(g.find(s => s.type === 'Power' && /Package/i.test(s.text)), `GPU Power${tag}`, { unit: 'W' });
      });
    }
    add(find(s => s.cls === 'mem' && s.hw === 'Total Memory' && s.type === 'Load'), 'RAM Used', { bounded: [0, 100], unit: '%' });
    const drives = sensors.filter(SQ.isPrimaryDriveTemp).sort((a, b) => b.raw - a.raw);
    add(drives[0], 'Drive Temp', { bounded: [25, 80], unit: '°C' });
    sensors.filter(s => s.type === 'Fan' && s.raw > 0).sort((a, b) => b.raw - a.raw).slice(0, 4)
      .forEach(f => add(f, f.text, { unit: 'rpm' }));
    return H.slice(0, 14);
  };

  SQ.netAdapterKey = function (s) {
    return (s && s.hwid) || ('hw:' + ((s && s.hw) || ''));
  };
  SQ.buildNetAdapters = function (sensors) {
    if (!Array.isArray(sensors)) return [];
    const byKey = new Map();
    sensors.forEach(s => {
      if (!s || s.cls !== 'nic') return;
      const key = SQ.netAdapterKey(s);
      if (!byKey.has(key)) byKey.set(key, { key, hw: s.hw, ss: [] });
      byKey.get(key).ss.push(s);
    });
    const adapters = [...byKey.values()];
    const byLabel = new Map();
    adapters.forEach(a => { (byLabel.get(a.hw) || byLabel.set(a.hw, []).get(a.hw)).push(a); });
    [...byLabel.values()].forEach(group => {
      if (group.length > 1) group.forEach((a, i) => { a.label = `${a.hw} #${i + 1}`; });
      else group[0].label = group[0].hw;
    });
    adapters.forEach(a => { a.active = a.ss.some(s => s.type === 'Throughput' && s.raw > 0); });
    return adapters;
  };

  SQ.buildPanelItems = function (sensors, state) {
    if (!Array.isArray(sensors)) return [];
    const byId = new Map();
    sensors.forEach(s => {
      if (s.cls === 'nic') return;
      const key = s.hwid || ('hw:' + s.hw);
      if (!byId.has(key)) byId.set(key, { hw: s.hw, ss: [], key });
      byId.get(key).ss.push(s);
    });
    const byLabel = new Map();
    [...byId.values()].forEach(item => {
      if (!byLabel.has(item.hw)) byLabel.set(item.hw, []);
      byLabel.get(item.hw).push(item);
    });
    [...byLabel.values()].forEach(group => {
      if (group.length > 1) group.forEach((item, i) => { item.label = `${item.hw} #${i + 1}`; });
      else group[0].label = group[0].hw;
    });
    const order = ['cpu','gpu','igpu','mem','dimm','nvme','disk','mb','other'];
    const items = [...byId.values()].map((item, index) =>
      ({ hw: item.hw, label: item.label, ss: item.ss, key: item.key, collapsed: false, index }))
      .sort((a,b) => {
        const ai = order.indexOf(a.ss[0].cls), bi = order.indexOf(b.ss[0].cls);
        return (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.index - b.index;
      }).map((item, index) => Object.assign(item, { index }));
    const cfg = state ? SQ.normalizeDashboardState(state) : null;
    const hiddenNet = new Set(cfg ? cfg.hiddenNetAdapters : []);
    let adapters = SQ.buildNetAdapters(sensors)
      .filter(a => a.active && !hiddenNet.has(a.key))
      .map((a, i) => ({ hw: a.hw, label: a.label, ss: a.ss, key: a.key, collapsed: true, net: true, index: i }));
    adapters = SQ.applyOrder(adapters, cfg ? cfg.netAdapterOrder : [], a => a.key);
    adapters.forEach(a => { a.index = items.length; items.push(a); });
    return items;
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
      limits: {},
      expanded: new Set(),
      xpEnter: null,
      primaryIds: new Set(),
      inlineEditing: false,
      inlineEditingUntil: 0
    };

    function esc(v) {
      return String(v ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
    }
    function isInlineEditTarget(el) {
      return !!(el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA') && el.closest('.xp, .rowxp'));
    }
    function finishInlineEdit() {
      state.inlineEditing = false;
      state.inlineEditingUntil = 0;
    }
    function ctlCluster(id, label, opts) {
      const pinned = SQ.isPinned(state.dashboard, id);
      const primary = state.primaryIds.has(id);
      const star = `<button class="ctl star${primary ? ' on' : ''}" data-act="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(id)}" aria-label="${primary ? 'Remove from primary' : 'Show as primary'} ${esc(label)}" title="${primary ? 'Remove from primary' : 'Show as primary'}">${primary ? '&#9733;' : '&#9734;'}</button>`;
      const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
      const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
      return star + pin + hide;
    }
    function rootNode(data) {
      return data.Children && data.Children[0] ? data.Children[0] : data;
    }
    function saveDashboard() {
      state.dashboard = SQ.saveDashboardState(localStorage, state.dashboard);
      paintGraphs();
    }
    function saveTelemetryDashboard() {
      state.dashboard = SQ.saveTelemetryState(localStorage, state.dashboard);
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
    function rowGroupKey(panelKey, type) {
      return `${panelKey}|${type}`;
    }
    function orderedRows(panelKey, type, rows) {
      const key = rowGroupKey(panelKey, type);
      return SQ.applyOrder(rows, state.dashboard.rowOrder[key] || [], s => s.id);
    }
    function moveRow(groupKey, id, delta) {
      const group = Array.from(document.querySelectorAll('[data-row-group]'))
        .find(el => el.dataset.rowGroup === groupKey &&
          Array.from(el.querySelectorAll('.row')).some(row => row.dataset.key === id));
      if (!group) return;
      const rows = Array.from(group.querySelectorAll('.row'))
        .map(el => el.dataset.key)
        .filter(k => typeof k === 'string' && k.length);
      state.dashboard.rowOrder[groupKey] = SQ.moveKey(SQ.mergeOrder(state.dashboard.rowOrder[groupKey], rows), id, delta);
      commitDashboard();
    }
    function movePanel(key, delta) {
      const merged = SQ.mergeOrder(state.dashboard.panelOrder, state.panelItems.filter(i => !i.net).map(i => i.key));
      const next = SQ.moveKey(merged, key, delta);
      if (next === merged) return;   // out-of-bounds (top ▲ / bottom ▼): don't dirty panelOrder
      state.dashboard.panelOrder = next;
      commitDashboard();
    }
    function resetPanelOrder() {
      state.dashboard.panelOrder = [];
      commitDashboard();
    }
    function moveAdapter(key, delta) {
      const merged = SQ.mergeOrder(state.dashboard.netAdapterOrder, state.panelItems.filter(i => i.net).map(i => i.key));
      const next = SQ.moveKey(merged, key, delta);
      if (next === merged) return;   // §12.2: top-▲ / bottom-▼ must not dirty netAdapterOrder
      state.dashboard.netAdapterOrder = next;
      commitDashboard();
    }
    function hideAdapter(key) {
      if (state.dashboard.hiddenNetAdapters.includes(key)) return;
      state.dashboard.hiddenNetAdapters = state.dashboard.hiddenNetAdapters.concat(key);
      commitDashboard();
    }
    function showAdapter(key) {
      state.dashboard.hiddenNetAdapters = state.dashboard.hiddenNetAdapters.filter(k => k !== key);
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
    function setPrimaryCardState(id, enabled) {
      state.dashboard = SQ.setPrimaryCard(state.dashboard, id, enabled, state.allSensors);
      commitDashboard();
    }
    function resetPrimaryCardsState() {
      state.dashboard = SQ.resetPrimaryCards(state.dashboard);
      commitDashboard();
    }
    function render(data) {
      state.lastData = data;
      const ae = document.activeElement;
      if (state.inlineEditing || Date.now() < state.inlineEditingUntil || isInlineEditTarget(ae))
        return;   // an inline detail edit is in progress; don't clobber it on poll.
      const root = rootNode(data);
      const host = root.Text || 'Sensor';
      const allSensors = SQ.flatten(root);
      SQ.trackSensorMotion(allSensors);
      SQ.trackSensorHistory(allSensors);
      // Machine-agnostic truth accumulation: observed peaks + GPU power-limit
      // samples. Pure merges run every tick (cheap); persistence is throttled.
      state.dashboard.observedMax = SQ.mergeObservedPeaks(allSensors, state.dashboard.observedMax);
      const newSamples = SQ.trackPowerSamples(allSensors, state.dashboard.powerLimitSamples);
      const samplesChanged = !SQ.shallowEqualArrays(newSamples, state.dashboard.powerLimitSamples);
      state.dashboard.powerLimitSamples = newSamples;
      state.tickCount = (state.tickCount || 0) + 1;
      const sensors = SQ.visibleSensors(allSensors, state.dashboard);
      const limits = SQ.deriveLimits(sensors);
      sensors.forEach(s => s.status = SQ.statusOf(s, limits));
      state.allSensors = allSensors;
      state.visibleSensors = sensors;
      state.primaryIds = new Set(SQ.primaryCardIds(allSensors, state.dashboard));
      state.limits = limits;

      const alarm = sensors.filter(s => s.status !== 'info' && s.status !== 'off');
      renderPinnedCards(sensors, limits);
      renderPFD(sensors, limits);
      renderPlacard(alarm);
      renderPanels(sensors);
      renderSensorsPopover();
      $('#host').textContent = host;
      $('#foot-left').textContent = `LibreHardwareMonitor ${data.Version} · host ${host} · GET /data.json · ${state.rate}s poll`;
      if (!state.paused) {
        $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
        $('#freshdot').className = 'lamp s-ok';
      }
      // Throttled persistence so peaks/derived limits survive reloads without
      // per-tick localStorage writes. Power samples (needed to derive a limit)
      // flush more often than peaks (which only grow).
      if (samplesChanged && state.tickCount % 5 === 0) saveTelemetryDashboard();
      else if (state.tickCount % 30 === 0) saveTelemetryDashboard();
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
    function rangeDetailText(s, rr) {
      const ctrl = SQ.fanControlFor(s, state.allSensors);
      const effective = ctrl ? { lo: 0, hi: 100, source: 'control' } : rr;
      if (!effective) return SQ.rangeSourceLabel(null);
      const prefix = effective.derived ? '~' : '';
      return `${effective.lo} - ${prefix}${effective.hi} · ${SQ.rangeSourceLabel(effective)}`;
    }
    function xpEl(s, rr, opts) {
      const ov = state.dashboard.rangeOverrides[s.id];
      const alias = SQ.sensorAlias(state.dashboard, s.id);
      const pinned = SQ.isPinned(state.dashboard, s.id);
      const isPrimary = state.primaryIds.has(s.id);
      const rawMin = s.min == null || s.min === '' ? '—' : s.min;
      const rawMax = s.max == null || s.max === '' ? '—' : s.max;
      const value = s.value ?? '—';
      const styleSel = opts.style
        ? `<select class="style-select" data-act="style" data-id="${esc(s.id)}" aria-label="Card style">
            ${['auto','gauge','number','graph'].map(v =>
              `<option value="${v}"${(state.dashboard.cardStyle[s.id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
          </select>` : '';
      const moveButtons = opts.movable
        ? `<button class="iconbtn" data-act="${opts.rowGroup ? 'row-up' : 'move-left'}" data-id="${esc(s.id)}"${opts.rowGroup ? ` data-row-group="${esc(opts.rowGroup)}"` : ''} aria-label="Move earlier" title="Move earlier">&#9650;</button>
           <button class="iconbtn" data-act="${opts.rowGroup ? 'row-down' : 'move-right'}" data-id="${esc(s.id)}"${opts.rowGroup ? ` data-row-group="${esc(opts.rowGroup)}"` : ''} aria-label="Move later" title="Move later">&#9660;</button>` : '';
      const el = document.createElement('div');
      el.className = opts.cls;
      el.innerHTML = `
        <div class="xp-grid">
          <div><span>label</span><b>${esc(SQ.sensorDisplayText(s, state.dashboard, opts.fallbackLabel))}</b></div>
          <div><span>raw label</span><b>${esc(s.text)}</b></div>
          <div><span>source</span><b>${esc(s.hw || '—')}</b></div>
          <div><span>hardware id</span><b>${esc(s.hwid || '—')}</b></div>
          <div><span>type</span><b>${esc(SQ.displayType(s))}</b></div>
          <div><span>current</span><b class="num">${esc(value)}</b></div>
          <div><span>raw min/max</span><b class="num">${esc(rawMin)} / ${esc(rawMax)}</b></div>
          <div><span>range</span><b class="num">${esc(rangeDetailText(s, rr))}</b></div>
          <div class="xp-id"><span>sensor id</span><code>${esc(s.id)}</code></div>
        </div>
        <div class="xp-actions">
          <label class="alias">alias <input class="alias-input" data-act="alias" data-id="${esc(s.id)}" value="${esc(alias)}" placeholder="${esc(s.text)}"></label>
          <button class="iconbtn" data-act="alias-clear" data-id="${esc(s.id)}" ${alias ? '' : 'disabled'}>Clear alias</button>
          <button class="iconbtn" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(s.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
          <button class="iconbtn" data-act="${isPrimary ? 'primary-remove' : 'primary-add'}" data-id="${esc(s.id)}">${isPrimary ? 'Remove from primary' : 'Show as primary'}</button>
          <button class="iconbtn" data-act="hide" data-id="${esc(s.id)}">Hide</button>
          ${styleSel}
          <label class="ov">max <input class="ov-max" inputmode="decimal" value="${ov ? esc(String(ov.max)) : ''}" placeholder="${rr && rr.source !== 'override' ? esc(String(rr.hi)) : 'max'}"></label>
          <label class="ov">min <input class="ov-min" inputmode="decimal" value="${ov?.min != null ? esc(String(ov.min)) : ''}" placeholder="0"></label>
          <button class="iconbtn" data-act="set-range" data-id="${esc(s.id)}">Set range</button>
          ${ov ? `<button class="iconbtn" data-act="clear-range" data-id="${esc(s.id)}">Clear range</button>` : ''}
          ${moveButtons}
        </div>`;
      return el;
    }
    function cardRange(h) {
      return h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                       : SQ.rangeFor(h.s, {}, state.dashboard);
    }
    function cardEl(h, pinned) {
      const { n, unit } = h.s.raw == null ? { n: '—', unit: '' } : SQ.splitValue(h.s.value);
      const u = unit || h.unit || '';
      const st = h.status;
      const kind = SQ.kindOf(h.s.type);
      const styleVal = state.dashboard.cardStyle[h.s.id];
      const rr = cardRange(h);
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
      let trendHtml = '<span class="trend"></span>';
      if (trend) {
        const trendArrow = trend.direction === 'rising' ? '&#8599;' : '&#8600;';
        const trendRate = `${Math.abs(trend.rate).toFixed(Math.abs(trend.rate) >= 10 ? 0 : 2)} ${esc(trend.rateUnit)}`;
        // Arc cards have a narrow readout (the arc takes ~half the card), so range + full rate can
        // clip: show the direction arrow only, with the rate in a tooltip. Number-only cards have the
        // full card width, so keep the inline rate there.
        trendHtml = fx.arc
          ? `<span class="trend" title="${trendArrow} ${trendRate}">${trendArrow}</span>`
          : `<span class="trend">${trendArrow} ${trendRate}</span>`;
      }
      const ceilLabel = fx.arc && !ctrl ? SQ.rangeLabelFor(gaugeRange, h.s) : null;
      const ceil = ceilLabel ? `<span class="ceil">/ ${esc(ceilLabel)}</span>` : '';
      const cell = document.createElement('div');
      cell.className = `cell s-${st}${pinned ? ' pinned' : ''}${fx.spark ? ' graph-on' : ''}`;
      cell.style.setProperty('--tc', `var(--t-${kind})`);
      cell.dataset.key = h.s.id;
      cell.dataset.sid = h.s.id;
      cell.tabIndex = 0;
      cell.setAttribute('aria-expanded', state.expanded.has('c:' + h.s.id) ? 'true' : 'false');
      const source = (h.s.hw || '').split(' ').slice(0, 3).join(' ');
      const label = SQ.sensorDisplayText(h.s, state.dashboard, h.label);
      const showHide = !pinned;
      const ctlHtml = `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        ctlCluster(h.s.id, label, { hide: showHide });
      cell.innerHTML =
        `<div class="chead"><div class="ktext">
           <div class="k"><span class="name">${esc(label)}</span>${chip}</div>
           <div class="k2"><span class="src">${esc(source)}</span>${tIcon(kind)}</div>
         </div><div class="cell-ctl">${ctlHtml}</div></div>
         <div class="body">${arc}<div class="readout">
           <div class="big"><span class="v">${esc(n)}</span><span class="u">${esc(u)}</span>${ceil}${ctrl ? `<span class="vcmd" title="commanded ${esc(ctrl.value)}">· ${esc(ctrl.value)}</span>` : ''}</div>
           <div class="meta">${rangeMarkup(h.s) || '<div class="range"></div>'}${trendHtml}</div>
         </div></div>${fx.spark ? sparkAreaSVG(h.s, range) : ''}`;
      if (state.expanded.has('c:' + h.s.id)) cell.classList.add('expanded');
      return cell;
    }
    function placeCardOverlay(grid, cards) {
      const old = grid.querySelector('.xp-overlay');
      if (old) old.remove();
      const h = cards.find(c => state.expanded.has('c:' + c.s.id));
      if (!h) return;
      const cell = grid.querySelector(`.cell[data-sid="${CSS.escape(h.s.id)}"]`);
      if (!cell) return;
      const ov = xpEl(h.s, cardRange(h), { cls: 'xp xp-overlay', style: true, movable: true, fallbackLabel: h.label });
      if (state.xpEnter === 'c:' + h.s.id) ov.classList.add('enter');
      cell.after(ov);
      ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
    }
    function renderPinnedCards(sensors, limits) {
      const cards = SQ.resolvePinnedCards(sensors, state.dashboard, limits);
      const sec = $('#pinnedsec'), grid = $('#pinned');
      grid.innerHTML = '';
      sec.style.display = cards.length ? '' : 'none';
      cards.forEach(h => grid.appendChild(cardEl(h, true)));
      placeCardOverlay(grid, cards);
      $('#pinnedtag').textContent = `${cards.length} pinned`;
    }
    function renderPFD(sensors, limits) {
      const custom = state.dashboard.primaryCardsCustomized;
      const base = custom ? SQ.resolvePrimaryCards(sensors, state.dashboard, limits)
                          : SQ.pickHero(sensors, limits);
      const H = SQ.applyOrder(
        base.map((h, index) => Object.assign(h, { index })),
        state.dashboard.cardOrder, h => h.s.id);
      const pfd = $('#pfd');
      pfd.innerHTML = '';
      H.forEach(h => pfd.appendChild(cardEl(h, false)));
      placeCardOverlay(pfd, H);
      const reset = $('#pfdReset');
      if (reset) reset.style.display = custom ? '' : 'none';
      $('#pfdtag').textContent = custom ? `${H.length} selected` : `${H.length} auto-selected`;
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
      r.dataset.sid = s.id;
      r.dataset.rowGroup = groupKey;
      r.tabIndex = 0;
      r.setAttribute('aria-expanded', state.expanded.has('r:' + s.id) ? 'true' : 'false');
      const fanCtl = s.type === 'Fan' ? SQ.fanControlFor(s, state.allSensors) : null;
      const label = SQ.sensorDisplayText(s, state.dashboard, s.text);
      r.innerHTML = `<button class="grip row-grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>
        <span class="glyph-stat g-${st}" title="${STLABEL[st]}">${st === 'info' ? '' : STGLYPH[st]}</span>
        <span class="rn">${esc(label)}${mm}</span><span class="rv">${esc(s.raw == null ? '—' : (s.value ?? '-'))}${fanCtl ? ` <small class="rvcmd">· ${esc(fanCtl.value)}</small>` : ''}</span>
        ${showBar ? `<div class="bar ${st==='warn'?'warn':st==='crit'?'crit':''}"><i style="width:${Math.max(0,Math.min(100,s.raw))}%"></i></div>` : ''}`;
      const rctl = document.createElement('span');
      rctl.className = 'row-ctl';
      rctl.innerHTML = `<button class="ctl" data-act="row-up" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(label)} up" title="Move up">&#9650;</button>` +
        `<button class="ctl" data-act="row-down" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(label)} down" title="Move down">&#9660;</button>` +
        ctlCluster(s.id, label, { hide: true });
      r.appendChild(rctl);
      return r;
    }
    function appendRow(container, s, type, groupKey) {
      container.appendChild(rowEl(s, type, groupKey));
      if (state.expanded.has('r:' + s.id))
        container.appendChild(xpEl(s, SQ.rangeFor(s, state.limits, state.dashboard),
          { cls: 'rowxp', style: false, movable: true, rowGroup: groupKey, fallbackLabel: s.text }));
    }
    function panelEl(item) {
      const { hw, label, ss, collapsed } = item;
      let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
      const cls = ss[0].cls;
      const startCollapsed = SQ.isPanelCollapsed(state.dashboard, item.key, hw, collapsed);
      const p = document.createElement('div'); p.className = 'panel' + (startCollapsed ? ' collapsed' : '');
      p.dataset.key = item.key;
      const temps = ss.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a,b)=>b.raw-a.raw);
      const head = temps[0] ? temps[0].value : (ss.find(s => s.type === 'Load')?.value || '');
      const h = document.createElement('div'); h.className = 'panel-head';
      const netHide = item.net
        ? `<button class="ctl" data-mv="hide" aria-label="Hide adapter ${esc(label)}" title="Hide adapter">&#8856;</button>`
        : '';
      h.innerHTML = `<span class="panel-move"><button class="ctl" data-mv="up" aria-label="Move ${esc(label)} up" title="Move up">&#9650;</button><button class="ctl" data-mv="down" aria-label="Move ${esc(label)} down" title="Move down">&#9660;</button>${netHide}</span>` +
        `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
        `<span class="lamp s-${worst}"></span><span class="nm">${esc(label)}</span>` +
        `<span class="cls">${CLASSLABEL[cls] || ''}</span>` +
        `<span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
      h.querySelectorAll('.panel-move .ctl').forEach(b => b.onclick = e => {
        e.stopPropagation();
        if (b.dataset.mv === 'hide') { hideAdapter(item.key); return; }
        (item.net ? moveAdapter : movePanel)(item.key, b.dataset.mv === 'up' ? -1 : 1);
      });
      h.onclick = () => {
        p.classList.toggle('collapsed');
        state.dashboard.collapsedPanels[item.key] = p.classList.contains('collapsed');
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
        primary.forEach(s => appendRow(group, s, type, groupKey));
        body.appendChild(group);
        if (extra.length) {
          const box = document.createElement('div'); box.className = 'extra'; box.dataset.rowGroup = groupKey;
          extra.forEach(s => appendRow(box, s, type, groupKey));
          const btn = document.createElement('button'); btn.className = 'morebtn';
          btn.textContent = `+ ${extra.length} per-core ${type.toLowerCase()}`;
          btn.onclick = e => { e.stopPropagation(); box.classList.toggle('open'); btn.textContent =
            box.classList.contains('open') ? `- hide per-core ${type.toLowerCase()}` : `+ ${extra.length} per-core ${type.toLowerCase()}`; };
          body.appendChild(btn); body.appendChild(box);
        }
      });
      p.appendChild(body); return p;
    }
    function renderPanels(sensors) {
      const panels = $('#panels');
      panels.innerHTML = '';
      state.panelItems = SQ.buildPanelItems(sensors, state.dashboard);
      const hwItems = state.panelItems.filter(i => !i.net);
      const netItems = state.panelItems.filter(i => i.net);
      const ordered = SQ.applyOrder(hwItems, state.dashboard.panelOrder, item => item.key);
      ordered.forEach(item => panels.appendChild(panelEl(item)));
      $('#subtag').textContent = `${ordered.length} components`;
      const preset = $('#panelsReset');
      if (preset) preset.style.display = state.dashboard.panelOrder.length ? '' : 'none';
      const netPanels = $('#netPanels');
      netPanels.innerHTML = '';
      netItems.forEach(item => netPanels.appendChild(panelEl(item)));   // already ordered by netAdapterOrder
      const hiddenNet = new Set(state.dashboard.hiddenNetAdapters);
      const hiddenCount = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNet.has(a.key)).length;
      $('#netsec').style.display = (netItems.length || hiddenCount) ? '' : 'none';
      $('#nettag').textContent = `${netItems.length} adapters` + (hiddenCount ? ` · ${hiddenCount} hidden` : '');
    }

    function renderSensorsPopover() {
      const countEl = $('#sensorsCount');
      if (countEl) {
        const n = SQ.hiddenSensorCount(state.allSensors, state.dashboard);
        countEl.textContent = n ? String(n) : '';
      }
      const list = $('#sensorsList');
      const menu = $('#sensorsMenu');
      if (!list || !menu || !menu.open) { state.sensorsSig = null; return; } // list renders only while open; clear sig so reopen rebuilds
      const rows = SQ.sensorPopoverRows(state.allSensors, state.dashboard, state.sensorsFilter || '');
      // Rebuild the list only when the filter or the row set / visibility / pin
      // state changes — NOT on every poll tick — so an in-progress text selection
      // (e.g. copying a SensorId) and search typing survive. Row values are
      // intentionally frozen while the popover is open; it is a discovery/manage
      // surface, not a live monitor (the dashboard behind it keeps updating).
      const pinnedIds = new Set(state.dashboard.pinnedCards.map(c => c.id));
      const hiddenNetKeys = new Set(state.dashboard.hiddenNetAdapters);
      const hiddenAdapters = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNetKeys.has(a.key));
      const sig = (state.sensorsFilter || '') + '|' +
        rows.map(r => `${r.id}:${r.visibility}:${pinnedIds.has(r.id) ? 1 : 0}`).join(',') +
        '|net:' + hiddenAdapters.map(a => a.key).join(',');
      if (sig === state.sensorsSig) return;
      state.sensorsSig = sig;
      list.innerHTML = rows.map(r => {
        const hidden = r.visibility === 'hidden';
        const pinned = pinnedIds.has(r.id);
        const alias = r.label !== r.rawLabel ? ` · ${esc(r.rawLabel)}` : '';
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(r.label)}</b><span>${esc(r.hw)} · ${esc(r.type)} · ${esc(r.value)}${alias}</span><code>${esc(r.id)}</code></div>
          <span class="vis-chip vis-${r.visibility}">${r.visibility}</span>
          <button class="iconbtn" data-action="${pinned ? 'unpin' : 'pin'}" data-id="${esc(r.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
          <button class="iconbtn" data-action="${hidden ? 'show' : 'hide'}" data-id="${esc(r.id)}">${hidden ? 'Show' : 'Hide'}</button>
        </div>`;
      }).join('') || '<div class="empty-note">No sensors</div>';
      const restore = $('#netRestore');
      restore.style.display = hiddenAdapters.length ? '' : 'none';
      $('#netRestoreList').innerHTML = hiddenAdapters.map(a => `<div class="sensor-choice is-hidden">
        <div><b>${esc(a.label)}</b><span>network adapter${a.active ? '' : ' · idle'}</span><code>${esc(a.key)}</code></div>
        <button class="iconbtn" data-action="net-show" data-key="${esc(a.key)}">Show</button>
      </div>`).join('');
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
        const r = await fetch('data.json', { cache: 'no-store' });
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
    $('#pfdReset').onclick = resetPrimaryCardsState;
    $('#panelsReset').onclick = resetPanelOrder;
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
    $('#netRestoreList').addEventListener('click', e => {
      const btn = e.target.closest('[data-action="net-show"]');
      if (!btn) return;
      showAdapter(btn.dataset.key);
      renderSensorsPopover();
    });
    // Escape / click-outside close for masthead disclosure menus (Pages + Sensors)
    document.addEventListener('keydown', e => {
      if (e.key === 'Escape') document.querySelectorAll('details.page-menu[open], details.sensors-menu[open]').forEach(d => { d.open = false; });
    });
    // Capture phase: this must observe e.target BEFORE the #sensorsList action
    // handler (bubble phase) rebuilds the list and detaches the clicked button —
    // otherwise the orphaned target reads as "outside" and closes the popover on
    // every Pin/Hide/Show click.
    document.addEventListener('click', e => {
      document.querySelectorAll('details.page-menu[open], details.sensors-menu[open]').forEach(d => { if (!d.contains(e.target)) d.open = false; });
    }, true);
    // Capture phase, and deliberately blind to clicks on any card/row/control
    // surface: card-to-card switching belongs to toggleExpand's single-open
    // logic, and a bubble-phase rebuild must never make a control click read
    // as "outside" (same race as the sensors-popover lesson above).
    document.addEventListener('click', e => {
      const open = [...state.expanded].filter(k => k.startsWith('c:'));
      if (!open.length) return;
      if (e.target.closest('.cell, .xp-overlay, .row, .rowxp, .panel-head, button, input, select, a, code, label, details')) return;
      open.forEach(k => state.expanded.delete(k));
      rerender();
    }, true);
    function toggleExpand(key) {
      if (state.expanded.has(key)) state.expanded.delete(key);
      else {
        if (key.startsWith('c:')) {
          [...state.expanded].filter(k => k.startsWith('c:')).forEach(k => state.expanded.delete(k));
          state.xpEnter = key;
        }
        state.expanded.add(key);
      }
      rerender();
      state.xpEnter = null;
    }
    function handleAct(btn) {
      const id = btn.dataset.id;
      switch (btn.dataset.act) {
        case 'pin': pinSensor(id); break;
        case 'unpin': unpinSensor(id); break;
        case 'primary-add': setPrimaryCardState(id, true); break;
        case 'primary-remove': setPrimaryCardState(id, false); break;
        case 'hide': setSensorHidden(id, true); break;
        case 'alias-clear':
          state.dashboard = SQ.updateSensorAlias(state.dashboard, id, '');
          finishInlineEdit();
          commitDashboard();
          break;
        case 'set-range': {
          const xp = btn.closest('.xp, .rowxp');
          state.dashboard.rangeOverrides = SQ.updateRangeOverride(
            state.dashboard.rangeOverrides, id,
            xp?.querySelector('.ov-max')?.value ?? '', xp?.querySelector('.ov-min')?.value ?? '');
          finishInlineEdit();
          commitDashboard();
          break;
        }
        case 'clear-range':
          state.dashboard.rangeOverrides = SQ.updateRangeOverride(state.dashboard.rangeOverrides, id, '', '');
          finishInlineEdit();
          commitDashboard();
          break;
        case 'move-left':
        case 'move-right': {
          const cell = btn.closest('.cell');
          const container = cell?.parentElement;
          if (!container) break;
          const keys = orderedKeysFor(container);
          const next = SQ.moveKey(SQ.mergeOrder(container.id === 'pinned' ? state.dashboard.pinnedOrder : state.dashboard.cardOrder, keys),
            cell.dataset.key, btn.dataset.act === 'move-left' ? -1 : 1);
          if (container.id === 'pinned') state.dashboard.pinnedOrder = next;
          else if (container.id === 'pfd') state.dashboard.cardOrder = next;
          commitDashboard();
          break;
        }
        case 'row-up':
        case 'row-down':
          moveRow(btn.dataset.rowGroup, id, btn.dataset.act === 'row-up' ? -1 : 1);
          break;
      }
    }
    ['#pfd', '#pinned', '#panels'].forEach(sel => {
      const host = $(sel);
      if (!host) return;
      host.addEventListener('click', e => {
        const btn = e.target.closest('[data-act]');
        if (btn && host.contains(btn) && btn.tagName === 'BUTTON') {
          e.stopPropagation();
          handleAct(btn);
          return;
        }
        if (e.target.closest('input, select, button, a, code, .xp, .rowxp')) return;
        const cell = e.target.closest('.cell');
        if (cell && host.contains(cell) && cell.dataset.sid) { toggleExpand('c:' + cell.dataset.sid); return; }
        const row = e.target.closest('.row');
        if (row && host.contains(row) && row.dataset.sid) toggleExpand('r:' + row.dataset.sid);
      });
      host.addEventListener('change', e => {
        const styleSel = e.target.closest('select[data-act="style"]');
        if (styleSel && host.contains(styleSel)) {
          const v = styleSel.value;
          if (v === 'auto') delete state.dashboard.cardStyle[styleSel.dataset.id];
          else state.dashboard.cardStyle[styleSel.dataset.id] = v;
          commitDashboard();
          return;
        }
        const aliasInput = e.target.closest('input[data-act="alias"]');
        if (aliasInput && host.contains(aliasInput)) {
          state.dashboard = SQ.updateSensorAlias(state.dashboard, aliasInput.dataset.id, aliasInput.value);
          commitDashboard();
        }
      });
    });
    document.addEventListener('focusin', ev => {
      if (isInlineEditTarget(ev.target)) state.inlineEditing = true;
    }, true);
    document.addEventListener('focusout', ev => {
      if (!isInlineEditTarget(ev.target)) return;
      state.inlineEditingUntil = Date.now() + 1000;
      setTimeout(() => { state.inlineEditing = isInlineEditTarget(document.activeElement); }, 0);
    }, true);

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
      const el = grip.closest('.row') || grip.closest('.panel') || grip.closest('.cell');
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
        if (a.container.id === 'panels') state.dashboard.panelOrder = next;
        else if (a.container.id === 'netPanels') state.dashboard.netAdapterOrder = next;
        else if (a.container.id === 'pinned') state.dashboard.pinnedOrder = next;
        else if (a.container.id === 'pfd') state.dashboard.cardOrder = next;
        else if (a.isRow) state.dashboard.rowOrder[a.rowGroup] = next;
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
    document.addEventListener('keydown', ev => {
      if (ev.key === 'Escape') {
        if (drag.active) { endDrag(false); return; }
        if (state.expanded.size) { state.expanded.clear(); rerender(); }
        return;
      }
      if (ev.key !== 'Enter' && ev.key !== ' ') return;
      if (ev.target.closest && ev.target.closest('input, select, textarea, button, a, code, .xp, .rowxp')) return;
      const cell = ev.target.closest && ev.target.closest('.cell');
      if (cell && cell.dataset.sid) { ev.preventDefault(); toggleExpand('c:' + cell.dataset.sid); return; }
      const row = ev.target.closest && ev.target.closest('.row');
      if (row && row.dataset.sid) { ev.preventDefault(); toggleExpand('r:' + row.dataset.sid); }
    });
    document.addEventListener('click', ev => {
      const grip = ev.target.closest && ev.target.closest('.grip');
      if (grip) { ev.preventDefault(); ev.stopPropagation(); }
    }, true);
    window.addEventListener('resize', () => {
      ['#pfd', '#pinned'].forEach(sel => {
        const grid = $(sel);
        const ov = grid && grid.querySelector('.xp-overlay');
        const cell = grid && grid.querySelector('.cell.expanded');
        if (ov && cell) ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
      });
    });

    paintPause();
    paintGraphs();
    window.SQ._STLABEL = STLABEL;
    tick(true); schedule();
  }
})();
