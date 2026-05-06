# Reaction-Backed Metabolism Migration Plan

## 1. Motivation

The current metabolism implementation is effective but heavily branch-driven (`if/else` and `switch` on `MetabolismType`) inside a single hot loop. This makes it harder to:

- add new metabolism behaviors without growing branch complexity,
- represent shared biochemical motifs (respiration, storage, toxicity response) consistently,
- move oxygen toxicity / UV / pigments into modular effects,
- validate stoichiometric assumptions in one place.

A reaction-backed model addresses this while preserving simulation stability by **keeping `MetabolismType` as the external identity in Phase 1** and mapping each type to a fixed package of reaction definitions.

This document is intentionally conservative and documentation-only: no simulation behavior changes are proposed for immediate implementation.

---

## 2. Current codebase metabolism flow

### 2.1 High-level control flow

- `ReplicatorManager` owns the metabolism tick cadence (`metabolismTickSeconds`) and builds `ReplicatorMetabolismSystem.Settings` from inspector/config fields before invoking the metabolism system tick.  
- `ReplicatorMetabolismSystem.MetabolismTick(...)` iterates all agents in `ReplicatorPopulationState`, computes temperature/performance, then dispatches per-metabolism behavior via explicit enum branching.  
- Per-agent starvation/toxicity timers are updated in the same hot loop; deaths are resolved from timer/energy outcomes.

### 2.2 Enum-centric behavior today

`MetabolismType` is currently the authoritative metabolism identity and contains:

- `SulfurChemosynthesis`
- `Hydrogenotrophy`
- `Photosynthesis`
- `Saprotrophy`
- `Predation`
- `Fermentation`
- `Methanogenesis`
- `Methanotrophy`

Each branch in `ReplicatorMetabolismSystem` directly performs:

- environmental resource reads,
- capped intake calculations,
- resource writes (layer-aware when possible),
- energy gains/losses,
- store updates,
- starvation/toxicity timer updates.

### 2.3 Data/state currently involved

- **Replicator-level identity/state**: `Replicator.metabolism`, per-agent starvation fields, O2 toxicity fields, replicate gating fields.
- **SoA hot state**: `ReplicatorPopulationState` stores `Metabolism[]`, `Energy[]`, `OrganicCStore[]`, starvation arrays, `O2ToxicSeconds[]`, `O2ComfortMax[]`, `O2StressMax[]`, and layer indices.
- **Environment/resources**: `PlanetResourceMap` and `ResourceType` define available resources and layer-aware/fallback write telemetry.

### 2.4 Existing O2 toxicity and special cases

- O2 toxicity is already represented by `DeathCause.O2_Toxicity` and per-agent timers (`o2ToxicSeconds` / `O2ToxicSeconds[]`), but logic is still metabolism-branch specific and not generalized as reaction effects.
- Photosynthesis has dedicated helper flow (`ProcessPhotosynthesisMetabolism`, dark aerobic respiration, dark anoxic fallback) and debug counters, showing the current pattern of “feature = special branch code”.
- Anoxygenic-like behavior currently exists as a photosynth dark fallback path, but it is encoded procedurally, not as explicit reactions.

---

## 3. Target model

### 3.1 Phase-appropriate architecture

- **Keep `MetabolismType` in place initially** for spawning, mutation, UI/HUD labels/colors, telemetry bucketing, and compatibility.
- Add a reaction execution layer where each metabolism enum maps to a **fixed, immutable reaction package**.
- In Phase 1, packages are static and mirror existing behavior. No freeform chemistry parser.
- Later phases may allow replicators to own package references directly, but not in this first migration.

### 3.2 Reaction-centric execution

Each reaction definition should encode (explicitly, data-first):

- inputs and outputs (resource deltas),
- energy delta,
- running cost (activity-dependent),
- maintenance cost (baseline burden),
- environmental modifiers (light, temperature, layer context, concentration saturation),
- inhibition/toxicity modifiers (e.g., O2 inhibition or toxicity pressure).

Execution remains deterministic and bounded: no arbitrary graph search, no user-authored chemistry strings, no unconstrained dynamic network mutation.

---

## 4. Core data structures

> These are conceptual structures for planning; names may change during implementation.

1. **ReactionDefinition**
   - Stable identifier
   - Fixed input/output stoichiometry (`ResourceType` + coefficients)
   - Base energy delta
   - Base running/maintenance costs
   - Modifier slots (environment + inhibition/toxicity)

2. **ReactionPackageDefinition**
   - Package identifier
   - Ordered list of `ReactionDefinition` references
   - Package-level policy flags (e.g., replicate gating behavior under fallback conditions)

3. **MetabolismReactionBinding**
   - Mapping table: `MetabolismType -> ReactionPackageDefinition`
   - Versioned for migration safety and A/B comparison.

4. **ReactionExecutionContext**
   - Per-agent context snapshot for one tick: cell/layer, temperature, light, resource handles, performance scalar, dt, current stores/timers.

5. **ReactionExecutionResult**
   - Aggregated resource deltas, energy delta, store deltas, timer delta intents, and derived death-cause signals.

### Likely file/class touch points when implementation begins

- `Assets/Scripts/ReplicatorMetabolismSystem.cs` (execution engine insertion point)
- `Assets/Scripts/ReplicatorPopulationState.cs` (timer/state fields consumed by reactions)
- `Assets/Scripts/ReplicatorData.cs` (metabolism enum + death causes)
- `Assets/Scripts/PlanetResourceMap.cs` (resource access/write paths + telemetry compatibility)
- `Assets/Scripts/ReplicatorManager.cs` (settings assembly, tick cadence, debug outputs)
- `Assets/Scripts/ReplicatorHudPresenter.cs` (still enum-facing in early phases)

---

## 5. Phase 1 migration plan

**Objective:** Back existing metabolisms with fixed reaction packages while preserving current behavior envelope.

1. **Introduce reaction definitions and package binding table**
   - One package per existing `MetabolismType`.
   - Preserve current stoichiometries and thresholds as closely as possible.

2. **Build reaction executor inside metabolism tick path**
   - Keep current hot-loop structure and SoA arrays.
   - Replace per-metabolism branch math with package execution calls incrementally.

3. **Maintain existing external interfaces**
   - `MetabolismType` remains authoritative identity for spawn/mutation/UI/telemetry.
   - Existing debug counters and death-cause reporting remain available.

4. **Paritization strategy**
   - Use side-by-side validation mode (old branch outputs vs reaction outputs) where feasible.
   - Migrate one metabolism package at a time in low-risk order (e.g., hydrogenotrophy/sulfur first, then saprotrophy/fermentation/methanogen/methanotroph, then photosynthesis last due to special fallback logic).

5. **No intentional macro behavior changes**
   - Keep population-level outcomes within expected noise bounds.
   - Defer balancing changes to explicit post-migration tuning.

---

## 6. Phase 2 O2 toxicity integration

**Objective:** Move O2 toxicity from metabolism-branch logic into reaction-level inhibition/toxicity effects.

- Model O2 toxicity as reaction modifiers that can:
  - reduce effective throughput,
  - increase maintenance burden,
  - increment toxicity timers under high O2,
  - optionally gate replication.
- Preserve existing death endpoint (`DeathCause.O2_Toxicity`) and timer semantics in `ReplicatorPopulationState` during transition.
- Keep compatibility with manager-level death-cause accounting and debug reporting.

Conservative rule: first replicate current toxicity thresholds and time accumulation behavior before introducing richer kinetics.

---

## 7. Phase 3 detox/protection reactions

**Objective:** Represent detox/protection as explicit reactions instead of scattered conditionals.

Possible patterns (fixed, non-freeform):

- O2 detox reaction package components that consume reducing equivalents / substrates to reduce toxicity pressure.
- Protection reactions that trade energy or stored carbon for lower effective toxicity.
- Optional byproduct emissions mapped into existing `ResourceType` channels where appropriate.

Constraints:

- Keep deterministic bounded cost per tick.
- Avoid introducing novel resource classes unless clearly required.
- Reuse existing timers and death-cause pipeline until a dedicated toxicity-state model is justified.

---

## 8. Phase 4 anoxygenic photosynthesis reactions

**Objective:** Replace hardcoded photosynthesis fallback branching with explicit reaction alternatives.

- Define oxygenic and anoxygenic photosynthetic reactions as separate definitions inside the photosynthesis package (or adjacent package variants), selected by environmental conditions/modifiers.
- Express current dark/anoxic fallback concepts as explicit low-yield reactions with clear inputs/outputs, energy yield constraints, and replication constraints.
- Maintain compatibility with existing photosynthesis unlock mechanics and mutation gating logic initially (still enum-driven externally).

---

## 9. Future UV/pigment direction

UV and pigment behavior should evolve as reaction-level modifiers/effects rather than enum branch checks:

- UV stress as an environmental modifier increasing damage/toxicity load.
- Pigment/protection reactions that reduce UV impact at metabolic cost.
- Potential coupling to light-dependent reaction efficiency tradeoffs.

This should remain **future work** and out of Phase 1 scope.

---

## 10. Performance constraints

Given current architecture and profiler instrumentation:

- Preserve a single-pass, per-agent hot loop profile similar to `ReplicatorMetabolismSystem.HotLoop`.
- Keep allocations out of tick path (no per-agent dynamic collections).
- Prefer compact fixed-size reaction arrays or prebuilt package lookup tables.
- Preserve layer-aware resource write behavior and existing fallback telemetry hooks.
- Avoid expensive indirection or runtime parsing in inner loops.

Reaction-backed execution is acceptable only if it remains within similar or better frame-time budget versus current branch implementation.

---

## 11. Non-goals

- No arbitrary chemistry grammar/parser.
- No free mutable metabolic network editor/runtime for this migration.
- No immediate replacement of `MetabolismType` in UI, spawn/mutation, or telemetry.
- No large rebalance of ecological outcomes during Phase 1.
- No UV/pigment full implementation in this phase.

---

## 12. Risks and validation checklist

### 12.1 Key risks

- **Behavior drift risk:** subtle stoichiometry/order differences can alter long-run ecology.
- **Performance regression risk:** over-generalized reaction execution may slow hot loop.
- **Telemetry mismatch risk:** existing death-cause/fallback counters may lose comparability.
- **Layered-resource correctness risk:** reaction abstraction could bypass current layered write safeguards.

### 12.2 Validation checklist (implementation-phase acceptance gates)

1. **Behavior parity (short horizon)**
   - Per-metabolism resource flux signs and rough magnitudes match baseline scenarios.
2. **Behavior parity (long horizon)**
   - Population composition trends remain within predefined tolerance windows.
3. **Toxicity parity**
   - O2 toxicity death timing/frequency comparable before richer toxicity kinetics are introduced.
4. **Performance**
   - Hot-loop timings and allocation profile meet or beat current baseline.
5. **Telemetry continuity**
   - Existing debug and death-cause reporting remain interpretable across migration.
6. **Layered ocean correctness**
   - No regressions in resource writes across valid/invalid layer states and fallback paths.
7. **Photosynthesis special-case parity**
   - Light mode, dark aerobic mode, and anoxic fallback mode outputs preserved in Phase 1-equivalent behavior.

---

## Appendix: Current code anchors for migration planning

- Metabolism enum and death causes: `Assets/Scripts/ReplicatorData.cs`
- Hot-loop metabolism execution and specialized paths: `Assets/Scripts/ReplicatorMetabolismSystem.cs`
- SoA agent-state fields/timers used by metabolism: `Assets/Scripts/ReplicatorPopulationState.cs`
- Resource types, layered-ocean write/fallback telemetry: `Assets/Scripts/PlanetResourceMap.cs`
- Tick cadence/settings assembly and debug handoff: `Assets/Scripts/ReplicatorManager.cs`
- HUD still presenting enum categories: `Assets/Scripts/ReplicatorHudPresenter.cs`

