# Agent Task — Move Candidate Ops

**Scope:** Move `ops/release/` (host-neutral candidate creation) to `ops/candidate/`
with `git mv`, preserving history, and update the synthetic `ops\release` mirror in
`Test-LhmReleaseSystem.ps1` to `ops\candidate`.

**Depends on:** none (group 0)

**Output files:** `ops/release/` → `ops/candidate/` (4 files: `LhmRelease.Common.ps1`,
`New-LhmRelease.ps1`, `Test-LhmReleaseCandidate.ps1`, `Test-LhmReleaseSystem.ps1`).

## Exit Criteria

- `ops/release/` is relocated to `ops/candidate/` via `git mv` so `git log --follow`
  resolves through the move.
- `New-LhmRelease.ps1`'s repo-root resolution (`$PSScriptRoot '..\..'`) still works —
  the move is depth-preserving (both `ops/release` and `ops/candidate` are one level
  deep), so it should need no edit; verify by building a candidate.
- `Test-LhmReleaseSystem.ps1` line 187 synthetic mirror updated from `ops\release` to
  `ops\candidate` so the test mirrors the real layout.

## Task

1. `mkdir ops/candidate`, then `git mv ops/release/LhmRelease.Common.ps1`,
   `New-LhmRelease.ps1`, `Test-LhmReleaseCandidate.ps1`, `Test-LhmReleaseSystem.ps1`
   into `ops/candidate/` (or `git mv` the directory if git accepts it).
2. In `ops/candidate/Test-LhmReleaseSystem.ps1`, change line 187
   `$orchestrationScripts = Join-Path $orchestrationRepository 'ops\release'` to
   `'ops\candidate'`.
3. Confirm `New-LhmRelease.ps1` line 15 (`$repositoryRoot = ... $PSScriptRoot '..\..'`)
   still resolves to the repo root from the new one-level-deep location (no edit
   expected).

## Constraints

- Do not edit `.codex/skills/project.toml`, the spike gate, docs, or scripts outside
  `ops/candidate/` — agents r and t own those.
- Do not touch inherited product roots, the shipping solution, or `Directory.Packages.props`.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
git status --short
git log --follow --oneline -1 -- ops/candidate/New-LhmRelease.ps1
# build a non-promotable candidate to prove the moved scripts run (do NOT promote):
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\New-LhmRelease.ps1 -AllowDirty -ReleaseRoot <external-release-root>
```

(Do not push. Do not edit `live-tracker.md`; return your tracker row text in your result.)
