# Discovery - LibreHardwareMonitor Sync And Upgrade

**Goal:** Evaluate this copied LibreHardwareMonitor checkout as the new local home, set up sync against the upstream repository, and identify .NET/package upgrade options.
**Date:** 2026-05-20
**Status:** complete historical sync; local modernization notes updated 2026-06-06; fork now has an `origin` remote and was re-audited against upstream on 2026-07-06 — see updates at end.
**Recommended next:** Save the current dirty dashboard-route work before any upstream integration, then merge or cherry-pick upstream in a review branch because this fork has intentional local changes in the same web-server, PawnIO, workflow, and package files.

---

> Current-state note: the Q1-Q6 findings below are the 2026-05-20 discovery record, with
> modernization follow-up through 2026-06-06. They are retained as historical evidence. Use the
> 2026-06-25 and 2026-07-06 update sections, plus
> [`local-ui-customizations.md`](local-ui-customizations.md), for current remotes, test projects,
> upstream delta, and fork-divergence status.

## Questions

1. What repository/remotes/branch state does this copy have?
2. What project structure and target frameworks are currently used?
3. How far is the local branch behind upstream, and what does upstream change?
4. What package and runtime upgrades are available?
5. What build/test baseline exists locally?
6. What sync setup was applied?

---

## Findings

### Q1: What repository/remotes/branch state does this copy have?

**Historical answer:** The checkout was on `master`, tracked `upstream/master`, and was then even with upstream at `abfc4f5`. The only pre-sync worktree change was a local deletion of `LibreHardwareMonitor/Resources/amd.png`; that deletion was preserved in a Git stash before fast-forwarding.

**Evidence:**
- Pre-sync `git status --short --branch` reported `## master...upstream/master [behind 35]` and `D LibreHardwareMonitor/Resources/amd.png`.
- Pre-sync `git rev-list --left-right --count HEAD...upstream/master` reported `0 35`; post-sync it reported `0 0`.
- `git log -1 --format="%H %ad %s" --date=short HEAD` reported local `HEAD` as `9d9bb0084b1a686f86ae37edde19e8799ff73cdc 2026-04-02 Fix NCT6687DR fan control for system fans (index 9-15) (#2294)`.
- `git log -1 --format="%H %ad %s" --date=short upstream/master` reported upstream as `abfc4f5705419d62cd6000f45a92563415c165fc 2026-05-15 Update README to remove WinGet installation section`.
- `git stash list --date=local` reported `stash@{...}: On master: pre-upstream-sync amd.png deletion`.

**Implications:**
- Upstream sync is complete. If the image deletion was intentional, reapply it at `LibreHardwareMonitor.Windows.Forms/Resources/amd.png`; otherwise drop the stash once no longer needed.

### Q2: What project structure and target frameworks are currently used?

**Answer:** The current local solution has three projects: the WinForms app, `LibreHardwareMonitorLib`, and `Aga.Controls`. This is not an old .NET-only checkout: the app targets `net472` and `net10.0-windows`; the library targets `net472`, `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`; `Aga.Controls` now targets `net472` and `net10.0-windows` after local modernization.

**Evidence:**
- `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj:4` - `<TargetFrameworks>net472;net10.0-windows</TargetFrameworks>`.
- `LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj:3` - `<TargetFrameworks>net472;netstandard2.0;net8.0;net9.0;net10.0</TargetFrameworks>`.
- `Aga.Controls/Aga.Controls.csproj` - `<TargetFrameworks>net472;net10.0-windows</TargetFrameworks>`.
- `dotnet --info` reported SDK `10.0.300`, runtimes `8.0.27` and `10.0.8`, and no `global.json`.

**Implications:**
- The main modernization issue is not adding .NET 10; it is deciding how much legacy compatibility to keep across `net472`, `netstandard2.0`, and older WinForms assumptions.

### Q3: How far is the local branch behind upstream, and what does upstream change?

**Answer:** Upstream had 35 new commits and they have now been fast-forwarded into local `master`. The important changes are: package updates to the latest NuGet versions currently available here, DiskInfoToolkit 2.x integration, several hardware support/fix commits, native AOT/trimming analyzer metadata for the library, and a project rename from `LibreHardwareMonitor` to `LibreHardwareMonitor.Windows.Forms`.

**Evidence:**
- `git log --oneline HEAD..upstream/master` included:
  - `aa7c8be feat: Add Native AOT compilation support for LibreHardwareMonitorLib (#2280)`
  - `9e831e0 Update for DiskInfoToolkit Version 2.0.0. (#2298)`
  - `9fce235 Update for DIT 2.0.1. (#2304)`
  - `e1f284c Prepare for Avalonia app - rename LHM UI project (#2360)`
  - `91b0178 Bump System.IO.Ports from 10.0.7 to 10.0.8 (#2373)`
- `git diff --name-status --find-renames HEAD..upstream/master` shows `LibreHardwareMonitor/LibreHardwareMonitor.csproj` renamed to `LibreHardwareMonitor.Windows.Forms/LibreHardwareMonitor.Windows.Forms.csproj` and the resource tree moved with it, including `Resources/amd.png`.
- `git show upstream/master:LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj` shows upstream added AOT/trimming properties for modern TFMs and moved package references to `10.0.8` / `DiskInfoToolkit` `2.1.0`.

**Implications:**
- The synced tree is now the best base for future work. The rename is the main path-breaking change for any local scripts or integrations that reference `LibreHardwareMonitor\...`.

### Q4: What package and runtime upgrades are available?

**Answer:** After syncing upstream, `dotnet list package --outdated` reports no updates with the current sources. .NET 10 is the current LTS line, while .NET 8 and .NET 9 both end support on 2026-11-10. .NET Framework 4.7.2 is still active but remains tied to Windows OS lifecycle support.

**Evidence:**
- Pre-sync `dotnet list LibreHardwareMonitor.sln package --outdated` reported:
  - `DiskInfoToolkit` `1.2.1` -> `2.1.0`
  - `System.Management`, `System.Resources.Extensions`, `System.Text.Json`, `System.IO.Ports`, and `System.Threading.AccessControl` `10.0.5` -> `10.0.8`
  - `Microsoft.Windows.CsWin32` `0.3.269` -> `0.3.275`
- Post-sync `dotnet list LibreHardwareMonitor.sln package --outdated` reported no updates for `LibreHardwareMonitorLib`, `LibreHardwareMonitor.Windows.Forms`, or `Aga.Controls`.
- Official .NET support policy, last updated 2026-05-14, lists `.NET 10` LTS active until 2028-11-14, `.NET 9` STS until 2026-11-10, and `.NET 8` LTS until 2026-11-10: https://dotnet.microsoft.com/en-us/platform/support/policy
- Official .NET Framework policy lists `.NET Framework 4.7.2` as active and `.NET Framework 4.8.1` as the latest Framework version: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework

**Implications:**
- The near-term package upgrade is complete via upstream sync.
- Medium-term options:
  - Keep all upstream TFMs for package compatibility.
  - For a local-only app, consider making `net10.0-windows` the primary build and treating `net472` as legacy output.
  - Do not spend effort on `net9.0` as a strategic target; it leaves support on 2026-11-10.

### Q5: What build/test baseline exists locally?

**Historical answer:** The synced local solution initially built successfully in Debug x64 on SDK `10.0.300` with modernization warnings. Subsequent local work cleared those warnings by multi-targeting `Aga.Controls`, removing the Framework-only web/install references from the modern app target, and moving high-DPI configuration into project/runtime setup. At this discovery point, no C# test projects were found. Current fork work has since added `LibreHardwareMonitor.Tests`; see the 2026-07-06 update and [`local-ui-customizations.md`](local-ui-customizations.md).

**Evidence:**
- Post-sync `dotnet build LibreHardwareMonitor.sln -c Debug -p:Platform=x64` completed with `Build succeeded`, `0 Error(s)`, and `5 Warning(s)`.
- The initial build warnings included:
  - `NU1702` for `Aga.Controls` resolved as `.NETFramework,Version=v4.7.2` for the `net10.0-windows` app target.
  - `MSB3245` for `System.Configuration.Install` and `System.Web` in the `net10.0-windows` app target.
  - `WFO0003` for high DPI settings in `LibreHardwareMonitor.Windows.Forms\Resources\app.manifest`.
- On 2026-06-06, `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net10\"` completed with `Build succeeded`, `0 Warning(s)`, and `0 Error(s)`.
- On 2026-06-06, `dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64 -p:OutDir="$env:TEMP\sq-librehw-verify\net472\"` completed with `Build succeeded`, `0 Warning(s)`, and `0 Error(s)`.
- The normal `net10.0-windows` release build without redirected `OutDir` was blocked by a running `Libre Hardware Monitor` process locking `bin\Release\net10.0-windows\LibreHardwareMonitorLib.dll`; this was an environment lock, not a compile failure.
- `rg --files -g "*Test*" -g "*Tests*" -g "*.csproj"` found only the three production `.csproj` files.

**Implications:**
- The current copy is usable as a clean build base. Build remains the main automated regression gate until a C# test project exists.

### Q6: What sync setup was applied?

**Answer:** The repo-local Git config now uses the canonical upstream URL, prunes upstream refs on fetch, and restricts pulls to fast-forward only.

**Evidence:**
- `git config --get-regexp "^(remote|branch|pull)\."` reported:
  - `remote.upstream.url https://github.com/LibreHardwareMonitor/LibreHardwareMonitor.git`
  - `remote.upstream.fetch +refs/heads/*:refs/remotes/upstream/*`
  - `remote.upstream.prune true`
  - `branch.master.remote upstream`
  - `branch.master.merge refs/heads/master`
  - `pull.ff only`

**Implications:**
- `git fetch upstream` and `git pull --ff-only` are now the intended sync path. Because this repo has no `origin` remote, there is not yet a configured personal fork/push target.

---

## Cross-Cutting Analysis

### Constraints

- The pre-existing local deletion of `LibreHardwareMonitor/Resources/amd.png` is preserved in stash rather than applied to the synced tree.
- Upstream renamed the app folder to `LibreHardwareMonitor.Windows.Forms`, so local path-based scripts need updates.
- `Aga.Controls` now multi-targets `net472` and `net10.0-windows`, but it still carries legacy WinForms code paths and conditional behavior.
- No automated C# test project exists in this checkout; build is the main regression gate currently available.

### Risks

| Risk | Likelihood | Impact | Notes |
|------|------------|--------|-------|
| Reapplying the old `amd.png` deletion to the wrong path | Medium | Low | The upstream path is now `LibreHardwareMonitor.Windows.Forms/Resources/amd.png`. |
| Local scripts/integrations break after the upstream project rename | High | Medium | The app project path changes from `LibreHardwareMonitor\...` to `LibreHardwareMonitor.Windows.Forms\...`. |
| Removing `net472` too early breaks legacy consumers or app packaging | Medium | High | Upstream still intentionally ships `net472`. |
| Modern .NET compatibility assumptions regress | Medium | Medium | Keep both app target builds clean and watch conditional `NETFRAMEWORK` paths during future changes. |

### Open Questions

- Was the `amd.png` deletion intentional? That determines whether to drop the stash or delete `LibreHardwareMonitor.Windows.Forms/Resources/amd.png`.
- Is this repository meant to stay as a local-only mirror, or should it get an `origin` remote for a personal fork?

---

## Recommendation

This does not need a multi-agent campaign. Upstream sync is complete. The practical next step is:

1. Decide whether to drop the preserved `amd.png` deletion stash or reapply it to `LibreHardwareMonitor.Windows.Forms/Resources/amd.png`.
2. Keep the `net10.0-windows` and `net472` app builds clean during feature work.
3. Decide whether the local fork should stay upstream-compatible or become a .NET 10-first app/library fork with legacy targets removed.

---

## Update 2026-06-25

- **`origin` remote now exists.** This is no longer a local-only mirror: it is a personal fork at
  `espensev/sq-librehw` (remote `origin`) with a PR-based workflow and GitHub Actions CI. This answers
  the open question above ("local-only mirror vs personal fork"). `upstream` remains
  `LibreHardwareMonitor/LibreHardwareMonitor` for sync.
- **`upstream` is shallow-fetched** (depth-1 graft at its tip), and fork history is now disjoint from
  upstream by SHA, so `git log master..upstream/master` is misleading. Run
  `git fetch upstream master --deepen=50` before comparing or cherry-picking from upstream.
- **Fresh upstream triage (10 substantive commits).** The fork is current: 8/10 were already present
  (content-verified). Two were genuinely missing and were backported via PR #20:
  - #2382 — web-server embedded-resource prefix for the renamed assembly (the web UI was 404ing).
  - #2386 — guard the `Publish to NuGet` step so it no longer fails on every fork merge.
  Dependency bumps are handled by this fork's own Dependabot (we are ahead of upstream on CsWin32).
  Also backported earlier the same day: #2390 (web-server password double-encoding auth fix).
- See [`local-ui-customizations.md`](local-ui-customizations.md) for the catalog of intentional local
  divergences (now including local build version stamping and the NuGet fork guard).

## Update 2026-07-06

- **Fresh fetch status.** `git fetch upstream --prune` moved `upstream/master` from `0c05d35` to
  `9837983`; `git fetch origin --prune` moved `origin/master` from `db1d2d5` to `a134b54`.
  Current local `HEAD` is `34e1f09` on `feat/web-dashboard-versioned-routes` with a dirty working
  tree. `git rev-list --left-right --count HEAD...upstream/master` reports `108 11`, with merge-base
  `abfc4f5705419d62cd6000f45a92563415c165fc`. `git rev-list --left-right --count HEAD...origin/master`
  reports `2 1`, so the fork remote also has one merge commit not in this checkout.
- **Incoming upstream queue.** The 11 upstream commits not literally present in this branch are:
  #2382 resource names for renamed assemblies, #2385 DiskInfoToolkit update, #2386 NuGet publish
  fork guard, #2384 NaN web-server crash fix, #2389 README nightly-link fix, #2390 auth
  double-encoding fix, #2397/#2405/e8a6249 package bumps, `0c05d35` Dependabot directory update, and
  #2411 PawnIO manifest-resource lookup.
- **Already covered or superseded locally.** The local fork already carries content-equivalent or
  stronger versions of #2382, #2384, #2386, #2390, and #2411. Evidence: `HttpServer.cs` uses the
  executing assembly resource prefix and maps non-finite JSON values to `null`; the workflow guards
  NuGet publishing to the upstream repository; auth password setting avoids the restart
  double-encoding path; `MainForm.ExtractPawnIO` uses `typeof(MainForm).Assembly.GetName().Name +
  ".Resources.PawnIO_setup.exe"`.
- **Package/dependency posture.** Upstream package commits are not a straight fast-forward because
  this fork uses central package management in `Directory.Packages.props`. Current local package
  versions are already ahead for several relevant packages (`DiskInfoToolkit` `2.1.1`,
  `System.IO.Ports`/`System.Management`/`System.Resources.Extensions`/`System.Threading.AccessControl`
  `10.0.9`, `Microsoft.Windows.CsWin32` `0.3.298`). Still compare upstream project files during the
  next sync because upstream edited package references and Dependabot configuration directly.
- **Integration guidance.** Do not pull into the current dirty checkout. Save or commit the existing
  dashboard route/spec work first, then integrate upstream in a branch and expect conflict review in
  `LibreHardwareMonitor.Windows.Forms/Utilities/HttpServer.cs`,
  `LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs`, `.github/workflows/master.yml`, project/package
  files, and web-dashboard docs/tests. After any integration, run the data.json golden tests and both
  app target builds from `AGENTS.md`.
