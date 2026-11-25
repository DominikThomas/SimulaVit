# B2.6 Minimal geodesic predation

Geodesic predators capture living organisms and feed on their stored organic carbon. Predators use the existing red base colour, **Predator** population HUD row, movement modes, and energy and carbon gates for division. B2.6 is implemented in source. Unity acceptance remains user-run.

## Capture, feeding and lifecycle

Legacy Cube Sphere supplies the Saprotrophy → Predation ancestry, optional motile-parent gate, timed Saprotrophy unlock, capture cooldown, assimilation fraction, temperature template and appearance. The Legacy scent-enabled layer bins select nonpredators for partial bites, allow some direct energy transfer when a bite is short of carbon, and kill below a prey energy threshold. Geodesic uses whole-prey capture at continuous world-space range. It excludes predators, dead organisms, invalid habitats and prey with no positive tracked carbon. Passive, anchored and active predators can all feed. Active predators use ordinary movement and thermal sensing. Prey scent, prey-directed taxis, fear, partial bites, direct prey energy transfer and Legacy predator-specific speed and maintenance multipliers are outside B2.6.

Each 0.25 simulated-second reaction window evaluates metabolism first. Ready predators then get at most one capture opportunity in ascending packed-slot order. A predator needs positive local dissolved oxygen and nonzero performance at its experienced temperature. The configured probability is multiplied by temperature performance. The cooldown restarts on an eligible attempt, including a failed roll or rejected transfer. A search without a valid target leaves the predator ready for the next reaction window.

All tracked prey carbon is in **OrganicCStore**. The biomass target is a division threshold. Size and energy carry no additional carbon. The resource field checks the debit and credits before writing them. The predator's node supplies O2 and receives CO2. The prey's node receives unused carbon as dissolved OrganicC. The following limits use C for the prey reserve and f for the assimilation fraction.

- Assimilation is limited to f × C and the predator's representable free store capacity.
- Oxidation is limited to (1 − f) × C and the local O2 inventory divided by the configured aerobic O2 yield. Store overflow can be released as OrganicC.
- C ≈ assimilated C + CO2 produced + OrganicC released. Oxygen withdrawn is the configured yield times oxidized carbon within the existing 2e-6 relative transfer tolerance. The oxidative energy credit is at most the measured CO2 production times the configured energy yield.

Dissolved quantities and stored carbon use concentration·km³ inventory units. Store and energy increments round down to representable floats. Invalid or unrepresentable transactions leave prey, predator and water inventories intact. Zero oxygen blocks capture. Low oxygen reduces oxidation and energy while increasing the OrganicC remainder. A full predator store can still support the oxidative part.

Successful commits credit the predator and immediately mark the prey consumed. The index verifies each candidate's stable organism ID against the current packed slot. Competition is serial, so later predators skip consumed prey. The existing reverse lifecycle pass removes each captured prey with cause **Predation**, records the normal death bookkeeping, and skips age, maintenance, division and ordinary recycling for that victim. Swap-back updates indexed slot mappings before compaction. Newborns and later founders enter the next capture window. A pending victim's slot becomes available once the reverse pass reaches its removal.

## Search cost and approximation

Geodesic resource competition tracks touched ocean nodes and resource demand. Predation uses a reusable hash of Cartesian world-space buckets whose width equals the capture distance. Each search checks its own bucket and 26 adjacent buckets and uses the exact squared separation to accept a target. This covers cell and layer boundaries without traversing the ocean grid. A shared resource cell confers no capture eligibility. The search does not test line of sight or intervening terrain.

The first oxygenated, thermally eligible predator ready to search builds one index from the post-movement, post-metabolism population. For N organisms, P ready predators, candidate budget B and Lᵢ local occupants for predator i, expected work is **O(N + 27P + Σ min(B, Lᵢ))**, plus clearing the previously allocated bucket hash. Reusable storage scales with the peak indexed population. The hot loop has no per-query managed allocation or sorting. A deterministic per-organism, per-step rotation spreads an evenly spaced sample across the concatenated buckets. Crowded or edge-of-range neighborhoods can miss nearby prey and tend to favor locally abundant prey. Captured entries can occupy part of the budget until the next rebuild. Ascending packed order gives earlier predators first access to contested prey. With Predation disabled or no living predator ready and eligible to hunt, there is no index build or capture search.

## Setup and observability

In **New Game → Biology**, enable **Predation** or choose **All implemented pathways**. The only transition is Saprotrophy → Predation. Both the existing Saprotrophy unlock and the additional Predation unlock apply. A daughter requires an allowed inherited locomotion, suitable experienced temperature, and positive local O2 at or above **Minimum local O2 for aerobic acquisition**. Its living Saprotrophy parent must remain within capture range and retain positive carbon after division. This direct prey check allows the first predator to evolve without an index query. It can favor predation within the same lineage, and unrelated prey cannot satisfy this acquisition gate. A successful mutation takes the **Predation** temperature template before ordinary thermal trait mutation. Capture, mutation and founder trait generation use separate seeded RNG streams.

| Biology control | Default or existing value | Meaning |
| --- | --- | --- |
| Predator mutation chance | Existing field, source default 0.002 | Probability per eligible Saprotrophy birth. |
| Predation evolution unlock (sim s) | 0 | Additional minimum elapsed simulation time. Experimental founders are exempt from evolution timers. |
| Require motile parent for predator evolution | On | Requires an Amoeboid or Flagellar parent. |
| Predation temperature template | 283.15–323.15 K (10–50 °C) | Initial range for founders and newly mutated predators. |
| Capture distance (world units) | 0.25 | Maximum three-dimensional separation. |
| Capture probability / attempt | 1 | Probability before the temperature multiplier. |
| Capture cooldown (sim s) | 1 | Minimum delay after an eligible attempt. At zero, reaction cadence still limits attempts. |
| Organic carbon assimilation fraction | Existing field, default 0.6 | Maximum share of prey carbon available to store. A value of 1 leaves no oxidative share and disables feeding. |
| Aerobic respiration O2 / carbon | Existing shared value, default 0.02 | O2 inventory per oxidized carbon. Saprotrophy shares this setting. |
| Predation energy / oxidized carbon | 0.3 | Energy yield per carbon released as CO2. |
| Candidate budget / prey search | 24 (range 1–256) | Maximum candidate entries examined per query. |
| Experimental predator founders | 0 | One-time total requested. Living prey must already exist. |
| Collect predation diagnostics | Off | Cumulative counters for this world. |

Experimental founders use Passive drift and require Predation availability and the manager's existing **Enable Predators** flag. Each reaction window with available capacity inspects up to 32 population slots in rotating order and can place a founder at a live prey's position if its local temperature and O2 qualify. Founders start with ordinary founder energy and an empty store. The startup line reports 0/requested, and a completed batch reports placed/requested. A blocked or partly fulfilled request stays pending and can be inspected on demand. Older startup profiles retain existing mutation settings and default the new founder count to zero.

During play use **Replicator Manager → Biology Diagnostics → Report Geodesic Predation** in the Inspector's component context menu. The readout gives placed/requested founders, builds, searches, candidates, searches without prey, failed probability or transaction attempts, captures, prey C, assimilated C, CO2, OrganicC, O2 and energy. A later predator can report a search without prey if its candidates have already been consumed. The Unity Profiler exposes **GeodesicBiology.Predation.Capture** and its **GeodesicBiology.Predation.IndexBuild** child. Population balance, prey-directed behavior and measured performance remain open.

## Manual Unity validation

Use a temporary startup profile or restore settings after these scenarios. Keep **Hydrogen → fermentation probability** at the saved **0.02**. For deterministic cases use **Window → General → Test Runner → EditMode → GeodesicPredationTests**. The observations below are acceptance criteria.

### A. Simple predator–prey ecosystem

1. Select **Geodesic Icosphere**. Turn off **Use Random Seed** and set **Planet Seed** to 12345. Set subdivision 3, **Ocean World** minimum depth 0.02, resource transport interval 5 simulated seconds, day length 60 simulated seconds, and axis tilt 0°.
2. Set initial ocean CO2/O2/CH4/Fe2+ to 0.10 / 0.10 / 0 / 0, base temperature to 293.15 K, insolation temperature gain to 0 K, global vent production multiplier to 0, and air–sea exchange half-life to 0.
3. Enable only **Oxygenic photosynthesis**, **Predation**, and **Passive drift**. Set **Initial replicator count** 0, **Initial photosynthetic founders** 40, **Experimental predator founders** 4, all other founder counts 0, **Maximum population** 100, and **Predator mutation chance** 0. Use 0–100 °C for both thermal templates and photosynthetic CO2 use/energy yield of 0.10 / 2. Set minimum aerobic acquisition O2 0.01, capture distance 0.25, probability 1, cooldown 0.25 s, assimilation 0.6, O2 yield 0.02, energy yield 1, candidate budget 24, and maximum organic carbon store 2. Enable **Collect predation diagnostics**.
4. Set basal maintenance 0.005, reproduction rate 1, carbon-limited division on, initial biomass target 0.005, division multiple 1.2, child carbon fraction 0.5, and division energy cost 0. Start paused and resume at 1×. Inspect the **Predator** HUD and request the report after 15 and 60 simulated seconds. Look for red predators, up to four placed founders, captures and positive carbon, O2 and energy transfers. A Predator count exceeding four, or a recorded Predator parent and daughter in division diagnostics, shows reproduction.
5. To isolate evolution, use **SaprotrophyCanAcquirePredationFromTheLocalParentWithoutBuildingAnIndex**. It starts with one Saprotroph with reserve 2, a 290–310 K template at 300 K, oxygen inventory 4, mutation chance 1, unlock 0 or 30 s, and the motility gate off. The first-substep division should acquire Predation only with availability on and unlock 0. All cases should report zero index builds.

### B. Competition for one prey

Run **TwoPredatorsConsumeOnePreyOnceWithoutOrdinaryRecycling**. Two predators share a site with one living nonpredator holding reserve C of 2. Use capture probability 1, assimilation 0.5, initial oxygen inventory 4, O2 yield 1, and energy yield 3. Expect one predation death, 1 C assimilated by the first predator, 1 CO2 produced, 1 O2 consumed, 3 energy credited and no ordinary OrganicC recycling. The second predator and its ID survive compaction. The next reaction should leave the death count and CO2 unchanged.

### C. Resource and capacity limits

1. Run **FullStoreAndLimitedOxygenSendUnusedCarbonToOrganicPool**. With prey C 2, a full store 1/1, assimilation 0.5 and available oxygen 0.25, O2 yield 1 and energy yield 3, expect CO2 0.25, OrganicC 1.75, energy gain 0.75 and no increase in stored carbon. Zero oxygen should reject the transfer.
2. Run **FoundersRemainPendingAtCapacityAndAnExistingPredatorCanDivideAfterFeeding**. The initial cap of 1 is full with one eligible prey. Raising the cap to 2 places the pending founder. Raising it to 3 and setting reproduction rate 4/s allows the fed predator to divide.
3. Repeat A with **Initial photosynthetic founders** 0. With four predators requested and all prey sources off, expect 0/4 placed and zero index builds. For a missing-oxygen check with prey present, use the zero-O2 case of **IdleCapturePathSkipsIndexAndSearch** so photosynthesis cannot replenish the pool.

### D. Disabled-path regression

Repeat A with **Predation** off. Leave the founder request at 4 and set **Predator mutation chance** to 1. Expect zero placements, captures, searches and index builds. Compare nonpredator populations and carbon stores against an identically seeded run with the same toggle off, founder count 0 and mutation chance 0, at matched step counts and speed. **DisabledAvailabilityAlsoSkipsAnInjectedPredatorWithOxygen** covers the disabled path with a predator present.

### E. Spatial correctness

Run **SearchUsesContinuousDistanceAcrossCellsAndLayersAndFollowsSwapBack**. With range 0.25, the origin is (0.99, 0, 7.80). The same-node candidate (1.23, 0.24, 7.80) is outside the capture sphere. The candidate at (1.01, 0, 7.78) occupies an adjacent logical cell and layer across a hash boundary and is within reach. Budget 1 rejects the far candidate and stops. Budget 2 finds the near candidate. The fixture removes the far candidate with swap-back, then checks that the near candidate retains its identity and that a marked prey cannot be selected again.

### F. Bounded search at modest populations

Repeat A with 200 photosynthetic founders, 20 predator founders, maximum population 300, and reproduction rate 0. Take cumulative reports at 15 and 30 simulated seconds. Verify that the increase in candidates is at most the increase in searches times the configured budget. Compare fresh runs with budgets 4 and 24. In Unity's CPU Profiler inspect both predation markers at 1× with Deep Profile off. A matched run with Predation disabled should omit them. Compare time and GC allocations during index growth and subsequent steady activity.
