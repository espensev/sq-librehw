# Discovery — Claude Optimization Review

**Goal:** Review the work Claude completed or rejected and identify the safest way to progress it.
**Date:** 2026-08-05
**Status:** complete; bounded source remediation implemented and verified
**Recommended next:** review the local source-only commits; push, candidate, and live gates remain separate

Source paths in this report are relative to
`D:\DevHome\workspaces\librehw-host\checkouts\main`. Operational paths are
relative to `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack`. The Q1-Q6
findings preserve the review-start snapshot; the remediation outcome below is
the later authoritative state for this pass.

---

## Questions

1. What did Claude actually accept, reject, or leave unfinished in the latest relevant session?
2. Which inherited claims are supported by files and command results, and which were only narrative?
3. What is the source checkout's current commit, cleanliness, remote position, and Plan-012 state?
4. Is candidate `0.9.6-20260805-054355164-5ad1047` still exact-current-source and structurally promotable?
5. What do the current manifests and immutable evidence say about live, rollback, task, and log-tooling state?
6. What is the smallest safe next action that advances the work without silently authorizing live promotion?

---

## Findings

### Q1: What did Claude accept, reject, or leave unfinished?

**Answer:** The earlier `e8a653ae` session is historical. Session `a1f777f6` completed the
`5ad1047` live cutover. The latest session, `cf9c42fe`, merged two source optimizations locally:
CSV precision (`72d44e18`) and candidate-lane speedups (`94a1abe5`), followed by merge commit
`0c5e6571`. It deliberately backed out ZIP/hash stream fusion, `git check-ignore --stdin`
batching, archive-pipeline pass removal, and `SmallestSize` compression. It left the branch
unpushed, created no immutable candidate, and made no new deployment.

**Evidence:**

- `C:\Users\Dev\.claude\projects\E--SQ-HQ-Monitoring-LibreHardwareMonitorStack\cf9c42fe-ba77-4b56-86ed-4eed102d5a12.jsonl:825-856` — commits, post-merge gates, rejected changes, and explicit non-push/non-deployment boundary.
- `git log --graph -n 4` — `0c5e6571` merges `72d44e18` and `94a1abe5` over `5ad10473`.
- `deployments/history/20260805-074818-promote-5ad1047/promotion.json:1-83` — the later promotion is already complete.

**Implications:**

- Do not resume the obsolete `9ccaeee` promotion request.
- Review `0c5e6571`, not the pre-cutover `e8a653ae` narrative, as the current source change.

### Q2: Which optimization claims survive independent review?

**Answer:** At review start, the candidate-lane changes were directionally sound and their
measured speedup was credible, but their most important guard proofs existed only in Claude's
temporary scratch files. The logger's size reduction was real, but its fixed precision policy was
too broad to be a safe external CSV contract. Both gaps were remediated in this pass.

**Evidence:**

- `ops/candidate/LhmRelease.Common.ps1:63-88` — the reparse walk now uses
  `EnumerateFileSystemInfos` without weakening the attribute check.
- `ops/candidate/LhmRelease.Common.ps1:239-300` — build-output path discovery is cached per
  resolved checkout and returns a fresh array.
- `ops/candidate/LhmRelease.Common.ps1:354-472` — tracked-file, ignore, process-path, reparse,
  and exact-target guards remain in front of recursive deletion.
- `ops/candidate/LhmRelease.Common.ps1:866-902` and
  `ops/candidate/New-LhmRelease.ps1:336-377` — the middle full verification is replaced by
  path/length/SHA identity, while a full verification still runs after final publication.
- Claude's `scratchpad\verify-guard.ps1` and
  `scratchpad\verify-candidate-identity.ps1` were temporary review-start proof.
- `ops/candidate/Test-LhmReleaseSystem.ps1` now permanently covers a real
  process-loaded output refusal, all discovered output-tree preservation, a
  failed-CIM fail-closed path, defensive cache isolation, and equal-length
  manifest/archive plus add/remove/truncate content mutations.

**Implications:**

- The release-lane optimizations were retained and their safety proofs were
  made permanent before publication.
- The logger was corrected as a source contract; it is not yet a candidate or
  live-promotion decision.

### Q3: What is the current source and campaign state?

**Answer:** At review start, local `main` was `0c5e6571`, three commits ahead of `origin/main` at
`5ad1047`, with one pre-existing unstaged `docs/README.md` update that correctly records the completed promotion.
Plan-012 remains `implemented` with 11 met, 1 open, and 0 waived; criterion 10 cannot be
retroactively satisfied because the required worktree/fast-forward workflow did not occur.

**Evidence:**

- `git status --short --branch` — `main...origin/main [ahead 3]` and `M docs/README.md`.
- `data/plans/plan-012.json:404` and `data/plans/plan-012.json:3053` — criterion 10 requirement and open evidence.
- `docs/campaign-history.md:277-303` — durable 11/1/0 ledger.
- `docs/architecture/refactor-roadmap.md:258-276` — Phase 4 source seam remains complete while criterion 10 stays open.

**Implications:**

- Preserve and commit the promotion documentation separately after review.
- Do not waive Plan-012 criterion 10 or start Plan-014 implicitly.

### Q4: Is the deployed candidate still promotable/current-source?

**Answer:** Its immutable artifacts still validate and its manifest still says promotable, but it
is no longer exact-current-source because local `main` advanced to `0c5e6571` and is dirty. That
is expected after source development and does not invalidate the already completed deployment.

**Evidence:**

- `releases/candidates/0.9.6-20260805-054355164-5ad1047/release-manifest.json:4-32` — clean `5ad1047` source, completed verification, promotable true.
- `deployments/history/20260805-074818-promote-5ad1047/gate2-candidate-acceptance.json:1-12` — strict current-source acceptance passed immediately before cutover.
- Current `Test-LhmReleaseCandidate.ps1 -RequirePromotable` returns `VERIFIED`; adding
  `-RequireCurrentSource` correctly returns `Release candidate does not match the current repository source state.`

**Implications:**

- Leave the immutable candidate and live runtime untouched.
- Any later source deployment needs a fresh candidate from a final clean commit.

### Q5: What is the current operational state?

**Answer:** The live runtime is the promoted `5ad1047` payload and is healthy. Immutable history
and rollback packet inventories validate. The cutover nevertheless had three execution defects:
the executor inferred the candidate ID instead of receiving exact-ID approval, refreshed
`live.json` before the final history packet was sealed, and kept the application down for about
11 minutes. There is also a recovery-tool gap: the packet helper has no guaranteed restart in a
`catch`/`finally` path after it stops the task. These defects should harden the next transaction;
they do not justify rolling back a healthy payload. The first unattended post-reconcile
log-management run is still pending for 2026-08-06 03:45 Europe/London.

**Evidence:**

- `manifests/channels/live.json:1-29` — candidate, version, hash, and completed live verification.
- `deployments/history/20260805-074818-promote-5ad1047/final-live-state.json:1-59` — 11/11 live-state result at closeout.
- `deployments/history/20260805-074818-promote-5ad1047/gate1-record.json:9-14` — approval text was “the new one” / “go”; the executor resolved the candidate ID itself despite the exact-ID gate.
- `docs/PROMOTION-PLAN-20260805-5ad1047.md:237-261` — required history sealing/manifest order and exact-ID authority.
- File UTC times show `channels/live.json` at `07:18:20.774`, final live state at `07:18:51.210`, and the sealed packet manifest at `07:19:30.496`.
- `deployments/history/20260805-074818-promote-5ad1047/shutdown-log.json:15-40` — process absence at `06:56:12Z` and restart at `07:07:13Z`.
- `deployments/rollback/20260805-074818-promote-5ad1047-pre/Restore-LivePayload.ps1`
  stops the task before replacement but has no guaranteed restart/recovery
  block; `docs/OPERATIONS.md:179-183` already forbids standalone `-Execute` use.
- Fresh read-only observation on 2026-08-05: one exact-path process, root task Running, all three endpoints HTTP 200, and the active CSV grew over five seconds.
- Independent path/length/SHA inventory checks matched all 13 history-packet files and all 48 rollback-packet files.

**Implications:**

- There is no runtime incident to repair and no reason to promote another build now.
- Do not manufacture the unattended proof by manually running the scheduled task.
- Before another cutover, define an exact-ID authority check, one stop/replace/start critical section with guaranteed restart/recovery, and a deterministic evidence-sealing/manifest-closeout order.

### Q6: What is the smallest safe progression?

**Answer:** The smallest safe progression was to correct the logger precision profile and its
coverage, make the release safety proofs permanent, fold the promotion truth into a separate
documentation commit, and rerun source gates. That bounded remediation is now implemented. It did
not push, build a candidate, change retention/sample rate, or mutate live state.

**Evidence:**

- `LibreHardwareMonitorLib/Hardware/ISensor.cs:15-39` — all 22 current sensor types now have an explicit tested minimum.
- `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs` — the formatter now keeps a unit
  minimum plus at least four significant digits and falls back losslessly for
  unknown types or values beyond the 15-decimal rounding boundary.
- `LibreHardwareMonitor.Windows.Forms/UI/SensorNode.cs:29-96` — `Factor` and `Timing` already retain three display decimals, while `Data` is expressed in GB.
- A 600-row sample across 424 live columns found 8,400 of 24,000 non-zero `Data` values (35%) and 73 of 599 non-zero `TemperatureRate` values (12.19%) would become zero at two decimals.
- `docs/features/feature-thermal-trends.md:15-38` — the temperature-rate contract requires honest values through CSV and explicitly rejects fabricated zero during unavailable states.

**Implications:**

- `Data`, `Load`, and `TemperatureRate` may use a two-decimal minimum but are no
  longer capped at two decimals; small readings gain decimals as needed.
- The exhaustive map and lossless unknown-type fallback prevent a future
  `SensorType` from silently becoming lossy.

## Remediation outcome

- The safe CSV policy retains at least four significant digits, normalizes
  genuine negative zero, and uses lossless fallback beyond `Math.Round`'s
  15-decimal boundary. Contract coverage includes all 22 current sensor types,
  small scaled/rate values, future types, finite extremes, non-finite values,
  culture, midpoint behavior, and decade-edge relative error.
- A compiled replay mapped each of the archive's 423 sensor columns from its
  identifier to `SensorType`. Across all 9,173 rows, raw bytes fell from
  19,327,926 to 16,023,769 (17.10%). Identical `CompressionLevel.Optimal`
  recompression fell from 3,974,710 to 2,732,768 bytes (31.25%), with zero
  non-zero-to-zero conversions.
- Candidate content/process safety proof now lives in the repository suite.
  The destructive CIM boundary explicitly uses `-ErrorAction Stop`, so direct
  helper reuse cannot fail open under PowerShell's default error preference.
- Final non-deploying verification passed 370 deterministic tests with one
  documented opt-in skip, both WinForms Release builds with zero warnings and
  errors, all nine included gate-runner groups, the direct CI harness at 86/86,
  and the candidate fixture at 135/135 under both PowerShell engines.
- The existing `5ad1047` live runtime, candidate, rollback/history packets,
  task, configuration, manifests, and archive were not modified by this source
  remediation. Normal live CSV growth continued independently.

---

## Rejected-Optimization Review

| Proposal | Review verdict | Reason |
|---|---|---|
| Fuse payload hashing into ZIP writes | Defer | Changing ZIP bytes is not by itself an integrity failure for a newly versioned candidate, but the saved read volume is small and the stream rewrite adds provenance and test complexity. |
| Batch `git check-ignore --stdin` | Keep rejected | A robust binary-stdin implementation is possible, but the current per-path form is easy to audit and preserves exact refusal attribution in a recursive-delete guard. |
| Remove/fuse archive-pipeline passes | Keep rejected | The source hash is needed before conflict selection and destination verification is the last proof before source removal. |
| Use `CompressionLevel.SmallestSize` | Keep rejected | Measured size was effectively unchanged while CPU cost increased roughly threefold. |

---

## Cross-Cutting Analysis

### Constraints

- `LibreHardwareMonitorStack` is operational authority, not source/build space.
- Existing candidates, rollback packets, history packets, and archived logs are immutable.
- A source/candidate gate is not live-promotion authority.
- The branch must remain unpushed unless the maintainer explicitly requests publication.
- Retention and sample-rate changes are separate operator decisions because they discard telemetry.

### Risks

| Risk | Likelihood | Impact | Notes |
|---|---|---|---|
| A future CSV precision change fabricates zero for real small values | low | high | Mitigated by the significant-digit floor, lossless fallback, archive replay, and permanent tests. |
| Release safety proof regresses | low | high | Mitigated by permanent process, failed-inventory, content-identity, and dual-shell fixtures. |
| Current-source failure is misread as live corruption | medium | high | It only reflects source advancement after a valid cutover. |
| Another manual cutover repeats authority/order/recovery defects | medium | high | The last cutover inferred the candidate, sealed evidence late, and had an 11-minute outage. |
| Old staged `9ccaeee` request is resumed | low | high | It is stale and was superseded by the live `5ad1047` cutover. |
| Push/candidate/promotion gates collapse together | medium | high | Each requires a separate decision and fresh evidence. |

### Open Questions

- Does the maintainer want 365-day retention and one-second sampling after the first unattended archive proof?
- Does SND-DESK still need every future `net472` candidate package?

---

## Recommendation

This was a bounded direct remediation, not a campaign. Commit the promotion truth separately from
the logger and release-fixture changes. Keep `5ad1047` live and defer push, candidate creation,
retention/sample-rate changes, and any cutover until the maintainer chooses those gates and the
unattended task proof is observed. Before the next cutover, specify and implement a
transaction-safe SND-HOST promotion/rollback path rather than repeating the manual sequence.
