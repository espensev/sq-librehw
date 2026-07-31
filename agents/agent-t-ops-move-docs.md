# Agent Task — Ops Move Docs

**Scope:** Update current path claims across docs and `eng/ci` docs to the new
`ops/candidate`, `ops/deploy/snd-desk`, and `eng/` layout, and explicitly update or
retire stale ops path claims in the structural discovery audit.

**Depends on:** Agent O, Agent P, Agent R (moves and config rewiring must be done)

**Output files:** `docs/README.md`, `docs/campaign-playbook.md`,
`docs/feature-local-release-system.md`, `docs/feature-release-packaging.md`,
`docs/feature-avalonia-fixture-sensor-explorer.md`,
`docs/repository-build-output-cleanup.md`,
`docs/discovery-librehw-structural-audit.md`, `eng/ci/README.md`.

## Exit Criteria

- Every current doc path claim and verification command points at the new ops/eng layout.
- `docs/discovery-librehw-structural-audit.md` stale ops path claims are updated or the
  doc is retired with a note; not left half-true.
- The extended stale-reference gate (agent R) passes against the updated docs.

## Task

Update path claims to the new locations (single-backslash doc convention):
- `ops/release` → `ops/candidate`
- `ops/local-release` → `ops/deploy/snd-desk`
- `scripts/local-release/Publish-LibreHardwareMonitor.ps1` and
  `Test-LhmLocalRelease.ps1` → `ops/deploy/snd-desk/`
- `scripts/local-release/Clear-LhmRepositoryBuildOutputs.ps1` → `eng/`

Known references to update (verified by `git grep`):
- `docs/README.md`: lines 33, 75, 311, 341–342, 359–365
- `docs/campaign-playbook.md`: lines 41, 207–208, 242
- `docs/feature-local-release-system.md`: lines 402, 405, 409, 413, 419, 422
- `docs/feature-release-packaging.md`: lines 45–48, 295–300
- `docs/feature-avalonia-fixture-sensor-explorer.md`: lines 264, 313–315 (candidate paths)
- `docs/repository-build-output-cleanup.md`: lines 86–87
- `eng/ci/README.md`: lines 72, 145–146
- `docs/discovery-librehw-structural-audit.md`: lines 148–150, 385 — update the path
  claims, or retire with a one-line note that Git history preserves point-in-time detail.

Keep historical candidate evidence (IDs, SHAs, inventories) intact; only the *current
path* claims move. Do not rewrite immutable evidence (completed agent specs, rendered
campaign docs).

## Constraints

- Do not edit `docs/campaign-history.md`, `live-tracker.md`,
  `docs/refactor-roadmap.md`, or `docs/campaign-backlog.md` — agent u owns those.
- Do not edit `.codex/skills/project.toml` or scripts — agents r/p own those.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
git diff --check
```

The stale-reference gate must pass. (Do not push. Return your tracker row text in your
result; do not edit `live-tracker.md`.)
