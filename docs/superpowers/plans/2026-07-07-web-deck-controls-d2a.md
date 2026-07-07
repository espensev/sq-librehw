# Web Dashboard D2a — Direct Flight-Deck Edit Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Plan ID:** web-dashboard-d2a-2026-07-07
**Requirement source:** operator feedback 2026-07-07 (recorded in `feature-web-dashboard-card-truth.md` §11 and v3-next-plan §4 row D2a): "users can't freely add or remove … it should be directly from the card, not a sub menu." Today the card hover cluster is only pin + dashboard-wide hide; the sole primary add/remove is one button among ~12 in the expansion's `.xp-actions` (console.js:1056); Sensors-popover rows have no primary toggle.
**Goal:** Primary-deck membership becomes a one-click star toggle on every visible item — PFD cards (remove), pinned cards and panel rows (add/remove), and Sensors-popover rows — using the state model and delegated actions that already exist.

**Architecture:** Zero model change. `SQ.setPrimaryCard`/`primaryCardIds` (B2 seed-from-visible semantics) and the `primary-add`/`primary-remove` cases in `handleAct` (console.js:1439-1440) already work — the gap is purely presentational. A star button (★ on-deck / ☆ off-deck) joins `ctlCluster` so it appears in the card cluster and row cluster automatically; the Sensors popover gets a matching text button plus the two `data-action` cases and a **rebuild-signature term** (the C1 `a.active` lesson: a sig omission leaves stale labels while the popover is open). To avoid recomputing auto-mode heroes per button per tick, one `state.primaryIds` Set is derived once per render pass and consumed everywhere (including a DRY refactor of `xpEl`'s existing lookup).

**Tech Stack:** Vanilla JS/CSS/HTML embedded resources in `LibreHardwareMonitor.Windows.Forms.exe`, served at `http://localhost:8085/`. No framework.

## Global Constraints (campaign non-negotiables — every task inherits these)

- **No `data.json` / server / contract change.** Client-only. Golden `dotnet test` must stay 42/42.
- **No new persisted state.** `primaryCards`/`primaryCardsCustomized` (B2) stay the only persistence; `state.primaryIds` is an in-memory per-render derivation, never written to localStorage.
- **No host-specific labels, limits, or sensor IDs in product code.**
- **Read-only dashboard.** No `/Sensor?action=Set` or write UI.
- **Both dark and light themes are first-class**, verified equally.
- **Build requires `-p:Platform=x64`.** Stop the running EXE before building; restart after (controller only).
- **Scope:** the star toggle and its plumbing only. Do not reopen the D1 gutter, the D2 overlay, or remove the existing `.xp-actions` primary button (it stays — the expansion remains the full-detail surface).
- **The DOM-less selftest must stay green** (`node webtests/selftest.node.js`, currently 227/227). Regression guard only — the gate is the controller's live pass.
- **Semantic color honesty:** the star's "on" styling reuses the pin's `--lime` active treatment. Do NOT use state colors (`--ok/--warn/--crit`) for selection state.

---

## File Structure

| File | Responsibility for D2a |
|---|---|
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` | Task 1: `primaryIds` state field + per-render refresh; star button in `ctlCluster`; `xpEl` primary lookup refactored to the Set. Task 2: popover row button (non-hidden rows only), sig term, two `data-action` cases. |
| `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` | Task 1: `.ctl.star.on` active styling (one line, next to `.ctl.pin.on`). |

No `index.html` change. No test-file change — see "Testing note".

## Testing note — why there is no new node unit test

`ctlCluster`, the popover renderer, and the delegated handlers are boot-block DOM code; both node harnesses are DOM-less. The one pure piece (`SQ.primaryCardIds`) is pre-existing B2 code already under test. The honest gate is the controller's live pass (below), which includes the sig-gated-label proof and the touch-width truncation check the D1/C1 lessons demand.

---

### Task 1: Star toggle in the shared control cluster

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — state literal (~:812-819), `render()` locals (~:948), `ctlCluster` (~:846-852), `xpEl` (~:1026)
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css` — after `.ctl.pin.on` (:257)

**Interfaces:**
- Consumes: `SQ.primaryCardIds(sensors, state)` (console.js:390 — pre-existing, returns the effective deck ids in both auto and custom modes), `SQ.isPinned`, `esc`, the delegated `handleAct` cases `primary-add`/`primary-remove` (:1439-1440 — pre-existing, no change).
- Produces: `state.primaryIds: Set<string>` refreshed once per `render()` pass; a `.ctl.star` button rendered by `ctlCluster` on every card and row cluster; `xpEl` reads `state.primaryIds` instead of calling `SQ.isPrimaryCard`.

- [ ] **Step 1: JS — state field.** In the state object literal, directly after the `xpEnter: null,` line (added by D2), add:

```js
      primaryIds: new Set(),
```

- [ ] **Step 2: JS — refresh once per render pass.** In `render(data)`, the locals block currently reads (~:945-950):

```js
      const sensors = SQ.visibleSensors(allSensors, state.dashboard);
      const limits = SQ.deriveLimits(sensors);
      sensors.forEach(s => s.status = SQ.statusOf(s, limits));
      state.allSensors = allSensors;
      state.visibleSensors = sensors;
```

Directly after the `state.visibleSensors = sensors;` line, add:

```js
      state.primaryIds = new Set(SQ.primaryCardIds(allSensors, state.dashboard));
```

(Same `allSensors` argument the existing `xpEl` lookup uses, so semantics are unchanged — one computation per pass instead of one per button.)

- [ ] **Step 3: JS — star button in `ctlCluster`.** Replace the whole function (currently ~:846-852):

```js
    function ctlCluster(id, label, opts) {
      const pinned = SQ.isPinned(state.dashboard, id);
      const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
      const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
      return pin + hide;
    }
```

with:

```js
    function ctlCluster(id, label, opts) {
      const pinned = SQ.isPinned(state.dashboard, id);
      const primary = state.primaryIds.has(id);
      const star = `<button class="ctl star${primary ? ' on' : ''}" data-act="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(id)}" aria-label="${primary ? 'Remove from primary' : 'Show as primary'} ${esc(label)}" title="${primary ? 'Remove from primary' : 'Show as primary'}">${primary ? '&#9733;' : '&#9734;'}</button>`;
      const pin = `<button class="ctl pin${pinned ? ' on' : ''}" data-act="${pinned ? 'unpin' : 'pin'}" data-id="${esc(id)}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${esc(label)}" title="${pinned ? 'Unpin' : 'Pin'}">&#128204;</button>`;
      const hide = opts && opts.hide ? `<button class="ctl hide" data-act="hide" data-id="${esc(id)}" aria-label="Hide ${esc(label)}" title="Hide">&#8856;</button>` : '';
      return star + pin + hide;
    }
```

(No call-site change: both callers — `cardEl` ~:1125 and `rowEl` ~:1199 — get the star automatically. The buttons route through the existing delegated `handleAct`, whose `primary-add`/`primary-remove` cases already call `setPrimaryCardState` → `SQ.setPrimaryCard` → seed-from-visible on first customization, `commitDashboard()` rerender. `&#9733;` = ★, `&#9734;` = ☆.)

- [ ] **Step 4: JS — DRY the existing expansion lookup.** In `xpEl` (~:1026), replace:

```js
      const isPrimary = SQ.isPrimaryCard(state.dashboard, s.id, state.allSensors);
```

with:

```js
      const isPrimary = state.primaryIds.has(s.id);
```

(The `.xp-actions` "Show as primary"/"Remove from primary" button at :1056 stays — the expansion remains the full-detail surface.)

- [ ] **Step 5: CSS — active-star styling.** In `console.css`, directly after `.ctl.pin.on{...}` (:257), add:

```css
.ctl.star.on{color:var(--lime);border-color:color-mix(in srgb,var(--lime) 45%,var(--line))}
```

- [ ] **Step 6: Static gates.**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
git diff --check
```

Expected: `--check` clean; `SELFTEST PASS 227/227`; no whitespace errors.

- [ ] **Step 7: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js LibreHardwareMonitor.Windows.Forms/Resources/Web/console.css
git commit -m "feat(web): one-click primary-deck star on card and row control clusters (D2a)"
```

---

### Task 2: Primary toggle in the Sensors popover

**Files:**
- Modify: `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` — `renderSensorsPopover` rows template + rebuild signature (~:1305-1320), popover `data-action` switch (~:1380-1387)

**Interfaces:**
- Consumes: `state.primaryIds` (Task 1), `setPrimaryCardState(id, enabled)` (console.js:920 — pre-existing), the popover's sig-gated rebuild and its capture-safe button-click pattern (pre-existing).
- Produces: a "Make primary"/"Remove primary" button on **non-hidden** popover rows; `primary-add`/`primary-remove` `data-action` cases; the primary bit in the rebuild signature.

- [ ] **Step 1: JS — sig term + row button.** In `renderSensorsPopover`, the signature currently reads (~:1305-1307):

```js
      const sig = (state.sensorsFilter || '') + '|' +
        rows.map(r => `${r.id}:${r.visibility}:${pinnedIds.has(r.id) ? 1 : 0}`).join(',') +
        '|net:' + hiddenAdapters.map(a => a.key).join(',');
```

Replace the middle line with:

```js
        rows.map(r => `${r.id}:${r.visibility}:${pinnedIds.has(r.id) ? 1 : 0}:${state.primaryIds.has(r.id) ? 1 : 0}`).join(',') +
```

Then in the row template just below (~:1311-1319), which currently reads:

```js
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(r.label)}</b><span>${esc(r.hw)} · ${esc(r.type)} · ${esc(r.value)}${alias}</span><code>${esc(r.id)}</code></div>
          <span class="vis-chip vis-${r.visibility}">${r.visibility}</span>
          <button class="iconbtn" data-action="${pinned ? 'unpin' : 'pin'}" data-id="${esc(r.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
          <button class="iconbtn" data-action="${hidden ? 'show' : 'hide'}" data-id="${esc(r.id)}">${hidden ? 'Show' : 'Hide'}</button>
        </div>`;
```

insert one line above the Pin button, and a `primary` const above the `return` (next to the existing `hidden`/`pinned` consts):

```js
        const primary = state.primaryIds.has(r.id);
```

```js
          ${hidden ? '' : `<button class="iconbtn" data-action="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(r.id)}">${primary ? 'Remove primary' : 'Make primary'}</button>`}
```

so the row becomes:

```js
        return `<div class="sensor-choice ${hidden ? 'is-hidden' : ''}">
          <div><b>${esc(r.label)}</b><span>${esc(r.hw)} · ${esc(r.type)} · ${esc(r.value)}${alias}</span><code>${esc(r.id)}</code></div>
          <span class="vis-chip vis-${r.visibility}">${r.visibility}</span>
          ${hidden ? '' : `<button class="iconbtn" data-action="${primary ? 'primary-remove' : 'primary-add'}" data-id="${esc(r.id)}">${primary ? 'Remove primary' : 'Make primary'}</button>`}
          <button class="iconbtn" data-action="${pinned ? 'unpin' : 'pin'}" data-id="${esc(r.id)}">${pinned ? 'Unpin' : 'Pin'}</button>
          <button class="iconbtn" data-action="${hidden ? 'show' : 'hide'}" data-id="${esc(r.id)}">${hidden ? 'Show' : 'Hide'}</button>
        </div>`;
```

Rationale for the `hidden` gate: a hidden sensor added to the deck would not render (`resolvePrimaryCards` filters to visible sensors), which reads as a broken button. Show it first, then star it.

- [ ] **Step 2: JS — action cases.** In the popover's `data-action` switch (~:1381-1386), which currently reads:

```js
      switch (btn.dataset.action) {
        case 'hide': setSensorHidden(id, true); break;
        case 'show': setSensorHidden(id, false); break;
        case 'pin': pinSensor(id); break;
        case 'unpin': unpinSensor(id); break;
      }
```

add two cases before the closing brace:

```js
        case 'primary-add': setPrimaryCardState(id, true); break;
        case 'primary-remove': setPrimaryCardState(id, false); break;
```

(`setPrimaryCardState` runs `commitDashboard()` → rerender, which refreshes `state.primaryIds`; the existing `renderSensorsPopover()` call after the switch then rebuilds the list because the sig's new primary bit changed — the label flips in place and the popover stays open, exactly like Pin/Unpin.)

- [ ] **Step 3: Static gates.**

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node webtests\selftest.node.js
git diff --check
```

Expected: `--check` clean; `SELFTEST PASS 227/227`; no whitespace errors.

- [ ] **Step 4: Commit.**

```powershell
git add LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js
git commit -m "feat(web): primary-deck toggle on Sensors popover rows with sig-gated label (D2a)"
```

---

## Controller-owned live gate (the real D2a verification — not a subagent task)

Implementers do static gates + commits only. The controller rebuilds once and runs:

- [ ] **G1 — Rebuild.** Stop the app; `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64`; restart; confirm served `console.js` contains `ctl star`; hard-reload the tab.
- [ ] **G2 — Functional pass (1440×900, then repeat key flows in the other theme):**
  - **Render:** PFD cards show ★ with `.on` (auto heroes are primary); non-primary rows show ☆; pinned cards show the star per their sensor's deck state.
  - **Add from a row:** click ☆ on a panel row → PFD flips to custom (`#pfdtag` = "N selected", `#pfdReset` visible), the sensor's card appears in `#pfd`, and every surface for that sensor now shows ★`.on` (row, card, its expansion button says "Remove from primary"). First click in auto mode seeds the visible heroes + adds (B2 semantics — expected, not a bug).
  - **Remove from a card:** click ★ on a PFD card → card leaves the deck, count drops, row star reverts to ☆.
  - **Auto reset:** `#pfdReset` → back to auto heroes; stars everywhere reflect auto membership; `#pfdReset` hides.
  - **Popover sig proof:** open Sensors popover; click "Make primary" on a visible row → the button label flips to "Remove primary" **while the popover stays open** (rebuild-sig term working); hidden rows show **no** primary button; Show → button appears.
  - **D2 non-regression:** expand a card, click its cluster star → deck updates and the overlay lifecycle stays sane (no orphan overlay, no strobe).
- [ ] **G3 — Width/touch matrix (the D1-lesson gate):** at **390 touch** and **~768 touch** emulation (cluster always visible, now 4 buttons) run the D1 rect-intersection gate (must stay `violations: []` — structural, but re-measure) **plus** the name-truncation check: count cards whose `.name` is ellipsized at rest. **Stop-condition:** if card names are unreadably crushed at ~768-touch (3-column grid), stop and present the fallback (drop ⊘ from the touch cluster, or star-only on touch) — operator call, do not improvise. Also check the row `.row-ctl` scrim: with the extra button, row values at 390 must not be fully covered at rest. Screenshots both themes at 1440 and 390.
- [ ] **G4 — Console + contract:** several poll ticks with popover open + a card expanded → zero console errors; `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` → 42/42.

## Closeout (standard campaign procedure)

- [ ] Verification-log row in `docs/feature-web-dashboard-card-truth.md` §11 (D2a: closes operator finding (1) from the 2026-07-07 feedback row).
- [ ] Advance the queue: v3-next-plan §4 row **D2a ✅** + critical path → D3 (fold the ~768-touch result into D3's matrix notes); handoff §0/§11 rows.
- [ ] Final whole-branch review (superpowers:requesting-code-review) — model scaled to a small presentational+wiring diff.
- [ ] Merge via superpowers:finishing-a-development-branch; post-merge selftest 227/227; leave rebuilt app running.

## Stop conditions (from the campaign)

Stop and review if: card names become unreadable where the cluster is permanently visible (G3 stop-condition); the star seeds or mutates the deck in any way that surprises (e.g., a click adds MORE than seed+target); the popover closes or loses scroll on primary toggle; any pin/hide/drag/expand path breaks; the app cannot restart after build.
