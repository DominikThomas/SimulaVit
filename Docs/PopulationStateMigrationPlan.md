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
