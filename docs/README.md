# SQ LibreHardwareMonitor Docs

**Status:** live map only
**Updated:** 2026-07-31

## Repository

- `origin` is `celine-anime/librehw-host`; the default and working branch is
  `main`. **Do not push unless a task explicitly asks for it.** `main` is
  deliberately kept ahead of `origin/main`; being ahead is the normal state,
  not a backlog to clear.
- Full local remote configuration. Two of the three pushes fail closed by
  design:

  | Remote | Fetch | Push |
  |---|---|---|
  | `origin` | `https://github.com/celine-anime/librehw-host.git` | same |
  | `upstream` | `https://github.com/espensev/sq-librehw.git` | `DISABLED` |
  | `vanilla` | `E:/SQ_HQ/Monitoring/libre-dev/LibreHardwareMonitor` | `DISABLED` |

  Push default is `origin` / `simple`.
- `upstream` is the fetch-only source `espensev/sq-librehw`, whose source branch
  is named `master`. Never develop on or push to that branch. Development stays
  on local `main`, which tracks and pushes to `origin/main`. Full history is
  preserved: `main` contains `upstream/master`, so older commit SHAs cited in
  these docs still resolve.
- Docs and completed plans written before 2026-07-21 say `master`, cite PR
  numbers on `espensev/sq-librehw`, and use `E:/SQ_HQ/Monitoring/sq-librehw`
  paths. Those are accurate history, deliberately left unrewritten; read them
  against this section. `main` is the branch for new work.
- Upstream `LibreHardwareMonitor/LibreHardwareMonitor` links in the root
  `README.md` point at the real upstream project and are not stale.
- The imported `ops/deploy/snd-desk` and `ops/deploy/snd-desk` surfaces are
  SND-DESK-only in production: deployment and launcher paths fail closed to
  `snd-desk`, while peer-safe non-live fixtures use isolated temporary roots.
  Their `LibreHW`, `sqdata`, launcher, task, user, and cleanup records do not
  replace the SND-HOST paths below.

## Current SND-HOST paths

- Source checkout:
  `D:\DevHome\workspaces\librehw-host\checkouts\main`
- Agent worktrees and fetch-only official comparison:
  `D:\DevHome\workspaces\librehw-host\worktrees` and
  `D:\DevHome\workspaces\librehw-host\references\official`
- Immutable release candidates:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\releases\candidates`
- Live application:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current`
- Deployment rollback packets:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\rollback`
- Installed log manager and archive:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\operations\log-management`
  and `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\data\logs\archive`

The user-level `LHM_RELEASE_ROOT` is
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\releases`. Source, worktrees,
release, live, rollback, and log/archive state are separate physical domains;
no release payload belongs under the development workspace.

## Current

- The product is the Windows app plus the read-only dashboard at `/`.
- Studio and the 2026-07-25 memory/UI/hardware-lifetime reliability baseline
  are deployed and verified as product version
  `0.9.6+d693da7.2026-07-25`.
- Sensor Workspace, its four-profile Thermal extension, and the GPU hotspot
  rate sensor are deployed on identity-verified SND-HOST. Live `/`,
  `data.json`, Prometheus, RTX 3080 rate, CSV logging, and controller-to-host
  access checks passed. The native scrollbar follow-up ships in the same build;
  a separate visual/UI Automation host inspection was not part of this smoke.
- Host-neutral log archival/retention tooling is source-controlled under
  `ops/log-management` and installed on SND-HOST in a separate stable runtime
  with a verified SYSTEM task. Its active configuration now targets the current
  runtime and archive roots above. Seven July 18-24 archives and the first
  repaired-path July 25 rollover archive passed structural inspection; the
  current-day CSV and 365-day retention previews selected nothing for removal.
- Release-candidate tooling is source-controlled under `ops/candidate`. It builds
  framework-dependent `win-x64` payloads for both application frameworks
  through a clean external staging root and publishes independently verified
  ZIP packages plus `release-manifest.json` outside the repository. Candidate
  `0.9.6-20260725-165558646-d693da7` remains the immutable provenance of the
  live net10 promotion; it is historical rather than current-source evidence.
  Candidate creation and validation themselves remain non-deploying. Before
  using a newer candidate, require both `-RequirePromotable` and
  `-RequireCurrentSource` against the external release store.
- The fixture-only Avalonia explorer is implemented in a separate, non-shipping
  `net10.0` source solution. Its complete runner passes 75/75 tests and the
  ten-stage isolation/regression gate passes. Exact-source candidate
  `0.9.6-20260730-210528120-b466837` passed dual-shell promotable/current-source
  verification, and both WinForms ZIP inventories contained zero Avalonia or
  spike entries. An attended normal-user smoke remains pending. The spike is
  not in `LibreHardwareMonitor.sln`, a task, or the live runtime. The live
  SND-HOST product remains the version recorded below.
- `/dash/cardtruth[/]` is retired; `data.json` and CSV IDs are contracts.
- Standard context layouts are merged and browser-fixture-verified; this packet
  did not replace a live LibreHardwareMonitor runtime.
- Fetch-only upstream includes a shallow local release/runtime system whose
  first identity-verified SND-DESK install runs from
  `E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe`.
  Mutable config/logs now use machine-local `sqdata`; the public
  `librehw.cmd` is unchanged. Normal-user foreground restoration passed, both
  shortcuts converge through that command, and the duplicate root task is
  retired. The delegated launcher is now regression-tested through the same
  Windows PowerShell 5.1 host used by the CMD shim, including its zero-process
  StrictMode branch. This is SND-DESK evidence, not SND-HOST live state.
- On SND-DESK every ignored repo-local `bin`/`obj` tree was cleaned. All 32
  non-authoritative output EXEs are gone; the 3,361 historical CSVs and three
  old config files are preserved under the collision-isolated
  `sqdata\LibreHardwareMonitor\historical` archive. Those paths and counts are
  peer-specific history.
- The merged runtime-path code deliberately ignores ambient `sqdata`.
  SND-DESK's manifest still selects its peer-local data root explicitly, while
  SND-HOST retains executable-adjacent state unless a future promotion carries
  a separately approved absolute runtime configuration.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.

## Source integration — 2026-07-30

- Local development remains on `main`, tracking `origin/main`; upstream remains
  fetch-only. This integration imports `upstream/master` through `9b1eb52`,
  including its audited official history through `81e8f83`, HTTP/dashboard
  hardening, hardware support, runtime-path model, and SND-DESK release tooling.
- Conflict resolution preserves the local ordered hardware-operation
  coordinator, UI teardown fix, external dual-framework candidate system,
  SND-HOST path map, and fetch-only upstream policy. The fork-specific GitHub
  Dependabot file remains intentionally absent.
- PR #29 source-shipped Standard contexts. PRs #26 and #28 landed five central
  package patch updates without redundant app-level references.
- Combined-tree source gates passed: dashboard self-test 315/315, focused Node
  tests 18/18, .NET tests 258 passed with one intentional skip, both x64 Release
  targets with zero warnings/errors, log-management checks 26/26,
  release-system checks 114/114 under both PowerShell engines, and the
  peer-safe non-live local-release fixture. No source merge command changed the live
  SND-HOST runtime, task, configuration, or logs.
- SND-DESK now runs the manifest-verified one-EXE local release from the shallow
  stable path. One exact process, HTTP health, populated native controls, the
  migrated Release config, managed task ownership, and a new `sqdata` CSV
  passed. Attended finalization also passed: the normal-user command restored
  the existing window, both Start Menu links target it, the exact legacy root
  task is absent, and both recovery packets validated before cleanup. Those
  pre-stable packets are now historical evidence only. The 3,361 old CSV files
  were moved intact into the accepted historical archive before all repo-local
  build outputs were removed.

## Deployed patch notes

- Deployed the confirmed scrollbar teardown fix, ordered hardware-option
  coordinator, transactional `Computer.Open`/cleanup, stable Storage/NVIDIA
  snapshots, shared NVIDIA ML ownership, and related lifetime regressions from
  clean commit `d693da7`. The live `\LibreHardwareMonitor` task, configuration,
  backup configuration, and active CSV were preserved.
- Added a third read-only `Workspace` view with adaptive `Main`, `Gaming`,
  `Storage`, and `Thermal` profiles, editable/reorderable card, table, and honest
  graph panels, exact sensor membership, and bounded portable JSON import/export.
- Added an honest NVIDIA GPU hotspot rate sensor with bounded five-second
  regression, unavailable warm-up/dropout states, and correct °C/s and °F/s
  formatting across native, web, plot, CSV/data, and Prometheus consumers.
- Ported the useful log-management intent into a parameterized, dry-run-capable
  archive/retention/task-install package with SHA-256 ZIP verification, then
  installed it behind a stable runtime and daily SYSTEM task on SND-HOST.
- Made the native sensor-tree scrollbar substantially easier to see and grab:
  a stable native-width gutter, high-contrast thumb, wider hover/drag states,
  24 px minimum thumb, high-contrast-mode fallback, and real UI Automation
  `ScrollBar`/`RangeValue` behavior at the painted hit target.
- Kept `data.json`, CSV, routes, hardware-write policy, `AssemblyVersion`, and
  current hardware ownership unchanged. SND-DESK's existing runtime stayed
  online during packaging; SND-HOST received its own elevated interactive
  `\LibreHardwareMonitor` task and scoped dashboard firewall rule.

## Roadmap

The structural baseline and phased reorganization are defined in
`docs/architecture/refactor-roadmap.md`. Phase 0 cleanup and Phase 1 campaign and
verification control plane are complete on `main`, which is deliberately
unpushed. Phase 1 closed with Plan-002: a non-deploying gate runner at
`eng/ci/`, a read-only GitHub Actions workflow that only delegates to it, and a
tracked acceptance ledger at `docs/campaign-history.md`. Source moves and any
live relocation remain gated; the next candidate is the isolated Avalonia
experiment move in Phase 2.

The imported upstream roadmap also records a stale SND-DESK
`hardware-optimization` health-feed task. That is a peer-only owner action, not
an unqualified SND-HOST command.

1. Continue hands-on dashboard and native scrollbar/UI Automation inspection
   through the verified runtime owner; deterministic coverage and the live
   served-asset/telemetry smoke are already complete.
2. Execute `docs/features/feature-native-ui-modernization.md` in bounded slices: define
   the presentation model first, then ship tree search/Favorites/order, native
   visual and graph polish, Gadget 2.0, and portable multi-gadget layouts.
   Canonical node order and downstream contracts must not change.
3. Iterate Sensor Workspace around flexibility: resizable/reflowing panels,
   density and visual options, sensor search/grouping, bulk membership, and
   richer graphs that never combine incompatible units dishonestly.
4. Finish the fixture-only Avalonia feasibility gate: the accepted plan,
   isolated bootstrap, bounded parser, shell, 75-test runner, and ten-stage
   source/regression gate are complete. The clean post-merge
   current-source/promotable candidate and direct package-isolation proof pass;
   a normal-user attended smoke remains separate. If that passes, draft a new
   read-only polling spec; the evidence says that follow-on is worth
   specifying, but this source spike adds no polling authority. Shared profile
   extraction, packaging, task ownership, and cutover still wait for a later
   accepted host-neutral seam. WinForms keeps hardware and task ownership.
5. Implement the host-neutral operator-utility plan: a portable read-only
   thermal snapshot first, then a report-only log evidence analyzer. Keep any
   lossy converter and profile alias behind their separate gates.
6. Close the remaining bounded reliability follow-ups in
   `docs/features/feature-memory-ui-reliability.md`; keep optional long-soak work separate
   from normal patch promotion.

## Rules

- New features and meaningful behavior changes need a spec first.
- Keep Standard behavior intact unless a spec explicitly changes it.
- Dashboard code must not call `/Sensor?action=Set`.
- Do not hard-code host sensor IDs, labels, limits, or missing values as zero.
- Preserve raw LibreHardwareMonitor labels and `SensorId` when aliases exist.
- Native sensor organization is presentation-only; canonical node order and
  `data.json` IDs/order remain unchanged.
- Check dark/light, desktop/narrow, failure, and empty states for UI work.

## Source and live runtime contracts

- The live SND-HOST runtime remains product `0.9.6+d693da7.2026-07-25`.
  Its external proxy guard blocks public reset routes; this source integration
  has not replaced that process.
- In merged source, GET `/Sensor` failures return JSON; GET Set and ResetMinMax
  are rejected. POST Set validates/clamps values. `ResetMinMax` and
  `/ResetAllMinMax` mutate only on POST. Cross-origin browser POSTs are rejected
  before mutation; header-less script clients remain allowed when they POST.
  These stricter request contracts are not live until a separately approved
  candidate is promoted.
- Sensor history, decompression, HTTP ownership, and dashboard state are bounded.
- Settings writes are ordered, atomic, backup-aware, and compact stale history.
- RTX 5090 hot spot and its rate remain unavailable until live telemetry proves
  otherwise; warm-up/dropouts never become zero.
- NVIDIA 12VHPWR pins use `/voltage/1..6`; core voltage keeps `/voltage/0`.

## Source map

- `docs/architecture/refactor-roadmap.md` - active repository/campaign reorganization
  phases, invariants, gates, standing prohibitions, preserved Plan-001 branch
  evidence, open quarantine decisions, and the next-campaign boundary. **Start
  here for continuation.** The former `docs/HANDOFF.md` was folded into this
  file and `docs/README.md` when Plan-002 landed, and then deleted; there is no
  separate handoff file any more.
- `docs/architecture/campaign-control-plane.md` - authority, provenance,
  configuration, refresh, safety contract, and the campaign-history acceptance
  transition contract for local campaign tooling.
- `docs/campaign-backlog.md` - the sequenced campaign queue: next campaign,
  entry conditions, agent ownership, exit criteria, and the outline arc through
  Phase 6.
- `docs/campaign-playbook.md` - operational runbook for running a campaign:
  lifecycle commands, non-obvious tooling behavior, environment traps, the
  four-tier verification ladder, and ownership rules.
- `docs/campaign-history.md` - durable tracked ledger of per-criterion campaign
  acceptance, with four ledger states and mandatory waiver fields. A row here
  is human acceptance evidence; it is not authority to deploy.
- `eng/ci/` - the non-deploying gate runner and its test suite. Gate commands
  are defined only in `.codex/skills/project.toml`; the runner reads them at run
  time, requires every gate to be classified, and refuses deny-listed commands.
  `eng/ci/README.md` documents the classification and exclusions.
- `.github/workflows/non-deploying-gates.yml` - read-only workflow that only
  delegates to that runner. Never executed on a hosted runner, because `origin`
  is deliberately unpushed.
- The structural discovery audits (`docs/discovery-*.md`) were retired in Plan-005
  after their findings were folded into `docs/architecture/refactor-roadmap.md` and
  the campaign backlog; the full text remains recoverable in Git history.
- `docs/features/feature-web-dashboard-studio-view.md` - shipped Studio contract.
- `docs/features/feature-sensor-workspace.md` - active Workspace contract.
- `docs/features/feature-thermal-trends.md` - additive hotspot-rate contract.
- `docs/features/feature-host-log-management.md` - archive, retention, and deployment
  safety contract.
- `docs/features/feature-release-packaging.md` - fail-closed, external dual-framework
  release-candidate packaging and validation contract.
- `docs/features/feature-host-operator-utilities.md` - planned portable thermal snapshot
  and evidence-gated log analysis.
- `docs/features/feature-independent-text-scaling.md` - shipped independent sensor-pane,
  tracker, and graph-axis text scaling contract.
- `docs/features/feature-native-ui-modernization.md` - phased native tree organization,
  graphics, graph, and Gadget 2.0 roadmap; the Phase 0 packet is drafted and
  product implementation has not started.
- `docs/features/feature-avalonia-fixture-sensor-explorer.md` - accepted bounded,
  fixture-only Avalonia explorer contract and automated evidence; implemented
  as a non-elevated, non-shipping source spike, with exact-source candidate and
  package-isolation proof complete and attended smoke still pending.
- `docs/features/feature-standard-context-layouts.md` - source-shipped,
  browser-fixture-verified per-context Standard trims (Main/Gaming/Storage) over
  a materialize-swap contexts key; live runtime promotion is not recorded.
- `docs/features/feature-memory-ui-reliability.md` - shipped reliability contract,
  deployment proof, and remaining follow-ups.
- `docs/features/feature-upstream-sync-2026-07-25.md` - audited upstream integration
  boundary, conflict decisions, compatibility requirements, and verification.
- `docs/features/feature-local-release-system.md` - implemented shallow one-EXE local
  runtime, `sqdata` separation, managed launch ownership, promotion, rollback,
  and attended-finalization contract for SND-DESK only.
- `docs/architecture/repository-build-output-cleanup.md` - completed repo-local `bin`/`obj`
  cleanup, preserved historical archive, repeatable cleanup command, retired
  pre-stable recovery boundary, and verified SND-DESK public launcher chain.
- `LibreHardwareMonitorLib/Hardware/Sensor.cs` - history bounds/persistence.
- `LibreHardwareMonitorLib/Hardware/TemperatureRateSensor.cs` - bounded direct
  sample regression for temperature rate.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` -
  streaming, cleanup, ordering, and atomic settings writes.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` - lifecycle/autosave.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` - HTTP contracts.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` - dashboard.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js` - bounded
  Workspace model, presets, profile operations, and import/export.
- `ops/log-management/` - host-neutral log operations and task-install package.
- `ops/candidate/` - external clean staging, candidate manifest/hash validation,
  guarded repository-output cleanup, and release-system regression tests; no
  deployment or promotion.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedVScrollIndicator.cs` and
  `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedHScrollIndicator.cs` -
  visible native-sized sensor-tree hit targets.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ScrollIndicatorAutomationProvider.cs`
  - UI Automation `RangeValue` bridge to the native scrollbars.

## Verify

Run everything through the gate runner. It is the single non-deploying entry
point and refuses any deploying command:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
```

Set `PYTHONDONTWRITEBYTECODE` before any Python command in this repository.
Without it, importing `scripts/task_runtime` leaves `__pycache__` directories,
and the structural baseline records zero generated directories.

`-All` runs three `dotnet build` gates, so `git clean -ndX` afterwards lists the
recreated `bin/` and `obj/` trees. That is expected. Return to a clean state
with the guarded cleanup tool, never with `git clean -fdX`:

```powershell
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\eng\Clear-LhmRepositoryBuildOutputs.ps1
```

That script's path list is deliberately explicit: a project does not inherit
destructive cleanup merely by having a `bin` or `obj` directory. Add any new
project to `$relativeOutputRoots` by hand.

The individual commands remain valid and are still the right tool when you want
one thing rather than the whole sweep:

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\New-LhmRelease.ps1 -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseCandidate.ps1 -Latest -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource -ReleaseRoot <external-release-root>
# Peer-safe non-live fixture for the SND-DESK workflow; production remains target-gated.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\deploy\snd-desk\Test-LhmLocalRelease.ps1
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

### Live SND-HOST proof

Source work must never change the live runtime. To prove it did not, check all
five and expect them unchanged except for CSV growth:

1. exactly one LHM process, and its executable path is
   `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current\LibreHardwareMonitor.Windows.Forms.exe`;
2. root task `\LibreHardwareMonitor` is `Running` — result `267009` / `0x41301`
   means still running, not failed;
3. the task action and working directory match the live root exactly;
4. proxy-bypassed `/`, `/data.json`, and `/metrics` all return HTTP `200`;
5. the current-day CSV under the live root is still growing.

Two things about step 4 will otherwise waste time:

- The port comes from `listenerPort` in the live
  `LibreHardwareMonitor.Windows.Forms.config`, currently `8080`.
- The listener binds the host's LAN address, not loopback, so `localhost:8080`
  is actively refused even while the server is healthy. Probe the bound address.

```powershell
$bound = (Get-NetTCPConnection -State Listen -LocalPort 8080).LocalAddress
foreach ($u in '/', '/data.json', '/metrics') {
    (Invoke-WebRequest "http://${bound}:8080$u" -UseBasicParsing -Proxy $null).StatusCode
}
```

The server is `HttpListener`, so it is http.sys-backed and the listening socket
is owned by `System` (PID 4), not by the LibreHardwareMonitor process.
`Get-NetTCPConnection -OwningProcess <lhm-pid>` therefore returns nothing. That
is normal and is **not** evidence that the dashboard is down.

Verify machine identity before any machine-sensitive mutation:

```powershell
& 'C:\Users\Dev\OneDrive\common\common_dev\Get-VerifiedMachineIdentity.ps1'
```

Stop unless it returns `VERIFIED` with machine ID `snd-host`.

## Docs policy

- Keep this README, current feature specs, and the active architecture and
  roadmap contracts only. Continuation state lives in
  `docs/architecture/refactor-roadmap.md`, not in a separate handoff file.
- Do not hand-edit `docs/campaign-plan-*.md`. Those documents are rendered from
  `data/plans/*.json` on every plan mutation, so a manual edit is silently
  overwritten. Change the plan JSON and let the tooling re-render.
- Fold live findings and proof into the owning spec.
- Delete completed point-in-time discovery/review notes after their unresolved
  findings are folded into an active contract; Git history preserves detail.
- Verify live repo/runtime state before trusting old evidence.
