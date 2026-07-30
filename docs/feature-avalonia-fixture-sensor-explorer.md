# Feature Spec: Fixture-only Avalonia Sensor Explorer

**Status:** automated source gate passed; post-merge candidate inspection and
attended smoke pending
**Updated:** 2026-07-30
**Scope:** non-shipping feasibility spike
**Discovery input:** `docs/discovery-pre-avalonia-readiness.md`

## Problem and motivation

LibreHardwareMonitor's current WinForms application owns hardware discovery,
polling, settings, HTTP, logging, task lifetime, and the shipping UI. A future
alternate presentation host may be useful, but proving that direction inside
the current process would compete with reliability work and could accidentally
create a second hardware or settings owner.

This spike answers a narrower question: can Avalonia render an honest,
bounded, keyboard-usable sensor hierarchy from the existing read-only
`data.json` contract without referencing WinForms, opening hardware, or joining
the shipping runtime?

## Decision summary

- Build a separate `net10.0` Avalonia desktop spike and separate spike solution.
- Pin the current stable Avalonia package family at `12.1.0`.
- Parse recorded or user-selected `data.json` files only.
- Convert accepted input into immutable spike-local snapshots and view models.
- Keep WinForms as the sole hardware, HTTP, settings, task, and release owner.
- Do not add the spike to `LibreHardwareMonitor.sln` or the WinForms release
  candidate.

The package choice was checked against current official metadata on 2026-07-30:

- [Avalonia installation requirements](https://docs.avaloniaui.net/docs/get-started/install-avalonia)
- [Avalonia 12.1.0](https://www.nuget.org/packages/Avalonia/12.1.0)
- [Avalonia.Desktop 12.1.0](https://www.nuget.org/packages/Avalonia.Desktop/12.1.0)
- [Avalonia.Themes.Fluent 12.1.0](https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.0)
- [Avalonia.Headless.XUnit 12.1.0](https://www.nuget.org/packages/Avalonia.Headless.XUnit/12.1.0)
- [xUnit v3 3.2.2](https://www.nuget.org/packages/xunit.v3/3.2.2)

## Goals

- Prove an isolated Avalonia project can restore, build, start, and render on
  the repository's pinned .NET 10 SDK.
- Consume the existing producer-side `data.json` shape without changing its
  bytes, identifiers, hierarchy, or ordering.
- Render hardware, type, and sensor hierarchy with label, sensor type,
  current/min/max values, and stable identity.
- Treat null, missing, and non-finite producer readings as unavailable rather
  than zero.
- Reject malformed or excessive input before it can create unbounded memory,
  recursion, node, or string growth.
- Cover the parser, immutable projection, view models, and core window behavior
  with deterministic tests.
- Leave a clear evidence packet for deciding whether a later read-only polling
  milestone is worth specifying.

## Non-goals

- No live HTTP polling, endpoint configuration, authentication, retry, backoff,
  or history.
- No reference to `LibreHardwareMonitor.Windows.Forms`.
- No `Computer.Open()`, `LibreHardwareMonitorLib` hardware enumeration,
  PawnIO/ring access, administrator manifest, or hardware control.
- No POST, reset, setting, fan, clock, power, or other mutation path.
- No read or write of current `PersistentSettings`, runtime configuration,
  Workspace storage, CSV logs, or task state.
- No shared snapshot/profile extraction from WinForms and no claim that the
  spike DTO is the future Phase 5 contract.
- No addition to `LibreHardwareMonitor.sln`, existing test projects, WinForms
  packaging, candidate payloads, scheduled tasks, Start Menu launchers, or the
  live runtime.
- No migration, replacement, feature-parity, packaging, promotion, or cutover
  decision.

## Project and dependency boundary

The serialized integration owner creates:

- `LibreHardwareMonitor.Avalonia.Spike.slnx`;
- `LibreHardwareMonitor.Avalonia.Spike.Core`, a dependency-light `net10.0`
  class library for bounded parsing and immutable snapshots;
- `LibreHardwareMonitor.Avalonia.Spike`, a `net10.0` desktop executable;
- `LibreHardwareMonitor.Avalonia.Spike.Tests`, an isolated `net10.0` test
  executable.

The root central package file registers, without changing existing versions:

- `Avalonia`, `Avalonia.Desktop`, and `Avalonia.Themes.Fluent` at `12.1.0`;
- `Avalonia.Headless.XUnit` at `12.1.0`;
- `xunit.v3` at `3.2.2`.

The spike test executable uses the xUnit v3 in-process runner through
`dotnet run`; it does not switch the repository-wide .NET 10 test runner or
migrate the existing xUnit v2 test project. No ReactiveUI, DI container,
telemetry, updater, or diagnostics package is needed for this milestone.

## Input contract

### Accepted source

- A bundled recorded fixture selected from the spike UI; or
- one explicit local `.json` file selected by the operator or supplied as the
  sole command-line argument.

Opening a file is read-only. Loading another file atomically replaces the
current immutable snapshot only after the complete new document passes.

### Shape consumed

The parser consumes the current producer fields:

- root `Version` and `Children`;
- every node's `Text`, `Min`, `Value`, `Max`, `ImageURL`, and `Children`;
- sensor `SensorId`, `Type`, `RawMin`, `RawValue`, and `RawMax`;
- hardware `HardwareId`.

Unknown properties are ignored for forward tolerance. The generated numeric
`id` is never used as identity. `SensorId` and `HardwareId` remain opaque,
case-sensitive strings and are never rewritten.

### Hard limits

| Limit | Version-one value | Enforcement |
|---|---:|---|
| Input bytes | 4 MiB | Reject before JSON parsing |
| JSON depth | 32 | Bounded parser option |
| Total nodes | 16,384 | Count while projecting; reject whole load |
| Direct children per node | 4,096 | Reject whole load |
| Characters per string | 1,024 | Reject the offending document |
| Stable sensor IDs | 16,384 unique | Empty or duplicate IDs reject the document |
| Retained snapshots | 1 | Atomic replace; no history |
| Concurrent loads | 1 active | A newer request cancels or supersedes the older request |

The byte, depth, node, child, string, and identity checks apply before the new
snapshot becomes visible. A rejected document never produces a partial tree.

### Value and failure semantics

- A finite raw value may use its matching producer-formatted string.
- Null or missing raw current/min/max values display `Unavailable`; formatted
  `NaN`, `Infinity`, `-`, empty, or stale text never becomes a numeric zero.
- Missing `Type` displays `Unknown`.
- Missing or empty non-identity labels display `(unnamed)`.
- Empty or duplicate `SensorId`, invalid JSON shape, invalid numeric tokens,
  excessive input, or I/O failure produces a typed load error.
- On a failed later load, retain the last accepted snapshot and show a clear
  rejection banner. On an initial failure, show the error and an empty state.
- Replacing a fixture with a hot-plug/removal fixture removes absent nodes
  atomically; the spike does not retain or synthesize stale sensors.

## Recorded fixture matrix

Committed fixtures must be deterministic, human-reviewable, and contain no
machine-specific credentials or mutable runtime state.

| Fixture | Required proof |
|---|---|
| `normal.json` | Current hierarchy, all stable IDs, finite values, escaped/non-ASCII text |
| `unavailable.json` | Null and missing raw values, missing optional type/labels |
| `hotplug-before.json` | A stable sensor set before removal |
| `hotplug-after.json` | One or more sensors absent; replacement is atomic |
| `malformed.json` | Invalid/truncated shape returns a typed parse error |
| `oversized-string.json` | A string over 1,024 characters is rejected |

Tests generate byte-limit, node-limit, child-limit, depth-limit, and duplicate-ID
inputs in memory so the repository does not carry multi-megabyte adversarial
fixtures.

## User-visible behavior

The spike opens a single desktop window titled
`LibreHardwareMonitor Avalonia Fixture Explorer`.

- A source area identifies the active fixture or file.
- `Load fixture` selects a bundled case.
- `Open data.json` opens one local JSON file read-only.
- A status area shows accepted/rejected state, version, total node count, and
  sensor count.
- The main view presents the original hierarchy in producer order.
- Sensor rows expose name, type, current, minimum, maximum, and stable ID.
- Hardware rows expose their stable hardware ID.
- Keyboard users can move through, expand, and collapse the hierarchy.
- Loading and error states are explicit; the previous valid snapshot remains
  visually distinguishable after a rejection.
- The window contains no mutation, settings, save, task, launch-at-startup,
  endpoint, or hardware controls.

No preference, window position, recent file, expansion, or selection state is
persisted in milestone one.

## Affected surfaces

| Surface | Effect |
|---|---|
| New spike solution/projects | Added outside the shipping solution |
| `Directory.Packages.props` | Additive version registrations only |
| Existing WinForms source | None |
| Existing `data.json` producer | None; golden bytes must remain identical |
| Existing settings and storage | None |
| Existing routes/API | None |
| Existing release candidates | Spike projects excluded |
| Logs and telemetry | None |
| Live task/runtime | None |

## Compatibility and risk notes

- Avalonia 12 headless xUnit support uses xUnit v3. The isolated test executable
  must not upgrade or retarget `LibreHardwareMonitor.Tests`.
- The root uses Central Package Management. Only the serialized bootstrap owner
  edits `Directory.Packages.props`; every other contributor treats it as frozen.
- The spike targets `net10.0` and does not claim `net472` support. Both existing
  WinForms targets must continue building unchanged.
- The separate spike solution prevents normal WinForms solution builds and
  release scripts from discovering the experimental projects.
- The parser is a consumer adapter, not a new external schema authority.
- A runnable prototype can be mistaken for a replacement. The title, docs,
  solution name, and status copy must consistently say fixture explorer/spike.

## Acceptance criteria

- [ ] The compact spec and campaign plan are reviewed, accepted, committed, and
      followed by an independently verified clean/promotable candidate from
      that exact commit before implementation starts. Candidate creation
      internally verified the clean/promotable checkpoint, but the independent
      dual-shell current-source verification was not recorded before Agent A
      started and cannot be repaired retrospectively.
- [x] The separate spike solution restores and builds with the pinned stable
      package versions and .NET SDK `10.0.302`.
- [x] Existing `LibreHardwareMonitor.sln`, WinForms project files, existing test
      project, and `global.json` remain unchanged.
- [x] The parser enforces every hard limit and never exposes a partial document.
- [x] Fixture tests cover normal, unavailable, hot-plug removal, malformed,
      overlong, byte, depth, node, child, and duplicate-ID cases.
- [x] The UI renders producer order, labels, types, current/min/max values,
      stable IDs, and honest unavailable states from immutable data.
- [ ] Keyboard expansion/navigation and initial/error/empty/loaded states pass
      headless tests and one attended local smoke. The automated headless half
      passes; the attended normal-user smoke remains pending.
- [x] No project references WinForms or opens hardware; no admin manifest,
      POST/control, settings write, task, packaging, or live integration exists.
- [x] Existing `DataJsonGoldenTests` remain byte-identical.
- [x] Existing .NET tests and both x64 Release WinForms targets remain green.
- [ ] The release-system fixture remains green and a source-output inventory
      proves the spike is absent from WinForms candidate packages. The
      114-assertion fixture passes; exact-source package inspection is the
      manager's pending post-merge candidate gate.
- [x] Verification evidence and the decision on a separately specified polling
      milestone are recorded here before the spike is called complete.

## Verification plan

Implementation must leave copy-pasteable commands in this section. The minimum
automated gate is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Test-AvaloniaSpike.ps1
dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
dotnet build LibreHardwareMonitor.Avalonia.Spike.slnx -c Release --no-restore
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
```

## Automated evidence — 2026-07-30

The implementation launch baseline was clean commit
`e4a722082ae21955d6b535aafd9b7c296685906d`, represented by immutable
candidate `0.9.6-20260730-181737576-e4a7220`. Candidate creation completed its
internal verification and recorded `Promotable=True`. The required independent
Windows PowerShell and PowerShell 7
`-RequirePromotable -RequireCurrentSource` calls were not recorded after
creation and before Agent A launched. A pre-create check correctly rejected the
older candidate, but that is not proof for this candidate. The historical
timing criterion therefore remains unmet; a post-merge endpoint candidate can
prove the final source checkpoint but cannot rewrite launch history.

The integrated source gate ran from Agent D's isolated worktree on the reviewed
A-C base `33cc19b50263184db4bd8ea6734d1f4d0bf8d522`, including Agent D's five
owned working-tree files. The manager must repeat candidate creation and
current-source/promotable inspection after Agent D's committed result is merged;
there is deliberately no post-implementation candidate ID or hash yet.

| Evidence | Result |
|---|---|
| Toolchain | SDK `10.0.302`; runtime `.NET 10.0.10`; Avalonia, Desktop, Themes Fluent, and Headless XUnit `12.1.0`; xUnit v3 `3.2.2` |
| Clean spike solution build | Passed with one `AVLN3001` warning and zero errors |
| Agent A bootstrap | Three isolated projects restored; Core Release build passed; frozen contracts and composition root reviewed; release-system fixture `114/114` |
| Agent B parser/fixtures | `49/49` parser and replacement cases |
| Agent C shell | `19/19` view-model and headless cases |
| Agent D integration | Seven real-loader-to-real-view-model cases: normal, unavailable, hot-plug replacement and old-snapshot immutability, two typed retained-rejection cases, deterministic supersession, and numeric-ID exclusion |
| Complete spike runner | `75/75` passed, zero errors, failures, skips, or not-run cases; the pre-D total was 68 |
| Isolation | 37 spike source/project inputs checked against 11 precise forbidden-reference rules; three project references remained inside the spike; zero manifests or forbidden matches; shipping solution listed zero spike projects |
| Existing regression | 259 total: 258 passed, one established live-config memory-budget test skipped, zero failed; `DataJsonGoldenTests` passed unchanged |
| Shipping builds | x64 Release `net10.0-windows` and `net472`: both passed with zero warnings and zero errors |
| Release fixture | `114/114` assertions; this is release-system behavior proof, not candidate-package inspection |
| Live boundary | No candidate creation, promotion, deployment, process launch, task, setting, log, release-store, rollback, or live-runtime mutation by Agent D |

The observed clean spike solution build emitted Avalonia warning `AVLN3001` for
`Views/MainWindow.axaml` and zero errors because the window intentionally
requires injected constructor arguments. Direct construction, XAML loading,
focus, keyboard, and headless behavior pass; the warning remains a known
source-spike issue and does not substitute for the pending attended smoke.

Post-merge candidate gate — **pending, manager only**:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource
pwsh -NoProfile -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource
```

- Post-merge candidate ID: pending.
- Exact source commit/hash: pending.
- WinForms package inventory proving no spike output: pending.

Attended smoke:

**Status: pending.** No attended item below is recorded as passed.

1. Start the spike as a normal, non-elevated user.
2. Load each bundled valid fixture and confirm hierarchy/order/counts.
3. Replace `hotplug-before` with `hotplug-after` and confirm absent sensors
   disappear without stale or zero readings.
4. Attempt malformed and oversized cases and confirm the last valid snapshot
   remains with an explicit rejection.
5. Navigate, expand, and collapse without a mouse.
6. Confirm no LibreHardwareMonitor process, task, HTTP listener, settings, log,
   release store, or live runtime file changes.

## Parallel ownership boundary

The first implementation change is serialized: package versions, separate
solution/project scaffolding, and immutable contracts land under one owner.
After that bootstrap is reviewed, parser/fixture work and Avalonia
view/view-model work may run in parallel because they own disjoint new files
and meet at the frozen loader/snapshot interfaces. A final integration owner
combines their outputs, runs the complete gate, and updates this evidence.

All contributors must avoid:

- `LibreHardwareMonitor.sln`;
- `LibreHardwareMonitor.Windows.Forms.csproj`;
- `LibreHardwareMonitor.Tests.csproj`;
- `MainForm.cs`, `Computer.cs`, `HttpServer.cs`, `PersistentSettings.cs`, and
  `RuntimePaths.cs`;
- WinForms release, promotion, task, and live-runtime code.

## Rollback

Until separately approved, the spike is isolated and non-shipping. Rollback is
a normal revert of the new solution/projects, additive package-version entries,
and documentation. No runtime rollback is needed because this spec authorizes
no deployment, task, settings, or live mutation.

## Future gate

The automated fixture evidence makes a separate read-only HTTP polling spec
**worth specifying** after the pending candidate and attended gates. This
decision adds no polling code or authority to the current spike. That later
spec must independently define endpoint selection, authentication, TLS/trust,
cancellation, retry/backoff, stale age, update cadence, payload history,
process ownership, and failure behavior. It still cannot authorize hardware
ownership, packaging, task replacement, or cutover.
