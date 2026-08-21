# Campaign — Verification suite boundaries

**Plan ID:** plan-006
**Date:** 2026-07-31
**Status:** executed
**Plan file:** data/plans/plan-006.json
**Plan doc:** docs/campaign-plan-006-verification-suite-boundaries.md
**Planner kind:** planner-refactor
**Source roadmap:** docs/architecture/refactor-roadmap.md

---

## 1. Goal

Split the single flat LibreHardwareMonitor.Tests project into four boundary-aligned suites under LibreHardwareMonitor.Tests/ — Library (LibreHardwareMonitorLib behavior only), Application (WinForms behavior), Contracts (data.json golden, HTTP routes, Prometheus, CSV, web-asset retirement), and Attended (hardware-dependent and attended, outside deterministic CI by construction) — so each future Phase 4/5 seam maps to a focused deterministic suite, while preserving every test case, the golden-master bytes, the one opt-in skip, both WinForms Release builds, and the byte-identical gate runner.

## 2. Exit Criteria

- The four suite projects (Library, Application, Contracts, Attended) and the LibreHardwareMonitor.Tests.slnf solution filter live under LibreHardwareMonitor.Tests/; all 27 test source files and data.golden.json are relocated with git mv so git log --follow resolves; the old LibreHardwareMonitor.Tests.csproj is removed.
- Boundary integrity holds: the Library suite references LibreHardwareMonitorLib only (no WinForms reference in its project or built dependency set); the Contracts suite holds the data.json golden, HTTP route/authentication/lifetime, Prometheus, CSV timestamp, and web-dashboard-retirement tests; the Application suite holds the 13 WinForms behavior test files including the preserved LHM_LIVE_CONFIG_PATH opt-in skip.
- data.golden.json is byte-identical - git records a pure rename - and DataJsonGoldenTests passes against it without regeneration.
- The three deterministic suites together discover 259 test cases with 258 passed and exactly the one pre-existing opt-in skip; no test case is lost, duplicated, or newly skipped relative to the recorded pre-campaign baseline.
- The Attended suite exists in LibreHardwareMonitor.sln, is absent from the slnf and from every configured gate command, and the new permanent eng/ci/tests/Test-SuiteBoundaries.ps1 proves that exclusion and passes; the CI dispatcher discovers four test scripts.
- git diff over the inherited product roots is limited to the InternalsVisibleTo ItemGroups of LibreHardwareMonitorLib.csproj and LibreHardwareMonitor.Windows.Forms.csproj plus the LibreHardwareMonitor.sln test-project graph; both WinForms x64 Release targets build.
- .codex/skills/project.toml commands, modules, smart-test mappings, conflict zones, and the winforms-net10 test command point at the new layout; the ops/candidate, ops/deploy/snd-desk, Test-AvaloniaSpike, and eng/Clear-LhmRepositoryBuildOutputs references are rewired; plan preflight --json is ready with zero errors; eng/ci/Invoke-LhmGates.ps1 is byte-identical.
- The extended stale-reference gate covers all 28 file relocations and the dissolved csproj path, including a purpose-built check for file types outside its default scan set, and passes; eng/ci/Invoke-LhmGates.ps1 -All passes from the campaign tree.
- No product source behavior, live runtime, scheduled task, release store, rollback packet, active log, or archive changed - proven by the standard five-point live SND-HOST check.
- The Plan-006 ledger row is added with criterion-specific evidence and is not marked accepted; the Phase 3 suite-split roadmap items are closed with characterization tests left open for plan-007; the backlog advances; after guarded cleanup, git clean -ndX lists only data/tasks.json and data/analysis-cache.json.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| 27 LibreHardwareMonitor.Tests/*.cs test files | git mv | move | medium - population and skip preservation proven by per-suite counts against the recorded baseline |
| LibreHardwareMonitor.Tests/data.golden.json | git mv | move | high - external data.json contract; bytes must stay identical, CallerFilePath adjacency preserved by moving it with DataJsonGoldenTests.cs |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj | git rm | retire | high - the gate and every script test command point here; all rewired by agent z in the same campaign |
| 4 new suite csproj + LibreHardwareMonitor.Tests.slnf + Attended README | new | add | medium - slnf must keep -p:Platform=x64 working and exclude the Attended suite |
| LibreHardwareMonitor.sln | project graph | rewire | high - cross-cutting conflict zone; one Tests entry replaced by four with x64 mappings |
| LibreHardwareMonitorLib.csproj + LibreHardwareMonitor.Windows.Forms.csproj | InternalsVisibleTo ItemGroup only | edit | high - first campaign touch of product project files; behavior-neutral visibility change guarded by criterion 6 |
| .codex/skills/project.toml | commands, modules, mappings, conflict zones, winforms-net10 test | rewire | high - single-owner configuration; runner reads it at run time |
| eng/ci/tests/Test-NoStaleReferences.ps1 | move-map + new textual block | extend | medium - default scan set misses .csproj/.sln/.slnf; purpose-built block required |
| eng/ci/tests/Test-SuiteBoundaries.ps1 | new permanent gate | add | medium - encodes the Attended-outside-CI contract durably |
| eng/Clear-LhmRepositoryBuildOutputs.ps1 | explicit bin/obj path list | edit | medium - destructive tool with deliberately explicit list; new suite outputs added by hand |
| ops/candidate/New-LhmRelease.ps1, ops/candidate/LhmRelease.Common.ps1, ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1 | test project path | rewire | medium - candidate creation and peer deploy preflight must keep running the deterministic suites |
| experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1 | test project path reference | rewire | low |
| eng/ci/README.md, AGENTS.md, docs/README.md, docs/features/feature-host-operator-utilities.md | path claims | docs | low - stale-reference gate enforces |
| docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, docs/campaign-history.md, live-tracker.md | campaign closure | docs | low - single writer ab |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| y | suite-split | Create the four suite projects and the solution filter under LibreHardwareMonitor.Tests/: git mv the 7 library-only test files into LibreHardwareMonitor.Tests.Library (references LibreHardwareMonitorLib only), the 13 WinForms behavior test files into LibreHardwareMonitor.Tests.Application, and the 7 external-contract test files plus data.golden.json into LibreHardwareMonitor.Tests.Contracts; create the empty LibreHardwareMonitor.Tests.Attended suite with its boundary README; retire the old LibreHardwareMonitor.Tests.csproj; author LibreHardwareMonitor.Tests.slnf listing exactly the three deterministic suites; rewire LibreHardwareMonitor.sln and replace the single InternalsVisibleTo entry with the four suite assembly names in both product csproj files. All three deterministic suites must pass with 259 total / 258 passed / 1 skipped and the golden master byte-identical. |  | LibreHardwareMonitor.Tests/ComputerOpenLifetimeTests.cs, LibreHardwareMonitor.Tests/CsvTimestampContractTests.cs, LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs, LibreHardwareMonitor.Tests/HardwareOperationCoordinatorTests.cs, LibreHardwareMonitor.Tests/HttpServerAuthenticationTests.cs, LibreHardwareMonitor.Tests/HttpServerLifetimeTests.cs, LibreHardwareMonitor.Tests/HttpServerPrometheusTests.cs, LibreHardwareMonitor.Tests/HttpServerSensorApiTests.cs, LibreHardwareMonitor.Tests/MotherboardModelCompatibilityTests.cs, LibreHardwareMonitor.Tests/Nct677XFanConfigTests.cs, LibreHardwareMonitor.Tests/NvidiaGroupSnapshotTests.cs, LibreHardwareMonitor.Tests/PlotPanelHistoryTests.cs, LibreHardwareMonitor.Tests/PlotPanelTextScaleTests.cs, LibreHardwareMonitor.Tests/RuntimePathsTests.cs, LibreHardwareMonitor.Tests/SensorHistoryTests.cs, LibreHardwareMonitor.Tests/SettingsPersistenceTests.cs, LibreHardwareMonitor.Tests/SmartUpdateCyclePolicyTests.cs, LibreHardwareMonitor.Tests/StartupManagerTests.cs, LibreHardwareMonitor.Tests/StorageGroupLifetimeTests.cs, LibreHardwareMonitor.Tests/StorageSmartUpdateCycleTests.cs, LibreHardwareMonitor.Tests/TemperatureRateSensorTests.cs, LibreHardwareMonitor.Tests/TextScaleSliderMenuTests.cs, LibreHardwareMonitor.Tests/UiScaleTests.cs, LibreHardwareMonitor.Tests/UiShutdownCoordinatorTests.cs, LibreHardwareMonitor.Tests/UiTextScaleCommitGateTests.cs, LibreHardwareMonitor.Tests/WebDashboardRetirementTests.cs, LibreHardwareMonitor.Tests/WinFormsUiLifetimeTests.cs, LibreHardwareMonitor.Tests/data.golden.json, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/LibreHardwareMonitor.Tests.Library.csproj, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/LibreHardwareMonitor.Tests.Application.csproj, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/LibreHardwareMonitor.Tests.Contracts.csproj, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Attended/LibreHardwareMonitor.Tests.Attended.csproj, LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Attended/README.md, LibreHardwareMonitor.sln, LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj, LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj | 0 | high |
| z | rewire-config-and-gates | Rewire every configuration and script reference to the retired single test project: .codex/skills/project.toml (commands test/test_fast, the winforms-tests module split, smart-test mappings, the data.json conflict-zone paths, and the winforms-net10 test command now targeting LibreHardwareMonitor.Tests.slnf), ops/candidate/New-LhmRelease.ps1 and LhmRelease.Common.ps1, ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1, experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1, and the six explicit test bin/obj paths in eng/Clear-LhmRepositoryBuildOutputs.ps1. Extend eng/ci/tests/Test-NoStaleReferences.ps1 with per-file move-map entries for all 28 relocations plus the dissolved csproj, and a purpose-built textual block covering the old csproj path (the default scan set misses .csproj/.sln/.slnf). Author the new permanent eng/ci/tests/Test-SuiteBoundaries.ps1 proving the Attended suite is excluded from the slnf and every gate command while the three deterministic suites are included. Update eng/ci/README.md gate documentation. eng/ci/Invoke-LhmGates.ps1 must stay byte-identical. | y | .codex/skills/project.toml, eng/ci/tests/Test-NoStaleReferences.ps1, eng/ci/tests/Test-SuiteBoundaries.ps1, eng/ci/README.md, eng/Clear-LhmRepositoryBuildOutputs.ps1, ops/candidate/LhmRelease.Common.ps1, ops/candidate/New-LhmRelease.ps1, ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1, experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1 | 1 | high |
| aa | verify-suite-boundaries | Owns no files. Independently verify the split campaign: eng/ci/Invoke-LhmGates.ps1 -All from the committed tree with a sanitized process PSModulePath (the known snd-desk-local-release-fixture Get-FileHash condition), eng/ci/Test-LhmCiGates.ps1 directly (must discover four test scripts), test-population preservation (259 discovered / 258 passed / exactly the one LHM_LIVE_CONFIG_PATH opt-in skip across the three deterministic suites, compared against the recorded pre-campaign baseline), golden-master byte identity via git diff rename detection, product-root diff limited to the InternalsVisibleTo groups and sln graph, Library-suite dependency isolation (no WinForms reference in its built dependency set), and the standard five-point live SND-HOST proof. Report defects in the result payload; do not patch other agents' files. | y, z |  | 2 | medium |
| ab | docs-and-close | Single tracker writer. Update every current document that names the retired test project path: AGENTS.md (baseline test command and the golden-master regeneration procedure path), docs/README.md (individual verification command), docs/features/feature-host-operator-utilities.md (targeted DataJsonGoldenTests command). Add the Plan-006 ledger row and criterion-specific evidence to docs/campaign-history.md without automatic acceptance, close the Phase 3 suite-split roadmap items in docs/architecture/refactor-roadmap.md (characterization tests stay open for plan-007), advance the backlog current position and remove the completed plan-006 section, and record all Plan-006 tracker rows in live-tracker.md from the agent result payloads. | y, z, aa | AGENTS.md, docs/README.md, docs/features/feature-host-operator-utilities.md, docs/campaign-history.md, docs/architecture/refactor-roadmap.md, docs/campaign-backlog.md, live-tracker.md, docs/features/feature-avalonia-fixture-sensor-explorer.md, docs/features/feature-local-release-system.md, docs/features/feature-memory-ui-reliability.md, docs/features/feature-native-ui-modernization.md, docs/features/feature-sensor-workspace.md, docs/features/feature-standard-context-layouts.md, docs/features/feature-thermal-trends.md, docs/features/feature-upstream-sync-2026-07-25.md, docs/features/feature-web-dashboard-studio-view.md | 3 | medium |

## 5. Dependency Graph

```text
Group 0: y
Group 1: z
Group 2: aa
Group 3: ab
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| LibreHardwareMonitor.Tests/ComputerOpenLifetimeTests.cs | y |
| LibreHardwareMonitor.Tests/CsvTimestampContractTests.cs | y |
| LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs | y |
| LibreHardwareMonitor.Tests/HardwareOperationCoordinatorTests.cs | y |
| LibreHardwareMonitor.Tests/HttpServerAuthenticationTests.cs | y |
| LibreHardwareMonitor.Tests/HttpServerLifetimeTests.cs | y |
| LibreHardwareMonitor.Tests/HttpServerPrometheusTests.cs | y |
| LibreHardwareMonitor.Tests/HttpServerSensorApiTests.cs | y |
| LibreHardwareMonitor.Tests/MotherboardModelCompatibilityTests.cs | y |
| LibreHardwareMonitor.Tests/Nct677XFanConfigTests.cs | y |
| LibreHardwareMonitor.Tests/NvidiaGroupSnapshotTests.cs | y |
| LibreHardwareMonitor.Tests/PlotPanelHistoryTests.cs | y |
| LibreHardwareMonitor.Tests/PlotPanelTextScaleTests.cs | y |
| LibreHardwareMonitor.Tests/RuntimePathsTests.cs | y |
| LibreHardwareMonitor.Tests/SensorHistoryTests.cs | y |
| LibreHardwareMonitor.Tests/SettingsPersistenceTests.cs | y |
| LibreHardwareMonitor.Tests/SmartUpdateCyclePolicyTests.cs | y |
| LibreHardwareMonitor.Tests/StartupManagerTests.cs | y |
| LibreHardwareMonitor.Tests/StorageGroupLifetimeTests.cs | y |
| LibreHardwareMonitor.Tests/StorageSmartUpdateCycleTests.cs | y |
| LibreHardwareMonitor.Tests/TemperatureRateSensorTests.cs | y |
| LibreHardwareMonitor.Tests/TextScaleSliderMenuTests.cs | y |
| LibreHardwareMonitor.Tests/UiScaleTests.cs | y |
| LibreHardwareMonitor.Tests/UiShutdownCoordinatorTests.cs | y |
| LibreHardwareMonitor.Tests/UiTextScaleCommitGateTests.cs | y |
| LibreHardwareMonitor.Tests/WebDashboardRetirementTests.cs | y |
| LibreHardwareMonitor.Tests/WinFormsUiLifetimeTests.cs | y |
| LibreHardwareMonitor.Tests/data.golden.json | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/LibreHardwareMonitor.Tests.Library.csproj | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/LibreHardwareMonitor.Tests.Application.csproj | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/LibreHardwareMonitor.Tests.Contracts.csproj | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Attended/LibreHardwareMonitor.Tests.Attended.csproj | y |
| LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Attended/README.md | y |
| LibreHardwareMonitor.sln | y |
| LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj | y |
| LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj | y |
| .codex/skills/project.toml | z |
| eng/ci/tests/Test-NoStaleReferences.ps1 | z |
| eng/ci/tests/Test-SuiteBoundaries.ps1 | z |
| eng/ci/README.md | z |
| eng/Clear-LhmRepositoryBuildOutputs.ps1 | z |
| ops/candidate/LhmRelease.Common.ps1 | z |
| ops/candidate/New-LhmRelease.ps1 | z |
| ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1 | z |
| experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1 | z |
| AGENTS.md | ab |
| docs/README.md | ab |
| docs/features/feature-host-operator-utilities.md | ab |
| docs/campaign-history.md | ab |
| docs/architecture/refactor-roadmap.md | ab |
| docs/campaign-backlog.md | ab |
| live-tracker.md | ab |
| docs/features/feature-avalonia-fixture-sensor-explorer.md | ab |
| docs/features/feature-local-release-system.md | ab |
| docs/features/feature-memory-ui-reliability.md | ab |
| docs/features/feature-native-ui-modernization.md | ab |
| docs/features/feature-sensor-workspace.md | ab |
| docs/features/feature-standard-context-layouts.md | ab |
| docs/features/feature-thermal-trends.md | ab |
| docs/features/feature-upstream-sync-2026-07-25.md | ab |
| docs/features/feature-web-dashboard-studio-view.md | ab |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| ['LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs', 'LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs', 'LibreHardwareMonitor.Tests/data.golden.json'] |  | External data.json zone: y relocates the two test-side files with bytes unchanged; z updates the zone's declared paths in project.toml; HttpServer.cs is never edited. |
| ['Directory.Packages.props', 'LibreHardwareMonitor.sln', 'experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx'] |  | Central package and project graph: y is the sole owner of the sln rewiring; Directory.Packages.props and the slnx are untouched - central versions already cover the test packages. |
| ['docs/README.md', 'live-tracker.md', 'data/plans/'] |  | Campaign truth: ab is the single writer; y, z, and aa return their tracker row text in their result payloads. |
| ['eng/ci/Invoke-LhmGates.ps1', '.codex/skills/project.toml'] |  | Gate-definition drift zone: z owns the configuration side only; the runner stays byte-identical so the zone cannot drift. |
| ['LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj', 'LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj'] |  | Inherited product roots: only the InternalsVisibleTo ItemGroups change, owned by y and guarded by criterion 6. |

## 8. Integration Points

- y freezes the four-suite layout, the slnf membership, the sln graph, and the InternalsVisibleTo entries; every downstream lane keys on those exact paths.
- z makes configuration, ops scripts, the cleanup tool, and the permanent gates agree with y's layout while the gate runner stays byte-identical.
- aa independently verifies the combined tree - gates, population counts, golden byte-identity, Library dependency isolation, live proof - and reports defects in its payload without patching other lanes.
- ab records the evidence: doc path claims, roadmap closure, backlog advance, the history ledger row, and every tracker row from the result payloads.

## 9. Schema Changes

- No database, settings, data.json payload, CSV, Prometheus, HTTP, or plan-artifact schema changes. New artifact kinds only: a solution filter (LibreHardwareMonitor.Tests.slnf) and four suite csproj files. The golden-master format and bytes are unchanged.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Golden master regenerated or byte-drifted during the move | low | high | Invariant forbids regeneration; git mv keeps bytes; criterion 3 requires git to record a pure rename and the test to pass unmodified; the file moves together with DataJsonGoldenTests.cs so CallerFilePath adjacency holds. |
| A test case is lost, duplicated, or silently skipped in the split | medium | high | Recorded pre-campaign baseline (259/258/1, exit 0); criterion 4 sums per-suite runs against it and names the only permitted skip. |
| InternalsVisibleTo rename breaks internals access for a suite | medium | medium | Uniform four-assembly IVT in both product csproj; agent y builds and runs every suite before handoff. |
| Old csproj path lingers where the stale-reference scan cannot see it (.csproj/.sln/.slnf/.json outside the .toml\|.md\|.ps1\|.yml scan set) | high | medium | Agent z adds per-file move-map entries plus a purpose-built textual block widened to the affected file types; agent aa independently greps the tracked tree. |
| The slnf invocation breaks -p:Platform=x64 resolution or skips a suite | medium | high | Agent y proves the exact gate command locally; Test-SuiteBoundaries.ps1 pins the slnf membership; verification step 3 re-proves it. |
| Clear-LhmRepositoryBuildOutputs misses the new suite outputs (explicit-list design) | medium | low | Agent z replaces the two old test paths with the new per-suite bin/obj paths; aa reviews -WhatIf before the destructive run. |
| A later change quietly adds the Attended suite to CI | low | medium | Permanent Test-SuiteBoundaries.ps1 gate fails if the slnf or any gate command references the Attended suite. |
| task_manager merge run after inline execution reverts every tracked edit | low | high | Standing prohibition; this campaign executes inline in the primary checkout and needs no merge step; recorded here so no operator invokes it. |
| The pre-existing snd-desk-local-release-fixture Get-FileHash condition masks a real regression in the sweep | medium | low | Sweep runs with the sanitized process PSModulePath per the Plan-003 criterion-4 evidence; the fixture is outside every plan-006 surface. |

## 11. Verification Strategy

- Pre-campaign baseline recorded from clean HEAD cae786c: dotnet test 259 total / 258 passed / 1 skipped (LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets), exit 0.
- Per-suite runs: dotnet test each of the three deterministic suite csproj files -p:Platform=x64; summed totals must equal the baseline population with the single skip in the Application suite.
- Aggregate gate path: dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf -p:Platform=x64 discovers and passes all three suites in one invocation.
- Tier 2 sweep: eng/ci/Invoke-LhmGates.ps1 -All with sanitized process PSModulePath passes; eng/ci/Test-LhmCiGates.ps1 run directly discovers four test scripts and passes; git diff --check clean.
- Structural proofs: git diff --stat over inherited product roots limited to the two InternalsVisibleTo ItemGroups and the sln graph; data.golden.json recorded as a pure rename; Library suite dependency set contains no LibreHardwareMonitor.Windows.Forms assembly.
- Live proof: the five-point SND-HOST check (single process at the live executable, root task Running, action and working directory match, proxy-bypassed / and /data.json and /metrics HTTP 200, current-day CSV growing) unchanged except CSV growth.
- Guarded cleanup: eng/Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf reviewed then run with the four new suite output paths; git clean -ndX lists only data/tasks.json and data/analysis-cache.json.
- Configured framework gates pass unchanged: compile command dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 and build command dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 (the winforms-net10 and winforms-net472 gates).

## 12. Documentation Updates

- AGENTS.md: baseline test command and the data.golden.json regeneration procedure path move to the Contracts suite location.
- docs/README.md: the individual dotnet test command targets the slnf; suite layout stated in the current-state map.
- docs/features/feature-host-operator-utilities.md: targeted DataJsonGoldenTests filter command path updated.
- eng/ci/README.md: winforms-net10 gate description reflects the slnf test command and the Attended exclusion contract.
- docs/architecture/refactor-roadmap.md: Phase 3 suite-split items closed; characterization tests remain open for plan-007.
- docs/campaign-backlog.md: plan-006 section removed, current position advanced to plan-007, next agent letter ac.
- docs/campaign-history.md: Plan-006 ledger row with per-criterion evidence, ledger state implemented, never accepted by automation.
- live-tracker.md: Plan-006 section with one row per agent, written only by agent ab.


## R1. Roadmap Phase

Phase: Phase 3 - Verification suite boundaries
Roadmap reference: docs/architecture/refactor-roadmap.md

## R2. Behavioral Invariants

- data.json shape, order, IDs, and golden bytes are unchanged; data.golden.json moves by git mv only and is never regenerated
- The deterministic test population is preserved: 259 discovered, 258 passed, exactly the one pre-existing LHM_LIVE_CONFIG_PATH opt-in skip; no test is dropped, duplicated, or newly skipped by the restructure
- Product behavior is unchanged: the only product-file edits are InternalsVisibleTo entries in LibreHardwareMonitorLib.csproj and LibreHardwareMonitor.Windows.Forms.csproj plus the LibreHardwareMonitor.sln test-project graph; both WinForms x64 Release targets keep building
- Hardware-dependent and attended tests are outside deterministic CI by construction: the winforms-net10 test command enumerates a solution filter that excludes the Attended suite
- The live SND-HOST runtime, scheduled tasks, config, release store, rollback packets, and logs are untouched

## R3. Rollback Strategy

Source-only campaign executed inline on main from clean HEAD cae786c: git revert of the campaign commit restores the single-project layout; every relocation is a git mv so the revert restores exact paths and the golden master bytes; no live, runtime, or scheduled-task state is involved
