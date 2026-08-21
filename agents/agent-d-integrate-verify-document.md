# Agent Task — Integrate, verify, and document

**Scope:** Integrate the parser and shell, automate the complete
isolation/regression gate, and record reviewed evidence and follow-on decisions.

**Depends on:** agent B (`implement-bounded-parser`) and agent C
(`build-fixture-explorer-shell`)

**Output files:** `scripts/Test-AvaloniaSpike.ps1`,
`LibreHardwareMonitor.Avalonia.Spike.Tests/Integration/FixtureExplorerIntegrationTests.cs`,
`docs/feature-avalonia-fixture-sensor-explorer.md`, `docs/README.md`,
`live-tracker.md`

## Exit Criteria

- Real parser-to-view-model integration covers valid, hot-plug replacement,
  malformed, and excessive fixtures.
- One fail-fast PowerShell gate verifies spike restore/build/tests, forbidden
  references, existing tests/builds, release-system behavior, and shipping
  solution isolation.
- Automated results are recorded without claiming an attended check that did
  not happen.
- Tracker rows from agents A-C are consolidated by the only tracker owner.
- Docs distinguish source feasibility, exact-source candidate proof, attended
  smoke, live state, and any separately gated polling recommendation.
- No candidate is promoted and no live runtime/task/settings/log state changes.

---

## Context — read before doing anything

1. `AGENTS.md` — repository handoff rules and baseline commands.
2. `docs/feature-avalonia-fixture-sensor-explorer.md` — complete accepted
   contract and evidence slots.
3. `docs/campaign-plan-001-fixture-only-avalonia-sensor.md` — authoritative
   exit criteria, file ownership, integration points, risks, and sequencing.
4. `docs/discovery-pre-avalonia-readiness.md` — source/live/candidate and
   ownership boundaries.
5. All files produced by agents A-C — review interfaces and behavior before
   integrating; do not casually revise files they own.
6. `.codex/skills/project.toml` — configured release build and test commands.
7. `ops/release/New-LhmRelease.ps1`,
   `ops/release/Test-LhmReleaseCandidate.ps1`, and
   `ops/release/Test-LhmReleaseSystem.ps1` — candidate creation is separate
   from promotion.
8. `docs/README.md` — preserve current live SND-HOST claims.

---

## Task

### Part 1 — Add concrete integration tests

Test the real `BoundedDataJsonFixtureLoader` with the real
`MainWindowViewModel`:

- normal fixture publishes exact roots, counts, source/version, values, and
  stable IDs;
- unavailable fixture stays honest;
- hotplug-before followed by hotplug-after atomically removes the absent
  sensor;
- malformed and oversized-string loads retain the prior valid snapshot and
  expose typed rejection;
- a superseded request cannot publish late;
- no numeric `id` is used as UI identity.

Do not duplicate the parser's exhaustive bound tests or the shell's fake-loader
tests.

### Part 2 — Add the complete fail-fast gate

Create `scripts/Test-AvaloniaSpike.ps1` with strict mode, stop-on-error, and
repo-root resolution relative to the script. It must:

1. confirm the pinned SDK resolves;
2. restore and build the separate spike solution in Release;
3. run the isolated xUnit v3 executable;
4. scan all spike project/source files and fail on references to
   `LibreHardwareMonitor.Windows.Forms`, `LibreHardwareMonitorLib`,
   `Computer.Open`, `PersistentSettings`, `HttpClient`, POST/control routes,
   admin manifests, scheduled tasks, or live/release roots;
5. prove `LibreHardwareMonitor.sln` does not list any spike project;
6. run the existing .NET tests;
7. build WinForms x64 Release for `net10.0-windows` and `net472`;
8. run `ops/release/Test-LhmReleaseSystem.ps1`;
9. emit a concise pass summary with individual gate counts.

Do not have this script create or promote a release candidate. The manager runs
the configured clean-source candidate commands after the accepted merged commit
exists.

Avoid deleting output or changing live state. Normal `bin`/`obj` output from
build/test commands is expected and remains ignored.

### Part 3 — Reconcile and record evidence

Review all agent results. If contracts drifted, stop and report the exact
ownership conflict rather than editing another agent's files silently.

Create `live-tracker.md` with a compact table and consolidate:

- AVSPIKE-001 from agent A;
- AVSPIKE-002 from agent B;
- AVSPIKE-003 from agent C;
- AVSPIKE-004 for integration, verification, and docs.

Update the feature spec:

- mark only objectively satisfied criteria;
- record package/SDK versions and automated test/build totals;
- record the exact candidate ID/hash only after a clean committed checkpoint
  passes both current-source/promotable verifiers;
- leave attended smoke unchecked until a normal-user session actually runs it;
- state whether evidence justifies drafting a separate read-only polling spec;
- do not broaden the current spike.

Update `docs/README.md` through its existing source/live distinction. A source
spike is not deployed merely because it builds.

### Part 4 — Prepare the post-merge candidate gate

After all changes are integrated, reviewed, committed, and the worktree is
clean, the manager—not this parallel agent—must run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\New-LhmRelease.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource
pwsh -NoProfile -File ops\release\Test-LhmReleaseCandidate.ps1 -Latest -RequirePromotable -RequireCurrentSource
```

Candidate creation remains non-deploying. Promotion requires a later explicit
instruction and is outside this campaign.

---

## Constraints

- Own only the listed integration, gate, docs, and tracker files.
- Treat agent A-C outputs as reviewed inputs; return ownership defects instead
  of absorbing them into unrelated files.
- Never edit current settings, tasks, live runtime, release promotion state, or
  rollback packets.
- Automated source verification and attended UI evidence are separate gates.
- A polling recommendation may say `worth specifying`; it cannot add polling
  code or authorize it.

---

## Verification

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Test-AvaloniaSpike.ps1
dotnet restore LibreHardwareMonitor.Avalonia.Spike.slnx
dotnet build LibreHardwareMonitor.Avalonia.Spike.slnx -c Release --no-restore
dotnet run --project LibreHardwareMonitor.Avalonia.Spike.Tests\LibreHardwareMonitor.Avalonia.Spike.Tests.csproj -c Release
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\release\Test-LhmReleaseSystem.ps1
```

After the manager commits the accepted integration, it runs the exact-source
candidate commands in Part 4 plus the attended smoke from the feature spec.

---

## Do NOT

- Do not modify agent A-C files merely to make the gate pass; report ownership
  defects for the owning agent.
- Do not add live polling, HTTP clients, settings, hardware, packaging, task,
  launcher, promotion, or cutover behavior.
- Do not mark attended or candidate evidence complete without running it.
- Do not approve, execute, promote, deploy, or launch the campaign.

## Post-completion

Create/update `live-tracker.md` and add:

| ID | Status | Owner | Scope | Issue | Update |
|---|---|---|---|---|---|
| AVSPIKE-004 | Done | agent-d | integration, verification, docs | Fixture-only Avalonia explorer | Record gate totals, candidate boundary, attended status, and polling recommendation. |
