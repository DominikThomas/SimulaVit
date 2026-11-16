# Geodesic active locomotion and evolution

Implemented on `codex/geodesic-biology-parity`. Verification is source inspection, a C# 9 Roslyn syntax parse, and `git diff --check` only. Unity, automated tests, builds, generation, simulations, benchmarks, and parameter sweeps were not run. Ecological advantage and performance remain for manual validation.

## Integration and files

- `GeodesicBiologyRuntime.cs`: invokes active movement inside the existing geodesic step, shares neighbour/coast traversal with passive drift, initializes separated newborns, and extends growth/division presentation to active organisms. Reaction formulas, carbon and energy division splitting, and death recycling are unchanged.
- `GeodesicBiologyRuntime.Locomotion.cs`: geodesic sensing, steering, paid horizontal/vertical integration, inheritance, bounded diagnostics, and diagnostic organism overrides.
- `GeodesicLocomotion.cs`: Inspector settings, packed short-term movement memory, and an optional population snapshot extension.
- `ReplicatorManager.cs`: connects existing steering/mutation controls, new geodesic controls, diagnostic context menus, and snapshot capture.
- `ReplicatorPopulationState.cs`: movement memory follows capacity growth, addition, swap-back, clearing, and explicit hot-state export.
- `ReplicatorRenderSystem.cs`: existing growth/division presentation also applies to amoeboid/flagellar bodies; geodesic flagella use authoritative packed headings instead of index-keyed smoothing.
- `ReplicatorData.cs`, `SimulationSaveDtos.cs`: compatible optional movement state capture/restore. No planet cache or terrain format change.

The `ReplicatorSimulationPipeline` early return remains intact. Legacy movement, steering, metabolism and mutation processing do not run after the geodesic step. Legacy movement/steering code and `Docs/METABOLISM_AWARE_TEMPORAL_TAXIS.md` were inspected for behavioural principles; no cube-sphere lookup is used by the new runtime.

## Modes and geometry

Passive drift keeps its existing angular speed, scheduled wandering, random vertical events, and boundary reflection. It neither senses for steering nor pays active energy costs. Anchored organisms receive no translation or passive drift, and their existing body rendering remains intact.

Amoeboid and flagellar movement use the same continuous local spherical direction, tangent, radius, and real geodesic-neighbour traversal as passive drift. Amoebae have slower travel and continuous irregular run noise. Flagella travel faster and have a lower tumble hazard. Actual turning is bounded by each existing turn-rate control, rather than snapping to a new heading.

Default active angular speed is:

`0.002 rad/s * activeSpeedMultiplier * (moveSpeed / 4) * locomotionSpeedMultiplier`

With default controls this is 0.0028 rad/s for amoebae and 0.0048 rad/s for flagella. These are effective simulation speeds, not a microscopic physical motility model. No vent coordinates or distant resource inventories enter steering.

Active geometry integrates on the existing 0.1 simulated-second movement cadence. Each active substep is additionally limited to 20% of the current cell's minimum neighbour spacing. Catch-up is bounded to eight substeps; remaining per-organism time stays pending. Each swimmer only accumulates movement time after birth. Changing acceleration does not multiply a geometric step by the requested speed factor; the existing pipeline advances more authoritative simulation steps.

Swimmers retain their physical radius across horizontal boundaries. A coastline, missing layer, or shallower seabed reflects the heading instead of silently mapping a swimmer to another depth. Passive boundary/layer mapping remains unchanged. World positions and tangent headings are transformed from planet-local geometry.

## Temporal suitability and runs

Only the currently experienced cell/layer is sampled. For hydrogenotrophy:

`suitability = min(clamp01(H2 / steerGoodH2), clamp01(CO2 / steerGoodCO2)) * temperaturePerformance * oxygenEfficiency`

A substrate with zero configured reaction requirement is omitted from the limiting minimum. Invalid ocean habitats score zero. Temperature uses the same coarse ocean cell/layer authority, inherited optimum, lethal margin, and dormancy floor as biology. O2 inhibition uses the exact enabled flag, comfort/stress thresholds, and efficiency floor passed to the hydrogenotrophy reaction. This is a concentration-based local metabolic opportunity score; resource withdrawals still use the existing inventory/competition system.

The product makes poor temperature or missing substrate limit metabolic opportunity. It deliberately does not use the legacy additive temperature/food weights, which could rate substrate-free water highly. `steerGoodH2` and `steerGoodCO2` retain their existing normalization role. Inspect observed concentrations and tune these scales/thresholds if a configured gradient falls entirely within the neutral band.

Existing sense intervals/jitter, suitability memory blend, improvement/worsening thresholds, tumble probabilities/limits, turn angles/rates, speed multipliers and amoeboid noise settings are connected. Improved experience lowers tumble chance; worsening raises it; neutral experience uses the baseline. Disabling `activeMovementMetabolismSuitabilityEnabled` makes temporal steering use temperature performance alone. Zero thresholds still require a strictly positive/negative change.

Tumble probabilities are converted to time hazards:

`P(tumble) = 1 - (1 - configuredProbability)^(elapsedSenseTime / tumbleReferenceSeconds * modeHazardMultiplier)`

The flagellar multiplier defaults to 0.3, so its shorter sensing interval does not accidentally cause more spontaneous turning than amoeboid sensing. Completed mean run duration measures paid propulsion time between actual tumbles, rather than reporting the sensing interval as a run.

The metabolism dispatch is intentionally limited to hydrogenotrophy. Future pathways must add suitability inputs matching their actual geodesic reactions (H2S/CO2, layered light/CO2, OrganicC, OrganicC/electron acceptor, H2/CO2/O2 inhibition, CH4/O2). No other metabolism or metabolic mutation is activated here.

## Vertical experience

After a jittered exploration interval, a swimmer tries an adjacent valid layer in a remembered up/down direction. Surface/seabed constraints reverse a blocked trial direction; a one-layer column blocks exploration. It never evaluates destination suitability in advance.

Travel advances continuously toward that adjacent layer centre. Geometric radius travel is converted using the ocean grid's physical depth scale and the configured effective metres per simulated second. Authoritative layer identity changes at the physical interface; rendered radius follows the actual paid travel. One trial cannot skip layers. Horizontal and vertical propulsion each receive `1/sqrt(2)` of the effort while swimming diagonally, sharing one energy budget.

The organism remembers the suitability where the trial began. After arrival, the next ordinary sensing event compares the experienced destination against that baseline. Better trials tend to retain the vertical direction; worse trials tend to reverse it; neutral trials allow either. Each subsequent trial waits another bounded interval. Horizontal resource changes during a vertical trial can affect this comparison; the model is short-term experience, not a controlled gradient measurement or optimal-depth calculation.

## Inheritance and energy

Founders remain passive. Once per successful birth, the child inherits the parent's locomotion and clamped locomotion skill, subject to a separate seeded locomotion mutation stream. This stream does not perturb existing reproduction or temperature mutation random draws.

The legacy rules are retained:

1. `locomotionMutationChance` gates the mutation attempt (default 0.01).
2. `locomotionAnchoredMutationChance` first offers anchoring, permitted only in the actual bottom ocean layer. This preserves the existing legacy anchoring rule; it is not a newly introduced transition.
3. Existing optional `allowAnchoredToAmoeboidMutation` / `anchoredToAmoeboidMutationChance` retain their legacy nested release gate.
4. Otherwise `locomotionUpgradeChance` permits PassiveDrift → Amoeboid or Amoeboid → Flagellum. The legacy code has no active-to-passive downgrade, so none is invented. Flagella can still acquire bottom-layer anchoring under that existing gate.
5. Attempts ending in the same type count as blocked/no-change; successful source/destination transitions are recorded separately. Invalid inherited enum values are corrected and counted.

Active organisms pay for propulsion effort at 0.02 energy/s (amoeboid) or 0.04 energy/s (flagellar), in addition to existing basal/temperature maintenance. Costs use integrated simulated time. Insufficient energy proportionally shortens that step's propulsion and cannot overdraw the available positive energy. Zero energy stops propulsion; passive and anchored organisms pay no active cost. Coastline effort still costs energy even when displacement is rejected. Horizontal and vertical motion share one charge; no legacy movement cost executes afterward. The new rates are explicitly configurable because the inspected legacy metabolism system currently charges zero movement cost.

No reproduction bonus, passive penalty, or movement-derived energy is introduced. Division still splits existing energy after its existing division cost, and carbon allocation is unchanged.

Newborns preserve the existing deterministic same-cell separation, inherited physical depth, mature size, carbon-cycle baseline, volume-share diameter and division offset animation. Their transport starts from the separated world position with a fresh seed-derived tangent. If a passive parent was still interpolating visually between layers, the active child's first movement tick reconciles its habitat to the layer containing that actual radius without changing depth. Short-term taxis/vertical memory is fresh, not inherited. Packed memory is copied/cleared/resized with its organism. Optional snapshot fields retain local transport and active countdown/memory state; older snapshots initialize transport at their saved position. Seeded movement and mutation are deterministic for identical authoritative step sequences; exact random-stream continuation across save/load is not newly guaranteed by this milestone.

## Controls and diagnostics

`ReplicatorManager > Geodesic Active Locomotion > geodesicLocomotion` adds active speed, energy rates, flagellar hazard multiplier, reference tumble time, vertical speed/interval, better/worse vertical persistence, and diagnostic logging controls. Normal mutation settings stay in the existing Locomotion Mutation group. `mutationRateOverrideEnabled` / `diagnosticMutationChance` temporarily override only the mutation-attempt gate; upgrade/anchor gates still apply. Defaults do not force active founders.

Manager context menus under `Diagnostics/Geodesic Locomotion` set one selected organism to Amoeboid, Flagellum or Passive Drift, retaining position, energy and growth. They use Biology Diagnostics' selected organism ID, or the first organism if no ID is selected. These explicit diagnostic assignments are logged separately and are not counted as evolutionary mutations.

Every existing ten simulated-second biology report can emit three aggregate locomotion lines:

- `GeodesicLocomotion`: population by all four modes, mutation attempts/successes/blocked-or-no-change, inheritance/state corrections, physical horizontal/vertical distance, active crossings/transitions, depth rejections, movement energy, energy-limited steps, temporal comparisons/tumbles/completed runs, and vertical trial outcomes.
- `GeodesicLocomotionEcology`: current active/passive hydrogenotroph mean H2, temperature performance, metabolic suitability and energy; cumulative parent birth and death counts by group. Counts are raw outcomes, not causal or per-capita ecological fitness estimates. Empty-group means display zero with a zero sample count.
- `GeodesicLocomotionMutations`: a 4×4 source/destination transition matrix. Row/column order is passive, amoeboid, flagellum, anchored.

Existing `GeodesicBiologyTelemetry` continues reporting total horizontal/vertical crossings, land rejections and invalid habitat/layer counts. New counters are cumulative since runtime initialization; the existing biology movement counters retain their interval semantics. Detailed passive movement trails remain optional and passive-specific. New profiler markers cover active movement, sensing and vertical work.

Resource sensing caches three concentrations per occupied node within a movement batch, independently of reaction scratch buffers. Temperature shares the existing environmental-tick cache. Topological spacing is cached lazily per visited cell. There are no per-agent GameObjects, resource authorities, global resource scans, candidate habitat rankings, topology rebuilds or per-step managed allocations after capacity/cache initialization. Logging/formatting is bounded to report boundaries.

## Manual validation checklist

1. **Passive baseline:** disable the diagnostic override and set `locomotionMutationChance = 0`. Run the accepted hydrogenotroph configuration. Compare wandering, passive vertical events, growth/division, consumption, death recycling and survival. Active expenditure must stay zero; confirm anchored overrides remain stationary if inspecting an existing anchored organism.
2. **Active modes:** use the context menus on two selected hydrogenotroph IDs. Observe slower irregular amoebae and faster, longer-running flagella, continuous positions/headings and valid real-neighbour crossings. Check energy expenditure, including near-zero energy and coastline/shallow-water rejection. Diagnostic assignment itself must not change E/C or position.
3. **Gradients:** retain the accepted vent/chemistry/temperature settings. Observe improving/worsening comparisons and increased tumbles after worse experience. Compare the group habitat averages over repeated manual observations. Movement must not aim directly at known vent coordinates. Check that concentration scales and thresholds permit this particular gradient to leave the neutral band; positive outcomes are not assumed.
4. **Vertical trials:** watch selected active organisms' current layer and world depth. Trials must cross only one adjacent layer over physical travel time; improved arrivals should more often retain the direction and worse arrivals reverse it. Inspect surface, seabed, one-layer columns, simultaneous horizontal crossings and energy interruption. No layer jump or free depth change should occur.
5. **Natural evolution:** restart with normal passive founders. Enable normal mutation; for a diagnostic observation enable the mutation-rate override (optionally raise the existing upgrade chance). Confirm passive → amoeboid → flagellum descendants appear at births, inherit modes/skill and actually move. Observe permitted bottom anchoring separately. Restore all diagnostic settings before ecological comparison; passive and active survival advantages are to be measured, not presumed.
6. **Division, removal and restore:** follow active parent/daughter IDs across births, carbon growth, division animation, population capacity growth, and deaths causing swap-back. Check distinct stable positions/headings/depths and conservative division E/C diagnostics. Save/load during a vertical run and during division, and inspect an older snapshot's initialization.
7. **Acceleration:** manually inspect 1×, 10×, 50× and 100×. Compare position continuity, adjacent layer changes, locomotion cost per integrated simulated second, actual simulated-time advance and active profiler cost. Existing throughput clamping remains authoritative under CPU load. No performance or ecological result is claimed without these manual runs.

Remaining limitations: effective rather than microscopic swimming speeds; layer-resolved rather than within-layer resource sensing; bounded integration with potential lag under extreme custom speeds/deltas; no other metabolic suitability dispatch yet; no new bit-for-bit save/resume RNG guarantee. A suitable next biology milestone is conservative hydrogenotrophy uptake accounting (actual environmental withdrawals, non-assimilated carbon and storage overflow), before expanding the functional metabolic niches.
