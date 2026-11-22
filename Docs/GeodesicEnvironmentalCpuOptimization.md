# Environmental CPU optimization — 2026-11-22

This pass starts from local revision `87d23691f688d2de8a595bf48ff7731138299399`. The working tree was clean when inspected. Saved settings, scene assets, biology/evolution code, transport intervals, rates, diffusion coefficients, and speed/timestep settings are unchanged. Hydrogen → Fermentation mutation probability remains **0.02**.

## Evidence and scope accounting

The supplied real benchmark is 50 organisms at requested 100x over approximately 88.5 steady-state real seconds: 10.2 FPS, 34x effective speed, 98 ms/frame, ocean/environment category 52 ms/frame, Transport 43.8, temperature Update 20.4, chemistry 11, horizontal 9.17, vertical 2.29, and 101,169 candidates / 101,169 active ocean nodes.

The analyzer's environment category already includes thermal work and excludes the separately reported chemistry scope. Chemistry, horizontal/vertical mixing and suspended transport are children of Transport. These timings must not all be added together. The evidence supports environmental work as the first target; it does not support making chemistry sparse when every node is a real candidate.

## Exact production changes

* `Assets/Scripts/Planet/Environment/Temperature/GeodesicSurfaceTemperatureField.cs`, `UpdateTemperatureTargetsAndApplyResponse` (line 452) and `CalculateSurfaceResponse` (line 489): compute the four land/ocean × warming/cooling relaxation coefficients once per thermal tick, after live atmosphere forcing has been refreshed. Previously each cell recomputed its capacity/timescale expression and called `Mathf.Exp`. At subdivision 6 this reduces response exponentials from 40,962 to four per tick. The multiply order, float intermediates, target comparison, temperature update, sun calculation and tick cadence are retained. Coefficients are recomputed each tick; changing pressure, heat capacities or response times takes effect immediately.
* `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs`, `MixSuspendedInventory` (line 835), `CacheSuspendedOutflow` (line 885) and `StageCachedSuspendedPair` (line 896): retain the exact signed transfer from the outgoing pass and read it in the incoming pass. Inventories do not change between these passes, so the second pair of concentration divisions and repeated transfer calculation was redundant. Both horizontal-then-vertical traversal orders, donor limiting, accumulation order, array clearing and final node commit remain unchanged. Every cache entry, including zero transfers, is overwritten each pass; values cannot leak between species or ticks. The scratch buffer is allocated lazily once per domain, included in `TransportCacheMemoryBytes`, cleared on world teardown, and excluded from serialization. Memory cost is **8 × (horizontal links + vertical links) bytes**, shared across all mineral species.
* The same resource file adds stage markers around staging clear, coefficient preparation and suspended clearing/outgoing/incoming/commit phases. `TickResources` (line 648) otherwise retains its operator split.
* `Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanSuspendedField.cs` adds whole mineral-step and settling markers. Its transport/settling arithmetic and species/layer traversal are unchanged.
* `Assets/Scripts/Debugging/SimulationPerformanceAnalyzer.cs` appends those detail markers plus existing surface response, diffusion, thermal callback, ocean relaxation and implicit-column markers. Aggregate indices/definitions are unchanged; nested detail columns are not added again to totals.

`ApplyStagedAllResources` is deliberately unchanged: its existing CandidateRefresh marker includes both seven-channel application and candidate collection. Even a zero inventory delta executes a multiply/divide round trip that can round the concentration; skipping it would not guarantee bitwise parity. Dissolved staging is still cleared in full. Existing uniform horizontal-channel skipping is retained. Dense chemistry and reaction stoichiometry are unchanged.

## Targeted runtime findings

A separate deterministic headless Unity **6000.3.29f1** fixture uses subdivision 6, 114,902 active nodes, unequal layer geometry, dense reactants, all three mineral species, finite atmosphere exchange and the production approximate thermal model. Each of three trials measures 12 resource ticks and 12 thermal ticks after one warm-up tick. These are **milliseconds per simulation tick**, not per rendered frame. The fixture is not a recreation of the user's 50-organism/100x capture.

Median values across the three trials:

| Scope | Reference ms/tick | Optimized ms/tick |
| --- | ---: | ---: |
| Resource tick, including recurring diagnostics (Stopwatch) | 183.561 | 144.276 |
| Entire surface tick, including synchronous ocean callback (Stopwatch) | 15.363 | 9.281 |
| Suspended minerals, inclusive | 90.704 | 63.501 |
| Suspended mixing, inclusive | 73.976 | 49.781 |
| Suspended incoming pass | 38.473 | 19.715 |
| Surface target and response | 8.767 | 4.104 |
| Dissolved staging clear | 1.220 | 1.084 |
| Seven-channel staged application + candidate refresh | 10.088 | 7.768 |
| Suspended settling | 16.721 | 13.715 |
| Approximate ocean relaxation | 6.539 | 5.111 |

The new scopes explain substantial transport cost in the dense-mineral fixture: suspended mixing is much larger than clearing, and settling remains significant. Surface response is also a substantial part of thermal cost. The source changes directly remove redundant work in these two locations.

**Timing limitation:** unchanged stages also ran faster in the later process, and resource-tick reference trials ranged from 156 to 214 ms. OS scheduling/CPU state affects these headless trials. Therefore the table supports the direction of improvement, not a precise attributable end-to-end speedup. In particular, the incoming/outgoing timing ratio decreases from about 1.224 to 0.796, and surface-response/ocean-relaxation from about 1.341 to 0.803; these provide context for process-wide variability. No resulting game FPS or effective speed is claimed. The real 50-organism/100x benchmark has **not** been rerun.

## Parity and regression results

The opt-in fixture in `Assets/Tests/EditMode/GeodesicEnvironmentalCpuParityTests.cs` executes the actual production resource ticks, chemistry, surface ticks, ocean callbacks and biology Step before and after the changes. The reference uses pre-optimization source with only equivalent profiling scopes added; it does not use a rewritten numerical reference model.

**All 14 serialized physical-state snapshots are byte-for-byte identical** (SHA-256 receipts retained):

* Four checkpoints at steps 300, 600, 900 and 1200 for each thermal model (`ApproximateEcologicalProfiles` and `ConservativeImplicit`), with 50 starting anchored organisms, 1/30 s biology steps, unchanged model-specific thermal intervals and five-second resource ticks. Both runs exercise changing sun direction, pressure/inertia, land/ocean capacities and heating/cooling timescales. Each model reaches 100 live organisms after **150 births and 100 deaths**.
* One complete large dense-domain checkpoint after the measured environment sequence.
* Five suspended-transfer checkpoints covering directional flux, transition to zero flux, uniform concentrations, disabled coefficients and a 200-second input to the existing stable donor-limited mixer.

Snapshots include every dissolved resource concentration (spatial distributions and therefore inventories with unchanged geometry), surface/target/subsurface temperature arrays, atmosphere inventories and exchange totals, suspended inventories, deposition/downward-transfer totals, sediment fields, chemistry products and visual-memory state, packed biology arrays and birth/death/mutation counters. **Maximum observed before/after difference is zero; no parity tolerance was introduced.**

Iron and sulfur conservation are separately asserted with `max(1e-10 absolute, 1e-11 relative to initial inventory)` tolerance for existing floating-point summation. At the coupled-fixture endpoints, residuals were approximately **+1.192093e-6 iron** and **−1.907349e-6 sulfur** in the model's physical inventory units, identical before and after. This tolerance applies to the mathematical inventory balance, not to differences between implementations.

Production and test assemblies compiled successfully. Existing selected regressions: **180 passed / 181** across chemistry, mixing, finite-atmosphere conservation/climate, thermal policy, physical scale, hydrogen/sulfur/fermentation biology, speed semantics and analyzer summaries. The sole failure is `GeodesicVentThermalModelTests.HabitatEligibilityIsBottomOnlyAndCrossHabitatSafe` (line 63), which expects an above-bottom marine habitat to be ineligible. It reproduces with the unoptimized sources and is left unchanged. The four opt-in replay/profiling tests pass in both reference and optimized runs.

Save schemas and serialized configuration fields have not changed. The link buffer is transient derived scratch and is rebuilt after a domain is recreated. Full interactive save/load and a rendered gameplay benchmark were not performed.

## Validation workspace and reproduction

One newly created, reusable disposable project is retained under the explicitly ignored directory:

```text
C:\Users\domin\OneDrive\Dokumenty\Unity\GPT\SimulaVit\work\EnvironmentalCpuPass\UnityProject
```

It has an ownership marker, active-project lock check, separate company/data identity and a startup-save guard only in the disposable source. It contains scripts/tests and the scene text, rather than a copy of all game assets. Its package manifest references the main checkout's existing package sources; no second package cache is downloaded or copied. The same isolated Library is reused for every phase. No existing workspace was deleted or repurposed. Source backups and valuable receipts are retained under `Before` and `Results` beside it.

Run explicitly from the repository root:

```powershell
pwsh -NoProfile -File Tools/ValidateEnvironmentalCpu.ps1 -Phase reference
pwsh -NoProfile -File Tools/ValidateEnvironmentalCpu.ps1 -Phase optimized
pwsh -NoProfile -File Tools/ValidateEnvironmentalCpu.ps1 -Phase regressions
```

The reference phase uses the retained pre-change files in `work/EnvironmentalCpuPass/Before/Instrumented`; keep those with the results when reproducing this comparison. Without the opt-in phase environment variable, the replay tests skip. This does not authorize future automatic test or simulation runs.

All results are in `work/EnvironmentalCpuPass/Results`: `reference-results.xml`, `optimized-results.xml`, `regressions-results.xml`, the corresponding Unity logs, `reference-filtered-results.xml` for the existing failing test, the two `*-fixture-timings.csv` files, and paired snapshot hashes/binary references. The analyzer's user-facing benchmark CSV destination remains the existing `Application.persistentDataPath` location described in `GeodesicPerformanceBaseline.md`.

The next measurement should rerun the original real benchmark with the new suspended-stage and thermal detail columns. Settling remains a candidate for exact input-keyed response caching; no additional optimization was implemented here.
