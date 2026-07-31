# Campaign Control-Plane Contract

**Status:** active
**Date:** 2026-07-31

## Authority

The campaign system has three distinct truth layers:

| Layer | Authority | Persistence |
|---|---|---|
| Plan contract | `data/plans/plan-NNN.json` and its campaign document | tracked |
| Execution ledger | `data/tasks.json` | local and ignored |
| Runtime/configuration | `scripts/task_manager.py`, `scripts/task_runtime`, `scripts/analysis`, `.codex/skills/project.toml` | tracked |

The tracker is a human-readable progress surface, not permission to infer
acceptance or deployment.

Plan status and execution-manifest status answer different questions. A plan
may correctly be `partial` because an attended acceptance gate is open while
its historical automated execution manifest is `verified`.

## Local runtime provenance

The campaign runtime restored on SND-HOST came from the read-only canonical
Codex package:

```text
C:\Users\Dev\OneDrive\common\Ai-skills\ai-skills-ready-packages\codex-skills
```

Pinned `scripts/task_manager.py` SHA-256:

```text
68706CD1CC1A71C0C497AF113BCE87714EA404FD8F595CF97ADF5159A2916510
```

The baseline consists of a canonical pinned surface plus a small
repository-owned safety overlay. The canonical surface consists of:

- `scripts/task_manager.py`;
- `scripts/task_runtime/*.py`;
- `scripts/analysis/*.py`;
- `.codex/skills/planning-contract.md`;
- `.codex/skills/project.toml.template`.

The project-owned `.codex/skills/project.toml` is not replaced during a runtime
refresh.

The repository-owned overlay is:

- `scripts/task_runtime/execution.py`: blocker/failure state takes precedence
  over stale success, and a consistent passed verification is terminal;
- `scripts/task_runtime/verify.py`: aggregate automated verification never
  claims that individual exit criteria, including attended criteria, passed;
- `scripts/task_runtime/test_execution.py`,
  `scripts/task_runtime/test_verify.py`, and
  `scripts/task_runtime/test_project_config.py`: regression coverage for those
  rules and the non-mutating generic-command boundary.

Recheck this overlay on each canonical refresh and drop individual deviations
when the shared package contains equivalent fixes.

Normalization-aware Git blob IDs for the reviewed overlay:

| File | Blob ID |
|---|---|
| `execution.py` | `4c287e3e81e11e210f4ebc69af4982578bc8446a` |
| `verify.py` | `6854d13fe2f34fdaff039c36073d85aea8d2a770` |
| `test_execution.py` | `e8a84281d7987cd3d3b67a4306399dc5b01887a2` |
| `test_verify.py` | `6e4cceed443bae055a2d948c8d11a6ebd3cd3d43` |
| `test_project_config.py` | `24e0c4207b7c18d96460ac39e686937642f1ee95` |

## Safe refresh procedure

1. Compare the canonical package and repository runtime files.
2. Copy canonical-matching runtime/analysis modules and shared contract files.
   Do not overwrite the five repository-owned overlay files. If a canonical
   update touches either implementation file, three-way review it and retain
   the safety behavior until the canonical version proves equivalent.
3. Do not run `init --force`; it may overwrite project-specific configuration.
4. Review the diff, including any schema or command changes.
5. Run:

```powershell
python scripts\task_manager.py plan preflight --json
python scripts\task_manager.py analyze --json
python -m unittest discover -s scripts -p test_*.py -v
```

6. Confirm `data/tasks.json` and `data/analysis-cache.json` remain ignored.

## Repository configuration

`.codex/skills/project.toml` owns:

- state, plan, agent-spec, and tracker paths;
- logical modules and high-conflict zones;
- analyzer providers and exclusions;
- documentation consistency tiers;
- source-to-test mappings and cross-cutting triggers;
- named build gates for WinForms, Avalonia, web contracts, log management,
  campaign control, candidate packaging, and the SND-DESK-only fixture.

Adding a project, moving a fork-only path, or changing a verification command
requires updating this file in the same change.

## Safety rules

- Preflight must be ready before registering or launching a campaign.
- A dirty tree is a warning for analysis, but a blocker for parallel
  source-moving work.
- `data/tasks.json` may cache plan metadata but cannot override the tracked plan
  contract.
- Verification success must produce terminal status `verified` and
  `next_action=done`.
- Failed verification or merge conflicts must produce
  `next_action=review_blockers`.
- Aggregate automated verification must leave individual exit criteria
  `not_evaluated` until criterion-specific evidence is recorded.
- Generic campaign verification must not deploy, promote, or create external
  release candidates; candidate creation belongs only to its named build gate.
- Campaign tools must never deploy, promote, alter tasks, or mutate live
  monitoring state.
- Worktree cleanup requires exact path validation plus clean-state and
  ancestry/patch-equivalence evidence; branch deletion is a separate decision.
