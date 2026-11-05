# Ocean-directed drainage planning

Implemented 2026-11-05. Planet-scale acceptance is pending a manual Unity generation. No full planet-generation run, benchmark, parameter sweep or large automated test suite was run for this change.

## Outcome and scope

Carving now selects bounded, witnessed spill proposals from an ocean-rooted graph, prioritizing independent upstream runoff relative to excavation cost. A proposal that merely enters another closed basin is insufficient: that basin must have an accepted downstream chain. The existing continuous-facet TIN tracer validates the combined proposed terrain before changes are applied and remains the final drainage authority.

This is a first ocean-directed selection implementation, not proof that the seed-123456 planet now delivers most runoff to ocean. That outcome needs the user's manual generation and final delivery logs. There is no prescribed ocean-delivery percentage.

No lake-routing mathematics, sea-level resolution, retained-ocean classification, ocean layers, hydrology filtering, river ribbon algorithms, chemistry, biology or loading-screen stages were changed. No hydrology cache was implemented.

## Root causes identified

1. **Local spill success did not prove ocean connectivity.** `PlanSpill()` could accept a descent into a lower closed basin. Three passes consequently could enlarge disconnected inland components without establishing their eventual ocean destination.
2. **Visibility-dependent eligibility omitted runoff.** The former planner started only from extracted `TrueClosedBasin` networks and used the maximum accumulated channel flow on a network for eligibility. It ignored all `TinyNoiseDepression` terminations and runoff without a visible source. Cumulative channel discharge is not an independent catchment budget.
3. **Tiny-noise classification does not mean numerical noise.** The tracer compares a local incident-facet barrier against the visible bump allowance. It does not classify the entire basin by global spill depth. Some such sinks have real positive-depth basin descriptors and valid spill witnesses. They now enter the same constrained proposal logic; no smoothing, filling or unconditional micro-pit escape was introduced.
4. **Old-polyline checks can reject valid reroutes.** A proposed cut can intercept an incoming river before its former terminal. Sampling the old path through the cut can show an uphill continuation that the rebuilt TIN would never follow. The focused spherical regression reproduces a valid connection previously rejected as `incoming-profile`.
5. **Symmetric conflicts discard both alternatives.** Former pairwise conflict handling rejected both proposals. Selection now makes deterministic priority choices and retains compatible downstream support. Overlap alone is not a conflict; accepted reroutes still require actual TIN proof.
6. **Some rejections remain physically necessary.** `no-downstream-side` includes missing/invalid spill triangles, missing shared saddle/side vertices, and a witness that is not strictly below the spill. Coastal failures include excessive incision/distance, no retained-ocean proof, or a combined route that still fails. These guards were not relaxed.
7. **An `OceanCoast` enum is not sufficient diagnostic evidence.** Trimming a path back across a sagged shoreline can leave an endpoint on a facet without retained-ocean authority. Existing delivery accounting treats that as unresolved. New planning uses that same strict boundary evidence. The rejection regression deliberately exercises this case. The existing delivery classification has not been changed or credited as a hydrological improvement.

## Implementation

### Independent catchments and the proposal graph

`Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicOceanDrainagePlan.cs` is a new generation-only helper.

- It groups **original triangle runoff parcels** by their observed closed/tiny-noise terminal basin floor, in stable triangle order. Every parcel is counted once. It never sums cumulative river discharge or overlapping basin footprint areas.
- It only matches exact basin-floor vertices. Failed, flat, interior, unknown or unmatched endpoints are not snapped into a convenient basin.
- Graph nodes are the existing basin IDs; each candidate edge uses the current basin's natural spill and recorded downstream witness. Its unedited end is traced with the existing TIN to either a confirmed retained-ocean endpoint or another matched closed basin.
- Reverse traversal starts at proven ocean roots. Unknown destinations, missing candidates, self-loops and closed cycles cannot enter the rooted set.
- Disjoint upstream catchment runoff is propagated through this graph for priority. A small downstream connector may therefore be worth building for a large upstream catchment even if its own local runoff is below the configured threshold.
- Available downstream-first proposals are ranked by potential upstream runoff divided by a cost incorporating path length, maximum incision and affected facet count. Ties use basin IDs. Potential runoff is a selection estimate, never reported as confirmed delivery.
- The cost is `max(step,length) * (1 + 4*maximumCut/max(depth,step)) * (1 + affectedFacets/64)`. Existing geometric bounds still apply regardless of score. There is one natural-spill proposal per considered basin, not an unconstrained global optimization or alternate river receiver system.

The initial candidate pool is capped at 2,048 descriptors, ordered by direct independent runoff and basin ID. Low-runoff connectors are included within that budget; omitted connectors can cause upstream proposals to remain unrooted. The log exposes `candidateBudgetSkipped`. This is a conservative limitation at high basin counts, not permission to invent missing links.

### Terrain selection and verification

`Assets/Scripts/Planet/Geodesic/Terrain/GeodesicValleyCarving.cs` retains its existing natural-saddle route geometry, incision profiles, lateral falloff, sea-relative floors and coastal limits.

- Both ordinary and tiny-noise catchments can now contribute to selection. A basin with an invalid spill remains inland; the active path no longer uses an arbitrary fallback search for that missing graph edge. Historical private search helpers remain in the file but are not called by the active planner.
- Geometric searches have the existing 8,192-label ceiling enforced in `RouteGeometry()` as well as the pre-existing distance/footprint limits.
- New candidates are admitted against the **combined** selected terrain. Every carved centre profile must still descend with the original spherical clearance rule.
- Old incoming/continuation polylines can be provisionally replaced, but this is not an unconditional relaxation. The combined proposed surface is projected using the same float operations as `ApplyGeodesicValleySurface()`, and a new TIN traces each selected floor and every associated original river source. All must reach a confirmed retained-ocean endpoint.
- Known original ocean-connected river sources are also retraced on the combined proposed TIN. If any loses confirmed ocean evidence, the whole batch is rejected. This protects the existing source set; it is not an exhaustive replay of every continental parcel. Final delivery diagnostics still measure all independent parcels.
- Failed proposals and their dependent upstream proposals are pruned. At most **three combined validation TINs per pass** are constructed. They do no accumulation or network extraction. If the remaining batch cannot stabilize within that budget, it is discarded. There is no TIN build per candidate.
- Invalid or ambiguous shoreline evidence remains a rejection, including for coastal candidates. All original coastal depth, distance and retained-vertex protection remains in effect.

The existing complete authoritative rebuild still occurs after every pass, including a zero-cut pass. This refreshes all drainage, basin and runoff data before the next plan.

### Runtime integration and diagnostics

`Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`:

- Captures parcel endpoints during the already-required intermediate/final accumulation when carving is active, regardless of the logging toggle. This avoids an additional full parcel replay just to rank candidates.
- Preserves the frozen original render ocean mask and the current loading-screen reports.
- Checks the existing cumulative bound against the original completed surface before each application: `pass * configuredPerPassDepth`, plus accumulated hydrological epsilon for numerical comparison. This does not introduce a larger excavation allowance or a new depth setting.
- Reports authoritative per-pass direct-ocean delivery for the selected pre-pass parcel IDs, explicitly excluding lake overflow in that intermediate report.
- When final delivery diagnostics are enabled, reports fully/partly ocean-connected **initial high-runoff catchments**, using their original parcel IDs against final delivery evidence, including lake overflow. It does not compare stale basin IDs across terrain changes or add per-pass catchment counts together.
- Keeps original catchment input separate from final comparable runoff. Former land parcels that become submerged are reported as `noLongerRunoffParcels`; their disappearance from the input is not credited as delivered runoff.
- Adds `effective.oceanConnectivityPlanVersion=1` to the fingerprint manifest. Endpoint scratch is released at generation completion even when delivery logging is disabled.

`Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageDelivery.cs` adds an observation-only confirmed-ocean bit per parcel and includes that storage in its memory estimate. The bit is copied from the existing classified outcome. Total runoff, direct/via-lake/inland/unresolved definitions, shoreline criteria, conservation tolerance, and routing behavior are unchanged. This is an additive measurement facility, not a reclassification that raises the ocean percentage.

### Lakes and intentional inland retention

Final lake qualification, clipped surfaces, witnessed outlets, shared-saddle zero-length transfers, lake-to-lake/lake-to-river routing, unique parcel sets and cycle/conservation exceptions are unchanged. Lake depth/area thresholds still do not guide carving; final lake construction remains after the last pass.

The new planner proves **raw TIN ocean-rooted incision chains**. It does not construct provisional rendered lakes or use speculative lake-overflow chains as carving roots. Existing final lake routing may deliver additional runoff through legitimate overflow cascades. Exploiting those cascades during planning is a possible later extension that would need explicit lake-setting dependencies and additional cost; it is not claimed here.

Deep/long/unsupported cuts, missing spill witnesses, unknown or cyclic downstream relationships, unproven coastlines, isolated subsea terrain, incompatible centre profiles, subthreshold catchments, and candidates excluded by budgets may intentionally remain inland. Terminal lakes/basins are not force-drained. No flooded fjord geometry or new ocean occupancy is created.

## Verification performed

Production assembly compilation used the existing Unity `SimulaVit.rsp` references and source list, with outputs redirected to `Artifacts/DrainageDelivery/ocean-plan/` and the new helper added. Compilation succeeded with 12 unused-field warnings in unrelated existing planet/environment classes. Unity was not launched by this validation.

Focused command:

```powershell
python Tools/ValidateTinHydrology.py --unity-data 'C:/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Data' --ocean-plan-only --output Artifacts/DrainageDelivery/ocean-plan
```

Result: **11 focused tests passed, 0 failed**. The new runner option selects only `GeodesicOceanDrainagePlanTests`; it does not execute the full existing suite. The focused fixture also calls three existing lake-delivery regressions and checks cycle rejection through the existing managed logging callback.

Coverage includes:

- Ocean-rooted versus missing/cyclic destinations and downstream-first ordering.
- Deterministic high-value conflict selection and rejected downstream dependencies.
- Low-local-runoff connectors serving large upstream catchments.
- Explicit budgets and invalid cost rejection.
- Unique tiny-noise parcel accounting without terrain, receiver or runoff mutation.
- Rejection of open fixture boundaries and isolated subsea endpoints as ocean evidence.
- A successful deterministic spherical incision requiring a valid incoming-path reroute; configured cut bounds, retained vertices/mask, authoritative TIN ocean proof, conserved runoff and zero material uphill segments.
- Rejection of a superficially ocean-terminated cut lacking retained shoreline evidence.
- Existing conservative lake cascades and invisible/zero-length outlet transfer.
- Final parcel reporting that does not invent connectivity for a closed bowl.

### Small analytic regression result — not seed 123456

This is an analytic L4 geometry fixture with explicit retained component authority, no planet initialization, and no benchmark timing. Its coastline deliberately isolates carving from shoreline-authority ambiguity; a separate negative test covers that ambiguity. Its percentages must not be extrapolated to the real planet.

| Quantity | Before | After |
|---|---:|---:|
| Total independent runoff | 7.916566447289610 | 7.916566447289610 |
| Confirmed direct ocean | 7.747106960508049 | 7.916566447289610 |
| Closed inland runoff | 0.169459486781560 | 0 |
| Unresolved runoff | 0 | 0 |
| Conservation residual | — | 0 |
| Accepted verified cuts | — | 1 |
| Combined validation TIN builds | — | 1 |
| Material uphill segments | — | 0 |

### Real planet comparison — manual result pending

The baseline below is the supplied seed-123456 result; the timing comes from the previously inspected matching three-pass Editor log. A matching seed alone is insufficient to reproduce it.

| Metric | Existing baseline | New implementation |
|---|---:|---|
| Total independent runoff | 283.314338 | Pending manual run |
| Confirmed direct ocean | 34.665979 | Pending |
| Ocean via lakes | 0 | Pending |
| Total confirmed ocean percentage | 12.24% | Pending |
| Closed inland runoff | 246.548180 | Pending |
| Unresolved runoff | 2.100180 | Pending |
| Fully/partly connected initial high-runoff catchments | Not recorded under the new definition | New final planner report |
| Ocean network terminations | 52 | Pending |
| Closed-basin network terminations | 777 | Pending |
| Tiny-noise runoff | 44.313387 | Pending |
| Lake bodies | 59 | Pending |
| Lake routing cycles / conservation errors | 0 / 0 | Pending |
| Delivery conservation residual | Approximately 1.06e-12 | Pending |
| Material visible uphill segments | 0 | Pending |
| River-generation elapsed stage | Approximately 55.214 s | Pending |

The baseline's 347 unexpected network components remain extraction ambiguities, not automatically proven drainage breaks. The planner does not change that classification or count them as newly connected water.

## Performance and cache implications

The authoritative P+1 accumulation builds remain. This change does not add another full accumulation/replay per pass. It does add endpoint capture, a linear parcel grouping pass, bounded spill proposals, graph selection and up to three proposed-TIN constructions plus bounded source traces per carving pass. The existing coastal path already constructed proposed TINs; this broadens that validation to selected inland connections and caps retry count.

Planning may inspect more basins than before because it includes tiny-noise sinks and low-runoff connectors. Combined-footprint compatibility still has a worst-case quadratic component in the number of selected proposals. The candidate/search/validation caps bound work but do not establish a planet-scale time guarantee. The manual stage timings must decide whether this is acceptable; no speedup or negligible overhead is claimed.

At render L7, captured endpoints cost approximately 12.5 MiB while an intermediate TIN is active. The final diagnostic bitset-as-bool-array adds about 0.31 MiB. Parcel ID lists, graph dictionaries and initial catchment evidence add further managed storage; a temporary full proposed TIN remains the largest scratch cost. Only one batch is constructed at a time, but GC can overlap dead allocations. No per-parcel full path collection is retained.

The previously proposed cache boundaries remain applicable. Carving snapshots must include the new planner algorithm version; their inputs still include actual terrain/ocean identity, river source settings, runoff and carving controls. Lake thresholds remain outside carving dependencies. All planner state is generation-local; no mutable hydrology is shared between games.

## Manual Unity acceptance

1. Use the same effective startup configuration as the baseline: simulation L6, render L7, three carving passes, Visible hydrology, zero TIN filtering, and identical terrain, bathymetry, sea, inland uplift, carve depth/width/runoff, river threshold and lake controls. Enable `logDrainageDelivery` for the comparison. Preserve the previous baseline logs.
2. Verify exact pre-carve evidence before comparing outcomes. The inspected baseline had `preCarveTerrainHash=06fd9452e0619dca11487b0cabcb2e84c880101b43dbebbb4543d1d22bcf8cbb`, `preCarvePositionsHash=06261a9af5fd55676d325504260e3b5b7445022cfc6baad033fc31a7561e36e5`, and frozen `retainedOceanMaskHash=d312991d7bf70d8637950e15ce9ef73c09ce64a058fffa171ac163c6c47535ff`, with sea radius `8.008555`. Compare effective numeric controls and raw/hydrological input hashes in the manifest too. The full diagnostic parameter hash will differ because assembly MVID and the new algorithm-version field differ; do not confuse that with a different pre-carve planet. Post-carve hashes are expected to change.
3. Keep the existing loading screen visible through each pass and final lake/river stages. Save `[GeodesicValleyCarving]`, `[GeodesicOceanDrainagePlan]`, `[GeodesicTIN]`, `[GeodesicTinLakes]`, `[GeodesicTinLakeRouting]`, `[GeodesicDrainageDelivery]`, fingerprint and generation-profile lines. Watch candidate/verification budget counts and rejection reasons.
4. Judge progress by `(directOcean + viaLakesOcean) / totalRunoff`, final accounting residual, unchanged retained-ocean identity, and the initial-catchment parcel report. Do not use accepted cuts, network mouths, `OceanCoast` enums or graph potential runoff as a substitute. Keep `OceanCoastWithoutRetainedBoundary` unresolved.
5. Inspect the largest remaining catchments using the **new** final basin IDs. Old basin IDs are only provenance; follow initial parcel IDs for cross-pass measurement. Examine rejected witnesses and coastlines rather than loosening their proof criteria.
6. Inspect river centrelines, accepted valley joins, coast breaches and lake inlets/outlets visually. Require zero material uphill segments, no ocean-mask changes, no new ridge-crossing shortcuts, no duplicate meshes and no broken zero-length lake transfers. Existing lake cycle/duplicate/conservation failures must still stop generation.
7. Repeat the same configuration to check deterministic final terrain/hydrology and geometry. Then return to the menu and start again to check cleanup and unchanged loading/biology initialization. A diagnostics-disabled repeat should produce the same hydrology because planning endpoint capture is independent of logging.

## Files changed

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicOceanDrainagePlan.cs` and `.meta`: graph selection, independent catchments and final catchment reporting.
- `Assets/Scripts/Planet/Geodesic/Terrain/GeodesicValleyCarving.cs`: proposal selection, bounded combined-TIN validation, reroute proof and diagnostic details.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`: endpoint capture lifecycle, cumulative guard, version fingerprint and reports.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageDelivery.cs`: observation-only per-parcel confirmed-ocean evidence.
- `Assets/Tests/EditMode/GeodesicOceanDrainagePlanTests.cs` and `.meta`: focused regressions.
- `Tools/ValidateTinHydrology.py`, `Tools/TinHydrologyValidation.cs`: opt-in focused selection.
- This report and the lifecycle documentation link.

Implementation and focused verification are complete. Stop here for manual generation and review of the new logs.
