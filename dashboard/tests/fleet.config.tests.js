const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const vm = require('node:vm');
const modelPath = path.join(__dirname, '../web/fleet.js');
const F = require(modelPath);
const source = (key = 'peer', url = 'https://peer.example', label = 'Peer') => ({key, label, url});
const fleet = sources => ({enabled:true, sources:sources || [source()]});
const document = sources => JSON.stringify({schema:'sq.fleet', version:1, sources});
const options = {pageOrigin:'https://dashboard.example'};

test('fleet defaults and malformed persisted state fail closed without throwing', () => {
  assert.deepEqual(F.normalize(undefined, options), {fleet:{enabled:false, sources:[]}, origins:{}, error:null});
  for (const invalid of [null, [], {}, {enabled:1,sources:[]}, {enabled:true,sources:'bad'},
    {enabled:true,sources:[source('local')]}, {...fleet(), layout:'panes'}]) {
    const result = F.normalize(invalid, options);
    assert.deepEqual(result.fleet, {enabled:false,sources:[]});
    assert.equal(typeof result.error, 'string');
  }
  const state = F.normalize(fleet(), options);
  assert.equal(state.error, null);
  assert.deepEqual(state.origins, {peer:'https://peer.example'});
});

test('source validation canonicalizes origins and enforces key, label and count boundaries', () => {
  assert.deepEqual(F.validateSources([source('a'.repeat(32), 'https://PEER.example:443/', ' Peer ')], options),
    [source('a'.repeat(32), 'https://peer.example', 'Peer')]);
  assert.equal(F.validateSources(Array.from({length:8}, (_, i) => source('p'+i, 'https://p'+i+'.example'))).length, 8);
  for (const key of ['', 'local', 'A', '_peer', 'a'.repeat(33), 'a|b', 'é'])
    assert.throws(() => F.validateSources([source(key)]));
  for (const label of ['', ' ', 'a'.repeat(81), 123, null])
    assert.throws(() => F.validateSources([source('peer', 'https://peer.example', label)]));
  assert.equal(F.validateSources([source('peer', 'https://peer.example', 'a'.repeat(80))])[0].label.length, 80);
  assert.throws(() => F.validateSources(Array.from({length:9}, (_, i) => source('p'+i, 'https://p'+i+'.example'))));
  assert.throws(() => F.validateSources([{...source(), extra:true}]));
  assert.throws(() => F.validateSources([{key:'peer',url:'https://peer.example'}]));
});

test('only HTTPS origins are accepted, including lexical paths the URL parser would erase', () => {
  for (const url of ['http://peer.example', '//peer.example', 'https://peer.example/path',
    'https://peer.example/a/..', 'https://peer.example/?', 'https://peer.example/#',
    'https://name:secret@peer.example', 'https://@peer.example', 'https://peer.example\\path',
    ' https://peer.example', 'https://peer.example\n', 'https://peer.example:99999', 'https://'+ 'a'.repeat(2041)])
    assert.throws(() => F.validateSources([source('peer', url)], options), url);
  assert.throws(() => F.validateSources([source('peer', 'https://DASHBOARD.example:443/')], options));
  assert.deepEqual(F.validateSources([source('ipv6', 'https://[::1]:9443/')])[0].url, 'https://[::1]:9443');
  assert.throws(() => F.validateSources([source('one'), source('two', 'https://PEER.example:443/')]));
  assert.throws(() => F.validateSources([source('peer'), source('peer', 'https://two.example')]));
});

test('source import is strict, atomic, byte bounded, and preserves the enabled flag', () => {
  const current = {...fleet(), enabled:false};
  const before = JSON.stringify(current);
  const imported = F.importSources(document([source('new', 'https://new.example')]), current, options);
  assert.deepEqual(imported, {enabled:false,sources:[source('new','https://new.example')]});
  assert.equal(JSON.stringify(current), before);
  for (const bad of ['{', 'null', '[]', JSON.stringify({schema:'wrong',version:1,sources:[]}),
    JSON.stringify({schema:'sq.fleet',version:2,sources:[]}),
    JSON.stringify({schema:'sq.fleet',version:1,sources:[],enabled:true}),
    document([source('new'), source('bad','http://bad.example')])]) {
    assert.throws(() => F.importSources(bad, current, options));
    assert.equal(JSON.stringify(current), before);
  }
  const base = document([]);
  assert.deepEqual(F.importSources(base+' '.repeat(32768-Buffer.byteLength(base)), current), {enabled:false,sources:[]});
  assert.throws(() => F.importSources(base+' '.repeat(32769-Buffer.byteLength(base)), current));
  // Whitespace is valid JSON but UTF-8 bytes, not JS string length, own the file bound.
  const multibyte = document([source('peer','https://peer.example','😀'.repeat(40))]);
  const padded = multibyte + ' '.repeat(32768-Buffer.byteLength(multibyte));
  assert.equal(F.importSources(padded, current).sources[0].label.length, 80);
  assert.throws(() => F.importSources(padded+' ', current));
});

test('export contains only the source-list document and round trips keys and canonical origins', () => {
  const state = fleet([source('stable', 'https://PEER.example:443/')]);
  const json = F.exportSources(state);
  assert.deepEqual(JSON.parse(json), {schema:'sq.fleet',version:1,sources:[source('stable')]});
  assert.deepEqual(F.importSources(json, fleet([])), fleet([source('stable')]));
});

test('rename and reorder preserve keys; endpoint replacement and addition allocate fresh keys', () => {
  const initial = fleet([source('one'), source('two','https://two.example')]);
  const original = JSON.stringify(initial);
  const renamed = F.renameSource(initial, 'one', ' Renamed ');
  assert.deepEqual(renamed.sources[0], source('one', 'https://peer.example', 'Renamed'));
  assert.deepEqual(F.moveSource(renamed, 'one', 1).sources.map(s => s.key), ['two','one']);
  const replacement = F.replaceSource(renamed, 'one', 'https://replacement.example', {keyFactory:() => 'replacement'});
  assert.deepEqual(replacement.sources[0], source('replacement','https://replacement.example','Renamed'));
  const added = F.addSource(initial, {label:'Added',url:'https://added.example'}, {keyFactory:() => 'added'});
  assert.deepEqual(added.sources[2], source('added','https://added.example','Added'));
  assert.deepEqual(F.removeSource(initial,'one').sources, [source('two','https://two.example')]);
  assert.throws(() => F.removeSource(initial,'local'));
  assert.throws(() => F.renameSource(initial,'missing','New'));
  assert.throws(() => F.moveSource(initial,'one',2));
  assert.throws(() => F.addSource(initial,{label:'Added',url:'https://added.example'},{keyFactory:() => 'one'}));
  assert.throws(() => F.addSource(initial,{label:'Added',url:'https://added.example'},
    {keyFactory:() => 'absent',referenceIds:['fleet:absent|/cpu/temp']}));
  assert.equal(JSON.stringify(initial), original);
});

test('active and removed referenced keys never rebind, including unknown imported references', () => {
  const initial = fleet();
  assert.throws(() => F.importSources(document([source('peer','https://different.example')]), initial));
  const origins = F.retainOrigins({peer:'https://peer.example',gone:'https://gone.example'}, [],
    ['fleet:peer|/cpu/temp', 'fleet:unknown|/cpu/temp', '/cpu/temp']);
  assert.deepEqual(origins, {peer:'https://peer.example',unknown:null});
  assert.throws(() => F.importSources(document([source('peer','https://different.example')]), fleet([]), {knownOrigins:origins}));
  assert.throws(() => F.importSources(document([source('unknown','https://new.example')]), fleet([]), {knownOrigins:origins}));
  assert.throws(() => F.importSources(document([source('unknown','https://new.example')]), fleet([]),
    {referenceIds:['fleet:unknown|/cpu/temp']}));
  assert.deepEqual(F.importSources(document([source()]),fleet([]),{knownOrigins:origins}), initial);
  assert.deepEqual(F.retainOrigins(origins,[source()],[]), {peer:'https://peer.example'});
  assert.deepEqual(F.normalize(fleet([]), {knownOrigins:origins}).origins, origins);
  assert.throws(() => F.validateSources([source('peer')],{knownOrigins:{peer:'http://bad.example'}}));
  assert.equal(typeof F.normalize(initial,{knownOrigins:{peer:'http://bad.example'}}).error,'string');
});

test('origin registry treats prototype-shaped keys as data and rejects unbounded retention', () => {
  const sources = [source('constructor','https://constructor.example'),source('toString','https://other.example')];
  assert.throws(() => F.validateSources(sources), 'uppercase remains invalid');
  const valid = F.normalize(fleet([sources[0]]));
  assert.equal(valid.origins.constructor, 'https://constructor.example');
  const oversized = Object.fromEntries(Array.from({length:F.LIMITS.retainedOrigins+1}, (_, i) => ['p'+i,'https://p'+i+'.example']));
  assert.throws(() => F.retainOrigins(oversized, [], undefined));
});

test('identity envelopes preserve raw IDs and enforce the separate Workspace raw limit', () => {
  const raw = '/cpu/'+'x'.repeat(187), key = 'a'.repeat(32);
  assert.equal(raw.length,192);
  assert.equal(F.persistenceKey('local',raw),raw);
  const peer = F.persistenceKey(key,raw);
  assert.equal(peer.length,231);
  assert.deepEqual(F.parseIdentity(peer), {sourceKey:key,rawId:raw});
  assert.deepEqual(F.parseIdentity('/cpu/0'), {sourceKey:'local',rawId:'/cpu/0'});
  assert.deepEqual(F.parseIdentity('fleet:peer|/raw|with|bars'), {sourceKey:'peer',rawId:'/raw|with|bars'});
  assert.equal(F.isWorkspaceId(raw),true);
  assert.equal(F.isWorkspaceId(peer),true);
  assert.equal(F.isWorkspaceId(raw+'x'),false);
  assert.equal(F.isWorkspaceId(peer+'x'),false);
  for (const invalid of ['',null,'fleet:local|/raw','fleet:UPPER|/raw','fleet:peer|','fleet:peer']) {
    assert.equal(F.parseIdentity(invalid),null);
    assert.equal(F.isWorkspaceId(invalid),false);
  }
  assert.throws(() => F.persistenceKey('bad|key','/cpu/0'));
  assert.throws(() => F.persistenceKey('peer',''));
});

test('browser UMD uses the page origin and runs the edit/export/import lifecycle without Node globals', () => {
  const window = {location:{origin:'https://dashboard.example'},crypto:{getRandomValues:xs => xs.fill(7)}};
  const context = vm.createContext({window,URL,Uint8Array});
  vm.runInContext(fs.readFileSync(path.join(path.dirname(modelPath),'identity.js'),'utf8'), context);
  vm.runInContext(fs.readFileSync(modelPath,'utf8'), context);
  const B = window.SQFleet;
  assert.throws(() => B.addSource(B.defaults(),{label:'Self',url:'https://dashboard.example'}));
  const added = B.addSource(B.defaults(),{label:'Peer',url:'https://PEER.example:443/'});
  const renamed = B.renameSource(added,added.sources[0].key,'Browser peer');
  const restored = B.importSources(B.exportSources(renamed),B.defaults());
  assert.equal(restored.sources[0].label,'Browser peer');
  assert.equal(restored.sources[0].key,added.sources[0].key);
  assert.equal(restored.sources[0].url,'https://peer.example');
  assert.equal(restored.enabled,false);
});
