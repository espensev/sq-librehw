# Agent Task — Rewire Config and Gates (Plan-006, Agent Z)

**Scope:** Make every configuration, ops script, cleanup path, and permanent gate agree
with agent y's four-suite layout. `eng/ci/Invoke-LhmGates.ps1` must stay **byte-identical**
— this is a configuration-only rewiring, the gate set does not change.

**Depends on:** y (the layout below is frozen by y's spec; verify it on disk before starting).

---

## Known references to rewire (verified line numbers at campaign start)

| File | Line | Current | Change to |
|---|---|---|---|
| `.codex/skills/project.toml` | 14–15 | `[commands] test` / `test_fast` → old csproj | `dotnet test LibreHardwareMonitor.Tests\\LibreHardwareMonitor.Tests.slnf -p:Platform=x64` |
| `.codex/skills/project.toml` | 27 | `winforms-tests = ["LibreHardwareMonitor.Tests/"]` | rename key to `dotnet-tests`, glob unchanged (covers the nested suites) |
| `.codex/skills/project.toml` | 42 | conflict zone names `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs, .../data.golden.json` | Contracts paths |
| `.codex/skills/project.toml` | 73 | `"LibreHardwareMonitorLib/**/*.cs"` → all tests | `[".../LibreHardwareMonitor.Tests.Library/**/*.cs", ".../LibreHardwareMonitor.Tests.Contracts/**/*.cs"]` |
| `.codex/skills/project.toml` | 74 | `HttpServer.cs` → `DataJsonGoldenTests.cs` + webtests | `[".../LibreHardwareMonitor.Tests.Contracts/**/*.cs", "webtests/*.js"]` |
| `.codex/skills/project.toml` | new | (absent) | add `"LibreHardwareMonitor.Windows.Forms/**/*.cs"` → Application + Contracts suite globs |
| `.codex/skills/project.toml` | 93 | `[build-gate.winforms-net10].test` → old csproj | the slnf command above |
| `ops/candidate/LhmRelease.Common.ps1` | 201 | label `'dotnet test LibreHardwareMonitor.Tests (x64)'` | label reflecting the slnf run |
| `ops/candidate/New-LhmRelease.ps1` | 214 | old csproj path | `LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf` |
| `ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1` | 73 | `$testProject = ...old csproj` | the slnf path |
| `experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1` | 337 | old csproj path | the appropriate new path(s) — read the surrounding logic first and preserve its intent |
| `eng/Clear-LhmRepositoryBuildOutputs.ps1` | 152–153 | `LibreHardwareMonitor.Tests\bin`, `...\obj` | the eight per-suite `bin`/`obj` paths (four suites × two); the list is deliberately explicit |

TOML strings: use literal (single-quoted) strings or escaped `\\` for Windows paths,
matching the existing file style exactly.

## `eng/ci/tests/Test-NoStaleReferences.ps1` — extend

1. **Move-map:** add one `@{ Old = ...; New = ... }` entry per relocated file — all 27
   `.cs` files plus `data.golden.json` (28 entries). Per-file is mandatory: the parent
   directory `LibreHardwareMonitor.Tests/` survives, so a directory-level entry would lie.
2. **Dissolved path:** the old `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.csproj`
   must not exist; follow the existing dissolved-path idiom.
3. **New textual block (critical):** the default scan set is `\.(toml|md|ps1|yml)$` — it
   cannot see `.csproj`, `.sln`, `.slnf`, or `.json`, which is exactly where stale test
   paths would hide after this campaign. Add a purpose-built block that scans a widened
   tracked set (add `csproj|sln|slnf|json`; the existing `$exemptPatterns` already exempt
   `data/plans/*.json` and the immutable evidence files) for:
   - the old csproj path (both slash styles);
   - any flat old-file reference `LibreHardwareMonitor.Tests[/\]<file>.cs|.json` — i.e. a
     path directly under `LibreHardwareMonitor.Tests/` whose next segment does **not**
     begin with `LibreHardwareMonitor.Tests.` (the four suite dirs and the slnf all do).
   Bare-directory references (`LibreHardwareMonitor.Tests/` as a module glob) stay legal.
4. Do not remove or weaken any existing block. The script self-exempts, so your new
   patterns may appear inside it.

## New permanent gate — `eng/ci/tests/Test-SuiteBoundaries.ps1`

Auto-discovered by `Test-LhmCiGates.ps1` (self-contained, parameter-free, `PASS:`/`FAIL:`
per assertion, exit 0 only when clean, follow the conventions of the existing three).
Assert at minimum:

1. `LibreHardwareMonitor.Tests.slnf` parses as JSON and its project list is exactly the
   three deterministic suite csproj paths (Library, Application, Contracts).
2. The Attended csproj exists on disk, is present in `LibreHardwareMonitor.sln`, and is
   referenced by neither the slnf nor any `[build-gate.*]` command in
   `.codex/skills/project.toml` (parse the TOML the way `Test-GateRunner.ps1` does).
3. The `[build-gate.winforms-net10]` `test` command references the slnf.
4. `data.golden.json` exists in the same directory as `DataJsonGoldenTests.cs` (the
   `[CallerFilePath]` adjacency the golden test depends on).

## `eng/ci/README.md`

Update the winforms-net10 gate description to the slnf command and document the Attended
exclusion contract (in the sln, outside the slnf and every gate, enforced by
Test-SuiteBoundaries). Remember this README's gate table is documentation only — nothing
asserts it, so update it by hand and say what changed in your payload.

## Verification

```powershell
git diff --stat eng/ci/Invoke-LhmGates.ps1           # MUST be empty (byte-identical)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-SuiteBoundaries.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1   # discovers 4 scripts
python scripts\task_manager.py plan preflight --json  # ready, zero errors ($env:PYTHONDONTWRITEBYTECODE = '1' first)
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf     # lists the new suite paths, nothing unexpected
```

Also run both PowerShell engines for the two `eng/ci/tests` scripts where the existing
suite does (`powershell.exe` and `pwsh`) if the dispatcher conventions require it.

## Exit Criteria

- Every reference in the rewire table above points at the new layout; no tracked current
  file references the old csproj path.
- `eng/ci/Invoke-LhmGates.ps1` is byte-identical (`git diff --stat` empty).
- `Test-NoStaleReferences.ps1` carries the 28 per-file move-map entries, the
  dissolved-csproj check, and the widened purpose-built textual block, and passes.
- `Test-SuiteBoundaries.ps1` exists with the four required assertions and passes; the CI
  dispatcher discovers four test scripts and passes.
- `eng/Clear-LhmRepositoryBuildOutputs.ps1` lists the eight per-suite `bin`/`obj` paths and
  its `-WhatIf` output shows the new suite outputs and nothing unexpected.
- `plan preflight --json` reports ready with zero errors.
- `eng/ci/README.md` documents the slnf test command and the Attended exclusion contract.

## Do NOT

- Edit `eng/ci/Invoke-LhmGates.ps1`, any file under `LibreHardwareMonitor.Tests/`, the sln,
  or the product csproj files (agent y owns those; report defects in your payload).
- Add, remove, or reclassify a gate.
- Use `Set-Content -Encoding UTF8` for any fixture (BOM breaks `tomllib`); use
  `[System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding($false)))`.
- Run `python scripts/task_manager.py merge`.
- Write to `live-tracker.md`. **Single-writer override:** agent ab owns the tracker; return
  your row text (ID `SUITEWIRE-001`, owner agent-z) in your result payload.

## Result payload

Return: your tracker row, the byte-identical proof for the runner, each verification
result, the exact list of rewired references, and any defect found outside your ownership.
