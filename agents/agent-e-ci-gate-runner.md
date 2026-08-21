# Agent Task — Ci Gate Runner

**Scope:** Add the non-deploying gate runner that executes named
`project.toml` build gates and refuses every deploying or host-mutating
command.

**Depends on:** none

**Output files:** `eng/ci/Invoke-LhmGates.ps1`, `eng/ci/README.md`

## Exit Criteria

- `-List` prints every `[build-gate.*]` name found in
  `.codex/skills/project.toml` with its classification and reason, and exits 0.
- Every gate present in the configuration is explicitly classified; an
  unclassified gate name exits 2 instead of being silently skipped or run.
- Gate commands are read from the configuration at run time. The runner
  hard-codes no gate command string.
- Any resolved command matching the deny-list is refused before execution and
  exits 3.
- `-All` runs only the included key set, reports a per-gate result table, and
  exits 1 if any gate fails.
- `-JsonSummary` writes a schema-versioned JSON summary that also carries the
  runner's own deny-list patterns.
- The runner never elevates, never starts or stops a process, service, or
  scheduled task, and writes no file outside `-JsonSummary`.
- The runner sets `PYTHONDONTWRITEBYTECODE=1` in its own process so Python
  gates leave no `__pycache__` directory in the repository, and sets no other
  environment variable.
- `eng/ci/README.md` documents the included gates, every excluded gate or key
  with its reason, the deny-list rationale, the exit codes, and the unpushed
  `origin` limitation.

---

## Context — read before doing anything

1. `AGENTS.md` — task classification, spec-first rule, and baseline commands.
2. `docs/campaign-plan-002-non-deploying-ci-gates.md` — ownership, dependency
   groups, behavioral invariants, and complete-campaign exit criteria.
3. `docs/refactor-roadmap.md` — Phase 1 scope and the non-negotiable contracts,
   especially that `ops/local-release` is SND-DESK-only and that no phase may
   treat a source build or candidate as a live promotion.
4. `docs/architecture/campaign-control-plane.md` — the safety rules this runner
   mechanizes, in particular that generic campaign verification must not
   deploy, promote, or create external release candidates, and that campaign
   tools must never alter tasks or mutate live monitoring state.
5. `.codex/skills/project.toml` — read the whole `[build-gate.*]` section. These
   nine gate keys across eight gates are the complete input surface:
   `winforms-net10` (`build`, `test`), `winforms-net472` (`build`),
   `avalonia-spike` (`build`, `test`), `web-dashboard` (`verify`, `test`),
   `log-management` (`test`), `campaign-control` (`verify`, `test`),
   `release-candidate` (`build`, `verify`, `test`), and
   `snd-desk-local-release-fixture` (`test`). Empty string values mean "no
   command for this key" and must be skipped, not run as an empty command.
6. `ops/release/New-LhmRelease.ps1` and `ops/release/Test-LhmReleaseCandidate.ps1`
   — read enough of the parameter blocks to confirm why the first creates an
   external candidate and why `-RequireCurrentSource` is a promotion check.
7. `ops/log-management/Test-LhmLogManagement.ps1` and
   `scripts/local-release/Test-LhmLocalRelease.ps1` — confirm both are
   fixture-only and use isolated temporary roots before classifying them as
   included.
8. `docs/README.md` — the current SND-HOST path map. The runner must not read
   or write any of the live, release, rollback, or archive roots listed there.

---

## Task

### Part 1 — `eng/ci/Invoke-LhmGates.ps1` parameter surface

Create the runner with exactly this parameter surface. Agents F and G assert
against it and add nothing to it.

| Parameter | Type | Behavior |
|---|---|---|
| `-Gate` | `string[]` | Run only the named gates, in the order given. |
| `-All` | `switch` | Run every included gate. Default when neither `-Gate` nor `-List` is supplied. |
| `-List` | `switch` | Print the classification table and deny-list, run nothing, exit 0. |
| `-DryRun` | `switch` | Resolve, classify, and deny-list-check every command, print what would run, execute nothing. |
| `-FailFast` | `switch` | Stop at the first failing or refused gate. |
| `-JsonSummary` | `string` | Write the run summary as JSON to this path. |
| `-ConfigPath` | `string` | Configuration to read. Defaults to `.codex/skills/project.toml` under the resolved repository root. |

Resolve the repository root from `$PSScriptRoot` by walking two levels up, and
resolve every relative path against it. Set `$ErrorActionPreference = 'Stop'`
and use `Set-StrictMode -Version Latest`.

### Part 2 — Configuration parsing

Do not write a TOML parser. Convert the configuration through Python, which the
repository already requires for the campaign runtime:

```powershell
$json = & python -c "import json,sys,tomllib; sys.stdout.write(json.dumps(tomllib.load(open(sys.argv[1],'rb'))))" $ConfigPath
```

Fail with exit code 2 and an explicit message if `python` is unavailable, the
configuration file is missing, or the parse fails. Read gate tables from the
`build-gate` key of the parsed object. For each gate, resolve its command keys
in the fixed order `build`, `verify`, `test`, skipping any key whose value is
empty or whitespace.

### Part 3 — Gate classification table

Define a script-scoped classification table. Each entry names the gate, the
keys the runner is allowed to execute, and a reason string. Use exactly these
entries and reasons:

| Gate | Allowed keys | Classification | Reason |
|---|---|---|---|
| `winforms-net10` | `build`, `test` | included | Non-deploying source build and the .NET test suite. |
| `winforms-net472` | `build` | included | Non-deploying second-framework source build. |
| `avalonia-spike` | `build`, `test` | included | Fixture-only, non-shipping spike solution and its runner. |
| `web-dashboard` | `verify`, `test` | included | Static dashboard self-test and Node contract tests. |
| `log-management` | `test` | included | Host-neutral fixture test; installs nothing. |
| `campaign-control` | `verify`, `test` | included | Planning preflight and campaign-runtime regression tests. |
| `release-candidate` | `test` | partially included | Only the release-system fixture runs. |
| `snd-desk-local-release-fixture` | `test` | included | Peer-safe fixture using isolated temporary roots; proves the SND-DESK path fails closed on SND-HOST. |
| `ci-gates` | none | excluded | Self-referential: this gate's test suite invokes this runner, so running it here would recurse. Run `eng/ci/Test-LhmCiGates.ps1` directly. |

Record the two excluded `release-candidate` keys with their own reasons and
surface them in `-List` output:

- `build` — creates an external release candidate; candidate creation belongs
  only to its named release gate and never to CI.
- `verify` — `Test-LhmReleaseCandidate.ps1 -RequirePromotable
  -RequireCurrentSource` is a promotion check, not a verification check. It is
  expected to fail whenever `HEAD` moves ahead of the last candidate, which is
  the normal state after a documentation or tooling commit.

Classification rules:

- A gate present in the configuration but absent from the table is a **hard
  error**: print the gate name and exit 2.
- A gate present in the table but absent from the configuration is a
  **warning** only. `ci-gates` does not exist until Agent I registers it, so a
  missing classified gate must never fail the run.
- `-Gate` naming an unknown gate exits 2.
- `-Gate` naming an excluded gate exits 2 and prints the exclusion reason. It
  must not run.

### Part 4 — Deny-list

Define a script-scoped array of case-insensitive regular expressions and apply
it to every resolved command string immediately before execution, including in
`-DryRun`. Cover at minimum:

- `New-LhmRelease`, `-RequireCurrentSource`, `Publish-LibreHardwareMonitor`
- `Install-`, `Finalize-`, `Restore-Legacy`, `Restore-PreStable`,
  `Restore-LibreHardwareMonitorRelease`, `Start-LibreHardwareMonitor`
- `Clear-LhmRepositoryBuildOutputs`
- `schtasks`, `Register-ScheduledTask`, `Set-ScheduledTask`,
  `Start-ScheduledTask`, `Unregister-ScheduledTask`
- `Start-Process`, `Stop-Process`, `New-Service`, `Set-Service`,
  `Stop-Service`, `Start-Service`
- `git\s+push`, `git\s+clean`, `git\s+reset\s+--hard`
- `reg\s+add`, `HKLM:`, `netsh`

A match refuses the whole gate without executing any of its keys, records the
gate as `Refused` with the matched pattern, and makes the process exit 3. The
check runs against the command string from the configuration, so a later
configuration edit that puts a denied command inside an included gate is caught
at run time rather than at classification time.

### Part 5 — Execution and reporting

Run each allowed key from the repository root with the working directory set
there, capture the exit code and elapsed seconds, and print a result line per
key plus a summary table per gate with status `Passed`, `Failed`, `Refused`, or
`Skipped`.

Set exactly one environment variable, in the runner's **own** process so gate
children inherit it:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
```

Without it the `campaign-control` gate's `python -m unittest discover -s scripts`
command imports `scripts/task_runtime` and `scripts/analysis` and leaves
`__pycache__` directories in the repository on every run. They are ignored by
Git but they violate the repository's keep-generated-output-out-of-source rule,
and the structural baseline recorded zero generated directories. The existing
fast-gate block in `docs/HANDOFF.md` already opens with this line, so this is
established practice rather than a new convention. Set no other environment
variable, and never modify machine or user environment scope.

Exit codes:

- `0` — every attempted gate passed, or `-List`/`-DryRun` completed.
- `1` — at least one gate failed.
- `2` — configuration error: unreadable configuration, missing Python,
  unclassified gate, unknown or excluded `-Gate` name.
- `3` — a command was refused by the deny-list.

When more than one condition applies, report the lowest-numbered failure last
so the refusal exit code 3 wins over a plain failure.

### Part 6 — JSON summary

When `-JsonSummary` is supplied, write UTF-8 JSON with this exact shape:

```json
{
  "schemaVersion": 1,
  "configPath": "<resolved path>",
  "repositoryRoot": "<resolved path>",
  "machineName": "<$env:COMPUTERNAME>",
  "mode": "list | dryrun | run",
  "startedAt": "<ISO 8601>",
  "completedAt": "<ISO 8601>",
  "denyList": ["<regex>", "..."],
  "gates": [
    {
      "name": "winforms-net10",
      "classification": "included",
      "reason": "...",
      "allowedKeys": ["build", "test"],
      "excludedKeys": [{ "key": "verify", "reason": "..." }],
      "steps": [
        { "key": "build", "command": "...", "status": "Passed", "exitCode": 0, "durationSeconds": 12.4 }
      ],
      "status": "Passed"
    }
  ],
  "summary": { "included": 0, "excluded": 0, "run": 0, "passed": 0, "failed": 0, "refused": 0, "skipped": 0 },
  "exitCode": 0
}
```

`denyList` must be present in every mode, including `-List`. Agent G's test
reads the patterns from here so the workflow check cannot drift from the
runner.

### Part 7 — Test-script convention

`-List` output and `eng/ci/README.md` must both state the convention that
Agents F and G implement against. Each `eng/ci/tests/Test-*.ps1` script:

- is self-contained and takes no required parameter;
- prints one line per assertion, prefixed `PASS:`, `FAIL:`, or `SKIP:`;
- prints `SKIP:` only for a genuinely unavailable optional dependency, never
  for a failed assertion;
- writes nothing outside a directory it creates under `$env:TEMP` and removes
  on exit;
- exits 0 only when no assertion failed.

### Part 8 — `eng/ci/README.md`

Document, with no forward-looking claims:

- what the runner is for and the exact commands to run it;
- the full gate table from Part 3, including the two excluded
  `release-candidate` keys and their reasons;
- why `ci-gates` is self-referential and excluded;
- the deny-list rationale and the exit codes;
- the test-script convention from Part 7;
- that `origin` is deliberately unpushed on SND-HOST, so the GitHub Actions
  workflow added by Agent G is verified by parse and structural inspection and
  has never been exercised on a hosted runner. State this as a current
  limitation, not as a plan.

---

## Constraints

- Do not modify `.codex/skills/project.toml`. Agent I owns it. If a gate needs
  a configuration change, report it in your result payload.
- Do not edit `live-tracker.md`. Agent I is its only writer. Return your
  tracker row text in your result payload instead.
- Do not create `eng/ci/Test-LhmCiGates.ps1` or anything under `eng/ci/tests/`.
  Agents F and G own those.
- Do not touch any product source, project file, solution, or package file.
- Do not read or write the live runtime, release store, rollback store, log
  archive, or any scheduled task.
- Do not add a parameter, exit code, or JSON field beyond the ones specified
  here without recording the change in your result payload.

---

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All -DryRun
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -Gate campaign-control
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -Gate release-candidate -DryRun
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List -JsonSummary $env:TEMP\lhm-ci-list.json
pwsh -NoProfile -File eng\ci\Invoke-LhmGates.ps1 -List
git clean -ndX
git status --porcelain
# Real gate sweep; several minutes. Run it last.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All
git clean -ndX
```

`git status --porcelain` must show only your own two new files, and the live
SND-HOST process, root task, and current CSV must be untouched.

The two `git clean -ndX` calls bracket the sweep deliberately. Before it, the
list is `data/tasks.json` and `data/analysis-cache.json` only. After it, the
three `dotnet build` gates have recreated `bin/` and `obj/` trees, so the list
is long — that is expected, not a defect, because build output is ignored and
reproducible. What must **not** appear in the second list is any
`__pycache__` directory; if one does, the `PYTHONDONTWRITEBYTECODE` setting is
not reaching the Python gate children. Report the post-sweep state in your
result payload so Agent I can clean it as a named step; the cleanup script is
on your deny-list and the runner must not invoke it.

---

## Do NOT

- Do not hard-code any gate command string in the runner.
- Do not run `ops/release/New-LhmRelease.ps1`,
  `Test-LhmReleaseCandidate.ps1 -RequireCurrentSource`, or anything under
  `ops/local-release/` from the runner or during verification.
- Do not add `ci-gates` to the runnable set; it recurses.
- Do not treat a missing classified gate as a failure.
- Do not silently skip a gate the configuration declares but the table does not
  classify.
- Do not require elevation, and do not add a self-elevating relaunch branch.
- Do not write files into the repository other than your two owned files.
