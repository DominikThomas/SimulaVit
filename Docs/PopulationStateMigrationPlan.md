# ReplicatorPopulationState migration (performance-oriented pre-DOTS step)

## 1) Hottest per-agent fields to move first
These fields are touched in nearly every metabolism/movement/steering iteration and are the first SoA candidates:

- Spatial + kinematics: `position`, `currentDirection`, `moveDirection`, `velocity`.
- Core scalar state: `energy`, `speedFactor`, `age`, `organicCStore`, `attackCooldown`, `fearCooldown`.
- Type/state branching keys: `metabolism`, `locomotion`.
- Temperature fitness/death inputs: `optimalTempMin`, `optimalTempMax`, `lethalTempMargin`.
- Starvation timers used for death attribution: `starve*Seconds` fields.

## 2) Concrete `ReplicatorPopulationState` design
Step-1 introduces a transitional runtime SoA container (`Assets/Scripts/ReplicatorPopulationState.cs`) with packed arrays and explicit sync APIs:

- `SyncFromAgents(List<Replicator>)`: copy hot fields from object list to arrays.
- `SyncToAgents(List<Replicator>)`: write updated hot fields back to objects.
- `CopyEntryToAgent(index, Replicator)`: precise sync for kill/death paths.
- Capacity growth uses next power-of-two to reduce realloc churn.

This keeps the extracted systems intact while preparing data-oriented loops.

## 3) Authoritative source of truth in migration step 1
`List<Replicator>` remains authoritative in step 1.

`ReplicatorPopulationState` is a per-tick working set for hot loops. This avoids a risky ownership rewrite while preserving current external APIs and extracted system boundaries.

## 4) Safe migration order for extracted systems
1. **Metabolism** (done first): highest scalar-touch density, low transform coupling.
2. **Movement**: consume SoA kinematics (`position/currentDirection/velocity/speedFactor`).
3. **Steering**: consume habitat inputs + write `desiredMoveDir/moveDirection/tumble` fields.
4. **Predation**: consume spatial partitioning over packed positions/types.
5. **Lifecycle/Spawn**: final ownership-sensitive conversions (adds/removes/trait mutation paths).
6. **Render adapter**: read-only packed transforms/colors for batching.

## 5) First converted system choice
`ReplicatorMetabolismSystem` is the best first conversion and now runs from packed arrays in its hot loop, then syncs back to object state.

## 6) Minimal compatibility-first implementation
- Keep `Replicator` objects and all extracted systems.
- Add `ReplicatorPopulationState` and pass it into metabolism.
- Metabolism loop reads/writes arrays and only resolves callbacks/deletions through existing object APIs at the boundary.

No broad API breakage, no pipeline ownership changes, and no full rewrite.

## 7) Multi-step-per-frame correctness
`ReplicatorManager` still drives `simulationStepsPerFrame` and calls `TickMetabolism()` each simulation step.

`TickMetabolism()` retains accumulated simulation-time ticking (`metabolismTickTimer += Time.deltaTime`, `while (timer >= tick)`) so behavior at high simulation speeds remains simulation-time based, not wall-clock based.

## 8) Measurement guidance and profiler markers
Added explicit profiler markers in metabolism:

- `ReplicatorPopulationState.SyncFromAgents`
- `ReplicatorMetabolismSystem.HotLoop`
- `ReplicatorPopulationState.SyncToAgents`
- `ReplicatorMetabolismSystem.RemoveDeadAgents`

Suggested compare workflow:
1. Record baseline with Deep Profile **off** and same seed/settings.
2. Compare marker timings over 300–600 frames at representative populations.
3. Run at low (`1`) and high (`>=20`) `simulationStepsPerFrame`.
4. Focus on total `MetabolismTick` cost and HotLoop share.

## 9) Measurement checkpoint playbook (post-current migration)

### Concise benchmark checklist
Use this exact sequence so each checkpoint can be compared apples-to-apples:

1. **Lock scenario**: same scene (`PlanetScene`), same seed/settings, same camera path, same quality level.
2. **Profile in Player or Development build** (not Deep Profile) and capture **300-600 warm frames** after initial spawn settles.
3. Run two operating points:
   - `simulationStepsPerFrame = 1` (real-time behavior)
   - `simulationStepsPerFrame >= 20` (CPU stress / fast-forward behavior)
4. For each run, log:
   - total population and predator count,
   - median + p95 frame time,
   - GC Allocated In Frame,
   - median + p95 of the markers listed below.
5. Repeat each operating point 3 times and compare medians (ignore single-run spikes).

### Existing profiler markers to compare

#### Population-state + metabolism markers (already used for migration proof)
- `ReplicatorPopulationState.SyncFromAgents`
- `ReplicatorMetabolismSystem.HotLoop`
- `ReplicatorPopulationState.SyncToAgents`
- `ReplicatorMetabolismSystem.RemoveDeadAgents`

#### Adjacent system markers for next-step direction
- `ReplicatorMovementSystem.SyncFromPopulationState`
- `ReplicatorMovementSystem.SyncToAgents`
- `ReplicatorSteeringSystem.SyncFromPopulationState`
- `ReplicatorSteeringSystem.HotLoop`
- `ReplicatorSteeringSystem.SyncToAgents`
- `ReplicatorManager.PopulationStateSyncForLocomotion`
- `ReplicatorManager.UpdateScentFields`
- `ReplicatorManager.SkipScentFields.NoPredators` (sanity marker that tells you scent work was skipped)

### Practical interpretation guide (choose the next optimization step)

Use marker share of the simulation frame (not absolute ms alone) as the decision signal.

1. **Choose more SoA migration next** when either is true:
   - any `Sync*` marker family is a large tax versus its system hot loop,
   - the combined `SyncFrom + SyncTo` cost scales faster than population growth.

   This means object<->array marshaling is dominating and more systems should run directly on packed arrays before syncing.

2. **Choose Jobs/Burst on hot loops next** when either is true:
   - `ReplicatorMetabolismSystem.HotLoop` and/or `ReplicatorSteeringSystem.HotLoop` dominates while sync cost is comparatively small,
   - cost rises roughly linearly with agent count and stays compute-bound.

   This indicates loop math itself is the bottleneck, so parallelizing/pushing Burst-friendly kernels gives best ROI.

3. **Choose render-side optimization next** when simulation markers are stable but frame time is still high:
   - simulation markers consume a minority of frame time,
   - render-thread/GPU timelines are heavier than simulation on the same workload.

   In this case simulation migration is no longer first-order; optimize instancing/material/property-block churn and visual workload first.

4. **Choose spawn/lifecycle ownership cleanup next** when removal/spawn churn spikes:
   - `ReplicatorMetabolismSystem.RemoveDeadAgents` shows bursty p95 spikes,
   - spikes correlate with population turnover events rather than steady-state count.

   This suggests ownership/deferred-removal paths are causing uneven frame pacing; move lifecycle/spawn to cleaner ownership boundaries next.

### Fast rule-of-thumb decision matrix
- **Sync heavy** -> continue **SoA migration**.
- **HotLoop heavy** -> do **Jobs/Burst**.
- **Simulation light but frame heavy** -> do **render optimization**.
- **Spiky remove/spawn costs** -> do **lifecycle ownership cleanup**.

## Layer-awareness extension (new)

With introduction of ocean layers, agent state must include:

- `layerIndex`
- `preferredLayerIndex`

All systems operating on spatial locality (movement, steering, predation) must consider (cell, layer) instead of cell alone.

This does not change the migration strategy, only extends the SoA schema.