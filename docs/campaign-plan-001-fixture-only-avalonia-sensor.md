# Campaign — Fixture-only Avalonia sensor explorer

**Plan ID:** plan-001
**Date:** 2026-07-30
**Status:** approved
**Plan file:** data/plans/plan-001.json
**Plan doc:** docs/campaign-plan-001-fixture-only-avalonia-sensor.md
**Planner kind:** planner
**Source discovery docs:** docs/discovery-pre-avalonia-readiness.md

---

## 1. Goal

Prove a bounded, fixture-only Avalonia sensor explorer can consume the existing read-only data.json contract and render honest immutable sensor state without taking WinForms hardware, settings, task, packaging, or live-runtime ownership.

## 2. Exit Criteria

- The accepted spec and plan checkpoint is committed and independently verified as a clean promotable current-source candidate before implementation agents launch.
- The separate net10.0 spike solution restores and builds with Avalonia 12.1.0 while LibreHardwareMonitor.sln, global.json, and existing project files remain unchanged.
- The parser enforces the 4 MiB, depth 32, 16384-node, 4096-child, 1024-character, unique-ID, single-snapshot, and single-load bounds with no partial state.
- Recorded and generated tests cover normal, unavailable, hot-plug removal, malformed, oversized, duplicate-ID, and every hard-limit case.
- The Avalonia shell renders producer order, stable IDs, honest current/min/max values, and loaded, empty, loading, and rejection states with keyboard navigation.
- No spike project references WinForms, opens hardware, requests elevation, writes current settings, exposes control requests, joins packaging, or changes live state.
- Existing DataJsonGoldenTests, the complete .NET test suite, and both x64 Release WinForms targets pass unchanged.
- The release-system fixture passes and candidate inspection proves spike outputs are absent from WinForms release packages.
- An attended normal-user smoke and documentation evidence record the feasibility result and any separately gated polling recommendation.

## 3. Impact Assessment

| File | Current Lines | Change Type | Risk |
| --- | --- | --- | --- |
| Directory.Packages.props | 37 | modify | high |
| LibreHardwareMonitor.Avalonia.Spike.slnx | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/LibreHardwareMonitor.Avalonia.Spike.Core.csproj | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/LibreHardwareMonitor.Avalonia.Spike.Tests.csproj | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorSnapshot.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorNodeSnapshot.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadLimits.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadResult.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadError.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/ISensorFixtureLoader.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike/Program.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike/App.axaml | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/BoundedDataJsonFixtureLoader.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/DataJsonProjection.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/SensorValueProjection.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/normal.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/unavailable.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-before.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-after.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/malformed.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/oversized-string.json | new | create | low |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/BoundedDataJsonFixtureLoaderTests.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/FixtureReplacementTests.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/GeneratedLimitCases.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/ViewModelBase.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/MainWindowViewModel.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/SensorNodeViewModel.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/FixtureChoice.cs | new | create | high |
| LibreHardwareMonitor.Avalonia.Spike/Services/FilePickerFixtureSource.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestApplication.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestAppBuilder.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowViewModelTests.cs | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowHeadlessTests.cs | new | create | medium |
| scripts/Test-AvaloniaSpike.ps1 | new | create | medium |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Integration/FixtureExplorerIntegrationTests.cs | new | create | medium |
| docs/feature-avalonia-fixture-sensor-explorer.md | 302 | modify | medium |
| docs/README.md | 288 | modify | medium |
| live-tracker.md | new | create | low |

## 4. Agent Roster

| Letter | Name | Scope | Deps | Files Owned | Group | Complexity |
| --- | --- | --- | --- | --- | --- | --- |
| a | bootstrap-spike-contracts | Register additive package versions and scaffold the isolated solution, projects, composition root, and immutable loader contracts. |  | Directory.Packages.props, LibreHardwareMonitor.Avalonia.Spike.slnx, LibreHardwareMonitor.Avalonia.Spike.Core/LibreHardwareMonitor.Avalonia.Spike.Core.csproj, LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj, LibreHardwareMonitor.Avalonia.Spike.Tests/LibreHardwareMonitor.Avalonia.Spike.Tests.csproj, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorSnapshot.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorNodeSnapshot.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadLimits.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadResult.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadError.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/ISensorFixtureLoader.cs, LibreHardwareMonitor.Avalonia.Spike/Program.cs, LibreHardwareMonitor.Avalonia.Spike/App.axaml, LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs | 0 | high |
| b | implement-bounded-parser | Implement bounded data.json parsing, immutable projection, the recorded fixture matrix, and adversarial parser tests. | a | LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/BoundedDataJsonFixtureLoader.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/DataJsonProjection.cs, LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/SensorValueProjection.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/normal.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/unavailable.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-before.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-after.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/malformed.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/oversized-string.json, LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/BoundedDataJsonFixtureLoaderTests.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/FixtureReplacementTests.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/GeneratedLimitCases.cs | 1 | high |
| c | build-fixture-explorer-shell | Build the non-elevated Avalonia fixture explorer window, view models, file-selection adapter, and headless interaction tests against the frozen contracts. | a | LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml, LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs, LibreHardwareMonitor.Avalonia.Spike/ViewModels/ViewModelBase.cs, LibreHardwareMonitor.Avalonia.Spike/ViewModels/MainWindowViewModel.cs, LibreHardwareMonitor.Avalonia.Spike/ViewModels/SensorNodeViewModel.cs, LibreHardwareMonitor.Avalonia.Spike/ViewModels/FixtureChoice.cs, LibreHardwareMonitor.Avalonia.Spike/Services/FilePickerFixtureSource.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestApplication.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestAppBuilder.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowViewModelTests.cs, LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowHeadlessTests.cs | 1 | high |
| d | integrate-verify-document | Integrate the parser and shell, automate the complete isolation/regression gate, and record reviewed evidence and follow-on decisions. | b, c | scripts/Test-AvaloniaSpike.ps1, LibreHardwareMonitor.Avalonia.Spike.Tests/Integration/FixtureExplorerIntegrationTests.cs, docs/feature-avalonia-fixture-sensor-explorer.md, docs/README.md, live-tracker.md | 2 | high |

## 5. Dependency Graph

```text
Group 0: a
Group 1: b, c
Group 2: d
```

## 6. File Ownership Map

| File | Owner |
| --- | --- |
| Directory.Packages.props | a |
| LibreHardwareMonitor.Avalonia.Spike.slnx | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/LibreHardwareMonitor.Avalonia.Spike.Core.csproj | a |
| LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj | a |
| LibreHardwareMonitor.Avalonia.Spike.Tests/LibreHardwareMonitor.Avalonia.Spike.Tests.csproj | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorSnapshot.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorNodeSnapshot.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadLimits.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadResult.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadError.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/ISensorFixtureLoader.cs | a |
| LibreHardwareMonitor.Avalonia.Spike/Program.cs | a |
| LibreHardwareMonitor.Avalonia.Spike/App.axaml | a |
| LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs | a |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/BoundedDataJsonFixtureLoader.cs | b |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/DataJsonProjection.cs | b |
| LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/SensorValueProjection.cs | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/normal.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/unavailable.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-before.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-after.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/malformed.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/oversized-string.json | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/BoundedDataJsonFixtureLoaderTests.cs | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/FixtureReplacementTests.cs | b |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/GeneratedLimitCases.cs | b |
| LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml | c |
| LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs | c |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/ViewModelBase.cs | c |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/MainWindowViewModel.cs | c |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/SensorNodeViewModel.cs | c |
| LibreHardwareMonitor.Avalonia.Spike/ViewModels/FixtureChoice.cs | c |
| LibreHardwareMonitor.Avalonia.Spike/Services/FilePickerFixtureSource.cs | c |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestApplication.cs | c |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestAppBuilder.cs | c |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowViewModelTests.cs | c |
| LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowHeadlessTests.cs | c |
| scripts/Test-AvaloniaSpike.ps1 | d |
| LibreHardwareMonitor.Avalonia.Spike.Tests/Integration/FixtureExplorerIntegrationTests.cs | d |
| docs/feature-avalonia-fixture-sensor-explorer.md | d |
| docs/README.md | d |
| live-tracker.md | d |

## 7. Conflict Zone Analysis

| Conflict Zone | Affected? | Mitigation |
| --- | --- | --- |
| Directory.Packages.props and all spike project files | yes | Agent a exclusively owns the additive central versions and all project scaffolding; the files freeze before agents b and c start. |
| HttpServer.cs and DataJsonGoldenTests/data.golden.json | no | Producer files remain untouched; the spike consumes copied bounded fixtures and the existing golden tests run as regression evidence. |
| WinForms startup, manifest, settings, hardware, and shipping solution surfaces | no | No campaign agent owns these files; the isolation script fails if the spike references or modifies the shipping ownership surfaces. |
| Shared documentation and tracker | yes | Agent d is the only file owner; agents a-c return tracker rows in their completion results instead of editing the shared files. |

## 8. Integration Points

- Agent a freezes SensorSnapshot, SensorNodeSnapshot, SensorLoadLimits, SensorLoadResult, SensorLoadError, and ISensorFixtureLoader; agents b and c compile only against those contracts.
- Agent b implements ISensorFixtureLoader and supplies the recorded fixture matrix; agent d verifies the real loader against the shell and all hard limits.
- Agent c builds the window and view models against the frozen contracts and fake loaders; agent d runs the concrete parser-to-view integration checks.
- Agent d exclusively owns the full gate script, spec evidence, README state, and tracker consolidation after agents b and c complete.
- No database, settings, HTTP producer, hardware, task, release-routing, or live-runtime integration exists in this campaign.

## 9. Schema Changes

- No schema changes required.

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Avalonia 12 headless support introduces xUnit v3 beside the existing xUnit v2 suite. | medium | high | Use a separate test executable and solution, pin xunit.v3 3.2.2, run it directly, and leave global.json and LibreHardwareMonitor.Tests unchanged. |
| A malformed or excessive payload bypasses one bound or leaks partial state. | medium | high | Centralize immutable limits, validate before publish, use atomic replacement, and test byte, depth, node, child, string, and duplicate-ID failures. |
| Parallel parser and UI work drift from the agreed snapshot/loader contract. | medium | medium | Agent a owns and freezes all shared contracts; agents b and c own disjoint files and agent d performs concrete integration. |
| The prototype is mistaken for a shipping replacement or second hardware owner. | medium | high | Keep spike naming, separate solution, normal-user launch, no hardware/settings references, no packaging, and explicit isolation tests. |
| Headless tests miss keyboard, native window, or normal-user behavior. | medium | medium | Require one attended non-elevated smoke after automated headless and regression gates. |
| Planning edits invalidate the previously verified current-source candidate. | high | medium | Keep this plan draft until maintainer acceptance, commit the accepted spec/plan, then cut and independently verify a new exact-source candidate before agent launch. |

## 11. Verification Strategy

- powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Test-AvaloniaSpike.ps1
- dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
- dotnet build LibreHardwareMonitor.Avalonia.Spike.slnx -c Release --no-restore
- dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release
- dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
- dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
- powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource

## 12. Documentation Updates

- Update docs/feature-avalonia-fixture-sensor-explorer.md with implementation and verification evidence plus the polling decision.
- Update docs/README.md only through the integration owner to reflect the accepted spike state without changing live-runtime claims.
- Create and maintain live-tracker.md through the integration owner; parallel agents return tracker rows without editing the shared file.
