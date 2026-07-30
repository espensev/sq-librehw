# Agent Task — Build fixture explorer shell

**Scope:** Build the non-elevated Avalonia fixture explorer window, view models,
file-selection adapter, and headless interaction tests against the frozen
contracts.

**Depends on:** agent A (`bootstrap-spike-contracts`)

**Output files:**
`LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml`,
`LibreHardwareMonitor.Avalonia.Spike/Views/MainWindow.axaml.cs`,
`LibreHardwareMonitor.Avalonia.Spike/ViewModels/ViewModelBase.cs`,
`LibreHardwareMonitor.Avalonia.Spike/ViewModels/MainWindowViewModel.cs`,
`LibreHardwareMonitor.Avalonia.Spike/ViewModels/SensorNodeViewModel.cs`,
`LibreHardwareMonitor.Avalonia.Spike/ViewModels/FixtureChoice.cs`,
`LibreHardwareMonitor.Avalonia.Spike/Services/FilePickerFixtureSource.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestApplication.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/UI/TestAppBuilder.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowViewModelTests.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/UI/MainWindowHeadlessTests.cs`

## Exit Criteria

- The single window clearly identifies itself as a fixture-only spike.
- Bundled fixture selection and explicit local JSON selection are read-only.
- Immutable hierarchy, values, stable IDs, source, version, and counts render
  without hardware or settings ownership.
- Loading, loaded, initial empty, initial rejection, and retained-last-good
  rejection states are explicit.
- Newer load requests supersede older requests without stale publication.
- Tree navigation and expand/collapse work from the keyboard in headless tests.
- UI/view-model tests use fake frozen loaders and do not depend on agent B's
  concrete parser, so agents B and C can run in parallel.

---

## Context — read before doing anything

1. `AGENTS.md` — repository rules, UI expectations, and baseline commands.
2. `docs/feature-avalonia-fixture-sensor-explorer.md` — approved window,
   commands, states, values, and non-goals.
3. `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` — file ownership,
   parallel group, integration contract, and risks.
4. `docs/discovery-pre-avalonia-readiness.md` — why the UI consumes immutable
   snapshots and cannot open hardware or settings.
5. Agent A's contracts under
   `LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/` and bootstrap
   `Program`/`App` — compile against them exactly; do not edit them.
6. Official Avalonia headless xUnit guidance linked in the feature spec.
7. `docs/feature-native-ui-modernization.md` — honesty, keyboard, color,
   bounded-resource, and presentation-only principles.

---

## Task

### Part 1 — Implement view models without another framework

Use namespace `LibreHardwareMonitor.Avalonia.Spike.ViewModels`. Implement
`ViewModelBase` with only `INotifyPropertyChanged`; do not add ReactiveUI,
CommunityToolkit, or a DI package.

`SensorNodeViewModel` is a read-only recursive projection of one
`SensorNodeSnapshot`. Expose:

- `Name`, `Kind`, `SensorType`, `StableId`;
- `Current`, `Minimum`, and `Maximum` display strings;
- immutable/read-only child view models;
- accessible summary text that includes unavailable state without relying on
  color.

`MainWindowViewModel` constructor is:

```csharp
MainWindowViewModel(
    ISensorFixtureLoader loader,
    SensorLoadLimits limits)
```

Expose read-only roots plus:

- selected `FixtureChoice`;
- source/version/node/sensor status;
- `IsLoading`, `HasSnapshot`, `HasError`, and rejection message;
- async methods to load a bundled fixture and an explicit local path.

Maintain one cancellation source for the active request. Starting a newer load
cancels/supersedes the older request. Publish only a successful result from the
currently active request. A failed later load keeps the last good roots/counts
and exposes an unmistakable rejection; an initial failure shows an empty error
state.

Do not persist selection, expansion, recent path, window position, or any
setting.

### Part 2 — Implement read-only source selection

In `FilePickerFixtureSource.cs`, define the small UI adapter interface and its
Avalonia implementation. Use the window's `StorageProvider` to select one
existing `.json` file. Return a local path or null for cancellation. Do not
write, create, rename, remember, or copy files.

Bundled fixture choices are the six names from the spec. Agent C may code their
relative test-output paths, but must not create or edit the fixture files owned
by agent B.

### Part 3 — Build the window

`MainWindow` constructor must match agent A's composition root:

```csharp
MainWindow(
    MainWindowViewModel viewModel,
    IFilePickerFixtureSource filePicker)
```

Requirements:

- Title: `LibreHardwareMonitor Avalonia Fixture Explorer`.
- A persistent visible `Fixture-only, non-shipping` label.
- Bundled fixture selector and `Load fixture` action.
- `Open data.json` action using the read-only picker.
- Status area for source, version, node/sensor counts, loading, and rejection.
- Header columns for name, type, current, minimum, maximum, and stable ID.
- A hierarchical `TreeView`; use a grid in each item template rather than
  adding a TreeDataGrid package.
- Original child order; no sort, filter, drag, edit, save, refresh, endpoint,
  or control action.
- Explicit empty/error/loading presentation.
- Automation names for source controls, status, tree, and value columns.
- Keyboard focus order and native TreeView expand/collapse behavior.
- No decorative color-only status cue and no permanent animation/timer.

Keep code-behind limited to view-only picker/event glue. Loading/state belongs
to the view model.

### Part 4 — Add isolated headless tests

Configure one headless test application and xUnit v3
`[AvaloniaTestApplication]` builder in the owned test files; do not change
project or global runner files.

Using a deterministic fake `ISensorFixtureLoader`, test:

- initial empty state;
- successful roots, counts, source, version, and stable IDs;
- unavailable display strings;
- loading transition;
- later rejection retains last good roots and exposes error;
- initial rejection is empty/error;
- a slow old request cannot overwrite a newer result;
- bundled fixture selection forwards the expected path/name;
- window title and non-shipping label;
- accessible control names and hierarchy headers;
- keyboard focus plus expand/collapse of a sample tree.

Avoid brittle pixel, font, timing, or platform-decoration assertions.

---

## Constraints

- Own only the listed UI/view-model/headless-test files.
- Compile only against agent A's frozen contracts; use fake loaders in tests.
- Do not edit projects, package versions, parser files, fixtures, docs, or
  tracker.
- Do not import WinForms, WPF, `LibreHardwareMonitorLib`, settings, HTTP, or
  runtime paths.
- No network, telemetry, logging, persistence, background polling, or history.

---

## Verification

After agent A's scaffold is present, run:

```powershell
dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
dotnet build LibreHardwareMonitor.Avalonia.Spike\LibreHardwareMonitor.Avalonia.Spike.csproj -c Release --no-restore
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release -- --filter-class MainWindowViewModelTests
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release -- --filter-class MainWindowHeadlessTests
```

If the runner filter differs, run the complete isolated test executable.

The project-configured full baseline remains:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
```

Do not cut a candidate from the parallel worktree. Agent D or the manager runs
the baseline after integration and a clean commit.

---

## Do NOT

- Do not edit agent A's composition root or contracts.
- Do not depend on agent B's concrete parser or fixture contents.
- Do not add a live URL, HTTP client, retry placeholder, hardware button,
  settings key, task, package, or launcher.
- Do not edit files owned by agents A, B, or D.
- Do not approve, execute, promote, deploy, or launch the campaign.

## Post-completion

Do not edit `live-tracker.md`; agent D exclusively owns it. Return this row in
your completion result:

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-003 | Done | agent-c | Avalonia shell and headless UI | Fixture-only Avalonia explorer | Summarize UI states, accessibility/keyboard coverage, and headless test totals. |
