# Agent Task — Move Spike Projects

**Scope:** Move the three Avalonia spike projects and the `.slnx` into
`experiments/avalonia-fixture-explorer/` using `git mv`, preserving history and
the cluster's internal relative references.

**Depends on:** none (group 0)

**Output files:** the three spike project directories and the root
`LibreHardwareMonitor.Avalonia.Spike.slnx`, relocated under
`experiments/avalonia-fixture-explorer/`.

## Exit Criteria

- The three project directories and the `.slnx` live under
  `experiments/avalonia-fixture-explorer/`, moved with `git mv` so
  `git log --follow` resolves through the move.
- `dotnet build` of the moved `.slnx` succeeds in Release from a clean output
  state.
- Project, assembly, and namespace names are unchanged; only the location
  changes.

## Task

1. Create the target directory `experiments/avalonia-fixture-explorer/`.
2. `git mv` each of the four items into it:
   - `LibreHardwareMonitor.Avalonia.Spike.Core`
   - `LibreHardwareMonitor.Avalonia.Spike`
   - `LibreHardwareMonitor.Avalonia.Spike.Tests`
   - `LibreHardwareMonitor.Avalonia.Spike.slnx`
3. The cluster's internal relative references are preserved by moving all four
   items into the same new parent, so the `.slnx` `<Project Path=...>` entries,
   the `.csproj` `<ProjectReference Include="..\...">` entries, and the Spike
   app's `<Content Include="..\...Fixtures\*.json">` entry all still resolve.
   Do NOT edit these paths unless the build proves otherwise; if the build fails
   on path resolution, correct the path to be relative to the new location.
4. `git mv` carries untracked `bin`/`obj` along on Windows, so after the move
   delete any carried-over `bin`/`obj` under the moved project directories (they
   are gitignored and disposable) to guarantee a clean output state.
5. Build the moved solution clean.

## Constraints

- Do not rename projects, assemblies, namespaces, or the `.slnx`.
- Do not edit `.codex/skills/project.toml`, `scripts/**`, or `docs/**` — agents
  k and m own those surfaces.
- Do not move or touch any inherited product root (`Aga.Controls`,
  `LibreHardwareMonitorLib`, `LibreHardwareMonitor.Windows.Forms`,
  `LibreHardwareMonitor.sln`, `Directory.Packages.props`, `global.json`).
- Do not move `Test-AvaloniaSpike.ps1` — agent k owns that move.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
git status --short
git log --follow --oneline -- experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Core/LibreHardwareMonitor.Avalonia.Spike.Core.csproj
dotnet build experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.slnx -c Release
git diff --stat -- Aga.Controls LibreHardwareMonitorLib LibreHardwareMonitor.Windows.Forms LibreHardwareMonitor.sln Directory.Packages.props global.json
```

The product-root `git diff --stat` must print nothing.

## Do NOT

- Do not run `git clean -fdX`.
- Do not push.
- Do not touch the live runtime, scheduled tasks, release store, rollback store,
  or log archive.
- Do not edit `live-tracker.md`; return your tracker row text in your result.
