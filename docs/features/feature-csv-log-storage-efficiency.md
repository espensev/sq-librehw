# CSV Log Storage Efficiency

**Status:** source remediation in review; not in a candidate or live runtime
**Updated:** 2026-08-05

## Problem

The CSV logger historically wrote each `float` with the round-trip `R` format.
That preserves every binary-float tail even when the hardware does not resolve
it, making daily CSV archives materially larger. A first compacting change used
three decimals for Voltage/Current and two for every other sensor type. Claude's
unsafe fixed-decimal prototype reduced a measured archive by 39.3%, but it also
made real small values dishonest: `Data` is expressed in GB, and near-zero
`Load` and `TemperatureRate` readings can be meaningful. That result is not the
measurement for the safer implementation specified here.

On a 600-row sample from the 424-column SND-HOST log, the fixed two-decimal
policy would have changed 8,400 of 24,000 non-zero `Data` samples (35%) and 73
of 599 non-zero `TemperatureRate` samples (12.19%) to zero.

## Goals

- Remove unhelpful float tails from normal-magnitude CSV values.
- Preserve at least four significant digits for every finite non-zero value.
- Keep unit-specific minimum decimal resolution.
- Never turn a real non-zero reading into zero merely to save space.
- Keep CSV identifiers, column order, timestamps, separators, non-finite text,
  and invariant parseability unchanged.
- Fail losslessly for a future `SensorType` until its precision is specified.

## Non-goals

- No retention-duration or sampling-interval change.
- No column filtering or sensor visibility policy.
- No archive format, compression level, or log-management task change.
- No `data.json`, Prometheus, native display, graph, or sensor-value change.
- No candidate creation, push, promotion, live-config edit, or manifest refresh.

## Behavior and contract

The logger first chooses a minimum number of decimal places by sensor type:

| Minimum | Sensor types |
|---|---|
| 3 decimals | `Voltage`, `Current`, `Factor`, `Timing` |
| 2 decimals | `Power`, `Clock`, `Temperature`, `Load`, `Frequency`, `Fan`, `Flow`, `Control`, `Level`, `Data`, `SmallData`, `Throughput`, `TimeSpan`, `Energy`, `Noise`, `Conductivity`, `Humidity`, `TemperatureRate` |

For a finite non-zero value, the logger increases that decimal count when
needed to retain at least four significant digits. It rounds half to even and
then uses invariant `R` formatting so normal values stay short and large values
may remain in exponent form. If a value is below `Math.Round`'s 15-decimal
limit, the logger writes the original lossless `R` text instead of zero.

An unknown future `SensorType` also uses the original lossless `R` path. The
contract test enumerates all current types, so adding a type without selecting
its minimum precision fails CI while production logging remains lossless.

Representative results:

| Type | Input | CSV text |
|---|---:|---:|
| `Temperature` | `37.771072` | `37.77` |
| `Fan` | `1250.4471` | `1250.45` |
| `Voltage` | `0.9339999` | `0.934` |
| `Data` | `0.001763016` GB | `0.001763` |
| `Load` | `0.00039373524` % | `0.0003937` |
| `TemperatureRate` | `0.00498691` °C/s | `0.004987` |

## Compatibility and risks

- CSV numeric values remain parseable invariant floats, but some normal values
  are deliberately rounded. This is an external telemetry behavior change and
  needs a fresh candidate plus separate promotion approval before becoming live.
- Four significant digits are a storage/measurement compromise, not a claim
  that every sensor has exactly that physical resolution.
- Downstream analytics that depended on binary-float tails were already
  depending on noise; the completed pre/post archive replay below quantifies
  the actual reduction for review before publication.
- `TemperatureRate` unavailable states remain empty/null through the existing
  sensor contract; compact formatting applies only when a real value exists.

## Acceptance criteria

- [x] Every current `SensorType` has an explicit minimum-precision test.
- [x] A future/unknown `SensorType` is written losslessly.
- [x] Representative small `Data`, `Load`, and `TemperatureRate` values remain
  non-zero and retain at least four significant digits.
- [x] Finite extremes, NaN, and both infinities retain invariant parseability
  and compact exponent behavior.
- [x] Genuine negative zero becomes `0`; a genuine small negative value remains
  negative and non-zero.
- [x] Contracts, aggregate tests, CI gates, and both WinForms Release targets pass.
- [x] A replay of the 2026-08-04 archive records raw and compressed before/after
  sizes for this exact implementation.
- [x] Source verification writes none of the candidate, release-store, live,
  task, config, archive, rollback, history, or manifest domains. Autonomous live
  CSV growth is expected and is not attributed to this source work.

## Verification

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng\ci\Test-LhmCiGates.ps1
git diff --check
```

### 2026-08-05 archive replay

The input was the immutable physical archive
`E:\SQ_HQ\Monitoring\LibreHardwareMonitorStack\data\logs\archive\SND-HOST\2026\08-Aug\LibreHardwareMonitorLog-2026-08-04.zip`
(4,011,990 bytes, SHA-256
`A27ECCFED46399D0EA1949C7AF8B65D1801D161A021942754935BFF0EE12C54E`).
A disposable .NET 10 harness loaded the just-built `Logger.FormatRowValue`
method as a delegate. It derived each column's `SensorType` from row-one sensor
identifiers: 423 of 423 sensor columns mapped, with zero unknowns. It replayed
all 9,173 data rows in 1.784 seconds and observed zero non-zero values formatted
as zero.

| Representation | Before | After | Reduction |
|---|---:|---:|---:|
| Raw CSV bytes | 19,327,926 | 16,023,769 | 17.10% |
| Identically recompressed ZIP bytes | 3,974,710 | 2,732,768 | 31.25% |

Both byte arrays were recompressed by the same .NET 10
`ZipArchive`/`CompressionLevel.Optimal` code, with the original entry name and a
fixed timestamp. The 3,974,710-byte baseline is deliberately a recompression,
not the physical source ZIP size; this isolates formatting from differences in
the original compressor or metadata.

Final source verification passed 99/99 Contracts tests and the deterministic
aggregate with 370 passed plus one documented opt-in skip. Both WinForms x64
Release targets built with zero warnings/errors. The non-deploying gate runner
passed all nine included gate groups (the self-referential CI group is excluded
by design), its direct harness passed 86/86 assertions, and the candidate
fixture passed 135/135 under both Windows PowerShell 5.1 and PowerShell 7.

Retention and sample rate remain explicit later operator decisions. The first
unattended post-reconcile log-management task run at 2026-08-06 03:45
Europe/London must be observed passively before any logging-path cutover.
