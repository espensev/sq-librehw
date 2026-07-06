# C1 — Network Adapter Subgroups Implementation Plan

> **STATUS: COMPLETE (2026-07-07).** Executed subagent-driven (TDD) on `feat/web-network-subgroups-c1`,
> commits `e48173c..555e7ae` (merge pending final whole-branch review). All 7 tasks landed + task-reviewed
> (0 Critical / 0 Important each). Gates: selftest 227/227, golden 42/42, `net472`+`net10.0-windows` Release
> x64 builds 0/0, `git diff --check` clean. Live-verified in a real browser (dark+light, across reloads) on a
> 37-NIC host: 5 active adapter panels, no-op guard holds, reorder/hide/restore all work, popover stays open on
> Show, state persists, zero console errors. Follow-up candidate: idle-Show (see the card-truth verification log).
> Execution ledger: `.superpowers/sdd/progress.md` (C1 section).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Break the single merged Network panel into one panel per network adapter, each reorderable (▲▼ + drag), hideable, and restorable from the masthead Sensors popover.

**Architecture:** Pure `SQ.*` model helpers group `cls === 'nic'` sensors by stable adapter key (`s.hwid`, `hw:` fallback) and make `SQ.buildPanelItems` emit one `net: true` item per *active* adapter (activity rule unchanged: any `Throughput` sensor with `raw > 0`), applying `netAdapterOrder` + `hiddenNetAdapters` (both already normalized in state). The render side gives adapters their own **`#netsec` section with a `#netPanels` grid** — separate from `#panels` — so the existing container-routed drag machinery (`endDrag` switches on `container.id`, `console.js:1497`) writes `netAdapterOrder` with a one-line branch and `panelOrder` can never absorb adapter keys. Adapter ▲▼ reorder goes through a dedicated `moveAdapter` mutator carrying the B3 no-op guard. Hidden adapters surface in a restore section inside the existing Sensors popover, and their sensors report `offscreen` in the popover visibility model.

**Tech Stack:** Vanilla JS/CSS/HTML (embedded resources), Node DOM-less selftest (`webtests/selftest.node.js`), .NET 10 / net472 x64 builds, C# golden tests.

**Spec sources:** continuation handoff §0 + §5 Slice 5B (`2026-07-06-web-dashboard-v3-continuation-handoff.md`), v3-next-plan §4 row C1 (`2026-07-06-web-dashboard-v3-next-plan.md`). The §0 resume brief supersedes the older §5 "id prefix" wording: adapter key = `s.hwid`, **not** a re-parsed `/nic/{GUID}` string.

## Global Constraints

- No `data.json` / server / contract change. State stays browser-local under `sq.dashboard.v1`; user-owned writes go through the existing multi-tab-safe save path (`SQ.saveTelemetryState` already merge-covers `netAdapterOrder`/`hiddenNetAdapters` — see `webtests/console.tests.js:392-400`).
- Vanilla JS/CSS/HTML only. No framework.
- No host-specific labels, limits, or sensor IDs in **product** code. Synthetic labels (e.g. `Realtek Gaming 2.5GbE`) appear in tests only (precedent: `KINGSTON` at `console.tests.js:187`).
- Raw label + `SensorId`/key stay visible wherever a friendly name shows (popover restore rows show the adapter key in `<code>`).
- C# golden tests stay green (42) and web selftest stays green (192 + the new assertions added here).
- Build requires `-p:Platform=x64`. The running EXE locks the DLL/EXE — **stop the app before rebuilding**.
- Both themes (dark AND light) are first-class; every visual change is checked in both.
- Stable `/` assets only. `/dash/cardtruth/` preview assets are intentionally untouched (divergence is accepted until the Phase E2 delta audit, matching B1–B3 precedent). Preview `node --check` stays in the gate to prove it still parses.
- Carry the B3 lessons (handoff §12): reorder no-op guard on every new reorder surface (§12.2); adapter-header controls always-visible because panel heads have no `tabindex` (§12.3); live browser console-clean check across poll ticks is the only gate that catches dangling references (§12.4); close stale pre-rebuild tabs before trusting persistence tests (§12.5).

## Decisions Locked In This Plan

| Decision | Rationale |
|---|---|
| Adapters render in their own `#netsec`/`#netPanels` section, not inside `#panels` | Drag machinery routes writes by `container.id` (`console.js:1496-1501`); a separate container isolates adapter drag to `netAdapterOrder` and keeps `panelOrder` free of nic keys with near-zero code. Cross-container drops are impossible by construction. |
| `netAdapterOrder` owns adapter order; `movePanel` merges over **non-nic** panel keys only | Otherwise the first hardware-panel move materializes adapter keys into `panelOrder`, which would then pin adapters and make adapter ▲▼ appear dead. |
| No adapter-order "Reset order" button in C1 | The no-op guard keeps `netAdapterOrder` empty until a real move, so no spurious reset affordance is needed (the B3 failure mode). A reset control is D-phase polish if wanted. |
| Idle-adapter filtering unchanged; only **explicitly hidden** adapters mark their sensors `offscreen` | Spec: "Idle-adapter filtering can stay." Idle-adapter sensors showing `visible` in the popover is a pre-existing nuance, deferred (noted in Out of Scope). |
| `webtests/fixture.data.json` untouched | Its one Ethernet adapter is idle (all raw 0) and useful as-is; multi-adapter cases use synthetic inline sensor arrays (established pattern, `console.tests.js:176`). |
| Legacy keys `panel:network` (panelOrder/rowOrder/collapsedPanels) become inert, no migration | `mergeOrder`/`applyOrder` ignore unknown keys; `cleanOrderMap`/`cleanCollapsedMap` keep them harmlessly. Adapter panels key by hwid, and their `hw` text is the adapter name, so the legacy `Network` text-fallback in `isPanelCollapsed` can never match them. |

## File Structure

| File | Responsibility in C1 |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | All model + render changes. SQ block (before `window.SQ = SQ` at `:744`): hoisted `SQ.mergeOrder`/`SQ.moveKey`, new `SQ.netAdapterKey`/`SQ.buildNetAdapters`, per-adapter `SQ.buildPanelItems`, `offscreen` rule in `SQ.sensorVisibility`. Boot block: `renderPanels` split, `panelEl` nic controls, `moveAdapter`/`hideAdapter`/`showAdapter`, `movePanel` merge fix, `endDrag` branch, popover restore render + handler. |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` | New `#netsec` section (after Subsystems), new `#netRestore` block inside the Sensors popover. |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | Two small rules for the popover restore section head/divider. Everything else reuses `.sec-head`, `.grid`, `.panel-move .ctl`, `.sensor-choice`, `.iconbtn`. |
| `webtests/console.tests.js` | New DOM-less assertions: order helpers, adapter grouping, per-adapter panel items, offscreen visibility. |
| `webtests/selftest.node.js` | Two structural index.html string checks (`#netPanels`/`#netsec`, `#netRestoreList`). |
| `docs/superpowers/plans/2026-07-06-web-network-subgroups-c1.md` | This plan; doubles as the execution record (checkboxes). |

Key existing anchors (verified 2026-07-06 at `adf1b13`): `buildPanelItems` `console.js:712-742`; `mergeOrder`/`moveKey` `:814-827`; `moveRow` `:835`; `movePanel` `:846-852` (guard at `:849`); `panelEl` `:1151-1200`; `renderPanels` `:1201-1210`; `renderSensorsPopover` `:1212-1243`; popover handlers `:1295-1313`; card move-left/right merge `:1361-1366`; `orderedKeysFor` `:1417`; `endDrag` container routing `:1489-1505`; `render()` sets `state.allSensors` at `:903` and calls `renderPanels(visibleSensors)` at `:911`.

---

### Task 0: Branch + docs baseline

**Files:**
- Commit (pre-existing, uncommitted): `docs/superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md` (§0 Resume Brief added by the prior session)
- Commit (new): `docs/superpowers/plans/2026-07-06-web-network-subgroups-c1.md` (this plan)

**Interfaces:**
- Consumes: clean `master` at `adf1b13` (only the handoff doc is dirty).
- Produces: branch `feat/web-network-subgroups-c1` with the docs baseline committed; all later tasks commit onto it.

- [ ] **Step 1: Create the branch**

```powershell
git checkout master
git pull --ff-only origin master
git checkout -b feat/web-network-subgroups-c1
```

Expected: new branch from `adf1b13` (or newer ff); `git status --short` shows only the two docs above.

- [ ] **Step 2: Commit the docs**

```powershell
git add docs/superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md docs/superpowers/plans/2026-07-06-web-network-subgroups-c1.md
git commit -m "docs(web): add v3 resume brief + C1 network subgroups plan"
```

- [ ] **Step 3: Baseline check (must be green before touching code)**

```powershell
node webtests\selftest.node.js
```

Expected: `SELFTEST PASS 192/192`.

---

### Task 1: Expose `SQ.mergeOrder` / `SQ.moveKey` (pure hoist + no-op reference tests)

The B3 no-op guard (`if (next === merged) return;`) depends on `moveKey` returning the **same array reference** on out-of-bounds and `mergeOrder` materializing a full order from an empty saved order. Both functions are currently private to the boot block (`console.js:814-827`), so that contract is untestable in the DOM-less selftest — handoff §12.2 says to test the no-op path. Hoist them into the `SQ.*` block unchanged and update the three boot call sites.

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:814-827` (remove), SQ block near `:303` (add), call sites `:843`, `:847-848`, `:1362`
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: module-scope `cleanStringList` (already shared by SQ block and boot block).
- Produces: `SQ.mergeOrder(saved, keys) -> string[]` and `SQ.moveKey(list, key, delta) -> string[]` (returns the *same reference* when the move is out-of-bounds or the key is missing). Tasks 5's `moveAdapter` and the existing `moveRow`/`movePanel`/card-move call sites use these.

- [ ] **Step 1: Write the failing tests** — append inside the test function in `webtests/console.tests.js` (next to the reorder tests near `:191-199`):

```js
    // --- C1 T1: exposed order helpers (mergeOrder/moveKey no-op contract) ---
    eq('mergeOrder keeps saved-first then appends missing', S.mergeOrder(['b'], ['a','b','c']), ['b','a','c']);
    eq('mergeOrder drops unknown saved keys', S.mergeOrder(['zz','c'], ['a','b','c']), ['c','a','b']);
    eq('mergeOrder empty saved materializes keys', S.mergeOrder([], ['a','b']), ['a','b']);
    eq('moveKey swaps within bounds', S.moveKey(['a','b','c'], 'b', 1), ['a','c','b']);
    eq('moveKey OOB returns same reference (no-op guard)', (() => { const m = S.mergeOrder([], ['a','b']); return S.moveKey(m, 'a', -1) === m; })(), true);
    eq('moveKey bottom-down returns same reference', (() => { const m = ['a','b']; return S.moveKey(m, 'b', 1) === m; })(), true);
    eq('moveKey missing key returns same reference', (() => { const m = ['a','b']; return S.moveKey(m, 'zz', 1) === m; })(), true);
```

- [ ] **Step 2: Run to verify failure**

Run: `node webtests\selftest.node.js`
Expected: FAIL — `S.mergeOrder is not a function`.

- [ ] **Step 3: Hoist the two functions.** Delete `function mergeOrder(...)` and `function moveKey(...)` from the boot block (`console.js:814-827`) and add to the SQ block, directly above `SQ.applyOrder` (`:303`), verbatim logic:

```js
  SQ.mergeOrder = function (saved, keys) {
    const set = new Set(keys);
    const merged = cleanStringList(saved).filter(k => set.has(k));
    keys.forEach(k => { if (!merged.includes(k)) merged.push(k); });
    return merged;
  };
  SQ.moveKey = function (list, key, delta) {
    const i = list.indexOf(key);
    const j = i + delta;
    if (i < 0 || j < 0 || j >= list.length) return list;
    const next = list.slice();
    [next[i], next[j]] = [next[j], next[i]];
    return next;
  };
```

Update the three boot call sites to the `SQ.` names:
- `moveRow` (`:843`): `state.dashboard.rowOrder[groupKey] = SQ.moveKey(SQ.mergeOrder(state.dashboard.rowOrder[groupKey], rows), id, delta);`
- `movePanel` (`:847-848`): `const merged = SQ.mergeOrder(state.dashboard.panelOrder, state.panelItems.map(i => i.key));` and `const next = SQ.moveKey(merged, key, delta);`
- card move-left/right (`:1362`): `const next = SQ.moveKey(SQ.mergeOrder(container.id === 'pinned' ? state.dashboard.pinnedOrder : state.dashboard.cardOrder, keys), cell.dataset.key, btn.dataset.act === 'move-left' ? -1 : 1);`

Then confirm no bare references remain: `Select-String` for `[^.]mergeOrder\(` / `[^.]moveKey\(` in `console.js` must only hit the two `SQ.` definitions.

- [ ] **Step 4: Verify pass**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS, 192 + 7 = 199 assertions, zero FAIL lines.

- [ ] **Step 5: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "refactor(web): expose mergeOrder/moveKey as SQ helpers with no-op contract tests (C1)"
```

---

### Task 2: Pure adapter grouping — `SQ.netAdapterKey` + `SQ.buildNetAdapters`

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` (SQ block, insert directly above `SQ.buildPanelItems` at `:712`)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: flattened sensor objects `{ cls, hw, hwid, type, text, raw, value, id }` (`SQ.flatten` shape, `console.js:38-43`).
- Produces:
  - `SQ.netAdapterKey(s) -> string` — `s.hwid`, else `'hw:' + s.hw` (matches panel keying at `:717`).
  - `SQ.buildNetAdapters(sensors) -> [{ key, hw, label, ss, active }]` — one entry per nic adapter in first-seen order; `label` deduped with the `#N` pattern; `active` = any `Throughput` sensor with `raw > 0`. Used by Task 3 (panel items), Task 5 (nettag hidden count), Task 6 (popover restore).

- [ ] **Step 1: Write the failing tests** — append in `webtests/console.tests.js`:

```js
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
```

- [ ] **Step 2: Run to verify failure**

Run: `node webtests\selftest.node.js`
Expected: FAIL — `S.netAdapterKey is not a function`.

- [ ] **Step 3: Implement** — insert above `SQ.buildPanelItems` (`console.js:712`):

```js
  SQ.netAdapterKey = function (s) {
    return (s && s.hwid) || ('hw:' + ((s && s.hw) || ''));
  };
  SQ.buildNetAdapters = function (sensors) {
    if (!Array.isArray(sensors)) return [];
    const byKey = new Map();
    sensors.forEach(s => {
      if (!s || s.cls !== 'nic') return;
      const key = SQ.netAdapterKey(s);
      if (!byKey.has(key)) byKey.set(key, { key, hw: s.hw, ss: [] });
      byKey.get(key).ss.push(s);
    });
    const adapters = [...byKey.values()];
    const byLabel = new Map();
    adapters.forEach(a => { (byLabel.get(a.hw) || byLabel.set(a.hw, []).get(a.hw)).push(a); });
    [...byLabel.values()].forEach(group => {
      if (group.length > 1) group.forEach((a, i) => { a.label = `${a.hw} #${i + 1}`; });
      else group[0].label = group[0].hw;
    });
    adapters.forEach(a => { a.active = a.ss.some(s => s.type === 'Throughput' && s.raw > 0); });
    return adapters;
  };
```

- [ ] **Step 4: Verify pass**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS, 199 + 9 = 208 assertions.

- [ ] **Step 5: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): add pure network adapter grouping helpers (C1)"
```

---

### Task 3: `SQ.buildPanelItems(sensors, state)` emits one item per active adapter

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:712-742` (signature + replace the network tail `:737-740`)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: `SQ.buildNetAdapters`, `SQ.applyOrder`, `SQ.normalizeDashboardState` (all in SQ block).
- Produces: `SQ.buildPanelItems(sensors, state?) -> items`. Non-nic items unchanged. Adapter items appended last as `{ hw, label, ss, key, collapsed: true, net: true, index }` — one per **active, not hidden** adapter, ordered by `netAdapterOrder`. `state` optional: omitted → all active adapters in input order (keeps the existing 1-arg tests and any legacy caller working). Task 5's `renderPanels` passes `state.dashboard`; `item.net` is the flag every boot-side branch keys on.

- [ ] **Step 1: Write the failing tests** — append in `webtests/console.tests.js` (needs `nicSensors` from Task 2's block; `sensors` is the fixture flatten already defined at the top of the test function):

```js
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
```

- [ ] **Step 2: Run to verify failure**

Run: `node webtests\selftest.node.js`
Expected: FAIL on `one panel item per active adapter` (got a single `panel:network` item).

- [ ] **Step 3: Implement.** Change the signature at `:712` to `SQ.buildPanelItems = function (sensors, state) {` and replace the tail (`:737-740`, the `nics`/`active`/`net`/`items.push` lines) with:

```js
    const cfg = state ? SQ.normalizeDashboardState(state) : null;
    const hiddenNet = new Set(cfg ? cfg.hiddenNetAdapters : []);
    let adapters = SQ.buildNetAdapters(sensors)
      .filter(a => a.active && !hiddenNet.has(a.key))
      .map((a, i) => ({ hw: a.hw, label: a.label, ss: a.ss, key: a.key, collapsed: true, net: true, index: i }));
    adapters = SQ.applyOrder(adapters, cfg ? cfg.netAdapterOrder : [], a => a.key);
    adapters.forEach(a => { a.index = items.length; items.push(a); });
    return items;
```

(The final `return items;` replaces the old one at `:741`.)

- [ ] **Step 4: Verify pass — including the pre-existing Slice-2 tests at `console.tests.js:176-181`, which call `buildPanelItems` with one argument and must stay green untouched.**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS, 208 + 12 = 220 assertions.

- [ ] **Step 5: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): emit one subsystem panel per active network adapter (C1)"
```

---

### Task 4: Popover visibility model — hidden-adapter sensors are `offscreen`

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:271-275` (`SQ.sensorVisibility`)
- Test: `webtests/console.tests.js`

**Interfaces:**
- Consumes: `SQ.netAdapterKey` (Task 2), `SQ.isSensorHidden`, `SQ.normalizeDashboardState`.
- Produces: `SQ.sensorVisibility(s, state)` returns `'offscreen'` for a nic sensor whose adapter key is in `hiddenNetAdapters` (explicit per-sensor hide still wins with `'hidden'`). `SQ.hiddenSensorCount` and `SQ.sensorPopoverRows` inherit the behavior unchanged — the masthead badge and popover ranking now reflect hidden adapters automatically.

- [ ] **Step 1: Write the failing tests** — append in `webtests/console.tests.js`:

```js
    // --- C1 T4: hidden-adapter sensors are offscreen in the popover model ---
    eq('nic sensor of hidden adapter is offscreen', S.sensorVisibility(nicSensors[0], nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 'offscreen');
    eq('nic sensor of visible adapter unaffected', S.sensorVisibility(nicSensors[0], S.defaultDashboardState()), 'visible');
    eq('explicit sensor hide beats adapter hide', S.sensorVisibility(nicSensors[0], nicState({ hiddenSensorIds: [nicSensors[0].id], hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 'hidden');
    eq('hiddenSensorCount includes adapter-hidden sensors', S.hiddenSensorCount(nicSensors, nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] })), 2);
    eq('popover ranks adapter-hidden ahead of visible', S.sensorPopoverRows(nicSensors, nicState({ hiddenNetAdapters: ['/nic/%7BAAA%7D'] }), '')[0].visibility, 'offscreen');
```

- [ ] **Step 2: Run to verify failure**

Run: `node webtests\selftest.node.js`
Expected: FAIL — `nic sensor of hidden adapter is offscreen  got=visible want=offscreen`.

- [ ] **Step 3: Implement** — replace `SQ.sensorVisibility` (`:271-275`) with:

```js
  SQ.sensorVisibility = function (s, state) {
    if (SQ.isSensorHidden(s, state)) return 'hidden';
    if (s && s.cls === 'nic' &&
        SQ.normalizeDashboardState(state).hiddenNetAdapters.includes(SQ.netAdapterKey(s))) return 'offscreen';
    if (SQ.isStaticDriveAuxTemp(s) || SQ.isStaticMbTemp(s)) return 'offscreen';
    return 'visible';
  };
```

- [ ] **Step 4: Verify pass**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS, 220 + 5 = 225 assertions.

- [ ] **Step 5: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js webtests/console.tests.js
git commit -m "feat(web): mark hidden-adapter sensors offscreen in sensors popover model (C1)"
```

---

### Task 5: Render wiring — `#netsec` section, adapter ▲▼/drag/hide

This task is boot-block/DOM work; the DOM-less selftest can only assert index.html structure, so its real gate is the live browser check at the end (handoff §12.4).

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:55-56` (insert section)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — `panelEl` (`:1151-1200`), `renderPanels` (`:1201-1210`), `movePanel` (`:846-852`), new mutators next to it, `endDrag` (`:1496-1501`)
- Test: `webtests/selftest.node.js` (structure checks)

**Interfaces:**
- Consumes: `item.net` flag + adapter items from Task 3; `SQ.mergeOrder`/`SQ.moveKey` from Task 1; `SQ.buildNetAdapters` from Task 2; existing `panelEl`, `commitDashboard`, drag machinery.
- Produces: boot functions `moveAdapter(key, delta)`, `hideAdapter(key)`, `showAdapter(key)` (Task 6's restore button calls `showAdapter`); DOM ids `#netsec`, `#nettag`, `#netPanels`.

- [ ] **Step 1: Add the structure checks to `webtests/selftest.node.js`** — extend the `menuChecks` array (`:14-21`):

```js
  ['root index has network section', indexHtml.includes('id="netsec"') && indexHtml.includes('id="netPanels"') && indexHtml.includes('id="nettag"')],
```

Run: `node webtests\selftest.node.js` → expected: exactly one FAIL line (`root index has network section`).

- [ ] **Step 2: Insert the section in `index.html`** between the Subsystems `</section>` (`:55`) and `<footer>` (`:56`), mirroring the `#pinnedsec` hidden-by-default pattern:

```html
  <section id="netsec" style="display:none">
    <div class="sec-head"><h2>Network</h2><div class="rule"></div><span class="tag" id="nettag"></span></div>
    <div class="grid" id="netPanels"></div>
  </section>
```

Run: `node webtests\selftest.node.js` → expected: PASS again.

- [ ] **Step 3: Adapter controls in `panelEl`.** In the `panel-head` template (`:1161`), keep the two move buttons and append a hide button for adapter panels only. Replace the `h.innerHTML = ...` panel-move span and its wiring loop (`:1161-1169`) with:

```js
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
```

`.panel-move .ctl` buttons are already always-visible (`console.css:260-261`), so the hide control is keyboard-reachable without a head `tabindex` (§12.3). `&#8856;` (⊘) matches the row hide glyph (`console.js:792`).

- [ ] **Step 4: Mutators.** In `movePanel` (`:847`) restrict the merge to non-adapter panels, and add the adapter mutators directly below `resetPanelOrder` (`:856`):

```js
    function movePanel(key, delta) {
      const merged = SQ.mergeOrder(state.dashboard.panelOrder, state.panelItems.filter(i => !i.net).map(i => i.key));
      const next = SQ.moveKey(merged, key, delta);
      if (next === merged) return;   // out-of-bounds (top ▲ / bottom ▼): don't dirty panelOrder
      state.dashboard.panelOrder = next;
      commitDashboard();
    }
```

```js
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
```

- [ ] **Step 5: Split `renderPanels`** (`:1201-1210`) into hardware + network segments:

```js
    function renderPanels(sensors) {
      const panels = $('#panels');
      panels.innerHTML = '';
      state.panelItems = SQ.buildPanelItems(sensors, state.dashboard);
      const hwItems = state.panelItems.filter(i => !i.net);
      const netItems = state.panelItems.filter(i => i.net);
      const ordered = SQ.applyOrder(hwItems, state.dashboard.panelOrder, item => item.key);
      ordered.forEach(item => panels.appendChild(panelEl(item)));
      $('#subtag').textContent = `${ordered.length} components`;
      const preset = $('#panelsReset');
      if (preset) preset.style.display = state.dashboard.panelOrder.length ? '' : 'none';
      const netPanels = $('#netPanels');
      netPanels.innerHTML = '';
      netItems.forEach(item => netPanels.appendChild(panelEl(item)));   // already ordered by netAdapterOrder
      const hiddenNet = new Set(state.dashboard.hiddenNetAdapters);
      const hiddenCount = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNet.has(a.key)).length;
      $('#netsec').style.display = (netItems.length || hiddenCount) ? '' : 'none';
      $('#nettag').textContent = `${netItems.length} adapters` + (hiddenCount ? ` · ${hiddenCount} hidden` : '');
    }
```

Notes: `state.allSensors` is assigned at `:903` before `renderPanels` runs at `:911`, so the hidden count sees sensors of hidden adapters even though `renderPanels` receives `visibleSensors`. The section stays visible when *all* adapters are hidden (`0 adapters · N hidden`) as the discoverability cue.

- [ ] **Step 6: Drag branch.** In `endDrag` (`:1497`), add the container route after the `#panels` line:

```js
        if (a.container.id === 'panels') state.dashboard.panelOrder = next;
        else if (a.container.id === 'netPanels') state.dashboard.netAdapterOrder = next;
        else if (a.container.id === 'pinned') state.dashboard.pinnedOrder = next;
```

No other drag change: `orderedKeysFor`/`dragSiblings` operate on the event's own container, so an adapter drag can only reorder within `#netPanels` and a hardware-panel drag can only write hardware keys.

- [ ] **Step 7: Static verification**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS 226/226 (225 + the index structure check).

- [ ] **Step 8: Rebuild + live smoke (the real gate for this task).** Stop the running `LibreHardwareMonitor.Windows.Forms.exe` first (it locks the EXE/DLL), then:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

Restart the app, open `http://localhost:8085/` in a real browser (close stale pre-rebuild tabs first — §12.5), and verify across several poll ticks in **both dark and light themes**:
- Network section appears below Subsystems with one panel per active adapter (NET chip, deduped labels), each starting collapsed.
- Expanding an adapter shows only that adapter's rows, grouped by type; row ▲▼/drag stays inside the adapter (`data-row-group` = `<hwid>|<type>`).
- Adapter ▲▼ reorders adapters; ▲ on the top adapter is a no-op **and `localStorage['sq.dashboard.v1']` still has `netAdapterOrder: []`** (B3 guard).
- Dragging an adapter grip reorders within the Network grid; dragging a hardware panel still works **and `panelOrder` contains no `/nic/` or `hw:` adapter keys afterward**.
- ⊘ hides the adapter immediately; `#nettag` shows `· 1 hidden`; reload persists order + hidden.
- Zero console errors across ticks (§12.4 — this is what catches a dangling reference the selftest can't).

- [ ] **Step 9: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html webtests/selftest.node.js
git commit -m "feat(web): render per-adapter network panels with reorder and hide (C1)"
```

---

### Task 6: Sensors popover — hidden-adapter restore section

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html:36-37` (restore block inside `.sensors-panel`)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — `renderSensorsPopover` (`:1212-1243`), new click handler next to the `#sensorsList` one (`:1297-1308`)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` (restore section head/divider)
- Test: `webtests/selftest.node.js` (structure check)

**Interfaces:**
- Consumes: `SQ.buildNetAdapters` (Task 2), `showAdapter` (Task 5), popover signature-gated rebuild pattern (B1).
- Produces: `#netRestore` / `#netRestoreList` DOM with `data-action="net-show"` buttons; popover rebuild signature extended with hidden adapter keys.

- [ ] **Step 1: Structure check first** — extend `menuChecks` in `webtests/selftest.node.js`:

```js
  ['root index has adapter restore block', indexHtml.includes('id="netRestore"') && indexHtml.includes('id="netRestoreList"')],
```

Run: `node webtests\selftest.node.js` → expected: exactly one FAIL line.

- [ ] **Step 2: Markup** — in `index.html`, after `<div class="sensor-list" id="sensorsList"></div>` (`:36`), inside `.sensors-panel`:

```html
        <div class="net-restore" id="netRestore" style="display:none">
          <div class="net-restore-head">Hidden network adapters</div>
          <div class="sensor-list" id="netRestoreList"></div>
        </div>
```

Run: `node webtests\selftest.node.js` → expected: PASS.

- [ ] **Step 3: CSS** — append to `console.css` next to the popover rules (after `.vis-chip` block, `:313`); variables keep both themes correct:

```css
.net-restore{margin-top:10px;padding-top:10px;border-top:1px solid var(--line-soft)}
.net-restore-head{font:700 9px var(--mono);letter-spacing:.14em;text-transform:uppercase;color:var(--dim);margin:0 0 8px}
```

- [ ] **Step 4: Render.** In `renderSensorsPopover`, extend the rebuild signature and render the section (both must sit inside the sig-gated region). Replace the sig lines (`:1227-1231`) and append after the `list.innerHTML = ...` statement (`:1232-1242`):

```js
      const pinnedIds = new Set(state.dashboard.pinnedCards.map(c => c.id));
      const hiddenNetKeys = new Set(state.dashboard.hiddenNetAdapters);
      const hiddenAdapters = SQ.buildNetAdapters(state.allSensors).filter(a => hiddenNetKeys.has(a.key));
      const sig = (state.sensorsFilter || '') + '|' +
        rows.map(r => `${r.id}:${r.visibility}:${pinnedIds.has(r.id) ? 1 : 0}`).join(',') +
        '|net:' + hiddenAdapters.map(a => a.key).join(',');
      if (sig === state.sensorsSig) return;
      state.sensorsSig = sig;
```

```js
      const restore = $('#netRestore');
      restore.style.display = hiddenAdapters.length ? '' : 'none';
      $('#netRestoreList').innerHTML = hiddenAdapters.map(a => `<div class="sensor-choice is-hidden">
        <div><b>${esc(a.label)}</b><span>network adapter${a.active ? '' : ' · idle'}</span><code>${esc(a.key)}</code></div>
        <button class="iconbtn" data-action="net-show" data-key="${esc(a.key)}">Show</button>
      </div>`).join('');
```

- [ ] **Step 5: Click handler** — after the `reset-hidden` wiring (`:1309-1313`):

```js
    $('#netRestoreList').addEventListener('click', e => {
      const btn = e.target.closest('[data-action="net-show"]');
      if (!btn) return;
      showAdapter(btn.dataset.key);
      renderSensorsPopover();
    });
```

The existing capture-phase outside-click handler (`:1322-1324`) already reads `e.target` before this bubble-phase rebuild detaches the button, so the popover stays open on Show — same pattern the B1 fix established.

- [ ] **Step 6: Static verification**

Run: `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` then `node webtests\selftest.node.js`
Expected: PASS 227/227.

- [ ] **Step 7: Rebuild + live smoke** (stop app → build as in Task 5 → restart; both themes):
- Hide an adapter via ⊘ → masthead Sensors badge increases (its sensors count as offscreen), popover shows "Hidden network adapters" with the deduped label, key in `<code>`, and `· idle` marker only when inactive.
- Click Show → adapter panel returns (respecting `netAdapterOrder`), section row disappears, popover **stays open**, badge drops.
- Hide **all** adapters → Network section shows `0 adapters · N hidden`; restore each from the popover.
- Search box typing still filters sensor rows; adapter-hidden nic sensors appear with the `offscreen` chip.
- Popover at 390 px viewport width: restore rows don't overflow (`.sensors-panel` is `min(420px, 92vw)`).
- Zero console errors across ticks, both themes.

- [ ] **Step 8: Commit**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css webtests/selftest.node.js
git commit -m "feat(web): restore hidden network adapters from sensors popover (C1)"
```

---

### Task 7: Full verification matrix, docs closeout, merge

**Files:**
- Modify: `docs/feature-web-dashboard-card-truth.md` (verification log — exact section per its existing log format)
- Modify: `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md` (§4: mark C1 ✅ with commits; §11 next-step pointer → D1)
- Modify: `docs/superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md` (§0 next-task → D1; §11 progress log row for C1)
- Modify: this plan (tick checkboxes, record evidence)

**Interfaces:**
- Consumes: all prior task commits on `feat/web-network-subgroups-c1`.
- Produces: merged `master`, pushed, branch deleted (B-phase precedent), app left running.

- [ ] **Step 1: Full gate**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -c Release -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
git diff --check
```

Expected: selftest PASS 227/227; golden 42/42; both builds succeed; no whitespace errors.

- [ ] **Step 2: Live closeout matrix** (rebuilt app running; real browser; record evidence in the verification log):
- `GET /`, `/data.json`, `/metrics`, `/dash/cardtruth/` all 200.
- Full C1 behavior sweep from Tasks 5–6 checklists, **dark + light**, desktop + 390 px.
- Multi-tab: with a second same-route tab open (post-rebuild!), hide/reorder an adapter in tab 1, wait a poll tick in tab 2, reload tab 1 → edits survive (background `SQ.saveTelemetryState` merge covers the two C1 fields).
- `localStorage` inspection: `panelOrder` has no adapter keys; `netAdapterOrder` empty until a real adapter move.

- [ ] **Step 3: Docs + final commit**

```powershell
git add docs/feature-web-dashboard-card-truth.md docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md docs/superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md docs/superpowers/plans/2026-07-06-web-network-subgroups-c1.md
git commit -m "docs(web): record C1 network subgroups verification + advance queue to D1"
```

- [ ] **Step 4: Merge and push** (superpowers:finishing-a-development-branch — B-phase precedent is a local no-ff merge to `master`, push, delete branch):

```powershell
git checkout master
git pull --ff-only origin master
git merge --no-ff feat/web-network-subgroups-c1 -m "Merge network adapter subgroups (Phase C1)"
git push origin master
git branch -d feat/web-network-subgroups-c1
```

- [ ] **Step 5: Leave the app running** with the rebuilt EXE serving `http://localhost:8085/`.

---

## Out of Scope (recorded so it isn't silently dropped)

- **Idle-adapter sensors as `offscreen` in the popover** — needs a model/activity parameter in `SQ.sensorVisibility`; pre-existing nuance, candidate for D-phase popover polish.
- **Adapter-order reset control** — deliberately omitted (see Decisions); D-phase if wanted.
- **Adapter collapse-state polish / throughput summary in the panel head** — adapter heads reuse the stock head-stat (top temp → Load fallback, i.e. Network Utilization %); a ↓/↑ throughput summary is D-phase material.
- **Preview route lockstep** — `/dash/cardtruth/` untouched; Phase E2 delta audit owns reconciliation.

## Self-Review (done while writing)

- **Spec coverage vs §5 Slice 5B + §0 brief:** identity (hwid key, label-only display, deterministic `#N`) → Tasks 2–3; render model (per-adapter grouping, rows grouped by type inside adapter) → Tasks 3+5; state (`netAdapterOrder`, `hiddenNetAdapters`, `rowOrder[adapterKey|type]`) → Tasks 3+5 (row keys derive from panel key automatically); UI actions (move ▲▼, drag where reliable, hide, restore from popover, collapse) → Tasks 5–6 (collapse inherited from stock panels); tests (stable GUID keys, duplicate labels, order normalization, hidden absent from render + present in restore, rows can't cross groups) → Tasks 1–4 model tests + Task 5 live row-group check; exit "Network no longer one merged bucket" → Task 3 test `legacy merged network bucket gone` + live sweep.
- **No-op guard (§12.2):** contract test at Task 1 Step 1 (`same reference`), mutator guard at Task 5 Step 4, live no-op check at Task 5 Step 8.
- **Type consistency:** adapter item shape `{hw, label, ss, key, collapsed, net, index}` used identically in Tasks 3 and 5; `showAdapter(key)` defined Task 5, consumed Task 6; `SQ.mergeOrder/moveKey` names match across Tasks 1, 5.
- **Assertion arithmetic** (192→199→208→220→225→226→227) is indicative; the run prints the authoritative total — the requirement is zero FAIL lines at every step.
