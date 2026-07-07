# Web Dashboard D3 - Responsive and Theme QA Patch List

**Date:** 2026-07-07
**Status:** implemented and verified; lands as the D3 follow-up commit on top of `5a95bd7`
**Baseline:** D3 implementation starts from local `master` at `5a95bd7` (`fix(web): harden telemetry server boundary`), ahead of `origin/master` `db4a9da`; product D2a merge baseline `0279333` (`Merge D2a direct deck-controls`)
**Primary spec:** [../../feature-web-dashboard-card-truth.md](../../feature-web-dashboard-card-truth.md)
**Queue source:** [2026-07-06-web-dashboard-v3-next-plan.md](2026-07-06-web-dashboard-v3-next-plan.md) §4 row D3
**User-perspective review:** [../../reviews/review-2026-07-07-dashboard-d3-user-perspective.md](../../reviews/review-2026-07-07-dashboard-d3-user-perspective.md)

## 1. Objective

D3 closes the responsive/theme quality gate for the card-first dashboard after D1, D2, and D2a.
It is a browser-driven QA and small visual-fix phase, not a model, server, telemetry, or feature
expansion phase.

The implementation branch may make scoped CSS/DOM fixes required by this patch list, but it must not
change `data.json`, HTTP server contracts, hardware writes, persisted dashboard fields, or route
semantics unless a separate accepted spec explicitly authorizes that change.

## 2. Live Snapshot and Confirmed Evidence

This patch list is grounded in the live root dashboard, not only static CSS inspection.

Final D3 closeout from the 2026-07-07 Edge/Playwright pass against `http://127.0.0.1:8085/`:

- Process: rebuilt Release `net10.0-windows` x64 EXE, PID `79624`; `GET /`, `/data.json`,
  `/metrics`, and `/dash/cardtruth/` returned 200 before the browser matrix.
- Matrix: 14 cases — 320x568 touch, 390x844 touch, 640x900 hover, 640x900 touch,
  768x1024 touch, 1440x900 hover, and 1920x1080 hover, each in dark and light.
- Browser gates: `maxRowOverlap=0`, `maxRowValueOverflow=0`, no horizontal scroll,
  Sensors panel X/Y fit true, Pages menu X fit true, overlay X fit true, zero console
  warnings/errors, and root route identity `{hasSensorsMenu:true, hasCustomizeDrawer:false}`.
- Stateful localStorage probe: seeded non-empty `panelOrder` and one hidden active NIC;
  `#panelsReset` and the hidden-adapter restore row remained in viewport at 320/390/640,
  with `panelHeaderBadCount=0` and no horizontal scroll.
- Evidence files: `C:\Users\Sev\AppData\Local\Temp\sq-librehw-d3-20260707-edge-r3\d3-report.json`
  and `d3-stateful-panels-report.json`; inspected screenshots include
  `320x568-touch-dark-sensors.png`, `390x844-touch-dark-rows.png`,
  `768x1024-touch-dark-default.png`, `1440x900-hover-light-default.png`,
  `320x568-touch-dark-overlay.png`, and `390x844-touch-stateful-panels.png`.
- Static gates: `node --check` passed for root and `/dash/cardtruth/` `console.js`;
  `node webtests\selftest.node.js` passed 227/227; `dotnet test
  LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` passed 55/55
  with the existing xUnit analyzer warning; Release `net10.0-windows` and `net472` x64 builds
  passed 0 warnings/0 errors; `git diff --check` passed with CRLF normalization warnings only.
  After the final rebuild/restart, PID `77988` served `/`, `/data.json`, `/metrics`, and
  `/dash/cardtruth/` as 200.
- Accepted observations: narrow row-name ellipsis is expected for long labels at 320/390;
  source text truncation at 320/390/~768 is acceptable because primary values/units hard-clip
  count is 0 and full raw labels/`SensorId` remain available in expansion/search. The single
  `GPU Mem J...` card-label truncation at ~768 touch is the known D2a baseline. Desktop hover
  controls remain compact; touch controls are now minimum 24 px and the destructive Hide action
  stays out of the touch card/row cluster.

D3-03's confirmed mobile row-value overlap is fixed by moving row controls in-flow on touch/narrow
layouts; D3 closes only because the browser matrix and screenshot review both passed.

## 3. Review Lenses

D3 should judge the dashboard from several real user modes, not only from rectangle math.

- **First-glance operator:** within three seconds, the user can answer whether the host is live,
  whether anything is warning/critical, which top cards matter, and how many sensors are hidden
  or offscreen. Normal state is calm; abnormal state is obvious.
- **Triage mode:** when something looks wrong, the user can drill from card or row to raw label,
  `SensorId`, hardware identity, range provenance, min/max, and local actions without hunting in
  a separate management surface.
- **Routine monitoring:** repeated viewing should feel dense, quiet, and stable. Controls are
  discoverable when needed, but telemetry stays visually dominant.
- **Configuration/editing:** direct add/remove/pin/hide/reorder controls belong on the item being
  edited and must be reversible. Destructive or de-surfacing actions should never be easier to hit
  accidentally on touch than they are on desktop.
- **Mobile/touch:** phone/tablet layouts optimize scanning and safe direct edits, not forced
  desktop parity. If a control cluster does not fit, it stacks in-flow; it must not cover readings.
- **Keyboard/accessibility:** focus order follows visual order; essential actions are not
  hover-only; controls have useful labels; raw identifiers remain reachable.
- **Trust/data honesty:** color communicates state or sensor kind only. Arcs appear only for
  trusted ranges. Unknown, estimated, or peak-only context stays visibly secondary.

## 4. UI Feel Contract

Use this as the D3 design bar before accepting a screenshot or visual fix.

- The dashboard should feel like an operational instrument panel: dense, legible, restrained,
  and honest. It should not feel like a debug table, a marketing landing page, or a decorative
  card gallery.
- Hierarchy is masthead status/actions, primary flight display cards, abnormal placard when
  present, subsystem panels, network panels, then footer/supporting links.
- Semantic color is scarce: health/state colors for status, type colors for sensor kind, and
  action accents for selected/pinned/active controls. Avoid adding decorative color systems.
- Normal state is quiet. Warning and critical states earn salience through placards, rails/chips,
  and value color, not through constant animation or visual noise.
- Controls are tertiary. They should be local, visible on hover/focus/touch as required, and never
  visually louder than the telemetry they operate on.
- Details are progressive disclosure on the visible item. A popover is acceptable for discovery;
  a permanent side drawer is not part of the v3 normal workflow.
- Touch layouts may change control placement. The rule is readability first, action availability
  second, desktop visual parity third.
- Motion is one-shot and functional only. No polling strobe, layout jumping, or animation that
  makes values harder to scan.
- Cards keep the existing compact card language and radius unless an accepted spec changes it.
  Do not introduce nested cards or decorative section cards during D3.

## 5. Stat Card Rules

Every primary or pinned stat card should answer, in priority order:

1. What sensor is this?
2. What is the current value and unit?
3. Is it OK, watch, critical, unknown, or estimated?
4. Where did it come from?
5. Is the visual range trusted, overridden, derived, or absent?
6. What can I do to this card right here?

D3 acceptance rules for stat cards:

- The value and unit are the strongest readout. Source, trend, ceiling, and controls are secondary.
- A number-only card without a trusted range must still look deliberate, not empty or unfinished.
- No arc is shown for peak-only, unknown, or untrusted ranges. `no known range`, `observed peak`,
  `derived`, or override language stays honest and compact.
- A card gets one dominant visual idea. Arc, sparkline, trend text, status chip, and controls must
  not all compete for the same attention.
- Long names and sources may truncate, but expansion/search must expose the full raw label and
  `SensorId`. Truncation is acceptable only when the primary value remains readable.
- Touch-visible card controls may be compact, but they must not cause name, chip, source, value,
  unit, or ceiling clipping beyond an explicitly recorded baseline.
- Health chips should be sparse. If every card is fine, the page should not become a field of
  loud green badges.
- Cards for duplicate devices should remain distinguishable without hardcoding host-specific
  labels. Hardware identity belongs in details and source text, not product assumptions.

## 6. Matrix

Run the matrix on stable `/` first. Run `/dash/cardtruth/` only while the preview route remains active
and only as a comparison surface.

| Width | Pointer mode | Required themes | Purpose |
|---:|---|---|---|
| 320 | touch / `hover:none` | dark, light | minimum mobile; masthead, popovers, rows, card controls |
| 390 | touch / `hover:none` | dark, light | common phone width; D1/D2a regression width |
| 640 | desktop hover + touch if practical | dark, light | breakpoint boundary |
| ~768 | touch / `hover:none` | dark, light | D2a stop-condition width for card clusters |
| 1440 | desktop hover/focus | dark, light | normal desktop |
| 1920+ | desktop hover/focus | dark, light | wide dashboard; D2 overlay span/column count |

Required short-height cases:

- 320 x 568 touch, dark and light, for old/small phones and Sensors `max-height:70vh`.
- 390 x 844 touch, dark and light, for common phone height and bottom-card overlays.

Required interaction states:

- default view after hard reload;
- card hover/focus and touch-visible controls;
- row hover/focus and touch-visible controls;
- one top, middle, and bottom PFD card expanded;
- one pinned card expanded if pinned cards exist;
- one panel row expanded;
- Sensors popover open with visible, hidden, and offscreen rows;
- Pages menu open;
- Subsystems header with non-empty `panelOrder` so `#panelsReset` renders;
- Network adapter headers with active panels, adapter move controls, and at least one hidden adapter restore row where the host supplies NIC data.
- Longest observed sensor label, longest `SensorId`, longest network adapter label, and a long host/hardware label visible in at least one screenshot or probe report.

## 7. Patch List

| ID | Severity | Finding | Evidence | D3 acceptance / fix rule |
|---|---|---|---|---|
| D3-01 | High | Sensors popover can clip offscreen on 320px mobile. | `.controls` becomes a two-column mobile grid, `.page-menu-panel` gets a mobile anchor correction, but `.sensors-panel` stays `right:0;width:min(420px,92vw)` (`console.css` mobile controls, page menu, sensors panel rules). | At 320 and 390 in both themes, the Sensors panel rect must stay within the viewport. If it fails, make `.sensors-menu` span both mobile columns or anchor `.sensors-panel` to the viewport/wrap with `left:0; right:auto; width:calc(100vw - 32px)`. |
| D3-02 | Medium | Sensors popover row actions can starve sensor text after D2a. | Visible rows render visibility chip + primary + pin + hide buttons, and `.sensors-panel .sensor-choice` uses four grid columns. | At 320/390, long labels and `SensorId` must remain readable or intentionally truncated with full text still accessible. If text collapses, stack row actions below the text or use compact icon buttons under the mobile breakpoint. |
| D3-03 | High | Row control clusters overlap mobile row values. | `.row-ctl` is `position:absolute;right:12px`, touch mode forces it visible, and the live 390px touch pass found `rowOverlapCount=11` plus `rowValueOverflowCount=11`. `ctlCluster` now adds star/pin/hide beside row move controls. | At 320/390/640 touch and keyboard focus, `.row-ctl` must not cover `.rn`, `.rv`, bars, or row expansion targets. Prefer moving row controls in-flow or stacking them as a second row on touch/narrow widths. D3 cannot close while the 390px overlap probe is non-zero. |
| D3-04 | Medium | Panel-header controls and `#panelsReset` need narrow-width proof and content-priority review. | Panel headers carry always-visible ▲▼ and adapter ⊘ controls before the label, `head-stat` is pushed right, and `#panelsReset` lives inside the Subsystems `.sec-head`. | With non-empty panel order and active NIC panels, header controls and labels must remain inside their panels at 320/390/640. Panel name/status stay legible; controls may wrap, stack, or compact first. `#panelsReset` must not clip the section title, tag, or rule. |
| D3-05 | Medium | D2a touch card cluster fix needs matrix regression coverage. | D2a drops `.ctl.hide` on coarse-pointer/touch to avoid the fourth button truncating card names around 768px. | At ~768 touch in both themes, PFD card clusters should be `[grip, star, pin]`, not Hide. Desktop hover/focus still shows Hide. Any card-name truncation above the known pre-D2a baseline is a stop condition. |
| D3-06 | Low | `.cell .chip-state` ellipsis is inert on `inline-flex`. | The D1/D2 review noted `text-overflow:ellipsis` does not ellipsize flex item contents. | Decide in D3: accept because current `OK/WATCH/CRIT` labels are short, or fix by adding an inner text span / changing display so long future chip text ellipsizes instead of hard-clipping. Record the decision in closeout. |
| D3-07 | Note | D2 overlay needs occlusion/collision checks, not only zero-displacement checks. | `.xp-overlay` is absolutely positioned under the card and does not reserve layout height. | Expanded top/middle/bottom cards must be readable, easy to dismiss, and not trap clicks. Covering later cards is acceptable only if visually intentional and documented with screenshots; covering masthead/popovers/menus is not acceptable. Verify click-away, Escape, scroll, resize re-anchor, and Sensors/Pages popover coexistence. |
| D3-08 | Note | DOM-less selftest cannot validate layout. | `node webtests/selftest.node.js` exercises pure `SQ.*` helpers only. | Treat selftest as a regression guard, not the D3 gate. D3 is complete only with browser rect checks plus screenshots in both themes. |
| D3-09 | Note | Mobile masthead and Pages menu still need full matrix coverage. | Slice 6 requires mobile masthead fit, and only `page-menu-panel` has an explicit mobile adjustment. | At 320/390/640, rate slider, Pause, Graphs, Theme, Pages, and Sensors remain reachable; no horizontal scroll or clipped buttons. |
| D3-10 | Note | Light theme parity must be proven after every CSS fix. | Repo invariant: both themes first-class. | Every D3 screenshot and rect probe runs dark and light. A fix verified only in dark is incomplete. |
| D3-11 | Medium | Stat-card readout rhythm needs user-visible review, not only overflow counters. | The live browser probe flagged several cards with large readout/source overflow candidates on desktop and mobile. Some may be benign flex sizing; some may be visible clipping or awkward wrapping. | At 320/390/640/~768/1440/wide, inspect cards with long names, long sources, number-only readouts, command suffixes, and ceiling labels. Primary value+unit must remain readable; source/range may truncate only with full detail available in expansion. |
| D3-12 | Medium | Sensors button and panel open-state alignment were not fully proven in the latest mobile pass. | The live pass measured the button position and default layout but did not complete every open-state popover probe. | D3 must open Sensors at 320/390 in both themes, verify panel rect, search input, visible/hidden/offscreen rows, primary/pin/hide actions, and hidden-adapter restore rows where present. |
| D3-13 | Medium | Pure geometry can miss user-perceived regressions. | D1 Option A previously passed overlap probes while still making default names worse at rest. | Each viewport/theme state needs a screenshot review against §3-§5, not only numeric probes. Reject fixes that pass rectangles but make first-glance scan, triage, or routine monitoring worse. |
| D3-14 | Low | Focus and accidental-touch behavior need explicit review after direct controls were added everywhere. | D2a put primary-card controls on cards, rows, and the Sensors popover; mobile/touch keeps many controls visible. | Keyboard tab order must be predictable, labels must be meaningful, and touch targets must not make hide/remove actions easy to trigger while scrolling or expanding rows. |
| D3-15 | Low | Route-surface divergence can confuse D3 evidence. | Stable `/` has `#sensorsMenu` and no drawer, while `/dash/cardtruth/` remains a legacy comparison route with Customize/drawer DOM until Phase E. | Root `/` is the acceptance surface. D3 evidence should assert `/` has `#sensorsMenu` and no drawer workflow; `/dash/cardtruth/` evidence is comparison-only while the preview route exists. |
| D3-16 | Low | Viewport width checks are insufficient for popovers and bottom overlays. | Sensors panel uses `max-height:min(70vh,560px)` and overlays are positioned below cards by offset math. | Add 320x568 and 390x844 checks. Open Sensors and a lower-card overlay; no horizontal scroll, clipped critical controls, unreachable popover actions, or offscreen overlay controls. |

## 7.1 Patch List Closeout

| ID | Status | D3 outcome |
|---|---|---|
| D3-01 | Fixed | Mobile Sensors rules now apply after the base popover rule: full-width mobile anchor plus `max-height:min(62vh,560px)`. 320x568 Sensors box is `16,211 -> 304,563`, inside the viewport in both themes. |
| D3-02 | Fixed | At <=640px, Sensors rows stack text, chip, and actions in one column; 320/390 screenshots keep label, source/type/value, `SensorId`, and row actions readable/reachable. |
| D3-03 | Fixed | `.row-ctl` moves in-flow on touch/narrow rows. Final matrix: `maxRowOverlap=0`, `maxRowValueOverflow=0`; row screenshots show readings first and controls below. |
| D3-04 | Fixed/proven | `.sec-head` and `.panel-head` wrap on mobile; stateful probe with non-empty `panelOrder` shows `#panelsReset` in viewport and `panelHeaderBadCount=0` at 320/390/640. |
| D3-05 | Proven | 768 touch dark/light still shows `[grip, star, pin]`; Hide remains desktop-only in the card/row cluster and available from the Sensors popover. |
| D3-06 | Accepted | Current `OK/WATCH/CRIT` chip labels are short, hard-clip count is 0, and no D3 screenshot shows chip clipping. Future longer chip text should add an inner text span or non-flex ellipsis. |
| D3-07 | Proven | Card overlay remains horizontally in viewport, opens below the card, does not cover masthead/popovers, and survives a poll tick with zero console errors. |
| D3-08 | Proven | D3 used browser rect probes plus screenshots; DOM-less selftest remains a regression guard only. |
| D3-09 | Proven | 320/390/640 masthead controls, rate slider, Pages, and Sensors remain reachable with no horizontal scroll. |
| D3-10 | Proven | Every matrix viewport ran dark and light. Light desktop screenshot review matched dark hierarchy and spacing. |
| D3-11 | Accepted/proven | Primary value+unit hard-clip count is 0. Long source/card labels may ellipsize at narrow widths; full raw label and `SensorId` remain in expansion/search. |
| D3-12 | Fixed/proven | Sensors open-state verified at 320/390 in both themes with visible, hidden, and offscreen rows; stateful probe also verified one hidden NIC restore row. |
| D3-13 | Proven | Screenshot review rejected geometry-only closeout; inspected mobile Sensors, mobile rows, tablet cards, desktop light, mobile overlay, and stateful panel reset. |
| D3-14 | Fixed/proven | Touch `.ctl` controls now have 24 px minimum dimensions; Hide is omitted from touch card/row clusters, reducing accidental destructive taps while scrolling. |
| D3-15 | Proven | Root `/` is the acceptance surface and has `#sensorsMenu` with no Customize drawer/button. `/dash/cardtruth/` remains comparison-only and still has legacy preview DOM until Phase E. |
| D3-16 | Proven | Short-height 320x568 and 390x844 checks pass for Sensors and overlays; no horizontal scroll, unreachable popover action, or offscreen critical control remains. |

## 8. Suggested Browser Probes

Use the browser tooling available in the session. These probes are examples; adapt selectors as needed
for the final implementation.

Viewport fit:

```javascript
(() => {
  const vw = innerWidth;
  const bad = [];
  document.querySelectorAll('.sensors-panel,.page-menu-panel,.cell,.row,.panel,.sec-head').forEach(el => {
    const r = el.getBoundingClientRect();
    if (r.left < -0.5 || r.right > vw + 0.5) {
      bad.push({ cls: el.className || el.tagName, left: r.left, right: r.right, vw });
    }
  });
  return bad;
})()
```

Document scroll-width gate:

```javascript
(() => ({
  innerWidth,
  scrollWidth: document.documentElement.scrollWidth,
  ok: document.documentElement.scrollWidth <= innerWidth
}))()
```

Row-control overlap:

```javascript
(() => [...document.querySelectorAll('.row')].map(row => {
  const ctl = row.querySelector('.row-ctl');
  const rn = row.querySelector('.rn');
  const rv = row.querySelector('.rv');
  if (!ctl) return null;
  const c = ctl.getBoundingClientRect();
  const overlaps = [rn, rv].filter(Boolean).map(el => {
    const r = el.getBoundingClientRect();
    return !(c.right <= r.left || c.left >= r.right || c.bottom <= r.top || c.top >= r.bottom);
  });
  return overlaps.some(Boolean) ? { label: rn?.textContent?.trim(), ctl: c.toJSON?.() ?? c } : null;
}).filter(Boolean))()
```

Card-control regression:

```javascript
(() => [...document.querySelectorAll('.cell')].map(cell => {
  const buttons = [...cell.querySelectorAll('.cell-ctl .ctl,.cell-ctl .grip')].map(b => b.textContent.trim() || b.title);
  const name = cell.querySelector('.name')?.textContent?.trim();
  const nameRect = cell.querySelector('.name')?.getBoundingClientRect();
  return { name, buttons, nameWidth: nameRect?.width };
}))()
```

Overlay check:

```javascript
(() => {
  const ov = document.querySelector('.xp-overlay');
  if (!ov) return { open: false };
  const r = ov.getBoundingClientRect();
  return {
    open: true,
    top: r.top,
    bottom: r.bottom,
    inViewportX: r.left >= 0 && r.right <= innerWidth,
    columns: new Set([...ov.querySelectorAll('.xp-grid > *')].map(el => Math.round(el.getBoundingClientRect().top))).size
  };
})()
```

Stat-card readout review:

```javascript
(() => [...document.querySelectorAll('.cell')].map(cell => {
  const q = sel => cell.querySelector(sel);
  const box = el => el ? el.getBoundingClientRect() : null;
  const over = el => !!el && (el.scrollWidth > el.clientWidth + 1 || el.scrollHeight > el.clientHeight + 1);
  return {
    name: q('.name')?.textContent?.trim(),
    value: q('.big')?.textContent?.trim(),
    source: q('.src')?.textContent?.trim(),
    nameOverflow: over(q('.name')),
    sourceOverflow: over(q('.src')),
    bigOverflow: over(q('.big')),
    cell: box(cell)?.toJSON?.() ?? box(cell)
  };
}))()
```

Keyboard/focus sweep:

```javascript
(() => [...document.querySelectorAll('button,[tabindex="0"],a,input,select')]
  .filter(el => el.offsetParent !== null)
  .map(el => ({
    text: (el.textContent || el.getAttribute('aria-label') || el.title || el.id || el.tagName).trim(),
    cls: el.className,
    rect: el.getBoundingClientRect().toJSON?.() ?? el.getBoundingClientRect()
  })))()
```

Touch target sanity:

```javascript
(() => [...document.querySelectorAll('.ctl,.iconbtn,summary.btn,.btn')]
  .filter(el => el.offsetParent !== null)
  .map(el => {
    const r = el.getBoundingClientRect();
    return {
      text: (el.textContent || el.getAttribute('aria-label') || el.title || el.id || el.tagName).trim(),
      width: Math.round(r.width),
      height: Math.round(r.height),
      tooSmall: r.width < 24 || r.height < 24
    };
  }))()
```

Route-surface assertion:

```javascript
(() => ({
  path: location.pathname,
  hasSensorsMenu: !!document.querySelector('#sensorsMenu'),
  hasCustomizeDrawer: !!document.querySelector('#customizeDrawer'),
  hasCustomizeButton: !!document.querySelector('#customize')
}))()
```

## 9. Static Gates

Run before and after D3 fixes:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
```

The preview `node --check` remains required while `/dash/cardtruth/` is still served.

## 10. Live Gate

1. Record the current process, executable stamp, and URLs before rebuild.
2. Stop the running EXE before the build.
3. Build `net10.0-windows` Release x64.
4. Restart the EXE and hard-reload browser tabs to avoid stale `console.js`.
5. Verify `GET /`, `/data.json`, `/metrics`, and `/dash/cardtruth/` while the preview route remains active.
6. Run the matrix in §6 with rect probes and screenshots.
7. Watch the browser console across poll ticks with Sensors popover and one card expansion open.
8. Keep Sensors open plus one overlay open across at least one polling tick; confirm no stale labels, no console errors, and no unexpected layout jump.

## 11. Closeout Checklist

- Patch-list item statuses recorded as fixed, accepted, or deferred with evidence.
- `docs/feature-web-dashboard-card-truth.md` §11 gets a D3 verification row with exact commands, live URLs, viewport/theme coverage, and screenshot/probe evidence.
- `2026-07-06-web-dashboard-v3-next-plan.md` §4 marks D3 complete only after the matrix is green, then advances to E1/E2.
- `docs/ai-guide.md` §6 snapshot is refreshed after D3 closes.
- `2026-07-06-web-dashboard-v3-continuation-handoff.md` top matter and progress log point to E as next after D3.
- `git diff --check` is clean.
