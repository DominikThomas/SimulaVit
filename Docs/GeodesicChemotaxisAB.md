# Manual geodesic chemotaxis A/B experiment

This adds one steering control and read-only experiment telemetry. It is not a simulation runner. Unity, Play mode, simulations, automated tests, builds, benchmarks and parameter sweeps have not been run by Codex; runtime results remain for manual evaluation.

## Inspector controls

On `ReplicatorManager`, under **Geodesic Active Locomotion**:

- **Enable Geodesic Chemotactic Steering** (`enableGeodesicChemotacticSteering`): defaults to **true**. True selects A; false selects B.
- **Enable Geodesic Chemotaxis AB Diagnostics** (`enableGeodesicChemotaxisABDiagnostics`): defaults to **false**. Enable in both experimental runs before starting the simulation.

No suitable Advanced startup switch already existed. The older `activeMovementMetabolismSuitabilityEnabled` switch selects metabolism-aware versus temperature-only sensing; it does **not** disable environmental feedback. Keep it enabled in both runs for this metabolism-aware comparison. The new toggle is Inspector-only, not a duplicated startup configuration field or inherited/evolvable trait.

## Exactly what differs

| Behaviour | A: feedback enabled | B: feedback disabled |
| --- | --- | --- |
| Local sensing, suitability and memory | Existing behaviour | Still evaluated and recorded for observational comparison |
| Horizontal reference tumble probability | Existing improving / neutral / worsening policy | Always the existing neutral probability after initial sensing |
| Sensing cadence, jitter, probability limits, time-hazard conversion | Existing values | Identical values |
| Amoeboid noise, turn angles/rates and flagellar persistence multiplier | Existing values | Identical values |
| Completed paired vertical trial | Existing better / worse / neutral persistence policy | Existing neutral policy: 50% retain direction, 50% reverse |
| Vertical trial timing, adjacent-layer constraints and paid travel | Existing behaviour | Identical behaviour |
| Speeds, propulsion costs, basal costs, metabolism, temperature physiology, reproduction, mortality, mutation and inheritance | Existing behaviour | Identical rules and settings |
| Passive wandering and anchored organisms | Existing behaviour | Unchanged |

B uses the current `movementTumbleChanceNeutral`, not a new tuned probability. With reference probability `p`, the existing decision probability over a sensing interval `t` remains `1 - (1 - p)^(t / referenceSeconds * locomotionHazardMultiplier)`. Initial sensing still initializes memory without tumbling. All subsequent random exploration remains stochastic; coast and habitat-validity constraints remain active.

This matches intrinsic swimming and baseline exploration parameters, not realized trajectories or an artificially matched number of tumbles. Environmental feedback is intended to change persistence and therefore can change observed tumble frequency, distance, boundary encounters, energy acquisition and population composition. The experiment reports actual tumbles per active organism-second and movement measures so that these differences remain visible.

In B, classifications may still report improving or worsening habitat, but those labels do not select the horizontal tumble probability or vertical persistence decision. Resource-dependent energy availability can still affect whether an organism can afford movement; its normal metabolism and energy accounting remain enabled in both modes.

## Reporting and definitions

The diagnostics reuse the existing approximately ten-simulated-second biology reporting boundary. `simTime`, `windowStart` and the actual `windowSeconds` are reported; a boundary can overshoot its target by a normal simulation step. Empty populations continue reporting. No real-time timer drives A/B aggregation.

Every line starts with `[GeodesicChemotaxisAB]` and includes simulation time, A/B mode, `mixedWindow`, and `runModeSwitches`. There are six lines per report: one population/movement summary, four locomotion-group summaries, and one grouped comparison summary. While enabled, these replace the five existing locomotion/taxis summary lines; the other biology, temperature, vent-profile and accounting reports retain their existing controls. Ordinary runs add no A/B output. Existing detailed individual tracing stays bounded, opt-in, and disabled by default.

### Population and movement (`kind=population`)

- Concrete planet seed and derived biology seed; total population and passive/amoeboid/flagellar/anchored snapshot counts.
- Births/deaths **during the window**, starvation (`EnergyDepletion`), and every nonzero death cause. No listed causes means no observed deaths.
- Current mean finite energy and stored organic C; total and active organism-seconds.
- Resource-limited reaction events: window count, runtime cumulative count and count per organism-second.
- Active distance, horizontal crossings, vertical transitions, movement expenditure and tumbles: window amounts, runtime cumulative counters and rates per active organism-second.
- Improving/worsening/neutral steering-memory classifications in the window; invalid or unsupported comparison counts, initialization events and unpaired vertical trials.
- `zeroReadinessExperiencedPairs` and `subFloorExperiencedPairs` identify valid poor/weak local experiences separately from invalid sensor data. They describe consecutive actual samples; smoothed memory may still contain an earlier stronger experience.
- Total ring counts, percentages and organism-time-weighted residence percentages.

`resourceLimited` retains its existing event definition: an evaluated reaction with positive desired extent but insufficient shared node inventory. Counts depend on reaction cadence, which remains unchanged between modes. Distance retains the existing physical geodesic distance calculation. “Starvation” retains the existing energy-depletion cause definition; it does not identify one specific missing substrate.

### Habitat and normalized outcomes (`kind=locomotion`)

Each of passive, amoeboid, flagellum and anchored reports:

- Snapshot population; number of valid hydrogenotroph habitat samples; mean local H2, temperature performance and metabolism-aware suitability for those samples.
- Current mean finite organism energy and organic C across that locomotion group.
- Window organism-seconds, gross metabolic energy awarded from achieved reaction extents, and awarded energy per organism-second. This is production before maintenance/movement/reproduction costs, not net energy or a new reaction formula.
- Parent reproduction events, deaths, and each count divided by the group's organism-seconds. A birth is attributed to its **parent's** locomotion, even if the daughter mutates; this avoids treating a new locomotion phenotype as having reproduced at its own birth.
- Ring counts/percentages and residence percentages for that group.

Organism-time is folded into the existing lifecycle pass: each organism alive over the current authoritative step contributes `dt` once, before mortality/removal or reproduction. Daughters start contributing on their next step. Initial/deferred founders are population initialization, not reproduction events. Residence assigns each step's time to the authoritative habitat after its movement update; this is a small-step quadrature, not exact within-step boundary timing.

Empty denominators produce `NaN`, not a claimed zero fitness or zero habitat value. Delta summaries separately show their comparison sample count. Time-normalized rates address changing population sizes, but are not controlled-age cohort survival estimates. To combine windows, sum event counts/energy and organism-seconds, then divide; do not average window rates without weighting.

### Vent rings: existing definition retained

The measurements read `GeodesicBiologyTemperatureTelemetry`'s already-built map. It is the minimum number of **ocean-cell neighbour hops from any active submarine outlet cell**, using the existing initialization rules, not metric distance and not a bottom-layer-only node metric.

Ring order is `0 / 1 / 2 / 3–4 / 5+ / unreachable / invalid`:

- Ring 0 includes valid ocean layers in an outlet's cell column; it does not imply the organism occupies the vent's injection depth.
- `unreachable` is the existing disconnected-ocean/no-path category.
- `invalid` is a separate diagnostic bucket for invalid geography/layer state; it is not silently merged into ring 5+ or unreachable.

`ringN` is a snapshot count, `ringPct` divides by snapshot population, and `residencePct` divides interval organism-time in each ring by the corresponding group's observed organism-time. The same fields for the whole population use the whole measured population/time. The map remains a diagnostic label and is never read by steering.

### Temporal versus spatial associations (`kind=comparisons`)

Four mutually exclusive categories compare consecutive sensed habitat identities:

| Category | Cell changed? | Layer changed? |
| --- | --- | --- |
| `sameNode` | No | No |
| `cellOnly` | Yes | No |
| `layerOnly` | No | Yes |
| `cellAndLayer` | Yes | Yes |

Cell-change totals are `cellOnly + cellAndLayer`; layer-change totals are `layerOnly + cellAndLayer`. The fourth category prevents silent double counting. Each category reports:

- `memoryTrend`: neutral/improving/worsening against the actual smoothed memory used by steering.
- `sampleTrend`: the same classification rule applied read-only to the **two consecutive actual observations**.
- Tumbles grouped by steering-memory trend and mean actual chosen tumble probability.
- Identical sampled inputs and the subset that still differ from smoothed memory.
- Actual horizontal crossings between senses and senses returning to the same endpoint after crossings.
- Absolute bounded-score and relative readiness delta min/mean/max/mean-absolute summaries, separately against memory and against the preceding actual sample. `raw` is signed bounded-score difference; `rel` is the normalized readiness difference. The existing relative comparison rule and thresholds are unchanged.

These are associations, not a decomposition of causality. Same-node samples can change because chemistry, transport, temperature or nearby organisms changed; they also include continuous movement inside a uniformly sampled cell. Unchanged samples can still trigger a comparison against older memory. Different-node samples combine spatial differences with environmental changes over elapsed time. A return trip can have identical endpoint identity; recorded crossing counts expose horizontal returns. The current data cannot isolate a pure spatial gradient or prove that the direction of travel caused the suitability change. No steering rule was changed to invent such an inference.

## Manual A/B procedure

1. Choose a concrete planet seed and turn **Use Random Seed off** in startup settings. Record the complete accepted configuration: grid, physical environment, vent production, ocean/atmosphere initialization, founder count and delay, hydrogenotrophy/temperature parameters, population cap, reproduction, locomotion/mutation settings, and speed profile.
2. Before run A, enable chemotactic steering and A/B diagnostics on the manager. Keep metabolism-aware suitability enabled and detailed individual tracing off. Do not apply diagnostic organism locomotion overrides. Start a fresh world through the normal startup flow.
3. Choose a fixed simulated duration, for example 1,800 simulated seconds, and save the Unity log. Compare full report windows with their actual times; if the final endpoint is between reports, use the last equivalent completed windows in both runs. Verify A labels, matching concrete/biology seeds and no mode switches.
4. Stop/reset the run and start an independent fresh world with the same concrete seed and identical settings. For run B, change **only Enable Geodesic Chemotactic Steering to false**. Keep A/B diagnostics enabled. Do not run A and B populations together, continue A as B, or reuse A's depleted environment/population.
5. Run B for the same simulated duration and save its log. Verify B labels, identical startup configuration, coverage from the beginning and no mode switches. A live mode change is allowed for inspection, but reports flag `mixedWindow` and the run retains a nonzero `runModeSwitches`; such a run is not an independent A/B trial. Enabling diagnostics late reports `coverageStart` rather than pretending earlier organism-time was observed.
6. Compare each locomotion group's valid habitat means, energy/C, gross energy production per organism-second, parent births/deaths per organism-second, and time-weighted ring residence. Examine composition/absolute counts alongside normalized measures. Check actual tumble rates, movement energy/time and displacement/time before interpreting different exploration as a habitat benefit. Use sample versus memory changes in the four categories to inspect how much steering responds to within-node temporal changes.
7. If manually repeating at different accelerations, keep matched A/B settings and equal simulated durations within each pair. Watch valid sampling counts, crossings between senses, partial windows and throughput. For confidence beyond one stochastic outcome, manually repeat independent matched pairs with additional recorded concrete seeds; no automated runner or sweep is provided.

## Interpretation and implementation limits

- Identical seeds initialize the same systems/settings but do not guarantee paired random trajectories. Changed tumble/vertical decisions, different resource competition, births, deaths and packed ordering can change subsequent random draw usage. The experiment does not claim common-random-number coupling.
- A different achieved tumble rate is a measured consequence of removing feedback, not a speed/cost retune. The control is the existing neutral explorer, not an empirically rate-matched counterfactual.
- Habitat quality is a snapshot mean; vent residence and event/energy rates cover the reporting interval. Population size, age structure, evolved locomotion composition, crowding and population-cap pressure still matter. Per-type normalized measures reduce size confounding but do not eliminate all ecological confounders.
- Metabolic suitability represents existing local resource opportunity before competition. Achieved metabolic production captures the actual model award after competition. Neither proves biochemical conservation or net fitness by itself.
- Ring proximity is topological cell-column proximity, not vent resource access, physical distance or exact depth. The map is the same static runtime diagnostic map already used elsewhere.
- Diagnostics read current packed state and cached resources without consuming gameplay RNG or mutating biological state. They reuse existing lifecycle, commit and population-report passes; there is no extra full-population scan or temporary collection in each movement step. Fixed-size statistic buffers are reused; strings are formatted only at report boundaries.
- The new control is not a biological trait and is not added to population save formats. These experiments should begin with independent fresh worlds rather than assuming snapshot restoration also restores all Inspector configuration or gameplay RNG.

Changed source systems: `ReplicatorManager` (Inspector/context), `GeodesicBiologyRuntime.Locomotion` (feedback gates only), `GeodesicBiologyRuntime.ChemotaxisAB` (new read-only aggregation), `GeodesicBiologyRuntime` (hooks in existing passes), `GeodesicBiologyRuntime.TaxisDiagnostics` (compact formatting), and `GeodesicBiologyTemperatureTelemetry` (read-only access to its existing ring map). No terrain, hydrology, chemistry, vent generation, rendering, reaction formula or mutation rule is changed.
