// Headless mirror of console.test.html. Evals console.js with a window shim
// (SQ_NO_BOOT skips the DOM bootstrap) and runs the shared assertion module.
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..');
global.window = { SQ_NO_BOOT: true };
const consoleJs = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js'), 'utf8');
const workspaceJs = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js'), 'utf8');
Function('window', 'module', workspaceJs)(global.window, undefined);
eval(consoleJs);
const runConsoleTests = require('./console.tests.js');
const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json'), 'utf8'));
const storage = value => { let slot = value; return { getItem: () => slot, setItem: (k, v) => { slot = v; } }; };
const { pass, fail, log } = runConsoleTests(global.window.SQ, data, storage);
const indexHtml = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html'), 'utf8');
const consoleCss = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css'), 'utf8');
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
    && !consoleJs.includes('/Sensor?action=Set')],
  ['Workspace export materializes a copy without replacing local adaptive state',
    consoleJs.includes('Workspace.materializeProfile(state.workspace, profile.id, state.allSensors)')
      && consoleJs.includes('Workspace.exportProfile(materialized, exactProfile.id, true)')
      && !consoleJs.includes('state.workspace = Workspace.save(storage, materialized)')],
  ['Workspace edits report storage fallback and restore keyboard focus',
    consoleJs.includes('Workspace.saveResult(storage, next)')
      && consoleJs.includes('this change is temporary for this page')
      && consoleJs.includes('captureWorkspaceFocus()')
      && consoleJs.includes('restoreWorkspaceFocus(focus)')
      && consoleJs.includes("workspaceAddPanel:'workspacePanelTitle'")
      && consoleJs.includes("workspaceProfileDuplicate:'workspaceProfileSelect'")],
  ['Workspace poll rendering does not churn its live region',
    consoleJs.includes("status.textContent === message && status.className === 'is-' + nextTone")
      && consoleJs.includes('paintWorkspaceControls(false)')],
  ['Workspace graph labels use the same scale as their plotted bounds',
    consoleJs.includes('const scale = SQ.graphScaleFor(range, history)')
      && consoleJs.includes('sparkAreaSVG(sensor, scale ? [scale.lo, scale.hi] : null)')
      && consoleJs.includes('SQ.graphScaleText(scale, sensor)')],
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
  ['cached rerenders preserve stale telemetry status',
    consoleJs.includes('function render(data, freshTelemetry = false)')
      && consoleJs.includes('if (freshTelemetry && !state.paused)')
      && consoleJs.includes('render(data, true)')],
  ['cached rerenders cannot ingest or persist telemetry',
    consoleJs.includes('SQ.ingestTelemetry(state, allSensors, freshTelemetry)')
      && consoleJs.includes('if (!freshTelemetry) return {ingested:false, samplesChanged:false}')
      && consoleJs.includes('if (ingestion.ingested && ingestion.samplesChanged')],
  ['polling is completion-driven, cancellable, and visibility-aware',
    consoleJs.includes('SQ.createPollController')
      && consoleJs.includes("document.addEventListener('visibilitychange'")
      && consoleJs.includes("window.addEventListener('pagehide'")
      && !consoleJs.includes('setInterval(tick')],
  ['dashboard storage is isolated behind safe fallback',
    consoleJs.includes('SQ.createSafeStorage')
      && consoleJs.includes('const storage = SQ.createSafeStorage(() => window.localStorage)')],
  ['Studio stable regions skip unchanged DOM replacement',
    consoleJs.includes('function syncKeyedRegion')
      && ['syncKeyedRegion(focusHost','syncKeyedRegion(systems','syncKeyedRegion(network'].every(token => consoleJs.includes(token))],
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
    consoleJs.includes('SQ.switchDashboardContext') && consoleJs.includes("$('#dashContext')")
      && consoleJs.includes('paintDashContext')],
];
for (const [name, ok] of menuChecks) log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${ok} want=true`);
const totalPass = pass + menuChecks.filter(([, ok]) => ok).length;
const totalFail = fail + menuChecks.filter(([, ok]) => !ok).length;
console.log(log.join('\n'));
console.log(`\nSELFTEST ${totalFail === 0 ? 'PASS' : 'FAIL'} ${totalPass}/${totalPass + totalFail}`);
process.exit(totalFail === 0 ? 0 : 1);
