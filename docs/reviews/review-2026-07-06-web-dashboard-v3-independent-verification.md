# Review - Web Dashboard V3 Independent Verification

**Date:** 2026-07-06
**Surface:** branch review, `origin/master...HEAD`; working tree was clean before this report was written
**Spec source:** `docs/feature-web-dashboard-card-truth.md`, `docs/feature-web-dashboard-versioned-routes.md`, `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`, current operator request
**Standards sources:** `AGENTS.md`, `docs/feature-workflow.md`
**Subagent passes:** frontend/runtime, build/test/smoke, spec/docs traceability
**Verdict:** FAIL for the original reviewed surface; follow-up fixes are recorded at the end of this report

## Findings

### Medium

- [axis: regression] `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html:5` and `LibreHardwareMonitor.Windows.Forms/Resources/WebDash/cardtruth/index.html:89` - The no-trailing-slash preview URL serves HTML but the browser resolves its relative assets under `/dash/`, leaving the page unstyled and without JS.
  Evidence: `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs:10` asserts `/dash/cardtruth` maps to the preview shell, and live smoke returned `/dash/cardtruth -> 200 text/html`, while `/dash/console.js -> 404` and `/dash/console.css -> 404`. Relative URL resolution from `http://localhost:8085/dash/cardtruth` maps `console.js` to `http://localhost:8085/dash/console.js`.
  Impact: the menu link `/dash/cardtruth/` works, but a directly opened or copied `/dash/cardtruth` route appears supported by tests and server mapping yet fails as an actual browser page.
  Recommendation: redirect `/dash/cardtruth` to `/dash/cardtruth/`, or make preview asset links root-absolute (`/dash/cardtruth/console.css`, `/dash/cardtruth/console.js`) and add a browser/HTML-resolution assertion for the no-slash route.

- [axis: standards] `docs/feature-web-dashboard-card-truth.md:179` - The verification log stops at Slice 0/C1 and stale `selftest 108/108`, but the current branch has implemented Slice 2 hardware identity behavior and now passes `SELFTEST PASS 117/117`.
  Evidence: `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md:99` defines Slice 2 hardware identity, with `hwid` grouping at lines 105, 109, and 118. Product code now keys panels/heroes by `hwid` (`LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js:200`, `:429`, `:454`), and tests cover duplicate NVMe panels/GPU hero hwids (`webtests/console.tests.js:107-120`). `AGENTS.md:76-78` requires running the build, exercising the workflow, and updating the spec verification log after behavior is checked.
  Impact: a future reviewer cannot tell from the spec which v3 slices are actually implemented and verified, and may re-plan or rework already landed behavior.
  Recommendation: update the active spec and v3 plan status/log to record the `08b64b7` Slice 2 hardware-identity commit and current verification results.

- [axis: standards] Changed docs still describe a dirty or in-flight route worktree even though the branch is clean and pushed.
  Evidence: `git status --short --branch` returned `## feat/web-dashboard-v3-card-first...origin/feat/web-dashboard-v3-card-first`. Stale claims remain in `docs/discovery-librehw-sync-upgrade.md:6`, `:186`, `:207`; `docs/local-ui-customizations.md:8`, `:284`, `:300`, `:379`; and `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md:19`, `:393`. `AGENTS.md:70-72` requires linked docs, stale path/name checks, and non-empty verification sections before handoff.
  Impact: upstream-sync and local-customization notes give the wrong operational instruction: they tell the maintainer to save or avoid a dirty route worktree that no longer exists.
  Recommendation: refresh the changed docs to distinguish historical dirty-state notes from the current clean branch state, or move the historical notes into clearly dated verification rows.

### Low

- [axis: standards] `docs/feature-web-dashboard-card-truth.md:4` - The status line says `Draft — scope accepted; implementation in progress`, while `docs/feature-workflow.md:37-48` says Draft is not acceptance by itself and implementation should proceed from an Accepted Spec.
  Evidence: acceptance criteria and verification sections exist, and the user has accepted concrete slices, but the top-level status remains semantically mixed.
  Impact: future implementation/review turns may disagree about whether the entire v3 spec is accepted or only specific slices are accepted.
  Recommendation: split status wording, for example `Status: Accepted for implemented slices; remaining v3 scope draft/in progress`, and list accepted slices explicitly.

- [axis: standards] `git diff --check origin/master...HEAD` fails on trailing whitespace in newly added/changed docs.
  Evidence: the command reported trailing whitespace in `docs/feature-web-dashboard-versioned-routes.md:3-6`, `docs/reviews/review-2026-07-04-web-dashboard-ui.md:3-6`, `docs/reviews/review-2026-07-06-dashboard-menu-gauge-correctness.md:3-6`, and `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md:3-8`.
  Impact: this is not a runtime defect, but it leaves the branch failing a standard whitespace check.
  Recommendation: strip trailing whitespace from the touched docs before merge.

## Verification

- `git status --short --branch` - pass before report write; branch clean and tracking `origin/feat/web-dashboard-v3-card-first`.
- `git diff --stat origin/master...HEAD` - pass; non-empty surface, 25 files changed, 3798 insertions, 140 deletions.
- `git diff --name-status origin/master...HEAD` - pass; reviewed changed file list.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js` - pass.
- `node --check LibreHardwareMonitor.Windows.Forms\Resources\WebDash\cardtruth\console.js` - pass.
- `node webtests\selftest.node.js` - pass, `SELFTEST PASS 117/117`.
- `dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64 --no-restore` - pass, 42/42; existing `xUnit2020` warning in `DataJsonGoldenTests.cs`.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64` - pass, 0 warnings/errors.
- `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64` - initially failed because the live app and MSBuild node reuse locked `bin\Release\net10.0-windows\Aga.Controls.dll`; after stopping the live app and running `dotnet build-server shutdown`, pass with 0 warnings/errors.
- Live app restart - pass; restarted from `bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe`, PID 49544.
- Live HTTP smoke after restart - pass for `/`, `/dash/cardtruth/`, `/data.json`, `/metrics`; `/dash/missing/` returned 404 as expected.
- Live no-slash preview smoke - fail for browser viability; `/dash/cardtruth` returned 200 HTML, but `/dash/console.js` and `/dash/console.css` returned 404.
- `git diff --check origin/master...HEAD` - fail; trailing whitespace in changed docs.

## Coverage Notes

- Deep-reviewed: `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`, `LibreHardwareMonitor.Tests/HttpServerRouteTests.cs`, stable and preview `index.html`, stable and preview `console.js`, stable and preview `console.css`, `webtests/console.tests.js`, `webtests/selftest.node.js`, `docs/feature-web-dashboard-card-truth.md`, `docs/feature-web-dashboard-versioned-routes.md`, `docs/superpowers/plans/2026-07-06-web-dashboard-v3-next-plan.md`, `docs/feature-workflow.md`, `docs/local-ui-customizations.md`, and `docs/discovery-librehw-sync-upgrade.md`.
- Sampled for traceability/staleness: existing review docs, handoff/campaign/visible-correctness plan docs, customization spec docs, and dashboard-template spec docs in the diff.
- Not run: browser visual automation. Runtime checks were HTTP-level plus static asset-resolution checks.

## Open Questions

- Should `/dash/cardtruth` be a supported public route, or should tests require only `/dash/cardtruth/` and the server redirect no-slash URLs?
- Should `origin/master` or local `master` be treated as the merge base for the next integration? `origin/HEAD` points to `origin/master`, but local `master` is a different commit than `origin/master`.

## Follow-Up Resolution

- 2026-07-06: The no-slash preview route was addressed by making the preview shell's CSS and JS links route-root-absolute (`/dash/cardtruth/console.css`, `/dash/cardtruth/console.js`) and adding selftest assertions for those links.
- 2026-07-06: Docs now record Slice 2 hardware-identity verification and refresh dirty/in-flight wording to the current committed branch state.
- 2026-07-06: `cardtruth` is documented as a temporary dev route only. After the selected changes are synced into `/`, the separate route should be retired; any surviving card-truth visual treatment belongs under the root Theme dropdown/view selector.
