# Geodesic sulfur chemosynthesis restoration

Source baseline: `7893485`, branch `codex/geodesic-biology-parity`. The initial working tree was clean. No branch switch, commit, reset, merge or production scene save was performed. Existing locomotion and metabolic parameter values are preserved. Source and runtime parameters take precedence over older audit prose.

## Legacy reference and parity

The preserved metabolism file is byte-identical at `main:Assets/Scripts/ReplicatorMetabolismSystem.cs` and baseline `Assets/Scripts/Biology/Replicators/ReplicatorMetabolismSystem.cs` (blob `d31ee61f824e645e3cee1eb782d135e12bba71b9`). Relevant history was checked against source:

- `751ac3b`: historical **scene** sulfur/vent balance changes, not universal code defaults.
- `7240085`: reaction-backed sulfur extraction; runtime inputs still come from manager fields, not provisional registry coefficients.
- `af7a280`, `d06c666`: local mutation gates and layered reads. Sulfur explicitly requires H2S and CO2, ignores output S0, and has no low-O2 mutation gate.
- `c2c7f7d`, `86c520f`: O2 inhibition and authoritative local layered oxygen. Optional direct O2 damage ultimately applies to methanogenesis, not sulfur.
- `658cdd0`: individual temperature performance and exposure rather than an older immediate lethal-band kill.
- `a10c838`, `345ec65`: newer geodesic carbon transactions, conservative division energy, and thermal exposure supersede unsafe legacy behavior.

| Behavior | Before milestone | Delivered status / reason |
|---|---|---|
| CO2 + H2S → stored C + S0 + energy | Existing geodesic switch and proportional competition; manually injected sulfur could feed | Completed through the existing planner/allocation path with measured two-input commits |
| Quantities and energy | Legacy configured values available | Preserved; reference tick 0.5 s, geodesic reaction cadence unchanged at 0.25 s |
| Carbon store/cap | Sulfur ignored store fraction and consumed beyond capacity | Superseded: cap limits demand before competition; no carbon is discarded |
| Store fraction < 1 | Legacy consumes full CO2 and stores only its fraction, leaving the rest unaccounted | Intentional conservative difference: fraction scales whole-reaction activity, all withdrawn CO2 is stored, following corrected hydrogenotrophy |
| Individual thermal performance | Already operational in geodesic requests/lifecycle | Reused for sulfur feeding, reproduction and sensing; both substrate throughput and energy scale together |
| Local O2 inhibition | Missing for geodesic sulfur | Restored with existing smoothstep comfort/stress/floor model; not a mutation gate |
| O2 replication/speed caps | Legacy shared inhibition also limited speed/replication | Deferred: preserve current geodesic locomotion and thermal reproduction rules; default sulfur floor .6 already exceeds legacy replication floor .35 |
| Direct sulfur O2 toxicity | Not executed by final legacy sulfur pathway | Not introduced; legacy optional direct damage is methanogenesis-specific |
| Stored-carbon starvation respiration | Legacy sulfur failure branch only inspects configuration and updates starvation timers | No actual respiration fallback exists there; none added |
| Maintenance, starvation, lifespan and thermal death | Already operational in geodesic | Preserved, including full measured death-carbon recycling and accumulated thermal exposure |
| Division C/energy inheritance | Already operational | Preserved: configured C split, energy cost then equal split of remaining energy |
| H→sulfur metabolic evolution | Missing in reachable geodesic division | Restored only for newborns with nested outer/inner rolls and local child-habitat gates |
| Gate placement | Legacy could probe a bottom layer for sulfur | Intentional geodesic correction: actual inherited child cell/layer only, no relocation |
| Thermal inheritance on metabolism change | Legacy rebases onto new metabolism's band | Restored before existing individual thermal mutations; same-metabolism descendants inherit their parent traits |
| Locomotion inheritance/mutation | Already operational | Reused unchanged; independent deterministic metabolism RNG does not draw from birth/locomotion/thermal streams |
| Food taxis | Hydrogen-only | Sulfur now samples local CO2/H2S inventory, individual performance and local O2 efficiency; existing memory/tumbles/vertical exploration reused |
| Yellow appearance | Legacy color resolver already knew sulfur; geodesic inherited parent color | Child becomes yellow immediately; manager supplies existing age/energy color resolver during geodesic lifecycle; locomotion shapes remain unchanged |
| Diagnostics | Enum population and reaction slots existed | Bounded sulfur mutation/lifecycle/resource/O2/metabolism×locomotion totals and resolved configuration added |
| Other metabolic transitions | Geodesic switches/scaffolding existed without evolution | Deliberately deferred; no photosynthesis, sulfur photosynthesis, sulfate reduction, fermentation, methane or reverse edge enabled |

The legacy reaction first limits local CO2/H2S withdrawal, multiplies energy by individual thermal performance and O2 efficiency, and adds S0 equal to consumed H2S. Its storage increment is CO2 consumed × `chemosynthStoreFraction`, clamped at maximum store. That clamp and partial assimilation can lose carbon; they are not copied into the corrected runtime. Existing geodesic temperature scaling of the **whole** reaction is retained. Legacy sulfur does not consume stored C or O2 in its unsuccessful-reaction branch.

## Effective configuration

No Inspector defaults are written into components. `ApplyGeodesicRuntimeControls` forwards resolved manager values after startup/load overrides. `[GeodesicSulfurConfig]` prints effective runtime values, including the distinction between local mutation eligibility and O2 efficiency. These were the inspected saved seed-283 settings, which differ from defaults:

| Parameter | Code default | Inspected effective setting |
|---|---:|---:|
| `chemosynthesisCo2NeedPerTick` | .02 | .02 |
| `chemosynthesisH2sNeedPerTick` | .001 | .01 |
| `chemosynthesisEnergyPerTick` | .3 | .1 |
| `chemosynthStoreFraction` | 1 | 1 |
| `metabolismMutationChance` | .01 | .2 |
| `hydrogenToSulfurMutationChance` | .003 | .4 |
| Probability per eligible birth before habitat gate | .00003 | .08 |
| `sulfurChemoTempRange` K | 333.15–393.15 | 323.15–393.15 |
| `mutationGateMinH2S` | .0005 | .0005 |
| `mutationGateMinCO2` | .001 | .001 |
| O2 global inhibition / sulfur participation | true / true | true / true |
| O2 comfort / stress / minimum sulfur efficiency | .02 / .12 / .6 | .02 / .12 / .6 |
| `defaultLethalMargin` K | 20 | 20 |
| Thermal mutation chance / scale | .02 / 2 K | .02 / 2 K |
| Basal maintenance / second | .01 | .01 |
| Division biomass target / threshold | .20 / .40 | .15 / .30 |
| Division energy cost / child carbon share | .2 / .5 | .2 / .5 |
| `maxOrganicCStore` | 10 | 6 |

Defaults for rarely used fields should be checked in source rather than inferred from this table; serialized values remain authoritative. Float values are serialized as nearby binary32 values (for example .02 = 0.019999999552965165); complete applied world configurations are under `Docs/Diagnostics/SulfurAudit`.

Both mutation rolls must succeed before an attempt is counted. The dedicated stream is `new Random(biologySeed ^ 0x53554C46)`. Gate attempts/allowed/blocked mean candidate transitions reaching habitat evaluation, as in legacy gate telemetry. An offspring remains hydrogenotrophic when the habitat gate fails. Disabled deferred pathways do not consume their old legacy candidate rolls. The legacy nested sulfur edge is retained without activating those pathways.

Geodesic mutation thresholds compare **local physical concentration** with the existing numeric field, plus require a positive substrate even if the threshold is zero. Legacy cube thresholds were area-normalized per-cell amounts. Applying that cube-area multiplier to concentration×km³ inventories would mix units, so it is not carried over. The exact units are logged. Gate concentration can be restrictive even when a huge physical layer contains many reference ticks of inventory; no gate values were retuned.

At full performance and no competition, saved settings give .04 C/s, .02 H2S/s and .2 energy/s, versus default .04 C/s, .002 H2S/s and .6 energy/s. Filling the .15 C gap after an equal division needs at least 3.75 s, before stochastic birth timing or resource competition. At maximal O2 stress the saved rates become .024 C/s, .012 H2S/s and .12 energy/s. Existing .01/s basal and .02/.04/s active movement costs remain. These calculations assume sustained food and suitable temperature, not guaranteed survival.

Saved experimental movement is retained: `activeSpeedMultiplier=.4`, move speed 4, A/F speed multipliers 1.2/2, amoeboid turn rate 2, flagellar turn rate 1.5. Locomotion mutation/upgrade/anchor/release probabilities are unchanged. No movement or evolution defaults were retuned.

## Carbon and sulfur accounting

1. The existing request computes `dt/.5 × individualTemperaturePerformance × localO2Efficiency × clamp01(storeFraction)`. Both substrate requirements must be finite and strictly positive.
2. The existing carbon transaction planner limits demand to remaining float store capacity **before** shared-resource competition. Sulfur and hydrogen requests compete for the same authoritative CO2 node inventory.
3. The shared two-input transaction rounds the store gain downward to a representable float, derives extent and donor consumption from that gain, plans both double ocean concentration writes, and verifies both measured withdrawals using the established hydrogen tolerance. No writes or credits occur on rejection.
4. Only after successful debits does sulfur receive energy/carbon and add **measured H2S withdrawal** to the existing suspended S0 field at its actual node/layer. Abiotic oxidation, FeS precipitation, suspension transport and sedimentation retain their existing authorities.
5. Division transfers existing stored carbon; death recycles it once into the local dissolved OrganicC authority. No new structural carbon, dissolved S0 pool, direct seabed production or unverified respiration is introduced.

For a fraction below one, unassimilated carbon stays in environmental CO2 because the entire reaction is reduced. For fraction zero, metabolism stops. A full store likewise stops withdrawal/energy/S0 production rather than discarding carbon. This is an explicitly documented conservation correction, not a stronger sulfur energy yield.

## Environmental source and opt-in reproduction

The inspected original geodesic H2S source was zero. The legacy `ventH2SPerTick` field is not the geodesic physical source. Use these **opt-in** startup settings for the world fixture or next manual experiment:

- Fixed seed **283**, `GeodesicIcosphere`, subdivision **6**. Keep terrain, hydrology, layers and all saved locomotion settings.
- `geodesicVentProductionMultiplier = 10`.
- `geodesicVentH2PhysicalPerSecond = 5`, `geodesicVentH2SPhysicalPerSecond = 2`, `geodesicVentCO2PhysicalPerSecond = 2`, `geodesicVentFe2PhysicalPerSecond = 0`.
- These are pre-multiplier physical inventory rates. Resolved H2/H2S/CO2/Fe2 = **50/20/20/0 concentration×km³ per simulated second**. No legacy reference-resolution conversion is involved.
- Keep 20 founders, biology spawn delay 300 s, initial dissolved CO2 = 1, O2 = 0, Fe2 = 8, all sulfur/thermal/mutation/maintenance settings above. Full startup JSON is exported, including the actual atmosphere.
- Matched control: change only `hydrogenToSulfurMutationChance` to **0**. The experimental supply stays nonzero in both worlds.

Production `SetStartupPhysicalVentRates` already supports H2S. `InjectVentSources` uses the same compact outlet weights and physical layer volumes as H2/CO2, puts submarine gases into actual outlet nodes, and routes the terrestrial share through the existing atmosphere. Seed 283 generated 42 systems / 56 compact outlets, submarine gas fraction .93469414, terrestrial .0653058602. Transport, layer concentration reads, biological consumption and mineral formation all use the same geodesic field.

Abiotic chemistry oxidizes H2S with O2 at half-life 120 s, requiring .5 O2 per H2S and yielding suspended S0. It then precipitates remaining H2S with Fe2 to suspended FeS at half-life 90 s (1:1 inventories). Per 5 s transport tick those fractions are about 2.847% and 3.778%, when the corresponding co-reactant is available. The saved ocean starts with Fe2 = 8 even though its ongoing Fe2 vent source is zero. This is a large existing competing sink; it is not disabled to help sulfur. O2 was zero in the executed world pair, so abiotic S0 oxidation was zero and FeS dominated the abiotic H2S sink.

## Verification and ecological evidence

The initial native sulfur suite passed **35/35** cases. The final guarded native sulfur suite passed **40/40**: 33 runtime cases, six controlled ecology/sensing/starvation cases, and one full-world paired test. The full world test enters and exits actual Play Mode inside its native EditMode host. Managed production + EditMode assemblies compiled; the existing selected managed checks passed **55/55** carbon/chemistry cases and **20/20** food/taxis cases. These counts overlap with native suites and must not be added as unique tests. The final suite includes production population-snapshot round trips, exact nested-RNG repeatability, high-volume transactions and occupied-layer isolation. Saved startup text was asserted unchanged, and its SHA-256 stayed `F5BA1464B5522650C2F62E8C91D5B56CC309814ED1F2D5719859D4346BC21D71` after guarded batch exit.

The tests exercise the public production `Step`, actual request/competition/commit methods, actual geodesic division/death, authoritative resource/mineral components, packed state and production population snapshots. Cases cover required inputs/zero requirements, mixed H/sulfur CO2 competition, measured S0, capacity/fraction carbon balance, local O2, child-only nested mutation and distant-H2S rejection, thermal/locomotion/color inheritance, H2S-only taxis feedback, no-H2S matched-control determinism and starvation without stored-carbon respiration. Native compilation is separate from native execution; helper arithmetic alone is not counted as native lifecycle verification.

The finite-habitat assays use saved sulfur yields, no new environmental source, eight hydrogen founders, an explicitly suitable 350 K layer, finite CO2/H2/H2S inventories and seed 123456. They establish accessible sulfur reproduction for anchored, amoeboid and flagellar phenotypes and compare with mutation-disabled controls. Their 80 s endpoint is not a steady-state or indefinite-coexistence claim. The fixture source explicitly applies the saved .4 active-speed multiplier and amoeboid turn rate 2; phenotype-selection cohorts disable locomotion mutation while retaining movement costs. The full-world trials retain the saved locomotion evolution probabilities.

The world protocol uses production startup/generation, biology, sunlight, ocean temperature, resource transport, abiotic chemistry and suspended mineral settling. Each trial drives **27,000 fixed 1/30 s steps through 900 s**, with 180 environmental transport ticks and 900 s of ocean thermal integration. It changes only the sulfur mutation setting between the paired applied configurations; H2S supply is an explicit opt-in change common to both. It is a controlled replay, not the historical variable-frame trajectory.

| World result at 900 s | Sulfur mutation OFF | Sulfur mutation ON |
|---|---:|---:|
| Final hydrogen population | 10519 | 9492 |
| Final sulfur population / peak | 0 / 0 | 2874 / 4056 |
| Sulfur candidates / allowed / blocked | 0 / 0 / 0 | 810 / 68 / 742 |
| Sulfur births / deaths | 0 / 0 | 19173 / 16299 |
| Sulfur-parent births | 0 | 19105 |
| Final sulfur P/A/F/X | 0/0/0/0 | 347/500/5/2022 |
| Sulfur-parent births P/A/F/X | 0/0/0/0 | 8438/6262/9/4396 |
| Sulfur completed reference-tick extent | 0 | 224129.3384 |
| CO2 withdrawn / C fixed | 0 / 0 | 4482.5866675 / 4482.5866675 |
| H2S withdrawn / S0 produced | 0 / 0 | 2241.2933337 / 2241.2933337 |
| Metabolic energy generated | 0 | 22412.9334 |
| Remaining ocean H2S inventory | 2366.1038 | 1735.1285 |
| Abiotic FeS produced | 14387.9700 | 12777.8125 |
| Suspended S0 / deposited S0 | 0 / 0 | 2241.0780955 / .2152383 |

Both paired executions reproduced all population, resource, birth/death and energy outcomes exactly. The final run adds per-metabolism observations and the startup-write guard. The paired run's aggregate C-fixed versus measured-CO2 difference was 3.4e-8 inventory units; suspended+deposited S0 agrees with measured biological H2S. FeS consumed about **5.70×** as much H2S as sulfur biology in the enabled world. The candidate block fraction was **91.60%**, all reported as insufficient local H2S in the bounded telemetry. Thus a real niche exists, but local physical concentration and competing FeS formation are major limits. The saved high candidate probability is far above the .00003 code-default probability; these results are not a claim that default-probability mutants will be common. No survival bonus or chemistry retuning was used.

In 1,160,993 one-second sulfur organism samples, 66.77% had positive individual thermal performance and local food, while 33.23% had zero thermal performance. Mean experienced H2S concentration was .00035032, below the .0005 **mutation** threshold; existing sulfur descendants can still metabolize below that threshold. Local O2 was zero in all 4,639,648 sulfur request samples, giving mean O2 efficiency 1; controlled native O2 cases separately establish inhibition. Precision guards rejected 2,509,897 small allocations (54.10% of all sulfur request samples, including requests in thermally inactive habitats in the denominator). This count is a real numerical limitation, not missing resources being awarded: these allocations receive no biological credit. No tolerance was relaxed.

The sulfur-disabled world had 10,551 total parent births. Applying the code-default nested probability .00003 to that many births would give only about .317 candidates **before** habitat gates, as an arithmetic expectation, not a rerun with default physiology. The executed saved .08 candidate probability is 2666.7 times larger. Default-probability rarity should not be mistaken for a disconnected mutation path.

Hydrogen-only behavior is checked with an exact no-H2S mutation-enabled/disabled runtime control; identical birth/movement/thermal outcomes are required. The full-world enabled trial intentionally changes competition and offspring identity, so its hydrogen population need not match the sulfur-disabled trial. Reactions throughout the completed first world pair reported zero model/measured/invalid violations, zero input transfer violations and zero energy-without-input incidents. Those are reaction ledgers, not a new proof of the entire atmosphere–ocean planetary carbon budget.

### Broad regression baseline

The final broad native EditMode run executed **649 cases: 632 passed, 17 failed** (the earlier pass before three additional layer/precision/O2 cases had 629 passes). Running unchanged `HEAD` source in the same disposable project executed **611 cases: 594 passed, the identical 17 failed**. The extra passing cases are sulfur coverage minus the obsolete assertion that sulfur sensing is absent. No new failure names were introduced.

The pre-existing failures are two passive-movement expectations, twelve terrain/hydrology/startup expectations, two obsolete `PlanetResourceMap.InitializeIfNeeded` reflection tests, and one manager/pipeline lifecycle setup test. Their complete names are retained in `Diagnostics/SulfurAudit/verification.json`. These are not reported as passing regressions or repaired by changing movement/terrain outside this milestone.

## Running the fixtures safely

`Tools/ValidateSulfur.ps1 -CompileManaged` compiles and executes the existing managed baseline checks. Native commands require a restored disposable project beneath `Temp/FoodTaxisAudit/UnityValidation-*`; reuse the existing validation copy rather than the live project:

```powershell
Tools/ValidateSulfur.ps1 -NativeProject <absolute-disposable-project> -Regressions
Tools/ValidateSulfur.ps1 -NativeProject <absolute-disposable-project> -World
```

Run commands sequentially. Each launches a hidden batch Editor and prints its PID and XML location; this launch message is not proof of completed tests. Check the XML root result and log completion. The tool synchronizes Assets, disables draft saving in the disposable scene, and inserts a batch-only early return into the startup-file writer **only in the disposable source copy**. Failing regression fixtures can leave additional constructed startup controllers alive, so the file-writer guard also covers those. Biological code is not replaced by this guard. The world fixture checks that guard and asserts saved startup text remains unchanged. It never saves a scene or startup configuration, and discards generation snapshots rather than publishing a new cache.

During the first native validation attempt, before this guard existed, the ordinary startup UI's `OnApplicationQuit → SaveSetupDraft` wrote the shared persistent startup file. The H2S field was observed changing from the verified original 0 to 5; it was restored to 0, and a copy of the observed post-batch file is retained under ignored `Temp/SulfurAudit`. No full pre-test raw-file backup was made before that first run, so other setup-draft fields cannot be certified byte-for-byte against their pre-test values. Production scene/Inspector settings remain unedited. A subsequent regression run exposed a leaked startup controller writing H2S=5 again; this time the exact pre-trial raw text was available and was restored byte-for-byte. The final writer-guarded regression exit preserved its SHA-256 exactly. Other values in that second write were verified equal to the backed-up pre-trial file. The absence of a raw backup before the first native attempt remains a verification limit. This harness side effect is reported separately from biological results.

## Remaining limits

- Temporal taxis still has the known hard-zero thermal clipping: neither hydrogen nor sulfur can recover a gradient through uniformly zero individual thermal performance. No distant vent lookup, neighbor scan, attraction bonus or locomotion retuning is added.
- A high local concentration gate, weak saved yield, large physical volumes and FeS precipitation can make sulfur rare. Legacy values are preserved first; no proposed balance adjustment is applied here.
- Strict inherited hydrogen transaction precision guards can reject very small sulfur allocations. Rejections produce no debit, energy, C or S0, and are counted. Competition does not redistribute these uncommitted tiny allocations within the same batch, as in hydrogenotrophy.
- O2 physiology is throughput inhibition. Existing geodesic thermal exposure, maintenance, reproduction and locomotion semantics are retained; no new sulfur direct O2 death, speed cap, respiration or starvation timer death is added.
- Population save schema/version is unchanged. Existing metabolism enums, individual traits, color, packed habitat and movement snapshots already carry sulfur; production snapshot round-trip tests cover the new reachable phenotype. RNG save/resume behavior is unchanged from existing geodesic streams and is not a promise of bit-exact trajectory replay across a save.
- The 80 s finite-habitat assays and 900 s world pair demonstrate descendants, not permanent coexistence or sulfur dominance. Chemistry/source transport and terrain are not retuned to guarantee those outcomes.
