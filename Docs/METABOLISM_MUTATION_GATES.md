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
