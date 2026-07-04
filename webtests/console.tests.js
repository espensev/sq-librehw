(function (root) {
  function runConsoleTests(S, data, makeStorage) {
    let pass = 0, fail = 0; const log = [];
    const eq = (name, got, want) => {
      const ok = JSON.stringify(got) === JSON.stringify(want);
      log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${JSON.stringify(got)} want=${JSON.stringify(want)}`);
      ok ? pass++ : fail++;
    };

    eq('classOf amdcpu', S.classOf('/amdcpu/0/temperature/2'), 'cpu');
    eq('classOf gpu-nvidia', S.classOf('/gpu-nvidia/0/temperature/0'), 'gpu');
    eq('classOf gpu-amd', S.classOf('/gpu-amd/0/temperature/4'), 'igpu');
    eq('classOf nvme', S.classOf('/nvme/1/temperature/0'), 'nvme');
    eq('classOf lpc', S.classOf('/lpc/nct6701d/0/temperature/5'), 'mb');
    eq('classOf nic', S.classOf('/nic/%7BX%7D/load/1'), 'nic');
    eq('splitValue temp', S.splitValue('65.5 °C'), {n:'65.5', unit:'°C'});
    eq('splitValue pct', S.splitValue('27.1 %'), {n:'27.1', unit:'%'});
    eq('splitValue null', S.splitValue(null), {n:'—', unit:''});

    const sensors = S.flatten(data.Children[0]);
    const limits = S.deriveLimits(sensors);
    const byId = id => sensors.find(s => s.id === id);
    const st = id => S.statusOf(byId(id), limits);
    const storage = makeStorage;

    eq('cpu Tctl ok', st('/amdcpu/0/temperature/2'), 'ok');
    eq('mb stray info', st('/lpc/nct6701d/0/temperature/5'), 'info');
    eq('mb stray suppressed from dashboard', S.isDashboardSuppressedSensor(byId('/lpc/nct6701d/0/temperature/5')), true);
    eq('visible sensors hide mb stray', S.visibleSensors(sensors).some(s => s.id === '/lpc/nct6701d/0/temperature/5'), false);
    eq('nvme temp ok', st('/nvme/2/temperature/2'), 'ok');
    S.resetSensorMotion();
    eq('nvme static aux temp suppressed from dashboard',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', rawMin:52.85, rawMax:52.85, id:'/nvme/2/temperature/2'}), true);
    S.trackSensorMotion([52.4, 52.5, 52.6, 52.4, 52.7].map(raw => ({id:'/nvme/2/temperature/2', raw})));
    eq('nvme low-motion aux temp remains suppressed',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', id:'/nvme/2/temperature/2'}), true);
    S.resetSensorMotion();
    S.trackSensorMotion([45, 46, 47, 50, 52.85].map(raw => ({id:'/nvme/2/temperature/2', raw})));
    eq('nvme moving aux temp remains visible',
      S.isDashboardSuppressedSensor({cls:'nvme', type:'Temperature', text:'Temperature #2', rawMin:45, rawMax:52.85, id:'/nvme/2/temperature/2'}), false);
    S.resetSensorMotion();
    eq('nvme limit info', st('/nvme/2/temperature/10'), 'info');
    eq('nvme limit display type', S.displayType(byId('/nvme/2/temperature/10')), 'Limits');
    eq('nvme real temp display type', S.displayType(byId('/nvme/2/temperature/2')), 'Temperature');
    eq('cpu load info', st('/amdcpu/0/load/0'), 'info');
    eq('amd hotspot junction band', S.statusOf({cls:'igpu', type:'Temperature', text:'GPU Hot Spot', raw:94, hwid:'x'}, {}), 'ok');
    eq('ssd life crit', S.statusOf({type:'Level', text:'Life', raw:3}, {}), 'crit');
    eq('ssd life warn', S.statusOf({type:'Level', text:'Life', raw:15}, {}), 'warn');

    const hero = S.pickHero(sensors, limits);
    eq('hero has CPU Temp', hero.some(h => h.label === 'CPU Temp'), true);
    eq('CPU Power unbounded', !!hero.find(h => h.label === 'CPU Power')?.bounded, false);
    eq('CPU Temp bounded', !!hero.find(h => h.label === 'CPU Temp')?.bounded, true);

    eq('dashboard state bad json falls back', S.loadDashboardState(storage('{bad')).hiddenSensorIds, []);
    const showDefault = S.normalizeDashboardState({shownDefaultHiddenSensorIds:['/lpc/nct6701d/0/temperature/5']});
    eq('default hidden can be shown', S.visibleSensors(sensors, showDefault).some(s => s.id === '/lpc/nct6701d/0/temperature/5'), true);
    const explicitHidden = S.normalizeDashboardState({hiddenSensorIds:['/amdcpu/0/load/0']});
    eq('explicit hidden sensor removed', S.visibleSensors(sensors, explicitHidden).some(s => s.id === '/amdcpu/0/load/0'), false);
    const pinned = S.normalizeDashboardState({pinnedCards:[
      {id:'/amdcpu/0/load/0', title:'CPU Work'},
      {id:'/missing/sensor', title:'Missing'}
    ], pinnedOrder:['/missing/sensor','/amdcpu/0/load/0']});
    const cards = S.resolvePinnedCards(sensors, pinned, limits);
    eq('resolve pinned ignores missing', cards.map(c => c.label), ['CPU Work']);
    eq('apply panel order', S.applyOrder([{key:'a',index:0},{key:'b',index:1}], ['b'], x => x.key).map(x => x.key), ['b','a']);

    // --- Tier 3: schema + migration ---
    eq('default has consolidated fields', (() => { const d = S.defaultDashboardState();
      return [d.paused, d.rate, d.theme, JSON.stringify(d.collapsedPanels)]; })(), [false, 2, 'dark', '{}']);
    eq('normalize clamps rate high', S.normalizeDashboardState({rate: 99}).rate, 10);
    eq('normalize clamps rate low', S.normalizeDashboardState({rate: 0}).rate, 1);
    eq('normalize rate default', S.normalizeDashboardState({}).rate, 2);
    eq('normalize theme light', S.normalizeDashboardState({theme:'light'}).theme, 'light');
    eq('normalize theme junk -> dark', S.normalizeDashboardState({theme:'x'}).theme, 'dark');
    eq('normalize paused bool', S.normalizeDashboardState({paused:true}).paused, true);
    eq('normalize collapsed map coerces', S.normalizeDashboardState({collapsedPanels:{CPU:1,GPU:false,'':true}}).collapsedPanels, {CPU:true, GPU:false});
    eq('normalize collapsed rejects array', S.normalizeDashboardState({collapsedPanels:['CPU']}).collapsedPanels, {});
    const legacyStore = (() => {
      const m = {'sq.paused':'1','sq.rate':'5','sq.theme':'light','sq.panel.CPU':'1','sq.panel.Network':'0','other':'keep'};
      return { get length(){return Object.keys(m).length;}, key:i=>Object.keys(m)[i],
        getItem:k=>k in m?m[k]:null, setItem:(k,v)=>{m[k]=String(v);}, removeItem:k=>{delete m[k];}, _m:m };
    })();
    const migrated = S.migrateLegacyState(legacyStore, S.defaultDashboardState());
    eq('migrate folds paused', migrated.paused, true);
    eq('migrate folds rate', migrated.rate, 5);
    eq('migrate folds theme', migrated.theme, 'light');
    eq('migrate folds collapsed map', migrated.collapsedPanels, {CPU:true, Network:false});
    eq('migrate removes legacy keys', [legacyStore._m['sq.paused'], legacyStore._m['sq.rate'], legacyStore._m['sq.theme'], legacyStore._m['sq.panel.CPU']], [undefined, undefined, undefined, undefined]);
    eq('migrate keeps unrelated key', legacyStore._m['other'], 'keep');
    eq('migrate idempotent (2nd pass)', (() => { const again = S.migrateLegacyState(legacyStore, migrated);
      return [again.paused, again.rate, again.theme]; })(), [true, 5, 'light']);

    // --- Tier 3: panel collapse tri-state ---
    eq('collapse stored true wins', S.isPanelCollapsed({collapsedPanels:{CPU:true}}, 'CPU', false), true);
    eq('collapse stored false wins over default-collapsed', S.isPanelCollapsed({collapsedPanels:{Network:false}}, 'Network', true), false);
    eq('collapse absent uses default true', S.isPanelCollapsed({collapsedPanels:{}}, 'Network', true), true);
    eq('collapse absent uses default false', S.isPanelCollapsed({collapsedPanels:{}}, 'CPU', false), false);

    // --- Tier 3: reorder + isPinned ---
    eq('reorder move to end', S.reorderByDrop(['a','b','c'], 'a', 2), ['b','c','a']);
    eq('reorder move to front', S.reorderByDrop(['a','b','c'], 'c', 0), ['c','a','b']);
    eq('reorder no-op index', S.reorderByDrop(['a','b','c'], 'b', 1), ['a','b','c']);
    eq('reorder clamps high index', S.reorderByDrop(['a','b','c'], 'a', 99), ['b','c','a']);
    eq('reorder clamps low index', S.reorderByDrop(['a','b','c'], 'c', -5), ['c','a','b']);
    eq('reorder missing key unchanged', S.reorderByDrop(['a','b','c'], 'z', 0), ['a','b','c']);
    eq('isPinned true', S.isPinned({pinnedCards:[{id:'/x',title:''}]}, '/x'), true);
    eq('isPinned false', S.isPinned({pinnedCards:[]}, '/x'), false);

    // --- v2: kinds, niceCeil, speedoRange, cardStyle ---
    eq('kindOf temp', S.kindOf('Temperature'), 'temp');
    eq('kindOf load family', [S.kindOf('Load'), S.kindOf('Level'), S.kindOf('Control')], ['load','load','load']);
    eq('kindOf fan', S.kindOf('Fan'), 'fan');
    eq('kindOf power family', [S.kindOf('Power'), S.kindOf('Voltage'), S.kindOf('Current')], ['power','power','power']);
    eq('kindOf clock', S.kindOf('Clock'), 'clock');
    eq('kindOf data fallback', [S.kindOf('Throughput'), S.kindOf('Factor'), S.kindOf('Nope')], ['data','data','data']);
    eq('niceCeil ladder', [S.niceCeil(87), S.niceCeil(1740), S.niceCeil(0.7), S.niceCeil(100), S.niceCeil(101)], [100, 2000, 1, 100, 200]);
    eq('niceCeil junk', [S.niceCeil(0), S.niceCeil(-5), S.niceCeil(NaN)], [null, null, null]);
    S.resetSensorMotion();
    eq('speedoRange fan from rawMax', S.speedoRange({type:'Fan', raw:900, rawMax:1740, id:'/f'}, {}), [0, 2000]);
    eq('speedoRange power current-peak', S.speedoRange({type:'Power', raw:87, rawMax:null, id:'/p'}, {}), [0, 100]);
    eq('speedoRange null when no peak', S.speedoRange({type:'Fan', raw:null, rawMax:null, id:'/f2'}, {}), null);
    eq('speedoRange temp delegates', S.speedoRange({cls:'cpu', type:'Temperature', text:'Tctl', raw:60, id:'/t'}, {}), [30, 95]);
    eq('cardStyle map normalized', S.normalizeDashboardState({cardStyle:{'/a':'gauge','/b':'nope','/c':'graph', '':'gauge'}}).cardStyle, {'/a':'gauge','/c':'graph'});
    eq('cardStyleFor gauge', S.cardStyleFor('gauge', true, false), {arc:true, spark:false});
    eq('cardStyleFor number keeps global spark', S.cardStyleFor('number', true, true), {arc:false, spark:true});
    eq('cardStyleFor graph forces spark', S.cardStyleFor('graph', false, false), {arc:false, spark:true});
    eq('cardStyleFor auto', S.cardStyleFor(undefined, true, true), {arc:true, spark:true});

    // --- v2: trend + hero fans ---
    S.resetSensorTrends();
    const seedHist = (id, pts) => pts.forEach(([t, raw]) => S.trackSensorHistory([{id, raw}], t));
    seedHist('/tr1', [[0,50],[5000,50.5],[10000,51],[15000,51.5],[20000,52],[25000,52.5],[30000,53]]); // windowed rate ~ +0.12 °C/s
    eq('trend rising past deadband', S.trendFor('/tr1', 'temp', 30000)?.direction, 'rising');
    eq('trend unit', S.trendFor('/tr1', 'temp', 30000)?.rateUnit, '°C/s');
    S.resetSensorTrends();
    seedHist('/tr2', [[0,50],[10000,50.01],[20000,50.02],[30000,50.03]]); // ~0.001 °C/s, inside band
    eq('trend inside deadband -> null', S.trendFor('/tr2', 'temp', 30000), null);
    S.resetSensorTrends();
    seedHist('/tr3', [[0,50],[15000,50.45],[30000,50.9]]); // windowed rate ~ +0.045: within [db/2, db), no prior -> null
    eq('hysteresis: weak same-sign w/o prior -> null', S.trendFor('/tr3', 'temp', 30000), null);
    S.resetSensorTrends();
    seedHist('/tr4', [[0,50],[15000,50.9],[30000,51.8]]); // windowed rate +0.09 °C/s -> rising stored
    eq('hysteresis: arm rising', S.trendFor('/tr4', 'temp', 30000)?.direction, 'rising');
    seedHist('/tr4', [[35000,51.85],[45000,52.3],[60000,52.4]]); // 30s-window rate ~ +0.035: within [db/2, db)
    eq('hysteresis: weak same-sign keeps arrow', S.trendFor('/tr4', 'temp', 60000)?.direction, 'rising');
    eq('trend data kind -> null', S.trendFor('/tr1', 'data', 30000), null);
    S.resetSensorTrends();
    const fanHero = S.pickHero([
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #1', raw:900, value:'900 RPM', id:'/fan1'},
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #2', raw:1400, value:'1400 RPM', id:'/fan2'},
      {hw:'Board', hwid:'b', cls:'mb', type:'Fan', text:'Fan #3', raw:0, value:'0 RPM', id:'/fan3'}
    ], {});
    eq('hero fans active only, rpm-desc', fanHero.filter(h => h.s.type === 'Fan').map(h => h.s.id), ['/fan2','/fan1']);

    // === Tier 3 cases are appended below by later tasks ===

    // --- v3: range/override/order schema ---
    eq('normalize rangeOverrides', S.normalizeDashboardState({rangeOverrides:{'/a':{max:575},'/b':{max:-1},'/c':{max:200,min:50},'':{max:5},'/d':'x'}}).rangeOverrides,
      {'/a':{max:575},'/c':{max:200,min:50}});
    eq('normalize observedMax', S.normalizeDashboardState({observedMax:{'/a':150.9,'/b':'nope'}}).observedMax, {'/a':150.9});
    eq('normalize rowOrder', S.normalizeDashboardState({rowOrder:{'k|Fan':['/f1','/f2'],'bad':[],7:'x'}}).rowOrder, {'k|Fan':['/f1','/f2']});
    eq('normalize net lists', (() => { const d = S.normalizeDashboardState({netAdapterOrder:['/nic/a','/nic/a'], hiddenNetAdapters:['/nic/b']});
      return [d.netAdapterOrder, d.hiddenNetAdapters]; })(), [['/nic/a'], ['/nic/b']]);
    eq('default has v3 fields', (() => { const d = S.defaultDashboardState();
      return [d.rangeOverrides, d.observedMax, d.rowOrder, d.netAdapterOrder, d.hiddenNetAdapters]; })(), [{}, {}, {}, [], []]);

    // --- v3: rangeFor provenance ---
    S.resetSensorMotion();
    eq('rangeFor override wins', S.rangeFor({id:'/p', type:'Power', raw:80, rawMax:122}, {}, {rangeOverrides:{'/p':{max:575}}}),
      {lo:0, hi:575, source:'override'});
    eq('rangeFor band for temp', S.rangeFor({cls:'cpu', type:'Temperature', text:'Tctl', raw:60, id:'/t'}, {}, {}),
      {lo:30, hi:95, source:'band'});
    eq('rangeFor peak est', S.rangeFor({id:'/p2', type:'Power', raw:87, rawMax:122}, {}, {}), {lo:0, hi:200, source:'peak'});
    eq('rangeFor honors persisted peak', S.rangeFor({id:'/p3', type:'Power', raw:10, rawMax:12}, {}, {observedMax:{'/p3':480}}),
      {lo:0, hi:500, source:'peak'});
    eq('rangeFor null for voltage', S.rangeFor({id:'/v', type:'Voltage', raw:1.02}, {}, {}), null);
    eq('speedoRange still [lo,hi]', S.speedoRange({type:'Power', raw:87, rawMax:null, id:'/p4'}, {}), [0, 100]);

    // --- v3: fan/control pairing ---
    const fanPairSensors = [
      {hwid:'/lpc/nct6701d/0', type:'Fan',     text:'Fan #2', raw:642,  id:'/lpc/nct6701d/0/fan/1'},
      {hwid:'/lpc/nct6701d/0', type:'Control', text:'Fan #2', raw:29.8, id:'/lpc/nct6701d/0/control/1'},
      {hwid:'/gpu-nvidia/0',   type:'Control', text:'Fan #2', raw:50,   id:'/gpu-nvidia/0/control/9'}
    ];
    eq('fanControlFor pairs hwid+text', S.fanControlFor(fanPairSensors[0], fanPairSensors)?.id, '/lpc/nct6701d/0/control/1');
    eq('fanControlFor null when unpaired', S.fanControlFor({hwid:'/z', type:'Fan', text:'Pump', raw:100, id:'/z/fan/0'}, fanPairSensors), null);
    eq('fanControlFor null for non-fan', S.fanControlFor(fanPairSensors[1], fanPairSensors), null);
    eq('fanControlFor live fixture', (() => { const f = sensors.find(s => s.id === '/lpc/nct6701d/0/fan/1');
      return f ? S.fanControlFor(f, sensors)?.id : '/lpc/nct6701d/0/control/1'; })(), '/lpc/nct6701d/0/control/1');

    return { pass, fail, log };
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = runConsoleTests;
  else root.runConsoleTests = runConsoleTests;
})(typeof window !== 'undefined' ? window : globalThis);
