# SQ LibreHardwareMonitor documentation

**Status:** current repository map
**Updated:** 2026-08-05

Verify Git and runtime state before relying on dynamic facts in this file.

## Authorities

| Purpose | Path |
|---|---|
| Source checkout | `D:\DevHome\workspaces\librehw-host\checkouts\main` |
| Agent worktrees | `D:\DevHome\workspaces\librehw-host\worktrees` |
| Official comparison | `D:\DevHome\workspaces\librehw-host\references\official` |
| Candidate store | `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\releases\candidates` |
| Live runtime | `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current` |
| Rollback and history | `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\rollback` and `deployments\history` |
| Installed log tooling | `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\operations\log-management` |
| Log archive | `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\data\logs\archive` |

Source, candidates, live runtime, rollback, history, and logs are separate
physical domains. Candidate validation does not authorize deployment. Before
an operational write, follow the stack-local `AGENTS.md`, manifests, and
`docs\OPERATIONS.md`.

Repository remotes:

| Remote | Fetch | Push |
|---|---|---|
| `origin` | `https://github.com/celine-anime/librehw-host.git` | same |
| `upstream` | `https://github.com/espensev/sq-librehw.git` | `DISABLED` |
| `vanilla` | local official comparison checkout | `DISABLED` |

Development stays on local `main`. Do not push unless explicitly requested.
Use live Git commands for branch, cleanliness, and ahead/behind state.

## Current boundaries

- `manifests\channels\live.json` currently records SND-HOST candidate
  `0.9.6-20260805-054355164-5ad1047`, product version
  `0.9.6+5ad1047.2026-08-05`, observed on 2026-08-05. Re-observe the host before
  an operational decision.
- CSV numeric compaction is implemented only in local source. It has no
  exact-current-source candidate and is not live.
- `ops\candidate` creates and validates external candidates; it does not
  deploy them.
- `ops\live-verification` is read-only and manifest-driven.
- `ops\log-management` is the source package for installed host log tooling.
- `ops\deploy\snd-desk` is target-gated to SND-DESK and is not SND-HOST
  deployment authority.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.

Open gates:

- Plan-001 attended Avalonia smoke is person-only.
- Plan-012 criterion 10 remains open without a waiver; the implemented source
  seam is not reopened.
- Plan-014 remains gated by explicit maintainer authorization.
- Push, candidate creation, candidate acceptance, and live promotion are
  separate decisions.

## Documentation map

| Document | Purpose |
|---|---|
| `docs\architecture\refactor-roadmap.md` | Structural continuation and phase gates |
| `docs\campaign-backlog.md` | Sequenced campaign queue |
| `docs\campaign-playbook.md` | Campaign procedure |
| `docs\campaign-history.md` | Per-criterion acceptance ledger |
| `docs\architecture\campaign-control-plane.md` | Campaign authority and provenance |
| `docs\features\feature-release-packaging.md` | Candidate packaging contract |
| `docs\features\feature-csv-log-storage-efficiency.md` | Source-only CSV formatting contract |
| `docs\features\feature-host-log-management.md` | Log archive and retention contract |
| `docs\features\feature-live-state-verification.md` | Read-only host verification contract |
| `eng\ci\README.md` | Non-deploying gate runner |

Other feature documents contain their own status and acceptance criteria.
`data\plans`, rendered campaign plans, `live-tracker.md`, and agent task files
are campaign-runtime records; do not remove or hand-edit generated plans.

## Source map

| Area | Path |
|---|---|
| Hardware library | `LibreHardwareMonitorLib` |
| Windows application | `LibreHardwareMonitor.Windows.Forms` |
| Automated tests | `LibreHardwareMonitor.Tests` and `webtests` |
| Candidate tooling | `ops\candidate` |
| Live-state verifier | `ops\live-verification` |
| Log tooling | `ops\log-management` |
| CI gates | `eng\ci` |
| Fixture-only Avalonia explorer | `experiments\avalonia-fixture-explorer` |

## Verification

The gate runner is non-deploying:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
```

Focused baseline commands:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\live-verification\Test-LhmLiveStateSystem.ps1
```

Builds create ignored `bin` and `obj` directories. Inspect and remove them only
through the guarded cleanup tool:

```powershell
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\eng\Clear-LhmRepositoryBuildOutputs.ps1
```

Never use `git clean -fdX` for this repository.

## Documentation policy

- Keep current contracts and links; place completed evidence in Git history.
- Delete completed discovery or review notes after unresolved findings are
  folded into an active contract.
- Edit plan JSON through campaign tooling; do not hand-edit rendered plans.
- Treat historical paths and counts as dated evidence, not current state.
