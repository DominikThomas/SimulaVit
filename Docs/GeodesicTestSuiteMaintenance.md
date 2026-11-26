# Geodesic test-suite maintenance

## Baseline and verification status

The 26 November 2026 user-run EditMode export, `TestResults.xml`, contains **813 cases in `SimulaVit.EditModeTests.dll`**. The XML records 735 passed, 74 failed across 22 fixtures, and 4 skipped. All 74 failure records contain a message and stack trace. The failure ledger at the end preserves the exact case names and arguments from the export.

The working tree on `codex/geodesic-performance` is the source baseline. The changes below were reviewed in source. A new Unity run is required to establish pass counts. The four overflow failures are still open. The P1 thermal implementation, reference path and default selection, the six P1 thermal cases, the nine vent thermal cases, and the five PlayMode recorder cases were preserved. Saved startup profiles, effective snapshots, the selected Hydrogen → Fermentation mutation probability of 0.02, and simulation scheduling retain their existing behavior.

## Suite classification

The inventory below covers passing, failing, and skipped cases in the user export, including mixed fixtures. Case totals are the original baseline before maintenance.

| Classification | Fixtures and scope |
| --- | --- |
| Active Geodesic | The Geodesic-prefixed EditMode fixtures cover atmosphere, ocean, chemistry, hydrology, spatial mapping, thermal systems, movement, founders, and packed biology. The full-world and parity fixtures have the separate invocation listed below. The isolated `GeodesicFoodEcologyExperiments` and `GeodesicLocomotionEcologyExperiments` exercise bounded synthetic fields and remain in Run All. `VentVisualizerVfxTests` uses Geodesic outlet effects, and its legacy cleanup check protects Geodesic isolation. |
| Shared and active | `PlanetSeedUtilityTests`, `ReplicatorManagerLifecycleTests`, `SimulationSpeedSemanticsTests`, and `SimulationPerformanceAnalyzerTests`. The separately run PlayMode `SimulationPerformanceRecorderTests` covers shared capture and recorder behavior. |
| Configuration and compatibility | `SimulationStartupConfigTests`, `SimulationStartupProfilesTests`, `SimulationStartupEvolutionTests`, and `VentStartupSettingsTests`. In mixed Geodesic fixtures this also includes founder startup round trips, the inland-elevation settings case, the two hydrology-mode migration cases, the first two lake settings cases, the uphill-debug round trip, and the lake integration Legacy isolation assertion. Older profile migration and still-supported Legacy startup controls remain covered. |
| Obsolete planet-mode coverage | `PlanetGridIndexingTests`, `PlanetResourceMapMathTests`, `ReplicatorSpawnSystemTests`, `TemperatureFitnessTests`, and the separate PlayMode `SimulationIntegrationTests`. Exact retired names and replacement coverage are listed below. Geodesic still builds render geometry with `IcosphereRenderMeshBuilder` and `IcosphereRenderGeometryCache`, so those tests remain active. The audit found no test dedicated solely to the original Icosphere planet mode. |
| Selected full-world experiments and benchmarks | Four `Geodesic*WorldExperiments` fixtures (five cases), `GeodesicEnvironmentalCpuParityTests` (four cases), and the separate Legacy `PerformanceBenchmarks` fixture (two PlayMode cases). |

## Reaction integration

All 38 failures reported `TargetParameterCountException` at the hydrogen or sulfur fixture's `CommitRequests` invocation. Fermentation delegates to the sulfur fixture. The current signature includes `photoEnergy` between the store cap and accounting switch. The profiling change samples from the runtime settings and step ordinal, so these isolated calls use disabled detailed profiling with zero photosynthetic inputs. The shared `GeodesicReactionTestPipeline` resolves each method against the parameter types of a delegate and names those types on lookup failure. It keeps habitat sampling, request construction, demand aggregation, availability, competition, measured commit, and accounting in production order. Existing substrate, transfer, energy, conservation, division, and recycling assertions remain in place. The additional locomotion calls to `BuildRequest`, `CommitRequests`, and `RunLifecycle` already match the current signatures.

## Founder emergence

The 11 failures share missing fixture setup. The resource catalog supplied real injection nodes, but the fixture omitted the experienced-temperature field and its source-node lookup. `TryFindFounderPosition` requires that lookup before it draws a candidate. Nine cases then reported missing founders, the same-seed case reported zero distinct vents, and the swap-back case indexed an empty list. The fixture now seeds matching resource and temperature outlets on the same active bottom nodes and builds the real thermal lookup. The synthetic weak vent anomaly keeps 350 K habitat viable and 100 K habitat unsuitable. Local CO₂ and H₂ still pass through the resource field, the rejection cache still has its production lifetime, and the cold check verifies actual thermal rejection. The deterministic and swap-back checks assert population sizes before inspecting packed state.

## Biology and environment expectations

| Fixture | Source finding and disposition |
| --- | --- |
| `GeodesicBiologyRuntimeTests` | The displacement is about 0.01146°. `Vector3.Angle` loses it when the single-precision dot product rounds to one. Both integration step sizes now have independent positive travel checks and a mutual agreement check using atan2 of cross and dot. The vertical opportunity rate was deliberately reduced from 0.015 to 0.0015 per second in `e0b7e6e`. The deterministic exponential check uses the mean scaled by the configured rate. The methanotrophy eligibility and inheritance checks now run independently of palette, and a separate rendering case checks the shared linear palette introduced in `b3fa96f`. |
| `GeodesicFoodGradientTests` | Methanotrophy needs the availability bit in the fixture (`7e23911`). Saprotrophy has a dissolved reaction (`643eaf8`). The test exercises reaction availability and makes the relevant substrates present, while still checking that these metabolisms do not get a hydrogen-taxis signal. Predation has its own capture path. |
| `GeodesicInlandUpliftIntegrationTests`, `GeodesicInlandUpliftTests` | `cc2583c` intentionally raised the upper bound to 5. The old clamp input of 1 is inside the new range. Both tests now check the in-range value and a separate above-limit input. |
| `GeodesicOceanIntegrationTests` | The inspection API returns the string label `Land`. The fixture now compares like types and still compares terrain, habitat loss, and basin classification. |

## Hydrology

| Cases | Source finding and disposition |
| --- | --- |
| Drainage delivery and the two hydrology terrain-mode cases | The initial seeded 123456 build reaches the same valid TIN spill witness before the logging or terrain-mode toggles execute. Basin 122 has floor 8.0054254531860352, spill 8.0071821212768555, spill vertex 478, downstream facet 770 and vertex 479. `TraceSpillOutlet` reports `RoutingFailure` with zero outlet samples and no downstream sample. These three cases remain active with their original inputs and assertions. |
| Lake integration | The synthetic bowl's flat outer shelf produces basin 0, whose floor 8.19999885559082 and spill 8.1999998092651367 differ by about 0.95 micro-units. The valid witness is vertex 854 on facet 520, with downstream vertex 59. It also fails before recording a downstream sample. The routing path considers all above-zero qualified depths before display thresholds, so a shelf artefact can reach the same strict overflow guard. This case remains active as a separate reproducer. |
| River grade | The old decimal terrace literals encode an actual rise slightly above two micro-units. The test uses a representable rise inside the original tolerance, asserts acceptance and rejection on opposite sides, and checks the measured corridor requirement. |
| River threshold startup | The NRE was at invocation of the removed `ApplyConfig` method. The fixture enumerates the current `ApplyConfigStages` iterator to completion, bypasses the generation cache for the generated fixture, and checks that startup thresholds affect the first TIN and only river qualification. |
| Uphill debug integration, line and ribbon cases | The same-threshold visual rebuild introduced in `3584f85` reused raw networks and skipped the rejected-path callback. The visual rebuild now replays failed published traces with observation disabled, or consumes existing snapshot evidence. It checks that a replay matches the published prefix so a relabeled confluence does not inherit a late trace rejection. The existing synthetic debug test compares the replay with fresh extraction and checks that diagnostic counters, paths, receivers, areas, and runoff are unchanged. The two original mesh and authority comparison cases remain active. |
| TIN coast geometry | The XML's radius of 7.959635219045949 is consistent with first contact at a retained facet handoff below the 7.96000004 sea radius. The current tracer deliberately supports both an exact intersection within a retained facet and a below-sea handoff at the same terrain point. The test now independently exercises a dry/wet pair on one facet, checks retained ownership at trace endpoints, requires the sampled handoff, and keeps both centreline checks. |

### Overflow follow-up

The reported `RoutingFailure` and missing downstream sample isolate the problem to the outlet's first attempted step, but they do not identify the tracer's internal failure kind. Reproduce against the original inputs and capture `TraceFailure.Kind`, the selected departure barycentrics, the running hydrology and visible-radius minima, the selected facet's completed-plane radius, and retained-ocean authority. Compare the recorded lower-star receiving facet and vertex with the permitted departure cone. Determine separately whether basin 0 came from the sub-epsilon shelf and whether basin 122 has a real non-uphill departure on that facet. Any production correction should exercise both witnesses and verify unique parcel ownership and conservation through the destination. The overflow safeguard currently throws before publishing fabricated terminal storage. It remains in place along with grade, spill, and conservation checks.

## Legacy retirement

The exact retired source and Unity metadata are recoverable from Git at `16f5cb8` using `git show 16f5cb8:<original path>`. The EditMode sources were under `Assets/Tests/EditMode`, and the retired smoke test was under `Assets/Tests/PlayMode`.

| Retired cases | Reason and Geodesic coverage |
| --- | --- |
| `PlanetGridIndexingTests.DirectionToCellIndex_SameDirection_ReturnsSameIndex`<br>`DirectionToCellIndex_OppositeDirections_Differ`<br>`DirectionToCellIndex_KnownAndRandomDirections_AreInBounds` | CubeSphere face indexing. `GeodesicHydrologyTopologyTests.GeodesicDirectionLookupMatchesNearestCellIndependentlyOfSearchOrigin` now checks Geodesic lookup against a brute-force nearest direction, bounds, independent starting cells, and antipodal separation. |
| `PlanetResourceMapMathTests.Add_ClampsAtZero_WhenApplyingNegativeDelta`<br>`AddAndGet_IgnoreInvalidCellIndices` | Legacy resource authority. Both setup failures looked for private `InitializeIfNeeded`, which is public now. Two Geodesic inventory tests on the live resource contract check bounded withdrawals and invalid cell/layer reads and writes. |
| `ReplicatorSpawnSystemTests.HandleSpontaneousSpawning_PreservesLowPopulationBehavior`<br>`HandleSpontaneousSpawning_DisablesWhenPopulationAtOrAboveDisableThreshold`<br>`HandleSpontaneousSpawning_RemainsDisabledUntilReenableThreshold`<br>`HandleSpontaneousSpawning_MultiStepLoopStopsWhenThresholdReachedMidStep` | Legacy low-population hysteresis, called after the pipeline's Geodesic early return. `GeodesicFounderEmergenceTests` covers the different Geodesic contract, including cap, event consumption, and grouped steps. |
| `TemperatureFitnessTests.ComputeTemperatureFitness_IsOneInsideOptimalBand`<br>`ComputeTemperatureFitness_DecreasesOutsideBand_AndReachesZeroBeyondLethalMargin` | Exercises a private manager helper with no production call. `GeodesicBiologyRuntimeTests` already checks the Geodesic Kelvin fitness band, shoulders, and lethal boundaries, and the six P1 cases exercise actual thermal reads. |
| `SimulationIntegrationTests.SceneLess_RunForSimulatedTime_RemainsStable` | Legacy PlayMode smoke uses default CubeSphere generation, `PlanetResourceMap`, and Legacy random spawn. Current automated coverage spans generated startup, packed population and founder lifecycle, and geodesic inventory finiteness and conservation. The opt-in world experiments cover longer coupled runs. Ordinary PlayMode no longer has this legacy scene-less smoke case. |

The mixed `ReplicatorManagerLifecycleTests` fixture stays active. In the recorded EditMode failure, the manager's runtime `Awake` had never installed its pipeline. The clock case now constructs and binds it for a Geodesic owner, asserts that reset discards the first delta, and checks the clock against the rendered delta while biology executes zero steps. The separate speed fixture still exercises positive deltas independently of the Editor frame clock. Legacy production code and supported startup compatibility remain available.

## Opt-in experiments and parity

The five full-world cases are NUnit `Explicit`. Their existing batch and disposable-project checks remain first in the experiment body. Select named cases from a closed disposable Unity project under `Temp/FoodTaxisAudit/UnityValidation-*` using the existing runner. Use a project you own that has the matching Unity packages restored. `-Graphics` is useful when evaluating rendered effects or when the fixture requires graphics.

```powershell
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFermentationWorldExperiments\.'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFoodWorldExperiments\.'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFounderEmergenceWorldExperiments\.RunSeed283GradualOnly$'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFounderEmergenceWorldExperiments\.CompareSeed283BatchAndGradualWithIdenticalBiology$'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicSulfurWorldExperiments\.'
```

The runner reports the process ID, XML, and log path under `Temp/SulfurAudit`. Archive each `filtered-results.xml` and log before invoking another filter, which uses the same filenames. Parity remains gated by the existing phase and workspace ownership checks. Run `./Tools/ValidateEnvironmentalCpu.ps1 -Phase reference` followed by `./Tools/ValidateEnvironmentalCpu.ps1 -Phase optimized` in the applicable reference and optimized source revisions, respectively, with the same owned workspace. Its reference input revision, results locations, and procedure are documented in the environmental CPU report. The two retained Legacy benchmarks are also `Explicit` and use PlayMode **Run Selected**. [Environmental parity procedure](GeodesicEnvironmentalCpuOptimization.md) · [Legacy benchmark procedure](../Assets/Tests/Performance/README.md)

## Manual validation

1. In Unity for this branch, use a blank scene so the startup fixture's temporary PlayMode transition does not start the ordinary planet scene. Let the project import changes, open **Window → General → Test Runner**, choose **EditMode**, and clear name or category filters.
2. First run these affected fixtures with **Run Selected**. For faster attribution, run the four groups separately.
   - Reactions and emergence. `GeodesicHydrogenotrophyIntegrationTests`, `GeodesicSulfurRuntimeTests`, `GeodesicFermentationRuntimeTests`, `GeodesicFounderEmergenceTests`.
   - Biology, movement and configuration. `GeodesicBiologyRuntimeTests`, `GeodesicFoodGradientTests`, `GeodesicInlandUpliftTests`, `GeodesicInlandUpliftIntegrationTests`, `GeodesicOceanIntegrationTests`, `ReplicatorManagerLifecycleTests`, `SimulationSpeedSemanticsTests`, `GeodesicVentPhysicalInventoryTests`, `GeodesicHydrologyTopologyTests`.
   - Hydrology repairs. `GeodesicRiverGradeTests`, `GeodesicRiverThresholdStartupTests`, `GeodesicRiverUphillDebugIntegrationTests`, `GeodesicRiverUphillDebugTests`, `GeodesicTinSurfaceTests`.
   - Open hydrology failures and existing guard coverage. `GeodesicDrainageDeliveryIntegrationTests`, `GeodesicHydrologyTerrainIntegrationTests`, `GeodesicLakeIntegrationTests`, `GeodesicTinLakeRoutingTests`.
3. Use **Run All** with filters clear and export the new XML separately from the 813-case baseline. Record the four spill cases by full name and keep the full failure and generation logs if they recur. The five explicit cases should not enter their full-world bodies, and the four environmental parity cases should report the existing opt-in skip.
4. In **PlayMode**, use **Run Selected** on `SimulationPerformanceRecorderTests` for the five shared recorder checks. In EditMode, the six P1 cases are in `GeodesicHydrogenotrophyIntegrationTests` and the nine vent cases are in `GeodesicVentThermalModelTests`. Compare startup and migration behavior through `SimulationStartupConfigTests`, `SimulationStartupEvolutionTests`, and `SimulationStartupProfilesTests` in the full run.

Expected inventory is **806 discovered EditMode cases** (813 minus 11 retirements plus four Geodesic additions). Five of the 806 are explicit and four reach the parity skip. That leaves **797 ordinary executable EditMode cases**. The exported XML may omit unselected explicit cases. PlayMode retains five ordinary recorder cases and two explicitly selected Legacy benchmarks, with one Legacy smoke case retired. These are source counts, pending the manual rerun.

## XML failure ledger

Each heading gives the original fixture, case count, and disposition. Parameter values are from the exported case names.

### `GeodesicBiologyRuntimeTests` (3)

Measurement and intentional behavior expectations.

- `FixedPassiveTickPreservesDisplacementWithinDocumentedTolerance`
- `MethanotrophyMutationRequiresAnEnabledHydrogenParentEdgeAndViableDaughterHabitat(True,Hydrogenotrophy,1.0f,1.0d,1.0d,True,True,True)`
- `VerticalScheduleIsDeterministicAndFinite`

### `GeodesicDrainageDeliveryIntegrationTests` (1)

Unresolved basin 122 overflow.

- `DiagnosticsRunOnceAfterFinalPassAndDoNotChangeHydrologyOrMeshes`

### `GeodesicFermentationRuntimeTests` (10)

Stale reaction invocation via sulfur fixture.

- `ActualNodeLayerOwnsOrganicWithdrawalAndBothProducts`
- `AuthoritativeLocalOxygenInhibitsThroughputWithoutBeingConsumed(0.02f,1.0f)`
- `AuthoritativeLocalOxygenInhibitsThroughputWithoutBeingConsumed(0.07f,0.825f)`
- `AuthoritativeLocalOxygenInhibitsThroughputWithoutBeingConsumed(0.12f,0.65f)`
- `ConfiguredAssimilationAndLegacyEnergyDenominatorRemainConservative(0.0f)`
- `ConfiguredAssimilationAndLegacyEnergyDenominatorRemainConservative(0.15f)`
- `ConfiguredAssimilationAndLegacyEnergyDenominatorRemainConservative(1.0f)`
- `SixteenFermentersShareFiniteFoodWithoutOverspending`
- `UnstorableCarbonIsFermentedRatherThanDiscardedOrDuplicated(10.0f)`
- `UnstorableCarbonIsFermentedRatherThanDiscardedOrDuplicated(9.999f)`

### `GeodesicFermentationWorldExperiments` (1)

Explicit disposable-batch experiment.

- `CompareSeed283WithOnlyFermentationMutationChanged`

### `GeodesicFoodGradientTests` (2)

Availability setup and changed reaction support.

- `AuditOtherMetabolismsWithoutPretendingHydrogenSensingSupportsThem(Methanotrophy,True)`
- `AuditOtherMetabolismsWithoutPretendingHydrogenSensingSupportsThem(Saprotrophy,False)`

### `GeodesicFoodWorldExperiments` (1)

Explicit disposable-batch experiment.

- `CompareSeed283WithIdenticalFixedStepsAndOnlySteeringChanged`

### `GeodesicFounderEmergenceTests` (11)

Missing experienced-temperature outlet lookup.

- `EmptyLocalInventoryRejectsWithoutInventingFoodAndCacheCanRecover(H2)`
- `EmptyLocalInventoryRejectsWithoutInventingFoodAndCacheCanRecover(CO2)`
- `FounderUsesInjectionBottomLayerAndOccupancyIncludesLayerAbove`
- `LaterEmergenceDoesNotResetTheMovementClockOrExistingSchedules`
- `NativeEmergenceIsIndependentOfOneOrHundredStepsPerRenderedFrame`
- `NativePlacementInitializesNormalPackedFounderAndPassiveSchedules`
- `OriginBirthAndDeathAccountingSurvivesSwapBack`
- `PopulationCapRejectsEventsRatherThanQueueingGuaranteedFounders`
- `SameSeedReproducesHabitatsTraitsAndPositionsWithoutDrawingOtherStreams`
- `SelectionUsesOnlyAuthenticViableInjectionNodesAndAllowsReplacement`
- `ViabilityUsesTheSameOrderedTemperatureTemplateAsFounderInitialization`

### `GeodesicFounderEmergenceWorldExperiments` (2)

Explicit disposable-batch experiments.

- `CompareSeed283BatchAndGradualWithIdenticalBiology`
- `RunSeed283GradualOnly`

### `GeodesicInlandUpliftIntegrationTests` (1)

Old upper-bound test input.

- `StartupDefaultMissingFieldsRoundTripAndResets`

### `GeodesicInlandUpliftTests` (1)

Old upper-bound test input.

- `NoCoastProducesNoUpliftAndInvalidMagnitudeIsSafe`

### `GeodesicLakeIntegrationTests` (1)

Unresolved basin 0 overflow.

- `TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain`

### `GeodesicOceanIntegrationTests` (1)

Inspection return type.

- `SameSeedFilterTogglePreservesTerrainAndRemovesMarineHabitat`

### `GeodesicRiverGradeTests` (1)

Single-precision terrace fixture.

- `DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted`

### `GeodesicRiverThresholdStartupTests` (1)

Removed synchronous startup method.

- `StartupThresholdsReachFirstBuildAndOnlyChangeRiverQualification`

### `GeodesicRiverUphillDebugIntegrationTests` (2)

Rejected-path overlay regression.

- `OffOnOffPreservesAcceptedMeshColoursRunoffTopologyMouthsLakesAndTerrain(True)`
- `OffOnOffPreservesAcceptedMeshColoursRunoffTopologyMouthsLakesAndTerrain(False)`

### `GeodesicSulfurRuntimeTests` (18)

Stale reaction invocation.

- `AuthoritativeLocalOxygenReducesAllThroughput(0.02f,1.0f)`
- `AuthoritativeLocalOxygenReducesAllThroughput(0.07f,0.8f)`
- `AuthoritativeLocalOxygenReducesAllThroughput(0.12f,0.6f)`
- `BatchCompetitionConservesSharedCarbonAndSulfur(16,False)`
- `BatchCompetitionConservesSharedCarbonAndSulfur(16,True)`
- `FeedingDivisionAndDeathRecycleExactlyOnce(0.3f)`
- `FeedingDivisionAndDeathRecycleExactlyOnce(0.5f)`
- `FeedingDivisionAndDeathRecycleExactlyOnce(0.7f)`
- `MissingEitherLocalInputStopsProduction(0.0d,1.0d)`
- `MissingEitherLocalInputStopsProduction(1.0d,0.0d)`
- `PhysicalHighVolumeSulfurCommitsMeasuredInputsBeforeCredit`
- `StoreCapacityLimitsDemandAndMeasuredWithdrawal(10.0f)`
- `StoreCapacityLimitsDemandAndMeasuredWithdrawal(9.999f)`
- `StoreFractionScalesWholeReactionWithoutDiscardedCarbon(0.0f)`
- `StoreFractionScalesWholeReactionWithoutDiscardedCarbon(0.4f)`
- `StoreFractionScalesWholeReactionWithoutDiscardedCarbon(1.0f)`
- `SulfurReactionReadsAndWritesTheOccupiedLayerOnly`
- `TurningOffConfiguredSulfurInhibitionRetainsFullThroughputAtHighOxygen`

### `GeodesicSulfurWorldExperiments` (1)

Explicit disposable-batch experiment.

- `CompareSeed283SulfurRichWithOnlySulfurMutationChanged`

### `GeodesicTinSurfaceTests` (1)

Retained-ocean handoff geometry.

- `SphereCoastUsesExactCompletedFacetAndProjectedPathRemainsDownhill`

### `GeodesicHydrogenotrophyIntegrationTests` (10)

Stale reaction invocation.

- `ActualDivisionAndDeathRecyclingPreserveWholeSystemCarbon(0.3f)`
- `ActualDivisionAndDeathRecyclingPreserveWholeSystemCarbon(0.5f)`
- `ActualDivisionAndDeathRecyclingPreserveWholeSystemCarbon(0.7f)`
- `FullStoreDoesNotCompeteForResourcesOrDiscardItsExistingCarbon`
- `HighVolumeTransfersAreMeasuredAndDiagnosticsDoNotChangeTheResult`
- `RealRequestCompetitionCommitPipelineBalancesSubstrateLimits(1.0d,1.0d,6.4f,0.008d)`
- `RealRequestCompetitionCommitPipelineBalancesSubstrateLimits(1.0d,0.004d,1.6f,0.002d)`
- `RealRequestCompetitionCommitPipelineBalancesSubstrateLimits(0.003d,1.0d,2.4f,0.003d)`
- `SixteenOrganismsShareBothLimitingInventoriesWithoutDuplicatingCarbon`
- `ZeroWithdrawalNeverAwardsEnergyInTheRealPipeline`

### `GeodesicHydrologyTerrainIntegrationTests` (2)

Unresolved basin 122 overflow.

- `RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(ReducedRidgeAndFineDetail)`
- `RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(FineDetailRemoved)`

### `PlanetResourceMapMathTests` (2)

Legacy setup reflection and retired authority.

- `Add_ClampsAtZero_WhenApplyingNegativeDelta`
- `AddAndGet_IgnoreInvalidCellIndices`

### `ReplicatorManagerLifecycleTests` (1)

Missing EditMode pipeline and old step expectation.

- `WorldClock_RemainsRunnableAcrossRepeatedBiologyDeinitialization`

## Baseline fixture counts

| EditMode fixture | Cases | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `GeodesicAbioticChemistryTests` | 20 | 0 | 0 |
| `GeodesicAtmosphereClimateTests` | 12 | 0 | 0 |
| `GeodesicAtmosphereTests` | 20 | 0 | 0 |
| `GeodesicBasinInfillTests` | 14 | 0 | 0 |
| `GeodesicBasinOverflowAuditTests` | 22 | 0 | 0 |
| `GeodesicBiologyRuntimeTests` | 87 | 3 | 0 |
| `GeodesicDrainageDeliveryIntegrationTests` | 1 | 1 | 0 |
| `GeodesicDrainageDeliveryTests` | 16 | 0 | 0 |
| `GeodesicDrainageTests` | 13 | 0 | 0 |
| `GeodesicEnvironmentalCpuParityTests` | 4 | 0 | 4 |
| `GeodesicFermentationEcologyTests` | 7 | 0 | 0 |
| `GeodesicFermentationRuntimeTests` | 28 | 10 | 0 |
| `GeodesicFermentationWorldExperiments` | 1 | 1 | 0 |
| `GeodesicFoodEcologyExperiments` | 7 | 0 | 0 |
| `GeodesicFoodGradientTests` | 23 | 2 | 0 |
| `GeodesicFoodSignalTests` | 15 | 0 | 0 |
| `GeodesicFoodWorldExperiments` | 1 | 1 | 0 |
| `GeodesicFounderEmergenceTests` | 21 | 11 | 0 |
| `GeodesicFounderEmergenceWorldExperiments` | 2 | 2 | 0 |
| `GeodesicChemistryTelemetryTests` | 10 | 0 | 0 |
| `GeodesicInlandUpliftIntegrationTests` | 3 | 1 | 0 |
| `GeodesicInlandUpliftTests` | 9 | 1 | 0 |
| `GeodesicLakeIntegrationTests` | 3 | 1 | 0 |
| `GeodesicLakeTests` | 19 | 0 | 0 |
| `GeodesicLocomotionEcologyExperiments` | 5 | 0 | 0 |
| `GeodesicLocomotionEvolutionDiagnosticsTests` | 6 | 0 | 0 |
| `GeodesicLocomotionMutationTests` | 8 | 0 | 0 |
| `GeodesicOceanConnectivityTests` | 13 | 0 | 0 |
| `GeodesicOceanDrainagePlanTests` | 20 | 0 | 0 |
| `GeodesicOceanFe2VisualTests` | 7 | 0 | 0 |
| `GeodesicOceanIntegrationTests` | 4 | 1 | 0 |
| `GeodesicOceanSedimentVisualTests` | 6 | 0 | 0 |
| `GeodesicPhysicalScaleTests` | 6 | 0 | 0 |
| `GeodesicPlanetCacheTests` | 3 | 0 | 0 |
| `GeodesicPredationTests` | 11 | 0 | 0 |
| `GeodesicRiverGradeTests` | 6 | 1 | 0 |
| `GeodesicRiverShorelineTests` | 1 | 0 | 0 |
| `GeodesicRiverThresholdStartupTests` | 1 | 1 | 0 |
| `GeodesicRiverUphillDebugIntegrationTests` | 3 | 2 | 0 |
| `GeodesicRiverUphillDebugTests` | 6 | 0 | 0 |
| `GeodesicRiverVisibleGradeAuditTests` | 4 | 0 | 0 |
| `GeodesicRiverVisibleGradeIntegrationTests` | 3 | 0 | 0 |
| `GeodesicRiverWidthProfileTests` | 2 | 0 | 0 |
| `GeodesicSulfurEcologyTests` | 6 | 0 | 0 |
| `GeodesicSulfurRuntimeTests` | 33 | 18 | 0 |
| `GeodesicSulfurWorldExperiments` | 1 | 1 | 0 |
| `GeodesicTaxisPolicyTests` | 5 | 0 | 0 |
| `GeodesicTinBasinTests` | 12 | 0 | 0 |
| `GeodesicTinHydrologyTests` | 17 | 0 | 0 |
| `GeodesicTinLakeGeometryTests` | 10 | 0 | 0 |
| `GeodesicTinLakeRoutingTests` | 8 | 0 | 0 |
| `GeodesicTinLakeSedimentTests` | 10 | 0 | 0 |
| `GeodesicTinSedimentCorridorTests` | 10 | 0 | 0 |
| `GeodesicTinSurfaceTests` | 3 | 1 | 0 |
| `GeodesicVentPhysicalInventoryTests` | 11 | 0 | 0 |
| `GeodesicVentSystemClustererTests` | 11 | 0 | 0 |
| `GeodesicVentThermalModelTests` | 9 | 0 | 0 |
| `GeodesicHorizontalMixingActivityTests` | 8 | 0 | 0 |
| `GeodesicHydrogenotrophyBalanceTests` | 14 | 0 | 0 |
| `GeodesicHydrogenotrophyDiagnosticsTests` | 5 | 0 | 0 |
| `GeodesicHydrogenotrophyIntegrationTests` | 16 | 10 | 0 |
| `GeodesicHydrologySnapshotTests` | 4 | 0 | 0 |
| `GeodesicHydrologyTerrainIntegrationTests` | 4 | 2 | 0 |
| `GeodesicHydrologyTerrainTests` | 7 | 0 | 0 |
| `GeodesicHydrologyTopologyTests` | 14 | 0 | 0 |
| `PlanetGridIndexingTests` | 3 | 0 | 0 |
| `PlanetResourceMapMathTests` | 2 | 2 | 0 |
| `PlanetSeedUtilityTests` | 2 | 0 | 0 |
| `ReplicatorManagerLifecycleTests` | 4 | 1 | 0 |
| `ReplicatorSpawnSystemTests` | 4 | 0 | 0 |
| `SimulationPerformanceAnalyzerTests` | 3 | 0 | 0 |
| `SimulationSpeedSemanticsTests` | 11 | 0 | 0 |
| `SimulationStartupConfigTests` | 32 | 0 | 0 |
| `SimulationStartupEvolutionTests` | 12 | 0 | 0 |
| `SimulationStartupProfilesTests` | 23 | 0 | 0 |
| `TemperatureFitnessTests` | 2 | 0 | 0 |
| `VentStartupSettingsTests` | 6 | 0 | 0 |
| `VentVisualizerVfxTests` | 18 | 0 | 0 |
