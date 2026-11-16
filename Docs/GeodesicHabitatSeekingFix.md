# Geodesic habitat-seeking correction

Implemented on `codex/geodesic-biology-parity`. Source inspection and static syntax/whitespace checks only: no Unity, automated tests, builds, simulation runs, benchmarks or parameter sweeps. Runtime ecological results remain for manual validation.

## Confirmed source defect

The former score used `min(H2 / steerGoodH2, CO2 / steerGoodCO2)`, clamped to 0–1, multiplied by metabolic temperature and O2 efficiencies. The saved scene and manager defaults use `steerGoodH2 = 0.4`, `steerGoodCO2 = 0.02`, and absolute improvement/worsening deadbands of `0.05`.

At H2 near `1e-5`, its normalized contribution is only `2.5e-5`, before physiology reduces it. This explains how the reported mean suitability could be `7.8e-6`: the nominally normalized score still occupied a tiny numerical range. A `0.05` absolute deadband is approximately 6,400 times that reported mean. A mean is not a bound on every observation, but the supplied zero improving/worsening counters confirm that none of the observed comparisons crossed the configured deadbands. Vertical classification used the identical incompatible thresholds. The code did not multiply raw H2 and CO2 concentrations together; the defect was the legacy resource scale plus the absolute comparison scale.

This was a defect in the previous implementation's normalization and comparison rule, not evidence that active translation was broken. The correction does not merely lower an absolute epsilon until movement appears responsive.

## Trace of the old and retained systems

| Stage | Source finding |
| --- | --- |
| Local resources | `SampleMovementHabitat` calls `TryGetConcentration` on the authoritative geodesic resource field for H2, CO2 and O2. Values are physical concentrations, not the legacy resource-map quantities. |
| Habitat identity | `GeodesicCellIndex` and `CurrentOceanLayerIndex` select the node. No cube-sphere conversion, vent-coordinate query or candidate-cell resource ranking is involved. |
| Sampling freshness | A movement-sample generation changes every active kinematic batch. A node is sampled once within that batch; chemistry does not change midway through the batch. Reaction scratch/cache generations are separate. |
| Temperature | `Step` refreshes the temperature-cache generation when the authoritative ocean temperature tick changes; physiology reads the current coarse ocean cell/layer. |
| Suitability memory | `GeodesicActiveMovementState` belongs to the packed organism. `CopyEntry` copies the entire struct, capacity growth resizes its array, new slots clear it, and snapshots carry it. Source inspection found no swap-back or initialization defect explaining the all-neutral log. |
| Comparison | Current score was compared with the smoothed previous score before memory was updated, so it was not simply comparing a sample with itself. Absolute 0.05 thresholds hid the small differences. |
| Tumble response | Better/neutral/worse choose existing reference probabilities, then convert them to a probability for actual elapsed sensing time and the locomotion-specific hazard. This remains intact. |
| Sensing cadence | Existing defaults are 0.5 s for amoebae and 0.2 s for flagella, plus jitter and 0.1 s movement quantization. Many repeated senses in one cell are expected. The supplied crossings/comparisons totals have a ratio around 0.11, but are not a direct measurement of per-organism crossings between senses; the new counters measure that directly. |
| Vertical experience | The old runtime did move through actual adjacent layers and compared the post-trial habitat with the starting score. The deadband hid those differences too. However, its delayed sample could include horizontal movement after arrival. The correction freezes actual arrival data immediately. |

## Corrected reaction readiness and comparison

For the current local ocean node, let `V` be `PhysicalLayerVolumeKm3`, and let `needH2` / `needCO2` be the actual configured inventory consumption per 0.5-second biological reference tick:

```text
resourceReferenceTicks = min(H2 * V / needH2, CO2 * V / needCO2)
readiness = resourceReferenceTicks * temperaturePerformance * oxygenEfficiency
score = ln(1 + readiness) / (ln(2) + ln(1 + readiness))
```

Equivalently, the concentration scale for one substrate is its actual per-reference-tick requirement divided by the current physical node volume. No global extrema, neighboring resources, known vent locations or hand-tuned legacy `steerGood*` values are used. The limiting resource combines with physiology; tiny raw substrate concentrations are not multiplied together.

Readiness is local resource opportunity in reference-tick equivalents, before shared competition. It is not a change to the metabolic reaction, its cap, uptake, energy yield or net-energy accounting. Zero required substrate, zero thermal efficiency, complete enabled O2 inhibition, or an invalid habitat gives zero readiness. H2 is omitted if its configured requirement is zero. Nonpositive primary CO2 demand gives zero readiness, matching the existing reaction-demand gate.

The bounded readout maps readiness 0, 0.1, 1 and 10 approximately to scores 0, 0.121, 0.5 and 0.776. It has no hard rich-resource cutoff. A stable small-value `ln(1+x)` approximation avoids loss of tiny observations. Rich display scores compress gradually; steering does not use that compression as its sensitivity limit.

The organism keeps an exponential moving memory of readiness, using the existing `movementSuitabilityMemoryBlend`:

```text
rawReadinessDelta = currentReadiness - rememberedReadiness
normalizedDelta = rawReadinessDelta
    / max(taxisReadinessFloor, currentReadiness, rememberedReadiness)
rawScoreDelta = currentScore - score(rememberedReadiness)
```

The normalized delta is bounded to −1…1. The existing improvement/worsening controls now mean fractional changes in readiness for **geodesic** taxis. Their defaults remain 0.05: a sufficiently detectable change of 5% of the larger experience leaves the neutral band. Legacy steering retains its absolute-score interpretation. For example, readiness 1 → 1.1 yields a normalized delta of about +0.091, independently of the absolute H2 concentration.

`taxisReadinessFloor` defaults to 0.0001 full-reference-tick equivalents. Changes far below that amount fade away instead of turning arbitrary numerical traces into strong taxis. A homogeneous zero-resource region has zero delta and ordinary random exploration. The floor is exposed for deliberate manual tuning; old serialized zero settings receive the new working default automatically.

Better experience still lowers the existing tumble probability, worse experience raises it, and neutral experience uses the existing baseline. Heading changes remain stochastic and continuously applied. Sense timing, swimming speeds, energy costs and propulsion geometry are unchanged.

On the first encounter with the corrected model, old snapshot experience is reinitialized once in the new units. Switching metabolism-aware sensing on/off also reinitializes its experience. This does not reset position, heading, energy, growth, mutation traits, RNG sequence or in-flight vertical travel. New fields reside in the already copied/cleared/resized/snapshotted movement struct. No population/planet save format version is changed.

## Vertical steering

Each trial records origin cell/layer, bounded score and readiness. On actually reaching the adjacent layer centre, it records the actual arrival cell/layer, score and readiness immediately. At the next ordinary sensing event, that frozen pair is compared using the same readiness floor and relative thresholds as horizontal taxis.

Only a recorded valid adjacent-layer pair ending in its intended layer contributes to improving/worsening/neutral vertical outcomes. Pairs inherited from old snapshots without matching origin data are counted as `verticalUnpaired`, then discarded without inventing a favourable or unfavourable experience. Regular temporal memory still updates from the current real habitat; vertical tendency updates from the recorded trial. Better arrivals retain the trial direction with the existing probability; worse arrivals reverse it with the existing probability.

No layer movement, physical effort, layer geometry, free depth transition, energy charge or destination-resource scan is added by this correction. Horizontal or environmental changes *during* a trial can still affect its result; this remains local temporal experience rather than a controlled vertical gradient experiment.

## Diagnostics

Existing aggregate locomotion lines remain. Two cumulative report lines are added at the existing biology reporting cadence:

- `GeodesicTaxisDistribution`: raw bounded-score delta and relative readiness-delta min/mean/max/mean absolute values; an absolute relative-delta histogram (`0`, `<1e-4`, `<1e-3`, `<.01`, `<.05`, `<.25`, `>=.25`); actual unchanged/changed local input samples; same/different cell-layer senses; exact memory-match neutral versus nonzero deadband-neutral comparisons; changed samples hidden in that deadband; per-organism cell crossings between senses (mean, maximum, and 0/1/2+ histogram); mean actual sense interval; thresholds, readiness floor and memory initializations.
- `GeodesicTaxisResponse`: comparison counts, actual tumbles and mean chosen tumble probabilities separately for neutral/improving/worsening experiences; equivalent vertical delta distributions and unpaired trial counts. These distinguish an ineffective classification rule from a classified signal that simply did not trigger a stochastic tumble.

“Unchanged input sample” means identical cell/layer and identical sampled H2, CO2, O2, temperature, performance, O2 efficiency and readiness versus the previous actual sample. This is separate from equality to smoothed memory: an unchanged current sample may still differ from its recent memory. Crossing counters count actual boundary events, including crossing back, rather than inferring distance from just the two endpoint cell IDs.

Detailed tracing is disabled by default. In `ReplicatorManager > Geodesic Active Locomotion > geodesicLocomotion`:

- Enable `detailedTaxisDiagnostics`.
- `maximumTrackedTaxisOrganisms` defaults to two and is capped at four. Automatic selection retains stable IDs while alive; packed index changes do not change the tracked organism.
- Set `trackedTaxisOrganismId` to a positive stable ID to follow only that organism; zero selects automatically. As before, runtime IDs are not retained across save/load.
- `detailedTaxisLogIntervalRealSeconds` defaults to two real seconds and has a one-second minimum. At most three lines per tracked organism can be emitted per interval, even at high acceleration.

`GeodesicTaxisTrace` reports the latest sense, plus the largest absolute delta since the previous report if different: stable ID, locomotion, simulation time, current cell/layer, H2, CO2, O2, temperature/performance, O2 efficiency, current/remembered readiness and score, raw score/readiness deltas, normalized delta, actual thresholds, reference/actual tumble probability, classification, chosen tumble/continue/initialize outcome, crossings and sensing elapsed time. Peak lines deliberately select significant events; use aggregate counts for unbiased frequencies.

`GeodesicVerticalTaxisTrace` retains the latest completed trial outcome for each tracked organism: actual origin/arrival, validity, trial/next vertical direction, before/after score and readiness, deltas, thresholds and classification. Faster trials can overwrite a detailed row within a reporting window; aggregate totals retain every paired comparison.

The caches and statistics are bounded reusable arrays/value types. Readiness conversion is cached once per occupied sampled node per movement batch. Detailed ID access and formatting are optional; formatting occurs only at report boundaries. No per-agent objects, global resource scans, pathfinding or per-sense managed allocations are introduced.

## Manual validation

1. **Known H2 gradient:** retain the accepted environment/vent configuration. Select an active hydrogenotroph near the existing gradient and enable its detailed trace. Across real cell/layer changes, check changing concentrations, readiness, nonzero relative deltas and appropriate classifications. Confirm no steering destination lookup or vent-coordinate target is involved.
2. **Improving runs:** observe repeated positive deltas above the improvement threshold. The reference and actual chosen tumble probabilities should decrease, while many such events continue their runs. Compare the response counters over many events, not one random outcome.
3. **Worsening runs:** observe repeated negative deltas beyond the worsening threshold. Their chosen probabilities and long-run tumble fraction should exceed improving events under the same locomotion/sensing settings. A single worsening event may still continue; it must not be guaranteed to tumble.
4. **Vertical experience:** follow completed trial traces. Origin/arrival layers must differ by exactly one and the arrival must match the intended layer. Verify actual paid depth travel stays smooth. Better/worse pairs should update next vertical direction with the configured probabilities; `verticalUnpaired` should not continually grow in a fresh normal run.
5. **Homogeneous poor region:** follow an organism in locally uniform resource-poor water. Expect mostly identical inputs/exact-zero deltas and baseline stochastic exploration. A zero score must not manufacture a preferred direction; tiny sub-floor traces should remain weak.
6. **Passive comparison:** compare active/passive habitat averages, energy, reproduction and mortality under identical existing environmental settings. Do not change vents, chemistry, costs or mutation settings to force an ecological advantage. Group means are observations, not a causal fitness estimate, and the corrected score is not directly comparable with the old scale.
7. **Acceleration and state continuity:** inspect 1×, 10×, 50× and 100× manually. Check the sensed-crossing histogram for skipped habitats, real-time trace bounds, valid vertical pairs, packed swap-back/removal, births and save/load memory initialization. Compare chosen probabilities for equivalent deltas and sensing intervals. Growth/division rendering, passive wandering, mutation transitions and energy rates should retain their existing behaviour.

## Limits

No ecological success or CPU result is claimed without manual runs. This is stochastic temporal taxis, not vent homing; no local detectable gradient means no directional signal. Readiness uses local node inventory and physiology before competition, not predicted net energy or per-organism uptake under crowding. Its scale consequently follows the actual node volume/requirements of the current model. Vertical trials can include concurrent horizontal/environmental changes. Existing sensing cadence can miss intermediate habitats under extreme custom speeds; the new crossing statistics expose this without silently changing movement. Exact random-stream continuation across save/load remains outside this correction.
