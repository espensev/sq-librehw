# SQ LibreHardwareMonitor Docs

**Status:** live map only
**Updated:** 2026-07-30

## Repository

- `origin` is `celine-anime/librehw-host`; the default and working branch is
  `main`. Push and pull there unless a task says otherwise.
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
- The imported `ops/local-release` and `scripts/local-release` surfaces are
  SND-DESK-only in production: deployment and launcher paths fail closed to
  `snd-desk`, while peer-safe non-live fixtures use isolated temporary roots.
  Their `LibreHW`, `sqdata`, launcher, task, user, and cleanup records do not
  replace the SND-HOST paths below.

## Current SND-HOST paths

- Source checkout:
  `E:\SQ_HQ\Monitoring\libre-dev\librehw-host`
- Immutable release candidates:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Releases\candidates`
- Live application:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor`
- Deployment rollback packets:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Rollback`
- Installed log manager and archive:
  `E:\SQ_HQ\Monitoring\LhmLogManagement` and
  `E:\SQ_HQ\Monitoring\LogArchive`

The user-level `LHM_RELEASE_ROOT` is
`E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Releases`. Source, release, live,
rollback, and log/archive state are separate; no release payload belongs under
`libre-dev`.

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
- Release-candidate tooling is source-controlled under `ops/release`. It builds
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

The imported upstream roadmap also records a stale SND-DESK
`hardware-optimization` health-feed task. That is a peer-only owner action, not
an unqualified SND-HOST command.

1. Continue hands-on dashboard and native scrollbar/UI Automation inspection
   through the verified runtime owner; deterministic coverage and the live
   served-asset/telemetry smoke are already complete.
2. Execute `docs/feature-native-ui-modernization.md` in bounded slices: define
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
   `docs/feature-memory-ui-reliability.md`; keep optional long-soak work separate
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

- `docs/feature-web-dashboard-studio-view.md` - shipped Studio contract.
- `docs/feature-sensor-workspace.md` - active Workspace contract.
- `docs/feature-thermal-trends.md` - additive hotspot-rate contract.
- `docs/feature-host-log-management.md` - archive, retention, and deployment
  safety contract.
- `docs/feature-release-packaging.md` - fail-closed, external dual-framework
  release-candidate packaging and validation contract.
- `docs/feature-host-operator-utilities.md` - planned portable thermal snapshot
  and evidence-gated log analysis.
- `docs/feature-independent-text-scaling.md` - shipped independent sensor-pane,
  tracker, and graph-axis text scaling contract.
- `docs/feature-native-ui-modernization.md` - phased native tree organization,
  graphics, graph, and Gadget 2.0 roadmap; the Phase 0 packet is drafted and
  product implementation has not started.
- `docs/discovery-pre-avalonia-readiness.md` - bounded gap/seam analysis and
  ownership rules for starting a fixture-only Avalonia spike in parallel.
- `docs/feature-avalonia-fixture-sensor-explorer.md` - accepted bounded,
  fixture-only Avalonia explorer contract and automated evidence; implemented
  as a non-elevated, non-shipping source spike, with exact-source candidate and
  package-isolation proof complete and attended smoke still pending.
- `docs/feature-standard-context-layouts.md` - source-shipped,
  browser-fixture-verified per-context Standard trims (Main/Gaming/Storage) over
  a materialize-swap contexts key; live runtime promotion is not recorded.
- `docs/feature-memory-ui-reliability.md` - shipped reliability contract,
  deployment proof, and remaining follow-ups.
- `docs/feature-upstream-sync-2026-07-25.md` - audited upstream integration
  boundary, conflict decisions, compatibility requirements, and verification.
- `docs/feature-local-release-system.md` - implemented shallow one-EXE local
  runtime, `sqdata` separation, managed launch ownership, promotion, rollback,
  and attended-finalization contract for SND-DESK only.
- `docs/repository-build-output-cleanup.md` - completed repo-local `bin`/`obj`
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
- `ops/release/` - external clean staging, candidate manifest/hash validation,
  guarded repository-output cleanup, and release-system regression tests; no
  deployment or promotion.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedVScrollIndicator.cs` and
  `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedHScrollIndicator.cs` -
  visible native-sized sensor-tree hit targets.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ScrollIndicatorAutomationProvider.cs`
  - UI Automation `RangeValue` bridge to the native scrollbars.

## Verify

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Test-AvaloniaSpike.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1 -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource -ReleaseRoot <external-release-root>
# Peer-safe non-live fixture for the SND-DESK workflow; production remains target-gated.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\local-release\Test-LhmLocalRelease.ps1
.\scripts\local-release\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Docs policy

- Keep this README and current feature specs only.
- Fold live findings and proof into the owning spec.
- Delete completed discovery/review notes; Git history preserves the detail.
- Verify live repo/runtime state before trusting old evidence.
