# Fine-detail-only hydrology input experiment

This follows the negative reduced-ridge experiment documented in
`Docs/LowFrequencyHydrologyExperiment.md`. That implementation, documentation and
captured results are retained. No river-output tuning accompanies this experiment.
Branch: `codex/test-low-frequency-hydrology-terrain`; original checkpoint `0a9480f`.

## Audit and height definition

`PlanetTerrainSample.LargeScaleHeightOffset` is computed during the same evaluator
call as the visible height. It shares the exact continent field, domain warp,
mountain mask, full configured ridge fBm, ridge sharpness and mountain amplitude.
The historical qualification rule removes explicit fine detail only when:

- `fineDetailScale >= 2 * max(continentScale, mountainScale)`; and
- `fineDetailAmplitude <= .25 * max(continentAmplitude, mountainAmplitude)`.

The two outputs each apply the existing height contrast and offset clamp. Thus this
is removal before the nonlinear final height transform, not subtraction of raw fine
noise after contrast. Unqualified detail leaves `LargeScaleHeightOffset == HeightOffset`.
The rule was factored into `IsQualifyingFineDetail` without changing its expression.
No new noise equations or fine-detail sampler were introduced.

`PlanetGenerator.ApplyGeodesicTerrainDisplacement` already caches paired raw radii
and hydrological radii at every render vertex, using the actual derived terrain seed
and the settings used to build the visible mesh:

```
rawRadius        = baseRadius + sample.HeightOffset
largeScaleRadius = baseRadius + sample.LargeScaleHeightOffset
```

`FineDetailRemoved` reuses these cached arrays. For non-ocean vertices:

```
hydrologyRadius = completedVisibleRadius + (largeScaleRadius - rawRadius)
```

Applying only the cached difference preserves completed-surface adjustments and
rounding instead of replacing them with freshly sampled terrain. Retained-ocean vertices
keep the completed visible radius exactly. Disabled displacement keeps the entire
visible field. Fixture meshes without matching cache arrays fall back to the same
existing `PlanetTerrainSampler.Evaluate` pair; no alternative terrain definition is used.

The scene's fine detail qualifies: scale 18 versus twice mountain scale 4.25,
amplitude .018 versus one quarter of mountain amplitude .8. The ridge octave count
remains **5**, ridge sharpness **1.25**, and the full continent/mountain/warp settings
remain unchanged. Only the qualifying explicit fine contribution is removed.

## Modes and persistence

`HydrologyTerrainMode` defines `Visible = 0`, `FineDetailRemoved = 1`, and
`ReducedRidgeAndFineDetail = 2`. The default remains `Visible`. Advanced startup UI
selects a mode; the third is labelled as the failed reference. Generator Inspector
also exposes the enum. Optional custom-panel support uses a dropdown.

Startup save schema 14 persists the enum. Schema-13 `useLowFrequencyTerrainForHydrology`
true migrates to `ReducedRidgeAndFineDetail`; older saves and invalid enum values default
to `Visible`. Normal reset preserves the mode; Advanced reset restores `Visible`.
The old bool API remains a compatibility property for the previous experiment's tests
and capture entry point. Its true value continues to select the failed reduced-ridge
definition. That definition still caps ridge octaves at 2 and removes fine detail.

The existing TIN constructor, topology, gradient/receiver algorithm, accumulation,
thresholds, tolerances, termination handling, lake pause and renderer remain unchanged.
The optional existing adaptive filter still operates on the selected input. Diagnostics
name all three modes and distinguish the unconditioned input from the filtered field.

## Reproduce and inspect

Use master seed **123456**, simulation subdivision **6**, render subdivision **7**.
Choose Visible or FineDetailRemoved and invoke the existing river component context
action **Rebuild Rivers For Current Terrain**. This rebuild action uses uniform runoff,
as before. No unrelated simulation generation is needed when only switching the mode.

For matching captures use **Tools / Hydrology / Capture Fine Detail Only A-B
(seed 123456)**. Batch entry point: `LowFrequencyHydrologyCapture.CaptureFineDetailBatch`.
The previous menu and `CaptureBatch` still run the failed reference comparison.

The helper captures four identical whole-planet camera views, a matching close-up
centered on the longest baseline candidate, and visible-grade overlays. It exports
settings, terminations and a metric table under ignored `Artifacts/FineDetailHydrology/`.
It verifies exact visible vertices/indices, authoritative ocean mask, TIN ocean
classification and sea level, checks that lakes remain paused, and verifies exact
Visible receivers/runoff after reverting. Previous mode, camera and debug settings
are restored afterward. Scene and startup preferences are not saved.

## Validation

Managed sources: **100 passed, 1 pre-existing failure**. Native Unity 6000.3.19f1:
**134 passed, 1 pre-existing failure** (135 cases). All **11 hydrology-input cases** pass.
The unchanged failure is
`GeodesicRiverGradeTests.DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted`
(expected 13, got 0), reproduced against the original checkpoint in the earlier work.
No existing TIN, grade or lake test was weakened or removed.

Coverage retains the previous regression, determinism and preservation checks and adds:

- Exact continent values, mountain masks, ridge values and mountain contributions for
  all configured octave counts 1 through 8; final heights equal the existing evaluator
  with only qualifying fine amplitude removed.
- Deterministic finite fine-only heights, cached/fallback equivalence, unchanged cached
  arrays, preservation of completed-surface adjustments and retained ocean values.
- Exact no-op behavior for unqualified or zero-amplitude fine detail.
- Three-mode serialization, schema-13 migration, reset behavior and invalid enum fallback.
- Native river-only toggle preservation and exact Visible restoration for both experiments.

The expected radii in the component test are stored as floats, matching the render
cache. This avoids excess intermediate precision in Mono; the assertions remain exact.

Managed command:
`python Tools/ValidateTinHydrology.py --unity-data "<Unity Editor>/Data" --output Artifacts/FineDetailHydrology/managed`.
Native tests cover hydrology input, TIN, topology, drainage, lakes, ocean connectivity,
grade audit, uphill debug and startup configuration. Logs/results live under
`Artifacts/FineDetailHydrology/`; the existing disposable Unity project is reused,
without overwriting previous capture reports or screenshots.

## Changed files for this follow-up

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicHydrologyTerrain.cs`: enum, cached fine-only difference and mode diagnostics; reduced-ridge implementation retained.
- `Assets/Scripts/Planet/Geodesic/Terrain/PlanetTerrainSampler.cs`: shared qualification predicate, unchanged noise math.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`: mode and access to existing raw-radius cache.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`: select input by mode; expose the unconditioned input for diagnostics.
- `Assets/Scripts/Core/Startup/SimulationStartupConfig.cs`, `SimulationStartupController.cs`, `SimulationStartupPanel.cs`: mode UI, persistence and migration.
- `Assets/Tests/EditMode/GeodesicHydrologyTerrainTests.cs`, `GeodesicHydrologyTerrainIntegrationTests.cs`: retained/extended focused tests.
- `Assets/Editor/HydrologyBaseline/LowFrequencyHydrologyCapture.cs`: shared A/B helper with fine-only entry points and radius/roughness metrics.
- `.gitignore`: ignore fine-only artifacts.
- `Docs/FineDetailHydrologyExperiment.md`: this audit and report; prior experiment documentation is unchanged.

## Actual scene A/B results

Capture: `Artifacts/FineDetailHydrology/20261029-081454/`.
Master seed 123456, simulation 6, render 7; 163,842 vertices and 327,680 TIN triangles.
The Visible baseline reproduces the previous experiment's baseline metrics exactly.
The FineDetailRemoved log confirms `cachedFineDetail=True`, `qualifyingFineDetail=True`,
and `ridgeOctaves=5`. The optional adaptive filter remained zero for both runs.

| Metric | Visible | FineDetailRemoved | Change |
| --- | ---: | ---: | ---: |
| Candidate networks | 247 | 222 | -10.1% |
| Visible chains/reaches | 247 | 222 | -10.1% |
| Ocean-connected rendered mouths | 78 | 72 | -7.7% |
| Confluences | 51 | 43 | -15.7% |
| Unexpected/broken chains | 1 | 10 | +900.0% |
| Unrouted active triangles | 2 | 3 | +50.0% |
| Rejected visible steps | 0 | 3,906 | n/a; baseline zero |
| Audited visible uphill segments | 5 | 10 | +100.0% |
| Audited uphill length fraction | 0.216889324% | 0.486085399% | +124.1%; +0.269196075 percentage points |
| Closed-basin terminations | 118 | 99 | -16.1% |
| Noise-depression terminations | 0 | 0 | unchanged |
| Flat terminations | 0 | 0 | unchanged |
| Mean candidate length | 0.151495445 | 0.153743514 | +1.5% |
| Maximum candidate length | 0.952429537 | 1.10507511 | +16.0% |
| TIN material-uphill segments / ridge crossings | 0 / 0 | 0 / 0 | unchanged |
| Depression/fill count (TIN does not fill) | 0 | 0 | unchanged |
| Mean absolute input radius difference | 0 | 0.00112851006 | n/a; baseline zero |
| RMS input radius difference | 0 | 0.00267135597 | n/a; baseline zero |
| Mean absolute neighboring height difference | 0.0124045387 | 0.0123818960 | -0.182535455% |

Radius statistics (planet units):

| Input | Minimum | Maximum | Mean | Population standard deviation |
| --- | ---: | ---: | ---: | ---: |
| Visible | 7.77689505 | 8.51040173 | 7.98178431 | 0.190815317 |
| FineDetailRemoved | 7.77689505 | 8.49951267 | 7.98173680 | 0.190755648 |

Roughness averages all render edges, including unchanged ocean/coast edges, as in the
previous experiment. Candidate length statistics describe the TIN networks. The
existing broken-chain counter includes unresolved active components as well as traced
breaks; unrouted triangles are listed separately. Depression counters are termination
counts, not unique basin inventories. Material-uphill/ridge counts remain zero under
the existing rejection checks; the independent rendered-grade audit still detects
uphill exposure. Lower depression counts alone therefore cannot establish improvement.

**Preservation passed:** exact visible vertex and triangle arrays, authoritative
simulation ocean mask, TIN ocean classification, sea level and paused lakes remained
unchanged. Reverting to Visible restored exact receiver and runoff arrays. No TIN,
visibility-threshold, tolerance, termination or rendering changes were made for this run.

## Visual assessment and stop decision

Inspected the matching whole-planet view 2 and the matching close-up centered on the
longest baseline candidate. Most of the visible network remains present, unlike the
severe thinning of the previous reduced-ridge experiment. The main close-up channel
survives, with altered bends and some conspicuous angular sections; the comparison
does not show a clear improvement in dendritic tributaries or fewer interruptions.
All four overview pairs and visible-grade overlays are available for user review.

**Negative result against the stated acceptance criteria.** Candidate lengths improve
slightly on average and more at the maximum, but chains, ocean mouths and confluences
decline while unexpected breaks, visible-step rejections and audited uphill exposure
increase. Noise-depression terminations do not improve. A 0.183% reduction in this
neighbor-roughness measure and fewer closed-basin terminations do not outweigh those
regressions. This conclusion applies to the unchanged TIN/visible-grade constraints
and this scene/seed; it is not a claim that all terrain-frequency separation is futile.

Default remains Visible. The failed reduced-ridge reference remains selectable, and
its original report and artifacts remain intact. No erosion, carving, tolerance
relaxation, routing repair or further experiment was attempted. Work stops here pending
visual review. Changes remain uncommitted; no merge or history rewrite was performed.
