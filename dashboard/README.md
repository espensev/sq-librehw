# SQ dashboard

Shared Standard, Studio, and Workspace dashboard, including the fleet
configuration and polling foundation. This directory owns the frontend source
and tests. LibreHW supplies telemetry and embeds `web/` under its existing
resource names; the assets have one editable source and no build-time copy.

`web/model.js` constructs an independent viewer model without a DOM.
`web/identity.js` defines stable sensor references, `workspace.js` owns portable
profiles, `fleet.js` owns source configuration, and the polling modules own
request lifecycles. `console.js` composes the embedded page and its three views.

Run from this directory with Node.js:

```powershell
node tests/selftest.node.js
node --test tests/*.tests.js
node tests/serve-fixture.cjs
```

The fixture command prints an ephemeral loopback URL. It serves the real web
assets with recorded local telemetry, needs no .NET build or hardware access,
and stops with Ctrl+C. It does not proxy live machines. An optional numeric
port can be supplied as the first argument.

For fleet acceptance it can also host up to eight HTTPS peer fixtures:

```powershell
node tests/serve-fixture.cjs 0 --peers 8 --cert <cert.pem> --key <key.pem>
```

Each peer answers only `GET /data.json` (CORS `*`) from the recorded peer
fixture with a distinct machine name. `GET /__peers` on the dashboard host
lists them and `GET /__peer?index=N&mode=ok|error|html|invalid|slow|duplicate`
switches a peer into a fault mode. Supply a throwaway self-signed certificate
and accept it in the isolated browser; nothing is installed system-wide.

There is no frontend compilation step. The Windows project includes `web/`
as embedded resources while excluding this README, tests, and fixtures. From
the repository root, verify that integration with:

```powershell
dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

The embedded page boots on the fleet poller: its local source is implicit and
the toolbar Machines panel adds up to eight HTTPS peers, rendered with machine
attribution under the [fleet specification](../docs/features/feature-tailnet-fleet-view.md).
Independent production hosting needs an explicit source-binding and browser
preference-migration contract; see the [extraction plan](../docs/architecture/dashboard-extraction.md).
