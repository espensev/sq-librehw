# LibreHardwareMonitor Sev IQ - Contributor and agent notes

**Project:** LibreHardwareMonitor Sev IQ local fork  
**Status:** Active implementation fork  
**Updated:** 2026-07-31
**Purpose:** keep feature work spec-first without blocking normal review, build, launch, and bugfix work.

**Current SND-HOST workspace:** primary checkout at
`D:\DevHome\workspaces\librehw-host\checkouts\main`; future linked agent lanes
belong under `D:\DevHome\workspaces\librehw-host\worktrees`. The physical
release/runtime/rollback/log authority is
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack`; see `docs/README.md` and its
`manifests` directory before host-path work.

This repository is not specs-only: product code exists and normal maintenance can proceed. The rule is narrower: **new features and meaningful behavior changes need a clear feature spec before implementation starts**, unless the maintainer explicitly asks for a small direct fix or exploratory spike.

## 1. First classify the task

Before editing, decide which lane the user asked for:

- **Review/audit:** inspect code, docs, build output, or runtime behavior. Stay read-only unless the user asks for fixes.
- **Build/launch/verification:** run the requested build, clean, launch, or monitoring steps. No feature spec is needed.
- **Bugfix:** fix a concrete defect. For small fixes, implement directly and document the result. If the fix changes user-facing behavior or settings/API semantics, update or create a feature spec.
- **Spec/doc refinement:** edit docs only. Keep requirements traceable to acceptance and verification.
- **New feature or behavior change:** start from the nearest current feature spec in `docs/`; when none exists, create a compact spec covering the readiness checklist below.

If a requested implementation is ambiguous and acceptance is unclear, draft the spec or ask for the missing decision before writing product code.

## 2. Source-of-truth map

- `docs/README.md`: **start here** — compact current-state, contract, implementation, and verification map.
- `docs/architecture/refactor-roadmap.md`: **current continuation checkpoint** — active
  structural phases, non-negotiable boundaries, standing prohibitions,
  preserved branch evidence, open quarantine decisions, and exit gates. Read
  before resuming the structural baseline or starting another campaign.
- `docs/campaign-backlog.md`: the sequenced campaign queue — what to run next,
  its entry conditions, ownership, and exit criteria.
- `docs/campaign-playbook.md`: how to actually run a campaign here, including
  the tooling behavior and environment traps that are not obvious from this
  file. Read it before your first campaign.
- `docs/campaign-history.md`: durable per-criterion campaign acceptance ledger.
  A row here is human acceptance evidence, not authority to deploy.
- `docs/architecture/campaign-control-plane.md`: campaign truth layers,
  runtime provenance, configuration ownership, safety rules, and the
  campaign-history acceptance transition contract.
- `eng/ci/README.md`: the non-deploying gate runner, its gate classification,
  the excluded deploying keys, and the deny-list.
- `docs/features/feature-memory-ui-reliability.md`: shipped memory, ownership, efficiency, and UI-reliability contract, verification, and open follow-ups.
- `docs/features/feature-web-dashboard-studio-view.md`: shipped Studio dashboard behavior and verification record.
- `docs/features/feature-sensor-workspace.md`: deployed Workspace view with named profiles, ordered panels, and bounded import/export.
- `docs/features/feature-standard-context-layouts.md`: merged Standard context trims (Main/Gaming/Storage) over a materialize-swap contexts key.
- `docs/features/feature-thermal-trends.md`: deployed NVIDIA GPU hotspot rate sensor across native, web, CSV, and Prometheus.
- `docs/features/feature-independent-text-scaling.md`: shipped independent sensor-pane and graph-axis text scaling.
- `docs/features/feature-http-server-safety.md`: fail-closed HTTP listener host resolution and exposure contract; source implemented, live deployment not yet promoted.
- `docs/features/feature-graph-lanes.md`: source-implemented operator-defined graph lanes (same-type sensors on separate axes, per-lane zoom, height weight); automated gates pass, live verification pending.
- `docs/features/feature-host-log-management.md`: deployed host-neutral CSV archive, retention, and task-install package.
- `docs/features/feature-csv-log-storage-efficiency.md`: source-only compact CSV numeric formatting with a non-zero significant-digit floor; not yet a candidate or live behavior.
- `docs/features/feature-host-operator-utilities.md`: planned portable thermal snapshot and report-only log evidence analyzer.
- `docs/features/feature-live-state-verification.md`: read-only, manifest-driven live-state verifier for the SND-HOST operational stack; report-only, no deployment authority.
- `docs/features/feature-native-ui-modernization.md`: phased native tree, graphics, graph, and Gadget 2.0 roadmap; implementation not started.
- `docs/features/feature-local-release-system.md`: SND-DESK-only shallow runtime,
  publish/promotion/rollback, and launcher contract imported from the fetch-only
  upstream. Its production deployment and launcher paths fail closed to
  `snd-desk`; peer-safe non-live fixtures use isolated temporary roots. It is
  not the SND-HOST release path.
- `docs/architecture/repository-build-output-cleanup.md`: SND-DESK cleanup record and
  repeatable source-output inventory command. Its runtime and archive paths are
  peer-specific history, not SND-HOST authority.

Completed discovery and review evidence belongs in Git history. Keep unresolved
findings and current verification in the nearest feature spec instead of a
separate review archive.

When adding or changing a requirement, update the nearest feature spec, traceability note, or verification section in the same pass. A behavior change without acceptance criteria is unfinished.

## 3. Definition of ready for new features

A feature is ready for implementation only when its spec covers:

- problem and motivation;
- goals and non-goals;
- user-visible behavior, including edge cases and failure states;
- affected UI/menu paths, settings, API, logs, or data contracts;
- compatibility risks, especially upstream sync, `net472` vs `net10.0-windows`, admin rights, DPI, and hardware access;
- acceptance criteria;
- verification plan, including build commands and any manual launch/runtime checks.

## 4. During implementation

- Implement from the accepted spec, not from unstated assumptions.
- Keep edits scoped to the feature or bugfix.
- If implementation discovers the spec is wrong, update the spec before or with the code change.
- Preserve upstream compatibility unless the spec explicitly accepts a local-fork divergence.
- Keep generated output, logs, and local scratch artifacts out of source.

Useful baseline commands:

```powershell
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
```

The Contracts suite includes the **data.json golden-master tests** (`DataJsonGoldenTests`): the data.json
payload is an external downstream contract, so any change touching
`WinFormsNodeSensorSnapshotSource.Capture`, `DataJsonProjection.Project`,
`HttpServer.BuildDataJsonObject`, or the serialization path must keep these green. The golden file embeds the
assembly version; after a version bump, delete
`LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json`, re-run
`dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64`
once to regenerate, and review the diff before committing.

## 5. Before handoff

For docs/spec edits:

- check that new docs are linked from the workflow or relevant spec;
- search for stale path/name claims in docs you changed;
- confirm acceptance criteria and verification sections are not empty.

For product-code edits:

- run the repo-appropriate build;
- launch or otherwise exercise the changed workflow when practical;
- update the spec verification log or implementation notes after the behavior is checked.
