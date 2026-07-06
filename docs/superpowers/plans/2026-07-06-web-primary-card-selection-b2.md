# Explicit Primary Card Selection (B2 / Slice 5A) Implementation Plan

> **For agentic workers:** Execute task-by-task with TDD (RED → GREEN → commit). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let the operator explicitly choose and reorder which sensors are primary (PFD) cards instead of relying only on the `pickHero` auto-heuristic, with a clean path back to auto.

**Architecture:** Pure client change in `Resources/Web/console.js` + one masthead-free DOM add in `index.html` + minimal CSS. The state already reserves `primaryCards: []` (a sensor-id list); B2 adds a `primaryCardsCustomized` boolean sentinel to distinguish "auto" from "operator chose (even if empty)". `renderPFD` consults the sentinel: auto → `pickHero`; custom → resolve `primaryCards` ids into hero-shaped cards via the same pattern `resolvePinnedCards` already uses. Affordances live on card + row expansion.

**Tech Stack:** Vanilla JS/CSS/HTML (no framework — non-negotiable). Model tests via `node webtests/selftest.node.js`. Golden/contract via `dotnet test`. Build `-p:Platform=x64`.

## Global Constraints (verbatim, apply to every task)

- No `data.json` / server / contract change. State stays browser-local under `sq.dashboard.v1`. (So the C# golden/`data.json` tests are structurally untouched — run them once at the end to confirm, not to "prove" each client edit.)
- No fake gauges, no guessed ceilings, no host-specific sensor ids. Bounds for promoted cards come only from `SQ.visualRangeForSensor` (honest bands) — same as pinned cards.
- Raw LibreHardwareMonitor label + `SensorId` stay visible wherever an alias shows (unchanged; `cardEl`/`xpEl` already do this).
- Vanilla JS/CSS, no framework. Customize drawer stays (drawer removal is B3, separate, parity-gated).
- Multi-tab-safe save pattern: user-owned writes go through `commitDashboard`; telemetry saves must not clobber. `mergeTelemetryState` rebuilds from persisted `base` and only advances `observedMax`/`powerLimitSamples`, so new user fields are preserved automatically once normalized.

## Design decisions (locked, with rationale)

1. **Sentinel = boolean `primaryCardsCustomized`, not an enum.** Only two states exist (auto / custom); a boolean is the YAGNI choice. Empty `primaryCards` + `customized:false` = auto; `customized:true` = operator owns the set (even if empty).
2. **First edit seeds from the currently-visible primary set, not from empty.** `setPrimaryCard` seeds from `primaryCardIds(...)` — in auto mode that is the live `pickHero` id set. So "Show as primary" on a subsystem row *adds* to the visible heroes; it does not collapse the PFD to a single card. This is why `setPrimaryCard` takes a 4th `sensors` arg (a deliberate, documented refinement of the handoff's 3-arg sketch — the seed set can't be computed without sensors).
3. **Button label is driven by membership in the *displayed* primary set** (`isPrimaryCard`, which returns the auto heroes in auto mode). A card currently in the PFD shows "Remove from primary"; a sensor not shown shows "Show as primary". No helper text needed — the state is the explanation.
4. **Affordances land on card expansion + row expansion (both call `xpEl`).** The sensors popover primary action is **deferred** — it would be a 5th per-row action and risks turning the popover into a full editor (handoff explicitly makes it optional). Card+row expansion already covers promote (from any subsystem row) and demote (from any PFD card).
5. **Missing ids preserved in state, filtered from render** — `resolvePrimaryCards` drops ids whose sensor is absent (device unplugged) but the id stays in `primaryCards`, so the layout returns when the device returns. Mirrors `resolvePinnedCards`.
6. **`cardOrder` stays the single order list for the PFD** for both auto and custom (unchanged); `renderPFD` applies it uniformly.

## File Structure

- Modify `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — state model, 5 helpers, `renderPFD`, `xpEl` button, action handlers, local `setPrimaryCardState`/`resetPrimaryCardsState`, `#pfdReset` wiring.
- Modify `LibreHardwareMonitor.Windows.Forms/Resources/Web/index.html` — add `#pfdReset` button in the PFD section header.
- Modify `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` — style for the reset control (minimal; reuse `.iconbtn`).
- Modify `webtests/console.tests.js` — Slice 5A model tests after the pinned-card test (line ~102) and a sentinel-preservation assertion.

---

## Task 1: State sentinel

**Files:** Modify `console.js` (`defaultDashboardState` ~147, `normalizeDashboardState` ~173); Test `webtests/console.tests.js`.

- [ ] **Step 1 (RED):** After the pinned-card test block (line ~102), add:
```js
// --- Explicit primary card selection (Slice 5A) ---
eq('default primaryCardsCustomized false', S.defaultDashboardState().primaryCardsCustomized, false);
eq('normalize primaryCardsCustomized true', S.normalizeDashboardState({primaryCardsCustomized:true}).primaryCardsCustomized, true);
eq('normalize primaryCardsCustomized junk -> false', S.normalizeDashboardState({primaryCardsCustomized:1}).primaryCardsCustomized, false);
```
- [ ] **Step 2:** Run `node webtests/selftest.node.js` → FAIL (`primaryCardsCustomized` undefined).
- [ ] **Step 3 (GREEN):** In `defaultDashboardState` add `primaryCardsCustomized: false,` (next to `primaryCards: []`). In `normalizeDashboardState` add `primaryCardsCustomized: value.primaryCardsCustomized === true,`.
- [ ] **Step 4:** Run selftest → PASS.
- [ ] **Step 5:** Commit `feat(web): add primaryCardsCustomized state sentinel (B2)`.

## Task 2: Selection model helpers (`primaryCardIds`, `isPrimaryCard`)

**Files:** Modify `console.js` (near `resolvePinnedCards` ~371); Test `console.tests.js`.

- [ ] **Step 1 (RED):** Append to the Slice 5A block:
```js
const autoIds = S.pickHero(sensors, limits).map(h => h.s.id);
eq('primaryCardIds auto mode = hero ids', S.primaryCardIds(sensors, S.defaultDashboardState()), autoIds);
eq('primaryCardIds non-array safe', S.primaryCardIds(null, S.defaultDashboardState()), []);
eq('isPrimaryCard true for auto hero', S.isPrimaryCard(S.defaultDashboardState(), autoIds[0], sensors), true);
const nonHeroId = sensors.map(s => s.id).find(id => !autoIds.includes(id));
eq('isPrimaryCard false for non-hero', S.isPrimaryCard(S.defaultDashboardState(), nonHeroId, sensors), false);
```
- [ ] **Step 2:** selftest → FAIL (`primaryCardIds` not a function).
- [ ] **Step 3 (GREEN):** Add after `resolvePinnedCards`:
```js
SQ.primaryCardIds = function (sensors, state) {
  const cfg = SQ.normalizeDashboardState(state);
  if (cfg.primaryCardsCustomized) return cfg.primaryCards.slice();
  return Array.isArray(sensors) ? SQ.pickHero(sensors, {}).map(h => h.s.id) : [];
};
SQ.isPrimaryCard = function (state, id, sensors) {
  return SQ.primaryCardIds(sensors, state).includes(id);
};
```
- [ ] **Step 4:** selftest → PASS.
- [ ] **Step 5:** Commit `feat(web): add primaryCardIds + isPrimaryCard helpers (B2)`.

## Task 3: Mutators (`setPrimaryCard`, `resetPrimaryCards`)

**Files:** Modify `console.js`; Test `console.tests.js`.

- [ ] **Step 1 (RED):** Append:
```js
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
```
- [ ] **Step 2:** selftest → FAIL.
- [ ] **Step 3 (GREEN):** Add:
```js
SQ.setPrimaryCard = function (state, id, enabled, sensors) {
  const cfg = SQ.normalizeDashboardState(state);
  const ids = SQ.primaryCardIds(sensors, cfg).filter(x => x !== id);
  if (enabled) ids.push(id);
  cfg.primaryCardsCustomized = true;
  cfg.primaryCards = ids;
  return cfg;
};
SQ.resetPrimaryCards = function (state) {
  const cfg = SQ.normalizeDashboardState(state);
  cfg.primaryCardsCustomized = false;
  cfg.primaryCards = [];
  return cfg;
};
```
- [ ] **Step 4:** selftest → PASS.
- [ ] **Step 5:** Commit `feat(web): add setPrimaryCard + resetPrimaryCards mutators (B2)`.

## Task 4: Render resolver (`resolvePrimaryCards`) + telemetry preservation

**Files:** Modify `console.js`; Test `console.tests.js`.

- [ ] **Step 1 (RED):** Append:
```js
const custPrim = S.normalizeDashboardState({primaryCardsCustomized:true, primaryCards:[autoIds[0], '/missing/x']});
const primCards = S.resolvePrimaryCards(sensors, custPrim, limits);
eq('resolvePrimaryCards keeps present sensor', primCards.some(c => c.s.id === autoIds[0]), true);
eq('resolvePrimaryCards drops missing sensor from render', primCards.some(c => c.s.id === '/missing/x'), false);
eq('resolvePrimaryCards row shape', Object.keys(primCards[0]).sort(), ['bounded','label','s','status']);
eq('missing primary id preserved in state', custPrim.primaryCards.includes('/missing/x'), true);
const primMerge = S.mergeTelemetryState(custPrim, S.defaultDashboardState());
eq('telemetry preserves primary sentinel + list',
  [primMerge.primaryCardsCustomized, primMerge.primaryCards], [true, [autoIds[0], '/missing/x']]);
```
- [ ] **Step 2:** selftest → FAIL.
- [ ] **Step 3 (GREEN):** Add next to `resolvePinnedCards`:
```js
SQ.resolvePrimaryCards = function (sensors, state, limits) {
  const byId = new Map(sensors.map(s => [s.id, s]));
  const cfg = SQ.normalizeDashboardState(state);
  return cfg.primaryCards.map(id => {
    const s = byId.get(id);
    if (!s) return null;
    return { s, label: s.text, status: SQ.statusOf(s, limits || {}), bounded: SQ.visualRangeForSensor(s, limits || {}) };
  }).filter(Boolean);
};
```
- [ ] **Step 4:** selftest → PASS.
- [ ] **Step 5:** Commit `feat(web): add resolvePrimaryCards render resolver (B2)`.

## Task 5: Wire `renderPFD` to the sentinel

**Files:** Modify `console.js` (`renderPFD` ~1044).

- [ ] **Step 1:** Replace `renderPFD` body so the card source depends on the sentinel; keep `applyOrder(cardOrder)` and `cardEl(h, false)` unchanged:
```js
function renderPFD(sensors, limits) {
  const custom = state.dashboard.primaryCardsCustomized;
  const base = custom ? SQ.resolvePrimaryCards(sensors, state.dashboard, limits)
                      : SQ.pickHero(sensors, limits);
  const H = SQ.applyOrder(base.map((h, index) => Object.assign(h, { index })),
    state.dashboard.cardOrder, h => h.s.id);
  const pfd = $('#pfd');
  pfd.innerHTML = '';
  H.forEach(h => pfd.appendChild(cardEl(h, false)));
  const reset = $('#pfdReset');
  if (reset) reset.style.display = custom ? '' : 'none';
  $('#pfdtag').textContent = custom ? `${H.length} selected` : `${H.length} auto-selected`;
}
```
- [ ] **Step 2:** `node --check console.js` + `node webtests/selftest.node.js` → still PASS (no model regressions; render is DOM-side).
- [ ] **Step 3:** Commit `feat(web): render PFD from explicit primary set when customized (B2)`.

## Task 6: UI affordances (expansion button, reset control, handlers)

**Files:** Modify `console.js` (`xpEl` ~961 actions; delegated click handler ~1360-1448; local mutator helpers near `pinSensor` ~819), `index.html` (PFD header ~49), `console.css`.

- [ ] **Step 1:** In `index.html`, add a reset button into the PFD section header, after `#pfdtag`:
```html
<span class="tag" id="pfdtag">auto-selected</span><button class="iconbtn" id="pfdReset" style="display:none" title="Return primary cards to auto">Auto</button>
```
- [ ] **Step 2:** In `xpEl` `.xp-actions` (right after the pin/unpin button, line ~964) add:
```js
const isPrimary = SQ.isPrimaryCard(state.dashboard, s.id, state.allSensors);
```
(compute near the top of `xpEl` with the other consts) and insert the button markup:
```js
<button class="iconbtn" data-act="${isPrimary ? 'primary-remove' : 'primary-add'}" data-id="${esc(s.id)}">${isPrimary ? 'Remove from primary' : 'Show as primary'}</button>
```
- [ ] **Step 3:** Add local mutator helpers near `pinSensor`/`unpinSensor` (~819):
```js
function setPrimaryCardState(id, enabled) {
  state.dashboard = SQ.setPrimaryCard(state.dashboard, id, enabled, state.allSensors);
  commitDashboard();
}
function resetPrimaryCardsState() {
  state.dashboard = SQ.resetPrimaryCards(state.dashboard);
  commitDashboard();
}
```
- [ ] **Step 4:** In the delegated `data-act` click handler (switch near ~1432), add cases:
```js
case 'primary-add': setPrimaryCardState(id, true); break;
case 'primary-remove': setPrimaryCardState(id, false); break;
```
- [ ] **Step 5:** Wire the reset button once during init (near where other masthead buttons bind, e.g. after `#customize` handler): `$('#pfdReset').onclick = resetPrimaryCardsState;`
- [ ] **Step 6:** `console.css` — no new rule required (reuse `.iconbtn`); add only a small left-margin so `#pfdReset` clears `#pfdtag` if the header spacing looks tight:
```css
#pfdReset{margin-left:8px}
```
- [ ] **Step 7:** `node --check console.js` + `node webtests/selftest.node.js` → PASS.
- [ ] **Step 8:** Commit `feat(web): add show/remove primary actions + auto reset (B2)`.

## Task 7: Rebuild, live verify, finish

- [ ] **Step 1:** Rebuild EXE (embedded assets): `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`; restart app.
- [ ] **Step 2:** Golden/contract sanity (must stay green; confirms zero contract drift): `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -c Release -p:Platform=x64`.
- [ ] **Step 3 (live, chrome-devtools):** On `/`:
  - Expand a subsystem row → "Show as primary" → that sensor appears as a PFD card; tag reads "N selected"; `#pfdReset` visible.
  - Expand a PFD card → "Remove from primary" → it leaves the PFD; other cards remain.
  - Verify first promote did NOT collapse the PFD to one card (seed-from-visible works).
  - Reorder a custom primary card with the expansion ▲▼ (move-left/right) → order persists across a poll tick.
  - Click "Auto" → PFD returns to auto heroes; tag "N auto-selected"; reset hidden.
  - Reload → custom set + order persists (localStorage). Promote a sensor, open a second tab, confirm the passive tab's telemetry save does not wipe the selection.
  - No console errors; dark + light both clean.
- [ ] **Step 4:** Update this plan's checkboxes + add an "## Execution notes" section recording any deviations/live-found bugs (as B1 did).
- [ ] **Step 5:** Use `superpowers:finishing-a-development-branch` — verify tests, then merge to `master` (`--no-ff`), push, delete the B2 branch. (Pre-authorized this session: "merge / push so it's clean".)

## Self-Review

1. **Spec coverage (handoff Slice 5A):** state contract ✓ (Task 1, sentinel); `primaryCardIds`/`setPrimaryCard`/`resetPrimaryCards`/`isPrimaryCard` ✓ (Tasks 2-3); card + row expansion actions ✓ (Task 6); primary grid drag/keyboard ✓ (already exists — `movable:true` on cards); the 7 Slice 5A tests are covered (auto default, add→custom, remove keeps custom, reset→auto, order applies, missing preserved, telemetry-safe). Popover primary action **deliberately deferred** (decision 4) — documented, not dropped.
2. **Placeholder scan:** every code step shows real code; no TBD/TODO.
3. **Type consistency:** `setPrimaryCard(state, id, enabled, sensors)` and `isPrimaryCard(state, id, sensors)` share arg order; `resolvePrimaryCards` returns `{s,label,status,bounded}` — the exact shape `cardEl` consumes (`h.s`, `h.label`, `h.status`, `h.bounded`), matching `resolvePinnedCards`.

## Execution notes (2026-07-06, inline)

**Result:** All 6 tasks executed via strict TDD (RED→GREEN→commit). Web selftest 185→**190/190**; C# golden **42/42** (contract untouched, as predicted for a browser-local change). No console errors/warnings. Dark + light both clean (screenshots in scratchpad).

**Deviations from the handoff sketch:** `setPrimaryCard`/`isPrimaryCard` take a 4th `sensors` arg (decision 2) so the first edit seeds from the live auto-hero set. Popover primary action deferred (decision 4). Both intentional and documented above.

**Live verification (chrome-devtools, real browser across poll ticks — the node harness cannot cover DOM/event/persistence):**
- Auto→promote→demote→reset cycle: all 6 verdicts pass. Promoting a non-hero subsystem row yields **auto-count + 1** cards ("15 selected"), NOT a collapse to one card — the seed-from-visible design (decision 2) works. Expansion button flips auto: "Show as primary" ⇄ "Remove from primary" on both the row (`.rowxp`) and the PFD card (`.xp`). `#pfdReset` ("Auto") shows only in custom mode; clicking returns to the exact auto hero set.
- Persistence across reload confirmed (isolated single tab): custom set + sentinel survive reload.

**Two findings (neither a B2 product bug):**
1. **Version-skew multi-tab clobber (test artifact, inherent limitation).** First reload test showed the custom set lost — root-caused to two *stale pre-B2 browser tabs* still running the old `console.js` (rebuild embeds new assets, but already-open tabs keep old JS until reloaded). The old `normalizeDashboardState` doesn't know `primaryCardsCustomized`, so its telemetry poll-save **strips the unknown flag while keeping the known `primaryCards` list** (signature: `customized:false` + list preserved). Verified by inspecting the stale tab (`SQ.setPrimaryCard` absent). After closing the stale tabs, persistence works. This affects *every* browser-local field ever added, not B2 — `mergeTelemetryState` protects concurrent same-version tabs, and cannot protect against genuinely older code. No fix warranted; recorded as a live-testing gotcha.
2. **`.rowxp` is a SIBLING of `.row`, not a child** (`appendRow`, console.js:1141-1145 appends `xpEl` to the container after the row; rows also get no `.expanded` class — only cards do). Initial live test queried inside `.row` and wrongly reported the button missing; the code was correct. Testing gotcha worth remembering.
