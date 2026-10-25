# Dashboard source extraction

**Status:** source extraction implemented and locally verified
**Date:** 2026-10-24
**Baseline:** `1cc3af3c`

## Decision and review

The browser dashboard becomes a separately owned frontend under `dashboard/`.
LibreHardwareMonitor remains the hardware owner and telemetry producer, and
embeds that frontend through a small resource declaration. Standard, Studio,
Workspace, and fleet support share one implementation.

The review found these concrete seams:

- `Resources/Web/console.js` combined 3,408 lines of model, persistence,
  polling, and DOM code. Its module-level telemetry maps prevented independent
  model instances. Model tests evaluated the entire file with `SQ_NO_BOOT`.
- `fleet.js` already owns bounded configuration and stable source identities;
  `fleet-poller.js` already owns independent, read-only peer polling. The
  foundation commit changed no C# hardware or HTTP implementation.
- The Windows project embeds `Resources/**`; `HttpServer.TryMapStableWebResource`
  and `ServeResourceFileAsync` resolve `Resources.Web.*` names. Moving source
  must preserve those names and the existing URLs.
- The fleet poller always creates `local` and requests relative `data.json`.
  A standalone-source probe confirmed this even with a separate viewer origin.
  Moving files alone cannot make an independently hosted fleet viewer.
- Source changes currently require coordinated preference validation, saving,
  telemetry acknowledgement, and polling configuration. The next fleet UI
  slice must own that sequence centrally rather than repeat it in views.

## Scope and compatibility

This delivery moves editable web assets and their tests out of the inherited
Windows project. It extracts an independently constructible model and the
existing local polling implementation from DOM boot, and gives Workspace a
small shared sensor-identity dependency. It preserves the existing JavaScript
style and requires no frontend framework, bundler, package installation, or
generated asset copy.

The embedded dashboard keeps its origin, URLs, storage keys, local raw sensor
IDs, peer envelopes, profiles, contexts, graph behavior, keyboard handling,
and all three views. Telemetry schemas, authentication, HTTP mutations,
hardware collection, native UI, and runtime deployment stay under their current
contracts. Both supported Windows frameworks remain required build gates.

## Ownership

| Owner | Responsibility |
|---|---|
| `dashboard/web/identity.js` | Stable sensor references shared by local and fleet models |
| `dashboard/web/model.js` | One viewer's classification, preferences, histories, trends, limits, and ingestion |
| `dashboard/web/workspace.js` | Portable profile and panel model |
| `dashboard/web/fleet.js` | Validated source configuration, imports, and origin associations |
| `dashboard/web/polling.js`, `fleet-poller.js` | Existing local and fleet request lifecycles |
| `dashboard/web/console.js` | Shared toolbar, Standard/Studio/Workspace DOM, and embedded-page composition |
| `dashboard/tests/` | Frontend fixtures and Node/browser regression tests |
| Windows project resource declaration | Embed the same source assets under stable logical names |

Keep the existing local and fleet polling behavior during this structural move.
They have different refresh/admission contracts; their consolidation belongs
with the subsequent fleet session integration, backed by both existing suites.

## Execution plan and acceptance

1. Move assets and tests into `dashboard/`; retarget test imports, gate mappings,
   and current documentation. Keep a single editable asset tree. Embed only
   runtime assets, with explicit stable logical names.
2. Extract model construction and local polling from the DOM implementation.
   Give each model its own telemetry caches and explicit page-origin input.
   Move sensor identity into a shared module without changing stored formats.
3. Verify Node model/identity/lifecycle cases, all existing web assertions,
   embedded resource availability, HTTP/data contracts, both Windows builds,
   and all three views in an isolated fixture browser. Commit and push after
   the checks pass.

Acceptance requires:

- No dashboard source or duplicate copy remains in `Resources/Web`.
- The root page, every script/style/icon it references, and existing resource
  names resolve from the built assembly.
- Model tests load the model directly without a DOM shim; two model instances
  cannot alter each other's histories or animation state.
- Existing local preferences and maximum-length peer Workspace IDs survive
  normalization/import/export unchanged.
- Local boot still uses its existing polling contract, and the independent
  fleet polling and ingestion suites remain green.
- Standard, Studio, and Workspace load, switch, pause, and reload in the
  source fixture; no live hardware or serving configuration is changed.

## Follow-on sequence

1. Done 2026-10-25: the fleet session integration (one owner in `console.js`
   for configuration commits, source receipts, ingestion, persistence,
   cancellation, and gap/removal handling), the Machines panel, and per-source
   rendering under [the fleet contract](../features/feature-tailnet-fleet-view.md).
   `polling.js` remains embedded for the asset contract but no longer drives boot.
2. Specify independent hosting. Make source membership explicit rather than
   treating the page origin as a machine; preserve or explicitly migrate local
   references, source-origin associations, dashboard contexts, and Workspace
   preferences when changing browser origin. Validate browser CORS and the
   credential-free peer request contract. A source-list export alone is not a
   full preference migration.
3. Once both hosting modes pass, a separate dashboard repository can publish
   a version-pinned asset artifact consumed by LibreHW. Keep the embedded
   dashboard available and avoid an unversioned network dependency at boot.
   Endpoint discovery, Serve configuration, and runtime promotion retain their
   separate operational owners.

## Verification

Baseline: 339 synchronous web assertions and 60 Node tests passed. The source
checkout was clean and matched remote `master` at `1cc3af3c` before this work.

Verified on 2026-10-24:

- `node dashboard/tests/selftest.node.js`: 339/339 assertions pass.
- `node --test dashboard/tests/*.tests.js`: 63 tests pass, including independent
  model instances, explicit page origins, and maximum-length shared identities.
- Server Contracts: 115 tests pass, including the compiled entry point's full
  script/style/icon dependency set, nested image resource naming, exclusion of
  fixtures, and the existing HTTP/data golden contracts.
- Release x64 builds for `net10.0-windows` and `net472`: zero warnings/errors.
- `ops/candidate/Test-LhmReleaseSystem.ps1` under Windows PowerShell: 191
  assertions pass. No release candidate or deployment was created.
- `eng/ci/Test-LhmCiGates.ps1` under Windows PowerShell: 101 assertions pass;
  two optional YAML parse checks are skipped because PyYAML is unavailable.
- Isolated Opera fixture smoke: Standard renders 14 primary cards, Studio six
  focus cards, and Workspace eight cards; all scripts and telemetry return 200.
  Pause, theme, view, and Workspace state survive reload; a paused reload still
  renders its startup snapshot. Desktop and 390-pixel Workspace layouts have
  no horizontal overflow, with zero console errors or external HTTP requests.
  The harness allows the existing inline font data URLs; they are not HTTP
  traffic. Its disposable tab and fixture listener were closed.

The production hosting mode, fleet UI, and installed runtime were not exercised
or changed by this extraction. They retain the follow-on gates above.
