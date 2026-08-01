# Agent Task — Suite Split (Plan-006, Agent Y)

**Scope:** Split the flat `LibreHardwareMonitor.Tests` project into four boundary-aligned
suite projects under `LibreHardwareMonitor.Tests/`, plus a solution filter that defines
deterministic CI membership. Pure `git mv` for every existing file — zero content edits to
moved `.cs` files or to `data.golden.json`.

**Depends on:** (none) — this lane freezes the layout everything downstream keys on.

---

## Baseline (recorded before the campaign, clean HEAD `cae786c`)

`dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64`
→ **Total 259, Passed 258, Skipped 1, Failed 0**, exit 0. The single skip is
`SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets`
(`LiveConfigFactAttribute`, gated on `LHM_LIVE_CONFIG_PATH`). Your result must reproduce
this population exactly, summed across the three deterministic suites.

## Target layout

```
LibreHardwareMonitor.Tests/
  LibreHardwareMonitor.Tests.slnf
  LibreHardwareMonitor.Tests.Library/      (references LibreHardwareMonitorLib ONLY)
  LibreHardwareMonitor.Tests.Application/  (references LibreHardwareMonitor.Windows.Forms)
  LibreHardwareMonitor.Tests.Contracts/    (references LibreHardwareMonitor.Windows.Forms)
  LibreHardwareMonitor.Tests.Attended/     (empty; outside deterministic CI by construction)
```

### Suite membership — exact, no discretion

**Library (7):** ComputerOpenLifetimeTests.cs, MotherboardModelCompatibilityTests.cs,
Nct677XFanConfigTests.cs, NvidiaGroupSnapshotTests.cs, SensorHistoryTests.cs,
StorageGroupLifetimeTests.cs, StorageSmartUpdateCycleTests.cs

**Application (13):** HardwareOperationCoordinatorTests.cs, PlotPanelHistoryTests.cs,
PlotPanelTextScaleTests.cs, RuntimePathsTests.cs, SettingsPersistenceTests.cs,
SmartUpdateCyclePolicyTests.cs, StartupManagerTests.cs, TemperatureRateSensorTests.cs,
TextScaleSliderMenuTests.cs, UiScaleTests.cs, UiShutdownCoordinatorTests.cs,
UiTextScaleCommitGateTests.cs, WinFormsUiLifetimeTests.cs

**Contracts (7 + golden):** DataJsonGoldenTests.cs, data.golden.json,
HttpServerAuthenticationTests.cs, HttpServerLifetimeTests.cs, HttpServerPrometheusTests.cs,
HttpServerSensorApiTests.cs, CsvTimestampContractTests.cs, WebDashboardRetirementTests.cs

**Attended (0):** no test files move here. Author a short `README.md` stating the boundary:
hardware-dependent and attended tests live here, the suite is in `LibreHardwareMonitor.sln`
but deliberately absent from `LibreHardwareMonitor.Tests.slnf` and from every configured
gate command, and adding it to deterministic CI requires a new accepted specification.

## Implementation notes

1. **Moves are `git mv` with no content edits.** All files keep
   `namespace LibreHardwareMonitor.Tests;` — namespaces do not need to match assembly
   names, and unchanged content is what makes git record pure renames (criterion 1) and
   keeps `data.golden.json` byte-identical (criterion 3).
2. **`DataJsonGoldenTests.cs` and `data.golden.json` must land in the same directory.**
   The test resolves the golden via `[CallerFilePath]` adjacency. Never regenerate the
   golden; a diff in its bytes is evidence of a mistake.
3. **New csproj files** mirror the retired one: `net10.0-windows`, `Platforms`/
   `PlatformTarget` x64, `IsPackable` false, `LangVersion` latest, packages
   `Microsoft.NET.Test.Sdk` + `xunit` + `xunit.runner.visualstudio` (centrally versioned —
   no version attributes). Differences per suite:
   - Library: `<ProjectReference>` → `..\..\LibreHardwareMonitorLib\LibreHardwareMonitorLib.csproj`.
     No `UseWindowsForms`, no WPF reference unless the build proves one necessary.
     `StorageGroupLifetimeTests` uses `DiskInfoToolkit` — expect it transitively from the
     Lib reference; add an explicit centrally-versioned `PackageReference` only if restore
     fails without it.
   - Application: `<ProjectReference>` → WinForms csproj, `UseWindowsForms` true, keep
     `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />`
     (`WinFormsUiLifetimeTests` uses `System.Windows.Automation`).
   - Contracts: `<ProjectReference>` → WinForms csproj, `UseWindowsForms` true. Include the
     WPF FrameworkReference only if the build requires it; drop it if clean without.
   - Attended: `<ProjectReference>` → WinForms csproj, `UseWindowsForms` true, no tests.
4. **`LibreHardwareMonitor.Tests.slnf`** references `..\LibreHardwareMonitor.sln`
   (JSON `"solution": { "path": ..., "projects": [...] }`) and lists exactly the three
   deterministic suite csproj paths — **not** Attended. Prove the gate command form works:
   `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64`.
5. **`LibreHardwareMonitor.sln`:** remove the old Tests project entry (GUID
   `{675ED7E8-E8E8-4423-8FAC-36990FC85FCE}`) and its configuration rows; add the four suite
   projects with fresh GUIDs and the same x64 mapping pattern the old entry used
   (Debug/Release × ARM64/x64/x86 → ActiveCfg x64, Build.0 on x64 only).
6. **InternalsVisibleTo:** in `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj` and
   `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj`, replace
   the single `<InternalsVisibleTo Include="LibreHardwareMonitor.Tests" />` with four
   entries: `.Tests.Library`, `.Tests.Application`, `.Tests.Contracts`, `.Tests.Attended`.
   **This ItemGroup is the only permitted edit in either product csproj.**
7. Retire the old `LibreHardwareMonitor.Tests.csproj` (`git rm`) once the suites hold every
   file.

## Verification (all must pass before handoff)

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Library\LibreHardwareMonitor.Tests.Library.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
git status --porcelain   # every moved file must show as a rename (R), data.golden.json included
```

Summed suite totals must equal 259/258/1 with the single skip in the Application suite.
The Library suite's build output must contain no `LibreHardwareMonitor.Windows.Forms.dll`.

## Exit Criteria

- The four suite projects and `LibreHardwareMonitor.Tests.slnf` exist under
  `LibreHardwareMonitor.Tests/` with exactly the membership tables above; the old
  `LibreHardwareMonitor.Tests.csproj` is removed; every moved file is a git-recorded rename.
- All three deterministic suites and the slnf invocation pass; summed totals equal the
  baseline 259 discovered / 258 passed / 1 skipped, the skip being the
  `LHM_LIVE_CONFIG_PATH` harness in the Application suite.
- `data.golden.json` is byte-identical, adjacent to `DataJsonGoldenTests.cs` in the
  Contracts suite, and `DataJsonGoldenTests` passes without regeneration.
- The Library suite csproj has no WinForms reference and its build output contains no
  `LibreHardwareMonitor.Windows.Forms.dll`.
- Both WinForms x64 Release targets build; product csproj deltas are limited to the
  InternalsVisibleTo ItemGroups; the `LibreHardwareMonitor.sln` delta is limited to the
  test-project graph.
- The Attended suite exists in the sln with its boundary README, holds no tests, and is
  absent from the slnf.

## Do NOT

- Edit any file outside the declared ownership (in particular: `.codex/skills/project.toml`,
  ops scripts, `eng/` — those belong to agent z; report needed changes in your payload).
- Edit moved `.cs` content, rename namespaces, or regenerate `data.golden.json`.
- Touch anything in the product csproj files beyond the InternalsVisibleTo ItemGroup.
- Run `python scripts/task_manager.py merge` (inline execution — merge reverts tracked edits).
- Write to `live-tracker.md`. **Single-writer override:** agent ab owns the tracker for
  Plan-006. Return your tracker row text in your result payload instead.

## Result payload

Return: your tracker row (ID `SUITESPLIT-001`, owner agent-y), per-suite test totals, the
slnf invocation result, both Release build results, the rename-detection evidence, and any
defect found in files you do not own.
