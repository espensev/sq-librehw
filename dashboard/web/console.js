// Embedded dashboard composition and shared Standard, Studio, and Workspace views.
(function () {
  'use strict';
  const SQ = window.SQModel.create({pageOrigin:window.location?.origin});
  window.SQ = SQ;
  const $ = s => document.querySelector(s);
  const Workspace = window.SQWorkspace;
  const STLABEL = { ok:'OK', warn:'WATCH', crit:'CRIT', info:'INFO', off:'IDLE' };
  const STGLYPH = { ok:'●', warn:'▲', crit:'✕', info:'·', off:'○' };
  const CLASSLABEL = { cpu:'CPU', gpu:'GPU', igpu:'iGPU', mem:'MEMORY', dimm:'DIMM', nvme:'STORAGE', disk:'DISK', mb:'BOARD', nic:'NET', other:'MISC' };
  const TORDER = ['Temperature','TemperatureRate','Limits','Load','Power','Clock','Fan','Control','Voltage','Current','Data','SmallData','Throughput','Level','Factor','Timing'];
  const TICONS = {
    temp:  '<path d="M8 3a2 2 0 0 1 4 0v7.3a4.5 4.5 0 1 1-4 0z" fill="none" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="14" r="2" fill="currentColor"/>',
    load:  '<path d="M3 13a7 7 0 0 1 14 0" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 13 13.5 8" stroke="currentColor" stroke-width="1.6"/><circle cx="10" cy="13" r="1.4" fill="currentColor"/>',
    fan:   '<circle cx="10" cy="10" r="1.8" fill="currentColor"/><path d="M10 8.2C10 4 13 3 15 5c-1 2-3 3-5 3.2M11.8 10c4.2 0 5.2 3 3.2 5-2-1-3-3-3.2-5M10 11.8C10 16 7 17 5 15c1-2 3-3 5-3.2M8.2 10C4 10 3 7 5 5c2 1 3 3 3.2 5" fill="none" stroke="currentColor" stroke-width="1.4"/>',
    power: '<path d="M11 2 4 12h5l-1 6 7-10h-5z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>',
    clock: '<circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M10 6v4l3 2" fill="none" stroke="currentColor" stroke-width="1.6"/>',
    data:  '<path d="M2 11h4l2-5 4 9 2-5h4" fill="none" stroke="currentColor" stroke-width="1.6"/>'
  };
  const tIcon = kind => `<svg class="ticon" viewBox="0 0 20 20" aria-hidden="true">${TICONS[kind] || TICONS.data}</svg>`;
  const isCoreRow = s => /\bcore\s*#?\d/i.test(s.text) && !/average|max|total/i.test(s.text);
  const storage = SQ.createSafeStorage(() => window.localStorage);
  const dashboard0 = SQ.migrateLegacyState(storage, SQ.loadDashboardState(storage));
  SQ.saveDashboardState(storage, dashboard0);
  const workspace0 = Workspace ? Workspace.load(storage) : null;
  let workspaceAuthoringOpen = false;
  let workspaceSensorReturnFocus = null;
  const state = {
    paused: dashboard0.paused,
    rate: dashboard0.rate,
    dragging: false,
    dashboard: dashboard0,
    contexts: SQ.loadContextState(storage),
    workspace: workspace0,
    workspaceSensorTargetId: null,
    workspaceSensorFilter: '',
    workspaceSensorSignature: null,
    workspaceNotice: null,
    lastData: null,
    allSensors: [],
    visibleSensors: [],
    panelItems: [],
    limits: {},
    expanded: new Set(),
    xpEnter: null,
    primaryIds: new Set(),
    inlineEditing: false,
    inlineEditingUntil: 0
  };
  const stableNodeSignatures = new WeakMap();
  const stableHtmlSignatures = new WeakMap();

  function esc(v) {
    return String(v ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
  }
  function isInlineEditTarget(el) {
    return !!(el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA') && el.closest('.xp, .rowxp'));
  }
  function finishInlineEdit() {
    state.inlineEditing = false;
    state.inlineEditingUntil = 0;
  }
  function captureStandardFocus(element) {
    const host = element?.closest?.(
      '.cell[data-sid], .row[data-sid], .xp-overlay[data-sid], .rowxp[data-sid], .panel[data-key]');
    const region = host?.closest?.('#pinned, #pfd, #panels, #netPanels');
    if (!host || !region) return null;
    let hostSelector = null;
    if (host.classList.contains('cell')) hostSelector = `.cell[data-sid="${CSS.escape(host.dataset.sid)}"]`;
    else if (host.classList.contains('row')) hostSelector = `.row[data-sid="${CSS.escape(host.dataset.sid)}"]`;
    else if (host.classList.contains('xp-overlay')) hostSelector = `.xp-overlay[data-sid="${CSS.escape(host.dataset.sid)}"]`;
    else if (host.classList.contains('rowxp')) hostSelector = `.rowxp[data-sid="${CSS.escape(host.dataset.sid)}"]`;
    else if (host.classList.contains('panel')) hostSelector = `.panel[data-key="${CSS.escape(host.dataset.key)}"]`;
    if (!hostSelector) return null;
    let controlSelector = null;
    if (element !== host) {
      if (element.classList?.contains('pin')) controlSelector = '.pin';
      else if (element.classList?.contains('star')) controlSelector = '.star';
      else if (element.classList?.contains('row-grip')) controlSelector = '.row-grip';
      else if (element.classList?.contains('grip'))
        controlSelector = host.classList.contains('panel') ? '.panel-head > .grip' : '.grip';
      else if (element.dataset?.mv) controlSelector = `[data-mv="${CSS.escape(element.dataset.mv)}"]`;
      else if (element.dataset?.moreGroup)
        controlSelector = `[data-more-group="${CSS.escape(element.dataset.moreGroup)}"]`;
      else if (element.dataset?.act === 'pin' || element.dataset?.act === 'unpin')
        controlSelector = '[data-act="pin"], [data-act="unpin"]';
      else if (element.dataset?.act === 'primary-add' || element.dataset?.act === 'primary-remove')
        controlSelector = '[data-act="primary-add"], [data-act="primary-remove"]';
      else if (element.dataset?.act) controlSelector = `[data-act="${CSS.escape(element.dataset.act)}"]`;
    }
    if (element !== host && !controlSelector) return null;
    return {
      element,
      regionId: region.id,
      hostSelector,
      controlSelector
    };
  }
  function restoreStandardFocus(focus) {
    if (!focus || focus.element?.isConnected) return;
    const region = document.getElementById(focus.regionId);
    if (!region) return;
    const host = region.querySelector(focus.hostSelector);
    const target = focus.controlSelector ? host?.querySelector(focus.controlSelector) : host;
    (target || host)?.focus({ preventScroll: true });
  }
  function ctlCluster(id, label, opts) {
    const pinned = SQ.isPinned(state.dashboard, id);
    const primary = state.primaryIds.has(id);
    const star = `<button class="ctl star${primary ? ' on' : ''}" data-act="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(id)}" aria-label="${primary ? 'Remove from primary' : 'Show as primary'} ${esc(label)}" title="${primary ? 'Remove from primary' : 'Show as primary'}">${primary ? '&#9733;' : '&#9734;'}</button>`;
    const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
    const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
    return star + pin + hide;
  }
  function rootNode(data) {
    return data.Children && data.Children[0] ? data.Children[0] : data;
  }
  function saveDashboard() {
    state.dashboard = SQ.saveDashboardState(storage, state.dashboard);
    SQ.acknowledgeTelemetrySave(state);
    paintGraphs();
  }
  function saveTelemetryDashboard(ingestion) {
    state.dashboard = SQ.saveTelemetryState(storage, state.dashboard, ingestion?.removals);
    SQ.acknowledgeTelemetrySave(state);
    paintGraphs();
  }
  function rerender() {
    if (state.dragging) return;
    if (state.lastData) render(state.lastData);
  }
  function commitDashboard() {
    saveDashboard();
    rerender();
  }
  function rowGroupKey(panelKey, type) {
    return `${panelKey}|${type}`;
  }
  function orderedRows(panelKey, type, rows) {
    const key = rowGroupKey(panelKey, type);
    return SQ.applyOrder(rows, state.dashboard.rowOrder[key] || [], s => s.id);
  }
  function moveRow(groupKey, id, delta) {
    const group = Array.from(document.querySelectorAll('[data-row-group]'))
      .find(el => el.dataset.rowGroup === groupKey &&
        Array.from(el.querySelectorAll('.row')).some(row => row.dataset.key === id));
    if (!group) return;
    const rows = Array.from(group.querySelectorAll('.row'))
      .map(el => el.dataset.key)
      .filter(k => typeof k === 'string' && k.length);
    state.dashboard.rowOrder[groupKey] = SQ.moveKey(SQ.mergeOrder(state.dashboard.rowOrder[groupKey], rows), id, delta);
    commitDashboard();
  }
  function movePanel(key, delta) {
    const merged = SQ.mergeOrder(state.dashboard.panelOrder, state.panelItems.filter(i => !i.net).map(i => i.key));
    const next = SQ.moveKey(merged, key, delta);
    if (next === merged) return;   // out-of-bounds (top ▲ / bottom ▼): don't dirty panelOrder
    state.dashboard.panelOrder = next;
    commitDashboard();
  }
  function resetPanelOrder() {
    state.dashboard.panelOrder = [];
    commitDashboard();
  }
  function moveAdapter(key, delta) {
    const merged = SQ.mergeOrder(state.dashboard.netAdapterOrder, state.panelItems.filter(i => i.net).map(i => i.key));
    const next = SQ.moveKey(merged, key, delta);
    if (next === merged) return;   // §12.2: top-▲ / bottom-▼ must not dirty netAdapterOrder
    state.dashboard.netAdapterOrder = next;
    commitDashboard();
  }
  function hideAdapter(key) {
    if (state.dashboard.hiddenNetAdapters.includes(key)) return;
    state.dashboard.hiddenNetAdapters = state.dashboard.hiddenNetAdapters.concat(key);
    commitDashboard();
  }
  function showAdapter(key) {
    state.dashboard.hiddenNetAdapters = state.dashboard.hiddenNetAdapters.filter(k => k !== key);
    commitDashboard();
  }
  function setSensorHidden(id, hidden) {
    const cfg = state.dashboard;
    cfg.hiddenSensorIds = cfg.hiddenSensorIds.filter(x => x !== id);
    if (hidden) cfg.hiddenSensorIds.push(id);
    commitDashboard();
  }
  function pinSensor(id) {
    if (!state.dashboard.pinnedCards.some(c => c.id === id))
      state.dashboard.pinnedCards.push({ id, title: '' });
    if (!state.dashboard.pinnedOrder.includes(id))
      state.dashboard.pinnedOrder.push(id);
    commitDashboard();
  }
  function unpinSensor(id) {
    state.dashboard.pinnedCards = state.dashboard.pinnedCards.filter(c => c.id !== id);
    state.dashboard.pinnedOrder = state.dashboard.pinnedOrder.filter(x => x !== id);
    commitDashboard();
  }
  function setPrimaryCardState(id, enabled) {
    state.dashboard = SQ.setPrimaryCard(state.dashboard, id, enabled, state.allSensors);
    commitDashboard();
  }
  function resetPrimaryCardsState() {
    state.dashboard = SQ.resetPrimaryCards(state.dashboard);
    commitDashboard();
  }
  function render(data, freshTelemetry = false) {
    state.lastData = data;
    const ae = document.activeElement;
    if (state.inlineEditing || Date.now() < state.inlineEditingUntil || isInlineEditTarget(ae))
      return;   // an inline detail edit is in progress; don't clobber it on poll.
    const root = rootNode(data);
    const host = root.Text || 'Sensor';
    const allSensors = SQ.flatten(root);
    // Cached rerenders (Studio preferences, order, expand/collapse) are paint
    // only. All telemetry mutation lives behind this fresh-data seam.
    const ingestion = SQ.ingestTelemetry(state, allSensors, freshTelemetry);
    allSensors.forEach(sensor => {
      sensor.presetType = SQ.isLimitSensor(sensor) || SQ.isStaticDriveAuxTemp(sensor) || SQ.isStaticMbTemp(sensor)
        ? 'Auxiliary'
        : sensor.type;
    });
    const sensors = SQ.visibleSensors(allSensors, state.dashboard);
    const limits = SQ.deriveLimits(sensors);
    sensors.forEach(s => s.status = SQ.statusOf(s, limits));
    state.allSensors = allSensors;
    state.visibleSensors = sensors;
    state.primaryIds = new Set(SQ.primaryCardIds(sensors, state.dashboard));
    state.limits = limits;

    const alarm = sensors.filter(s => s.status !== 'info' && s.status !== 'off');
    if (state.dashboard.viewTheme === 'workspace') {
      const workspaceLimits = SQ.deriveLimits(allSensors);
      allSensors.forEach(s => s.status = SQ.statusOf(s, workspaceLimits));
      state.limits = workspaceLimits;
      renderWorkspace(host, allSensors, workspaceLimits, data.Version);
    } else if (state.dashboard.viewTheme === 'cardTruth') {
      state.panelItems = SQ.buildPanelItems(sensors, state.dashboard);
      renderStudio(host, sensors, limits, alarm, data.Version);
    } else {
      const standardFocus = captureStandardFocus(ae);
      renderPinnedCards(sensors, limits);
      renderPFD(sensors, limits);
      renderPlacard(alarm);
      renderPanels(sensors);
      restoreStandardFocus(standardFocus);
    }
    renderSensorsPopover();
    $('#host').textContent = host;
    $('#foot-left').textContent = `LibreHardwareMonitor ${data.Version} · host ${host} · GET /data.json · ${state.rate}s poll`;
    if (freshTelemetry && !state.paused) {
      $('#freshtxt').textContent = 'updated ' + new Date().toLocaleTimeString();
      $('#freshdot').className = 'lamp s-ok';
    }
    // Throttled persistence so peaks/derived limits survive reloads without
    // per-tick localStorage writes. Power samples (needed to derive a limit)
    // flush more often than peaks (which only grow).
    if (ingestion.ingested && (ingestion.removals.observedMax.length || ingestion.removals.powerLimitSamples.length))
      saveTelemetryDashboard(ingestion);
    else if (ingestion.ingested && ingestion.samplesChanged && state.tickCount % 5 === 0) saveTelemetryDashboard(ingestion);
    else if (ingestion.ingested && state.tickCount % 30 === 0) saveTelemetryDashboard(ingestion);
  }

  function arcSVG(id, frac) {
    const R = 30, C = 2 * Math.PI * R, len = C * 0.75;
    const f = SQ.smoothFraction(id, frac, state.rate);
    const off = len * (1 - f);
    return `<svg class="arc" viewBox="0 0 78 78" aria-hidden="true"><g transform="rotate(135 39 39)">
      <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--line-soft)" stroke-width="6"
        stroke-linecap="round" stroke-dasharray="${len} ${C}"/>
      <circle cx="39" cy="39" r="${R}" fill="none" stroke="var(--c)" stroke-width="6" stroke-linecap="round"
        stroke-dasharray="${len} ${C}" stroke-dashoffset="${off}"/></g></svg>`;
  }
  function sparkAreaSVG(sensor, bounded) {
    const hist = SQ.historyFor(sensor.id).filter(p => Number.isFinite(p.raw));
    if (hist.length < 2) return '<div class="spark empty"></div>';
    const values = hist.map(p => p.raw);
    let min = bounded ? bounded[0] : Math.min(...values);
    let max = bounded ? bounded[1] : Math.max(...values);
    if (!(max > min)) { min -= 1; max += 1; }
    const w = 120, h = 28;
    const pts = hist.map((p, i) => {
      const x = (i / (hist.length - 1)) * w;
      const y = h - ((p.raw - min) / (max - min)) * h;
      return `${x.toFixed(1)},${Math.max(0, Math.min(h, y)).toFixed(1)}`;
    });
    return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">
      <polygon points="0,${h} ${pts.join(' ')} ${w},${h}" fill="var(--tc)" opacity="0.18"/>
      <polyline points="${pts.join(' ')}" fill="none" stroke="var(--tc)" stroke-width="1.6" vector-effect="non-scaling-stroke"/></svg>`;
  }
  function rangeMarkup(s) {
    const rmin = SQ.splitValue(s.min).n, rmax = SQ.splitValue(s.max).n;
    const badTempMin = s.type === 'Temperature' && !(parseFloat(rmin) > 0);
    if (s.min == null || s.min === '') return '';
    return badTempMin ? `<div class="range">peak <b>${esc(rmax)}</b></div>` :
      `<div class="range"><b>${esc(rmin)}</b> &rarr; <b>${esc(rmax)}</b></div>`;
  }
  function rangeDetailText(s, rr) {
    const ctrl = SQ.fanControlFor(s, state.allSensors);
    const effective = ctrl ? { lo: 0, hi: 100, source: 'control' } : rr;
    if (!effective) return SQ.rangeSourceLabel(null);
    const prefix = effective.derived ? '~' : '';
    return `${effective.lo} - ${prefix}${effective.hi} · ${SQ.rangeSourceLabel(effective)}`;
  }
  function xpEl(s, rr, opts) {
    const ov = state.dashboard.rangeOverrides[s.id];
    const alias = SQ.sensorAlias(state.dashboard, s.id);
    const pinned = SQ.isPinned(state.dashboard, s.id);
    const isPrimary = state.primaryIds.has(s.id);
    const rawMin = s.min == null || s.min === '' ? '—' : s.min;
    const rawMax = s.max == null || s.max === '' ? '—' : s.max;
    const value = s.value ?? '—';
    const styleSel = opts.style
      ? `<select class="style-select" data-act="style" data-id="${esc(s.id)}" aria-label="Card style">
          ${['auto','gauge','number','graph'].map(v =>
            `<option value="${v}"${(state.dashboard.cardStyle[s.id] || 'auto') === v ? ' selected' : ''}>${v}</option>`).join('')}
        </select>` : '';
    const moveButtons = opts.movable
      ? `<button class="iconbtn" data-act="${opts.rowGroup ? 'row-up' : 'move-left'}" data-id="${esc(s.id)}"${opts.rowGroup ? ` data-row-group="${esc(opts.rowGroup)}"` : ''} aria-label="Move earlier" title="Move earlier">&#9650;</button>
         <button class="iconbtn" data-act="${opts.rowGroup ? 'row-down' : 'move-right'}" data-id="${esc(s.id)}"${opts.rowGroup ? ` data-row-group="${esc(opts.rowGroup)}"` : ''} aria-label="Move later" title="Move later">&#9660;</button>` : '';
    const el = document.createElement('div');
    el.className = opts.cls;
    el.dataset.sid = s.id;
    el.innerHTML = `
      <div class="xp-grid">
        <div><span>label</span><b>${esc(SQ.sensorDisplayText(s, state.dashboard, opts.fallbackLabel))}</b></div>
        <div><span>raw label</span><b>${esc(s.text)}</b></div>
        <div><span>source</span><b>${esc(s.hw || '—')}</b></div>
        <div><span>hardware id</span><b>${esc(s.hwid || '—')}</b></div>
        <div><span>type</span><b>${esc(SQ.displayType(s))}</b></div>
        <div><span>current</span><b class="num">${esc(value)}</b></div>
        <div><span>raw min/max</span><b class="num">${esc(rawMin)} / ${esc(rawMax)}</b></div>
        <div><span>range</span><b class="num">${esc(rangeDetailText(s, rr))}</b></div>
        <div class="xp-id"><span>sensor id</span><code>${esc(s.id)}</code></div>
      </div>
      <div class="xp-actions">
        <label class="alias">alias <input class="alias-input" data-act="alias" data-id="${esc(s.id)}" value="${esc(alias)}" placeholder="${esc(s.text)}"></label>
        <button class="iconbtn" data-act="alias-clear" data-id="${esc(s.id)}" ${alias ? '' : 'disabled'}>Clear alias</button>
        <button class="iconbtn" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(s.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
        <button class="iconbtn" data-act="${isPrimary ? 'primary-remove' : 'primary-add'}" data-id="${esc(s.id)}">${isPrimary ? 'Remove from primary' : 'Show as primary'}</button>
        <button class="iconbtn" data-act="hide" data-id="${esc(s.id)}">Hide</button>
        ${styleSel}
        <label class="ov">max <input class="ov-max" inputmode="decimal" value="${ov ? esc(String(ov.max)) : ''}" placeholder="${rr && rr.source !== 'override' ? esc(String(rr.hi)) : 'max'}"></label>
        <label class="ov">min <input class="ov-min" inputmode="decimal" value="${ov?.min != null ? esc(String(ov.min)) : ''}" placeholder="0"></label>
        <button class="iconbtn" data-act="set-range" data-id="${esc(s.id)}">Set range</button>
        ${ov ? `<button class="iconbtn" data-act="clear-range" data-id="${esc(s.id)}">Clear range</button>` : ''}
        ${moveButtons}
      </div>`;
    return el;
  }
  function cardRange(h) {
    return h.bounded ? { lo: h.bounded[0], hi: h.bounded[1], source: 'band' }
                     : SQ.rangeFor(h.s, {}, state.dashboard);
  }
  function cardEl(h, pinned) {
    const { n, unit } = h.s.raw == null ? { n: '—', unit: '' } : SQ.splitValue(h.s.value);
    const u = unit || h.unit || '';
    const st = h.status;
    const kind = SQ.kindOf(h.s.type);
    const styleVal = state.dashboard.cardStyle[h.s.id];
    const rr = cardRange(h);
    const range = rr ? [rr.lo, rr.hi] : null;
    const ctrl = kind === 'fan' ? SQ.fanControlFor(h.s, state.allSensors) : null;
    const gaugeRange = SQ.gaugeRangeFor(rr, h.s, ctrl);
    const fx = SQ.cardStyleFor(styleVal, !!gaugeRange && h.s.raw != null, state.dashboard.graphsEnabled);
    const arcTarget = SQ.arcTargetFor(h.s, ctrl, gaugeRange, fx.arc);
    const arc = arcTarget == null ? '' : arcSVG(h.s.id, arcTarget);
    const isHealth = (h.s.type === 'Temperature' && !SQ.isLimitSensor(h.s)) ||
                     (h.s.type === 'Level' && (h.s.text || '').toLowerCase().includes('life'));
    const chip = isHealth && (st === 'ok' || st === 'warn' || st === 'crit')
      ? `<span class="chip-state g-${st}">${STGLYPH[st]} ${STLABEL[st]}</span>` : '';
    const trend = SQ.trendFor(h.s.id, kind);
    let trendHtml = '<span class="trend"></span>';
    if (trend) {
      const trendArrow = trend.direction === 'rising' ? '&#8599;' : '&#8600;';
      const trendRate = `${Math.abs(trend.rate).toFixed(Math.abs(trend.rate) >= 10 ? 0 : 2)} ${esc(trend.rateUnit)}`;
      // Arc cards have a narrow readout (the arc takes ~half the card), so range + full rate can
      // clip: show the direction arrow only, with the rate in a tooltip. Number-only cards have the
      // full card width, so keep the inline rate there.
      trendHtml = fx.arc
        ? `<span class="trend" title="${trendArrow} ${trendRate}">${trendArrow}</span>`
        : `<span class="trend">${trendArrow} ${trendRate}</span>`;
    }
    const ceilLabel = fx.arc && !ctrl ? SQ.rangeLabelFor(gaugeRange, h.s) : null;
    const ceil = ceilLabel ? `<span class="ceil">/ ${esc(ceilLabel)}</span>` : '';
    const cell = document.createElement('div');
    cell.className = `cell s-${st}${pinned ? ' pinned' : ''}${fx.spark ? ' graph-on' : ''}`;
    cell.style.setProperty('--tc', `var(--t-${kind})`);
    cell.dataset.key = h.s.id;
    cell.dataset.sid = h.s.id;
    cell.tabIndex = 0;
    cell.setAttribute('aria-expanded', state.expanded.has('c:' + h.s.id) ? 'true' : 'false');
    const source = (h.s.hw || '').split(' ').slice(0, 3).join(' ');
    const label = SQ.sensorDisplayText(h.s, state.dashboard, h.label);
    const showHide = !pinned;
    const ctlHtml = `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
      ctlCluster(h.s.id, label, { hide: showHide });
    cell.innerHTML =
      `<div class="chead"><div class="ktext">
         <div class="k"><span class="name">${esc(label)}</span>${chip}</div>
         <div class="k2"><span class="src">${esc(source)}</span>${tIcon(kind)}</div>
       </div><div class="cell-ctl">${ctlHtml}</div></div>
       <div class="body">${arc}<div class="readout">
         <div class="big"><span class="v">${esc(n)}</span><span class="u">${esc(u)}</span>${ceil}${ctrl ? `<span class="vcmd" title="commanded ${esc(ctrl.value)}">· ${esc(ctrl.value)}</span>` : ''}</div>
         <div class="meta">${rangeMarkup(h.s) || '<div class="range"></div>'}${trendHtml}</div>
       </div></div>${fx.spark ? sparkAreaSVG(h.s, range) : ''}`;
    if (state.expanded.has('c:' + h.s.id)) cell.classList.add('expanded');
    return cell;
  }
  function placeCardOverlay(grid, cards) {
    const old = grid.querySelector('.xp-overlay');
    if (old) old.remove();
    const h = cards.find(c => state.expanded.has('c:' + c.s.id));
    if (!h) return;
    const cell = grid.querySelector(`.cell[data-sid="${CSS.escape(h.s.id)}"]`);
    if (!cell) return;
    const ov = xpEl(h.s, cardRange(h), { cls: 'xp xp-overlay', style: true, movable: true, fallbackLabel: h.label });
    if (state.xpEnter === 'c:' + h.s.id) ov.classList.add('enter');
    cell.after(ov);
    ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
  }
  function renderPinnedCards(sensors, limits) {
    const cards = SQ.resolvePinnedCards(sensors, state.dashboard, limits);
    const sec = $('#pinnedsec'), grid = $('#pinned');
    sec.style.display = cards.length ? '' : 'none';
    syncKeyedRegion(grid, cards, h => h.s.id, standardCardSignature, h => cardEl(h, true));
    placeCardOverlay(grid, cards);
    $('#pinnedtag').textContent = `${cards.length} pinned`;
  }
  function renderPFD(sensors, limits) {
    const custom = state.dashboard.primaryCardsCustomized;
    const base = custom ? SQ.resolvePrimaryCards(sensors, state.dashboard, limits)
                        : SQ.pickHero(sensors, limits);
    const H = SQ.applyOrder(
      base.map((h, index) => Object.assign(h, { index })),
      state.dashboard.cardOrder, h => h.s.id);
    const pfd = $('#pfd');
    syncKeyedRegion(pfd, H, h => h.s.id, standardCardSignature, h => cardEl(h, false));
    placeCardOverlay(pfd, H);
    const reset = $('#pfdReset');
    if (reset) reset.style.display = custom ? '' : 'none';
    $('#pfdtag').textContent = custom ? `${H.length} selected` : `${H.length} auto-selected`;
  }
  function renderPlacard(alarm) {
    const flagged = alarm.filter(s => s.status === 'warn' || s.status === 'crit')
      .sort((a, b) => SQ.RANK[b.status] - SQ.RANK[a.status]);
    const ps = $('#placardsec');
    if (!flagged.length) { ps.style.display = 'none'; ps.innerHTML = ''; return; }
    const crit = flagged.some(s => s.status === 'crit');
    ps.style.display = '';
    ps.innerHTML = `<div class="placard ${crit ? 'crit' : ''}">
      <div class="placard-head"><span class="lamp ${crit ? 's-crit' : 's-warn'}"></span>
        <h3>${crit ? 'Thermal Alert' : 'Thermal Watch'}</h3>
        <span class="tag">${flagged.length} over band</span></div>
      <ul>${flagged.map(s => `<li><span class="glyph-stat g-${s.status}">${STGLYPH[s.status]}</span>
        <span class="who">${esc(s.text)} <small>${esc(s.hw)}</small></span>
        <span class="val g-${s.status}">${esc(s.value)}</span></li>`).join('')}</ul></div>`;
  }
  function studioCardsFor(sensors, limits) {
    const custom = state.dashboard.primaryCardsCustomized;
    const base = custom ? SQ.resolvePrimaryCards(sensors, state.dashboard, limits)
                        : SQ.pickHero(sensors, limits);
    return SQ.applyOrder(
      base.map((h, index) => Object.assign(h, { index })),
      state.dashboard.cardOrder, h => h.s.id)
      .slice(0, state.dashboard.studioFocusCount);
  }
  function studioFocusCardEl(h) {
    const { n, unit } = h.s.raw == null ? { n: '—', unit: '' } : SQ.splitValue(h.s.value);
    const kind = SQ.kindOf(h.s.type);
    const status = h.status || 'info';
    const range = cardRange(h);
    const trend = SQ.trendFor(h.s.id, kind);
    const label = SQ.sensorDisplayText(h.s, state.dashboard, h.label);
    const article = document.createElement('article');
    article.className = `studio-focus-card s-${status}`;
    article.style.setProperty('--tc', `var(--studio-${kind})`);
    article.dataset.sid = h.s.id;
    article.innerHTML = `<div class="studio-card-head">
        <div><span>${esc(h.s.hw || 'Sensor')}</span><h3>${esc(label)}</h3></div>
        <div class="studio-card-actions"><span class="studio-status g-${status}">${STLABEL[status]}</span>
        <button class="studio-star" data-studio-act="primary-remove" data-id="${esc(h.s.id)}"
          aria-label="Remove ${esc(label)} from focus" title="Remove from focus">&#9733;</button></div>
      </div>
      <div class="studio-card-reading"><strong>${esc(n)}</strong><span>${esc(unit || h.unit || '')}</span></div>
      <div class="studio-card-meta"><span>${esc(rangeDetailText(h.s, range))}</span>
        <span>${h.s.raw == null ? 'unavailable' : trend ? `${trend.direction === 'rising' ? '&#8599;' : '&#8600;'} ${esc(Math.abs(trend.rate).toFixed(Math.abs(trend.rate) >= 10 ? 0 : 2))} ${esc(trend.rateUnit)}` : 'live'}</span></div>
      ${state.dashboard.studioShowSparklines ? sparkAreaSVG(h.s, range ? [range.lo, range.hi] : null) : ''}`;
    return article;
  }
  function studioSensorPriority(s) {
    const order = ['Temperature','TemperatureRate','Load','Power','Fan','Control','Clock','Throughput','Data','SmallData','Level'];
    const typeRank = order.indexOf(s.type);
    return (SQ.RANK[s.status] + 1) * 100 - (typeRank < 0 ? 99 : typeRank);
  }
  function studioSystemEl(item) {
    let worst = 'info';
    item.ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
    const cls = item.ss[0]?.cls || 'other';
    const useful = item.ss.filter(s => !SQ.isLimitSensor(s));
    const sorted = (useful.length ? useful : item.ss).slice()
      .sort((a, b) => studioSensorPriority(b) - studioSensorPriority(a));
    const readings = [], seenTypes = new Set();
    sorted.forEach(s => {
      const type = SQ.displayType(s);
      if (readings.length < 4 && !seenTypes.has(type)) { readings.push(s); seenTypes.add(type); }
    });
    sorted.forEach(s => { if (readings.length < 4 && !readings.includes(s)) readings.push(s); });
    const article = document.createElement('article');
    article.className = `studio-system-card s-${worst}`;
    article.innerHTML = `<div class="studio-system-head"><div><span>${esc(CLASSLABEL[cls] || 'SYSTEM')}</span>
        <h3>${esc(item.label || item.hw)}</h3></div><span class="lamp s-${worst}" title="${STLABEL[worst]}"></span></div>
      <div class="studio-readings">${readings.map(s => `<div><span>${esc(SQ.sensorDisplayText(s, state.dashboard, s.text))}</span>
        <strong class="g-${s.status}">${esc(s.raw == null ? '—' : (s.value ?? '—'))}</strong></div>`).join('')}</div>`;
    return article;
  }
  function sensorRenderSignature(s) {
    return [s.id, s.hwid, s.cls, s.type, s.raw, s.rawMin, s.rawMax, s.value, s.min, s.max, s.status, s.text, s.hw,
      SQ.sensorAlias(state.dashboard, s.id)].join('\u001f');
  }
  function standardCardSignature(h) {
    const s = h.s;
    const kind = SQ.kindOf(s.type);
    const ctrl = kind === 'fan' ? SQ.fanControlFor(s, state.allSensors) : null;
    const range = cardRange(h);
    const gaugeRange = SQ.gaugeRangeFor(range, s, ctrl);
    const style = state.dashboard.cardStyle[s.id] || 'auto';
    const effects = SQ.cardStyleFor(style, !!gaugeRange && s.raw != null, state.dashboard.graphsEnabled);
    const trend = SQ.trendFor(s.id, kind);
    const arcTarget = SQ.arcTargetFor(s, ctrl, gaugeRange, effects.arc);
    return JSON.stringify([sensorRenderSignature(s), h.label, h.unit, h.bounded, h.status,
      style, state.dashboard.graphsEnabled, SQ.isPinned(state.dashboard, s.id), state.primaryIds.has(s.id),
      state.expanded.has('c:' + s.id), state.dashboard.rangeOverrides[s.id] || null,
      SQ.rangeRenderSignature(range), SQ.rangeRenderSignature(gaugeRange), effects.arc, effects.spark,
      arcTarget, arcTarget == null ? null : SQ.smoothedFraction(s.id),
      ctrl ? [ctrl.raw, ctrl.value] : null,
      trend ? [trend.direction, trend.rate, trend.rateUnit] : null,
      effects.spark ? SQ.historyFor(s.id).map(p => [p.t, p.raw]) : null]);
  }
  function panelSignature(item) {
    const collapsed = SQ.isPanelCollapsed(state.dashboard, item.key, item.hw, item.collapsed);
    const rowOrderKeys = Object.keys(state.dashboard.rowOrder)
      .filter(k => k.startsWith(item.key + '|'))
      .map(k => k + ':' + state.dashboard.rowOrder[k].join(','));
    return JSON.stringify([item.key, item.label, item.net, collapsed,
      item.ss.map(s => {
        const expanded = state.expanded.has('r:' + s.id);
        const control = s.type === 'Fan' ? SQ.fanControlFor(s, state.allSensors) : null;
        return [sensorRenderSignature(s), expanded, state.dashboard.rangeOverrides[s.id] || null,
          SQ.isPinned(state.dashboard, s.id), state.primaryIds.has(s.id),
          expanded ? SQ.rangeRenderSignature(SQ.rangeFor(s, state.limits, state.dashboard)) : null,
          control ? [control.raw, control.value] : null];
      }),
      rowOrderKeys]);
  }
  function syncKeyedRegion(host, items, keyFor, signatureFor, createNode) {
    const current = new Map([...host.children]
      .filter(node => node.dataset.renderKey)
      .map(node => [node.dataset.renderKey, node]));
    let changed = false;
    const wanted = items.map(item => {
      const key = String(keyFor(item));
      const signature = signatureFor(item);
      let node = current.get(key);
      if (!node || stableNodeSignatures.get(node) !== signature) {
        node = createNode(item);
        node.dataset.renderKey = key;
        stableNodeSignatures.set(node, signature);
        changed = true;
      }
      return node;
    });
    const wantedSet = new Set(wanted);
    [...host.children].forEach(node => {
      if (!wantedSet.has(node)) { node.remove(); changed = true; }
    });
    wanted.forEach((node, index) => {
      if (host.children[index] !== node) {
        host.insertBefore(node, host.children[index] || null);
        changed = true;
      }
    });
    return changed;
  }
  function setStableHtml(host, html) {
    if (stableHtmlSignatures.get(host) === html) return;
    host.innerHTML = html;
    stableHtmlSignatures.set(host, html);
  }
  function renderStudio(host, sensors, limits, alarm, version) {
    const activeAction = document.activeElement?.closest?.('#studioFocus [data-studio-act]');
    const activeActionId = activeAction?.dataset.id || null;
    const activeActionIndex = activeAction
      ? [...$('#studioFocus').querySelectorAll('[data-studio-act]')].indexOf(activeAction)
      : -1;
    const focus = studioCardsFor(sensors, limits);
    const focusHost = $('#studioFocus');
    const focusItems = focus.length ? focus : [{empty:true, s:{id:'__empty'}}];
    const focusChanged = syncKeyedRegion(focusHost, focusItems, h => h.s.id, h => h.empty ? 'empty' :
      JSON.stringify([state.dashboard.studioShowSparklines, sensorRenderSignature(h.s),
        h.label, h.unit, h.bounded, SQ.historyFor(h.s.id).map(p => [p.t, p.raw])]), h => {
        if (!h.empty) return studioFocusCardEl(h);
        const empty = document.createElement('div');
        empty.className = 'studio-empty';
        empty.innerHTML = '<b>No focus sensors selected</b><span>Open Sensors and choose Make primary to build this deck.</span>';
        return empty;
      });
    if (activeActionId && focusChanged) {
      const nextAction = focusHost.querySelector(`[data-studio-act][data-id="${CSS.escape(activeActionId)}"]`);
      const remainingActions = [...focusHost.querySelectorAll('[data-studio-act]')];
      const fallbackAction = remainingActions[Math.min(activeActionIndex, remainingActions.length - 1)]
        || $('#studioOpenSensors');
      (nextAction || fallbackAction).focus({ preventScroll: true });
    }

    const flagged = alarm.filter(s => s.status === 'warn' || s.status === 'crit')
      .sort((a, b) => SQ.RANK[b.status] - SQ.RANK[a.status]);
    const crit = flagged.filter(s => s.status === 'crit').length;
    const warn = flagged.length - crit;
    const unavailable = sensors.filter(s => s.raw == null).length;
    $('#studioHost').textContent = host;
    $('#studioSummary').textContent = `${sensors.length} visible readings · ${unavailable} unavailable · ${focus.length} focus cards · read-only telemetry`;
    setStableHtml($('#studioHealth'), `${flagged.length
      ? `<span class="studio-health-pill ${crit ? 'crit' : 'warn'}"><b>${flagged.length}</b> needs attention</span>`
      : unavailable
        ? `<span class="studio-health-pill off"><b>Telemetry partial</b> ${unavailable} unavailable</span>`
        : '<span class="studio-health-pill ok"><b>All clear</b> monitored bands nominal</span>'}
      <span class="studio-health-pill"><b>${crit}</b> critical</span>
      <span class="studio-health-pill"><b>${warn}</b> watch</span>
      <span class="studio-health-pill"><b>${unavailable}</b> unavailable</span>
      <span class="studio-health-pill"><b>${state.dashboard.pinnedCards.length}</b> pinned</span>`);

    const alerts = $('#studioAlerts');
    alerts.style.display = flagged.length ? '' : 'none';
    setStableHtml(alerts, flagged.length ? `<div><span>Attention</span><b>${crit ? 'Critical telemetry' : 'Watch telemetry'}</b></div>
      <ul>${flagged.slice(0, 4).map(s => `<li><span>${esc(SQ.sensorDisplayText(s, state.dashboard, s.text))}</span>
        <strong class="g-${s.status}">${esc(s.value ?? '—')}</strong></li>`).join('')}</ul>` : '');
    const alertSignature = flagged.map(s => `${s.id}:${s.status}`).join('|');
    if (alertSignature !== state.studioAlertSignature) {
      const priorSignature = state.studioAlertSignature;
      state.studioAlertSignature = alertSignature;
      if (priorSignature !== undefined) {
        const alertNames = flagged.slice(0, 3)
          .map(s => SQ.sensorDisplayText(s, state.dashboard, s.text)).join(', ');
        $('#studioAlertStatus').textContent = flagged.length
          ? `Studio telemetry alert: ${crit} critical, ${warn} watch. ${alertNames}.`
          : 'Studio telemetry alerts cleared.';
      }
    }

    const hardwareItems = SQ.applyOrder(state.panelItems.filter(item => !item.net), state.dashboard.panelOrder, item => item.key);
    const systemsSection = $('#studioSystemsSection');
    systemsSection.hidden = !state.dashboard.studioShowSystems;
    $('#studioSystemsCount').textContent = `${hardwareItems.length} systems`;
    const systems = $('#studioSystems');
    const visibleHardwareItems = state.dashboard.studioShowSystems ? hardwareItems : [];
    syncKeyedRegion(systems, visibleHardwareItems, item => item.key,
      item => JSON.stringify([item.label, item.ss.map(sensorRenderSignature)]), studioSystemEl);

    const networkItems = state.panelItems.filter(item => item.net);
    const networkSection = $('#studioNetworkSection');
    networkSection.hidden = !state.dashboard.studioShowNetwork || !networkItems.length;
    $('#studioNetworkCount').textContent = `${networkItems.length} active`;
    const network = $('#studioNetwork');
    const visibleNetworkItems = state.dashboard.studioShowNetwork ? networkItems : [];
    syncKeyedRegion(network, visibleNetworkItems, item => item.key,
      item => JSON.stringify([item.label, item.ss.map(sensorRenderSignature)]), studioSystemEl);
    $('#studioFootLeft').textContent = `LibreHardwareMonitor ${version} · ${host} · ${state.rate}s poll`;
  }
  function activeWorkspaceProfile() {
    if (!Workspace || !state.workspace || !Array.isArray(state.workspace.profiles)) return null;
    return state.workspace.profiles.find(profile => profile.id === state.workspace.activeProfileId)
      || state.workspace.profiles[0] || null;
  }
  function workspacePanel(profile, panelId) {
    return profile && profile.panels.find(panel => panel.id === panelId) || null;
  }
  function paintWorkspaceStatus(message, tone) {
    const status = $('#workspaceStatus');
    if (!status) return;
    const compactStatus = $('#workspaceCompactStatus');
    const nextTone = ['ok','warn','crit','error','info','off'].includes(tone) ? tone : 'info';
    if (status.textContent === message && status.className === 'is-' + nextTone
      && compactStatus?.textContent === message && compactStatus?.dataset.tone === nextTone) return;
    status.textContent = message;
    status.className = 'is-' + nextTone;
    if (compactStatus) {
      compactStatus.textContent = message;
      compactStatus.dataset.tone = nextTone;
    }
    const live = status.closest('.workspace-live');
    if (live) {
      ['ok','warn','crit','error','info','off'].forEach(value => live.classList.remove('is-' + value));
      live.classList.add('is-' + nextTone);
    }
  }
  function announceWorkspace(message, tone, durationMs) {
    state.workspaceNotice = {
      message,
      tone: tone || 'info',
      until: Date.now() + (Number(durationMs) || 6000)
    };
    paintWorkspaceStatus(message, tone);
  }
  function captureWorkspaceFocus() {
    const active = document.activeElement;
    if (!active || !active.closest || !active.closest('#workspaceView')) return null;
    const keys = ['workspaceAct','panelId','sensorId','workspaceSensor','workspaceField'];
    const data = {};
    keys.forEach(key => {
      if (typeof active.dataset?.[key] === 'string') data[key] = active.dataset[key];
    });
    return {id:active.id || '', data};
  }
  function restoreWorkspaceFocus(token) {
    if (!token) return;
    let target = token.id ? document.getElementById(token.id) : null;
    const keys = Object.keys(token.data || {});
    const candidates = [...document.querySelectorAll('#workspaceView [data-workspace-act], #workspaceView [data-workspace-sensor], #workspaceView [data-workspace-field]')];
    const findData = data => {
      const wanted = Object.keys(data);
      return candidates.find(candidate => wanted.every(key => candidate.dataset?.[key] === data[key]));
    };
    if (!target && keys.length) target = findData(token.data);
    if (target?.disabled && token.data?.workspaceAct) {
      const opposite = {
        'panel-up':'panel-down', 'panel-down':'panel-up',
        'sensor-up':'sensor-down', 'sensor-down':'sensor-up'
      }[token.data.workspaceAct];
      if (opposite) target = findData({...token.data, workspaceAct:opposite}) || target;
    }
    if (target?.disabled && token.data?.sensorId) {
      target = candidates.find(candidate => candidate.dataset?.workspaceSensor === token.data.sensorId) || target;
    }
    if (target?.disabled && token.data?.panelId) {
      target = findData({workspaceAct:'manage', panelId:token.data.panelId}) || target;
    }
    if (target?.disabled && token.id) {
      const fallbackId = {
        workspaceAddPanel:'workspacePanelTitle',
        workspaceProfileNew:'workspaceProfileSelect',
        workspaceProfileDuplicate:'workspaceProfileSelect',
        workspaceProfileDelete:'workspaceProfileSelect'
      }[token.id];
      if (fallbackId) target = document.getElementById(fallbackId) || target;
    }
    if (!target || target.disabled || typeof target.focus !== 'function') return;
    try { target.focus({preventScroll:true}); } catch { target.focus(); }
  }
  function commitWorkspace(next, message, tone) {
    if (!Workspace) return;
    const focus = captureWorkspaceFocus();
    const result = Workspace.saveResult(storage, next);
    state.workspace = result.state;
    state.workspaceSensorSignature = null;
    if (!result.ok) {
      state.workspaceNotice = {
        message: 'Browser storage is unavailable; this change is temporary for this page.',
        tone: 'warn',
        until: Date.now() + 9000
      };
    } else if (message) {
      state.workspaceNotice = {
        message,
        tone: tone || 'ok',
        until: Date.now() + 6000
      };
    }
    paintWorkspaceControls();
    rerender();
    restoreWorkspaceFocus(focus);
  }
  function paintWorkspaceControls(paintFallbackStatus = true) {
    const profileSelect = $('#workspaceProfileSelect');
    const profileName = $('#workspaceProfileName');
    const targetSelect = $('#workspaceSensorTarget');
    const interactive = [
      '#workspaceProfileNew','#workspaceProfileDuplicate','#workspaceProfileDelete',
      '#workspaceImport','#workspaceExport','#workspaceReset','#workspacePanelTitle',
      '#workspacePanelKind','#workspaceAddPanel','#workspaceSensorSearch','#workspaceChooseSensors'
    ].map($).filter(Boolean);
    if (!Workspace || !state.workspace) {
      interactive.forEach(control => { control.disabled = true; });
      if (profileSelect) profileSelect.disabled = true;
      if (profileName) profileName.disabled = true;
      if (targetSelect) targetSelect.disabled = true;
      paintWorkspaceStatus('Workspace model unavailable. Standard and Studio remain usable.', 'error');
      return;
    }

    const profile = activeWorkspaceProfile();
    const profiles = state.workspace.profiles;
    $('#workspaceCompactProfile').textContent = profile ? profile.name : 'Workspace';
    setStableHtml(profileSelect, profiles.map(item =>
      '<option value="' + esc(item.id) + '">' + esc(item.name) + '</option>').join(''));
    profileSelect.disabled = !profiles.length;
    if (profile) profileSelect.value = profile.id;
    profileName.disabled = !profile;
    if (profile && document.activeElement !== profileName) profileName.value = profile.name;
    $('#workspaceProfileDuplicate').disabled = !profile || profiles.length >= Workspace.LIMITS.profiles;
    $('#workspaceProfileDelete').disabled = !profile || profiles.length <= 1;
    $('#workspaceExport').disabled = !profile || (!state.lastData && profile.panels.some(panel => panel.preset));
    $('#workspaceProfileNew').disabled = profiles.length >= Workspace.LIMITS.profiles;

    const panels = profile ? profile.panels : [];
    if (!panels.some(panel => panel.id === state.workspaceSensorTargetId))
      state.workspaceSensorTargetId = panels[0]?.id || null;
    setStableHtml(targetSelect, panels.length
      ? panels.map(panel => '<option value="' + esc(panel.id) + '">' + esc(panel.title) + '</option>').join('')
      : '<option value="">Choose a panel</option>');
    targetSelect.disabled = !panels.length;
    targetSelect.value = state.workspaceSensorTargetId || '';
    $('#workspaceSensorSearch').disabled = !panels.length;
    const chooseSensors = $('#workspaceChooseSensors');
    chooseSensors.disabled = !panels.length;
    chooseSensors.title = panels.length ? 'Choose sensors for the first panel'
      : 'Open Edit layout and add a panel first';
    chooseSensors.setAttribute('aria-expanded', String($('#workspaceSensorManager').open));
    $('#workspaceAddPanel').disabled = !profile || panels.length >= Workspace.LIMITS.panelsPerProfile;

    if (state.workspaceNotice && state.workspaceNotice.until > Date.now())
      paintWorkspaceStatus(state.workspaceNotice.message, state.workspaceNotice.tone);
    else if (paintFallbackStatus) {
      state.workspaceNotice = null;
      paintWorkspaceStatus(profile
        ? profile.name + ' ready · ' + panels.length + ' panels · read-only'
        : 'No profile is available.', profile ? 'info' : 'off');
    }
    if ($('#workspaceSensorManager')?.open) renderWorkspaceSensorList();
  }
  function workspaceStatusClass(sensor) {
    const status = sensor?.status || 'off';
    return 'is-' + (['ok','warn','crit','info','off'].includes(status) ? status : 'off');
  }
  function workspaceSensorHeadMarkup(sensor) {
    const label = SQ.sensorDisplayText(sensor, state.dashboard, sensor.text);
    const raw = label !== sensor.text ? ' · raw ' + sensor.text : '';
    const statusLabel = STLABEL[sensor.status] || 'Unavailable';
    return '<div class="workspace-sensor-head"><div class="workspace-sensor-copy"><strong>' + esc(label) + '</strong>' +
      '<span>' + esc(sensor.hw || 'Sensor') + ' · ' + esc(sensor.type || 'Unknown') + esc(raw) +
      '</span></div><div class="workspace-sensor-state"><span class="lamp s-' + esc(sensor.status || 'off') +
      '" aria-hidden="true"></span><span>' + esc(statusLabel) + '</span></div></div>';
  }
  function workspaceReadingMarkup(sensor) {
    if (sensor.raw == null)
      return '<div class="workspace-reading"><strong>—</strong><span>unavailable</span></div>';
    const split = SQ.splitValue(sensor.value ?? String(sensor.raw));
    return '<div class="workspace-reading"><strong>' + esc(split.n) + '</strong><span>' +
      esc(split.unit || '') + '</span></div>';
  }
  function workspaceRangeMarkup(sensor, limits) {
    const range = SQ.rangeFor(sensor, limits, state.dashboard);
    return '<span>' + esc(range ? SQ.graphScaleText(range, sensor).replace(/^Scale /, '')
      : SQ.rangeSourceLabel(null)) + '</span>';
  }
  function workspaceMissingSummary(ids) {
    if (!ids.length) return '';
    return '<div class="workspace-missing"><strong>' + ids.length + ' saved sensor' +
      (ids.length === 1 ? '' : 's') + ' unavailable</strong><span>' +
      ids.map(id => '<code>' + esc(id) + '</code>').join(' · ') +
      '</span><span>The SensorIds remain in this profile and reconnect when the hardware returns.</span></div>';
  }
  function workspaceCardsMarkup(sensors, limits) {
    return '<div class="workspace-card-grid">' + sensors.map(sensor => {
      const range = SQ.rangeFor(sensor, limits, state.dashboard);
      const history = SQ.historyFor(sensor.id);
      return '<article class="workspace-sensor-card ' + workspaceStatusClass(sensor) + '">' +
        workspaceSensorHeadMarkup(sensor) + workspaceReadingMarkup(sensor) +
        '<div class="workspace-sensor-meta">' + workspaceRangeMarkup(sensor, limits) + '</div>' +
        (history.length > 1 ? sparkAreaSVG(sensor, range ? [range.lo, range.hi] : null) : '') +
        '</article>';
    }).join('') + '</div>';
  }
  function workspaceTableMarkup(sensors) {
    return '<div class="workspace-table-wrap"><table class="workspace-table"><thead><tr>' +
      '<th>Sensor</th><th>Hardware</th><th>Type</th><th>Minimum</th><th>Maximum</th><th>Current</th>' +
      '</tr></thead><tbody>' + sensors.map(sensor => {
        const label = SQ.sensorDisplayText(sensor, state.dashboard, sensor.text);
        return '<tr class="' + workspaceStatusClass(sensor) + '"><td><strong>' + esc(label) +
          '</strong><br><code>' + esc(sensor.id) + '</code></td><td>' + esc(sensor.hw || '—') +
          '</td><td>' + esc(sensor.type || '—') + '</td><td>' + esc(sensor.min == null || sensor.min === '' ? '—' : sensor.min) +
          '</td><td>' + esc(sensor.max == null || sensor.max === '' ? '—' : sensor.max) + '</td><td>' + workspaceReadingMarkup(sensor) +
          '</td></tr>';
      }).join('') + '</tbody></table></div>';
  }
  function workspaceGraphsMarkup(sensors, limits) {
    return '<div class="workspace-trend-grid">' + sensors.map(sensor => {
      const range = SQ.rangeFor(sensor, limits, state.dashboard);
      const history = SQ.historyFor(sensor.id);
      const scale = SQ.graphScaleFor(range, history);
      const chart = history.length > 1
        ? sparkAreaSVG(sensor, scale ? [scale.lo, scale.hi] : null)
        : '<div class="workspace-trend-chart workspace-empty"><span>Collecting live history…</span></div>';
      return '<article class="workspace-trend ' + workspaceStatusClass(sensor) + '">' +
        workspaceSensorHeadMarkup(sensor) + workspaceReadingMarkup(sensor) +
        '<div class="workspace-sensor-meta"><span>' + esc(SQ.graphScaleText(scale, sensor)) + '</span></div>' +
        chart + '</article>';
    }).join('') + '</div>';
  }
  function workspacePanelEl(profile, panel, index) {
    const article = document.createElement('article');
    const titleId = 'workspace-panel-title-' + profile.id + '-' + panel.id;
    article.className = 'workspace-panel workspace-panel-' + panel.type;
    article.dataset.panelId = panel.id;
    article.setAttribute('aria-labelledby', titleId);
    article.innerHTML = '<div class="workspace-panel-head">' +
      '<h2 class="workspace-panel-title workspace-panel-title-display" id="' + esc(titleId) + '">' +
      esc(panel.title) + '</h2>' +
      '<input id="workspace-panel-title-input-' + esc(profile.id + '-' + panel.id) + '" name="workspacePanelTitle"' +
      ' class="workspace-panel-title" data-workspace-field="title" data-panel-id="' + esc(panel.id) +
      '" maxlength="80" value="' + esc(panel.title) + '" aria-label="Panel title">' +
      '<div class="workspace-panel-actions">' +
      '<select id="workspace-panel-type-' + esc(profile.id + '-' + panel.id) + '" name="workspacePanelType"' +
      ' data-workspace-field="type" data-panel-id="' + esc(panel.id) +
      '" aria-label="Presentation for ' + esc(panel.title) + '">' +
      Workspace.PANEL_TYPES.map(type => '<option value="' + type + '"' +
        (panel.type === type ? ' selected' : '') + '>' +
        (type === 'card' ? 'Cards' : type === 'table' ? 'Table' : 'Graphs') + '</option>').join('') +
      '</select><button class="iconbtn" data-workspace-act="manage" data-panel-id="' + esc(panel.id) +
      '">Sensors</button><button class="iconbtn" data-workspace-act="panel-up" data-panel-id="' +
      esc(panel.id) + '" aria-label="Move ' + esc(panel.title) + ' earlier"' +
      (index === 0 ? ' disabled' : '') + '>▲</button><button class="iconbtn" data-workspace-act="panel-down" data-panel-id="' +
      esc(panel.id) + '" aria-label="Move ' + esc(panel.title) + ' later"' +
      (index === profile.panels.length - 1 ? ' disabled' : '') +
      '>▼</button><button class="iconbtn workspace-danger" data-workspace-act="panel-remove" data-panel-id="' +
      esc(panel.id) + '" aria-label="Remove ' + esc(panel.title) + '">Remove</button></div></div>' +
      '<div class="workspace-panel-body"></div>';
    return article;
  }
  function paintWorkspacePanelBody(node, panel, sensors, limits) {
    const result = Workspace.resolvePanel(panel, sensors);
    const missing = workspaceMissingSummary(result.missingSensorIds);
    let content = '';
    if (!result.sensors.length) {
      const preset = result.source === 'preset';
      content = '<div class="workspace-empty"><strong>' +
        (result.missingSensorIds.length ? 'All selected sensors are unavailable'
          : preset ? 'No live sensors match this preset' : 'No sensors selected') +
        '</strong><span>' + (result.missingSensorIds.length
          ? 'The saved SensorIds are retained above and will reconnect automatically.'
          : preset
          ? 'The host-neutral preset will populate when matching hardware is available.'
          : 'Open Sensor manager to choose exact SensorIds for this panel.') + '</span></div>';
    } else if (panel.type === 'card') content = workspaceCardsMarkup(result.sensors, limits);
    else if (panel.type === 'graph') content = workspaceGraphsMarkup(result.sensors, limits);
    else content = workspaceTableMarkup(result.sensors);
    setStableHtml(node.querySelector('.workspace-panel-body'), missing + content);
  }
  function renderWorkspace(host, sensors, limits, version) {
    paintWorkspaceControls(false);
    const panelsHost = $('#workspacePanels');
    if (!Workspace || !state.workspace) {
      setStableHtml(panelsHost, '<div class="workspace-error"><strong>Workspace unavailable</strong>' +
        '<span>The profile model did not load. Standard and Studio are unaffected.</span></div>');
      return;
    }
    const profile = activeWorkspaceProfile();
    if (!profile) {
      setStableHtml(panelsHost, '<div class="workspace-empty"><strong>No profile available</strong>' +
        '<span>Create or import a profile to begin.</span></div>');
      return;
    }

    if (!profile.panels.length) {
      setStableHtml(panelsHost, '<div class="workspace-empty"><strong>This profile has no panels</strong>' +
        '<span>Add a card, table, or graph panel above.</span></div>');
    } else {
      stableHtmlSignatures.delete(panelsHost);
      syncKeyedRegion(panelsHost, profile.panels,
        panel => profile.id + ':' + panel.id,
        panel => JSON.stringify([
          profile.id, panel.id, panel.title, panel.type, panel.preset, panel.sensorIds,
          profile.panels.indexOf(panel), profile.panels.length
        ]),
        panel => workspacePanelEl(profile, panel, profile.panels.indexOf(panel)));
      const nodes = new Map([...panelsHost.children].map(node => [node.dataset.panelId, node]));
      profile.panels.forEach(panel => {
        const node = nodes.get(panel.id);
        if (node) paintWorkspacePanelBody(node, panel, sensors, limits);
      });
    }

    renderWorkspaceSensorList();
    const selectedCount = profile.panels.reduce((count, panel) =>
      count + Workspace.resolvePanel(panel, sensors).sensorIds.length, 0);
    const presetCount = profile.panels.filter(panel => panel.preset).length;
    $('#workspaceFootLeft').textContent = `LibreHardwareMonitor ${version} · ${host} · ${state.rate}s poll · ${selectedCount} panel entries`;
    if (!state.workspaceNotice || state.workspaceNotice.until <= Date.now()) {
      state.workspaceNotice = null;
      paintWorkspaceStatus(`${profile.name} · ${profile.panels.length} panels · ${selectedCount} entries${presetCount ? ` · ${presetCount} adaptive presets` : ''} · ${sensors.length} live sensors`,
        sensors.length ? 'ok' : 'off');
    }
  }
  function renderWorkspaceSensorList() {
    const manager = $('#workspaceSensorManager');
    const list = $('#workspaceSensorList');
    if (!manager || !list || !manager.open || !Workspace || !state.workspace) {
      state.workspaceSensorSignature = null;
      return;
    }
    const profile = activeWorkspaceProfile();
    const panel = workspacePanel(profile, state.workspaceSensorTargetId);
    if (!profile || !panel) {
      setStableHtml(list, '<div class="workspace-empty"><strong>Choose a target panel</strong>' +
        '<span>Available live sensors will be listed here.</span></div>');
      return;
    }

    const resolved = Workspace.resolvePanel(panel, state.allSensors);
    const selected = new Map(resolved.sensorIds.map((id, index) => [id, index]));
    const liveById = new Map();
    state.allSensors.forEach(sensor => { if (sensor.id && !liveById.has(sensor.id)) liveById.set(sensor.id, sensor); });
    const rows = resolved.sensorIds.map(id => ({ id, sensor: liveById.get(id) || null, selected: true }));
    [...liveById.values()]
      .filter(sensor => !selected.has(sensor.id))
      .sort((a, b) => {
        const al = SQ.sensorDisplayText(a, state.dashboard, a.text).toLowerCase();
        const bl = SQ.sensorDisplayText(b, state.dashboard, b.text).toLowerCase();
        return al.localeCompare(bl) || String(a.id).localeCompare(String(b.id));
      })
      .forEach(sensor => rows.push({ id: sensor.id, sensor, selected: false }));
    const filter = (state.workspaceSensorFilter || '').trim().toLowerCase();
    const filtered = rows.filter(row => {
      if (!filter) return true;
      const sensor = row.sensor;
      const text = sensor
        ? [SQ.sensorDisplayText(sensor, state.dashboard, sensor.text), sensor.text, sensor.hw, sensor.type, row.id].join(' ')
        : ['unavailable', row.id].join(' ');
      return text.toLowerCase().includes(filter);
    });
    const signature = JSON.stringify([
      profile.id, panel.id, panel.preset, resolved.sensorIds, filter,
      rows.map(row => row.sensor
        ? [row.id, row.sensor.text, row.sensor.hw, row.sensor.type, SQ.sensorAlias(state.dashboard, row.id)]
        : [row.id, null])
    ]);
    if (signature === state.workspaceSensorSignature) return;
    state.workspaceSensorSignature = signature;
    const full = resolved.sensorIds.length >= Workspace.LIMITS.sensorIdsPerPanel;
    const html = filtered.map(row => {
      const sensor = row.sensor;
      const index = selected.get(row.id);
      const label = sensor ? SQ.sensorDisplayText(sensor, state.dashboard, sensor.text) : 'Unavailable SensorId';
      const detail = sensor ? (sensor.hw || 'Sensor') + ' · ' + (sensor.type || 'Unknown') : 'Saved membership · hardware not present';
      return '<div class="workspace-sensor-choice' + (sensor ? '' : ' workspace-missing') + '">' +
        '<input type="checkbox" data-workspace-sensor="' + esc(row.id) + '"' +
        (row.selected ? ' checked' : '') + (!row.selected && full ? ' disabled' : '') +
        ' aria-label="' + esc((row.selected ? 'Remove ' : 'Add ') + label) + '">' +
        '<div><strong>' + esc(label) + '</strong><span>' + esc(detail) +
        '</span><code>' + esc(row.id) + '</code></div><div class="workspace-sensor-actions">' +
        (row.selected
          ? '<span>#' + (index + 1) + '</span><button class="iconbtn" data-workspace-act="sensor-top" data-sensor-id="' +
            esc(row.id) + '" aria-label="Move ' + esc(label) + ' to top"' + (index === 0 ? ' disabled' : '') +
            '>Top</button><button class="iconbtn" data-workspace-act="sensor-up" data-sensor-id="' +
            esc(row.id) + '" aria-label="Move ' + esc(label) + ' earlier"' + (index === 0 ? ' disabled' : '') +
            '>▲</button><button class="iconbtn" data-workspace-act="sensor-down" data-sensor-id="' + esc(row.id) +
            '" aria-label="Move ' + esc(label) + ' later"' +
            (index === resolved.sensorIds.length - 1 ? ' disabled' : '') + '>▼</button>'
          : '') + '</div></div>';
    }).join('');
    setStableHtml(list, html || '<div class="workspace-empty"><strong>No matching sensors</strong>' +
      '<span>Try another name, hardware label, type, or SensorId.</span></div>');
  }
  function rowEl(s, type, groupKey) {
    const st = s.status, showBar = (s.type === 'Load' || s.type === 'Level' || s.type === 'Control') && s.raw != null;
    const mm = (s.min != null && s.min !== '' && type === 'Temperature')
      ? (parseFloat(s.min) > 0 ? `<span class="mm">${esc(s.min)} / ${esc(s.max)}</span>` : `<span class="mm">peak ${esc(s.max)}</span>`)
      : '';
    const r = document.createElement('div'); r.className = `row ${st}`;
    r.dataset.key = s.id;
    r.dataset.sid = s.id;
    r.dataset.rowGroup = groupKey;
    r.tabIndex = 0;
    r.setAttribute('aria-expanded', state.expanded.has('r:' + s.id) ? 'true' : 'false');
    const fanCtl = s.type === 'Fan' ? SQ.fanControlFor(s, state.allSensors) : null;
    const label = SQ.sensorDisplayText(s, state.dashboard, s.text);
    r.innerHTML = `<button class="grip row-grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>
      <span class="glyph-stat g-${st}" title="${STLABEL[st]}">${st === 'info' ? '' : STGLYPH[st]}</span>
      <span class="rn">${esc(label)}${mm}</span><span class="rv">${esc(s.raw == null ? '—' : (s.value ?? '-'))}${fanCtl ? ` <small class="rvcmd">· ${esc(fanCtl.value)}</small>` : ''}</span>
      ${showBar ? `<div class="bar ${st==='warn'?'warn':st==='crit'?'crit':''}"><i style="width:${Math.max(0,Math.min(100,s.raw))}%"></i></div>` : ''}`;
    const rctl = document.createElement('span');
    rctl.className = 'row-ctl';
    rctl.innerHTML = `<button class="ctl" data-act="row-up" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(label)} up" title="Move up">&#9650;</button>` +
      `<button class="ctl" data-act="row-down" data-id="${esc(s.id)}" data-row-group="${esc(groupKey)}" aria-label="Move ${esc(label)} down" title="Move down">&#9660;</button>` +
      ctlCluster(s.id, label, { hide: true });
    r.appendChild(rctl);
    return r;
  }
  function appendRow(container, s, type, groupKey) {
    container.appendChild(rowEl(s, type, groupKey));
    if (state.expanded.has('r:' + s.id))
      container.appendChild(xpEl(s, SQ.rangeFor(s, state.limits, state.dashboard),
        { cls: 'rowxp', style: false, movable: true, rowGroup: groupKey, fallbackLabel: s.text }));
  }
  function panelEl(item) {
    const { hw, label, ss, collapsed } = item;
    let worst = 'info'; ss.forEach(s => { if (SQ.RANK[s.status] > SQ.RANK[worst]) worst = s.status; });
    const cls = ss[0].cls;
    const startCollapsed = SQ.isPanelCollapsed(state.dashboard, item.key, hw, collapsed);
    const p = document.createElement('div'); p.className = 'panel' + (startCollapsed ? ' collapsed' : '');
    p.dataset.key = item.key;
    const temps = ss.filter(s => s.type === 'Temperature' && s.raw != null && !SQ.isLimitSensor(s)).sort((a,b)=>b.raw-a.raw);
    const head = temps[0] ? temps[0].value : (ss.find(s => s.type === 'Load')?.value ?? '');
    const h = document.createElement('div'); h.className = 'panel-head';
    const netHide = item.net
      ? `<button class="ctl" data-mv="hide" aria-label="Hide adapter ${esc(label)}" title="Hide adapter">&#8856;</button>`
      : '';
    h.innerHTML = `<span class="panel-move"><button class="ctl" data-mv="up" aria-label="Move ${esc(label)} up" title="Move up">&#9650;</button><button class="ctl" data-mv="down" aria-label="Move ${esc(label)} down" title="Move down">&#9660;</button>${netHide}</span>` +
      `<button class="grip" aria-label="Drag to reorder ${esc(label)}" title="Drag to reorder">&#8942;&#8942;</button>` +
      `<span class="lamp s-${worst}"></span><span class="nm">${esc(label)}</span>` +
      `<span class="cls">${CLASSLABEL[cls] || ''}</span>` +
      `<span class="head-stat">${esc(head)}<span class="chev">&#9656;</span></span>`;
    h.querySelectorAll('.panel-move .ctl').forEach(b => b.onclick = e => {
      e.stopPropagation();
      if (b.dataset.mv === 'hide') { hideAdapter(item.key); return; }
      (item.net ? moveAdapter : movePanel)(item.key, b.dataset.mv === 'up' ? -1 : 1);
    });
    h.onclick = () => {
      p.classList.toggle('collapsed');
      state.dashboard.collapsedPanels[item.key] = p.classList.contains('collapsed');
      saveDashboard();
    };
    p.appendChild(h);
    const body = document.createElement('div'); body.className = 'panel-body';
    const byType = new Map(); ss.forEach(s => { const t = SQ.displayType(s); (byType.get(t) || byType.set(t, []).get(t)).push(s); });
    [...byType.entries()].sort((a,b) => TORDER.indexOf(a[0]) - TORDER.indexOf(b[0])).forEach(([type, list]) => {
      body.appendChild(Object.assign(document.createElement('div'), { className: 'tg', textContent: type }));
      list = orderedRows(item.key, type, list);
      const groupKey = rowGroupKey(item.key, type);
      const group = document.createElement('div');
      group.className = 'row-group';
      group.dataset.rowGroup = groupKey;
      const primary = [], extra = [];
      list.forEach(s => (cls === 'cpu' && isCoreRow(s) ? extra : primary).push(s));
      primary.forEach(s => appendRow(group, s, type, groupKey));
      body.appendChild(group);
      if (extra.length) {
        const box = document.createElement('div'); box.className = 'extra'; box.dataset.rowGroup = groupKey;
        extra.forEach(s => appendRow(box, s, type, groupKey));
        const btn = document.createElement('button'); btn.className = 'morebtn';
        btn.dataset.moreGroup = groupKey;
        btn.textContent = `+ ${extra.length} per-core ${type.toLowerCase()}`;
        btn.onclick = e => { e.stopPropagation(); box.classList.toggle('open'); btn.textContent =
          box.classList.contains('open') ? `- hide per-core ${type.toLowerCase()}` : `+ ${extra.length} per-core ${type.toLowerCase()}`; };
        body.appendChild(btn); body.appendChild(box);
      }
    });
    p.appendChild(body); return p;
  }
  function renderPanels(sensors) {
    const panels = $('#panels');
    state.panelItems = SQ.buildPanelItems(sensors, state.dashboard);
    const hwItems = state.panelItems.filter(i => !i.net);
    const netItems = state.panelItems.filter(i => i.net);
    const ordered = SQ.applyOrder(hwItems, state.dashboard.panelOrder, item => item.key);
    syncKeyedRegion(panels, ordered, item => item.key, panelSignature, panelEl);
    $('#subtag').textContent = `${ordered.length} components`;
    const preset = $('#panelsReset');
    if (preset) preset.style.display = state.dashboard.panelOrder.length ? '' : 'none';
    const netPanels = $('#netPanels');
    syncKeyedRegion(netPanels, netItems, item => item.key, panelSignature, panelEl);
    const hiddenNet = new Set(state.dashboard.hiddenNetAdapters);
    const hiddenCount = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNet.has(a.key)).length;
    $('#netsec').style.display = (netItems.length || hiddenCount) ? '' : 'none';
    $('#nettag').textContent = `${netItems.length} adapters` + (hiddenCount ? ` · ${hiddenCount} hidden` : '');
  }

  function renderSensorsPopover() {
    const countEl = $('#sensorsCount');
    if (countEl) {
      const n = SQ.hiddenSensorCount(state.allSensors, state.dashboard);
      countEl.textContent = n ? String(n) : '';
    }
    const list = $('#sensorsList');
    const menu = $('#sensorsMenu');
    if (!list || !menu || !menu.open) { state.sensorsSig = null; return; } // list renders only while open; clear sig so reopen rebuilds
    const rows = SQ.sensorPopoverRows(state.allSensors, state.dashboard, state.sensorsFilter || '');
    // Rebuild the list only when the filter or the row set / visibility / pin
    // state changes — NOT on every poll tick — so an in-progress text selection
    // (e.g. copying a SensorId) and search typing survive. Row values are
    // intentionally frozen while the popover is open; it is a discovery/manage
    // surface, not a live monitor (the dashboard behind it keeps updating).
    const pinnedIds = new Set(state.dashboard.pinnedCards.map(c => c.id));
    const hiddenNetKeys = new Set(state.dashboard.hiddenNetAdapters);
    const hiddenAdapters = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNetKeys.has(a.key));
    const sig = (state.sensorsFilter || '') + '|' +
      rows.map(r => `${r.id}:${r.visibility}:${pinnedIds.has(r.id) ? 1 : 0}:${state.primaryIds.has(r.id) ? 1 : 0}`).join(',') +
      '|net:' + hiddenAdapters.map(a => a.key).join(',');
    if (sig === state.sensorsSig) return;
    state.sensorsSig = sig;
    list.innerHTML = rows.map(r => {
      const hidden = r.visibility === 'hidden';
      const pinned = pinnedIds.has(r.id);
      const primary = state.primaryIds.has(r.id);
      const alias = r.label !== r.rawLabel ? ` · ${esc(r.rawLabel)}` : '';
      return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
        <div><b>${esc(r.label)}</b><span>${esc(r.hw)} · ${esc(r.type)} · ${esc(r.value)}${alias}</span><code>${esc(r.id)}</code></div>
        <span class="vis-chip vis-${r.visibility}">${r.visibility}</span>
        ${r.visibility === 'visible' ? `<button class="iconbtn" data-action="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(r.id)}">${primary ? 'Remove primary' : 'Make primary'}</button>` : ''}
        <button class="iconbtn" data-action="${pinned ? 'unpin' : 'pin'}" data-id="${esc(r.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
        <button class="iconbtn" data-action="${hidden ? 'show' : 'hide'}" data-id="${esc(r.id)}">${hidden ? 'Show' : 'Hide'}</button>
      </div>`;
    }).join('') || '<div class="empty-note">No sensors</div>';
    const restore = $('#netRestore');
    restore.style.display = hiddenAdapters.length ? '' : 'none';
    $('#netRestoreList').innerHTML = hiddenAdapters.map(a => `<div class="sensor-choice is-hidden">
      <div><b>${esc(a.label)}</b><span>network adapter${a.active ? '' : ' · idle'}</span><code>${esc(a.key)}</code></div>
      <button class="iconbtn" data-action="net-show" data-key="${esc(a.key)}">Show</button>
    </div>`).join('');
  }
  function paintGraphs() {
    const btn = $('#graphs');
    btn.textContent = state.dashboard.graphsEnabled ? 'Graphs On' : 'Graphs';
    btn.setAttribute('aria-pressed', state.dashboard.graphsEnabled ? 'true' : 'false');
    btn.classList.toggle('active', state.dashboard.graphsEnabled);
  }

  const poller = window.SQPolling.create({
    intervalMs:state.rate * 1000,
    timeoutMs:15000,
    paused:state.paused,
    hidden:document.hidden === true,
    request:async signal => {
      const r = await fetch('data.json', {cache:'no-store', signal});
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    },
    onData:data => {
      if (state.dragging) return; // drag started during fetch; do not replace its DOM
      render(data, true);
      document.body.classList.remove('stale');
    },
    onError:() => {
      $('#freshdot').className = 'lamp s-warn';
      $('#freshtxt').textContent = 'stale - retrying';
      document.body.classList.add('stale');
    }
  });
  document.addEventListener('visibilitychange', () => poller.setHidden(document.hidden === true));
  window.addEventListener('pagehide', () => poller.stop());
  window.addEventListener('pageshow', () => {
    poller.setHidden(document.hidden === true);
    poller.start(true);
  });

  function paintStudioPreferences() {
    const cfg = state.dashboard;
    document.documentElement.setAttribute('data-studio-accent', cfg.studioAccent);
    document.documentElement.setAttribute('data-studio-canvas', cfg.studioCanvas);
    document.documentElement.style.setProperty('--studio-canvas-opacity', String(cfg.studioCanvasOpacity / 100));
    document.documentElement.setAttribute('data-studio-density', cfg.studioDensity);
    document.documentElement.setAttribute('data-studio-focus-layout', cfg.studioFocusLayout);
    document.documentElement.setAttribute('data-studio-sparklines', String(cfg.studioShowSparklines));
    $('#studioAccent').value = cfg.studioAccent;
    $('#studioCanvas').value = cfg.studioCanvas;
    $('#studioCanvasOpacity').value = String(cfg.studioCanvasOpacity);
    $('#studioCanvasOpacity').setAttribute('aria-valuetext', `${cfg.studioCanvasOpacity}%`);
    $('#studioCanvasOpacityValue').textContent = `${cfg.studioCanvasOpacity}%`;
    $('#studioDensity').value = cfg.studioDensity;
    $('#studioFocusLayout').value = cfg.studioFocusLayout;
    $('#studioFocusCount').value = String(cfg.studioFocusCount);
    $('#studioShowSparklines').checked = cfg.studioShowSparklines;
    $('#studioShowSystems').checked = cfg.studioShowSystems;
    $('#studioShowNetwork').checked = cfg.studioShowNetwork;
  }
  function paintWorkspacePreferences() {
    const cfg = state.dashboard;
    const authoring = SQ.workspaceAuthoringPresentation(cfg.workspaceDensity, workspaceAuthoringOpen);
    document.documentElement.setAttribute('data-workspace-density', cfg.workspaceDensity);
    document.documentElement.setAttribute('data-workspace-authoring', authoring.state);
    $('#workspaceDensity').value = cfg.workspaceDensity;
    $('#workspaceDensityCompact').value = cfg.workspaceDensity;
    const authoringToggle = $('#workspaceAuthoringToggle');
    authoringToggle.setAttribute('aria-expanded', String(authoring.expanded));
    authoringToggle.textContent = authoring.label;
  }
  function paintToolbarPreferences() {
    const cfg = state.dashboard;
    document.documentElement.setAttribute('data-toolbar', cfg.toolbarCollapsed ? 'collapsed' : 'expanded');
    const collapse = $('#toolbarCollapse');
    const restore = $('#toolbarRestore');
    collapse.setAttribute('aria-expanded', String(!cfg.toolbarCollapsed));
    restore.setAttribute('aria-expanded', String(!cfg.toolbarCollapsed));
    restore.hidden = !cfg.toolbarCollapsed;
  }
  function paintViewTheme() {
    const view = $('#viewTheme');
    const studio = state.dashboard.viewTheme === 'cardTruth';
    const workspace = state.dashboard.viewTheme === 'workspace';
    document.documentElement.setAttribute('data-view-theme', state.dashboard.viewTheme);
    if (view) view.value = state.dashboard.viewTheme;
    $('#standardView').hidden = studio || workspace;
    $('#studioView').hidden = !studio;
    $('#workspaceView').hidden = !workspace;
    $('#dashboardSubtitle').textContent = studio ? 'Telemetry Studio'
      : workspace ? 'Sensor Workspace' : 'Hardware Telemetry Console';
    paintStudioPreferences();
    paintWorkspacePreferences();
    paintToolbarPreferences();
    if (workspace) paintWorkspaceControls();
    paintDashContext();
  }
  function paintDashContext() {
    const select = $('#dashContext');
    if (!select) return;
    const standard = state.dashboard.viewTheme === 'standard';
    select.value = state.contexts.active;
    select.disabled = !standard;
    select.title = standard ? 'Standard dashboard context'
      : 'Context applies to the Standard dashboard';
  }
  function paintTheme() {
    const light = state.dashboard.theme === 'light';
    document.documentElement.setAttribute('data-theme', state.dashboard.theme);
    const button = $('#theme');
    button.textContent = light ? '◐ Light' : '◐ Dark';
    button.setAttribute('aria-pressed', light ? 'true' : 'false');
    button.setAttribute('aria-label', `Switch to ${light ? 'dark' : 'light'} theme`);
    button.title = `Current theme: ${light ? 'light' : 'dark'}`;
  }
  paintTheme();
  paintViewTheme();
  $('#toolbarCollapse').onclick = () => {
    document.querySelectorAll('#dashboardHeader details[open]').forEach(details => { details.open = false; });
    state.dashboard.toolbarCollapsed = true;
    paintToolbarPreferences();
    saveDashboard();
    setTimeout(() => $('#toolbarRestore').focus(), 0);
  };
  $('#toolbarRestore').onclick = () => {
    state.dashboard.toolbarCollapsed = false;
    paintToolbarPreferences();
    saveDashboard();
    setTimeout(() => $('#toolbarCollapse').focus(), 0);
  };
  $('#theme').onclick = () => {
    const t = state.dashboard.theme === 'dark' ? 'light' : 'dark';
    state.dashboard.theme = t;
    paintTheme();
    saveDashboard();
  };
  $('#viewTheme').onchange = e => {
    state.dashboard.viewTheme = SQ.normalizeViewTheme(e.target.value);
    paintViewTheme();
    saveDashboard();
    rerender();
  };
  $('#dashContext').onchange = e => {
    const result = SQ.switchDashboardContext(storage, state.dashboard, e.target.value);
    state.dashboard = result.dashboard;
    state.contexts = result.contexts;
    paintDashContext();
    paintGraphs();
    rerender();
  };
  $('#studioAccent').onchange = e => {
    state.dashboard.studioAccent = cleanStudioAccent(e.target.value);
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioCanvas').onchange = e => {
    state.dashboard.studioCanvas = cleanStudioCanvas(e.target.value);
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioCanvasOpacity').oninput = e => {
    state.dashboard.studioCanvasOpacity = cleanStudioCanvasOpacity(e.target.value);
    paintStudioPreferences();
    saveDashboard();
  };
  $('#studioDensity').onchange = e => {
    state.dashboard.studioDensity = cleanStudioDensity(e.target.value);
    paintStudioPreferences();
    commitDashboard();
  };
  const setWorkspaceDensity = value => {
    workspaceAuthoringOpen = false;
    state.dashboard.workspaceDensity = cleanWorkspaceDensity(value);
    paintWorkspacePreferences();
    commitDashboard();
  };
  $('#workspaceDensity').onchange = e => setWorkspaceDensity(e.target.value);
  $('#workspaceDensityCompact').onchange = e => setWorkspaceDensity(e.target.value);
  $('#workspaceAuthoringToggle').onclick = () => {
    workspaceAuthoringOpen = !workspaceAuthoringOpen;
    paintWorkspacePreferences();
  };
  $('#studioFocusLayout').onchange = e => {
    state.dashboard.studioFocusLayout = cleanStudioFocusLayout(e.target.value);
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioFocusCount').onchange = e => {
    state.dashboard.studioFocusCount = cleanStudioFocusCount(e.target.value);
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioShowSparklines').onchange = e => {
    state.dashboard.studioShowSparklines = e.target.checked;
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioShowSystems').onchange = e => {
    state.dashboard.studioShowSystems = e.target.checked;
    commitDashboard();
  };
  $('#studioShowNetwork').onchange = e => {
    state.dashboard.studioShowNetwork = e.target.checked;
    commitDashboard();
  };
  $('#studioReset').onclick = () => {
    const defaults = SQ.defaultDashboardState();
    state.dashboard.studioAccent = defaults.studioAccent;
    state.dashboard.studioCanvas = defaults.studioCanvas;
    state.dashboard.studioCanvasOpacity = defaults.studioCanvasOpacity;
    state.dashboard.studioDensity = defaults.studioDensity;
    state.dashboard.studioFocusLayout = defaults.studioFocusLayout;
    state.dashboard.studioFocusCount = defaults.studioFocusCount;
    state.dashboard.studioShowSparklines = defaults.studioShowSparklines;
    state.dashboard.studioShowSystems = defaults.studioShowSystems;
    state.dashboard.studioShowNetwork = defaults.studioShowNetwork;
    paintStudioPreferences();
    commitDashboard();
  };
  $('#studioFocus').addEventListener('click', e => {
    const btn = e.target.closest('[data-studio-act="primary-remove"]');
    if (btn) setPrimaryCardState(btn.dataset.id, false);
  });
  $('#studioOpenSensors').onclick = () => {
    const menu = $('#sensorsMenu');
    menu.open = true;
    renderSensorsPopover();
    $('#sensorsSearch').focus();
  };
  $('#workspaceChooseSensors').onclick = () => {
    const profile = activeWorkspaceProfile();
    const panelId = profile?.panels[0]?.id;
    if (!panelId) return;
    const manager = $('#workspaceSensorManager');
    workspaceSensorReturnFocus = $('#workspaceChooseSensors');
    state.workspaceSensorTargetId = panelId;
    state.workspaceSensorSignature = null;
    manager.open = true;
    paintWorkspaceControls();
    renderWorkspaceSensorList();
    manager.scrollIntoView({block:'start'});
    setTimeout(() => $('#workspaceSensorSearch').focus(), 0);
  };
  $('#workspaceProfileSelect').onchange = event => {
    if (!Workspace) return;
    state.workspaceSensorTargetId = null;
    commitWorkspace(Workspace.setActiveProfile(state.workspace, event.target.value),
      'Profile selected.', 'info');
  };
  $('#workspaceProfileName').onchange = event => {
    if (!Workspace) return;
    const profile = activeWorkspaceProfile();
    if (!profile) return;
    commitWorkspace(Workspace.renameProfile(state.workspace, profile.id, event.target.value),
      'Profile name saved.', 'ok');
  };
  $('#workspaceProfileName').onkeydown = event => {
    if (event.key === 'Enter') event.currentTarget.blur();
  };
  $('#workspaceProfileNew').onclick = () => {
    if (!Workspace) return;
    const before = state.workspace.profiles.length;
    const next = Workspace.createProfile(state.workspace, 'New profile', {
      panels: [{ title:'Overview', type:'card', preset:null, sensorIds:[] }]
    });
    if (next.profiles.length === before) {
      announceWorkspace('The profile limit has been reached.', 'warn');
      return;
    }
    state.workspaceSensorTargetId = next.profiles.find(profile => profile.id === next.activeProfileId)?.panels[0]?.id || null;
    commitWorkspace(next, 'New profile created. Name it and choose sensors.', 'ok');
    setTimeout(() => { $('#workspaceProfileName').focus(); $('#workspaceProfileName').select(); }, 0);
  };
  $('#workspaceProfileDuplicate').onclick = () => {
    if (!Workspace) return;
    const profile = activeWorkspaceProfile();
    if (!profile) return;
    const before = state.workspace.profiles.length;
    const next = Workspace.duplicateProfile(state.workspace, profile.id);
    if (next.profiles.length === before) {
      announceWorkspace('The profile limit has been reached.', 'warn');
      return;
    }
    state.workspaceSensorTargetId = next.profiles.find(item => item.id === next.activeProfileId)?.panels[0]?.id || null;
    commitWorkspace(next, 'Profile duplicated with its panel and sensor order.', 'ok');
  };
  $('#workspaceProfileDelete').onclick = () => {
    if (!Workspace) return;
    const profile = activeWorkspaceProfile();
    if (!profile || state.workspace.profiles.length <= 1) return;
    if (!window.confirm(`Delete the “${profile.name}” profile? This cannot be undone.`)) return;
    state.workspaceSensorTargetId = null;
    commitWorkspace(Workspace.deleteProfile(state.workspace, profile.id), 'Profile deleted.', 'ok');
  };
  $('#workspaceReset').onclick = () => {
    if (!Workspace || !window.confirm('Reset Workspace to Main, Gaming, and Storage defaults? Local profiles and edits will be removed.')) return;
    state.workspaceSensorTargetId = null;
    state.workspaceSensorFilter = '';
    $('#workspaceSensorSearch').value = '';
    commitWorkspace(Workspace.createDefaults(), 'Workspace reset to host-neutral defaults.', 'ok');
  };
  $('#workspaceImport').onclick = () => $('#workspaceImportFile').click();
  $('#workspaceImportFile').onchange = async event => {
    if (!Workspace) return;
    const input = event.currentTarget;
    const file = input.files && input.files[0];
    if (!file) return;
    try {
      if (file.size > Workspace.LIMITS.profileDocumentBytes) {
        announceWorkspace('Import rejected: the profile document exceeds 256 KiB.', 'error');
        return;
      }
      const text = await file.text();
      const result = Workspace.importProfile(state.workspace, text);
      if (!result.ok) {
        announceWorkspace('Import rejected: ' + result.error.message, 'error', 9000);
        return;
      }
      state.workspaceSensorTargetId = result.state.profiles.find(profile => profile.id === result.profileId)?.panels[0]?.id || null;
      commitWorkspace(result.state, 'Profile imported without overwriting local profiles.', 'ok');
    } catch {
      announceWorkspace('Import failed while reading the selected file.', 'error', 9000);
    } finally {
      input.value = '';
    }
  };
  $('#workspaceExport').onclick = () => {
    if (!Workspace) return;
    const profile = activeWorkspaceProfile();
    if (!profile) return;
    if (!state.lastData && profile.panels.some(panel => panel.preset)) {
      announceWorkspace('Export needs one live snapshot to resolve adaptive presets safely.', 'warn', 9000);
      return;
    }
    try {
      const materialized = Workspace.materializeProfile(state.workspace, profile.id, state.allSensors);
      const exactProfile = materialized.profiles.find(item => item.id === profile.id);
      const json = Workspace.exportProfile(materialized, exactProfile.id, true);
      const blob = new Blob([json], {type:'application/json'});
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = (exactProfile.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'sensor-profile') + '.json';
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      announceWorkspace('Profile exported with exact ordered SensorIds; local adaptive presets were preserved.', 'ok');
    } catch (error) {
      announceWorkspace('Export failed: ' + (error?.message || 'unknown error'), 'error', 9000);
    }
  };
  $('#workspaceAddPanel').onclick = () => {
    if (!Workspace) return;
    const profile = activeWorkspaceProfile();
    if (!profile) return;
    const titleInput = $('#workspacePanelTitle');
    const title = titleInput.value.trim() || 'New panel';
    const before = profile.panels.map(panel => panel.id);
    const next = Workspace.addPanel(state.workspace, profile.id, {
      title,
      type: $('#workspacePanelKind').value,
      preset: null,
      sensorIds: []
    });
    const nextProfile = next.profiles.find(item => item.id === profile.id);
    const added = nextProfile?.panels.find(panel => !before.includes(panel.id));
    if (!added) {
      announceWorkspace('This profile already has the maximum number of panels.', 'warn');
      return;
    }
    state.workspaceSensorTargetId = added.id;
    titleInput.value = '';
    commitWorkspace(next, 'Panel added. Open Sensors to choose exact readings.', 'ok');
  };
  $('#workspacePanels').addEventListener('click', event => {
    if (!Workspace) return;
    const button = event.target.closest('[data-workspace-act]');
    if (!button) return;
    const profile = activeWorkspaceProfile();
    const panel = workspacePanel(profile, button.dataset.panelId);
    if (!profile || !panel) return;
    const index = profile.panels.findIndex(item => item.id === panel.id);
    switch (button.dataset.workspaceAct) {
      case 'manage':
        workspaceSensorReturnFocus = button;
        state.workspaceSensorTargetId = panel.id;
        paintWorkspaceControls();
        $('#workspaceSensorManager').open = true;
        renderWorkspaceSensorList();
        $('#workspaceSensorSearch').focus();
        break;
      case 'panel-up':
      case 'panel-down':
        commitWorkspace(Workspace.movePanel(state.workspace, profile.id, panel.id,
          index + (button.dataset.workspaceAct === 'panel-up' ? -1 : 1)), 'Panel order saved.', 'ok');
        break;
      case 'panel-remove':
        if (!window.confirm(`Remove the “${panel.title}” panel?`)) return;
        if (state.workspaceSensorTargetId === panel.id) state.workspaceSensorTargetId = null;
        commitWorkspace(Workspace.removePanel(state.workspace, profile.id, panel.id), 'Panel removed.', 'ok');
        setTimeout(() => $('#workspaceAddPanel').focus(), 0);
        break;
    }
  });
  $('#workspacePanels').addEventListener('change', event => {
    if (!Workspace) return;
    const field = event.target.dataset.workspaceField;
    const panelId = event.target.dataset.panelId;
    if (!field || !panelId) return;
    const profile = activeWorkspaceProfile();
    if (!profile) return;
    const patch = field === 'title' ? {title:event.target.value} : {type:event.target.value};
    commitWorkspace(Workspace.updatePanel(state.workspace, profile.id, panelId, patch, state.allSensors),
      field === 'title' ? 'Panel title saved.' : 'Panel presentation changed.', 'ok');
  });
  $('#workspacePanels').addEventListener('keydown', event => {
    if (event.key === 'Enter' && event.target.matches('[data-workspace-field="title"]')) event.target.blur();
  });
  $('#workspaceSensorManager').addEventListener('toggle', () => {
    const manager = $('#workspaceSensorManager');
    state.workspaceSensorSignature = null;
    $('#workspaceChooseSensors').setAttribute('aria-expanded', String(manager.open));
    if (!manager.open) workspaceSensorReturnFocus = null;
    if (manager.open) {
      paintWorkspaceControls();
      renderWorkspaceSensorList();
    }
  });
  $('#workspaceSensorManager').addEventListener('keydown', event => {
    const manager = $('#workspaceSensorManager');
    if (event.key !== 'Escape' || !manager.open) return;
    event.preventDefault();
    const focusTarget = workspaceSensorReturnFocus;
    workspaceSensorReturnFocus = null;
    manager.open = false;
    if (focusTarget?.isConnected) focusTarget.focus();
    else if (state.dashboard.workspaceDensity === 'compact') $('#workspaceChooseSensors').focus();
    else manager.querySelector('summary')?.focus();
  });
  $('#workspaceSensorTarget').onchange = event => {
    state.workspaceSensorTargetId = event.target.value || null;
    state.workspaceSensorSignature = null;
    renderWorkspaceSensorList();
  };
  $('#workspaceSensorSearch').oninput = event => {
    state.workspaceSensorFilter = event.target.value;
    state.workspaceSensorSignature = null;
    renderWorkspaceSensorList();
  };
  $('#workspaceSensorList').addEventListener('change', event => {
    if (!Workspace || !event.target.matches('[data-workspace-sensor]')) return;
    const profile = activeWorkspaceProfile();
    const panel = workspacePanel(profile, state.workspaceSensorTargetId);
    if (!profile || !panel) return;
    try {
      commitWorkspace(Workspace.togglePanelSensor(state.workspace, profile.id, panel.id,
        event.target.dataset.workspaceSensor, event.target.checked, state.allSensors),
        event.target.checked ? 'Sensor added to the panel.' : 'Sensor removed from the panel.', 'ok');
    } catch (error) {
      event.target.checked = !event.target.checked;
      announceWorkspace(error.message, 'error');
    }
  });
  $('#workspaceSensorList').addEventListener('click', event => {
    if (!Workspace) return;
    const button = event.target.closest('[data-workspace-act="sensor-top"], [data-workspace-act="sensor-up"], [data-workspace-act="sensor-down"]');
    if (!button) return;
    const profile = activeWorkspaceProfile();
    const panel = workspacePanel(profile, state.workspaceSensorTargetId);
    if (!profile || !panel) return;
    const ids = Workspace.resolvePanel(panel, state.allSensors).sensorIds;
    const index = ids.indexOf(button.dataset.sensorId);
    if (index < 0) return;
    const nextIndex = button.dataset.workspaceAct === 'sensor-top' ? 0
      : index + (button.dataset.workspaceAct === 'sensor-up' ? -1 : 1);
    commitWorkspace(Workspace.movePanelSensor(state.workspace, profile.id, panel.id,
      button.dataset.sensorId, nextIndex, state.allSensors), 'Sensor order saved.', 'ok');
  });
  const rate = $('#rate'); rate.value = state.rate; $('#ratev').textContent = state.rate + 's';
  rate.setAttribute('aria-valuetext', `${state.rate} seconds`);
  rate.oninput = e => {
    state.rate = clampRate(e.target.value);
    state.dashboard.rate = state.rate;
    $('#ratev').textContent = state.rate + 's';
    rate.setAttribute('aria-valuetext', `${state.rate} seconds`);
    saveDashboard(); poller.setInterval(state.rate * 1000);
  };
  const pause = $('#pause');
  function paintPause() { pause.textContent = state.paused ? 'Resume' : 'Pause';
    pause.setAttribute('aria-pressed', state.paused ? 'true' : 'false');
    $('#freshdot').className = 'lamp ' + (state.paused ? 's-off' : 's-ok');
    $('#freshtxt').textContent = state.paused ? 'paused' : 'live'; }
  pause.onclick = () => {
    state.paused = !state.paused;
    state.dashboard.paused = state.paused;
    saveDashboard(); paintPause();
    poller.setPaused(state.paused);
  };
  $('#graphs').onclick = () => { state.dashboard.graphsEnabled = !state.dashboard.graphsEnabled; commitDashboard(); };
  $('#pfdReset').onclick = resetPrimaryCardsState;
  $('#panelsReset').onclick = resetPanelOrder;
  $('#sensorsSearch').oninput = e => { state.sensorsFilter = e.target.value; renderSensorsPopover(); };
  $('#sensorsMenu').addEventListener('toggle', () => { if ($('#sensorsMenu').open) renderSensorsPopover(); });
  $('#sensorsList').addEventListener('click', e => {
    const btn = e.target.closest('[data-action]');
    if (!btn) return;
    const id = btn.dataset.id;
    switch (btn.dataset.action) {
      case 'hide': setSensorHidden(id, true); break;
      case 'show': setSensorHidden(id, false); break;
      case 'pin': pinSensor(id); break;
      case 'unpin': unpinSensor(id); break;
      case 'primary-add': setPrimaryCardState(id, true); break;
      case 'primary-remove': setPrimaryCardState(id, false); break;
    }
    renderSensorsPopover();
  });
  $('#sensorsMenu').querySelector('[data-action="reset-hidden"]').onclick = () => {
    state.dashboard.hiddenSensorIds = [];
    commitDashboard();
    renderSensorsPopover();
  };
  $('#netRestoreList').addEventListener('click', e => {
    const btn = e.target.closest('[data-action="net-show"]');
    if (!btn) return;
    showAdapter(btn.dataset.key);
    renderSensorsPopover();
  });
  // Escape / click-outside close for disclosure menus.
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') document.querySelectorAll('details.page-menu[open], details.sensors-menu[open], details.studio-customize[open], details.workspace-sensor-manager[open]').forEach(d => { d.open = false; });
  });
  // Capture phase: this must observe e.target BEFORE the #sensorsList action
  // handler (bubble phase) rebuilds the list and detaches the clicked button —
  // otherwise the orphaned target reads as "outside" and closes the popover on
  // every Pin/Hide/Show click.
  document.addEventListener('click', e => {
    document.querySelectorAll('details.page-menu[open], details.sensors-menu[open], details.studio-customize[open]').forEach(d => { if (!d.contains(e.target)) d.open = false; });
  }, true);
  // Capture phase, and deliberately blind to clicks on any card/row/control
  // surface: card-to-card switching belongs to toggleExpand's single-open
  // logic, and a bubble-phase rebuild must never make a control click read
  // as "outside" (same race as the sensors-popover lesson above).
  document.addEventListener('click', e => {
    const open = [...state.expanded].filter(k => k.startsWith('c:'));
    if (!open.length) return;
    if (e.target.closest('.cell, .xp-overlay, .row, .rowxp, .panel-head, button, input, select, a, code, label, details')) return;
    open.forEach(k => state.expanded.delete(k));
    rerender();
  }, true);
  function toggleExpand(key) {
    if (state.expanded.has(key)) state.expanded.delete(key);
    else {
      if (key.startsWith('c:')) {
        [...state.expanded].filter(k => k.startsWith('c:')).forEach(k => state.expanded.delete(k));
        state.xpEnter = key;
      }
      state.expanded.add(key);
    }
    rerender();
    state.xpEnter = null;
  }
  function handleAct(btn) {
    const id = btn.dataset.id;
    switch (btn.dataset.act) {
      case 'pin': pinSensor(id); break;
      case 'unpin': unpinSensor(id); break;
      case 'primary-add': setPrimaryCardState(id, true); break;
      case 'primary-remove': setPrimaryCardState(id, false); break;
      case 'hide': setSensorHidden(id, true); break;
      case 'alias-clear':
        state.dashboard = SQ.updateSensorAlias(state.dashboard, id, '');
        finishInlineEdit();
        commitDashboard();
        break;
      case 'set-range': {
        const xp = btn.closest('.xp, .rowxp');
        state.dashboard.rangeOverrides = SQ.updateRangeOverride(
          state.dashboard.rangeOverrides, id,
          xp?.querySelector('.ov-max')?.value ?? '', xp?.querySelector('.ov-min')?.value ?? '');
        finishInlineEdit();
        commitDashboard();
        break;
      }
      case 'clear-range':
        state.dashboard.rangeOverrides = SQ.updateRangeOverride(state.dashboard.rangeOverrides, id, '', '');
        finishInlineEdit();
        commitDashboard();
        break;
      case 'move-left':
      case 'move-right': {
        const cell = btn.closest('.cell');
        const container = cell?.parentElement;
        if (!container) break;
        const keys = orderedKeysFor(container);
        const next = SQ.moveKey(SQ.mergeOrder(container.id === 'pinned' ? state.dashboard.pinnedOrder : state.dashboard.cardOrder, keys),
          cell.dataset.key, btn.dataset.act === 'move-left' ? -1 : 1);
        if (container.id === 'pinned') state.dashboard.pinnedOrder = next;
        else if (container.id === 'pfd') state.dashboard.cardOrder = next;
        commitDashboard();
        break;
      }
      case 'row-up':
      case 'row-down':
        moveRow(btn.dataset.rowGroup, id, btn.dataset.act === 'row-up' ? -1 : 1);
        break;
    }
  }
  ['#pfd', '#pinned', '#panels'].forEach(sel => {
    const host = $(sel);
    if (!host) return;
    host.addEventListener('click', e => {
      const btn = e.target.closest('[data-act]');
      if (btn && host.contains(btn) && btn.tagName === 'BUTTON') {
        e.stopPropagation();
        handleAct(btn);
        return;
      }
      if (e.target.closest('input, select, button, a, code, .xp, .rowxp')) return;
      const cell = e.target.closest('.cell');
      if (cell && host.contains(cell) && cell.dataset.sid) { toggleExpand('c:' + cell.dataset.sid); return; }
      const row = e.target.closest('.row');
      if (row && host.contains(row) && row.dataset.sid) toggleExpand('r:' + row.dataset.sid);
    });
    host.addEventListener('change', e => {
      const styleSel = e.target.closest('select[data-act="style"]');
      if (styleSel && host.contains(styleSel)) {
        const v = styleSel.value;
        if (v === 'auto') delete state.dashboard.cardStyle[styleSel.dataset.id];
        else state.dashboard.cardStyle[styleSel.dataset.id] = v;
        commitDashboard();
        return;
      }
      const aliasInput = e.target.closest('input[data-act="alias"]');
      if (aliasInput && host.contains(aliasInput)) {
        state.dashboard = SQ.updateSensorAlias(state.dashboard, aliasInput.dataset.id, aliasInput.value);
        commitDashboard();
      }
    });
  });
  document.addEventListener('focusin', ev => {
    if (isInlineEditTarget(ev.target)) state.inlineEditing = true;
  }, true);
  document.addEventListener('focusout', ev => {
    if (!isInlineEditTarget(ev.target)) return;
    state.inlineEditingUntil = Date.now() + 1000;
    setTimeout(() => { state.inlineEditing = isInlineEditTarget(document.activeElement); }, 0);
  }, true);

  const drag = { active: null };
  function orderedKeysFor(container) {
    return Array.from(container.children).map(el => el.dataset.key).filter(k => typeof k === 'string' && k.length);
  }
  function dragSiblings(container, movedKey) {
    return Array.from(container.children).filter(el => el.dataset.key && el.dataset.key !== movedKey);
  }
  function dropIndex(sibs, clientX, clientY, columnMajor) {
    for (let i = 0; i < sibs.length; i++) {
      const r = sibs[i].getBoundingClientRect();
      if (columnMajor) {
        // #panels CSS multi-column masonry: columns left->right, items top->bottom.
        if (clientX < r.left) return i;
        if (clientX <= r.right && clientY < r.top + r.height / 2) return i;
      } else {
        // #pinned row-major grid: rows top->bottom, items left->right.
        if (clientY < r.top) return i;
        if (clientY <= r.bottom && clientX < r.left + r.width / 2) return i;
      }
    }
    return sibs.length;
  }
  function placeIndicator(a, sibs) {
    const ind = a.ind;
    if (!sibs.length) { ind.style.display = 'none'; return; }
    const crect = a.container.getBoundingClientRect();
    const before = sibs[a.dropIdx]; // insert-before sibling, or undefined for end-drop
    const anchor = before || sibs[sibs.length - 1];
    const r = anchor.getBoundingClientRect();
    ind.style.display = 'block';
    if (a.isPanel || a.isRow) {
      // horizontal bar above the anchor (below the last panel for end-drop)
      ind.style.left = (r.left - crect.left) + 'px';
      ind.style.width = r.width + 'px';
      ind.style.height = '2px';
      ind.style.top = ((before ? r.top : r.bottom) - crect.top - 1) + 'px';
    } else {
      // vertical bar left of the anchor (right of the last card for end-drop)
      ind.style.top = (r.top - crect.top) + 'px';
      ind.style.height = r.height + 'px';
      ind.style.width = '2px';
      ind.style.left = ((before ? r.left : r.right) - crect.left - 1) + 'px';
    }
  }
  function moveGhost(ev) {
    const a = drag.active; if (!a) return;
    a.ghost.style.left = (ev.clientX + 12) + 'px';
    a.ghost.style.top = (ev.clientY + 12) + 'px';
    const sibs = dragSiblings(a.container, a.key);
    a.dropIdx = dropIndex(sibs, ev.clientX, ev.clientY, a.isPanel || a.isRow);
    placeIndicator(a, sibs);
  }
  function startDrag(grip, ev) {
    if (drag.active) return;
    const el = grip.closest('.row') || grip.closest('.panel') || grip.closest('.cell');
    if (!el || !el.dataset.key) return;
    ev.preventDefault();
    const nameEl = el.querySelector('.rn') || el.querySelector('.nm') || el.querySelector('.k .name');
    state.dragging = true;
    const ghost = document.createElement('div');
    ghost.className = 'drag-ghost';
    ghost.textContent = nameEl ? nameEl.textContent : el.dataset.key;
    document.body.appendChild(ghost);
    const ind = document.createElement('div');
    ind.className = 'drop-ind';
    el.parentElement.appendChild(ind);
    const isRow = el.classList.contains('row');
    drag.active = { container: el.parentElement, el, key: el.dataset.key,
      isPanel: el.classList.contains('panel'), isRow, rowGroup: el.dataset.rowGroup, ghost, ind, grip, pointerId: ev.pointerId };
    el.classList.add('dragging');
    moveGhost(ev);
    try { grip.setPointerCapture(ev.pointerId); } catch (e) {}
  }
  function endDrag(commit) {
    const a = drag.active; if (!a) return;
    drag.active = null; state.dragging = false;
    a.el.classList.remove('dragging');
    a.ghost.remove(); a.ind.remove();
    try { a.grip.releasePointerCapture(a.pointerId); } catch (e) {}
    if (commit && typeof a.dropIdx === 'number') {
      const next = SQ.reorderByDrop(orderedKeysFor(a.container), a.key, a.dropIdx);
      if (a.container.id === 'panels') state.dashboard.panelOrder = next;
      else if (a.container.id === 'netPanels') state.dashboard.netAdapterOrder = next;
      else if (a.container.id === 'pinned') state.dashboard.pinnedOrder = next;
      else if (a.container.id === 'pfd') state.dashboard.cardOrder = next;
      else if (a.isRow) state.dashboard.rowOrder[a.rowGroup] = next;
      commitDashboard();
    } else {
      rerender();
    }
  }
  document.addEventListener('pointerdown', ev => {
    const grip = ev.target.closest && ev.target.closest('.grip');
    if (grip) { ev.stopPropagation(); startDrag(grip, ev); }
  });
  document.addEventListener('pointermove', ev => { if (drag.active) moveGhost(ev); });
  document.addEventListener('pointerup', () => { if (drag.active) endDrag(true); });
  document.addEventListener('pointercancel', () => { if (drag.active) endDrag(false); });
  document.addEventListener('keydown', ev => {
    if (ev.key === 'Escape') {
      if (drag.active) { endDrag(false); return; }
      if (state.expanded.size) { state.expanded.clear(); rerender(); }
      return;
    }
    if (ev.key !== 'Enter' && ev.key !== ' ') return;
    if (ev.target.closest && ev.target.closest('input, select, textarea, button, a, code, .xp, .rowxp')) return;
    const cell = ev.target.closest && ev.target.closest('.cell');
    if (cell && cell.dataset.sid) { ev.preventDefault(); toggleExpand('c:' + cell.dataset.sid); return; }
    const row = ev.target.closest && ev.target.closest('.row');
    if (row && row.dataset.sid) { ev.preventDefault(); toggleExpand('r:' + row.dataset.sid); }
  });
  document.addEventListener('click', ev => {
    const grip = ev.target.closest && ev.target.closest('.grip');
    if (grip) { ev.preventDefault(); ev.stopPropagation(); }
  }, true);
  window.addEventListener('resize', () => {
    ['#pfd', '#pinned'].forEach(sel => {
      const grid = $(sel);
      const ov = grid && grid.querySelector('.xp-overlay');
      const cell = grid && grid.querySelector('.cell.expanded');
      if (ov && cell) ov.style.top = (cell.offsetTop + cell.offsetHeight + 6) + 'px';
    });
  });

  paintPause();
  paintGraphs();
  window.SQ._STLABEL = STLABEL;
  poller.start(true);
})();
