# External Release Candidate Packaging

**Status:** implemented; clean promotable candidate and separate SND-HOST promotion verified; post-upstream-sync source gates verified; any new candidate or promotion remains a separate non-deploying or identity-verified gate
**Updated:** 2026-07-30

## Problem

The application and referenced projects normally write incremental build and
intermediate output under repository/project `bin` and `obj` directories. Those
directories are ignored by Git and may contain Debug, Release, application, and
library generations from different commands. Copying from them into a release
can therefore retain a file that the current build no longer produces.

Release candidates need a clean, inspectable boundary outside the source
checkout. Creating a candidate must not imply that it is accepted, deployed, or
safe to substitute for the running LibreHardwareMonitor instance.

## Goals

- Build one framework-dependent `win-x64` Release payload for each supported
  Windows application target: `net472` and `net10.0-windows`.
- Stage, verify, and publish candidates under an external release root that is
  disjoint from the repository.
- Bind every payload file to its candidate with relative paths, lengths, and
  SHA-256 hashes in a versioned manifest.
- Refuse partial, ambiguous, stale, or colliding output.
- Leave every discovered repository/project `bin` and `obj` output root absent
  after a successful or failed release attempt.

## Non-goals

- No runtime promotion, process restart, scheduled-task change, deployment,
  firewall change, or live hardware smoke.
- No GitHub Release, branch/tag creation, commit, push, NuGet publication,
  signing, or update-channel behavior.
- No `AssemblyVersion` change. It remains `0.9.6` so the `data.json` contract
  stays stable.
- No x86 or ARM64 application package in this first local release system. Their
  existing project support is not removed.
- No broad cleanup of ignored files. Cleanup is restricted to exact discovered,
  guarded repository/project `bin` and `obj` output roots.

## Entry points

- `ops/candidate/New-LhmRelease.ps1` creates and verifies a new candidate.
- `ops/candidate/Test-LhmReleaseCandidate.ps1` independently validates an existing
  candidate without changing it.
- `ops/candidate/Test-LhmReleaseSystem.ps1` exercises path, cleanup, build,
  collision, failure, manifest, and tamper behavior with isolated fixtures.

These scripts package source output only. A separate, explicitly approved
identity-verified workflow must own any later runtime promotion.

## Runtime-data compatibility gate

Source imported from fetch-only `upstream/master` now supports
`librehw.runtime.json` and resolves mutable data in this order: runtime
configuration, `LIBREHARDWAREMONITOR_DATA_ROOT`, then the executable directory.
The integration deliberately ignores ambient `sqdata`: on this host it can
point at unrelated machine or shell state. The SND-DESK local-release system
always materializes its own absolute runtime configuration and is separately
fail-closed to `snd-desk`.

The current SND-HOST runtime still uses the executable-directory fallback: its
configuration, backup, and active CSV are beside
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current\LibreHardwareMonitor.Windows.Forms.exe`.
The 2026-07-31 host-layout migration moved that directory byte-identically and
proved every pre-cutover setting plus fresh CSV growth; it did not invent a
`librehw.runtime.json` or separate data root. Any later data-root extraction
still requires an explicit authority, rollback, and config/log continuity
proof. Ambient `sqdata` is never deployment authority.

Candidate creation remains machine-neutral and must not embed this host-specific
file. Candidate integrity and promotability do not prove that a deployment's
runtime-data configuration is safe.

## Release-root contract

`New-LhmRelease.ps1` resolves the release root in this order:

1. explicit `-ReleaseRoot`;
2. `LHM_RELEASE_ROOT`;
3. the `<repository-directory-name>-releases` sibling of the repository
   (`librehw-host-releases` for this checkout).

All paths are resolved before mutation. The release root is rejected when it is
the repository, is inside the repository, contains the repository, or resolves
through a reparse-point alias to any of those relationships. The script also
rejects a candidate or staging path that escapes the resolved release root.

The generic sibling fallback is portable, but it is not the SND-HOST promotion
location. On SND-HOST,
release and verification commands use an explicit root, or a machine-local
`LHM_RELEASE_ROOT`, of
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\releases`. The live runtime
remains separate at
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\deployments\current`; neither
path is inside `D:\DevHome\workspaces\librehw-host`.

The external layout is:

```text
<ReleaseRoot>\
  .staging\
    <unique-run-id>\
    <release-id>\  # transient fully validated ready directory
  candidates\
    <release-id>\
      packages\
        LibreHardwareMonitor-<release-id>-win-x64-net472.zip
        LibreHardwareMonitor-<release-id>-win-x64-net10.0-windows.zip
      release-manifest.json
```

Each invocation owns only its unique staging directory. On failure it removes
that directory and publishes no candidate. It must not delete unrelated
staging runs, candidates, or sibling files. After complete validation, external
artifact cleanup and the final guarded source-output cleanup finish before the
ready directory is renamed within the same release root into
`candidates\<release-id>`. An existing release ID is immutable and causes a
collision failure rather than an overwrite. If a final handoff check fails
after publication, the command rolls back only the candidate it owns and
returns no `VERIFIED` result.

## Source-state policy

Candidate creation checks tracked and untracked Git state independently of the
application's informational-version stamp.

- Clean source is the default requirement.
- Dirty source is rejected unless the operator explicitly passes `-AllowDirty`
  for a development candidate.
- A dirty candidate is labelled as dirty in both its release ID and manifest.
  It is evidence for local testing only and is never an accepted or promotable
  release.
- `-SkipVerification` also forces a non-promotable candidate even when its
  source is clean.
- The manifest records the full commit SHA, branch, clean/dirty state, complete
  source fingerprint/file count, base version, file version, and product
  version. It records a credential-free repository identifier rather than the
  raw remote URL. The two framework entry points must identify the same source
  generation.

The release tools do not commit, stash, reset, or otherwise alter source
changes.

## Clean build and repository cleanup boundary

Release verification and both framework builds use fresh output and
intermediate locations owned by the external staging run. `global.json` pins
SDK `10.0.302`, and the project `UseArtifactsOutput` conditions let SDK artifact
routing supersede their normal source-tree `OutputPath` values. Packaging reads
only the external artifact tree; it never copies from repository `bin` or `obj`.

Before verification/build and again during final cleanup, the script discovers
the repository-root `bin` plus direct `bin` and `obj` children of every project
root. It removes an existing output root only after proving that:

- the repository root is the current Git top level and the candidate is inside
  it;
- the candidate's leaf is exactly `bin` or `obj` at a discovered location;
- Git reports the candidate as ignored;
- a case-insensitive comparison against Git's complete tracked-file inventory
  finds no tracked child, including case variants such as `Bin`;
- neither the candidate, its traversed path, nor any descendant is
  reparse-backed; and
- no discoverable running process executable is loaded from that output root.

Any failed guard stops the release instead of risking unrelated state. The
release also fails closed and publishes nothing if a build recreates a source
output root; final cleanup restores the required absent state. It never runs
broad commands such as `git clean -fdX`.

This cleanup deliberately includes project `obj` caches so a release cannot
reuse stale intermediates. It does not touch any other ignored file, an
unrelated staging run, an external deployed runtime, or a developer path outside
the discovered output-root set.

## Build and candidate contract

The creator runs the repository verification gates, then invokes the Windows
Forms project exactly once for each target with:

- configuration `Release`;
- platform `x64` and runtime identifier `win-x64`;
- `--self-contained false`, so the packages remain framework-dependent;
- target framework `net472` or `net10.0-windows`;
- all artifacts directed into that run's external staging tree; and
- package-on-build disabled for this application candidate.

Framework payloads never share an output directory. Each complete clean payload
is compressed into its own ZIP under the candidate `packages` directory; raw
staging payload directories are not published. A non-zero verification or build
exit code, missing application entry point, source-tree output, or
framework/version mismatch aborts the complete candidate.

The complete RID-specific framework output is preserved. The validator rejects
any residual `runtimes/` subtree: a `win-x64` build must let the SDK select and
flatten the applicable native assets instead of carrying Android, Linux, macOS,
or x86 payloads. This is an SDK build property, not a hand-maintained dependency
allowlist. The modern package requires the .NET 10 Desktop Runtime; the legacy
package requires .NET Framework 4.7.2.

## Manifest and validation

`release-manifest.json` uses schema `sq.lhm-release`, version `1`. It records:

- release ID and UTC creation time;
- base version and the pinned/observed SDK version;
- repository branch, full commit SHA, clean state, and recorded changes;
- a lowercase SHA-256 fingerprint and file count for tracked plus non-ignored
  untracked source, and a credential-safe repository identifier;
- configuration, platform, verification completion/commands, and whether the
  candidate is promotable;
- the two target frameworks, application entry points, file/product versions,
  `runtimeIdentifier: win-x64`, `selfContained: false`, and
  `packages/...zip` archive paths; and
- each archive's byte length and lowercase SHA-256 digest, plus the relative
  path, length, and digest of every file inside that ZIP.

Manifest paths use `/`, contain no drive or root prefix, and cannot contain an
empty, current-directory, or parent-directory segment. Duplicate archive paths,
duplicate/case-colliding ZIP entries, entry-count mismatches, missing files, or
anything outside the candidate are invalid.

`Test-LhmReleaseCandidate.ps1` is candidate-read-only. It validates the schema
and exact framework set, recomputes each archive length/hash, opens both ZIPs,
recomputes every entry length/hash, rejects directory or case-colliding entries,
extracts each entry point to one guarded short-lived temp file, and compares its
actual file/product versions with the manifest. Boolean, array, integer,
source-state, verification-command, dirty-label, and version fields are checked
for type and coherence rather than coerced. A valid hash manifest proves
candidate integrity; it does not provide publisher authentication or code
signing.

`-RequirePromotable` rejects dirty or verification-skipped candidates.
`-RequireCurrentSource` also compares commit, branch, status, fingerprint, and
file count with the current checkout. The configured release build gate uses
both switches with `-Latest`; latest selection is based on manifest UTC creation
time rather than directory write time.

## Failure and promotion boundary

- Nothing appears under `candidates` until every build and validation succeeds.
- A failed second framework build cannot leave a one-framework candidate.
- A manifest is never published before its payload is complete.
- Existing candidates are never mutated or replaced.
- All discovered source `bin`/`obj` output roots are absent at handoff.
- Candidate creation and validation do not update a `latest` pointer, mark a
  candidate accepted, or copy anything into a runtime directory.

Manual or automated promotion is separate work requiring explicit approval,
verified machine identity, runtime-owner coordination, a live smoke, and a
rollback reference to a previously accepted package.

## Acceptance criteria

- [x] Default output resolves to the external repository sibling, while
  `-ReleaseRoot` and `LHM_RELEASE_ROOT` select an explicit external root.
- [x] Equal, nested, containing, and escaping release roots fail before build
  or cleanup mutation.
- [x] Equal/nested roots, live release/candidate-root junctions, traversed
  project junctions, and a descendant junction inside `bin` all fail closed;
  junction targets and unrelated files remain unchanged.
- [x] A full development candidate contains exactly one x64 ZIP for `net472`
  and one for `net10.0-windows`; both are framework-dependent `win-x64`
  payloads, contain no `runtimes/` subtree, and never package source `bin` or
  `obj`.
- [x] Repeat the full candidate run from clean source and verify
  `promotable: true`, no `-dirty` suffix, and no recorded source changes. The
  first full proof deliberately used the then-dirty implementation tree; the
  clean proof is recorded below.
- [x] Dirty source fails by default, and an explicitly allowed dirty candidate
  is unambiguously labelled in its ID and manifest.
- [x] Every declared relative path, byte length, and SHA-256 digest validates;
  a one-byte payload change is detected.
- [x] A failed verification/build, missing entry point, source-output leak, or
  destination collision publishes no new or partial candidate.
- [x] A release run cleans only its own external staging directory and guarded,
  discovered repository/project `bin` and `obj` roots; unrelated ignored,
  candidate, and external files remain.
- [x] All discovered repository/project output roots are absent after success
  and after handled failure.
- [x] Candidate validation performs no runtime, task, repository, or candidate
  mutation.
- [x] Candidate creation and validation never describe a candidate as accepted,
  deployed, signed, or live-verified; separate promotion evidence is recorded
  explicitly as a different workflow.
- [x] Verify the integrated runtime-path regression: absent explicit runtime
  configuration or `LIBREHARDWAREMONITOR_DATA_ROOT`, ambient `sqdata` is ignored
  and mutable state remains executable-adjacent.

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseSystem.ps1
pwsh -NoProfile -File ops\candidate\Test-LhmReleaseSystem.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\New-LhmRelease.ps1 -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseCandidate.ps1 -Latest -ReleaseRoot <external-release-root>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\candidate\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource -ReleaseRoot <external-release-root>

if (Get-ChildItem -Recurse -Directory -Force | Where-Object Name -in @('bin', 'obj')) { throw 'Repository output was repopulated.' }
git diff --check
```

The real candidate command is a packaging gate, not deployment proof. Do not
run a normal repository-output build after its final output assertion when
verifying the cleanup contract.

## Verification log

- 2026-07-30 SND-HOST source integration: runtime-path tests prove that runtime
  configuration and `LIBREHARDWAREMONITOR_DATA_ROOT` remain explicit authorities
  while ambient `sqdata` is ignored and the portable executable-directory
  fallback is preserved. A present directory, reparse point, unreadable, locked,
  or invalid runtime descriptor fails closed rather than falling through to
  another root. The dashboard self-test passed 315/315, focused Node tests
  18/18, the .NET suite 258 passed with one intentional skip, both x64
  Release targets built with zero warnings/errors, log-management checks passed
  26/26, release-system checks passed 114/114 under Windows PowerShell 5.1 and
  PowerShell 7, and the peer-scoped local-release fixture passed all failure,
  recovery-manifest, hostile-reparse, launcher-compatibility, and cleanup
  groups. This was source verification only: temporary fixture candidates and
  payloads were removed, no external/promotable candidate was published, and no
  live SND-HOST runtime, task, configuration, or logs were changed. Guarded
  post-build cleanup removed 556 generated files (111,664,958 bytes), and all
  declared repository `bin`/`obj` roots were absent at handoff.
- 2026-07-25 SND-HOST clean candidate and separate promotion: candidate
  `0.9.6-20260725-165558646-d693da7` came from clean `main` commit
  `d693da7b1cd23159732123ba1a672ed8d9cf244b`, recorded no source changes, and
  was independently accepted with both `-RequirePromotable` and
  `-RequireCurrentSource` under PowerShell 7 and Windows PowerShell 5.1. The
  gate passed 306/306 dashboard checks, 18/18 focused Node tests, 182 .NET tests
  with one documented opt-in skip, and both Release builds with zero warnings
  or errors. Net10 contains 35 files and net472 45; both are
  framework-dependent `win-x64` packages with no `runtimes/` subtree.
  Repository `bin`/`obj` and external staging were empty afterward. Through a
  separate maintainer-approved, identity-verified workflow, the net10 package
  was clean-materialized at `E:\SQ_HQ\Monitoring\LibreHardwareMonitor` with a
  complete rollback and live HTTP/sensor/log-growth proof. The release scripts
  did not perform that promotion.
- 2026-07-25 SND-HOST: the hardened release-system fixture suite passed all 114
  assertions under PowerShell 7 and Windows PowerShell 5.1. It covers disjoint
  and live-junction roots, descendant reparse rejection, case-insensitive
  tracked-child protection, exact cleanup, immutable dual ZIPs, failure and
  source-drift rollback, strict manifest types/coherence, recalculated archive
  and entry hashes, explicit ZIP-directory rejection, actual entry-point
  version proof, safe origin normalization, dirty-ID enforcement, pinned-SDK
  working-directory proof, exact framework-dependent `win-x64` build routing,
  residual-runtime-subtree rejection, and the `-Latest`
  promotable/current-source build-gate path.
- The historical first full current-tree proof created dirty, non-promotable
  candidate `0.9.6-20260725-151355368-52f9c03-dirty` under an earlier manifest
  validator. It removed 645 generated files totalling 136,403,711 bytes from
  guarded repository/project output roots; the final hardened candidate below
  supersedes it as handoff evidence.
- The earlier final handoff candidate
  `0.9.6-20260725-final-hardening-52f9c03-dirty` passed the 306/306 dashboard
  self-test, all 18 focused Node tests, the .NET suite with 182 passed and one
  opt-in skip, and both x64 Release builds with zero build warnings/errors.
  Both external ZIP packages, every declared entry, and the versions read from
  both archived entry points passed the then-current independent validation.
  `-Latest
  -RequireCurrentSource` passed, while `-RequirePromotable` rejected it as
  intended because the reviewed source was not committed. It is now superseded:
  its net10 archive had 69 files, including 37 cross-platform/runtime-variant
  entries that a Windows x64 release does not need.
- RID-specific external probes then built both targets with zero warnings or
  errors. Net10 reduced to 35 files and net472 contained 45; neither had a
  `runtimes/` subtree. Both remained framework-dependent. The manifest contract,
  creator, and validator now lock that result to `win-x64`.
- The probes and fixture runs used SDK `10.0.302`; repository `bin`/`obj`
  output roots were absent afterward and all guarded temporary roots were
  removed. No runtime, process, task, configuration, or deployment target was
  promoted or changed by these source checks.
