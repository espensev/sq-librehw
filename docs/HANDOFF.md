# LibreHardwareMonitor Structural Baseline Handoff

**Checkpoint:** 2026-07-31 06:10 BST
**Machine:** verified `snd-host` / `SND-HOST`
**Repository:** `E:\SQ_HQ\Monitoring\libre-dev\librehw-host`
**Branch/HEAD:** `main`, one commit ahead of `origin/main`; the parent of the
baseline commit is `96746807a41e186e1404e3f4220985db5dd6c276`
**State:** structural baseline reviewed, verified, and committed on `main`;
deliberately not pushed
**Lifecycle:** retire this file once Plan-002 is registered and its remaining
scope is carried by the roadmap and current-state docs

## Resume objective

The structural baseline is committed. The next step is to register a bounded
Plan-002 from that clean baseline. The recommended first campaign is
non-deploying CI and campaign-history contracts, not product-source moves.

Do not restart discovery. The deep audit, workspace cleanup, live-consumer
inventory, control-plane repair, and phased roadmap are complete.

## Outcome

- Removed four clean completed Plan-001 worktrees after ancestry or
  patch-equivalence proof.
- Preserved every Plan-001 branch; no branch was deleted.
- Removed only three verified-empty false Git markers:
  `Monitoring\.git`, `HWiNFO64\.git`, and `libre-dev\.git`.
- Reclaimed approximately 4,093 MiB. `libre-dev` is now stable at 46.44 MiB,
  down 98.88% from 4,139.49 MiB.
- Removed all generated repository `bin`/`obj` and Python cache directories
  after verification. They are reproducible.
- Repaired the local `vanilla` fetch URL to the current clean checkout and made
  its push URL fail closed.
- Restored and configured the local Codex campaign runtime.
- Reconciled Plan-001 without falsely closing its attended acceptance gate.
- Added the structural audit, roadmap, campaign-control contract, and
  regression coverage for control-plane safety defects.
- Kept the live runtime, tasks, shortcuts, settings, logs, releases, rollback
  packets, archives, and environment bindings untouched.

## Non-negotiable architecture decisions

- Keep the inherited product roots in place for upstream compatibility:
  `Aga.Controls`, `LibreHardwareMonitorLib`,
  `LibreHardwareMonitor.Windows.Forms`, and `LibreHardwareMonitor.sln`.
- Reorganize fork-only experiments, tests, operations, documentation, and
  campaign tooling before extracting inherited product code.
- Keep source, release candidates, live runtime, rollback, and log/archive
  authority separate.
- WinForms remains the sole current hardware, process, and scheduled-task
  owner.
- Preserve `data.json` shape/order/IDs, Prometheus, CSV, settings, hardware
  behavior, and both supported WinForms targets throughout structural work.
- Treat the Avalonia explorer as fixture-only and non-shipping.
- Do not relocate live/data paths until a separate manifest-backed SND-HOST
  migration accounts for every durable consumer and rollback surface.

## Current Git state

The primary checkout is the only registered worktree. `main` now carries the
structural baseline commit and is one commit ahead of `origin/main`. Nothing
was pushed. The working tree is otherwise clean; only ignored local state
remains.

The baseline commit updated these tracked files:

- `.codex/skills/project.toml`
- `.gitignore`
- `AGENTS.md`
- `data/plans/plan-001.json`
- `docs/README.md`
- `docs/campaign-plan-001-fixture-only-avalonia-sensor.md`

It added these previously untracked groups:

- `.codex/skills/planning-contract.md`
- `.codex/skills/project.toml.template`
- `docs/HANDOFF.md`
- `docs/architecture/`
- `docs/discovery-librehw-structural-audit.md`
- `docs/refactor-roadmap.md`
- `scripts/task_manager.py`
- `scripts/analysis/`
- `scripts/task_runtime/`

Ignored local state:

- `data/tasks.json`
- `data/analysis-cache.json`

Do not use `git clean -fdX`; it would remove the local execution ledger and
analysis cache.

Local remote configuration, which is not part of the tracked diff:

- `origin`: `https://github.com/celine-anime/librehw-host.git`
- `upstream` fetch:
  `https://github.com/espensev/sq-librehw.git`
- `upstream` push: `DISABLED`
- `vanilla` fetch:
  `E:/SQ_HQ/Monitoring/libre-dev/LibreHardwareMonitor`
- `vanilla` push: `DISABLED`
- push default: `origin` / `simple`

Preserved branch evidence:

| Branch | Commit |
|---|---|
| `main` | `96746807a41e186e1404e3f4220985db5dd6c276` |
| `agent/plan-001-b-parser` | `1d40e04f3b61aa35a4cc72f2ae6fa92295728621` |
| `integration/plan-001-bc-review` | `33cc19b50263184db4bd8ea6734d1f4d0bf8d522` |
| `agent/plan-001-c-shell` | `59cb5489ff65f64e2c4f3b3d5fc674421994c082` |
| `agent/plan-001-d-integration` | `b466837b20051862f1268b284fa06ec4bedd1f48` |

## Plan-001 truth

Two statuses are intentionally different:

- ignored execution ledger: `verified`, four of four tasks done,
  `next_action=done`;
- tracked plan contract: `partial`, `executed_at` empty.

Automated implementation, merge, regression, and exact-source candidate
evidence completed at commit `9674680`. Exit criterion 9, an attended
normal-user smoke and evidence record, remains open. Do not mark the plan
executed unless that criterion is performed or explicitly waived.

## Campaign-control repair

The canonical task-manager runtime was pinned locally with a five-file
repository safety overlay:

- `scripts/task_runtime/execution.py`
- `scripts/task_runtime/verify.py`
- `scripts/task_runtime/test_execution.py`
- `scripts/task_runtime/test_verify.py`
- `scripts/task_runtime/test_project_config.py`

The overlay enforces:

- task failures, verification failures, and merge conflicts override stale
  success;
- a consistent passed automated verification is terminal;
- aggregate automation leaves each exit criterion `not_evaluated` instead of
  claiming manual acceptance;
- generic campaign verification runs only the two .NET builds and .NET tests;
- external candidate creation remains isolated in the named release gate.

The exact overlay blob IDs and safe refresh procedure are recorded in
`docs/architecture/campaign-control-plane.md`. Do not run
`python scripts/task_manager.py init --force`, and do not overwrite the overlay
with the canonical package without a three-way review.

## Verification evidence

Current fast checkpoint:

- machine identity: `VERIFIED`;
- planning preflight: ready, zero errors, pre-commit dirty-tree warning only;
- campaign-control tests: 12/12 passed;
- analyzer: high confidence, non-partial, seven .NET projects;
- `git diff --check`: no errors; Windows line-ending warnings only;
- repository build directories: zero.

Runtime provenance re-proved at commit time. Every vendored campaign-runtime
file was compared against the canonical package with line-ending-normalized
hashes: 27 of 32 are byte-identical, exactly the two documented overlay
implementations differ, and exactly the three documented overlay tests are
repository-only additions. No undocumented deviation exists. The pinned
`scripts/task_manager.py` SHA-256 and all five overlay blob IDs recorded in
`docs/architecture/campaign-control-plane.md` matched.

Full non-live baseline completed during this pass:

- .NET: 258 passed, one documented opt-in skip, zero failed;
- WinForms x64 Release: `net10.0-windows` and `net472` both passed;
- Avalonia fixture: 75/75 tests passed;
- web: 315/315 selftests and 18/18 Node tests passed;
- release-system fixture: 114/114 assertions passed;
- log-management fixture: passed;
- peer-safe SND-DESK local-release fixture: passed.

The latest candidate
`0.9.6-20260730-211154919-9674680` is internally verified and promotable for
the source at `9674680`, which is now the parent of the baseline commit.
`-RequireCurrentSource` therefore no longer matches `HEAD`. That is expected
and not a regression: the baseline changes only documentation, campaign
tooling, and configuration, and touches no product source, so no replacement
candidate was created.

## Live SND-HOST proof

Verified again at this checkpoint:

- exactly one LHM process, PID `13104`;
- executable:
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor\LibreHardwareMonitor.Windows.Forms.exe`;
- product version: `0.9.6+d693da7.2026-07-25`;
- SHA-256:
  `7215C2569A7021B761F89C54FF734819D015D58240F3287F40446B5B5028733B`;
- root task `\LibreHardwareMonitor`: `Running`;
- task result `267009` / `0x41301`, meaning still running;
- task action and working directory exactly match the live root;
- proxy-bypassed `/`, `/data.json`, and `/metrics`: all HTTP `200`;
- `LibreHardwareMonitorLog-2026-07-31.csv` grew 7,665 bytes in three seconds.

The full consumer audit found 16 scheduled-task bindings across Monitoring,
four Start Menu shortcuts, SQ shims, environment bindings, Scribe paths, and
log-management ownership. No service, Run/RunOnce entry, Startup item, or WMI
consumer owns LHM.

There is still no `librehw.runtime.json` and no explicit
`LIBREHARDWAREMONITOR_DATA_ROOT`; configuration and the active CSV remain
co-located with the live executable. This is why the live directory must not be
raw-moved.

## Remaining ambiguity deliberately left untouched

- empty `.agents` placeholders in several Monitoring/source directories;
- empty `Monitoring\Active`;
- empty repo `.github`, pending the CI phase;
- foreign `HWiNFO64\logex.txt` scratch transcript;
- `TerminateWarThunder`, pending registry-alert ownership inspection;
- root operational `docs` and `tests`, which still lack declared ownership.

These are quarantine/ownership decisions, not cleanup permission.

## Next safe sequence

Baseline review, gate re-runs, and the baseline commit are complete. The diff
and the five-file overlay were reviewed, every vendored runtime file was
provenance-checked against the canonical package, the fast gates were re-run
green, and the baseline landed as a single commit containing no source moves
and no product-behavior change.

Remaining:

1. Do not push unless explicitly requested.
2. Register Plan-002 from the clean committed baseline. Prefer non-deploying CI
   and campaign-history contracts first.
3. Keep the attended Plan-001 smoke as a separate explicit acceptance action.
4. Only after CI/control-plane closure, consider the isolated Avalonia
   experiment move described in Phase 2 of the roadmap.

To re-run the fast gates at any point:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python scripts\task_manager.py plan preflight --json
python -m unittest discover -s scripts -p 'test_*.py' -v
git diff --check
git clean -ndX
```

## Do not do

- Do not move or replace the live LHM runtime.
- Do not run SND-DESK `ops/local-release` as a host deployment path.
- Do not create a release candidate merely to verify docs/control changes.
- Do not delete the preserved Plan-001 branches during baseline review.
- Do not raw-delete Git worktrees or Git administration paths.
- Do not move inherited product roots under `src`.
- Do not infer that automated `verified` means attended acceptance completed.
- Do not register Plan-002 from a dirty tree. The baseline commit satisfied
  this gate; re-check preflight before registering.

## Read first

- `docs/discovery-librehw-structural-audit.md`
- `docs/refactor-roadmap.md`
- `docs/architecture/campaign-control-plane.md`
- `.codex/skills/project.toml`
- `docs/campaign-plan-001-fixture-only-avalonia-sensor.md`
- `docs/feature-avalonia-fixture-sensor-explorer.md`
