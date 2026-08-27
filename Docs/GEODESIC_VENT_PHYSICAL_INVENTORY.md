# Geodesic vent physical-inventory units

## Scope and conversion boundary

This migration preserves historical vent authoring semantics; it is not a geological or ecological retune. `SimulationStartupConfig.vent*PerTick` retains its serialized field names and UI values. Despite the legacy name, each value is a global rate per simulated second in the old `concentration * Unity^3 / s` inventory convention. `SimulationStartupController` clamps, copies, saves, and passes those values unchanged to `GeodesicOceanResourceField.SetStartupVentRates`. Scene serialization and the legacy `PlanetResourceMap` therefore remain compatible and unchanged.

`GeodesicOceanResourceField.BuildTransportCaches` is the sole environmental runtime conversion boundary. It caches each rate as a `double` physical rate using `GeodesicPhysicalScale.PhysicalInventoryRate`, whose authority is `PhysicalCubicKilometresPerUnityUnitCubed`. At the current scale this maps H2/H2S/CO2/Fe2 authoring examples `0.12/0.004/0.05/0.002` to `1.2e8/4e6/5e7/2e6 concentration*km^3/s`. Cached runtime rates, telemetry, and source diagnostics are physical; the serialized floats remain authoring values.

## Distribution and application

Candidate generation and strongest-seed clustering store only location, habitat, and dimensionless raw strength. Systems normalize production weights independently within each habitat. Compact outlets divide a system budget with dimensionless within-system weights. None of these records cache a material rate or perform a unit conversion.

The raw-strength submarine/terrestrial fractions are computed before injection and sum to one. Gases (CO2, H2, H2S) use the same converted global physical rate on both sides of that split. Each submarine outlet receives `physicalGlobalRate * submarineFraction * systemWeight * outletWeight`; dividing its `rate * dt` by physical node volume gives concentration delta. Each terrestrial outlet contributes the corresponding weighted physical inventory directly to `GeodesicAtmosphereField.AddGeologicalSource`; that API does no scale conversion, and pressure remains inventory divided by physical atmosphere inventory per bar. Fe2 remains submarine-only and its independently normalized submarine outlet weights preserve its global budget.

Chemistry telemetry now reports cached physical source rates. Cell picking and biology diagnostics read concentrations after injection and require no rate conversion. Vent-system and outlet records contain only dimensionless weights, so there is no stale authoring-rate cache and no double scaling.

## Thermal audit

Vent thermal behavior is deliberately not converted. `BuildVentThermalInfluence` derives a dimensionless local influence from relative cluster and outlet strengths (square-root normalization plus a 0.3 neighbor falloff). `GeodesicExperiencedTemperatureField` uses vent target temperatures, distance falloff, and these normalized influences. These are temperature targets and dimensionless spatial/relaxation controls, not material inventory fluxes; multiplying them by the volume scale would be a unit error.

## Geological calibration is separate

This conversion preserves the historical SimulaVit authoring meaning under physical ocean and atmosphere inventory units. It does **not** assert realistic Earth hydrothermal fluxes. A later model may calibrate production against seafloor area, vent-system density, planetary heat flow, mantle activity, radius, or an ecological gameplay timescale. That modeling decision is separate from unit consistency and is intentionally not made here.
