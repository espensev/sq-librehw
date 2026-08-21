# Agent Task — Ci Gate Runner Tests

**Scope:** Add the `eng/ci` test dispatcher and the gate-runner regression
suite covering gate classification, the deny-list, selection, and exit codes.

**Depends on:** Agent E

**Output files:** `eng/ci/Test-LhmCiGates.ps1`,
`eng/ci/tests/Test-GateRunner.ps1`

## Exit Criteria

- `eng/ci/Test-LhmCiGates.ps1` discovers every `eng/ci/tests/Test-*.ps1`, runs
  each in a child process of the same PowerShell engine, and reports aggregate
  passed, failed, and skipped counts.
- The dispatcher exits 0 only when no child script failed, and exits non-zero
  when any child script fails.
- The dispatcher is the single command registered as the `ci-gates` build gate,
  so it must succeed with the tests that exist at the time it runs, including
  when Agent G's script is not yet present.
- `eng/ci/tests/Test-GateRunner.ps1` covers all eleven cases in Part 3 below and
  exits non-zero if any assertion fails.
- Every fixture configuration is created under `$env:TEMP` and removed on exit;
  no test writes into the repository.
- The suite proves the deny-list refuses without executing, by asserting that a
  sentinel the denied command would have created does not exist.

---

## Context — read before doing anything

1. `AGENTS.md` — task classification and baseline commands.
2. `agents/agent-e-ci-gate-runner.md` — the frozen parameter surface, exit-code
   contract, JSON summary shape, classification table, deny-list, and the
   test-script convention. Treat it as the interface specification.
3. `eng/ci/Invoke-LhmGates.ps1` — the delivered runner. Read it before writing
   assertions; assert against observable behavior, not internal variables.
4. `eng/ci/README.md` — the documented gate table and exit codes. Any
   disagreement between the README and the runner is a defect to report, not
   something to work around in the tests.
5. `.codex/skills/project.toml` — the real configuration used by the
   whole-configuration cases.
6. `ops/log-management/Test-LhmLogManagement.ps1` and
   `scripts/local-release/Test-LhmLocalRelease.ps1` — existing hand-rolled
   PowerShell test scripts. Match their structure and reporting style rather
   than introducing Pester or another test framework.

---

## Task

### Part 1 — `eng/ci/Test-LhmCiGates.ps1`

A dispatcher, not a test. Parameters:

| Parameter | Type | Behavior |
|---|---|---|
| `-Filter` | `string` | Wildcard applied to the test script base name. Default `*`. |
| `-TestRoot` | `string` | Directory to search. Defaults to `eng/ci/tests` under the resolved repository root. |

Behavior:

- Resolve `$PSScriptRoot` and enumerate `Test-*.ps1` under the test root,
  sorted by name, so ordering is deterministic.
- If the test root is missing or matches no script, print an explicit message
  and exit 0. An empty suite is not a failure; the dispatcher lands before
  Agent G's script exists.
- Run each script as a child process of the **same** engine that is running the
  dispatcher, so the suite behaves identically under Windows PowerShell 5.1 and
  PowerShell 7:

  ```powershell
  $engine = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
  & $engine -NoProfile -ExecutionPolicy Bypass -File $script
  ```

- Parse each child's output for lines beginning `PASS:`, `FAIL:`, and `SKIP:`
  and accumulate the counts. Treat the child's exit code as authoritative for
  pass or fail; use the parsed counts for reporting only.
- Print a per-script line and a final aggregate line with total scripts,
  passed, failed, and skipped assertion counts.
- Exit 1 if any child exited non-zero, otherwise exit 0.

Set `$ErrorActionPreference = 'Stop'` and `Set-StrictMode -Version Latest`.

### Part 2 — Fixture helper

Inside `eng/ci/tests/Test-GateRunner.ps1`, add a helper that writes a minimal
TOML configuration to a fresh directory under `$env:TEMP` and returns its path.
The fixture only needs a `[build-gate.*]` section; the runner reads nothing
else from the configuration. Remove the directory in a `finally` block.

Use a sentinel-file pattern to prove non-execution: give a fixture gate a
command such as

```
powershell.exe -NoProfile -Command "Set-Content -Path <sentinel> -Value ran"
```

and assert the sentinel's absence after a refusal or a fail-fast stop.

### Part 3 — Required cases in `eng/ci/tests/Test-GateRunner.ps1`

1. `-List` against the real `.codex/skills/project.toml` exits 0 and names
   every `[build-gate.*]` present in that file. Derive the expected set by
   parsing the configuration with the same `python -c` + `tomllib` call the
   runner uses, so the assertion cannot go stale when a gate is added.
2. Every gate in `-List` output carries a non-empty classification and reason.
3. A fixture configuration containing `[build-gate.rogue]` exits 2 and the
   output contains `rogue`.
4. A fixture configuration whose *included* gate carries a `test` command
   containing `New-LhmRelease.ps1` exits 3, and the sentinel that command would
   have written does not exist.
5. `-Gate release-candidate -DryRun` against the real configuration prints the
   `Test-LhmReleaseSystem.ps1` command and prints neither `New-LhmRelease` nor
   `-RequireCurrentSource`.
6. `-List` output marks `ci-gates` excluded with a self-reference reason, and
   `-All -DryRun` never emits the `ci-gates` command. This case must pass both
   before and after Agent I registers the gate, so assert it conditionally on
   the gate being present in the configuration and assert the classification
   entry exists either way.
7. `-All -DryRun` against the real configuration emits no command matching any
   pattern in the runner's own `denyList`. Read the patterns from a
   `-List -JsonSummary` file rather than restating them.
8. `-Gate does-not-exist` exits 2 with a message naming the gate.
9. `-JsonSummary` writes parseable JSON with `schemaVersion` equal to 1, a
   non-empty `denyList`, and a `gates` array whose names match the requested
   gates.
10. A fixture gate whose command exits 1 makes the runner exit 1 and records
    that gate as `Failed` in the JSON summary.
11. `-FailFast` with two failing fixture gates stops after the first: the
    second gate's sentinel does not exist and its JSON status is not `Passed`.

Each case prints exactly one `PASS:` or `FAIL:` line naming the case.

### Part 4 — Isolation

- Every fixture path lives under a per-run directory created with
  `New-Item -ItemType Directory` under `$env:TEMP`, removed in `finally`.
- No case invokes a real gate command from the repository configuration except
  in `-DryRun` or `-List` mode.
- No case runs `-All` without `-DryRun`. The full sweep is Agent E's
  verification, not a unit test.

---

## Constraints

- Do not modify `eng/ci/Invoke-LhmGates.ps1` or `eng/ci/README.md`. Agent E
  owns them. Report any runner defect in your result payload instead of
  patching it.
- Do not create `eng/ci/tests/Test-Workflow.ps1`. Agent G owns it.
- Do not modify `.codex/skills/project.toml`. Agent I owns it.
- Do not edit `live-tracker.md`. Return your tracker row text in your result
  payload.
- Do not add Pester, xUnit, or any test package. These are hand-rolled scripts,
  matching the existing `ops/**/Test-*.ps1` convention.
- Do not weaken a case to make it pass. A failing assertion against the runner
  is a finding for Agent E, reported through your result payload.

---

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-GateRunner.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
pwsh -NoProfile -File eng\ci\Test-LhmCiGates.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1 -Filter Test-GateRunner
git status --porcelain
git clean -ndX
```

Run the dispatcher under both engines. `git status --porcelain` must show only
your two new files, `$env:TEMP` must contain no leftover fixture directory, and
`git clean -ndX` must list only `data/tasks.json` and `data/analysis-cache.json`
— no `__pycache__`. Your cases invoke Python for the `tomllib` fixture parse, so
set `PYTHONDONTWRITEBYTECODE` before running them, exactly as the runner does
for its gate children.

---

## Do NOT

- Do not let the dispatcher fail on an empty or missing test directory.
- Do not run the real `-All` sweep from a test case.
- Do not restate the deny-list patterns; read them from the runner's JSON
  summary so the two cannot drift.
- Do not assume PowerShell 7 features. The dispatcher and suite must run under
  Windows PowerShell 5.1 as well.
- Do not leave a fixture directory behind on a failing path; clean up in
  `finally`.
