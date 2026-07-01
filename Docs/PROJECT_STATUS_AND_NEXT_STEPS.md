# Project Status and Next Steps

Status: Current roadmap / implementation audit
Last reviewed: 2026-06-28
Reviewed documents:
- `Docs/SAVE_RELOAD_SIMULATION_STATE_ANALYSIS.md`
- `Docs/LAYERED_OCEAN_PLAN.md`
- `Docs/LAYERED_OCEAN_AUDIT_2026-05-03.md`
- `Docs/PopulationStateMigrationPlan.md`
- `Docs/SimulationRefactorPlan.md`
- `Docs/REACTION_BACKED_METABOLISM_PLAN.md`

## Executive summary

The codebase has moved beyond several assumptions in the older planning documents. Save/load is no longer just a feasibility analysis: a versioned compressed snapshot service exists and captures population, clock, sun, mutable resources, timers, temperature, and authoritative layered-ocean arrays. Layered ocean is implemented across the main simulation loops and now persists through save/load. `ReplicatorPopulationState` is the authoritative hot-path population state, while `List<Replicator>` remains a companion compatibility object list for lifecycle/debug/render bridges and for slower fields that are not yet fully migrated.

The best next branch is **save-load-validation-and-determinism**. It should not start by adding new metabolism behavior. First make snapshot continuation measurable and trustworthy, then remove or tighten compatibility bridges using telemetry, then add phosphorus/nutrient-limited replication, and only after that convert metabolism to true reaction execution.

## 1. Current implemented state

### Save/load

Implemented:
- `SimulationSaveLoadService` writes compressed JSON save files (`*.simv.json.gz`), lists save files, loads the newest save, validates schema/counts, and temporarily disables the simulation pipeline while applying state.
- `SimulationSaveFile` is versioned (`CurrentSchemaVersion = 2`) and includes clock, sun, planet generator summary, resource map snapshot, population snapshot, and diagnostics.
- Population capture serializes hot SoA fields from `ReplicatorPopulationState` plus currently companion-only fields (`maxLifespan`, traits, `biomassTarget`, `locomotionSkill`). Load rebuilds both `agents` and `ReplicatorPopulationState` from snapshots.
- Mutable resource capture/restoration includes CO2, O2, OrganicC, H2S, H2, CH4, S0, DissolvedFe2+, surface temperature when thermal inertia is enabled, dissolved organic leak, toxic proteolytic waste, and resource timers.
- Sun state has snapshot/apply integration through `SunSkyRotator` and the load service.

Assessment against `SAVE_RELOAD_SIMULATION_STATE_ANALYSIS.md`:
- The main snapshot goals are **substantially satisfied for same-scene/same-resolution continuation**.
- It is not yet proven as exact deterministic continuation. The save file does not appear to carry a complete scenario/generator/resource configuration envelope, Unity RNG state, or replay-grade transient state. Validation is currently diagnostic/log-oriented rather than a formal automated save-load parity suite.

### Layered ocean

Implemented:
- Ocean resources are stored in layered arrays for the key mutable ocean resources. Current code includes CO2, O2, OrganicC, H2, H2S, CH4, S0, DissolvedFe2+, DissolvedOrganicLeak, and ToxicProteolyticWaste in layered ocean flows.
- Core agent systems are layer-aware: movement shell placement, steering/habitat reads, predation bins/search, metabolism reads/writes, rendering via population position, spawning layer selection, and lifecycle persistence of current/preferred layer indices.
- Physical/ecological processes are layer-aware: surface oxygenation, vertical diffusion/mixing, marine snow/downward OrganicC transport, lateral spread, limited upward bleed, layer light factors, layer temperature offsets, layered solar heating, and bottom/near-bottom vent heating.
- Layered ocean persistence is implemented: save captures the authoritative layered-or-aggregate arrays and load applies them back to the live layered arrays when lengths/resolution match.

### Population state ownership

Implemented/current ownership:
- `ReplicatorPopulationState` is authoritative for hot simulation fields used by metabolism, steering, movement, lifecycle gating, rendering transforms/colors, cooldowns, starvation counters, O2 toxicity timers, and ocean layer indices.
- `List<Replicator>` remains a companion structure for compatibility, debugging, lifecycle object construction, spawn/reproduction trait handling, and any fields not yet mirrored in SoA.
- Save/load correctly treats `ReplicatorPopulationState` as the primary source for hot fields and captures selected companion-only fields until those are migrated.

### Simulation refactor

Already happened:
- Major ReplicatorManager responsibilities have been extracted into dedicated classes/files: population state, lifecycle, metabolism, movement, steering, predation, rendering, HUD/debug telemetry, spawn system, simulation pipeline, performance analyzer, speed controller, and startup flow.
- `ReplicatorManager` still coordinates these systems and owns significant orchestration/compatibility state.
- `PlanetResourceMap` remains a large central component. It has more explicit APIs and layered/persistence responsibilities, but most planned extraction targets such as separate vent chemistry, atmosphere mixing, oxidation, local diffusion, scent field, temperature service, initializer/state containers, and debug drawer are still future refactors.

### Reaction-backed metabolism

Implemented/current state:
- `ReactionBackedMetabolismScaffolding.cs` defines reaction IDs, reaction definitions, package definitions, metadata flags, runtime bindings, and validation helpers.
- `ReplicatorMetabolismSystem` uses registry-derived runtime bindings for resource identities in the existing branch-driven metabolism paths.

Not implemented:
- Reactions are not the authoritative execution model.
- Stoichiometry values are marked as scaffolding/provisional.
- O2 toxicity, UV protection, inhibition, maintenance cost, running cost, and modular reaction effects are not yet generalized as reaction effects.

### Phosphorus and nutrient-limited replication

Current state:
- `ResourceType.P` and generated phosphorus fields exist in `PlanetResourceMap`/cache.
- Replication gating currently centers on energy/carbon (`canReplicate`, carbon-limited division support) rather than a consumed phosphorus/nutrient budget.

Recommendation:
- Add phosphorus/nutrient-limited replication **before** full reaction-backed metabolism. It is a lifecycle/resource-gating feature that can be validated independently with the existing metabolism model. Reaction-backed metabolism can later consume the same resource access and nutrient-accounting primitives.

## 2. Partially implemented items

- Save/load exactness: functional snapshot capture/apply exists, but deterministic parity and complete configuration/RNG capture remain incomplete.
- Population migration: hot fields are SoA-authoritative, but slower fields (`maxLifespan`, traits, `biomassTarget`, `locomotionSkill`) still depend on the companion object list and snapshot bridge.
- Layered-ocean compatibility pressure reduction: bridges are intentionally retained, but there is not yet a documented threshold or automated gate for removing/tightening them.
- Reaction-backed metabolism: scaffolding and runtime resource binding exist, but actual execution remains branch-driven.
- Simulation refactor: Replicator systems are mostly extracted; PlanetResourceMap extraction is still mostly pending.
- Persistence UI/productization: debug hotkeys/context menu and latest-save load exist, but a full save-slot UI/workflow is not the primary implemented surface.

## 3. Future planned items

- Save/load validation suite with zero-elapsed round trip, resource sum and sampled-cell checks, layered ocean exact-array checks, sun phase checks, population field checks, and one-step deterministic comparisons where feasible.
- Complete save envelope for fresh-scene restore: scenario/startup config, full generator/resource config, relevant random states, and clear version migration behavior.
- Decide and document compatibility bridge retention/removal gates for layered ocean.
- Migrate or formally classify companion-only population fields.
- Add nutrient-limited replication, starting with phosphorus as a generated/static nutrient and later extending to mutable dissolved/available nutrient pools if needed.
- Convert reaction scaffolding into an authoritative reaction executor after resource access, nutrient gating, save/load, and validation are stable.
- Extract PlanetResourceMap subsystems incrementally once tests/diagnostics can protect behavior.

## 4. Stale or superseded assumptions

### `SAVE_RELOAD_SIMULATION_STATE_ANALYSIS.md`

Stale/superseded:
- Save/load is no longer only proposed; schema DTOs and a service exist.
- The plan's suggested APIs are partially realized through `SimulationSaveLoadService`, `ReplicatorManager.CapturePopulationSnapshot`/`ApplyPopulationSnapshot`, `PlanetResourceMap.CaptureSnapshotSummary`/`ApplyMutableResourceSnapshot`, simulation clock snapshots, and sun snapshots.

Still useful:
- Warnings about exact continuation, deterministic config, timers, RNG state, and transient cache rebuilds remain relevant.

### `LAYERED_OCEAN_PLAN.md` and `LAYERED_OCEAN_AUDIT_2026-05-03.md`

Stale/superseded:
- Layered ocean persistence should now be considered implemented, not future work.
- CO2 is now included in layered ocean chemistry and persistence. Atmosphere coupling remains surface/top-layer focused by design.

Still useful:
- The compatibility bridge rationale and tuning/validation checklist remain useful.

### `PopulationStateMigrationPlan.md`

Stale/superseded:
- The statement that `List<Replicator>` remains authoritative in step 1 is no longer the best description of current ownership. Current code treats `ReplicatorPopulationState` as authoritative for hot fields.

Still useful:
- The migration-order logic and benchmark guidance remain useful.

### `SimulationRefactorPlan.md`

Stale/superseded:
- The ReplicatorManager extraction list is no longer just a target; many target classes already exist.

Still useful:
- The PlanetResourceMap extraction plan and caution against behavior-changing refactors remain useful.

### `REACTION_BACKED_METABOLISM_PLAN.md`

Stale/superseded:
- The project has moved from pure documentation into scaffolding and registry/runtime-binding code.

Still useful:
- The conservative direction remains correct: keep `MetabolismType` externally stable while gradually moving execution toward reaction packages.

## 5. Recommended next 5 implementation milestones

### Milestone 1 — Save/load validation and deterministic audit

Goal:
- Make current save/load behavior measurable and safe to rely on.

Scope:
- Add automated or reproducible validation for save→load with zero elapsed time.
- Compare population count and per-agent fields.
- Compare resource sums plus sampled cell/layer values for all persisted resources.
- Verify sun direction/orbit phase and simulation clock restoration.
- Identify what prevents one-step deterministic continuation.

Branch name suggestion:
- `save-load-validation-and-determinism`

Risks before milestone:
- Hidden transient state may affect post-load behavior even if visible state matches.
- Current save envelope may only be robust for same-scene/same-resolution loads.
- Floating-point and update-order differences may make exact one-step comparisons brittle.

### Milestone 2 — Complete snapshot envelope and load robustness

Goal:
- Move from debug snapshot to robust same-version continuation.

Scope:
- Capture authoritative startup/scenario config and enough generator/resource configuration for fresh-scene regeneration.
- Decide whether to store Unity RNG state or own deterministic RNG streams.
- Add clearer schema migration/failure behavior.
- Consider save-slot metadata/UI only after data correctness is stable.

Risks before milestone:
- Generator/cache assumptions could mask missing config fields.
- Saving too much static data may inflate save size; saving too little may prevent fresh-scene restore.
- Schema versioning can become expensive if not defined early.

### Milestone 3 — Layered-ocean bridge telemetry gates and cleanup plan

Goal:
- Keep intentional bridges, but make their use explicit and bounded.

Scope:
- Define acceptable thresholds for aggregate `Get`/`Add` compatibility, read/write fallbacks, and callsite counters.
- Document which bridges are permanent design bridges versus migration-only bridges.
- Add validation scenes or profiler captures for layered gradients and fallback volume.

Risks before milestone:
- Removing bridges too early can break land/non-ocean or debug call paths.
- Keeping bridges forever can hide regressions and make resource accounting ambiguous.
- Telemetry may be disabled by default, so audits need explicit enablement instructions.

### Milestone 4 — Phosphorus/nutrient-limited replication

Goal:
- Add ecological nutrient pressure before changing metabolism execution architecture.

Scope:
- Define whether phosphorus is static availability, mutable dissolved resource, or both.
- Add replication gating/consumption to lifecycle/spawn paths.
- Persist any new mutable nutrient pools.
- Add debug counters for blocked replication by nutrient limitation.

Risks before milestone:
- If phosphorus is static only, it may feel like a habitat mask rather than a nutrient cycle.
- If phosphorus is mutable immediately, resource-map persistence and balancing complexity increase.
- Nutrient gating can collapse populations if tuned before save/load and layered validation are stable.

### Milestone 5 — Reaction-backed metabolism execution phase 1

Goal:
- Convert from branch-owned chemistry toward data-owned reaction execution without changing external identities.

Scope:
- Keep `MetabolismType` as public identity.
- Promote reaction package data from scaffold to authoritative for one low-risk metabolism first.
- Route resource reads/writes through existing layer-aware helpers.
- Add parity tests/diagnostics against the legacy branch for the selected metabolism.
- Only then expand to photosynthesis and special-case toxicity/dark-respiration behavior.

Risks before milestone:
- Photosynthesis and toxicity contain special-case behavior that may not map cleanly into generic reactions initially.
- Stoichiometric changes can accidentally rebalance the whole sim.
- Reaction execution in the hot loop may regress performance unless data layout/allocation is controlled.

## 6. Compatibility bridges that remain and whether they are intentional

Intentional bridges:
- Aggregate `PlanetResourceMap.Get(...)` reads over layered ocean resources, returning an effective value for legacy/debug callers.
- Aggregate `PlanetResourceMap.Add(...)` writes routed into layered arrays for ocean cells.
- `GetResourceForCellLayer(...)` and `AddResourceForCellLayer(...)` fallbacks for invalid/missing layer context, land cells, non-layered resources, and transitional callers.
- Legacy ocean array synchronization from layered arrays for selected resources needed by atmosphere/global/debug consumers.
- Per-callsite compatibility telemetry arrays/counters, optionally enabled for audits.
- `List<Replicator>` companion objects, retained for compatibility, debugging, lifecycle construction, trait fields, and bridge APIs.

Intentional but should be measured:
- Any fallback caused by `MissingPopulationStateSync`, invalid current/preferred layer, or no active ocean layers in an ocean resource path should trend toward zero and should be treated as a bug or migration-pressure signal unless a specific design exception is documented.

## 7. Suggested validation tests

Save/load:
- Save, load immediately, and assert population count matches.
- Save, load immediately, and compare representative per-agent fields: position, direction, velocity, energy, age, metabolism, locomotion, starvation timers, O2 toxicity timer, color, size, current/preferred ocean layer.
- Save, load immediately, and compare resource sums and sampled cell/layer values for CO2, O2, OrganicC, H2S, H2, CH4, S0, DissolvedFe2+, DissolvedOrganicLeak, and ToxicProteolyticWaste.
- Save, load immediately, and verify surface temperature arrays when thermal inertia is enabled.
- Save, load immediately, and verify vent/atmosphere/thermal timers.
- Save, load immediately, and verify sun rotation/orbit state and simulation time.
- Run uninterrupted one step and save-load-one-step; compare deltas and document non-deterministic differences.

Layered ocean:
- For every ocean replicator, assert current layer is valid for its cell.
- Assert movement output radius matches `GetOceanLayerShellRadius(cell, layer)` within tolerance.
- Track top/bottom means for O2, OrganicC, H2S, and S0 under fixed seeds.
- Enable compatibility telemetry and confirm fallback/callsite counters are understood and do not unexpectedly rise.
- Verify aggregate `Add` routes CO2 to top-layer behavior and bottom-injected resources to bottom/appropriate layer behavior as designed.

Population state:
- Assert `ReplicatorPopulationState.Count == agents.Count` after load, spawn, death, and reproduction.
- Add parity checks for companion-only fields until they are migrated or deliberately frozen.
- Ensure death/removal swap-back keeps population arrays and companion list aligned.

Simulation refactor:
- Before each PlanetResourceMap extraction, capture fixed-seed resource sums/time series and compare after extraction.
- Profile warm-frame medians/p95 at `simulationStepsPerFrame = 1` and `>= 20`.

Nutrient-limited replication:
- Validate replication succeeds with sufficient phosphorus and fails with insufficient phosphorus.
- Validate nutrient consumption/accounting is conserved or intentionally replenished.
- Validate save/load preserves any mutable nutrient state and blocked-replication counters if added.

Reaction-backed metabolism:
- Registry validation must pass for every `MetabolismType`.
- For the first migrated metabolism, run legacy and reaction execution in a parity harness with identical resource/cell/layer inputs.
- Verify no per-agent allocations in the hot loop.
- Verify layer-aware resource reads/writes match existing helper behavior.
