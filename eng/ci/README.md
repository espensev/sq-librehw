# Non-deploying CI gates

**Status:** active
**Owner:** repository verification, not deployment

`Invoke-LhmGates.ps1` is the single entry point for running this repository's
verification gates. It exists so that "run the gates" is one reviewable command
instead of a dozen remembered ones, and so that the boundary between the
non-deploying and deploying halves of the tooling is mechanical rather than
conventional.

Gate commands are **not** defined here. They live in the `[build-gate.*]` tables
of `.codex/skills/project.toml`, and the runner reads them at run time. Adding a
gate, or changing a gate's command, is a `project.toml` edit.

## Usage

```powershell
# Show every gate, its classification, the deny-list, and the exit codes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -List

# Resolve and classify everything, print what would run, execute nothing.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All -DryRun

# Run the full included set. Several minutes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All

# Run one or more named gates.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -Gate campaign-control,web-dashboard

# Stop at the first failure and record a machine-readable summary.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All -FailFast -JsonSummary ci-gates-summary.json
```

| Parameter | Behavior |
|---|---|
| `-Gate` | Run only the named gates, in the order given. |
| `-All` | Run every included gate. This is the default when neither `-Gate` nor `-List` is supplied. |
| `-List` | Print the classification table and deny-list, run nothing, exit 0. |
| `-DryRun` | Resolve, classify, and deny-list-check every command, print what would run, execute nothing. |
| `-FailFast` | Stop at the first failing or refused gate. |
| `-JsonSummary` | Write the run summary as JSON to this path. |
| `-ConfigPath` | Configuration to read. Defaults to `.codex/skills/project.toml`. |

Both Windows PowerShell 5.1 and PowerShell 7 are supported.

## Gate classification

Every `[build-gate.*]` name in the configuration must be classified. A gate the
configuration declares but the runner does not classify is a **hard error**, not
a silent skip — a silently skipped gate is indistinguishable from a passing one.
A gate the runner classifies but the configuration does not declare is only a
warning, so a not-yet-registered gate never fails a run.

| Gate | Runs | Reason |
|---|---|---|
| `winforms-net10` | `build`, `test` | Non-deploying source build and the .NET test suite. |
| `winforms-net472` | `build` | Non-deploying second-framework source build. |
| `avalonia-spike` | `build`, `test` | Fixture-only, non-shipping spike solution and its runner. |
| `web-dashboard` | `verify`, `test` | Static dashboard self-test and Node contract tests. |
| `log-management` | `test` | Host-neutral fixture test; installs nothing. |
| `campaign-control` | `verify`, `test` | Planning preflight and campaign-runtime regression tests. |
| `release-candidate` | `test` only | See below. |
| `snd-desk-local-release-fixture` | `test` | Peer-safe fixture using isolated temporary roots; proves the SND-DESK path fails closed on SND-HOST. |
| `ci-gates` | nothing | See below. |

### Excluded keys

Classification is per key, not per gate. `release-candidate` is the only gate
that is split:

- **`build` is excluded.** `ops/candidate/New-LhmRelease.ps1` creates an external
  release candidate. Candidate creation belongs only to its named release gate,
  never to CI.
- **`verify` is excluded.** `Test-LhmReleaseCandidate.ps1 -RequirePromotable
  -RequireCurrentSource` is a promotion check, not a verification check. It is
  expected to fail whenever `HEAD` moves ahead of the last candidate, which is
  the normal state after any documentation or tooling commit. A CI gate
  asserting current-source promotability would be permanently red for a reason
  that is not a defect.
- **`test` is included.** `Test-LhmReleaseSystem.ps1` is a fixture and creates
  nothing outside its own temporary state.

### `ci-gates` is self-referential

`ci-gates` runs `eng/ci/Test-LhmCiGates.ps1`, whose suite invokes this runner.
Including it in `-All` would recurse. Run the suite directly instead:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
```

## Deny-list

Classification decides *which* commands may run. The deny-list is a second,
independent check on *what* they actually contain, applied to each resolved
command string immediately before it would execute — including under `-DryRun`.

The two checks are deliberately separate. Classification is a decision made when
a gate is added; the deny-list catches a later `project.toml` edit that puts a
deploying command inside an already-included gate. A match refuses the whole
gate without executing any of its keys and exits 3.

The patterns cover release creation and promotion, installers and launchers,
restore and finalize paths, repository-output cleanup, scheduled-task and
service management, process start and stop, destructive Git operations, registry
writes, and `netsh`. `-List` prints the current list; `-JsonSummary` records it
under `denyList` in every mode, so other checks can read the patterns rather
than restating them.

`Clear-LhmRepositoryBuildOutputs.ps1` is on the deny-list even though it is the
repository's own sanctioned cleanup tool, because it mutates. Cleaning up after
a sweep is an explicit human step, never something the runner does to itself.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Every attempted gate passed, or `-List` / `-DryRun` completed. |
| `1` | At least one gate failed. |
| `2` | Configuration error: unreadable configuration, missing Python, unclassified gate, or an unknown or excluded `-Gate` name. |
| `3` | A command was refused by the deny-list. |

Refusal outranks failure. A denied command is a contract violation, not a red
test, so `3` wins when both occur.

## What the runner does not do

It never elevates, never starts or stops a process, service, or scheduled task,
never touches the live runtime, release store, rollback store, or log archive,
and writes no file other than `-JsonSummary`.

It sets exactly one environment variable, `PYTHONDONTWRITEBYTECODE=1`, in its own
process so Python gate children inherit it. Without that, the `campaign-control`
gate's unittest discovery leaves `__pycache__` directories in the repository.

## After a full sweep

`-All` runs three `dotnet build` gates, so `git clean -ndX` immediately
afterwards lists the recreated `bin/` and `obj/` trees. That is expected: build
output is ignored and reproducible. What must **not** appear is a `__pycache__`
directory. To return to the baseline's zero-generated-directories state:

```powershell
.\eng\Clear-LhmRepositoryBuildOutputs.ps1 -WhatIf
.\eng\Clear-LhmRepositoryBuildOutputs.ps1
```

Do not use `git clean -fdX`. It would delete `data/tasks.json` and
`data/analysis-cache.json`, the local execution ledger and analysis cache.

## Test-script convention

`eng/ci/Test-LhmCiGates.ps1` discovers and runs every `eng/ci/tests/Test-*.ps1`.
Each such script:

- is self-contained and takes no required parameter;
- prints one line per assertion, prefixed `PASS:`, `FAIL:`, or `SKIP:`;
- prints `SKIP:` only for a genuinely unavailable optional dependency, never for
  a failed assertion;
- writes nothing outside a directory it creates under `$env:TEMP` and removes on
  exit;
- exits 0 only when no assertion failed.

A fixture `project.toml` written by a test should use TOML **literal** strings
(single-quoted) for commands containing Windows paths. TOML basic strings
process backslash escapes, so an unescaped `C:\Users\...` path fails to parse.
The real `project.toml` uses basic strings with `\\` escaping for the same
reason.

## GitHub Actions

`.github/workflows/non-deploying-gates.yml` delegates to this runner and
duplicates no gate command.

`origin` is deliberately unpushed on SND-HOST. The workflow has therefore never
run on a hosted runner: it is verified by YAML parse and structural inspection
only. That is a current limitation of the evidence, not a plan to change the
push policy.
