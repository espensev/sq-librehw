# Agent Task — Verify Suite Boundaries (Plan-006, Agent AA)

**Scope:** Independent verification of the combined y+z tree. You own **no files**. You
run, measure, and report. A defect in another lane's file goes in your result payload —
you never patch it.

**Depends on:** y, z.

---

## Checks (all required)

1. **Full sweep.** From the campaign tree, with a sanitized process `PSModulePath`
   (the two WindowsPowerShell module directories only — the Plan-003 criterion-4
   evidence: PowerShell 7 module dirs in the process value hide `Get-FileHash` from 5.1
   and turn `snd-desk-local-release-fixture` red):
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -All`
   → expect 8/8 included gates passed, `ci-gates` skipped as excluded.
2. **Dispatcher.** `eng\ci\Test-LhmCiGates.ps1` run directly under both engines — must
   discover **four** test scripts (GateRunner, NoStaleReferences, SuiteBoundaries, plus
   the workflow test file set as currently discovered) and pass.
3. **Population preservation.** Run each deterministic suite and the slnf invocation;
   sum totals. Required: 259 discovered / 258 passed / 1 skipped, the skip being
   `SettingsPersistenceTests.LiveConfigCopy_LoadsAndCompactsWithinMemoryBudgets` in the
   Application suite. Compare against the recorded pre-campaign baseline (identical
   numbers from clean HEAD `cae786c`). Any delta, including a new skip, is a defect.
4. **Golden byte-identity.** `git status`/`git diff` must record
   `data.golden.json` as a pure rename (R100) with zero content delta, and
   `DataJsonGoldenTests` must pass unmodified.
5. **Product-root guard.** `git diff` over `Aga.Controls/`, `LibreHardwareMonitorLib/`,
   `LibreHardwareMonitor.Windows.Forms/`, `Directory.Packages.props`, `global.json`:
   the only permitted deltas are the InternalsVisibleTo ItemGroups in the two csproj
   files. `LibreHardwareMonitor.sln` delta must be limited to the test-project graph.
6. **Library isolation.** The built `LibreHardwareMonitor.Tests.Library` output directory
   must contain no `LibreHardwareMonitor.Windows.Forms.dll` and its csproj no WinForms
   ProjectReference — the library boundary is real, not nominal.
7. **Stale references.** Independently grep the tracked tree (all extensions) for the old
   csproj path and flat old-file paths, over and above the extended gate — the gate is
   z's work; your check is the independent confirmation.
8. **Live five-point proof** (read-only): exactly one LHM process at
   `E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current\LibreHardwareMonitor.Windows.Forms.exe`;
   root task `\LibreHardwareMonitor` `Running` (result `267009`/`0x41301` = still running);
   task action and working directory match the live root; proxy-bypassed `/`, `/data.json`,
   `/metrics` HTTP 200 on the **LAN-bound** address (port from `listenerPort` in the live
   config, currently 8080 — `localhost` is refused while healthy, and the socket belongs to
   PID 4/System, both by design); current-day CSV still growing. Expect everything
   unchanged except CSV growth.

## Environment rules

- `$env:PYTHONDONTWRITEBYTECODE = '1'` before any Python command.
- After the sweep, do **not** clean up build outputs yourself unless instructed — report
  the `git clean -ndX` listing instead (the guarded cleanup is part of campaign close).
- Never `git clean -fdX`; never `python scripts/task_manager.py merge`.

## Exit Criteria

- `Invoke-LhmGates.ps1 -All` passed 8/8 included gates with the sanitized process
  `PSModulePath`, the runner untouched, and `ci-gates` skipped as excluded.
- The dispatcher discovers four test scripts and passes under both engines.
- Summed suite population equals the recorded baseline 259/258/1 with the named single
  skip in the Application suite and no new skips.
- `data.golden.json` is recorded as a pure rename and `DataJsonGoldenTests` passes.
- The product-root diff is limited to the InternalsVisibleTo ItemGroups plus the sln
  test-project graph; the Library suite's built dependency set proves WinForms isolation.
- The independent stale-reference grep over all tracked extensions is clean.
- The five-point live SND-HOST proof is recorded unchanged except CSV growth.
- A defect list (empty if none) names the owning lane for each finding.

## Do NOT

- Modify any tracked file. Your lane is evidence-only.
- Mark any criterion met without criterion-specific evidence.
- Write to `live-tracker.md`. **Single-writer override:** agent ab owns the tracker;
  return your row text (ID `SUITEVERIFY-001`, owner agent-aa) in your result payload.

## Result payload

Return: your tracker row; the per-gate result table; dispatcher counts under both engines;
per-suite and summed test totals against the baseline; the golden rename proof; the
product-root diff summary; the Library isolation proof; the stale-reference grep result;
the five live-proof observations; and a defect list (empty if none) with owning lane named.
