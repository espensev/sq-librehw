# Audited upstream sync — 2026-07-25

**Status:** source verified; attended hardware smoke pending

**Upstream range:** `abfc4f5..81e8f83` (17 commits)

**Target:** merge `upstream/master` into the Sev IQ `master` branch

## Problem and motivation

The fork is 17 commits behind LibreHardwareMonitor upstream. Those commits add
motherboard sensors, safer NCT6687DR fan writes, a DiskInfoToolkit update and
S.M.A.R.T. polling control, plus auth, resource, packaging, and maintenance
fixes. A blind merge is unsafe because the fork has newer central package
versions and stricter HTTP, persistence, PawnIO, and UI-lifetime contracts.

## Goals

- Preserve the complete upstream ancestry through `81e8f83`.
- Import upstream hardware support and the DiskInfoToolkit 2.1.2 S.M.A.R.T.
  update-cycle behavior.
- Keep the fork's central package management, async PawnIO initialization,
  bounded HTTP implementation, and `data.json` non-finite-value contract.
- Keep failed NCT6687DR default-fan restoration retryable until a later
  explicit reset or close succeeds.
- Refresh Dependabot so every package-consuming project is scanned.

## Non-goals

- No runtime deployment, scheduled-task change, firewall change, or remote-host
  mutation is part of this source integration.
- No change to `AssemblyVersion` 0.9.6, dashboard routes, sensor IDs/order, CSV,
  Prometheus, or hardware ownership.
- No claim of live validation for newly supported motherboards or EC fan
  control without matching hardware.

## User-visible behavior

`Options > Update Interval > Throttle Disk S.M.A.R.T. Updates` replaces the old
ATA throttle toggle with these persisted choices:

- Follow Update Interval
- Every 10 Cycles
- Every 25 Cycles
- Every 50 Cycles
- Every 100 Cycles

Disk performance data continues to update on each normal application cycle.
The more expensive S.M.A.R.T. refresh runs only on the selected cycle. Sleeping
disk and force-wakeup behavior remains unchanged.

If `smartUpdateCycle` is absent and the legacy
`throttleAtaUpdateMenuItem=true` setting exists, selection starts at Every 25
Cycles. This approximates the old 30-second throttle at the default one-second
update interval. An explicit `smartUpdateCycle` always wins.

For NCT6687DR fan controls, manual-mode and PWM changes remain a single EC
configuration transaction. If restoring the firmware defaults times out or the
EC rejects all three attempts, the saved defaults and retry-required flag must
remain intact so a later reset or close can retry.

## Integration decisions

- Keep versionless `PackageReference` entries. Root
  `Directory.Packages.props` already matches or supersedes every upstream
  package bump.
- Keep the fork's `HttpServer` conflict side. Upstream's named floating-point
  serializer would turn non-finite sensor values into strings; this fork's
  external `data.json` contract requires `null`.
- Keep the fork's async and assembly-name-neutral PawnIO/resource loading.
- Apply the upstream S.M.A.R.T. menu and storage cadence atomically, with the
  legacy-setting fallback above.
- Accept the upstream board/sensor mappings, solution cleanup, nightly-link
  correction, and NuGet publish guard.
- Adapt upstream Dependabot directories to include the fork-only test project.

## Compatibility and risks

- Both `net472` and `net10.0-windows` application targets must compile for x64.
- NuGet restore must remain compatible with central package management; inline
  package versions are forbidden.
- `data.json` golden-master output must not change.
- The new DiskInfoToolkit call shape and UI must land together.
- Board mappings and NCT6687DR register transactions cannot be fully validated
  without exact hardware. Compile/tests and upstream review are necessary but
  do not replace an attended hardware smoke.

## Acceptance criteria

- Git history contains upstream `81e8f83` and no upstream-only commits remain.
- No merge markers or inline package versions remain.
- All five S.M.A.R.T. choices map to cycle counts `1, 10, 25, 50, 100`.
- Legacy true, legacy false/absent, and explicit-new-setting precedence have
  regression coverage.
- Storage cadence regression coverage proves the selected cycle is respected.
- Failed NCT6687DR restore transactions keep retry state; success clears it.
- Dependabot scans Windows Forms, library, Aga.Controls, and tests projects.
- Web/HTTP contract tests, full .NET tests, Node suites, log-management checks,
  and both x64 Release application targets pass.

## Verification plan

```powershell
git diff --cached --no-ext-diff | rg -n '^\+(<<<<<<< |=======|>>>>>>> )'
rg -n '<PackageReference\b[^>]*\bVersion\s*=' -g '*.csproj'
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
git diff --cached --check
```

## Verification record

Source integration was verified on `snd-desk` on 2026-07-25:

- official `upstream/master` resolved to `81e8f83`; its Dependabot and build
  checks were successful;
- all staged conflict resolutions passed three independent cross-lane reviews,
  including a second review of the NCT6687DR acknowledgment and retry-state
  correction;
- the focused NCT6687DR regression suite passed 9/9, and the full .NET suite
  passed 194 with one intentional opt-in skip and no failures;
- isolated x64 Release builds for `net10.0-windows` and `net472` both completed
  with zero warnings and zero errors;
- library Release builds passed for x64, x86, and ARM64 across their supported
  target frameworks; the reference build completed with no errors and its 30
  known AnyCPU `PInvoke005` warnings;
- NuGet packing completed successfully;
- web syntax checks passed, the dashboard self-test passed 315/315, the Node
  suites passed 18/18, and log-management checks passed;
- staged whitespace, conflict-marker, inline-package-version, secret, and
  debug/temp scans were clean.

An attended smoke of the newly mapped motherboards/NCT6687DR hardware and a
manual check of the S.M.A.R.T. menu remain pending because matching hardware
was not available in this source-only integration. No runtime payload, task,
service, or deployed copy was changed.
