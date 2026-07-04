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
console.log(log.join('\n'));
console.log(`\nSELFTEST ${fail === 0 ? 'PASS' : 'FAIL'} ${pass}/${pass + fail}`);
process.exit(fail === 0 ? 0 : 1);
