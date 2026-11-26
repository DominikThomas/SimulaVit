# New Simulation setup tabs

> **UI implementation checkpoint.** The tables below describe the original setup release. Later milestones extend the Biology controls with metabolic availability, fermentation, photosynthesis, saprotrophy, methanogenesis, methanotrophy and gradual founders. [Current Biology controls](StartupBiologyEvolution.md) · [Saprotrophy controls](GeodesicAerobicSaprotrophy.md) · [Methanogenesis controls](GeodesicMethanogenesis.md) · [Methanotrophy controls](GeodesicAerobicMethanotrophy.md) · [Vent VFX persistence](HydrothermalVentVfx.md) · [Roadmap](GEODESIC_MIGRATION_ROADMAP.md)


The built-in setup remains Unity IMGUI. Its common header contains the title, random-seed toggle, editable seed, Randomize button and grid selector. Six tabs share a fixed bottom action row (Start, Start Paused, Reset Defaults, Back). Each tab retains its own scroll position and uses automatic GUILayout sizing. Setup uses a responsive panel, at least 900 logical pixels wide when space permits, with uniform scaling below a 760 × 520 logical viewport. The main menu, save picker and loading presentation are unchanged.

## Controls

| Tab | Controls |
| --- | --- |
| Planet | Tilt, day/year lengths, legacy resolution; geodesic target ocean coverage and target-mode toggle; river master, flow/major thresholds; lake master, basin area/depth; disconnected-sea exclusion and component area. |
| Ocean | Initial dissolved CO2, O2, CH4, Fe2+; base temperature and insolation gain; geological vent production. Geodesic has clustering, terrestrial fraction, multiplier and four physical inventory rates. Legacy has four separate per-tick rates. |
| Atmosphere | Geodesic pressure, dense-atmosphere opt-in, five trace-gas fractions, derived N2/partials and air–sea L0 exchange half-life. |
| Biology | Population, founder delay, reproduction/division, maintenance/movement costs, evolution, eight temperature templates, thermal settings and a metabolism parameter selector. Controls reflect the selected mode's actual consumers. |
| Advanced | Performance recording, Geodesic geometry and sea level, terrain and hydrology, basin infill, environment and telemetry intervals, atmosphere inventory scaling, and Reset Advanced. |
| Debug | River destination colours, rejected/uphill reaches, visible-terrain grade, termination reasons, carving gizmos and the existing geodesic chemotaxis steering/A/B diagnostic switches. |

Unsupported geodesic planet/environment controls are hidden with explanations in legacy mode. All previously editable built-in startup fields remain represented. No runtime searches occur while drawing the tabs; static labels/styles and float input formatting are cached. Numeric inputs accept a decimal point or comma without interpreting thousands separators. Partial input stays visible while focused; only finite, validated values enter configuration.

### Performance recording

In **New Game → Advanced → Performance recording**, configure the existing scene analyzer for either grid.

| Control | Default |
| --- | --- |
| Enable performance recording | Off |
| Detailed Geodesic profiling (this session) | Off |
| Lifecycle thermal sampling (Geodesic) | Reference |
| Write CSV | On |
| Log periodic summaries to Console | Off |
| Summary interval (real seconds) | 5 seconds, minimum 1 |
| Append timestamp to CSV filename | On |

Startup saves the basic recording options with the other settings. The detailed toggle appears when recording is enabled for a Geodesic world and applies to this session. **Reset Advanced to Defaults** restores the recording options. **Start** and **Start Paused** begin a fresh capture once the world is ready. The runtime **Esc** menu displays the active CSV path and can copy it to the clipboard. Returning to the main menu stops recording and flushes the completed samples. [Performance recording and CSV details](GeodesicPerformanceBaseline.md) · [Detailed attribution and capture modes](GEODESIC_PERFORMANCE_BENCHMARKS.md#profiling-modes-and-overhead)

**Lifecycle thermal sampling** selects **Reference** or **Optimized** for the next Geodesic world, even with recording off. The choice is held by the setup controller for the current Unity session. **Reset Advanced to Defaults** restores Reference. Saved profiles retain their existing settings, and each capture records the selected mode. [Lifecycle thermal sampling pilot](GeodesicLifecycleThermalOptimization.md)

## Newly connected configuration

Five new top-level fields mirror PlanetGenerator: `geodesicSeaLevelControlMode`, `geodesicTargetOceanCoveragePercent`, `geodesicSeaLevelOffset`, `geodesicOceanWorldMinimumDepth`, and `enableGeodesicRivers`.

These are captured from the scene and applied before authoritative planet generation. Target coverage is active only in TargetAreaCoverage mode; the Planet toggle explicitly selects that mode. Manual offset and Ocean World retain their existing meanings. Ocean enablement remains on PlanetGenerator. Excluding disconnected seas and geological constraints can reduce retained-ocean area below the target. Lakes are not guaranteed on every generated terrain. Lake/carving/infill work may still require drainage when visible rivers are disabled.

`SimulationStartupConfig.biology` contains 83 typed startup values. They are captured from ReplicatorManager, cloned independently, validated, persisted and applied before `InitializeForSimulation` / founder spawning. No organism temperature traits are assigned by the startup configuration code.

| Group | Newly persisted fields |
| --- | --- |
| Population and costs | `maxPopulation`, `reproductionRate`, `replicationEnergyCost`, `divisionEnergyCost`, `basalEnergyCostPerSecond`, `moveEnergyCostPerSecond`, `enableCarbonLimitedDivision`, `defaultBiomassTarget`, `divisionBiomassMultiple`, `divisionCarbonSplitToChild` |
| Geodesic movement costs | `geodesicAmoeboidEnergyPerSecond`, `geodesicFlagellumEnergyPerSecond` map only to the two corresponding members of `ReplicatorManager.geodesicLocomotion`; all other locomotion settings are preserved. |
| Metabolism evolution | `metabolismMutationChance`, `hydrogenToPhotosynthesisMutationChance`, `hydrogenToSaprotrophyMutationChance`, `hydrogenToSulfurMutationChance`, `hydrogenToFermentationMutationChance`, `fermentationToMethanogenesisMutationChance`, `hydrogenToMethanotrophyMutationChance`, `saprotrophyMutationChance`, `predatorMutationChance` |
| Locomotion evolution | `locomotionMutationChance`, `locomotionUpgradeChance`, `locomotionAnchoredMutationChance`, `allowAnchoredToAmoeboidMutation`, `anchoredToAmoeboidMutationChance` |
| Biomass evolution | `biomassMutationChance`, `biomassMutationScale` |
| Temperature templates | `hydrogenTempRange`, `sulfurChemoTempRange`, `photoTempRange`, `saproTempRange`, `predatorTempRange`, `fermentationTempRange`, `methanogenesisTempRange`, `methanotrophyTempRange` |
| Shared thermal settings | `defaultLethalMargin`, `temperatureDormancyFloor`, `temperatureReplicationMinEfficiency`, `tempMutationChance`, `tempMutationScale`, `temperatureDirectDamageEnabled`, `extremeColdDeathTempKelvin`, `extremeHeatDeathTempKelvin` |
| Geodesic thermal settings | `geodesicTemperatureStressMaintenanceMultiplier`, `geodesicColdExposureDeathSeconds`, `geodesicHeatExposureDeathSeconds`, `geodesicTemperatureExposureRecoveryPerSecond` |
| Hydrogenotrophy | `hydrogenotrophyH2PerTick`, `hydrogenotrophyCO2PerTick`, `hydrogenotrophyEnergyPerTick`, `hydrogenotrophyStoreFraction` |
| Sulfur chemosynthesis | `chemosynthesisCo2NeedPerTick`, `chemosynthesisH2sNeedPerTick`, `chemosynthesisEnergyPerTick`, `chemosynthStoreFraction`, `chemoRespirationCPerTick` |
| Fermentation | `fermentationOrganicCPerTick`, `fermentationEnergyPerTick`, `fermentationAssimilationFraction` |
| Methanogenesis | `methanogenesisH2PerTick`, `methanogenesisCO2PerTick`, `methanogenesisEnergyPerTick`, `methanogenesisAssimilationFraction` |
| Methanotrophy | `methanotrophyCH4PerTick`, `methanotrophyO2PerTick`, `methanotrophyEnergyPerTick`, `methanotrophyAssimilationFraction` |
| Photosynthesis and respiration | `photosynthesisCo2PerTickAtFullInsolation`, `photosynthesisEnergyPerCo2`, `photosynthStoreFraction`, `nightRespirationCPerTick`, `maxOrganicCStore`, `aerobicO2PerC`, `aerobicEnergyPerC` |
| Saprotrophy | `saproCPerTick`, `saproAssimilationFraction`, `saproRespireStoreCPerTick` (energy/O2 use shared aerobic fields above) |
| Predation | `predatorBiteOrganicC`, `predatorBiteEnergy`, `predatorAssimilationFraction` |
| Debug | `enableGeodesicChemotacticSteering`, `enableGeodesicChemotaxisABDiagnostics` |

The existing initial-count and geodesic spawn-delay fields remain top-level. The pre-existing legacy `ventFe2PerTick` now captures and applies the authoritative `PlanetResourceMap.ventDissolvedFe2PlusPerTick`; previously the startup field was disconnected.

## Biology support and preserved semantics

| Controls | Geodesic | Legacy |
| --- | --- | --- |
| Population cap, reproduction, division costs/gates, biomass target/multiple/split, basal cost, maximum store | Used | Used |
| Founder delay | Used | Not used; hidden |
| Movement energy | Separate amoeboid/flagellar energy rates | `moveEnergyCostPerSecond` |
| Metabolism mutation/transition probabilities, biomass mutation | Not implemented; explanatory text | Existing paths/gates/order retained |
| Locomotion mutation/upgrade/anchoring | Used; existing Inspector diagnostic override still takes precedence | Used |
| Temperature adaptation, lethal margin, dormancy/reproduction thresholds, direct-damage/extreme thresholds | Used | Used |
| Exposure times/recovery and stress maintenance multiplier | Used | Not used; hidden |
| Temperature templates | Hydrogen range seeds normal founders; other seven displayed as inactive legacy templates | All eight seed the existing metabolism-specific traits |
| Hydrogen substrate/energy parameters | Used; `hydrogenotrophyStoreFraction` scales the whole reaction, with all withdrawn CO2 stored | Used; the fraction retains legacy store-yield behavior |
| Sulfur substrate/energy parameters | Used | Used |
| Sulfur store fraction/fallback respiration | Existing fixed carbon conversion; these controls hidden | Used |
| Methanogenesis substrate/energy parameters | Used with existing 0.85 energy factor and fixed 0.15 assimilation | Used with editable assimilation |
| Methanotrophy substrate/energy parameters | Used with existing fixed 0.15 assimilation | Used with editable assimilation |
| Photosynthetic uptake and energy/CO2 | Used; existing fixed carbon conversion | Used |
| Photosynthetic store fraction/night respiration; shared aerobic yields | Not used; hidden | Used |
| Fermentation, saprotrophy, predation | Reactions not implemented; explanatory text | Existing implemented fields exposed |

Geodesic per-tick reaction parameters use its existing **0.5 simulated-second reference tick**. Legacy uses its manager metabolism tick. Geological vent rates retain separate geodesic physical inventory and legacy per-tick fields. Energy per reaction tick, energy per unit CO2/carbon, substrate uptake and store/assimilation fractions have distinct labels. Legacy fermentation/methane reaction energy scales with the catabolized substrate share.

Normal geodesic founders remain hydrogenotrophs. Geodesic offspring inherit one authoritative metabolism and their parent's thermal traits, then use existing temperature/locomotion mutation. There is no new metabolism mutation implementation. Legacy same-metabolism births retain inherited thermal adaptations; its **existing** metabolism-changing birth path rebases onto the destination temperature template before mutation. That behavior is described in the UI and is unchanged. Startup ranges are templates, not species-wide hard survival limits.

The existing population-cap difference is retained: geodesic founder requests are independent of the reproduction cap, whereas legacy initial/spontaneous spawning also checks the cap.

Temperature controls display Celsius but retain Kelvin internally. Viewing or switching tabs does not convert values back to Kelvin; conversion occurs only after an actual edit. Finite min/max endpoints are ordered when editing finishes, on tab changes, loading and before Start. New preference endpoints follow the existing 150–500 K trait domain.

The following Inspector fields were deliberately **not exposed or connected** because the current code does not consume them: `saproO2PerC`, `saproEnergyPerC`, `nightRespirationO2PerC`, `nightRespirationEnergyPerC`, `predatorEnergyPerC`. Legacy saprotrophy/night respiration use `aerobicO2PerC` / `aerobicEnergyPerC`. Predation takes prey energy only when carbon intake falls short and retains the existing 50% energy transfer. Predator enablement, motility/attack gates, oxygen mechanics and advanced chemotaxis settings remain on their existing runtime components.

No modular evolution, extra metabolism ownership, missing reaction, oxygen-stress/UV placeholder or other gameplay mechanic was added.

## Persistence and reset behavior

- Saved startup schema is **23**. Existing atmosphere, hydrology and historical migrations are retained.
- New fields are stored flat in the existing saved-config wrapper. Before JSON overwrite, every field is seeded from the scene-backed defaults. Missing new fields in v22/older files, or in partial current files, do not become zero temperatures, energy yields, mutation rates or ocean coverage. Explicit zero values remain valid.
- Biology defaults and current values have independent clones. Existing saves containing no biology parameters follow the captured manager values. Newly connected planet fields follow PlanetGenerator.
- Applying config writes the planet fields before generation and manager fields before biology initialization. The applied config log includes sea-level settings, river enablement and all biology startup values, with the selected grid mode.
- With the existing `saveStartupConfigOnStart` persistence option enabled, Start still saves configuration; Back and normal application quit also save the edited setup draft. Re-entering setup retains the in-memory edits/tab/scroll positions. The existing load-saved option still controls restoration on application startup.
- Reset Defaults retains the existing normal/advanced split and additionally restores the new normal planet/biology fields. Debug chemotaxis switches retain their values. Reset Advanced retains its previous scope even where fields moved to Planet/Ocean/Atmosphere/Biology, and additionally restores sea-level mode/manual/Ocean World values. Reset Advanced does not reset biology reaction/evolution parameters. River target coverage and river master belong to Reset Defaults.
- Schema 23 permits an explicitly saved zero legacy Fe2+ source; the historical older-version Fe2+ fallback is retained for older files.
- The optional serialized prefab UI retains its existing layout and serialized bindings. Its public `PushToConfig()` API is preserved. It commits outgoing values before changing grids so vent units are not reinterpreted, commits before Back/quit, and accepts the same decimal separators. It preserves newly configured fields for which it has no control. The six-tab redesign is in the built-in IMGUI path; no prefab asset was migrated.

## Files

Modified:

- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupPanel.cs`
- `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs` (read-only scene-default snapshot helper only)

Added, with Unity `.meta` files:

- `Assets/Scripts/Core/Startup/SimulationStartupBiologyConfig.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.Defaults.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.PersistenceV23.cs`
- `Assets/Scripts/Core/Startup/SimulationStartupController.Tabs.cs`

Also added this document. No generation, reaction, mutation, inheritance, chemotaxis, hydrology, atmosphere, rendering or save-game algorithm was edited.

## Verification and manual checklist

Static verification only: Roslyn syntax parsing, source binding/field coverage inspection, and `git diff --check`. No Unity Editor, Play Mode, generation, simulation tests, build, benchmark or performance sweep was run.

Manual checks for Unity:

1. Open New Game at common window sizes, narrow aspect ratios and a short viewport. Check all six tabs, visible header/footer, selection highlight and independent scrolling, including the bottom of Biology and expanded infill settings.
2. Type partial, negative and decimal-comma values; switch tabs/grids. Verify valid edits persist, invalid/nonfinite text does not enter config, and reversed temperature endpoints are ordered. Check 0 °C corresponds to 273.15 K and viewing untouched ranges does not alter stored values.
3. Use Back → New Game, then restart the application. Check saved v23 values restore. Compare an old v22 startup file with customized Inspector biology/sea-level values: newly missing fields should use those scene defaults. Check deliberate zero mutation/energy/coverage/rate values survive saving.
4. In geodesic mode, compare target coverage, manual offset and Ocean World selections; inspect the generator values before generation and retained coverage afterward. Toggle rivers, lakes and inland-sea filtering, including their dependent controls. Check unsupported controls are explained in legacy mode.
5. Start Paused and inspect manager values: reproduction/costs, hydrogen temperature template/yield, temperature mutation, locomotion energy and A/B switches. Check geodesic-only fields and legacy-only reaction/evolution controls match the support table. In legacy mode inspect shared aerobic yields and Fe2+ vent startup application.
6. Check Reset Defaults and Reset Advanced independently, Start versus Start Paused, and the existing Back, main menu, save picker and loaded-game paths. If an optional prefab root is configured, verify its existing controls still work and switching grids does not mix vent-rate units.

The geodesic hydrogen activity/yield semantics and measured-transfer correction are detailed in [GeodesicHydrogenotrophyConservation.md](GeodesicHydrogenotrophyConservation.md).
