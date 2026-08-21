# Agent Task — Stale-Reference Gate

**Scope:** Add the permanent stale-current-reference regression
`eng/ci/tests/Test-NoStaleReferences.ps1` with a centralized allow-list for
immutable historical evidence, so later taxonomy moves cannot leave broken
current paths silently.

**Depends on:** Agent J, Agent K (the move and config rewire must be done)

**Output files:** `eng/ci/tests/Test-NoStaleReferences.ps1`.

## Exit Criteria

- `Test-NoStaleReferences.ps1` passes, asserting no tracked configuration or
  current document references a moved path that no longer exists.
- The allow-list is data (arrays), not scattered conditionals, and covers
  completed agent specs and rendered campaign documents.
- `Test-LhmCiGates.ps1` now discovers three test scripts (this is the third).

## Task

Create `eng/ci/tests/Test-NoStaleReferences.ps1` following the
`eng/ci/README.md` test-script convention (self-contained, prints `PASS:`/`FAIL:`
lines, writes nothing outside `$env:TEMP`, exits 0 only when clean). It must:

1. Declare a data-driven **move map** of old root path -> new nested path for the
   five relocated items (the three project dirs, the `.slnx`, and
   `Test-AvaloniaSpike.ps1`).
2. Declare a data-driven **exempt-glob allow-list** for immutable historical
   evidence that is never scanned: `agents/agent-*.md`,
   `docs/campaign-plan-*.md`, `docs/campaign-history.md`, `data/plans/*.json`,
   and this test file itself.
3. **Existence check:** for each move-map pair, assert the new path exists and
   the old path no longer exists.
4. **Config staleness check:** in `.codex/skills/project.toml`, assert there is
   no bare (non-`experiments/avalonia-fixture-explorer/`-prefixed) reference to
   any moved spike path. Use a negative-lookbehind regex over both slash forms.
5. **Unambiguous doc staleness check:** across all non-exempt tracked text files
   (`.toml`, `.md`, `.ps1`, `.yml`), assert none reference the old script
   location `scripts/Test-AvaloniaSpike.ps1` (either slash form).

The dispatcher `Test-LhmCiGates.ps1` auto-discovers every
`eng/ci/tests/Test-*.ps1`, so no registration is needed; creating the file makes
the count three.

## Constraints

- Do not patch configuration if you find a defect; report it to agent k in your
  result. You own only this test file.
- Do not rewrite or allow-list a current document merely to silence it; a real
  stale reference is a failure.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
```

The gate passes; `Test-LhmCiGates.ps1` reports three scripts discovered.

## Do NOT

- Do not edit `project.toml`, docs, or `live-tracker.md`.
- Do not run `git clean -fdX` or push.
- Return your tracker row text in your result.
