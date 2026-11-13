# Geodesic biology parity audit

Source audit of `345ec6535fa14651840a552e1beaaa07f5ebb256`. No runtime behavior was changed or validated by execution.

**1. Branch and checkpoint**

- Inspected HEAD, tracked and untracked working-tree status, staged/unstaged diffs, recent commits, and local/remote-tracking branches. The starting tree was clean, with nothing to classify or checkpoint.
- Created and switched to `codex/geodesic-biology-parity` directly from `codex/implement-geodesic-planet-prototype-mode`. Both point to `345ec65` (`Enforce geodesic temperature constraints`); the prototype matched its tracked remote at inspection. No fetch was performed.
- `main` remains at `cd5381c`. No commit, merge, stash, reset, discard, push, or simulation-code edit was performed. The only working-tree addition is this uncommitted report.
- The accepted changes are already checkpointed: temperature constraints and conservative division energy (`345ec65`), movement diagnostics (`43adaab`), completed river presentation caching (`4e905f7`), sediment river meanders (`d1a720c`), and mineral settling/death-carbon recycling (`e943f46`). No additional prototype checkpoint is required.

The preserved baseline includes L6 simulation/L7 terrain presentation, TIN drainage/rivers/basins and caches, physical five-layer ocean geometry, authoritative ocean chemistry/transport/suspended minerals, vent sources, thermal profiles/heating, continuous passive movement, spawning, carbon-limited division, corrected energy splitting, death recycling, and temperature biology. This audit does not re-audit or alter terrain, hydrology, cache formats, ocean solvers, or production settings.

The user's manual ecological reference is seed 123456, vent multiplier 10, H2/CO2 production entries 10 units/s each, 100 founders, optimum 15–80 °C, peak about 6,445, later about 5,300–5,500, and 5,413 at 1,890 simulated seconds. These are observations supplied by the user, not results reproduced here or pass/fail population targets. Saved startup defaults differ from that manual configuration: [PlanetScene] has seed 12345/random seed enabled and CO2 vent production 0, while the organism component has the 15–80 °C range. Manual validation should explicitly restore the user's configuration; no settings were changed to reconcile them.

**2. Reference and history**

The older simulation is named `LegacyCubeSphere` in [GridType]; its spherical appearance should not be confused with the new `GeodesicIcosphere` resource topology. The legacy metabolism, lifecycle, steering, predation, spawn system, and reaction-scaffold files on this branch are byte-identical to their corresponding `Assets/Scripts/...` files at `main` (`cd5381c`), after the feature-directory relocation. Thus they provide an actual preserved legacy implementation for comparison.

Relevant history inspected:

| Commit | Why it matters |
| --- | --- |
| `af7a280` (2026-07-01) | Added local habitat gates for legacy mutation candidates. |
| `cb51f5c` (2026-07-04) | Added metabolism-aware temporal taxis. |
| `86c520f` (2026-07-20) | Corrected oxygen-dependent mutation to require local layered O2; global atmospheric O2 is diagnostic context. |
| `956ed71` (2026-07-22) | Relocated scripts into feature directories. |
| `5d6b872` (2026-08-10) | Established coherent authoritative simulation-speed semantics. |
| `dc07931`, `3ac156d` (2026-08-17/24) | Introduced the separate geodesic biology runtime and hydrogenotroph founder path. |
| `12d3e84` (2026-09-01) | Made packed geodesic cell/lifespan/biomass state authoritative. |
| `c37a0dc`, `43adaab`, `345ec65` | Current passive kinematics, diagnostics/render/newborn fixes, and temperature/division improvements. |

Older reports describe intermediate states. In particular, earlier evolution-web text about global methane/oxygen gates is superseded by the current source and the local-O2 correction. The reaction registry explicitly labels its stoichiometric coefficients provisional; legacy runtime code reads resource identities from it, not a complete generic reaction solver. Geodesic execution uses its own switch. Placeholder definitions are not operational pathways.

**3. Compact feature parity**

Status describes reachable geodesic behavior: **implemented**, **partial**, **different**, **missing**, or **deferred/new design**. A manually injected organism of a supported enum is distinguished from a strategy reachable from normal founders.

| Area | Legacy behavior | Geodesic behavior / status | Evidence |
| --- | --- | --- | --- |
| Passive drift and random movement | Noise-driven surface movement; energy/temperature affect speed. | **Implemented, different:** deterministic continuous spherical advection/wandering, real neighbor crossings, land reflection, rare adjacent vertical drift; no habitat steering or movement energy charge. Existing movement fixes remain intact. | [LegacyMove], [Passive] |
| Amoeboid / flagellar motion | Active turn/speed differences and run-and-tumble suitability memory. | **Missing:** both enums and render shapes exist, but all geodesic movement loops skip non-passive types. | [Steering], [Passive] |
| Anchoring / sessile communities | Anchored movement early-return, bottom-associated layers, gated offspring mutation. | **Partial representation:** enum, inheritance and stalk rendering exist; no normal emergence, substrate attachment, or geodesic attachment lifecycle. A forced non-passive agent is merely skipped by movement. | [LocomotionMutation], [LegacyLayers], [Render] |
| Chemotaxis / habitat selection | Temporal local suitability comparison for active movers; adjacent-layer food/light probing. No horizontal neighbor-gradient search. | **Missing active capability:** passive wandering uses no resource/temperature score. Vertical drift is stochastic, not habitat selection. | [Steering], [LegacyLayers], [Passive] |
| Nutrient/energy growth | Energy and stored carbon regulate division; no N/P growth model. | **Implemented storage foundation:** actual local H2/CO2 withdrawal, energy generation, stored C threshold. **Missing in both:** structural biomass growth and other nutrient-limited growth. | [LegacyMetabolism], [Requests], [Division] |
| Size and biomass | Size initialized to 1; biomass target can mutate; no continuous size growth. | **Different:** size also stays 1; only OrganicCStore accumulates. BiomassTarget is an inherited threshold, not a carbon stock, and does not mutate. | [Data], [LegacyBirth], [Division], [Render] |
| Division / placement | Rate-per-step random trial; nearby direction scatter; lifecycle precedes metabolism; legacy child energy grant is not conservative. | **Implemented, improved/different:** temperature eligibility and probability, carbon/energy/population gates, exact parent world position and cell/layer, inherited thermal traits, conservative energy split and carbon transfer. Parent retains age; child starts at zero. No explicit cooldown beyond eligibility/rate. | [LegacyLife], [LegacyBirth], [Division], [EnergySplit] |
| Evolution | Child-only metabolic/locomotion/biomass mutations with habitat/unlock gates. | **Partial:** deterministic temperature mutations work at birth. Metabolism, locomotion and biomass-target mutation routines are disconnected. Founders and their descendants remain hydrogenotroph/passive. Lifespan is inherited from the parent, whereas legacy samples a new lifespan per birth. | [LegacyMutation], [Division], [ThermalMutation] |
| Competition / regulation | Sequential local resource withdrawals plus maintenance/lifespan and cap. | **Implemented, different:** batch proportional competition for actual cell/layer inventories; maintenance, thermal exposure, age and cap regulate population. Global carbon closure still has the issues below. | [Competition], [Commit], [Division] |
| Death / detritus | Stored C returned locally, with legacy marine-snow redistribution. | **Implemented:** exact death-node dissolved OrganicC return once, with rounding/unplaced diagnostics. **Deferred:** distinct organic particles/seabed reservoir; no dissolved OrganicC feeding pathway yet. | [Recycling], [OceanTick] |
| Predation / waste / cooperation | Functional layer-binned bites when scent predation is enabled; organic-leak/toxic-waste scent fields. Some fear/range/energy fields are unused, not full behaviors. No explicit symbiosis/cooperation system found. | **Missing:** predation, organic feeding, scent and waste ecology. Existing chemical byproducts and competition are real; cooperative/symbiotic systems would be new design. | [Predation], [LegacyScent], [Pipeline] |
| Rendering / player information | Instanced shapes by locomotion, metabolism/age/energy coloring, aggregate HUD counts. | **Partial:** shared instanced renderer and HUD counts/environment tabs work; geodesic colors stay inherited founder colors because legacy color updates are skipped. No growth/division animation, organism trait-inspection UI, or population-history graphs found in either mode. Cell pickers inspect environment. | [Render], [Colors], [Hud], [CellPicker] |

Locomotion costs are not a restored legacy feature: `moveEnergyCostPerSecond` exists, but the legacy metabolism switch sets movement cost to zero for every locomotion type. Geodesic passive motion likewise has no propulsion cost. Adding active locomotion costs is an explicit design/balance decision, separate from restoring the movement itself ([LegacyCosts]).

Neither mode turns size into a separate structural biomass inventory or scales intake/maintenance with ongoing body growth. Sea-only traits and locomotion skill are inherited metadata; no mutation of those fields or skill-driven movement was found. Restoring biomass-target mutation changes division thresholds, not physical growth by itself.

**4. Metabolism coverage**

All eight strategies have shared enum/organism representation in [Data] (requirement 1). All geodesic organisms, including a forced unsupported type, enter the common age/maintenance/temperature/death loop and generic inherited division gates. That common infrastructure does not supply a missing food reaction. In normal startup only hydrogenotrophs occur: [Founders] and [Division] contain no metabolic mutation.

| Strategy | 2. Environmental sources | 3. Geodesic consumption / products | 4. Energy | 5. Stored carbon | 6. Maintenance / survival compared with legacy | 7. Evolution / unlock | 8. Reproduction / inheritance |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Hydrogenotrophy | Ocean CO2 and vent H2/CO2, with transport/exchange. | Working CO2 + H2 withdrawal; no explicit non-assimilated carbon product. | Configured yield × achieved extent. | Configured CO2 storage fraction, capped. | Working basal/thermal/age/energy death; local O2 inhibits uptake. Legacy O2 reproduction suppression and specific deprivation timers are absent. Neither current hydrogen path respires its store as fallback. | No metabolic mutation; thermal mutation works. | Operational; inherits type, threshold, habitat and traits; conservative energy/C division. |
| Sulfur chemosynthesis | CO2; configured H2S vent sources, if enabled. H2S production may be zero in a chosen setup. | Real CO2 + H2S withdrawal, S0 enters local suspended minerals. | Configured yield × extent. | All consumed CO2 becomes desired store; legacy storage-fraction setting is ignored. | Common survival only; configured legacy sulfur O2 inhibition is not applied. Legacy sulfur fallback calculates starvation diagnostics but does not actually call store respiration. | No emergence from founders. | Generic inherited division works if supplied with a sulfur organism and enough substrate. |
| Methanogenesis | CO2 + H2. | Real withdrawal; 85% CO2-equivalent CH4 output. | Hardcoded 85% of configured yield × extent. | Hardcoded 15%, irrespective of configured assimilation fraction. | Common survival; legacy low-O2 efficiency/optional extreme damage are absent. Cap can lose assimilated C. | Legacy Fermentation → Methanogenesis edge is disconnected. | Generic inherited division; dormant in normal ecology. |
| Methanotrophy | CH4 from startup/exchange or methanogens; local O2. No normal endogenous methanogens currently emerge. | Real CH4/O2 withdrawal; full CH4-equivalent CO2 output. | Full configured yield × extent, unlike legacy oxidized-fraction yield. | Additional hardcoded 15% CH4-equivalent store: duplicate carbon credit. | Common survival; no legacy O2-state diagnostics or food attribution. | No emergence; legacy hydrogen → methanotrophy and local CH4/O2 gates are disconnected. | Generic inherited division; dormant. |
| Photosynthesis | Geodesic daylight and CO2; light multipliers L0=1, L1=0.55, deeper=0. | Real CO2 withdrawal and O2 output in light. | Energy per consumed CO2. | All consumed CO2 desired as store; photosynth storage setting ignored. | Common survival; legacy aerobic dark respiration and weak anoxic stored-C fallback are missing. Stored energy/C alone can permit dark division under the common gates. | Timer property still exists, but no geodesic mutation caller uses it. | Generic inherited division; child keeps parent's actual layer, unlike legacy automatic top-layer placement. |
| Saprotrophy | Dissolved OrganicC from actual death recycling, plus local O2; both transport normally. | **Missing.** No request case, withdrawal, respiration or CO2 return. | None. | None from feeding. | Basal/thermal/age processing only. Legacy assimilates food and respires the rest, or its own store if food is absent. | Missing; enum/scaffold and elapsed unlock property do not enable feeding. | Type could be inherited by a forced agent, but no sustainable food-supported lineage. |
| Fermentation | OrganicC exists, but has no geodesic biological consumer. | **Missing.** Legacy consumes environmental OrganicC, stores a fraction, releases CO2 + H2; despite a gate accepting inherited store, its current productive runtime does not consume stored C as fallback. | None in geodesic mode. | None from feeding. | Common maintenance only; no fermentation/O2 physiology. | Hydrogen → Fermentation and subsequent methanogenesis edge absent. | Generic forced-type inheritance only. |
| Predation | Living prey exist; no geodesic prey-search/feeding pass. | **Missing.** Legacy bite transfer and subsequent aerobic stored-C respiration are bypassed. | None from feeding. | None from prey. | Common maintenance only; no predator-specific basal multiplier or store respiration. | Saprotrophy → Predation/motility gate disconnected. | Generic forced-type inheritance only. |

Working five-strategy request formulas are in [Requests]; their joint inventory withdrawal, energy and store commit are in [Commit]. Legacy comparisons are [LegacySulfur], [LegacySapro], [LegacyHydrogen], [LegacyFermentation], [LegacyMethane], and [LegacyPhoto]. Environmental authority is [OceanTick]/[Inventory].

Photoferrotrophy, sulfur/hydrogen anoxygenic photosynthesis, O2-detox and UV-protection entries are explicitly scaffold placeholders ([Registry]); they are not additional implemented legacy strategies to restore. Marine snow's old combined food/detritus treatment and fading mineral visuals should not replace the newer conservative mineral reservoirs.

**5. Integration, evolution and accounting findings**

The reachable call graph is:

```text
ReplicatorManager.Update → ReplicatorSimulationPipeline.RunFrame
  → N × RunSimulationStep(dt), advancing the clock once per step
    → RunGeodesicBiologyStep → GeodesicBiologyRuntime.Step
      → passive movement schedules / kinematics
      → accumulated reaction tick → requests → competition → inventory commit
      → per-step age / thermal exposure / maintenance / death / division
        → inherited temperature mutation only
    → return (all legacy biology passes bypassed)
  → render once per rendered frame; aggregate counts / telemetry

Separate component Updates consume the same authoritative simulation clock:
  ocean resources → sources/exchange/transport/particles/chemistry
  surface temperature → ocean-temperature callback (duplicate-tick guarded)
```

Evidence: [ManagerStep], [Pipeline], [GeoStep], [OceanUpdate], [ThermalUpdate], [OceanThermal].

- **No double biology processing found.** The early return prevents legacy lifecycle/metabolism/predation/steering from also executing. `PlanetResourceMap.Update` rejects a geodesic authoritative world ([LegacyGuard]). Restoring a feature by simply invoking those legacy passes would introduce the wrong indexing, inventories and possibly duplicate lifecycle charges.
- **Time scaling is already coherent at the clock boundary.** N substeps each use `min(unscaledFrameDelta, maximumStepDelta)`; no additional speed multiplier is applied inside geodesic biology. Kinematics accumulate at 0.10 s, reactions at 0.25 s, rates are normalized against a fixed 0.50 s reference; maintenance/age/exposure/reproduction use each authoritative dt ([Cadence], [GeoStep]). `metabolismTickSeconds` controls the legacy loop, not geodesic reactions. Changing the reaction cadence must not multiply per-tick yields a second time.
- **Temperature scales the complete geodesic reaction extent.** Uptake, stored C, products and energy all scale together in [Requests]/[Commit]. In several legacy pathways, substrate uptake/storage is not temperature-scaled while energy yield is ([LegacyHydrogen], [LegacySulfur]). Preserve the newer geodesic behavior rather than importing the older split treatment.
- **Coupling is operator-split, not substep-interleaved.** Biology performs all N steps inside the manager update; resource and thermal components catch up separately. Resource ticks default to 5 s; thermal cadence is model-specific. Uptake, maintenance and reproduction can therefore see the same last-completed environmental state across several biology steps. Catch-up guards retain backlog, which may drain even with a stationary clock while paused. This is a confirmed integration characteristic and a risk for equal-time comparisons at different speed/frame grouping, not proof of a newly broken solver. No execution-order or solver changes are proposed here.
- **Packed state is authoritative.** Cell, layer, energy, store, thermal traits, lifespan and biomass target live in `ReplicatorPopulationState`. The companion object is not broadly refreshed in the geodesic hot path ([Packed]). Use packed reads for restored gates, feeding, statistics and inspection. Shared HUD locomotion counts currently read companions; inheritance keeps them consistent today, but future mutation must update the birth companion and packed state together.
- **Mutation is disconnected except temperature.** [LegacyMutation] performs child-only choices; [LocomotionMutation] and [LegacyBirth] add locomotion/biomass variation. None is called by [Division]. Legacy outer/conditional mutation probabilities are nested and ordered, not one flat chance per strategy: H → Photo/Sulfur/Fermentation uses an outer roll; H → Methanotrophy and saprotrophy rolls are separate and can override earlier choices. Preserve parent lineages and review probabilities explicitly when restoring edges. No diversity quota, protected ancestral population, or guaranteed coexistence exists in either mode; diversity depends on niches, resources and child-only branching.
- **Some geodesic methods are calculation helpers only.** `CalculateAchievedExtent` and `CalculateHydrogenotrophyTick` have no production call sites ([CalculationHelpers]); the reachable implementation is request building, shared competition and commit. Editing only those helpers would not change live biology, and calling them in addition to the current pass would risk double processing.
- **Local eligibility needs a geodesic implementation.** Current legacy saprotrophy/methanotrophy require local O2; methane mutation also passes a local CH4 gate. Atmospheric/global O2 is not permission to feed in an anoxic layer. Legacy candidate gates can inspect a proposed top/bottom spawn layer ([Gates]), whereas geodesic births stay at the actual parent cell/layer. Do not validate a distant layer and then create an immobile child in another habitat. Legacy area-scaled cube-cell thresholds cannot be reused as physical geodesic concentrations/inventories ([LegacyIndexing]).

Confirmed accounting limitations, left unchanged for manual review:

| Finding | Exact source / consequence | Scope |
| --- | --- | --- |
| Non-assimilated hydrogen carbon has no destination. | [Requests] credits `CO2Need × storeFraction`; [Commit] withdraws full CO2. At the saved fraction 0.8, 0.2 of every consumed CO2-equivalent is untracked, even below the store cap. Energy is substrate-backed, but this is not a closed carbon budget. Current legacy hydrogen code has the same limitation. | Active founder pathway; frequency/total loss not measured. |
| Store cap discards otherwise assimilated C. | [Commit] clamps store after full substrate withdrawal, without limiting uptake or returning overflow. This affects the five implemented paths; a reproduction-ineligible/capped population can keep consuming. `BiomassTarget`, size and energy are not hidden carbon reservoirs. | Active/potential; requires a deliberate overflow/uptake policy before claiming conservation. |
| Methanotrophy creates carbon. | [Requests] emits CO2 equal to 100% of consumed CH4 and also credits 15% to the organism. Below the cap, C output/store totals 115% of input. [LegacyMethane] instead computes CO2 as consumed CH4 minus actual stored C. | Dormant normally; must correct before evolutionary activation. |
| Requested and actual float-backed transfers can differ. | [Inventory] returns measured inventory exchange after float concentration rounding/clamping; [Commit] ignores it and already awards energy/store using requested achieved extent. Sub-ULP withdrawals can be invisible in large physical volumes. Death recycling does measure its actual transfer/residual ([Recycling]). | Code-level conservation risk; magnitude unmeasured. No resource precision change made. |
| Configured physiology can be silently ignored. | [ManagerStep]/[Requests] forward hydrogen O2 settings only; sulfur/methanogen O2 inhibition, O2 reproduction suppression, assimilation controls and dark respiration are absent or hardcoded. | Becomes ecologically material as other strategies are enabled. |
| Legacy division is an unsafe conservation reference. | [LegacyBirth] grants child energy from the parent, including a minimum grant; [LegacyLife] subtracts only the configured cost from the parent. In contrast, [EnergySplit] pays once and partitions the remainder; [Division] splits existing C exactly when carbon-limited division is enabled. | Preserve geodesic correction; do not port the old grant. |

There is no additional structural-carbon amount to return on death. The geodesic hook correctly clears and transfers the stored inventory at the authoritative node before swap-back removal; generic cleanup does not recycle again. Carbon division conserves `parentC + childC = priorC`, and energy division conserves `parentE + childE = priorE − cost`, within floating-point precision. When carbon-limited division is disabled, the child receives no C and the parent retains its store; that alternate mode is not a structural-biomass model.

Diagnostics also have a boundary: legacy `averageOrganicCStore`, division-ready and metabolism/temperature debug fields are filled by bypassed legacy passes, so some general telemetry can remain zero/stale. The dedicated geodesic logs compute their own store/readiness, movement, thermal and recycling values ([GeoDiagnostics]); use those as current evidence. No generic graph/inspection restoration should use stale legacy summaries.

**6. Incremental implementation sequence**

Each row is independently reviewable. Where two phases are named, finish and review the first before the second. All acceptance tests below are future **manual Unity tests for the user**, not tests executed or automated in this audit. Retain the user's environment/reference settings unless an isolated manual fixture explicitly changes a biological trait or substrate for inspection.

| Priority / complexity | Missing functionality and dependencies | Expected effect / main risk | Manual acceptance test |
| --- | --- | --- | --- |
| 1 — Small | Geodesic growth/division observability: packed store, target, division progress/readiness and bounded per-birth C/E ledger. Reuse existing diagnostics and temperature policy. | Makes the existing storage-growth model reviewable. Risk: observing stale companions or altering RNG draws. No ecological rules change. | Inspect a stable organism ID before/after a birth; check both conservation equations, age zero in child, unchanged parent age, correct node/placement, and explicit thermal/energy/carbon blocking reasons. |
| 2 — Medium | Close current hydrogen growth carbon accounting: choose explicit handling of non-assimilated C and store overflow, and account for actual inventory transfers. Depends on priority 1 evidence. | Conserved uptake makes subsequent growth/food webs meaningful. Risk: changes nutrient demand/population; requires explicit model review, not retuning vent production to recover a target count. | Isolated manually observed uptake below/at store cap balances ocean CO2 plus organism/product C; starvation, division and death close the same ledger. Other environment settings stay fixed. |
| 3 — Medium | Sulfur pathway readiness: local O2 physiology and explicit carbon/storage policy using existing suspended-S0 authority. Depends on conservative transaction rules. | A viable second vent niche. Risk: accidentally depositing directly to seabed or charging old normalized inventories. | Manually supplied sulfur agents require H2S/CO2; source-node inventory falls, S0 enters suspension, local O2 response is honored, division/death balance C. Zero-H2S habitat gives no reaction energy. |
| 4 — Medium | Photosynthetic survival: conservative daylight uptake plus stored-C aerobic dark respiration; add the weak anoxic fallback only as a separately reviewed follow-up. | Day/night survival and oxygen niches. Risk: inheriting the old fallback's untracked carbon fractions or permitting resource-free dark reproduction. | Follow an agent through lit/dark oxic/anoxic habitats. Energy always has a substrate/light source; store respiration uses real O2 and returns CO2; fallback cannot exceed its maintenance policy. |
| 5 — Medium | Deterministic child metabolic mutation into reviewed sulfur/photo paths, with actual birth-node resource/light gates, readiness allowlist, unlock timing and blocked-attempt telemetry. Biomass-target drift can follow in a separate small commit. | First branching evolution while parents retain their strategy. Risk: nested probability changes, wrong-layer gates, or perturbing movement/thermal RNG. | Same seed and timestep sequence repeats birth outcomes; zero chance preserves types; unsuitable local habitats block targets even if another layer/global atmosphere is favorable. Parent type persists; inherited target only drifts when enabled. |
| 6 — Medium | Saprotrophic environmental intake and stored-C respiration, using existing dissolved OrganicC/O2 and proportional competition. Keep mutation disabled until feeding is reviewed. | Closes the consumer side of death recycling. Risk: double-spending C/O2, cap loss, confusing dissolved C with sediments. | Supply saprotrophs in a known death-carbon node: consumed OrganicC equals assimilated C plus respired CO2; O2 limits respiration; competing agents cannot exceed inventory; no food/O2 gives no unbacked energy. |
| 7 — Small | Saprotrophy child-mutation edge and local OrganicC/O2 gate; depends on priorities 5/6. | Decomposer lineages can emerge without replacing their parents. Risk: global oxygen shortcut or an enum-only unlock. | Eligible dividing parents occasionally produce functioning saprotroph children; zero food or zero local O2 blocks emergence regardless of global O2; deaths return remaining C once. |
| 8 — Medium | Environmental OrganicC fermentation and H → Fermentation edge, after the conservative intake contract. Review any stored-C fallback separately; legacy does not supply one. | An anaerobic detritus niche and CO2/H2 cross-feeding. Risk: adopting scaffold coefficients as authoritative or inventing carbon during assimilation. | In low-O2 OrganicC habitat, food falls, stored/returned C balances, H2 and energy track achieved reaction; without food no reaction energy. Parent hydrogen lineage remains. |
| 9 — Medium per pathway | Correct/review dormant methanogenesis, then methanotrophy as separate commits; honor assimilation settings, local O2 and actual transfers. After that, add Fermentation → Methanogenesis and H → Methanotrophy edges separately. | A methane producer/consumer cycle. Risk: current 115% methanotroph C output and oxygen-insensitive methanogens. | For each isolated reaction input C equals store plus CH4/CO2 output; oxic methanogenesis is inhibited; CH4/O2 shortages limit methanotrophy. Only reviewed types enter mutation choices. |
| 10 — Medium per locomotion | Amoeboid movement/temporal sensing first, then flagellar motion as a separate task. Use the geodesic continuous habitat state and deterministic per-agent schedules; add mutation edges only after each movement works. | Resource-sensitive dispersal and habitat retention. Risk: reopening fixed passive motion, cube-sphere lookups, RNG drift or unreviewed energy costs. | Passive control retains its existing behavior; active organisms continue/tumble with temporal suitability, cross real neighbors and valid adjacent layers, and pay only explicitly reviewed propulsion costs. |
| 11 — Medium | Bottom-only anchoring, physical attachment semantics and inheritance/mutation gates; depends on locomotion capability gating. | Mixed mobile/sessile communities. Risk: treating a motion-skipped pelagic organism as attached. | Bottom attachment remains fixed as the planet transforms; mid-water anchoring is rejected; offspring retain valid attachment/habitat; optional release is explicit. |
| 12 — Large, split feeding then signals | Conservative cell/layer-binned predation after saprotrophy and locomotion; scent/waste transport is a later separate task. | Actual trophic interactions and prey pressure. Risk: prey death double recycling, cap losses, stale bins and waste fields mistaken for food. | One bounded bite debits real prey C/energy; predator storage plus returned C balances; death releases only remainder once; out-of-habitat prey are inaccessible. Scent changes steering only after separately validated. |
| 13 — Medium per presentation task | Stored-C-derived size growth, then visual division events; later organism inspection and population histories in separate commits. Depend on priority 1 packed diagnostics, not a new carbon reservoir. | Visible growth/strategy differentiation and useful player inspection. Risk: animation changing habitat, rendering cadence/flicker or a visual minimum becoming invented biomass. | Size follows existing C and resets consistently after splitting; images/selection read the same stable organism state; pausing freezes biological history; animation does not change energy, C or placement. |

Explicit structural biomass, N/P limitation, photoferrotrophy, symbiosis, organic particles, evolved lifespan/O2 tolerance and geodesic save/replay are extensions, not implemented legacy features to copy. Geodesic live save/load is explicitly rejected by [Save]; persistent planet caches are a separate working feature. Save support needs its own future scope, including mutation and movement RNG/scheduler state, and no cache-format changes as a side effect.

**7. Recommended first implementation task and stop point**

**Implement geodesic growth/division diagnostics for the existing hydrogenotroph lineage.** Scope: a packed-state snapshot for stored C, biomass target, threshold/progress, energy and readiness; bounded aggregate birth records showing before/after parent/child C/E and IDs; actual-transfer versus requested-uptake residuals; and clear current-habitat thermal/carbon/energy blockers. Extend existing diagnostics rather than adding a general mutation or growth framework. Do not change uptake, thermal decisions, division accounting, RNG consumption/order, movement, visuals, or baseline parameters in this first task. Actual transfer instrumentation must observe the existing return values without recomputing reactions or reading legacy inventories.

Manual acceptance: use the supplied ecological reference and follow a stable organism ID through uptake, a blocked division, successful division and death. Verify `Cparent + Cchild = Cbefore` and `Eparent + Echild = Ebefore − divisionCost`; child age is zero and cell/layer/world position match its parent at birth; thermal-ineligible divisions remain zero; death returns only the remaining store. Distinguish known uptake-carbon residuals from the already-correct division/recycling ledger. Confirm diagnostics introduce no RNG calls or per-agent log flood, and pause/resume does not create births or duplicate death returns. No population-number assertion is appropriate.

This is a small first task with no new gameplay parameters. A second task can then resolve the measured carbon-accounting policy before restoring new pathways; continuous visible growth remains a separate change derived from the chosen biomass model.

Audit validation was limited to source/call-site/history inspection and static document checks. Unity Editor/Play Mode, automated tests, builds, planet generation, simulation runs, benchmarks and parameter sweeps were not run. No proposed milestone has been implemented. **Stop for manual review.**

[PlanetScene]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/PlanetScene.unity:151
[GridType]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Common/Generation/PlanetGridType.cs:1
[Data]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorData.cs:3
[Founders]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:245
[GeoStep]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:390
[Requests]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:446
[Competition]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:503
[Commit]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:554
[Division]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:580
[Recycling]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:641
[Passive]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:708
[Cadence]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:859
[EnergySplit]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:1063
[GeoDiagnostics]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:1142
[ThermalMutation]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyTemperaturePolicy.cs:73
[CalculationHelpers]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:1080
[ManagerStep]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:950
[Pipeline]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Core/Simulation/ReplicatorSimulationPipeline.cs:301
[Packed]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorPopulationState.cs:278
[LegacyLife]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorLifecycleSystem.cs:36
[LegacyBirth]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:3144
[LegacyMutation]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:2392
[Gates]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:2522
[LocomotionMutation]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:2908
[LegacyIndexing]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:1498
[LegacyMetabolism]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:149
[LegacyCosts]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:465
[LegacySulfur]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:518
[LegacySapro]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:583
[LegacyHydrogen]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:662
[LegacyFermentation]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:723
[LegacyMethane]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:766
[LegacyPhoto]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:910
[LegacyLayers]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs:1318
[LegacyMove]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorMovementSystem.cs:191
[Steering]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorSteeringSystem.cs:190
[Predation]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorPredationSystem.cs:25
[LegacyScent]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:1681
[Render]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorRenderSystem.cs:91
[Colors]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:3480
[Hud]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/UI/ReplicatorHudPresenter.cs:76
[CellPicker]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Geodesic/Diagnostics/GeodesicCellPicker.cs:623
[Registry]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReactionBackedMetabolismScaffolding.cs:175
[OceanUpdate]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs:213
[OceanTick]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs:614
[Inventory]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs:852
[ThermalUpdate]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/Temperature/GeodesicSurfaceTemperatureField.cs:208
[OceanThermal]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/Temperature/GeodesicOceanTemperatureField.cs:320
[LegacyGuard]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/ResourceSimulation/PlanetResourceMap.cs:1009
[Save]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Persistence/SimulationSaveLoadService.cs:257
