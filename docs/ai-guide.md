# AI Guide — Repo Orientation & Overall Plan

**Project:** LibreHardwareMonitor Sev IQ local fork
**Updated:** 2026-07-07 — §6 Snapshot is maintained as part of every phase closeout; everything else here is durable convention.
**Audience:** any AI session (or human) picking up work in this repo. Read this first; it tells you where truth lives and how work ships here.

## 1. What this repo is

A local fork of LibreHardwareMonitor ("Sev IQ") whose headline surface is a client-side **hardware-telemetry web console** served by the Windows app at `http://localhost:8085/` (also exposed at `https://telemetry.seviq.org/`). The web assets (`index.html`, `console.js`, `console.css`) are **embedded resources** in `LibreHardwareMonitor.Windows.Forms.exe` — served changes require a rebuild.

**This repo feeds a downstream consumer** (ThermalTrace): `data.json` and the CSV `Identifier`/`Time` columns are **external contracts**, enforced by golden tests. GitHub issues here are data-contract trackers, not a general backlog.

## 2. Read order (start here)

1. **This guide** — invariants + how work ships.
2. [`../AGENTS.md`](../AGENTS.md) — task-lane classification (review / build / bugfix / spec / feature) and the spec-first rule.
3. [`superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md`](superpowers/plans/2026-07-06-web-dashboard-v3-continuation-handoff.md) **§0 Resume Brief** — the current campaign state on one screen. If it disagrees with this guide's §6 snapshot, the handoff is newer — trust it and fix the snapshot.
4. [`superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`](superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md) **§4** — the authoritative A→F work queue.
5. [`feature-web-dashboard-card-truth.md`](feature-web-dashboard-card-truth.md) — the parent v3 spec; its **§11 verification log** is the evidence trail every phase appends to.
6. [`discovery-webserver-dashboard-interaction.md`](discovery-webserver-dashboard-interaction.md) + [`reviews/review-2026-07-07-webserver-dashboard-interaction.md`](reviews/review-2026-07-07-webserver-dashboard-interaction.md) + [`feature-webserver-api-hardening.md`](feature-webserver-api-hardening.md) — current map of how `HttpServer`, `data.json`, root `/`, preview `/dash/cardtruth/`, and legacy `/Sensor`/reset APIs interact, plus the bounded hardening pass for GET `/Sensor` failures and control-value range checks.
7. The active phase's own spec/plan under `docs/` and `docs/superpowers/plans/` (each next-plan §4 row links them).
8. `.superpowers/sdd/progress.md` — the execution ledger (git-ignored scratch): per-task models, gate numbers, RED→GREEN evidence, triage decisions. Trust it plus `git log` over conversation memory.
9. [`feature-workflow.md`](feature-workflow.md) — spec lifecycle + the full docs inventory.

## 3. Hard invariants (breaking one = stop and surface it)

- **Contract:** no `data.json` / HTTP server / CSV change. Golden `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64` stays **42/42**.
- **Build:** always `-p:Platform=x64` (AnyCPU breaks CsWin32 → CS0246). **Stop the running EXE before building** (it locks the DLL/EXE); restart after; **close stale browser tabs** after a rebuild (multi-tab version skew: old tabs run old `console.js` and can strip new persisted fields on save).
- **No host-specific sensor IDs, labels, or limits in product code** — the dashboard must work on any machine's `data.json`.
- **Read-only dashboard:** no `/Sensor?action=Set`, no write UI. This does **not** mean the whole webserver is read-only: legacy `/Sensor` and reset APIs still exist; see the 2026-07-07 server/dashboard audit and API-hardening spec before exposing the listener beyond the trusted local operator path.
- **Label honesty:** raw LibreHardwareMonitor labels + `SensorId` stay visible wherever aliases/renames exist; missing data renders as missing/estimated, never as fake precision.
- **Both themes first-class:** every UI change is verified in dark AND light.
- **DOM-less selftest stays green:** `node webtests/selftest.node.js`. It exercises `SQ.*` pure functions only — it is a **regression guard, never the gate for UI/layout work**.
- **Persistence discipline:** `sq.dashboard.v1` gains no new persisted fields without a spec saying so; per-render derivations live on in-memory `state` only.
- **Semantic color honesty:** state colors (`--ok/--warn/--crit`) mean health only; selection/active controls use the accent treatments (lime like `pin.on`, cyan hover).
- **AssemblyVersion stays 0.9.6** (data.json contract); local builds stamp git SHA+date into FileVersion/ProductVersion — the stamp tells you what an EXE actually contains.

## 4. How a phase ships (the loop that carried A1 → D2)

1. **Finding → queue row.** Operator feedback or a live audit becomes a row in next-plan §4 with grounded `file:line` evidence.
2. **Brainstorm → Draft spec** with explicit **§9 Open Decisions** — acceptance-blocking choices belong to the operator, never buried in implementation notes. Spec flips Draft→Accepted when they're resolved. (Small mechanical items may go straight to a plan doc that embeds the decision record.)
3. **Plan** (superpowers:writing-plans): bite-size tasks with the exact code in the steps. Plans are **revisable** — new operator evidence legitimately re-opens a §9 decision (D2 shipped as rev 2 after "B grid-breakout" was overturned by UX feedback). Record re-resolutions, don't overwrite history.
4. **Execute** (superpowers:subagent-driven-development): implementers do **static work only** (edits + `node --check` + selftest + commit — no rebuild, no browser); per-task spec+quality reviews; ledger in `.superpowers/sdd/progress.md`. Right-size models: cheap for verbatim transcription, mid for DOM/integration wiring, capable only where judgment is dense (final reviews scale to diff risk). Strong controller, cheap workers.
5. **Controller-owned live gate:** run the measurement **RED on the old build first** (a gate that can't fail is broken), rebuild once, then GREEN — and **always pair measurements with live screenshots in both themes** (D1's Option A passed every rect check while visibly truncating names). Pause polling before rect-snapshot comparisons (the 1s tick repaints cards mid-measurement and fakes failures).
6. **Closeout in the same commits:** spec §10/§11 filled, card-truth §11 row, next-plan §4 row ✅ + critical path advanced, handoff §0 updated. **The resume brief must never describe a state that is no longer true** — stale "uncommitted"/"not merged" claims have cost real time.
7. **Final whole-branch review** (fresh reviewer, cross-task seams as the charge) → **merge** via superpowers:finishing-a-development-branch as a `--no-ff` phase merge (`Merge <thing> (Phase Xn)`), push origin, post-merge selftest, leave the rebuilt app running.

## 5. Lessons ledger (apply by default; each was paid for)

- **Live visual gate beats measurement** — a GREEN geometry gate can hide a visible regression; screenshots in both themes are part of "done".
- **Sig-gated rebuilds:** every datum a rebuilt row *displays* must be a term in its rebuild signature, or the label goes stale while the surface is open (C1's `· idle`; D2a's `:primary` term).
- **Capture-phase before bubble rebuilds:** click-outside/away listeners must run capture-phase and be blind to control surfaces, or a bubble-phase rebuild detaches `e.target` and every control click reads as "outside".
- **One-shot animations gate on the action, not the render** — anything animated on render strobes at the 1s poll (`state.xpEnter` pattern: set before the synchronous `rerender()`, clear after).
- **Overlays portal as siblings without `data-key`** — absolutely-positioned grid children take no track; drag/reorder machinery keyed on `data-key` never sees them (D2).
- **chrome-devtools MCP profile lock:** if the browser drops mid-session ("already running for chrome-profile"), kill the `*chrome-devtools-mcp*` chrome processes and reopen.
- **`<details>` toggling is async;** PFD and pinned twins share the expand key `c:<sensorId>` by design.
- **Deleting UI needs a parity re-assessment first** — B3's drawer removal found the claimed gap list was wrong; verify which affordances actually lack an inline home before deleting their old one.
- **Dashboard truth != server truth:** browser aliases, overrides, hidden state, observed peaks, and derived limits are presentation state over `data.json`; keep raw labels, `SensorId`, and provenance visible, and never imply they mutated LibreHardwareMonitor.

## 6. Snapshot — overall plan and current position (as of 2026-07-07)

**Vision:** an honest, modern, card-first telemetry console — no invented maxima, real/derived/overridden ranges with provenance, hardware identity for duplicate devices, detail and actions living **on the visible item** (no drawers/side panes), everything orderable from the UI, dense but attractive in both themes.

**Shipped on `master` (each major phase has a card-truth §11 evidence row):** v2 customization + cards; card-truth slices 1–3 (honest ranges, fan %/RPM, identity, expansion actions); A1/A2 clipping fixes; **B1** masthead Sensors popover; **B2** explicit primary-card selection (seed-from-visible); **B3** Customize drawer removal; **C1** per-adapter network subgroups; **D1** card-header reserved gutter (structural non-overlap); **D2** anchored-overlay expansion (full-grid-width detail, zero displacement — merge `6a2c2d7`); **D2a** direct flight-deck edit controls (★/☆ primary toggle on cards/rows + popover, merge `0279333`, docs pin `db4a9da`); **D3** responsive/theme QA closeout (row controls in-flow on touch/narrow, mobile Sensors fit, panel/stateful controls proved, dark/light matrix clean).

**Queue (next-plan §4 is authoritative):**
- **E1/E2 — `viewTheme` selector, sync accepted deltas to `/`, retire the `/dash/cardtruth/` preview route** ← *next* (one product surface; route-namespace-ready state).
- **F — context dashboards (Main/Gaming/Storage)** — separate campaign; orthogonal to the E-phase view selector (two dropdowns, deferred coexist lane).
- **X1 — planning-doc consolidation** (archive the superseded 2026-07-04 set) — can run anytime.

**Runtime:** app serves `localhost:8085`; D3 live closeout on 2026-07-07 rebuilt Release `net10.0-windows` x64 as PID `79624`, with `GET /`, `/data.json`, `/metrics`, and `/dash/cardtruth/` returning 200. Final D3 Edge matrix ran 14 viewport/theme cases with `maxRowOverlap=0`, `maxRowValueOverflow=0`, no horizontal scroll, Sensors/Page/overlay fit true, and zero console warnings/errors. Root `/` asserted `#sensorsMenu` and no Customize drawer/button; `/dash/cardtruth/` remains comparison-only until Phase E. The server/dashboard audit found `GET /Sensor?action=Get&id=/missing` returned HTTP 500 while POST returned JSON fail; `feature-webserver-api-hardening.md` fixed that bounded server bug and live rebuilt PID `30508` returned JSON failures for missing GET `/Sensor` and GET `action=Set`. Legacy write-auth and GET reset policy remain open compatibility decisions.

## 7. Quick commands

```powershell
# regression guard (DOM-less; NOT a UI gate)
node webtests\selftest.node.js                       # expect SELFTEST PASS 227/227
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js

# contract gate
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64   # 42/42

# build the app (STOP the running EXE first; restart after)
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
# EXE: bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe  (serves http://localhost:8085/)
```
