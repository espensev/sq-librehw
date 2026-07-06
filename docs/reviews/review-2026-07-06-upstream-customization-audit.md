# Review - Upstream Sync and Local Customization Audit

**Date:** 2026-07-06
**Surface:** `HEAD` `34e1f09` on `feat/web-dashboard-versioned-routes`, fetched `upstream/master` `9837983`, fetched `origin/master` `a134b54`, plus current working tree
**Spec source:** user request; `AGENTS.md`; `docs/discovery-librehw-sync-upgrade.md`; `docs/local-ui-customizations.md`
**Standards sources:** `AGENTS.md`, `docs/feature-workflow.md`
**Verdict:** PASS WITH NOTES

## Findings

### High

No high-severity findings.

### Medium

No medium-severity findings after the docs update.

### Low

- [axis: standards] The upstream-sync and workflow docs were stale before this pass.
  Evidence: `docs/discovery-librehw-sync-upgrade.md` still described the fork as even with upstream at `abfc4f5`; `docs/feature-workflow.md` listed `feature-web-dashboard-versioned-routes.md` as draft while that spec says `Status: Verified`.
  Impact: future sync/review work could start from the wrong authority.
  Resolution: updated `docs/discovery-librehw-sync-upgrade.md`, `docs/local-ui-customizations.md`, and `docs/feature-workflow.md`.

- [axis: regression] Upstream integration should not be done in-place from the current dirty checkout.
  Evidence: `git status --short --branch` shows 10 modified tracked paths and 9 untracked paths, including route tests, preview assets, route spec, review reports, and v3 plans. `git rev-list --left-right --count HEAD...upstream/master` reports `108 11`.
  Impact: a pull/merge now would mix upstream conflict resolution with in-flight dashboard-route work.
  Recommendation: save or commit the current dashboard work first, then integrate upstream in a dedicated branch.

## Audit Summary

### Upstream Updates

- `git fetch upstream --prune` advanced `upstream/master` from `0c05d35` to `9837983`.
- Incoming upstream commits from the local merge-base are:
  `084aad3` #2382 resource names, `2145344` #2385 DiskInfoToolkit, `469aa97` #2386 NuGet fork guard,
  `10c4f41` #2384 NaN web-server crash, `70df79e` #2389 nightly-link README, `95e97cf` #2390 auth double-encoding,
  `e17be45` #2397 dependency bump, `827de39` #2405 dependency bump, `e8a6249` package upgrades,
  `0c05d35` Dependabot directory, and `9837983` #2411 PawnIO resource lookup.
- Local content already covers or supersedes #2382, #2384, #2386, #2390, and #2411. Package/dependency updates need file-by-file review because the fork uses central package management and is ahead on several package versions.

### Custom Local Changes

- Desktop UI: compact mode, bulk sensor-tree actions, Graph Inputs, graph-local controls, real-time axis labels, long-window plot rendering, text-size/high-DPI scaling, window sizing, and Sev IQ branding.
- Library/data contracts: NVIDIA sensor-id uniqueness, sensor averaging reset, CSV one-column guard, millisecond CSV timestamps, data.json golden tests, and NaN/Infinity-safe JSON.
- Web/server: renamed-assembly resource lookup, PawnIO resource lookup, auth double-encoding fix, fork-safe NuGet publish guard, SQ Telemetry Console replacement, browser-local customization state, and v2/v3 dashboard model work.
- Build/package posture: central package management, local build version stamping without changing `AssemblyVersion`, `net10.0-windows` and `net472` app targets, and fork-specific docs/spec workflow.

### Working Tree Notes

- Dirty tracked paths at audit time: stable web dashboard CSS/JS/HTML, `HttpServer.cs`, card-truth/workflow/planning docs, and web self-tests.
- Untracked paths at audit time: `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs`, `Resources/WebDash/cardtruth/*`, `docs/feature-web-dashboard-versioned-routes.md`, two review reports, and two v3 dashboard plans.
- The versioned-route work should remain labeled in-flight until the current working tree is committed or otherwise handed off.

## Verification

- `git fetch upstream --prune` - pass, upstream advanced to `9837983`.
- `git fetch origin --prune` - pass, origin advanced to `a134b54`.
- `git rev-list --left-right --count HEAD...upstream/master` - pass, `108 11`.
- `git merge-base HEAD upstream/master` - pass, `abfc4f5705419d62cd6000f45a92563415c165fc`.
- `git log --oneline "$(git merge-base HEAD upstream/master)..upstream/master"` - pass, 11 upstream commits inspected.
- `git diff --stat upstream/master...HEAD` - pass, 109 committed local-change paths summarized.
- `git diff --stat` and `git ls-files --others --exclude-standard` - pass, dirty working tree enumerated.
- Product build/test - not run; this was a docs/audit update and no product source was edited by this pass.

## Coverage Notes

- Files reviewed deeply: `docs/discovery-librehw-sync-upgrade.md`, `docs/local-ui-customizations.md`, `docs/feature-workflow.md`, upstream commit list, and git diff summaries.
- Product-code review was limited to sync-sensitive evidence checks in `HttpServer.cs`, `MainForm.cs`, `AuthForm.cs`, `.github/workflows/master.yml`, and package files.

## Open Questions

- Should the current versioned-route working tree be committed before upstream sync, or parked separately?
- Should the upstream README nightly-link-only update be imported during the next sync, or left to a broader upstream merge?
