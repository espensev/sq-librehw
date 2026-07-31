# Campaign Control-Plane Contract

**Status:** active
**Date:** 2026-07-31

## Authority

The campaign system has four distinct truth layers:

| Layer | Authority | Persistence |
|---|---|---|
| Plan contract | `data/plans/plan-NNN.json` and its campaign document | tracked |
| Execution ledger | `data/tasks.json` | local and ignored |
| Acceptance ledger | `docs/campaign-history.md` | tracked |
| Runtime/configuration | `scripts/task_manager.py`, `scripts/task_runtime`, `scripts/analysis`, `.codex/skills/project.toml` | tracked |

The tracker is a human-readable progress surface, not permission to infer
acceptance or deployment.

Plan status and execution-manifest status answer different questions. A plan
may correctly be `partial` because an attended acceptance gate is open while
its historical automated execution manifest is `verified`.

## Campaign history and acceptance

Per-criterion acceptance evidence is durable only if it is tracked.
`data/tasks.json` is ignored local execution state, so it can record that
automation ran but can never record that a person accepted the result.
`docs/campaign-history.md` is that record.

### The two axes

Plan lifecycle status answers **"has the tooling registered and run this
campaign"**. Ledger state answers **"has a person accepted the result"**. They
move independently and neither implies the other.

The distinction is not academic. `executed` in this tooling means the agents
were registered and their spec templates generated. A plan is `executed` from
the moment it is registered, with every one of its exit criteria still open.
Any acceptance rule keyed on plan status is therefore wrong on the day the plan
is created.

### Ledger states

| State | Meaning | Who may set it |
|---|---|---|
| `registered` | The plan is approved and its agents exist. | whoever registers the campaign |
| `implemented` | Every automated exit criterion has recorded evidence and all agent work is merged. | the campaign owner, after the gates pass |
| `accepted` | Every criterion is `met`, or `waived` with a complete waiver record. | a person only |
| `closed` | Accepted, and the campaign document, tracker rows, and agent specs are reconciled. | a person only |

**Automated verification must never write `accepted`.** This is the same rule
the `verify.py` overlay already enforces one level down, where aggregate
automation leaves every exit criterion `not_evaluated`. A campaign tool that
could write `accepted` would make that overlay pointless.

### The transition rule

A campaign may not be recorded `accepted` or `closed` while any criterion is
`open`, or is `waived` without a complete waiver record.

Moving to `implemented` with attended criteria still open is correct and
expected. That is precisely plan-001's position, and it is not a defect.

### Waiver records

A waiver is the only way a criterion may be treated as closed without being met.
All five fields are required:

| Field | Content |
|---|---|
| Waived by | the person accepting the risk |
| Date | absolute ISO date, never "recently" or "last week" |
| Reason | why the criterion cannot or need not be met |
| Accepted risk | what could go wrong because it was not met |
| Reopens if | the condition that puts the criterion back to `open` |

A waiver missing any field is treated as `open`.

### Applied to plan-001

Three statuses are simultaneously correct:

- its automated execution manifest is `verified`;
- its tracked plan contract is `partial`;
- its ledger state is `implemented`.

Criterion 9, the attended normal-user smoke, is `open`. Closing it requires
either performing the attended smoke or filing a waiver record. Plan-002
defined this contract; it deliberately did neither.

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
  rules and the non-mutating generic-command boundary;
- `scripts/task_runtime/test_campaign_history.py`: regression coverage for the
  acceptance ledger above — plan-to-ledger agreement in both directions,
  verbatim criterion text, the transition rule, and complete waiver records.

There are now four repository-only overlay tests and two overlay
implementations. `test_campaign_history.py` is a repository-only addition: it
modifies no canonical file and the pinned `scripts/task_manager.py` SHA-256
above is unchanged by it.

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
| `test_campaign_history.py` | `58a37e2c879dd2fce554c5c6ac4efc978572b08e` |

Recompute a blob ID with `git hash-object <path>` after any edit to the file it
names. A stale blob ID is a provenance defect, not a formatting detail.

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
