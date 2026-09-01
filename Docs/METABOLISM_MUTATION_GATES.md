# Metabolism Mutation Habitat Gates

This note documents the centralized viability gates used when reproduction mutates a child into a different `MetabolismType`.

## Implementation location

Mutation-gate code lives in `Assets/Scripts/Biology/Replicators/ReplicatorManager.cs` near the reproduction mutation helpers:

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

Thresholds are serialized fields on `ReplicatorManager` under **Metabolism Mutation Habitat Gates**. The current final defaults for this validation pass are:

- `mutationGateMinH2S = 0.0005`
- `mutationGateMinCO2 = 0.001`
- `mutationGateMinH2 = 0.001`
- `mutationGateMaxO2ForAnaerobes = 0.02`
- `mutationGateMinO2ForAerobes = 0.01`
- `mutationGateMinOrganicC = 0.001`
- `mutationGateMinCH4 = 0.001`
- `mutationGateMinLight = 0.05`

Resource thresholds are area-normalized through the same local-threshold helper used by existing local O2/OrganicC mutation checks. Each field has an Inspector tooltip describing the resource or habitat condition it gates; `mutationGateMaxO2ForAnaerobes` remains a mutation viability gate only and is not O2 toxicity.

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

Inspect telemetry in any of these ways:

- During play mode, select the object with `ReplicatorManager` and expand **Metabolism Mutation Gate Telemetry** in the Inspector.
- Watch `metabolismMutationGateTelemetry` for total/allowed/blocked counts and per-enum arrays.
- Watch `topMutationGateBlockReason` / `topMutationGateBlockReasonCount` and `topBlockedMutationGateTarget` / `topBlockedMutationGateTargetCount` for the leading block reason and target.
- Read the aggregate line emitted by the throttled metabolism debug log: `Mutation gates: attempts=... allowed=... blocked=...`.

Counters are lifetime counters for the current play/session state:

- `TotalAttempts` increments whenever a reproduction mutation candidate reaches the centralized metabolism gate for a target metabolism.
- `Allowed` increments when the gate passes and the child metabolism is changed to the target.
- `Blocked` increments when the gate rejects the target.
- `AttemptsByTarget`, `AllowedByTarget`, and `BlockedByTarget` are fixed-size arrays indexed by `MetabolismType` integer value.
- `BlockedByReason` is a fixed-size array indexed by `MetabolismMutationGateBlockReason` integer value.
- `topMutationGateBlockReason` / `topMutationGateBlockReasonCount` and `topBlockedMutationGateTarget` / `topBlockedMutationGateTargetCount` summarize the highest blocked reason and target for quick Inspector reads. These two fields are independent summaries.
- `topBlockedMutationGatePairTarget` / `topBlockedMutationGatePairReason` / `topBlockedMutationGatePairCount` summarize the highest blocked target/reason pair from the lightweight `BlockedByTargetAndReason` matrix.

To reset telemetry during play mode, use the `ReplicatorManager` component context menu item **Reset Metabolism Mutation Gate Telemetry**. This replaces the telemetry object, clears top-counter fields, and rebuilds fixed-size arrays to the current enum lengths.

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

## Methanogenesis `TooMuchO2` note

`TooMuchO2` blocks for Methanogenesis are expected behavior. They only block new methanogenesis mutations in oxic habitats. Existing methanogens are handled by runtime O2 inhibition, where O2 reduces metabolism efficiency and usually causes decline through energy/carbon limitation rather than direct death. If methanogenesis disappears globally, first check for anoxic H2 + CO2 refuges and O2 layer transport/mixing before weakening the gate.

## Validation playtest note

The first telemetry playtest after enabling reaction-derived mutation gates showed the gates were active but not over-restrictive: mutation attempts were both allowed and blocked, many attempts remained allowed, Fermentation blocks appeared as `MissingOrganicC`, SulfurChemosynthesis blocks appeared as `MissingH2S`, and later Methanogenesis blocks appeared as `TooMuchO2`. Runtime O2 pressure is now modeled primarily as anaerobe metabolism inhibition rather than normal direct oxidative death.
