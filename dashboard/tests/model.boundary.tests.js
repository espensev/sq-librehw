const test = require('node:test');
const assert = require('node:assert/strict');
const Model = require('../web/model.js');
const Identity = require('../web/identity.js');
const Workspace = require('../web/workspace.js');

test('model instances own their telemetry and animation state without a browser', () => {
  const first = Model.create(), second = Model.create();
  const sensor = {id:'/intelcpu/0/temperature/0', raw:42, type:'Temperature'};
  first.trackSensorHistory([sensor], 1000);
  first.trackSensorMotion([sensor]);
  first.smoothFraction(sensor.id, 0.5, 2);
  assert.equal(first.historyFor(sensor.id).length, 1);
  assert.equal(first.smoothedFraction(sensor.id), 0.5);
  assert.deepEqual(second.telemetryCacheSizes(), {motion:0, history:0, smooth:0, trends:0});
  second.resetTelemetryCaches();
  assert.equal(first.historyFor(sensor.id).length, 1);
  assert.equal(first.smoothedFraction(sensor.id), 0.5);
});

test('each model validates sources against its explicit host origin', () => {
  const fleet = {enabled:true, sources:[{key:'peer', label:'Peer', url:'https://peer.example'}]};
  const first = Model.create({pageOrigin:'https://peer.example'});
  const second = Model.create({pageOrigin:'https://viewer.example'});
  assert.equal(first.normalizeDashboardState({fleet}).fleet.enabled, false);
  assert.deepEqual(second.normalizeDashboardState({fleet}).fleet, fleet);
});

test('shared sensor identities preserve local and maximum-length Workspace references', () => {
  const raw = '/' + 's'.repeat(191);
  const peer = Identity.persistenceKey('p'.repeat(32), raw);
  assert.equal(Identity.persistenceKey('local', raw), raw);
  assert.equal(peer.length, 231);
  assert.equal(Identity.isWorkspaceId(peer), true);
  assert.equal(Identity.isWorkspaceId(peer + 'x'), false);
  const state = Workspace.createDefaults();
  const profile = state.profiles[0], panel = profile.panels[0];
  const edited = Workspace.setPanelSensorIds(state, profile.id, panel.id, [raw, peer]);
  const imported = Workspace.importProfile(state, Workspace.exportProfile(edited, profile.id));
  assert.equal(imported.ok, true);
  assert.deepEqual(imported.state.profiles.find(p => p.id === imported.profileId).panels[0].sensorIds, [raw, peer]);
});
