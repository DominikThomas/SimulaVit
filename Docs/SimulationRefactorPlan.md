# Incremental Refactor Plan: `ReplicatorManager` and `PlanetResourceMap`

# Simulation Refactor Plan

Status: Draft / Active architecture plan

Purpose:
- guide incremental refactoring of ReplicatorManager and PlanetResourceMap
- preserve simulation behaviour during extraction
- prepare the project for profiling and later Jobs/Burst/DOTS migration

Non-goals:
- immediate gameplay rebalance
- one-shot full rewrite
- forced ECS migration before profiling

## Constraints
- Preserve gameplay/simulation behavior first (no balancing changes during extraction).
- Refactor in small, low-risk steps with parity checks between steps.
- Separate simulation, rendering, debugging, and configuration.
- Prepare for later Jobs/Burst/DOTS by isolating pure data + deterministic systems.

## Current Responsibility Clusters

### `ReplicatorManager` clusters
1. **Unity lifecycle orchestration** (`Start`, `Update`) and dependency resolution.
2. **Agent lifecycle** (aging, death, reproduction, spontaneous spawn).
3. **Metabolism chemistry loops** (resource uptake/consumption, starvation/death causes).
4. **Predation and prey indexing** (candidate bins, kill/removal pass).
5. **Locomotion decisions** (run-and-tumble and desired direction updates).
6. **Movement execution** via `ReplicatorUpdateJob` and post-validation.
7. **Rendering** (`RenderAgents`, batching/property blocks, color logic).
8. **HUD/debug telemetry** (`OnGUI`, unit formatting, counters, logs, sessile validation).
9. **Habitat scoring** (temperature + food + scent steering scores).

### `PlanetResourceMap` clusters
1. **Resource storage and API** (`Get`, `Add`, resource arrays, volatile checks).
2. **Initialization/generation** (cell arrays, vent placement, masks, heat neighbor setup).
3. **Tick scheduling** (`Update` timers for vent and atmosphere ticks).
4. **Vent chemistry** (replenishment, caps, local decay).
5. **Atmosphere chemistry** (global mixing + natural oxidation).
6. **Local diffusion** (H2/H2S diffusion loops, scent decay/diffusion).
7. **Temperature model** (insolation, vent heat contribution, clamping, stats).
8. **Sun/dependency resolution and editor validation** (`ResolveSunReferences`, `OnValidate`).
9. **Debug visualization** (`OnDrawGizmosSelected`, labels, gradient scaling).

## Target Module Split

## `ReplicatorManager` extraction targets
- `ReplicatorSimulationConfig` (ScriptableObject): all tunables now serialized on manager.
- `ReplicatorPopulationState`: `List<Replicator>`, counters, and reusable buffers.
- `ReplicatorLifecycleSystem`: aging, lifespan, reproduction eligibility, death bookkeeping.
- `ReplicatorMetabolismSystem`: per-metabolism resource/energy updates and starvation causes.
- `ReplicatorPredationSystem`: prey binning, candidate queries, bite/kill/removal pipeline.
- `ReplicatorSteeringSystem`: habitat scoring + run-and-tumble decision logic.
- `ReplicatorMovementSystem`: wraps job buffer prep + schedule/complete.
- `ReplicatorRenderSystem` (MonoBehaviour-adjacent): instanced draw and color mapping.
- `ReplicatorHudPresenter` (MonoBehaviour/UI): HUD string composition and rendering.
- `ReplicatorDebugTelemetry`: throttled logs, counters, debug-only validation.
- `ReplicatorSpawnSystem`: initial spawn + spontaneous spawn rules and sampling.

## `PlanetResourceMap` extraction targets
- `PlanetResourceState`: arrays, masks, vent cells, neighbors, resolution metadata.
- `PlanetResourceInitializer`: deterministic map construction from generator/noise.
- `VentChemistrySystem`: replenishment + vent caps + vent-local resource logic.
- `AtmosphereMixingSystem`: global CO2/O2 averaging + land/ocean exchange.
- `OxidationSystem`: organic C oxidation pass.
- `LocalDiffusionSystem`: H2/H2S diffusion + decay.
- `ScentFieldSystem`: add/clear/decay/diffuse scent fields.
- `PlanetTemperatureService`: insolation + vent heat + ocean damping + stats.
- `PlanetResourceDebugDrawer` (MonoBehaviour/editor): gizmo rendering and labels.
- `PlanetResourceValidation`: parameter clamps currently in `OnValidate`.

## Proposed Interfaces / Data Containers
- `IPlanetResourceReadOnly`
  - `float Get(ResourceType, int cell)`
  - `float GetTemperature(Vector3 dir, int cell)`
  - `float GetInsolation(Vector3 dir)`
  - `bool IsOceanCell(int cell)`
- `IPlanetResourceWritable : IPlanetResourceReadOnly`
  - `void Add(ResourceType, int cell, float delta)`
  - `void AddScent(ResourceType scentType, int cell, float amount)`
- `IReplicatorPopulationView`
  - `IReadOnlyList<Replicator> Agents`
- `IReplicatorPopulationMutator : IReplicatorPopulationView`
  - spawn/remove helpers, deferred removal queue.
- `SimulationTickContext`
  - `float DeltaTime`, `float Time`, `int Resolution`, shared references.
- `ReplicatorJobInput/ReplicatorJobOutput`
  - NativeArrays for movement job boundary.

These interfaces let pure C# systems compile against abstractions now, then swap backing stores later (NativeArray/ECS component data) with minimal logic rewrite.

## MonoBehaviour vs Pure C# Guidance

### Keep MonoBehaviour-driven initially
- Scene references and serialized defaults (`PlanetGenerator`, materials, meshes, lights).
- Unity lifecycle entrypoints (`Awake/Start/Update/OnValidate/OnDrawGizmosSelected/OnGUI`).
- Rendering and immediate-mode/editor debug drawing.

### Convert to pure C# systems first
- Tick math: metabolism, predation scoring, movement steering decisions.
- Resource mixing/diffusion/oxidation loops.
- Spawn candidate scoring and selection logic.
- Temperature/fitness utility logic.

Rule of thumb: anything requiring `Transform`, `Graphics.DrawMeshInstanced`, `GUI`, or editor APIs stays in MonoBehaviour shells; everything else moves to stateless/system classes.

## Incremental Refactor Order (Low-Risk Commits)

1. **Create data/config wrappers only**
   - Introduce `ReplicatorSimulationConfig` + `PlanetResourceConfig` (ScriptableObjects).
   - Manager/map copy values from old serialized fields into config at runtime (temporary bridge).
   - No logic moves yet.

2. **Extract read-only query services**
   - Move `ComputeTemperatureFitness`, `ComputeFoodFitness`, `ComputeLocalHabitatValue` into `HabitatScoringService`.
   - Move planet temperature-related methods into `PlanetTemperatureService` wrapper.
   - Keep original methods as thin delegators for parity.

3. **Extract debug/UI from simulation classes**
   - Move `OnGUI` and HUD formatting to `ReplicatorHudPresenter`.
   - Move gizmo/debug rendering from `PlanetResourceMap` to `PlanetResourceDebugDrawer`.
   - Keep existing outputs/text identical.

4. **Extract resource tick systems from `PlanetResourceMap`**
   - Pull `ApplyVentReplenishment`, `ApplyAtmosphereMixing`, `ApplyNaturalOxidation`, `ApplyLocalResourceMixing`, scent decay/diffusion into separate classes operating on `PlanetResourceState`.
   - `PlanetResourceMap.Update` becomes orchestration only.

5. **Extract replicator simulation systems in dependency order**
   - 5a. `ReplicatorLifecycleSystem` (age/death/reproduction eligibility).
   - 5b. `ReplicatorMetabolismSystem` (chemistry update).
   - 5c. `ReplicatorPredationSystem`.
   - 5d. `ReplicatorSteeringSystem`.
   - Keep methods in manager as pass-through calls while tests verify parity.

6. **Isolate movement job boundary**
   - Move job struct and buffer management into `ReplicatorMovementSystem`.
   - Manager provides context + calls `movementSystem.Step(...)`.
   - This is the anchor point for later Burst compilation and ECS migration.

7. **Introduce explicit simulation pipeline object**
   - `ReplicatorSimulationPipeline` calls systems in current order.
   - Manager `Update` only constructs context and invokes pipeline.

8. **Prepare DOTS-friendly data seam**
   - Add optional mirror structures (`NativeArray` snapshots / structs-of-arrays) behind interfaces.
   - Keep authoritative source unchanged for now, but verify conversion layer.

9. **Only after parity is stable: optimize**
   - Burst attributes on pure jobs.
   - Replace selected list traversals with Native containers.
   - Migrate one subsystem at a time to ECS.

## Suggested Parity Gates Per Step
- Existing edit/play mode tests green.
- Add deterministic smoke checks for:
  - Total population trend over N ticks (within tolerance).
  - Global CO2/O2 means after fixed tick sequences.
  - Vent H2/H2S statistics invariants.
- Compare pre/post extraction metrics from same seed if deterministic seed injection is added.

## Post-Split `Update()` Orchestration Example

```csharp
// MonoBehaviour shell only.
private void Update()
{
    if (!_initialized) return;

    var ctx = new SimulationTickContext
    {
        DeltaTime = Time.deltaTime,
        Time = Time.time,
        Resolution = _planet.Resolution,
    };

    _pipeline.Step(ctx);
}
```

```csharp
// Pure C# pipeline preserving current order.
public void Step(in SimulationTickContext ctx)
{
    _scentSystem.Step(ctx, _population, _resources);
    _lifecycleSystem.PreMetabolismStep(ctx, _population);
    _metabolismSystem.Step(ctx, _population, _resources);
    _predationSystem.Step(ctx, _population, _resources);
    _spawnSystem.Step(ctx, _population, _resources);
    _steeringSystem.Step(ctx, _population, _resources);
    _movementSystem.Step(ctx, _population, _planetShape);
    _renderSystem.Step(ctx, _population); // optional, MonoBehaviour adapter
    _telemetrySystem.Step(ctx, _population, _resources);
}
```

This preserves today’s effective execution order while isolating each concern behind a stable boundary.

## DOTS/Jobs Readiness Notes
- Avoid passing MonoBehaviour references into pure systems; pass only config/state interfaces.
- Keep system methods deterministic and side-effect-scoped (input state → output state).
- Favor `struct` data packets for per-agent hot paths.
- Keep a single translation layer between OO `Replicator` objects and SoA/Native arrays.

