# Agent Task — Verify, Document, and Close Plan-011

**Plan:** `plan-011`

**Depends on:** Agents AP and AQ

**Exclusive outputs:**

- `.codex/skills/project.toml`
- `data/plans/plan-011.json`
- `docs/campaign-plan-011-settings-projection-and-persistence.md`
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/features/feature-memory-ui-reliability.md`
- `live-tracker.md`

## Goal

Independently verify the integrated Plan-011 settings projection/persistence seam, then record precise current truth once. Do not repair product or test files: report any gate defect to the coordinator. Ledger state is `implemented`; only a person may record `accepted`.

## Context — read before doing anything

1. `AGENTS.md`
2. `.codex/skills/project.toml`
3. `docs/campaign-playbook.md`
4. `docs/architecture/campaign-control-plane.md`
5. `docs/campaign-plan-011-settings-projection-and-persistence.md`
6. `docs/architecture/refactor-roadmap.md`
7. `docs/campaign-backlog.md`
8. `docs/campaign-history.md`
9. `docs/README.md`
10. `docs/features/feature-memory-ui-reliability.md`
11. `live-tracker.md`
12. all integrated AP/AQ files and unchanged settings, lifecycle, golden, HTTP, snapshot, project, and operations contracts named in the plan

Require the coordinator to supply the planning baseline, integrated AP/AQ commits, source commits, exact test/build results, proposed tracker rows, and a clean worktree based on integrated source.

## Task

### Part 1 — Independently verify source and contracts

Unset both live-config variables, disable terminal logging where counts matter, and run:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsPersistenceCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceCoordinatorTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan validate plan-011 --json
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
git diff --check
```

Expected: coordinator `7/7`; Application `167` discovered / `166` passed / one established skip; Contracts `73/73`; deterministic aggregate `308/307/1`; Avalonia `75/75` with only established AVLN3001; web `315/315` plus `18/18`; both WinForms targets `0W/0E`; all eight included non-deploying gates green.

Protect exact compatibility by proving `PersistentSettings.cs`, `RuntimePaths.cs`, `StartupManager.cs`, existing settings tests, Plan-008 snapshot/projector, Plan-009 HTTP listener/facade, Plan-010 lifecycle files/tests, `Computer.cs`, projects/packages/solutions, golden file, web, and operations files are unchanged from baseline `af84370`. Require golden Git blob `05113704acc6fefeb4128004b3f523d876fbcec4` and SHA-256 `BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.

Outside campaign/configuration/documentation truth, the exact diff is AP’s two new files plus AQ’s `MainForm.cs`. Prove source commit ancestry, patch IDs, owned blobs, and integrated blobs.

### Part 2 — Read-only SND-HOST separation proof

Run the verified-machine helper. Then only observe:

- exact live executable/process;
- root `\LibreHardwareMonitor` task state/action/working directory;
- proxy-bypass HTTP GET for `/`, `/data.json`, and `/metrics` on the actual bound address;
- current-day CSV size twice, proving growth.

Do not start, stop, restart, reconfigure, publish, promote, or write under the operational tree. This is separation/health evidence, not deployment proof.

### Part 3 — Record campaign truth

1. `data/plans/plan-011.json`: preserve all 13 elements plus R1-R3; add exact AP/AQ commits, counts, hashes, gates, live samples, diff/patch proof, and one evidence reason per exit criterion to `backfill_reasons`; keep plan status `executed`.
2. Generated campaign Markdown: never hand-edit it. Patch JSON, regenerate through the sanctioned `_persist_plan_artifacts` call, and prove a second render byte-identical.
3. `.codex/skills/project.toml`: add `SettingsPersistenceCoordinator.cs` only to the existing settings/runtime/startup conflict-zone row. Add no mapping or gate.
4. `docs/README.md`, roadmap, backlog: mark Phase-4 item 4 implemented; keep Phase 2 partial and A1 person-only; advance to Plan-012/agent `as`; make no deploy claim.
5. Feature reliability spec: record the new ownership boundary while keeping the underlying `PersistentSettings` contract and unrelated follow-ups intact.
6. `docs/campaign-history.md`: add Plan-011 with all 11 criterion-specific `implemented` entries and zero automatic accepted/deployed claims.
7. `live-tracker.md`: add accurate rows for AP, AQ, and AR using actual commits/results. AR is the only tracker writer.

Leave `AGENTS.md`, older plans/specs/history rows, root README, playbook, control-plane, source/tests/projects/packages/gates outside the one project.toml inventory edit, operations manifests/docs, release/candidate/rollback records, live tasks/settings/logs/runtime, and ignored execution/cache state unchanged.

## Exit Criteria

- Every Plan-011 criterion has command- or artifact-backed evidence; no gate is softened to fit a result.
- All nine exclusive outputs agree on commits, counts, hashes, Phase state, Plan-012/agent `as`, retained A1, live separation, and `implemented` state.
- No product, test, golden, project, package, solution, web, operations, candidate, release, rollback, task, setting, log, or runtime file changes.
- Closure is committed and the handoff lists each gate, document, remaining person-only action, and exact cleanup boundary.

## Constraints

- Edit only the nine exclusive outputs.
- Do not fix source/tests to make verification pass; report the defect.
- Do not hand-edit generated campaign Markdown.
- Preserve person-only acceptance and the source/candidate/live/rollback boundaries.

## Verification

Run the full command ladder above plus campaign-history regression tests, stale-reference gates, diff checks, plan render idempotence, guarded cleanup preview/apply/second-preview, and `git clean -ndX` showing only `data/tasks.json` and `data/analysis-cache.json`.

## Do NOT

- Do not create a candidate, deploy, promote, mutate tasks/settings/listeners/logs, accept the campaign, close A1, push, or clean another checkout.
- Do not remove preserved Plan-001 branches or raw-delete a worktree.

## Post-completion

Commit with `docs(campaign): close plan-011 settings seam`. Return the commit SHA, every exact result/hash/live sample, criterion evidence, changed files, remaining person-only action, and safe cleanup boundary.
