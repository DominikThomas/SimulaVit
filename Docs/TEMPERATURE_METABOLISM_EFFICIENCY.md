# Temperature Metabolism Efficiency

Temperature ranges are now treated primarily as metabolism-efficiency curves rather than hard survival boxes. Each organism keeps its existing `optimalTempMin`, `optimalTempMax`, and `lethalTempMargin` traits, but runtime metabolism interprets those values as the center and width of a smooth efficiency response.

## Efficiency curve

The current helper infers the organism optimum from the midpoint of the optimal min/max range. Efficiency is near `1.0` at that optimum, falls smoothly on both sides, and is clamped to `[0, 1]`. The cold side uses a broader falloff, while the hot side uses a narrower falloff so overheating becomes stressful faster. A tiny dormancy floor can keep very cold organisms at low but non-negative throughput until they reach the broad zero-efficiency edge.

## Metabolism, starvation, and replication

The temperature efficiency multiplier is applied to useful metabolism performance for all current metabolisms: Hydrogenotrophy, SulfurChemosynthesis, Photosynthesis, Fermentation, Methanogenesis, Methanotrophy, Saprotrophy, and Predation. Lower efficiency means lower energy and carbon gain, so reproduction slows or stops because organisms fail to accumulate the energy and biomass thresholds for division.

If efficiency falls below the replication minimum, the organism is marked unable to replicate for that tick. This replaces the previous lifecycle-side hard check that only allowed division inside the old optimal range. Starvation and energy depletion are therefore the normal consequence of mild cold or mild heat.

## Direct temperature death

Direct temperature death is reserved for broad extremes. Slightly outside the old optimal range no longer kills immediately. When direct temperature damage is enabled, the runtime only uses the existing temperature death causes once the local temperature crosses conservative extreme cold or heat thresholds, with a fallback based on the organism range plus an expanded lethal margin.

For Earth-like water-carbon life, the default extreme heat threshold is 423.15 K (150 °C). Future chemistry modes can override these assumptions by changing the settings.

## Debug inspection

During play mode, inspect the `ReplicatorManager` debug fields:

- `debugAverageTemperatureEfficiency`: average metabolism temperature efficiency for the latest metabolism tick.
- `debugTemperatureLimitedCount`: organisms below `0.5` efficiency.
- `debugTemperatureHeatDamageCount`: organisms in the configured severe heat band.
- `debugTemperatureColdDormantCount`: organisms near the cold dormancy floor.

The throttled metabolism debug log also includes the average efficiency and these aggregate counts in the temperature line. There is no per-agent temperature spam.

## Known limitations

- The model is a compact first-pass curve, not a detailed enzyme kinetics simulation.
- Temperature stress does not yet have a dedicated persisted timer; direct death is immediate only at configured extremes.
- Existing temperature mutation/adaptation traits are preserved, but no new adaptive evolution dimensions are added here.
