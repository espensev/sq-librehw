# Agent Task — Verify, Document, and Close Plan-007

**Plan:** `plan-007`
**Depends on:** AC, AD, and AE integrated into the supplied baseline
**Exclusive outputs:**

- `data/plans/plan-007.json`
- `docs/campaign-plan-007-characterization-tests-before-extraction.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `live-tracker.md`

## Goal

Independently verify the integrated test-only campaign and record criterion-specific current truth. Do not repair test or production files: report any defect to the coordinator.

## Inputs required from the coordinator

- Integrated commit SHAs for AC, AD, and AE.
- Their focused/full test counts and proposed tracker updates.
- The exact pre-integration commit used for the campaign diff.
- A worktree based on the integrated branch, not the original planning baseline.

## Verification

Run with both `LHM_LIVE_CONFIG_PATH` variables unset and disable the .NET terminal logger where counts matter:

```powershell
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HardwareGroupLifetimeCharacterizationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SettingsProjectionTests|FullyQualifiedName~SettingsPersistenceTests|FullyQualifiedName~RuntimePathsTests|FullyQualifiedName~StartupManagerTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan preflight --json
git diff --check
```

The aggregate result must be exactly 279 discovered, 278 passed, and the one existing opt-in skip. Confirm the implementation diff is limited to the seven plan-owned test files, with no production, project, package, gate, golden-master, or operations change.

Perform only read-only SND-HOST separation proof: verified identity, live executable/process ownership, scheduled-task action and working directory, proxy-bypassed `/`, `/data.json`, and `/metrics` HTTP status, and current-day CSV growth. Do not restart, reconfigure, publish, or write the operational tree.

## Exit Criteria

- All focused filters, the 279/278/1 aggregate population, both framework builds, full CI sweep, planner preflight, and Git diff checks pass.
- The implementation diff is limited to the seven Plan-007 test files and the live deployment separation proof is read-only and healthy.
- All six exclusive campaign-truth files agree on exact commits, counts, criterion evidence, Phase-3 closure, Plan-008 queue position, retained A1, and ledger state `implemented`.
- The documentation-only change is committed with the required message and the handoff lists every command result and any remaining person-only action.

## Documentation work

1. `docs/campaign-plan-007-characterization-tests-before-extraction.md`
   - Set execution/closure state accurately.
   - Replace broad analyzer boilerplate with the scoped ownership, integration, risks, exact counts, and evidence.
2. `data/plans/plan-007.json`
   - Keep schema-valid plan elements synchronized with the campaign doc; record criterion evidence and implemented ledger state without claiming maintainer acceptance.
3. `docs/architecture/refactor-roadmap.md`
   - Mark the Phase-3 characterization item complete and Phase 3 complete; do not start Phase 4.
4. `docs/campaign-backlog.md`
   - Remove the completed Plan-007 section, advance the next executable campaign to Plan-008, and preserve person-only A1.
5. `docs/campaign-history.md`
   - Add a Plan-007 row and evidence for every exit criterion. State `implemented`, never `accepted` unless the maintainer explicitly accepts it.
6. `live-tracker.md`
   - Add one row each for AC, AD, AE, and AF using their actual commits/counts.

## Boundaries

- Edit only the six exclusive outputs.
- Do not edit tests to make a gate pass; report the defect and stop documentation closure.
- Do not touch operational documents under `E:\SQ_HQ\Monitoring`, the live runtime, tasks, logs, release store, or rollback store.
- Preserve A1 and unrelated history exactly.

## Handoff

Commit the six owned files with `docs(campaign): close plan-007 characterization`. Return the commit SHA, exact command results, per-criterion evidence, files changed, and any remaining gate or person-only action. Do not merge it yourself.
