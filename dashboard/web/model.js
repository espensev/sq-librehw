// Dashboard model factory. Each viewer owns its telemetry and preference state.
(function (root, factory) {
  'use strict';
  const fleet = typeof module === 'object' && module.exports ? require('./fleet.js') : root.SQFleet;
  const api = factory(fleet);
  if (root) root.SQModel = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis, function (Fleet) {
  'use strict';
  function create(options = {}) {
    const SQ = {};
    const DASHBOARD_STORAGE_KEY = 'sq.dashboard.v1';
    const SENSOR_MOTION = new Map();
    const SENSOR_HISTORY = new Map();
    const SMOOTH_FRACTIONS = new Map();
    const TREND_DIRS = new Map();
    const TRANSIENT_LAST_SEEN = new Map();
    const OBSERVED_LAST_SEEN = new Map();
    const POWER_LAST_SEEN = new Map();
    const MAX_HISTORY_POINTS = 90;
    const SENSOR_STATE_MAX_KEYS = 2048;
    const POWER_STATE_MAX_KEYS = 512;
    const SENSOR_STATE_GRACE_MS = 5 * 60 * 1000;
    const VIEW_THEMES = ['standard', 'cardTruth', 'workspace'];
    const STUDIO_ACCENTS = ['coral', 'rose', 'amber', 'plum'];
    const STUDIO_CANVASES = ['ember', 'strata', 'plain'];
    const STUDIO_DENSITIES = ['comfortable', 'compact'];
    const STUDIO_FOCUS_LAYOUTS = ['spotlight', 'grid'];
    const STUDIO_FOCUS_COUNTS = [4, 6, 8, 12];
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
    SQ.SENSOR_STATE_MAX_KEYS = SENSOR_STATE_MAX_KEYS;
    SQ.POWER_STATE_MAX_KEYS = POWER_STATE_MAX_KEYS;
    SQ.SENSOR_STATE_GRACE_MS = SENSOR_STATE_GRACE_MS;

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

    SQ.flatten = function (root, source) {
      const out = [];
      (function walk(node, hw, hwid) {
        if (node.HardwareId !== undefined) { hw = node.Text; hwid = node.HardwareId; }
        if (node.SensorId !== undefined) {
          const sensor = { hw, hwid, cls: SQ.classOf(node.SensorId), type: node.Type, text: node.Text,
            value: node.Value, min: node.Min, max: node.Max, raw: node.RawValue,
            rawMin: node.RawMin, rawMax: node.RawMax, id: node.SensorId };
          if (source) {
            Object.assign(sensor, {sourceKey:source.key, sourceLabel:source.label,
              rawId:node.SensorId, rawHardwareId:hwid,
              id:Fleet.persistenceKey(source.key, node.SensorId),
              hwid:Fleet.persistenceKey(source.key, hwid === undefined ? 'hw:' + hw : hwid)});
          }
          out.push(sensor);
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
    function cleanViewTheme(value) {
      return VIEW_THEMES.includes(value) ? value : 'standard';
    }
    function cleanStudioAccent(value) {
      return STUDIO_ACCENTS.includes(value) ? value : 'coral';
    }
    function cleanStudioCanvas(value) {
      return STUDIO_CANVASES.includes(value) ? value : 'ember';
    }
    function cleanStudioCanvasOpacity(value) {
      const n = Math.round(Number(value));
      if (!Number.isFinite(n)) return 55;
      return Math.max(0, Math.min(100, n));
    }
    function cleanStudioDensity(value) {
      return STUDIO_DENSITIES.includes(value) ? value : 'comfortable';
    }
    function cleanWorkspaceDensity(value) {
      return value === 'compact' ? 'compact' : 'comfortable';
    }
    SQ.workspaceAuthoringPresentation = function (density, requestedOpen) {
      const expanded = cleanWorkspaceDensity(density) === 'compact' && requestedOpen === true;
      return {state:expanded ? 'open' : 'closed', expanded, label:expanded ? 'Done editing' : 'Edit layout'};
    };
    function cleanStudioFocusLayout(value) {
      return STUDIO_FOCUS_LAYOUTS.includes(value) ? value : 'spotlight';
    }
    function cleanStudioFocusCount(value) {
      const n = Number(value);
      return STUDIO_FOCUS_COUNTS.includes(n) ? n : 6;
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
    function sourceOfKey(key) {
      return Fleet.parseIdentity(key)?.sourceKey;
    }
    function configuredSources(dashboard) {
      return new Set(['local', ...(dashboard?.fleet?.sources || []).map(s => s.key)]);
    }
    function keepNewestKeys(value, maxKeys, sources = new Set(['local'])) {
      const groups = new Map();
      Object.keys(value).forEach(key => {
        const source = sourceOfKey(key);
        if (!sources.has(source)) { delete value[key]; return; }
        if (!groups.has(source)) groups.set(source, []);
        groups.get(source).push(key);
      });
      groups.forEach(keys => {
        for (let i = 0; i < keys.length - maxKeys; i++) delete value[keys[i]];
      });
      return value;
    }
    function cleanNumberMap(value, sources) {
      const out = {};
      if (value && typeof value === 'object' && !Array.isArray(value))
        Object.keys(value).forEach(k => { const n = Number(value[k]); if (k && Number.isFinite(n)) out[k] = n; });
      return keepNewestKeys(out, SENSOR_STATE_MAX_KEYS, sources);
    }
    // Persisted watt/percent ratios used to derive GPU power limits. Each hwid
    // keeps a bounded ring of finite, idle-gated ratios; never blocks rendering.
    function cleanPowerSamples(value, sources) {
      const out = {};
      if (value && typeof value === 'object' && !Array.isArray(value))
        Object.keys(value).forEach(k => {
          if (!k || !Array.isArray(value[k])) return;
          const xs = value[k].filter(x => Number.isFinite(x) && x > 0).slice(-SQ.POWER_LIMIT_MAX_SAMPLES);
          if (xs.length) out[k] = xs;
        });
      return keepNewestKeys(out, POWER_STATE_MAX_KEYS, sources);
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
        fleet: Fleet.defaults(),
        fleetOrigins: {},
        fleetError: null,
        hiddenSensorIds: [],
        pinnedCards: [],
        panelOrder: [],
        pinnedOrder: [],
        graphsEnabled: false,
        paused: false,
        rate: 2,
        theme: 'dark',
        viewTheme: 'standard',
        studioAccent: 'coral',
        studioCanvas: 'ember',
        studioCanvasOpacity: 55,
        studioDensity: 'comfortable',
        studioFocusLayout: 'spotlight',
        studioFocusCount: 6,
        studioShowSparklines: true,
        studioShowSystems: true,
        studioShowNetwork: true,
        workspaceDensity: 'comfortable',
        toolbarCollapsed: false,
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
      const fleetState = Fleet.normalize(value.fleet, {
        pageOrigin:options.pageOrigin, knownOrigins:value.fleetOrigins
      });
      const sources = configuredSources({fleet:fleetState.fleet});
      return {
        version: 1,
        fleet: fleetState.fleet,
        fleetOrigins: fleetState.origins,
        fleetError: fleetState.error || (typeof value.fleetError === 'string' ? value.fleetError.slice(0, 240) : null),
        hiddenSensorIds: cleanStringList(value.hiddenSensorIds),
        pinnedCards: cleanPinnedCards(value.pinnedCards),
        panelOrder: cleanStringList(value.panelOrder),
        pinnedOrder: cleanStringList(value.pinnedOrder),
        graphsEnabled: value.graphsEnabled === true,
        paused: value.paused === true,
        rate: clampRate(value.rate),
        theme: value.theme === 'light' ? 'light' : 'dark',
        viewTheme: cleanViewTheme(value.viewTheme),
        studioAccent: cleanStudioAccent(value.studioAccent),
        studioCanvas: cleanStudioCanvas(value.studioCanvas),
        studioCanvasOpacity: cleanStudioCanvasOpacity(value.studioCanvasOpacity),
        studioDensity: cleanStudioDensity(value.studioDensity),
        studioFocusLayout: cleanStudioFocusLayout(value.studioFocusLayout),
        studioFocusCount: cleanStudioFocusCount(value.studioFocusCount),
        studioShowSparklines: value.studioShowSparklines !== false,
        studioShowSystems: value.studioShowSystems !== false,
        studioShowNetwork: value.studioShowNetwork !== false,
        workspaceDensity: cleanWorkspaceDensity(value.workspaceDensity),
        toolbarCollapsed: value.toolbarCollapsed === true,
        collapsedPanels: cleanCollapsedMap(value.collapsedPanels),
        cardStyle: cleanCardStyleMap(value.cardStyle),
        rangeOverrides: cleanRangeOverrides(value.rangeOverrides),
        observedMax: cleanNumberMap(value.observedMax, sources),
        powerLimitSamples: cleanPowerSamples(value.powerLimitSamples, sources),
        sensorAliases: cleanAliasMap(value.sensorAliases),
        primaryCards: cleanStringList(value.primaryCards),
        primaryCardsCustomized: value.primaryCardsCustomized === true,
        cardOrder: cleanStringList(value.cardOrder),
        rowOrder: cleanOrderMap(value.rowOrder),
        netAdapterOrder: cleanStringList(value.netAdapterOrder),
        hiddenNetAdapters: cleanStringList(value.hiddenNetAdapters)
      };
    };
    SQ.normalizeViewTheme = cleanViewTheme;
    SQ.fleetSourceOptions = function (dashboard, contexts, workspace) {
      const references = new Set();
      const add = id => {
        const identity = Fleet.parseIdentity(id);
        if (identity && identity.sourceKey !== 'local') references.add(id);
      };
      const layout = value => {
        if (!value) return;
        for (const name of ['hiddenSensorIds','panelOrder','pinnedOrder','primaryCards','cardOrder','netAdapterOrder','hiddenNetAdapters'])
          if (Array.isArray(value[name])) value[name].forEach(add);
        if (Array.isArray(value.pinnedCards)) value.pinnedCards.forEach(card => add(typeof card === 'string' ? card : card?.id));
        for (const name of ['collapsedPanels','cardStyle','rangeOverrides','sensorAliases','rowOrder'])
          Object.keys(value[name] || {}).forEach(add);
        Object.values(value.rowOrder || {}).forEach(ids => { if (Array.isArray(ids)) ids.forEach(add); });
      };
      layout(dashboard);
      Object.values(contexts?.saved || {}).forEach(layout);
      (workspace?.profiles || []).forEach(profile =>
        (profile.panels || []).forEach(panel => (panel.sensorIds || []).forEach(add)));
      const referenceIds = [...references];
      return {pageOrigin:options.pageOrigin, referenceIds,
        knownOrigins:Fleet.retainOrigins(dashboard.fleetOrigins, dashboard.fleet.sources, referenceIds)};
    };
    SQ.applyFleetState = function (dashboard, nextFleet, contexts, workspace) {
      const options = SQ.fleetSourceOptions(dashboard, contexts, workspace);
      const fleet = Fleet.validateFleet(nextFleet, options);
      return SQ.normalizeDashboardState({...dashboard, fleet, fleetError:null,
        fleetOrigins:Fleet.retainOrigins(options.knownOrigins, fleet.sources, options.referenceIds)});
    };
    SQ.createSafeStorage = function (storageOrProvider) {
      const memory = new Map();
      const removed = new Set();
      const primary = () => {
        try { return typeof storageOrProvider === 'function' ? storageOrProvider() : storageOrProvider; }
        catch { return null; }
      };
      const readPrimary = (method, ...args) => {
        try {
          const target = primary();
          return target && typeof target[method] === 'function' ? target[method](...args) : null;
        } catch { return null; }
      };
      const keysSnapshot = () => {
        const keys = [];
        try {
          const target = primary();
          const length = target ? Number(target.length) : 0;
          if (target && Number.isFinite(length) && typeof target.key === 'function') {
            for (let i = 0; i < length; i++) {
              const key = target.key(i);
              if (key != null && !keys.includes(String(key))) keys.push(String(key));
            }
          }
        } catch {}
        memory.forEach((_, key) => { if (!keys.includes(key)) keys.push(key); });
        return keys.filter(key => !removed.has(key));
      };
      return {
        getItem(key) {
          const k = String(key);
          if (memory.has(k)) return memory.get(k);
          if (removed.has(k)) return null;
          const value = readPrimary('getItem', key);
          if (value != null) { memory.set(k, String(value)); return String(value); }
          return null;
        },
        setItem(key, value) {
          const k = String(key), v = String(value);
          removed.delete(k);
          memory.set(k, v);
          try {
            const target = primary();
            if (target && typeof target.setItem === 'function') {
              target.setItem(k, v);
              return true;
            }
          } catch {}
          return false;
        },
        removeItem(key) {
          const k = String(key);
          memory.delete(k);
          removed.add(k);
          try { const target = primary(); if (target && typeof target.removeItem === 'function') target.removeItem(k); } catch {}
        },
        key(index) {
          return keysSnapshot()[index] ?? null;
        },
        get length() {
          return keysSnapshot().length;
        }
      };
    };
    SQ.loadDashboardState = function (storage) {
      if (!storage) return SQ.defaultDashboardState();
      try {
        if (typeof storage.getItem !== 'function') return SQ.defaultDashboardState();
        const raw = storage.getItem(DASHBOARD_STORAGE_KEY);
        return raw ? SQ.normalizeDashboardState(JSON.parse(raw)) : SQ.defaultDashboardState();
      } catch {
        return SQ.defaultDashboardState();
      }
    };
    SQ.saveDashboardState = function (storage, value) {
      const state = SQ.normalizeDashboardState(value);
      try {
        if (storage && typeof storage.setItem === 'function')
          storage.setItem(DASHBOARD_STORAGE_KEY, JSON.stringify(state));
      } catch {}
      return state;
    };
    function mergeNumberMaxMap(a, b, sources) {
      const out = Object.assign({}, cleanNumberMap(a, sources));
      const next = cleanNumberMap(b, sources);
      Object.keys(next).forEach(k => { if (!Number.isFinite(out[k]) || next[k] > out[k]) out[k] = next[k]; });
      return keepNewestKeys(out, SENSOR_STATE_MAX_KEYS, sources);
    }
    function mergePowerSampleMap(a, b, sources) {
      const aa = cleanPowerSamples(a, sources), bb = cleanPowerSamples(b, sources), out = Object.assign({}, aa);
      Object.keys(bb).forEach(k => {
        const current = out[k] || [];
        out[k] = (bb[k].length > current.length ? bb[k] : current).slice(-SQ.POWER_LIMIT_MAX_SAMPLES);
      });
      return keepNewestKeys(out, POWER_STATE_MAX_KEYS, sources);
    }
    SQ.mergeTelemetryState = function (persisted, telemetry) {
      const base = SQ.normalizeDashboardState(persisted);
      const t = SQ.normalizeDashboardState(telemetry);
      const merged = SQ.normalizeDashboardState(base);
      const sources = configuredSources(base);
      merged.observedMax = mergeNumberMaxMap(base.observedMax, t.observedMax, sources);
      merged.powerLimitSamples = mergePowerSampleMap(base.powerLimitSamples, t.powerLimitSamples, sources);
      return merged;
    };
    SQ.saveTelemetryState = function (storage, telemetry, removals) {
      const persisted = SQ.loadDashboardState(storage);
      const merged = SQ.mergeTelemetryState(persisted, telemetry);
      (removals?.observedMax || []).forEach(key => { delete merged.observedMax[key]; });
      (removals?.powerLimitSamples || []).forEach(key => { delete merged.powerLimitSamples[key]; });
      return SQ.saveDashboardState(storage, merged);
    };
    // Standard dashboard contexts: sq.dashboard.v1 always holds the ACTIVE
    // context's trim; inactive trims park in CONTEXT_STORAGE_KEY. Only the
    // curation subset forks; telemetry caches and device prefs never do.
    const CONTEXT_STORAGE_KEY = 'sq.dashboard.contexts.v1';
    const DASHBOARD_CONTEXTS = ['main', 'gaming', 'storage'];
    const CONTEXT_LAYOUT_FIELDS = [
      'hiddenSensorIds', 'pinnedCards', 'panelOrder', 'pinnedOrder',
      'graphsEnabled', 'collapsedPanels', 'cardStyle',
      'primaryCards', 'primaryCardsCustomized', 'cardOrder', 'rowOrder',
      'netAdapterOrder', 'hiddenNetAdapters'
    ];
    function cleanContext(value) {
      return DASHBOARD_CONTEXTS.includes(value) ? value : 'main';
    }
    SQ.DASHBOARD_CONTEXTS = DASHBOARD_CONTEXTS.slice();
    SQ.normalizeDashboardContext = cleanContext;
    SQ.extractContextLayout = function (dashboard) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      const out = {};
      CONTEXT_LAYOUT_FIELDS.forEach(k => { out[k] = cfg[k]; });
      return out;
    };
    SQ.applyContextLayout = function (dashboard, layout) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      const src = layout && typeof layout === 'object' ? layout : {};
      CONTEXT_LAYOUT_FIELDS.forEach(k => { if (k in src) cfg[k] = src[k]; });
      return SQ.normalizeDashboardState(cfg);
    };
    SQ.normalizeContextState = function (value) {
      const base = { version: 1, active: 'main', saved: {} };
      if (!value || typeof value !== 'object') return base;
      base.active = cleanContext(value.active);
      const saved = value.saved;
      if (saved && typeof saved === 'object' && !Array.isArray(saved))
        DASHBOARD_CONTEXTS.forEach(k => {
          if (k !== base.active && saved[k] && typeof saved[k] === 'object')
            base.saved[k] = SQ.extractContextLayout(SQ.applyContextLayout(SQ.defaultDashboardState(), saved[k]));
        });
      return base;
    };
    SQ.loadContextState = function (storage) {
      if (!storage || typeof storage.getItem !== 'function') return SQ.normalizeContextState(null);
      try {
        const raw = storage.getItem(CONTEXT_STORAGE_KEY);
        return SQ.normalizeContextState(raw ? JSON.parse(raw) : null);
      } catch {
        return SQ.normalizeContextState(null);
      }
    };
    SQ.saveContextState = function (storage, value) {
      const state = SQ.normalizeContextState(value);
      try {
        if (storage && typeof storage.setItem === 'function')
          storage.setItem(CONTEXT_STORAGE_KEY, JSON.stringify(state));
      } catch {}
      return state;
    };
    SQ.switchDashboardContext = function (storage, dashboard, nextContext) {
      const contexts = SQ.loadContextState(storage);
      const next = cleanContext(nextContext);
      const cfg = SQ.normalizeDashboardState(dashboard);
      if (next === contexts.active) return { dashboard: cfg, contexts, changed: false };
      const parked = Object.assign({}, contexts.saved);
      parked[contexts.active] = SQ.extractContextLayout(cfg);
      const seed = parked[next] || SQ.extractContextLayout(cfg);
      delete parked[next];
      const applied = SQ.applyContextLayout(cfg, seed);
      // Persist the pointer+parked trims first: a partial failure duplicates a
      // trim rather than losing one (see spec, State and behavior).
      const nextContexts = SQ.saveContextState(storage, { version: 1, active: next, saved: parked });
      const nextDashboard = SQ.saveDashboardState(storage, applied);
      return { dashboard: nextDashboard, contexts: nextContexts, changed: true };
    };
    SQ.migrateLegacyState = function (storage, state) {
      const cfg = SQ.normalizeDashboardState(state);
      if (!storage) return cfg;
      const get = key => { try { return storage.getItem(key); } catch { return null; } };
      const paused = get('sq.paused');
      if (paused != null) cfg.paused = paused === '1';
      const rate = get('sq.rate');
      if (rate != null && rate !== '') cfg.rate = clampRate(rate);
      const theme = get('sq.theme');
      if (theme === 'dark' || theme === 'light') cfg.theme = theme;
      const panelKeys = [];
      try {
        const length = Number(storage.length);
        if (Number.isFinite(length) && typeof storage.key === 'function') {
          for (let i = 0; i < length; i++) {
            const k = storage.key(i);
            if (typeof k === 'string' && k.indexOf('sq.panel.') === 0) panelKeys.push(k);
          }
        }
      } catch {}
      panelKeys.forEach(k => { cfg.collapsedPanels[k.slice('sq.panel.'.length)] = get(k) === '1'; });
      const remove = key => { try { if (typeof storage.removeItem === 'function') storage.removeItem(key); } catch {} };
      ['sq.paused', 'sq.rate', 'sq.theme'].forEach(remove);
      panelKeys.forEach(remove);
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
      return `${s.sourceLabel || ''} ${s.hw || ''} ${s.text || ''} ${alias} ${s.type || ''} ${val} ${s.id || ''}`.toLowerCase();
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
          visibility: vis,
          sourceKey: SQ.sensorSourceKey(s),
          machine: s.sourceLabel || ''
        }));
    };
    SQ.panelKey = function (hw, sensors) {
      const hwid = sensors && sensors.find(s => s.hwid)?.hwid;
      return hwid || Fleet.persistenceKey(sensors?.[0]?.sourceKey || 'local', 'hw:' + hw);
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
        peak: 'observed peak',
        history: 'visible history'
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
        h.push({ t, raw: s.raw });
        while (h.length > MAX_HISTORY_POINTS) h.shift();
        SENSOR_HISTORY.set(s.id, h);
      });
    };
    SQ.historyFor = function (id) {
      return SENSOR_HISTORY.get(id) || [];
    };
    function pruneOwnedKeys(keys, activeKeys, lastSeen, maxKeys, remove, now, sources) {
      const groups = new Map();
      keys.forEach(key => {
        const source = sourceOfKey(key);
        if (!sources.has(source)) { remove(key); lastSeen.delete(key); return; }
        if (!groups.has(source)) groups.set(source, []);
        groups.get(source).push(key);
      });
      const owned = new Set(keys);
      [...lastSeen.keys()].forEach(key => { if (!owned.has(key)) lastSeen.delete(key); });
      groups.forEach(group => pruneSourceKeys(group, activeKeys, lastSeen, maxKeys, remove, now));
    }
    function pruneSourceKeys(keys, activeKeys, lastSeen, maxKeys, remove, now) {
      const owned = new Set(keys);
      activeKeys.forEach(key => { if (owned.has(key)) lastSeen.set(key, now); });
      keys.forEach(key => {
        if (activeKeys.has(key)) return;
        if (!lastSeen.has(key)) lastSeen.set(key, now);
        else if (now - lastSeen.get(key) > SENSOR_STATE_GRACE_MS) {
          remove(key);
          lastSeen.delete(key);
        }
      });
      const remaining = keys.filter(key => lastSeen.has(key));
      if (remaining.length <= maxKeys) return;
      remaining.sort((a, b) => {
        const activity = Number(activeKeys.has(a)) - Number(activeKeys.has(b));
        return activity || (lastSeen.get(a) - lastSeen.get(b));
      });
      remaining.slice(0, remaining.length - maxKeys).forEach(key => {
        remove(key);
        lastSeen.delete(key);
      });
    }
    function pruneTransientSensorState(sensors, now, sources) {
      const active = new Set((Array.isArray(sensors) ? sensors : []).map(s => s && s.id).filter(Boolean));
      const keys = [...new Set([...SENSOR_MOTION.keys(), ...SENSOR_HISTORY.keys(),
        ...SMOOTH_FRACTIONS.keys(), ...TREND_DIRS.keys()])];
      pruneOwnedKeys(keys, active, TRANSIENT_LAST_SEEN, SENSOR_STATE_MAX_KEYS, key => {
        SENSOR_MOTION.delete(key);
        SENSOR_HISTORY.delete(key);
        SMOOTH_FRACTIONS.delete(key);
        TREND_DIRS.delete(key);
      }, now, sources);
    }
    function prunePersistedMap(value, active, lastSeen, maxKeys, now, sources) {
      const map = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
      const keys = Object.keys(map);
      pruneOwnedKeys(keys, active, lastSeen, maxKeys, key => { delete map[key]; }, now, sources);
      return map;
    }
    SQ.pruneTelemetryState = function (sensors, dashboard, now) {
      const t = Number.isFinite(now) ? now : Date.now();
      const list = Array.isArray(sensors) ? sensors : [];
      const sources = configuredSources(dashboard);
      const activeSensors = new Set(list.map(s => s && s.id).filter(Boolean));
      const activeHardware = new Set(list.map(s => s && s.hwid).filter(Boolean));
      dashboard.observedMax = prunePersistedMap(dashboard.observedMax, activeSensors,
        OBSERVED_LAST_SEEN, SENSOR_STATE_MAX_KEYS, t, sources);
      dashboard.powerLimitSamples = prunePersistedMap(dashboard.powerLimitSamples, activeHardware,
        POWER_LAST_SEEN, POWER_STATE_MAX_KEYS, t, sources);
      pruneTransientSensorState(list, t, sources);
      return dashboard;
    };
    SQ.telemetryCacheSizes = function () {
      return {motion:SENSOR_MOTION.size, history:SENSOR_HISTORY.size,
        smooth:SMOOTH_FRACTIONS.size, trends:TREND_DIRS.size};
    };
    SQ.resetTelemetryCaches = function () {
      SENSOR_MOTION.clear();
      SENSOR_HISTORY.clear();
      SMOOTH_FRACTIONS.clear();
      TREND_DIRS.clear();
      TRANSIENT_LAST_SEEN.clear();
      OBSERVED_LAST_SEEN.clear();
      POWER_LAST_SEEN.clear();
    };

    SQ.TRENDBANDS = {
      temp:  { unit: '°C/s',    db: 0.05, scale: 1 },
      fan:   { unit: 'rpm/min', db: 30,   scale: 60 },
      power: { unit: 'W/s',     db: 1.5,  scale: 1 },
      load:  { unit: '%/s',     db: 0.5,  scale: 1 },
      clock: { unit: 'MHz/s',   db: 15,   scale: 1 }
    };
    SQ.resetSensorTrends = function () { TREND_DIRS.clear(); };
    SQ.resetSourceDerivatives = function (sourceKey) {
      SENSOR_HISTORY.forEach((history, id) => {
        if (sourceOfKey(id) !== sourceKey) return;
        // Preserve the displayed trace, but never infer a rate across a gap.
        if (history.length) history[history.length - 1].gapAfter = true;
        TREND_DIRS.delete(id);
      });
    };
    SQ.trendFor = function (id, kind, now) {
      const band = SQ.TRENDBANDS[kind];
      if (!band) return null;
      const t = Number.isFinite(now) ? now : Date.now();
      const history = SENSOR_HISTORY.get(id) || [];
      let start = 0;
      history.forEach((point, index) => { if (point.gapAfter) start = index + 1; });
      const win = history.slice(start).filter(p => t - p.t <= 30000 && Number.isFinite(p.raw));
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
    SQ.ingestTelemetry = function (runtime, sensors, freshTelemetry, now, retainedSensors) {
      if (!freshTelemetry) return {ingested:false, samplesChanged:false};
      const list = Array.isArray(sensors) ? sensors : [];
      const t = Number.isFinite(now) ? now : Date.now();
      const dashboard = runtime.dashboard || SQ.defaultDashboardState();
      ensureSavedTelemetryKeys(runtime, dashboard);
      const priorSamples = dashboard.powerLimitSamples;
      const priorObservedKeys = Object.keys(dashboard.observedMax || {});
      const priorPowerKeys = Object.keys(dashboard.powerLimitSamples || {});
      SQ.trackSensorMotion(list);
      SQ.trackSensorHistory(list, t);
      dashboard.observedMax = SQ.mergeObservedPeaks(list, dashboard.observedMax);
      dashboard.powerLimitSamples = SQ.trackPowerSamples(list, dashboard.powerLimitSamples);
      SQ.pruneTelemetryState(retainedSensors || list, dashboard, t);
      runtime.dashboard = dashboard;
      runtime.tickCount = (runtime.tickCount || 0) + 1;
      const removedObserved = priorObservedKeys.filter(key => !(key in dashboard.observedMax));
      const removedPower = priorPowerKeys.filter(key => !(key in dashboard.powerLimitSamples));
      const pending = runtime.telemetryRemovals || {observedMax:[], powerLimitSamples:[]};
      pending.observedMax = [...new Set(pending.observedMax.concat(removedObserved))]
        .filter(key => runtime.telemetrySavedKeys.observedMax.has(key) && !(key in dashboard.observedMax));
      pending.powerLimitSamples = [...new Set(pending.powerLimitSamples.concat(removedPower))]
        .filter(key => runtime.telemetrySavedKeys.powerLimitSamples.has(key) && !(key in dashboard.powerLimitSamples));
      runtime.telemetryRemovals = pending;
      return {ingested:true,
        samplesChanged:!SQ.shallowEqualArrays(dashboard.powerLimitSamples, priorSamples),
        removals:pending};
    };
    function ensureSavedTelemetryKeys(runtime, dashboard) {
      runtime.telemetrySavedKeys ||= {
        observedMax:new Set(Object.keys(dashboard.observedMax || {})),
        powerLimitSamples:new Set(Object.keys(dashboard.powerLimitSamples || {}))
      };
    }
    // Acknowledge only after writing the dashboard. Removal queues contain at
    // most the bounded keys in that saved snapshot, never unsaved sensor churn.
    SQ.acknowledgeTelemetrySave = function (runtime) {
      runtime.telemetrySavedKeys = null;
      ensureSavedTelemetryKeys(runtime, runtime.dashboard);
      runtime.telemetryRemovals = {observedMax:[], powerLimitSamples:[]};
    };
    // Called only for validated coordinator successes; rendering consumes the
    // stored sensors and never advances source generations or telemetry.
    SQ.ingestSourceTelemetry = function (runtime, source, data, sample) {
      if (!configuredSources(runtime.dashboard).has(source.key))
        throw new Error('Telemetry source is not configured.');
      if (!Number.isFinite(sample?.completedAt) || !Number.isSafeInteger(sample?.generation) || sample.generation < 1)
        throw new Error('Telemetry sample requires a generation and completion time.');
      runtime.sourceGenerations ||= new Map();
      if ((runtime.sourceGenerations.get(source.key) || 0) >= sample.generation)
        return {ingested:false, samplesChanged:false};
      const sensors = SQ.flatten(data.Children[0], source);
      runtime.sourceSensors ||= new Map();
      runtime.sourceSensors.set(source.key, sensors);
      const retained = [...runtime.sourceSensors.values()].flat();
      const result = SQ.ingestTelemetry(runtime, sensors, true, sample.completedAt, retained);
      runtime.sourceGenerations.set(source.key, sample.generation);
      return result;
    };
    SQ.removeSourceTelemetry = function (runtime, sourceKey) {
      if (sourceKey === 'local') throw new Error('The local source cannot be removed.');
      runtime.sourceSensors?.delete(sourceKey);
      ensureSavedTelemetryKeys(runtime, runtime.dashboard);
      // The coordinator invalidates removed requests; retain no per-source
      // generations once that source has left the bounded active collection.
      runtime.sourceGenerations?.delete(sourceKey);
      const pending = runtime.telemetryRemovals || {observedMax:[], powerLimitSamples:[]};
      for (const name of ['observedMax', 'powerLimitSamples']) {
        const map = runtime.dashboard[name] || {};
        const removed = Object.keys(map).filter(key => sourceOfKey(key) === sourceKey);
        removed.forEach(key => { delete map[key]; });
        pending[name] = [...new Set(pending[name].concat(removed))]
          .filter(key => runtime.telemetrySavedKeys[name].has(key));
      }
      for (const map of [SENSOR_MOTION, SENSOR_HISTORY, SMOOTH_FRACTIONS, TREND_DIRS,
        TRANSIENT_LAST_SEEN, OBSERVED_LAST_SEEN, POWER_LAST_SEEN]) {
        for (const key of map.keys()) if (sourceOfKey(key) === sourceKey) map.delete(key);
      }
      runtime.telemetryRemovals = pending;
      return {removals:pending};
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
    SQ.rangeRenderSignature = function (rangeInfo) {
      return rangeInfo
        ? [rangeInfo.lo ?? null, rangeInfo.hi ?? null, rangeInfo.source || null, !!rangeInfo.derived]
        : null;
    };
    SQ.arcTargetFor = function (sensor, control, rangeInfo, enabled) {
      if (!enabled) return null;
      const raw = control
        ? control.raw / 100
        : rangeInfo && sensor ? (sensor.raw - rangeInfo.lo) / (rangeInfo.hi - rangeInfo.lo) : null;
      if (raw == null) return null;
      return Math.max(0, Math.min(1, Number.isFinite(raw) ? raw : 0));
    };
    SQ.smoothedFraction = id => SMOOTH_FRACTIONS.get(id) ?? null;
    SQ.smoothFraction = function (id, target, rate) {
      const next = SQ.stepFraction(SMOOTH_FRACTIONS.get(id), target, rate <= 1 ? 0.14 : 0.18);
      SMOOTH_FRACTIONS.set(id, next);
      return next;
    };
    SQ.stepFraction = function (previous, target, maxStep) {
      const t = Math.max(0, Math.min(1, Number.isFinite(target) ? target : 0));
      if (!Number.isFinite(previous)) return t;
      const step = Number.isFinite(maxStep) && maxStep > 0 ? maxStep : 1;
      const p = Math.max(0, Math.min(1, previous));
      const delta = t - p;
      return p + Math.sign(delta) * Math.min(Math.abs(delta), step);
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
    SQ.graphScaleFor = function (rangeInfo, history) {
      if (rangeInfo && Number.isFinite(rangeInfo.lo) && Number.isFinite(rangeInfo.hi) &&
          rangeInfo.hi > rangeInfo.lo)
        return Object.assign({}, rangeInfo);
      const values = Array.isArray(history)
        ? history.map(point => point && point.raw).filter(Number.isFinite)
        : [];
      if (values.length < 2) return null;
      let lo = Math.min(...values), hi = Math.max(...values);
      if (!(hi > lo)) {
        lo = lo >= 0 ? Math.max(0, lo - 1) : lo - 1;
        hi += 1;
      }
      return {lo, hi, source:'history'};
    };
    SQ.graphScaleText = function (scale, sensor) {
      if (!scale) return 'Scale pending · collecting live history';
      const compact = value => Number.isInteger(value) ? String(value) : String(Number(value.toFixed(2)));
      const unit = SQ.splitValue(sensor && sensor.value).unit;
      const suffix = unit ? ' ' + unit : '';
      return `Scale ${compact(scale.lo)} - ${scale.derived ? '~' : ''}${compact(scale.hi)}${suffix} · ${SQ.rangeSourceLabel(scale)}`;
    };

    SQ.pickHero = function (sensors, limits) {
      const groups = new Map();
      sensors.forEach(sensor => {
        const key = sensor.sourceKey || 'local';
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(sensor);
      });
      if (groups.size > 1) return [...groups.values()].flatMap(group => SQ.pickHero(group, limits));
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
      return (s && s.hwid) || Fleet.persistenceKey(s?.sourceKey || 'local', 'hw:' + (s?.hw || ''));
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
        const key = SQ.panelKey(s.hw, [s]);
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
      // Machines group first (in composed source order), then hardware class.
      const sourceRank = new Map();
      sensors.forEach(s => {
        const key = SQ.sensorSourceKey(s);
        if (!sourceRank.has(key)) sourceRank.set(key, sourceRank.size);
      });
      const items = [...byId.values()].map((item, index) =>
        ({ hw: item.hw, label: item.label, ss: item.ss, key: item.key, collapsed: false, index,
          sourceKey: SQ.sensorSourceKey(item.ss[0]) }))
        .sort((a,b) => {
          const as = sourceRank.get(a.sourceKey) ?? 99, bs = sourceRank.get(b.sourceKey) ?? 99;
          const ai = order.indexOf(a.ss[0].cls), bi = order.indexOf(b.ss[0].cls);
          return as - bs || (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.index - b.index;
        }).map((item, index) => Object.assign(item, { index }));
      const cfg = state ? SQ.normalizeDashboardState(state) : null;
      const hiddenNet = new Set(cfg ? cfg.hiddenNetAdapters : []);
      let adapters = SQ.buildNetAdapters(sensors)
        .filter(a => a.active && !hiddenNet.has(a.key))
        .map((a, i) => ({ hw: a.hw, label: a.label, ss: a.ss, key: a.key, collapsed: true, net: true, index: i,
          sourceKey: SQ.sensorSourceKey(a.ss[0]) }));
      adapters = SQ.applyOrder(adapters, cfg ? cfg.netAdapterOrder : [], a => a.key);
      adapters.forEach(a => { a.index = items.length; items.push(a); });
      return items;
    };

    // --- Fleet session: composition, source status, and machine attribution ---
    // The viewer keeps one sensor list per source (runtime.sourceSensors). The
    // merged view follows the configured order with local first; disabling the
    // fleet hides peers without discarding their retained telemetry.
    SQ.sourceOrder = function (dashboard) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      return ['local', ...cfg.fleet.sources.map(source => source.key)];
    };
    SQ.activeSourceKeys = function (dashboard) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      return cfg.fleet.enabled ? SQ.sourceOrder(cfg) : ['local'];
    };
    SQ.composeSensors = function (runtime, dashboard) {
      const sensors = runtime?.sourceSensors;
      if (!(sensors instanceof Map)) return [];
      return SQ.activeSourceKeys(dashboard).flatMap(key => sensors.get(key) || []);
    };
    SQ.hasTelemetry = function (runtime) {
      return runtime?.sourceSensors instanceof Map && runtime.sourceSensors.size > 0;
    };
    SQ.fleetActive = function (dashboard) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      return cfg.fleet.enabled && cfg.fleet.sources.length > 0;
    };
    const SOURCE_STATUSES = ['pending', 'current', 'stale', 'unavailable', 'paused'];
    SQ.sourceStatusMap = function (states) {
      const map = new Map();
      (Array.isArray(states) ? states : []).forEach(item => {
        const key = item?.source?.key;
        if (typeof key !== 'string' || !key) return;
        map.set(key, {key, label:item.source.label, url:item.source.url ?? null,
          status:SOURCE_STATUSES.includes(item.status) ? item.status : 'pending',
          lastSuccess:Number.isFinite(item.lastSuccess) ? item.lastSuccess : null,
          error:typeof item.error === 'string' && item.error ? item.error : null,
          hasData:item.lastGood != null});
      });
      return map;
    };
    // Only a stale source keeps showing readings that are no longer current.
    // Pending/unavailable sources have nothing to show; paused applies to the
    // local source too and keeps today's presentation.
    SQ.staleSourceKeys = function (states) {
      const keys = new Set();
      SQ.sourceStatusMap(states).forEach((item, key) => { if (item.status === 'stale') keys.add(key); });
      return keys;
    };
    SQ.sensorSourceKey = function (sensor) {
      return sensor?.sourceKey || sourceOfKey(sensor?.id) || 'local';
    };
    SQ.isStaleSensor = function (sensor, staleKeys) {
      return staleKeys instanceof Set && staleKeys.has(SQ.sensorSourceKey(sensor));
    };
    SQ.currentAlarms = function (sensors, staleKeys) {
      if (!Array.isArray(sensors)) return [];
      return sensors.filter(s => s && s.status !== 'info' && s.status !== 'off' && !SQ.isStaleSensor(s, staleKeys));
    };
    SQ.machineLabel = function (sensorOrKey, dashboard, hostLabel) {
      const key = typeof sensorOrKey === 'string' ? sensorOrKey : SQ.sensorSourceKey(sensorOrKey);
      if (key === 'local') {
        const own = typeof hostLabel === 'string' && hostLabel.trim() ? hostLabel.trim() : null;
        return own || (sensorOrKey && typeof sensorOrKey === 'object' && sensorOrKey.sourceLabel) || 'Local';
      }
      const cfg = SQ.normalizeDashboardState(dashboard);
      const source = cfg.fleet.sources.find(item => item.key === key);
      return source ? source.label : (sensorOrKey && typeof sensorOrKey === 'object' && sensorOrKey.sourceLabel) || key;
    };
    SQ.sourceAgeText = function (lastSuccess, now) {
      if (!Number.isFinite(lastSuccess)) return 'no sample yet';
      const t = Number.isFinite(now) ? now : Date.now();
      const seconds = Math.max(0, Math.round((t - lastSuccess) / 1000));
      if (seconds < 60) return seconds + 's ago';
      const minutes = Math.floor(seconds / 60);
      if (minutes < 60) return minutes + 'm ago';
      return Math.floor(minutes / 60) + 'h ago';
    };
    SQ.sourceStatusText = function (item, now) {
      if (!item) return 'unknown';
      switch (item.status) {
        case 'current': return 'current · ' + SQ.sourceAgeText(item.lastSuccess, now);
        case 'stale': return 'stale · ' + SQ.sourceAgeText(item.lastSuccess, now) + (item.error ? ' · ' + item.error : '');
        case 'unavailable': return 'unavailable' + (item.error ? ' · ' + item.error : '');
        case 'paused': return item.hasData ? 'paused · last ' + SQ.sourceAgeText(item.lastSuccess, now) : 'paused';
        default: return 'connecting…';
      }
    };
    SQ.fleetSummary = function (dashboard, states) {
      const cfg = SQ.normalizeDashboardState(dashboard);
      const statuses = SQ.sourceStatusMap(states);
      const peers = cfg.fleet.enabled ? cfg.fleet.sources : [];
      const counts = {peers:peers.length, current:0, stale:0, unavailable:0, pending:0, paused:0};
      peers.forEach(source => {
        const status = statuses.get(source.key)?.status || 'pending';
        counts[status] = (counts[status] || 0) + 1;
      });
      const parts = [];
      if (counts.peers) {
        parts.push(`${counts.peers} peer${counts.peers === 1 ? '' : 's'}`);
        if (counts.current) parts.push(`${counts.current} current`);
        if (counts.stale) parts.push(`${counts.stale} stale`);
        if (counts.unavailable) parts.push(`${counts.unavailable} unavailable`);
        if (counts.pending) parts.push(`${counts.pending} connecting`);
        if (counts.paused) parts.push(`${counts.paused} paused`);
      }
      return Object.assign(counts, {text:parts.join(' · ')});
    };

    return SQ;
  }
  return {create};
});
