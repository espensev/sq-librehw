# SQ LibreHardwareMonitor Docs

**Status:** live summary only.
**Updated:** 2026-07-14.

## Current State

- This is the Sev IQ LibreHardwareMonitor fork.
- The product surface is the Windows app plus the web dashboard at `/`.
- Dashboard v3 A-E is shipped on `master`.
- Next dashboard work: context dashboards `Main / Gaming / Storage`.
- `/dash/cardtruth/` is retired.
- `data.json` and CSV identifiers are external contracts.
- Keep `AssemblyVersion` at `0.9.6`.
- Builds must use `-p:Platform=x64`.

## Docs Policy

- This compact docs set is intentional.
- Keep live docs short, current, and action-oriented.
- Do not recreate long planning/review/spec archives in `docs/`.
- Put detailed historical evidence in git history, not new docs.
- New docs should be rare and should stay under a clear todo, issue, or implementation path.

## Hard Rules

- New features and meaningful behavior changes need a spec first.
- Small direct bugfixes may be fixed directly.
- Review/audit tasks stay read-only unless fixes are requested.
- Dashboard remains read-only: no write UI and no `/Sensor?action=Set` from dashboard code.
- Legacy server mutation routes still exist; treat them as compatibility surface.
- No host-specific sensor IDs, labels, or limits in product code.
- Raw LibreHardwareMonitor labels and `SensorId` stay visible when aliases exist.
- Missing values render as missing, not zero.
- Both dark and light themes must be checked for UI work.

## Web Dashboard Done

- Honest range provenance shipped.
- RTX 5090 power uses derived/real/override limit or number-only; no fake `/ 200`.
- Fans use Control % for gauge arcs and RPM as the value.
- Duplicate hardware is keyed by `HardwareId`.
- Card/row expansion carries details and actions.
- Sensors popover replaced the drawer.
- Network adapters render as distinct subgroups.
- Direct deck edit controls shipped.
- Anchored expansion overlay shipped with zero card displacement.
- Responsive/theme QA passed for the current dashboard.
- Root `viewTheme` selector shipped.
- Studio dashboard is implemented and verified locally; behavior follows
  `docs/feature-web-dashboard-studio-view.md`.
- Memory, ownership, and UI reliability remediation is implemented, deployed,
  and verified;
  its acceptance and evidence follow
  `docs/feature-memory-ui-reliability.md` and its closeout review.

## Hardware Notes

- RTX 5090 hot spot: no live sensor on this card/driver.
- Do not publish a guessed RTX 50 thermal index.
- Revisit hot spot only if live telemetry reports non-zero `hotspot_C` or `hotspot_idx >= 0`.
- NVIDIA 12VHPWR voltage pins use `/voltage/1..6`; `GPU Core Voltage` keeps `/voltage/0`.
- Consumers with old 12VHPWR pin data need a one-time remap.

## Server Notes

- GET `/Sensor` failures return JSON failure payloads.
- GET `action=Set` is rejected.
- POST `action=Set` is legacy compatibility and validates/clamps values.
- Browser-originated POST Set must match scheme, host, and port.
- Public telemetry is intended through the local proxy path.
- Public reset routes are blocked by the proxy guard.
- If `:8085` is exposed directly, revisit auth/write-enable/reset policy first.

## Settings Notes

- Sensor history persistence is bounded to prevent config bloat.
- Oversized/stale `/values` blobs are cleaned during load/save.
- Settings autosave runs during the session.
- Writes are atomic with backup fallback.
- The fallback save path fails (keeping the live config) if the backup cannot be preserved.

## Implementation Map

- `.gitignore` - ignores local MCP/tool scratch state.
- `LibreHardwareMonitorLib/Hardware/Sensor.cs` - bounds persisted sensor value history.
- `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj` - exposes internals to tests.
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` - runs dirty-settings autosave.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` - cleans stale history, tracks dirty state, writes atomically, and preserves backup safety.
- `LibreHardwareMonitor.Tests/SettingsPersistenceTests.cs` - covers config bloat, cleanup, dirty tracking, atomic save, and backup recovery.
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs` - hardens sensor API failures, control writes, and browser-origin checks.
- `LibreHardwareMonitor.Tests/HttpServerSensorApiTests.cs` - covers sensor API errors, control values, and same-origin policy.
- `docs/README.md` - live repo summary.
- `docs/feature-memory-ui-reliability.md` - implemented reliability contract and verification record.
- `docs/reviews/review-2026-07-14-memory-ui-lifecycle.md` - deployed reliability patch closeout.
- `docs/reviews/review-2026-07-07-settings-persistence-fix.md` - concise review record for the settings fix.
- `docs/reviews/review-2026-07-07-settings-autosave-rereview.md` - ultra-compact rereview summary.
- `docs/reviews/settings-autosave-issues.md` - accepted autosave residual-risk todo list.

## Standard Commands

```powershell
node webtests\selftest.node.js
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

## Working Rule

- Verify live repo/runtime state before trusting old notes.
- Historical plans/reviews were removed from `docs/` on purpose.
- Use git history for old evidence.
