# SQ LibreHardwareMonitor Docs

**Status:** live map only
**Updated:** 2026-07-15

## Current

- The product is the Windows app plus the read-only dashboard at `/`.
- Studio and the memory/UI reliability baseline are deployed and verified.
- Sensor Workspace and the sensor-tree scrollbar accessibility follow-up are
  implemented and verified; deployment remains pending.
- `/dash/cardtruth[/]` is retired; `data.json` and CSV IDs are contracts.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.

## Pending patch notes

- Added a third read-only `Workspace` view with adaptive `Main`, `Gaming`, and
  `Storage` profiles, editable/reorderable card, table, and honest graph panels,
  exact sensor membership, and bounded portable JSON import/export.
- Made the native sensor-tree scrollbar substantially easier to see and grab:
  a stable native-width gutter, high-contrast thumb, wider hover/drag states,
  24 px minimum thumb, high-contrast-mode fallback, and real UI Automation
  `ScrollBar`/`RangeValue` behavior at the painted hit target.
- Kept `data.json`, CSV, routes, hardware-write policy, `AssemblyVersion`, and
  current hardware ownership unchanged. The deployed `LibreHW-No-UAC` task and
  runtime were not replaced, restarted, or reconfigured by this patch.

## Roadmap

1. Promote this checkpoint only with explicit approval, then smoke all three
   dashboard views and the native scrollbar through `LibreHW-No-UAC`.
2. Iterate Sensor Workspace around flexibility: resizable/reflowing panels,
   density and visual options, sensor search/grouping, bulk membership, and
   richer graphs that never combine incompatible units dishonestly.
3. Extract a host-neutral, read-only sensor/profile presentation contract, then
   prototype Avalonia in parallel. WinForms keeps hardware and task ownership
   until accessibility, DPI, packaging, lifecycle, and feature-parity gates pass.
4. Close the remaining bounded reliability follow-ups in
   `docs/feature-memory-ui-reliability.md`; keep optional long-soak work separate
   from normal patch promotion.

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
- RTX 5090 hot spot remains unavailable until live telemetry proves otherwise.
- NVIDIA 12VHPWR pins use `/voltage/1..6`; core voltage keeps `/voltage/0`.

## Source map

- `docs/feature-web-dashboard-studio-view.md` - shipped Studio contract.
- `docs/feature-sensor-workspace.md` - active Workspace contract.
- `docs/feature-memory-ui-reliability.md` - shipped reliability contract,
  deployment proof, and remaining follow-ups.
- `LibreHardwareMonitorLib/Hardware/Sensor.cs` - history bounds/persistence.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` -
  streaming, cleanup, ordering, and atomic settings writes.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` - lifecycle/autosave.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` - HTTP contracts.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` - dashboard.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/workspace.js` - bounded
  Workspace model, presets, profile operations, and import/export.
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
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Docs policy

- Keep this README and current feature specs only.
- Fold live findings and proof into the owning spec.
- Delete completed discovery/review notes; Git history preserves the detail.
- Verify live repo/runtime state before trusting old evidence.
