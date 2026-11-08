# Complete multi-basin drainage chains

2026-11-05. Final planner revision: `GeodesicOceanDrainagePlan.Version = 3`.

The planner now admits complete compatible downstream packages, reserves candidate capacity for their connectors, and retains the immediate basin-to-basin target in diagnostics. Final seed-123456 improvement is **not established**. No full Unity generation or benchmark was launched by this work. Production compilation and 20 focused managed tests passed; manual validation of the finished revision remains necessary.

## What the investigation established

The previous local algorithm accepted a bounded cut into another closed basin without requiring that basin to have a proven ocean destination. This explains how it could make 426 cuts and reduce closed-network terminations to 777 while delivering only 12.235872% of runoff to ocean. Some local connectivity was useful, but accepted cuts were not equivalent to completed ocean routes.

Version 1 already represented downstream basin edges and traversed the graph from ocean roots. Its combined proposed TIN already contained all selected cuts before verification. It is therefore **incorrect** to diagnose the problem as a general requirement that A→B must reach ocean before B→ocean is applied. The actual restrictions were more specific:

1. **The zero downstream-target counter was a reporting bug.** `PlanSpill()` initialized an inland target, but `VerifyOceanProposals()` overwrote every successful target with `OceanDrainage` or `Coastline`. A multi-basin connection could thus be accepted and reported as direct ocean-target work. Revision 3 keeps `DownstreamBasin` when the immediate downstream ID is a basin. The separate confirmed-delivery diagnostics are not reclassified by this correction.
2. **The graph excluded any source whose full downstream proposal chain was absent.** `Select()` marked those valid individual spill proposals as unrooted; `SelectOceanConnections()` assigned `no-ocean-route`. Missing downstream proposals could be physically rejected, outside the preselected candidate pool, unknown, or cyclic. Version 1 did not distinguish these causes. “Unrooted” did not prove geological impossibility.
3. **Capacity was assigned before dependency discovery.** The old `OrderByDescending(localRunoff).Take(2048)` could discard a small downstream connector needed by a large upstream catchment. Candidate construction now walks each high-priority seed's actual witnessed downstream dependencies before considering the next seed, within the same 2,048-proposal cap. It does not invent a witness or expand the cap.
4. **Selection committed and ranked individual edges.** A downstream prefix could be selected based on potential upstream runoff even when its upstream edge later failed compatibility. The cost ranking did not evaluate the complete new package. Revision 3 evaluates marginal runoff and combined cost for the actual set of new edges, commits the package atomically, and charges an already selected shared trunk once.
5. **Combined old centre profiles could block a valid dependency join before TIN proof.** Lowering a shared downstream valley can replace a proposal's original continuation. Previously every selected centre polyline still had to descend on the combined surface before the actual tracer could evaluate it. Revision 3 allows that provisional replacement only within the same proven graph trunk; every accepted floor and incoming source must then pass the existing continuous-TIN verification on the combined terrain. Strict compatibility checks remain for unrelated trunks. The focused chain fixtures do not require this allowance (`collectiveJoins=0`); they prove complete-chain handling, not that this particular guard was the dominant real-world failure.
6. **Small branches need joint eligibility.** Several individually subthreshold tributaries can jointly justify their connector. A complete shared-trunk bundle can now combine those branches, using their disjoint local runoff and total new-edge cost. It must meet the existing runoff threshold as a group and fit the package bound. No unselected potential tributary is credited as realized benefit.

The final correction retains the ocean-rooted architecture. It does not restore unconditional inland carving or permit a genuinely terminal A→B chain to count as connected.

## Exact version-1 pass-3 stage breakdown

The existing Editor log contains the reported `unrooted=1282` run, around line 15476. Reading it did not start Unity. The counters allow this stage attribution:

| Stage | Count / interpretation |
|---|---|
| Candidate pool | 2,048 attempted; 149 descriptors excluded by the preselection cap |
| Individually valid proposals reaching the graph | 1,427 = 145 rooted + 1,282 unrooted |
| Rejected before the graph | 621, inferred from 2,048 − 1,427 |
| Graph rejection | 1,282 sources had no complete path in the available proposal graph |
| Rooted but not selected | 89, including threshold/compatibility and blocked dependent edges; five compatibility callback rejections were reported |
| Entered combined verification | 56 = 145 − 89 |
| Accepted after verification | 0 |
| Ordinary selected proposals failing ocean proof | 24 (`unverifiedOcean`) |
| Remaining selected failures | 32 coastal proposals, inferred from the selected set and ordinary failures |

The final `coastal-no-ocean-connection=229` counter includes construction and verification failures; it cannot all be assigned to one stage. The separate `no-downstream-side=208`, coastal depth/distance failures and missing graph support are not reasons to raise limits without further evidence. A failed bounded proposal is not proof that no alternative physical route exists.

The supplied counters do not identify how many of the 1,282 unrooted sources were caused by invalid connectors versus the 149 excluded descriptors. One missing connector can block many sources. The new split logs address precisely that uncertainty.

## Selection and conservative verification

`GeodesicOceanDrainagePlan.SelectChains()` constructs complete paths ending at a confirmed ocean root. Unknown destinations, missing edges, cycles and overlong chains receive explicit rejection categories.

For each package:

- New edges are ordered downstream first. Already selected support edges remain part of its complete dependency record.
- Ranking uses independent local runoff from the new edges divided by their combined incision cost. The existing per-edge cost incorporates length, maximum cut and affected facet count. Shared support is not charged or counted again. Summed edge costs remain a conservative approximation; this is not an optimal earthwork-volume solver.
- Compatible small tributaries can be bundled around a common root until their combined benefit meets the unchanged configured threshold. The union is limited to 32 edges. This is a new explicit bound on group size, not an increase to candidate, search, incision or verification limits.
- A failed compatibility decision commits none of the package. A different independently viable package may retain shared support.

All selected packages are applied to **one combined proposed surface per verification attempt** using the existing float projection. The existing TIN then traces each selected floor and all associated incoming river sources, with the original grade and retained-ocean checks. Known original ocean-connected river sources are also protected as before.

If an edge fails proof, only complete surviving packages can retain cuts. Orphan prefixes and incomplete upstream chains are removed before rechecking. A shared trunk survives if another complete verified package still needs it. The existing maximum of three combined verification TINs per carving pass remains. These temporary builds do no parcel accumulation or network extraction. There is no TIN rebuild per potential chain.

After committing a pass, the existing authoritative terrain/TIN/basin rebuild still runs. The cumulative incision guard, configured per-pass depth, bounded coastal submergence/distance, frozen retained-ocean mask, protected ocean vertices, ridge/grade checks, and final lake cycle/conservation checks remain authoritative. Lake thresholds and generation inputs were not changed by this patch.

## Lake overflow and the 84-lake observation

A lake count and a valid spill witness do not prove an eventual ocean destination. Lake outlets can enter another still-closed basin or end with unresolved shoreline evidence. With `viaLakesOcean=0`, the existing final diagnostic confirmed no lake-routed parcel reaching retained ocean; it does not imply that every lake outlet failed or that conservation failed.

The observed lake-routing log had 84 bodies, 82 visible outlets, 25 lake edges, zero cycles and zero conservation errors. That proves conservative routing among the published nodes, not an ocean path. No lake is suppressed or made terminal because its visible outlet has zero length.

Final lake routing is unchanged. A focused regression builds an actual two-lake overflow cascade, selects its already verified downstream graph, and checks final parcel delivery through lakes, unique ownership, conservation and reporting without double-counting shared ancestry. This verifies compatibility with lake overflow. Carving still does **not** build speculative rendered lakes during intermediate passes or use potential future lakes to turn an unrooted raw-TIN proposal into a proven root. That remains a limitation; adding such roots safely would require explicit eligibility dependencies and lake-route proof, not a parent-ID shortcut.

## New diagnostics

- `directTargetCuts`, `intermediateBasinCuts`: immediate targets of accepted cuts, without overwriting them with the eventual destination.
- `multiChainsProposed`, `multiChainsAccepted`, `multiChainsVerificationRejected`: complete selected packages containing more than one edge; a branching bundle is one package. These counts do not add up to independent catchments or unique runoff.
- `unrootedMissingPhysical`, `unrootedMissingBudget`: valid sources blocked by a rejected downstream proposal versus a missing candidate. Missing physical proposals can be caused by the current bounded search or witness rules; the counter does not assert global impossibility.
- `unrootedCycles`, `unrootedUnknown`, `chainBudgetRejected`, `physicalInvalidEdges`: remaining planning categories. Existing `no-ocean-route` rejection remains an explicit lack of a complete available path.
- `unverifiedBoundary`, `unverifiedClosed`, `unverifiedRoutingFailure`: distinguish failed proof despite an `OceanCoast` enum from a continuing closed basin or a tracer failure. Coastal proof failures remain separately visible.
- `acceptedCollectiveJoins`: accepted candidates that required retracing a combined same-trunk profile.
- `newlyDirectOceanThroughChains`, `reroutedToStillClosedBasin`: authoritative **post-pass raw-TIN** observations on selected pre-pass closed parcels. These exclude lake overflow and must not be summed across passes.
- `finalChains=True`: takes the **unique union of parcel IDs across all completed multi-edge packages and passes**, then reports confirmed direct ocean and via-lake runoff from final delivery outcomes. It reports submerged/disappeared land inputs separately and does not credit them as delivered water.

“Newly delivered through chains” means final delivery of parcels observed closed before their selected chain. It is not a causal estimate of net improvement over another algorithm or another run. The final whole-planet delivery percentage and accounting remain the comparison authority.

`GeodesicDrainageDelivery` stores direct/via-lake evidence in one byte per triangle instead of the previous boolean. Existing classifications, tolerance and totals are unchanged; the evidence storage remains approximately the same size. No mutable simulation state or cache was introduced.

## Verification and available results

Production assembly compilation succeeded using the project's existing Unity response-file references and sources, with outputs redirected under `Artifacts/DrainageDelivery/`. It reported 12 existing unused-field warnings outside these hydrology changes.

Focused command:

```powershell
python Tools/ValidateTinHydrology.py --unity-data 'C:/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Data' --ocean-plan-only --output Artifacts/DrainageDelivery/multi-basin
```

**20 focused tests passed; 0 failed.** No full suite, planet startup or benchmark was run. Coverage includes A→B→ocean, A→B→C→ocean, a terminal B, shared trunks, jointly eligible small tributaries, overlapping incompatible packages, removal of orphan cuts, dependency-aware capacity, deterministic selection, actual lake overflow, conservation, retained ocean evidence and no-uphill safeguards.

Two small analytic spherical geometry fixtures exercise real carving and authoritative rebuilding. They use no procedural planet terrain generation and are not seed-123456 performance measurements:

| Fixture | Confirmed direct ocean before | After | Accepted cuts | Intermediate basin cuts | Conservation residual |
|---|---:|---:|---:|---:|---:|
| Two basins | 7.709653270551755 | 7.947453926031084 | 2 | 1 | 0 |
| Three basins | 7.692857820925153 | 7.947453926031084 | 3 | 2 | 0 |

Both preserve their total independent runoff and retained vertices and rebuild with zero material uphill segments. The graph tests establish atomic rollback and shared-trunk accounting; the spherical tests establish physically traced complete chains. These results do not establish the expected magnitude of continental improvement.

The supplied real-world comparison remains:

| Measurement | Earlier local algorithm | Planner v1 | Finished v3 |
|---|---:|---:|---|
| Confirmed ocean percentage | 12.235872% | 13.420212% | Manual validation pending |
| Closed-basin network terminations | 777 | 1,166 | Pending |
| Ocean network terminations | 52 | 85 | Pending |
| Accepted carves | 426 | 75 | Pending |
| Tiny-noise trapped runoff | 44.313 | 29.243 | Pending |
| River-generation elapsed time | ~56.0 s | ~41.6 s | Pending |

Existing Editor logs also contain **intermediate version-2** runs made while this revision was under development. One reports 12.440481% ocean delivery and only two accepted intermediate links in pass 1. This is adverse interim evidence, not a successful validation of the finished changes. The final shared-tributary eligibility correction and detailed missing-connector split were not present in that run. Revision 3 makes the completed implementation distinguishable; its planet-scale benefit and performance must be measured before acceptance.

## Expected effect and remaining risks

Within complete physically viable downstream groups, useful inland links can now survive as a coherent package rather than being evaluated as unrelated commitments. Dependency-first admission can recover missing support without increasing the cap, and jointly eligible tributaries can justify a shared connector. These changes should reduce closed terminations **where those restrictions were binding** and can increase confirmed ocean runoff, as the fixtures demonstrate. A reduction to 777 terminations, a particular ocean percentage, or preservation of the 41.6-second time is not established.

Large inland regions may remain unrooted because the single natural-spill proposal has no valid bounded continuation, its downstream witness is invalid/non-descending, the coast proof fails, or a required edge is outside the budget. No alternate-saddle search, ancestor-as-receiver substitution, speculative lake flood, extra excavation depth, or longer submarine channel was added. If the new splits show these dominate, selection changes alone will not solve that geographical restriction.

Package enumeration is bounded by 2,048 proposals and 32 edges per package. Marginal scoring and shared-branch bundling can still have quadratic worst-case planning cost; complete TIN verification remains batched and capped at three attempts. Many late verification failures may conservatively reject a useful prefix. Retrying alternate package geometries is intentionally outside this correction. Manual stage timings and the new failure breakdown should guide further work.

## Files changed by this correction

- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicOceanDrainagePlan.cs`: dependency expansion, atomic package scoring, shared tributary bundles, rejection categories, completed-package retention and final parcel report; version 3.
- `Assets/Scripts/Planet/Geodesic/Terrain/GeodesicValleyCarving.cs`: dependency-aware candidate construction, combined package compatibility, complete-package verification cleanup, preserved target identity and logs.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicDrainageDelivery.cs`: additive direct/via-lake parcel evidence access.
- `Assets/Scripts/Planet/Geodesic/Hydrology/GeodesicRiverSystem.cs`: final unique chain-parcel report.
- `Assets/Tests/EditMode/GeodesicOceanDrainagePlanTests.cs`: focused chain, budget, conflict, lake and physical rebuild regressions.
- This report and the link from the earlier planner report.

Other atmosphere, terrain-generator presentation and shader edits visible in the shared checkout were not part of this hydrology correction.

For manual acceptance, use the verified baseline's effective settings and pre-carve terrain/ocean fingerprints. Require `oceanPlanVersion=3`; preserve all stage, chain, lake, delivery and fingerprint lines. Check final delivery percentage and unresolved runoff separately from accepted chain counts, examine `unrootedMissingPhysical` versus `unrootedMissingBudget`, and retain all conservation/cycle/grade guards. Compare elapsed time under the same loading-screen conditions. Stop here for the user's seed-123456 run and review.
