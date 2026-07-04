# HANDOFF — Web Dashboard Card Truth (round 1 stopped 2026-07-04)

**For the next session/agent.** Operator ordered: one round, then stop. Round 1 = Tasks A0–A3, done and committed. Nothing pushed.

## Where everything is

| Thing | Value |
|---|---|
| Repo | `E:\SQ_HQ\Monitoring\sq-librehw` (host: SND-DESK) |
| Work branch | `feat/web-card-truth-base` — **isolated worktree** `E:\SQ_HQ\Monitoring\sq-librehw\.worktrees\card-truth` (excluded via `.git/info/exclude`) |
| Cut from | master `db1d2d5` |
| Commits this round | `6ee50c5` (A1 state schema) → `13fbd6b` (A2 rangeFor) → `a6af9e1` (A3 fan pairing) |
| Plan (with per-task steps + code) | `docs/superpowers/plans/2026-07-04-web-dashboard-card-truth-plan.md` — see its **Execution Status** block |
| Spec (requirements + acceptance + log) | `docs/feature-web-dashboard-card-truth.md` |
| Test gate | `node webtests\selftest.node.js` → currently **SELFTEST PASS 100/100** (baseline was 85) |
| Build gate | `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 -o %TEMP%\lhm-verify` → 0 errors (use `-o` temp; default output may be locked by the running monitor — do NOT kill it) |

## What round 1 delivered (client-only, data.json untouched)

- `sq.dashboard.v1` gained additive fields: `rangeOverrides {id:{max,min?}}`, `observedMax {id:num}`, `rowOrder {groupKey:[ids]}`, `netAdapterOrder []`, `hiddenNetAdapters []` (normalizers follow the existing `cleanX` pattern).
- `SQ.rangeFor(s, limits, state) → {lo, hi, source: override|limit|band|peak, derived?}` — the single range resolver. `SQ.speedoRange` is now a thin `[lo,hi]` wrapper. `SQ.derivedPowerLimit` is a **stub returning null** until Task A5. `cardEl` computes `rr` and renders from it.
- `SQ.fanControlFor(fan, sensors)` pairs Fan↔Control by same `hwid` + identical `text`. Fan cards: arc = Control % (0–100), big number = RPM, `cmd N %` in card meta; fan rows show `· N %` after RPM. Unpaired fans keep the peak-estimate path.

## Resume point — exactly

**Plan Task A4 Step 1**: add the failing test for `SQ.mergeObservedPeaks` (test code is in the plan), run selftest expecting FAIL, then implement per plan. Then A5 (replaces the `derivedPowerLimit` stub — the A2 call-site condition `Power && /^GPU Package/` is already in place), B1, B2. After B2: `git checkout -b feat/web-card-first` in the worktree for Phase C (tasks C1–C5), and optionally a parallel `feat/web-row-subgroup-order` for Phase D. Phase E stays parked.

Work in the worktree, not the main checkout. TDD per task: tests → red → implement → green → one `feat(web)` commit.

## Warnings / open items

1. **Concurrent writer**: another session commits to master in this repo (it merged+deleted `feature/web-dashboard-customization` mid-planning on 2026-07-04 and removed the evidence screenshots; it left `docs/superpowers/specs/2026-07-04-dashboard-templates.md` untracked in the main checkout — not ours, don't touch). Check `git log master -3` before merging.
2. **No visual verification yet.** All green gates so far are model/build-level. At the Phase A gate (after A5): serve a fixture page or have the operator relaunch the monitor and check on `http://localhost:8085/`: fan arcs = %, power ceiling `≈`-labeled, override flow works. The plan's Phase A gate lists the exact checks.
3. **Merge**: when the operator says go — from the main checkout `git merge feat/web-card-truth-base` onto master (fast-forward-ish, only web assets + webtests + docs touched). `dotnet test …Tests.csproj -p:Platform=x64` must stay green with no golden diff (nothing outside `Resources/Web`/`webtests`/`docs` was modified).
4. Card sparkline for paired fans still auto-ranges on RPM history (deliberate; revisit only if it looks noisy in the visual pass).
5. Worktree cleanup after merge: `git worktree remove .worktrees/card-truth` (and drop the branch once merged).
