# Documentation index and audit

The [Geodesic migration roadmap](GEODESIC_MIGRATION_ROADMAP.md) owns migration status and priorities. The [Geodesic performance roadmap](GEODESIC_PERFORMANCE_ROADMAP.md) owns P0–P6 on the performance branch. [Project status](PROJECT_STATUS_AND_NEXT_STEPS.md) is a short entry point. Detailed subsystem reports own technical explanations and checkpoint-specific evidence. Runtime source and actual resolved settings take precedence over historical prose.

## Audit snapshot — 2026-11-20

Inventory baseline: **68 tracked Markdown files**: 65 under `Docs/` (including one archive), root README, project instructions and the performance README. The starting working tree was clean at `f0b4b01`. All inventory entries were reviewed for role, overlap and supersession; important implementation claims were checked against source/call paths and representative test bodies. This was static review only. Literature recommendations in the evolution audit were retained as design discussion, not independently re-researched.

Concurrent C# profiling edits appeared after the initial snapshot, including the performance analyzer and environment/biology markers and counters. They were read only where relevant and left untouched. Consequently, the final workspace diff also contains concurrent non-Markdown work; this audit's own changes are Markdown only. New instrumentation is not a measured geodesic performance baseline or validation of the other migration milestones.

The former implementation plan is about 207 KB and mixes architectural contracts, several generations of numbered phases, implementation notes and embedded next-task prompts. It remains useful as a development record; a separate concise roadmap avoids repeatedly rewriting that history. The June project status had a competing save/load-first/nutrients-next sequence and described legacy functionality as project-wide. Its full content is now [archived](Archive/PROJECT_STATUS_AND_NEXT_STEPS_2026-06-28.md); current status delegates sequencing to the roadmap.

### Corrections and consolidation

| Finding | Resolution / remaining evidence |
| --- | --- |
| Early biology audit says sulfur/fermentation, active movement, taxis and growth visuals are missing. | Preserve its baseline; add a prominent supersession map. Current source wires these features. The three metabolisms have recorded execution evidence, while current thermal/coverage revisions and non-anchored growth appearance still need acceptance. |
| Evolution-web audit's “current” eight-type mutation web was actually legacy. | Label it a historical legacy comparison/design source. Geodesic normally reaches only hydrogen, sulfur and fermentation; enum/forced reaction support is not normal evolution. |
| Save/load and layered ocean described as generally implemented; old status names schema 2. | Scope those claims to legacy. Current save schema is 4, but full geodesic saving/loading is explicitly rejected. Population visual DTOs, startup JSON and generation caches do not close this gap. |
| Old population plan says lifespan/biomass target remain companion-only. | Forward scope note links to the packed geodesic hot-state audit: cell, lifespan and target are packed authority. Companion construction/traits/debug boundaries remain. |
| Snow/ice visually conflated with temperature implementation or future climate. | Roadmap A5 explicitly restores geodesic land snow/ice and sea-ice presentation early; K4 separately proposes physical mass, melt and albedo feedback. |
| Smoke/heat, afterglow and water reflections could be incorrectly listed as absent. | Source and newer reports show wired effects. Roadmap tracks acceptance/refinement and cost; synthetic lighting checks and VFX tests do not establish current gameplay appearance/performance. |
| Old cache investigation still proposes persistent hydrology stages now present in code. | Mark it historical; link the implemented staged snapshots and final presentation-cache report. Keep generated-state caching separate from live simulation saving. |
| Growth report's original passive-only scope omits a later active-body extension. | Clarify that current source renders reserve-based growth/division for passive, amoeboid and flagellar bodies; anchored presentation is unchanged. |
| Generic reaction registry and dark anoxic fallback imply stronger biochemical coverage. | Clarify scaffold/resource-binding role and separate geodesic dispatch. Correct the suggestion that dark anoxic respiration is anoxygenic photosynthesis. |
| Duplicate current roadmaps and chronological next-step lists. | Consolidate current sequencing in one file. Preserve original plan/history and archive June status; focused notices override their old instructions. No detailed report was deleted. |
| Unindexed subsystem reports and missing current summary of recent work. | Inventory below links every original Markdown file; roadmap/source evidence table documents current thermal sampling, coverage weighting, global atmosphere limits, visual scope and persistence guard. |

### Uncertainty and known contradictions retained explicitly

- Historical seed-283 ecology results predate position-dependent temperature integration and initial founder system/outlet balancing. The vent thermal model fixture now checks eligibility throughout the active ocean layers. Older founder fixtures still include placement assumptions from the earlier model. P0 timings and P1 acceptance status are tracked in the performance roadmap. [Position-dependent vent temperature](GeodesicPositionDependentVentTemperature.md) · [Performance roadmap](GEODESIC_PERFORMANCE_ROADMAP.md)
- Sulfur and fermentation reports record the same **17 baseline failures** in broad native suites. Focused passes do not erase those failures. Their disposition remains C1 work; no fresh run was performed.
- B2.2 makes oxygenic photosynthesis reachable and uses measured atomic transactions. [Photosynthesis and manual Unity validation](GeodesicOxygenicPhotosynthesis.md)
- B2.3 adds aerobic OrganicC feeding, local-gated evolution and optional founders at suitable death-recycling sites. [Saprotrophy and manual Unity validation](GeodesicAerobicSaprotrophy.md)
- B2.4 adds measured biological methane production, Fermentation → Methanogenesis evolution and optional anoxic submarine-vent founders. [Methanogenesis and manual Unity validation](GeodesicMethanogenesis.md)
- B2.5 adds measured aerobic methane consumption, Hydrogenotrophy → Methanotrophy evolution and optional capacity-aware founders. [Methanotrophy and manual Unity validation](GeodesicAerobicMethanotrophy.md)
- B2.6 adds bounded prey capture, measured aerobic feeding, Saprotrophy → Predation evolution and capacity-aware predator founders. [Predation and manual Unity validation](GeodesicPredation.md)
- Some source comments, such as the old “no migrated O2 state” comment in `PlanetGenerator.GetGeodesicOceanAppearanceSample`, lag implemented atmosphere/chemistry. That method still returns default appearance inputs; existence of atmospheric O2 does not prove every color binder consumes it. Source comments were not edited in this documentation-only task; optional presentation follow-up should trace the actual binding.
- “Uncommitted,” branch names, scene/profile values, license failures and results inside historical reports describe their authoring checkpoints. They are not statements about today's working tree, license, saved settings or all later code. External startup-profile contents were not reloaded by this audit; preserve the confirmed fermentation value **0.02** rather than substituting source defaults or old trial profiles.
- Current multi-seed equilibrium, visual acceptance, high-speed throughput, complete elemental closure and geodesic continuation are not established. No timings, estimates or new pass claims were invented.

## Inventory and ownership

**Current authority** lives in the migration and performance roadmaps for their respective work. **Subsystem references** record implementation contracts and evidence. **Historical** entries cover dated audits, experiments and implementation stages. **Legacy/design** entries cover earlier runtime behavior or future designs.

### Entry points and project rules

| Document | Role / disposition |
| --- | --- |
| [Root README](../README.md) | Updated navigation to current roadmap, inventory and architecture record. |
| [AGENTS.md](../AGENTS.md) | Project instructions; unchanged. User-owned testing and saved-settings preservation apply. |
| [Project status](PROJECT_STATUS_AND_NEXT_STEPS.md) | Substantially rewritten as a short current entry point; old June content archived. |
| [Implementation plan](GEODESIC_PLANET_IMPLEMENTATION_PLAN.md) | Architectural contracts and historical development record; all old next-step sequences subordinate to roadmap. |
| [Migration roadmap](GEODESIC_MIGRATION_ROADMAP.md) | New, single current migration and post-migration sequence. |
| [June project status archive](Archive/PROJECT_STATUS_AND_NEXT_STEPS_2026-06-28.md) | New archival location preserving the full previous status/plan with legacy/checkpoint notice. |
| [This inventory](README.md) | New navigation and dated documentation-audit evidence; not a second roadmap. |

### Biology, evolution and physiology

| Document | Role / disposition |
| --- | --- |
| [GeodesicBiologyParityAudit](GeodesicBiologyParityAudit.md) | Historical baseline at `345ec65`; notice maps obsolete missing-feature claims to later reports. |
| [GeodesicBiologyGrowthDiagnostics](GeodesicBiologyGrowthDiagnostics.md) | Historical diagnostic findings; existing hydrogen-conservation supersession retained. |
| [GeodesicHydrogenotrophyConservation](GeodesicHydrogenotrophyConservation.md) | Subsystem reference: measured transactions/whole-reaction activity and checkpoint-specific tests. |
| [GeodesicSulfurParity](GeodesicSulfurParity.md) | Subsystem reference: reachable sulfur, products, physiology and historical native evidence/failures. |
| [GeodesicFermentationParity](GeodesicFermentationParity.md) | Subsystem reference: reachable fermentation, inheritance fallback, conservation, .02 setting and evidence limits. |
| [GeodesicPredation](GeodesicPredation.md) | Subsystem reference for B2.6 capture, evolution, carbon and oxygen accounting, and manual acceptance. |
| [GeodesicFounderEmergence](GeodesicFounderEmergence.md) | Subsystem reference: batch/gradual scheduler and latest coverage policy; distinguishes original tested behavior from unvalidated refinement. |
| [GeodesicPositionDependentVentTemperature](GeodesicPositionDependentVentTemperature.md) | Latest thermal/founder contract; source-only evidence, old-fixture limitations explicit. |
| [GeodesicHydrogenotrophTemperatureConstraints](GeodesicHydrogenotrophTemperatureConstraints.md) | Historical thermal physiology implementation; new position sampling supersedes older coarse-temperature assumptions. |
| [GeodesicVisualGrowthAndDivision](GeodesicVisualGrowthAndDivision.md) | Subsystem reference: original passive reserve-based growth/division, extended by active locomotion to amoeboid/flagellar bodies; packed visuals, DTO support and manual acceptance. |
| [GEODESIC_BIOLOGY_HOT_STATE_AUDIT](GEODESIC_BIOLOGY_HOT_STATE_AUDIT.md) | Packed ownership reference; future-active-locomotion discussion is now historical. |
| [GeodesicReplicatorMovementInvestigation](GeodesicReplicatorMovementInvestigation.md) | Historical newborn/flicker diagnosis; later active motion/growth references added. |
| [GeodesicActiveLocomotion](GeodesicActiveLocomotion.md) | Subsystem reference: active/anchored movement and evolution; later food/thermal reports qualify original scope. |
| [GeodesicChemotaxisAB](GeodesicChemotaxisAB.md) | Experiment controls/metrics; existing links lead to later completed checkpoint evidence. |
| [GeodesicLocomotionEvolutionAudit](GeodesicLocomotionEvolutionAudit.md) | Historical probability/selection investigation; later native food work supersedes runner blocker. |
| [GeodesicFoodTaxisAudit](GeodesicFoodTaxisAudit.md) | Historical controlled/native ecology evidence and limitations; sulfur/fermentation follow-ups retained, current thermal boundary noted. |
| [GeodesicHabitatSeekingFix](GeodesicHabitatSeekingFix.md) | Source-level signal correction; runtime acceptance and subsequent thermal integration distinguished. |
| [CURRENT_MODEL_EVOLUTION_WEB_AUDIT](CURRENT_MODEL_EVOLUTION_WEB_AUDIT.md) | Historical legacy evolution audit and future ecology discussion; “current” in filename/body is checkpoint-specific. |
| [METABOLISM_MUTATION_GATES](METABOLISM_MUTATION_GATES.md) | Primarily legacy gate reference; existing geodesic sulfur/fermentation distinction retained. |
| [METABOLISM_AWARE_TEMPORAL_TAXIS](METABOLISM_AWARE_TEMPORAL_TAXIS.md) | Legacy temporal-steering reference; geodesic implementation is separately linked. |
| [ENVIRONMENTAL_PRESSURE_MODEL](ENVIRONMENTAL_PRESSURE_MODEL.md) | Shared design principles with legacy examples; no claim that all geodesic types implement every effect. |
| [TEMPERATURE_METABOLISM_EFFICIENCY](TEMPERATURE_METABOLISM_EFFICIENCY.md) | Legacy/helper physiology reference; current geodesic policy and position sampling linked. |
| [ANAEROBE_O2_INHIBITION](ANAEROBE_O2_INHIBITION.md) | Legacy physiology/reference; geodesic H/S/fermentation scope linked. |
| [ANAEROBE_O2_TOXICITY](ANAEROBE_O2_TOXICITY.md) | Existing tiny supersession redirect retained for inbound links; no competing content. |

### Environment, chemistry and presentation

| Document | Role / disposition |
| --- | --- |
| [GEODESIC_VENT_PHYSICAL_INVENTORY](GEODESIC_VENT_PHYSICAL_INVENTORY.md) | Subsystem units/migration contract; provisional defaults are not saved settings. |
| [GeodesicChemistryScanAudit](GeodesicChemistryScanAudit.md) | Subsystem cadence/sparse-scan/invalidation reference; operation counts are not current frame-time proof. |
| [GEODESIC_SETTLING_AND_DEATH_RECYCLING](GEODESIC_SETTLING_AND_DEATH_RECYCLING.md) | Mineral settling and dissolved death-C implementation record; no organic particle/seabed-carbon model. |
| [GEODESIC_SEDIMENT_VISIBILITY_CALIBRATION](GEODESIC_SEDIMENT_VISIBILITY_CALIBRATION.md) | Historical visual calibration; current gameplay acceptance remains unverified. |
| [GeodesicOceanConnectivity](GeodesicOceanConnectivity.md) | Subsystem classification/filtering reference with historical validation bounds. |
| [AtmosphericLighting](AtmosphericLighting.md) | Afterglow/water/land rendering reference and historical synthetic GPU evidence; not weather simulation. |
| [HydrothermalVentVfx](HydrothermalVentVfx.md) | Latest smoke/heat/settings/offscreen behavior reference; focused historical tests, human visual review pending. |

### Hydrology and generation history

| Document | Role / disposition |
| --- | --- |
| [GeodesicDrainage](GeodesicDrainage.md) | Historical graph-based stage; active TIN references added. |
| [GeodesicStaticLakes](GeodesicStaticLakes.md) | Historical graph-lake implementation/results; active TIN lake path linked. |
| [GeodesicHighResolutionHydrology](GeodesicHighResolutionHydrology.md) | Historical rejected baseline restoration; fix onward pointer through historical filtered routing to TIN. |
| [Archived high-resolution experiment](Archive/GeodesicHighResolutionHydrology-20261002.md) | Already archived failed experiment; preserved unchanged. |
| [FilteredVisibleHydrology](FilteredVisibleHydrology.md) | Historical intermediate router with existing forward link to TIN. |
| [TinHydrology](TinHydrology.md) | Continuous TIN foundation/algorithm record; old license failure is historical, later runtime reports linked. |
| [TinBasins](TinBasins.md) | Original read-only basin topology audit; later live/lake integration supersedes “no water rendering” as project status. |
| [TinBasinsLive](TinBasinsLive.md) | Editor diagnostic/capture reference; diagnostic overlay is not lake water. |
| [TinLakes](TinLakes.md) | TIN static lake geometry/reference and recorded live results. |
| [TinLakeRouting](TinLakeRouting.md) | Original visible-lake sidecar reference; later all-lakes/sediment/delivery details scoped forward. |
| [TinValleyCarvingLifecycle](TinValleyCarvingLifecycle.md) | Carving lifecycle reference with existing newer-planner supersession. |
| [MajorOutletBreaching](MajorOutletBreaching.md) | Already marked historical retired experiment; retain rejected measurements. |
| [HydrologyCleanupInventory](HydrologyCleanupInventory.md) | Historical cleanup classification and current fixture families; not authority for fresh deletions or test execution. |
| [RiverUphillDebug](RiverUphillDebug.md) | Diagnostic feature and checkpoint captures; does not legitimize uphill drainage. |
| [RiverVisibleGradeAudit](RiverVisibleGradeAudit.md) | Rendered-grade/termination diagnostic reference and captured limits. |
| [LowFrequencyHydrologyExperiment](LowFrequencyHydrologyExperiment.md) | Historical negative terrain-input experiment; no recommendation to repeat it as default. |
| [FineDetailHydrologyExperiment](FineDetailHydrologyExperiment.md) | Historical negative experiment and stop decision retained. |
| [InlandElevationExperiment](InlandElevationExperiment.md) | Optional terrain-control experiment/reference; default zero is not authorization to reset saved values. |
| [OceanDirectedDrainagePlanning](OceanDirectedDrainagePlanning.md) | Planner development record; existing v3 pointer plus later delivery/cache scope. |
| [MultiBasinDrainageChains](MultiBasinDrainageChains.md) | Historical planner-v3 package selection and validation limits; later analysis linked. |
| [BasinOverflowInvestigation](BasinOverflowInvestigation.md) | Historical recommendation/capture analysis; subsequent runtime lake/sediment/cache work linked, no new deletion recommendation. |
| [SedimentRiverMeanders](SedimentRiverMeanders.md) | Current whole-basin visual channel/reference; manual acceptance pending. |
| [GeodesicHydrologyCachingInvestigation](GeodesicHydrologyCachingInvestigation.md) | Historical staged cache proposal/measurements; implemented source and stage-6 reference linked. |
| [GeodesicRiverPresentationCache](GeodesicRiverPresentationCache.md) | Latest final-ribbon cache contract; runtime/performance evidence boundaries preserved. |

### Architecture, persistence, startup and performance

| Document | Role / disposition |
| --- | --- |
| [Geodesic performance roadmap](GEODESIC_PERFORMANCE_ROADMAP.md) | Authoritative sequence, acceptance gates, and evidence for P0–P6 on the performance branch. |
| [Geodesic P0 manual benchmarks](GEODESIC_PERFORMANCE_BENCHMARKS.md) | Fixed-seed A–D profiles, matched measurement windows, timing scope definitions, and observer overhead checks. |
| [Geodesic P1 lifecycle thermal sampling](GeodesicLifecycleThermalOptimization.md) | Reference and optimized paths, cache and numerical equivalence, and user-run acceptance procedure. |
| [Geodesic test-suite maintenance](GeodesicTestSuiteMaintenance.md) | 813-case EditMode baseline, failure dispositions, retired legacy coverage, and manual rerun instructions. |
| [Geodesic scalability architecture assessment](GEODESIC_SCALABILITY_ARCHITECTURE_ASSESSMENT.md) | Starting architecture assessment for milestone A7. Covers execution dependencies and historical projections through 100,000 organisms. |
| [Geodesic performance baseline](GeodesicPerformanceBaseline.md) | Original analyzer’s metrics, capture behavior, and historical timing and validation context. |
| [Geodesic environmental CPU optimization](GeodesicEnvironmentalCpuOptimization.md) | Earlier transport and thermal optimization report, with historical measurements and attribution limits. |
| [SimulationRefactorPlan](SimulationRefactorPlan.md) | Legacy/incremental design reference; extracted systems exist, geodesic runtime has separate ownership. No independent priority sequence. |
| [PopulationStateMigrationPlan](PopulationStateMigrationPlan.md) | Historical migration steps; packed geodesic lifespan/target ownership now explicit. |
| [LAYERED_OCEAN_PLAN](LAYERED_OCEAN_PLAN.md) | Legacy implemented layered-array model; not geodesic resource/persistence status. |
| [LAYERED_OCEAN_AUDIT_2026-05-03](LAYERED_OCEAN_AUDIT_2026-05-03.md) | Historical legacy audit and bridges; scope of later save support corrected. |
| [SAVE_RELOAD_SIMULATION_STATE_ANALYSIS](SAVE_RELOAD_SIMULATION_STATE_ANALYSIS.md) | Historical design and current legacy service boundary; full geodesic restore remains absent. |
| [REACTION_BACKED_METABOLISM_PLAN](REACTION_BACKED_METABOLISM_PLAN.md) | Future executor design, legacy resource bindings and provisional registry; scoped away from current geodesic reachability. |
| [StartupSetupTabs](StartupSetupTabs.md) | Setup UI/reference with later biology/founder/vent-controls scope note. |
| [StartupBiologyEvolution](StartupBiologyEvolution.md) | Evolution UI implementation report; earlier disabled fermentation superseded, later founder controls linked. |
| [PlanetGenerationLoadingScreen](PlanetGenerationLoadingScreen.md) | Generation progress/loading UI reference; not a simulation save feature. |
| [Performance README](../Assets/Tests/Performance/README.md) | Existing legacy benchmark instructions/gates; does not establish geodesic performance or authorize running tests. |

## Cleanup record and maintenance

Created the current roadmap and this inventory. Replaced the old project-status body with an entry point and preserved its full contents in the dated archive. Updated root navigation, historical/scope notices and the reaction-plan terminology correction. No original detailed subsystem report was removed or moved; the pre-existing failed-experiment archive and toxicity redirect remain. This avoids breaking historical links or requiring non-Markdown metadata changes.

Current sequencing now lives only in the roadmap; detailed reports retain technical and experiment evidence. New work should update its milestone there and add narrowly scoped evidence to the relevant report. Do not copy another global feature/status table into subsystem docs.

Documentation verification checks local Markdown targets and anchors, checks changed-file scope against the initial clean Git status, and reviews the diff for preservation of historical content. It does not execute project tests or validate runtime behavior. Final verification results are reported in the task reply.
