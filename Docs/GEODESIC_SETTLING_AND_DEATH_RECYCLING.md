# Geodesic mineral settling and death-carbon recycling

Static investigation and implementation, 2026-11-11. No Unity, Play Mode, build, automated test, simulation, benchmark, parameter sweep, or test harness was run or created. Changes are uncommitted and await manual review.

## Earlier implementation and migration

| Commit | Relevant files at that commit | Finding |
| --- | --- | --- |
| `24eba4e` | `Assets/Scripts/PlanetResourceMap.cs` | Added layered ocean resources and downward OrganicC marine snow. OrganicC served both as food and detritus, without separate dissolved/particulate pools. Bottom-layer carbon stayed in the water resource array; there was no separate organic seabed inventory. |
| `330a19f` | `Assets/Scripts/PlanetResourceMap.cs` | Added the weak OrganicC lateral spread and tiny upward bleed alongside preferential downward transport. |
| `6bb04e3` | `Assets/Scripts/PlanetResourceMap.cs`, `Assets/Scripts/PlanetCellInspectorPanel.cs` | Added layer-local sulfur and iron-oxide **visual** suspension arrays. Bottom-first traversal moved a fixed fraction down one layer per atmosphere tick, then transferred a fraction from the bottom to per-column tint. Suspended material and tint could fade. These were scaled visual signals rather than conservative physical mineral inventories. |
| `1f593b2` | `Assets/Scripts/PlanetResourceMap.cs` | Separated the surface precipitate overlay from seabed sediment tint. Surface visibility sampled L0 and a small L1 contribution; deep suspension did not colour the surface. |
| `f89e4f1` | `Assets/Scripts/ReplicatorManager.cs`, `Assets/Scripts/ReplicatorData.cs` | Established stored organic carbon and death recycling. Current legacy lifecycle, metabolism, and predation systems still call the manager's `DepositDeathOrganicC` before removal. |
| `77b063b` | `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicAbioticChemistry.cs`, `GeodesicOceanSedimentField.cs` | Geodesic oxidation put S0 and Fe(III) straight into the source column's seabed inventory, regardless of the source layer. No physical suspended inventory or settling stage. |
| `a98819c` | Same geodesic chemistry/sediment files | Added FeS precipitation, also deposited immediately. |
| `dc07931` | `Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs` | Introduced the independent geodesic biology loop. Its death branch removed organisms without invoking the legacy death-carbon return. Biological sulfur also bypassed suspension. |
| `7848fa4`, `af71c1c` | Geodesic ocean layer/resource/physical-scale files | Established physical layer inventories and the current depth scale. The implementation uses the existing physical geometry, including depth scale **0.0175**, without changing it. |
| `f124e8e` | `Assets/Scripts/Planet/Environment/Ocean/GeodesicOceanSedimentVisual.cs` | Working live-seabed sediment tint. Its deposited-inventory inputs and charcoal/rust/yellow colours are unchanged. |

Current legacy equivalents live under `Assets/Scripts/Planet/Environment/ResourceSimulation/PlanetResourceMap.cs`, `Assets/Scripts/Biology/Replicators/`, and `Assets/Scripts/UI/`.

The legacy mineral visual arrays had no horizontal particle-transport pass and no resuspension. Dissolved resources and OrganicC had their own mixing passes. The fading visual mineral quantities cannot be transplanted as a mass-conserving model. No existing independent particulate-organic or organic-seabed inventory was found in either chemistry implementation.

## Mineral correction

`GeodesicOceanSuspendedField` owns three double-precision inventory channels: S0, Fe(III) precipitate, and FeS. Units match precipitation: concentration * physical km3. Abiotic products retain their exact existing reaction extents and enter the actual reacting node. The existing biological sulfur yield also enters its producing node, with its quantity unchanged.

Each existing fixed resource tick now performs dissolved sources/exchange/mixing as before, then mixes and settles existing particles, then performs abiotic precipitation. Thus a new abiotic precipitate is not deposited in its formation tick.

Particle mixing reuses the existing horizontal overlap links, vertical links, geometry-weighted conductance caches, and bulk-water mixing rates. This is the model's existing diffusive transport, not a new current/advection model. Dissolved-species multipliers do not apply to particles. Link transfers use inventory divided by each node's own physical volume. Outgoing requests are collected before application and limited by the donor's inventory; equal material enters receivers. There is no concentration copied between unequal volumes, negative clipping, decay, chemical reactivity, or resuspension of deposited sediment on this path.

Settling uses a well-mixed layer residence model:

`fractionLeaving = 1 - exp(-speedKmPerSecond * simulatedSeconds / physicalLayerThicknessKm)`

The mean residence time is layer thickness / speed. Bottom-first traversal lets a settling transfer cross at most one layer interface per tick. At the actual bottom layer it enters that column's existing deposited field. Inactive layers and land are skipped, so one- through five-layer columns use the same rule and nothing settles below the seabed. Mixing may also exchange particles vertically before settling, as bulk-water mixing already does for dissolved resources.

New effective settling-speed defaults are **S0 0.0002 m/s, Fe(III) 0.0005 m/s, FeS 0.001 m/s**. These are explicit tunable modelling assumptions, not a calibrated particle-size/density model or recovered legacy physical constants. Zero disables that mineral's settling while allowing mixing. They can yield long delays: a hypothetical 100 m layer has mean settling residence times of 500,000 / 200,000 / 100,000 simulated seconds respectively. Small early deposition is expected; precipitation rates were not increased to compensate.

All particle evolution uses the existing resource clock and simulated delta, including its retained catch-up backlog. No frame-time integration, random draws, or per-tick array allocations were added. World initialization/reset owns and clears the new state alongside existing ocean chemistry. Persistent terrain/planet caches and vent production were untouched. Geodesic simulation save/load is already unsupported; this does not add persistence for live chemistry.

## Death investigation and correction

The pipeline returns immediately after the geodesic biology step, bypassing the legacy lifecycle/metabolism/predation passes. In geodesic mode, the implemented death conditions are old age and energy depletion. Temperature/O2 affect metabolism and can indirectly cause energy starvation; the legacy direct environmental-death, specific starvation-reason, predation, and mutation mechanisms are not implemented in this runtime. This change does not add them or change their rates.

Legacy old age, starvation/energy depletion, direct environmental damage, and predation deaths already copy packed state and call carbon release before removal. Predation removes bite carbon from prey before transferring the remaining death store. Render work, generic swap-back removal, population clearing, and world reset are removal/cleanup operations, not biological carbon sources.

The geodesic defect was genuine loss: old-age and starvation deaths removed `OrganicCStore` without transferring it. There is no separate structural biomass stock, ingested mineral store, or stored dissolved-substrate inventory. `BiomassTarget` is a division threshold; `size` and `energy` are not additional carbon quantities. Adding any of those to death carbon would invent material. Geodesic metabolism withdraws physical environmental inventories and increases `OrganicCStore` in those same inventory units; the legacy normalized-resource concentration-add path is unsuitable here.

The new death hook reads the authoritative packed store, cell, and current layer; clears both packed and companion stores; adds that carbon as **dissolved OrganicC inventory** at the exact death node; then the caller removes the organism and its packed state. No position-derived cell, preferred-layer fallback, or distant-ocean deposition is used. The hook is only on biological death. Generic cleanup cannot release it again. The measured float-storage transfer and rounding residual are reported separately.

All tracked death carbon goes to the existing dissolved pool. There is no invented dissolved/particulate fraction. **Particulate organic settling and separate organic seabed accumulation remain unsupported.** They require an explicit organic-particle reservoir and decomposition/availability rules, which would be a separate extension. Deposited minerals are never used as an organic reservoir.

Legacy saprotrophy consumes local OrganicC, stores its configured assimilated fraction, and uses O2 to respire the rest into CO2; it can respire stored C when environmental food is absent. Its inputs, accounting, and settings are unchanged. The geodesic foundation currently supports hydrogenotrophy, sulfur chemosynthesis, methanogenesis, photosynthesis, and methanotrophy only: it has no saprotrophy/fermentation branch or evolution into them. Returning OrganicC to the existing dissolved channel preserves the appropriate substrate, but does **not** make geodesic saprotrophy operational. No geodesic abiotic reaction currently consumes or decomposes OrganicC; released carbon is subsequently mixed horizontally and vertically. It has no air-sea exchange path.

Normal geodesic founders/descendants are marine and passive movement rejects land. If an invalid/land habitat nevertheless reaches the death hook, its carbon is counted as `unplacedCarbon`, unavailable to environmental metabolisms. No terrestrial detritus reservoir exists and no carbon is silently moved into an ocean cell. Nonfinite/negative stores are diagnosed rather than converted into invented carbon.

## Diagnostics and display

- `[GeodesicChemistryTelemetry]` retains its existing simulated-time schedule and real-time throttle. `particles` reports each mineral's L0..L4 suspension, cumulative downward settling out of each layer, cumulative transfer to seabed, total produced, suspended + actual deposited inventory, absolute and relative conservation residuals. Bottom deposition is reported separately from downward layer transfer. Produced totals include biological sulfur; `chemistryTotal` contains abiotic precipitation only. The chemistry counter labels now say **precipitated**, since formation and deposition are different events.
- Chemistry telemetry additionally reports positive OrganicC node count and peak concentration alongside the existing scientific/general-format inventory, ocean mean, and per-layer means. The geodesic HUD already switches small positive concentrations to scientific notation, and its cell picker uses `G4`; neither rounds every small positive quantity to zero. Legacy inspector fields do use fixed decimal precision, but they are not geodesic resource authority. HUD cached means can lag death release until the next normal resource diagnostic refresh.
- `[GeodesicDeathRecycling]` is emitted with the existing periodic biology summary (every 10 simulated seconds, including after population extinction), not per death/frame. It reports cumulative reason counts/carbon, dissolved return by layer, total remaining death carbon, measured dissolved transfer, float rounding residual, unplaced/invalid state, accounting residual, and the most recent death's exact cell/layer/reason/carbon/transfer. Unsupported particulate and organic-sediment paths are explicit.
- The observed lack of OrganicC was a missing death source, not evidence of rapid geodesic decomposition or saprotrophic consumption. Once fixed, large physical layer volumes can still make concentration and ocean-wide mean extremely small. Compare physical inventory and peak/local concentration with the death-source totals.

## Files changed

- `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanSuspendedField.cs` and its `.meta`: new mineral inventories, physical settling, and particle diagnostics.
- `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs`: initialization/reset, reuse of bulk transport, and fixed-tick integration.
- `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicAbioticChemistry.cs`: route existing products into suspension and name formation counters accurately.
- `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicChemistryTelemetry.cs`: particle accounting and OrganicC distribution diagnostics.
- `Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs`: biological sulfur suspension, death-carbon return before disposal, periodic recycling diagnostics, and reset of their state.
- This report.

## Next manual simulation

1. Check startup reports `settling=physicalLayerResidence`, with suspended state initialized. Observe formation in suspension before deposited totals grow. Check FeS/Fe(III)/S0 independently, including bottom-produced products. Their configured speeds imply potentially long simulated settling times.
2. Compare L0..L4 inventories, `settlingDownwardOutTotal`, and `toSeabedTotal`. Verify shallow one-/partial-layer columns deposit at their own bottom, with no inactive-layer inventory. Mixing can spread suspension laterally and vertically; no particles or sediment should become negative.
3. Compare each mineral's `suspendedPlusDeposited` against `producedTotal`; conservation residuals should be at floating-point summation scale. Biological sulfur is included in particle production but not abiotic-only chemistry counters. Deposited tint should retain FeS charcoal, Fe(III) rust, and S0 yellow. No suspended optical rendering was added.
4. Observe both lifespan and energy-depletion deaths, including the final organism. Check reason counts, death location/layer, remaining carbon, and measured dissolved return. `unplacedCarbon`/`invalidCarbonStates` should stay zero for normal marine organisms; accounting and transfer-rounding residuals should stay negligible relative to released carbon. Extinction must not erase ocean OrganicC or repeat death release.
5. Compare `oceanInventory.OrganicC`, `organicCDistribution`, and the death layer in the cell picker. Expect dilution, not a large immediately visible ocean mean. Geodesic saprotrophic consumption and organic sediment are not expected in this implementation.
6. Compare equal simulated times at normal/accelerated speeds and pause with no pending resource backlog. Check that no new evolution occurs while paused, and that reset starts with empty new reservoirs and no death-carbon pulse. Terrain/hydrology caches, vent production, and dissolved/atmospheric behavior should remain as before.

Static review only. **STOP for manual review.**
