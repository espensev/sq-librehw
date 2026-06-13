# Discovery: RTX 50-series GPU Hot Spot temperature (GH #10)

**Project:** LibreHardwareMonitor Sev IQ local fork
**Status:** Discovery — investigation only, no code change. GitHub issue #10 stays **open**.
**Updated:** 2026-06-13
**Related:** GH #10; [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md) (the other NVIDIA/`NvidiaGpu.cs` change); [`local-ui-customizations.md`](local-ui-customizations.md)

## Why this is a discovery note, not a fix

The correct value for the 50-series hot spot depends on **what NVAPI returns on the actual RTX 5090** —
and the only such card is the maintainer's live machine. No amount of further code reading produces a
*verified* fix, and shipping a wrong value writes bad data to the most safety-relevant GPU temperature
(it drives fan control and feeds ThermalTrace). So this note records what is provable from source and
isolates the single hardware-confirmed data point needed to choose the fix. **`NvidiaGpu.cs` is
unchanged.**

## What is provable from source

### 1. It's an upstream stub, not a fork regression

`LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs` (50-series branch, ~line 588):

```csharp
// RTX 50xx series
if (Name.StartsWith("NVIDIA GeForce RTX 50", ...)) {
    _hotSpotTemperature.Value = 0;                                  // stubbed
    _temperatures[0].Value     = thermalSensors.Temperatures[1] / 256.0f;  // Core
    _memoryJunctionTemperature.Value = thermalSensors.Temperatures[2] / 256.0f;
}
```

Upstream `LibreHardwareMonitor/LibreHardwareMonitor` `master` has the **identical** stub
(`_hotSpotTemperature.Value = 0;`) in its 50-series branch. The 50-series undocumented thermal-array
layout differs from the 40-series (where idx 1 = hot spot); upstream left the 50-series hot-spot index
undetermined and stubbed it. Because the value stays 0, `if (_hotSpotTemperature.Value != 0)
ActivateSensor(...)` is skipped, so the sensor is absent from the UI and the CSV.

### 2. The issue's suggested fix — "port the undoc index N" — is unreliable (controller evidence)

The maintainer's sibling controllers read the hot spot on the **same card** via direct NVAPI
(`D:/Development/Thermals/nvg-gpu/unofficial-nvapi/nvapi-controller/`). Their logic is *more* careful
than a fixed index:

- `nvapi_thermals.cpp::discover_sensors` probes all 32 undoc thermal slots, then **deliberately does
  not publish a fixed undoc slot as the hot spot**: `out.hotspot_index` is left at its default `-1`,
  with the comment *"do not auto-publish index 1 as hotspot: some boards expose a limit-style sensor
  there instead."* It instead records `documented_hotspot_sensor_idx` from the **documented** thermal
  settings, by matching `target == NV_THERMAL_TARGET_HOTSPOT` (enum value **16**,
  `nvapi_undoc_types.h`) with `current_temp > 0`.
- `hotspot_available = (documented_hotspot_sensor_idx >= 0) || (hotspot_index >= 0)`
  (`nvapi_controller.cpp`).

Caveat (don't overstate): the controller's *runtime* read (`gpu_probe.cpp:83`,
`temps[thermal_disc.hotspot_index] / 256.0`) does use the undoc slot — but only when discovery has
*proven* that slot, which the documented-target validation gates. The takeaway is the **negative**: a
hard-coded undoc `Temperatures[N]` for the 50-series (what GH #10 proposes) is exactly what the
controller refuses to trust. Don't port a guessed N.

### 3. What LHM's NvApi binding lacks for the documented path

LHM already reads the documented thermal settings into `_temperatures[]` and labels them by target
(`NvidiaGpu.cs` ~line 87). But:

- **No hot-spot target.** `NvThermalTarget` (`Interop/NvApi.cs`) defines `None,Gpu,Memory,PowerSupply,
  Board,VisualComputing{Board,Inlet,Outlet},All=15` — **no `Hotspot = 16`**. A documented hot-spot
  sensor would hit the `_ => "GPU"` fallback and be mislabeled "GPU".
- **3-sensor cap.** `MAX_THERMAL_SENSORS_PER_GPU = 3`; `NvThermalSettings.Sensor` is
  `SizeConst = 3`; `GetThermalSettings` (`NvidiaGpu.cs:1252`) queries `NvThermalTarget.All` with
  `Count = 3`. If a documented hot-spot sensor sits beyond slot 3 on the 50-series, LHM never sees it.

## The one open question (hardware-gated)

On the RTX 5090, when `NvAPI_GPU_GetThermalSettings(NV_THERMAL_TARGET_ALL)` is called:

> **Does it return a sensor whose `target == HOTSPOT (16)` with a valid `current_temp`, and is that
> sensor within the first 3 entries (`Count <= 3`) — i.e. reachable by LHM's current struct?**

The controller's discovery on this card already computes the answer. The data point needed is the
maintainer's controller output for the 5090: `documented_hotspot_sensor_idx`, `thermalSettings.count`,
and (if used) the proven undoc `hotspot_index`.

## Fix paths, conditioned on that answer

| If, on the 5090… | Fix | Risk |
|---|---|---|
| documented HOTSPOT sensor **is present within the first 3 slots** | **Zero-marshaling:** add `Hotspot = 16` to `NvThermalTarget` + a `"GPU Hot Spot"` case in the `sensor.Target` switch. The existing `_temperatures[]` loop then names/activates/logs it automatically; the 50-series `_hotSpotTemperature` undoc handling can be dropped. | Low. Adds a named constant to an already-marshaled `int`-backed field — **no struct-layout / ABI change** — plus one `switch` case. |
| documented HOTSPOT sensor exists **beyond slot 3** | Enlarge `MAX_THERMAL_SENSORS_PER_GPU` (and re-verify the marshaled `NvThermalSettings` against the driver for **all** NVIDIA GPUs), then as above. | Higher — ABI-sensitive `SizeConst` change; must be hardware-verified across cards. |
| **no** documented HOTSPOT sensor (only the undoc array has it) | Read `thermalSensors.Temperatures[N]` for the 50-series, where **N is the value the controller proves on the card** (not a guess). | Medium — undoc, card-specific; wrong N = wrong safety-critical data. |

## Recommendation

1. Do **not** ship a guessed undoc index (GH #10's literal suggestion); the controller evidence shows it
   is unreliable.
2. Pull the one data point above from the controller's discovery on the 5090.
3. If the documented HOTSPOT sensor is reachable within LHM's 3-slot window, take the **zero-marshaling
   enum + label** fix (preferred — no ABI risk, and it generalizes to any card that reports a documented
   hot spot). Otherwise fall back to the undoc index using the controller-proven N, or the struct-size
   change, accepting the higher risk and requiring hardware verification.
4. Whatever path: it's a shared-lib (`NvidiaGpu.cs`/`NvApi.cs`) **and** data-contract change (new
   `GPU Hot Spot` at `/gpu-nvidia/<n>/temperature/2` in CSV + `data.json`) — needs a feature spec and
   runtime verification on the 5090 before merge, mirroring [`feature-unique-gpu-sensor-ids.md`](feature-unique-gpu-sensor-ids.md).

## Consumer note (ThermalTrace)

When fixed, the hot spot lands at `/gpu-nvidia/<n>/temperature/2` (`index = thermalSettings.Count + 1`;
Memory Junction `+2` → `/temperature/3`, matching today's logs). ThermalTrace's cross-source fold map
should align controller hot spot to `/temperature/2`.
