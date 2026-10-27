# SQ LibreHardwareMonitor documentation

**Status:** current repository map
**Updated:** 2026-10-27

This file records stable boundaries. Query Git and stack manifests for current
commits, versions, hashes, processes, and candidate/live state.

## Path and machine configuration

The shared repository requires no machine-wide environment-variable naming
scheme. Resolve source paths from the Git checkout or script location. Supply
deployment paths through the tool's explicit arguments and the selected
installation's configuration or manifests.

| Task | Portable input | Optional convenience |
|---|---|---|
| Locate source | Git checkout or script location | A machine's own checkout shortcut |
| Create or validate candidates | `-ReleaseRoot` | `LHM_RELEASE_ROOT`; otherwise the documented checkout-sibling fallback |
| Verify a deployment | `-StackRoot`, then its layout, consumer, and live manifests | Any locally configured path passed to the argument |
| Select runtime data | Valid `librehw.runtime.json` (`schema`, `dataRoot`, `managedStartupTaskPath`) | `LIBREHARDWAREMONITOR_DATA_ROOT`; without either, the executable directory |
| Run log management | Required `-ConfigPath` with the installation's paths and settings | Any locally selected configuration path passed to that argument |

Callers may use their own variable names when passing these arguments. A
missing optional variable is not an enrollment defect. Do not infer a stack
root from the candidate directory or create global variables just to match
another machine. Validate the selected paths and target identity using the
tool's contract and the target's operational policy.

`MACHINE_*_ROOT` and `SEV_LOCAL_*` are SND-DESK conventions used by its local
adapters and inventory. Other machines need not define them. Windows/platform
requirements and the SND-DESK-only deployment boundary still apply.

## Current installations

The two current installations use one product and source history. `origin`
(`https://github.com/espensev/sq-librehw.git`, branch `master`) is the only
development remote. `librehw-host`
(`https://github.com/celine-anime/librehw-host.git`, branch `main`) is the
SND-HOST mirror of that same history: it is only ever fast-forwarded from
`master` and never receives independent commits. These remote aliases describe
the SND-DESK checkout; SND-HOST calls the mirror `origin` and keeps its push URL
`DISABLED`. `upstream` on SND-DESK is the fetch-only
official source; the [upstream divergence ledger](architecture/upstream-divergence.md)
lists every file that differs from it. Verify the local machine identity before
any operational write; a peer's paths never establish local authority.

The table identifies each installation's configuration authority. These
machine-specific arrangements are not prerequisites for a new installation.

| | SND-DESK | SND-HOST |
|---|---|---|
| Role | development checkout and its own live runtime | peer operational stack |
| Source checkout | `sq-app.json`: `root`, resolved locally | Stack `manifests/layout.json`: `development.mainCheckout` |
| Remote and branch | `origin` / `master` | local `origin` / `main`, pulling the `librehw-host` mirror; push disabled |
| Release tooling | `ops\deploy\snd-desk`; fails closed unless the machine is `snd-desk` | `ops\candidate` (machine-neutral) plus the stack's own manifests |
| Candidate store | `-CandidateDirectory` passed to the SND-DESK publish helper | Explicit `-ReleaseRoot` or the host's configured optional shortcut |
| Live runtime | `sq-app.json`: deployment `root`, with its `rollback` slot | Stack `manifests/layout.json`: `operations.live` |
| Live version authority | Deployment `release.json` | `manifests\channels\live.json` in the stack |
| Mutable data | Deployment `librehw.runtime.json` | Beside the executable unless configured otherwise |
| Launcher and public command | `sq-app.json`: `consumers` | The stack's own manifests and `docs\OPERATIONS.md` |
| Live-state check | `release.json`, `http://127.0.0.1:18085/data.json`, `Sync-LibreHardwareMonitorLauncher.ps1 -Mode Validate` | `ops\live-verification\Test-LhmLiveState.ps1 -StackRoot <stack>` |
| Machine record | `sq-app.json` (paths and consumers only; no versions) | none in this repository |
| Installed version | query `release.json` | query `manifests\channels\live.json`; refresh runtime evidence before an operational decision |

Source, candidates, runtime, rollback, and mutable data are separate domains
on both machines. Candidate validation never authorizes promotion. The complete
SND-DESK flow is in [the local release contract](features/feature-local-release-system.md);
the shared candidate and verification contracts used by SND-HOST are in
[release packaging](features/feature-release-packaging.md) and
[live-state verification](features/feature-live-state-verification.md).

`sq-app.json` is a descriptive SND-DESK inventory. Its `%NAME%` path tokens
require explicit expansion by a reader, such as
`[Environment]::ExpandEnvironmentVariables`; JSON parsing alone does not expand
them. Resolve and validate them on the owning machine before using a path.

## Version identity

- `Directory.Build.props` pins `Version` `0.9.6`. `AssemblyVersion` stays
  `0.9.6` because the `data.json` contract embeds it; build with `-p:Platform=x64`.
- Local builds from a Git checkout stamp the product version
  `0.9.6+<sha7>[-dirty].<yyyy-MM-dd>`. `StampLocalBuildVersion` is disabled under
  `GITHUB_ACTIONS=true`; those verification builds use the SDK's versioning
  and source-revision behavior. If Git cannot supply a local revision, the
  stamp says `nogit`, which is not a release identity.
- Release and candidate identifiers embed that commit. SND-DESK
  `Publish-LibreHardwareMonitor.ps1` writes
  `<product version with + replaced by _>-<sha12>-<yyyyMMddTHHmmssZ>`, which
  renders as `0.9.6_<sha7>[-dirty].<date>-<sha12>-<utc>`; SND-HOST `New-LhmRelease.ps1`
  writes `0.9.6-<yyyyMMdd-HHmmssfff>-<sha7>`, appends `-dirty` for a dirty
  checkout, and accepts an explicit `-ReleaseId`. Compare machines by commit,
  never by identifier shape.
- The commit is the only cross-machine version key. Read it from
  `release.json`, `live.json`, or Git when needed; do not copy release
  identifiers or commits into current-state documents or inventory records,
  where they go stale. Dated verification evidence may name the commit tested.

Development stays on `master`. Owner direction on 2026-10-19 authorizes committing
and pushing validated work, with ordinary merges when needed.
Use live Git commands for branch, cleanliness, and ahead/behind state.

### Source routing on SND-HOST

The selected stack's `manifests/layout.json` supplies
`development.mainCheckout` for inspecting the checkout. Review recovered its six unpublished
commits through `f0dadd06` and integrated their history, including CSV clock
recovery and live-verifier fixes. Keep that operational checkout pull-only;
make source changes through the canonical repository's `master` branch and
fast-forward the mirror.
No SND-DESK-style environment enrollment is required. Matching GitHub branch
tips alone does not establish a checkout's state.
See the [integration review](reviews/review-2026-10-27-machine-version-unification.md)
for integration evidence. Runtime promotion and the held
live-machine changes remain separate.

## Current boundaries

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
| `docs\architecture\upstream-divergence.md` | Every upstream-owned file the fork modified, deleted, moved, or declined, with its owning record; checked by `eng\ci\tests\Test-UpstreamDivergenceLedger.ps1` |
| `docs\campaign-backlog.md` | Sequenced campaign queue |
| `docs\campaign-playbook.md` | Campaign procedure |
| `docs\campaign-history.md` | Per-criterion acceptance ledger |
| `docs\architecture\campaign-control-plane.md` | Campaign authority and provenance |
| `docs\features\feature-release-packaging.md` | Candidate packaging contract |
| `docs\features\feature-csv-log-storage-efficiency.md` | CSV formatting contract and SND-HOST promotion status |
| `docs\features\feature-csv-clock-recovery.md` | Automatic CSV time-zone recovery and verification |
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Show-LhmExecutableIdentity.ps1 -Path '<exact-exe>' -StackRoot '<stack-root>' -Json
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
- When a change touches a file that also exists in upstream LibreHardwareMonitor,
  update its row in `docs\architecture\upstream-divergence.md` in the same pass;
  `eng\ci\Test-LhmCiGates.ps1` fails when a differing upstream file has no row.
- Delete completed discovery or review notes after unresolved findings are
  folded into an active contract.
- Edit plan JSON through campaign tooling; do not hand-edit rendered plans.
- Treat historical paths and counts as dated evidence, not current state.
