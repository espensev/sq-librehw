# Review - Handoff readiness and remaining issues

**Date:** 2026-10-07

**Surface:** document review of the Remember handoff and linked issue records, reconciled against `master` at `e8d75cecb19d68010b0585e4b8e31e8cc364c9e6` and the existing working trees

**Spec sources:** `docs/features/feature-memory-ui-reliability.md`, `docs/features/feature-local-release-system.md`, `docs/architecture/campaign-control-plane.md`, `docs/campaign-backlog.md`

**Standards:** `AGENTS.md`, `docs/README.md`, `.codex/skills/project.toml`

**Verdict:** FAIL for remediation readiness of the affected paths. Normal installed PSU telemetry is healthy in the current sample.

**Subsequent source resolution, 2026-10-07:** Corsair findings 5-8 are fixed and
regression-tested in source. The remaining findings and live release gate stay
open. See the [current reliability verification](../features/feature-memory-ui-reliability.md#verification-log).

## Findings

### High

1. **[standards/regression] The primary checkout remains an incomplete build and tooling baseline (F-001/F-005).**
   There are exactly 12 unstaged tracked deletions, plus the separately modified reliability specification. `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj:11` still requires the missing application icon; `UI/AuthForm.Designer.cs:158` still reads the deleted form resource. `scripts/task_manager.py:66`, `:71`, `:78`, `:79` and `scripts/task_runtime/__init__.py:1` import deleted Python modules before dispatch. Installation parses the missing launcher at `ops/deploy/snd-desk/Install-LibreHardwareMonitorRelease.ps1:122`; launcher synchronization reports that absence as blocking at `Sync-LibreHardwareMonitorLauncher.ps1:851`.
   Evidence: fresh Git/disk inspection and `D:/DevHome/tmp/librehw-handoff-review-20261007/baseline.json`. Builds and campaign commands were not run in this incomplete checkout. Deletion ownership is unknown; no files were restored. Use the existing clean Corsair worktree for focused verification and resolve deletion provenance before building or starting campaigns here. Missing license files are an incomplete-checkout finding; this review makes no legal-compliance conclusion.

2. **[standards/regression] Campaign merge cleanup still admits data loss (F-002/F-003/F-004).**
   `scripts/task_runtime/merge.py:56` adds each recorded worktree's parent to its own allowed roots; `:180-184` then permits recursive deletion under those roots. A recorded arbitrary existing directory therefore authorizes itself. Registered worktrees are removed with `--force` at `:173`, and branches with `-D` at `:190`, without the required cleanliness/ancestry proof. `:236` unconditionally stashes tracked work before processing candidates; there is no corresponding stash restoration. Even the no-files result reaches cleanup at `:268`.
   These paths remain source-confirmed; no destructive command was executed. Repair path authority, preserve dirty work/branches, and make stash recovery explicit before using campaign merge/cleanup. The missing imports currently block the ordinary CLI, but restoring them would expose these defects again.

3. **[regression] Promotion and rollback still default to the wrong health endpoint (F-006).**
   `Install-LibreHardwareMonitorRelease.ps1:17` and `Restore-LibreHardwareMonitorRelease.ps1:11` default to `http://localhost:8085/data.json`. The live configured endpoint checked this turn is `http://127.0.0.1:18085/data.json`. Default invocation can fail health checks against an unrelated listener or no listener. Recent successful deployments supplied the matching explicit URI, so this is a tooling-default defect, not a current runtime outage. Bind health checks to validated runtime configuration and add non-live mismatch coverage before the next release. No installer or rollback was invoked.

4. **[regression] PawnIO platform gating remains unmerged (F-013).**
   Current `LibreHardwareMonitorLib/PawnIo/PawnIo.cs:23` enters Windows registry APIs unconditionally; `:65` reaches Windows device APIs without a platform gate. The existing `fix/f-013-pawnio-os-gate` worktree at `D:/DevHome/worktrees/LibreHW/LibreHW-f-013-pawnio-20261003` contains a small uncommitted fix, two regression tests, and a specification. Its branch HEAD is still `fd6f106a`; the committed PawnIO blob is identical to master's. The patch is suitable for a focused completion pass, not yet ready to call landed. Historical 2/2 focused and 95/95 library results in its spec were not rerun here; injected Unix flags do not establish real Unix startup. Its diff whitespace check passes.

### Medium

5. **[regression] Corsair lock misses retain unlimited apparently current telemetry.**
   `LibreHardwareMonitorLib/Hardware/Psu/Corsair/CorsairPsu.cs:81` returns without invalidation. CSV reads the old value at `LibreHardwareMonitor.Windows.Forms/Utilities/Logger.cs:440`; HTTP does so at `Adapters/WinFormsNodeSensorSnapshotSource.cs:58`. Fresh reproduction: three blocked polls opened zero streams and retained all 18 fixture values unchanged over 1,538 ms. The fixture advertises optional input current; the physical HX1500i has 17 values.
   Recommended policy, not yet implemented: immediately mark current readings unavailable after a missed transaction; retain min/max and recover on the next successful poll. Define open/read/partial-poll failure behavior as well. A grace period requires a specified deadline and monotonic-clock tests. Sample-age API fields would expand the current contract unnecessarily.

6. **[regression/spec] An earlier failed PSU still transfers its identity to a later device.**
   `CorsairPsuGroup.cs:60` assigns identifiers from successful-device count. The repeated fixture makes B inherit `/psu/corsair/0` and `PSU A temperature` when A alone fails. `CorsairPsuDiscoveryTests.cs:100` explicitly expects the compressed numbering. Reserve a supported-device enumeration slot before construction; unsupported devices must not consume slots. This narrow repair needs no new user-facing policy. Cover failed-first, failed-middle, multiple failures and recovery against the same saved labels. USB enumeration reordering remains a separate pre-existing limitation.

7. **[regression; pre-existing defect] Derived output power converts unavailable rails into valid-looking watts.**
   `CorsairPsu.cs:196-202` coalesces each absent rail to zero; `CompositeSensor.cs:34-37` always returns a numeric aggregate. A fresh three-rail fixture changed from 60 W to 50 W after one required rail became null, then to 0 W when all three became null. This predates the latest Corsair repair but must be addressed with finding 5. Require all component readings for a valid total, preserving genuine zero readings. Do not set every exposed sensor to null indiscriminately: the composite setter throws. Keep the repair scoped to the affected derived-value behavior.

8. **[spec/regression] Corsair access fails open when its mutex handle is unavailable.**
   `Mutexes.cs:48-60` can return null after access-denied creation and unsuccessful fallback opening. `WaitCorsairLink` at `:78-80` uses `WaitMutex`, which treats null as success at `:138-141`. The new Corsair code then opens HID streams without ownership. An isolated injected-null fixture reproduced successful wait plus one stream each for discovery, update and properties. Actual Windows mutex ACL denial was not induced, and no claim is made that the live process currently lacks its lock. Make only the Corsair wrapper fail closed; test zero device I/O and recovery with explicit private-mutex fixtures.

9. **[regression] Autosave still removes history before clean close can refresh it.**
   `Sensor.cs:473` deletes loaded `/values`; `PersistentSettings.cs:476` marks removal dirty. History is rewritten on hardware close (`Sensor.cs:116`). An autosave can therefore strip primary history, and a later dirty save can rotate that stripped file over the remaining backup before a crash. Simply keeping the key is insufficient: `PersistentSettings.cs:311` omits unclaimed histories. Existing `SensorHistoryTests.cs:283` expects removal, while `SettingsPersistenceTests.cs:128` covers the clean-close sequence. Add load, sensor construction, two dirty autosaves and reopen coverage; preserve and claim only validated bounded histories. This was source-reviewed, not newly fault-reproduced.

10. **[spec/regression] Failed runtime hardware changes still leave UI, settings and hardware inconsistent.**
    `UserOption.cs:45-47` persists the requested flag before applying it. GPU enable adds AMD then NVIDIA at `Computer.cs:199-200`, committing the backing flag only at `:212`. Failure in the second addition can leave AMD present with the backing flag false and UI/settings true. A subsequent false request can no-op (`Computer.cs:334`). Group event/cleanup failures occur after membership changes (`HardwareGroupRegistry.cs:64`, `:95-117`). `MainForm.cs:703-708` only debug-logs failure. Implement category-level transactions before UI/settings rollback; include partial enable, notification failure, failed disable, deliberate retry and superseding queued requests. Already-closed groups cannot be restored by flipping a Boolean. No hardware fault was induced this turn.

## What the handoff now supports

- The original two medium findings are current and reproducible. The smallest coherent next Corsair change covers findings 5-8 together: immediate unavailability, nullable derived power, stable discovery slots and fail-closed lock acquisition. These are recommendations, not accepted or implemented product changes.
- Source tests must seed prior nonzero values, preserve min/max, assert HTTP null and CSV empty output, and prove recovery. Add valid-zero/one-null/all-null aggregate cases and persisted-identity reopen cases. Then run both framework builds and repository release gates before considering live coexistence verification.
- Existing clean worktree `D:/DevHome/worktrees/LibreHW/LibreHW-psu-discovery-20261007` matches `e8d75cec`; it avoids silently restoring the primary checkout's unrelated deletions.
- The earlier audit's first deferred fix, F-013, still has recoverable work. Finish its review/verification rather than recreate it. Campaign cleanup and release-default repairs remain separate follow-ups.

## Deferred work and genuine decisions

- Failed `Computer.Reset()` deliberately closes the old generation before building replacement (`Computer.cs:859`) and closes partial replacement on failure. `ComputerOpenLifetimeTests.cs:336` explicitly asserts empty hardware afterward. Choose between visible failure plus explicit retry and reconstructing hardware; the latter re-probes devices. This is a policy gate, not a missing transactional-Open fix.
- Transactional Open and partial-reset cleanup already exist; do not reopen them as absent implementation. Option rollback and crash-time history continuity are distinct gaps.
- The local older sweep at `E:/Monitoring/sq-librehw/docs/reviews/audit-librehw-grand-sweep-20261003.md` and `.audit/crew/librehw-grand-sweep-20261003-1e5346a3/REPORT.md` retain the broader queue, including traversal-freeze findings and 75 then-unconfirmed medium/high candidates. Those artifacts remain outside this commit; the counts are historical and this review did not reconfirm that entire audit or resume its workflow.
- `docs/campaign-backlog.md` still leaves Plan-001 attended smoke, Plan-012 criterion 10, and explicitly gated Plan-014 open. Native layout/text-scaling and SND-HOST UI acceptance require their specified checks; a local HTTP read does not close them.
- `feature-http-server-safety.md:3` still says not promoted and its acceptance combines listener and firewall proof. The installed commit contains later source, but this review did not inspect firewall scope or exercise rejection paths. Reconcile that record during a focused live verification; do not infer full acceptance from healthy loopback HTTP.
- Optional Corsair input-current detection increments later sensor indices (`CorsairPsu.cs:153-158`); changing that pre-existing identity scheme needs a compatibility decision beyond reserving device slots.

## Verification and coverage

Evidence directory: `D:/DevHome/tmp/librehw-handoff-review-20261007`.

- `git status --short --branch`, `git log`, `git worktree list`, targeted `git show`/diff and source reads reconciled the current handoff. Source remains `e8d75cec`; the installed manifest is `07d2d25e`.
- `Get-FileHash` of the installed EXE matches manifest SHA-256 `c502fee0cda440eaa59f62de9e90d3834047f703f8897e6d640ff19772f0c97c`. `Invoke-RestMethod http://127.0.0.1:18085/data.json` returned 17/17 non-null PSU readings. Monitor PID 4204 and HWiNFO/iCUE/Corsair service coexistence are recorded in `baseline.json`. This is a current health sample, not a new soak.
- In the clean matching worktree: `dotnet test LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Library/LibreHardwareMonitor.Tests.Library.csproj -c Release -p:Platform=x64 --filter FullyQualifiedName~CorsairPsu --no-restore --logger "console;verbosity=minimal"` passed 22/22. Those tests do not cover the reproduced defects.
- `dotnet run --project D:/DevHome/tmp/librehw-psu-crossreview-20261007/ReviewProbe.csproj -c Release -p:Platform=x64 --no-restore` reproduced findings 5-6; output is `edge-repro.log` in this turn's evidence directory.
- `dotnet run --project D:/DevHome/tmp/librehw-handoff-review-20261007/AdditionalProbe.csproj -c Release -p:Platform=x64 --no-restore` reproduced findings 7-8; output is `additional-repro.log`. The probe selects all three rails by sensor type/name and asserts the count. Both probes use fake HID and a unique local mutex, never the physical PSU.
- Focused independent source reviews covered Corsair readiness, non-Corsair reliability follow-ups, and checkout/F-013 blockers; the controller checked material findings and ran the reproductions. Deep review covered the files/lines cited above and the linked contracts/tests. Other hardware drivers, all old sweep candidates, SND-HOST state and interactive UI acceptance were excluded.
- Full release builds, deployment, destructive campaign tooling, restart, and live failure injection were not performed because this was a review without product changes. Documentation checks and final preservation results are recorded in `closeout.json`.

Current unresolved reliability findings are tracked in [the feature specification](../features/feature-memory-ui-reliability.md#open-follow-ups).
