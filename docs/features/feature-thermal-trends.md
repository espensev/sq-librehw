# Feature Spec: Thermal Trends

**Status:** deployed and live NVIDIA activation verified on SND-HOST
**Updated:** 2026-07-18

## Problem

A temperature alone does not show how quickly a component is heating or
cooling. The retired host workspace exposed this idea, but its implementation
depended on sparse persisted history, fabricated zero during warm-up, and had
no current contract or tests.

## Goal

Publish one reliable NVIDIA GPU hotspot temperature-rate sensor and carry it
through native UI, history plots, `data.json`, CSV, Workspace, and Prometheus.
The implementation must be bounded, host-neutral, and honest while telemetry is
warming up or unavailable.

## Behavior and contracts

- The initial source is the live NVIDIA `GPU Hot Spot` update value.
- The derived sensor is `GPU Hot Spot Rate`, type `TemperatureRate`, index `0`,
  with identifier `/gpu-nvidia/<adapter>/temperaturerate/0`.
- A five-second rolling linear regression uses direct update samples, not the
  persisted/downsampled `Sensor.Values` collection.
- At least three samples spanning two seconds are required. Until then, and on
  missing, non-finite, unsupported, or clock-regressed input, the value is
  unavailable (`null`), never a fabricated zero.
- The direct-sample queue is capped at 64 entries. Normal sensor history keeps
  the existing global retention and persistence bounds.
- Celsius displays use `°C/s`. Fahrenheit displays multiply by `1.8` and use
  `°F/s`; they never apply the absolute-temperature `+32` offset.
- `data.json` exposes the additive type and raw numeric value through the
  existing sensor contract. Prometheus uses the suffix
  `temperaturerate_celsius_per_second` with factor `1`.
- The derived sensor activates only after a valid rate exists. Once activated,
  a telemetry dropout remains visible as unavailable through existing behavior.

## Non-goals

- No CPU, AMD GPU, Intel GPU, coolant, or arbitrary user-selected rate sources
  in this slice.
- No alarm threshold or automatic fan/control action.
- No attempt to synthesize RTX 50-series hotspot telemetry while the source is
  unavailable.
- No change to `AssemblyVersion`, routes, existing sensor identifiers, or the
  `data.json` serialization shape.

## Compatibility and risks

`TemperatureRate` is appended to `SensorType` so existing numeric enum values
remain stable. The implementation must compile for both `net472` and
`net10.0-windows`. Its queue and regression are constant-bounded, and no
additional hardware call is made. Consumers that ignore unknown additive
sensor types continue to work; current native and web surfaces explicitly
format and order the new type.

## Acceptance

- [x] Warm-up, dropout, non-finite input, and backward-clock handling publish no
  false zero or stale rate.
- [x] Linear ramps produce the expected smoothed slope across window expiry.
- [x] Sample storage stays bounded at 64 entries.
- [x] Native values and plots convert rates correctly between Celsius and
  Fahrenheit.
- [x] Workspace semantic presets can select the sensor without host IDs/labels.
- [x] Prometheus declares an explicit Celsius-per-second unit.
- [x] Full tests and both Release target builds pass.
- [x] Live NVIDIA runtime smoke confirms activation and plausible values.

## Verification

```powershell
dotnet test LibreHardwareMonitor.Tests\LibreHardwareMonitor.Tests.csproj -p:Platform=x64
node --test webtests\workspace.tests.js webtests\console.tests.js
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net10.0-windows -p:Platform=x64
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -f net472 -p:Platform=x64
```

The live smoke is a separate, explicitly approved deployment step. Confirm the
verified target, launch through its owned task/runtime, observe the hotspot and
rate through native UI plus `data.json`, then exercise an unavailable/recovery
transition without changing hardware controls.

## Verification log

- 2026-07-18 SND-DESK -> SND-HOST live smoke: after verified deployment, the
  RTX 3080 exposed one `GPU Hot Spot Rate` sensor at
  `/gpu-nvidia/0/temperaturerate/0`. `data.json` reported a finite live value,
  and Prometheus exposed the Celsius-per-second metric. This closed activation
  and value plausibility; a forced hardware dropout/recovery transition was not
  induced on the live host.
- 2026-07-18 focused implementation check: 25 temperature-rate, plot-history,
  and Prometheus tests passed. No runtime or scheduled task was changed.
- 2026-07-18 repository gate: both Release targets built in isolated outputs
  with zero warnings/errors; the full .NET suite passed 150 tests with the one
  existing opt-in skip; Node checks, 15 model/polling tests, and the 285/285
  dashboard self-test passed.
