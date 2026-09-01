# Feature Spec: Direct HTTP Server Safety

**Status:** source implemented; live deployment not yet promoted
**Updated:** 2026-09-01

## Problem and motivation

Libre Hardware Monitor can serve its dashboard and sensor APIs directly over
plain HTTP. Before this hardening, any configured listener value that was not
currently assigned to a local interface silently became the HTTP.sys `+`
wildcard. A stale interface selection could therefore broaden exposure without
operator intent, while the menu could remain checked after listener startup
failed.

## Goals

- Preserve explicit all-interface choices.
- Accept a specific listener only while that IPv4 address is assigned locally.
- Fail closed for stale, malformed, or unsupported specific addresses.
- Keep the `Remote Web Server > Run` menu state aligned with listener startup.

## Non-goals

- Opening Windows Firewall, configuring routing, tunnels, TLS, or public DNS.
- Changing HTTP routes, response schemas, port defaults, or Basic-auth format.
- Enabling authentication or selecting a network trust boundary on the
  operator's behalf.
- Adding IPv6 listener configuration; the existing UI is IPv4-only.

## User-visible behavior and contracts

- `+`, `*`, and the UI's `0.0.0.0` choice are explicit wildcard requests and
  resolve to the HTTP.sys `+` host.
- The settings default `"?"` is the never-configured sentinel (the interface
  picker excludes it). It also resolves to the HTTP.sys `+` host, so a fresh
  profile that ticks `Remote Web Server > Run` still starts an all-interface
  listener instead of silently unchecking.
- An assigned local IPv4 address remains an exact listener host.
- A stale local address, malformed value, or IPv6-specific value causes
  listener startup to return false. It never broadens to a wildcard.
- When startup returns false, the `Run` option is cleared immediately and
  projected into settings rather than displaying a server state that was not
  achieved. Durable config write follows the normal autosave/exit path.
- `listenerIp`, `listenerPort`, `authenticationEnabled`, HTTP routes, and the
  `data.json` wire contract otherwise remain unchanged.

## Compatibility and risk

- A machine whose saved specific address disappeared will no longer start the
  server automatically. The operator must select a current address or the
  explicit all-interface option.
- A never-configured profile still starts on the all-interface wildcard because
  `"?"` is the unset sentinel, not a malformed address. The live SND-DESK
  profile already stores `+`, so this sentinel path is a fresh-profile contract
  rather than a promotion blocker there.
- Both `net10.0-windows` and `net472` use the same resolution code.
- HTTP.sys URL ACLs and Windows Firewall remain separate operational gates.
  A specific-IP prefix narrows the listener but is not an authorization
  boundary; authentication and firewall scope remain explicit operator
  decisions. Wildcard plus anonymous access must not be described as remotely
  safe merely because the process is listening.

## Acceptance criteria

- [x] Explicit wildcard selections resolve to `+`.
- [x] The never-configured `"?"` sentinel resolves to `+`.
- [x] Assigned IPv4 selections remain exact.
- [x] Stale, malformed, and IPv6-specific selections fail closed.
- [x] Failed startup clears the `Run` option.
- [x] No HTTP route or payload contract changes.
- [ ] A promoted live build is observed with the intended listener and firewall
  boundary before this document is marked deployed.

## Verification plan

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

Automated source verification on 2026-08-27:

- `HttpServerLifetimeTests`: 13/13 passed, including explicit wildcard aliases,
  assigned IPv4, stale/malformed/IPv6 rejection, and listener lifetime cases.
- Deterministic .NET suites: 383 passed, one established live-config test
  skipped, zero failed; both x64 Release target frameworks built with zero
  warnings and zero errors.
- The complete non-deploying gate set passed 9/9 in an isolated source clone.
  Live listener/menu/firewall behavior was not exercised by that fixture run.

Automated source verification on 2026-09-01:

- `HttpServerLifetimeTests` listener-resolution cases 10/10, including the
  never-configured `"?"` sentinel resolving to `+`.
- Deterministic .NET suites: 384 passed, one established live-config test
  skipped, zero failed; both x64 Release target frameworks built with zero
  warnings and zero errors.

After a future promotion, verify the exact process path, configured listener,
HTTP.sys registration, local `data.json` response, firewall rule scope, and the
absence of an unintended wildcard fallback.
