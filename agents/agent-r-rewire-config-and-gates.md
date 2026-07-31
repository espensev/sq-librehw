# Agent Task — Rewire Config and Gates

**Scope:** Rewire `.codex/skills/project.toml` modules/mappings/gates/conflict-zones to
the new ops paths, rewire the Avalonia spike gate's `ops\release` reference to
`ops\candidate`, and extend the permanent stale-reference gate
(`Test-NoStaleReferences.ps1`) move-map and allow-list for the ops moves.

**Depends on:** Agent O, Agent P (both moves must be done)

**Output files:** `.codex/skills/project.toml`,
`experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1`,
`eng/ci/tests/Test-NoStaleReferences.ps1`.

## Exit Criteria

- `project.toml` modules (`candidate-ops`, `snd-desk-deploy-ops`), smart-test mappings,
  the `release-candidate` and `snd-desk-local-release-fixture` build gates, and conflict
  zones point at the new paths; `plan preflight --json` is ready with zero errors.
- `eng/ci/Invoke-LhmGates.ps1` is byte-identical (configuration-only rewiring).
- The spike gate's release-system reference points at `ops\candidate`.
- The stale-reference gate is extended to cover the ops moves and passes.

## Task

### Part 1 — `.codex/skills/project.toml`

- `[modules]`: `candidate-ops = ["ops/candidate/"]`;
  `snd-desk-deploy-ops = ["ops/deploy/snd-desk/"]` (drop `scripts/local-release/` — it
  no longer exists).
- `[smart-test.mappings]`:
  - `"ops/release/*.ps1"` → `"ops/candidate/*.ps1"` = `["ops/candidate/Test-LhmReleaseSystem.ps1"]`
  - `"ops/local-release/*.ps1"` and `"scripts/local-release/*.ps1"` → a single
    `"ops/deploy/snd-desk/*.ps1"` = `["ops/deploy/snd-desk/Test-LhmLocalRelease.ps1"]`
- `[build-gate.release-candidate]`: every `ops\release\*` path → `ops\candidate\*`.
- `[build-gate.snd-desk-local-release-fixture]`: `test` script path →
  `ops\deploy\snd-desk\Test-LhmLocalRelease.ps1`.
- `[conflict-zones]`: any entry naming `ops/release` or `scripts/local-release` → new
  paths.

### Part 2 — spike gate

- `experiments/avalonia-fixture-explorer/Test-AvaloniaSpike.ps1` line 411:
  `'ops\release\Test-LhmReleaseSystem.ps1'` → `'ops\candidate\Test-LhmReleaseSystem.ps1'`.

### Part 3 — extend the stale-reference gate

- `eng/ci/tests/Test-NoStaleReferences.ps1`: add the ops moves to `$moveMap`
  (`ops/release` → `ops/candidate`, `ops/local-release` → `ops/deploy/snd-desk`,
  `scripts/local-release/Publish-LibreHardwareMonitor.ps1` and
  `Test-LhmLocalRelease.ps1` → `ops/deploy/snd-desk/...`,
  `scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1` → `eng/...`).
- Add `eng/ci/tests/Test-GateRunner.ps1` to `$exemptPatterns` (its deny-test command
  strings deliberately contain non-existent paths; they are synthetic test content, not
  real references).

## Constraints

- Do not edit `eng/ci/Invoke-LhmGates.ps1` — it must stay byte-identical; the deny-list
  is content-based and survives without changes.
- Do not edit docs or `live-tracker.md` — agents t and u own those.
- Do not change gate classification or the deny-list.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python scripts\task_manager.py plan preflight --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -DryRun
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
git diff --stat -- eng/ci/Invoke-LhmGates.ps1
```

`-List`/`-DryRun` must resolve the new paths; the runner diff must be empty; the stale
gate must pass. (Do not push. Return your tracker row text in your result.)
