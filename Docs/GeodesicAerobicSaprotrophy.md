# B2.3 Geodesic aerobic saprotrophy

## Reaction and accounting

Saprotrophs use dissolved OrganicC and O2 from their current ocean cell and layer. They assimilate part of the carbon into their organism store and oxidize the remainder of their configured uptake into dissolved CO2. The recipe reuses legacy `saproCPerTick` and `saproAssimilationFraction`, with the shared `aerobicO2PerC` and `aerobicEnergyPerC` yields. The provisional reaction-scaffold coefficients are descriptive. The Geodesic resource field owns all dissolved transfers.

The 0.5 simulated-second reference rate scales with elapsed reaction time and individual thermal performance. The oxidizable fraction sets the O2 demand. Remaining capacity caps the assimilable fraction, and both inputs use the existing proportional competition by ocean node. Fermenters share the same OrganicC pool. Dropping either input reduces the competed extent for both uptake and assimilation. A full store still allows oxidation of the configured oxidizable share. Carbon that cannot fit or be represented in the float store stays dissolved.

The resource field rechecks current inventories and plans all writes before applying any of them. Each constituent write uses the relative transfer tolerance of `2e-6` in the following ledger.

```text
OrganicC withdrawn ≈ increase in stored carbon + CO2 produced
O2 withdrawn ≈ carbon oxidized × aerobicO2PerC
energy gained ≤ min(CO2 produced, O2 withdrawn / aerobicO2PerC) × aerobicEnergyPerC
```

Energy rounds down to the representable organism increment. If a required transfer cannot be represented within tolerance, the reaction leaves the ocean and organism unchanged. A productive recipe requires a positive intake, an assimilation fraction below `1`, and positive O2 and energy yields. Feeding requires positive dissolved concentrations of both inputs, with their measured inventories governing the actual rate. Acquisition uses the additional concentration thresholds below.

Photosynthesis supplies dissolved O2 through its existing daytime reaction. Death releases remaining stored carbon as dissolved OrganicC. Both inputs move through ordinary ocean transport. Saprotrophic production has no light term. Maintenance, thermal exposure, division and death recycling use the existing Geodesic lifecycle.

## Evolution and setup

In **New Game → Biology → Biology availability**, enable **Aerobic saprotrophy**. The **All implemented pathways** preset includes it. **Biology → Metabolic evolution** exposes **Hydrogen → saprotrophy probability** and **Saprotrophy mutation chance**. The latter applies to sulfur chemosynthesis, fermentation and oxygenic photosynthesis parents. These are independent per-birth rolls after the existing metabolism choices. A successful saprotrophy roll can supersede an earlier metabolism choice for that daughter. The parent retains its traits. The saprotrophy roll has its own world-seeded stream and uses the existing `PlanetGenerator.SaprotrophyUnlocked` timer. With saprotrophy disabled, the earlier mutation streams and decisions follow their established sequence.

Acquisition checks the actual child layer for positive dissolved O2 and OrganicC at or above `mutationGateMinO2ForAerobes` and `mutationGateMinOrganicC` on `ReplicatorManager`. Both values are concentrations in Geodesic mode. The birth position must have positive thermal performance in the configured Saprotrophy template. New saprotrophs start from that template and the default lethal margin before individual thermal mutation. Saprotroph descendants inherit their parent's individual traits before ordinary thermal mutation.

Select **Saprotrophy** in the Biology metabolism parameter selector to edit organic carbon intake, assimilation, maximum organic carbon store and the shared aerobic yields. The temperature template is editable in the Biology tab. The existing fields keep their saved values, including Hydrogen → Fermentation at `0.02`. Blue agents appear in the HUD's **Sapro** count and in the optional Geodesic biology reports.

## Experimental founders

**Experimental saprotroph founders** defaults to `0`, including when loading older startup files. A positive count requests one batch and requires **Aerobic saprotrophy** and **Passive drift**. A new ocean starts with zero OrganicC, so the batch waits for a death that adds carbon to a layer meeting the O2, OrganicC and thermal gates. The first eligible site in lifecycle order is considered for each step. On placement, the cohort starts at that layer's center with ordinary founder energy, zero stored carbon and the Saprotrophy temperature template. It uses the same placement and passive initialization as B2.2 with a separate deterministic random stream. The batch shares the population cap. If no space remains that step, a later qualifying death can trigger the batch. Setup logs the request at startup and logs `[GeodesicSaprotrophFounders]` once if placement succeeds. There is no scheduled planet-wide search.

## Limits

Legacy Cube Sphere retains its stored-carbon fallback. Geodesic saprotrophy uses environmental OrganicC throughout, so poor habitat leaves existing energy to cover maintenance. There is no new taxis behavior. Physical layer volumes can dilute recycled carbon below the acquisition threshold even when the global inventory shows OrganicC. The initial population can take time to supply both resources. A successful mutation or founding event can still starve under the current ecology and energy settings. Endogenous population persistence is pending user-run validation.

## Manual Unity validation

Use a disposable **settings profile** or back up `startup_config.json` before the following temporary edits. Restore the saved startup and Inspector settings afterwards. These checks are for the user to run.

### 1. Founder availability and endogenous inputs

Use a fixed Geodesic seed. In Biology, select **Passive-only movement**, enable **Oxygenic photosynthesis** and **Aerobic saprotrophy**, set **Initial replicator count** to `0`, **Initial photosynthetic founders** to `20`, **Experimental saprotroph founders** to `5`, **Maximum population** to `100`, and **Reproduction rate** to `0`. Use **Founder batch** and disable Hydrogenotrophy. On `ReplicatorManager`, enable **Log Geodesic Biology Reports** and set both saprotrophy resource gates to `0` for the observation run so any positive local amount can qualify. This also lowers the shared fermentation carbon gate, so keep fermentation disabled for this run. For an endogenous O2 check, use positive initial CO2, zero initial ocean O2 and Fe2+, zero vent production, and an air–sea exchange half-life of `0`. Start paused. The HUD should initially show green phototrophs and `Sapro 0`. Resume through the first carbon-bearing deaths. Compare the first `[GeodesicSaprotrophFounders]` cell and layer with `[GeodesicDeathRecycling]` and the Geodesic cell picker's layer concentrations. Check for blue founders, positive local OrganicC and O2, and one placement line. If no death qualifies, inspect temperature, local O2 and whether phototrophs had stored carbon at death. For a shorter observation, temporarily reduce the manager's lifespan range to 10–20 seconds and increase photosynthetic CO2 uptake to `0.1` per reference tick and energy per CO2 to `0.5` in Biology. Repeat with the fixed seed, and with Saprotrophy and Passive drift disabled individually to check the startup restrictions. Passive drift also gates the phototroph batch.

### 2. Evolution and inheritance

Set experimental founders to `0`. Keep the producer setup and enable division again. Set **General metabolism mutation chance** to `0` and **Saprotrophy mutation chance** to `1`. Set the Saprotrophy unlock time and thermal mutation chance to `0` for this check. Once a phototroph divides in an eligible layer, check for a blue daughter with the exact Saprotrophy template and a green parent. Eligible sulfur and fermentation parents use the same probability. With an eligible hydrogenotroph birth, set **Hydrogen → saprotrophy probability** to `1` to check its independent roll. Compare births below either local resource gate, outside the template tolerance, before the unlock time, or with Aerobic saprotrophy disabled. For a priority check, enable B2.2 photosynthesis mutation with the general and Hydrogen → photosynthesis probabilities at `1`. Compare the same fully eligible hydrogenotroph birth with the independent saprotrophy probability at `1` and `0`.

### 3. Reaction ledger and light

On `ReplicatorManager`, enable **Biology Diagnostics → Enabled** and **Log Geodesic Biology Reports**. Follow `[GeodesicReactionAccounting] metabolism=Saprotrophy`. Compare `actualAttributedInputs`, `actualAttributedProducts`, `actualStoreDelta`, and `energyAwarded/actualDelta` against the equations above. Check `measuredAttributedCResidual` and `zeroMeasuredInputWithEnergy`. Compare a saprotroph in daylight and in darkness while its layer still carries both resources and temperature remains suitable. Use the per-reaction report to distinguish biology from air–sea exchange, transport and other chemistry.

### 4. Limits and competition

In the disposable session, attach the Unity debugger and break in `BuildSaprotrophyDemand`, then `TryCommitSaprotrophy`. Compare a normal reaction with a store already at its configured maximum. The store increment should be zero at capacity, and the oxidizable share should still yield CO2 and energy. Use a node with multiple consumers and compare `Desired`, `CompetedActual` and committed `Actual` in the runtime request. Observe limited OrganicC, then limited O2. Reducing either existing inventory to zero in the debugger between reaction steps should leave the other input and store unchanged by saprotrophy. For a precision check, lower the temporary O2 yield until the debit is too small to represent at the current concentration and volume. The transaction should return `false` before any resource write and grant zero energy. These comparisons are per reaction, before maintenance or other consumers change totals.

### 5. Persistence and disabled regression

Save and reopen setup with a nonzero experimental count, then load a pre-B2.3 settings copy and confirm its count is `0`. Verify that Hydrogen → Fermentation still reads `0.02`. With saprotrophy unavailable and the experimental count at zero, compare the same seed and settings against B2.2 for vent and photosynthetic founding and for the established hydrogen, sulfur, fermentation and photosynthesis birth paths.
