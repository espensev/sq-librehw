// Headless mirror of console.test.html. Evals console.js with a window shim
// (SQ_NO_BOOT skips the DOM bootstrap) and runs the shared assertion module.
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..');
global.window = { SQ_NO_BOOT: true };
const consoleJs = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js'), 'utf8');
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
  ['root index has network section', indexHtml.includes('id="netsec"') && indexHtml.includes('id="netPanels"') && indexHtml.includes('id="nettag"')],
  ['root index has adapter restore block', indexHtml.includes('id="netRestore"') && indexHtml.includes('id="netRestoreList"')],
];
for (const [name, ok] of menuChecks) log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${ok} want=true`);
const totalPass = pass + menuChecks.filter(([, ok]) => ok).length;
const totalFail = fail + menuChecks.filter(([, ok]) => !ok).length;
console.log(log.join('\n'));
console.log(`\nSELFTEST ${totalFail === 0 ? 'PASS' : 'FAIL'} ${totalPass}/${totalPass + totalFail}`);
process.exit(totalFail === 0 ? 0 : 1);
