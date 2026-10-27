# Review - machine and version unification

**Date:** 2026-10-27

**Surface:** `4e4f1be7..caab381f`, recovered SND-HOST history `495cde2d..f0dadd06`, and their integration

**Spec sources:** `docs/README.md`, `docs/architecture/upstream-divergence.md`, CSV clock and live-verification feature contracts

**Standards:** `AGENTS.md`, `.codex/skills/project.toml`, owner environment and complete-delivery rules

**Verdict:** PASS after the source corrections below. The later owner clarification
removes the proposed host environment enrollment: local variable names are not
shared prerequisites.

## Findings and resolution

- **High, spec:** matching the two GitHub tips did not establish unified machine
  history. The manifest-selected SND-HOST checkout held six commits through
  `f0dadd06` absent from shared `master`; `git rev-list --left-right --count`
  returned `134 6` before this work. Retrieved a verified Git bundle without
  modifying that checkout and merged its history. CSV clock recovery and
  live-verifier fixes were missing; compact density was already represented by
  `8d075a66`. The current SDK `10.0.400` is preserved. The host operational
  checkout is to pull the shared mirror with its push URL disabled.
- **Medium, regression:** `eng/ci/tests/Test-UpstreamDivergenceLedger.ps1:82`
  previously returned success when the pinned commit was unavailable. Its
  HEAD-only inventory also rejected correctly documented staged additions.
  Regression fixtures reproduced both failures. Missing history now fails;
  file existence uses the tracked working tree.
- **Medium, spec:** the ledger was explicitly local-only: the hosted workflow
  skipped `ci-gates`, and its shallow checkout lacked the upstream base. The
  included `upstream-divergence` gate now runs the comparison and regression
  fixtures with full checkout history. The self-referential suite stays excluded.
- **Medium, regression:** imported `Test-LhmLiveState.ps1` ignored declared
  `rawArguments` and allowed raw declarations to bypass required task paths.
  The checks at lines 324, 337, 357, and 373 now enforce the declared arguments
  and limit exemptions to extra script tasks. Three focused regressions cover
  added switches, the required log engine, and the required live working directory.
- **Low, standards/spec:** the machine table repeated drive-qualified paths;
  the version text claimed the local stamp applied to every build, and the
  candidate fallback assumed a checkout name. Documentation now uses owning
  roots/manifests, states the GitHub Actions exception, and derives the fallback
  from the actual directory name. JSON token expansion is explicit.

## Verification

- `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.slnf -c Release -p:Platform=x64`:
  583 passed, one pre-existing opt-in skip; includes nine CSV clock cases.
- Release x64 WinForms builds for `net10.0-windows` and `net472`: zero warnings/errors.
- `ops/live-verification/Test-LhmLiveStateSystem.ps1`: passed under PowerShell 7 and Windows PowerShell 5.1.
- `eng/ci/Test-LhmCiGates.ps1`: 124 passed per engine; two optional PyYAML skips.
- Original hosted runs `40575375474` and `40575379222`: verified successful on
  `caab381f`; these are baseline evidence, not acceptance of the integration.

## Coverage and portability clarification

All ten files in Claude's final documentation/gate changes were reviewed;
ledger classifications were sampled while path coverage was checked mechanically.
Logger and live-verifier behavior and their regression cases were reviewed.
Historical Phase 7 scope is preserved and explicitly requires reconciliation
with the later dashboard extraction before any campaign starts.

The absence of `MACHINE_*_ROOT` on SND-HOST is not a defect or open enrollment
task. Owner clarification on 2026-10-27 requires shared workflows to accept each
machine's own configuration and explicit paths. The existing interfaces already
support this; the path conventions are documented in `docs/README.md`.
SND-HOST's identity and checkout were verified using its existing local authority.
Live promotion, listener changes, crash tasks, and launcher Apply remain separate
from this source integration.
