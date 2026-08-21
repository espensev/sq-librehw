# Agent Task — Rewire Spike Configuration

**Scope:** Rewire `.codex/skills/project.toml` (module paths, smart-test
mappings, gate commands, conflict zones, cross-cutting paths), move
`Test-AvaloniaSpike.ps1` into the experiment root and fix its repository-root
resolution, and update the six Avalonia `bin`/`obj` paths in
`Clear-LhmRepositoryBuildOutputs.ps1`.

**Depends on:** Agent J (the projects must be moved first)

**Output files:** `.codex/skills/project.toml`,
`experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1` (moved from
`scripts/`), `scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1`.

## Exit Criteria

- `project.toml` points every spike module, smart-test mapping, gate command,
  conflict-zone entry, and cross-cutting path at the new
  `experiments/avalonia-fixture-explorer/` location, and `plan preflight --json`
  reports ready with zero errors.
- `Test-AvaloniaSpike.ps1` lives under the experiment root and resolves the
  repository root and spike paths correctly so the 75-test gate still passes.
- `Clear-LhmRepositoryBuildOutputs.ps1` removes the six Avalonia `bin`/`obj`
  trees at their new location and no longer references the old root paths.
- `eng/ci/Invoke-LhmGates.ps1` is byte-identical (configuration-only rewiring).

## Task

### Part 1 — `.codex/skills/project.toml`

Prefix every spike location with `experiments/avalonia-fixture-explorer/`. Use
forward slashes for module/mapping globs; keep the existing `\\` escaping in
command strings.

- `[modules]`: `avalonia-core`, `avalonia-app`, `avalonia-tests` →
  `experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Core/`,
  `experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike/`, and
  `experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Tests/`
  plus `experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1`.
- `[commands]`: `test_full` script path →
  `experiments\\avalonia-fixture-explorer\\Test-AvaloniaSpike.ps1`.
- `[smart-test.mappings]`: the two `LibreHardwareMonitor.Avalonia.Spike*` glob
  keys → prefixed with `experiments/avalonia-fixture-explorer/`.
- `[smart-test.cross-cutting]`: the `.slnx` entry → prefixed.
- `[build-gate.avalonia-spike]`: `build` →
  `dotnet build experiments\\avalonia-fixture-explorer\\LibreHardwareMonitor.Avalonia.Spike.slnx -c Release`;
  `test` → `...-File experiments\\avalonia-fixture-explorer\\Test-AvaloniaSpike.ps1`.
- `[conflict-zones]`: the `central package and project graph` entry's `.slnx`
  path → prefixed.

Do not touch `[commands]` `compile`/`build`/`test`/`test_fast`, other gates, or
non-spike entries.

### Part 2 — move and fix `Test-AvaloniaSpike.ps1`

`git mv scripts/Test-AvaloniaSpike.ps1 experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1`,
then fix four references (the script now lives two levels below the repo root):

- `$repositoryRoot` resolution: `Join-Path $PSScriptRoot '..'` → `'..\..'`.
- `$spikeSolution` →
  `'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.slnx'`.
- `$spikeTestProject` →
  `'experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj'`.
- the three `$spikeRoots` entries → prefixed with
  `experiments\avalonia-fixture-explorer\`.

`$shippingSolution`, the WinForms build paths, and the existing-tests path stay
unchanged (they are still at the repo root).

### Part 3 — `Clear-LhmRepositoryBuildOutputs.ps1`

Prefix the six Avalonia `$relativeOutputRoots` entries
(`LibreHardwareMonitor.Avalonia.Spike\bin`, `...\obj`, `.Core\bin/obj`,
`.Tests\bin/obj`) with `experiments\avalonia-fixture-explorer\`.

## Constraints

- Do not edit `eng/ci/Invoke-LhmGates.ps1` — it must stay byte-identical.
- Do not edit docs or `live-tracker.md` — agents m and n own those.
- Do not change gate classification or the deny-list.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python scripts\task_manager.py plan preflight --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File experiments\avalonia-fixture-explorer\Test-AvaloniaSpike.ps1
git diff --stat -- eng/ci/Invoke-LhmGates.ps1
```

`-List` must show the `avalonia-spike` gate resolving to the new paths; the
runner diff must be empty; the spike gate must still report 75/75.

## Do NOT

- Do not run `git clean -fdX`.
- Do not push.
- Do not edit `live-tracker.md`; return your tracker row text in your result.
