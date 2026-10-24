const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const P = require('../LibreHardwareMonitor.Windows.Forms/Resources/Web/fleet-poller.js');
const vm = require('node:vm');

test('browser origin remains the default when poller options omit pageOrigin', () => {
  const window = {location:{origin:'https://self.example'}, AbortController, fetch:() => {}};
  const context = vm.createContext({window, URL, setTimeout, clearTimeout});
  const web = path.join(__dirname, '../LibreHardwareMonitor.Windows.Forms/Resources/Web');
  for (const name of ['fleet.js','fleet-poller.js'])
    vm.runInContext(fs.readFileSync(path.join(web, name), 'utf8'), context);
  const self = {enabled:true, sources:[{key:'self', label:'Self', url:'https://self.example'}]};
  assert.throws(() => window.SQFleetPoller.create({fleet:self}), /local source/);
  const controller = window.SQFleetPoller.create({});
  assert.throws(() => controller.configure(self), /local source/);
  controller.dispose();
});

const peer = {key:'peer', label:'Peer', url:'https://peer.example'};
const other = {key:'other', label:'Other', url:'https://other.example'};
const fleet = {enabled:true, sources:[peer]};
const tree = (raw = 40) => ({Text:'Sensor', Children:[{Text:'Machine', Children:[
  {Text:'CPU', HardwareId:'/amdcpu/0', Children:[
    {Text:'Temperature', Type:'Temperature', SensorId:'/amdcpu/0/temperature/0', RawValue:raw, Children:[]}
  ]}
]}]});
const response = (data = tree(), extra = {}) => ({ok:true, status:200, redirected:false,
  headers:{get:() => 'application/json; charset=utf-8'}, json:async () => data, ...extra});
async function flush() { for (let i = 0; i < 12; i++) await Promise.resolve(); }

function clock() {
  let time = 0, sequence = 0;
  const timers = new Map();
  return {
    now:() => time,
    setTimeout(fn, delay) { const id = ++sequence; timers.set(id, {fn, at:time + delay}); return id; },
    clearTimeout:id => timers.delete(id),
    async advance(ms) {
      const end = time + ms;
      while (true) {
        const next = [...timers].filter(([, value]) => value.at <= end)
          .sort((a, b) => a[1].at - b[1].at || a[0] - b[0])[0];
        if (!next) break;
        time = next[1].at;
        timers.delete(next[0]);
        next[1].fn();
        await flush();
      }
      time = end;
      await flush();
    },
    count:() => timers.size
  };
}

function harness(options = {}) {
  const timer = clock(), requests = [], samples = [], gaps = [], removed = [], changes = [];
  const controller = P.create({fleet, pageOrigin:'https://dashboard.example', intervalMs:1000,
    ...timer, ...options,
    fetch:(url, init) => new Promise((resolve, reject) => requests.push({url, init, resolve, reject})),
    onSample:(source, data, receipt) => samples.push({source, data, receipt}),
    onGap:source => gaps.push(source.key), onRemove:source => removed.push(source.key),
    onState:states => changes.push(states)
  });
  return {controller, timer, requests, samples, gaps, removed, changes,
    state:key => controller.states().find(value => value.source.key === key)};
}

test('resumed retained samples remain paused until success, or become stale on failure', async () => {
  const h = harness();
  try {
    h.controller.start();
    h.requests.forEach(r => r.resolve(response()));
    await flush();
    const success = h.state('peer').lastSuccess;
    h.controller.configure({enabled:false, sources:[peer]});
    h.controller.configure(fleet);
    assert.equal(h.state('peer').status, 'paused');
    assert.equal(h.state('peer').lastSuccess, success);
    h.requests.at(-1).resolve(response());
    await flush();
    assert.equal(h.state('peer').status, 'current');
    h.controller.setPaused(true);
    h.controller.setPaused(false);
    assert.ok(h.controller.states().every(s => s.status === 'paused'));
    h.requests.at(-1).reject(new Error('offline'));
    await flush();
    assert.equal(h.state('peer').status, 'stale');
  } finally { h.controller.dispose(); }
});

test('a removed suspended source re-added during cancellation has no paused snapshot', async () => {
  const h = harness();
  try {
    h.controller.start();
    h.requests.forEach(r => r.resolve(response()));
    await flush();
    h.controller.refresh();
    h.controller.configure({enabled:false, sources:[peer]});
    h.controller.configure({enabled:false, sources:[]});
    h.controller.configure(fleet);
    assert.equal(h.state('peer').lastGood, null);
    assert.equal(h.state('peer').status, 'pending');
  } finally { h.controller.dispose(); }
});

test('telemetry validation accepts both fixtures and an empty machine without rewriting wire data', () => {
  for (const name of ['fixture.data.json', 'fixture.peer.data.json']) {
    const data = JSON.parse(fs.readFileSync(path.join(__dirname, name), 'utf8'));
    const before = JSON.stringify(data);
    assert.equal(P.validateTelemetry(data), data);
    assert.equal(JSON.stringify(data), before);
  }
  assert.doesNotThrow(() => P.validateTelemetry({Text:'Sensor', Children:[{Text:'Empty', Children:[], FutureField:true}]}));
  const cycle = tree(); cycle.Children[0].Children.push(cycle);
  const duplicate = tree(); duplicate.Children[0].Children.push(duplicate.Children[0].Children[0]);
  for (const invalid of [null, [], {}, {Children:[]}, {Children:[{}, {}]},
    {Text:'Sensor', Children:[{Text:'Machine', Children:null}]}, cycle, duplicate,
    {Text:'Sensor', Children:[{Text:'Machine', Children:[{Text:'Bad', SensorId:5, Children:[]}]}]}]) {
    assert.throws(() => P.validateTelemetry(invalid), /telemetry|sensor|tree|machine/i);
  }
});

test('each source publishes and schedules independently; identical successful data is a new sample', async () => {
  const h = harness({fleet:{enabled:true, sources:[peer, other]}});
  h.controller.start();
  assert.deepEqual(h.requests.map(r => r.url), ['data.json', peer.url + '/data.json', other.url + '/data.json']);
  h.requests[0].resolve(response());
  h.requests[2].reject(new TypeError('CORS blocked'));
  await flush();
  assert.deepEqual(h.samples.map(s => s.source.key), ['local']);
  assert.equal(h.state('local').status, 'current');
  assert.equal(h.state('peer').status, 'pending');
  assert.equal(h.state('other').status, 'unavailable');
  await h.timer.advance(1000);
  assert.equal(h.requests.filter(r => r.url === 'data.json').length, 2);
  assert.equal(h.requests.filter(r => r.url === peer.url + '/data.json').length, 1);
  h.requests.find((r, index) => index > 2 && r.url === 'data.json').resolve(response());
  h.requests[1].resolve(response());
  await flush();
  assert.deepEqual(h.samples.map(s => s.source.key), ['local', 'local', 'peer']);
  assert.deepEqual(h.samples.map(s => s.receipt.completedAt), [0, 1000, 1000]);
  assert.notEqual(h.samples[0].receipt.generation, h.samples[1].receipt.generation);
  assert.equal(h.state('local').ingestionGeneration, 2);
  h.controller.dispose();
});

test('peer request options restrict dispatch to credential-free telemetry GETs', async () => {
  const h = harness();
  h.controller.start();
  const request = h.requests[1];
  assert.equal(request.url, peer.url + '/data.json');
  assert.deepEqual(Object.keys(request.init).sort(), ['cache','credentials','method','redirect','signal']);
  assert.equal(request.init.method, 'GET');
  assert.equal(request.init.credentials, 'omit');
  assert.equal(request.init.redirect, 'error');
  assert.equal(request.init.cache, 'no-store');
  request.resolve(response(tree(), {redirected:true}));
  await flush();
  assert.equal(h.state('peer').status, 'unavailable');
  assert.match(h.state('peer').error, /redirect/i);
  assert.equal(h.samples.length, 0);
  h.controller.dispose();
});

test('HTTP, MIME, JSON, tree, duplicate-ID and transport failures keep last-good data and recover', async () => {
  const duplicate = tree();
  duplicate.Children[0].Children[0].Children.push({...duplicate.Children[0].Children[0].Children[0]});
  const failures = [
    {reply:response(null, {ok:false, status:503}), error:/503/},
    {reply:response('<html>Login</html>', {headers:{get:() => 'text/html'}}), error:/JSON/i},
    {reply:response(null, {json:async () => { throw new SyntaxError('invalid JSON'); }}), error:/JSON/i},
    {reply:response({Children:[null]}), error:/tree|machine|telemetry/i},
    {reply:response(duplicate), error:/duplicate/i},
    {reject:new TypeError('CORS blocked'), error:/CORS/},
    {reject:new Error(), error:/failed/i}
  ];
  for (const failure of failures) {
    const h = harness();
    h.controller.start();
    h.requests[0].resolve(response());
    h.requests[1].resolve(response(tree(51)));
    await flush();
    h.controller.refresh();
    h.requests[2].resolve(response());
    if (failure.reject) h.requests[3].reject(failure.reject); else h.requests[3].resolve(failure.reply);
    await flush();
    assert.equal(h.state('local').status, 'current');
    assert.equal(h.state('peer').status, 'stale');
    assert.match(h.state('peer').error, failure.error);
    assert.equal(h.state('peer').lastGood.Children[0].Children[0].Children[0].RawValue, 51);
    assert.equal(h.samples.filter(s => s.source.key === 'peer').length, 1);
    assert.ok(h.gaps.includes('peer'));
    h.controller.refresh();
    h.requests[5].resolve(response(tree(52)));
    await flush();
    assert.equal(h.state('peer').status, 'current');
    assert.equal(h.state('peer').error, null);
    assert.equal(h.samples.filter(s => s.source.key === 'peer').length, 2);
    h.controller.dispose();
  }
});

test('timeout marks only its source immediately, suppresses late success and retains single-flight ownership', async () => {
  const h = harness();
  h.controller.start();
  h.requests[0].resolve(response());
  await flush();
  await h.timer.advance(15000);
  assert.equal(h.requests[1].init.signal.aborted, true);
  assert.equal(h.state('peer').status, 'unavailable');
  assert.match(h.state('peer').error, /timeout/i);
  assert.equal(h.requests.filter(r => r.url === peer.url + '/data.json').length, 1);
  assert.equal(h.state('local').status, 'current');
  await h.timer.advance(1000);
  assert.equal(h.state('local').status, 'stale', 'receipt age also expires while its request is outstanding');
  h.controller.refresh();
  assert.equal(h.requests.filter(r => r.url === peer.url + '/data.json').length, 1);
  h.requests[1].resolve(response(tree(99)));
  await flush();
  assert.equal(h.samples.some(s => s.source.key === 'peer'), false);
  assert.equal(h.requests.filter(r => r.url === peer.url + '/data.json').length, 2);
  h.controller.dispose();
});

test('pause and visibility cancellation suppress late samples and resume once per source', async () => {
  const h = harness();
  h.controller.start();
  h.controller.refresh();
  assert.equal(h.requests.length, 2);
  h.controller.setPaused(true);
  assert.ok(h.requests.every(r => r.init.signal.aborted));
  assert.ok(h.controller.states().every(s => s.status === 'paused' && s.error === null));
  h.controller.setPaused(false);
  assert.equal(h.requests.length, 2);
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 4);
  h.controller.setHidden(true);
  h.controller.setHidden(false);
  h.requests.slice(2).forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 6);
  h.requests.slice(4).forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 2);
  assert.ok(h.controller.states().every(s => s.status === 'current' && s.error === null));
  h.controller.dispose();
  assert.equal(h.timer.count(), 0);
});

test('paused startup and explicit refresh take one snapshot; hidden cancellation preserves the pending snapshot', async () => {
  const h = harness({paused:true, hidden:true});
  h.controller.start(true);
  assert.equal(h.requests.length, 0);
  h.controller.setHidden(false);
  assert.equal(h.requests.length, 2);
  h.controller.setHidden(true);
  h.controller.setHidden(false);
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 4);
  h.requests.slice(2).forEach(r => r.resolve(response()));
  await flush();
  await h.timer.advance(60000);
  assert.equal(h.requests.length, 4);
  assert.ok(h.controller.states().every(s => s.status === 'paused'));
  h.controller.refresh();
  assert.equal(h.requests.length, 6);
  h.requests.slice(4).forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.timer.count(), 0);
  h.controller.dispose();
});

test('all eight peers have separate receipts and rename/reorder do not invalidate active requests', async () => {
  const sources = Array.from({length:8}, (_, i) => ({key:'peer' + i, label:'Peer ' + i, url:'https://peer' + i + '.example'}));
  const h = harness({fleet:{enabled:true, sources}, paused:true});
  h.controller.start(true);
  assert.equal(h.requests.length, 9);
  const renamed = sources.map(source => ({...source, label:'Renamed ' + source.key})).reverse();
  h.controller.configure({enabled:true, sources:renamed});
  assert.ok(h.requests.every(r => !r.init.signal.aborted));
  assert.deepEqual(h.controller.states().map(s => s.source.key), ['local', ...renamed.map(s => s.key)]);
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 9);
  assert.equal(new Set(h.samples.map(s => s.receipt.generation)).size, 9);
  assert.ok(h.samples.filter(s => s.source.key !== 'local').every(s => s.source.label.startsWith('Renamed')));
  assert.equal(h.timer.count(), 0);
  h.controller.dispose();
});

test('timeout and cancellation also suppress a late JSON body', async () => {
  const h = harness();
  let finishBody;
  h.controller.start();
  h.requests[1].resolve(response(null, {json:() => new Promise(resolve => { finishBody = resolve; })}));
  await flush();
  await h.timer.advance(15000);
  assert.equal(h.state('peer').status, 'unavailable');
  finishBody(tree());
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 2);
  await h.timer.advance(1000);
  assert.equal(h.requests.length, 3);
  h.controller.dispose();
});

test('rate change cancels requests and schedules once at the new interval', async () => {
  const h = harness();
  h.controller.start();
  h.controller.setInterval(2000);
  assert.ok(h.requests.every(r => r.init.signal.aborted));
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 0);
  await h.timer.advance(1999);
  assert.equal(h.requests.length, 2);
  await h.timer.advance(1);
  assert.equal(h.requests.length, 4);
  h.controller.dispose();
});

test('a paused one-shot timeout ends the attempt without a recurring retry', async () => {
  const h = harness({paused:true});
  h.controller.start(true);
  await h.timer.advance(15000);
  h.requests.forEach(r => r.reject(new Error('aborted')));
  await flush();
  await h.timer.advance(60000);
  assert.equal(h.requests.length, 2);
  assert.equal(h.timer.count(), 0);
  assert.ok(h.controller.states().every(s => s.status === 'paused' && /timeout/i.test(s.error)));
  h.controller.dispose();
});

test('disable preserves peer cache, remove/replacement cleans it, and local requests continue', async () => {
  const h = harness();
  h.controller.start();
  h.requests[1].resolve(response());
  await flush();
  h.controller.refresh();
  h.controller.configure({enabled:false, sources:[peer]});
  assert.equal(h.requests[0].init.signal.aborted, false);
  assert.equal(h.requests[2].init.signal.aborted, true);
  assert.equal(h.state('peer').status, 'paused');
  assert.notEqual(h.state('peer').lastGood, null);
  assert.deepEqual(h.removed, []);
  h.requests[2].resolve(response(tree(99)));
  await flush();
  assert.equal(h.samples.length, 1);
  h.controller.configure({enabled:true, sources:[peer]});
  assert.equal(h.requests.length, 4);
  h.controller.configure({enabled:true, sources:[other]});
  assert.equal(h.requests[3].init.signal.aborted, true);
  assert.deepEqual(h.removed, ['peer']);
  assert.equal(h.state('peer'), undefined);
  assert.equal(h.requests[4].url, other.url + '/data.json');
  h.requests[3].resolve(response(tree(99)));
  h.requests[0].resolve(response());
  await flush();
  assert.deepEqual(h.samples.map(s => s.source.key), ['peer','local']);
  h.controller.dispose();
});

test('configuration rejects rebinding atomically and remove/re-add waits for cancelled ownership', async () => {
  const h = harness();
  h.controller.start();
  assert.throws(() => h.controller.configure({enabled:true, sources:[{...peer, url:other.url}]}), /origin|bound|key/i);
  assert.equal(h.requests.length, 2);
  assert.equal(h.requests[1].init.signal.aborted, false);
  h.controller.configure({enabled:true, sources:[]});
  h.controller.configure(fleet);
  assert.equal(h.requests.length, 2);
  h.requests[1].resolve(response());
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 3);
  h.controller.dispose();
});

test('re-importing a settled removed key keeps sample generations monotonic', async () => {
  const h = harness();
  h.controller.start();
  h.requests[1].resolve(response());
  await flush();
  const firstGeneration = h.samples[0].receipt.generation;
  h.controller.configure({enabled:true, sources:[]});
  h.controller.configure(fleet);
  h.requests[2].resolve(response());
  await flush();
  assert.equal(h.samples.length, 2);
  assert.ok(h.samples[1].receipt.generation > firstGeneration);
  h.controller.dispose();
});

test('stop is restartable and dispose permanently suppresses late completion', async () => {
  const h = harness();
  h.controller.start();
  h.controller.stop();
  assert.ok(h.requests.every(r => r.init.signal.aborted));
  h.controller.start();
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.requests.length, 4);
  assert.equal(h.samples.length, 0);
  h.controller.dispose();
  h.requests.slice(2).forEach(r => r.resolve(response()));
  await flush();
  h.controller.start();
  h.controller.refresh();
  assert.equal(h.requests.length, 4);
  assert.equal(h.samples.length, 0);
  assert.equal(h.timer.count(), 0);
});

test('paused startup survives rate changes without creating recurring work', async () => {
  const h = harness({paused:true});
  h.controller.start(true);
  h.controller.setInterval(5000);
  h.requests.forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 0);
  assert.equal(h.requests.length, 4);
  h.requests.slice(2).forEach(r => r.resolve(response()));
  await flush();
  assert.equal(h.samples.length, 2);
  assert.equal(h.timer.count(), 0);
  h.controller.dispose();
});

test('stale receipt age is published even when reconfiguration postpones the next timeout', async () => {
  const h = harness({fleet:{enabled:false, sources:[]}});
  h.controller.start();
  h.requests[0].resolve(response());
  await flush();
  await h.timer.advance(15500);
  h.controller.setInterval(1000);
  h.requests[1].reject(new Error('aborted'));
  await flush();
  await h.timer.advance(500);
  assert.equal(h.state('local').status, 'stale');
  assert.equal(h.changes.at(-1)[0].status, 'stale');
  h.controller.dispose();
});

test('real HTTP transport exercises JSON admission and publishes both sources without touching live routes', {timeout:5000}, async () => {
  const http = require('node:http');
  const seen = [];
  const server = http.createServer((request, reply) => {
    seen.push({method:request.method, path:request.url});
    reply.writeHead(200, {'content-type':'application/json'});
    reply.end(JSON.stringify(tree(request.url.startsWith('/peer/') ? 99 : 40)));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let controller;
  try {
    const base = 'http://127.0.0.1:' + server.address().port;
    const samples = [], dispatch = [];
    let complete;
    const ready = new Promise(resolve => { complete = resolve; });
    controller = P.create({fleet, paused:true, pageOrigin:'https://dashboard.example',
      fetch:(url, init) => {
        dispatch.push({url, init});
        return fetch(base + (url === 'data.json' ? '/local/data.json' : '/peer/data.json'), init);
      },
      onSample:(source, data) => { samples.push({source, data}); if (samples.length === 2) complete(); }
    });
    controller.start(true);
    await ready;
    assert.deepEqual(samples.map(s => s.source.key).sort(), ['local','peer']);
    assert.deepEqual(seen.map(s => s.path).sort(), ['/local/data.json','/peer/data.json']);
    assert.ok(seen.every(s => s.method === 'GET'));
    assert.deepEqual(dispatch.map(s => s.url).sort(), ['data.json',peer.url + '/data.json']);
    assert.equal(samples.find(s => s.source.key === 'peer').data.Children[0].Children[0].Children[0].RawValue, 99);
  } finally {
    if (controller) controller.dispose();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
