# Agent Task — Verify Fail-Closed

**Scope:** Read-only verification that the SND-DESK fail-closed guards still fail closed
on SND-HOST after the move, that the content-based deny-list still refuses deploying
commands, and that the extended stale-reference gate passes. Records evidence in its
result payload (writes no source file).

**Depends on:** Agent R (config/gate rewiring must be done)

**Output files:** none (verification-only; returns structured evidence in its result).

## Exit Criteria

- The relocated peer-safe fixture (`ops/deploy/snd-desk/Test-LhmLocalRelease.ps1`) run
  under `pwsh` proves the SND-DESK fail-closed guards still hold on this (snd-host)
  machine.
- `Invoke-LhmGates.ps1 -DryRun` shows the deny-list still refuses deploying commands at
  the new paths.
- The extended `Test-NoStaleReferences.ps1` passes.
- The gate-runner regression (`Test-LhmCiGates.ps1`) is green.

## Task

This agent proves the one property a path move could break in a way no build catches.
Run each check and record pass/fail + the key output line as evidence.

1. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\tests\Test-NoStaleReferences.ps1`
   — must pass.
2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Invoke-LhmGates.ps1 -DryRun`
   — confirm every deploying command (`New-LhmRelease`, `Publish-LibreHardwareMonitor`,
   `Install-`, `Finalize-`, `Restore-Legacy`, `Start-LibreHardwareMonitor`,
   `Clear-LhmRepositoryBuildOutputs`) is classified `refused`/excluded, not `included`.
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File ops\deploy\snd-desk\Test-LhmLocalRelease.ps1`
   — the relocated fail-closed fixture. **Use `pwsh`, not `powershell.exe`**: the configured
   gate engine is Windows PowerShell 5.1, whose `Get-FileHash` is unavailable on this
   machine (the `Microsoft.PowerShell.Utility` module resolves to the PowerShell 7 path).
   This is the same open defect as Plan-003 criterion 4. Running under `pwsh` (7.6.4)
   bypasses it and exercises the fail-closed guards. Report the assertion count and
   confirm fail-closed on snd-host.
4. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1`
   — gate-runner regression, must be green.

## Constraints

- Write no source file. If you find a defect, report it in your result; do not patch
  another agent's file.
- Do not promote, deploy, or touch the live runtime/release store.

## Verification

The four commands above ARE the verification. Report each result. If the `pwsh` run still
fails for a reason other than Get-FileHash, report the exact failure (it may indicate the
move broke fail-closed). (Do not push. Return your tracker row text in your result.)
