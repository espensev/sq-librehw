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
    S.resetSensorMotion();
    for (let i = 0; i < 6; i++) S.trackSensorMotion([
      { id: '/lpc/test/0/temperature/a', raw: [30,35,32,38,31,30][i] },
      { id: '/lpc/test/0/temperature/b', raw: 50 }
    ]);
    eq('static mb temp suppressed', S.isStaticMbTemp({ cls: 'mb', type: 'Temperature', id: '/lpc/test/0/temperature/b' }), true);
    eq('moving mb temp visible', S.isStaticMbTemp({ cls: 'mb', type: 'Temperature', id: '/lpc/test/0/temperature/a' }), false);
    eq('non-mb temp unaffected', S.isStaticMbTemp({ cls: 'cpu', type: 'Temperature', id: '/x' }), false);
    S.resetSensorMotion();
    const explicitHidden = S.normalizeDashboardState({hiddenSensorIds:['/amdcpu/0/load/0']});
    eq('explicit hidden sensor removed', S.visibleSensors(sensors, explicitHidden).some(s => s.id === '/amdcpu/0/load/0'), false);

    // --- Sensors popover model helpers (Phase B / Slice 4B) ---
    S.resetSensorMotion();
    const popState = S.normalizeDashboardState({
      hiddenSensorIds: ['/amdcpu/0/load/0'],
      sensorAliases: { '/amdcpu/0/temperature/2': 'Cpu Core Alias' }
    });
    const sst = S.sensorSearchText(byId('/amdcpu/0/temperature/2'), popState);
    eq('sensorSearchText includes id', sst.includes('/amdcpu/0/temperature/2'), true);
    eq('sensorSearchText includes alias (lowercased)', sst.includes('cpu core alias'), true);
    eq('sensorSearchText is lowercased', sst === sst.toLowerCase(), true);
    eq('sensorVisibility hidden', S.sensorVisibility(byId('/amdcpu/0/load/0'), popState), 'hidden');
    eq('sensorVisibility visible', S.sensorVisibility(byId('/amdcpu/0/temperature/2'), popState), 'visible');
    eq('sensorVisibility offscreen (static mb temp)', S.sensorVisibility(byId('/lpc/nct6701d/0/temperature/5'), popState), 'offscreen');
    eq('hiddenSensorCount counts hidden+offscreen', S.hiddenSensorCount(sensors, popState) >= 2, true);
    eq('hiddenSensorCount ignores plainly-visible-only list', S.hiddenSensorCount([byId('/amdcpu/0/temperature/2')], popState), 0);
    const popRows = S.sensorPopoverRows(sensors, popState, '');
    eq('sensorPopoverRows returns rows', popRows.length > 0, true);
    eq('sensorPopoverRows row shape', Object.keys(popRows[0]).sort(),
      ['hw','id','label','rawLabel','type','value','visibility']);
    eq('sensorPopoverRows hidden sorted before visible',
      popRows.findIndex(r => r.visibility === 'hidden') < popRows.findIndex(r => r.visibility === 'visible'), true);
    const loadRows = S.sensorPopoverRows(sensors, popState, 'load');
    eq('sensorPopoverRows query returns a narrowed subset', loadRows.length > 0 && loadRows.length < popRows.length, true);
    eq('sensorPopoverRows query includes the matching load sensor', loadRows.some(r => r.id === '/amdcpu/0/load/0'), true);
    eq('sensorPopoverRows keeps raw label under alias',
      S.sensorPopoverRows([byId('/amdcpu/0/temperature/2')], popState, '')[0].rawLabel, byId('/amdcpu/0/temperature/2').text);
    eq('sensorPopoverRows non-array safe', S.sensorPopoverRows(null, popState, ''), []);
    S.resetSensorMotion();
    const pinned = S.normalizeDashboardState({pinnedCards:[
      {id:'/amdcpu/0/load/0', title:'CPU Work'},
      {id:'/missing/sensor', title:'Missing'}
    ], pinnedOrder:['/missing/sensor','/amdcpu/0/load/0']});
    const cards = S.resolvePinnedCards(sensors, pinned, limits);
    eq('resolve pinned ignores missing', cards.map(c => c.label), ['CPU Work']);
    eq('apply panel order', S.applyOrder([{key:'a',index:0},{key:'b',index:1}], ['b'], x => x.key).map(x => x.key), ['b','a']);

    // --- Explicit primary card selection (Slice 5A) ---
    eq('default primaryCardsCustomized false', S.defaultDashboardState().primaryCardsCustomized, false);
    eq('normalize primaryCardsCustomized true', S.normalizeDashboardState({primaryCardsCustomized:true}).primaryCardsCustomized, true);
    eq('normalize primaryCardsCustomized junk -> false', S.normalizeDashboardState({primaryCardsCustomized:1}).primaryCardsCustomized, false);
    const autoIds = S.pickHero(sensors, limits).map(h => h.s.id);
    const nonHeroId = sensors.map(s => s.id).find(id => !autoIds.includes(id));
    eq('primaryCardIds auto mode = hero ids', S.primaryCardIds(sensors, S.defaultDashboardState()), autoIds);
    eq('primaryCardIds non-array safe', S.primaryCardIds(null, S.defaultDashboardState()), []);
    eq('isPrimaryCard true for auto hero', S.isPrimaryCard(S.defaultDashboardState(), autoIds[0], sensors), true);
    eq('isPrimaryCard false for non-hero', S.isPrimaryCard(S.defaultDashboardState(), nonHeroId, sensors), false);
    const addState = S.setPrimaryCard(S.defaultDashboardState(), nonHeroId, true, sensors);
    eq('setPrimaryCard switches to custom', addState.primaryCardsCustomized, true);
    eq('setPrimaryCard seeds visible set + adds id',
      addState.primaryCards.includes(nonHeroId) && autoIds.every(id => addState.primaryCards.includes(id)), true);
    const remState = S.setPrimaryCard(addState, nonHeroId, false, sensors);
    eq('setPrimaryCard remove keeps custom', remState.primaryCardsCustomized, true);
    eq('setPrimaryCard remove drops id', remState.primaryCards.includes(nonHeroId), false);
    eq('setPrimaryCard no duplicate on re-add',
      S.setPrimaryCard(addState, nonHeroId, true, sensors).primaryCards.filter(x => x === nonHeroId).length, 1);
    eq('resetPrimaryCards returns to auto', S.primaryCardIds(sensors, S.resetPrimaryCards(addState)), autoIds);
    eq('resetPrimaryCards clears list', S.resetPrimaryCards(addState).primaryCards, []);
    const custPrim = S.normalizeDashboardState({primaryCardsCustomized:true, primaryCards:[autoIds[0], '/missing/x']});
    const primCards = S.resolvePrimaryCards(sensors, custPrim, limits);
    eq('resolvePrimaryCards keeps present sensor', primCards.some(c => c.s.id === autoIds[0]), true);
    eq('resolvePrimaryCards drops missing sensor from render', primCards.some(c => c.s.id === '/missing/x'), false);
    eq('missing primary id preserved in state', custPrim.primaryCards.includes('/missing/x'), true);
    // curated hero presentation survives promotion; genuine non-heroes fall back to raw text
    const heroSample = S.pickHero(sensors, limits).find(h => h.label === 'CPU Temp');
    const custHero = S.normalizeDashboardState({primaryCardsCustomized:true, primaryCards:[heroSample.s.id]});
    eq('resolvePrimaryCards preserves curated hero label', S.resolvePrimaryCards(sensors, custHero, limits)[0].label, 'CPU Temp');
    const custNon = S.normalizeDashboardState({primaryCardsCustomized:true, primaryCards:[nonHeroId]});
    const nonRow = S.resolvePrimaryCards(sensors, custNon, limits)[0];
    eq('resolvePrimaryCards non-hero uses raw text', nonRow.label, sensors.find(s => s.id === nonHeroId).text);
    eq('resolvePrimaryCards non-hero row shape', Object.keys(nonRow).sort(), ['bounded','label','s','status']);
    const primMerge = S.mergeTelemetryState(custPrim, S.defaultDashboardState());
    eq('telemetry preserves primary sentinel + list',
      [primMerge.primaryCardsCustomized, primMerge.primaryCards], [true, [autoIds[0], '/missing/x']]);

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

    // --- Slice 2: hardware identity (hwid grouping) ---
    const nvmeSensors = sensors.filter(s => s.cls === 'nvme');
    const nvmePanels = S.buildPanelItems(nvmeSensors);
    eq('three same-name NVMe produce three panels', nvmePanels.length, 3);
    eq('NVMe panel keys are hwids', nvmePanels.every(p => /^\/nvme\//.test(p.key)), true);
    eq('NVMe duplicate labels get #N suffix', nvmePanels.every(p => /#\d+$/.test(p.label)), true);
    const panelKeys = S.buildPanelItems(sensors).map(p => p.key);
    eq('no panel merges two hwids', new Set(panelKeys).size, panelKeys.length);
    eq('panelKey returns hwid not text', S.panelKey('Samename', [{hwid:'/x/0'}]), '/x/0');
    const hero2 = S.pickHero(sensors, limits);
    const gpuHeroSensors = hero2.filter(h => /^GPU/.test(h.label)).map(h => h.s);
    const gpuHeroHwids = [...new Set(gpuHeroSensors.map(s => s.hwid))];
    eq('hero covers all distinct GPU hwids', gpuHeroHwids.length >= 2, true);
    eq('collapse dual-read: hwid key wins', S.isPanelCollapsed({collapsedPanels:{'/nvme/0':true,'KINGSTON':false}}, '/nvme/0', 'KINGSTON', false), true);
    eq('collapse dual-read: text fallback applies', S.isPanelCollapsed({collapsedPanels:{'KINGSTON':true}}, '/nvme/0', 'KINGSTON', false), true);
    eq('collapse dual-read: absent uses default', S.isPanelCollapsed({collapsedPanels:{}}, '/nvme/0', 'KINGSTON', true), true);

    // --- Tier 3: reorder + isPinned ---
    eq('reorder move to end', S.reorderByDrop(['a','b','c'], 'a', 2), ['b','c','a']);
    eq('reorder move to front', S.reorderByDrop(['a','b','c'], 'c', 0), ['c','a','b']);
    eq('reorder no-op index', S.reorderByDrop(['a','b','c'], 'b', 1), ['a','b','c']);
    eq('reorder clamps high index', S.reorderByDrop(['a','b','c'], 'a', 99), ['b','c','a']);
    eq('reorder clamps low index', S.reorderByDrop(['a','b','c'], 'c', -5), ['c','a','b']);
    eq('reorder missing key unchanged', S.reorderByDrop(['a','b','c'], 'z', 0), ['a','b','c']);
    eq('isPinned true', S.isPinned({pinnedCards:[{id:'/x',title:''}]}, '/x'), true);
    eq('isPinned false', S.isPinned({pinnedCards:[]}, '/x'), false);

    // --- C1 T1: exposed order helpers (mergeOrder/moveKey no-op contract) ---
    eq('mergeOrder keeps saved-first then appends missing', S.mergeOrder(['b'], ['a','b','c']), ['b','a','c']);
    eq('mergeOrder drops unknown saved keys', S.mergeOrder(['zz','c'], ['a','b','c']), ['c','a','b']);
    eq('mergeOrder empty saved materializes keys', S.mergeOrder([], ['a','b']), ['a','b']);
    eq('moveKey swaps within bounds', S.moveKey(['a','b','c'], 'b', 1), ['a','c','b']);
    eq('moveKey OOB returns same reference (no-op guard)', (() => { const m = S.mergeOrder([], ['a','b']); return S.moveKey(m, 'a', -1) === m; })(), true);
    eq('moveKey bottom-down returns same reference', (() => { const m = ['a','b']; return S.moveKey(m, 'b', 1) === m; })(), true);
    eq('moveKey missing key returns same reference', (() => { const m = ['a','b']; return S.moveKey(m, 'zz', 1) === m; })(), true);

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
    eq('gauge rejects peak estimate', S.gaugeRangeFor({lo:0, hi:500, source:'peak'}, {id:'/p', type:'Power', raw:233}, null), null);
    eq('gauge allows explicit override', S.gaugeRangeFor({lo:0, hi:575, source:'override'}, {id:'/p', type:'Power', raw:233}, null),
      {lo:0, hi:575, source:'override'});
    eq('gauge allows paired fan control', S.gaugeRangeFor({lo:0, hi:2000, source:'peak'}, {id:'/f', type:'Fan', raw:900}, {raw:45}),
      {lo:0, hi:100, source:'control'});

    // --- Slice 1: range truth + machine-agnostic limit derivation ---
    const sPwr = {id:'/gpu-nvidia/0/power/0', type:'Power', raw:233};
    eq('rangeLabelFor override', S.rangeLabelFor({lo:0, hi:575, source:'override'}, sPwr), '575');
    eq('rangeLabelFor derived limit is approximate', S.rangeLabelFor({lo:0, hi:599, source:'limit', derived:true}, sPwr), '~575');
    eq('rangeLabelFor real limit', S.rangeLabelFor({lo:0, hi:575, source:'limit'}, sPwr), '575');
    eq('rangeLabelFor band', S.rangeLabelFor({lo:25, hi:95, source:'band'}, sPwr), '95');
    eq('rangeLabelFor peak -> null', S.rangeLabelFor({lo:0, hi:500, source:'peak'}, sPwr), null);
    eq('rangeLabelFor unknown -> null', S.rangeLabelFor({lo:0, hi:500, source:'weird'}, sPwr), null);
    eq('rangeLabelFor null sensor -> null', S.rangeLabelFor({lo:0, hi:500, source:'override'}, {id:'/x', type:'Power', raw:null}), null);
    eq('roundPowerBucket floors to 25W', [S.roundPowerBucket(599), S.roundPowerBucket(24), S.roundPowerBucket(50)], [575, 25, 50]);
    eq('median odd/even', [S.median([3,1,2]), S.median([4,1,2,3])], [2, 2.5]);
    eq('median empty -> null', S.median([]), null);
    // derived GPU limit: synthetic watt + percent sensors under one hwid
    const gpuWattPct = (watt, pct) => [
      {id:'/g/0/power/0', hwid:'/g/0', cls:'gpu', type:'Power', text:'GPU Package', raw:watt},
      {id:'/g/0/load/0',  hwid:'/g/0', cls:'gpu', type:'Load',   text:'GPU Power',  raw:pct}
    ];
    // accumulate enough non-idle samples to clear the min-sample gate
    let samples = {};
    for (let i = 0; i < S.POWER_LIMIT_MIN_SAMPLES; i++) samples = S.trackPowerSamples(gpuWattPct(150, 30), samples);
    eq('trackPowerSamples collected min count', samples['/g/0'].length >= S.POWER_LIMIT_MIN_SAMPLES, true);
    eq('derivedPowerLimit from watt/pct ratio (~500W, bucketed)', S.derivedPowerLimit('/g/0', {powerLimitSamples: samples}), 500);
    // idle percent (below floor) must not produce a sample
    const idleOnly = S.trackPowerSamples(gpuWattPct(20, 1), {});
    eq('trackPowerSamples drops idle pct', idleOnly['/g/0'], undefined);
    // too few samples -> no limit derived
    const tooFew = S.trackPowerSamples(gpuWattPct(150, 30), {});
    eq('derivedPowerLimit refuses too few samples', S.derivedPowerLimit('/g/0', {powerLimitSamples: tooFew}), null);
    // AMD-style iGPU: watt present but no power-percent sensor -> no samples, no limit
    const amdIgpu = [{id:'/a/0/power/0', hwid:'/a/0', cls:'igpu', type:'Power', text:'GPU Core', raw:45}];
    eq('trackPowerSamples no pct sensor -> no samples', S.trackPowerSamples(amdIgpu, {})['/a/0'], undefined);
    eq('derivedPowerLimit null without pct sensor', S.derivedPowerLimit('/a/0', {powerLimitSamples: {}}), null);
    // CPU power stays number-only: no derivation path, rangeFor gives peak, gauge rejects it
    const cpuPkg = sensors.find(s => s.type === 'Power' && /package/i.test(s.text));
    if (cpuPkg) {
      const r = S.rangeFor(cpuPkg, {}, undefined);
      eq('CPU power range is peak (no derivation)', r && r.source, 'peak');
      eq('CPU power gauge rejects peak', S.gaugeRangeFor(r, cpuPkg, null), null);
    }
    // mergeObservedPeaks keeps running max, ignores temp/level, tolerates junk
    const peakSensors = [{id:'/f1', type:'Fan', raw:1200}, {id:'/f1', type:'Fan', raw:1500}, {id:'/t1', type:'Temperature', raw:90}];
    eq('mergeObservedPeaks keeps running max', S.mergeObservedPeaks(peakSensors, {'/f1': 1000})['/f1'], 1500);
    eq('mergeObservedPeaks ignores temperature', S.mergeObservedPeaks(peakSensors, {})['/t1'], undefined);
    eq('mergeObservedPeaks tolerates junk input', S.mergeObservedPeaks(null, 'junk'), {});
    eq('mergeObservedPeaks preserves prior peak when lower', S.mergeObservedPeaks([{id:'/f1', type:'Fan', raw:900}], {'/f1': 2000})['/f1'], 2000);

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
    eq('normalize aliases and card order', (() => {
      const d = S.normalizeDashboardState({
        sensorAliases:{'/fan':'Pump','/bad':'','/long':'x'.repeat(120), '':'Nope'},
        primaryCards:['/cpu','/cpu','/gpu'],
        cardOrder:['/gpu','/cpu','/gpu']
      });
      return [d.sensorAliases, d.primaryCards, d.cardOrder];
    })(), [{'/fan':'Pump','/long':'x'.repeat(80)}, ['/cpu','/gpu'], ['/gpu','/cpu']]);
    eq('normalize rowOrder', S.normalizeDashboardState({rowOrder:{'k|Fan':['/f1','/f2'],'bad':[],7:'x'}}).rowOrder, {'k|Fan':['/f1','/f2']});
    eq('normalize net lists', (() => { const d = S.normalizeDashboardState({netAdapterOrder:['/nic/a','/nic/a'], hiddenNetAdapters:['/nic/b']});
      return [d.netAdapterOrder, d.hiddenNetAdapters]; })(), [['/nic/a'], ['/nic/b']]);
    eq('default has v3 fields', (() => { const d = S.defaultDashboardState();
      return [d.rangeOverrides, d.observedMax, d.powerLimitSamples, d.sensorAliases, d.primaryCards, d.cardOrder, d.rowOrder, d.netAdapterOrder, d.hiddenNetAdapters]; })(),
      [{}, {}, {}, {}, [], [], {}, [], []]);
    eq('sensorDisplayText prefers alias then fallback then raw text', [
      S.sensorDisplayText({id:'/fan', text:'Fan #7'}, {sensorAliases:{'/fan':'Pump'}}, 'Fan card'),
      S.sensorDisplayText({id:'/fan', text:'Fan #7'}, {}, 'Fan card'),
      S.sensorDisplayText({id:'/fan', text:'Fan #7'}, {}, '')
    ], ['Pump', 'Fan card', 'Fan #7']);
    eq('updateSensorAlias trims sets and clears', (() => {
      const set = S.updateSensorAlias({}, '/fan', '  Pump  ');
      const cleared = S.updateSensorAlias(set, '/fan', ' ');
      return [set.sensorAliases, cleared.sensorAliases];
    })(), [{'/fan':'Pump'}, {}]);
    eq('updateRangeOverride sets max', S.updateRangeOverride({}, '/p', '575', ''), {'/p':{max:575}});
    eq('updateRangeOverride sets min+max', S.updateRangeOverride({}, '/p', '575', '50'), {'/p':{max:575, min:50}});
    eq('updateRangeOverride ignores min >= max', S.updateRangeOverride({}, '/p', '575', '600'), {'/p':{max:575}});
    eq('updateRangeOverride empty max clears', S.updateRangeOverride({'/p':{max:575}}, '/p', '', ''), {});
    eq('updateRangeOverride junk max clears', S.updateRangeOverride({'/p':{max:575}}, '/p', 'abc', ''), {});
    eq('updateRangeOverride keeps other ids', S.updateRangeOverride({'/q':{max:100}}, '/p', '575', ''), {'/q':{max:100}, '/p':{max:575}});
    eq('rangeSourceLabel per source', [
      S.rangeSourceLabel({lo:0, hi:575, source:'override'}),
      S.rangeSourceLabel({lo:0, hi:575, source:'limit', derived:true}),
      S.rangeSourceLabel({lo:0, hi:89, source:'limit'}),
      S.rangeSourceLabel({lo:0, hi:100, source:'band'}),
      S.rangeSourceLabel({lo:0, hi:100, source:'control'}),
      S.rangeSourceLabel({lo:0, hi:178.5, source:'peak'}),
      S.rangeSourceLabel(null)
    ], ['operator override', 'derived hardware limit', 'hardware limit', 'semantic band', 'paired control %', 'observed peak', 'no known range']);

    // --- Slice 3 pre-flight: telemetry saves must not clobber user state ---
    const freshUserState = S.normalizeDashboardState({
      hiddenSensorIds:['/new-hidden'],
      pinnedCards:[{id:'/new-pin', title:'Keep Me'}],
      pinnedOrder:['/new-pin'],
      panelOrder:['/panel-new'],
      collapsedPanels:{'/panel-new':true},
      cardStyle:{'/new-pin':'graph'},
      rangeOverrides:{'/gpu/power':{max:575}},
      sensorAliases:{'/fan':'Pump'},
      primaryCards:['/cpu/temp','/gpu/power'],
      cardOrder:['/gpu/power','/cpu/temp'],
      rowOrder:{'/panel-new|Fan':['/fan2','/fan1']},
      netAdapterOrder:['/nic/a'],
      hiddenNetAdapters:['/nic/b'],
      graphsEnabled:true,
      paused:true,
      rate:5,
      theme:'light',
      observedMax:{'/fan':1200},
      powerLimitSamples:{'/gpu/0':[100,110,120,130,140,150,160,170,180]}
    });
    const staleTelemetryState = S.normalizeDashboardState({
      hiddenSensorIds:['/old-hidden'],
      pinnedCards:[{id:'/old-pin', title:'Lose Me'}],
      pinnedOrder:['/old-pin'],
      panelOrder:['/panel-old'],
      collapsedPanels:{'/panel-old':true},
      cardStyle:{'/old-pin':'number'},
      rangeOverrides:{'/gpu/power':{max:200}},
      sensorAliases:{'/fan':'Old Pump'},
      primaryCards:['/old/card'],
      cardOrder:['/old/card'],
      rowOrder:{'/panel-old|Fan':['/fan1','/fan2']},
      netAdapterOrder:['/nic/old'],
      hiddenNetAdapters:['/nic/old-hidden'],
      graphsEnabled:false,
      paused:false,
      rate:1,
      theme:'dark',
      observedMax:{'/fan':1500, '/pump':900},
      powerLimitSamples:{'/gpu/0':[900], '/gpu/1':[500,525,550,575,600,625,650,675,700,725]}
    });
    const mergedTelemetry = S.mergeTelemetryState(freshUserState, staleTelemetryState);
    eq('telemetry merge preserves fresh user layout', [
      mergedTelemetry.hiddenSensorIds, mergedTelemetry.pinnedCards, mergedTelemetry.pinnedOrder,
      mergedTelemetry.panelOrder, mergedTelemetry.collapsedPanels, mergedTelemetry.cardStyle,
      mergedTelemetry.rangeOverrides, mergedTelemetry.sensorAliases, mergedTelemetry.primaryCards,
      mergedTelemetry.cardOrder, mergedTelemetry.rowOrder, mergedTelemetry.netAdapterOrder,
      mergedTelemetry.hiddenNetAdapters, mergedTelemetry.graphsEnabled, mergedTelemetry.paused,
      mergedTelemetry.rate, mergedTelemetry.theme
    ], [
      ['/new-hidden'], [{id:'/new-pin', title:'Keep Me'}], ['/new-pin'],
      ['/panel-new'], {'/panel-new':true}, {'/new-pin':'graph'},
      {'/gpu/power':{max:575}}, {'/fan':'Pump'}, ['/cpu/temp','/gpu/power'],
      ['/gpu/power','/cpu/temp'], {'/panel-new|Fan':['/fan2','/fan1']}, ['/nic/a'],
      ['/nic/b'], true, true, 5, 'light'
    ]);
    eq('telemetry merge combines telemetry accumulators',
      [mergedTelemetry.observedMax, mergedTelemetry.powerLimitSamples],
      [{'/fan':1500,'/pump':900}, {'/gpu/0':[100,110,120,130,140,150,160,170,180], '/gpu/1':[500,525,550,575,600,625,650,675,700,725]}]);
    const telemetryStore = (() => { let slot = JSON.stringify(freshUserState); return {
      getItem:k => slot, setItem:(k, v) => { slot = v; }, read:() => JSON.parse(slot)
    }; })();
    const savedTelemetry = S.saveTelemetryState(telemetryStore, staleTelemetryState);
    eq('saveTelemetryState writes merged state', [savedTelemetry.sensorAliases, savedTelemetry.hiddenSensorIds, savedTelemetry.observedMax],
      [{'/fan':'Pump'}, ['/new-hidden'], {'/fan':1500,'/pump':900}]);
    eq('user save can intentionally replace layout', (() => {
      S.saveDashboardState(telemetryStore, staleTelemetryState);
      const saved = S.loadDashboardState(telemetryStore);
      return [saved.hiddenSensorIds, saved.sensorAliases, saved.rangeOverrides];
    })(), [['/old-hidden'], {'/fan':'Old Pump'}, {'/gpu/power':{max:200}}]);

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
