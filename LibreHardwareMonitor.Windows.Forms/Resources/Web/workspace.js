// SQ Sensor Workspace - pure, presentation-only profile and panel model.
(function (root, factory) {
  'use strict';

  const api = factory(root);
  if (root) root.SQWorkspace = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : (typeof globalThis !== 'undefined' ? globalThis : this), function (root) {
  'use strict';

  const STORAGE_KEY = 'sq.workspace.v1';
  const STATE_SCHEMA = 'sq.workspace';
  const PROFILE_SCHEMA = 'sq.workspace.profile';
  const VERSION = 1;
  const PANEL_TYPES = Object.freeze(['card', 'table', 'graph']);
  const BUILT_IN_IDS = Object.freeze(['main', 'gaming', 'storage']);
  const LIMITS = Object.freeze({
    profiles: 10,
    panelsPerProfile: 12,
    sensorIdsPerPanel: 24,
    nameLength: 80,
    localIdLength: 96,
    sensorIdLength: 192,
    profileDocumentBytes: 256 * 1024,
    stateBytes: 3 * 1024 * 1024
  });

  const TYPE_ORDER = Object.freeze({
    primary: Object.freeze(['Temperature', 'Load', 'Power', 'Fan']),
    detail: Object.freeze(['Temperature', 'Load', 'Power', 'Clock', 'Fan', 'Control', 'Throughput', 'Data', 'SmallData', 'Level', 'Voltage', 'Current']),
    gaming: Object.freeze(['Temperature', 'Load', 'Power', 'Clock', 'Fan', 'Control']),
    storage: Object.freeze(['Temperature', 'Load', 'Throughput', 'Data', 'SmallData', 'Level']),
    thermal: Object.freeze(['Temperature']),
    performance: Object.freeze(['Temperature', 'Load', 'Power', 'Clock']),
    storageGraph: Object.freeze(['Temperature', 'Throughput', 'Load'])
  });

  const CLASS_ORDER = Object.freeze({
    main: Object.freeze(['cpu', 'gpu', 'igpu', 'mem', 'memory', 'dimm', 'storage', 'nvme', 'disk', 'hdd', 'mb', 'motherboard', 'network', 'nic', 'other']),
    gaming: Object.freeze(['cpu', 'gpu', 'igpu']),
    storage: Object.freeze(['storage', 'nvme', 'disk', 'hdd', 'drive'])
  });

  const PRESETS = Object.freeze({
    'main-primary': freezePreset(TYPE_ORDER.primary, CLASS_ORDER.main, 8, true),
    'main-details': freezePreset(TYPE_ORDER.detail, CLASS_ORDER.main, 24, true),
    'main-thermals': freezePreset(TYPE_ORDER.thermal, CLASS_ORDER.main, 12, true),
    'gaming-primary': freezePreset(TYPE_ORDER.primary, CLASS_ORDER.gaming, 8),
    'gaming-details': freezePreset(TYPE_ORDER.gaming, CLASS_ORDER.gaming, 24),
    'gaming-performance': freezePreset(TYPE_ORDER.performance, CLASS_ORDER.gaming, 12),
    'storage-primary': freezePreset(TYPE_ORDER.storage, CLASS_ORDER.storage, 8),
    'storage-details': freezePreset(TYPE_ORDER.storage, CLASS_ORDER.storage, 24),
    'storage-activity': freezePreset(TYPE_ORDER.storageGraph, CLASS_ORDER.storage, 12)
  });
  const PRESET_IDS = Object.freeze(Object.keys(PRESETS));

  function freezePreset(types, classes, cap, includeOtherClasses) {
    return Object.freeze({ types, classes, cap, includeOtherClasses: !!includeOtherClasses });
  }

  class WorkspaceError extends Error {
    constructor(code, message) {
      super(message);
      this.name = 'WorkspaceError';
      this.code = code;
    }
  }

  function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
  }

  function hasOwn(value, key) {
    return Object.prototype.hasOwnProperty.call(value, key);
  }

  function cleanText(value, fallback) {
    const text = typeof value === 'string' ? value.trim() : '';
    return (text || fallback || '').slice(0, LIMITS.nameLength);
  }

  function slug(value, fallback) {
    let id = typeof value === 'string' ? value.trim().toLowerCase() : '';
    id = id.normalize ? id.normalize('NFKD') : id;
    id = id.replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9._-]+/g, '-')
      .replace(/^[._-]+|[._-]+$/g, '')
      .slice(0, LIMITS.localIdLength);
    return id || fallback;
  }

  function cleanLocalId(value, fallback) {
    const raw = typeof value === 'string' ? value.trim() : '';
    if (/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(raw))
      return raw.slice(0, LIMITS.localIdLength);
    return slug(raw, fallback);
  }

  function uniqueId(base, used) {
    const rootId = cleanLocalId(base, 'item');
    if (!used.has(rootId)) {
      used.add(rootId);
      return rootId;
    }

    let suffix = 2;
    while (true) {
      const tail = '-' + suffix;
      const candidate = rootId.slice(0, LIMITS.localIdLength - tail.length) + tail;
      if (!used.has(candidate)) {
        used.add(candidate);
        return candidate;
      }
      suffix++;
    }
  }

  function cleanSensorIds(value) {
    if (!Array.isArray(value)) return [];
    const seen = new Set();
    const ids = [];
    for (const candidate of value) {
      if (typeof candidate !== 'string' || candidate.length === 0 ||
          candidate.length > LIMITS.sensorIdLength || seen.has(candidate)) continue;
      seen.add(candidate);
      ids.push(candidate);
      if (ids.length === LIMITS.sensorIdsPerPanel) break;
    }
    return ids;
  }

  function ordinalCompare(left, right) {
    const a = String(left);
    const b = String(right);
    return a < b ? -1 : a > b ? 1 : 0;
  }

  function utf8ByteLength(value) {
    const text = String(value);
    let bytes = 0;
    for (let i = 0; i < text.length; i++) {
      const code = text.charCodeAt(i);
      if (code < 0x80) bytes++;
      else if (code < 0x800) bytes += 2;
      else if (code >= 0xD800 && code <= 0xDBFF &&
               i + 1 < text.length &&
               text.charCodeAt(i + 1) >= 0xDC00 && text.charCodeAt(i + 1) <= 0xDFFF) {
        bytes += 4;
        i++;
      } else bytes += 3;
    }
    return bytes;
  }

  function clonePanel(panel) {
    return {
      id: panel.id,
      title: panel.title,
      type: panel.type,
      preset: panel.preset,
      sensorIds: panel.sensorIds.slice()
    };
  }

  function cloneProfile(profile) {
    return {
      id: profile.id,
      name: profile.name,
      builtIn: profile.builtIn,
      panels: profile.panels.map(clonePanel)
    };
  }

  function defaultPanel(id, title, type, preset) {
    return { id, title, type, preset, sensorIds: [] };
  }

  function defaultProfiles() {
    return [
      {
        id: 'main', name: 'Main', builtIn: 'main', panels: [
          defaultPanel('main-primary', 'Primary', 'card', 'main-primary'),
          defaultPanel('main-details', 'System details', 'table', 'main-details'),
          defaultPanel('main-thermals', 'Thermals', 'graph', 'main-thermals')
        ]
      },
      {
        id: 'gaming', name: 'Gaming', builtIn: 'gaming', panels: [
          defaultPanel('gaming-primary', 'Game telemetry', 'card', 'gaming-primary'),
          defaultPanel('gaming-details', 'CPU and GPU', 'table', 'gaming-details'),
          defaultPanel('gaming-performance', 'Performance', 'graph', 'gaming-performance')
        ]
      },
      {
        id: 'storage', name: 'Storage', builtIn: 'storage', panels: [
          defaultPanel('storage-primary', 'Drive health', 'card', 'storage-primary'),
          defaultPanel('storage-details', 'Storage details', 'table', 'storage-details'),
          defaultPanel('storage-activity', 'Drive activity', 'graph', 'storage-activity')
        ]
      }
    ];
  }

  function createDefaults() {
    return {
      schema: STATE_SCHEMA,
      version: VERSION,
      activeProfileId: 'main',
      profiles: defaultProfiles().map(cloneProfile)
    };
  }

  function normalizePanel(value, index, usedIds, allowAdaptivePreset = true) {
    const panel = isObject(value) ? value : {};
    const type = PANEL_TYPES.includes(panel.type) ? panel.type : 'table';
    const preset = allowAdaptivePreset && typeof panel.preset === 'string' && hasOwn(PRESETS, panel.preset)
      ? panel.preset
      : null;
    return {
      id: uniqueId(cleanLocalId(panel.id, 'panel-' + (index + 1)), usedIds),
      title: cleanText(panel.title, 'Panel ' + (index + 1)),
      type,
      preset,
      sensorIds: preset ? [] : cleanSensorIds(panel.sensorIds)
    };
  }

  function normalizeProfile(value, index, usedIds, usedBuiltIns) {
    const profile = isObject(value) ? value : {};
    let builtIn = BUILT_IN_IDS.includes(profile.builtIn) ? profile.builtIn : null;
    if (builtIn && usedBuiltIns.has(builtIn)) builtIn = null;
    if (builtIn) usedBuiltIns.add(builtIn);

    const name = cleanText(profile.name, builtIn ? titleCase(builtIn) : 'Profile ' + (index + 1));
    const baseId = cleanLocalId(profile.id, builtIn || slug(name, 'profile-' + (index + 1)));
    const panelIds = new Set();
    const sourcePanels = Array.isArray(profile.panels) ? profile.panels : [];
    const panels = sourcePanels.slice(0, LIMITS.panelsPerProfile)
      .map((panel, panelIndex) => normalizePanel(panel, panelIndex, panelIds));
    return {
      id: uniqueId(baseId, usedIds),
      name,
      builtIn,
      panels
    };
  }

  function titleCase(value) {
    return value ? value.charAt(0).toUpperCase() + value.slice(1) : '';
  }

  function normalizeState(value) {
    if (!isObject(value)) return createDefaults();
    if (hasOwn(value, 'schema') && value.schema !== STATE_SCHEMA) return createDefaults();
    if (hasOwn(value, 'version') && value.version !== VERSION) return createDefaults();

    const sourceProfiles = Array.isArray(value.profiles) ? value.profiles : [];
    if (sourceProfiles.length === 0) return createDefaults();

    const usedIds = new Set();
    const usedBuiltIns = new Set();
    const profiles = sourceProfiles.slice(0, LIMITS.profiles)
      .map((profile, index) => normalizeProfile(profile, index, usedIds, usedBuiltIns));
    if (profiles.length === 0) return createDefaults();

    const requestedActive = typeof value.activeProfileId === 'string' ? value.activeProfileId : '';
    const activeProfileId = profiles.some(profile => profile.id === requestedActive)
      ? requestedActive
      : profiles[0].id;
    return { schema: STATE_SCHEMA, version: VERSION, activeProfileId, profiles };
  }

  function resolveStorage(storage) {
    if (storage) return storage;
    try { return root && root.localStorage ? root.localStorage : null; }
    catch { return null; }
  }

  function loadResult(storage) {
    const target = resolveStorage(storage);
    if (!target || typeof target.getItem !== 'function')
      return { ok: false, source: 'default', state: createDefaults(), error: 'storage-unavailable' };

    try {
      const raw = target.getItem(STORAGE_KEY);
      if (!raw) return { ok: true, source: 'default', state: createDefaults(), error: null };
      if (utf8ByteLength(raw) > LIMITS.stateBytes)
        return { ok: false, source: 'default', state: createDefaults(), error: 'state-too-large' };
      const parsed = JSON.parse(raw);
      if (!isObject(parsed) || parsed.schema !== STATE_SCHEMA || parsed.version !== VERSION)
        return { ok: false, source: 'default', state: createDefaults(), error: 'unsupported-state' };
      return { ok: true, source: 'storage', state: normalizeState(parsed), error: null };
    } catch {
      return { ok: false, source: 'default', state: createDefaults(), error: 'malformed-state' };
    }
  }

  function load(storage) {
    return loadResult(storage).state;
  }

  function saveResult(storage, value) {
    const state = normalizeState(value);
    let json;
    try { json = JSON.stringify(state); }
    catch { return { ok: false, state, error: 'state-not-serializable' }; }
    if (utf8ByteLength(json) > LIMITS.stateBytes)
      return { ok: false, state, error: 'state-too-large' };

    const target = resolveStorage(storage);
    if (!target || typeof target.setItem !== 'function')
      return { ok: false, state, error: 'storage-unavailable' };
    try {
      const durable = target.setItem(STORAGE_KEY, json);
      if (durable === false)
        return { ok: false, state, error: 'storage-write-failed' };
      return { ok: true, state, error: null };
    } catch {
      return { ok: false, state, error: 'storage-write-failed' };
    }
  }

  function save(storage, value) {
    return saveResult(storage, value).state;
  }

  function sensorIdOf(sensor) {
    if (!sensor) return null;
    const id = typeof sensor.id === 'string' ? sensor.id : sensor.SensorId;
    return typeof id === 'string' && id.length > 0 && id.length <= LIMITS.sensorIdLength ? id : null;
  }

  function semanticClassOf(sensor) {
    if (!sensor) return 'other';
    const raw = sensor.cls ?? sensor.className ?? sensor.hardwareClass ?? sensor.hardwareType ?? '';
    const value = String(raw).toLowerCase().replace(/[^a-z0-9]/g, '');
    if (value === 'cpu' || value.includes('processor')) return 'cpu';
    if (value === 'igpu') return 'igpu';
    if (value.includes('gpu')) return 'gpu';
    if (value === 'dimm') return 'dimm';
    if (value === 'mem' || value.includes('memory')) return 'memory';
    if (value === 'nvme') return 'nvme';
    if (value === 'hdd') return 'hdd';
    if (value === 'disk') return 'disk';
    if (value.includes('storage') || value.includes('drive')) return 'storage';
    if (value === 'nic') return 'nic';
    if (value.includes('network')) return 'network';
    if (value === 'mb' || value.includes('motherboard') || value.includes('superio')) return 'motherboard';
    return value || 'other';
  }

  function sensorTypeOf(sensor) {
    const value = sensor && (sensor.presetType ?? sensor.displayType ?? sensor.type ?? sensor.Type ?? sensor.sensorType);
    return typeof value === 'string' ? value : '';
  }

  function indexOfIgnoreCase(values, candidate) {
    const sought = String(candidate).toLowerCase();
    for (let i = 0; i < values.length; i++)
      if (String(values[i]).toLowerCase() === sought) return i;
    return -1;
  }

  function resolvePreset(presetId, sensors) {
    const definition = PRESETS[presetId];
    if (!definition || !Array.isArray(sensors)) return [];

    const seen = new Set();
    const buckets = new Map();
    for (const sensor of sensors) {
      const id = sensorIdOf(sensor);
      if (!id || seen.has(id)) continue;
      const typeIndex = indexOfIgnoreCase(definition.types, sensorTypeOf(sensor));
      if (typeIndex < 0) continue;
      const semanticClass = semanticClassOf(sensor);
      let classIndex = definition.classes.indexOf(semanticClass);
      if (classIndex < 0 && definition.includeOtherClasses)
        classIndex = definition.classes.length;
      if (classIndex < 0) continue;
      seen.add(id);
      const key = typeIndex + ':' + classIndex;
      if (!buckets.has(key)) buckets.set(key, { typeIndex, classIndex, ids: [] });
      buckets.get(key).ids.push(id);
    }

    const orderedBuckets = Array.from(buckets.values()).sort((a, b) =>
      a.typeIndex - b.typeIndex || a.classIndex - b.classIndex);
    orderedBuckets.forEach(bucket => bucket.ids.sort(ordinalCompare));

    const result = [];
    for (let offset = 0; result.length < definition.cap; offset++) {
      let appended = false;
      for (const bucket of orderedBuckets) {
        if (offset < bucket.ids.length) {
          result.push(bucket.ids[offset]);
          appended = true;
          if (result.length === definition.cap) break;
        }
      }
      if (!appended) break;
    }
    return result;
  }

  function resolvePanel(value, sensors) {
    const panel = normalizePanel(value, 0, new Set());
    const liveById = new Map();
    if (Array.isArray(sensors)) {
      for (const sensor of sensors) {
        const id = sensorIdOf(sensor);
        if (id && !liveById.has(id)) liveById.set(id, sensor);
      }
    }

    const source = panel.preset ? 'preset' : 'explicit';
    const sensorIds = panel.preset ? resolvePreset(panel.preset, sensors) : panel.sensorIds.slice();
    const items = sensorIds.map(sensorId => {
      const sensor = liveById.get(sensorId) || null;
      return { sensorId, sensor, missing: sensor === null };
    });
    return {
      source,
      preset: panel.preset,
      sensorIds,
      sensors: items.filter(item => !item.missing).map(item => item.sensor),
      items,
      missingSensorIds: items.filter(item => item.missing).map(item => item.sensorId)
    };
  }

  function replaceProfile(value, profileId, update) {
    const state = normalizeState(value);
    const index = state.profiles.findIndex(profile => profile.id === profileId);
    if (index < 0) return state;
    const profiles = state.profiles.map(cloneProfile);
    const updated = update(profiles[index]);
    if (!updated) return state;
    profiles[index] = updated;
    return normalizeState({ ...state, profiles });
  }

  function replacePanel(value, profileId, panelId, update) {
    return replaceProfile(value, profileId, profile => {
      const index = profile.panels.findIndex(panel => panel.id === panelId);
      if (index < 0) return null;
      const panels = profile.panels.map(clonePanel);
      const updated = update(panels[index]);
      if (!updated) return null;
      panels[index] = updated;
      return { ...profile, panels };
    });
  }

  function setActiveProfile(value, profileId) {
    const state = normalizeState(value);
    return state.profiles.some(profile => profile.id === profileId)
      ? { ...state, activeProfileId: profileId }
      : state;
  }

  function createProfile(value, name, options) {
    const state = normalizeState(value);
    if (state.profiles.length >= LIMITS.profiles) return state;
    const profileName = cleanText(name, 'Untitled profile');
    const usedIds = new Set(state.profiles.map(profile => profile.id));
    const id = uniqueId(slug(profileName, 'profile'), usedIds);
    const rawPanels = isObject(options) && Array.isArray(options.panels) ? options.panels : [];
    const panelIds = new Set();
    const panels = rawPanels.slice(0, LIMITS.panelsPerProfile)
      .map((panel, index) => normalizePanel(panel, index, panelIds));
    const profile = { id, name: profileName, builtIn: null, panels };
    return normalizeState({ ...state, activeProfileId: id, profiles: state.profiles.concat(profile) });
  }

  function renameProfile(value, profileId, name) {
    return replaceProfile(value, profileId, profile => ({
      ...profile,
      name: cleanText(name, profile.name)
    }));
  }

  function duplicateProfile(value, profileId, name) {
    const state = normalizeState(value);
    if (state.profiles.length >= LIMITS.profiles) return state;
    const source = state.profiles.find(profile => profile.id === profileId);
    if (!source) return state;
    const copyName = cleanText(name, source.name + ' Copy');
    const usedIds = new Set(state.profiles.map(profile => profile.id));
    const id = uniqueId(slug(copyName, source.id + '-copy'), usedIds);
    const copy = { id, name: copyName, builtIn: null, panels: source.panels.map(clonePanel) };
    return normalizeState({ ...state, activeProfileId: id, profiles: state.profiles.concat(copy) });
  }

  function deleteProfile(value, profileId) {
    const state = normalizeState(value);
    const index = state.profiles.findIndex(profile => profile.id === profileId);
    if (index < 0 || state.profiles.length === 1) return state;
    const profiles = state.profiles.filter(profile => profile.id !== profileId).map(cloneProfile);
    const activeProfileId = state.activeProfileId === profileId
      ? profiles[Math.min(index, profiles.length - 1)].id
      : state.activeProfileId;
    return normalizeState({ ...state, activeProfileId, profiles });
  }

  function resetBuiltInProfile(value, builtInId) {
    const state = normalizeState(value);
    const template = defaultProfiles().find(profile => profile.builtIn === builtInId);
    if (!template) return state;
    const index = state.profiles.findIndex(profile => profile.builtIn === builtInId);
    const profiles = state.profiles.map(cloneProfile);
    if (index >= 0) {
      profiles[index] = { ...cloneProfile(template), id: profiles[index].id };
      return normalizeState({ ...state, profiles });
    }
    if (profiles.length >= LIMITS.profiles) return state;
    const usedIds = new Set(profiles.map(profile => profile.id));
    const restored = cloneProfile(template);
    restored.id = uniqueId(restored.id, usedIds);
    profiles.push(restored);
    return normalizeState({ ...state, profiles });
  }

  function resetBuiltInProfiles(value) {
    return BUILT_IN_IDS.reduce((state, builtInId) => resetBuiltInProfile(state, builtInId), normalizeState(value));
  }

  function addPanel(value, profileId, panel) {
    return replaceProfile(value, profileId, profile => {
      if (profile.panels.length >= LIMITS.panelsPerProfile) return null;
      const usedIds = new Set(profile.panels.map(item => item.id));
      const next = normalizePanel(panel, profile.panels.length, usedIds);
      return { ...profile, panels: profile.panels.concat(next) };
    });
  }

  function updatePanel(value, profileId, panelId, patch, sensors) {
    if (!isObject(patch)) return normalizeState(value);
    return replacePanel(value, profileId, panelId, panel => {
      const next = clonePanel(panel);
      if (next.preset && Array.isArray(sensors) &&
          (hasOwn(patch, 'title') || hasOwn(patch, 'type'))) {
        next.sensorIds = resolvePanel(panel, sensors).sensorIds;
        next.preset = null;
      }
      if (hasOwn(patch, 'title')) next.title = cleanText(patch.title, panel.title);
      if (hasOwn(patch, 'type') && PANEL_TYPES.includes(patch.type)) next.type = patch.type;
      if (hasOwn(patch, 'preset')) {
        next.preset = typeof patch.preset === 'string' && hasOwn(PRESETS, patch.preset)
          ? patch.preset
          : null;
        if (next.preset) next.sensorIds = [];
      }
      if (hasOwn(patch, 'sensorIds')) {
        next.preset = null;
        next.sensorIds = cleanSensorIds(patch.sensorIds);
      }
      return next;
    });
  }

  function movePanel(value, profileId, panelId, toIndex) {
    return replaceProfile(value, profileId, profile => {
      const fromIndex = profile.panels.findIndex(panel => panel.id === panelId);
      if (fromIndex < 0 || profile.panels.length < 2) return null;
      const destination = Math.max(0, Math.min(profile.panels.length - 1, Math.trunc(Number(toIndex) || 0)));
      const panels = profile.panels.map(clonePanel);
      const moved = panels.splice(fromIndex, 1)[0];
      panels.splice(destination, 0, moved);
      return { ...profile, panels };
    });
  }

  function removePanel(value, profileId, panelId) {
    return replaceProfile(value, profileId, profile => {
      if (!profile.panels.some(panel => panel.id === panelId)) return null;
      return { ...profile, panels: profile.panels.filter(panel => panel.id !== panelId).map(clonePanel) };
    });
  }

  function setPanelSensorIds(value, profileId, panelId, sensorIds) {
    return replacePanel(value, profileId, panelId, panel => ({
      ...panel,
      preset: null,
      sensorIds: cleanSensorIds(sensorIds)
    }));
  }

  function materializePanel(value, profileId, panelId, sensors) {
    const state = normalizeState(value);
    const profile = state.profiles.find(item => item.id === profileId);
    const panel = profile && profile.panels.find(item => item.id === panelId);
    if (!panel) return state;
    return setPanelSensorIds(state, profileId, panelId, resolvePanel(panel, sensors).sensorIds);
  }

  function materializeProfile(value, profileId, sensors) {
    return replaceProfile(value, profileId, profile => ({
      ...profile,
      panels: profile.panels.map(panel => {
        if (!panel.preset) return clonePanel(panel);
        return {
          ...clonePanel(panel),
          preset: null,
          sensorIds: resolvePanel(panel, sensors).sensorIds
        };
      })
    }));
  }

  function togglePanelSensor(value, profileId, panelId, sensorId, selected, sensors) {
    const state = normalizeState(value);
    if (typeof sensorId !== 'string' || sensorId.length === 0 || sensorId.length > LIMITS.sensorIdLength) return state;
    const profile = state.profiles.find(item => item.id === profileId);
    const panel = profile && profile.panels.find(item => item.id === panelId);
    if (!panel) return state;
    const ids = resolvePanel(panel, sensors).sensorIds;
    const next = ids.filter(id => id !== sensorId);
    if (selected && !ids.includes(sensorId) && next.length < LIMITS.sensorIdsPerPanel) next.push(sensorId);
    return setPanelSensorIds(state, profileId, panelId, next);
  }

  function movePanelSensor(value, profileId, panelId, sensorId, toIndex, sensors) {
    const state = normalizeState(value);
    const profile = state.profiles.find(item => item.id === profileId);
    const panel = profile && profile.panels.find(item => item.id === panelId);
    if (!panel) return state;
    const ids = resolvePanel(panel, sensors).sensorIds;
    const fromIndex = ids.indexOf(sensorId);
    if (fromIndex < 0) return panel.preset ? setPanelSensorIds(state, profileId, panelId, ids) : state;
    const destination = Math.max(0, Math.min(ids.length - 1, Math.trunc(Number(toIndex) || 0)));
    const next = ids.slice();
    const moved = next.splice(fromIndex, 1)[0];
    next.splice(destination, 0, moved);
    return setPanelSensorIds(state, profileId, panelId, next);
  }

  function createProfileDocument(value, profileId) {
    const state = normalizeState(value);
    const profile = state.profiles.find(item => item.id === profileId);
    if (!profile) throw new WorkspaceError('profile-not-found', 'The selected profile does not exist.');
    if (profile.panels.length === 0)
      throw new WorkspaceError('empty-profile', 'A profile must contain at least one panel before it can be exported.');
    return {
      schema: PROFILE_SCHEMA,
      version: VERSION,
      profile: {
        id: profile.id,
        name: profile.name,
        panels: profile.panels.map(clonePanel)
      }
    };
  }

  function exportProfile(value, profileId, space) {
    const document = createProfileDocument(value, profileId);
    const indent = space === true ? 2 : Math.max(0, Math.min(2, Number(space) || 0));
    const json = JSON.stringify(document, null, indent);
    if (utf8ByteLength(json) > LIMITS.profileDocumentBytes)
      throw new WorkspaceError('profile-too-large', 'The profile exceeds the export size limit.');
    return json;
  }

  function parseProfileDocument(input) {
    let document = input;
    if (typeof input === 'string') {
      if (utf8ByteLength(input) > LIMITS.profileDocumentBytes)
        throw new WorkspaceError('profile-too-large', 'The profile document exceeds the import size limit.');
      try { document = JSON.parse(input); }
      catch { throw new WorkspaceError('invalid-json', 'The profile document is not valid JSON.'); }
    } else {
      try {
        const encoded = JSON.stringify(input);
        if (!encoded || utf8ByteLength(encoded) > LIMITS.profileDocumentBytes)
          throw new WorkspaceError('profile-too-large', 'The profile document exceeds the import size limit.');
      } catch (error) {
        if (error instanceof WorkspaceError) throw error;
        throw new WorkspaceError('invalid-document', 'The profile document cannot be serialized.');
      }
    }

    if (!isObject(document) || document.schema !== PROFILE_SCHEMA)
      throw new WorkspaceError('invalid-schema', 'The profile document has an unknown schema.');
    if (document.version !== VERSION)
      throw new WorkspaceError('unsupported-version', 'The profile document version is not supported.');
    if (!isObject(document.profile))
      throw new WorkspaceError('invalid-profile', 'The profile document does not contain a profile.');
    if (typeof document.profile.name !== 'string' || !document.profile.name.trim())
      throw new WorkspaceError('invalid-profile-name', 'The imported profile requires a name.');
    if (!Array.isArray(document.profile.panels) || document.profile.panels.length === 0)
      throw new WorkspaceError('empty-profile', 'The imported profile must contain at least one panel.');
    if (document.profile.panels.length > LIMITS.panelsPerProfile)
      throw new WorkspaceError('too-many-panels', 'The imported profile contains too many panels.');

    for (const panel of document.profile.panels) {
      if (!isObject(panel))
        throw new WorkspaceError('invalid-panel', 'Every imported panel must be an object.');
      if (Array.isArray(panel.sensorIds) && panel.sensorIds.length > LIMITS.sensorIdsPerPanel)
        throw new WorkspaceError('too-many-sensors', 'An imported panel contains too many sensors.');
      if (Array.isArray(panel.sensorIds) && panel.sensorIds.some(id =>
        typeof id === 'string' && id.length > LIMITS.sensorIdLength))
        throw new WorkspaceError('sensor-id-too-long', 'An imported SensorId exceeds the size limit.');
    }

    const panelIds = new Set();
    const panels = document.profile.panels
      .map((panel, index) => normalizePanel(panel, index, panelIds, false));
    return {
      id: cleanLocalId(document.profile.id, slug(document.profile.name, 'imported-profile')),
      name: cleanText(document.profile.name, 'Imported profile'),
      builtIn: null,
      panels
    };
  }

  function importProfileOrThrow(value, input) {
    const state = normalizeState(value);
    if (state.profiles.length >= LIMITS.profiles)
      throw new WorkspaceError('too-many-profiles', 'The workspace already contains the maximum number of profiles.');
    const imported = parseProfileDocument(input);
    const usedIds = new Set(state.profiles.map(profile => profile.id));
    imported.id = uniqueId(imported.id, usedIds);
    const next = normalizeState({
      ...state,
      activeProfileId: imported.id,
      profiles: state.profiles.concat(imported)
    });
    return { state: next, profileId: imported.id };
  }

  function importProfile(value, input) {
    const state = normalizeState(value);
    try {
      const result = importProfileOrThrow(state, input);
      return { ok: true, state: result.state, profileId: result.profileId, error: null };
    } catch (error) {
      const safeError = error instanceof WorkspaceError
        ? { code: error.code, message: error.message }
        : { code: 'import-failed', message: 'The profile could not be imported.' };
      return { ok: false, state, profileId: null, error: safeError };
    }
  }

  return Object.freeze({
    STORAGE_KEY,
    STATE_SCHEMA,
    PROFILE_SCHEMA,
    VERSION,
    LIMITS,
    PANEL_TYPES,
    BUILT_IN_IDS,
    PRESET_IDS,
    PRESETS,
    WorkspaceError,
    createDefaults,
    defaultState: createDefaults,
    normalizeState,
    normalize: normalizeState,
    load,
    loadState: load,
    loadResult,
    save,
    saveState: save,
    saveResult,
    setActiveProfile,
    createProfile,
    renameProfile,
    duplicateProfile,
    deleteProfile,
    resetBuiltInProfile,
    resetBuiltInProfiles,
    addPanel,
    updatePanel,
    movePanel,
    removePanel,
    setPanelSensorIds,
    materializePanel,
    materializeProfile,
    togglePanelSensor,
    movePanelSensor,
    moveSensor: movePanelSensor,
    resolvePreset,
    resolvePanel,
    resolvePanelMembership: resolvePanel,
    createProfileDocument,
    exportProfile,
    parseProfileDocument,
    importProfile,
    importProfileOrThrow
  });
});
