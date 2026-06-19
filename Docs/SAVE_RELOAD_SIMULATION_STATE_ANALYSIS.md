# Save/Reload Simulation State Analysis

## Goal

Assess whether SimulaVit can save a running simulation and reload it later so the user can continue from the same biological and planetary state. The desired save includes at least:

- Replicator counts and positions.
- Replicator per-agent state needed to continue lifecycle, metabolism, steering, movement, and rendering.
- Planet state, including temperature, resource chemistry, and seasonal/day-year timing.

## Executive summary

Saving and reloading is feasible, but it should be treated as a simulation snapshot feature rather than a simple list of visible agents. The current architecture already has good foundations:

- Replicators have a central authoritative hot-path state in `ReplicatorPopulationState`.
- Planet chemistry and temperature are concentrated in `PlanetResourceMap`.
- Terrain generation is mostly deterministic from `PlanetGenerator` settings and seed-derived offsets.
- Simulation time is tracked by `ReplicatorSimulationPipeline` / `ReplicatorManager`.

The best approach is to add an explicit versioned snapshot model and small capture/apply APIs on the authoritative systems. Save deterministic configuration plus mutable runtime state. On load, initialize deterministic/generated systems first, then apply mutable state, rebuild transient caches, and resume the simulation clock.

A minimal save that stores only replicator count/position and a few planet values would load visually, but it would not reliably continue the same simulation because metabolism starvation counters, ocean layers, chemical resources, thermal inertia, timers, spontaneous spawn gating, and sun phase all affect future behavior.

## Current architecture relevant to save/load

### Replicator state

`ReplicatorPopulationState` is explicitly documented as authoritative for hot per-agent simulation fields. It owns parallel arrays for position, rotation, direction, velocity, energy, age, organic carbon store, cooldowns, starvation counters, metabolism, locomotion, temperature preferences, movement steering, size, color, and ocean-layer indices.

The manager still keeps a `List<Replicator>` companion list for compatibility, debug, lifecycle spawning, and rendering bridges. New agents are added to both the list and the population state through `populationState.AddAgentFromReplicatorData(...)`.

Implication: save/load should primarily serialize `ReplicatorPopulationState`, then rebuild the companion `agents` list from that data. Serializing only `agents` risks missing hot-path fields if agents are stale between compatibility sync points.

### Planet/resource state

`PlanetResourceMap` owns mutable simulation arrays for atmospheric/surface resources (`co2`, `o2`, `organicC`, `h2s`, `h2`, `ch4`, `s0`, etc.), layered ocean resources, dissolved resources, scent fields, vent heat, surface temperature, ocean masks/layer counts, and timer accumulators for vents, atmosphere mixing, and thermal updates.

Some arrays are deterministic or cacheable generation results, while others are true mutable state:

- Deterministic/generated: cell directions, ocean mask, vent mask, vent strength, vent cells, mineral patch fields, ocean layer structure/light/temperature offsets.
- Mutable and must be saved for continuation: CO2/O2/CH4/OrganicC/H2S/H2/S0, dissolved Fe2+, layered ocean resources, scent fields if predator steering continuity matters, surface temperatures when thermal inertia is enabled, and the internal vent/atmosphere/thermal timers.

Implication: save/load needs a resource-map snapshot API. Accessing private arrays externally by reflection would be fragile; the owner should expose explicit capture/apply methods.

### Time, day/night, and seasons

`ReplicatorSimulationPipeline` owns `simulationTimeSeconds` and advances it once per simulation step. `ReplicatorManager` mirrors current simulation time for other systems and HUD access. `SunSkyRotator` has an internal `accumulatedOrbitAngle` that drives daily rotation, and seasonal declination is derived from the current daily orbit angle, day length, and year length.

Implication: the snapshot should include both global simulation time and sun/orbit phase. The cleanest implementation is to add APIs that set the pipeline simulation time and sun accumulated orbit angle from a saved value. A less exact variant can reconstruct sun phase from saved simulation time and day/year settings, but this will not preserve manual phase resets or any future non-time-derived sun state.

## What should be saved

### Snapshot envelope

Use a versioned root DTO, for example:

```text
SimulationSaveFile
  schemaVersion
  applicationVersion / unityVersion (optional)
  savedUtc
  startupConfig / scenario config
  planetGeneratorConfig
  planetResourceMapConfig
  simulationTiming
  sunSkyState
  populationState
  resourceMapState
  diagnostics/debug state (optional)
```

The save should contain enough deterministic configuration to regenerate stable static data even when loading into a fresh scene.

### Planet generation/static config

Save the settings that define the generated planet and resource grid:

- Planet seed and `useRandomSeed`.
- Terrain parameters: radius, resolution, noise magnitude/roughness/layers/persistence/offset, ocean coverage/depth/enabled, bathymetry settings.
- Resource-map simulation resolution and all baseline generation parameters that affect resource grid generation: resource baselines, patch scales, vent frequency/threshold/strength, layered ocean configuration, thermal config.
- Startup sun/climate/atmosphere settings where applicable.

If the project treats scenes/prefabs as the source of truth, the save can store a hash of this config and refuse or warn when loading into incompatible settings. However, for portable saves, include the actual config values.

### Global simulation time/timers

Save:

- `simulationTimeSeconds` from `ReplicatorSimulationPipeline` / `ReplicatorManager`.
- `simulationStepCount` from `ReplicatorManager`, because spawn-candidate caches and steering throttles depend on step count.
- `metabolismTickTimer`, `nextScentUpdateTime`, spontaneous-spawn gating state, and any other non-derived manager timers/gates.
- `ventTimer`, `atmosphereTimer`, and `thermalTimer` from `PlanetResourceMap`.
- Sun/orbit state: accumulated orbit angle, skybox rotation if it must resume exactly, day length, year length, axis tilt, seasonal phase options.

### Replicator population

Save a compact array of per-replicator records. At minimum, include every field from `ReplicatorPopulationState` that can influence future simulation or visuals:

- `position`, `rotation`, `currentDirection`, `moveDirection`, `desiredMoveDirection`, `velocity`.
- `energy`, `age`, `organicCStore`, `speedFactor`, `attackCooldown`, `fearCooldown`.
- `metabolism`, `locomotion`.
- Temperature traits: `optimalTempMin`, `optimalTempMax`, `lethalTempMargin`.
- Starvation/toxicity timers: all `Starve*Seconds`, `O2ToxicSeconds`, plus `O2ComfortMax`, `O2StressMax`, `canReplicate`.
- Steering/movement memory: `lastHabitatValue`, `tumbleProbability`, `nextSenseTime`, `movementSeed`.
- Visual/size/layer state: `size`, `color`, `currentOceanLayerIndex`, `preferredOceanLayerIndex`.

Also save fields currently only present on the companion `Replicator` object and not mirrored in `ReplicatorPopulationState`, because they affect future behavior or child traits:

- `maxLifespan`.
- `traits` (`spawnOnlyInSea`, `replicateOnlyInSea`, `moveOnlyInSea`, `surfaceMoveSpeedMultiplier`).
- `biomassTarget`.
- `locomotionSkill`.
- `lastDeathCauseCandidate` is optional unless useful for diagnostics.

This reveals one important implementation gap: `ReplicatorPopulationState` currently does not mirror `maxLifespan`, `traits`, `biomassTarget`, or `locomotionSkill`. A robust save system either needs to serialize these from the companion `agents` list after first syncing hot state to agents, or extend `ReplicatorPopulationState` to own/mirror them as well. For correctness and consistency, extending the authoritative state is preferable long-term.

### Planet mutable resources and temperatures

Save resource arrays by resource type and layer domain:

- Aggregate/surface arrays: CO2, O2, OrganicC, H2S, H2, CH4, S0, P, Fe, Si, Ca if minerals can be consumed or modified.
- Ocean dissolved resources dictionary, including dissolved Fe2+.
- Layered ocean resources dictionary for resources that are layered.
- Scent fields: toxic proteolytic waste and dissolved organic leak, including layered scent fields if enabled.
- Surface temperature array when thermal inertia is enabled; otherwise it can be recomputed from sun and planet state.
- Debug counters can be omitted unless desired for diagnostics continuity.

Because generated/static arrays can be large, the save format should distinguish generated data from mutable data. For normal saves, regenerate static arrays from config and then apply mutable arrays, validating cell count and schema version. For exact/diagnostic saves, optionally include static arrays too.

## Recommended approach

### 1. Create versioned save DTOs independent of MonoBehaviours

Add plain serializable classes/structs such as:

- `SimulationSaveFile`
- `SimulationClockSnapshot`
- `SunSkySnapshot`
- `PlanetGeneratorSnapshot`
- `PlanetResourceMapSnapshot`
- `ReplicatorPopulationSnapshot`
- `ReplicatorSnapshot`

Keep these DTOs free of Unity object references, materials, meshes, GameObjects, or transient caches. Use serializable numeric structs for vectors/quaternions/colors if using JSON.

### 2. Add explicit capture/apply methods to owners

Avoid external systems reaching into private fields. Add APIs roughly like:

```text
ReplicatorManager.CaptureSnapshot()
ReplicatorManager.ApplySnapshot(snapshot)
PlanetResourceMap.CaptureSnapshot()
PlanetResourceMap.ApplySnapshot(snapshot)
ReplicatorSimulationPipeline.CaptureClockSnapshot()
ReplicatorSimulationPipeline.ApplyClockSnapshot(snapshot)
SunSkyRotator.CaptureSnapshot()
SunSkyRotator.ApplySnapshot(snapshot)
```

Each owner should know which internal fields are durable and which are cache-only.

### 3. Load sequence

A reliable load sequence should be:

1. Pause simulation / set simulation steps per frame to 0.
2. Apply startup/config snapshot to `PlanetGenerator`, `PlanetResourceMap`, `SunSkyRotator`, and manager settings.
3. Regenerate planet geometry and resource grid static data.
4. Validate resolution/cell count/resource schema.
5. Apply mutable `PlanetResourceMap` arrays and timers.
6. Clear population, rebuild `agents` and `ReplicatorPopulationState` from `ReplicatorPopulationSnapshot`.
7. Apply simulation clock and sun phase.
8. Rebuild transient caches: prey spatial bins, spontaneous spawn candidate cache, vent heat neighbors if needed, render buffers/material property blocks, debug counters as desired.
9. Render once while paused, then resume at saved or user-selected speed.

### 4. Save format

Recommended initial format: compressed JSON or MessagePack in `Application.persistentDataPath/Saves`.

- JSON is easiest to inspect and debug, but large arrays can become slow and big.
- MessagePack or a custom binary format is better for high-population/high-resolution saves.
- A practical hybrid is JSON metadata plus compressed binary array payloads.

For this project, start with JSON + gzip or MessagePack-CSharp. The population can reach `maxPopulation = 50000`, and resource maps may include many arrays, so uncompressed pretty JSON will become bulky quickly.

### 5. Versioning and validation

Include:

- `schemaVersion`.
- `simulationResolution`, visual planet resolution, and expected cell count.
- Enum encoding strategy. Prefer strings for JSON readability or explicit integer maps with validation for compactness.
- Resource list/version, including layered-resource dimensions.
- Optional config hash to detect incompatible scene/settings.

On load, fail with a clear error if array lengths do not match the generated cell count or if a required resource is missing. For older saves, write migration code that fills new fields with safe defaults.

## Possible variants

### Variant A: Deterministic replay from seed and inputs

Save only the initial seed/config and a log of user inputs/events, then replay to the target time.

Pros:

- Very small saves.
- Great for reproducibility if every random source and update order is deterministic.

Cons:

- Current use of Unity random, frame deltas, Update-order timing, caches, and floating-point behavior makes exact replay difficult.
- Long simulations would require long replay times.
- Fragile across code changes.

Recommendation: not suitable as the primary save/load feature right now.

### Variant B: Minimal visual checkpoint

Save replicator positions/counts, basic age/energy/metabolism, global time, and a few planet/global atmosphere values.

Pros:

- Fastest to implement.
- Good for screenshots, demos, or approximate continuation.

Cons:

- Not scientifically or simulation-correct.
- Loses starvation/toxicity history, movement memory, resource gradients, layered ocean state, thermal inertia, and spawn timers.
- Reloaded simulation can diverge immediately.

Recommendation: only use as a temporary prototype or “export population” feature.

### Variant C: Full runtime snapshot without generated static arrays

Save deterministic planet/resource configuration plus all mutable runtime state arrays and population fields. Regenerate static terrain/resource masks on load, then apply mutable state.

Pros:

- Best balance of correctness, file size, and maintainability.
- Portable across machines if config and code are compatible.
- Avoids storing huge generated meshes/static masks.

Cons:

- Requires careful capture/apply APIs.
- Requires validation that regenerated static data exactly matches saved dimensions/config.

Recommendation: best primary approach.

### Variant D: Full memory-style snapshot including generated arrays

Save nearly all arrays, including static cell directions, ocean/vent masks, vent strength, layer light/temperature offsets, generated radii, and mutable arrays.

Pros:

- Most robust against procedural-generation drift.
- Useful for debugging and archival/research saves.

Cons:

- Largest files.
- More migration burden when arrays change.
- Still cannot serialize Unity objects directly.

Recommendation: useful as an optional “exact diagnostic snapshot,” not the default.

### Variant E: Unity scene serialization / prefab snapshot

Instantiate GameObjects or ScriptableObjects and rely on Unity serialization.

Pros:

- Editor-friendly for small data.
- Easy to inspect in Unity.

Cons:

- Poor fit for large hot-path arrays.
- Tightly coupled to scene objects and Unity serialization limitations.
- Runtime save-file compatibility is harder.

Recommendation: avoid for main simulation saves; ScriptableObjects may be fine for static scenario presets.

## Main issues and risks

### Authoritative population vs companion agents

`ReplicatorPopulationState` is authoritative for hot fields, but several biologically important fields remain only on `Replicator`. If save/load serializes only one side, it can miss data. The load path also needs a way to rebuild both structures without accidentally spawning through random constructors that mutate state.

Suggested fix: create a deterministic `ReplicatorSnapshot` apply path that constructs `Replicator` objects from saved values and directly fills `ReplicatorPopulationState` arrays.

### Private mutable resource arrays

Most planet resource arrays are private. That is good encapsulation, but it means save/load needs first-class methods on `PlanetResourceMap`, not an external serializer.

Suggested fix: implement `CaptureMutableState()` and `ApplyMutableState()` inside `PlanetResourceMap`.

### Timers and update ordering

Vents, atmosphere mixing, surface thermal inertia, metabolism ticks, scent updates, spawning, and sun rotation all have timer or phase state. If those are not saved, reloads can apply a tick too early/late or shift day/night phase.

Suggested fix: include all durable timer accumulators and phases in snapshots. Treat caches as rebuildable, timers as durable.

### Randomness

Spawning, mutation, movement seeding, and child trait mutation use Unity random calls. Continuing exactly after load requires saving random generator state, not only generated entities. Unity does not expose a simple cross-version-stable RNG guarantee.

Possible solutions:

1. Save `UnityEngine.Random.state` for near-term exact continuation.
2. Move simulation randomness to explicit `System.Random` or a small custom deterministic RNG owned by simulation systems and save its state.
3. Accept statistical continuation rather than bit-identical continuation.

Recommendation: for a serious simulation save, introduce explicit simulation RNG state. Unity random can still be used for visuals/UI.

### Large files and performance

At high population and resource resolution, snapshots can be large. `maxPopulation` is 50,000, and each agent has many float/vector fields. Resource maps include multiple per-cell and per-layer arrays.

Mitigations:

- Use binary or compressed JSON.
- Store arrays in structure-of-arrays form for compactness rather than one giant object per cell.
- Avoid saving generated meshes/materials/textures.
- Add asynchronous save/write to prevent frame hitches.
- Provide quicksave slots and periodic autosave with temporary file + atomic rename.

### Schema evolution

The simulation is actively evolving. New metabolism/resources/layers will add fields.

Mitigations:

- Version every save.
- Provide default values for missing fields.
- Store enum names or validate integer enum ranges.
- Keep migration tests with old sample saves.

### Floating-point and cross-version determinism

Even full snapshots will diverge after loading if code changes or if Unity/platform math behavior differs. The goal should be “continue from equivalent state,” not “bit-identical forever,” unless the project invests in deterministic simulation constraints.

### Thermal state ambiguity

If `enableSurfaceThermalInertia` is false, temperature can be recomputed from sun/resource state. If true, `surfaceTemperatureKelvin` is a real state variable and must be saved. The thermal timer and last thermal simulation time should also be saved or reset intentionally.

### Generated cache compatibility

`PlanetGenerationCache` can load generated data from cache based on config keys. Save/load should not depend on a cache file existing. The save should carry enough config to regenerate or enough static arrays to bypass cache.

## Suggested implementation milestones

### Milestone 1: Analysis/prototype save DTOs

- Define DTOs and schema version.
- Add capture-only methods and a debug command to write a snapshot.
- Verify snapshot includes population count, clock, planet config, and resource dimensions.

### Milestone 2: Population save/load

- Add deterministic rebuild path for `agents` + `ReplicatorPopulationState`.
- Include missing companion fields (`maxLifespan`, traits, `biomassTarget`, `locomotionSkill`).
- Load a paused scene and render identical population positions/colors.

### Milestone 3: Clock and sun phase

- Add clock apply API on `ReplicatorSimulationPipeline`.
- Add sun phase capture/apply on `SunSkyRotator`.
- Confirm day/night and seasonal position match before/after reload.

### Milestone 4: Planet resource mutable arrays

- Add `PlanetResourceMap` mutable snapshot capture/apply.
- Validate cell/layer lengths.
- Save timers and thermal state.
- Confirm chemistry/temperature inspection values match before/after reload.

### Milestone 5: Deterministic RNG and robustness

- Replace simulation-critical Unity random calls with explicit RNG streams.
- Save RNG states.
- Add autosave atomic write flow.
- Add migration tests and a compatibility error UI.

## Validation tests to add later

- Save then load with zero elapsed time; assert population count and per-agent key fields match.
- Save then load resource map; assert per-resource sums and sampled cell values match.
- Save then load sun state; assert sun direction and seasonal declination match.
- Save, load, run one fixed simulation step; compare against uninterrupted run if deterministic RNG is implemented.
- Load older schema; assert missing fields receive safe defaults.
- Corrupt/truncated save; assert load fails gracefully without damaging current simulation.

## Best recommendation

Implement Variant C: a versioned full runtime snapshot that stores deterministic configuration plus mutable population, resource, clock, timer, and sun-phase state. Do not serialize Unity scene objects directly. Add owner-level capture/apply APIs, rebuild transient caches on load, and migrate simulation randomness toward explicit saveable RNG streams.

This approach gives the best chance that a saved world can be reloaded later and continued in a meaningful way while keeping file size and code coupling manageable.
