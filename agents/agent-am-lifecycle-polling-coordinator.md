# Agent Task — Application Lifecycle and Polling Coordinator

**Plan:** `plan-010`
**Product baseline:** `eed51e4ce3605e294937c41959e53bdd51b46dd3`
**Depends on:** none
**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/UI/ApplicationLifecycleCoordinator.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/ApplicationLifecycleCoordinatorTests.cs`

## Goal

Add one internal delegate-driven coordinator that owns application hardware
initialization, the existing ordered option/reset coordinator, independent
single-flight poll admission, cancellation, drain, and exactly-once close. Add
eight deterministic facts. Do not wire `MainForm`; Agent AN owns that step.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-010-application-lifecycle-and-polling.md`
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`, especially construction,
  initialization, option/reset, polling, and shutdown
- `LibreHardwareMonitor.Windows.Forms/UI/HardwareOperationCoordinator.cs`
- `LibreHardwareMonitor.Windows.Forms/UI/UiShutdownCoordinator.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/HardwareOperationCoordinatorTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Application/UiShutdownCoordinatorTests.cs`

Run the unchanged 19 coordinator facts before editing and stop if they fail.

## Internal contract

Create `ApplicationLifecycleCoordinator` in the existing
`LibreHardwareMonitor.Windows.Forms.UI` namespace. It must be `internal sealed`,
compatible with `net472`, and have no typed dependency on a Form, control,
`Node`, `HttpServer`, `PersistentSettings`, snapshot DTO, or concrete hardware
implementation. Constructor delegates supply:

- asynchronous open work that accepts the lifecycle cancellation token;
- synchronous `HardwareOptionKind` application;
- synchronous reset;
- asynchronous close work that is not canceled after stop begins.

Expose the smallest MainForm-facing API needed for:

- `InitializeAsync()`;
- `RequestOption(HardwareOptionKind, bool)` and `RequestReset()`;
- `TryBeginPoll()` and `CompletePoll()`;
- idempotent `BeginStop()` and `StopAsync()`;
- initialization, option/reset, and stopping state needed by existing UI logic;
- pass-through `StateChanged`, `OptionFailed`, and `ResetFailed` events;
- deterministic disposal after stop has completed.

Names may vary only where C# clarity requires it. Keep the surface internal and
do not introduce an interface, package, project, public API, or async-disposal
dependency.

## Required behavior

- Own the lifecycle `CancellationTokenSource`, lifecycle `SemaphoreSlim`,
  initialization task/completion/state, one unchanged
  `HardwareOperationCoordinator`, polling state/completion, and one shared stop
  task. Callers must not need parallel copies of those fields.
- Before initialization begins, `RequestOption` applies synchronously so the
  initial `Computer.Open` sees the configured hardware groups.
- Once initialization begins, option/reset work uses the existing coordinator,
  waits for the initialization barrier, and is serialized through the lifecycle
  gate exactly as today. Initialization failure releases the barrier and is
  observable by the `InitializeAsync` caller.
- Preserve the existing latest-value-per-option and ordered-reset behavior by
  composing `HardwareOperationCoordinator` unchanged.
- Polling is independently single-flight: a busy tick is rejected, completion
  admits the next tick, and stop rejects new ticks. Do **not** place active poll
  work behind the lifecycle gate or newly serialize it with option/reset work;
  current polling may overlap those operations.
- `BeginStop` is idempotent, rejects new option/reset/poll admission, and
  cancels pending lifecycle work. `StopAsync` waits for initialization (also
  after an initialization fault), the option/reset drain, and any admitted poll
  before invoking close exactly once. Concurrent callers share the same stop
  completion.
- Never dispose a cancellation source, gate, or poll completion while work can
  still observe it. Disposal is deterministic and valid only after stop has
  completed.
- Callback exceptions must not corrupt coordinator state. Follow the existing
  coordinator's bounded/debug-observed event style.

## Exactly eight facts

Create one `ApplicationLifecycleCoordinatorTests` class with exactly eight
`[Fact]` methods and no `[Theory]` methods or data rows:

1. `RequestOption_BeforeInitialization_AppliesSynchronously`
2. `InitializeAsync_BlocksQueuedOptionUntilOpenCompletes`
3. `InitializeAsync_FailureReleasesBarrierAndStopStillClosesOnce`
4. `TryBeginPoll_DropsBusyTicksUntilCompletion`
5. `ActivePoll_DoesNotSerializeOptionWork`
6. `BeginStop_RejectsNewOptionResetAndPollAdmission`
7. `StopAsync_WaitsForInitializationOperationsAndActivePollBeforeClose`
8. `ConcurrentStopAsync_ClosesExactlyOnceAndSharesCompletion`

Use delegate fakes, `TaskCompletionSource` barriers with
`RunContinuationsAsynchronously`, bounded waits, and event/order records. Do not
sleep, access hardware, open listeners, read live configuration, or construct a
Form. Every acquired barrier must be released in `finally` so a failed assertion
cannot hang the runner.

## Exit Criteria

- Exactly the two exclusive new files change and the test file has exactly
  eight facts.
- New facts pass 8/8. Together with unchanged
  `HardwareOperationCoordinatorTests` and `UiShutdownCoordinatorTests`, the
  family passes exactly 27/27.
- The full Application project is exactly 160 discovered, 159 passed, and the
  one established live-config opt-in skip.
- Both x64 Release WinForms targets build with zero warnings/errors.
- `HardwareOperationCoordinator.cs`, its tests, `UiShutdownCoordinator.cs`, its
  tests, `MainForm.cs`, `Computer.cs`, projects, packages, settings, HTTP,
  snapshot/golden, docs, tracker, operations, and live runtime remain unchanged.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~ApplicationLifecycleCoordinatorTests|FullyQualifiedName~HardwareOperationCoordinatorTests|FullyQualifiedName~UiShutdownCoordinatorTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Application\LibreHardwareMonitor.Tests.Application.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
git diff --check
git status --short
```

## Do not

- Do not edit outside the two exclusive outputs or wire MainForm.
- Do not rewrite `HardwareOperationCoordinator`, serialize polling with
  option/reset, change Computer semantics, or add policy/retries.
- Do not edit campaign truth, `live-tracker.md`, or the operations tree; do not
  merge, deploy, promote, or clean another checkout.

## Handoff

Commit with `refactor(lifecycle): add application coordinator`. Return the
commit SHA, exact 8/8, 27/27, 160/159/1, build results, exact file list, disposal
and no-hang evidence, any concern, and one concise proposed tracker row.
