# Discovery — LibreHW Structural Audit

**Goal:** Before further LibreHardwareMonitor feature work, deeply audit and
reorganize the repository, campaign state, workspace layout, and live ownership
boundaries.
**Date:** 2026-07-31
**Machine:** verified `snd-host` / `SND-HOST`
**Status:** active audit baseline; Phase 0 cleanup and the local portion of
Phase 1 control-plane recovery are complete and committed, while source
reorganization, CI, and all live relocation remain gated
**Recommended next:** register the first bounded campaign from
`docs/refactor-roadmap.md`, preferring non-deploying CI and campaign-history
contracts over product-source moves.

---

## Questions

1. What are the authoritative repository, runtime, data, release, rollback, and
   deployment roots?
2. What entry points and durable consumers actually own LibreHardwareMonitor?
3. Which files and directories are source, generated output, live data,
   operations tooling, or historical residue?
4. Where do duplicate/stale copies, campaign artifacts, or hard-coded paths
   create ambiguity?
5. What are the real module boundaries and highest-coupling structural
   hotspots?
6. What build, test, deploy, rollback, and live-verification coverage exists?
7. What target layout and migration order are safe without breaking monitoring
   or upstream synchronization?

---

## Phase 0 Actions Completed After the Read-Only Audit

- Removed four clean completed Plan-001 worktrees with native
  `git worktree remove`, without `--force` and without deleting branch refs.
  B/C patch equivalence and B/C-integration/D ancestry were rechecked first.
- Reduced `libre-dev` from approximately 4.14 GiB to 46.4 MiB after
  verification output was removed (about 4.0 GiB / 98.9% reclaimed).
- Removed the three verified-empty false Git markers at
  `Monitoring\.git`, `HWiNFO64\.git`, and `libre-dev\.git`.
- Repaired the fork's local `vanilla` remote from the retired
  `Thermal_Control` path to the verified current vanilla checkout and made its
  push URL fail closed.
- Reconciled Plan-001 to `partial`: automated implementation, integration,
  regression, and candidate-isolation gates are complete; the attended
  normal-user smoke remains open. No execution timestamp was invented.
- Revalidated the untouched live boundary: one exact-path LHM process, root
  task still running, HTTP `/`, `/data.json`, and `/metrics` all `200`, and the
  current CSV continuing to grow.

No live runtime, scheduled task, shortcut, configuration, log, candidate,
rollback packet, archive, service, or environment binding was changed.

Baseline verification after control-plane repair:

- 12/12 campaign-control regression tests passed.
- The planning preflight is ready; current analysis is high-confidence,
  non-partial, and recognizes seven .NET projects.
- 258/259 .NET tests passed with one documented opt-in skip; both x64 Release
  WinForms targets built with zero warnings/errors.
- 75/75 Avalonia tests, 315/315 web selftests, 18/18 Node tests, and 114/114
  release-system assertions passed.
- Log-management and peer-safe local-release fixtures passed.
- The latest candidate is internally valid/promotable for clean commit
  `9674680`; current-source matching is intentionally false now that the
  structural baseline has been committed on top of it.

---

## Findings

### Q1: What are the authoritative roots?

**Answer:** `E:\SQ_HQ\Monitoring` is an operational deployment tree, not a Git
repository. Source, live runtime, release candidates, rollback packets, and log
archives are already intentionally separate domains. That separation is sound
and must survive the reorganization.

| Domain | Current authority | Current assessment |
|---|---|---|
| Fork source | `E:\SQ_HQ\Monitoring\libre-dev\librehw-host` | Audit/reorganization changes are committed on `main` as the structural baseline, one commit ahead of `origin/main` at `9674680` and unpushed |
| Vanilla checkout | `E:\SQ_HQ\Monitoring\libre-dev\LibreHardwareMonitor` | Clean but redundant local upstream checkout |
| Live LHM runtime | `E:\SQ_HQ\Monitoring\LibreHardwareMonitor` | Running production path; unsafe to raw-move |
| Candidate store | `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Releases` | Six manifest-backed candidates; not deployment |
| Rollback | `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Rollback` | Protected recovery evidence |
| LHM log manager | `E:\SQ_HQ\Monitoring\LhmLogManagement` | Daily SYSTEM task dependency |
| LHM archive | `E:\SQ_HQ\Monitoring\LogArchive` | Active archive |
| HWiNFO/FanControl | `E:\SQ_HQ\Monitoring\HWiNFO64`, `FanControl` | Running and task-bound |

**Evidence:**

- `docs/README.md:27-44` declares the source, candidate, live, rollback, and
  log/archive boundaries.
- `git -C ...\librehw-host status --short --branch` returned clean
  `main...origin/main`.
- A recursive top-level inventory measured approximately 6.43 GiB across the
  Monitoring tree.
- At audit time, `Monitoring\.git`, `HWiNFO64\.git`, and `libre-dev\.git`
  were empty directories and `git -C` failed for each. They were misleading
  markers, not repositories, and were removed during Phase 0.

**Implications:**

- Do not turn the Monitoring root into a monorepo.
- Do not combine source, candidate, live, rollback, or archive domains.
- Initial reorganization should preserve current live paths and focus on the
  source/control plane.

### Q2: What actually owns and launches LibreHardwareMonitor?

**Answer:** One scheduled task is the sole automatic LHM owner, but manual
launchers, the log manager, an environment variable, and mutable-state fallback
all bind the current live path.

**Evidence:**

- `\LibreHardwareMonitor` is enabled and running for `SND-HOST\Dev`, using
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor\LibreHardwareMonitor.Windows.Forms.exe`.
- PID `13104` was the only LHM process and matched that exact executable.
- The Start Menu `LibreHardwareMonitor.lnk` and
  `E:\SHQ-HOST\userdata\sqpath\librehw.cmd:2` bind the live executable.
- `E:\SHQ-HOST\userdata\sqpath\libredev.cmd:2` binds
  `E:\SQ_HQ\Monitoring\libre-dev`.
- The user/process `LHM_RELEASE_ROOT` binds
  `E:\SQ_HQ\Monitoring\LibreHardwareMonitor-Releases`.
- `LhmLogManagement\log-management.json:5-6` binds the live directory and
  `LogArchive`.
- No `librehw.runtime.json` and no
  `LIBREHARDWAREMONITOR_DATA_ROOT` exist. `RuntimePaths.cs:105-150` therefore
  falls back to the executable directory.
- `StartupManager.cs:30-120` permits the application to own the root scheduled
  task when no managed-task path is configured.
- Monitoring-wide inspection found 16 current scheduled-task bindings, four
  Start Menu shortcuts, three running applications, Scribe cache/config
  bindings, and two machine environment variables.
- No LHM service, Run/RunOnce entry, Startup-folder item, or WMI permanent
  consumer exists.

**Implications:**

- A live move currently moves code, settings, backup settings, active CSV
  logging, and startup ownership together.
- Any later live relocation needs captured task/shortcut/config state, one
  declared startup authority, proxy-bypassed HTTP checks, CSV growth proof, and
  rollback.
- `ops/local-release` must not be used for SND-HOST: `AGENTS.md:34-38`,
  `ops/local-release/LhmLocalRelease.Common.ps1:9-42`, and
  `ops/local-release/Start-LibreHardwareMonitor.ps1:12-55` define a
  fail-closed SND-DESK-only contract.

### Q3: What is source, generated output, live data, tooling, or residue?

**Answer:** The major waste is completed worktree output; the major structural
mix is mutable monitoring data inside runtime directories.

| Area | Size | Classification | Decision |
|---|---:|---|---|
| `libre-dev` | ~4.14 GiB at audit; 46.4 MiB after verification cleanup | Source + four completed worktrees | Worktrees reclaimed |
| `HWiNFO64` | ~1.75 GiB | Runtime + Scribe + tasks + logs + backups | Preserve path; separate data later |
| `LogArchive` | ~323.6 MiB | Active archive | Preserve |
| LHM rollback | ~177.8 MiB | Recovery packet | Preserve |
| LHM candidates | ~75.7 MiB | Immutable candidate evidence | Preserve/retain by policy |
| Live LHM | ~47.7 MiB | Runtime + config + active CSV | Preserve path until data seam exists |

The four worktrees account for almost all source-area growth:

- `wt-librehw-host-plan-001-b`: ~575.8 MiB
- `wt-librehw-host-plan-001-bc-integration`: ~1.11 GiB
- `wt-librehw-host-plan-001-c`: ~1.11 GiB
- `wt-librehw-host-plan-001-d`: ~1.22 GiB

Their largest contents were reproducible Avalonia `bin`/`obj` output. The
primary checkout's verification output was removed after the gates passed.
The remaining ignored state is the local execution ledger and analysis cache;
Python bytecode caches are removed at handoff.

Other residue:

- The empty false `.git` markers were removed in Phase 0. Empty `.agents`
  placeholders remain for later control-plane reconciliation.
- Empty `Active`.
- `HWiNFO64\logex.txt` is foreign SND-DESK/MAINDESK scratch transcript material.
- `TerminateWarThunder` has no current process, service, or task consumer; it
  remains a quarantine candidate pending its registry-alert check.
- Root `docs` and `tests` are unversioned operational material with no declared
  owner.

**Implications:**

- Worktree cleanup is the safest high-value first mutation.
- Runtime/log separation must be a separately designed migration, not folder
  tidying.
- Historical and foreign-machine artifacts should be quarantined, not silently
  interpreted as live configuration.

### Q4: Where is ownership or path state ambiguous?

**Answer:** The largest audit-time ambiguities were stale campaign truth and a
broken local remote; both are reconciled. Mixed active/historical documentation
remains until the attended gate closes or is waived.

**Evidence:**

- At audit time, `data/plans/plan-001.json` and the campaign document still said
  `approved` even though all tasks and tracker entries were done. Phase 0
  reconciled both tracked surfaces to `partial`; `executed_at` remains empty
  because the attended acceptance gate has not run.
- The ignored execution ledger now records all four tasks as merged and the
  historical automated verification as passed. Its execution manifest is
  `verified`, while the cached plan remains `partial`. Those are intentionally
  different dimensions.
- The attended normal-user smoke remains legitimately open at
  `docs/feature-avalonia-fixture-sensor-explorer.md:331-345` and campaign exit
  criterion `docs/campaign-plan-001-fixture-only-avalonia-sensor.md:27`.
- Four clean worktrees remained registered at audit time. Agent A's recorded
  worktree no longer existed. B and C were not ancestors of `main`, but
  `git cherry main <branch>` returned only `-` entries, proving patch
  equivalence. B/C integration and D were ancestors of `main`. Phase 0 removed
  all four worktrees while preserving every branch ref.
- At audit time, `.git/config` defined `vanilla` using the retired
  `E:/SQ_HQ/Thermal_Control/Monitoring/...` path. Phase 0 rebound it to the
  verified clean checkout at
  `E:/SQ_HQ/Monitoring/libre-dev/LibreHardwareMonitor`; `ls-remote` now
  resolves its exact HEAD and the push URL is `DISABLED`.
- The campaign document, four agent specs, plan JSON, and tracker still look
  active because attended acceptance remains open. They should move to campaign
  history only after that gate is explicitly closed or waived.
- A docs-sync scan found no merge conflict markers, but confirmed the status
  and retention-policy contradiction above.

**Implications:**

- Record automated completion and the still-open attended gate separately;
  do not falsely mark the campaign fully accepted.
- Remove clean completed worktrees without deleting their branch refs during
  the first cleanup pass.
- Reconcile active versus archived campaign documents before creating plan-002.
- Keep the corrected local `vanilla` URL synchronized if the source container
  moves later.

### Q5: Where are the structural code hotspots?

**Answer:** The inherited project roots should remain stable for upstream
compatibility. Fork-specific orchestration is concentrated in a few oversized
files and should be split only behind contracts and characterization tests.

**Evidence:**

- `MainForm.cs` is 2,574 lines and combines settings, hardware lifecycle,
  HTTP/log/update state, tree/plot presentation, tray/gadget, persistence, and
  shutdown (`MainForm.cs:27-253,356-735,1681-1882`).
- `HttpServer.cs` is 1,512 lines and combines listener lifecycle, dispatch,
  mutations, static assets, data.json, Prometheus, authentication, and
  concurrency (`HttpServer.cs:216,612,831,963,1188,1410`).
- `Computer.cs` is 1,255 lines; open/rollback/group creation/close/reset remain
  concentrated at `Computer.cs:654-918,996-1100`.
- `NvidiaGroup.cs:118-273,402-627` and
  `StorageGroup.cs:70-120,241-441` mix dynamic refresh, snapshot publication,
  events, error handling, and lifetime ownership.
- `LibreHardwareMonitor.Tests.csproj:18` references the entire WinForms
  executable, which itself references Aga.Controls and the hardware library at
  `LibreHardwareMonitor.Windows.Forms.csproj:87-88`.
- The Avalonia spike's separate three-project graph
  (`experiments\avalonia-fixture-explorer\LibreHardwareMonitor.Avalonia.Spike.slnx:1-5`) is isolated and provides a
  useful experimental boundary.

**Implications:**

- Do not start by moving the upstream-derived root projects under `src/`; that
  would create continuous upstream merge damage.
- Extract logical contracts first: immutable sensor snapshots/data.json,
  application lifecycle/settings coordination, and adapter boundaries.
- Split tests into library-only, WinForms/application, and external-contract
  suites before large source extraction.

### Q6: What verification and rollback coverage exists?

**Answer:** Product and release verification is broad. Phase 1 restored the
local automation model, but remote CI is still absent.

**Evidence:**

- `AGENTS.md:69-81` defines the dual-framework WinForms and .NET test baseline.
- `docs/README.md:259-289` lists Node dashboard, log-management, Avalonia,
  release-system, local-release fixture, .NET test, and both x64 build gates.
- At audit time, `.codex/skills/project.toml` modeled only external candidate
  build and release verification, and `scripts/task_manager.py` was absent.
- Phase 1 pinned the canonical Codex campaign runtime locally, expanded
  `project.toml` with paths, logical modules, conflict zones, docs-sync,
  smart-test, and eight build gates, and restored a ready preflight.
- The refreshed high-confidence analyzer recognizes the current seven-project
  .NET graph. Non-project docs and operations files remain logically mapped
  even though they are not MSBuild project members.
- `.github` is empty, so structural changes have no remote CI gate.
- Live verification is strong but manual/host-bound: exact process and task
  paths, `/`, `/data.json`, and `/metrics` returned `200` with proxy bypass,
  and the current CSV was actively growing.
- The live EXE hash matches historical candidate
  `0.9.6-20260725-165558646-d693da7`, not the newer July 30 source candidates.
  That is an intentional deployment boundary.
- The latest July 30 candidate still verifies as internally valid and
  promotable for commit `9674680`. `-RequireCurrentSource` correctly rejects it
  against the newer baseline commit; no replacement candidate was created for
  documentation/control-plane-only work.

**Implications:**

- Restore the campaign backend/config and define granular verification before
  registering a refactor campaign.
- Add non-deploying CI before extracting central classes.
- Candidate-ready, source-verified, attended-smoke-complete, and live-promoted
  must remain separate gates.

### Q7: What target layout and migration order are safe?

**Answer:** Reorganize fork-only and control-plane surfaces first while keeping
upstream-facing project roots and current live paths stable.

#### Workspace target

```text
Monitoring/
├── FanControl/                         # stable live path initially
├── HWiNFO64/                           # stable live path initially
├── LibreHardwareMonitor/               # stable live path initially
├── source/
│   ├── librehw-host/
│   ├── upstream/LibreHardwareMonitor/
│   └── worktrees/                      # ephemeral and bounded
├── releases/LibreHardwareMonitor/
├── rollback/LibreHardwareMonitor/
├── data/
│   ├── logs/{HWiNFO64,LibreHardwareMonitor}/
│   └── backups/HWiNFO64/
├── ops/{Scribe,LhmLogManagement,tests}/
├── docs/
└── quarantine/
```

The new grouped paths are a target, not authority to move live directories in
Phase 0. Stable compatibility paths remain until every consumer is rebound and
verified.

#### Repository target

```text
Aga.Controls/                            # keep upstream-facing root
LibreHardwareMonitorLib/                 # keep upstream-facing root
LibreHardwareMonitor.Windows.Forms/      # keep upstream-facing root
LibreHardwareMonitor.sln                 # keep upstream-facing root

experiments/avalonia-fixture-explorer/{Core,App,Tests}/
tests/{Library,WindowsForms,Contracts}/
eng/{build,test,powershell,ci}/
ops/{candidate,deploy/snd-desk,log-management}/
docs/
  architecture/
  features/{active,shipped}/
  operations/{snd-host,snd-desk}/
  campaigns/{active,archive}/
```

Logical source boundaries:

- **Contracts:** immutable sensor snapshots and stable data.json semantics.
- **Application:** hardware lifecycle, polling, option/reset coordination, and
  settings projection.
- **Adapters:** WinForms tree, HTTP/data.json, Prometheus, and logging.
- **Hosts:** WinForms shipping host; Avalonia remains an experiment until a
  separately accepted promotion.
- **Hardware:** existing `LibreHardwareMonitorLib`, changed conservatively for
  upstream compatibility.

---

## Cross-Cutting Analysis

### Constraints

- WinForms remains the only current hardware/process/task owner.
- Current live directories and archives cannot be raw-moved.
- Source, candidates, live runtime, rollback, and logs must remain distinct.
- SND-DESK `ops/local-release` defaults are not SND-HOST authority.
- Upstream-facing project roots should not be wholesale relocated.
- `data.json`, CSV IDs/order, Prometheus, settings, and hardware behavior must
  remain unchanged during structural phases.

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Live outage from path tidying | High without gates | High | Preserve live paths until manifest-backed cutover |
| Lost campaign work | Low after proof | High | Require clean worktree plus ancestor/patch-equivalence proof |
| False plan completion | Medium | Medium | Separate automated completion from pending attended smoke |
| Upstream merge damage | High after root moves | High | Keep inherited product roots stable |
| SND-DESK tooling run on host | Low if gated | High | Preserve fail-closed peer contract and explicit taxonomy |
| Monolith extraction regression | Medium | High | Contracts and characterization tests before moves |
| Source candidate mistaken for live | Medium | High | Keep candidate, attended, and promotion gates distinct |

### Open Questions

- Whether the long-term source container should remain under Monitoring or move
  to the machine's development root. This is not needed for Phase 0.
- Whether the live LHM data-root seam should use `librehw.runtime.json`, an
  explicit environment setting, or a separately managed host manifest.
- Final retention policy for old candidates and rollback packets after a newer
  live promotion.

---

## Recommendation

Proceed in this order:

1. **Campaign closure and source hygiene — bounded Phase 0 complete**
   - Plan-001 now records automated completion while preserving the pending
     attended smoke as open.
   - The four clean completed worktrees were removed after
     patch-equivalence/ancestry proof; branch refs remain for audit until the
     ledger reconciliation is committed.
   - The verified-empty false Git markers were removed. Ambiguous foreign or
     historical material remains untouched pending an explicit quarantine map.

2. **Control-plane contracts**
   - Restored/pinned `scripts/task_manager.py` and its analysis/runtime modules.
   - Expanded `project.toml` with paths, modules, conflict zones, tracker,
     docs-sync, smart-test, and all current non-live verification gates.
   - Added the control-plane architecture contract and regression coverage for
     terminal execution status.
   - Re-ran the non-live .NET, Avalonia, web, log-management, release-system,
     and peer-safe local-release fixtures; all passed. Candidate/source identity
     remains gated until the baseline is committed.
   - Non-deploying CI remains open.

3. **Fork-only surface organization**
   - Move the Avalonia experiment, campaign documents, and machine-bound
     operations taxonomy without moving inherited upstream project roots.
   - Distinguish candidate creation from SND-DESK deployment by name and config.

4. **Verification split**
   - Separate library, WinForms/application, and contract tests.

5. **Contracts and seam extraction**
   - Extract snapshot/data.json, HTTP service, lifecycle/settings, and
     presentation seams with behavior-preserving gates.

6. **Runtime/data separation**
   - Introduce an accepted SND-HOST runtime/data/startup contract.
   - Only then migrate live/data/archive paths with rollback, full consumer
     rebinding, exact-path process/task proof, HTTP `200` checks, settings
     persistence, and CSV/archive growth verification.

The control-plane blocker is resolved: preflight is ready and the analyzer is
high confidence. Plan-002 is intentionally not registered yet. First review and
commit this baseline, then select the first bounded phase from
`docs/refactor-roadmap.md`; do not mix baseline repair with a new source-moving
campaign.

---

## Appendix — Read-Only Evidence Commands

- Verified identity with
  `Get-VerifiedMachineIdentity.ps1` (`status=VERIFIED`, `machineId=snd-host`).
- Used `git status`, `git log`, `git branch --merged/--no-merged`,
  `git worktree list --porcelain`, `git cherry`, and tree comparisons.
- Parsed `data/plans/plan-001.json` and `data/tasks.json`.
- Inventoried file counts/bytes and `bin`/`obj` concentration recursively.
- Inspected scheduled tasks, processes, services, shortcuts, environment,
  Run/RunOnce, Startup folders, WMI consumers, configs, and SQ shims.
- Compared release manifests and SHA-256 hashes without promotion.
- Probed `/`, `/data.json`, and `/metrics` read-only with inherited proxies
  bypassed.
- Searched current and retired path prefixes while classifying historical
  backups/logs separately from live consumers.
- Ran docs conflict/status/reference scans; no merge conflict markers were
  present.
