# Patch Closeout - Memory, lifetime, efficiency, and UI handling

**Date:** 2026-07-14

**Implementation:** `63816bb..5b9c6f9`

**Deployed product:** `0.9.6+5b9c6f9.2026-07-14`

**Specification:** `docs/feature-memory-ui-reliability.md`

**Standards:** repository `AGENTS.md`

**Verdict:** **PASS WITH NOTES**

The original findings-first review is preserved in Git history. All 14 high-
and medium-severity findings were fixed and covered by targeted tests, bounded
runtime evidence, or both. A final fixed-point review found no high- or
medium-severity residual. One low hidden-start dashboard edge case and two
operational follow-ups remain explicitly deferred below.

## Patch status

| Item | Status | Evidence |
| --- | --- | --- |
| 1. Storage group reset/static-event lifetime | Fixed/proven | Closed groups detach, removed devices close, and weak-reference/reset regressions pass in `StorageGroupLifetimeTests`. The staged and deployed resume/reset smokes stayed bounded. |
| 2. Sensor-history retention, copying, plots, and metrics | Fixed/proven | History is capped at 10,000 representative points; tail/delta readers are covered by `SensorHistoryTests`, `PlotPanelHistoryTests`, and Prometheus tests. |
| 3. Large settings load and cleanup | Fixed/proven | Settings load through `XmlReader`; stale/oversized entries dirty the store for compaction. Persistence tests and the historical approximately 252 MiB harness cover the regression seam. |
| 4. Session/form shutdown ownership | Fixed/proven | `UiShutdownCoordinator` makes shutdown UI-thread and exactly-once; coordinator tests pass and two controlled live closes drained HTTP and exited cleanly. |
| 5. Gadget HBITMAP/DC ownership | Fixed/proven | Selected objects, bitmap, DC, HWND, menu, and subscriptions now have idempotent teardown; GDI stayed flat in staged and deployed smokes. |
| 6. Studio cached-telemetry replay | Fixed/proven | Telemetry ingestion is separated from cached paint; Node tests prove rerenders do not fabricate samples, extrema, or derived limits. |
| 7. Tray retry UI stalls | Fixed/proven | Retries are bounded, timer-driven, cancellable, and do not sleep while holding UI ownership. |
| 8. Concurrent settings-save ordering | Fixed/proven | Snapshot and file replacement share one ordering gate; overlapping-save and backup-safety tests pass. |
| 9. HTTP and browser polling ownership | Fixed/proven | Server handlers are bounded/tracked/drained and browser fetches are single-flight, abortable, timed, and generation-checked. The deployed runtime served 32/32 concurrent data requests. |
| 10. History decompression expansion | Fixed/proven | Byte/record budgets reject malformed, trailing, and oversized expansion; targeted history tests pass. |
| 11. Hardware callback thread affinity | Fixed/proven | Callbacks marshal through a live UI owner and late work is dropped across initialization, reset, and shutdown. |
| 12. UI GDI/static-event teardown | Fixed/proven | Tree, theme, font, gadget, tooltip, dialog, icon, and image ownership is deterministic; WinForms lifetime tests pass. |
| 13. Web storage failure handling | Fixed/proven | Dashboard storage uses guarded access with an in-memory fallback; throwing-storage boot tests pass. |
| 14. Multi-second UI-thread work | Fixed/proven | PawnIO/discovery, DNS, hardware lifecycle, and server stop are asynchronous or bounded without the prior synchronous waits. |
| Web state cardinality, keyed DOM reuse, modal/image cleanup, accessibility, and contributor-map issues | Fixed/proven | Node/browser checks, WinForms lifetime tests, and current docs-map inspection cover the original low-severity list. |
| Bounded clean-close history file | Accepted | A clean close persisted 475 valid histories in 2,889,088 bytes; all decoded, the maximum was 3,256 records and 25,376 encoded characters, below the 10,000-record and 65,536-character per-sensor limits. This is intended cross-restart history, not stale config growth. |
| Initially hidden persisted-paused dashboard | Deferred | Low severity: the forced startup snapshot is lost while hidden, so the dashboard stays blank until Resume. Preserve the pending snapshot across first visibility restore and add the missing hidden-start test. |
| Crash-after-autosave graph-history continuity | Deferred | Autosave can write live settings without current `/values`; the existing risk remains tracked in `docs/reviews/settings-autosave-issues.md`. |
| 60-minute current-commit live soak | Deferred | The bounded staged and deployed gates passed; an extended current-commit soak is an additional confidence check, not claimed as completed. |

## Deployment verification

- The implementation commit is
  `5b9c6f9f7c867bfd8f82549f1d1dde531d5705b4`.
- Isolated Release builds for `net10.0-windows` and `net472` completed with
  0 warnings and 0 errors. The 71-file net10 candidate matched the live runtime
  byte-for-byte.
- Core live SHA-256 values:
  - executable:
    `CF43D8E4B4820A29208DB2E20520400C8381B472300D629CC6758B70416E62C8`;
  - Forms DLL:
    `EDDED07EC0BB99EAC452E98548E628EE20017B5A9BADC7439821E6F12529442D`;
  - library DLL:
    `B0CFEA49DBB8F54C65C25BF6C0337C859B652B4BC8F431A452054FDE7A24789D`.
- The rollback packet is
  `C:\ProgramData\LibreHardwareMonitor\backups\20260714-042926-pre-5b9c6f9`.
  Deployment preserved both settings files and all 3,305 existing CSV logs
  (6,935,205,771 bytes); later log growth is from the running app.
- The actual owner task `\SevGrp\AdminTask\LibreHW-No-UAC` is `Running`
  with one process at the expected live path and the deployed product version.
- `/`, `/data.json`, and `/metrics` return HTTP 200.
  `/dash/cardtruth` and `/dash/cardtruth/` remain HTTP 404.
- One deployed synthetic resume/reset completed 60/60 HTTP polls and 32/32
  concurrent data requests. Working set moved from 210.7 to 210.1 MiB, private
  memory from 130.2 to 127.5 MiB, handles from 753 to 725, GDI stayed at 47,
  and USER handles moved from 55 to 56.
- A final 45-request sample returned 45/45 HTTP 200 responses. Working set stayed
  between 154.0 and 158.7 MiB, private memory between 142.6 and 148.2 MiB, GDI
  stayed at 47, and USER handles stayed at 56. Total handles oscillated between
  660 and 1,187 with a negative least-squares trend, confirming the observed
  spike was transient rather than monotonic growth.
- After the controlled restart, the first five-minute autosave compacted the
  primary config from 2,889,088 to 22,643 bytes. The bounded clean-close
  histories first rotated to the backup; a later autosave left both primary and
  backup at 22,643 bytes, directly confirming the deferred continuity risk.

## Automated and manual evidence

- `node --check` - pass.
- Dashboard self-test - 267/267 pass.
- Polling suite - 3/3 pass.
- x64 .NET suite - 124 pass, one intentionally opt-in live-config-copy test
  skipped, zero failures; the `data.json` golden master is unchanged.
- Current-head browser smoke - Standard/Studio, pause/resume, dark/light,
  360 CSS-pixel viewport, focusable controls, live polling/restart, and clean
  console passed.
- Isolated process smoke - three resume cycles, 180/180 HTTP polls, 32/32
  concurrent requests, stable GDI/USER/handle envelope, and clean main-form
  exit passed.
- Final fixed-point diff review - no high- or medium-severity residual findings;
  the low hidden-start paused case above is the only new code finding.

## Coverage

The closeout covered the complete implementation diff, specification,
settings/history contracts, storage reset ownership, WinForms and native
lifetimes, shutdown/dispatch, server request ownership, dashboard behavior,
targeted tests, staged binaries, rollback packet, and deployed runtime. The
deferred 60-minute soak is the only unperformed extended runtime gate.
