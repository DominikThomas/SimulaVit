# Geodesic fermentation parity milestone

Baseline: `5296bbf`, `codex/geodesic-biology-parity`; the starting working tree was clean. Existing sulfur, startup-menu and HUD commits were inspected. No branch switch, merge, reset, production scene save, gameplay-default change or save-schema change was made. The user changed Hydrogen → Fermentation probability from .3 to .02 during work and explicitly confirmed keeping .02. The exported paired world configurations confirm that **.02 was resolved before the experiment began**; no restoration to .3 was performed.

## Legacy authority and parity

The legacy metabolism file at `main:Assets/Scripts/ReplicatorMetabolismSystem.cs` and current `Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs` shares blob `d31ee61f824e645e3cee1eb782d135e12bba71b9`. Relevant history includes fermentation extraction `c2b4742`, corrected scaffold outputs `fd25773`, local/layered gates `af7a280`/`d06c666`, O2 physiology `c2c7f7d`/`86c520f`, thermal performance `658cdd0`, measured geodesic hydrogen transactions `a10c838`, and death recycling `e943f46`. Current runtime source, not provisional registry coefficients, defines the reaction.

`ProcessFermentationReactionBacked` actually withdraws **environmental** OrganicC. It stores up to `withdrawal × assimilationFraction`, limited by remaining store capacity, and ferments **everything not stored**. Its runtime binding returns equal quantities of H2 and CO2 for that fermented carbon. Energy is `energyParameter × fermentedCarbon / max(.0001, configuredNeed) × thermalPerformance`. There is no productive stored-carbon fallback. The failure branch sets food/capacity starvation attribution flags; ordinary maintenance and eventual energy depletion remain responsible for starvation.

| Area | Before this milestone | Delivered behavior / intentional difference |
|---|---|---|
| Dissolved OrganicC, transport and death recycling | Operational authoritative geodesic resources | Reused; no new detritus pool or production source |
| Fermentation feeding | Missing in reachable geodesic pipeline | Added to existing request, proportional competition and commit stages |
| Quantities, assimilation and energy interpretation | Legacy manager fields and mappings existed | Preserved, including the .0001 denominator floor and 0.5 s reference tick |
| Full store | Legacy already fermented excess instead of discarding it | Restored faithfully: actual unstored carbon becomes CO2/H2 and contributes energy; no duplicate assimilation |
| Measured withdrawals/products | Legacy float withdrawals did not form an atomic transaction | New geodesic correction: OrganicC debit and both product credits must be representable before any credit |
| Individual thermal performance | Working shared geodesic model | Reused for whole-reaction throughput, carbon and energy; supersedes legacy energy-only thermal scaling |
| O2 inhibition | Missing for geodesic fermentation | Existing local-layer smoothstep/floor model restored; no O2 consumption or direct O2 death |
| O2 speed/replication caps | Legacy shared physiology had additional caps | Current geodesic locomotion and reproduction rules retained; no additional cap is introduced |
| H → Fermentation edge | Missing | Child-only nested transition, after sulfur selection; no F → Methanogenesis edge |
| OrganicC mutation eligibility | Legacy local OrganicC OR inherited store | Same principle, at the actual geodesic child habitat or actual projected carbon allocation |
| Gate units | Cube gates area-normalized per-cell amounts | Geodesic local gates use physical concentration, consistent with the restored sulfur gate; inherited-store threshold uses its actual stored inventory |
| Thermal inheritance | Current geodesic inheritance/mutation already working | New fermenters rebase onto configured fermentation template, then use the existing independent thermal mutation stream |
| Division, energy split and death | Conservative geodesic lifecycle already working | Preserved: normal C gates/split, cost then half remaining energy, inherited lifespan, measured death-carbon recycling |
| Locomotion and chemotaxis | Movement operational; fermentation had no food signal | Local OrganicC tick-readiness added; movement speeds, turning, costs, memory and exploration unchanged |
| Color | Legacy orange-red palette existed | Mutant child becomes `Color.orangeRed`; existing age/energy/growth/locomotion visuals retained |
| Startup and persistence | Fields saved but disabled in geodesic menu | Fermentation probability, recipe and temperature template editable; existing v23 mappings reused |
| Diagnostics | Enum slots existed, fermentation inactive | Bounded request/lifecycle/gate/resource/O2/temperature/locomotion totals plus two-product accounting |
| Other transitions | Deferred | Remain deferred: methanogenesis, photosynthesis, saprotrophy, predation and reverse transitions are not enabled |

## Effective parameters

No Inspector or startup values were overwritten with defaults. Resolved configuration is printed in `[GeodesicFermentationConfig]`. Exported startup JSON under `Diagnostics/FermentationAudit` contains exact binary32 values; the nominal decimals below are easier to read.

| Parameter | Code default | Executed current profile |
|---|---:|---:|
| `fermentationOrganicCPerTick` | .02 | .02 |
| `fermentationEnergyPerTick` | .04 | .7 |
| `fermentationAssimilationFraction` | .15 | .4 |
| `metabolismMutationChance` | .01 | .01 |
| `hydrogenToSulfurMutationChance` | .003 | .03 |
| `hydrogenToFermentationMutationChance` | .002 | **.02**, user-confirmed |
| `fermentationTempRange` K | 285.15–330.15 | 285.15–330.15 |
| Fermentation O2 inhibition / participation | true / true | true / true |
| O2 comfort / stress / minimum efficiency | .02 / .12 / .65 | .02 / .12 / .65 |
| `mutationGateMinOrganicC` | .001 | .001 |
| Maximum stored OrganicC | 10 | 6 |
| Basal energy cost / simulated second | .01 | .01 |
| Biomass target / division multiple / C threshold | .2 / 2 / .4 | .15 / 2 / .3 |
| Division energy cost / child C share | .2 / .5 | .2 / .5 |
| Lethal shoulder / thermal mutation chance / scale | 20 K / .02 / 2 K | 20 K / .02 / 2 K |
| Scene founder lifespan range | — | 360–600 s, subsequently inherited |

At full performance, no competition and spare store capacity, the saved recipe consumes **.04 OrganicC/s**, stores **.016 C/s**, emits **.024 CO2/s + .024 H2/s**, and grants **.84 energy/s**. A full store redirects the consumed carbon to products and gives up to **1.4 energy/s**, still requiring measured external food. A newly divided .15-C daughter needs at least .15/.016 = **9.375 s** of uninterrupted productive uptake to regain the .3 division threshold, before stochastic birth timing. Default assimilation .15 gives .006 C/s and a 25 s minimum gap at the same threshold; default yield gives .068 energy/s. These are rate calculations, not experiments with retuned defaults.

Movement remains the saved .4 active angular multiplier, A/F speed multipliers 1.2/2, amoeboid turn 2 and flagellar turn 1.5; costs .02/.04 energy/s. The production locomotion and thermal algorithms are unchanged. The isolated gradient harness uses its existing E-speed test variant; reproductive cohorts and full-world replay retain the current movement settings.

## Transaction and carbon accounting

For each competed request:

1. Desired extent is `reactionDt / .5 × individualThermalPerformance × localO2Efficiency`. The OrganicC requirement must be finite and strictly positive. Allocation uses the existing node/resource demand buffers; fermenters share the available local OrganicC proportionally.
2. `GeodesicFermentationBalance.PlanCommit` calculates the actual representable double OrganicC debit. A zero or inaccurately representable debit rejects the transaction.
3. The planner budgets assimilation using the configured fraction and remaining capacity, then rounds the **actual float store increment downward**. Unstored carbon, including an unrepresentable tiny assimilation remainder, is fermented rather than silently lost.
4. CO2 and H2 credits equal this fermented carbon in the legacy inventory abstraction. Both double writes must pass the established strict relative transfer guard. Any rejection leaves all ocean values, stored C and energy unchanged.
5. Only after all ocean writes are planned and committed does the organism receive stored C and energy. Division/death use the existing authoritative store and existing recycling methods.

Thus `OrganicC withdrawn ≈ actual stored-C increase + measured CO2 output`, within the established floating-point transaction tolerance. H2 is **carbon-free** and is not added again to the carbon sum. This is the preserved simplified biochemical abstraction, not a claim of atom-balanced fermentation chemistry.

At full capacity the stored increment is zero; the entire measured withdrawal is fermented. With assimilation zero it is all fermented; with assimilation one it is stored while capacity permits, giving no catabolic energy for the stored portion. Zero required substrate produces neither energy nor food readiness. There is no stored-carbon respiration fallback.

The resource field remains the only dissolved authority. A specialized atomic commit writes OrganicC/CO2/H2 at the same actual node/layer. The existing accounting API gains an optional second product so H2 is visible beside CO2; existing H/S calls retain their defaults and measured accounting. There is no second OrganicC inventory: the added float cache is observation-only.

## Mutation, eligibility and inheritance

Sulfur's existing `biologySeed ^ 0x53554C46` stream retains its original outer roll, short-circuit inner roll, eligibility and counters. When its inner selection does not choose sulfur, fermentation draws its conditional roll from `biologySeed ^ 0x4645524D`. A selected sulfur candidate suppresses fermentation **even if sulfur's gate fails**, matching legacy `else if` priority. If sulfur chance is zero, its stream remains idle exactly as before; fermentation performs both outer and inner rolls on its own stream. If fermentation chance is zero it draws nothing from that stream.

With both edges enabled, the illustrative candidate probability per eligible **hydrogenotroph-parent birth**, before the fermentation gate, is `general × (1 − sulfurSelectionChance) × fermentationConditionalChance`. Current values give .01 × .97 × .02 = **.000194**. This is not an unconditional all-population mutation rate. A child can receive one selected identity; no reverse or fermenter-to-methanogen transition is introduced.

The fermentation gate reads actual child-cell/layer OrganicC concentration OR the **actual projected child store** from the configured division split. In energy-only reproduction there is no fictional carbon allocation to use as fallback. The existing zero minimum threshold disables the carbon eligibility requirement, as in legacy; it does not create productive food. Eligibility is separate from runtime uptake, O2 inhibition and thermal exposure. All four ordinary world mutants passed via inherited store, not local food.

Parent metabolism is retained. Cell/layer, position, age initialization, locomotion inheritance/mutation, energy allocation and carbon split are unchanged. New fermentation traits rebase to the configured range and lethal margin before ordinary thermal mutation; same-metabolism offspring inherit their individual traits. The existing LOCOMOTION, TEMPERATURE and REPRODUCTION random streams receive no additional draws. Matched native births verify identical sulfur selections/counters, movement seeds, locomotion, energy and C allocation with fermentation enabled versus disabled.

## Organic-carbon ecology and controlled tests

The normal pathway is measured CO2 fixation by H/S → internal stored C → real death at its actual node/layer → dissolved OrganicC → existing conservative ocean transport → fermentation → stored C plus CO2/H2 → normal division/death. Current abiotic chemistry consumes O2/H2/H2S/Fe2, not OrganicC. No additional abiotic OrganicC decay or new detritus source was added. Transport can dilute and redistribute local food even when the global pool grows.

Synthetic rich/poor assays use eight manually selected fermenter founders at 300 K, .15 internal C and 1 energy each, and the saved .02/.7/.4 recipe. These matched founder endowments are included in the ledger. The rich condition has a finite **3.2-unit initial test reservoir**, with no ongoing injection; the poor condition starts with zero dissolved OrganicC. These are phenotype-selection tests, not ordinary founder spawning or guaranteed mutations. At 80 s:

| Condition | Anchored | Amoeboid | Flagellar |
|---|---:|---:|---:|
| Rich: survivors / births | 16 / 8 | 16 / 8 | 16 / 8 |
| Poor: survivors / births | 8 / 0 | 0 / 0 | 0 / 0 |

Each rich cohort consumed 3.2 OrganicC, stored 1.2799963 C and returned 1.9200037 CO2/H2, with carbon residual about 1.8e-15. The tiny difference from the ideal 1.28/1.92 split is the deliberate conservative float-rounding redirect. Poor motile cohorts exhausted their existing energy under movement/maintenance costs, released 1.2 C on death, and had no survivors to consume it. Poor anchored organisms survived the 80 s window without food or births. Stored carbon was not respired to rescue them.

The mixed test starts **zero dissolved OrganicC**, eight H, eight S and eight F test organisms in a closed single-node habitat with finite producer resources. All have 100 s lifespans; producer founders start at age 80 to supply a controlled real death pulse, while descendants inherit normally. It uses actual requests, commits, reproduction and death at the shared node, not a new corpse/food injector. At 160 s it had H/S/F = **9/289/182**, with **190 fermenter-parent births**. Fermenters consumed **113.29104 C**, exactly the measured cumulative death input; whole-system C residual was zero. Cumulative flux can exceed the initial stock because the same carbon passes through repeated deaths; it is not additional carbon creation. Full-world tests below cover real physical transport, geometry and unforced metabolic evolution.

## Fixed-seed full-world comparison

The native runner was available and passed. It enters actual Play Mode in a disposable project, runs production generation/startup, biology, sun/temperature, transport, abiotic chemistry and mineral settling, and never saves the production scene. The saved setup's seed was 123456; both experiments use the explicitly requested **283**, subdivision **6**. Each trial runs **36,000 fixed 1/30 s steps through 1200 s**, with 240 transport ticks and 1200 s of ocean thermal integration. Spawn delay 300 s, 20 normal H founders, resource/thermal/movement/maintenance settings remain unchanged.

Actual physical source rates H2/H2S/CO2/Fe2 are **50/50/20/0 concentration×km³ per second** (saved pre-multiplier 5/5/2/0, multiplier 10). Initial ocean CO2=1, O2=0, Fe2=8 and OrganicC=0 are retained. The only difference between the two exported applied configurations is `hydrogenToFermentationMutationChance`: 0 versus the user-confirmed .02. No OrganicC was injected. Chemotaxis remains enabled; diagnostic A/B observation is disabled identically in both trials, with no steering override.

| At 1200 simulated seconds | Mutation disabled | Mutation enabled |
|---|---:|---:|
| Total population | 26223 | 30000 (existing cap) |
| Hydrogenotrophs / sulfur organisms | 16719 / 9504 | 15275 / 11998 |
| Fermenters / peak | 0 / 0 | 2727 / 2727 |
| First successful fermentation mutation | none | 580.90003 s |
| F attempts / allowed / blocked | 0 / 0 / 0 | 4 / 4 / 0 |
| F births / deaths | 0 / 0 | 2728 / 1 |
| F-parent births P/A/F/X | 0/0/0/0 | 468/2016/60/180 |
| Final F population P/A/F/X | 0/0/0/0 | 465/2006/74/182 |
| Measured death C delivered to water | 6885.12317 | 6140.77180 |
| F OrganicC consumption / assimilation | 0 / 0 | 1638.57239 / 655.42575 |
| F CO2 / H2 produced | 0 / 0 | 983.14664 / 983.14664 |
| F energy generated | 0 | 34410.13265 |
| Remaining dissolved OrganicC | 6885.12317 | 4502.19941 |
| Organic pool ledger residual | −2.12e-10 | −1.26e-10 |
| Modeled H2 withdrawal by hydrogenotrophs | 9323.72136 | 9537.66152 |
| Modeled hydrogenotroph C fixation | 4661.86068 | 4768.83076 |

The pool check is `remaining dissolved OrganicC + fermentation uptake − measured death input`; it closes without an invented OrganicC source. Fermentation input versus store plus CO2 differs by only about 5.5e-8 inventory units over the enabled run. H2/CO2 credits differ by about 5.4e-8 from independent double rounding. Existing world logs recorded zero resource transfer violations, input-transfer violations or energy-without-input incidents.

Enabled-run mean individual F thermal performance was **.981205**, O2 efficiency **1**, and experienced OrganicC concentration **.000104219** (request-weighted). O2 was zero; separate native cases establish the configured 1/.825/.65 efficiencies at comfort/midpoint/stress. **760842** tiny competing transactions were precision-rejected; no resource, energy or store credit is awarded for them. The original strict tolerance was not relaxed.

Fermentation-derived H2 equals about **10.31%** of modeled hydrogenotroph H2 withdrawal in the enabled trial. Hydrogenotroph C fixation rose about 2.29%, while its final population fell and sulfur/fermenter populations grew. These paired outcomes do not isolate the effect of H2 products from changed lifecycles, competition and subsequent evolution. The enabled endpoint hits the pre-existing 30000 population cap: it is not evidence of an uncapped substrate-limited equilibrium. Finite-substrate validity is established by actual committed debits and the recycling ledger. No unlimited positive-feedback carbon source was observed, and no permanent coexistence or default-parameter fitness claim is made.

## Verification and performance

The results below were obtained before the user's instruction to stop further runs. No additional Unity tests, simulations, benchmarks or validation runs are authorized. Future milestone testing and simulation experiments are left to the user unless explicitly requested. Manual gameplay review, longer sessions, additional seeds and uncapped ecological equilibrium remain unverified by this milestone.

Final native graphics-enabled EditMode run: **694 cases, 677 passed, 17 failed, 0 skipped**. The 17 failure names match the already documented baseline exactly (stale passive movement expectations, hydrology/startup cases and older reflection/setup fixtures); none is a new fermentation failure. Native fermentation runtime cases: **28/28**. Ecology/sensing/performance cases: **7/7**. Startup evolution/persistence/GUI cases: **11/11**, including real keyboard editing of fermentation probability and recipe parameters. The separate full-world paired Unity test passed and entered/exited actual Play Mode. Counts overlap; do not add them as unique cases.

Production and EditMode assemblies compiled. Selected managed carbon/chemistry cases passed **55/55**, food/taxis cases **20/20**. Tests cover no-food/no-stored-respiration, zero/invalid requirements, finite competition, full stores, output locations, local O2, both nested rolls, distant-food rejection, inherited fallback, thermal/color/locomotion inheritance, real division/death, snapshots, same-seed sulfur selection and blocked-sulfur priority. Existing H/S conservation and locomotion tests retain their prior results.

A warmed public production Step with **10000 anchored fermenters in one occupied test node** allocated **0 managed bytes**, and took **11.238 ms** in the final recorded native run. This verifies absence of per-organism warm-step allocations, not an end-to-end GPU/frame guarantee for a dispersed moving world. New work uses existing sparse request/demand buffers, one observation cache, constant-time local sampling and value-type transactions. No new whole-ocean sweep is added to biology ticks; world inventory samples are fixture-only, every 100 simulated seconds.

Raw XML/logs remain under ignored `Temp/FermentationAudit` and `Temp/SulfurAudit`; durable measurements, applied configurations, fixture counts and baseline failures are under `Docs/Diagnostics/FermentationAudit`. Repeat using the existing guarded disposable project:

```powershell
Tools/ValidateSulfur.ps1 -CompileManaged
Tools/ValidateSulfur.ps1 -NativeProject <absolute-disposable-project> -Regressions -Graphics
Tools/ValidateSulfur.ps1 -NativeProject <absolute-disposable-project> -TestFilter '^GeodesicFermentationWorldExperiments\.'
```

The validation copy blocks the shared startup writer; isolated startup test files are allowlisted for regression tests. Final saved user settings match the native experiment's pre-trial bytes, including the confirmed .02 probability. Source defaults, production scene, physical settings and persistence mappings are unedited. Save schema remains v23; existing enums and packed state already represent fermentation. As with existing streams, save/load preserves metabolic/thermal/carbon/movement state, but newly added RNG state is not serialized and no new promise of trajectory-identical resume is made.

## Remaining limits

- The abstract H2/CO2 fermentation outputs and legacy energy normalization are preserved; no new methane, O2-producing, saprotrophic or predatory pathway is enabled.
- Eligibility through inherited C does not imply local food. Existing banked energy can support temporary survival, while actual fermentation waits for environmental OrganicC.
- Local food can be diluted or numerically too small after competition; strict transaction rejection can leave a positive prospective food signal without a completed reaction, as in existing resource-backed pathways.
- Temporal chemotaxis retains hard-zero thermal clipping and has no distant knowledge of deaths or vents. No locomotion settings were retuned.
- Full-store diversion to catabolism is deliberate legacy parity. Float assimilation rounding is returned as products rather than lost; this can change a tiny amount of energy compared with ideal arithmetic.
- One 1200 s paired seed and short controlled assays demonstrate a real recycling niche and descendants, not equilibrium or robustness across seeds. The executed .7 yield/.4 assimilation differ from code defaults; no stronger defaults or other parameter adjustments are proposed or applied.
