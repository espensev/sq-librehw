# CSV log numeric precision

**Status:** implemented locally; not pushed, packaged, or deployed
**Updated:** 2026-08-05

## Scope

CSV sensor values are rounded before invariant round-trip formatting. This
reduces numeric float tails while preserving small non-zero readings.

This change does not alter sensor values in memory, `data.json`, Prometheus,
native display, sampling frequency, retention, archive format, or task state.

## Contract

| Minimum decimals | Sensor types |
|---|---|
| 3 | `Voltage`, `Current`, `Factor`, `Timing` |
| 2 | All other current sensor types |

- Finite non-zero values retain at least four significant digits.
- Values requiring more than 15 decimal places use their original lossless
  `R` representation.
- Unknown future sensor types use the lossless `R` representation.
- Negative zero is written as `0`.
- NaN, infinities, column order, identifiers, timestamps, and invariant
  parseability are unchanged.

The test suite enumerates every current `SensorType`; adding a type without a
precision classification fails the contract test.

## Compatibility

This is an external CSV formatting change. It requires a fresh exact-source
candidate and separate promotion approval before it can affect the live host.
Downstream consumers must accept invariant floating-point text with variable
decimal length and exponent notation.

## Verification

Relevant checks:

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.Contracts\LibreHardwareMonitor.Tests.Contracts.csproj -c Release -p:Platform=x64
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.slnf -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
git diff --check
```

A 2026-08-05 replay of the immutable 2026-08-04 SND-HOST archive processed
9,173 rows and mapped all 423 sensor columns. No non-zero value became zero.
With the same .NET compression settings, CSV bytes changed from 19,327,926 to
16,023,769 and ZIP bytes from 3,974,710 to 2,732,768. These measurements apply
only to that input archive.
