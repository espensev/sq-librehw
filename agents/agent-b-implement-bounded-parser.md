# Agent Task — Implement bounded parser

**Scope:** Implement bounded `data.json` parsing, immutable projection, the
recorded fixture matrix, and adversarial parser tests.

**Depends on:** agent A (`bootstrap-spike-contracts`)

**Output files:**
`LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/BoundedDataJsonFixtureLoader.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/DataJsonProjection.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Core/Parsing/SensorValueProjection.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/normal.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/unavailable.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-before.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/hotplug-after.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/malformed.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Fixtures/oversized-string.json`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/BoundedDataJsonFixtureLoaderTests.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/FixtureReplacementTests.cs`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Parsing/GeneratedLimitCases.cs`

## Exit Criteria

- Stream and file loads enforce every accepted hard limit before publishing a
  snapshot.
- Projection preserves producer hierarchy/order and opaque stable IDs while
  ignoring numeric `id`.
- Null/missing raw values remain unavailable and never become zero.
- Failed loads return typed, operator-safe errors and cannot mutate a previous
  immutable result.
- Six committed fixtures plus generated adversarial inputs cover the complete
  parser matrix.
- Parser tests pass under the isolated xUnit v3 executable.

---

## Context — read before doing anything

1. `AGENTS.md` — conventions, source contract, and baseline commands.
2. `docs/feature-avalonia-fixture-sensor-explorer.md` — input shape, bounds,
   fixture matrix, values, and failure behavior.
3. `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` — ownership and
   dependency graph.
4. `docs/discovery-pre-avalonia-readiness.md` — why this consumer cannot become
   a hardware/settings owner.
5. Agent A's files under
   `LibreHardwareMonitor.Avalonia.Spike.Core/Contracts/` — implement these
   signatures exactly; do not revise them.
6. `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`, especially
   `BuildDataJsonObject`, `GenerateJsonForNode`, and `SanitizeFloat` — inspect
   producer shape only.
7. `LibreHardwareMonitor.Tests/DataJsonGoldenTests.cs` and
   `LibreHardwareMonitor.Tests/data.golden.json` — source for deterministic
   normal/unavailable semantics; never edit them.

---

## Task

### Part 1 — Guard bytes and parse deterministically

Implement namespace
`LibreHardwareMonitor.Avalonia.Spike.Core.Parsing`.

`BoundedDataJsonFixtureLoader` implements the frozen interface:

- File load checks existence and `FileInfo.Length` before opening.
- Stream load accepts seekable or non-seekable input and reads at most
  `MaxInputBytes + 1`, honoring cancellation.
- Never use an unbounded `ReadToEndAsync`.
- Parse with `System.Text.Json`, comments disallowed, trailing commas
  disallowed, and `MaxDepth` from the limits.
- Map cancellation/supersession separately from malformed input.
- Map file-not-found, access, and other I/O failures without exposing secrets
  or stack traces in the operator message.

After parsing, validate every property name and every JSON string value against
`MaxStringCharacters`, including unknown fields. Unknown properties remain
otherwise ignorable.

### Part 2 — Project the current producer shape

Require a root object with a `Children` array. Consume the documented fields
and preserve child order exactly.

Projection rules:

- Count every projected node and reject before exceeding `MaxNodes`.
- Reject a node whose `Children` exceeds `MaxChildrenPerNode`.
- A node with `SensorId` is a sensor; its ID must be a non-empty string and
  unique across the document.
- A node with `HardwareId` and no `SensorId` is hardware; its hardware ID must
  be non-empty.
- Other nodes are groups.
- Missing/empty non-identity `Text` becomes `(unnamed)`.
- Missing/empty sensor `Type` becomes `Unknown`.
- Preserve `ImageURL` only as inert metadata; do not resolve or fetch it.
- Ignore generated numeric `id` for identity and ordering decisions.
- Raw values accept JSON number or null/missing only. A finite number is
  available; null/missing is unavailable. Wrong JSON types reject the shape.
- For available values, preserve the matching producer display string when it
  is non-empty. For unavailable values, force display to `Unavailable`
  regardless of `NaN`, `Infinity`, `-`, empty, or other producer text.
- The returned arrays and nodes must be immutable.

Keep JSON traversal at or below the already bounded depth. Do not use dynamic
objects or deserialize directly into mutable public DTOs.

### Part 3 — Record the fixture matrix

Use the existing 2,015-byte golden payload as the basis for `normal.json`, with
reviewable adjustments only where needed to make the fixture self-describing.
Do not copy machine-specific live payloads.

Create:

- `normal.json`: hierarchy, finite values, stable IDs, escaped/non-ASCII label;
- `unavailable.json`: null and missing values plus missing optional label/type;
- `hotplug-before.json` and `hotplug-after.json`: same source with at least one
  stable sensor removed in the latter;
- `malformed.json`: deliberately truncated/invalid JSON;
- `oversized-string.json`: valid JSON containing a string of 1,025 characters.

All valid fixtures stay comfortably below the 4 MiB byte bound.

### Part 4 — Add red-capable tests

Use xUnit v3 `[Fact]`/`[Theory]` tests. Cover:

- stream/file success and source name/version/counts;
- producer order and exact sensor/hardware IDs;
- numeric `id` ignored;
- unavailable semantics and finite display preservation;
- missing label/type fallbacks;
- hot-plug before/after independent immutable snapshots;
- malformed and wrong-shape failures;
- empty and duplicate sensor IDs;
- file not found and cancellation;
- exact-bound success and bound-plus-one failure for bytes, depth, nodes,
  children, and strings;
- non-seekable stream enforcement;
- previous successful result unchanged after a later failure.

`GeneratedLimitCases` is a test-only generator. It must not write large files
or runtime state.

---

## Constraints

- Own only the listed parser, fixture, and parser-test files.
- Do not edit contracts, projects, central packages, UI files, existing golden
  files, docs, or tracker.
- Do not add networking, retry, history, cache, persistence, or logging.
- Keep error strings deterministic enough for assertions, but assert error
  codes rather than brittle complete messages.
- Never recover an excessive/invalid document into partial success.

---

## Verification

After agent A's project scaffolding is present, run:

```powershell
dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
dotnet build LibreHardwareMonitor.Avalonia.Spike.Core\LibreHardwareMonitor.Avalonia.Spike.Core.csproj -c Release --no-restore
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release -- --filter-class BoundedDataJsonFixtureLoaderTests
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release -- --filter-class FixtureReplacementTests
```

If the runner's filter syntax differs, run the complete isolated test
executable and record that exact command instead.

The project-configured full baseline remains:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
```

Do not create a release candidate from the parallel worktree. Agent D or the
manager runs the baseline after integration and a clean commit.

---

## Do NOT

- Do not edit or call the existing `HttpServer`.
- Do not reference WinForms, `LibreHardwareMonitorLib`, hardware, settings, or
  live paths.
- Do not edit files owned by agents A, C, or D.
- Do not create an HTTP client or live polling placeholder.
- Do not approve, execute, promote, deploy, or launch the campaign.

## Post-completion

Do not edit `live-tracker.md`; agent D exclusively owns it. Return this row in
your completion result:

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-002 | Done | agent-b | bounded parser and fixtures | Fixture-only Avalonia explorer | Summarize bounds, fixture coverage, test totals, and any contract concern. |
