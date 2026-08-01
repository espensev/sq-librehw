# Agent Task — Immutable Snapshot Capture

**Plan:** `plan-008`
**Product baseline:** `779d9044ef63b3cb91f547d9306dc543e2eb4ae6`
**Depends on:** none
**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/Application/Snapshots/SensorSnapshot.cs`
- `LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/SensorSnapshotTests.cs`

## Goal

Add the first Phase-4 application contract: an internal, detached, immutable
snapshot of the current WinForms sensor tree and a capture adapter that copies
the tree under `Node.SyncRoot`. Preserve current producer order and values, but
do not claim an atomic all-sensor reading sample.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-008-immutable-sensor-snapshot-and.md`
- `LibreHardwareMonitor.Windows.Forms/UI/Node.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/HardwareNode.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/TypeNode.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/SensorNode.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`, especially the
  current `GenerateJsonForNode`, hardware-image, and type-image helpers
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs`
- `experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorSnapshot.cs`
- `experiments/avalonia-fixture-explorer/LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/SensorNodeSnapshot.cs`

The Avalonia files are design evidence only. Shipping must not reference, link,
retarget, or copy their records, `ImmutableArray`, `double?`, file-loader limits,
or unavailable-value normalization.

## Contract to implement

Create namespace
`LibreHardwareMonitor.Windows.Forms.Application.Snapshots` in
`SensorSnapshot.cs`. Use ordinary internal sealed classes compatible with
`net472`:

- `SensorSnapshotNodeKind` with `Group`, `Hardware`, and `Sensor`.
- `SensorValueSnapshot` with get-only `float? Raw` and `string Display`.
- `SensorNodeSnapshot` with get-only `Kind`, `Text`, `SensorId`, `HardwareId`,
  `SensorType`, `ImageUrl`, `Minimum`, `Current`, `Maximum`, and
  `IReadOnlyList<SensorNodeSnapshot> Children`.
- `SensorSnapshot` with one get-only `Root`.

Constructors may be internal so Agent AH's Contracts tests can build precise
snapshot fixtures through the existing `InternalsVisibleTo`. Defensively copy
every supplied child sequence into a private array and expose it through a
`ReadOnlyCollection<T>` or equivalently non-mutable wrapper. Do not expose the
backing array. Strings, nullable floats, enum values, and immutable child
snapshots are the only retained data. Numeric `data.json` IDs and application
version do not belong in the snapshot; Agent AH derives them during projection.

Do not normalize NaN or infinity during capture. `Raw` retains the exact
nullable `float?` observed; data.json-specific finite/null normalization belongs
to Agent AH's projector. Display strings remain independent of raw availability.

## Capture adapter

Create
`LibreHardwareMonitor.Windows.Forms/Adapters/WinFormsNodeSensorSnapshotSource.cs`.
The adapter may be a static internal source with `Capture(Node root)` or an
equally small internal instance API, provided Agent AH can call it from
`HttpServer.BuildDataJsonObject` without changing the public `HttpServer`
constructor.

Requirements:

- Reject a null capture root before traversal.
- Acquire `Node.SyncRoot` once around the complete recursive copy, then release
  it before returning. Never serialize, project JSON, or perform I/O there.
- Traverse `Node.Nodes` exactly as stored. Do not filter `IsVisible`, sort,
  deduplicate, rename, normalize identity casing, or enumerate `Computer`.
- For every node, read values in the same order as the current data.json walk:
  text; subtype identity/type; formatted minimum/current/maximum; raw
  minimum/current/maximum; image mapping; then children.
- A generic `Node` and a `TypeNode` are `Group`; `HardwareNode` is `Hardware`;
  `SensorNode` is `Sensor`.
- Resolve exact current image URLs during capture. Preserve the pseudo-machine
  fallback `images_icon/computer.png`, sensor
  `images/transparent.png`, every current hardware/type enum mapping, and all
  legacy defaults exactly. Do not derive URLs from `Node.Image`.
- Retain no `Node`, `HardwareNode`, `SensorNode`, `IHardware`, `ISensor`,
  mutable source collection, delegate, or UI object after `Capture` returns.
- Comments must state that `Node.SyncRoot` makes the structure coherent, while
  formatted and raw sensor reads can still interleave with hardware updates;
  the guarantee is detached-after-capture, not one-instant sampling.

## Required tests

Create one class, `SensorSnapshotTests`, containing exactly five `[Fact]`
methods (no data-row theories, so the campaign count remains deterministic):

1. `Capture_PreservesProducerOrderKindsIdentityValuesAndLegacyImages`
2. `Capture_RemainsDetachedAfterTreeNamesAndSensorValuesMutate`
3. `Capture_ExposesReadOnlyChildrenWithoutMutableSourceReferences`
4. `Capture_IncludesHiddenNodes`
5. `Capture_MapsEveryLegacyHardwareAndSensorTypeImageIncludingFallbacks`

Use test-local fake `IHardware` and mutable fake `ISensor` implementations,
in-memory `PersistentSettings`, and `UnitManager`. In the immutability fact,
capture first, then change node text, sensor name/readings, visibility, and child
membership; prove the original snapshot is unchanged. In the collection fact,
prove mutation through any supported non-generic/generic list cast is rejected
and inspect the snapshot property graph so no forbidden source type escapes.
Keep all hardware and type mapping assertions inside the fifth single fact.

## Exit Criteria

- Exactly the three exclusive files change.
- Exactly five new Application facts pass; the complete Application project is
  152 discovered, 151 passed, and the one established live-config opt-in skip.
- The x64 Release `net472` WinForms target builds, proving the new source uses no
  net10-only type or package.
- No `MainForm`, `HttpServer`, library, project, package, solution, golden,
  spike, web, documentation, tracker, operations, or live-runtime file changes.
- The owned change is committed and the handoff contains exact evidence plus a
  proposed tracker row for Agent AI.

## Verification

```powershell
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~SensorSnapshotTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

## Do not

- Do not edit outside the three exclusive outputs.
- Do not add a public contract to `LibreHardwareMonitorLib`, a new project,
  interface, package, or project reference.
- Do not change current sampling/locking semantics to manufacture an atomic
  raw/display tuple.
- Do not touch `live-tracker.md`; return row text to Agent AI.
- Do not merge your branch or clean another checkout.

## Handoff

Commit with `refactor(snapshot): add immutable tree capture`. Return the commit
SHA, exact focused/full counts, net472 build result, file list, any concern, and
one concise proposed tracker row.
