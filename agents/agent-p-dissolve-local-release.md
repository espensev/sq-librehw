# Agent Task — Dissolve Local Release

**Scope:** Dissolve `scripts/local-release/` and relocate `ops/local-release/`: move
`ops/local-release/` plus `scripts/local-release/{Publish,Test}` into
`ops/deploy/snd-desk/`, move `Clear-LhmRepositoryBuildOutputs.ps1` into `eng/`, and fix
every internal `$PSScriptRoot`/repository-root/cleanup reference broken by the split.

**Depends on:** none (group 0)

**Output files:** `ops/local-release/` (7 files) → `ops/deploy/snd-desk/`;
`scripts/local-release/Publish-LibreHardwareMonitor.ps1` and
`Test-LhmLocalRelease.ps1` → `ops/deploy/snd-desk/`;
`scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1` → `eng/`;
`scripts/local-release/` removed (empty).

## Exit Criteria

- `ops/local-release/` and the two deploy scripts live under `ops/deploy/snd-desk/`;
  `Clear-LhmRepositoryBuildOutputs.ps1` lives under `eng/`; `scripts/local-release/`
  is gone; all via `git mv`.
- All internal references resolve: `Test-LhmLocalRelease.ps1` repo-root depth, `$opsRoot`,
  and cleanup-script reference; `Publish-LibreHardwareMonitor.ps1` repo-root depth and
  Common reference; `Clear-LhmRepositoryBuildOutputs.ps1` repo-root depth.
- The relocated `Test-LhmLocalRelease.ps1` dot-sources `LhmLocalRelease.Common.ps1`
  successfully (co-located).

## Task

1. `mkdir ops/deploy/snd-desk` (create `ops/deploy` then `ops/deploy/snd-desk`).
2. `git mv` all 7 `ops/local-release/*.ps1` into `ops/deploy/snd-desk/`.
3. `git mv scripts/local-release/Publish-LibreHardwareMonitor.ps1 ops/deploy/snd-desk/`.
4. `git mv scripts/local-release/Test-LhmLocalRelease.ps1 ops/deploy/snd-desk/`.
5. `git mv scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1 eng/`.
6. Remove the now-empty `scripts/local-release/` directory.
7. Fix `ops/deploy/snd-desk/Test-LhmLocalRelease.ps1`:
   - line 7: `$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')` →
     `'..\..\..'` (now 3 levels deep).
   - line 8: `$opsRoot = Join-Path $repositoryRoot 'ops\local-release'` → `$opsRoot = $PSScriptRoot`
     (the deploy scripts are now co-located with the test).
   - line 13: `$cleanupScript = Join-Path $PSScriptRoot 'Clear-LhmRepositoryBuildOutputs.ps1'` →
     `$cleanupScript = Join-Path $repositoryRoot 'eng\Clear-LhmRepositoryBuildOutputs.ps1'`
     (the cleanup tool moved to `eng/`).
   - line 257: `$allScripts = @(Get-ChildItem -LiteralPath $opsRoot, $PSScriptRoot ...)` —
     since `$opsRoot` is now `$PSScriptRoot`, simplify to scan `$PSScriptRoot` only.
8. Fix `ops/deploy/snd-desk/Publish-LibreHardwareMonitor.ps1`:
   - line 13: `$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')` → `'..\..\..'`.
   - line 14: `$commonScript = Join-Path $repositoryRoot 'ops\local-release\LhmLocalRelease.Common.ps1'` →
     `$commonScript = Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1'` (now co-located).
9. Fix `eng/Clear-LhmRepositoryBuildOutputs.ps1`:
   - the default `$RepositoryRoot` (around line 102): `Join-Path $PSScriptRoot '..\..'` →
     `Join-Path $PSScriptRoot '..'` (now 1 level deep under `eng/`).

## Constraints

- Do not edit `.codex/skills/project.toml`, the spike gate, docs, or the stale-reference
  gate — agents r and t own those.
- Do not change the fail-closed/machine-identity logic in `LhmLocalRelease.Common.ps1`;
  only location changes.
- Do not touch `ops/log-management/` or inherited product roots.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
git status --short
git log --follow --oneline -1 -- ops/deploy/snd-desk/LhmLocalRelease.Common.ps1
# dot-source check: the fixture should parse and reach the fail-closed guards
pwsh -NoProfile -ExecutionPolicy Bypass -File ops\deploy\snd-desk\Test-LhmLocalRelease.ps1
# cleanup tool still resolves repo root:
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
```

(Use `pwsh` for the fixture — the configured gate engine is blocked by the pre-existing
Get-FileHash defect. Do not push. Return your tracker row text in your result.)
