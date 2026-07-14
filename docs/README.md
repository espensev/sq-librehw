# SQ LibreHardwareMonitor Docs

**Status:** live map only
**Updated:** 2026-07-14

## Current

- The product is the Windows app plus the read-only dashboard at `/`.
- Studio and the memory/UI reliability patch are shipped and verified.
- Next dashboard work is context views: `Main / Gaming / Storage`.
- `/dash/cardtruth[/]` is retired; `data.json` and CSV IDs are contracts.
- Keep `AssemblyVersion` at `0.9.6`; build with `-p:Platform=x64`.

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
- `docs/feature-memory-ui-reliability.md` - shipped reliability contract,
  deployment proof, and remaining follow-ups.
- `LibreHardwareMonitorLib/Hardware/Sensor.cs` - history bounds/persistence.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` -
  streaming, cleanup, ordering, and atomic settings writes.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` - lifecycle/autosave.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` - HTTP contracts.
- `LibreHardwareMonitor.Windows.Forms/Resources/Web/console.js` - dashboard.

## Verify

```powershell
node webtests\selftest.node.js
node --test webtests\console.tests.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Docs policy

- Keep this README and current feature specs only.
- Fold live findings and proof into the owning spec.
- Delete completed discovery/review notes; Git history preserves the detail.
- Verify live repo/runtime state before trusting old evidence.
