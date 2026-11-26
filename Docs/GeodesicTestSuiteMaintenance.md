# Geodesic test-suite maintenance

## Baseline and verification status

Both EditMode exports are from user runs on 26 November 2026. The original failure ledger at the end preserves the case names and arguments from `SimulaVit.EditModeTests.dll`. The [follow-up failure dispositions](#a-full-world-experiment-context) cover the ten remaining cases from the second export.

| Export | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Original `TestResults.xml` | 813 | 735 | 74 across 22 fixtures | 4 |
| Verified `TestResults(4).xml` after the first repair | **806** | **792** | **10** | **4** |

| Fixture | Verified passes |
| --- | ---: |
| `GeodesicHydrogenotrophyIntegrationTests` | 16/16, including all six P1-specific cases |
| `GeodesicSulfurRuntimeTests` | 33/33 |
| `GeodesicFermentationRuntimeTests` | 28/28 |
| `GeodesicFounderEmergenceTests` | 21/21 |
| `GeodesicVentThermalModelTests` | 9/9 |
| `SimulationStartupProfilesTests` | 23/23 |
| `GeodesicRiverGradeTests`, `GeodesicRiverThresholdStartupTests`, `GeodesicRiverUphillDebugIntegrationTests`, `GeodesicRiverUphillDebugTests`, `GeodesicTinSurfaceTests` | 6/6, 1/1, 3/3, 6/6, 3/3 |

The latest changes on `codex/geodesic-performance` have static review only. The four overflow failures are still open. P1 and its default mode, startup profile persistence, the effective configuration snapshot, biological reaction parameters and the selected Hydrogen → Fermentation mutation probability of 0.02 are preserved. The verified export covers EditMode. The five existing PlayMode recorder cases are outside that count.

## PlayMode results

The two original PlayMode exports from 26 November 2026 each contain seven cases across the recorder and Legacy benchmark assemblies. Both benchmarks have the `Explicit` run state and skip label in those exports. After retirement, the standalone run with GPU lifetime diagnostics enabled contains only the five recorder cases.

| Environment | Export | Passed | Failed | Explicitly skipped |
| --- | --- | ---: | ---: | ---: |
| Unity Editor | `TestResults(6).xml` | 5 | 0 | 2 |
| Standalone Windows test player | `TestResults(7).xml` | 5 | 0 | 2 |
| Standalone Windows test player with GPU lifetime diagnostics | `TestResults.xml` | 5 | 0 | 0 |

The original Windows player log identifies a Unity 6000.3.29f1 Mono test player on Direct3D 11 and records the same capture CSV names as `TestResults(7).xml`. The ComputeBuffer finalizer warning appears during shutdown. The next line contains Unity's `allocatedMemory=589841` summary, which spans engine memory labels including `Manager` and `JobScheduler`.

## PlayMode benchmark retirement

`PerformanceBenchmarks.PredatorOff_Benchmark_WithinRegressionGate` and `PerformanceBenchmarks.PredatorOn_Benchmark_WithinRegressionGate` ran the same code-built `PerfBenchmarkScene`. Both use `PlanetGenerator`'s default Legacy CubeSphere mode, a `PlanetResourceMap`, and a 5,000-agent Legacy `ReplicatorManager`, with seed 424242 and cube face resolution 32. The tests varied the Legacy predator toggle. Their gates compared mean rendered-frame time and GC allocation against CubeSphere-specific thresholds. P95 and population counts were printed as context.

The fixture, standalone `SimulaVit.PerformanceTests` assembly, baseline JSON, README, and their Unity metadata are retired. The baseline and assembly had no active Geodesic references, and no supporting project script invoked either case. All retired files are recoverable from `47a8a59` under `Assets/Tests/Performance`. The five recorder tests still cover marker aggregation, frame collection, CSV output and observational behavior. The Geodesic benchmark profiles and capture procedure still measure rendered frame time, p95, GC allocation, and population and predation work. The old gate values applied only to the retired workload. [Geodesic performance benchmarks](GEODESIC_PERFORMANCE_BENCHMARKS.md)

## GPU buffer ownership audit

Project source under `Assets` and `Packages` contains no `ComputeBuffer` or `GraphicsBuffer` allocation or wrapper acquisition. The search covered runtime, tests, Editor code, static fields, mesh and skinned-mesh buffer getters, indirect draw APIs and native-buffer pointer access. It found no project buffer owner with capacity replacement, partial allocation, or release paths.

| Path | Ownership and cleanup finding |
| --- | --- |
| Recorder tests and their indirect setup | The tests use `SimulationPerformanceAnalyzer`, temporary CSV files, and `ProfilerRecorder` handles. The one `ReplicatorManager` is added to an inactive object for packed-state metrics. It receives an uninitialized biology runtime and does not initialize a world or draw organisms. The analyzer disposes its profiling recorders in `OnDisable`, and fixture cleanup destroys its temporary objects. |
| Project rendering and world lifecycle | The replicator renderer submits `Graphics.DrawMeshInstanced` with managed arrays and material properties. Planet regeneration and return to menu clear world components, meshes, materials, and other scene resources through their existing teardown paths. No managed GPU buffer wrapper transfers from those paths into project statics. Disable, destroy, scene unload, shutdown, and domain reload reveal no project buffer release site. |
| Unity Test Framework and rendering packages | The Windows run uses a test bootstrap scene. The configured standalone quality points to the PC URP asset. URP owns shared light and shader buffers and calls renderer disposal, `ShaderData.Dispose`, and `ConstantBuffer.ReleaseAll` during pipeline disposal. Cookie buffers have package-level capacity and disposal paths. Probe-volume and GPU Resident Drawer paths are gated by settings, and the checked-in PC asset has both disabled. The diagnostics confirm entry into pipeline disposal. Buffer ownership and disposal completion remain unconfirmed. |

### Windows shutdown trace

The supplied local `Player.log` matches the five-case `TestResults.xml` through its capture CSV name. It records `EnabledWithStackTrace` on Direct3D 11 with `PC_RPAsset`, then URP creation, application quitting, and the start of URP disposal. The ComputeBuffer finalizer warning appears after the disposal-start marker during shutdown. The trace includes call stacks for the diagnostic messages. It produced no allocation stack identifying the buffer owner.

All three inspected standalone shutdown records report **589841 bytes** of remaining memory and identical byte counts in all 17 reported memory categories. That is consistent across these short PlayMode runs. The totals do not attribute the buffer to a particular owner or measure memory growth during repeated world regeneration.

**Disposition.** Unresolved, low-priority shutdown resource cleanup warning. Unity or a rendering package could own the buffer. Ownership remains unconfirmed. Keep the opt-in trace available and defer further investigation until an application-owned allocation is identified or memory is shown to accumulate during repeated world regeneration. This follow-up changes documentation only.

### Manual PlayMode verification

1. With the diagnostics menu unchecked, run **Run All** in the Test Runner's **PlayMode** tab in the Editor and again with the runner set to the standalone Windows player. Expect five recorder cases. Export both XML results and save the standalone `Player.log` before the next run overwrites it.
2. If new evidence reopens the warning, check **Tools → Diagnostics → Trace GPU Buffer Lifetime in Windows Test Players** for a separate attribution run. The process flag is inherited by the development player, which enables Unity's native leak stack tracking during subsystem registration. In `Player.log`, look for **`GeodesicGpuBufferLifetime`** to confirm enablement and align scene unload, shutdown and pipeline disposal events. Preserve the full native leak report and any allocation stacks, which Unity emits under its own labels. The pipeline disposal message marks the start of disposal.
3. Uncheck the menu afterward. If the player lacks the enablement marker, start the Editor from a shell with `SIMULAVIT_GPU_BUFFER_LIFETIME=1` in its environment and repeat the diagnostic run. Unity can substitute its native leak report for the ordinary finalizer warning when stack tracking is active. Compare the ordinary warning using the trace-disabled run. The diagnostic adds no buffer allocation, forced collection, per-frame hook, or GPU synchronization. [Unity native leak detection](https://docs.unity.com/en-us/engine/6000.3/script-reference/unity/collections/nativeleakdetection/mode) · [Unity ComputeBuffer allocation and release](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.3/Runtime/Export/Shaders/ComputeBuffer.bindings.cs) · [Render-pipeline disposal event](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/renderpipelinemanager/activerenderpipelinedisposed)

The PlayMode retirement and diagnostic changes were reviewed statically. No Unity runs or builds were performed during this audit.

## A. Full-world experiment context

The XML records `runstate="Explicit"` on all five cases below, and their stacks reach their first batch assertions. The installed Unity Test Framework 1.6.0 uses an empty filter for unfiltered **Run All**, and its test command builder permits explicit methods when the effective filter matches them explicitly. Its root-selection path can use an all-groups pattern that deliberately includes explicit cases. The XML does not identify the effective filter or UI path. It does establish that discovery preserved the designation and that it did not block execution in this run.

| World experiment | Disposition from source review |
| --- | --- |
| `GeodesicFermentationWorldExperiments.CompareSeed283WithOnlyFermentationMutationChanged` | Interactive runtime skip |
| `GeodesicFoodWorldExperiments.CompareSeed283WithIdenticalFixedStepsAndOnlySteeringChanged` | Interactive runtime skip |
| `GeodesicFounderEmergenceWorldExperiments.CompareSeed283BatchAndGradualWithIdenticalBiology` | Interactive runtime skip |
| `GeodesicFounderEmergenceWorldExperiments.RunSeed283GradualOnly` | Interactive runtime skip |
| `GeodesicSulfurWorldExperiments.CompareSeed283SulfurRichWithOnlySulfurMutationChanged` | Interactive runtime skip |

Each coroutine keeps `Explicit` and calls the shared context check before setup, scene changes, or file work. An interactive invocation uses NUnit `Assert.Ignore`. A batch attempt outside the existing disposable project path still fails, and the scene draft-writer checks in the fermentation, founder and sulfur experiments still run before entering PlayMode. The named batch filters and experiment assertions are unchanged.

## B. Passive movement displacement

`GeodesicBiologyRuntimeTests.FixedPassiveTickPreservesDisplacementWithinDocumentedTolerance` reported a **0.000190338877°** endpoint gap against a **0.0001°** bound. The original `Vector3.Angle` hid small rotations when the dot product rounded to one. The first repair's atan2 exposed a second issue. The step updates the tangent before advancing, so one 0.1-second step travels with the full heading change, whereas twenty 0.005-second steps travel with progressively updated headings. Their constant turn rate, initial frame and nominal elapsed times are equivalent. The helper does not evolve wander or draw RNG. Production refreshes the seeded wander target on its own schedule and evolves the rate before kinematic integration.

For small angles, the two paths' mean headings differ by `abs(w) × (T − dt) / 2`. With speed `v = 0.002 rad/s`, `T = 0.1 s`, `dt = 0.005 s` and `abs(w) = 0.35 rad/s`, the predicted endpoint separation `v × T × abs(w) × (T − dt) / 2` is about **0.000190508°**. The observed gap is consistent with that step ordering. The new test compares against the prediction within **0.000002°**. This covers the higher-order terms for headings up to 0.035 radians and float integration roundoff. It applies the original 0.0001° bound to straight-line timestep consistency and to each displacement magnitude. Angular measurement uses double-precision products and atan2, and the test checks both turn signs, forward direction, and unit radius. Existing scheduling tests remain separate. Production movement and RNG behavior are unchanged. [Movement integration and measurement contract](GeodesicReplicatorMovementInvestigation.md)

## C. Lake overflow routing

The rerun reproduced the four original guard failures with the same spill witnesses. [Hydrology findings and diagnostic scope](#overflow-follow-up)

## Suite classification

The inventory below covers passing, failing, and skipped cases in the user export, including mixed fixtures. Case totals are the original baseline before maintenance.

| Classification | Fixtures and scope |
| --- | --- |
| Active Geodesic | The Geodesic-prefixed EditMode fixtures cover atmosphere, ocean, chemistry, hydrology, spatial mapping, thermal systems, movement, founders, and packed biology. The full-world and parity fixtures have the separate invocation listed below. The isolated `GeodesicFoodEcologyExperiments` and `GeodesicLocomotionEcologyExperiments` exercise bounded synthetic fields and remain in Run All. `VentVisualizerVfxTests` uses Geodesic outlet effects, and its legacy cleanup check protects Geodesic isolation. |
| Shared and active | `PlanetSeedUtilityTests`, `ReplicatorManagerLifecycleTests`, `SimulationSpeedSemanticsTests`, and `SimulationPerformanceAnalyzerTests`. The separately run PlayMode `SimulationPerformanceRecorderTests` covers shared capture and recorder behavior. |
| Configuration and compatibility | `SimulationStartupConfigTests`, `SimulationStartupProfilesTests`, `SimulationStartupEvolutionTests`, and `VentStartupSettingsTests`. In mixed Geodesic fixtures this also includes founder startup round trips, the inland-elevation settings case, the two hydrology-mode migration cases, the first two lake settings cases, the uphill-debug round trip, and the lake integration Legacy isolation assertion. Older profile migration and still-supported Legacy startup controls remain covered. |
| Obsolete planet-mode coverage | `PlanetGridIndexingTests`, `PlanetResourceMapMathTests`, `ReplicatorSpawnSystemTests`, `TemperatureFitnessTests`, and the separate PlayMode `SimulationIntegrationTests`. Exact retired names and replacement coverage are listed below. Geodesic still builds render geometry with `IcosphereRenderMeshBuilder` and `IcosphereRenderGeometryCache`, so those tests remain active. The audit found no test dedicated solely to the original Icosphere planet mode. |
| Selected full-world experiments and parity | Four `Geodesic*WorldExperiments` fixtures (five cases) and `GeodesicEnvironmentalCpuParityTests` (four cases). The separate two-case Legacy benchmark fixture is retired. |

## Reaction integration

All 38 failures reported `TargetParameterCountException` at the hydrogen or sulfur fixture's `CommitRequests` invocation. Fermentation delegates to the sulfur fixture. The current signature includes `photoEnergy` between the store cap and accounting switch. The profiling change samples from the runtime settings and step ordinal, so these isolated calls use disabled detailed profiling with zero photosynthetic inputs. The shared `GeodesicReactionTestPipeline` resolves each method against the parameter types of a delegate and names those types on lookup failure. It keeps habitat sampling, request construction, demand aggregation, availability, competition, measured commit, and accounting in production order. Existing substrate, transfer, energy, conservation, division, and recycling assertions remain in place. The additional locomotion calls to `BuildRequest`, `CommitRequests`, and `RunLifecycle` already match the current signatures.

## Founder emergence

The 11 failures share missing fixture setup. The resource catalog supplied real injection nodes, but the fixture omitted the experienced-temperature field and its source-node lookup. `TryFindFounderPosition` requires that lookup before it draws a candidate. Nine cases then reported missing founders, the same-seed case reported zero distinct vents, and the swap-back case indexed an empty list. The fixture now seeds matching resource and temperature outlets on the same active bottom nodes and builds the real thermal lookup. The synthetic weak vent anomaly keeps 350 K habitat viable and 100 K habitat unsuitable. Local CO₂ and H₂ still pass through the resource field, the rejection cache still has its production lifetime, and the cold check verifies actual thermal rejection. The deterministic and swap-back checks assert population sizes before inspecting packed state.

## Biology and environment expectations

| Fixture | Source finding and disposition |
| --- | --- |
| `GeodesicBiologyRuntimeTests` | The rerun passed 87/88 cases. The remaining displacement case is covered in section B. The vertical opportunity rate was deliberately reduced from 0.015 to 0.0015 per second in `e0b7e6e`. Its exponential check uses the mean scaled by the configured rate. The methanotrophy eligibility and inheritance checks run independently of the shared linear palette introduced in `b3fa96f`. |
| `GeodesicFoodGradientTests` | Methanotrophy needs the availability bit in the fixture (`7e23911`). Saprotrophy has a dissolved reaction (`643eaf8`). The test exercises reaction availability and makes the relevant substrates present, while still checking that these metabolisms do not get a hydrogen-taxis signal. Predation has its own capture path. |
| `GeodesicInlandUpliftIntegrationTests`, `GeodesicInlandUpliftTests` | `cc2583c` intentionally raised the upper bound to 5. The old clamp input of 1 is inside the new range. Both tests now check the in-range value and a separate above-limit input. |
| `GeodesicOceanIntegrationTests` | The inspection API returns the string label `Land`. The fixture now compares like types and still compares terrain, habitat loss, and basin classification. |

## Hydrology

| Cases | Source finding and disposition |
| --- | --- |
| `GeodesicDrainageDeliveryIntegrationTests.DiagnosticsRunOnceAfterFinalPassAndDoNotChangeHydrologyOrMeshes` | Open. The seed-123456 Visible baseline reaches basin 122 before the logging toggle. Floor 8.0054254531860352, spill 8.0071821212768555, spill vertex 478, facet 770, and downstream vertex 479. The intermediate routing logs contain numerous visible-step rejections. |
| `GeodesicHydrologyTerrainIntegrationTests.RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(ReducedRidgeAndFineDetail)` and `(FineDetailRemoved)` | Both open. Basin 122 fails on the initial **Visible** build with 642 samples, before either alternative terrain mode runs. |
| `GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain` | Open. Basin 0 has floor 8.19999885559082 and spill 8.1999998092651367, exactly one float ULP apart at this radius. The valid witness is spill vertex 854 on facet 520 and downstream vertex 59. Geometry suppressed descendants 1, 5, 11, 54, 779, 1052 and 1271. |
| River grade | The old decimal terrace literals encode an actual rise slightly above two micro-units. The test uses a representable rise inside the original tolerance, asserts acceptance and rejection on opposite sides, and checks the measured corridor requirement. |
| River threshold startup | The NRE was at invocation of the removed `ApplyConfig` method. The fixture enumerates the current `ApplyConfigStages` iterator to completion, bypasses the generation cache for the generated fixture, and checks that startup thresholds affect the first TIN and only river qualification. |
| Uphill debug integration, line and ribbon cases | The same-threshold visual rebuild introduced in `3584f85` reused raw networks and skipped the rejected-path callback. The visual rebuild now replays failed published traces with observation disabled, or consumes existing snapshot evidence. It checks that a replay matches the published prefix so a relabeled confluence does not inherit a late trace rejection. The existing synthetic debug test compares the replay with fresh extraction and checks that diagnostic counters, paths, receivers, areas, and runoff are unchanged. The two original mesh and authority comparison cases remain active. |
| TIN coast geometry | The XML's radius of 7.959635219045949 is consistent with first contact at a retained facet handoff below the 7.96000004 sea radius. The current tracer deliberately supports both an exact intersection within a retained facet and a below-sea handoff at the same terrain point. The test now independently exercises a dry/wet pair on one facet, checks retained ownership at trace endpoints, requires the sampled handoff, and keeps both centreline checks. |

### Overflow follow-up

The lower-star sweep records a downstream facet and vertex in the winning component before union. That supplies a topological witness at or below the spill. It does not establish a usable first segment across the completed terrain. Runtime lake routing uses zero depth and area thresholds to build its logical geometry, with ocean exclusions and hierarchy suppression, then filters visible lake geometry separately. An accepted outlet must depart through the recorded facet without reentering its source lake. The tracer compares both linear hydrology and visible heights and the **minimum radius along the completed planar facet**. A segment can descend at both endpoints yet rise from an interior chord minimum. The current trace selects its steepest feasible descent before applying those grade checks. The failed exports show no accepted downstream sample, but do not identify the rejected step or establish whether a different allowed departure would pass.

Basin 122 is about 0.00176 units deep, far above the roughly two-micro-unit routing epsilon. Basin 0 is consistent with float roundoff on the synthetic bowl's nominal 8.2-unit shelf. The fixture scales float unit directions and final routing recomputes their magnitudes, and the XML gives a maximum of 8.20000076. The lower-star sweep compares float heights exactly, and the flat-face rule uses one quarter of routing epsilon, smaller than one ULP at 8.2. This explains why such relief can enter lake qualification, but it does not prove basin 0's rejected-step type or establish that discarding it would preserve parcel delivery.

The sidecar can already carry logical flow without a visible ribbon through a verified same-shoreline lake edge, a retained-ocean spill contact, or a below-threshold outlet. Neither failed trace supplies the corresponding downstream proof. This follow-up adds an Editor-only, session-scoped diagnostic that replays only failed spill traces, with observation disabled. It logs the exact failure kind, accepted sample count, barycentrics, running minima, rim ULP, retained-ocean authority, and the witness and descent candidates with their first-step grades. Routing, terrain, qualification, conservation, and the existing exception remain unchanged. `GeodesicTinLakeRoutingTests.DownhillSpillEdgeCanFailCompletedFacetGrade` adds two synthetic cases that exercise chord-grade rejection with one-ULP and larger drops and check that the diagnostic probes leave trace counters unchanged. These are source-reviewed examples of a possible mechanism, while the two real witnesses still need capture. [Basin topology](TinBasins.md) · [Lake geometry](TinLakes.md) · [TIN lake routing](TinLakeRouting.md)

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

The five full-world cases remain NUnit `Explicit`. If the interactive runner selects them, they report an intentional skip at the start of the coroutine. Select named cases from a closed disposable Unity project under `Temp/FoodTaxisAudit/UnityValidation-*` using the existing runner. Use a project you own that has the matching Unity packages restored. `-Graphics` is useful when evaluating rendered effects or when the fixture requires graphics.

```powershell
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFermentationWorldExperiments\.'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFoodWorldExperiments\.'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFounderEmergenceWorldExperiments\.RunSeed283GradualOnly$'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicFounderEmergenceWorldExperiments\.CompareSeed283BatchAndGradualWithIdenticalBiology$'
./Tools/ValidateSulfur.ps1 -NativeProject '<absolute-disposable-project>' -TestFilter '^GeodesicSulfurWorldExperiments\.'
```

The runner reports the process ID, XML, and log path under `Temp/SulfurAudit`. Archive each `filtered-results.xml` and log before invoking another filter, which uses the same filenames. Parity remains gated by the existing phase and workspace ownership checks. Run `./Tools/ValidateEnvironmentalCpu.ps1 -Phase reference` followed by `./Tools/ValidateEnvironmentalCpu.ps1 -Phase optimized` in the applicable reference and optimized source revisions, respectively, with the same owned workspace. Its reference input revision, results locations, and procedure are documented in the environmental CPU report. [Environmental parity procedure](GeodesicEnvironmentalCpuOptimization.md)

## Manual validation

1. In a blank scene on this branch, open **Window → General → Test Runner → EditMode**. Select `GeodesicFermentationWorldExperiments`, `GeodesicFoodWorldExperiments`, `GeodesicFounderEmergenceWorldExperiments`, and `GeodesicSulfurWorldExperiments` individually and use **Run Selected** to verify that all five cases report a skip in the interactive Editor.
2. Run `GeodesicBiologyRuntimeTests` and `GeodesicTinLakeRoutingTests`. The former includes the passive displacement case and the latter includes the two new chord-grade tests and existing conservation and logical-delivery coverage.
3. In Unity, check **Tools → Hydrology → Trace Failed TIN Lake Spills**. Run these two EditMode cases individually. With informational messages visible in the Console, copy each full entry matching **`TIN-LAKE-SPILL-TRACE`**.
   - `GeodesicHydrologyTerrainIntegrationTests.RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(FineDetailRemoved)` for the shared basin 122 reproduction.
   - `GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain` for basin 0.
4. Turn the trace menu off. Clear Test Runner filters, use **EditMode → Run All**, and export the XML separately from `TestResults(4).xml`. The full suite includes the other basin 122 cases in `GeodesicDrainageDeliveryIntegrationTests` and `GeodesicHydrologyTerrainIntegrationTests`. If the runner includes the explicit world experiments, expect five interactive skips alongside the four environmental parity skips.
5. For the separate recorder baseline, use the [PlayMode verification procedure](#manual-playmode-verification).

Expected inventory after this follow-up is **808 discovered EditMode cases**, including the two new chord-grade cases. Five are explicit and four reach the environmental parity skip, leaving **799 ordinary executable cases**. The XML may omit unselected explicit cases or record them as skipped. The four unresolved overflow cases are still among the executable cases. These are source counts pending the manual rerun.

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
