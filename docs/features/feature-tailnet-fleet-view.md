# Feature Spec: Tailnet Fleet View

**Status:** Option A foundation implemented; Machines and fleet rendering pending
**Updated:** 2026-10-24
**Supersedes:** [Unified Tailnet Monitoring View](feature-unified-tailnet-monitoring-view.md)

The owner selected Option A on 2026-10-23 UTC and requested this refinement on
2026-10-24. The first slice is a browser-local machine list and merged telemetry
in Standard, Studio, and Workspace. Panes, seed tooling, and server aggregation
are deferred. The first implementation step supplies configuration, identity,
polling, and ingestion models with regression tests. The interrupted peer
fixture is retained unchanged and now used by those tests. Browser boot still
uses the existing local poller; fleet UI and end-to-end acceptance are pending.

## Problem and scope

Each LibreHardwareMonitor instance serves its own dashboard and `data.json`.
The former static unified page hardcoded two endpoints outside the product.
Operators need to choose their machines inside the web console, see which
machine owns each reading, and keep monitoring healthy sources when one fails.

Phase 1 changes embedded web resources, web tests, and their verification
registration. It adds no C# route,
server-side outbound request, hardware control, authentication change, or
`data.json`/`metrics` wire change. Machine names, domains, endpoints, and
ports are operator configuration, never embedded defaults. Reachable HTTPS
origins with compatible telemetry and CORS can be used, including tailnet peers.

Fleet is read-only toward peers. Presentation actions such as alias, pin, hide,
order, and range override remain browser-local. No hardware-write or reset
action may dispatch to a peer, even through a direct handler call.

## Configuration and identity contract

### Machine list

A `fleet` section in normalized `sq.dashboard.v1` contains `enabled` and
`sources`. There is one merged layout, so no layout selector or persisted
layout field is needed. Missing fleet state means disabled with an empty list.
The local source is implicit, uses relative `data.json`, has reserved key
`local`, and cannot be removed or have its endpoint edited.

The toolbar's **Machines** panel supports enable/disable, add, rename, reorder,
replace endpoint, remove, and source-list import/export. Disabling fleet stops
peer requests and hides peer groups, retaining their configuration and browser
preferences. Local monitoring continues.

| Field or collection | Contract |
|---|---|
| Sources | At most eight peers, plus the implicit local source |
| `key` | Stable opaque key generated once; 1–32 ASCII characters matching `[a-z0-9][a-z0-9_-]{0,31}`; `local` reserved; unique |
| `label` | Trimmed nonempty text, at most 80 JavaScript string code units; rendered as text, never HTML |
| `url` | At most 2048 characters; absolute HTTPS origin only, with an optional trailing slash; no user information, non-root path, query, or fragment |
| Import | At most 32 KiB of UTF-8; versioned source-list document; all-or-nothing validation |

Normalize URLs with the URL parser: host casing, default HTTPS port, and trailing
slash must not create distinct endpoints. Reject duplicate normalized origins
and the dashboard's own origin. Aliases for the same machine cannot reliably be
detected; label and URL remain visible in Machines.

Renaming or reordering preserves the source key and sensor preferences.
Replacing an endpoint creates a new key, cancels the old source, and clears its
cached telemetry/history; explain before applying that selections for the old
source will become unavailable. Never silently bind old selections to another
endpoint. Removing a source has the same cancellation/cache cleanup behavior.
Missing peer references in profiles remain identifiable as unavailable, not
rewritten to the local sensor with the same raw ID.

### Import, persistence, and compatibility

The source-list document is
`{ "schema": "sq.fleet", "version": 1, "sources": [...] }`, containing only
`key`, `label`, and `url` per source. Export excludes telemetry, credentials,
Workspace profiles, and the fleet-enabled flag. Import replaces the source list
only; it preserves the current enabled flag and unrelated dashboard and
Workspace settings. Display the validated replacement before applying it.

Reject malformed JSON, unknown schema/version/fields, wrong types, excess size
or count, invalid field lengths, duplicate keys/origins, and an existing key
mapped to a different origin. No truncation, partial import, or network requests
occur on rejection. Keys survive an export/import round trip; a removed key
still referenced by stored preferences must not be rebound to another origin.
Retain its key-to-origin association while such references exist.

Use the same validator for Machines edits and persistence. Absent legacy fleet
state defaults cleanly; malformed stored fleet state disables peers with a
visible error without discarding other dashboard settings. Telemetry saves,
view changes, and reloads preserve the normalized fleet section.
Source-list import is distinct from Workspace profile import; neither operation
implicitly imports or replaces the other.

### Sensor and hardware identity

Keep the incoming tree and raw `SensorId`/`HardwareId` unchanged. The browser
model carries source key/label, raw identities, and a separate persistence key:

- Local persistence keys remain the existing raw IDs, byte-for-byte.
- Peer keys use `fleet:<source-key>|<raw-id>`. Source keys cannot contain
  `|`, so splitting the envelope once is unambiguous.
- Classification, alarm bands, hardware relationships, and Workspace preset
  matching use raw identity/type plus source identity, never a prefixed raw ID.
  Hardware joins, fan/control pairing, network adapters, power-limit selection,
  and machine summaries stay within one source.
- Every sensor-keyed cache and preference uses the persistence key, including
  histories, observed peaks, derived limits, aliases, hidden/pinned/order state,
  expanded cards, and Workspace references. DOM identity must also be unique.
- Workspace retains its existing 192-code-unit raw-ID allowance. Validate peer
  envelopes separately, allowing up to 231 code units
  (`fleet:` + 32 + `|` + 192), including import/export. Do not truncate IDs
  or raise the raw-ID limit. Longer raw IDs may be displayed but cannot be added
  to a Workspace selection; report the limit explicitly. Local profiles and
  their existing size/count limits stay compatible.

A virtual root alone is insufficient: today's `rootNode` selects
`Children[0]` and `flatten` drops machine metadata. Adapt that model boundary
explicitly so every configured source reaches all three views.

## Polling and freshness contract

Each enabled source owns its poll controller, last-good snapshot, active request
generation, last-success time, error, and ingestion generation. Reuse the
existing polling lifecycle: selected refresh interval, 15-second timeout,
at most one request per source, and no overlapping retry or catch-up burst.
Each source schedules its next attempt independently. A slow or failed peer
must never delay local or healthy-peer publication behind an all-source batch.

A successful sample requires a successful HTTP response, JSON content type,
parseable JSON, and the expected single-machine `Children` root with a
traversable sensor tree. Duplicate sensor IDs within one source or malformed
tree structure reject that sample. Unknown optional fields and a valid empty
machine tree are allowed. HTTP 200 with HTML is a failure, not health evidence.
Reject redirects for peer telemetry and send no credentials; authenticated or
CORS-incompatible peers remain unsupported without weakening their security.

On success, ingest that source's new sample exactly once using its completion
timestamp. A successful fetch with unchanged values is still a new sample.
Repainting, importing settings, another source's success, or displaying a cached
snapshot must not update its history, motion, peaks, power samples, or derived
limits. Clear cross-gap derivative baselines after failure, pause, or hidden-tab
suspension; do not invent intermediate samples or rates across the gap.

Fresh ingestion and cache retention are separate inputs: ingest only the fresh
source subset; retain siblings according to complete source membership.
Preserve existing telemetry bounds per source (2048 sensor/history/peak keys,
512 power-sample keys, 90 history points per sensor), within the eight-peer cap.
Peers reaching their quotas must not evict local history. Removing/replacing
a source clears its transient and persisted derived telemetry, while user
selections remain unavailable references.

| Source state | Visible behavior |
|---|---|
| Pending | Source label and loading state; no invented readings |
| Current | Last-good readings and browser last-success time |
| Stale | Last-good readings marked stale, age and failure reason; no fresh ingestion |
| Unavailable | No successful sample yet; source label and failure reason remain visible |
| Paused/suspended | Last-good readings explicitly marked paused; no background polling |

Failure or timeout changes only that source to stale/unavailable. Also show
stale if no success arrives within the selected interval plus timeout while
polling is active. Recovery clears the error after one valid new sample.
Browser receipt time measures transport freshness, not each sensor's hardware
sampling time. Historical temperature severity may remain visible but must be
labelled stale, never presented as a current fleet alarm.

Pause, hidden tab, page disposal, fleet disable, source removal, and endpoint
replacement cancel affected requests/timers and invalidate their generations.
Ignore late responses even if abort is not honored; cancellation is not a
transport failure. Resume/visibility restoration starts at most one immediate
request per active source. Preserve existing explicit refresh and startup
one-snapshot behavior when paused, with no continuing timer. Disabling/removing
peers must not stop the local source. Disabling retains preferences but marks
cached peer readings paused until a new success.

## Rendering and peer request boundary

Standard and Studio group readings by machine; Workspace may select sensors
across machines, with machine labels on rows, cards, and graph legends.
Machine labels and freshness remain visible in search, alarms, and expanded
details. Automatic hero/preset choices and summaries are computed per source
before presentation; they must not silently combine two machines' hardware.
When fleet is disabled, preserve today's local layout and selections.

Peer telemetry requests are only `GET <origin>/data.json` with credentials
omitted and redirects rejected. Use bundled dashboard icons for peer readings;
do not load peer-supplied `ImageURL` values. This keeps the peer request surface
to one read-only route and avoids fetching arbitrary image targets.
There are no peer frames, script/style loads, control/reset requests, or automatic
peer-page navigation. Request tests exercise dispatch paths as well as UI
visibility; the server's cross-origin reset check is not the read-only guarantee.

## Deferred work and operational boundary

- **Option B, panes:** separate peer documents need their own read-only,
  authentication, resource-loading, and lifecycle contract before inclusion.
- **Seed tooling/delivery:** no `fleet.json` route or `ops/tailnet` helper in
  phase 1. Future discovery must validate telemetry shape, not just HTTP 200;
  any server route requires a separate HTTP contract review.
- **Option C, aggregation:** server outbound HTTP, credentials, and a new route
  remain a separate design. No implicit fallback when browser access fails.
- **Live exposure:** use already authorized compatible HTTPS endpoints for
  acceptance. No common HTTPS port is required; each configured origin includes
  its port. Serve ownership/port changes and runtime promotion are separate
  operational steps under the applicable release contract.

The 2026-10-24 takeover review observed desktop HTTPS 8443 proxying to a live
HTML service at loopback 3847, while host HTTPS 8443 returned telemetry JSON.
The desktop handler is occupied, not proven abandoned. These are dated
observations, not reusable configuration: recheck `tailscale serve status --json`,
backend ownership, response type/shape, and browser CORS before an operational
decision. Preserve other services' handlers; never reset or repoint Serve as a
side effect of fleet setup. This specification authorizes no such mutation.

Future tooling must derive machine paths/settings from existing authoritative
environment variables, resolve endpoints from operator input or live discovery,
and resolve variables at JSON generation time because JSON does not expand them.

## Acceptance and verification

These are full feature gates. A2–A7 have foundation evidence below; their
remaining UI integration is unchecked. A1/A8 browser fleet acceptance remains
unchecked. A9 source contract/build verification passes for this foundation.

| ID | Acceptance criterion | Required evidence |
|---|---|---|
| A1 | Local plus eight peers render with stable machine attribution in Standard, Studio, and Workspace; disabled mode preserves the local view | Model and browser cases for zero, one, and eight peers, including a valid empty machine |
| A2 | Repeated raw sensor/hardware IDs across sources cannot collide or change CPU/GPU/network classification, alarm severity, presets, joins, or browser preferences | Peer fixture collision tests; 99-degree CPU remains critical; source-isolated fan/control and power-limit tests |
| A3 | Healthy sources publish independently; only the failed source becomes stale/unavailable and recovers | Fake-clock tests for slow peer, timeout, HTTP error, HTML, invalid JSON/tree, duplicate IDs, CORS failure, and recovery |
| A4 | Each valid source sample is ingested once; cached data/redraws never manufacture telemetry or evict sibling history | Unchanged-value success versus cached render; interleaved source successes; no derivative across failure/pause gaps; per-source cache quota isolation |
| A5 | Cancellation and stale-response suppression hold across lifecycle changes | Async tests for pause/refresh, hidden/resume, disable, remove, endpoint replacement, disposal, and late completion after abort |
| A6 | Machine edits/import/export obey all bounds and retain unrelated state; keys never silently rebind | Boundary and round-trip tests, duplicate canonical URLs, rejected atomic imports, rename/reorder, endpoint replacement, retained missing references, reload/telemetry-save preservation |
| A7 | Existing local Workspace IDs/profiles remain valid; maximum peer envelopes survive profile import/export | 192-character raw local and peer IDs, 231-character peer envelope, explicit over-limit rejection, unchanged profile count/size caps |
| A8 | Peer traffic is read-only by construction | Mock dispatch and browser network trace: only credential-free data.json GETs; no peer writes, images, frames, redirects, or scripts |
| A9 | Local server wire contracts and both supported framework builds remain intact | Contracts suite and both Windows builds; compare deterministic contract fixtures, not changing live readings |

Run from the source repository for the implementation gate:

```powershell
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js webtests\fleet.config.tests.js webtests\fleet.polling.tests.js webtests\fleet.model.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Preserve the existing local fixture and use the interrupted peer fixture for
collision cases. Add deterministic clock/transport fixtures to the existing
web suites; the synchronous selftest alone does not run async polling tests.

Browser acceptance uses the isolated DevHome browser and temporary source
servers/proxies with representative fixtures. Exercise add/edit/remove,
keyboard access and validation, reload, all three views, mixed-source Workspace
panels, failure/recovery, pause/visibility, and network request boundaries.
Simulate faults through test fixtures; do not stop live Serve routes.
Final tailnet acceptance requires compatible authorized endpoints, successful
browser CORS, labelled fresh/stale evidence, and the separate release process.
No admin or hardware access is needed for the browser model/tests; native
framework builds and installed-runtime acceptance remain distinct.

## Implementation order and current evidence

1. Completed: configuration/identity normalization and per-source polling/
   ingestion with deterministic foundation tests for A2–A7.
2. Next: integrate Machines and all three views, then satisfy A1/A8 browser checks.
3. Run the full verification commands above and record evidence against A1–A9
   before candidate/release work.

### Foundation implementation (2026-10-24)

- `fleet.js` owns strict source validation, canonical origins, bounded atomic
  imports, immutable edits, origin associations, and identity envelopes.
  `fleetOrigins` and `fleetError` accompany `fleet` in dashboard storage.
  The association registry fails closed at 4096 entries; explicit pruning
  requires complete preference references.
- `fleet-poller.js` owns independent source requests, admission, receipts,
  statuses, monotonic generations, cancellation, and gap/removal callbacks.
  It is loaded as a browser module but is not yet the dashboard's boot poller.
  A transport that ignores abort retains request ownership until settlement;
  its timeout is reported and late data discarded while siblings continue.
- `console.js` projects source-aware identities without changing wire nodes,
  partitions every telemetry bound by configured source, and separates fresh
  source ingestion from full membership retention. Trends retain displayed
  history while refusing derivatives across a gap.
- `workspace.js` accepts raw IDs up to 192 and peer envelopes up to 231,
  rejects invalid explicit edits, and selects presets within each source
  before sharing the existing 24-sensor panel allowance.
- The three fleet suites are registered in
  `.codex/skills/project.toml` alongside the existing web suites.

Machines integration must use `SQ.fleetSourceOptions` with the active
dashboard, parked Standard contexts, and Workspace state when validating
edits/imports. It collects missing references and prevents unknown keys from
being rebound. Apply changes through `SQ.applyFleetState`, save the dashboard,
call `SQ.acknowledgeTelemetrySave`, and then configure the poll coordinator.
This order prevents a telemetry save from restoring the previous source list.
Successful coordinator callbacks call `SQ.ingestSourceTelemetry`; gap callbacks
call `SQ.resetSourceDerivatives`; removal callbacks call
`SQ.removeSourceTelemetry`. After telemetry persistence, acknowledge the save
so deletion queues remain bounded by the last saved keys.

Verification: 339 synchronous assertions and 60 Node tests pass. The latter
include 10 configuration, 21 polling, 11 model integration, and 18 existing
polling/Workspace tests. Server Contracts pass 114/114. Both Release x64
Windows Forms builds (`net10.0-windows`, `net472`) pass without warnings or
errors. Two independent source reviews found no remaining blocking findings
after fixes to generation reuse, deletion queues, null preset entries,
browser-origin fallback, and suspension status.

An isolated Opera fixture smoke loaded all modules and exercised the existing
local Standard, Studio, and Workspace views with successful resource and
telemetry responses. The real-HTTP Node fixture tests use ephemeral loopback
servers. Neither check establishes HTTPS peer CORS, Machines controls, merged
fleet rendering, or installed-runtime acceptance. Those remain next-slice and
release gates. The peer fixture retains SHA256
`8FC42562E6ADCBD168C5AD8EDD54DB64F885B86B9242A20C7803F09AC74BBB3B`.

Remaining operational decisions are desktop endpoint ownership/port and later
release promotion. They do not block phase-1 fixture-based implementation.
