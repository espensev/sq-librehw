# Agent Task — Extract HTTP Listener Adapter

**Plan:** `plan-009`
**Depends on:** Agent AJ
**Exclusive outputs:**

- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpListenerDispatchService.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`

## Goal

Move the existing `System.Net.HttpListener` lifecycle and terminal request
envelope into one internal sealed service. `HttpServer` remains the stable
public facade and retains every route, credential check, mutation, producer,
resource, serializer, and response writer. This is a mechanical seam
extraction, not an HTTP redesign.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-009-http-listener-dispatch-service.md`
- Agent AJ's committed `HttpServerWireContractTests.cs`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerLifetimeTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerAuthenticationTests.cs`
- all Sensor, Prometheus, data.json, and web-retirement Contracts tests
- `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs` only to confirm the facade
  caller; do not edit it

Before editing, run AJ's six facts and the 12-test wire/lifetime/authentication
slice on the inherited baseline. Stop if either fails.

## Internal service

Create this collision-free internal contract in the existing
`LibreHardwareMonitor.Windows.Forms.Utilities` namespace so the moved bounded
pool retains its type/namespace and existing lifetime tests remain untouched:

```csharp
internal sealed class HttpListenerDispatchService
{
    internal const int DefaultMaxConcurrentHandlers = 16;

    internal HttpListenerDispatchService(
        Func<HttpListenerContext, CancellationToken, Task> dispatchAsync,
        int maxConcurrentHandlers = DefaultMaxConcurrentHandlers);

    internal bool PlatformNotSupported { get; }

    internal bool Start(
        string listenerIp,
        int listenerPort,
        bool authEnabled,
        out string effectiveListenerIp);

    internal Task<bool> StopAsync();
    internal void Abort();
}
```

Reject a null dispatch callback and non-positive concurrency. Construct the
listener exactly once with `IgnoreWriteExceptions = true`; preserve the current
`PlatformNotSupportedException` behavior. Keep implementation compatible with
`net472` and introduce no interface, package, project, or public type.

## Move without semantic rewriting

Move these responsibilities out of `HttpServer`:

- listener, lifecycle gate, session cancellation source, listener task;
- local-interface validation and invalid-IP fallback to `+`;
- prefix, realm, startup authentication scheme, listener start;
- accept loop and bounded scheduling;
- error 50 five-second retry, error 995 termination, cancellation/disposal
  exits, and unexpected-error debug logging;
- five-second listener wait and handler drain, including retention of a timed-
  out canceled session so restart cannot overwrite it;
- cancellation registration that aborts the context, dispatch callback
  invocation, expected cancellation exception handling, unexpected fault to
  status 500, and unconditional best-effort response close;
- final best-effort abort mechanics;
- `BoundedRequestHandlerPool` unchanged in name, namespace, concurrency,
  accounting, fault observation, and drain semantics, but physically moved to
  the new service file.

`Start` must initialize `effectiveListenerIp` from its input and change it to
`+` as soon as the current fallback decision is made, before later prefix/start
operations can fail. `HttpServer.StartHttpListener` must assign that output back
to `ListenerIp` on both success and failure, preserving current persistence
behavior.

## Keep in HttpServer

- Public constructor and all public/internal test-visible facade signatures.
- `AuthEnabled`, `ListenerIp`, `ListenerPort`, `UserName`, `PasswordSHA256`,
  `SetPassword`, and dynamic username/password hash checks.
- `DispatchRequestAsync` and its complete method/path switch.
- Sensor lookup/control/reset/origin behavior.
- resource mapping and streaming.
- Plan-008 snapshot capture/projection, data buffer gate, pooled copy, gzip.
- Prometheus generation, Sensor JSON, generic response writing, and hashing.
- `_root`, `_rootElement`, `_version`, and all endpoint-specific constants.

Construct the service with `DispatchRequestAsync`. Delegate `PlatformNotSupported`,
start, stop, quit, and finalizer mechanics through it without changing public
signatures. `MainForm`, `AuthForm`, and `InterfacePortForm` must stay byte-identical.

## Exit criteria

- Exactly the two exclusive outputs change.
- The adapter retains no `Node`, `IElement`, hardware, sensor, settings, form,
  snapshot, projector, resource, or serializer reference.
- `HttpServer` no longer owns listener/session/accept/pool/context-finalization
  mechanics; it still owns every route and endpoint/content policy.
- AJ's unchanged facts remain 6/6; wire/lifetime/authentication remains 12/12;
  Contracts remains 73/73.
- Both x64 Release `net472` and `net10.0-windows` WinForms targets build with
  zero warnings and errors; Sensor, Prometheus, data.json golden, and web tests
  remain green.
- No UI, existing test, golden, web, project/package, solution, documentation,
  tracker, operations, or live-runtime file changes.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests|FullyQualifiedName~HttpServerLifetimeTests|FullyQualifiedName~HttpServerAuthenticationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServer|FullyQualifiedName~DataJson|FullyQualifiedName~WebDashboardRetirementTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 --no-restore
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 --no-restore
node webtests\selftest.node.js
node --test webtests\console.tests.js webtests\workspace.tests.js
git diff --check
git status --short
```

## Do not

- Do not edit outside the two exclusive outputs, including AJ's tests.
- Do not extract `DispatchRequestAsync`, authentication policy, route handlers,
  content producers, response writers, or buffer/gzip code.
- Do not add retries, abstractions, public APIs, packages, projects, settings,
  routes, or cleanup unrelated to the move.
- Do not touch `live-tracker.md`, merge, deploy, or clean another checkout.

## Handoff

Commit with `refactor(http): extract listener dispatch service`. Return the
commit SHA, exact focused/full/build/web results, old/new line ownership, exact
file list, any concern, and one concise proposed tracker row.
