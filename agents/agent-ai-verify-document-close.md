# Agent Task — Verify, Document, and Close Plan-008

**Plan:** `plan-008`
**Depends on:** Agent AG, Agent AH
**Exclusive outputs:**

- `.codex/skills/project.toml`
- `AGENTS.md`
- `data/plans/plan-008.json`
- `docs/campaign-plan-008-immutable-sensor-snapshot-and.md`
- `docs/README.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `live-tracker.md`

## Goal

Independently verify the integrated Plan-008 seam and record precise current
truth. Do not repair product or test files: report a gate defect to the
coordinator. Ledger state is `implemented`; only a person can record
`accepted`.

## Inputs required from the coordinator

- The planning baseline commit and integrated AG/AH commit SHAs.
- AG and AH source commit SHAs, exact focused/full results, and proposed tracker
  rows.
- A clean worktree based on the integrated branch, not the original product or
  planning baseline.
- The pre-campaign and pre-closure live CSV samples for comparison.

## Read first

- `AGENTS.md`
- `.codex/skills/project.toml`
- `docs/campaign-playbook.md`
- `docs/architecture/campaign-control-plane.md`
- `docs/campaign-plan-008-immutable-sensor-snapshot-and.md`
- `docs/architecture/refactor-roadmap.md`
- `docs/campaign-backlog.md`
- `docs/campaign-history.md`
- `docs/README.md`
- `live-tracker.md`
- all six integrated AG/AH source and test files
- unchanged `DataJsonGoldenTests.cs` and `data.golden.json`

## Independent verification

Unset both live-config variables and disable the .NET terminal logger where
counts matter:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SensorSnapshotTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~DataJsonProjectionTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~DataJsonGoldenTests|FullyQualifiedName~HttpServerSensorApiTests|FullyQualifiedName~HttpServerPrometheusTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
python scripts\task_manager.py plan validate plan-008 --json
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
git diff --check
```

Expected counts are AG `5/5`, AH `3/3`, Contracts `67/67`, Application
`152` discovered / `151` passed / one established opt-in skip, and aggregate
`287` discovered / `286` passed / that same skip. The spike remains `75/75`.
Both WinForms x64 Release builds must have zero errors and no new warnings. The
full non-deploying sweep must pass all eight included gates; `ci-gates` is
excluded only because it invokes the runner itself.

Guard exact compatibility:

```powershell
git hash-object LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
Get-FileHash -Algorithm SHA256 LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --exit-code 779d9044ef63b3cb91f547d9306dc543e2eb4ae6..HEAD -- LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\DataJsonGoldenTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
rg -n "Avalonia|Spike.Core" LibreHardwareMonitor.Windows.Forms LibreHardwareMonitor.sln
```

Require Git blob `05113704acc6fefeb4128004b3f523d876fbcec4`, SHA-256
`BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`,
no shipping Avalonia/spike reference, and an implementation diff limited to the
six AG/AH files. Use the coordinator-supplied planning commit for the separate
implementation-only name-status comparison.

Perform only the standard read-only SND-HOST separation proof: run the verified
identity helper; capture the exact live executable/process; inspect root task
state/action/working directory; proxy-bypass GET `/`, `/data.json`, and
`/metrics`; sample the current-day CSV twice and prove growth. Do not restart,
reconfigure, publish, promote, or write anywhere under the operational tree.

## Documentation work

1. `data/plans/plan-008.json`
   - Preserve all 13 plan elements and refactor metadata.
   - Add exact AG/AH integration commits, command results, hashes, live samples,
     and implemented-not-accepted closure evidence to `backfill_reasons`.
   - Keep plan status `executed`; execution registration is not acceptance.
2. `docs/campaign-plan-008-immutable-sensor-snapshot-and.md`
   - Never hand-edit this rendered file. After changing JSON, regenerate it with
     the repository's sanctioned `_persist_plan_artifacts` call.
3. `.codex/skills/project.toml`
   - Widen only the existing external data.json conflict-zone row to include the
     snapshot contract, capture source, and pure projector. Existing generic
     smart-test mappings already cover the new paths; do not change commands.
4. `AGENTS.md`
   - Replace the removed `GenerateJsonForNode` reference with the new capture and
     `DataJsonProjection` seam while retaining golden-master instructions.
5. `docs/README.md`
   - Replace the stale Phase-1/Plan-003 roadmap paragraph with the current state:
     Phases 0, 1, 2, and 3 complete; Phase 4 in progress after Plan-008; Plan-009
     next; A1 still person-only; no live/cutover implication.
6. `docs/architecture/refactor-roadmap.md`
   - Mark Phase-4 item 1 complete with Plan-008 evidence, Phase 4 `in progress`,
     and the next campaign Plan-009. Leave later items unstarted and A1 open.
7. `docs/campaign-backlog.md`
   - Remove Plan-008 from the combined Phase-4 table, advance current position
     to Plan-009 and next agent `aj`, and retain the one-seam-at-a-time rule.
8. `docs/campaign-history.md`
   - Add a Plan-008 ledger row and one evidence row per exit criterion. State
     `implemented`, never `accepted`, and distinguish source/build proof from
     live deployment proof.
9. `live-tracker.md`
   - Add one accurate row each for AG, AH, and AI using actual commits/counts.

## Exit Criteria

- Every Plan-008 criterion has command- or artifact-backed evidence and no gate
  is softened to fit a result.
- All nine exclusive outputs agree on commits, exact counts, hashes, Phase-4
  state, Plan-009/agent-aj queue position, retained A1, live separation, and
  `implemented` ledger state.
- No product, test, golden, project, package, solution, spike, web, operations,
  candidate, release, rollback, task, setting, log, or live-runtime file changes.
- The documentation/configuration change is committed and the handoff lists
  every command result and remaining person-only action.

## Do not

- Do not edit source or tests to make verification pass; stop and report.
- Do not hand-edit the generated campaign markdown.
- Do not run candidate creation, deployment, promotion, task mutation, listener
  restart, settings write, or cleanup outside this worktree.
- Do not accept the campaign or close A1.
- Do not merge your branch.

## Handoff

Commit with `docs(campaign): close plan-008 snapshot projection`. Return the
commit SHA, every exact gate/count/hash/live result, per-criterion evidence,
files changed, and remaining person-only action.
