// Shared model assertions and markup contracts, without evaluating DOM boot.
const fs = require('fs');
const path = require('path');
const WEB = path.resolve(__dirname, '../web');
const read = name => fs.readFileSync(path.join(WEB, name), 'utf8');
const S = require('../web/model.js').create();
const dashboardJs = ['model.js','polling.js','console.js'].map(read).join('\n');
const fleetPollerJs = read('fleet-poller.js');
const workspaceJs = read('workspace.js');
const runConsoleTests = require('./console.tests.js');
const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json'), 'utf8'));
const storage = value => { let slot = value; return { getItem: () => slot, setItem: (k, v) => { slot = v; } }; };
const { pass, fail, log } = runConsoleTests(S, data, storage);
const indexHtml = read('index.html');
const consoleCss = read('console.css');
const menuChecks = [
  ['root menu retired card-truth preview link', !indexHtml.includes('href="/dash/cardtruth/"')],
  ['root menu links data.json', indexHtml.includes('href="/data.json"')],
  ['root menu links metrics', indexHtml.includes('href="/metrics"')],
  ['root has viewTheme selector', indexHtml.includes('id="viewTheme"')],
  ['root selector is labelled Dashboard', indexHtml.includes('<span>Dashboard</span>') && indexHtml.includes('aria-label="Dashboard"')],
  ['root offers Standard with stable value', indexHtml.includes('<option value="standard">Standard</option>')],
  ['root offers Studio with cardTruth value', indexHtml.includes('<option value="cardTruth">Studio</option>')],
  ['root offers Workspace with stable value', indexHtml.includes('<option value="workspace">Workspace</option>')],
  ['Workspace model loads before dashboard boot',
    indexHtml.indexOf('<script src="workspace.js"></script>') >= 0
      && indexHtml.indexOf('<script src="workspace.js"></script>') < indexHtml.indexOf('<script src="console.js"></script>')],
  ['root has Workspace view regions', [
    'workspaceView','workspaceStatus','workspaceProfileSelect','workspaceProfileName','workspacePanels',
    'workspaceSensorManager','workspaceSensorTarget','workspaceSensorSearch','workspaceSensorList','workspaceFootLeft'
  ].every(id => indexHtml.includes(`id="${id}"`))],
  ['root has Workspace profile, file, and panel controls', [
    'workspaceProfileNew','workspaceProfileDuplicate','workspaceProfileDelete','workspaceImport','workspaceImportFile',
    'workspaceExport','workspaceReset','workspacePanelTitle','workspacePanelKind','workspaceAddPanel'
  ].every(id => indexHtml.includes(`id="${id}"`))],
  ['Workspace status is an atomic live region', indexHtml.includes('id="workspaceStatus"')
    && indexHtml.includes('role="status"') && indexHtml.includes('aria-live="polite"') && indexHtml.includes('aria-atomic="true"')],
  ['Workspace CSS scopes its root and responsive panel surfaces', [
    'data-view-theme="workspace"','.workspace-panel','.workspace-card-grid','.workspace-table-wrap',
    '.workspace-trend-grid','.workspace-sensor-choice','@media (max-width:640px)'
  ].every(token => consoleCss.includes(token))],
  ['Workspace remains presentation-only', !workspaceJs.includes('/Sensor?action=Set')
    && !dashboardJs.includes('/Sensor?action=Set')],
  ['Workspace export materializes a copy without replacing local adaptive state',
    dashboardJs.includes('Workspace.materializeProfile(state.workspace, profile.id, state.allSensors)')
      && dashboardJs.includes('Workspace.exportProfile(materialized, exactProfile.id, true)')
      && !dashboardJs.includes('state.workspace = Workspace.save(storage, materialized)')],
  ['Workspace edits report storage fallback and restore keyboard focus',
    dashboardJs.includes('Workspace.saveResult(storage, next)')
      && dashboardJs.includes('this change is temporary for this page')
      && dashboardJs.includes('captureWorkspaceFocus()')
      && dashboardJs.includes('restoreWorkspaceFocus(focus)')
      && dashboardJs.includes("workspaceAddPanel:'workspacePanelTitle'")
      && dashboardJs.includes("workspaceProfileDuplicate:'workspaceProfileSelect'")],
  ['root has Workspace density controls', [
    'workspaceDensity','workspaceDensityCompact','workspaceCompactBar','workspaceCompactProfile'
  ].every(id => indexHtml.includes(`id="${id}"`))],
  ['root has accessible persistent toolbar collapse and restore controls',
    indexHtml.includes('id="dashboardToolbar"')
      && indexHtml.includes('id="toolbarCollapse"')
      && indexHtml.includes('aria-controls="dashboardToolbar"')
      && indexHtml.includes('id="toolbarRestore"')
      && indexHtml.includes('Show toolbar')],
  ['Workspace top chooser precedes panels and Sensor Manager is no longer buried below them',
    indexHtml.includes('id="workspaceChooseSensors"')
      && indexHtml.indexOf('id="workspaceSensorManager"') < indexHtml.indexOf('id="workspacePanels"')],
  ['Workspace compact bar retains an accessible heading and polite status region',
    indexHtml.includes('<h1 class="workspace-compact-profile" id="workspaceCompactProfile">')
      && indexHtml.includes('id="workspaceCompactStatus"')
      && indexHtml.includes('role="status"')
      && indexHtml.includes('aria-live="polite"')],
  ['root has accessible Workspace authoring disclosure', (() => {
    const start = indexHtml.indexOf('id="workspaceAuthoringToggle"');
    const block = start >= 0 ? indexHtml.slice(Math.max(0, start - 120), indexHtml.indexOf('</button>', start) + 9) : '';
    return block.includes('<button') && block.includes('type="button"')
      && block.includes('aria-expanded="false"') && block.includes('Edit layout');
  })()],
  ['Workspace density options are stable', (() => {
    const check = (id) => {
      const start = indexHtml.indexOf(`id="${id}"`);
      const block = start >= 0 ? indexHtml.slice(start, indexHtml.indexOf('</select>', start)) : '';
      return ['<option value="comfortable">Comfortable</option>','<option value="compact">Compact</option>'].every(option => block.includes(option));
    };
    return check('workspaceDensity') && check('workspaceDensityCompact');
  })()],
  ['Workspace compact CSS hides editor chrome and densifies cards', [
    '[data-workspace-density="compact"][data-workspace-authoring="closed"] .workspace-hero{display:none}',
    '[data-workspace-density="compact"][data-workspace-authoring="closed"] .workspace-command-bar{display:none',
    '[data-workspace-density="compact"][data-workspace-authoring="closed"] .workspace-add-panel{display:none',
    '[data-workspace-density="compact"][data-workspace-authoring="closed"] .workspace-panels-heading{display:none',
    '[data-workspace-density="compact"] .workspace-compact-bar{display:flex',
    '[data-workspace-density="compact"][data-workspace-authoring="closed"] .workspace-panel-actions{display:none'
  ].every(token => consoleCss.includes(token))],
  ['Workspace compact cards stay substantial and drop secondary catalog prose', [
    'minmax(min(100%,240px),1fr)',
    '[data-workspace-density="compact"] .workspace-sensor-card{min-height:112px',
    '[data-workspace-density="compact"] .workspace-sensor-copy>span{display:none}',
    '[data-workspace-density="compact"] .workspace-sensor-card .workspace-sensor-meta{display:none}',
    'workspace-sensor-state'
  ].every(token => consoleCss.includes(token))],
  ['collapsed toolbar leaves a non-overlaying in-flow restore affordance and consumes no header layout',
    consoleCss.includes(':root[data-toolbar="collapsed"] header{display:none}')
      && consoleCss.includes('.toolbar-restore{display:block;position:relative')
      && !/\.toolbar-restore\{[^}]*position:(?:fixed|sticky|absolute)/.test(consoleCss)],
  ['compact closed panels expose headings instead of editable title fields', [
    'workspace-panel-title-display',
    '[data-workspace-authoring="closed"] .workspace-panel-title{display:none}',
    '[data-workspace-authoring="closed"] .workspace-panel-title-display{display:block}'
  ].every(token => dashboardJs.includes(token) || consoleCss.includes(token))],
  ['generated Workspace panel fields expose stable id and name attributes', [
    'id="workspace-panel-title-input-',
    'name="workspacePanelTitle"',
    'id="workspace-panel-type-',
    'name="workspacePanelType"'
  ].every(token => dashboardJs.includes(token))],
  ['console wires workspace density', dashboardJs.includes("setAttribute('data-workspace-density', cfg.workspaceDensity)")
    && dashboardJs.includes('cleanWorkspaceDensity')
    && dashboardJs.includes("$('#workspaceDensity').onchange")
    && dashboardJs.includes("$('#workspaceDensityCompact').onchange")
    && dashboardJs.includes("$('#workspaceCompactProfile').textContent")],
  ['console wires transient Workspace authoring without storage',
    dashboardJs.includes('let workspaceAuthoringOpen = false')
      && dashboardJs.includes("setAttribute('data-workspace-authoring', authoring.state)")
      && dashboardJs.includes("setAttribute('aria-expanded', String(authoring.expanded))")
      && dashboardJs.includes("$('#workspaceAuthoringToggle').onclick")
      && dashboardJs.includes('workspaceAuthoringOpen = !workspaceAuthoringOpen')
      && !dashboardJs.includes('workspaceAuthoringOpen:')],
  ['console persists toolbar collapse and restores focus safely',
    dashboardJs.includes("setAttribute('data-toolbar', cfg.toolbarCollapsed ? 'collapsed' : 'expanded')")
      && dashboardJs.includes("$('#toolbarCollapse').onclick")
      && dashboardJs.includes("$('#toolbarRestore').onclick")
      && dashboardJs.includes("$('#toolbarCollapse').focus()")
      && dashboardJs.includes("$('#toolbarRestore').focus()")
      && dashboardJs.includes("querySelectorAll('#dashboardHeader details[open]')")],
  ['console opens top sensor selection on the first panel and offers one-click move-to-top',
    dashboardJs.includes("$('#workspaceChooseSensors').onclick")
      && dashboardJs.includes('profile?.panels[0]?.id')
      && dashboardJs.includes('data-workspace-act="sensor-top"')
      && dashboardJs.includes("button.dataset.workspaceAct === 'sensor-top' ? 0")
      && dashboardJs.includes("event.key !== 'Escape'")
      && dashboardJs.includes("$('#workspaceChooseSensors').focus()")
      && dashboardJs.includes('if (!manager.open) workspaceSensorReturnFocus = null')
      && dashboardJs.includes('const focusTarget = workspaceSensorReturnFocus')],
  ['Workspace poll rendering does not churn its live region',
    dashboardJs.includes("status.textContent === message && status.className === 'is-' + nextTone")
      && dashboardJs.includes('paintWorkspaceControls(false)')],
  ['Workspace graph labels use the same scale as their plotted bounds',
    dashboardJs.includes('const scale = SQ.graphScaleFor(range, history)')
      && dashboardJs.includes('sparkAreaSVG(sensor, scale ? [scale.lo, scale.hi] : null)')
      && dashboardJs.includes('SQ.graphScaleText(scale, sensor)')],
  ['root has Studio view regions', ['studioView','studioHealth','studioAlertStatus','studioFocus','studioSystems','studioNetwork'].every(id => indexHtml.includes(`id="${id}"`)) && indexHtml.includes('class="studio-atmosphere"')],
  ['root has transition-only Studio alert status', indexHtml.includes('id="studioAlertStatus"') && indexHtml.includes('role="status"') && indexHtml.includes('aria-atomic="true"')],
  ['root has Studio customization controls', [
    'studioCustomize','studioAccent','studioCanvas','studioCanvasOpacity','studioCanvasOpacityValue',
    'studioDensity','studioFocusLayout','studioFocusCount',
    'studioShowSparklines',
    'studioShowSystems','studioShowNetwork','studioReset'
  ].every(id => indexHtml.includes(`id="${id}"`))],
  ['Studio opacity has an explicit input label', indexHtml.includes('<label for="studioCanvasOpacity">Atmosphere opacity</label>')],
  ['root has warm Studio accent options', ['coral','rose','amber','plum'].every(value =>
    indexHtml.includes(`<option value="${value}">`)) && !['cyan','blue','green','emerald'].some(value =>
    indexHtml.includes(`<option value="${value}">`))],
  ['root has Studio canvas options', ['ember','strata','plain'].every(value =>
    indexHtml.includes(`<option value="${value}">`))],
  ['root has Studio focus layout options', ['spotlight','grid'].every(value =>
    indexHtml.includes(`<option value="${value}">`))],
  ['Studio CSS scopes canvas, spotlight, and sparkline modes', [
    'data-studio-canvas="ember"','data-studio-canvas="strata"','data-studio-canvas="plain"',
    'data-studio-focus-layout="spotlight"','data-studio-sparklines="false"'
  ].every(token => consoleCss.includes(token))],
  ['Studio canvas masthead styles cannot leak into Standard', [
    'data-view-theme="cardTruth"][data-studio-canvas="strata"] header',
    'data-view-theme="cardTruth"][data-studio-canvas="plain"] header'
  ].every(token => consoleCss.includes(token))],
  ['Studio replaces the shared active-control green token', consoleCss.includes('--lime:var(--studio-ok)')],
  ['repaints are paint-only and read stored per-source sensors',
    dashboardJs.includes('function render() {')
      && dashboardJs.includes('const allSensors = SQ.composeSensors(state, state.dashboard)')
      && !dashboardJs.includes('SQ.ingestTelemetry(state, allSensors')
      && dashboardJs.includes('if (SQ.hasTelemetry(state)) paint()')],
  ['fresh samples ingest once in the poller seam and persist with a local cadence',
    dashboardJs.includes('const ingestion = SQ.ingestSourceTelemetry(state, machine, data, receipt)')
      && dashboardJs.includes('persistTelemetry(ingestion, isLocal)')
      && dashboardJs.includes('if (!freshTelemetry) return {ingested:false, samplesChanged:false}')
      && dashboardJs.includes('if ((state.samplesDirty && state.localTicks % 5 === 0) || state.localTicks % 30 === 0)')],
  ['fleet polling is the boot poller: per source, cancellable, and visibility-aware',
    dashboardJs.includes('window.SQFleetPoller.create')
      && !dashboardJs.includes('window.SQPolling.create')
      && dashboardJs.includes('onGap:source => SQ.resetSourceDerivatives(source.key)')
      && dashboardJs.includes('SQ.removeSourceTelemetry(state, source.key)')
      && dashboardJs.includes("document.addEventListener('visibilitychange'")
      && dashboardJs.includes("window.addEventListener('pagehide'")
      && !dashboardJs.includes('setInterval(tick')],
  ['Machines edits apply, save, acknowledge, then configure polling',
    (() => {
      const apply = dashboardJs.indexOf('state.dashboard = SQ.applyFleetState(state.dashboard, nextFleet, state.contexts, state.workspace)');
      const save = dashboardJs.indexOf('saveDashboard();                              // persists and acknowledges');
      const configure = dashboardJs.indexOf('poller.configure(state.dashboard.fleet)');
      return apply >= 0 && save > apply && configure > save
        && dashboardJs.includes('SQ.acknowledgeTelemetrySave(state)');
    })()],
  ['Machines panel exists with enable, list, add, import and export controls',
    ['machinesMenu','machinesCount','fleetEnabled','fleetImport','fleetExport','fleetImportFile',
      'fleetError','machinesList','fleetAddForm','fleetAddLabel','fleetAddUrl','machinesNote']
      .every(id => indexHtml.includes(`id="${id}"`))
      && indexHtml.includes('id="fleetError" role="status" aria-live="polite"')
      && indexHtml.indexOf('<script src="fleet-poller.js"></script>') < indexHtml.indexOf('<script src="console.js"></script>')
      && consoleCss.includes('.machines-panel') && consoleCss.includes('.machine-row')],
  ['peer configuration never dispatches hardware writes or loads peer-supplied images',
    !dashboardJs.includes('ImageURL') && !dashboardJs.includes('/Sensor?action=Set')
      && fleetPollerJs.includes("credentials:'omit', redirect:'error'")
      && fleetPollerJs.includes("value.source.url + '/data.json'")],
  ['dashboard storage is isolated behind safe fallback',
    dashboardJs.includes('SQ.createSafeStorage')
      && dashboardJs.includes('const storage = SQ.createSafeStorage(() => window.localStorage)')],
  ['Studio stable regions skip unchanged DOM replacement',
    dashboardJs.includes('function syncKeyedRegion')
      && ['syncKeyedRegion(focusHost','syncKeyedRegion(systems','syncKeyedRegion(network'].every(token => dashboardJs.includes(token))],
  ['Standard keyed regions preserve exact focus context',
    dashboardJs.includes('function captureStandardFocus(element)')
      && dashboardJs.includes('.xp-overlay[data-sid], .rowxp[data-sid], .panel[data-key]')
      && dashboardJs.includes("host?.closest?.('#pinned, #pfd, #panels, #netPanels')")
      && dashboardJs.includes('element.dataset?.mv')
      && dashboardJs.includes('el.dataset.sid = s.id')
      && dashboardJs.includes('focus.element?.isConnected')
      && dashboardJs.includes('restoreStandardFocus(standardFocus)')],
  ['Standard keyed signatures cover controls, ranges, and bounded arc progress',
    dashboardJs.includes('SQ.isPinned(state.dashboard, s.id), state.primaryIds.has(s.id)')
      && dashboardJs.includes('SQ.rangeRenderSignature(range), SQ.rangeRenderSignature(gaugeRange)')
      && dashboardJs.includes('SQ.smoothedFraction(s.id)')
      && dashboardJs.includes('expanded ? SQ.rangeRenderSignature(SQ.rangeFor(s, state.limits, state.dashboard)) : null')
      && dashboardJs.includes("effects.spark ? SQ.historyFor(s.id).map")
      && dashboardJs.includes('control ? [control.raw, control.value] : null')],
  ['poll rate and stateful controls expose accessible state',
    indexHtml.includes('<label class="rate" for="rate">')
      && indexHtml.includes('aria-describedby="ratev"')
      && indexHtml.includes('id="pause" aria-pressed="false"')
      && indexHtml.includes('id="theme" aria-pressed="false"')],
  ['root index has network section', indexHtml.includes('id="netsec"') && indexHtml.includes('id="netPanels"') && indexHtml.includes('id="nettag"')],
  ['root index has adapter restore block', indexHtml.includes('id="netRestore"') && indexHtml.includes('id="netRestoreList"')],
  ['root has Standard context selector', indexHtml.includes('id="dashContext"')],
  ['context selector is labelled Context',
    indexHtml.includes('<span>Context</span>') && indexHtml.includes('aria-label="Standard dashboard context"')],
  ['context options are stable', ['<option value="main">Main</option>',
    '<option value="gaming">Gaming</option>',
    '<option value="storage">Storage</option>'].every(s => indexHtml.includes(s))],
  ['context CSS covers the disabled state', consoleCss.includes('.dash-context')],
  ['console wires context switching',
    dashboardJs.includes('SQ.switchDashboardContext') && dashboardJs.includes("$('#dashContext')")
      && dashboardJs.includes('paintDashContext')],
];
for (const [name, ok] of menuChecks) log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${ok} want=true`);
const totalPass = pass + menuChecks.filter(([, ok]) => ok).length;
const totalFail = fail + menuChecks.filter(([, ok]) => !ok).length;
console.log(log.join('\n'));
console.log(`\nSELFTEST ${totalFail === 0 ? 'PASS' : 'FAIL'} ${totalPass}/${totalPass + totalFail}`);
process.exit(totalFail === 0 ? 0 : 1);
