# Geodesic vent physical-inventory units

## Current startup semantics

Legacy Cube Sphere and Geodesic vent configuration use distinct fields. The existing `vent*PerTick` fields retain their serialized names, defaults, UI path, and legacy behavior. Geodesic startup instead uses `geodesicVent*PhysicalPerSecond`, whose values are already authoritative `concentration * km3 / simulated second` inventory fluxes.

The provisional Geodesic defaults are H2/H2S/CO2/Fe2 = `10/0/0/0`. H2 is intentionally set to 10 for the immediate diagnostic. The other resources are disabled because no deliberate physical calibration has yet been selected; old legacy ratios are not silently carried forward.

`SimulationStartupController` passes the four Geodesic fields directly to `GeodesicOceanResourceField.SetStartupPhysicalVentRates`. That setter validates finite, nonnegative values and stores them as runtime doubles. `BuildTransportCaches` performs no unit conversion. In particular, neither the startup path nor injection multiplies these values by `GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed`.

## Saved-config migration

Startup-config schema v8 saves both the unchanged legacy fields and the distinct Geodesic physical fields. Schema v7 and older files lack the new fields. Loading them preserves their legacy vent values but initializes Geodesic rates from the current defaults (`10/0/0/0`); an old H2 value such as `0.12` is never reinterpreted as a calibrated physical Geodesic rate. Explicit zero values in v8 remain zero.

## Distribution and application

Candidate generation and clustering store only location, habitat, and dimensionless strength. Compact outlets retain normalized system and within-system weights. Gases use the configured physical global rate on both sides of the unchanged submarine/terrestrial split. Submarine injection computes `rate * dt / physicalNodeVolumeKm3`; terrestrial injection adds the corresponding physical inventory directly to the physical atmosphere. Fe2 remains submarine-only. No outlet or atmosphere path converts the source rate again.

## Deliberately unchanged paths

Atmosphere inventory capacity and physical ocean volume remain unchanged. Legacy Cube Sphere vent source semantics remain unchanged. Vent thermal influence remains a dimensionless function of relative cluster/outlet strength, neighbor falloff, temperature targets, and local relaxation; it is not a material inventory flux and receives no scaling.

Geological calibration remains a separate modeling question. The zero H2S/CO2/Fe2 defaults explicitly expose that those physical source rates still require a deliberate calibration decision.
