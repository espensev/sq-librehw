# SQ LibreHardwareMonitor Docs

**Status:** live map only
**Updated:** 2026-07-21

## Current

- The product is the Windows app plus the read-only dashboard at `/`.
- Studio and the memory/UI reliability baseline are deployed and verified.
- Sensor Workspace, its four-profile Thermal extension, and the GPU hotspot
  rate sensor are deployed on identity-verified SND-HOST. Live `/`,
  `data.json`, Prometheus, RTX 3080 rate, CSV logging, and controller-to-host
  access checks passed. The native scrollbar follow-up ships in the same build;
  a separate visual/UI Automation host inspection was not part of this smoke.
- Host-neutral log archival/retention tooling is source-controlled under
  `ops/log-management` and installed on SND-HOST in a separate stable runtime
  with a verified SYSTEM task. The first real prior-day rollover archive remains
  to be inspected because the cleaned host had no completed CSV candidate.
- `/dash/cardtruth[/]` is retired; `data.json` and CSV IDs are contracts.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.

## Deployed patch notes

- Added a third read-only `Workspace` view with adaptive `Main`, `Gaming`,
  `Storage`, and `Thermal` profiles, editable/reorderable card, table, and honest
  graph panels, exact sensor membership, and bounded portable JSON import/export.
- Added an honest NVIDIA GPU hotspot rate sensor with bounded five-second
  regression, unavailable warm-up/dropout states, and correct °C/s and °F/s
  formatting across native, web, plot, CSV/data, and Prometheus consumers.
- Ported the useful log-management intent into a parameterized, dry-run-capable
  archive/retention/task-install package with SHA-256 ZIP verification, then
  installed it behind a stable runtime and daily SYSTEM task on SND-HOST.
- Made the native sensor-tree scrollbar substantially easier to see and grab:
  a stable native-width gutter, high-contrast thumb, wider hover/drag states,
  24 px minimum thumb, high-contrast-mode fallback, and real UI Automation
  `ScrollBar`/`RangeValue` behavior at the painted hit target.
- Kept `data.json`, CSV, routes, hardware-write policy, `AssemblyVersion`, and
  current hardware ownership unchanged. SND-DESK's existing runtime stayed
  online during packaging; SND-HOST received its own elevated interactive
  `\LibreHardwareMonitor` task and scoped dashboard firewall rule.

## Roadmap

1. Continue hands-on dashboard and native scrollbar/UI Automation inspection
   through the verified runtime owner; deterministic coverage and the live
   served-asset/telemetry smoke are already complete.
2. Iterate Sensor Workspace around flexibility: resizable/reflowing panels,
   density and visual options, sensor search/grouping, bulk membership, and
   richer graphs that never combine incompatible units dishonestly.
3. Extract a host-neutral, read-only sensor/profile presentation contract, then
   prototype Avalonia in parallel. WinForms keeps hardware and task ownership
   until accessibility, DPI, packaging, lifecycle, and feature-parity gates pass.
4. Inspect the first completed SND-HOST daily CSV rollover ZIP and its task
   history; current-day retention and the installed task already passed live.
5. Implement the host-neutral operator-utility plan: a portable read-only
   thermal snapshot first, then a report-only log evidence analyzer. Keep any
   lossy converter and profile alias behind their separate gates.
6. Close the remaining bounded reliability follow-ups in
   `docs/feature-memory-ui-reliability.md`; keep optional long-soak work separate
   from normal patch promotion.
7. Implement Standard dashboard context layouts on the PR #29 lane per
   `docs/feature-standard-context-layouts.md` and its step plan in
   `docs/superpowers/plans/2026-07-21-standard-context-layouts.md`; the Jul 4
   spike stays archived in branch history and merges with zero code carried.

## Rules

- New features and meaningful behavior changes need a spec first.
- Keep Standard behavior intact unless a spec explicitly changes it.
- Dashboard code must not call `/Sensor?action=Set`.
- Do not hard-code host sensor IDs, labels, limits, or missing values as zero.
- Preserve raw LibreHardwareMonitor labels and `SensorId` when aliases exist.
- Check dark/light, desktop/narrow, failure, and empty states for UI work.

## Runtime contracts

- GET `/Sensor` failures return JSON; GET Set is rejected.
- Legacy POST Set validates/clamps values and requires same-origin browser calls.
- Public reset routes are blocked by the proxy guard.
- Sensor history, decompression, HTTP ownership, and dashboard state are bounded.
- Settings writes are ordered, atomic, backup-aware, and compact stale history.
- RTX 5090 hot spot and its rate remain unavailable until live telemetry proves
  otherwise; warm-up/dropouts never become zero.
- NVIDIA 12VHPWR pins use `/voltage/1..6`; core voltage keeps `/voltage/0`.

## Source map

- `docs/feature-web-dashboard-studio-view.md` - shipped Studio contract.
- `docs/feature-sensor-workspace.md` - active Workspace contract.
- `docs/feature-thermal-trends.md` - additive hotspot-rate contract.
- `docs/feature-host-log-management.md` - archive, retention, and deployment
  safety contract.
- `docs/feature-host-operator-utilities.md` - planned portable thermal snapshot
  and evidence-gated log analysis.
- `docs/feature-standard-context-layouts.md` - planned per-context Standard
  trims (Main/Gaming/Storage) over a materialize-swap contexts key.
- `docs/feature-memory-ui-reliability.md` - shipped reliability contract,
  deployment proof, and remaining follow-ups.
- `LibreHardwareMonitorLib/Hardware/Sensor.cs` - history bounds/persistence.
- `LibreHardwareMonitorLib/Hardware/TemperatureRateSensor.cs` - bounded direct
  sample regression for temperature rate.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` -
  streaming, cleanup, ordering, and atomic settings writes.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` - lifecycle/autosave.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` - HTTP contracts.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` - dashboard.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js` - bounded
  Workspace model, presets, profile operations, and import/export.
- `ops/log-management/` - host-neutral log operations and task-install package.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedVScrollIndicator.cs` and
  `LibreHardwareMonitor.Windows.Forms/UI/Themes/ThemedHScrollIndicator.cs` -
  visible native-sized sensor-tree hit targets.
- `LibreHardwareMonitor.Windows.Forms/UI/Themes/ScrollIndicatorAutomationProvider.cs`
  - UI Automation `RangeValue` bridge to the native scrollbars.

## Verify

```powershell
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\console.js
node --check LibreHardwareMonitor.Windows.Forms\Resources\Web\workspace.js
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\log-management\Test-LhmLogManagement.ps1
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Docs policy

- Keep this README and current feature specs only.
- Fold live findings and proof into the owning spec.
- Delete completed discovery/review notes; Git history preserves the detail.
- Verify live repo/runtime state before trusting old evidence.
