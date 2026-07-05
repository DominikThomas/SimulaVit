# Environmental Pressure Model

## 1. Core principle

Environmental factors should usually change metabolism efficiency first. Lower efficiency then reduces useful energy gain and carbon gain, which suppresses replication, increases starvation pressure, and creates competitive selection. Direct death is reserved for extreme cases where an environment is beyond plausible physiological tolerance.

## 2. Mutation gates vs runtime inhibition

- **Mutation gates** decide whether a newly mutated metabolism is plausible in the child organism's current habitat. They are local viability checks at mutation time, not ongoing death rules.
- **Runtime metabolism inhibition** affects organisms that already exist. Local environmental pressure lowers the useful throughput of their metabolism, which then propagates through energy, carbon, replication, starvation, and selection.
- **Direct damage/death** is retained only for severe or optional extremes, such as configured direct oxidative damage or broad lethal temperature extremes.
- **Movement/taxis response** remains primitive temporal taxis. Motile organisms compare the habitat they are currently experiencing against remembered previous suitability; they do not scan all neighboring cells and pick the best one.

## 3. O2 model

The mutation gate `TooMuchO2` can block new Methanogenesis mutation attempts in oxic habitats. This is expected because methanogenesis is modeled as an anaerobic H2 + CO2 pathway and should arise preferentially in low-O2 refuges.

That mutation gate only blocks new methanogenesis mutations. Existing methanogens and other configured O2-sensitive anaerobic metabolisms are handled by runtime O2 inhibition: local layered O2 lowers metabolism efficiency, and the resulting energy/carbon shortfall should normally lead to replication failure, starvation, or competitive loss rather than immediate death.

Default runtime O2 efficiency behavior is per metabolism:

- **Methanogenesis** is strongly O2-inhibited and can fall to zero useful efficiency in high O2.
- **Fermentation** is weakly/moderately O2-inhibited as a gameplay simplification and is not directly killed by O2 by default.
- **Hydrogenotrophy** has configurable moderate O2 inhibition when treated as an anaerobic H2 + CO2 metabolism.
- **SulfurChemosynthesis** has mild/configurable O2 inhibition because sulfur metabolism should eventually be split into aerobic and anaerobic pathways.
- **Methanotrophy** is not O2-avoiding; it requires CH4 + O2 and uses its separate methane oxidation handling.
- **Saprotrophy, Predation, and Photosynthesis** are not part of the anaerobe O2 inhibition path.

Optional direct oxidative damage remains available through direct-damage settings, but it is disabled by default and should represent extreme cases rather than normal oxygen-crisis ecology.

## 4. Temperature model

Temperature efficiency is implemented as a runtime metabolism-efficiency curve. Organisms retain `optimalTempMin`, `optimalTempMax`, and `lethalTempMargin`, but metabolism interprets them as inputs to a smooth efficiency response around the preferred temperature range.

Efficiency is near full at the inferred optimum, falls smoothly on both cold and hot sides, and is clamped between zero and one. The cold side has a broader falloff with a small dormancy floor, while overheating becomes stressful faster. The efficiency multiplier applies to useful metabolism performance across current metabolisms, so marginal temperatures primarily reduce energy/carbon gain and replication success.

Direct temperature death is reserved for broad extremes. Slightly outside the old optimal range should reduce efficiency rather than instantly kill. Debug fields include `debugAverageTemperatureEfficiency`, `debugTemperatureLimitedCount`, `debugTemperatureHeatDamageCount`, and `debugTemperatureColdDormantCount`.

## 5. Movement / taxis model

Movement remains temporal run-and-tumble. Organisms do not scan all neighboring cells, enumerate destinations, or choose the best adjacent cell. Instead, motile organisms keep moving along a persistent direction, periodically evaluate current experienced suitability, and compare it with remembered previous suitability.

Suitability combines temperature and metabolism-specific resource terms. O2-sensitive anaerobes move away from O2 only indirectly because O2 lowers their metabolism suitability. Methanotrophs can seek O2 + CH4 because O2 is beneficial to their metabolism when methane is available. Photosynthesizers seek light, CO2, and tolerable temperature rather than avoiding oxygen globally.

## 6. Future UV model

UV is not implemented yet. When added, UV should mainly affect replication fidelity, mutation pressure, and DNA/RNA damage. High UV may reduce replication success; extreme UV may directly damage or kill. UV pressure should be attenuated by water depth, clouds, haze, ice, atmosphere, pigments, or comparable shielding.

## 7. Debug and telemetry

Relevant Inspector/debug fields include:

- mutation-gate attempts, allowed counts, and blocked counts
- top blocked mutation-gate reason and target
- top blocked mutation-gate target/reason pair when pair telemetry is present
- O2 inhibition counters such as inhibited count, average inhibition, and stressed average local O2
- optional direct O2 damage counters and direct O2 kill counters
- temperature-efficiency counters such as average efficiency, limited count, heat damage count, and cold dormant count
- movement suitability/tumble debug counters when run-and-tumble debug is enabled

## 8. Known limitations

- Bottom-layer oxygenation depends on O2 transport/mixing and remains a separate issue.
- `topBlockedReason` and `topBlockedTarget` are independent summaries; use the top blocked target/reason pair when pair telemetry is available.
- Sulfur metabolism should eventually be split into aerobic and anaerobic sulfur pathways.
- Fermentation O2 sensitivity is a gameplay simplification.
- UV is not yet implemented.
