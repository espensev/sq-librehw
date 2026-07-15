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
    eq('normalize viewTheme cardTruth', S.normalizeDashboardState({viewTheme:'cardTruth'}).viewTheme, 'cardTruth');
    eq('normalize viewTheme workspace', S.normalizeDashboardState({viewTheme:'workspace'}).viewTheme, 'workspace');
    eq('normalize viewTheme junk -> standard', S.normalizeDashboardState({viewTheme:'x'}).viewTheme, 'standard');
    eq('default has studio settings', (() => { const d = S.defaultDashboardState();
      return [d.studioAccent, d.studioCanvas, d.studioCanvasOpacity, d.studioDensity, d.studioFocusLayout,
        d.studioFocusCount, d.studioShowSparklines, d.studioShowSystems, d.studioShowNetwork];
    })(), ['coral', 'ember', 55, 'comfortable', 'spotlight', 6, true, true, true]);
    eq('normalize accepts studio accents', ['coral','rose','amber','plum'].map(studioAccent =>
      S.normalizeDashboardState({studioAccent}).studioAccent), ['coral','rose','amber','plum']);
    eq('normalize accepts studio canvases', ['ember','strata','plain'].map(studioCanvas =>
      S.normalizeDashboardState({studioCanvas}).studioCanvas), ['ember','strata','plain']);
    eq('normalize clamps studio canvas opacity', [-5,0,47,100,180,'x'].map(studioCanvasOpacity =>
      S.normalizeDashboardState({studioCanvasOpacity}).studioCanvasOpacity), [0,0,47,100,100,55]);
    eq('normalize accepts studio densities', ['comfortable','compact'].map(studioDensity =>
      S.normalizeDashboardState({studioDensity}).studioDensity), ['comfortable','compact']);
    eq('normalize accepts studio focus layouts', ['spotlight','grid'].map(studioFocusLayout =>
      S.normalizeDashboardState({studioFocusLayout}).studioFocusLayout), ['spotlight','grid']);
    eq('normalize accepts studio focus counts', [4,6,8,12].map(studioFocusCount =>
      S.normalizeDashboardState({studioFocusCount}).studioFocusCount), [4,6,8,12]);
    eq('normalize studio visibility booleans', (() => {
      const d = S.normalizeDashboardState({studioShowSparklines:false, studioShowSystems:false, studioShowNetwork:false});
      return [d.studioShowSparklines, d.studioShowSystems, d.studioShowNetwork];
    })(), [false, false, false]);
    eq('normalize malformed studio settings independently', (() => {
      const d = S.normalizeDashboardState({
        studioAccent:'red', studioCanvas:'noise', studioCanvasOpacity:'none', studioDensity:'dense', studioFocusLayout:'stack',
        studioFocusCount:10, studioShowSparklines:0, studioShowSystems:0, studioShowNetwork:'false'
      });
      return [d.studioAccent, d.studioCanvas, d.studioCanvasOpacity, d.studioDensity, d.studioFocusLayout,
        d.studioFocusCount, d.studioShowSparklines, d.studioShowSystems, d.studioShowNetwork];
    })(), ['coral', 'ember', 55, 'comfortable', 'spotlight', 6, true, true, true]);
    eq('studio settings save/load round-trip', (() => {
      let slot = null;
      const store = {getItem:() => slot, setItem:(key, value) => { slot = value; }};
      S.saveDashboardState(store, {studioAccent:'plum', studioCanvas:'strata', studioCanvasOpacity:35,
        studioDensity:'compact', studioFocusLayout:'grid', studioFocusCount:8,
        studioShowSparklines:false, studioShowSystems:false, studioShowNetwork:true});
      const d = S.loadDashboardState(store);
      return [d.studioAccent, d.studioCanvas, d.studioCanvasOpacity, d.studioDensity, d.studioFocusLayout,
        d.studioFocusCount, d.studioShowSparklines, d.studioShowSystems, d.studioShowNetwork];
    })(), ['plum', 'strata', 35, 'compact', 'grid', 8, false, false, true]);
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
    const throwingStorage = {
      get length() { throw new Error('storage blocked'); },
      key: () => { throw new Error('storage blocked'); },
      getItem: () => { throw new Error('storage blocked'); },
      setItem: () => { throw new Error('storage blocked'); },
      removeItem: () => { throw new Error('storage blocked'); }
    };
    eq('throwing storage load falls back safely', S.loadDashboardState(throwingStorage).theme, 'dark');
    eq('throwing storage save does not abort', (() => {
      try { return S.saveDashboardState(throwingStorage, {theme:'light'}).theme; } catch (e) { return e.message; }
    })(), 'light');
    eq('throwing storage migration does not abort', (() => {
      try { return S.migrateLegacyState(throwingStorage, {rate:7}).rate; } catch (e) { return e.message; }
    })(), 7);
    eq('safe storage adapter retains an in-memory fallback', (() => {
      if (typeof S.createSafeStorage !== 'function') return 'missing';
      const safe = S.createSafeStorage(throwingStorage);
      const durable = safe.setItem('x', 'kept');
      return [safe.getItem('x'), safe.length, safe.key(0), durable];
    })(), ['kept', 1, 'x', false]);
    eq('safe storage adapter reports durable writes', (() => {
      const primary = new Map();
      const safe = S.createSafeStorage({
        getItem:key => primary.get(key) ?? null,
        setItem:(key, value) => primary.set(key, value)
      });
      return [safe.setItem('x', 'durable'), primary.get('x')];
    })(), [true, 'durable']);
    eq('safe storage adapter tolerates a throwing browser storage getter', (() => {
      const safe = S.createSafeStorage(() => { throw new Error('security'); });
      S.saveDashboardState(safe, {theme:'light', rate:4});
      const loaded = S.loadDashboardState(safe);
      return [loaded.theme, loaded.rate];
    })(), ['light', 4]);
    eq('safe storage shadow wins after quota and removal failures', (() => {
      let primary = 'old';
      const quotaStore = {getItem:() => primary, setItem:() => { throw new Error('quota'); },
        removeItem:() => { throw new Error('blocked'); }};
      const safe = S.createSafeStorage(quotaStore);
      const loaded = safe.getItem('x');
      safe.setItem('x', 'new');
      const afterWrite = safe.getItem('x');
      safe.removeItem('x');
      return [loaded, afterWrite, safe.getItem('x')];
    })(), ['old', 'new', null]);

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

    // --- C1 T2: network adapter grouping ---
    const mkNic = (g, hw, type, text, raw, n) => {
      const s = { cls: 'nic', hw, type, text, raw, value: String(raw), id: (g || 'hw') + '/x/' + n };
      if (g) s.hwid = g;
      return s;
    };
    const nicSensors = [
      mkNic('/nic/%7BAAA%7D', 'Realtek Gaming 2.5GbE', 'Throughput', 'Upload Speed', 100, 1),
      mkNic('/nic/%7BAAA%7D', 'Realtek Gaming 2.5GbE', 'Load', 'Network Utilization', 1, 2),
      mkNic('/nic/%7BBBB%7D', 'Realtek Gaming 2.5GbE', 'Throughput', 'Download Speed', 900, 3),
      mkNic('/nic/%7BCCC%7D', 'Wi-Fi', 'Throughput', 'Upload Speed', 0, 4),
      mkNic(null, 'TAP Adapter', 'Throughput', 'Upload Speed', 5, 5)
    ];
    eq('netAdapterKey uses hwid', S.netAdapterKey(nicSensors[0]), '/nic/%7BAAA%7D');
    eq('netAdapterKey falls back to hw label', S.netAdapterKey(nicSensors[4]), 'hw:TAP Adapter');
    const adapters = S.buildNetAdapters(nicSensors);
    eq('adapters group by hwid', adapters.map(a => a.key), ['/nic/%7BAAA%7D', '/nic/%7BBBB%7D', '/nic/%7BCCC%7D', 'hw:TAP Adapter']);
    eq('duplicate adapter labels get #N', adapters.slice(0, 2).map(a => a.label), ['Realtek Gaming 2.5GbE #1', 'Realtek Gaming 2.5GbE #2']);
    eq('unique adapter label stays plain', adapters[2].label, 'Wi-Fi');
    eq('adapter activity needs Throughput raw>0', adapters.map(a => a.active), [true, true, false, true]);
    eq('adapter keeps its own sensors', adapters[0].ss.map(s => s.id), ['/nic/%7BAAA%7D/x/1', '/nic/%7BAAA%7D/x/2']);
    eq('non-nic sensors ignored', S.buildNetAdapters([{ cls: 'cpu', hw: 'X', hwid: '/c/0', type: 'Load', text: 't', raw: 1, id: '/c/0/l/0' }]), []);
    eq('buildNetAdapters tolerates junk', S.buildNetAdapters(null), []);

    // --- C1 T3: per-adapter panel items ---
    const nicState = patch => Object.assign(S.defaultDashboardState(), patch);
    const netItems0 = S.buildPanelItems(nicSensors, S.defaultDashboardState());
    eq('one panel item per active adapter', netItems0.map(i => i.key), ['/nic/%7BAAA%7D', '/nic/%7BBBB%7D', 'hw:TAP Adapter']);
    eq('adapter items flagged net and collapsed', netItems0.every(i => i.net === true && i.collapsed === true), true);
    eq('adapter item labels deduped', netItems0[0].label, 'Realtek Gaming 2.5GbE #1');
    eq('idle adapter emits no panel', netItems0.some(i => i.key === '/nic/%7BCCC%7D'), false);
    eq('hidden adapter excluded', S.buildPanelItems(nicSensors, nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] })).map(i => i.key), ['/nic/%7BBBB%7D', 'hw:TAP Adapter']);
    eq('netAdapterOrder applies', S.buildPanelItems(nicSensors, nicState({ netAdapterOrder: ['hw:TAP Adapter', '/nic/%7BBBB%7D'] })).map(i => i.key), ['hw:TAP Adapter', '/nic/%7BBBB%7D', '/nic/%7BAAA%7D']);
    eq('stale order keys ignored', S.buildPanelItems(nicSensors, nicState({ netAdapterOrder: ['panel:network', '/nic/%7BBBB%7D'] })).map(i => i.key)[0], '/nic/%7BBBB%7D');
    eq('no-state call keeps active adapters (compat)', S.buildPanelItems(nicSensors).map(i => i.key), ['/nic/%7BAAA%7D', '/nic/%7BBBB%7D', 'hw:TAP Adapter']);
    const mixedItems = S.buildPanelItems(sensors.concat(nicSensors), S.defaultDashboardState());
    eq('nic panels trail non-nic panels', mixedItems.findIndex(i => i.net), mixedItems.filter(i => !i.net).length);
    eq('mixed panel keys stay unique', new Set(mixedItems.map(i => i.key)).size, mixedItems.length);
    eq('legacy merged network bucket gone', mixedItems.some(i => i.key === 'panel:network'), false);
    eq('mixed items reindex contiguously', mixedItems.every((it, i) => it.index === i), true);

    // --- C1 T4: hidden-adapter sensors are offscreen in the popover model ---
    eq('nic sensor of hidden adapter is offscreen', S.sensorVisibility(nicSensors[0], nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 'offscreen');
    eq('nic sensor of visible adapter unaffected', S.sensorVisibility(nicSensors[0], S.defaultDashboardState()), 'visible');
    eq('explicit sensor hide beats adapter hide', S.sensorVisibility(nicSensors[0], nicState({ hiddenSensorIds: [nicSensors[0].id], hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 'hidden');
    eq('hiddenSensorCount includes adapter-hidden sensors', S.hiddenSensorCount(nicSensors, nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 2);
    eq('popover ranks adapter-hidden ahead of visible', S.sensorPopoverRows(nicSensors, nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] }), '')[0].visibility, 'offscreen');

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
      S.rangeSourceLabel({lo:0, hi:178.5, source:'history'}),
      S.rangeSourceLabel(null)
    ], ['operator override', 'derived hardware limit', 'hardware limit', 'semantic band', 'paired control %', 'observed peak', 'visible history', 'no known range']);
    eq('graphScaleFor keeps the plotted sensor range',
      S.graphScaleFor({lo:0, hi:3000, source:'peak'}, [{raw:20}, {raw:80}]),
      {lo:0, hi:3000, source:'peak'});
    eq('graphScaleFor labels history-scaled sensors honestly', (() => {
      const scale = S.graphScaleFor(null, [{raw:39.125}, {raw:41.5}, {raw:null}]);
      return [scale, S.graphScaleText(scale, {value:'41.5 MB/s'})];
    })(), [{lo:39.125, hi:41.5, source:'history'}, 'Scale 39.13 - 41.5 MB/s · visible history']);
    eq('graphScaleFor expands a flat history without inventing zero',
      S.graphScaleFor(null, [{raw:7}, {raw:7}]), {lo:6, hi:8, source:'history'});
    eq('graphScaleFor keeps non-negative flat history non-negative',
      S.graphScaleFor(null, [{raw:0}, {raw:0}]), {lo:0, hi:1, source:'history'});

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
      viewTheme:'cardTruth',
      studioAccent:'plum',
      studioCanvas:'strata',
      studioCanvasOpacity:25,
      studioDensity:'compact',
      studioFocusLayout:'grid',
      studioFocusCount:12,
      studioShowSparklines:false,
      studioShowSystems:false,
      studioShowNetwork:false,
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
      viewTheme:'standard',
      studioAccent:'amber',
      studioCanvas:'plain',
      studioCanvasOpacity:90,
      studioDensity:'comfortable',
      studioFocusLayout:'spotlight',
      studioFocusCount:4,
      studioShowSparklines:true,
      studioShowSystems:true,
      studioShowNetwork:true,
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
      mergedTelemetry.rate, mergedTelemetry.theme, mergedTelemetry.viewTheme
    ], [
      ['/new-hidden'], [{id:'/new-pin', title:'Keep Me'}], ['/new-pin'],
      ['/panel-new'], {'/panel-new':true}, {'/new-pin':'graph'},
      {'/gpu/power':{max:575}}, {'/fan':'Pump'}, ['/cpu/temp','/gpu/power'],
      ['/gpu/power','/cpu/temp'], {'/panel-new|Fan':['/fan2','/fan1']}, ['/nic/a'],
      ['/nic/b'], true, true, 5, 'light', 'cardTruth'
    ]);
    eq('telemetry merge preserves studio settings', [
      mergedTelemetry.studioAccent, mergedTelemetry.studioCanvas, mergedTelemetry.studioCanvasOpacity,
      mergedTelemetry.studioDensity,
      mergedTelemetry.studioFocusLayout, mergedTelemetry.studioFocusCount,
      mergedTelemetry.studioShowSparklines, mergedTelemetry.studioShowSystems,
      mergedTelemetry.studioShowNetwork
    ], ['plum', 'strata', 25, 'compact', 'grid', 12, false, false, false]);
    const workspaceTelemetry = S.mergeTelemetryState(
      S.normalizeDashboardState(Object.assign({}, freshUserState, {viewTheme:'workspace'})),
      staleTelemetryState);
    eq('telemetry merge preserves Workspace root plus Standard and Studio preferences', [
      workspaceTelemetry.viewTheme, workspaceTelemetry.hiddenSensorIds,
      workspaceTelemetry.sensorAliases, workspaceTelemetry.cardOrder,
      workspaceTelemetry.studioAccent, workspaceTelemetry.studioCanvas,
      workspaceTelemetry.studioDensity, workspaceTelemetry.studioFocusLayout
    ], ['workspace', ['/new-hidden'], {'/fan':'Pump'}, ['/gpu/power','/cpu/temp'],
      'plum', 'strata', 'compact', 'grid']);
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

    // Cached paints must never be treated as telemetry ingestion. This helper is
    // the exact seam used by render(), so Studio preference rerenders exercise a
    // no-op rather than growing histories, samples, peaks, or ticks.
    eq('cached telemetry ingestion is a byte-for-byte no-op', (() => {
      if (typeof S.ingestTelemetry !== 'function') return 'missing';
      const runtime = {dashboard:S.defaultDashboardState(), tickCount:0};
      const sample = [
        {id:'/g/0/power/0', hwid:'/g/0', cls:'gpu', type:'Power', text:'GPU Package', raw:150},
        {id:'/g/0/load/0', hwid:'/g/0', cls:'gpu', type:'Load', text:'GPU Power', raw:30}
      ];
      S.ingestTelemetry(runtime, sample, true, 1000);
      const before = JSON.stringify({tick:runtime.tickCount, dashboard:runtime.dashboard,
        history:S.historyFor('/g/0/power/0')});
      for (let i = 0; i < 12; i++) S.ingestTelemetry(runtime, sample, false, 2000 + i);
      return [JSON.stringify({tick:runtime.tickCount, dashboard:runtime.dashboard,
        history:S.historyFor('/g/0/power/0')}) === before,
        runtime.dashboard.powerLimitSamples['/g/0'].length,
        S.derivedPowerLimit('/g/0', runtime.dashboard)];
    })(), [true, 1, null]);

    eq('telemetry maps are globally bounded during normalization', (() => {
      const observedMax = {}, powerLimitSamples = {};
      for (let i = 0; i < 3000; i++) {
        observedMax['/sensor/' + i] = i;
        powerLimitSamples['/gpu/' + i] = [i + 1];
      }
      const normalized = S.normalizeDashboardState({observedMax, powerLimitSamples});
      return [Object.keys(normalized.observedMax).length, Object.keys(normalized.powerLimitSamples).length,
        S.SENSOR_STATE_MAX_KEYS, S.POWER_STATE_MAX_KEYS];
    })(), [2048, 512, 2048, 512]);

    eq('transient motion and history maps are globally bounded', (() => {
      S.resetTelemetryCaches();
      const many = Array.from({length:2300}, (_, i) => ({id:'/temp/' + i, hwid:'/hw/' + i,
        cls:'cpu', type:'Temperature', text:'Core', raw:40 + (i % 10)}));
      S.ingestTelemetry({dashboard:S.defaultDashboardState()}, many, true, 1);
      const sizes = S.telemetryCacheSizes();
      return [sizes.motion, sizes.history];
    })(), [2048, 2048]);

    eq('departed telemetry survives grace then prunes from memory and persisted maps', (() => {
      if (typeof S.ingestTelemetry !== 'function' || typeof S.telemetryCacheSizes !== 'function') return 'missing';
      S.resetTelemetryCaches();
      const runtime = {dashboard:S.normalizeDashboardState({observedMax:{'/gone':40},
        powerLimitSamples:{'/gone-hw':[100]}}), tickCount:0};
      const gone = [{id:'/gone', hwid:'/gone-hw', cls:'gpu', type:'Power', text:'GPU Package', raw:40}];
      S.ingestTelemetry(runtime, gone, true, 0);
      S.ingestTelemetry(runtime, [], true, S.SENSOR_STATE_GRACE_MS - 1);
      const during = ['/gone' in runtime.dashboard.observedMax, '/gone-hw' in runtime.dashboard.powerLimitSamples];
      const pruned = S.ingestTelemetry(runtime, [], true, S.SENSOR_STATE_GRACE_MS + 1);
      const after = ['/gone' in runtime.dashboard.observedMax, '/gone-hw' in runtime.dashboard.powerLimitSamples,
        S.telemetryCacheSizes().history];
      let slot = JSON.stringify({observedMax:{'/gone':40}, powerLimitSamples:{'/gone-hw':[100]}});
      const persisted = {getItem:() => slot, setItem:(key, value) => { slot = value; }};
      const saved = S.saveTelemetryState(persisted, runtime.dashboard, pruned.removals);
      return [during, after, ['/gone' in saved.observedMax, '/gone-hw' in saved.powerLimitSamples]];
    })(), [[true, true], [false, false, 0], [false, false]]);

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
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = runConsoleTests;
    if (require.main === module) {
      const test = require('node:test');
      const assert = require('node:assert/strict');
      const fs = require('node:fs');
      const path = require('node:path');
      const source = fs.readFileSync(path.join(__dirname,
        '../LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js'), 'utf8');
      const modelWindow = {SQ_NO_BOOT:true};
      Function('window', source)(modelWindow);
      const S = modelWindow.SQ;

      test('polling is single-flight, abortable, and ignores cancelled generations', async () => {
        assert.equal(typeof S.createPollController, 'function');
        const pending = [], painted = [], errors = [];
        const poller = S.createPollController({
          intervalMs:60000,
          timeoutMs:60000,
          request:signal => new Promise(resolve => pending.push({signal, resolve})),
          onData:data => painted.push(data),
          onError:error => errors.push(error)
        });
        poller.start();
        assert.equal(pending.length, 1);
        poller.refresh();
        assert.equal(pending.length, 1, 'a refresh must not overlap the active request');
        poller.setPaused(true);
        assert.equal(pending[0].signal.aborted, true);
        poller.setPaused(false);
        assert.equal(pending.length, 1, 'resume waits for cancelled request ownership to settle');
        pending[0].resolve({generation:0});
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(pending.length, 2);
        assert.deepEqual(painted, [], 'cancelled response cannot paint');
        pending[1].resolve({generation:1});
        await new Promise(resolve => setImmediate(resolve));
        assert.deepEqual(painted, [{generation:1}]);
        assert.deepEqual(errors, []);
        poller.stop();
      });

      test('visibility and rate changes cancel work; timeout reports an error', async () => {
        const pending = [], errors = [];
        const poller = S.createPollController({
          intervalMs:60000,
          timeoutMs:15,
          request:signal => new Promise((resolve, reject) => {
            pending.push({signal, resolve});
            signal.addEventListener('abort', () => reject(new Error('aborted')), {once:true});
          }),
          onData:() => {},
          onError:error => errors.push(error)
        });
        poller.start();
        poller.setHidden(true);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(pending[0].signal.aborted, true);
        assert.equal(errors.length, 0, 'visibility cancellation is not a telemetry error');
        poller.setHidden(false);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(pending.length, 2);
        poller.setInterval(20000);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(pending[1].signal.aborted, true);
        poller.refresh();
        await new Promise(resolve => setTimeout(resolve, 30));
        assert.equal(errors.length, 1, 'request timeout reaches stale/error handling');
        poller.stop();
      });

      test('a persisted paused dashboard gets one snapshot and no recurring poll', async () => {
        let requests = 0, paints = 0;
        const poller = S.createPollController({
          paused:true,
          intervalMs:10,
          timeoutMs:1000,
          request:async () => { requests++; return {snapshot:true}; },
          onData:() => { paints++; }
        });
        await poller.start(true);
        await new Promise(resolve => setTimeout(resolve, 20));
        assert.deepEqual([requests, paints, poller.status().scheduled], [1, 1, false]);
        poller.stop();
      });
    }
  }
  else root.runConsoleTests = runConsoleTests;
})(typeof window !== 'undefined' ? window : globalThis);
