# Geodesic hydrogenotroph temperature constraints

Source review and focused changes, 2026-11-13. Unity, builds, automated tests, simulations and benchmarks were not run. Exact live temperature distributions and the resulting population balance remain for manual measurement. All changes are uncommitted.

## Settings actually present

`Assets/PlanetScene.unity` serializes hydrogenotroph optimal temperatures as **288.15–353.15 K (15–80 °C)**. The manager's C# default is 293.15–343.15 K (20–70 °C); that default does not describe this scene's founders. The scene also contains:

| Control | Serialized value |
| --- | --- |
| Individual founder tolerance margin | 20 K |
| `temperatureReplicationMinEfficiency` | 0.15 |
| `temperatureDormancyFloor` | 0.02 |
| `temperatureDirectDamageEnabled` | true |
| Broad extreme cold / heat settings | 233.15 / 423.15 K (-40 / 150 °C) |
| Legacy cold / heat diagnostic markers | 253.15 / 373.15 K (-20 / 100 °C) |
| Temperature mutation chance / scale | 0.02 per trait / ±2 K |
| Basal maintenance | 0.01 energy/second |
| Reproduction rate | 0.5/second |
| Carbon division cost / threshold | 0.2 energy / twice biomass target 0.15 |
| Existing population cap | 30,000 |
| Ocean thermal model | `ApproximateEcologicalProfiles` (`thermalModel: 0`) |
| Approximate thermal interval | 2 simulation seconds |

Startup controls can override climate base temperature and vent production. The latest supplied run used production multiplier 10 and effective H2/CO2 fluxes of 10 inventory units/second each; these values were not changed. The scene's resource component has a 268.15 K base, while serialized startup configurations contain 273.15 K. The runtime startup choice and atmosphere determine the active climate base, so neither serialized number alone establishes the live profile.

## Temperature field around vents

Biology samples `GeodesicOceanTemperatureField.GetLayerTemperatureKelvin` using the **packed authoritative cell and current ocean layer**. It does not use a cell inferred from a rendered marker, a companion object's possibly stale position, or a distance from a vent.

The approximate ocean model defines the abyssal target:

```
D = 274.65 K + 0.25 * (activeClimateBaseK + nonnegativeGreenhouseDeltaK - 273.15 K)
```

At the reference climate base with no greenhouse anomaly, `D = 274.65 K` (1.5 °C). With a 268.15 K base and no greenhouse anomaly, it is 273.4 K (0.25 °C). These are abyssal target examples, not measured bottom-water temperatures.

For each subsurface node:

```
p = (nodeCentreDepth / maximumOceanDepth)^1.4
background = lerp(localSurfaceTemperatureK, D, p)
target = background + max(0, 623.15 K - D) * localVentInfluence * layerBlend
```

`layerBlend` is 0.08 at that cell's bottom layer, 0.028 immediately above the bottom, and zero in higher subsurface layers. Layer 0 always reads the surface field, so it does not receive these subsurface additions even when it is the bottom or immediately above it. For a complete five-layer column the direct additions therefore affect layers 4 and 3. Partial columns use their own bottom and above-bottom indices.

Depth is normalized against the world's maximum ocean depth. A shallow cell's bottom can retain substantial cold surface influence; it is not necessarily close to the 1.5 °C abyssal target. Background temperature varies with local surface climate, column geometry, greenhouse forcing and relaxation history.

`GeodesicOceanResourceField.BuildVentThermalInfluence` supplies normalized influence from the existing compact outlets. At each outlet it uses the normalized system/member strength, at immediate ocean neighbours it uses 0.3 times that strength, and beyond those neighbours there is no direct vent addition unless another vent is nearby. Overlapping contributions use the maximum, rather than addition. A strongest submarine outlet can still have influence below 1 if another system establishes the normalization maximum.

At the reference `D = 274.65 K`, theoretical additions for influence 1 are:

| Geographic location | Bottom-layer target addition | Above-bottom subsurface target addition |
| --- | --- | --- |
| Actual outlet cell | +27.88 K | +9.76 K |
| Immediate ocean neighbour | +8.36 K | +2.93 K |
| More distant, with no other nearby outlet | 0 K | 0 K |

The background at each queried node must be added separately. Temperatures relax toward these targets with timescales interpolated between 80 and 1,200 simulation seconds. Approximate-mode initialization starts at its target profile; the serialized 2 K-per-layer startup drop applies to the other thermal model. No thermal solver or thermal parameter was modified.

There is also a separate `GeodesicExperiencedTemperatureField` microthermal query. It blends toward 623.15 K (350 °C) within visible vent cores and their short falloff, at the eligible bottom layer. That query can approach source-fluid temperature. **It is not the temperature supplied to geodesic organisms.** Keeping the current cell/layer habitat authority avoids exposing founders to a visual outlet's source core by accident.

The 20–70 °C C# optimum is compatible with sufficiently warmed outlet habitats, rather than with 350 °C source fluid. For example, an abyssal background plus the theoretical strongest addition is about 302.53 K (29.38 °C), inside both the C# and scene optima. Weaker outlets, cold shallow columns and immediate neighbours can lie below either optimum. The supplied 251–299 K organism range also includes both unsuitable cold water and temperatures inside both optima. Actual per-outlet and neighbour measurements require the new telemetry during the manual run.

Changing the gas-production multiplier changes substrate supply, not the already-defined thermal footprint's normalized strengths. No vent production, global multiplier or source temperature was adjusted here.

## Legacy versus geodesic biology before these changes

| Behaviour | Legacy path | Previous geodesic path |
| --- | --- | --- |
| Metabolic response | Smooth efficiency shoulders: cold width 1.5 × inherited margin, hot width 0.75 × margin. | Linear shoulders of one inherited margin on either side. |
| Dormancy floor | Applied to positive residual efficiency within a shoulder; truly zero efficiency stays zero. | Ignored the manager's floor setting. |
| Reproduction eligibility | Metabolism updates `CanReplicate` from the efficiency threshold; lifecycle checks it. | Lifecycle checked energy/carbon and chance, with no temperature check. Stored resources could support division at zero performance. |
| Stress maintenance | Basal maintenance increases with `1 - efficiency`; ordinarily up to twice basal. | Flat basal cost, regardless of temperature. |
| Direct thermal death | Optional immediate death at broad extreme thresholds, extended where the individual's traits require it. No exposure timer. | Only energy depletion and old age were classified. |
| Thermal traits | Inherit min/max/margin and mutate them with the configured chance/scale. | Inherited those traits exactly, but did not mutate them. |

The geodesic runtime is a separate mode branch and bypasses the legacy metabolism/lifecycle systems. The shared manager's temperature controls were not passed across that boundary. This explains why the Inspector settings alone did not restore legacy restrictions.

The new code keeps the existing geodesic **linear** response rather than silently replacing it with the wider legacy cold shoulder. It restores the floor, eligibility and stress semantics explicitly. No substrate stoichiometry, reaction energy yield, oxygen inhibition, thermal transport or reproduction carbon split was rebalanced.

## Temperature changes implemented

### Reproduction and dormancy

Every lifecycle step computes efficiency from the current authoritative habitat temperature and the organism's own thermal traits. Temperature reads share an existing per-node value buffer with a separate revision stamp keyed to the completed ocean thermal tick. Moving into a different cell/layer queries that node; a thermal tick invalidates the biology read cache. Reproduction is not allowed to use a previous habitat's reaction request from the independent 0.25-second reaction cadence.

Division requires **positive performance at least `temperatureReplicationMinEfficiency`**, sufficient energy remaining after the division cost, and the existing carbon conditions. Above the threshold, probability is `clamp01(reproductionRate * simulationDelta * performance)`. Optimal performance retains the old probability; marginal temperatures reduce it directly as well as reducing metabolic production.

Temperature rejection does not spend division energy or split stored carbon. Normal maintenance, drift, aging and any subsequent metabolism continue. The dormancy floor applies only to positive residual production inside the inherited tolerance shoulders; it does not restore production beyond a zero-efficiency edge. “Dormant” telemetry means performance at or below `max(0.05, floor + 0.01)`; it does not freeze passive movement.

With the scene's unmutated traits and current 0.15 threshold:

- Optimal performance is 1 from 288.15 to 353.15 K (15–80 °C).
- Reproduction is permitted approximately from 271.15 to 370.15 K (-2 to 97 °C), with reduced probability outside the optimum.
- At 273.15 K (0 °C), performance is 0.25 and division probability is one quarter of its optimal value; the base rate itself was not changed.
- Production is zero at or beyond 268.15 / 373.15 K (-5 / 100 °C). Drift continues.

Consequently **0.15 remains a permissive reproduction setting**, even after it is enforced. It was preserved rather than silently raised. For comparison, threshold 0.5 would put the cold reproduction edge at 278.15 K (5 °C) with these same traits; threshold 0.8 would put it at 284.15 K (11 °C). Evolved min/max/margin traits move these boundaries.

### Gradual stress and survival

The new scene-explicit controls under **Geodesic Temperature Stress** are:

| Control | Value | Effect |
| --- | --- | --- |
| `geodesicTemperatureStressMaintenanceMultiplier` | 1 | Extra maintenance = basal × (1 - performance) × this value. Total cost ranges from basal to twice basal, matching the ordinary legacy stress multiplier. Set 0 for flat maintenance. |
| `geodesicColdExposureDeathSeconds` | 120 | Full-severity-equivalent cold exposure needed for a cold death; 0 disables cold exposure death. |
| `geodesicHeatExposureDeathSeconds` | 60 | Equivalent heat exposure needed for a heat death; 0 disables heat exposure death. |
| `geodesicTemperatureExposureRecoveryPerSecond` | 1 | Exposure seconds removed per simulation second once that side of the thermal tolerance is no longer exceeded. |

Cold exposure begins below the individual's `optimalTempMin - lethalTempMargin`; heat exposure begins above `optimalTempMax + lethalTempMargin`. Severity rises linearly from zero at that edge to one at the broad extreme setting, clamped so the extreme cannot intrude inside the individual's tolerance. If an extreme setting is nonpositive, its fallback is two margins outside the optimum. The old broad settings anchor severity rather than causing immediate death.

Exposure accumulates as `severity * simulationDelta`. Cold and heat have separate state and recover separately; an organism does not inherit its parent's exposure. Crossing an edge briefly cannot trigger immediate death from a fresh state. At constant 251 K with the current unmutated scene traits, cold severity is about 0.49, implying about 245 simulation seconds from fresh exposure to the cold threshold, **if energy depletion or old age has not already killed it**. At full extreme severity, the configured times are 120 seconds cold and 60 seconds heat.

`temperatureDirectDamageEnabled=false` disables and clears exposure death state. It does not disable reduced metabolic efficiency, the reproduction gate or stress maintenance. The old heat/cold diagnostic-marker settings remain legacy markers; they were not promoted into extra hard survival boundaries. Organism min/max/margin traits determine suitability and exposure edges, keeping tolerance evolvable.

Old age and energy-depletion classification retain priority if they coincide with an exposure threshold in the same step. A death attributed to cold or heat therefore means the exposure threshold was reached while neither earlier classifier applied. Deaths use the existing carbon-recycling path.

### Inheritance and mutation

Children inherit thermal min/max/margin from packed parent state. The existing mutation chance and ±K scale now apply in geodesic biology as well, with ordered bands, minimum width 2 K, band limits 150–500 K and minimum margin 1 K. A separate deterministic temperature-mutation random stream avoids using the passive movement stream or adding mutation draws to reproduction's stream. Unmutated valid band edges remain exact copies.

The two exposure counters are packed organism state: they resize, copy on swap-back, reset for new organisms and mirror through the existing snapshot DTO. Older snapshots default the added fields to zero. This does not add planet-cache data or extend the pre-existing geodesic world save/restore implementation.

## Separate confirmed correction: reproduction energy conservation

At the start of this task the working tree was clean at commit `43adaab`. Both the committed and local geodesic implementation did:

```
parentEnergy = previousEnergy - divisionCost
childEnergy = max(0.1, parentEnergy * 0.5)
```

It never deducted the child's allocation from the parent. Without the floor, combined remaining energy was `1.5 * (previousEnergy - divisionCost)`. This creates energy relative to the intended post-cost budget and can even increase total pre-division energy. For example, previous energy 1 and cost 0.2 produced parent 0.8 plus child 0.4 = 1.2. The 0.1 floor adds another source of unbacked energy for low-energy parents. This can subsidize survival after drift into unsuitable water; its numerical contribution to the supplied run cannot be determined from aggregate death counts alone.

The legacy spawn path also grants child energy from the parent's energy while lifecycle subtracts only the division cost. It is not a conservation-correct reference. That legacy implementation was left unchanged because the requested correction concerns geodesic biology.

`SplitDivisionEnergy` now pays the existing cost once and splits the remaining energy equally:

```
remaining = previousEnergy - divisionCost
childEnergy = remaining * 0.5
parentEnergy = remaining - childEnergy
```

For the example above, both receive 0.4, totaling 0.8. There is no unbacked minimum-energy grant. Eligibility requires energy strictly greater than the cost so a split leaves positive energy. Carbon transfer and its configured split are unchanged. This conservation correction is independent of the temperature gate and introduces no reaction-yield rebalance.

## Manual telemetry and expected population effects

The existing ten-simulation-second biology diagnostic interval now also emits:

- `[GeodesicTemperatureConfig]` once at the first step: active temperature settings and conservation rule. The band threshold, dormancy floor and direct-damage flag are repeated in aggregate snapshots to reflect live Inspector edits.
- `[GeodesicBiologyTemperature]`: population in suitable/marginal/unsuitable bands, births by the **parent's** temperature band, cold/heat/starvation/old-age deaths, mean energy in unsuitable habitats, dormancy/stress/exposure counts, and inherited trait ranges.
- `[GeodesicBiologyVentRings]`: actual organism temperature and performance ranges by nearest active submarine outlet's ocean-neighbour ring: 0, 1, 2, 3–4, 5+, and unreachable.
- `[GeodesicVentHabitatProfile]`: current sampled bottom and above-bottom temperature ranges in those same rings, including the strongest outlet's exact cell, bottom layer, influence and bottom temperature. It continues to report when population is zero.

Suitable means performance >= max(0.5, reproduction threshold). Marginal means positive performance >= the reproduction threshold but below the suitable boundary. Unsuitable means reproduction is disallowed. With current settings this is >=0.5 / [0.15,0.5) / <0.15. `temperatureBlocked` counts all temperature-ineligible organisms; `temperatureOnlyBlocked` narrows that to ones otherwise energy/carbon ready and below the population cap. Birth/death totals are cumulative since runtime initialization; population, energy and stress figures are current snapshots.

Ring labels come from a one-time read-only multi-source walk through the actual ocean-neighbour graph. “Active outlet” here means a valid submarine outlet with positive raw strength and an active injection node, as used for its thermal footprint; it is not a distance-based reproduction or survival restriction. The ring map is consulted only by diagnostics. All organisms contribute to organism summaries; environmental profiles use at most 64 deterministic reservoir-sampled cells per ring band, with a strongest outlet included. `cells=sampled/total` and `n=...` expose coverage; these are **sampled ranges**, not claimed global extrema. No per-organism Console logging was added.

Expected effects are reduced division in cold habitats, no division below the configured gate even with stored resources, finite cold/heat exposure tolerance, and less energy available to subsidize offspring or distant survivors. Populations should become more associated with genuinely warm, resource-supported habitat, while cold drifting survivors and later temperature adaptation remain possible. No final population size or specific change from 6,100 is predicted without a manual run. Background water above the permissive gate can still support marginal reproduction where substrates permit it.

For manual review, compare the temperature bands and births alongside vent-ring profiles. Check that unsuitable-band births stay zero, exposure can recover after a return to tolerated water, and new thermal deaths appear separately from starvation and old age. Use the existing stable-ID movement diagnostics to inspect particular drifting organisms if needed. Movement equations, newborn position initialization, rendering fixes, ocean chemistry, physical depth, hydrology, terrain, vent production and persistent planet caches were not modified.

Static review covers callback signatures, per-node temperature-cache invalidation, birth/death classification, conservative energy partition, mutation stream separation, exposure state copying/reset/snapshot fields, ring-label isolation from biology, and scene-only biological parameter additions. `git diff --check` passed. Compilation and runtime behaviour remain unverified by execution, as requested. Stop for manual review.
