# Feature Spec: Tailnet Fleet View

**Status:** proposed; design options awaiting owner selection, no code changed
**Updated:** 2026-10-23
**Supersedes:** [Unified Tailnet Monitoring View](feature-unified-tailnet-monitoring-view.md)
(static two-iframe page; its Serve routes are dead and its machine list was
hardcoded).

## Problem and motivation

Every LibreHardwareMonitor instance in the fork exposes the same dashboard and
`data.json`, but each is a separate browser origin. The previous unified view
was a static page outside the product with two hardcoded tailnet URLs, a
hardcoded listener port, and Tailscale Serve plumbing that has since died.
The owner wants the multi-machine view to live in this codebase, to use the
tailnet as the transport and trust boundary, and to let each operator choose
which machines appear instead of baking names, ports, or a tailnet domain into
source.

## Goals

- One browser view that shows several machines' telemetry, served by the
  product's own web server on whichever node the operator opens.
- Machine list chosen by the operator; no machine names, tailnet domain, or
  ports in product source or embedded resources.
- Tailscale provides reachability, HTTPS certificates, MagicDNS names, and
  access control. The product only needs reachable `data.json` URLs, so the
  same view works over a LAN or any other HTTPS reverse proxy.
- Peers stay read-only. Write controls (`/Sensor?action=Set`, min/max resets)
  never target another machine.
- `data.json` and `/metrics` wire contracts stay unchanged.

## Non-goals

- Tailscale Funnel or any public exposure.
- Changing SND-DESK HTTPS `443` (owned by Usage Atlas per the old spec).
- Authenticated peers (see Compatibility).
- Server-side merging or proxying of peer telemetry in the first phase.
- A single blended sensor tree that hides which machine a reading came from.

## Design options

### Option A (recommended): Fleet mode inside the web console

`console.js` learns a list of peer base URLs. The poller fetches the local
`data.json` (relative, unchanged) and each peer's `<url>/data.json` with
`Promise.allSettled` and a per-source timeout, then builds one virtual root
whose children are the machine nodes. `data.json` already has the shape
`{ Children: [ <machine> ] }` on every node, and both live nodes send
`Access-Control-Allow-Origin: *`, so no server change is needed.

- Peer sensors are namespaced in the browser model only: `SensorId`,
  `HardwareId`, and `ImageURL` are rewritten as `<key>|<SensorId>`,
  `<key>|<HardwareId>`, and `<url>/<ImageURL>`. Local sensors keep their
  unprefixed ids so existing hidden, pinned, ordered, and Workspace state
  keeps working.
- Standard, Studio, and Workspace render from the same flattened model with a
  machine grouping level and a per-machine freshness chip. One unreachable
  peer marks only that machine stale; the page never blanks.
- Write controls are hidden for peer sensors; the server already rejects
  cross-origin resets.
- Includes a **panes** layout (Option B) as a second rendering of the same
  machine list for operators who want each machine's full dashboard
  side by side.

### Option B: Panes mode only

Move the old unified page into `Resources/Web` as a fleet layout that renders
one iframe per configured machine plus standalone links. Cheapest and fully
independent per pane, but no merged model, no cross-machine Workspace panels,
and N full dashboards polling in one tab. Peers must keep omitting
`X-Frame-Options`, which they do today.

### Option C (deferred): server-side `/fleet.json` aggregation

`HttpServer.cs` fetches peer `data.json` payloads, merges them, and serves a
single-origin fleet document. Removes the browser cross-origin dependency and
would allow authenticated peers, but adds outbound HTTP from the monitoring
process, a new route surface, credential storage, and golden-test adjacency.
Keep as a later layer if cross-origin or authentication becomes a blocker.

## Configuration: who chooses the machines

1. **Browser-local list (phase 1, authoritative).** A `fleet` section in the
   existing normalized `sq.dashboard.v1` state: `enabled`, `layout`
   (`merged` or `panes`), and `sources` with `key`, `label`, `url`. Edited in a
   Machines panel in the toolbar and exported or imported as JSON through the
   same mechanism the Workspace view already uses for profiles.
2. **Optional seed file (phase 2).** `fleet.json` with schema `sq.fleet.v1`
   delivered relative to the dashboard, applied only when the browser has no
   fleet state or when the operator clicks Reload seed. Two delivery routes are
   possible and neither is chosen yet: a Tailscale Serve path handler for the
   file on the same HTTPS port as the proxy (to be verified on a scratch port
   before adoption), or a small read-only `GET /fleet.json` route in
   `HttpServer.cs` that reads the file from the settings directory and also
   works on plain HTTP without Tailscale. The second route amends the
   "routes unchanged" statement in the HTTP server safety spec and needs a
   wire-contract test.
3. **Seed generator (ops, Tailscale-aware).** `ops/tailnet/New-LhmFleetSeed.ps1`
   reads `tailscale status --json`, filters peers by tag or name pattern,
   builds `https://<node>.<MagicDNSSuffix>:<port>` from the status output,
   probes `<url>/data.json`, and writes `fleet.json`. JSON does not expand
   environment variables, so names, suffix, and port are resolved at
   generation time; the script itself derives paths from
   `$env:MACHINE_TOOLS_ROOT` and friends and contains no node names.

## Tailscale exposure contract

Each node that should appear runs one Serve route from its own LHM listener
port to a tailnet-only HTTPS port, for example:

```powershell
tailscale serve --bg --https=<https-port> http://127.0.0.1:<lhm-listener-port>
```

- Proposed convention: the same HTTPS port on every node, so discovery is
  hostname-only. SND-HOST already serves its dashboard this way on `8443`.
  SND-DESK's `8443` currently proxies to a dead local port; repointing it is an
  owner decision recorded in the verification record when made.
- `ops/tailnet/Enable-LhmTailnetServe.ps1` wraps the command: it refuses to
  replace a handler that points at a different target unless told to, never
  runs `tailscale serve reset`, and prints the resulting URL from
  `tailscale status --json`.
- Viewing rights are Tailscale ACL grants on that port. The product's Basic
  authentication stays off for fleet peers.
- Serve state is machine-local runtime state and must be re-verified after a
  Tailscale reset or re-enrollment, as the old spec already recorded.

## Compatibility and risk

- **Mixed content.** An HTTPS fleet page cannot fetch `http://` peers. Every
  peer URL must be HTTPS; the local instance is always fetched relative.
- **SensorId collisions.** Ids such as `/intelcpu/0/temperature/0` repeat
  across machines. All browser state keyed by `SensorId` relies on the
  namespacing rule above; `data.json` itself is never rewritten.
- **Authenticated peers.** Browsers reject `Access-Control-Allow-Origin: *`
  together with credentials, so a peer with Basic authentication enabled
  cannot join fleet mode in Option A. This is a documented limitation, not a
  fallback to weaker auth: the tailnet ACL is the boundary.
- **Per-source staleness.** The single body `stale` class becomes per-machine
  state; the local machine keeps today's behavior.
- **Golden tests.** `DataJsonGoldenTests` are unaffected while `data.json`
  stays untouched. Option C would change that.
- **Frameworks.** Both `net10.0-windows` and `net472` embed `Resources\**`, so
  one web change ships to both.
- **Upstream sync.** `Resources/Web` is already fork-owned; this adds no new
  divergence outside it unless Option C or the seed route is adopted.
- **Peer versions.** Peers are assumed to run this fork's `data.json` shape
  (`Children[0]` is the machine node). Upstream LibreHardwareMonitor produces
  the same root shape, so stock peers should also appear, unverified.

## Acceptance criteria

- [ ] Fleet mode renders the local machine plus every configured reachable peer
      with a visible machine label per group.
- [ ] One unreachable peer marks only that machine stale; local telemetry
      keeps updating.
- [ ] Peer `SensorId`s never collide with local ones in hidden, pinned,
      ordered, or Workspace state; existing single-machine state is unchanged
      after upgrade.
- [ ] Peer images load from the peer origin.
- [ ] No write control is offered for a peer sensor, and no request other than
      `GET <url>/data.json` and image fetches is sent to a peer.
- [ ] The machine list is editable, exportable, and importable in the browser;
      embedded resources contain no node names, tailnet suffix, or ports.
- [ ] Panes layout renders one labelled frame per configured machine.
- [ ] `data.json` and `/metrics` payloads are byte-for-byte unchanged for the
      local machine; the Contracts suite stays green.
- [ ] Serve helper refuses to replace a foreign handler without an explicit
      flag and never resets Serve.

## Verification plan

```powershell
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

- Extend `webtests/fixture.data.json` with a second machine fixture and add
  merge, namespace, image-rebase, and per-source staleness assertions to
  `webtests/console.tests.js`.
- Manual: run the Serve helper on the local node, open the fleet page over the
  tailnet in the isolated DevHome browser, add a peer, confirm both machine
  groups update, stop the peer's Serve route, confirm only that group goes
  stale.
- Production Serve changes and runtime promotion follow the local release
  contract and the installed verifier identity check; this spec grants neither.

## Open decisions

1. Which option to build first: A (recommended), B, or C.
2. Whether to repurpose SND-DESK `8443` for the LHM listener or pick another
   shared HTTPS port on every node.
3. Seed delivery route for phase 2: Serve path handler or HttpServer route.
