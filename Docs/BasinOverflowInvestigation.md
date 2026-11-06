# Basin overflow investigation

Investigation date: 2026-11-05. **Investigation only.** No production code, terrain, tracing, lake routing, thresholds, chemistry or visuals were changed. No generation, benchmark or tests were run. Evidence comes from the current source, existing capture files and the already-recorded final planner-v3 Unity run in `C:/Users/domin/AppData/Local/Unity/Editor/Editor.log`.

## Recommendation and confidence

Pursue **C: hybrid hydrological conditioning**, starting with a read-only basin-overflow analysis, then a logical water-body routing layer. Keep continuous TIN gradients for exposed land and reserve physical carving for justified dry-channel changes. Further candidate-ranking changes are unlikely to address the dominant restriction shown by this run.

The data strongly support the diagnosis that many depressions have a potential spill but lack a logical filled-water continuation. However, the existing logs do **not** establish a complete, geometrically verified, runoff-weighted overflow graph. An exact percentage for that graph cannot be honestly recovered from aggregate basin counts and the 20 logged catchments.

The defensible numerical estimate is:

- **12.440481%**: currently confirmed ocean delivery.
- **99.891352%**: conditional upper estimate if all currently closed runoff acquires a valid filled-basin route, while the existing unresolved runoff remains unresolved. This is **not a measured graph result or actual delivery**.
- **100%**: the abstract unlimited-fill ceiling for a connected, finite-height closed sphere with an ocean outlet, unlimited filling time, positive continuing inputs and no losses. This is a mathematical limiting scenario, not a proposed target or a model of natural endorheic hydrology.

The first implementation milestone should measure the gap between the first and second figures on the existing final TIN, separating structurally ocean-rooted ancestry from geometrically verified overflow and unknown evidence. It should not alter actual delivery accounting yet.

## 1. Exact run and settings inspected

The final recorded v3 run has:

| Quantity | Value |
|---|---:|
| Simulation / render subdivision | 6 / 7 |
| Independent runoff | 283.32805543526104 |
| Confirmed direct ocean | 35.247373322958993 |
| Confirmed ocean via lakes | 0 |
| Closed inland | 247.77285052209396 |
| Unresolved | 0.30783159020692707 |
| Tiny-noise terminal runoff | 29.331440147440588 |
| True-closed terminal runoff | 218.44141037465465 |
| Independent parcels | 115,469 |
| Conservation residual | 1.1546319456101628e-12 |
| Raw / finite-spill / terminal basin descriptors | 2,250 / 2,250 / 0 |
| Qualifying / rendered lakes | 86 / 84 |
| Below configured minimum depth | 2,149 |
| Subsea-unclassified descriptors | 354 |
| Nested qualifying bodies suppressed | 2 |

The logged effective settings matter: **minimum lake depth is 0.02, minimum lake area fraction is 0**, Visible hydrology, three carving passes, maximum incision 0.1 per pass, minimum carving runoff 2 mean-cell-area equivalents, width 0.01, and resolved sea radius 8.008555. Defaults from source files would misdescribe this run. No setting was changed for this investigation.

Matching evidence:

- Pre-carve terrain: `06fd9452e0619dca11487b0cabcb2e84c880101b43dbebbb4543d1d22bcf8cbb`.
- Pre-carve XYZ: `06261a9af5fd55676d325504260e3b5b7445022cfc6baad033fc31a7561e36e5`.
- Final terrain / hydrology height hash: `a091f20f9a6a059f4be09813067f5609c14899381b10b47e8cab792551d12c6e`.
- Frozen retained ocean: `d312991d7bf70d8637950e15ce9ef73c09ce64a058fffa171ac163c6c47535ff`.

The final basin/lake/delivery summaries appear around log lines 25278–25313; the v3 total timing is around line 25330.

## 2. What “overflowing=2250” means

In `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinBasins.cs`, `Summary()` counts descriptors for which `HasEscape` is true. `HasEscape` is simply `Spill.HasValue`. It records a **potential sublevel-component merge**, not water, an activated outlet, or discharge crossing the saddle.

`Build()` sorts valid TIN vertices by `HydrologyHeight`, then vertex ID. It joins lower-star components using an elder rule: an external component wins; otherwise the lower minimum wins, with ID breaking exact ties. A losing inland component is recorded at its first positive-height connection to the winning component. The descriptor stores floor, spill elevation, parent floor ID, conservative member facets, and an incident winning-side witness.

Important distinctions:

| Existing field | What it establishes | What it does not establish |
|---|---|---|
| `HasEscape` / finite `Spill` | A recorded connection at a saddle | A valid dry channel from the basin floor to that saddle |
| `ParentBasinId` | Older component in a merge hierarchy | An ordinary downhill river receiver or constant-head outlet path |
| `DownstreamTriangle` / `DownstreamVertex` | A recorded winning-side incident witness | Successful tracing, current flooding, or final ocean delivery |
| `TouchesRetainedOcean` | Local footprint adjacency | Ultimate downstream ancestry |
| `QualifiesForVisibleLake` | Escape, depth/area and subsea display criteria | A water budget or a proof that an outlet reaches ocean |
| `Terminal` | No recorded external merge for the remaining root | Every local `TrueClosedBasin` trace termination |

Only one raw footprint is ocean-adjacent in the final summary. That does not imply that the other 2,249 basin ancestries are endorheic.

### Is the hierarchy complete and cycle-free?

As a **component-merge hierarchy**, it is designed to be acyclic: every nonexternal parent is older in the lexicographic `(minimum height, floor ID)` order. The parent survives the child merge and may later merge at a higher or equal saddle. Any referenced parent of a positive-depth child has already survived above its own birth height; suppression of zero-persistence transient components should not remove that referenced positive parent. The construction therefore supports a compact ancestry forest rather than an arbitrary cyclic receiver graph.

On a closed connected sphere with valid faces, retained-ocean vertices are the external roots. `ParentBasinId=-1` must be interpreted together with `HasEscape`: a finite-spill child merging into external is different from a terminal root with no spill. Generic open TIN fixtures can also have external boundary roots; those must never be credited as retained ocean in a planet audit.

The 2,250 finite-spill descriptors and zero terminal roots are strong **construction-based evidence** that this run's detected depressions ultimately merge into ocean-connected components. They are not a saved per-ID graph validation. Missing parent IDs, malformed witnesses, invalid faces, equal-level handoffs, excluded inland-water policy and retained-ocean endpoint evidence still need explicit audit before publishing a witnessed reachability percentage.

The hierarchy is **not yet a ready-to-use directed water-transport graph**. Parent spill levels can rise up the hierarchy. Giving every child its own first spill level and then sending water into a parent standing at a higher final water level would be physically inconsistent. Connected flooded children must merge or share the resolved enclosing water level. A source's eventual escape head is related to the maximum required saddle along its route, including receiving-water backwater, rather than its first local saddle alone.

This is the main architectural distinction between a depression hierarchy and a drainage DAG.

## 3. Why the continuous tracer traps runoff

`GeodesicTinHydrology.TraceInto()` attempts a facet-gradient step and existing flat routing. If no valid descent exists, it classifies a local closed minimum or tiny depression and stops. `AcceptStep()` prevents height rises and material radial/visible rises. `Accumulate()` consequently ends that parcel at the minimum; it does not consult `TinBasinDescriptor.Spill` or fill storage.

That behavior is correct for a river on **exposed terrain**. Requiring it to climb from an unfilled floor to a spill would violate the current mathematics. A filled basin is a different transport domain: an inlet meets a water surface, and an outlet begins at a witnessed spill. The water need not descend along the submerged bed between them.

The existing rendered-lake sidecar already applies this distinction, but only to bodies emitted by `GeodesicTinLakeGeometry`. Its constructor creates nodes by iterating `geometry.Bodies`. All other raw depressions remain outside the lake graph, irrespective of their potential spill.

## 4. Potential connectivity and what can be calculated now

With the current source runoff held fixed, let D be confirmed ocean runoff, C closed inland runoff and U unresolved runoff. A counterfactual graph audit should compute:

`potentialOcean = D + sum(q[p] for independently owned closed parcels with a verified eligible overflow route)`.

Unknown links must remain U or a separate potential-unknown category. Basin footprint areas and cumulative river/lake discharge must not be summed to estimate source input: footprints overlap and cumulative discharges repeat the same parcels.

The existing totals give these conditional scenarios:

| Scenario | Potential ocean runoff | Fraction of fixed source input | Status |
|---|---:|---:|---|
| Existing confirmed result | 35.247373 | 12.440481% | Measured final delivery |
| Every tiny-noise terminal parcel obtains a valid route; everything else stays as classified | 64.578813 | 22.792947% | Conditional scenario, not a proof that these all have ocean routes |
| Every currently closed parcel obtains a valid overflow route; unresolved stays unresolved | 283.020224 | 99.891352% | Conditional upper estimate |
| Arbitrary unlimited filling over the whole connected finite sphere, also resolving present ambiguities | 283.328055 | 100% | Abstract topographic ceiling; not justified actual delivery |

The 99.891352% estimate assumes all closed parcels can be matched to valid basins, their hierarchy/external witnesses are correct, all required connected water bodies are allowed to fill, their receiving head is respected, and no water is permanently lost or held below spill. It is not a computed percentage of the current saved per-basin graph.

The exact **witness-validated, runoff-weighted** result cannot be calculated from available captures: none contains the final 2,250 descriptors with complete parent/witness/member data and all parcel ownership/outcomes for this fingerprint. Current capture reports contain summaries, top basins or selected examples. `ParcelDestinations` is normally released after generation diagnostics, and complete candidate-chain identities/failure traces are not serialized. This investigation did not reconstruct a planet or replay generation to replace that missing evidence.

Thus the current evidence supports high theoretical fill-and-spill potential, rather than evidence that roughly 87% of continental input is topologically unable to reach ocean. It does not establish an acceptable flooding extent or an exact operational overflow fraction.

### Required steady-state assumptions

An ideal overflow scenario assumes finite storage to the relevant escape head, continuing positive water input, enough elapsed filling time, and no evaporation, infiltration, withdrawal or other permanent loss balancing that input below the rim. Water-body mergers, receiving heads and equal-level saddle transfers must be resolved. Boundary outflow is disabled for the planet; roots require retained-ocean evidence.

Those are **assumptions**, not invented rainfall or storage dynamics. No time-to-fill, storage volume, precipitation, evaporation, or actual extra discharge can be inferred from these logs. Holding the existing original parcel inputs fixed also avoids adding rainfall on hypothetical new water area or silently changing runoff input when land becomes submerged.

With unlimited continuing input and no losses, a finite basin with a finite escape eventually overflows. The same assumption therefore cannot also preserve natural endorheic behavior caused by evaporation or insufficient water supply. That tradeoff must be explicit.

## 5. Small depressions and unrendered water bodies

At the actual configured depth threshold **0.02**, 2,149 / 2,250 descriptors (95.51%) are shallow. Area threshold is zero in this run, so lack of area is not the reason most are absent. Of 101 descriptors deep enough, 86 survive the other qualification criteria; two qualifying descendants are suppressed, leaving 84 bodies. The 354 subsea-unclassified flags overlap other exclusion flags and must not be added as a disjoint count.

Tiny-noise terminal runoff is **29.331440**, or **10.352466%** of all independent input. `TinyNoiseDepression` compares local incident relief against the visible bump allowance. It is not proof of floating-point error, a global spill-depth measurement or an endorheic classification. The much larger `TrueClosedBasin` category also contains shallow natural depressions.

The 20 largest logged disconnected terminal catchments account for 32.993046 runoff (11.644822% of input). Every one reports a spill witness. **18 of these 20 have floor-to-spill barriers below 0.02**, together accounting for 27.131441 runoff (9.575981%). These are examples of a missing logical continuation at a shallow terminal; they are not proof that the complete downstream route is shallow, ocean-connected or absent solely because of mesh eligibility. Some already received water through upstream rendered lakes. This sample overlaps the tiny-noise category and must not be added to it.

Examples from the exact final fingerprint:

| Terminal basin | Runoff | Local barrier | Significance |
|---|---:|---:|---|
| 7045 | 2.722972 | 0.000336647 | Valid local spill; planner reports no complete ocean carve route |
| 78537 | 2.447941 | 0.002730370 | Shallow terminal below display depth |
| 11597 | 1.143554 | 0.000082016 | Tiny-noise sink after a path that passed rendered lakes |
| 137675 | 4.687385 | 0.173025131 | Deeper excluded inland water: floor 7.896484, spill 8.069510, sea 8.008555 |

Basin 137675 illustrates why “excavate every depression” is the wrong universal operation: its floor is already well below sea level, and a shallow coastal cut is not interchangeable with filling it to its rim. This does not imply that its current first spill is its final enclosing water level, or that other logged basins are its descendants; the required parent records are absent from the log.

The amount trapped **solely** by absence of logical continuation cannot be isolated exactly without parcel-to-terminal-to-overflow mapping. The 29.331440 tiny-noise total and 27.131441 shallow sample quantify the opportunity, not exclusive causation.

## 6. What the rejected v3 chains actually show

The final v3 log reports 14 multi-edge packages proposed, two accepted, and 12 rejected during combined verification. The accepted packages deliver 0.25834837785385811 units from 109 unique previously closed parcels. Later passes repeat the same state and accept nothing further.

| Evidence | Interpretation |
|---|---|
| Pass 1: 618 physically rejected candidate edges; 1,231 unrooted sources with missing physical support; zero missing-budget sources | Most graph failures already occur before ranking/verification, because the available **carving** proposal graph lacks accepted downstream edges |
| Pass 3: 619 rejected physical edges; 1,247 missing-physical sources, one missing-budget source, two unknown destinations | Raising the candidate cap would address at most the small reported missing-budget subset here; it is not the dominant explanation |
| 212 `no-downstream-side` in later passes | Missing/unsuitable strictly descending witness or a bounded downstream geometry search failure; not proof that the raw basin has no spill |
| 184 coastal depth-limit and 28 coastal distance-limit failures in later passes | The excavation geometry is outside existing bounds; overflow may be a different operation but is not proven by these failures |
| Pass 1: `unverifiedBoundary=27`, `unverifiedRoutingFailure=7`, `unverifiedClosed=0` | Selected candidate proof failures are dominated by retained-boundary evidence and tracer failure, not reported closed-floor stopping |
| `protectedOceanRejected=0`, `verificationBudgetRejected=0`, no rooted cycles | These safeguards did not account for the observed failures |

The 27 + 7 counts are **candidate failures**, not 34 independent chains. Packages share edges, and one failed edge can invalidate several packages. They cannot be assigned one-for-one to the 12 rejected chains. Complete chain ID lists, per-attempt surfaces and failure paths were not logged, so a responsible per-chain causal classification is unavailable from the saved evidence. Largest-catchment logs show last-pass candidate evidence, not the original 14 package records.

Consequently:

- Missing **accepted carve edges** are the dominant structural limitation in the planner graph.
- They are not synonymous with missing **topographic spill relationships**: all 2,250 raw descriptors have a recorded spill, and lake routing permits a different filled-water transport mechanism.
- The 12 selected-chain failures specifically call for boundary/tracer evidence inspection. It would be unsupported to claim that all 12 failed because they needed filling.
- Combined centre-profile conflicts may reject packages during selection; pass 1 reports 87 selection conflicts. The generic selection-rejection counter also includes runoff thresholds and blocked support. This is separate from the later 12 verification rejections.

The strongest conclusion is that candidate ranking is no longer the only, or main demonstrated, obstacle. The planner asks for a dry descending route through depressions that may instead need an explicitly flooded domain. An overflow audit should test that hypothesis independently of carving feasibility.

## 7. Architectural comparison

| Approach | Plausibility and terrain | Continuous TIN / lake compatibility | Cost and mineral transport |
|---|---|---|---|
| **A — Continue carving** | Appropriate for real incision/breaching; poor universal treatment of every local pit. Existing depth/coast limits remain meaningful | Keeps exposed rivers on descending terrain, but cannot substitute for water storage or lake interiors | Multiple TIN/parcel rebuilds and proof failures remain expensive. Connected dry channels are straightforward mineral paths, but still leave unexcavated sinks |
| **B — Logical overflow** | Appropriate only for depressions explicitly assumed filled to an admissible water level. It preserves the solid terrain; it can imply substantial water coverage | Dry paths enter a water body and resume at a proven spill. Requires compound-level resolution, independent ownership and a graph not restricted to rendered bodies | A small basin DAG can transport existing input cheaply after topology/geometry validation. Lakes become explicit future storage/retention nodes for minerals |
| **C — Hybrid conditioning** | Use logical fill-and-spill for wet depressions; carve selected barriers where a physical dry channel is justified | Preserves continuous TIN on exposed facets, uses common-level wet regions and exact spill departures. Avoids the earlier polygonal corridor-routing replacement | Best long-term balance. Cache static overflow topology separately; rerun physical generation only where terrain actually changes. Keep water and future nutrient mass budgets distinct |

**Preferred: C, implemented from B outward.** First measure potential overflow on immutable final terrain. Do not enlarge the carve search or depth limits while assuming every inland sink ought to be excavated.

The historical `GeodesicDrainageGraph` already has priority-flood machinery, but it is a vertex/simulation-graph reference with receiver geometry that is not the active continuous render TIN. Re-enabling it wholesale would revive the competing routing authority this task seeks to avoid. A priority-flood/minimax pass can instead compute spill heads, wet components and deterministic equal-level order **without drawing its parent edges as rivers**.

## 8. Reusing the lake system safely

The current system provides useful pieces: exact `TinPoint` entries, spill-side departure validation, clipped footprints, logical zero-length lake transfers, unique parcel sets, deterministic DAG discharge and hard cycle/conservation failures. The main coupling is that `GeodesicTinLakeRouting` creates a node only for `GeodesicTinLakeGeometry.Bodies`.

Logical routing for unrendered depressions is feasible after separating **hydrological water-body membership** from **mesh eligibility**. That is a refactor, not a threshold change. It requires managed wet-region geometry/indices even when a Unity mesh is not emitted, so inlets and dry segments can still be clipped correctly.

Several constraints are essential:

- Parent ancestry must resolve compound water bodies and common levels before tracing outlets. Never treat a parent ID as a dry downhill receiver or allow a child surface to discharge into a higher receiving water surface.
- Exposed paths retain existing TIN grade validation. No floor-to-spill “river” is projected uphill over dry land. A conceptual wet region must intercept the inlet and start its outgoing dry path at the spill.
- An unrendered sub-resolution pond can be an explicit hydrological approximation. A substantial flooded area cannot remain visually dry while rendered river ribbons cross it uphill. Before active routing, large wet regions require a deliberate visual policy; the shadow audit needs no new meshes.
- Terminal/unknown bodies, unmatched witnesses, cycles and ocean-policy conflicts remain retained or unresolved. A below-sea minimum does not gain ocean identity. The 354 subsea exclusions need explicit treatment before any active freshwater overflow is enabled.
- A lake with a zero-length dry outlet still transfers logically through its proven directed edge. Keep current unique ownership and conservation rules.

For later mineral transport, use the same verified inlet/body/outlet DAG. Ocean-connected water does not automatically imply complete mineral delivery: sedimentation, adsorption or biological retention would need their own future mass accounting. No chemistry behavior is proposed in this investigation.

## 9. Preserving genuinely closed basins

The present local `TrueClosedBasin` label means “no current accepted gradient descent,” not “a globally endorheic system at every possible water level.” The final lower-star sweep reports zero terminal roots, so it does not currently identify a set of topologically isolated endorheic components on this connected planet.

Preserve real topological terminals where a component has no verified finite external path, there is no retained-ocean root, or the geometry/domain is disconnected. Keep invalid and ambiguous cases unresolved rather than making up an outlet.

Natural closed basins with finite rims can also remain closed because inputs balance losses below the rim or because there is insufficient stored water. Terrain/spill data alone cannot decide that. An unlimited-fill, lossless steady-state model will eventually overflow all such finite basins and therefore cannot also claim to preserve climate-driven endorheism. A future water-budget model or explicit admissible-water-level policy is required for that distinction. For this milestone, leave existing actual routing intact and report these cases as conditional potential rather than silently activating them.

## 10. The 78.6 s versus 50.7 s discrepancy

Existing stage entries were summed arithmetically; no timing experiment was performed.

| Recorded boundary | v1 comparison run | v3 final run | Difference |
|---|---:|---:|---:|
| `fullSynchronousGenerationDurationMs` | 50,677.12 ms | 78,648.47 ms | +27,971.35 ms |
| `coreGenerationDurationMs` | 50,658.90 ms | 78,626.41 ms | +27,967.51 ms |
| River drainage and ribbons | 41,590.78 ms | 41,662.71 ms | **+71.93 ms** |
| Sum of all named generation stages | 50,548.58 ms | 50,711.40 ms | +162.82 ms |
| Full timer minus named-stage sum | 128.54 ms | **27,937.07 ms** | +27,808.53 ms |
| Full minus core timer | 18.22 ms | 22.06 ms | +3.84 ms |

Almost all extra elapsed time lies **outside the named stage timers but inside the enclosing generation timer**. It is not explained by a 28-second increase in the measured river stage, nor by post-core diagnostics.

`PlanetGenerator.GenerateGeodesicPrototypeStages()` starts its enclosing `Stopwatch` before generation. Interactive startup consumes this iterator through `SimulationStartupController`, which yields a frame after every report. The enclosing timer keeps running during those waits. Some waits occur before a stage stopwatch starts, notably `PreparingSurface` immediately after the river timer. Experienced-temperature/vent initialization in that gap is also not individually profiled. Transport/validation and logging contain other unprofiled work.

Plausible causes include an unusually long repaint/frame wait, editor pause or scheduling stall, graphics/shader work during a yielded frame, garbage collection/logging stalls, or genuinely expensive unprofiled initialization. They are not individually established. The log records only 60 vent outlets and a 34-ms Asset Pipeline Refresh near that boundary; the refresh entry itself cannot explain 27.9 seconds. An adjacent run of the same final-terrain hash recorded a full 51.668 s, supporting caution about attributing the outlier to stable hydrology work.

Despite its name, “full synchronous” is elapsed wall time across iterator suspension when called through interactive startup. It also stops before later startup biology initialization, so biology is not an explanation for this particular timer gap. Future profiling should separately report active `MoveNext()` work, frame-wait time and the unprofiled initialization boundaries. No profiling code was changed here.

## 11. Narrow implementation plan for review

**First milestone: immutable shadow overflow audit only.**

1. At the end of the existing final generation, before parcel endpoint evidence is released, export/capture exact TIN and ocean fingerprints; basin IDs, floors, spill/parent/witness records and membership; fixed local parcel inputs; raw endpoints and final routed outcomes. Capture complete package/failure identities if comparing the 12 rejected chains. Do not rerun planet generation to recreate a supposedly identical final state.
2. Build a managed hierarchy audit with explicit root types. Check parent existence, elder order, cycles, terminal roots, equal-level joins, witness triangle membership and departure side. Report structural ancestry separately from witnessed transport. Reuse current basin data; do not create a second live river receiver system.
3. Resolve enclosing spill heads and compound wet components. A priority-flood/minimax reference on the same render TIN can cross-check required heads; initialize ocean heads consistently with the sea level and flag backwater/ocean-policy conflicts. Do not modify `HydrologyHeight`, visible heights or the frozen ocean mask. Flat ordering must be deterministic without introducing artificial visible slopes.
4. Assign each existing input parcel once to the appropriate dry/wet route, respecting first-entry ownership and water-body mergers. Memoize downstream results. Compute potential ocean, policy-retained/terminal, unmatched, invalid-witness, ambiguous-boundary and unknown-input totals. Preserve unresolved evidence; assert the complete source budget and no duplicate contribution.
5. Produce the actual runoff-weighted potential percentage and a required-head/depth/area report, including shallow, deeper and subsea categories. Any head/depth restriction is an analysis scenario or future policy, not an invented storage/rainfall model. Do not credit a route merely because its parent chain reaches `-1`.
6. Only after review, decouple logical node eligibility from rendered-body eligibility and integrate verified wet-body transport using the existing lake guards. Keep the first active scope small and explicit; clip dry paths at logical wet boundaries, review substantial flooding, and preserve meaningful retained/unknown basins. Selective carving can then operate against verified overflow destinations rather than every raw minimum.

The first audit should involve a new isolated analysis helper/exporter and reporting only. Later production touchpoints would be `GeodesicTinBasins`, `GeodesicTinLakeGeometry`, `GeodesicTinLakeRouting`, `GeodesicDrainageDelivery` and `GeodesicRiverSystem`. `GeodesicTinHydrology` remains the dry-route authority. Do not start by rewriting the carve planner or enabling the historical vertex-routing path.

The small basin graph should be inexpensive to traverse compared with repeated parcel tracing; matching, clipping and deeply nested footprints remain the main costs. Reuse original accumulation evidence rather than performing another complete `VisitRunoffParcels()` pass just to measure ancestry. Avoid storing every parcel path or duplicating upstream parcel sets for each ancestor. At L7 the already-existing optional endpoint evidence is about 12.5 MiB; sparse basin/parcel IDs can remain managed arrays. Cache immutable overflow topology separately from live discharge and simulation state in a later cache milestone.

Future focused verification should cover nested backwater/common levels, flat/shared saddles, rendered and unrendered ponds, true terminal components, subsea exclusions, exact retained-ocean evidence, unique source budgets, lake cascades and clipping at hidden wet regions. This investigation did not run those checks or implement that audit.

## Source map and limits

Primary inspected code:

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinBasins.cs`: lower-star `Build`, descriptor semantics and summary.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinHydrology.cs`: `TraceInto`, `AcceptStep`, `Accumulate`, `VisitRunoffParcels`, retained-ocean tests.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinLakeGeometry.cs`: threshold qualification, ancestor suppression and clipping.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicTinLakeRouting.cs`: rendered-body-only node creation, witnessed outlets, first entries, DAG discharge and conservation.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicOceanDrainagePlan.cs` and `Assets/Scripts/Planet/Geodesic/Terrain/GeodesicValleyCarving.cs`: proposal graph restrictions and bounded combined verification.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageDelivery.cs`: final independent-source accounting and top-20 report limits.
- `Assets/Scripts/Planet/Common/Generation/PlanetGenerator.cs`, `Assets/Scripts/Core/Startup/SimulationStartupController.cs`: elapsed profiling boundaries and repaint yields.
- Existing hydrology editor capture code and capture reports: no matching complete final hierarchy/parcel/package dump was found.

The available evidence establishes the semantic mismatch and a large **conditional** opportunity. It does not justify claiming that 99.891352% already has a valid permitted overflow route, that all 12 failed chains require filling, or that basin overflow would solve natural endorheic hydrology without additional assumptions.

Investigation complete. Stop for review before production hydrology changes.
