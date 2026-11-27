# Geodesic test-suite maintenance

## Baseline and verification status

The EditMode exports are from user runs on 26 November 2026. The original failure ledger preserves the case names and arguments from `SimulaVit.EditModeTests.dll`. The [follow-up failure dispositions](#a-full-world-experiment-context) cover the ten cases remaining after the first repair.

| Export | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Original `TestResults.xml` | 813 | 735 | 74 across 22 fixtures | 4 |
| Verified `TestResults(4).xml` after the first repair | **806** | **792** | **10** | **4** |
| Authoritative pre-change baseline for the lake outlet investigation, `TestResults(8).xml` | **808** | **795** | **4** | **9** |
| Full EditMode run after the first spherical routing correction, `TestResults(20261126-122039).xml` | **818** | **800** | **9** | **9** |

The full `TestResults(8).xml` run took **15.71 seconds**. `FixedPassiveTickPreservesDisplacementWithinDocumentedTolerance` passed. The nine intentional skips comprise five full-world experiments and four environmental parity cases. The targeted `TestResults(5).xml` rerun discovered the same 808 EditMode cases and ran four. Its four exception messages and failing stack frames match the full baseline. The four failures all occur in `GeodesicTinLakeRouting.TraceSpillOutlet` during `Build`.

Comparison by full test name confirms that the 818-case export retained every case and passed all ten added cases. The original four failures moved beyond their initial outlet exceptions. Five previously passing cases regressed, and all ten existing `GeodesicTinLakeSedimentTests` passed. The nine downstream failure dispositions and remaining verification are documented below. [Downstream regressions](#downstream-regressions-after-the-first-spherical-correction)

The user reports **824 discovered and 24 selected** in `TestResults(20261126-135519).xml`, with **14 passed, 1 failed and 9 intentionally skipped**. The pass count includes the other eight prior failures and all six new regressions. The supplied local `TestResults.xml` currently contains a later single-case rerun. It independently reproduces the remaining assertion and provides the lakes-on and lakes-off traces below.

| Hydrology fixture in `TestResults(8).xml` | Passed |
| --- | ---: |
| `GeodesicTinLakeRoutingTests` | 10/10 |
| `GeodesicTinLakeGeometryTests` | 10/10 |
| `GeodesicDrainageDeliveryTests` | 16/16 |
| `GeodesicBasinOverflowAuditTests` | 22/22 |
| `GeodesicRiverGradeTests` | 6/6 |

| Fixture | Verified passes |
| --- | ---: |
| `GeodesicHydrogenotrophyIntegrationTests` | 16/16, including all six P1-specific cases |
| `GeodesicSulfurRuntimeTests` | 33/33 |
| `GeodesicFermentationRuntimeTests` | 28/28 |
| `GeodesicFounderEmergenceTests` | 21/21 |
| `GeodesicVentThermalModelTests` | 9/9 |
| `SimulationStartupProfilesTests` | 23/23 |
| `GeodesicRiverGradeTests`, `GeodesicRiverThresholdStartupTests`, `GeodesicRiverUphillDebugIntegrationTests`, `GeodesicRiverUphillDebugTests`, `GeodesicTinSurfaceTests` | 6/6, 1/1, 3/3, 6/6, 3/3 |

On `codex/geodesic-performance`, the user verified the outlet diagnostics, the first spherical routing correction in the 818-case run, and the downstream changes in the targeted 824-case inventory. The final lake assertion revision has static review only. Post-retirement PlayMode verification by the user reports five recorder cases passed in both the Editor and standalone Windows Player, with zero failures or skips. The GPU shutdown warning remains deferred and its opt-in diagnostics remain available. The maintenance preserves P1 and its default mode, startup profile persistence, the effective configuration snapshot, biological reaction parameters, and the selected Hydrogen → Fermentation mutation probability of 0.02.

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
| `GeodesicDrainageDeliveryIntegrationTests.DiagnosticsRunOnceAfterFinalPassAndDoNotChangeHydrologyOrMeshes` | The 808-case baseline stopped at seed-123456 basin 122, before the logging toggle. The 818-case run reached sediment mesh construction. |
| `GeodesicHydrologyTerrainIntegrationTests.RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(ReducedRidgeAndFineDetail)` and `(FineDetailRemoved)` | Both moved from basin 122 to sediment mesh construction on the initial **Visible** build with 642 samples. |
| `GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain` | The 808-case baseline stopped on basin 0's witness from spill vertex 854 on facet 520 to vertex 59. The 818-case run reached the lake graph cycle guard and reported eight self-edges from rounding-scale shelf dips. |
| River grade | The old decimal terrace literals encode an actual rise slightly above two micro-units. The test uses a representable rise inside the original tolerance, asserts acceptance and rejection on opposite sides, and checks the measured corridor requirement. |
| River threshold startup | The NRE was at invocation of the removed `ApplyConfig` method. The fixture enumerates the current `ApplyConfigStages` iterator to completion, bypasses the generation cache for the generated fixture, and checks that startup thresholds affect the first TIN and only river qualification. |
| Uphill debug integration, line and ribbon cases | The same-threshold visual rebuild introduced in `3584f85` reused raw networks and skipped the rejected-path callback. The visual rebuild now replays failed published traces with observation disabled, or consumes existing snapshot evidence. It checks that a replay matches the published prefix so a relabeled confluence does not inherit a late trace rejection. The existing synthetic debug test compares the replay with fresh extraction and checks that diagnostic counters, paths, receivers, areas, and runoff are unchanged. The two original mesh and authority comparison cases remain active. |
| TIN coast geometry | The XML's radius of 7.959635219045949 is consistent with first contact at a retained facet handoff below the 7.96000004 sea radius. The current tracer deliberately supports both an exact intersection within a retained facet and a below-sea handoff at the same terrain point. The test now independently exercises a dry/wet pair on one facet, checks retained ownership at trace endpoints, requires the sampled handoff, and keeps both centreline checks. |

### Overflow follow-up

#### What the previous changes verified

`47a8a59` restored rejected raw-river diagnostics when reusing extracted networks and adjusted the tests for the existing grade and retained-ocean contracts. `f007e27` added the opt-in spill replay and two synthetic spherical grade cases. Neither change altered the spill chooser, basin qualification, or the facet geometry used by the grade checks. Both XML exports report `validSpillWitness=True`, `traceTermination=RoutingFailure`, and no downstream sample for all four cases. Neither contains the previous opt-in spill trace.

#### Shared coordinates and different height profiles

The lower-star sweep uses authoritative scalar vertex heights and records a downstream facet and vertex on the winning component before union. The witness proves the recorded vertex is at or below the spill. A continuous first step also has to leave through the recorded facet with its midpoint outside the source lake. The tracer transfers barycentric weights across shared vertices and edges, chooses the steepest feasible scalar descent, and then grades it. A steepest descent can fail this grade even if another candidate was geometrically feasible.

Both grade checks use the same `TinPoint` endpoints. The downhill gradient comes from barycentric interpolation of hydrology heights on the unit-direction chord plane. The linear visible check interpolates the visible vertex radii using those same weights. The physical visible check projects the ray through the direction chord onto the completed terrain plane and measures the resulting radius. If the rendered vertices have different radii, intersecting that ray can also give a different point from directly interpolating the rendered Cartesian vertices with the direction weights. The physical grade includes the segment's analytic radial minimum. The radial maximum for a valid projected segment is at one of its endpoints.

For equal-radius endpoints of radius `R` at angular separation `θ`, the rendered chord's midpoint has radius `R cos(θ/2)`. The rise back to an endpoint scales as `R θ²/8` for small `θ`, so finer subdivision reduces this geometric effect. The scalar endpoints and running scalar minimum are checked with the routing epsilon, approximately `2e-6` in basin 122. Linear visible heights and completed radius are checked against their own running minima with the same epsilon plus the visible bump allowance, approximately `0.000100586419` in that fixture. The bump depends on the visible height range, while the epsilon is capped by an absolute and a radius-scaled term. Neither term measures facet angular span.

#### Two reproductions

- **Basin 122.** The intermediate carving logs record 502 networks, 2 ocean terminations, 500 unexpected broken networks and inland breaks, 1,000 visible-grade rejections and zero hydrology-grade rejections. The published segments include nine hydrology descents, eight visible descents and one minor visible bump. The visible and hydrology vertex arrays match, and adaptive filtering is zero. This makes the completed radius a plausible source of those visible rejections. Both carving passes accepted zero modifications, and the 13 candidate coastal paths failed the separate coastal depth limit. These aggregates cover raw accumulation and network tracing. They do not identify the first failed transition from the final lake spill on facet 770. The basin is approximately 0.00176 units deep.
- **Basin 0.** The 2,562-sample fixture uses simulation subdivision 3 and hydrology subdivision 4. Its synthetic shelf multiplies float unit directions by the nominal radius 8.2 and reads back their float magnitudes, with a logged maximum of 8.20000076. The exact scalar sweep preserves positive-depth depressions and the flat-face cutoff is `epsilon / 4`, less than one float ULP at that radius. Routing builds logical lake geometry with zero depth and area thresholds, plus ocean exclusions and hierarchy suppression. The visible lake mesh uses separate thresholds. This explains how a one-ULP depression can qualify. The actual rejecting check and the safety of changing that qualification are still unverified.

#### Confirmed rejection mechanisms

The targeted diagnostic run from 26 November 2026, archived as `TestResults(20261126-114236).xml` and supplied as `TestResults.xml`, reproduced all four failures. The complete XML has `replayMatches=True` and `failsBeforeFirstDownstreamStep=True` in all four cases. The accompanying Console excerpt cuts off some verbose entries.

| Reproduction | Verified transition |
| --- | --- |
| Basin 122 in all three seed-123456 cases | The permitted descent on facet 770 spans 0.13959561624 radians. Hydrology and visible vertex elevation descend from 8.00718212128 to 8.00111198425. The completed facet falls to 7.98453928526 and rises by 0.01657252513 before the endpoint. The radial endpoint alone fails, by about 162 times the 0.00010258642 allowance. |
| Basin 0 in the seed-12345 bowl | Witness facet 520 has a valid flat exit at distance 1. The flat chooser selected incident facet 772 at distance 0 and the departure predicate then rejected it. The scalar witness edge on 520 is flat and its mesh chord separately fails radial grade. |

#### First production correction

Logical routing now uses the hydrology and completed **vertex elevation** fields with their existing tolerances and running minima. In each facet, the visible elevation of a segment is the affine interpolation of its two endpoint elevations, even when the three vertex radii differ. With direction chord `d = Σwᵢdᵢ` and completed plane `n·x = 1`, the rendered radius is `‖d‖/(n·d)`. The direction norm contributes the tessellation minimum. Separating the two quantities preserves rejection of a real visible elevation rise, including where the filtered hydrology field descends across a visible ridge. It also avoids adding an angle-dependent allowance to either tolerance.

The shared trace, parcel accumulation and replay, rivers, lake outlets, and confluence validation switched to logical grade. Shore intersection, carving verification and exposed overflow certification kept the exact completed mesh. At that stage, presentation still used the raw radial profile as a grade gate. Presentation omissions no longer rewrote confluences or added logical breaks.

The flat router now tests permitted departure on the actual start and end carriers before ranking plateau exits by distance and facet ID. Its first step can therefore use facet 520 and continue to the shorter exit after a legitimate handoff. When all candidates violate the predicate, the trace still fails. Source-basin membership and the recorded witness remain required.

The basin 0 floor and rim directions both sample the fixture's nominal 8.2 shelf. Their one-ULP relief is consistent with storing a scaled float direction and reading back its magnitude. The general basin sweep still keeps positive-depth depressions and the separate visual thresholds still determine lake meshes. Those scalar samples alone cannot distinguish intentional shallow relief from roundoff in other worlds. No basin is removed and the existing runoff accounting stays in force.

`[GeodesicOutletTrace]` emits a compact failure line before the detailed replay. The 818-case run passed the cases for equal-radius sag at four representative angular spans, downhill tessellation across one-ULP and larger drops, a genuine spherical visible elevation obstacle, permitted and prohibited flat departures, logical ocean delivery with a failed physical grade, and an unresolved spill that still throws. [Basin topology](TinBasins.md) · [Lake geometry](TinLakes.md) · [TIN lake routing](TinLakeRouting.md)

### Downstream regressions after the first spherical correction

The seed-123456 L3 intermediate builds went from **502 to 251 networks**, **500 to 61 unexpected broken networks**, **500 to 54 inland breaks**, **2 to 34 ocean terminations**, **0 to 105 confluences**, and **0 to 58 closed-basin terminations**. Rejected visible tracing steps fell from **1,000 to 0**, mean network length rose from **0.00529359 to 1.55681**, and hydrology uphill segments stayed at zero. The old `visibleMaterialUphillSegments` field counted **683** completed radial rises along those logical routes. It included chord sag recovery.

#### Sediment mesh construction

The drainage-delivery integration case and both hydrology-terrain parameterizations reached `GeodesicTinLakeSediment.BuildSurface`. Their source meshes provide positions and indices, with an empty normals channel. When `Fan` triangulates the dry portion of a clipped facet, `Add` looks up `normals[a/b/c]` to interpolate a normal. Those IDs reference original vertices, while `normals` started empty and contained only normals appended for generated wet vertices. It could read a generated normal or index past the list. The positions and fan indices are valid.

The builder now interpolates populated normals from the original array and preserves an absent normal channel, as it already did for UV0 and colors. Wet, dry, and rim-wall geometry use the same positions and indices. The sediment test now checks both positions-only input and preservation of present normals, UV0, and colors.

#### Lake self-ownership

In the 818-case run, the bowl integration case reported self-edges for lakes **96, 9, 72, 87, 124, 142, 148, and 161**. Each had one float ULP of depth, the same floor and spill elevations, and its own first-edge witness. The first samples lay on the dry rim. The witness predicate applies at departure, after which the flat router uses distance to available grade or coast exits. Those ranks describe the scalar surface and carry no source-basin identity. `FirstEntry` later encounters a below-rim endpoint in a facet owned by the same lake, clips the link at its rim, and assigns that owner. The channel-join search excludes the source as a destination. The export contains the initial samples and clipped lengths, while the new cycle detail includes the segment entering water and the trace prefix through that approach.

The exact basin sweep retains the descriptor, hierarchy, recorded witness, and parcel matching. For a depth within the existing numerical epsilon, it now asks whether the floor, saddle, and every member vertex below the rim reproduce their stored Cartesian positions from **one identical float radius** multiplied by their original directions. It also verifies that the hydrology and visible samples are the stored positions' measured float magnitudes. A proven common radius marks the descriptor `NumericalShelf` and keeps it out of lake geometry and lake ownership. A distinct authored shallow radius can still qualify, even within the numerical band. Runoff retains its raw trace outcome and accounting. The cycle exception remains active.

#### River presentation

Both mode parameters in each of the two toggle fixtures reported **52 logical networks and 52 centreline rejections**. Presentation called the same analytic radial-rise gate that logical tracing had already separated. Centrelines now use the two vertex-elevation fields across facet boundaries, with their existing tolerances. The same great-circle direction arc traverses each facet along a straight projected line. Its normalized direction changes, but affine vertex elevation on the direction plane has extrema at the crossings and endpoints, including unequal-height facets. The mesh plane still supplies actual intersections and the existing surface offset supplies clearance. Facet-resolved raw samples and the existing fallback from optional resampling remain available. Shorelines, sediment walls, and physical overflow and carving checks retain the exact radial profile.

The toggle tests still require accepted geometry. The opt-in uphill overlay separately records a bounded sample of accepted radial variation so it can exercise the diagnostic on the sphere without counting that sample as a rejected path. The visible grade audit colors vertex-elevation rises and reports completed radial rise intervals separately. `[GeodesicRiverContinuity]` keeps logical breaks, radial variations, and actual centreline and projection rejections distinct.

#### Basin infill and timing

`RebuiltDrainageConservesIndependentRunoffWithNoMaterialUphillSegments` failed at the assertion on `VisibleMaterialUphillSegments`. Extraction previously counted the completed radial rise from the exact facet minimum in that field and also called it a ridge crossing. The counter now measures visible vertex elevation. Completed radial rises and radial-only rises have separate counters. The test retains both zero-uphill assertions and checks that the observed radial rise is accounted for separately.

The infill fixture builds and accumulates two TINs, extracts networks and audits each independent parcel. Each trace is bounded at 32,768 steps. Longer accepted paths increase parcel replay and confluence work. Candidate confluences search for the target sample and validate the remaining trunk, including downstream junctions, so unresolved candidate searches can revisit long suffixes within the existing bounds. This fixture does not run river presentation or lake surface construction. The 818-case XML reports **51.118349 seconds** for this case and **12.3965335 seconds** for the whole run, with the case starting before the enclosing run. The older case reported **0.895299 seconds**. These stamps do not establish a comparable performance regression. Capture a standalone rerun with consistent timings before changing the trace limits or join algorithm.

### Last lake integration assertion

`GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain` formerly compared the keys of the cloned lakes-on `RiverPaths` dictionary with the keys after switching lakes off and changing the planet transform. Those keys are source facet IDs for **displayed** reaches. `GeodesicTinLakeRouting.FirstEntry` clips an inlet at its first lake entry, and presentation publishes it only if at least two distinct directions remain. The latest single-case export reports 52 clipped inlets, 40 displayable inlets and 12 lake-boundary gaps with lakes on. With lakes off it reports zero such gaps. Both modes have 856 raw networks and zero centreline and projection rejections. The XML truncates the keys, but the counts and the presentation filter explain why short inlets are absent with lakes on and reappear with lakes off. The dictionaries cannot contain duplicate keys.

The revised test still compares exact source collections and reports counts, missing IDs, extra IDs, and multiplicity differences. It derives the only permitted lakes-on omissions from real first-entry clips, checks exact adjusted paths and unchanged paths, and requires unclipped endpoints with lakes off. It independently compares raw network IDs, samples, terminations, junctions, receivers, accumulated runoff, basin IDs, the bowl's spill witness, and source terrain across the original toggle and transform. The fixture still requires one genuine rendered and routed bowl lake, with no duplicate ownership or conservation errors.

The terminal labels belong to delivery accounting. When lakes are enabled, parcels with a first owner follow the lake's actual outlet, which ends in a `TinyNoiseDepression` in this fixture. Without the lake link, the raw parcels end at the bowl as `TrueClosedBasin`. The shift of **92.786605071232145** matches the lake's conserved discharge. The total **520.92903863472543**, ocean **87.614276937743526**, inland closed **348.12897218293585** and unresolved **85.185789514042767** are the same in both modes. The 25 raw basin descriptors, 88 matched tiny-noise terminations, one lake and working cycle guard are consistent with this classification. The terminal-label shift does not remove source facets. The test checks that this shift equals the routed lake discharge and that the delivery totals are conserved.

In the targeted results reported by the user, the four river diagnostic-toggle cases passed with centreline rejections reduced from 52 to zero. Seed 123456 in Visible mode reported 100% logical ocean delivery, zero unresolved runoff and zero lake conservation errors. Geometric outlet verification remains a separate check.

### Performance follow-up for meander collisions

The supplied L3 timing for a build with 250 final logical networks reports **7752.55 ms** of geometry generation, **7503.19 ms** of meanders, **771.27 ms** of containment and **6647.75 ms** of collisions. Containment and collision times are subsets of meander work. `GeodesicTinLakeCrossings.AddMeanders` compares each candidate span against other branch spans and earlier spans of the same path on successive passes. A future optimization can evaluate a spatial index over bounds expanded by bank clearance, then retain the current exact segment-distance and shared-contact checks and deterministic candidate order. This work is outside the correctness fix.

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

## Manual outlet and downstream rerun

In **Window → General → Test Runner → EditMode**, first select only `GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain` and use **Run Selected**. Export this result under a distinct filename. If the source collection check fails, keep its full message and the `[GeodesicTinLakeRouting]` and `[GeodesicRiverContinuity]` entries for both modes.

To repeat the targeted integration checks, select the same nine cases and use **Run Selected**. The fixtures supply their own geometry and settings. A remaining unresolved outlet emits the trace automatically.

- `GeodesicDrainageDeliveryIntegrationTests.DiagnosticsRunOnceAfterFinalPassAndDoNotChangeHydrologyOrMeshes`
- `GeodesicHydrologyTerrainIntegrationTests.RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(ReducedRidgeAndFineDetail)`
- `GeodesicHydrologyTerrainIntegrationTests.RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(FineDetailRemoved)`
- `GeodesicLakeIntegrationTests.TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain`
- `GeodesicRiverUphillDebugIntegrationTests.OffOnOffPreservesAcceptedMeshColoursRunoffTopologyMouthsLakesAndTerrain(True)`
- `GeodesicRiverUphillDebugIntegrationTests.OffOnOffPreservesAcceptedMeshColoursRunoffTopologyMouthsLakesAndTerrain(False)`
- `GeodesicRiverVisibleGradeIntegrationTests.AllToggleCombinationsPreserveGeometryAndHydrologyAndOffRestoresExactMesh(True)`
- `GeodesicRiverVisibleGradeIntegrationTests.AllToggleCombinationsPreserveGeometryAndHydrologyAndOffRestoresExactMesh(False)`
- `GeodesicBasinInfillTests.RebuiltDrainageConservesIndependentRunoffWithNoMaterialUphillSegments`

Next run `GeodesicTinLakeSedimentTests`, `GeodesicTinLakeRoutingTests`, `GeodesicTinSurfaceTests`, `GeodesicTinLakeGeometryTests`, `GeodesicDrainageDeliveryTests`, `GeodesicBasinOverflowAuditTests`, `GeodesicRiverGradeTests` and `GeodesicHydrologySnapshotTests`. The first three fixtures include the six optional-attribute, shelf and projection regressions. Finally clear filters, use **EditMode → Run All**, and export the full result. The expected inventory is still 824 cases. For a remaining failure, include any `GeodesicOutletTrace` or lake self-entry trace and the corresponding `GeodesicRiverContinuity` line.

For the gameplay comparison, capture the same world and river settings at effective hydrology subdivisions 6 and 7. Compare `[GeodesicTIN]` and `[GeodesicRiverContinuity]` at each subdivision and inspect the accepted river surface and shoreline contacts. The old `visibleMaterialUphillSegments` field corresponds to the new `completedRadialRiseSegments` for equivalent paths and tolerances. The current `visibleMaterialUphillSegments` counts elevation rises. With the existing delivery diagnostic enabled, check independent runoff, residual, and cycle counts in `[GeodesicDrainageDelivery]`.

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
