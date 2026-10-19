# Feature: PawnIO platform gating

**Status:** reviewed implementation on `fix/f-013-pawnio-os-gate`, integrated
with current `master`; platform verification is in progress before delivery.
**Origin:** grand-sweep finding F-013 (verified high) —
`.audit/crew/librehw-grand-sweep-20261003-1e5346a3/FINDINGS.md`,
preserved at
`archive/pre-cleanup-20261019:docs/reviews/audit-librehw-grand-sweep-20261003.md`.

## Problem and motivation

`PawnIo`'s static constructor probed the Windows registry unconditionally and
`LoadModuleFromResource` P/Invoked `CreateFile` on a Windows device path. The
library ships Linux RIDs; on Linux, constructing any PawnIO-backed module
(`AmdFamily17`, `RyzenSmu`, `IntelMsr`, the LPC/EC bridges, …) threw
`TypeInitializationException`/`DllNotFoundException`, which escaped
`Computer.Open` for Zen CPUs and rolled back the entire open.

## Goals

- On non-Windows platforms, PawnIO reports "not installed / not loaded" instead
  of throwing: `Version == null`, `IsInstalled == false`, module loads return a
  not-loaded instance whose `Execute` returns zeros, `ExecuteHr` returns success
  with zero returned elements and leaves caller buffers unchanged, and `Close`
  no-ops. These preserve the existing unloaded-module contract.

## Non-goals

- Making PawnIO function on non-Windows (it is a Windows driver interface).
- Changing `Amd17Cpu` construction on Unix (the not-loaded module degrades it
  exactly like a Windows machine without PawnIO installed; a constructor
  early-return like `IntelCpu`'s would fork Unix sensor-creation behavior
  further than this defect requires — separate decision if ever wanted).

## Behavior

- Windows: registry lookup (normal view, then Registry64/WOW64 view) and device
  open behave exactly as before, including static-constructor timing.
- Non-Windows (`Software.OperatingSystem.IsUnix`): the registry probe and the
  device `CreateFile` are both skipped; `FindInstalledVersion` returns `null`
  and `LoadModuleFromResource` returns a not-loaded instance.

## Compatibility

- No public API changes; two internal members added (`FindInstalledVersion`,
  a three-arg `LoadModuleFromResource` overload) for the regression tests.
- net472 / netstandard2.0 / net8–net10: `Software.OperatingSystem` uses only
  `Environment.OSVersion` (TFM-safe; no .NET 5+ APIs).
- A separate net10.0 console fixture runs against the portable library rather
  than loading a Windows-targeted test assembly. It is outside the deterministic
  Windows solution filter and performs no hardware discovery or driver I/O.

## Acceptance criteria

1. `PawnIoPlatformGateTests.FindInstalledVersion_OnNonWindows_ReturnsNullWithoutThrowing` passes.
2. `PawnIoPlatformGateTests.LoadModuleFromResource_OnNonWindows_ReturnsNotLoadedInstance` passes.
3. Windows registry lookup semantics unchanged (two-step probe preserved verbatim).
4. A real Linux process reads `Version`/`IsInstalled`, calls the production
   two-argument loader with a missing resource, checks unloaded execute/close
   behavior, and constructs representative AMD/Intel/SMU wrappers without
   registry/native-library failures. No injected Unix flag counts as Linux proof.
5. The Windows fixture checks metadata and injected unsupported-platform paths
   without opening the installed PawnIO driver. Both application frameworks,
   all library targets, and the full Windows deterministic suite pass.

## Portable verification

`eng/ci/fixtures/PawnIoPortable` owns the standalone fixture. The
`pawnio-portable` gate in `.codex/skills/project.toml` runs it through
`eng/ci/Invoke-LhmGates.ps1 -Gate pawnio-portable`. On Windows it explicitly
reports injected guard coverage; on Linux it requires the real platform path.
`.github/workflows/pawnio-portable.yml` runs that gate on Ubuntu and Windows
with read-only repository permissions. It is a test workflow, not deployment.
Record the exact hosted commit and run URL before closing Linux acceptance.

## Verification record

- Pre-patch: both tests red (missing seam, compile errors CS0117/CS1739) —
  2026-10-03.
- Post-patch: 2/2 gated tests green; full `LibreHardwareMonitor.Tests.Library`
  suite 95/95 green (net10.0, `dotnet test … -p:Platform=x64`) — 2026-10-03.
- 2026-10-19: preserved the existing source and test bytes in a local checkpoint;
  this cleanup does not establish implementation acceptance.
- At the preservation checkpoint, pending work was implementation review,
  integration with current `master`,
  the solution-filter suite and a real portable-process smoke on non-Windows.
  The two injected Unix-flag tests do not establish execution on Linux.
- 2026-10-19 follow-up review of `1a18c5ab`: both guards precede the unsupported
  operation and preserve Windows probe order. The missing Linux process evidence
  and inaccurate zero-buffer wording for `ExecuteHr` were the remaining gaps.
  The wording is corrected above; merge `36559793` integrates `master` through
  `8aae1fbc` without changing the preserved PawnIO implementation.
- Local follow-up verification: 515 deterministic tests passed (Library 172,
  Application 229, Contracts 114), with one existing opt-in skip. Both Windows
  application builds and all five library targets passed with zero warnings/errors.
  The portable Windows fixture passed, as did 99 CI assertions with two existing
  optional YAML skips. Real Linux acceptance remains pending the hosted run.
