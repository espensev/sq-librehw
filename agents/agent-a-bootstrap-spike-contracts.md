# Agent Task — Bootstrap spike contracts

**Scope:** Register additive package versions and scaffold the isolated
solution, projects, composition root, and immutable loader contracts.

**Depends on:** none

**Output files:** `Directory.Packages.props`,
`LibreHardwareMonitor.Avalonia.Spike.slnx`,
`LibreHardwareMonitor.Avalonia.Spike.Core/LibreHardwareMonitor.Avalonia.Spike.Core.csproj`,
`LibreHardwareMonitor.Avalonia.Spike/LibreHardwareMonitor.Avalonia.Spike.csproj`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/LibreHardwareMonitor.Avalonia.Spike.Tests.csproj`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorSnapshot.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorNodeSnapshot.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadLimits.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadResult.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorLoadError.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/ISensorFixtureLoader.cs`,
`LibreHardwareMonitor.Avalonia.Spike/Program.cs`,
`LibreHardwareMonitor.Avalonia.Spike/App.axaml`,
`LibreHardwareMonitor.Avalonia.Spike/App.axaml.cs`

## Exit Criteria

- The root central package file adds only the approved Avalonia/xUnit v3
  versions and changes no existing package version.
- A separate three-project `net10.0` spike solution restores without touching
  `LibreHardwareMonitor.sln` or `global.json`.
- Immutable contracts compile independently and exactly encode the accepted
  limits and load-result semantics.
- The desktop bootstrap is non-elevated and references no WinForms, hardware,
  settings, HTTP, task, or release component.
- Project references and package references are sufficient for agents B and C
  to add implementation files without editing any project file.

---

## Context — read before doing anything

1. `AGENTS.md` — repository task classification, spec-first rule, source map,
   and baseline commands.
2. `docs/feature-avalonia-fixture-sensor-explorer.md` — accepted package,
   project, contract, hard-limit, and non-shipping decisions.
3. `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` — ownership,
   dependencies, risks, and complete-campaign exit criteria.
4. `docs/discovery-pre-avalonia-readiness.md` — hardware/settings/process
   boundaries and serialized bootstrap requirement.
5. `Directory.Packages.props` — preserve Central Package Management and every
   existing version.
6. `global.json` — use the pinned stable SDK; do not change its test runner.
7. `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj`
   and `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj` — inspect
   only to preserve existing project/test isolation.
8. Official version evidence linked from the feature spec. Do not upgrade past
   the accepted versions during implementation.

---

## Task

### Part 1 — Register additive central versions

Add these `PackageVersion` entries to `Directory.Packages.props`, preserving
alphabetical readability and all current entries:

- `Avalonia` `12.1.0`
- `Avalonia.Desktop` `12.1.0`
- `Avalonia.Headless.XUnit` `12.1.0`
- `Avalonia.Themes.Fluent` `12.1.0`
- `xunit.v3` `3.2.2`

Do not change the existing `xunit`, `xunit.runner.visualstudio`, or
`Microsoft.NET.Test.Sdk` entries. The old and new test frameworks coexist in
different projects.

### Part 2 — Scaffold the isolated solution and projects

Create `LibreHardwareMonitor.Avalonia.Spike.slnx` containing only:

1. `LibreHardwareMonitor.Avalonia.Spike.Core`;
2. `LibreHardwareMonitor.Avalonia.Spike`;
3. `LibreHardwareMonitor.Avalonia.Spike.Tests`.

Project requirements:

- Core: `Microsoft.NET.Sdk`, `net10.0`, nullable enabled, implicit usings
  enabled, no package or project reference.
- Desktop: `Microsoft.NET.Sdk`, `net10.0`, `WinExe`, nullable enabled,
  compiled Avalonia bindings enabled, project reference to Core, and
  versionless package references to `Avalonia`, `Avalonia.Desktop`, and
  `Avalonia.Themes.Fluent`.
- Tests: `Microsoft.NET.Sdk`, `net10.0`, `Exe`, `IsTestProject=true`,
  `IsPackable=false`, project references to Core and Desktop, and versionless
  package references to `Avalonia.Headless.XUnit` and `xunit.v3`. Copy
  `Fixtures/**/*.json` to the output directory without embedding them.

Do not add the projects to `LibreHardwareMonitor.sln`. Do not add a Windows
application manifest or any runtime identifier/publish profile.

### Part 3 — Freeze the immutable contract

Use namespace `LibreHardwareMonitor.Avalonia.Spike.Core.Contracts`.

Define:

- `SensorNodeKind` with `Group`, `Hardware`, and `Sensor`.
- `SensorValueSnapshot(double? Raw, string Display)` with `IsAvailable`.
  Availability is true only for a finite raw value.
- `SensorNodeSnapshot` as a sealed immutable record containing kind, name,
  nullable sensor ID, nullable hardware ID, nullable sensor type, nullable
  image URL, minimum/current/maximum `SensorValueSnapshot` values, and an
  `ImmutableArray<SensorNodeSnapshot>` of children. Expose a computed
  `StableId` that returns sensor ID, then hardware ID, then null.
- `SensorSnapshot` as a sealed immutable record containing source name,
  nullable version, immutable root nodes, total node count, and sensor count.
- `SensorLoadLimits` as a sealed immutable record with `MaxInputBytes`,
  `MaxDepth`, `MaxNodes`, `MaxChildrenPerNode`, and `MaxStringCharacters`.
  `Default` must be exactly 4 MiB, 32, 16,384, 4,096, and 1,024.
- `SensorLoadErrorCode` values for file not found, access denied, I/O failure,
  input too large, invalid JSON, invalid shape, excessive depth, excessive
  nodes, excessive children, excessive string length, missing sensor ID,
  duplicate sensor ID, and superseded/cancelled load.
- `SensorLoadError` as a sealed record of code and operator-safe message.
- `SensorLoadResult` as a sealed record with exactly one of snapshot or error,
  an `IsSuccess` property, and validated `Success`/`Failure` factories.
- `ISensorFixtureLoader` with cancellation-aware asynchronous methods for a
  stream plus source name and for a local file path. Both accept explicit
  `SensorLoadLimits` and return `Task<SensorLoadResult>`.

Reject mutable collection types from public contracts. Validate constructor
arguments where an invalid combination could publish contradictory state.

### Part 4 — Add the non-elevated composition root

Create a normal Avalonia desktop `Program` and `App` using Fluent theme. Do not
enable diagnostics, telemetry, persistence, single-instance behavior, or an
admin manifest.

Freeze these integration signatures for later agents:

```csharp
new MainWindowViewModel(
    new BoundedDataJsonFixtureLoader(),
    SensorLoadLimits.Default);

new MainWindow(
    viewModel,
    new FilePickerFixtureSource());
```

`App.axaml.cs` may refer to those types before agents B and C create them.
Agent A's independent compile gate is therefore the Core project; the complete
solution build belongs to agent D.

---

## Constraints

- Own only the listed files.
- Preserve Central Package Management; package versions never appear in a
  project file.
- Keep all public DTO state immutable.
- Keep the contracts consumer-local; do not reference or copy WinForms node,
  settings, or hardware types.
- Do not introduce a repository-wide MTP runner setting.
- Do not install templates or global/user packages as part of the change.

---

## Verification

Run:

```powershell
dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
dotnet build LibreHardwareMonitor.Avalonia.Spike.Core\LibreHardwareMonitor.Avalonia.Spike.Core.csproj -c Release --no-restore
dotnet sln LibreHardwareMonitor.sln list
git diff -- LibreHardwareMonitor.sln global.json LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj
```

The existing solution list and the final diff command must prove no change.

The project-configured full baseline remains:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
```

Do not run `New-LhmRelease.ps1` from this dirty parallel worktree. Agent D or
the manager runs both commands after integration and a clean accepted commit.

---

## Do NOT

- Do not edit `LibreHardwareMonitor.sln`, `global.json`, existing project
  files, WinForms source, hardware source, release scripts, or live files.
- Do not add `LibreHardwareMonitorLib` or WinForms project references.
- Do not create parser, view, view-model, fixture, test, verification, or docs
  files owned by agents B-D.
- Do not approve, execute, promote, deploy, or launch the campaign.

## Post-completion

Do not edit `live-tracker.md`; agent D exclusively owns it. Return this row in
your completion result for agent D to consolidate:

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-001 | Done | agent-a | package/project bootstrap and contracts | Fixture-only Avalonia explorer | Summarize restored projects, frozen signatures, and version isolation. |
