# Metabolism-Aware Temporal Taxis

Active replicator movement remains a temporal run-and-tumble behavior. Motile organisms do **not** scan neighboring cells, enumerate candidate destinations, or choose the best local cell. They continue moving in their current persistent direction, periodically sample the habitat they are currently experiencing, and compare that current suitability against their remembered previous suitability.

## Decision model

At each sensing interval, amoeboid and flagellum locomotion compute a scalar movement suitability for the current cell/layer. The score combines the existing temperature suitability with metabolism-specific resource availability. The remembered previous score is smoothed with `movementSuitabilityMemoryBlend`, then the temporal delta drives persistence:

- If current suitability improved by at least `movementSuitabilityImprovementThreshold`, tumble probability is lowered toward `movementTumbleChanceWhenBetter`.
- If current suitability worsened by at least `movementSuitabilityWorseningThreshold`, tumble probability is raised toward `movementTumbleChanceWhenWorse`.
- If the delta is within the neutral band, tumble probability uses `movementTumbleChanceNeutral`.

The movement job still applies the existing movement cost/speed pipeline, surface/ocean constraints, and persistent desired direction. This feature only changes the scalar used by the existing temporal comparison.

## Metabolism-aware suitability inputs

The food/resource term is metabolism-specific:

- Sulfur chemosynthesis: H2S + CO2.
- Hydrogenotrophy: H2 + CO2.
- Photosynthesis: layered light + CO2.
- Saprotrophy: OrganicC + O2.
- Fermentation: OrganicC.
- Methanogenesis: H2 + CO2.
- Methanotrophy: CH4 + O2.
- Predation: preserves the existing O2 + dissolved-organic-leak scent steering behavior when scent fields are enabled.

Temperature remains part of the same scalar suitability score, preserving the existing tendency to tumble away from too-hot or too-cold regions by making those current experiences score worse than the remembered run.

## O2 inhibition is indirect

O2-sensitive anaerobes do not have a special “fear oxygen” movement rule. For metabolisms configured as anaerobe-O2-sensitive, the movement score multiplies metabolism resource suitability by the same style of O2 inhibition efficiency used by metabolism runtime settings:

- Methanogenesis can score poorly in high-O2 layers because its reaction efficiency can fall to the configured minimum.
- Fermentation still primarily follows OrganicC; O2 only matters through its configured inhibition floor.
- Hydrogenotrophy follows H2 + CO2, with O2 pressure only through configured inhibition.
- Sulfur chemosynthesis follows H2S + CO2, with mild/configurable O2 inhibition.

Aerobic or oxygen-producing metabolisms do not globally avoid O2. Methanotrophs can treat O2 as beneficial when CH4 is present, and photosynthesizers respond primarily to light, CO2, and temperature rather than fleeing oxygen.

## What this does not change

This is not bottom-layer oxygen propagation, vertical chemistry transport, or a layer-mixing fix. If deep or bottom layers are not receiving O2, metabolism-aware temporal taxis will not create that oxygen. Layer mixing and O2 propagation remain separate follow-up work.

## Tuning

Key inspector fields live under the run-and-tumble movement settings:

- `activeMovementMetabolismSuitabilityEnabled`
- `movementSuitabilityMemoryBlend`
- `movementSuitabilityImprovementThreshold`
- `movementSuitabilityWorseningThreshold`
- `movementTumbleChanceWhenWorse`
- `movementTumbleChanceWhenBetter`
- `movementTumbleChanceNeutral`

Anaerobe O2 response is tuned with the existing anaerobe O2 inhibition settings, including comfort/stress O2 thresholds, per-metabolism minimum efficiencies, and per-metabolism enable flags.

## Verification in play mode

Enable run-and-tumble debug to inspect aggregate windowed counters. Useful scenarios:

1. Put methanogenesis or hydrogenotrophy in a gradient where O2 increases along the current run. They should increase tumble probability after their experienced suitability worsens.
2. Put fermentation in OrganicC-rich oxic water. It should still value OrganicC, with only the configured O2 inhibition reducing the score.
3. Put methanotrophs where CH4 and O2 overlap. They should be able to continue runs into that overlap rather than treating O2 as bad.
4. Put photosynthesizers in lit CO2-rich water. They should respond to light/CO2/temperature, not O2 avoidance.
