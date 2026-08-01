# Agent Task — Verify, Document, and Close Plan-010

**Plan:** `plan-010`
**Depends on:** Agent AM, Agent AN
**Exclusive outputs:**

- `.codex/skills/project.toml`
- `data/plans/plan-010.json`
- `docs/campaign-plan-010-application-lifecycle-and-polling.md`
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/features/feature-memory-ui-reliability.md`
- `live-tracker.md`

## Goal

Independently verify the integrated Plan-010 lifecycle/polling seam, then record
precise current truth once. Do not repair product or test files: report a gate
defect to the coordinator. Ledger state is `implemented`; only a person may
record `accepted`.

## Inputs required from the coordinator

- Planning baseline and integrated AM/AN commit SHAs.
- AM and AN source commit SHAs, exact results, and proposed tracker rows.
- A clean worktree based on the integrated source.
- Pre-campaign read-only live sample and permission to take the same standard
  closure sample.

## Read first

- `AGENTS.md`
- `.codex/skills/project.toml`
- `docs/campaign-playbook.md`
- `docs/architecture/campaign-control-plane.md`
- `docs/campaign-plan-010-application-lifecycle-and-polling.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/README.md`
- `docs/features/feature-memory-ui-reliability.md`
- `live-tracker.md`
- all integrated AM/AN files and the unchanged coordinator/lifetime/golden/
  HTTP/settings contracts named in the plan

## Independent verification

Unset live-config variables, disable the .NET terminal logger where counts
matter, and run:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ComputerOpenLifetimeTests|FullyQualifiedName~HardwareGroupLifetimeCharacterizationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan validate plan-010 --json
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
git diff --check
```

Expected counts are lifecycle `8/8`, all coordinator families `27/27`, library
lifetime `18/18`, Application `160` discovered / `159` passed / the one
established live-config opt-in skip, Contracts `73/73`, and deterministic
aggregate `301` discovered / `300` passed / that same skip. Avalonia remains
`75/75` with only established AVLN3001. Web remains `315/315` and `18/18`.
Both WinForms x64 Release builds have zero warnings/errors. All eight included
non-deploying gates pass; `ci-gates` remains excluded only because it invokes
the runner itself.

Protect exact compatibility:

```powershell
git hash-object LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
Get-FileHash -Algorithm SHA256 LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --exit-code eed51e4ce3605e294937c41959e53bdd51b46dd3..HEAD -- LibreHardwareMonitorLib\Hardware\Computer.cs LibreHardwareMonitor.Windows.Forms\UI\HardwareOperationCoordinator.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\HardwareOperationCoordinatorTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\UiShutdownCoordinatorTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\DataJsonGoldenTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json LibreHardwareMonitor.Windows.Forms\Application\Snapshots\SensorSnapshot.cs LibreHardwareMonitor.Windows.Forms\Adapters\WinFormsNodeSensorSnapshotSource.cs LibreHardwareMonitor.Windows.Forms\Utilities\DataJsonProjection.cs LibreHardwareMonitor.Windows.Forms\Utilities\HttpListenerDispatchService.cs LibreHardwareMonitor.Windows.Forms\Utilities\HttpServer.cs
```

Require golden Git blob `05113704acc6fefeb4128004b3f523d876fbcec4`
and SHA-256
`BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.
Outside campaign/configuration/documentation truth, the exact diff is AM's two
new files plus `MainForm.cs` and the comment-only `UiShutdownCoordinator.cs`
correction. Prove each source commit/patch matches its integrated result.

After running the verified-machine helper, perform only the standard read-only
SND-HOST separation proof: capture exact live executable/process; inspect root
task state/action/working directory; use proxy-bypass GET for `/`, `/data.json`,
and `/metrics`; sample the current-day CSV twice and prove growth. Do not start,
stop, restart, reconfigure, publish, promote, or write under the operations tree.

## Documentation work

1. `data/plans/plan-010.json`
   - Preserve all plan elements and refactor metadata.
   - Add exact AM/AN source and integration commits, focused/full counts,
     golden hashes, gate outputs, live samples, file/patch proof, and one reason
     per exit criterion to `backfill_reasons`.
   - Keep status `executed`; execution registration is not acceptance.
2. Generated campaign markdown
   - Never hand-edit it. Patch JSON, regenerate through the sanctioned
     `_persist_plan_artifacts` call, and prove a second render byte-identical.
3. `.codex/skills/project.toml`
   - Add `ApplicationLifecycleCoordinator.cs` only to the existing
     MainForm/Computer lifecycle conflict-zone row. The existing WinForms
     wildcard already maps it; add no redundant smart-test mapping or gate.
4. `docs/README.md`, `docs/architecture/refactor-roadmap.md`, and
   `docs/campaign-backlog.md`
   - Mark Phase-4 item 3 implemented while Phase 4 stays in progress, retain
     partial Phase 2 and person-only A1, and advance to Plan-011/agent-ap with no
     deploy claim.
5. `docs/features/feature-memory-ui-reliability.md`
   - Record the extracted lifecycle/polling/shutdown-drain boundary. Keep the
     separate transactional runtime-option and failed-reset policy follow-ups
     open.
6. `docs/campaign-history.md`
   - Add Plan-010 with all 12 criterion-specific `implemented` entries, zero
     accepted/deployed entries, and exact evidence.
7. `live-tracker.md`
   - Add one accurate row each for AM, AN, and AO using actual commits/counts.

Leave `AGENTS.md`, Plan-009 and older artifacts/history/tracker rows, root README,
playbook, control-plane, commands/gates, product/tests, projects/packages,
operations manifests/docs, release/candidate/rollback records, live tasks,
settings, logs, and runtime unchanged.

## Exit Criteria

- Every Plan-010 criterion has command- or artifact-backed evidence; no gate is
  softened to fit a result.
- All nine exclusive outputs agree on commits, counts, hashes, Phase state,
  Plan-011/agent-ap, retained A1, live separation, and `implemented` state.
- No product/test/golden/project/package/solution/web/operations/candidate/
  release/rollback/task/setting/log/runtime file changes.
- Closure is committed and the handoff lists each gate, document, remaining
  person-only action, and exact cleanup boundary.

## Do not

- Do not edit source/tests to make verification pass; stop and report.
- Do not hand-edit generated campaign markdown.
- Do not create a candidate, deploy, promote, mutate tasks/settings/listeners,
  accept the campaign, close A1, merge, or clean another checkout.

## Handoff

Commit with `docs(campaign): close plan-010 lifecycle seam`. Return the commit
SHA, every exact result/hash/live sample, criterion evidence, changed files, the
remaining person-only action, and the proposed safe cleanup boundary.
