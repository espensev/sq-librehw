// Fleet session composition, source status, attribution, and alarm gating.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const web = path.join(__dirname, '../web');
const P = require(path.join(web, 'fleet-poller.js'));
const pageOrigin = 'https://dashboard.example';
const S = require(path.join(web, 'model.js')).create({pageOrigin});
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json')));
const peerFixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.peer.data.json')));
const local = {key:'local', label:'SND-DESK'};
const peers = Array.from({length:8}, (_, i) => ({key:'peer' + i, label:'Peer ' + i, url:'https://peer' + i + '.example'}));
function dashboard(sources, enabled = true, extra = {}) {
  return S.normalizeDashboardState({fleet:{enabled, sources}, ...extra});
}
function stateOf(source, status, extra = {}) {
  return {source, status, lastGood:status === 'pending' || status === 'unavailable' ? null : {}, lastSuccess:1000,
    error:null, generation:1, ingestionGeneration:1, active:false, scheduled:false, ...extra};
}
function ingestAll(runtime, sources, data = peerFixture) {
  sources.forEach((source, i) => S.ingestSourceTelemetry(runtime, source, data, {generation:1, completedAt:1000 + i}));
}

test('composition follows configured order with local first and honours the enabled flag', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard(peers)};
  S.ingestSourceTelemetry(runtime, local, fixture, {generation:1, completedAt:1});
  ingestAll(runtime, peers);
  const sensors = S.composeSensors(runtime, runtime.dashboard);
  const order = [...new Set(sensors.map(S.sensorSourceKey))];
  assert.deepEqual(order, ['local', ...peers.map(p => p.key)]);
  assert.equal(sensors.length, S.flatten(fixture.Children[0]).length + 8 * S.flatten(peerFixture.Children[0]).length);
  assert.equal(new Set(sensors.map(s => s.id)).size, sensors.length, 'persistence keys stay unique across eight peers');
  const disabled = S.composeSensors(runtime, dashboard(peers, false));
  assert.deepEqual([...new Set(disabled.map(S.sensorSourceKey))], ['local']);
  assert.equal(runtime.sourceSensors.size, 9, 'disabling keeps retained peer telemetry');
  assert.deepEqual(S.composeSensors({}, runtime.dashboard), []);
  assert.equal(S.hasTelemetry(runtime), true);
  assert.equal(S.hasTelemetry({}), false);
  assert.equal(S.fleetActive(dashboard([])), false);
  assert.equal(S.fleetActive(dashboard(peers.slice(0, 1))), true);
  assert.equal(S.fleetActive(dashboard(peers.slice(0, 1), false)), false);
});

test('a reordered machine list changes composition without touching stored identities', () => {
  S.resetTelemetryCaches();
  const two = peers.slice(0, 2);
  const runtime = {dashboard:dashboard(two)};
  S.ingestSourceTelemetry(runtime, local, fixture, {generation:1, completedAt:1});
  ingestAll(runtime, two);
  const before = S.composeSensors(runtime, runtime.dashboard).map(s => s.id);
  const reordered = dashboard([two[1], two[0]]);
  const after = S.composeSensors(runtime, reordered).map(s => s.id);
  assert.deepEqual(new Set(after), new Set(before));
  assert.notDeepEqual(after, before);
  assert.equal(S.sensorSourceKey({id:'fleet:peer1|/x'}), 'peer1');
  assert.equal(S.sensorSourceKey({id:'/x'}), 'local');
});

test('zero peers and a valid empty machine both compose cleanly', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard([])};
  S.ingestSourceTelemetry(runtime, local, fixture, {generation:1, completedAt:1});
  assert.equal(S.composeSensors(runtime, runtime.dashboard).length, S.flatten(fixture.Children[0]).length);
  const empty = {Text:'Sensor', Children:[{Text:'EMPTY', Children:[]}]};
  P.validateTelemetry(empty);
  const one = peers.slice(0, 1);
  const runtime2 = {dashboard:dashboard(one)};
  S.ingestSourceTelemetry(runtime2, local, fixture, {generation:1, completedAt:1});
  const result = S.ingestSourceTelemetry(runtime2, one[0], empty, {generation:1, completedAt:2});
  assert.equal(result.ingested, true);
  assert.deepEqual(runtime2.sourceSensors.get('peer0'), []);
  const statuses = S.sourceStatusMap([stateOf(local, 'current'), stateOf(one[0], 'current', {lastGood:empty})]);
  assert.equal(statuses.get('peer0').status, 'current');
  assert.equal(statuses.get('peer0').hasData, true);
});

test('source status map, stale keys and status text describe each machine independently', () => {
  const now = 61000;
  const states = [
    stateOf(local, 'current', {lastSuccess:58000}),
    stateOf(peers[0], 'stale', {lastSuccess:1000, error:'Telemetry request timeout.'}),
    stateOf(peers[1], 'unavailable', {lastSuccess:null, error:'HTTP 404'}),
    stateOf(peers[2], 'pending', {lastSuccess:null}),
    stateOf(peers[3], 'paused', {lastSuccess:1000}),
    {source:{key:'', label:'bad'}, status:'current'},
    {source:{key:peers[4].key, label:'Odd'}, status:'nonsense', lastSuccess:'x'}
  ];
  const map = S.sourceStatusMap(states);
  assert.deepEqual([...map.keys()], ['local', 'peer0', 'peer1', 'peer2', 'peer3', 'peer4']);
  assert.equal(map.get('peer4').status, 'pending');
  assert.equal(map.get('peer4').lastSuccess, null);
  assert.deepEqual([...S.staleSourceKeys(states)], ['peer0']);
  assert.equal(S.sourceStatusText(map.get('local'), now), 'current · 3s ago');
  assert.equal(S.sourceStatusText(map.get('peer0'), now), 'stale · 1m ago · Telemetry request timeout.');
  assert.equal(S.sourceStatusText(map.get('peer1'), now), 'unavailable · HTTP 404');
  assert.equal(S.sourceStatusText(map.get('peer2'), now), 'connecting…');
  assert.equal(S.sourceStatusText(map.get('peer3'), now), 'paused · last 1m ago');
  assert.equal(S.sourceStatusText(null), 'unknown');
  assert.equal(S.sourceAgeText(null), 'no sample yet');
  assert.equal(S.sourceAgeText(0, 2 * 3600 * 1000), '2h ago');
  assert.deepEqual(S.staleSourceKeys(undefined), new Set());
  assert.deepEqual(S.sourceStatusMap(null), new Map());
});

test('stale readings keep their severity but are excluded from current alarms', () => {
  const cpu = {id:'/intelcpu/0/temperature/0', cls:'cpu', type:'Temperature', text:'CPU Package', raw:99, hwid:'/intelcpu/0'};
  const peerCpu = {...cpu, id:'fleet:peer0|' + cpu.id, sourceKey:'peer0', sourceLabel:'Peer 0', rawId:cpu.id};
  const sensors = [cpu, peerCpu].map(s => ({...s, status:S.statusOf(s, {})}));
  assert.deepEqual(sensors.map(s => s.status), ['crit', 'crit'], 'classification is unchanged by the namespace');
  const stale = S.staleSourceKeys([stateOf(local, 'current'), stateOf(peers[0], 'stale')]);
  assert.deepEqual(S.currentAlarms(sensors, stale).map(s => s.id), [cpu.id]);
  assert.equal(S.isStaleSensor(peerCpu, stale), true);
  assert.equal(S.isStaleSensor(cpu, stale), false);
  assert.deepEqual(S.currentAlarms(sensors, new Set()).length, 2);
  const paused = S.staleSourceKeys([stateOf(local, 'paused'), stateOf(peers[0], 'paused')]);
  assert.equal(S.currentAlarms(sensors, paused).length, 2, 'paused sources keep the existing alarm presentation');
  assert.deepEqual(S.currentAlarms([{status:'info'}, {status:'off'}, null], new Set()), []);
  assert.deepEqual(S.currentAlarms(null, new Set()), []);
});

test('machine labels come from the host for local and the configured label for peers', () => {
  const cfg = dashboard(peers.slice(0, 2));
  assert.equal(S.machineLabel('local', cfg, 'SND-DESK'), 'SND-DESK');
  assert.equal(S.machineLabel({id:'/x', sourceLabel:'Local'}, cfg, null), 'Local');
  assert.equal(S.machineLabel({id:'/x'}, cfg, '  '), 'Local');
  assert.equal(S.machineLabel({id:'fleet:peer1|/x'}, cfg, 'SND-DESK'), 'Peer 1');
  assert.equal(S.machineLabel('peer1', cfg), 'Peer 1');
  assert.equal(S.machineLabel({id:'fleet:gone|/x', sourceLabel:'Old'}, cfg), 'Old');
  assert.equal(S.machineLabel('gone', cfg), 'gone');
  const flat = S.flatten(peerFixture.Children[0], peers[0]);
  assert.equal(S.machineLabel(flat[0], cfg), 'Peer 0');
  assert.equal(S.machineLabel(flat[0], dashboard([{...peers[0], label:'Renamed'}])), 'Renamed', 'renames keep the key');
});

test('fleet summary counts peers by status only while the fleet is enabled', () => {
  const states = [stateOf(local, 'current'), stateOf(peers[0], 'current'), stateOf(peers[1], 'stale'),
    stateOf(peers[2], 'unavailable'), stateOf(peers[3], 'pending'), stateOf(peers[4], 'paused')];
  const five = peers.slice(0, 5);
  const summary = S.fleetSummary(dashboard(five), states);
  assert.equal(summary.text, '5 peers · 1 current · 1 stale · 1 unavailable · 1 connecting · 1 paused');
  assert.equal(S.fleetSummary(dashboard(five, false), states).text, '');
  assert.equal(S.fleetSummary(dashboard([]), states).text, '');
  assert.equal(S.fleetSummary(dashboard(peers.slice(0, 1)), []).text, '1 peer · 1 connecting');
  assert.equal(S.fleetSummary(dashboard(peers.slice(0, 1)), states).text, '1 peer · 1 current');
});

test('panels and adapters group by machine before hardware class and carry the source key', () => {
  S.resetTelemetryCaches();
  const runtime = {dashboard:dashboard(peers.slice(0, 1))};
  S.ingestSourceTelemetry(runtime, local, fixture, {generation:1, completedAt:1});
  S.ingestSourceTelemetry(runtime, peers[0], peerFixture, {generation:1, completedAt:2});
  const sensors = S.composeSensors(runtime, runtime.dashboard);
  S.trackSensorMotion(sensors);
  const items = S.buildPanelItems(sensors, runtime.dashboard);
  const hardware = items.filter(item => !item.net);
  const firstPeer = hardware.findIndex(item => item.sourceKey === 'peer0');
  assert.ok(firstPeer > 0);
  assert.ok(hardware.slice(0, firstPeer).every(item => item.sourceKey === 'local'));
  assert.ok(hardware.slice(firstPeer).every(item => item.sourceKey === 'peer0'));
  const order = ['cpu','gpu','igpu','mem','dimm','nvme','disk','mb','other'];
  const peerClasses = hardware.slice(firstPeer).map(item => order.indexOf(item.ss[0].cls));
  assert.deepEqual(peerClasses, peerClasses.slice().sort((a, b) => a - b), 'class order holds within a machine');
  items.filter(item => item.net).forEach(item => assert.ok(['local', 'peer0'].includes(item.sourceKey)));
  const rows = S.sensorPopoverRows(sensors, runtime.dashboard, 'peer 0');
  assert.ok(rows.length > 0);
  assert.ok(rows.every(row => row.sourceKey === 'peer0' && row.machine === 'Peer 0'));
});

test('popover rows expose the machine for search and labelling without changing local identities', () => {
  const sensors = S.flatten(fixture.Children[0]);
  const rows = S.sensorPopoverRows(sensors, S.defaultDashboardState(), '');
  assert.ok(rows.length > 0);
  assert.ok(rows.every(row => row.sourceKey === 'local' && row.machine === ''));
  assert.equal(rows[0].id, sensors.find(s => s.id === rows[0].id).id);
});
