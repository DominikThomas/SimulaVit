# Geodesic locomotion evolution audit — 17 November 2026

Follow-up: [GeodesicFoodTaxisAudit.md](GeodesicFoodTaxisAudit.md) resolves the native
execution blocker and records completed food-gradient tests, 48 controlled cohort
outcomes and a fixed-step seed-283 OFF/ON world pair. The bootstrap limitation
below describes the earlier audit stage; it is no longer the validation status.

Mutation production is consistent with the implemented probabilities. The supplied
population imbalance is strongly supported as reproductive selection after rare
anchored origins, rather than excessive generation of anchored mutants. Both
recorded seed-283 runs used effective **mode B (feedback disabled)** throughout.
The ecological investigation is **not resolved**: native controlled experiments
were added and compiled, but Unity exited during bootstrap before running them.
No speed, mutation rate, thermal trait, source, reaction, population or division
rule has been balanced to produce a preferred population distribution.

## Authoritative transition graph

`GeodesicBiologyRuntime.RunLifecycle` calls
`ResolveGeodesicInheritedLocomotion` exactly once for each successful birth, after
the parent's thermal, energy, carbon, reproduction-roll and population-cap gates.
The child initially inherits its parent's type. Existing adults do not undergo
this mutation check. Explicit manager diagnostic assignments are a separate path.
`ReplicatorSimulationPipeline.RunSimulationStep` returns immediately after the
geodesic step; legacy reproduction/mutation does not also execute on that step.

The existing decision was extracted into `GeodesicLocomotionMutationPolicy.Sample`
for testing; branch order and random draws are preserved:

1. Correct an invalid inherited enum to Passive, never to Anchored.
2. Outer attempt gate `m = locomotionMutationChance`, unless the explicit
   `geodesicLocomotion.mutationRateOverrideEnabled` selects its diagnostic rate.
3. Roll `a = locomotionAnchoredMutationChance` first.
   - A non-Anchored child becomes Anchored only if its parent's logical habitat
     is an active bottom-layer node. This takes priority over an upgrade.
   - An Anchored parent can produce Amoeboid only when anchoring was rolled,
     `allowAnchoredToAmoeboidMutation` is true, and the additional release roll
     `r = anchoredToAmoeboidMutationChance` succeeds. Release has no bottom gate.
4. If still unchanged, roll `u = locomotionUpgradeChance`: Passive → Amoeboid,
   Amoeboid → Flagellum. An ineligible anchor attempt falls through to this roll.
   A failed Anchored release also falls through, but there is no Anchored upgrade.
5. Otherwise inherit unchanged. Flagellum can anchor at the bottom and otherwise
   remains Flagellum. There are no downgrades, direct Passive → Flagellum,
   Anchored → Flagellum, or active → Passive paths.

The legacy `ReplicatorManager.ResolveInheritedLocomotion` has the same type graph,
priority, fallthrough and nested release. Its geography helper also allows
anchoring on land/unknown geography; the active geodesic runtime is ocean-only
and requires an actual bottom node. No missing ocean locomotion transition was
found. Both implementations simply inherit/clamp locomotion skill.

Let `b` equal one for an eligible bottom node and zero otherwise. Define
`q = m*a*b`, `v = m*(1-a*b)*u`, and `w = m*a*r` when release is enabled (zero
otherwise). In P/A/F/X row and column order, the complete matrix is:

| Parent | Passive | Amoeboid | Flagellum | Anchored |
|---|---:|---:|---:|---:|
| Passive | 1−q−v | v | impossible | q |
| Amoeboid | impossible | 1−q−v | v | q |
| Flagellum | impossible | impossible | 1−q | q |
| Anchored | impossible | w | impossible | 1−w |

The observed startup settings enable release with `r=.01`. These are probabilities
per successful parental birth, not probabilities per living organism per second.

### Previous settings: m=.06, u=.05, a=.015, r=.01

Active bottom node:

| Parent | P | A | F | X |
|---|---:|---:|---:|---:|
| P | .996145 | .002955 | impossible | .000900 |
| A | impossible | .996145 | .002955 | .000900 |
| F | impossible | impossible | .999100 | .000900 |
| X | impossible | .000009 | impossible | .999991 |

Not at the bottom:

| Parent | P | A | F | X |
|---|---:|---:|---:|---:|
| P | .997 | .003 | impossible | ineligible |
| A | impossible | .997 | .003 | ineligible |
| F | impossible | impossible | 1 | ineligible |
| X | impossible | .000009 | impossible | .999991 |

### Latest settings: m=.20, u=.05, a=.001, r=.01

Active bottom node:

| Parent | P | A | F | X |
|---|---:|---:|---:|---:|
| P | .989810 | .009990 | impossible | .000200 |
| A | impossible | .989810 | .009990 | .000200 |
| F | impossible | impossible | .999800 | .000200 |
| X | impossible | .000002 | impossible | .999998 |

Not at the bottom:

| Parent | P | A | F | X |
|---|---:|---:|---:|---:|
| P | .99 | .01 | impossible | ineligible |
| A | impossible | .99 | .01 | ineligible |
| F | impossible | impossible | 1 | ineligible |
| X | impossible | .000002 | impossible | .999998 |

Thus `.20` means an attempt is selected for 20% of births; only about **1%** of
Passive births produce Amoeboid. Bottom anchoring fell from .09% to .02% per
non-Anchored birth, a **4.5-fold** reduction across these two configurations.
Holding `m` fixed, changing `a` from .001 to .015 increases anchoring fifteenfold.
It also increases the nested release path fifteenfold. Anchored inheritance is
almost absorbing: mean waiting births for a release are about 111,111 previously
and 500,000 currently, not 100.

## Mutation-only experiment: expected versus observed

`Tools/ValidateLocomotionAudit.ps1` samples the actual production policy without
resources, movement, mortality or reproductive selection. Each row contains
1,000,000 independent birth decisions with a fixed System.Random seed recorded
in [the full sample](Diagnostics/GeodesicLocomotionMutationSample.json).
Runtime-sanitized float values are used, including release enabled and mutation
override disabled. Each row's expected counts, binomial standard deviations and
observed counts are exported. The following bottom-node counts are shown as
**observed / expected**, in P/A/F/X order:

| Settings | Parent | Observed P/A/F/X | Expected P/A/F/X |
|---|---|---|---|
| Previous | P | 996193 / 2903 / 0 / 904 | 996145 / 2955 / 0 / 900 |
| Previous | A | 0 / 996248 / 2889 / 863 | 0 / 996145 / 2955 / 900 |
| Previous | F | 0 / 0 / 999154 / 846 | 0 / 0 / 999100 / 900 |
| Previous | X | 0 / 13 / 0 / 999987 | 0 / 9 / 0 / 999991 |
| Latest | P | 989827 / 9968 / 0 / 205 | 989810 / 9990 / 0 / 200 |
| Latest | A | 0 / 989893 / 9909 / 198 | 0 / 989810 / 9990 / 200 |
| Latest | F | 0 / 0 / 999792 / 208 | 0 / 0 / 999800 / 200 |
| Latest | X | 0 / 3 / 0 / 999997 | 0 / 2 / 0 / 999998 |

For a transition probability `p`, count SD is `sqrt(N*p*(1-p))`. For example,
previous P→A expects 2955 ±54.28 (one SD), previous P→X 900 ±29.99;
latest P→A 9990 ±99.45 and latest P→X 200 ±14.14. Rare-release expectations
are 9 ±3 and 2 ±1.41; their low-count distributions should not be treated as
normally distributed confidence intervals.

All bottom and non-bottom rows, plus four matched-rate anchoring comparison rows,
total **20 million decisions**. No impossible transition occurred. The largest
absolute standardized deviation was 2.334. The predeclared rejection bound was
six SD plus two counts, rather than an assertion of exact frequencies.

The comparison holding `m=.20,u=.05` fixed produced 2911, 2941 and 2980 anchored
children from P/A/F respectively at `a=.015`, versus 205, 198 and 208 at
`a=.001`; expectations were 3000 versus 200. A literal copy of the pre-audit
decision tree also matches the extracted production decision and following RNG
value after 20,000 decisions for each source enum (including invalid values),
habitat and configuration. Extraction does not add random draws or double-roll
birth mutations.

Normal gameplay uses a separate mutation stream seeded from
`biologySeed ^ 0x4C4F434F`; the logged biology seed is 2042016952 for these worlds.
The isolated experiment deliberately uses explicit per-parent/per-habitat seeds
so it can sample without constructing a world. The JSON records each actual
sampling seed; it is not a reproduction of the world's entire RNG history.

## Evidence from the two existing seed-283 runs

The source is the local Editor.log. Reproducible extraction is in
`Tools/ExtractLocomotionRunEvidence.py`; the relevant initialization segments are
39 and 41, at source lines 122196 and 126338. See the aggregate
[selected-run evidence](Diagnostics/GeodesicLocomotionRunEvidence.json).
All logged environment settings match; the biology startup JSON differs only in
the outer mutation rate and anchoring probability. Unlogged live Inspector
changes cannot be excluded retroactively; the new resolved log covers the
previously unreported mutation override, speeds and sensing controls.

| Measurement | Previous run through 1040.035 s | Latest run through 1000.098 s |
|---|---:|---:|
| P/A/F/X snapshot | 3402 / 10 / 0 / 9279 | 8780 / 89 / 0 / 1536 |
| Parent births P/A/F/X | 3759 / 0 / 0 / 9282 | 9199 / 0 / 0 / 1534 |
| Deaths P/A/F/X | 363 / 0 / 0 / 7 | 347 / 1 / 0 / 0 |
| Amoeboid organism-seconds | 3267.97 | 21180.13 |
| Anchored parent births / organism-second | .00455057 | .01077023 |
| Passive parent births / organism-second | .00274318 | .00391709 |
| First positive A snapshot | 550.035 s | 480.098 s |
| First positive X snapshot | 410.035 s | 670.098 s |
| Report windows / effective mode | 104 / B only | 100 / B only |

With zero initial Anchored founders, `finalX + deathsX - X-parent births` is
**4 and 2** respectively. This is **net outside origins minus X→A releases**,
not an exact count of independent founders. With zero A/F-parent births, only
P→X can introduce X. Expected P→X counts, assuming every P birth is bottom
eligible, are at most 3.383 and 1.840; four and two net introductions are
consistent with those small expectations. The old logs do not record each
birth's bottom eligibility or lineage, so this is not an exact binomial test
of real-run mutation counts.

Nearly all Anchored population growth can therefore be inherited reproduction
from very few origins. One early successful founder could produce most of the
final population, but old aggregate logs cannot establish which founder did so.
The earlier run already had Anchored organisms about 260 simulated seconds
earlier, leaving much more time for descendants. These two runs are **not a
steering A/B comparison**: both are B and both mutation parameters differ.

There were **zero Amoeboid-parent births in either complete recorded run**.
Since A→F requires an Amoeboid parent birth, the expected number of newly evolved
Flagella in these runs is exactly zero, regardless of a positive upgrade setting.
There is no evidence that Flagellum is inaccessible when an A parent reproduces;
the isolated experiment produces it at the expected frequency.

At 990.098 s in the latest run:

- Anchored: 1500 organisms, all vent-core; mean thermal performance .998265;
  129 parent births in the interval, 1.49198 metabolic energy per organism-second.
- Amoeboid: 88 organisms, no core occupants, 74 in ring 5+; snapshot mean thermal
  performance zero; zero parent births; .016884 metabolic energy per organism-second
  over the preceding interval, .02 movement energy per organism-second.
- Amoeboid mean energy is 131.648, mean carbon .177126. Division requires energy
  above .2, carbon at least `.15*2=.3`, and thermal performance at least .15.
  Energy alone cannot permit division. Every sampled A has zero performance at
  that snapshot; mean carbon is also below the division threshold (a mean does
  not establish that every individual fails the carbon gate).
- A small amount of interval metabolic energy is compatible with brief earlier
  visits to warm habitats and zero performance at the final snapshot. The
  reaction accounting fix is still present: .8 scales CO2, H2, carbon and energy
  together, with atomic measured substrate debits and capacity-limited reactions.

## Effective steering and configuration provenance

`SimulationStartupBiologyConfig.Capture/Apply` captures scene defaults and applies
the selected startup configuration to the manager. Schema-23 flat JSON persistence
reads/writes `enableGeodesicChemotacticSteering` and
`enableGeodesicChemotaxisABDiagnostics`. Saved values override Inspector defaults
before biology initializes. The current saved `startup_config.json` has steering
false and A/B diagnostics true; the serialized PlanetScene manager has both true.
Normal Reset Defaults intentionally retains both debug switches, and Reset
Advanced does not reset them. This explains how checking the scene Inspector or
resetting normal settings can still leave startup's effective steering off.

Each simulation step passes the manager's explicit steering flag to
`SetChemotaxisABContext`. That method **sets** the runtime flag. Mode A is simply
the label for true; B is the label for false. A/B diagnostics do not choose a
mode, force B, or change the flag. All 204 relevant population windows report B,
zero mode switches and no mixed windows. The startup JSON independently reports
false. `metabolismAwareSensing=True` chooses the observed signal; it does not
enable feedback.

In B, sensing and smoothed temporal memory still update and diagnostics can
classify improving/worsening experience. Horizontal runs use neutral tumble
probability after initial sensing; vertical outcome persistence is random rather
than suitability-driven. Seeing nonzero gradient counters does not prove that
gradient feedback controlled movement. In A, improved local temporal readiness
selects .05 reference tumble probability, worsening .65, neutral .20, followed
by existing bounds and time-hazard conversion.

New `[GeodesicResolvedLocomotion]` prints one authoritative runtime configuration
immediately after manager initialization, including the effective flag/mode,
observer independence, sensing model, diagnostic mutation override, actual
probabilities, angular speeds, movement costs and sense intervals. The Debug tab
explains the saved-value override. No saved value or switch meaning was changed.

## Movement, sensing, retention and possible asymmetries

### Speed and geometry

The production formula is `.002 rad/s * activeSpeedMultiplier * moveSpeed/4 *
phenotypeSpeed`. Serialized controls are active multiplier 2, moveSpeed 4,
A multiplier 1.2, F multiplier 2. This gives .0048/.008 rad/s, approximately
38.4/64 km/s at an 8000 km surface radius. The actual physical radius accounts
for ocean depth; there is no extra kilometre conversion factor in the telemetry.
While exploring vertically, horizontal effort is multiplied by `1/sqrt(2)`;
38.4 becomes about 27.15 km/s, consistent with the measured 28.1982 average.
These are effective model transport speeds, not microscopic swimming velocities.

Production topology at subdivision 6 has 40,962 cells. Neighbour center distances
at radius 8000 km range from 138.329 to 165.432 km, mean 151.087 km. Straight
horizontal travel from a cell center to a typical boundary takes about **1.97 s
for A and 1.18 s for F**, before turns or diagonal effort sharing. Retention is
therefore a real concern, even without a distance-conversion error. Changing
speed would change selection and is a separate design decision.

Movement substeps are at most .1 s and 20% of minimum local center spacing, with
real neighbour traversal. Sensing occurs before a substep on its own .5/.2 s
jittered A/F schedule, not automatically at each boundary. A central cell crossing
usually has several opportunities to sense, but short corner/grazing chords can
be entered and left between senses. At 990 s, 218 cell-only endpoint comparisons
contained 219 crossings, already consistent with an extra intermediate crossing.
This does not prove that the skipped habitat was a productive vent.

### What a swimmer can sense

Hydrogenotroph readiness is
`min(CO2Inventory/CO2Need, H2Inventory/H2Need) * thermalPerformance * O2Efficiency`.
It uses current local inventory, temperature and inherited traits, not distance
to a known vent. Comparison uses raw readiness relative to smoothed memory and
the numerical floor, **before** logarithmic display-score compression. Display
saturation does not remove raw gradients. A senses H2/temperature only through
this combined score when metabolism-aware sensing is selected.

Two cold cells with thermal performance zero both have readiness zero regardless
of their H2 or raw temperatures. A cannot infer which zero-performance cell is
closer to recovery from this signal. Immediately after leaving a productive node,
remaining positive memory can cause a worsening classification; as memory fades
below the floor, comparisons become neutral. Neither temporal sensing nor random
tumbles know the direction home. Amoeboid turn requests are random within ±60°
and bounded at .5 rad/s; a worse score is not an instant 180° reversal. At 990 s,
1585 experienced comparison pairs had zero readiness on both samples. A enabled
would respond to resolvable local gradients, but would not solve this flat-zero
recovery problem by itself.

Cold performance zero does not stop propulsion. A/F movement costs are .02/.04
energy/s plus ordinary basal/stress maintenance, charged on paid propulsion time
even when metabolism is zero or a coast blocks travel. Insufficient positive
energy proportionally reduces effort. Thermal exposure and carbon gates still
apply independently. Large inherited energy stores can keep an A alive while
it accumulates too little carbon or thermal suitability to reproduce.

### Anchored advantage audit

| Mechanism | Finding |
|---|---|
| Movement and cost | X intentionally has no passive drift, active swimming or active movement cost. P also has no active cost. X stays in its assigned habitat. |
| Resource access / competition | Same cell/layer inventories, volume units, proportional demand competition, reaction and store-cap logic. No X-specific resource priority found. |
| Reproduction | Same thermal, energy, carbon, rate and population-cap gates; same cost and energy/carbon split. X skips division **presentation**, not biological gates. |
| Mortality / recycling | Same age, energy and thermal-exposure policy and local death-carbon recycling. No X survival bonus found. |
| Birth placement | Child inherits the same logical node; bounded same-cell daughter separation; fresh movement memory, inherited thermal traits. No special teleport to a vent for X. |
| Inheritance | X is nearly absorbing because release is nested `m*a*r`. This is an existing rule, not an observed RNG error. |
| Logical versus rendered depth | Passive vertical changes switch logical layer before visual radius interpolates. An active newborn reconciles that radius on its first active tick; X never performs that active reconciliation. This existing asymmetry can preserve a logical bottom assignment while the rendered body is outside it. Its contribution in the supplied runs is unknown. |

The last row is a genuine representational asymmetry, not proof of extra carbon,
an erroneous habitat assignment for sessile biology, or the cause of dominance.
New bounded birth-depth counts and maximum physical mismatch by phenotype expose
its occurrence without relocating organisms or silently redefining attachment.
Tiny floating point boundary offsets should be distinguished from material depth
mismatches. Controlled cohorts start at the actual bottom center to exclude it.

## Added diagnostics, tests and scope

`GeodesicLocomotionEvolutionDiagnostics` observes the existing lifecycle pass:

- Cumulative 4×4 **all births** matrix, including unchanged inheritance; separate
  mutant/inherited child counts and parent births per organism-second.
- Outer attempts, successes, no-change reasons and anchor eligibility/release/
  upgrade-stage counts by parent; diagnostic adult assignments kept separate.
- Deaths and first observed appearance times by phenotype; complete-lifetime
  offspring sums for deaths born under coverage. Initial/imported organisms are
  explicitly lifetime-censored. Surviving lifetimes are not mistaken for completed
  lifetimes, and first baseline observation can lag spawn by one biology step.
- Eight sampled untracked mutant roots per destination phenotype, maximum 32,
  with aggregate descendant births/deaths/alive/organism-seconds. Descendants stay
  in their root cohort through later phenotype changes, rather than starting
  overlapping ancestry trees. Untracked additional roots are counted.
- Birth depth outside the assigned layer, by phenotype.

The sidecar is O(live-population capacity), kept with stable organism IDs through
swap-back removal. History is bounded to 32 root aggregates; no per-agent log or
gameplay random draw is introduced. Two aggregate lines use the existing ten
simulated-second report cadence when locomotion or A/B diagnostics are enabled.
Toggling coverage resets these observation counters; they are not save-game
lineages and cannot reconstruct ancestry preceding coverage.

Production modifications are confined to `GeodesicBiologyRuntime` partials,
`ReplicatorManager`, the new diagnostic/pure decision helpers, and one startup
Debug help line. Existing carbon transactions, environmental sources, temperature
policy, mutation paths, population cap and division math remain intact. There
is no saved-config schema or migration change.

Verification completed:

- Full runtime and EditMode test assemblies compiled using the restored Unity
  Roslyn compiler responses. Only existing unused-field warnings were emitted.
- **19 locomotion managed tests passed**: matrices, sampling, original decision
  and RNG regression, gates/ordering/release/override, cohort accounting,
  swap-back/lifetime censoring, adult assignments, birth-depth observations,
  A/B response and flat-zero/deadband/display-saturation cases.
- **55 existing selected managed carbon/chemistry tests passed** as regression
  checks. This is not a full Unity/native test suite.
- 20-million-decision mutation experiment and actual subdivision-6 topology
  measurement completed; `git diff --check` passed.

### Controlled ecological fixtures: compiled, not executed

`GeodesicLocomotionEcologyExperiments` adds A/B/C/D production-method experiments:

| Experiment | Condition |
|---|---|
| A | Feedback disabled; one warm vent-bottom node, cold ocean. |
| B | Identical habitat and controls, feedback enabled. |
| C | Same as B; restore spatial position after movement, retaining actual paid effort, phenotype, sensing state and all other costs. Attempted distance is reported separately from actual horizontal displacement. |
| D | Feedback enabled; controlled local H2/thermal shoulders plus a cold adjacent zero-performance cell. Displace original founders into that cold node at 10 s, retaining temporal memory. |

Each has four equal eight-founder cohorts in separate cloned fields, identical
seed-283 random streams, subdivision-6 geometry, initial energy 100, carbon .15,
biomass target .15, inherited 323.15–353.15 K range and 20 K margin. Duration is
60 s in .05 s increments; production .1 s movement and .25 s reaction cadences,
hydrogenotrophy .01/.02/8/.8, basal .01, reproduction .5, division .2/2/.5 and cap
30000 are retained. Explicit single-fixture-vent source is 50 H2 / 20 CO2 inventory
units/s (saved base rates 5/2 times multiplier 10). This is a frozen **synthetic
habitat**, not the seed-283 generated world's vent inventory, climate or transport.
Locomotion and thermal mutations are disabled only in these selection fixtures.
Normal founder seeding and normal settings are unaffected.

Reports include normalized residence and unsuitable time, departed/returned
founders, actual horizontal and attempted active distance/crossings, metabolic
and movement energy, carbon gain/balance, births/deaths, surviving founders and
descendants, trend classifications and mean chosen tumble probabilities. Return
fraction uses departed founders as denominator; no departures is reported as zero
with denominator zero, not evidence of failed return. Assertions check inventory
balance, population reconciliation and immobilized-cost invariants, without
asserting a preferred phenotype ranking.

Sixty-second fixtures measure short-term retention and reproduction; they cannot
establish long-term cold-exposure mortality, lifespan selection or ecological
equilibrium. Longer matched full-world runs remain necessary for those claims.

Two native Unity attempts exited **1 before project/test-runner initialization**;
no result XML was produced. The sandbox attempt logged WMI assertions; the
outside-sandbox attempt logged licensing validation/access-token errors before
exiting. These logs do not establish a definitive bootstrap root cause. Neither
the four ecological outcomes nor a matched full seed-283 A/B replay is available.
Do not treat compilation or mutation-only tests as proof of ecological dynamics.

## Reproduction and next steps

Run `pwsh -NoProfile -File Tools/ValidateLocomotionAudit.ps1 -CompileManaged` for
the managed checks and exported mutation/geometry data. Add `-UnityTests` for the
native runner, or run `GeodesicLocomotionEcologyExperiments` in Unity's EditMode
Test Runner. Results go to `Temp/LocomotionAudit/ecology-A.json` through `-D.json`.

For the full-world comparison, use seed 283/subdivision 6, identical saved startup
configuration and scene controls, and an identical simulation-step sequence:

1. Keep latest `m=.20,u=.05,a=.001,r=.01`, current vent/temperature settings and
   corrected carbon model fixed. Enable A/B diagnostics in both runs.
2. Set **Debug → Enable geodesic chemotactic steering** off for one fresh run and
   on for the matched run. Check `[GeodesicResolvedLocomotion]` actually says B/A.
3. Compare through the same authoritative simulation time, including cohort
   origins, inherited births and births per organism-second. Do not compare runs
   stopped at different times or with changed mutation controls as steering tests.
4. Examine both population growth and per-capita residence, metabolism, movement
   cost, return and thermal-zero exposure. Native fixture outcomes should guide
   interpretation; several independent founder histories are needed before
   general ecological balancing claims.

Ranked recommendations, pending those controlled outcomes:

1. **Smallest immediate step:** use the intended saved steering flag and verify
   the resolved log; collect the matched A/B and A/B/C/D results. No probability
   or fitness change is justified by the evidence so far.
2. If A still cannot recover from cold regions, consider an explicitly local
   recovery signal that retains information when metabolic readiness is zero
   (for example a separate thermal-distance comparison). This changes the
   sensing model and must be designed/tested separately; do not add vent lookup.
3. Evaluate residence relative to cell geometry, sense intervals and bounded turn
   rates. Test a slower effective transport scale separately before choosing it
   as a balance change; never compensate by changing resource or temperature rules.
4. If birth-depth counters show material mismatches, decide whether sessile
   attachment is logically to the parent's assigned layer or physically to its
   current radius, then add a focused reproduction test before changing it.
5. Reconsider the almost-absorbing X release gate only as an explicit evolution
   design decision. Raising A/F production alone cannot rescue lineages that
   never reproduce in the habitats they occupy.

## Verdict

**A. Probabilities:** correct for the implemented nested model; isolated sampling
and original-RNG regression support this. `.20` is not a 20% Amoeboid yield.

**B. Anchored dominance:** rare bottom anchoring followed by inherited expansion
in productive, persistent habitats is strongly supported. There is no evidence
of overproduction of Anchored mutants; exact founder dominance awaits cohort logs.

**C. Amoeboid failure:** complete logs show zero parental reproduction; late
snapshots show thermal inactivity away from vents and low mean division carbon.
Fast departure, mode-B random motion and flat-zero recovery are supported
mechanisms, but their causal shares await the controlled ecological outcomes.

**D. Flagellar absence:** the only path requires an A-parent birth. There were
none; the mutation-only test confirms the path works when such births exist.

**E. Chemotaxis:** it was not enabled in either supplied effective run. The local
temporal feedback rule responds correctly to resolvable signals in managed tests;
it cannot navigate a flat-zero metabolic field. Ecological effectiveness is unproven.

**F. Next change:** verify the intended saved A setting using the new resolved log
and finish matched ecological tests. No ecological balancing correction has been
implemented or declared successful.
