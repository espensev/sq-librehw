# Agent Task — Ci Workflow Delegation

**Scope:** Add the read-only GitHub Actions workflow that delegates to the gate
runner, plus its structural and YAML-parse regression tests.

**Depends on:** Agent E, Agent F

**Output files:** `.github/workflows/non-deploying-gates.yml`,
`eng/ci/tests/Test-Workflow.ps1`

## Exit Criteria

- `.github/workflows/non-deploying-gates.yml` parses as valid YAML.
- The workflow declares top-level `permissions:` with `contents: read` and
  nothing else.
- The workflow references no repository secret and no deployment, publish,
  release, or cloud-login action.
- Every step that runs gates delegates to `eng/ci/Invoke-LhmGates.ps1`; the
  workflow contains no inline `dotnet`, `node`, or `python` gate command.
- The workflow contains no command matching any pattern in the runner's own
  `denyList`.
- `eng/ci/tests/Test-Workflow.ps1` follows Agent E's test-script convention,
  covers all cases in Part 2, and is discovered and run by
  `eng/ci/Test-LhmCiGates.ps1`.
- The workflow's hosted execution is recorded as unverified, because `origin`
  is deliberately unpushed.

---

## Context — read before doing anything

1. `AGENTS.md` — task classification and baseline commands.
2. `agents/agent-e-ci-gate-runner.md` — the frozen runner surface, exit codes,
   JSON summary shape, and the test-script convention.
3. `eng/ci/Invoke-LhmGates.ps1` and `eng/ci/README.md` — the delivered runner
   and its documented gate table. The workflow may use only the parameters
   published there.
4. `eng/ci/Test-LhmCiGates.ps1` and `eng/ci/tests/Test-GateRunner.ps1` — the
   dispatcher contract and the existing suite's structure and reporting style.
   Match them.
5. `docs/README.md` — the repository section. `origin` is
   `celine-anime/librehw-host`, `upstream` is fetch-only, and the structural
   baseline is deliberately unpushed. The fork-specific Dependabot file is
   intentionally absent; do not add one.
6. `global.json` — the pinned SDK the workflow must install.
7. `docs/refactor-roadmap.md` — Phase 1's exit gate requires that CI cannot
   deploy or mutate the host.

---

## Task

### Part 1 — `.github/workflows/non-deploying-gates.yml`

Write the workflow with a comment header stating, in plain terms, that:

- it runs only non-deploying gates;
- every gate command comes from `.codex/skills/project.toml` through
  `eng/ci/Invoke-LhmGates.ps1`, and none is duplicated here;
- `origin` is deliberately unpushed on SND-HOST, so this workflow has never run
  on a hosted runner and is verified by parse and structural inspection;
- it must never gain a deploying, publishing, or host-mutating step.

Structure:

- `name: Non-deploying gates`
- `on:` `workflow_dispatch`, `pull_request` on `main`, and `push` on `main`.
- Top-level `permissions:` containing exactly `contents: read`.
- A `concurrency` group keyed on the workflow and ref, with
  `cancel-in-progress: true`.
- A single job `gates` with `runs-on: windows-latest`, because every gate is
  Windows-only, and a `timeout-minutes` value.
- Steps, in order:
  1. `actions/checkout@v5`
  2. `actions/setup-dotnet@v5` with `global-json-file: global.json`
  3. `actions/setup-node@v6` with a `node-version` matching the version the
     repository already uses; state the chosen version in the step name
  4. `actions/setup-python@v6` with a Python 3.11 or newer version, required by
     the runner's `tomllib` parse
  5. `Run non-deploying gates` — the only gate-running step:

     ```yaml
     shell: powershell
     run: >-
       .\eng\ci\Invoke-LhmGates.ps1 -All -JsonSummary ci-gates-summary.json
     ```

  6. `actions/upload-artifact@v5` with `if: always()`, uploading
     `ci-gates-summary.json`

Use version tags rather than commit SHAs for the actions, and say so in the
comment header, because this workflow is inspected rather than executed and a
pinned SHA could not be verified from this machine.

Do not add a matrix, a second job, a scheduled trigger, a self-hosted runner, a
`permissions` entry other than `contents: read`, or any `env:` block carrying a
secret or token.

### Part 2 — `eng/ci/tests/Test-Workflow.ps1`

Follow Agent E's convention exactly: one `PASS:`, `FAIL:`, or `SKIP:` line per
assertion, no required parameters, temp-only writes, exit 0 only when nothing
failed.

Cases:

1. `.github/workflows/non-deploying-gates.yml` exists and is non-empty.
2. The file parses as YAML. Use the Python already required by the repository:

   ```powershell
   python -c "import sys,yaml;yaml.safe_load(open(sys.argv[1],encoding='utf-8'))" <path>
   ```

   If `python` is present but PyYAML is not, print a single
   `SKIP: PyYAML unavailable; YAML parse not executed` and continue with the
   remaining cases. If `python` itself is missing, that is a `FAIL:` — the
   repository requires Python.
3. Top-level `permissions` resolves to exactly `contents: read` and grants
   nothing else.
4. The file contains no `secrets.` reference.
5. No `uses:` value matches `deploy`, `publish`, `release`, `gh-pages`,
   `azure/`, `aws-actions/`, or `docker/login`.
6. At least one step invokes `eng/ci/Invoke-LhmGates.ps1`, and every step that
   runs a gate does so through it.
7. No `run:` block contains an inline gate command: assert the absence of
   `dotnet build`, `dotnet test`, `node webtests`, `node --test`, and
   `python -m unittest`. Gate commands belong in `.codex/skills/project.toml`.
8. No `run:` block matches any pattern in the runner's `denyList`. Obtain the
   patterns by invoking
   `eng/ci/Invoke-LhmGates.ps1 -List -JsonSummary <temp path>` and reading the
   `denyList` array. Do not restate the patterns.
9. `runs-on` is `windows-latest`.
10. The workflow declares a `concurrency` group and a job `timeout-minutes`.

Where a case needs structured access, prefer the parsed YAML converted to JSON
in the same Python call used by case 2. Fall back to line-oriented assertions
only for the cases that remain meaningful without a parse, and mark the parse
dependency with `SKIP:` rather than silently passing.

---

## Constraints

- Do not modify `eng/ci/Invoke-LhmGates.ps1`, `eng/ci/README.md`,
  `eng/ci/Test-LhmCiGates.ps1`, or `eng/ci/tests/Test-GateRunner.ps1`. Agents E
  and F own them.
- Do not modify `.codex/skills/project.toml`. Agent I registers the
  `.github/workflows` smart-test mapping.
- Do not edit `live-tracker.md`. Return your tracker row text in your result
  payload.
- Do not add `.github/dependabot.yml`, an issue template, a `CODEOWNERS` file,
  or any other `.github` content. The Dependabot omission is a deliberate fork
  decision recorded in `docs/README.md`.
- Do not push, and do not add any step or instruction that would push, tag,
  create a release, or write to the repository.
- Do not claim the workflow has been executed on a hosted runner.

---

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -c "import sys,yaml;yaml.safe_load(open(sys.argv[1],encoding='utf-8'));print('yaml ok')" .github\workflows\non-deploying-gates.yml
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-Workflow.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
pwsh -NoProfile -File eng\ci\Test-LhmCiGates.ps1
git status --porcelain
git clean -ndX
git remote -v
```

`eng/ci/Test-LhmCiGates.ps1` must now discover two test scripts and report both.
`git clean -ndX` must list only `data/tasks.json` and `data/analysis-cache.json`
— your YAML-parse case invokes Python, so set `PYTHONDONTWRITEBYTECODE` first.
`git remote -v` is a read-only confirmation that `origin` is unchanged; do not
push.

---

## Do NOT

- Do not duplicate any gate command in the workflow.
- Do not add `contents: write`, `packages:`, `id-token:`, or any other
  permission.
- Do not reference a secret, even an unused one.
- Do not add a `schedule:` trigger; nothing on this repository should run
  unattended.
- Do not restate the deny-list; read it from the runner's JSON summary.
- Do not mark the YAML-parse case as passed when PyYAML is unavailable; print
  `SKIP:` and let the dispatcher count it as a skip.
