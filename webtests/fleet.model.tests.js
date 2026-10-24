const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const web = path.join(__dirname, '../LibreHardwareMonitor.Windows.Forms/Resources/Web');
const F = require(path.join(web, 'fleet.js'));
const W = require(path.join(web, 'workspace.js'));
const P = require(path.join(web, 'fleet-poller.js'));
const modelWindow = {SQ_NO_BOOT:true, SQFleet:F, location:{origin:'https://dashboard.example'}};
Function('window', fs.readFileSync(path.join(web, 'console.js'), 'utf8'))(modelWindow);
const S = modelWindow.SQ;
const peer = {key:'peer', label:'Peer', url:'https://peer.example'};
const local = {key:'local', label:'Local'};
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json')));
const peerFixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.peer.data.json')));
function dashboard(extra) {
  return S.normalizeDashboardState({fleet:{enabled:true, sources:[peer]}, ...extra});
}
function sample(id, raw, extra) {
  return {id, raw, hwid:'/gpu/0', cls:'gpu', type:'Power', text:'GPU Package', ...extra};
}
function tree(sensors) {
  return {Children:[{Text:'Fixture', Children:sensors.map(s => ({
    SensorId:s.id, HardwareId:s.hwid, Text:s.text, Type:s.type, RawValue:s.raw, Children:[]
  }))}]};
}
function memoryStorage(value) {
  let slot = JSON.stringify(value);
  return {getItem:() => slot, setItem:(_key, text) => {slot = text;}};
}

test('fleet identity preserves raw input and classifications while isolating joins and preferences', () => {
  const before = JSON.stringify(peerFixture);
  const a = S.flatten(fixture.Children[0], local);
  const b = S.flatten(peerFixture.Children[0], peer);
  assert.equal(JSON.stringify(peerFixture), before);
  assert.equal(new Set([...a, ...b].map(s => s.id)).size, a.length + b.length);
  for (const s of b) {
    assert.equal(s.id, F.persistenceKey(peer.key, s.rawId));
    assert.equal(s.cls, S.classOf(s.rawId));
    assert.equal(s.sourceKey, 'peer');
    assert.equal(s.sourceLabel, 'Peer');
  }
  const cpu = b.find(s => s.cls === 'cpu' && s.type === 'Temperature');
  assert.ok(cpu);
  assert.equal(S.statusOf({...cpu, raw:99}, {}), 'crit');
  const settings = dashboard({sensorAliases:{[cpu.id]:'Remote CPU'}, hiddenSensorIds:[cpu.id]});
  assert.equal(S.sensorAlias(settings, cpu.rawId), '');
  assert.equal(S.sensorAlias(settings, cpu.id), 'Remote CPU');
  const fans = S.flatten(tree([
    sample('/gpu/0/fan/0', 1500, {type:'Fan', text:'Fan'}),
    sample('/gpu/0/control/0', 25, {type:'Control', text:'Fan'})
  ]).Children[0], local);
  const peerFans = S.flatten(tree([
    sample('/gpu/0/fan/0', 2000, {type:'Fan', text:'Fan'}),
    sample('/gpu/0/control/0', 75, {type:'Control', text:'Fan'})
  ]).Children[0], peer);
  assert.equal(S.fanControlFor(peerFans[0], [...fans, ...peerFans]).raw, 75);
  assert.notEqual(S.netAdapterKey({hw:'Same', sourceKey:'local'}), S.netAdapterKey({hw:'Same', sourceKey:'peer'}));
  assert.equal(S.buildPanelItems([{hw:'Same',sourceKey:'local'}, {hw:'Same',sourceKey:'peer'}], settings).length, 2);
  const withoutHardware = {Text:'Same', Children:[{SensorId:'/nvme/temperature/limit', Text:'Critical Temperature',
    Type:'Temperature', RawValue:90, Children:[]}]};
  const localLimit = S.flatten(withoutHardware, local)[0], peerLimit = S.flatten(withoutHardware, peer)[0];
  assert.notEqual(localLimit.hwid, peerLimit.hwid);
  assert.equal(peerLimit.rawHardwareId, undefined);
  assert.equal(Object.keys(S.deriveLimits([localLimit, peerLimit])).length, 2);
});

test('automatic hero and Workspace preset selection include each source independently', () => {
  const data = tree([sample('/intelcpu/0/temperature/0', 42, {type:'Temperature', text:'CPU Package'})]);
  const sensors = [...S.flatten(data.Children[0], local), ...S.flatten(data.Children[0], peer)];
  assert.deepEqual(S.pickHero(sensors, {}).map(h => h.s.sourceKey), ['local','peer']);
  assert.deepEqual(W.resolvePreset('main-primary', sensors), sensors.map(s => s.id));
  assert.deepEqual(W.resolvePreset('main-primary', [null, ...sensors]), sensors.map(s => s.id));
});

test('configuration survives reload, context switches and telemetry saves; corrupt fleet preserves other settings', () => {
  const initial = dashboard({theme:'light', sensorAliases:{'/cpu':'Main'}});
  const storage = memoryStorage(initial);
  const saved = S.saveTelemetryState(storage, dashboard({observedMax:{'/power':120}}));
  assert.deepEqual(saved.fleet, initial.fleet);
  assert.equal(S.loadDashboardState(storage).theme, 'light');
  assert.equal(S.switchDashboardContext(storage, saved, 'gaming').dashboard.fleet.enabled, true);
  const bad = S.normalizeDashboardState({theme:'light', fleet:{enabled:true, sources:[{...peer, url:'http://peer.example'}]}});
  assert.equal(bad.fleet.enabled, false);
  assert.equal(bad.theme, 'light');
  assert.ok(bad.fleetError);
  assert.ok(S.normalizeDashboardState(bad).fleetError);
});

test('samples ingest once and retain sibling history across interleaved source successes', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard()};
  const data = tree([sample('/gpu/0/power/0', 100)]);
  const first = S.ingestSourceTelemetry(runtime, local, data, {generation:1, completedAt:0});
  assert.equal(first.ingested, true);
  S.ingestSourceTelemetry(runtime, peer, data, {generation:1, completedAt:1000});
  assert.equal(S.ingestSourceTelemetry(runtime, peer, data, {generation:1, completedAt:2000}).ingested, false);
  S.ingestSourceTelemetry(runtime, peer, data, {generation:2, completedAt:S.SENSOR_STATE_GRACE_MS + 2000});
  assert.equal(S.historyFor('/gpu/0/power/0').length, 1);
  assert.equal(S.historyFor('fleet:peer|/gpu/0/power/0').length, 2);
  assert.equal(runtime.tickCount, 3);
  const before = JSON.stringify(runtime.dashboard);
  S.ingestTelemetry(runtime, runtime.sourceSensors.get('peer'), false, 999999);
  assert.equal(JSON.stringify(runtime.dashboard), before);
});

test('source failure breaks derivatives without deleting displayed history or sibling trends', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard()};
  for (let i = 1; i <= 3; i++) {
    const data = tree([sample('/gpu/0/power/0', i * 100)]);
    for (const source of [local, peer]) S.ingestSourceTelemetry(runtime, source, data, {generation:i, completedAt:i * 1000});
  }
  assert.ok(S.trendFor('fleet:peer|/gpu/0/power/0', 'power', 3000));
  S.resetSourceDerivatives('peer');
  assert.equal(S.trendFor('fleet:peer|/gpu/0/power/0', 'power', 3000), null);
  assert.ok(S.trendFor('/gpu/0/power/0', 'power', 3000));
  assert.equal(S.historyFor('fleet:peer|/gpu/0/power/0').length, 3);
  S.ingestSourceTelemetry(runtime, peer, tree([sample('/gpu/0/power/0', 900)]), {generation:4, completedAt:4000});
  assert.equal(S.trendFor('fleet:peer|/gpu/0/power/0', 'power', 4000), null);
});

test('per-source quotas cover normalization, ingestion and persisted merges without unknown source buckets', () => {
  S.resetTelemetryCaches();
  const observedMax = {'/local':1}, powerLimitSamples = {'/local-hw':[10]};
  for (let i = 0; i < 2300; i++) {
    observedMax['fleet:peer|/sensor/' + i] = i;
    powerLimitSamples['fleet:peer|/hw/' + i] = [i + 1];
    observedMax['fleet:unknown' + i + '|/sensor'] = i;
  }
  const state = dashboard({observedMax, powerLimitSamples});
  assert.equal(Object.keys(state.observedMax).length, 2049);
  assert.equal(Object.keys(state.powerLimitSamples).length, 513);
  assert.equal(state.observedMax['/local'], 1);
  const storage = memoryStorage(state);
  assert.equal(Object.keys(S.saveTelemetryState(storage, state).observedMax).length, 2049);
  const runtime = {dashboard:state};
  S.ingestSourceTelemetry(runtime, local, tree([sample('/local', 1)]), {generation:1, completedAt:0});
  const many = Array.from({length:2300}, (_, i) => sample('/sensor/' + i, i + 1));
  S.ingestSourceTelemetry(runtime, peer, tree(many), {generation:1, completedAt:1000});
  assert.equal(S.telemetryCacheSizes().history, 2049);
  assert.equal(S.historyFor('/local').length, 1);
});

test('source removal deletes derived telemetry through persistence while retaining missing selections', () => {
  S.resetTelemetryCaches();
  const id = 'fleet:peer|/gpu/0/power/0';
  const runtime = {dashboard:dashboard({hiddenSensorIds:[id]})};
  S.ingestSourceTelemetry(runtime, peer, tree([sample('/gpu/0/power/0', 100)]), {generation:1, completedAt:1});
  const storage = memoryStorage(runtime.dashboard);
  S.acknowledgeTelemetrySave(runtime);
  const removal = S.removeSourceTelemetry(runtime, 'peer');
  assert.equal(S.historyFor(id).length, 0);
  const saved = S.saveTelemetryState(storage, runtime.dashboard, removal.removals);
  assert.equal(saved.observedMax[id], undefined);
  assert.deepEqual(saved.hiddenSensorIds, [id]);
  assert.equal(runtime.sourceSensors.has('peer'), false);
});

test('unsaved sensor churn never grows persisted-removal queues; save acknowledgement bounds tombstones', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard({observedMax:{'/saved':10}})};
  for (let i = 1; i <= 20; i++) {
    S.ingestSourceTelemetry(runtime, local, tree([sample('/replacement/' + i, 100)]),
      {generation:i, completedAt:i * (S.SENSOR_STATE_GRACE_MS + 1)});
  }
  assert.deepEqual(runtime.telemetryRemovals.observedMax, ['/saved']);
  const storage = memoryStorage({observedMax:{'/saved':10}});
  runtime.dashboard = S.saveTelemetryState(storage, runtime.dashboard, runtime.telemetryRemovals);
  S.acknowledgeTelemetrySave(runtime);
  assert.deepEqual(runtime.telemetryRemovals.observedMax, []);
  assert.equal(runtime.telemetrySavedKeys.observedMax.size, 1);
});

test('Workspace keeps raw 192 and peer 231 limits, round trips missing references and rejects oversize edits', () => {
  const raw = '/' + 'x'.repeat(191);
  const envelope = 'fleet:' + 'k'.repeat(32) + '|' + raw;
  let state = W.createDefaults();
  const profile = state.profiles[0], panel = profile.panels[0];
  state = W.setPanelSensorIds(state, profile.id, panel.id, [raw, envelope]);
  const json = W.exportProfile(state, profile.id, true);
  const imported = W.importProfile(W.createDefaults(), json);
  assert.equal(imported.ok, true);
  const importedPanel = imported.state.profiles.find(p => p.id === imported.profileId).panels[0];
  assert.deepEqual(importedPanel.sensorIds, [raw, envelope]);
  assert.deepEqual(W.resolvePanel(importedPanel, [{id:raw}]).missingSensorIds, [envelope]);
  assert.throws(() => W.togglePanelSensor(state, profile.id, panel.id, raw + 'x', true, []), /192/);
  assert.throws(() => W.setPanelSensorIds(state, profile.id, panel.id, [envelope + 'x']), /192/);
  const invalid = JSON.parse(json);
  invalid.profile.panels[0].sensorIds = [envelope + 'x'];
  assert.equal(W.importProfile(state, JSON.stringify(invalid)).ok, false);
});

test('source transitions protect missing references in active, parked and Workspace settings', () => {
  const state = dashboard({hiddenSensorIds:['fleet:active|/missing']});
  const contexts = {saved:{gaming:{pinnedCards:[{id:'fleet:parked|/missing'}]}}};
  const workspace = {profiles:[{panels:[{sensorIds:['fleet:workspace|/missing']}]}]};
  const before = JSON.stringify({state, contexts, workspace});
  const options = S.fleetSourceOptions(state, contexts, workspace);
  for (const key of ['active','parked','workspace']) {
    assert.equal(options.knownOrigins[key], null);
    assert.throws(() => S.applyFleetState(state, {enabled:true, sources:[{...peer, key}]}, contexts, workspace));
  }
  const renamed = S.applyFleetState(state, F.renameSource(state.fleet, 'peer', 'Renamed', options), contexts, workspace);
  assert.equal(renamed.fleet.sources[0].key, 'peer');
  assert.equal(renamed.fleet.sources[0].label, 'Renamed');
  assert.equal(renamed.fleetOrigins.workspace, null);
  assert.equal(JSON.stringify({state, contexts, workspace}), before);
});

test('coordinator successes feed real model caches, persist removals and recover a re-imported source', async () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard()};
  const storage = memoryStorage(runtime.dashboard), requests = [];
  const flush = async () => { for (let i = 0; i < 16; i++) await Promise.resolve(); };
  const response = data => ({ok:true, headers:{get:() => 'application/json'}, json:async () => data});
  const controller = P.create({fleet:runtime.dashboard.fleet, pageOrigin:modelWindow.location.origin,
    paused:true, fetch:(url, init) => new Promise(resolve => requests.push({url, init, resolve})),
    onSample:(source, data, receipt) => {
      const result = S.ingestSourceTelemetry(runtime, source, data, receipt);
      runtime.dashboard = S.saveTelemetryState(storage, runtime.dashboard, result.removals);
      S.acknowledgeTelemetrySave(runtime);
    },
    onGap:source => S.resetSourceDerivatives(source.key),
    onRemove:source => {
      const result = S.removeSourceTelemetry(runtime, source.key);
      runtime.dashboard = S.saveTelemetryState(storage, runtime.dashboard, result.removals);
      S.acknowledgeTelemetrySave(runtime);
    }
  });
  try {
    controller.start(true);
    assert.equal(requests.length, 2);
    requests[0].resolve(response(fixture));
    await flush();
    assert.ok(runtime.sourceSensors.has('local'), 'local publishes while peer is pending');
    requests[1].resolve(response(peerFixture));
    await flush();
    const id = runtime.sourceSensors.get('peer')[0].id;
    assert.equal(S.historyFor(id).length, 1);
    controller.refresh();
    requests[2].resolve(response(fixture));
    requests[3].resolve(response(peerFixture));
    await flush();
    assert.equal(S.historyFor(id).length, 2, 'unchanged successful response remains a sample');
    const restoredFleet = runtime.dashboard.fleet;
    const configure = next => {
      runtime.dashboard = S.applyFleetState(runtime.dashboard, next);
      runtime.dashboard = S.saveDashboardState(storage, runtime.dashboard);
      S.acknowledgeTelemetrySave(runtime);
      controller.configure(runtime.dashboard.fleet);
    };
    configure({enabled:true, sources:[]});
    assert.equal(S.historyFor(id).length, 0);
    assert.equal(S.loadDashboardState(storage).fleet.sources.length, 0);
    assert.ok(!Object.keys(S.loadDashboardState(storage).observedMax).some(key => key.startsWith('fleet:peer|')));
    configure(restoredFleet);
    controller.refresh();
    for (const request of requests.slice(4))
      request.resolve(response(request.url === 'data.json' ? fixture : peerFixture));
    await flush();
    assert.equal(S.historyFor(id).length, 1, 'same-key reimport recovers on its first new sample');
  } finally { controller.dispose(); }
});
