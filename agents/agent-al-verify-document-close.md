# Agent Task — Verify, Document, and Close Plan-009

**Plan:** `plan-009`
**Depends on:** Agent AJ, Agent AK
**Exclusive outputs:**

- `.codex/skills/project.toml`
- `data/plans/plan-009.json`
- `docs/campaign-plan-009-http-listener-dispatch-service.md`
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/features/feature-native-ui-modernization.md`
- `live-tracker.md`

## Goal

Independently verify the integrated Plan-009 wire characterization and listener
adapter, then record precise current truth. Do not repair product or test files:
report a gate defect to the coordinator. Ledger state is `implemented`; only a
person may record `accepted`.

## Inputs required from the coordinator

- Planning baseline and integrated AJ/AK commit SHAs.
- AJ and AK source commit SHAs, exact results, port cleanup proof, and proposed
  tracker rows.
- A clean worktree based on the integrated source, not the planning baseline.
- Pre-campaign and closure live CSV samples or permission to take the same
  standard read-only samples.

## Read first

- `AGENTS.md`
- `.codex/skills/project.toml`
- `docs/campaign-playbook.md`
- `docs/architecture/campaign-control-plane.md`
- `docs/campaign-plan-009-http-listener-dispatch-service.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/README.md`
- `live-tracker.md`
- the three integrated AJ/AK product/test paths
- unchanged snapshot/projector/golden, Sensor, Prometheus, and lifetime contracts

## Independent verification

Unset both live-config variables and disable the .NET terminal logger where
counts matter:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests|FullyQualifiedName~HttpServerLifetimeTests|FullyQualifiedName~HttpServerAuthenticationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServer|FullyQualifiedName~DataJson|FullyQualifiedName~WebDashboardRetirementTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan validate plan-009 --json
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
git diff --check
```

Expected counts are wire `6/6`, wire/lifetime/authentication `12/12`, Contracts
`73/73`, deterministic `293` discovered / `292` passed / the one established
live-config opt-in skip, and Avalonia `75/75`. Web remains `315/315` and `18/18`.
Both WinForms x64 Release builds have zero warnings/errors. All eight included
non-deploying gates pass; `ci-gates` remains excluded only because it invokes the
runner itself.

Protect exact compatibility:

```powershell
git hash-object LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
Get-FileHash -Algorithm SHA256 LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --exit-code a45790c646ce40be00c627dbcc1f99f681a3f639..HEAD -- LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\DataJsonGoldenTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --exit-code a45790c646ce40be00c627dbcc1f99f681a3f639..HEAD -- LibreHardwareMonitor.Windows.Forms\UI\MainForm.cs LibreHardwareMonitor.Windows.Forms\UI\AuthForm.cs LibreHardwareMonitor.Windows.Forms\UI\InterfacePortForm.cs LibreHardwareMonitor.Windows.Forms\Application\Snapshots\SensorSnapshot.cs LibreHardwareMonitor.Windows.Forms\Adapters\WinFormsNodeSensorSnapshotSource.cs LibreHardwareMonitor.Windows.Forms\Utilities\DataJsonProjection.cs
```

Require golden Git blob `05113704acc6fefeb4128004b3f523d876fbcec4`
and SHA-256
`BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.
The implementation diff outside campaign truth is exactly the three authorized
paths. Compare AJ's patch against its integrated commit and prove AK did not
edit the characterization.

After running the verified-machine helper, perform only the standard read-only
SND-HOST separation proof: capture exact live executable/process; inspect root
task state/action/working directory; use proxy-bypass GET for `/`, `/data.json`,
and `/metrics`; sample the current-day CSV twice and prove growth. Do not start,
stop, restart, reconfigure, publish, promote, or write under the operations tree.

## Documentation work

1. `data/plans/plan-009.json`
   - Preserve all 13 elements and refactor metadata.
   - Add exact AJ/AK source and integration commits, focused/full counts,
     golden hashes, gate outputs, live samples, file/patch proof, and one reason
     per exit criterion to `backfill_reasons`.
   - Keep status `executed`; execution registration is not acceptance.
2. Generated campaign markdown
   - Never hand-edit it. Patch JSON, then regenerate with the sanctioned
     `_persist_plan_artifacts` call and prove a second render is byte-identical.
3. `.codex/skills/project.toml`
   - Add `HttpListenerDispatchService.cs` and the wire test to the existing HTTP
     conflict-zone row. Add an exact smart-test mapping for the new service to
     Contracts plus `webtests/*.js`; do not change modules, commands, or gates.
4. `docs/README.md`
   - Mark Plan-009/Phase-4 item 2 implemented, Plan-010 next, retain partial
     Phase 2/A1/no-deploy truth, and split the source map between the internal
     listener service and `HttpServer` route/content ownership.
5. `docs/architecture/refactor-roadmap.md`
   - Check Phase-4 item 2 with actual evidence; keep Phase 4 in progress and
     advance the next campaign to Plan-010.
6. `docs/campaign-backlog.md`
   - Keep Phase 2 partial; advance last/next campaign and agent `am`; narrow the
     shared Phase-4 section from Plan-009–012 to Plan-010–012.
7. `docs/campaign-history.md`
   - Add the Plan-009 ledger row and criterion-specific `implemented`, never
     accepted/deployed, evidence.
8. `live-tracker.md`
   - Add one accurate row each for AJ, AK, and AL using actual commits/counts.
9. `docs/features/feature-native-ui-modernization.md`
   - Replace only the stale `GenerateJsonForNode` reference with the current
     snapshot capture, projector, and listener/route boundary.

Leave `AGENTS.md`, Plan-008 artifacts/history/tracker rows, Avalonia ownership
docs, operational manifests, live docs, release/candidate records, root README,
playbook, control-plane, commands, and gates unchanged.

## Exit Criteria

- Every Plan-009 criterion has command- or artifact-backed evidence and no gate
  is softened to fit a result.
- All nine exclusive outputs agree on commits, counts, hashes, Phase state,
  Plan-010/agent-am, retained A1, live separation, and `implemented` state.
- No product, test, golden, project/package, solution, spike, web, operations,
  candidate, release, rollback, task, setting, log, or runtime file changes.
- The closure is committed and the handoff lists every exact gate, document,
  remaining person-only action, and proposed cleanup boundary.

## Do not

- Do not edit source/tests to make verification pass; stop and report.
- Do not hand-edit the generated campaign markdown.
- Do not create a candidate, deploy, promote, mutate tasks/settings/listeners,
  accept the campaign, close A1, merge, or clean another checkout.

## Handoff

Commit with `docs(campaign): close plan-009 listener adapter`. Return the commit
SHA, every exact result/hash/live sample, criterion evidence, changed files, and
the remaining person-only action.
