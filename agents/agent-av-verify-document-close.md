# Agent Task — Verify, Document, and Close Plan-012

**Plan:** `plan-012`

**Baseline:** `9d4786c15e7c800fb07ba2eeecd6a0e112b80eb0`

**Depends on:** Agents AS, AT, and AU

**Exclusive outputs:**

- `.codex/skills/project.toml`
- `data/plans/plan-012.json`
- `docs/campaign-plan-012-winforms-presentation-surface-adapters.md`
- `docs/plan-012-senior-developer-brief.html`
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/features/feature-native-ui-modernization.md`
- `live-tracker.md`

## Goal

Independently verify the integrated Plan-012 presentation adapter seam and every protected external/lifecycle/persistence/UI contract, then record precise current truth once. Refresh the visual senior-developer brief with actual source commits and results. Do not repair product or test files; report defects to the coordinator. Ledger state is `implemented`; only a person may record `accepted`.

## Context — read before doing anything

1. `AGENTS.md`
2. `.codex/skills/project.toml`
3. `docs/campaign-playbook.md`
4. `docs/architecture/campaign-control-plane.md`
5. `docs/campaign-plan-012-winforms-presentation-surface-adapters.md`
6. `data/plans/plan-012.json`
7. `docs/architecture/refactor-roadmap.md`
8. `docs/campaign-backlog.md`
9. `docs/campaign-history.md`
10. `docs/README.md`
11. `docs/features/feature-native-ui-modernization.md`
12. `docs/plan-012-senior-developer-brief.html`
13. `live-tracker.md`
14. all integrated AS/AT/AU files and every unchanged tree, plot, tray, gadget, snapshot, HTTP, lifecycle, settings, hardware, project, and operations contract named in the plan

Require the coordinator to supply the clean planning baseline, integrated AS/AT/AU commits, original source commits, exact focused/full/build results, proposed tracker rows, and a clean primary worktree based on integrated source.

## Task

### Part 1 — Independently verify source and contracts

Unset both live-config variables, disable terminal logging where counts matter, and run:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~PresentationSurfaceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~WinFormsUiLifetimeTests|FullyQualifiedName~PlotPanelHistoryTests|FullyQualifiedName~PlotPanelTextScaleTests|FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests|FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests|FullyQualifiedName~RuntimePathsTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan validate plan-012 --json
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
git diff --check
```

Expected: coordinator `12/12`; Application `179` discovered / `178` passed / one established live-config opt-in skip; Contracts `73/73`; deterministic aggregate `320/319/1`; Avalonia `75/75` with only established AVLN3001; web `315/315` plus `18/18`; both WinForms targets `0W/0E`; all eight included non-deploying gates green.

Protect compatibility by proving `PlotPanel.cs`, `SystemTray.cs`, `SensorGadget.cs`, `Gadget.cs`, `GadgetWindow.cs`, both TreeModel files, `TreeViewAdv` and themed scrollbar/UI Automation files, Plan-008 snapshot/projector, Plan-009 listener/HTTP facade, Plan-010 lifecycle files, Plan-011 persistence files, `Computer.cs`, projects/packages/solutions/manifests, golden, web, and operations files are unchanged from baseline `9d4786c`. Require golden Git blob `05113704acc6fefeb4128004b3f523d876fbcec4` and SHA-256 `BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.

Outside campaign/configuration/documentation truth, the exact diff is AS's coordinator and twelve-test file, AT's concrete adapter file, and AU's `MainForm.cs`. Prove commit ancestry, patch IDs, owned blobs, and integrated blobs for all three source agents.

### Part 2 — Read-only SND-HOST separation proof

Run the verified-machine helper and require `VERIFIED` with machine ID `snd-host`. Then only observe:

- exact live executable/process;
- root `\LibreHardwareMonitor` task state/action/working directory;
- proxy-bypass HTTP GET for `/`, `/data.json`, and `/metrics` on the actual bound address;
- current-day CSV size twice, proving growth.

Do not start, stop, restart, reconfigure, publish, promote, or write under the operational tree. This is separation/health evidence, not deployment proof.

### Part 3 — Record campaign truth

1. `data/plans/plan-012.json`: preserve all 13 elements plus R1-R3; add actual AS/AT/AU commits, exact counts/hashes/gates/live samples/diff proof, and one evidence reason per exit criterion to `backfill_reasons`; keep plan status `executed`.
2. Generated campaign Markdown: never hand-edit it. Patch JSON, regenerate through sanctioned `_persist_plan_artifacts`, and prove a second render byte-identical.
3. `.codex/skills/project.toml`: add one presentation-surface conflict-zone row covering the two new source files plus the existing MainForm/PlotPanel/SystemTray/SensorGadget entry points. Add no mapping, command, gate, or project change.
4. `docs/README.md`, roadmap, and backlog: mark Phase-4 item 5 implemented and Phase 4 complete; keep Phase 2 partial and A1 person-only; advance to Plan-013/agent `aw`; make no deploy claim.
5. Native UI modernization spec: record the legacy WinForms adapter boundary, its intentionally retained direct tree interaction policy, and the fact that it is not the later host-neutral immutable presentation seam.
6. `docs/campaign-history.md`: add Plan-012 with all twelve criterion-specific `implemented` entries and zero automatic accepted/deployed claims.
7. `live-tracker.md`: add accurate rows for AS, AT, AU, and AV using actual commits/results. AV is the only tracker writer.
8. `docs/plan-012-senior-developer-brief.html`: replace planned placeholders with actual commits/results and a concise architecture result. Keep it self-contained/offline, responsive, keyboard-readable, printable, and explicitly labeled “presentation brief — not execution authority” and “no candidate/deployment.” Visually inspect a wide and narrow render; retain relative links to canonical plan JSON, generated campaign Markdown, roadmap, specs, and key source files.

Leave `AGENTS.md`, older plans/specs/history rows, root README, playbook, control-plane, source/tests/projects/packages/gates outside the one project.toml inventory edit, operations manifests/docs, release/candidate/rollback records, live tasks/settings/logs/runtime, and ignored execution/cache state unchanged.

## Exit Criteria

- Every Plan-012 criterion has command- or artifact-backed evidence; no gate is softened to fit a result.
- All ten exclusive outputs agree on commits, counts, hashes, Phase state, Plan-013/agent `aw`, retained A1, live separation, `implemented` state, and no deployment.
- The HTML brief renders cleanly at desktop and mobile widths, distinguishes prior completed work from Plan-012 results, and links to canonical authority without becoming a second handoff authority.
- No product, test, golden, project, package, solution, web, operations, candidate, release, rollback, task, setting, log, or runtime file changes.
- Closure is committed and the handoff lists each gate, document, remaining person-only action, and exact cleanup boundary.

## Constraints

- Edit only the ten exclusive outputs.
- Do not fix source/tests to make verification pass; report the defect.
- Do not hand-edit generated campaign Markdown.
- Preserve person-only acceptance and source/candidate/live/rollback boundaries.
- Keep the HTML free of external fonts, scripts, analytics, network calls, embedded secrets, or claims not backed by the plan/evidence.

## Verification

Run the full command ladder above plus campaign-history regression tests, stale-reference gates, docs synchronization, HTML link/file checks, two viewport renders, plan render idempotence, guarded cleanup preview/apply/second-preview, and `git clean -ndX` showing only `data/tasks.json` and `data/analysis-cache.json`.

## Do NOT

- Do not create a candidate, deploy, promote, mutate tasks/settings/listeners/logs, accept the campaign, close A1, push, or clean another checkout.
- Do not remove preserved Plan-001 branches or raw-delete a worktree.
- Do not make the visual brief the continuation authority; the plan JSON, generated campaign plan, roadmap/backlog, and tracker remain canonical.

## Post-completion

Commit with `docs(campaign): close plan-012 presentation adapters`. Return the commit SHA, every exact result/hash/live sample, criterion evidence, changed files, HTML render evidence, remaining person-only action, and safe cleanup boundary.
