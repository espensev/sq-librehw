# Agent Task — Spike Move Docs

**Scope:** Update current path claims in `docs/README.md` and the Avalonia
feature spec to the new location, and explicitly update or retire the structural
discovery document so no current document references a stale path.

**Depends on:** Agent J, Agent K

**Output files:** `docs/README.md`,
`docs/feature-avalonia-fixture-sensor-explorer.md`,
`docs/discovery-librehw-structural-audit.md`.

## Exit Criteria

- `docs/README.md` source map and verification commands point at
  `experiments/avalonia-fixture-explorer/`.
- The Avalonia feature spec's path claims and verification commands are updated.
- The discovery audit document is either updated to the new paths or explicitly
  retired with a one-line note that Git history preserves its point-in-time
  detail; it is not left half-true.

## Task

1. In `docs/README.md`, update the source map entry and any verification command
   that names the old spike solution/script location to the new
   `experiments/avalonia-fixture-explorer/` paths.
2. In `docs/feature-avalonia-fixture-sensor-explorer.md`, update every current
   path claim (project/solution/script locations and verification commands) to
   the new location. Keep the historical candidate evidence (IDs, SHAs,
   inventories) intact; only the *current path* claims move.
3. For `docs/discovery-librehw-structural-audit.md`: decide explicitly. It
   describes the pre-move structure. Either update its path claims to the new
   layout, or retire it with a short note that its findings are folded into the
   roadmap/backlog and Git history preserves the detail. Do not leave it
   half-true.

Verify your edits do not introduce stale references by relying on agent l's
gate; if the gate flags one of your files, fix the claim (do not allow-list a
current doc).

## Constraints

- Do not rewrite immutable evidence: completed agent specs and rendered campaign
  documents stay byte-for-byte unchanged.
- Do not edit `docs/campaign-history.md`, `live-tracker.md`,
  `docs/refactor-roadmap.md`, or `docs/campaign-backlog.md` — agent n owns those.
- Do not edit `.codex/skills/project.toml` or scripts — agent k owns those.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
git diff --check
```

The stale-reference gate (agent l) must pass against your updated docs.

## Do NOT

- Do not run `git clean -fdX` or push.
- Return your tracker row text in your result; do not edit `live-tracker.md`.
