# Agent Task — HTTP Wire Characterization

**Plan:** `plan-009`
**Product baseline:** `a45790c646ce40be00c627dbcc1f99f681a3f639`
**Depends on:** none
**Exclusive output:**

- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerWireContractTests.cs`

## Goal

Characterize the existing public `HttpServer` over its real loopback wire before
the listener mechanics move. Add one deterministic test file and no production
abstraction. The same committed facts must pass before and after Agent AK's
extraction.

## Read first

- `AGENTS.md`
- `docs/campaign-plan-009-http-listener-dispatch-service.md`
- `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerLifetimeTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerAuthenticationTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerSensorApiTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/HttpServerPrometheusTests.cs`
- `LibreHardwareMonitor.Tests/LibreHardwareMonitor.Tests.Contracts/DataJsonGoldenTests.cs`

Reuse the existing fake-hardware, sensor, control, `PersistentSettings`,
`UnitManager`, `Node`, and `HardwareNode` patterns inside the exclusive new file.
Do not edit an existing fixture to share helpers.

## Required facts

Create one `HttpServerWireContractTests` class containing exactly six `[Fact]`
methods and no `[Theory]` methods or data rows:

1. `Routes_PreserveMethodStatusContentTypeAndHeaders`
   - Loop a table inside this fact and cover root/static success, missing static
     and icon resources, `/data.json`, `/metrics`, `/Sensor`, GET
     `/ResetAllMinMax`, ordinary POST `/Sensor`, unsupported/case-mismatched
     POST, and an unsupported HTTP verb.
   - Pin the current status, `Allow`, content type, cache, CORS, and important
     route-specific header presence/absence. Do not make generic POST responses
     acquire the data/metrics/Sensor response headers.
2. `DataJson_PreservesIdentityAndGzipPayloads`
   - Request `/data.json` with identity and gzip encodings, disable automatic
     decompression, inflate gzip explicitly, and require equal uncompressed
     bytes plus the established content encoding/length/cache/CORS behavior.
3. `Mutations_PreservePostOnlyAndOriginPolicy`
   - Through HTTP, prove GET Set/ResetMinMax never writes; cross-origin POST
     `/Sensor` returns HTTP 200 failure JSON without a write; same-origin and
     headerless POST Set succeed and clamp; cross-origin POST
     `/ResetAllMinMax` is 403 without reset; allowed POST reset-all resets and
     returns the full data payload; GET reset-all is 405 with `Allow: POST`.
4. `BasicAuthentication_PreservesFailureAndSuccessResponses`
   - Start with auth enabled, call `SetPassword` once, and prove absent, wrong
     user, and wrong password fail with 401 and the existing HTML contract while
     exact credentials reach an endpoint successfully. Do not weaken the test
     if HTTP.sys performs the initial Basic challenge.
5. `DispatchFailure_Returns500AndClosesResponse`
   - Use a deliberately invalid in-memory server root and a route that reaches
     dispatch. Require a bounded client completion with status 500; a hang,
     timeout, or leaked listener is failure evidence.
6. `ListenerLifecycle_StartStopAndRestartRemainIdempotent`
   - Prove successful start, repeated start, reachable response, successful
     stop, repeated stop, restart on the same facade/port, reachable response,
     and final stop/quit.

## Loopback harness

- Acquire each port by briefly binding `TcpListener(IPAddress.Loopback, 0)`,
  record the assigned port, release it, and start `HttpServer` immediately.
  Never use a fixed or live-runtime port.
- Bind only `127.0.0.1`. Use `HttpClientHandler.UseProxy = false`, explicit
  request headers, disabled automatic decompression where bytes matter, and a
  bounded timeout of at most ten seconds.
- Serialize this class through an xUnit collection so its listeners cannot run
  concurrently with each other. Helper classes and the collection definition
  may live in the same file but add no extra test cases.
- Every start must have `try/finally` cleanup that calls stop/quit even when an
  assertion fails. Dispose clients, requests, responses, streams, and temporary
  listeners. Do not add URL ACLs, firewall rules, tasks, or configuration.
- If the platform reports `PlatformNotSupported`, return consistently as the
  existing lifetime fact does; do not convert it into a skip.

## Exact current behavior to preserve

- GET matching is case-insensitive. POST segment matching remains exact and a
  non-`Sensor` segment returns HTTP 200 failure JSON.
- POST `/ResetAllMinMax` returns data.json when allowed and 403 JSON when a
  browser origin is rejected. POST `/Sensor` mutation failures remain HTTP 200.
- GET `/ResetAllMinMax` is 405 with `Allow: POST`; unknown verbs are 404.
- Authentication failure is 401 HTML. `/data.json` supports gzip. `/metrics`
  and Sensor/data responses retain their existing cache/CORS/content headers.
- Successful, canceled, and faulted handling must terminate the response.

## Exit Criteria

- Exactly the exclusive new file changes and it contains exactly six facts.
- All six pass at product baseline `a45790c`; the full Contracts project grows
  from 67 to exactly 73 passing cases.
- Existing HttpServer lifetime/authentication facts plus the new facts pass
  exactly 12/12.
- No production, existing test, project, package, solution, web, golden,
  documentation, tracker, operations, or live-runtime file changes.
- The owned file is committed and the handoff includes counts, cleanup proof,
  the chosen ports, and a proposed tracker row.

## Verification

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
Remove-Item Env:LHM_LIVE_CONFIG_PATH -ErrorAction SilentlyContinue
Remove-Item Env:LHM_LIVE_CONFIG_EXPECTED_LENGTH -ErrorAction SilentlyContinue
dotnet restore LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~HttpServerWireContractTests|FullyQualifiedName~HttpServerLifetimeTests|FullyQualifiedName~HttpServerAuthenticationTests" --logger "console;verbosity=minimal" --tl:off
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64 --no-restore --logger "console;verbosity=minimal" --tl:off
git diff --check
git status --short
```

## Do not

- Do not edit outside the exclusive output or add a fake transport layer.
- Do not change expectations to anticipate Agent AK; characterize HEAD exactly.
- Do not bind a non-loopback address or touch the operational tree.
- Do not edit `live-tracker.md`, merge, or clean another checkout.

## Handoff

Commit with `test(http): characterize listener wire contract`. Return the commit
SHA, exact 6/6, 12/12, and 73/73 results, exact file list, port/cleanup evidence,
any issue, and one concise proposed tracker row.
