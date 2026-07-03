# Metabolism Mutation Habitat Gates

This note documents the centralized viability gates used when reproduction mutates a child into a different `MetabolismType`.

## Implementation location

Mutation-gate code lives in `Assets/Scripts/ReplicatorManager.cs` near the reproduction mutation helpers:

- `GetMutationGateRequirements(...)`
- `TryGetReactionDerivedMutationGateRequirements(...)`
- `PassesMetabolismMutationGate(...)`
- `HasRequiredLocalResource(...)`
- `GetMutationGateResourceThreshold(...)`

The gate answers a narrow gameplay question: could the child metabolism plausibly operate in the local habitat immediately after mutation? Runtime metabolism and starvation remain responsible for exact consumption, energy gain, and death.

## Reaction-derived inputs

The gate first reads the target metabolism's reaction package from `ReactionDefinitionRegistry` and derives candidate requirements from the first productive reaction's input list. Outputs are ignored, and later maintenance/fallback reactions are not treated as mutation requirements.

Currently reaction-derived productive inputs are available for:

- Hydrogenotrophy: CO2 + H2
- SulfurChemosynthesis: CO2 + H2S
- Photosynthesis: CO2 plus the reaction `RequiresLight` flag
- Saprotrophy: OrganicC + O2
- Fermentation: OrganicC
- Methanogenesis: CO2 + H2
- Methanotrophy: CH4 + O2

## Explicit overlays

Explicit overlays are applied after reaction-derived requirements so the mutation gate remains conservative and audit-friendly:

- SulfurChemosynthesis requires H2S + CO2 and does not require light or O2.
- Fermentation requires OrganicC, but inherited stored OrganicC can satisfy the carbon basis when local OrganicC is low.
- Methanogenesis requires CO2 + H2 and low local O2.
- Photosynthesis requires CO2 and current-layer light; it intentionally does not require O2 from dark aerobic maintenance.
- Methanotrophy requires CH4 + O2.
- Saprotrophy requires OrganicC + O2.
- Hydrogenotrophy, if used as a mutation target, requires H2 + CO2.
- Predation remains gated by existing Saprotrophy-parent and motility rules.

## Tunable thresholds

Thresholds are serialized fields on `ReplicatorManager` under **Metabolism Mutation Habitat Gates**:

- `mutationGateMinH2S = 0.0005`
- `mutationGateMinCO2 = 0.001`
- `mutationGateMinH2 = 0.001`
- `mutationGateMaxO2ForAnaerobes = 0.02`
- `mutationGateMinO2ForAerobes = 0.01`
- `mutationGateMinOrganicC = 0.001`
- `mutationGateMinCH4 = 0.001`
- `mutationGateMinLight = 0.05`

Resource thresholds are area-normalized through the same local-threshold helper used by existing local O2/OrganicC mutation checks.

## Known limitations

- Predation is not reaction-backed with a cheap prey-density resource. The gate therefore preserves the existing Saprotrophy-parent and motility requirements and does not invent a food-web or local prey-density system here.
- The reaction registry identifies the main productive mode by convention as the first ordered reaction in each package. This is sufficient for the current packages but should become explicit metadata if packages later contain multiple productive alternatives.
- The gate does not simulate exact runtime consumption, maintenance fallback, starvation timers, or temperature fitness.

## Follow-up telemetry ideas

- Count failed mutation attempts by target metabolism and failed requirement.
- Track local resource/light values at successful mutation events.
- Add a cheap local prey-density signal before tightening Predation mutation gates.

## Mutation gate telemetry

Mutation gate counters are implemented in `ReplicatorManager` next to the centralized gate helpers. The manager owns a serialized `MetabolismMutationGateTelemetry` object plus compact top-counter fields so the values can be inspected directly on the `ReplicatorManager` component during play mode. The same counters are also copied into `ReplicatorTelemetrySnapshot`, and the throttled metabolism debug log emits one aggregate line in the form `Mutation gates: attempts X allowed Y blocked Z ...`; it does not log per organism.

Counters are lifetime counters for the current play/session state:

- `TotalAttempts` increments whenever a reproduction mutation candidate reaches the centralized metabolism gate for a target metabolism.
- `Allowed` increments when the gate passes and the child metabolism is changed to the target.
- `Blocked` increments when the gate rejects the target.
- `AttemptsByTarget`, `AllowedByTarget`, and `BlockedByTarget` are fixed-size arrays indexed by `MetabolismType` integer value.
- `BlockedByReason` is a fixed-size array indexed by `MetabolismMutationGateBlockReason` integer value.
- `topMutationGateBlockReason` / `topMutationGateBlockReasonCount` and `topBlockedMutationGateTarget` / `topBlockedMutationGateTargetCount` summarize the highest blocked reason and target for quick Inspector reads.

Block reasons mean:

- `None`: no block; used only as the default/success state.
- `MissingH2S`: local hydrogen sulfide is below the sulfur chemosynthesis mutation threshold.
- `MissingCO2`: local carbon dioxide is below the target metabolism's mutation threshold.
- `MissingH2`: local hydrogen is below the target metabolism's mutation threshold.
- `MissingOrganicC`: local organic carbon is below threshold, and fermentation's inherited-store fallback did not satisfy the gate.
- `TooMuchO2`: local oxygen is above the anaerobe maximum, currently used by methanogenesis.
- `MissingO2`: local oxygen is below the aerobic target threshold, currently used by saprotrophy and methanotrophy gates.
- `MissingCH4`: local methane is below the methanotrophy threshold.
- `MissingLight`: local layer light is below the photosynthesis threshold.
- `MissingReactionDefinition`: the target had no reaction-derived definition available before explicit gate evaluation could classify it.
- `UnsupportedTransition`: the target metabolism or local cell resolution could not be classified as a supported gate.
- `PredationGateUnavailable`: predation has no reaction/resource-backed gate and remains handled by its existing parent/motility limitations.
- `PredationGateFailed`: a predation mutation roll reached the preserved predation gate, but the existing predation requirements failed.

### Playtest checklist

- In dark deep layers, Photosynthesis mutation attempts should mostly block with `MissingLight`.
- Away from vents, SulfurChemosynthesis mutation attempts should block with `MissingH2S`.
- In low-organic early worlds, Fermentation and Saprotrophy mutation attempts should block with `MissingOrganicC`.
- In oxic zones with no methane, Methanotrophy mutation attempts should block with `MissingCH4`.
- In high-O2 zones, Methanogenesis mutation attempts should block with `TooMuchO2`.
- Near vents with H2 + CO2 and low O2, Methanogenesis should be allowed when attempted.
- Near lit CO2-rich layers, Photosynthesis should be allowed when attempted.
