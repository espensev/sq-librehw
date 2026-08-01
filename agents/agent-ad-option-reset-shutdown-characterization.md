# Agent Task — Option, Reset, and Shutdown Characterization

**Plan:** `plan-007`
**Baseline:** `9762f4d69882ff9d3fb7c98fef0c6d1c3e981232`
**Depends on:** none
**Exclusive outputs:**

- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/HardwareOperationCoordinatorTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/UiShutdownCoordinatorTests.cs`

## Goal

Add eight deterministic facts that pin the current option/reset drain and UI shutdown coordination rules. Characterize existing public/internal seams; do not instantiate `MainForm` or add a production seam.

## Read first

- `AGENTS.md`
- both exclusive test files
- `LibreHardwareMonitor.Windows.Forms/UI/HardwareOperationCoordinator.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`
- read-only context in `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` around coordinator construction, option/reset dispatch, session end, and close

## Required facts

Add these tests to `HardwareOperationCoordinatorTests`:

1. `ActiveOption_SameKeyBurstRunsOneLatestFollowUp` — while the first key is active, a burst for the same key produces exactly one follow-up carrying the latest value.
2. `ResetRequestedWhileActive_CoalescesOneFollowUpAfterOptionsQueuedDuringReset` — a reset requested during an active reset produces one follow-up, and options queued during the first reset drain before that follow-up.
3. `ExecutorFailures_RaiseTypedEventsAndContinueOrderedDrain` — option and reset executor failures raise the correct typed failure events without stopping later ordered work; `WhenIdleAsync` completes and busy state clears.
4. `CancellationAfterNonCooperativeActiveOption_DropsPendingAndRejectsNewRequests` — cancellation cannot abort a deliberately non-cooperative active executor, but it drops pending work, rejects later admission, and becomes idle after the active call is released.

Add these tests to `UiShutdownCoordinatorTests`:

5. `UiCloseTakeover_CompletesQueuedBackgroundRequestTaskExactlyOnce` — a UI-thread close takes ownership before a queued background callback, both callers share/observe one completion, and delayed dispatch cannot execute shutdown again.
6. `BeginInvokeStyleSessionEndedWait_DoesNotReturnBeforeAsyncShutdownCompletes` — model queued `BeginInvoke`; the session-end waiter blocks until the asynchronous shutdown task completes.
7. `ShutdownFailure_FaultsSharedCompletionAndDoesNotRetry` — the exact shutdown exception faults every shared completion and later requests do not rerun shutdown.
8. `RequestAsyncDispatchFailure_ReleasesClaimForUiRetry` — exact dispatch failure reaches the first task/caller, clears the claim, and a later UI request succeeds once.

Reuse local queues, cancellation sources, and run-continuations-asynchronously completion sources. Keep timeouts bounded and assertions ordered.

## Boundaries

- Edit only the two exclusive test files.
- Do not rename or weaken existing facts merely to satisfy new assertions.
- Do not construct `MainForm`, use reflection against its private close path, or change comments/product code.
- Do not modify `live-tracker.md`; return tracker evidence in the completion summary for Agent AF.
- Do not access live hardware, registry, Task Scheduler, ports, configuration, or operations paths.

## Verification

```powershell
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
git diff --check
git status --short
```

The focused population must grow by exactly 8 facts. The full Application suite must discover 139 tests: 138 passed and the existing one live-config skip.

## Handoff

Commit the two owned files with `test(lifecycle): characterize ordered shutdown work`. Return the commit SHA, exact counts, files changed, any issue, and one concise tracker-row update. Do not merge or edit other lanes.
