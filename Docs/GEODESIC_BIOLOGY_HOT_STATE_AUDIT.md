# Geodesic biology hot-state audit

## Ownership before and after

Before this change, `ReplicatorPopulationState` already owned nearly all biology and passive-movement hot state as parallel arrays. Geodesic habitat cell, maximum lifespan, and division biomass target were exceptions owned by companion `Replicator` objects, so reaction sampling, lifecycle, and passive movement dereferenced `agents[i]`.

After this change the ownership boundary is:

```
ReplicatorPopulationState (authoritative)       Replicator companion (cold/bridge)
cell + layer, metabolism + locomotion           mutation Traits, locomotionSkill
energy, age, OrganicC, lifespan, biomass target color and other visual metadata
thermal and starvation/O2 state                 construction/save/debug compatibility
passive direction, wander and RNG schedules     receives explicit debug bridge copies
visual position/radius (not habitat authority)
```

`GeodesicCellIndex`, `MaxLifespan`, and `BiomassTarget` are the migrated fields. Geodesic reaction request preparation, occupied-habitat sampling, lifecycle tests, horizontal/vertical boundary handling, and visual target calculation now read packed state. Reproduction still constructs one companion object at the birth boundary; immutable/cold traits, color, and locomotion skill deliberately remain there. Diagnostics use packed fields. No broad synchronization pass was added.

Capacity is next-power-of-two with a minimum of four. Removal swaps the final entry through every packed array, clears the old final slot, and decrements `Count`. Reset retains capacity and sets `Count` to zero. Render/debug bridges are one-way: packed habitat state may be copied to a companion for inspection, while visual `Position` cannot alter cell/layer authority.

## Subsystem classification

| Subsystem | Before | After |
|---|---|---|
| Core biology and thermal/stress state | already packed | already packed |
| Habitat authority | partially packed (layer packed, cell on object) | already packed |
| Reaction requests and sparse competition cache | partially packed (reused request AoS, packed scratch arrays) | partially packed by design; the reused compact request struct is consumed densely across adjacent passes and allocates nothing after growth |
| Lifecycle/division gating | partially packed (lifespan/biomass on object) | packed for hot tests; object construction remains only at birth |
| Passive kinematics/wander/vertical schedules | partially packed (movement state packed, cell on object) | already packed |
| Visual interpolation | already packed | already packed and explicitly non-authoritative |
| Spawn/reproduction construction | still object/struct heavy, cold boundary | deliberately unchanged cold boundary |
| Telemetry/debug/save compatibility | companion/list based, cold | deliberately outside hot SoA |

The reusable `Request[]` remains AoS: aggregation, competition, and commit access its records by reference (rather than repeatedly copying the record), and splitting it without Unity profiler evidence would be cosmetic. The sparse generation-stamped habitat cache, bounded resource reads, proportional competition, and physical inventory commit are unchanged.

## Legacy active-locomotion audit (future Geodesic work only)

Legacy steering runs only for `Amoeboid` and `Flagellum`; `PassiveDrift` and `Anchored` exit before habitat sampling. It samples the current habitat temporally, never scans neighboring habitats. Defaults are Amoeboid 0.5 s sensing with ±0.15 s jitter and Flagellum 0.2 s with ±0.05 s jitter. Improvement/worsening thresholds are both 0.05; better/worse/neutral tumble chances are 0.05/0.65/0.2, clamped to 0.02–0.9. Suitability memory blends at 0.35. On the first sample, the base probability is 0.2 and memory is initialized directly.

Amoeboid turns at most 60 degrees and adds run noise of strength 0.08 between senses; Flagellum may turn up to 180 degrees and has no Amoeboid run noise. Both tumble decisions and sensing jitter currently use `UnityEngine.Random`. A future Geodesic port must instead use packed deterministic per-agent seed/cursor state so save/replay and swap-back remain stable.

Suitability is weighted temperature fitness plus metabolism food fitness (defaults 5:1): sulfur chemosynthesis=min(H2S, CO2), hydrogenotrophy=min(H2, CO2), photosynthesis=min(layered light, CO2), saprotrophy=min(OrganicC, O2), fermentation=OrganicC, methanogenesis=min(H2, CO2), methanotrophy=min(CH4, O2), and predation=min(O2, dissolved-organic scent). Optional scent terms further reward prey scent for predators and penalize toxic waste otherwise. Anaerobic O2 inhibition multiplies food fitness through metabolism efficiency; it is not a separate avoidance rule.

Geodesic PassiveDrift invokes none of that steering code and reads no resource, temperature-suitability, or `LastHabitatValue` input for movement. Its fixed intervals and angular speed remain 0.10 s, 0.25 s, and 0.002 rad/s respectively (passive kinematics, reaction, angular speed).

## Profiling status

Profiler markers cover request preparation, packed reaction passes, packed movement phases, capacity growth, and swap-back removal. Unity Player profiling was not available in the command-line environment, so this audit makes no measured speedup claim. Capture comparable subdivision-6 runs at populations 500, 1000, and 1800–2200 before selecting another optimization target.
