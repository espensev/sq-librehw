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

There is no frontend compilation step. The Windows project includes `web/`
as embedded resources while excluding this README, tests, and fixtures. From
the repository root, verify that integration with:

```powershell
dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

The embedded page still monitors its local source. Machines controls and merged
fleet rendering follow the [fleet specification](../docs/features/feature-tailnet-fleet-view.md).
Independent production hosting needs an explicit source-binding and browser
preference-migration contract; see the [extraction plan](../docs/architecture/dashboard-extraction.md).
