# Agent Task — data.json Projection

**Plan:** `plan-008`
**Depends on:** Agent AG
**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/Utilities/DataJsonProjection.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonProjectionTests.cs`

## Goal

Project Agent AG's detached immutable snapshot into the exact current
`data.json` object graph and rewire only that path in `HttpServer`. The existing
golden source and bytes are protected evidence and must remain unchanged.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-008-immutable-sensor-snapshot-and.md`
- Agent AG's committed `Application/Snapshots/SensorSnapshot.cs`
- Agent AG's committed `Adapters/WinFormsNodeSensorSnapshotSource.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`, especially
  `/data.json`, `/Sensor`, Prometheus, `BuildDataJsonObject`, `WriteDataJson`,
  `SendJsonAsync`, `GenerateJsonForNode`, `SanitizeFloat`, and image helpers
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/data.golden.json`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerSensorApiTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerPrometheusTests.cs`

## Pure projector

Create internal static
`LibreHardwareMonitor.Windows.Forms.Utilities.DataJsonProjection`. Its public
within-assembly entry point accepts a non-null `SensorSnapshot` and application
`Version` (or the exact `Major.Minor.Build` string) and returns the existing
`Dictionary<string, object>` shape. Do not serialize inside the projector.

Insert envelope keys in exactly this order:

1. `id` = `0`
2. `Version` = `Major.Minor.Build`
3. `Text` = `Sensor`
4. `Min` = `Min`
5. `Value` = `Value`
6. `Max` = `Max`
7. `ImageURL` = empty string
8. `Children` = a one-element list containing the captured machine root

Assign node IDs depth-first preorder starting at `1` for the captured root and
restart on every projection. Every node starts with `id`, `Text`, `Min`,
`Value`, `Max`; then append subtype keys in the current order:

- Sensor: `SensorId`, `Type`, overwritten formatted `Min`/`Value`/`Max`,
  `RawMin`, `RawValue`, `RawMax`, `ImageURL`, then `Children`.
- Hardware: `HardwareId`, `ImageURL`, then `Children`.
- Group: `ImageURL`, then `Children`.

Use `List<object>` for children and preserve the snapshot's order exactly.
Box finite `float` values unchanged. Map null, NaN, positive infinity, and
negative infinity to null. Do not normalize display strings: values such as
`"NaN %"`, `"Infinity %"`, and `"-"` are contractual. Do not mutate the
snapshot or cache numeric IDs in it.

## HttpServer integration

Keep `BuildDataJsonObject()` and `WriteDataJson(Stream)` signatures unchanged.
`BuildDataJsonObject` must:

1. capture `_root` through Agent AG's source;
2. return the pure projection with `_version`;
3. hold no `Node.SyncRoot` lock after capture returns.

Remove only the superseded `GenerateJsonForNode` function and the hardware/type
image helpers whose exact behavior now lives in the capture adapter. Keep
`HttpServer.SanitizeFloat` because the separate `/Sensor` endpoint uses it.
Leave `_root` in `HttpServer` because `/Sensor` and Prometheus still traverse it.

Do not alter routing, listener ownership, authentication, mutation policy,
headers, CORS, response closure, buffer reuse/gate, ArrayPool copy, gzip,
cancellation, Prometheus, Sensor API, or constructor/MainForm wiring.

## Required tests

Create one class, `DataJsonProjectionTests`, containing exactly three `[Fact]`
methods (no data-row theories):

1. `Project_AssignsEnvelopeAndDepthFirstPreorderIdsWithoutMutatingSnapshot`
2. `Project_PreservesExactPropertyOrderOptionalFieldsAndLegacyValues`
3. `Project_MapsNonFiniteRawValuesToNullAndIsDeterministic`

Construct snapshot fixtures directly through the internal constructors. Assert
dictionary key enumeration order explicitly for the envelope and every node
kind, exact optional-field presence/absence, one root child, empty leaf children,
case-sensitive opaque identities, image URLs, boxed `float` runtime type,
non-finite/null behavior, consecutive preorder IDs, repeated equal JSON bytes,
and unchanged snapshot values/children after projection.

The existing `DataJsonGoldenTests.cs` and `data.golden.json` must not be edited.
Their two existing facts must pass against the rewired HttpServer. Guard the
blob as both Git object
`05113704acc6fefeb4128004b3f523d876fbcec4` and SHA-256
`BEBDE807A7F0037827E16CFBC1F41707701F2BEE7232385C3D2EAEBD2261D2F3`.

## Exit criteria

- Exactly the three exclusive files change.
- Exactly three new projection facts pass; the complete Contracts project is
  exactly 67/67.
- The two unchanged golden facts pass with the protected source/blob unchanged.
- The separate Sensor API and Prometheus contract tests, web dashboard tests,
  and x64 Release `net10.0-windows` and `net472` builds pass.
- No `MainForm`, snapshot/capture source, library, project, package, solution,
  golden, spike, web asset, documentation, tracker, operations, or live-runtime
  file changes.
- The owned change is committed and the handoff contains exact evidence plus a
  proposed tracker row for Agent AI.

## Verification

```powershell
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~DataJsonProjectionTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~DataJsonGoldenTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerSensorApiTests|FullyQualifiedName~HttpServerPrometheusTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git hash-object LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
Get-FileHash -Algorithm SHA256 LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --exit-code -- LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\DataJsonGoldenTests.cs LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\data.golden.json
git diff --check
git status --short
```

## Do not

- Do not edit outside the three exclusive outputs.
- Do not regenerate the golden file or change `DataJsonGoldenTests.cs`.
- Do not move or reuse `SanitizeFloat` in a way that changes `/Sensor`.
- Do not introduce an HTTP service abstraction; that is Plan-009.
- Do not touch `live-tracker.md`; return row text to Agent AI.
- Do not merge your branch or clean another checkout.

## Handoff

Commit with `refactor(http): project data json from snapshots`. Return the
commit SHA, exact focused/full counts, build/web/hash results, file list, any
concern, and one concise proposed tracker row.
