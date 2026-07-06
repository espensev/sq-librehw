// Headless mirror of console.test.html. Evals console.js with a window shim
// (SQ_NO_BOOT skips the DOM bootstrap) and runs the shared assertion module.
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '..');
global.window = { SQ_NO_BOOT: true };
eval(fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js'), 'utf8'));
const runConsoleTests = require('./console.tests.js');
const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.data.json'), 'utf8'));
const storage = value => { let slot = value; return { getItem: () => slot, setItem: (k, v) => { slot = v; } }; };
const { pass, fail, log } = runConsoleTests(global.window.SQ, data, storage);
const indexHtml = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html'), 'utf8');
const previewHtml = fs.readFileSync(path.join(ROOT, 'LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html'), 'utf8');
const menuChecks = [
  ['root menu links card-truth preview', indexHtml.includes('href="/dash/cardtruth/"')],
  ['root menu links data.json', indexHtml.includes('href="/data.json"')],
  ['root menu links metrics', indexHtml.includes('href="/metrics"')],
  ['preview css is root-absolute for no-slash route', previewHtml.includes('href="/dash/cardtruth/console.css"')],
  ['preview js is root-absolute for no-slash route', previewHtml.includes('src="/dash/cardtruth/console.js"')],
  ['preview index has no route-relative shell assets', !previewHtml.includes('href="console.css"') && !previewHtml.includes('src="console.js"')]
];
for (const [name, ok] of menuChecks) log.push(`${ok ? 'ok  ' : 'FAIL'}  ${name}  got=${ok} want=true`);
const totalPass = pass + menuChecks.filter(([, ok]) => ok).length;
const totalFail = fail + menuChecks.filter(([, ok]) => !ok).length;
console.log(log.join('\n'));
console.log(`\nSELFTEST ${totalFail === 0 ? 'PASS' : 'FAIL'} ${totalPass}/${totalPass + totalFail}`);
process.exit(totalFail === 0 ? 0 : 1);
