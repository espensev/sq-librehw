const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const MODEL_PATH = path.join(__dirname,
  '../LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js');
const CONSOLE_PATH = path.join(__dirname,
  '../LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js');
const W = require(MODEL_PATH);
const consoleWindow = {SQ_NO_BOOT:true};
Function('window', fs.readFileSync(CONSOLE_PATH, 'utf8'))(consoleWindow);
const S = consoleWindow.SQ;

function sensor(id, cls, type, extra) {
  return Object.assign({id, cls, type, text:id.split('/').pop(), hw:cls, raw:1, value:'1'}, extra);
}

function memoryStorage(initial) {
  const values = Object.assign(Object.create(null), initial);
  const calls = [];
  return {
    get length() { return Object.keys(values).length; },
    key:index => Object.keys(values)[index] ?? null,
    getItem:key => {
      calls.push(['get', key]);
      return Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null;
    },
    setItem:(key, value) => {
      calls.push(['set', key]);
      values[key] = String(value);
    },
    removeItem:key => {
      calls.push(['remove', key]);
      delete values[key];
    },
    values,
    calls
  };
}

function jsonWithAsciiPadding(value, targetBytes) {
  const padded = Object.assign({}, value, {padding:''});
  const baseBytes = Buffer.byteLength(JSON.stringify(padded), 'utf8');
  assert.ok(baseBytes <= targetBytes, 'base document must fit inside target size');
  padded.padding = 'x'.repeat(targetBytes - baseBytes);
  const json = JSON.stringify(padded);
  assert.equal(Buffer.byteLength(json, 'utf8'), targetBytes);
  return json;
}

function profileDocument(overrides) {
  return {
    schema:W.PROFILE_SCHEMA,
    version:W.PROFILE_VERSION,
    profile:Object.assign({
      id:'imported',
      name:'Imported',
      panels:[{id:'panel', title:'Panel', type:'table', preset:null, sensorIds:['/cpu/load']}]
    }, overrides)
  };
}

const sensors = [
  sensor('/cpu/temp-b', 'cpu', 'Temperature'),
  sensor('/cpu/temp-a', 'cpu', 'Temperature'),
  sensor('/cpu/load', 'cpu', 'Load'),
  sensor('/gpu/temp', 'gpu', 'Temperature'),
  sensor('/gpu/hotspot-rate', 'gpu', 'TemperatureRate'),
  sensor('/gpu/load', 'gpu', 'Load'),
  sensor('/gpu/power', 'gpu', 'Power'),
  sensor('/gpu/voltage', 'gpu', 'Voltage'),
  sensor('/nvme/temp', 'nvme', 'Temperature'),
  sensor('/nvme/load', 'nvme', 'Load'),
  sensor('/nic/rate', 'nic', 'Throughput')
];

test('defaults and storage stay versioned, bounded, and isolated', () => {
  const defaults = W.createDefaults();
  assert.equal(W.STORAGE_KEY, 'sq.workspace.v1');
  assert.equal(defaults.schema, 'sq.workspace');
  assert.equal(defaults.version, 2);
  assert.equal(W.STATE_VERSION, 2);
  assert.equal(W.PROFILE_VERSION, 1);
  assert.deepEqual(defaults.profiles.map(profile => profile.name), ['Main', 'Gaming', 'Storage', 'Thermal']);
  assert.deepEqual(defaults.profiles.flatMap(profile => profile.panels.map(panel => panel.type))
    .filter((value, index, all) => all.indexOf(value) === index), ['card', 'table', 'graph']);

  const storage = memoryStorage({'sq.dashboard.v1':'untouched'});
  const saved = W.save(storage, defaults);
  assert.deepEqual(Object.keys(storage.values).sort(), ['sq.dashboard.v1', 'sq.workspace.v1']);
  assert.deepEqual(W.load(storage), saved);
  assert.equal(storage.values['sq.dashboard.v1'], 'untouched');

  assert.equal(W.load(memoryStorage({'sq.workspace.v1':'{'})).activeProfileId, 'main');
  assert.equal(W.load(memoryStorage({'sq.workspace.v1':JSON.stringify({schema:'future', version:99})})).activeProfileId, 'main');
  assert.doesNotThrow(() => W.save({
    setItem() { throw new Error('blocked'); }
  }, defaults));
  const safeFallback = S.createSafeStorage({
    setItem() { throw new Error('quota'); }
  });
  const fallbackResult = W.saveResult(safeFallback, defaults);
  assert.equal(fallbackResult.ok, false);
  assert.equal(fallbackResult.error, 'storage-write-failed');
  assert.equal(JSON.parse(safeFallback.getItem(W.STORAGE_KEY)).schema, W.STATE_SCHEMA);
});

test('v1 state gains Thermal once while v2 deletions remain durable', () => {
  const legacy = W.createDefaults();
  legacy.version = 1;
  legacy.profiles = legacy.profiles.filter(profile => profile.builtIn !== 'thermal');
  const storage = memoryStorage({[W.STORAGE_KEY]:JSON.stringify(legacy)});

  const migrated = W.loadResult(storage);
  assert.equal(migrated.ok, true);
  assert.equal(migrated.source, 'storage');
  assert.equal(migrated.state.version, 2);
  assert.equal(migrated.state.profiles.filter(profile => profile.builtIn === 'thermal').length, 1);

  const withoutThermal = W.deleteProfile(migrated.state, 'thermal');
  assert.equal(withoutThermal.version, 2);
  assert.equal(withoutThermal.profiles.some(profile => profile.builtIn === 'thermal'), false);
  assert.equal(W.normalizeState(withoutThermal).profiles.some(profile => profile.builtIn === 'thermal'), false);
});

test('workspace and dashboard storage remain isolated in both directions', () => {
  const workspace = W.createDefaults();
  const dashboard = S.normalizeDashboardState({
    viewTheme:'workspace',
    theme:'light',
    hiddenSensorIds:['/hidden'],
    sensorAliases:{'/cpu/load':'CPU Work'},
    studioAccent:'plum',
    studioCanvas:'strata',
    observedMax:{'/fan':1200}
  });
  const storage = memoryStorage({
    [W.STORAGE_KEY]:JSON.stringify(workspace),
    [S.DASHBOARD_STORAGE_KEY]:JSON.stringify(dashboard)
  });

  const dashboardBytes = storage.values[S.DASHBOARD_STORAGE_KEY];
  W.save(storage, W.renameProfile(workspace, 'main', 'Main Live'));
  assert.equal(storage.values[S.DASHBOARD_STORAGE_KEY], dashboardBytes);

  const workspaceBytes = storage.values[W.STORAGE_KEY];
  const savedDashboard = S.saveTelemetryState(storage, S.normalizeDashboardState({
    viewTheme:'standard',
    theme:'dark',
    observedMax:{'/fan':1500, '/pump':900}
  }));
  assert.equal(storage.values[W.STORAGE_KEY], workspaceBytes);
  assert.equal(savedDashboard.viewTheme, 'workspace');
  assert.equal(savedDashboard.theme, 'light');
  assert.deepEqual(savedDashboard.hiddenSensorIds, ['/hidden']);
  assert.deepEqual(savedDashboard.sensorAliases, {'/cpu/load':'CPU Work'});
  assert.deepEqual(savedDashboard.observedMax, {'/fan':1500, '/pump':900});
  assert.equal(savedDashboard.studioAccent, 'plum');
  assert.equal(savedDashboard.studioCanvas, 'strata');

  const badWorkspace = memoryStorage({
    [W.STORAGE_KEY]:'{',
    [S.DASHBOARD_STORAGE_KEY]:dashboardBytes
  });
  assert.equal(W.load(badWorkspace).activeProfileId, 'main');
  assert.equal(badWorkspace.values[S.DASHBOARD_STORAGE_KEY], dashboardBytes);

  const badDashboard = memoryStorage({
    [W.STORAGE_KEY]:workspaceBytes,
    [S.DASHBOARD_STORAGE_KEY]:'{'
  });
  assert.equal(S.loadDashboardState(badDashboard).viewTheme, 'standard');
  assert.equal(badDashboard.values[W.STORAGE_KEY], workspaceBytes);
});

test('semantic presets are deterministic, label-independent, and class-scoped', () => {
  const reversed = sensors.slice().reverse().map(item => Object.assign({}, item, {
    text:'labels do not select presets'
  }));
  assert.deepEqual(W.resolvePreset('gaming-primary', sensors),
    W.resolvePreset('gaming-primary', reversed));

  const byId = new Map(sensors.map(item => [item.id, item]));
  const gaming = W.resolvePreset('gaming-details', sensors);
  const storage = W.resolvePreset('storage-details', sensors);
  assert.ok(gaming.length > 0 && gaming.every(id => ['cpu','gpu','igpu'].includes(byId.get(id).cls)));
  assert.ok(storage.length > 0 && storage.every(id => ['nvme','disk','hdd','storage','drive'].includes(byId.get(id).cls)));
  assert.ok(!W.resolvePreset('gaming-primary', sensors).includes('/gpu/voltage'));
  assert.deepEqual(W.resolvePreset('main-primary', sensors), [
    '/cpu/temp-a', '/gpu/temp', '/nvme/temp', '/cpu/load',
    '/gpu/load', '/nvme/load', '/gpu/power', '/cpu/temp-b'
  ]);
  assert.deepEqual(W.resolvePreset('gaming-primary', sensors), [
    '/cpu/temp-a', '/gpu/temp', '/cpu/load', '/gpu/load', '/gpu/power', '/cpu/temp-b'
  ]);
  assert.deepEqual(W.resolvePreset('storage-primary', sensors), ['/nvme/temp', '/nvme/load']);
  assert.deepEqual(W.resolvePreset('thermal-critical', sensors), [
    '/cpu/temp-a', '/gpu/temp', '/nvme/temp', '/gpu/hotspot-rate', '/cpu/temp-b'
  ]);
  const thermalSensors = sensors.concat(
    sensor('/gpu/fan', 'gpu', 'Fan'),
    sensor('/gpu/control', 'gpu', 'Control'));
  assert.ok(W.resolvePreset('thermal-cooling', thermalSensors).includes('/gpu/fan'));
  assert.ok(W.resolvePreset('thermal-cooling', thermalSensors).includes('/gpu/control'));
  assert.ok(W.resolvePreset('thermal-trends', sensors).includes('/gpu/hotspot-rate'));
  assert.ok(!W.resolvePreset('thermal-trends', sensors).includes('/gpu/voltage'));

  const duplicate = Object.assign({}, sensors[0], {text:'same ID, different label'});
  assert.equal(W.resolvePreset('main-primary', sensors.concat(duplicate))
    .filter(id => id === duplicate.id).length, 1);
  const auxiliaryLimit = sensor('/nvme/warning', 'nvme', 'Temperature', {presetType:'Auxiliary'});
  assert.equal(W.resolvePreset('storage-details', sensors.concat(auxiliaryLimit)).includes(auxiliaryLimit.id), false);

  const manyTemperatures = Array.from({length:30}, (_, index) =>
    sensor('/cpu/temp-' + String(index).padStart(2, '0'), 'cpu', 'Temperature'));
  assert.deepEqual(W.resolvePreset('main-primary', manyTemperatures),
    manyTemperatures.slice(0, 8).map(item => item.id));
  assert.deepEqual(W.resolvePreset('main-details', manyTemperatures),
    manyTemperatures.slice(0, 24).map(item => item.id));
  assert.deepEqual(W.resolvePreset('main-thermals', manyTemperatures),
    manyTemperatures.slice(0, 12).map(item => item.id));
});

test('editing materializes presets and preserves exact order plus missing SensorIds', () => {
  const adaptive = W.createDefaults();
  const adaptiveBytes = JSON.stringify(adaptive);
  const editedPreset = W.updatePanel(adaptive, 'main', 'main-primary',
    {title:'Primary live', type:'graph'}, sensors);
  const editedPresetPanel = editedPreset.profiles.find(profile => profile.id === 'main').panels
    .find(item => item.id === 'main-primary');
  assert.equal(editedPresetPanel.preset, null);
  assert.deepEqual(editedPresetPanel.sensorIds, W.resolvePreset('main-primary', sensors));
  assert.equal(editedPresetPanel.title, 'Primary live');
  assert.equal(editedPresetPanel.type, 'graph');

  let state = W.materializeProfile(adaptive, 'main', sensors);
  assert.equal(JSON.stringify(adaptive), adaptiveBytes);
  let main = state.profiles.find(profile => profile.id === 'main');
  assert.ok(main.panels.every(panel => panel.preset === null));
  assert.ok(main.panels.every(panel => panel.sensorIds.length > 0));

  state = W.setPanelSensorIds(state, 'main', 'main-primary',
    ['/cpu/load', '/temporarily/missing', '/gpu/temp']);
  let panel = state.profiles.find(profile => profile.id === 'main').panels
    .find(item => item.id === 'main-primary');
  let resolved = W.resolvePanel(panel, sensors);
  assert.deepEqual(resolved.sensorIds, ['/cpu/load', '/temporarily/missing', '/gpu/temp']);
  assert.deepEqual(resolved.missingSensorIds, ['/temporarily/missing']);

  const savedBytes = JSON.stringify(state);
  resolved = W.resolvePanel(panel, sensors.concat(
    sensor('/temporarily/missing', 'other', 'Load', {text:'Returned sensor'})));
  assert.deepEqual(resolved.sensorIds, ['/cpu/load', '/temporarily/missing', '/gpu/temp']);
  assert.deepEqual(resolved.missingSensorIds, []);
  assert.equal(JSON.stringify(state), savedBytes);

  state = W.movePanelSensor(state, 'main', 'main-primary', '/gpu/temp', 0, sensors);
  panel = state.profiles.find(profile => profile.id === 'main').panels
    .find(item => item.id === 'main-primary');
  assert.deepEqual(panel.sensorIds, ['/gpu/temp', '/cpu/load', '/temporarily/missing']);

  const presetState = W.togglePanelSensor(W.createDefaults(), 'gaming', 'gaming-primary',
    '/custom/sensor', true, sensors);
  const edited = presetState.profiles.find(profile => profile.id === 'gaming').panels
    .find(item => item.id === 'gaming-primary');
  assert.equal(edited.preset, null);
  assert.ok(edited.sensorIds.includes('/custom/sensor'));
});

test('profile and panel operations remain collision-safe and ordered', () => {
  let state = W.createProfile(W.createDefaults(), 'Diagnostics', {
    panels:[{id:'one', title:'One', type:'card', sensorIds:['/cpu/load']}]
  });
  const profileId = state.activeProfileId;
  state = W.renameProfile(state, profileId, 'Diagnostics Live');
  state = W.addPanel(state, profileId, {id:'two', title:'Two', type:'table', sensorIds:[]});
  state = W.movePanel(state, profileId, 'two', 0);
  state = W.updatePanel(state, profileId, 'two', {type:'graph', title:'Trends'});
  let profile = state.profiles.find(item => item.id === profileId);
  assert.deepEqual(profile.panels.map(panel => [panel.id, panel.title, panel.type]),
    [['two', 'Trends', 'graph'], ['one', 'One', 'card']]);

  state = W.duplicateProfile(state, profileId, 'Diagnostics Live');
  assert.notEqual(state.activeProfileId, profileId);
  assert.equal(state.profiles.filter(item => item.name === 'Diagnostics Live').length, 2);
  state = W.removePanel(state, state.activeProfileId, 'one');
  assert.equal(state.profiles.find(item => item.id === state.activeProfileId).panels.length, 1);
});

test('profile import/export is exact, additive, and rejects incompatible input', () => {
  const exact = W.materializeProfile(W.createDefaults(), 'main', sensors);
  const json = W.exportProfile(exact, 'main', true);
  const document = JSON.parse(json);
  assert.equal(document.schema, 'sq.workspace.profile');
  assert.equal(document.version, 1);
  assert.equal('builtIn' in document.profile, false);
  assert.ok(document.profile.panels.every(panel => panel.preset === null && panel.sensorIds.length > 0));

  const imported = W.importProfile(W.createDefaults(), json);
  assert.equal(imported.ok, true);
  assert.equal(imported.profileId, 'main-2');
  assert.equal(imported.state.activeProfileId, 'main-2');
  assert.deepEqual(imported.state.profiles.find(profile => profile.id === 'main-2').panels,
    document.profile.panels);

  const invalid = value => W.importProfile(W.createDefaults(), JSON.stringify(value));
  assert.equal(invalid({schema:'wrong', version:1, profile:{}}).error.code, 'invalid-schema');
  assert.equal(invalid({schema:W.PROFILE_SCHEMA, version:2, profile:{}}).error.code, 'unsupported-version');
  assert.equal(invalid({schema:W.PROFILE_SCHEMA, version:1,
    profile:{id:'x', name:'Empty', panels:[]}}).error.code, 'empty-profile');
  assert.equal(invalid({schema:W.PROFILE_SCHEMA, version:1,
    profile:{id:'x', name:'Large', panels:Array.from({length:13}, (_, i) =>
      ({id:'p' + i, title:'P', type:'table', sensorIds:[]}))}}).error.code, 'too-many-panels');
  assert.equal(W.importProfile(W.createDefaults(), 'x'.repeat(W.LIMITS.profileDocumentBytes + 1)).error.code,
    'profile-too-large');

  const empty = W.createProfile(W.createDefaults(), 'Empty');
  assert.throws(() => W.exportProfile(empty, empty.activeProfileId),
    error => error.code === 'empty-profile');
});

test('imports suffix collisions, drop authority, and leave rejected state untouched', () => {
  const document = profileDocument({
    id:'main',
    name:'<script>globalThis.__workspaceImported=true</script>',
    builtIn:'main',
    unknownProfileField:'drop me',
    panels:[
      {id:'panel', title:'<img src=x onerror=alert(1)>', type:'unknown', preset:'main-details',
        sensorIds:['/cpu/load', '/cpu/load'], unknownPanelField:true},
      {id:'panel', title:'Second', type:'graph', sensorIds:[]}
    ]
  });
  Object.defineProperty(document.profile, '__proto__', {
    value:{polluted:true}, enumerable:true, configurable:true
  });
  Object.defineProperty(document.profile.panels[0], '__proto__', {
    value:{polluted:true}, enumerable:true, configurable:true
  });
  const json = JSON.stringify(document);

  const first = W.importProfile(W.createDefaults(), json);
  const second = W.importProfile(first.state, json);
  assert.equal(first.ok, true);
  assert.equal(second.ok, true);
  assert.equal(first.profileId, 'main-2');
  assert.equal(second.profileId, 'main-3');

  const imported = first.state.profiles.find(profile => profile.id === first.profileId);
  assert.equal(imported.builtIn, null);
  assert.deepEqual(imported.panels.map(panel => panel.id), ['panel', 'panel-2']);
  assert.equal(imported.panels[0].type, 'table');
  assert.equal(imported.panels[0].preset, null);
  assert.deepEqual(imported.panels[0].sensorIds, ['/cpu/load']);
  assert.deepEqual(Object.keys(imported).sort(), ['builtIn', 'id', 'name', 'panels']);
  assert.deepEqual(Object.keys(imported.panels[0]).sort(), ['id', 'preset', 'sensorIds', 'title', 'type']);
  assert.equal({}.polluted, undefined);
  assert.equal(globalThis.__workspaceImported, undefined);

  const before = W.createDefaults();
  const beforeBytes = JSON.stringify(before);
  const rejected = W.importProfile(before, '{');
  assert.equal(rejected.error.code, 'invalid-json');
  assert.equal(JSON.stringify(rejected.state), beforeBytes);
  assert.equal(JSON.stringify(before), beforeBytes);
});

test('import failures report the complete bounded error surface', () => {
  const defaults = W.createDefaults();
  const errorFor = input => W.importProfile(defaults,
    typeof input === 'string' ? input : JSON.stringify(input)).error.code;

  assert.equal(errorFor('{'), 'invalid-json');
  assert.equal(errorFor({schema:W.PROFILE_SCHEMA, version:W.PROFILE_VERSION, profile:null}), 'invalid-profile');
  assert.equal(errorFor(profileDocument({name:' '})), 'invalid-profile-name');
  assert.equal(errorFor(profileDocument({panels:[null]})), 'invalid-panel');
  assert.equal(errorFor(profileDocument({panels:[{
    id:'panel', title:'Panel', type:'table',
    sensorIds:Array.from({length:W.LIMITS.sensorIdsPerPanel + 1}, (_, index) => '/sensor/' + index)
  }]})), 'too-many-sensors');
  assert.equal(errorFor(profileDocument({panels:[{
    id:'panel', title:'Panel', type:'table', sensorIds:['/' + 's'.repeat(W.LIMITS.sensorIdLength)]
  }]})), 'sensor-id-too-long');

  const fullState = W.normalizeState({
    schema:W.STATE_SCHEMA,
    version:W.STATE_VERSION,
    activeProfileId:'profile-0',
    profiles:Array.from({length:W.LIMITS.profiles}, (_, index) => ({
      id:'profile-' + index,
      name:'Profile ' + index,
      panels:[{id:'panel', title:'Panel', type:'table', sensorIds:[]}]
    }))
  });
  assert.equal(W.importProfile(fullState, JSON.stringify(profileDocument())).error.code, 'too-many-profiles');
});

test('normalization applies documented collection and string bounds', () => {
  assert.deepEqual(W.LIMITS, {
    profiles:10,
    panelsPerProfile:12,
    sensorIdsPerPanel:24,
    nameLength:80,
    localIdLength:96,
    sensorIdLength:192,
    profileDocumentBytes:256 * 1024,
    stateBytes:3 * 1024 * 1024
  });
  const longName = 'N'.repeat(100);
  const exactSensorId = '/' + 's'.repeat(191);
  const tooLongSensorId = '/' + 's'.repeat(192);
  const profile = index => ({
    id:'profile-' + index,
    name:longName,
    panels:Array.from({length:14}, (_, panelIndex) => ({
      id:'panel-' + panelIndex,
      title:longName,
      type:'card',
      preset:null,
      sensorIds:Array.from({length:30}, (_, sensorIndex) =>
        sensorIndex === 0 ? exactSensorId : sensorIndex === 1 ? tooLongSensorId : '/s/' + sensorIndex)
    }))
  });
  const state = W.normalizeState({
    schema:W.STATE_SCHEMA,
    version:W.STATE_VERSION,
    activeProfileId:'profile-0',
    profiles:Array.from({length:12}, (_, index) => profile(index))
  });
  assert.equal(state.profiles.length, W.LIMITS.profiles);
  assert.equal(state.profiles[0].name.length, W.LIMITS.nameLength);
  assert.equal(state.profiles[0].panels.length, W.LIMITS.panelsPerProfile);
  assert.equal(state.profiles[0].panels[0].sensorIds.length, W.LIMITS.sensorIdsPerPanel);
  assert.ok(state.profiles[0].panels[0].sensorIds.includes(exactSensorId));
  assert.ok(!state.profiles[0].panels[0].sensorIds.includes(tooLongSensorId));
});

test('profile documents and stored state enforce exact UTF-8 byte boundaries', () => {
  const exactProfile = jsonWithAsciiPadding(profileDocument(), W.LIMITS.profileDocumentBytes);
  assert.equal(W.importProfile(W.createDefaults(), exactProfile).ok, true);
  assert.equal(W.importProfile(W.createDefaults(), exactProfile + ' ').error.code, 'profile-too-large');

  const unicodeDocument = profileDocument();
  unicodeDocument.padding = '😀'.repeat(Math.ceil(W.LIMITS.profileDocumentBytes / 4));
  const unicodeJson = JSON.stringify(unicodeDocument);
  assert.ok(unicodeJson.length < W.LIMITS.profileDocumentBytes);
  assert.ok(Buffer.byteLength(unicodeJson, 'utf8') > W.LIMITS.profileDocumentBytes);
  assert.equal(W.importProfile(W.createDefaults(), unicodeJson).error.code, 'profile-too-large');

  const exactState = jsonWithAsciiPadding(W.createDefaults(), W.LIMITS.stateBytes);
  const exactResult = W.loadResult(memoryStorage({[W.STORAGE_KEY]:exactState}));
  assert.equal(exactResult.ok, true);
  assert.equal(exactResult.source, 'storage');
  const oversizedResult = W.loadResult(memoryStorage({[W.STORAGE_KEY]:exactState + ' '}));
  assert.equal(oversizedResult.ok, false);
  assert.equal(oversizedResult.error, 'state-too-large');
});

test('browser global attaches before console boot without CommonJS globals', () => {
  const source = fs.readFileSync(MODEL_PATH, 'utf8');
  const context = {window:{}};
  vm.createContext(context);
  vm.runInContext(source, context);
  assert.equal(typeof context.window.SQWorkspace.createDefaults, 'function');
});
