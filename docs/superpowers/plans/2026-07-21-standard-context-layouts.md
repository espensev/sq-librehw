# Standard Dashboard Context Layouts Implementation Plan

> **Status:** completed 2026-07-21. PR #29 merged as `04a04aa`; the
> docs closeout landed on `master` as `b39aa25`. All checklist items below are
> complete. The final selftest result is 306/306: 14 initial model assertions,
> four markup checks, one wiring check, and two post-review hardening checks over
> the 285-check baseline. This closes the merged source and browser-fixture lane;
> it does not record promotion into a live LibreHardwareMonitor runtime.

**Goal:** Rebuild PR #29's spike properly — a masthead `Context` selector that switches the Standard dashboard between three independently persisted trims (Main/Gaming/Storage) via a materialize-swap over a new `sq.dashboard.contexts.v1` key, leaving `sq.dashboard.v1` the single live authority.

**Architecture:** Pure state functions (`extract`/`apply`/`normalize`/`switch`) join the existing `SQ` model layer in `console.js`; the live key `sq.dashboard.v1` always holds the active context's trim, so every existing read/write/telemetry path is untouched. Parked trims live in the new contexts key, which old builds and old tabs never touch. UI is one `<select>` mirroring the existing `view-theme` label pattern, disabled outside Standard.

**Tech Stack:** Vanilla ES (IIFE, no modules) in embedded web assets; bespoke `webtests/selftest.node.js` harness + `runConsoleTests` eq-assertions + `node --test` wrapper; .NET builds/tests via `dotnet` with explicit `-p:Platform=x64`; live gate via chrome-devtools MCP.

**Spec:** `docs/feature-standard-context-layouts.md` (field lists, invariants, non-goals are normative there).

## Global Constraints

- No changes to `data.json`, CSV, Prometheus, routes, WMI, hardware access, or `AssemblyVersion`.
- No new fields and no version bump in `sq.dashboard.v1`; the new key is `sq.dashboard.contexts.v1`.
- Telemetry caches (`observedMax`, `powerLimitSamples`) and global prefs (`paused`, `rate`, `theme`, `viewTheme`, `studio*`, `sensorAliases`, `rangeOverrides`) must never fork per context.
- Per-context subset is exactly these 13 fields: `hiddenSensorIds`, `pinnedCards`, `panelOrder`, `pinnedOrder`, `graphsEnabled`, `collapsedPanels`, `cardStyle`, `primaryCards`, `primaryCardsCustomized`, `cardOrder`, `rowOrder`, `netAdapterOrder`, `hiddenNetAdapters`.
- All file mutations via Write/Edit tools only (ClaudeGate baseline requirement); never shell redirects.
- `dotnet` build/test always with `-p:Platform=x64` (AnyCPU breaks CsWin32).
- Web assets are LF; do not re-encode line endings.
- Work happens on branch `worktree-dashboard-templates` (PR #29). Use an isolated worktree (`git worktree add .worktrees/context-layouts worktree-dashboard-templates`) or switch this checkout; master must be clean and checked out again at ship time.
- Every commit message ends with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- Selftest total was 285/285 before this feature; it must end green with a strictly larger total (record the number).

---

### Task 1: Reconcile the PR branch to the master baseline (archive the spike)

The branch is 117 commits behind and its three files are superseded. Merge master in and resolve **all three** web assets to master's content, so the spike stays in history but contributes zero diff. (`console.css` and `index.html` auto-merge cleanly — that would *keep* spike code, so overriding all three explicitly is required, not just the conflicted `console.js`.)

**Files:**
- Modify: branch state only (merge commit); no content changes vs master.

**Interfaces:**
- Produces: branch `worktree-dashboard-templates` whose tree is identical to `master` for all files, green baseline for Tasks 2-6.

- [x] **Step 1: Create the worktree and merge**

```bash
cd "E:/SQ_HQ/Monitoring/sq-librehw"
git worktree add .worktrees/context-layouts worktree-dashboard-templates
cd .worktrees/context-layouts
git merge master
```
Expected: `CONFLICT (content): Merge conflict in LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`

- [x] **Step 2: Resolve every spike file to master's content**

```bash
git checkout master -- \
  LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js \
  LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css \
  LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html
git add -A
```

- [x] **Step 3: Commit the merge**

```bash
git commit -F - <<'EOF'
chore(branch): reconcile spike branch to master baseline

Merge master and resolve console.js/console.css/index.html to master's
content. The Jul 4 template-tabs spike stays archived in branch history;
the proper rebuild lands in the following commits per
docs/feature-standard-context-layouts.md.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
```

- [x] **Step 4: Verify zero diff vs master and a green baseline**

```bash
git diff master...HEAD --stat
node webtests/selftest.node.js | tail -1
```
Expected: empty diffstat; `SELFTEST PASS 285/285`

---

### Task 2: Context state model (pure functions + tests)

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (insert immediately **before** the line `SQ.migrateLegacyState = function (storage, state) {`)
- Test: `webtests/console.tests.js` (insert the new block immediately before the function's final `return` — find it with `grep -n "return { pass, fail" webtests/console.tests.js`)

**Interfaces:**
- Consumes: `SQ.normalizeDashboardState`, `SQ.defaultDashboardState`, `SQ.loadDashboardState`, `SQ.saveDashboardState` (all existing).
- Produces (used by Task 4): `SQ.DASHBOARD_CONTEXTS: string[]`, `SQ.normalizeDashboardContext(value) -> 'main'|'gaming'|'storage'`, `SQ.extractContextLayout(dashboard) -> layoutSubset`, `SQ.applyContextLayout(dashboard, layout) -> dashboard`, `SQ.normalizeContextState(value) -> {version:1, active, saved}`, `SQ.loadContextState(storage)`, `SQ.saveContextState(storage, value)`, `SQ.switchDashboardContext(storage, dashboard, next) -> {dashboard, contexts, changed}`.

- [x] **Step 1: Write the failing tests**

Append inside `runConsoleTests` (before its final `return`):

```js
    // Standard dashboard contexts (feature-standard-context-layouts)
    const ctxStore = (() => { const m = new Map(); return {
      getItem: k => (m.has(k) ? m.get(k) : null),
      setItem: (k, v) => { m.set(k, String(v)); },
      removeItem: k => { m.delete(k); } }; })();
    eq('context default state', S.normalizeContextState(null), {version:1, active:'main', saved:{}});
    eq('context unknown active falls back', S.normalizeContextState({active:'x'}).active, 'main');
    eq('context saved drops active entry', S.normalizeContextState({active:'gaming', saved:{gaming:{hiddenSensorIds:['/a/1']}, storage:{}}}).saved.gaming, undefined);
    const ctxBase = S.normalizeDashboardState({hiddenSensorIds:['/a/1'], observedMax:{'/a/1':42}, theme:'light', graphsEnabled:true});
    const ctxLayout = S.extractContextLayout(ctxBase);
    eq('layout extract keeps curation', ctxLayout.hiddenSensorIds, ['/a/1']);
    eq('layout extract excludes globals', ['observedMax','powerLimitSamples','theme','viewTheme','paused','rate'].map(k => k in ctxLayout), [false,false,false,false,false,false]);
    const sw1 = S.switchDashboardContext(ctxStore, ctxBase, 'gaming');
    eq('first switch seeds from current trim', sw1.dashboard.hiddenSensorIds, ['/a/1']);
    eq('first switch parks main', S.loadContextState(ctxStore).saved.main.hiddenSensorIds, ['/a/1']);
    eq('switch preserves telemetry cache', sw1.dashboard.observedMax, {'/a/1':42});
    eq('switch preserves global theme', sw1.dashboard.theme, 'light');
    eq('switch moves active pointer', S.loadContextState(ctxStore).active, 'gaming');
    const ctxGaming = S.normalizeDashboardState(Object.assign({}, sw1.dashboard, {hiddenSensorIds:['/a/1','/b/2']}));
    const sw2 = S.switchDashboardContext(ctxStore, ctxGaming, 'main');
    eq('return restores main trim', sw2.dashboard.hiddenSensorIds, ['/a/1']);
    eq('gaming trim parked separately', S.loadContextState(ctxStore).saved.gaming.hiddenSensorIds, ['/a/1','/b/2']);
    eq('same-context switch is a no-op', S.switchDashboardContext(ctxStore, sw2.dashboard, 'main').changed, false);
    eq('context storage failure stays safe', S.switchDashboardContext(null, ctxBase, 'storage').dashboard.hiddenSensorIds, ['/a/1']);
```

- [x] **Step 2: Run tests to verify they fail**

```bash
node webtests/selftest.node.js | tail -3
```
Expected: FAIL lines mentioning `normalizeContextState is not a function` (or similar) and a non-zero fail count.

- [x] **Step 3: Implement the model**

Insert into `console.js` immediately before `SQ.migrateLegacyState = function (storage, state) {`:

```js
  // Standard dashboard contexts: sq.dashboard.v1 always holds the ACTIVE
  // context's trim; inactive trims park in CONTEXT_STORAGE_KEY. Only the
  // curation subset forks; telemetry caches and device prefs never do.
  const CONTEXT_STORAGE_KEY = 'sq.dashboard.contexts.v1';
  const DASHBOARD_CONTEXTS = ['main', 'gaming', 'storage'];
  const CONTEXT_LAYOUT_FIELDS = [
    'hiddenSensorIds', 'pinnedCards', 'panelOrder', 'pinnedOrder',
    'graphsEnabled', 'collapsedPanels', 'cardStyle',
    'primaryCards', 'primaryCardsCustomized', 'cardOrder', 'rowOrder',
    'netAdapterOrder', 'hiddenNetAdapters'
  ];
  function cleanContext(value) {
    return DASHBOARD_CONTEXTS.includes(value) ? value : 'main';
  }
  SQ.DASHBOARD_CONTEXTS = DASHBOARD_CONTEXTS.slice();
  SQ.normalizeDashboardContext = cleanContext;
  SQ.extractContextLayout = function (dashboard) {
    const cfg = SQ.normalizeDashboardState(dashboard);
    const out = {};
    CONTEXT_LAYOUT_FIELDS.forEach(k => { out[k] = cfg[k]; });
    return out;
  };
  SQ.applyContextLayout = function (dashboard, layout) {
    const cfg = SQ.normalizeDashboardState(dashboard);
    const src = layout && typeof layout === 'object' ? layout : {};
    CONTEXT_LAYOUT_FIELDS.forEach(k => { if (k in src) cfg[k] = src[k]; });
    return SQ.normalizeDashboardState(cfg);
  };
  SQ.normalizeContextState = function (value) {
    const base = { version: 1, active: 'main', saved: {} };
    if (!value || typeof value !== 'object') return base;
    base.active = cleanContext(value.active);
    const saved = value.saved;
    if (saved && typeof saved === 'object' && !Array.isArray(saved))
      DASHBOARD_CONTEXTS.forEach(k => {
        if (k !== base.active && saved[k] && typeof saved[k] === 'object')
          base.saved[k] = SQ.extractContextLayout(SQ.applyContextLayout(SQ.defaultDashboardState(), saved[k]));
      });
    return base;
  };
  SQ.loadContextState = function (storage) {
    if (!storage || typeof storage.getItem !== 'function') return SQ.normalizeContextState(null);
    try {
      const raw = storage.getItem(CONTEXT_STORAGE_KEY);
      return SQ.normalizeContextState(raw ? JSON.parse(raw) : null);
    } catch {
      return SQ.normalizeContextState(null);
    }
  };
  SQ.saveContextState = function (storage, value) {
    const state = SQ.normalizeContextState(value);
    try {
      if (storage && typeof storage.setItem === 'function')
        storage.setItem(CONTEXT_STORAGE_KEY, JSON.stringify(state));
    } catch {}
    return state;
  };
  SQ.switchDashboardContext = function (storage, dashboard, nextContext) {
    const contexts = SQ.loadContextState(storage);
    const next = cleanContext(nextContext);
    const cfg = SQ.normalizeDashboardState(dashboard);
    if (next === contexts.active) return { dashboard: cfg, contexts, changed: false };
    const parked = Object.assign({}, contexts.saved);
    parked[contexts.active] = SQ.extractContextLayout(cfg);
    const seed = parked[next] || SQ.extractContextLayout(cfg);
    delete parked[next];
    const applied = SQ.applyContextLayout(cfg, seed);
    // Persist the pointer+parked trims first: a partial failure duplicates a
    // trim rather than losing one (see spec, State and behavior).
    const nextContexts = SQ.saveContextState(storage, { version: 1, active: next, saved: parked });
    const nextDashboard = SQ.saveDashboardState(storage, applied);
    return { dashboard: nextDashboard, contexts: nextContexts, changed: true };
  };
```

- [x] **Step 4: Run tests to verify they pass**

```bash
node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
node webtests/selftest.node.js | tail -1
node --test webtests/console.tests.js 2>&1 | tail -3
```
Result: syntax OK; `SELFTEST PASS 299/299` at this task boundary (285 + 14);
node --test passed. Two further model hardening checks landed during review.

- [x] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -F - <<'EOF'
feat(web): add dashboard context state model

Pure extract/apply/normalize/switch functions over a new
sq.dashboard.contexts.v1 key. sq.dashboard.v1 stays the single live
authority; only the 13-field curation subset forks per context, and
telemetry caches, theme, view, pause, rate, Studio prefs, aliases, and
range overrides never do. Covered by 14 new model assertions.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Masthead selector markup + CSS + markup self-test

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` (insert before the line containing `<label class="view-theme"><span>Dashboard</span>`)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` (insert after the line `input[type=range]{accent-color:var(--cy)}`)
- Test: `webtests/selftest.node.js` (append entries inside `menuChecks`, after the `['root index has adapter restore block', ...]` entry)

**Interfaces:**
- Produces: `#dashContext` select with option values `main|gaming|storage` (Task 4 wires it); `.dash-context` CSS hook.

- [x] **Step 1: Write the failing markup assertions**

Append to `menuChecks` in `webtests/selftest.node.js`:

```js
  ['root has Standard context selector', indexHtml.includes('id="dashContext"')],
  ['context selector is labelled Context',
    indexHtml.includes('<span>Context</span>') && indexHtml.includes('aria-label="Standard dashboard context"')],
  ['context options are stable', ['<option value="main">Main</option>',
    '<option value="gaming">Gaming</option>',
    '<option value="storage">Storage</option>'].every(s => indexHtml.includes(s))],
  ['context CSS covers the disabled state', consoleCss.includes('.dash-context')],
```

- [x] **Step 2: Run to verify they fail**

```bash
node webtests/selftest.node.js | tail -6
```
Expected: 4 FAIL lines for the new checks.

- [x] **Step 3: Add the markup and CSS**

`index.html`, immediately before the `Dashboard` selector label:

```html
    <label class="view-theme dash-context"><span>Context</span><select id="dashContext" aria-label="Standard dashboard context">
      <option value="main">Main</option>
      <option value="gaming">Gaming</option>
      <option value="storage">Storage</option>
    </select></label>
```

`console.css`, after `input[type=range]{accent-color:var(--cy)}`:

```css
/* Standard context selector: mirrors .view-theme; disabled outside Standard */
.dash-context select:disabled{opacity:.55;cursor:not-allowed}
```

- [x] **Step 4: Run to verify they pass**

```bash
node webtests/selftest.node.js | tail -1
```
Expected: `SELFTEST PASS <prev+4>/<prev+4>`

- [x] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css webtests/selftest.node.js
git commit -F - <<'EOF'
feat(web): add Standard context selector markup and styles

Labelled Context select beside the Dashboard selector with stable
main/gaming/storage values, plus a disabled-state style. Asserted by
four new markup self-tests.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Boot wiring — paint, gate, and switch

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js`:
  - state init: add `contexts` beside the dashboard load (find with `grep -n "migrateLegacyState(storage" LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — the boot assigns the dashboard state near `const storage = SQ.createSafeStorage(() => window.localStorage);`, line ≈1189)
  - `paintDashContext()` definition: insert before `function paintTheme() {`
  - call `paintDashContext();` as the last line of `paintViewTheme()` (after `if (workspace) paintWorkspaceControls();`)
  - `onchange` handler: insert before `$('#studioAccent').onchange = e => {`
- Test: `webtests/selftest.node.js` (one wiring-presence entry in `menuChecks`)

**Interfaces:**
- Consumes: `SQ.loadContextState`, `SQ.switchDashboardContext` (Task 2); `#dashContext` (Task 3); existing `paintGraphs()`, `rerender()`, `state.dashboard`.
- Produces: `state.contexts` (`{version, active, saved}`) live in the boot scope; selector disabled whenever `state.dashboard.viewTheme !== 'standard'`.

- [x] **Step 1: Write the failing wiring assertion**

Append to `menuChecks` in `webtests/selftest.node.js`:

```js
  ['console wires context switching',
    consoleJs.includes('SQ.switchDashboardContext') && consoleJs.includes("$('#dashContext')")
      && consoleJs.includes('paintDashContext')],
```

- [x] **Step 2: Run to verify it fails**

```bash
node webtests/selftest.node.js | tail -3
```
Expected: 1 FAIL line (`console wires context switching`).

- [x] **Step 3: Implement the wiring**

State init — where the boot creates its state from storage, load the contexts pointer too:

```js
    state.contexts = SQ.loadContextState(storage);
```
(If boot state is built as an object literal, add `contexts: SQ.loadContextState(storage),` as a sibling of the dashboard field instead — match the surrounding idiom.)

Paint function, inserted before `function paintTheme() {`:

```js
    function paintDashContext() {
      const select = $('#dashContext');
      if (!select) return;
      const standard = state.dashboard.viewTheme === 'standard';
      select.value = state.contexts.active;
      select.disabled = !standard;
      select.title = standard ? 'Standard dashboard context'
        : 'Context applies to the Standard dashboard';
    }
```

Last line of `paintViewTheme()` (after `if (workspace) paintWorkspaceControls();`):

```js
      paintDashContext();
```

Handler, inserted before `$('#studioAccent').onchange = e => {`:

```js
    $('#dashContext').onchange = e => {
      const result = SQ.switchDashboardContext(storage, state.dashboard, e.target.value);
      state.dashboard = result.dashboard;
      state.contexts = result.contexts;
      paintDashContext();
      paintGraphs();
      rerender();
    };
```

Note: `switchDashboardContext` persists both keys itself — do **not** also call `saveDashboard()` here, which would be redundant but harmless; keep the single write path.

- [x] **Step 4: Run to verify it passes**

```bash
node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
node webtests/selftest.node.js | tail -1
```
Result: syntax OK; `SELFTEST PASS 304/304` at this task boundary
(285 + 14 + 4 + 1). Post-review hardening raised the merged total to 306/306.

- [x] **Step 5: Commit**

```bash
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/selftest.node.js
git commit -F - <<'EOF'
feat(web): wire context switching into dashboard boot

Load the active context at boot, paint and gate the selector on every
view change (disabled outside Standard), and switch trims through the
single materialize-swap write path with graphs repaint and rerender.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Full deterministic gate

**Files:** none modified (verification only; fix-forward if anything fails, folding fixes into the responsible task's commit style).

- [x] **Step 1: Run the complete gate**

```bash
node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
node --check LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js
node webtests/selftest.node.js | tail -1
node --test webtests/console.tests.js webtests/workspace.tests.js 2>&1 | tail -3
```
Expected: both checks silent; selftest green at the recorded total; node --test all pass.

- [x] **Step 2: .NET suite and both Release builds (isolated outputs)**

```bash
dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj -p:Platform=x64 2>&1 | tail -5
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 2>&1 | tail -3
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 2>&1 | tail -3
```
Expected: tests pass (150 passed, one opt-in skip is normal); both builds zero warnings/errors. Do not stop or replace any running local monitor process.

---

### Task 6: Live browser matrix + screenshots + spec log

**Files:**
- Modify: `docs/feature-standard-context-layouts.md` (check acceptance boxes, append Verification Log entry with the recorded selftest total and live results)

- [x] **Step 1: Stage and serve the fixture**

```bash
STAGE="$TMPDIR_OR_SCRATCH/ctx-live"   # use the session scratchpad dir
mkdir -p "$STAGE"
cp -r LibreHardwareMonitor.Windows.Forms/Resources/Web/* "$STAGE/"
cp webtests/fixture.data.json "$STAGE/data.json"
(cd "$STAGE" && python -m http.server 8123)   # run in background
```

- [x] **Step 2: Drive the matrix via chrome-devtools MCP**

Known session hazards (project memory): if the browser drops with "already running for chrome-profile", kill `chrome-devtools-mcp` Chrome processes and reopen; close any stale dashboard tabs first — old tabs running pre-context code can otherwise confuse persistence checks.

At `http://localhost:8123/`, dark theme, desktop size:
1. Confirm `#dashContext` shows `Main`, enabled under Standard.
2. Hide one sensor and star one card; switch Context to `Gaming`; confirm the trim seeds identically; hide a second sensor in Gaming.
3. Switch back to `Main`: the second hide must NOT appear; switch to `Gaming`: it must.
4. Reload: both trims persist; active context restores.
5. Toggle theme light, pause, and change poll rate; switch contexts: all three survive unchanged.
6. Switch Dashboard to Studio then Workspace: selector disables with the explanatory title; back to Standard re-enables.
7. Check the console drawer: zero errors/warnings.
8. Screenshots at rest in dark AND light (the measurement gate alone is not sufficient — visual check is required by project memory).
9. Resize to 390 px width: selector usable, no horizontal overflow; screenshot.

- [x] **Step 3: Record results in the spec and commit**

Check off `docs/feature-standard-context-layouts.md` acceptance boxes that passed, append a dated Verification Log entry (selftest total, .NET counts, live matrix outcome), then:

```bash
git add docs/feature-standard-context-layouts.md
git commit -F - <<'EOF'
docs(web): record context-layouts verification

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Ship — push, update PR #29, merge, sync master

- [x] **Step 1: Push and update the PR to reflect the rebuild**

```bash
git push origin worktree-dashboard-templates
gh pr edit 29 --repo espensev/sq-librehw \
  --title "feat(web): per-context Standard dashboard layouts (rebuilt from spike)" \
  --body-file - <<'EOF'
Rebuilds the Jul 4 template-tabs spike properly per
`docs/feature-standard-context-layouts.md` (plan:
`docs/superpowers/plans/2026-07-21-standard-context-layouts.md`).

- `Context` dropdown (Main/Gaming/Storage) beside the Dashboard selector,
  disabled outside Standard.
- Materialize-swap persistence: `sq.dashboard.v1` stays the single live
  authority; parked trims in new `sq.dashboard.contexts.v1`; telemetry
  caches and global prefs never fork.
- Spike archived unmerged-as-code in branch history via the baseline
  reconcile merge; net spike diff is zero.
- Gates: full selftest green (record total), node --test suites, .NET
  suite, both x64 Release builds, live browser matrix dark/light at
  desktop + 390 px with screenshots.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
```

- [x] **Step 2: Merge PR #29 with a merge commit (keeps the spike lineage)**

```bash
gh pr merge 29 --repo espensev/sq-librehw --merge
```

- [x] **Step 3: Sync master, verify, clean up**

```bash
cd "E:/SQ_HQ/Monitoring/sq-librehw"
git checkout master && git pull origin master
node webtests/selftest.node.js | tail -1
git worktree remove .worktrees/context-layouts
git branch -d worktree-dashboard-templates
git push origin --delete worktree-dashboard-templates   # optional: PR #29 preserves the diff
```
Expected: selftest green on master at the recorded total.

- [x] **Step 4: Close the docs loop**

Update `docs/README.md`: flip this feature's line from planned to shipped in the doc map, refresh `Updated:`, and remove the implement-step from `Current` once shipped. Update the spec `Status:` line to shipped-with-verification. Commit to master:

```bash
git add docs/README.md docs/feature-standard-context-layouts.md
git commit -F - <<'EOF'
docs: close out Standard context layouts

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
git push origin master
```

---

## Self-review (spec coverage)

- Selector markup/labels/values → Task 3; disabled gating → Task 4 + live matrix 6.
- 13-field isolation, telemetry/global preservation, seed-from-current, no-op switch, storage-failure safety → Task 2 tests t1-t16.
- Reload persistence, cross-view gating, both themes, 390 px, console cleanliness → Task 6.
- Node totals, .NET suite, both x64 builds → Tasks 4-5.
- Spike archived, PR #29 merged with lineage, master synced, docs closed → Tasks 1 and 7.
- Non-goals honored: no `sq.dashboard.v1` schema change (Task 2 model), no hash routing, no Workspace/Studio changes, no backend changes (no server files touched anywhere in the plan).
