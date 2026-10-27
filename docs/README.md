# SQ LibreHardwareMonitor documentation

**Status:** current repository map
**Updated:** 2026-10-27

This file records stable boundaries. Query Git and stack manifests for current
commits, versions, hashes, processes, and candidate/live state.

## Authorities

This checkout is the SND-DESK fork. Verify local machine identity before
operational writes; a peer's paths do not establish local authority.

| Purpose | Path |
|---|---|
| Source checkout | `E:\Monitoring\sq-librehw` |
| Candidate store | `E:\Monitoring\LibreHW\Candidates` |
| Live runtime | `E:\Monitoring\LibreHW\Runtime` |
| One rollback slot | `E:\Monitoring\LibreHW\Runtime\rollback` |
| Installed launcher | `E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1` |
| Settings, recovery, and logs | `%SEV_LOCAL_DATA%\LibreHardwareMonitor` |
| Public command | `%SEV_LOCAL_BIN%\librehw.cmd` |

Source, candidates, runtime, rollback, and mutable data are separate domains.
Use `ops\deploy\snd-desk\Publish-LibreHardwareMonitor.ps1` for local staging;
candidate validation does not authorize promotion. The complete local flow is
in [the local release contract](features/feature-local-release-system.md).

SND-HOST has a separate checkout at
`D:\DevHome\workspaces\librehw-host\checkouts\main` and operational stack at
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack`. Its stack-local `AGENTS.md`,
manifests, and `docs\OPERATIONS.md` govern host operations; verify that target
before relying on its live state.

Repository remotes:

| Remote | Fetch | Push |
|---|---|---|
| `origin` | `https://github.com/espensev/sq-librehw.git` | same |
| `librehw-host` | `https://github.com/celine-anime/librehw-host.git` | same; separate peer repository |

Development stays on `master`. Owner direction on 2026-10-19 authorizes committing
and pushing validated work, with ordinary merges when needed.
Use live Git commands for branch, cleanliness, and ahead/behind state.

## Current boundaries

- The SND-HOST stack's `manifests\channels\live.json` historically recorded candidate
  `0.9.6-20260805-054355164-5ad1047`, product version
  `0.9.6+5ad1047.2026-08-05`, observed on 2026-08-05. Re-observe the host before
  an operational decision.
- CSV numeric compaction's SND-HOST promotion status is tracked in its feature
  spec; it does not establish SND-DESK runtime state.
- `ops\candidate` owns the separate host candidate format: it creates and
  validates external candidates and does not deploy them. The SND-DESK local
  release flow uses `ops\deploy\snd-desk`.
- `ops\candidate\Show-LhmExecutableIdentity.ps1` identifies an exact EXE from
  its hash and the applicable Git/candidate/live/rollback evidence; only a
  fully validated manifest contract can grant authority. Process or evidence
  query failures are reported as `INCOMPLETE` instead of being hidden.
- `ops\live-verification` is read-only and manifest-driven.
- `ops\log-management` is the source package for installed host log tooling.
- `ops\deploy\snd-desk` is target-gated to SND-DESK and is not SND-HOST
  deployment authority.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.
- [PawnIO platform gating](features/feature-pawnio-platform-gating.md) records
  unsupported-platform behavior and the separate portable Linux acceptance gate.

## Executable flow

| Stage | Executable truth |
|---|---|
| Automated tests | `testhost` loads source-built assemblies; it does not make a copied product EXE a development or release authority. |
| Development | Checkout `bin` output is disposable and identified against its enclosing Git checkout and embedded source stamp. |
| Candidate | Candidate creation ZIPs each framework, extracts it, and smoke-runs that exact EXE before publishing a hash-bound manifest. |
| Live | Only the exact `deployments\current` path/hash/product-version match under the valid, current-machine layout/consumer/live manifest contract is live authority. |
| Rollback | Only an exact file record whose length and SHA-256 validate inside a machine-bound sealed packet is rollback authority; location alone is not. |

Candidate smoke is pre-UI identity/startup proof, not hardware or live health.
Promotion and live verification remain separate workflows.

Open gates:

- Plan-001 attended Avalonia smoke is person-only.
- Plan-012 criterion 10 remains open without a waiver; the implemented source
  seam is not reopened.
- Plan-014 remains gated by explicit maintainer authorization.
- SND-DESK launcher convergence: the 2026-09-11 identity-verified
  `Plan`/`Apply`/`Validate` run aligned the helper and receipt with the current
  RunW artifact and portable public shim; Validate returned PASS without drift.
  The graph-lane build and subsequent startup zoom repair were promoted on
  SND-DESK. Health, layout, and exact manual-zoom restart checks pass;
  operator layout sign-off remains separate.
  See `docs\features\feature-local-release-system.md` and
  `docs\features\feature-graph-lanes.md`.
- Push, candidate creation, candidate acceptance, and live promotion are
  separate decisions.

## Documentation map

| Document | Purpose |
|---|---|
| `docs\architecture\refactor-roadmap.md` | Structural continuation and phase gates |
| `docs\architecture\dashboard-extraction.md` | Dashboard source ownership and extraction acceptance |
| `docs\campaign-backlog.md` | Sequenced campaign queue |
| `docs\campaign-playbook.md` | Campaign procedure |
| `docs\campaign-history.md` | Per-criterion acceptance ledger |
| `docs\architecture\campaign-control-plane.md` | Campaign authority and provenance |
| `docs\features\feature-release-packaging.md` | Candidate packaging contract |
| `docs\features\feature-csv-log-storage-efficiency.md` | CSV formatting contract and SND-HOST promotion status |
| `docs\features\feature-host-log-management.md` | Log archive and retention contract |
| `docs\features\feature-live-state-verification.md` | Read-only host verification contract |
| `docs\features\feature-http-server-safety.md` | Direct HTTP listener fail-closed and exposure contract |
| `docs\features\feature-local-release-system.md` | SND-DESK local staging, promotion, and rollback contract |
| `docs\features\feature-tailnet-fleet-view.md` | Option A phase 1 promoted to the SND-DESK runtime on 2026-10-27 and smoke-verified there; listener port and peer acceptance pending |
| `docs\features\feature-unified-tailnet-monitoring-view.md` | Superseded static unified view; historical operational evidence only |
| `eng\ci\README.md` | Non-deploying gate runner |
| `docs\features\feature-graph-lanes.md` | Promoted and live-verified on SND-DESK; lane splits, height, layout, and repaired manual-zoom persistence pass; operator sign-off separate |
| `docs\reviews\settled-2026-10-23-smu-and-rapl.md` | Closed Ryzen SMU reply and Intel RAPL claims; do not reopen |

Other feature documents contain their own status and acceptance criteria.
The [current integration and open-work review](reviews/review-2026-10-19-upstream-and-open-work.md)
records the official upstream merge, completed checkout cleanup, preserved
unfinished work, and prioritized reliability/tooling follow-ups.
`data\plans`, rendered campaign plans, `live-tracker.md`, and agent task files
are campaign-runtime records; do not remove or hand-edit generated plans.

## Source map

| Area | Path |
|---|---|
| Hardware library | `LibreHardwareMonitorLib` |
| Windows application | `LibreHardwareMonitor.Windows.Forms` |
| Browser dashboard source and frontend workflow | `dashboard` |
| Automated tests | `LibreHardwareMonitor.Tests` and `dashboard/tests` |
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Show-LhmExecutableIdentity.ps1 -Path <exact-exe> -StackRoot E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack -Json
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
The standalone command and candidate workflow share the same tracked-file,
Git-ignore, reparse, loaded-process, and fail-closed CIM inventory guards.

## Documentation policy

- Keep current contracts and links; place completed evidence in Git history.
- Delete completed discovery or review notes after unresolved findings are
  folded into an active contract.
- Edit plan JSON through campaign tooling; do not hand-edit rendered plans.
- Treat historical paths and counts as dated evidence, not current state.
