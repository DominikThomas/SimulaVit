# Gradual geodesic founder emergence

Implemented from clean local baseline `555d1bd` on `codex/geodesic-biology-parity`. The fermentation implementation and the user's saved Hydrogen → Fermentation probability **0.02** are preserved. No metabolism, movement, terrain, ocean transport or vent-production parameters were retuned. Testing in this milestone was explicitly requested; the project preference remains user-owned testing for future milestones unless requested.

## Startup and compatibility

Startup → Biology now selects **Founder batch** or **Gradual emergence**. The default remains batch, including profiles saved before startup schema v24. Zero-delay batch spawning retains immediate founders and their existing sampled ages; delayed batches retain a single release with zero-age founders. The requested batch is now clipped to available population capacity, as required for all founder placement.

| Control | Meaning / default |
|---|---|
| Initial emergence delay | Existing `geodesicBiologySpawnDelaySeconds`; no change to saved values |
| Maximum initial founder events | Existing `initialSpawnCount`, relabeled in gradual mode; caps attempted initial events, including rejections |
| Mean initial emergence interval | New `geodesicFounderMeanIntervalSeconds`, default 10 simulated seconds |
| Ongoing independent emergence | New `geodesicFounderOngoingEmergence`, default **off** |
| Mean ongoing emergence interval | New `geodesicFounderOngoingMeanIntervalSeconds`, default 300 simulated seconds; used only with ongoing emergence enabled |

Startup schema v24 round-trips the new fields through defaults capture, draft copying, normalization, configuration application, saved JSON and resolved-settings logs. Older profiles explicitly remain batch with ongoing emergence off, even if a caller supplies gradual-mode fallback defaults. Existing biomass, energy, mutation and locomotion persistence mappings are untouched. The real saved startup file was not rewritten by implementation or test fixtures.

## Scheduling and placement

Gradual mode draws exponentially distributed waiting times: `-log(1-U) × meanInterval`. The first event is scheduled **after the delay plus the first waiting time**. Initial phase duration is stochastic; with N events its expected duration after the delay is `N × meanInterval`. It is not a promise of N living founders or a fixed deadline. Once the initial event cap is exhausted, optional ongoing events use their own configured mean interval, independent of births, deaths or extinction.

Each event is consumed once. Population-cap or habitat rejection does not accumulate a guaranteed founder to release later. At most 64 due events are processed per biology step; an unusually large backlog remains scheduled for subsequent steps. At most eight cached weighted candidates are sampled per event. A selected habitat that has become nonviable is removed from sampling until the next viability refresh.

Dedicated RNG streams derive from the existing biology seed:

| Purpose | XOR salt |
|---|---|
| Event timing | `0x454D4552` |
| Vent selection | `0x56454E54` |
| Founder traits and movement seed | `0x464F554E` |
| Small within-cell visual scatter | `0x56495355` |

None draws from reproduction, locomotion mutation, temperature mutation, sulfur mutation or fermentation mutation streams. Batch spawning retains its previous selection/trait/visual draw sequence. Gradual founder construction shares the existing batch initializer: Hydrogenotrophy, PassiveDrift, authoritative injection cell and bottom layer, zero age, configured lifespan/temperature/biomass, zero stored OrganicC, existing .1–.5 starting energy distribution, packed position/movement fields and passive schedules initialized on the movement clock at placement. An empty runtime aligns that clock with simulation time after warmup; later events leave the existing clock and other organisms' schedules intact. Normal subsequent metabolism, maintenance, reproduction, death and rendering apply.

Candidates come only from `TryGetVentInjectionHabitat`, filtered to authentic submarine bottom-layer injection nodes. No new habitat search, relocation, transport or dispersal is introduced. Selection remains with replacement: a vent can receive several founders, and other vents may receive none.

Local eligibility requires at least one full configured hydrogenotrophy reference reaction's worth of both H2 and CO2 and positive configured O2 efficiency. Thermal suitability and food readiness are also checked at sampled placement positions by the existing bounded thermal-placement routine. For **ongoing emergence**, selection retains the global outlet pool and its existing weight: `SystemBudgetWeight × WithinSystemWeight × O2 efficiency × availableReferenceTicks / (1 + availableReferenceTicks)`. Readiness is an opportunity before competition, not a food reservation or survival guarantee.

For **initial gradual emergence only**, eligible systems are selected first with weight `1 / (1 + successful founders at system)`. Eligible outlets within the chosen system use `1 / (1 + successful founders at outlet)`. The existing cached viability weight must be positive, but its magnitude and the number of outlets do not increase a system's selection weight. Counts increase only after successful placement and remain cumulative after founder deaths. The final initial event uses this rule; the following event returns to the existing ongoing selector. Batch selection, scheduling, caps, founder physiology and startup settings are unchanged.

An initial event whose selected outlet has no valid sampled placement is rejected normally, without another placement attempt or a successful-placement increment. Repeated placement failures temporarily exclude that outlet from initial selection for 10, 20, 40, 80, 160, then at most 300 simulated seconds, alongside the existing viability refresh. A success resets this failure streak. Systems with no currently eligible outlets are excluded; other systems retain their opportunity. This cooldown is ignored by ongoing emergence and does not change event times or guarantee a placement.

Success counters are initialized with the runtime's world catalog and cleared on simulation initialization. Systems use the existing resource-world `SystemIndex`; outlets use their authoritative injection `SourceNode`, independent of filtered candidate indices or the founder's sampled position. These identities are stable for the initialized world, with deterministic catalog order and the existing seeded selection RNG. No dictionary enumeration determines a destination, no population scan is added per event, and no per-organism allocation or non-event tick work is added. The changed initial selector produces a different realization from older code while remaining deterministic for the same seed, settings and simulation-time sequence.

The geometry catalog is cached for the runtime's initialized world. Local weights refresh only on an event when the previous observation is at least 10 simulated seconds old, or after catalog initialization/change. Selected candidates are checked against current local state before placement. Temporarily foodless vents can recover at a later refresh. The cache does not become another resource authority.

## Time semantics

All due times use authoritative **simulation time**, with no `Time.deltaTime`, wall clock, render count or speed multiplier in the scheduler. Polling at different frame rates consumes the same scheduled event timestamps; grouping identical biology steps into 1x versus 100x rendered frames produces the same placements and traits. Placement occurs on the first biology step reaching the due time, after that step's normal lifecycle, so organisms are not aged or charged maintenance before they exist.

The existing simulation pipeline uses variable, capped integration steps. This change does not make the entire environment/biology trajectory bit-identical across different integration-step sizes. Actual placement is quantized to a biology-step boundary; local resource/temperature observations depend on the existing environmental integration. The full-world comparison uses identical fixed 1/30-second steps. Pathological event backlogs can also delay actual placement beyond their scheduled time; diagnostics expose scheduled and actual first placement separately.

## Diagnostics and occupancy

`[GeodesicFounderEmergence]` reports bounded cumulative attempts, successes, population-cap/habitat rejections, first scheduled/actual time and vent node, distinct directly seeded outlets, living direct founders, founder deaths, remaining initial events, founder-parent births, descendant-parent births and cache refreshes. The existing 10-second biology reporting cadence is reused; there is no per-founder ongoing log.

The initial-coverage adjustment adds `directlySeededSystems`, `neverSeededSystems`, `directlySeededOutlets`, `neverSeededOutlets`, and `minFoundersPerSystem` / `meanFoundersPerSystem` / `maxFoundersPerSystem` plus the corresponding `PerOutlet` fields. They count successful direct placements cumulatively across phases, independently of occupancy or survival. Minima and means include zero-placement submarine systems/outlets, including unsuitable ones; an empty catalog reports zeros. All pre-existing telemetry fields retain their meanings. These additions use small world-identity counters at the existing reporting cadence.

Vent occupancy counts **living organisms of any metabolism or locomotion** in the source cell plus its immediate topology neighbors, across each cell's bottom two active layers. A precomputed reverse node-to-outlet map gives one population pass per diagnostic sample, rather than scanning the population separately for every vent. No full-ocean sweep is added to the biology tick.

Distinct direct coverage is cumulative even after a founder dies. Currently inhabited/unoccupied counts use the current living population. An indirectly colonized outlet means its neighborhood is currently inhabited but no founder has ever been placed at that exact injection node. Overlapping outlet neighborhoods can share organisms, so this metric is not proof of a long-distance journey or a count of independent colonies. Founder-parent births mean births directly from an emerged founder; descendant-parent births mean births from all other parents. Only currently living founder IDs and bounded outlet/node sets are retained.

## Historical verification and requested comparison

The initial-coverage adjustment above received source-level review only. No tests, Unity runs, simulations, builds or benchmarks were run for this adjustment; the results below predate it and do not measure the new weighting. Coverage improvement and recovery from unsuitable outlets remain for manual Unity evaluation. Weighted sampling does not guarantee every outlet receives a founder or that any founder survives.

The final graphics-enabled native EditMode run passed **36/36 cases**: `GeodesicFounderEmergenceTests` **21/21**, existing `GeodesicBiologyRuntimeTests` batch timing/initialization cases **4/4**, and `SimulationStartupEvolutionTests` **11/11**. Production and EditMode assemblies compiled in the native Editor.

These cover event timing/caps, ongoing opt-in, invalid intervals, seed reproducibility, independent RNG streams, authentic viable placement with replacement, empty inventories/catalogs, thermal rejection, bounded processing, population limits, packed founder initialization, local occupancy, origin accounting, startup migration/persistence, and preservation of the existing movement clock. Existing startup GUI/persistence cases also passed. No unrelated full regression suite or performance benchmark was run for this milestone.

The opt-in full-world fixture compares seed **283**, batch versus gradual, with the same saved biological and physical parameters. It uses the saved initial count/delay, a 10-second gradual mean interval and ongoing emergence off, through 1200 simulated seconds. Both modes use the same explicit fixed-step order for production biology, sun/temperature and resource transport/chemistry. Automatic scene updates and startup writes are disabled only in the disposable project. No production scene or user profile is saved.

The gradual mode's dedicated streams produce different vent and founder-trait realizations from the historical batch stream. The comparison therefore measures the complete emergence modes under the same seed/settings, rather than isolating timing alone with identical individual founders.

Results and exact native case names are recorded in `Docs/Diagnostics/FounderEmergenceAudit`. Raw native XML/logs remain under ignored `Temp/FounderEmergenceAudit`. The batch result remains valid after the gradual-only movement-clock correction; the gradual trial was repeated on the corrected code. Reported gradual measurements use that final trial.

Both the initial paired native Unity test and the corrected gradual-only native replay passed. They entered/exited actual Play Mode in the disposable project. The exported applied configurations differ **only** in `geodesicFounderEmergenceMode`. Both use seed 283, subdivision 6, 20 initial events, 300-second delay, a 30000 population cap, and the unchanged saved biological/physical profile. Final user startup bytes retain SHA256 `1FB367D83097FFCBE4B764D788E38545316E7F6E2F503F55F87B87BD30E21532`, including the confirmed .02 fermentation probability. Chemistry logs reported zero transfer violations, input-transfer violations and zero-input energy awards.

| Measurement at 1200 simulated seconds | Batch | Gradual, corrected |
|---|---:|---:|
| Initial attempts / successful founders / rejected | 20 / 20 / 0 | 20 / 20 / 0 |
| First actual emergence (sim s) | 300.000016 | 328.166684 |
| First scheduled emergence (sim s) | batch release boundary | 328.159693 |
| First vent injection node | 37099 | 9961 |
| Distinct directly seeded outlets | 10 / 37 | 10 / 37 |
| Currently inhabited outlet neighborhoods | 4 / 37 | 9 / 37 |
| Inhabited without direct founder placement | 0 | 2 |
| Currently unoccupied outlet neighborhoods | 33 | 28 |
| Living direct founders / founder deaths | 0 / 20 | 0 / 20 |
| Births directly from founders | 14 | 60 |
| Births from descendant parents | 57554 | 59689 |
| Final total population | 30000 | 30000 |
| Candidate viability refreshes | not used | 10 |

| Simulated time | Batch population | Gradual population | Living batch founders | Living gradual founders |
|---:|---:|---:|---:|---:|
| 300 | 20 | 0 | 20 | 0 |
| 400 | 60 | 28 | 19 | 10 |
| 500 | 212 | 423 | 18 | 18 |
| 600 | 1226 | 1909 | 17 | 20 |
| 800 | 9499 | 9568 | 7 | 14 |
| 1000 | 20078 | 19714 | 0 | 2 |
| 1200 | 30000 | 30000 | 0 | 0 |

All 20 gradual events had succeeded by the 600-second observation, with no ongoing emergence. Both lineages persisted after all direct founders died. Gradual emergence left more vent neighborhoods inhabited at this endpoint, while direct placement coverage was identical. Two neighborhoods were inhabited without exact-source founder placement; the overlap caveat above applies. Many outlets remained unoccupied, and no settings were changed to force coverage. Both populations hit the existing cap, so neither endpoint establishes uncapped growth limits or equilibrium. The time series does not show a consistent population advantage at every observation.

## Limits

- This milestone does not guarantee every vent is inhabited or that any particular founder survives. It adds no artificial colonization or locomotion assistance.
- One seed and a finite trial cannot establish long-term equilibrium, robust coverage across seeds or gameplay balance. Ongoing emergence is covered by deterministic fixtures, not an additional full-world trial.
- Candidate observations can be up to ten simulated seconds old, with a fresh eligibility check on the selected outlet. This deliberately trades perfect ranking freshness for bounded work.
- Geodesic world save/loading is already unavailable in `SimulationSaveLoadService`; this milestone persists startup controls, not an in-progress emergence RNG/checkpoint or founder ancestry across world reloads.
- Interactive menu feel and live rendered gameplay remain for manual review. No unrelated baseline test failures are repaired and no new performance claims are made.
