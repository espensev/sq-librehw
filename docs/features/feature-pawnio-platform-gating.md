# Feature: PawnIO platform gating

**Status:** local preservation checkpoint on branch `fix/f-013-pawnio-os-gate`
(worktree `LibreHW-f-013-pawnio-20261003`), not accepted or merged into `master`.
Implementation review, current-upstream integration and platform verification
remain open.
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
  not-loaded instance whose `Execute`/`ExecuteHr` already fail soft (zeros) and
  whose `Close` no-ops.

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

## Acceptance criteria

1. `PawnIoPlatformGateTests.FindInstalledVersion_OnNonWindows_ReturnsNullWithoutThrowing` passes.
2. `PawnIoPlatformGateTests.LoadModuleFromResource_OnNonWindows_ReturnsNotLoadedInstance` passes.
3. Windows registry lookup semantics unchanged (two-step probe preserved verbatim).

## Verification record

- Pre-patch: both tests red (missing seam, compile errors CS0117/CS1739) —
  2026-10-03.
- Post-patch: 2/2 gated tests green; full `LibreHardwareMonitor.Tests.Library`
  suite 95/95 green (net10.0, `dotnet test … -p:Platform=x64`) — 2026-10-03.
- 2026-10-19: preserved the existing source and test bytes in a local checkpoint;
  this cleanup does not establish implementation acceptance.
- Pending: independent implementation review, integration with current `master`,
  the solution-filter suite and a real portable-process smoke on non-Windows.
  The two injected Unix-flag tests do not establish execution on Linux.
