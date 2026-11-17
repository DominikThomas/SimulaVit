# Geodesic hydrogenotrophy carbon-conservation audit

Date: 2026-11-16. The user explicitly selected **whole-reaction scaling**: the existing 0.8 parameter limits uptake, energy and carbon fixation together. It is no longer an unexplained carbon-storage loss in geodesic mode. Legacy metabolism is unchanged.

## A. Root cause and authoritative pathway

Before this correction, `GeodesicBiologyRuntime.BuildRequest` used:

```csharp
r.NeedA = hCo2;
r.NeedB = hH2;
r.EnergyPerExtent = hEnergy;
r.StorePerExtent = hCo2 * Mathf.Clamp01(hStoreFraction);
```

No carbon product was assigned. At the usual inputs (CO2 0.01, H2 0.02, energy 8, storage fraction 0.8), a completed reference reaction removed 0.01 CO2, stored 0.008 carbon and left 0.002 carbon unaccounted for. This is exactly 20%, before caps or rounding.

The reported interval corroborates that formula: `87.9848 × 0.2 = 17.59696`, matching `modelCResidual=17.5969561`. The difference between desired store `70.3878493` and measured store increment `70.3878739` is float addition rounding, not a destination for the missing 20%. Small resource-transfer rounding explains the remaining difference between planned and measured aggregate residuals.

The pathway is:

1. ReplicatorManager supplies the configured uptake, energy, activity, maximum-store and temperature/O2 settings to `GeodesicBiologyRuntime.Step`.
2. `BuildRequest` samples the authoritative cell/layer temperature and existing oxygen efficiency; requests use reference ticks of 0.5 simulated seconds. It now also limits the whole hydrogen reaction by activity and available storage.
3. `AggregateDemands`, `ResolveAvailabilityFactors` and `ResolveCompetition` retain their proportional competition policy. Available inventory is concentration × the node's physical km³ volume. All requested resources compete before any reaction products become available.
4. Hydrogen requests now commit through the paired resource transaction in `GeodesicOceanResourceField.CommitHydrogenotrophy`. Both measured substrate debits must be valid before energy or organism carbon is credited. Other metabolisms retain their existing staged reaction path/formulas.
5. `organicCStore` is the organism's only tracked carbon inventory. `biomassTarget` and `divisionBiomassMultiple` define a division threshold; visual size and energy do not contain another carbon pool.
6. Existing division splits existing stored carbon between parent and daughter; it creates no new carbon and makes no environmental carbon transfer. Existing division energy cost is paid once, then remaining energy is split. Neither allocation rule was changed.
7. Existing death recycling clears packed/companion stores once and transfers their remaining carbon to dissolved OrganicC in the exact active death node. Invalid/dry habitats retain the existing explicit unplaced-carbon diagnostic. Generic removal does not recycle a second time.

The reaction-definition scaffolding is not the geodesic numerical authority. Its coefficients must not be substituted for the manager values used by this request path.

## B. Meaning of 0.8

The old 80% value was intentional configuration: it exists in the manager, scene, startup configuration and a prior test that explicitly expected 0.008 stored carbon from 0.01 withdrawn CO2. No modeled reservoir or scientific calibration in this code accounts for the remaining 20%. A yield parameter does not make disappearing carbon a valid mass balance.

The alternatives were explained to the user: leave unassimilated CO2 in the ocean while changing the net H2:CO2 ratio; store all CO2 and raise carbon growth 25%; or scale the entire reaction and reduce ample-substrate energy 20%. The user chose the third option.

Geodesic semantics now are:

```text
desired extent = dt / 0.5 × temperature efficiency × O2 efficiency × activity
capacity limit = max(0, maximum store − current store) / CO2 per reaction
CO2 consumed   = CO2 per reaction × committed extent
H2 consumed    = H2 per reaction × committed extent
stored carbon = CO2 consumed, apart from explicitly measured numerical residual
energy        = energy per completed reaction × committed extent
carbon products = none
```

These are the existing simulation's carbon-equivalent inventory units, **not calibrated moles, kilograms or a newly specified biochemical reaction**. At the default coefficients the model input ratio stays H2:CO2 = 2:1. No electron acceptor, organic product, carbon grant or biochemical pathway was invented.

`hydrogenotrophyStoreFraction` retains its serialized name/value for compatibility. In geodesic mode its Inspector tooltip, startup UI and applied reaction log explicitly identify it as the whole-reaction activity fraction. In legacy mode it retains the old storage-fraction behavior. Zero activity or a zero required-substrate coefficient disables the geodesic reaction; neither gives free metabolic energy.

## C. Changes and expected biological effects

| Quantity per ample-substrate reference tick | Before | After |
| --- | ---: | ---: |
| CO2 withdrawal | 0.01 | 0.008 |
| H2 withdrawal | 0.02 | 0.016 |
| Stored carbon | 0.008 | 0.008 |
| Energy awarded | 8 | 6.4 |
| Missing model carbon | 0.002 | 0 |
| H2:CO2 input ratio | 2:1 | 2:1 |

Consequences are deliberately not described as unchanged ecology:

- With ample substrates and space, carbon accumulation is nominally unchanged; energy and substrate consumption fall 20%.
- When the same fixed substrate inventory is limiting, energy per completed reaction/input remains unchanged, but all consumed CO2 is retained. Carbon gain per limiting CO2/H2 inventory rises **25%** relative to the previous 80% retention.
- At/near the store cap, the reaction is throttled or stopped instead of consuming inputs and discarding carbon. Lowering a cap below existing stores no longer deletes existing hydrogenotroph carbon. Energy formerly awarded by such wasteful capped reactions is no longer produced.
- Organism storage remains float-backed. The planner charges only a representable, nonnegative storage increment that fits both capacity and substrate budgets. It rounds that increment downward when necessary, and derives the committed reaction extent/energy from it. This introduces a small quantization effect, visible in the replay results.
- Both required measured input transfers must be positive. A failed/invalid/poorly resolved transaction changes neither resource nor organism state and awards no energy. `hydrogenPrecisionRejected` exposes precision-limited rejections; invalid transactions increment the existing invalid-state diagnostic. Very small reactions can therefore be rejected instead of receiving unbacked rewards.

Population limits, maintenance, reproduction probabilities, mutations, temperature preferences/physics, vent coefficients/geography/cadence, chemotaxis and locomotion algorithms were not adjusted to compensate.

### Separate precision correction

Previously `ApplyDirectExchangeInventory` updated a **float concentration** after rewards had already been granted. In a sufficiently large node, `float(1 − 0.008 / 10^8) == 1`: measured CO2 withdrawal was exactly zero despite positive planned uptake, storage and energy. This is a real missing observable debit, not merely a late telemetry snapshot.

The authoritative shared dissolved concentration buffer is now **double**. Public float sensing/visual readouts retain their API; `GetNodeInventory`, direct exchanges, transport, source accumulation and chemistry's access to that shared buffer preserve its precision. Chemistry reaction coefficients, half-lives, splitting order and stoichiometry remain unchanged. Twenty existing abiotic chemistry cases and eight mixing activity cases pass in managed execution.

This shared numerical-storage change has a cost: **28 additional bytes per allocated ocean node** (seven channels × four extra bytes). It can change small numerical transfers for any resource using that authority, including vents, air–sea exchange and recycling; the configured physical rates and formulas are unchanged. It is not an ecological compensation. No performance sweep or player build was performed.

Reporting tolerances remain exactly `1e-7 + 2e-6 × |transfer|`. They were not raised. The hydrogen preflight uses the stricter relative part for accepting a debit, preventing the absolute reporting floor from allowing a large relative error on a tiny reaction. Double precision is finite: genuinely unresolved transactions are rejected and counted, not asserted to be exact or silently credited.

### Transfer-warning interpretation

- Original node-level `transferViolations` and reaction-level `inputTransferViolations` have different denominators. One imperfect shared node write can flag many organisms; 330 bad node/resource exchanges can therefore produce 21,940 per-request flags.
- Equal aggregate requested/measured input totals do not invalidate local warnings. Over- and under-rounded node transfers can cancel in the aggregate, including some rounded-zero nodes.
- The original hydrogen path had no defined products. For unmixed nodes, `actualNet / plannedNet` attributed the measured uptake proportionally. `zeroMeasuredInputWithEnergy` was evidence of zero **shared-node** measured required uptake alongside positive awards; it was not an independent measurement of each organism's withdrawal.
- Store deltas were captured before lifecycle/division/removal and reaction accounting ran before reverse-loop swap-back. The reported problem does not come from reading daughter allocation or death recycling as metabolism.
- Hydrogen inputs now have directly measured paired transactions. Other staged, mixed input/product pairs remain explicitly net-only/unobservable at the gross reaction level; no fabricated gross attribution is introduced.
- Logs retain `afterCompetitionInputs` and now add `committedInputs`, separating competition allocation from any representability reduction/rejection. Reaction accounting includes only reaction exchanges. Death recycling remains separate, avoiding double-counting it as metabolic product carbon.

## D. Verification results

Executed with:

```powershell
./Tools/ValidateHydrogenotrophy.ps1 -CompileManaged -Replay
```

The tool uses restored Unity compiler responses plus all current local source files; outputs stay in `Temp/HydrogenotrophyAudit`. It compiles the actual runtime and EditMode test assemblies with Unity's installed C# compiler, then runs only engine-independent methods. It requires the restored NUnit package, Unity compiler files and a dotnet runtime. It does not stub Unity behavior or treat native tests as passed.

**55 managed tests passed; 0 failed**:

- 14 new production transaction-planner cases: isolated reaction, hydrogen/CO2 limits, cap behavior, zero input, invalid input, sub-float ocean transfers, unrepresentable debits, repeated small committed/rejected transfers, store rounding and overflow rejection.
- Five actual diagnostic-ledger cases distinguish the old 20% formula, rounded-zero inputs, mixed gross attribution, per-node/per-request flag multiplicity and single-counted direct exchanges.
- Eight actual runtime arithmetic cases, including the changed hydrogen reference/modifier behavior, competition and individual thermal traits.
- Twenty existing abiotic chemistry cases and eight existing mixing activity cases.

The complete runtime and EditMode assemblies compile successfully. Compiler warnings are pre-existing unused fields. `git diff --check` passes.

### Seed-123456 inventory fixture, 2180 simulated seconds

This is a **controlled four-node inventory/recycling fixture**, not a planet/ecological rerun. It has 32 organisms, 0.25 s steps, known node volumes from 1,000 to 1,000,000 km³, initial CO2 concentration 1, no initial H2, the current saved physical source rates (CO2 2 and H2 5, multiplier 10), ample substrates after source injection, and constant temperature performance 1. Seed 123456 determines recycling/replacement thresholds. The fixture does not recreate movement, mutation, climate, population growth or the original founder history.

The pre-fix reference formulas are explicitly retained only in this test tool. A double-backed pre-fix control isolates the 20% model gap from float errors; a float-backed pre-fix reference reproduces rounding warnings. The corrected run invokes the production transaction planner. Values are concentration × km³ carbon-equivalent units; energy remains the model's energy units.

| Metric | Old model, double control | Old float reference | Corrected double/atomic |
| --- | ---: | ---: | ---: |
| Planned CO2 input | 1395.199969 | 1395.199969 | 1116.156926 |
| Measured CO2 input | 1395.199969 | 1014.556885 | 1116.156928 |
| Measured H2 input | 2790.399938 | 2795.196167 | 2232.313853 |
| Organism store increments, before recycling | 1116.160160 | 1116.160160 | 1116.156926 |
| Metabolic carbon products | 0 | 0 | 0 |
| Energy awarded | 1116160 | 1116160 | 892925.544570 |
| Model carbon residual | 279.039916 | 279.039916 | **0** |
| Measured reaction C residual | 279.039809 | −101.603275 | **1.86425e−6** |
| Whole-system C residual | 279.039809 | −101.604144 | **1.86171e−6** |
| Per-request input-transfer violations | 0 | 279040 | **0** |
| Zero measured required input with energy | 0 | 69760 | **0** |
| Precision-rejected transactions | 0 | 0 | **0** |

The float reference's negative measured residual is deliberate evidence that quantization can also create apparent excess carbon. It is a different fixture from the user's reported positive-deficit interval. The systematic old-model loss remains 20% in both old controls.

Whole-system balance uses:

```text
initial ocean carbon + measured external vent carbon
− (final dissolved CO2 + final dissolved OrganicC + final living organism stores)
```

Source rounding is measured separately from reaction transfers. Recycling is an internal transfer, not a second carbon input; its planned and measured totals are included separately in the machine-readable replay output. The corrected residual is retained as numerical rounding, not forced to zero. At transferred carbon 1116.156928, the existing reporting bound is approximately 0.0022324; the measured residual is about 1.9e−6. No carbon was added to close the equation.

Full precision results: `Docs/GeodesicHydrogenotrophyConservationReplay.json`. Generated compiler/test/replay output and Unity startup log: `Temp/HydrogenotrophyAudit/`.

## E. Limits and pending native validation

Ten new deterministic native EditMode cases compile in `GeodesicHydrogenotrophyIntegrationTests`. They exercise the real private request → competition → commit path, 16 competing organisms, capacity-demand exclusion, diagnostics enabled/disabled, high-volume transfers, existing division splits 0.3/0.5/0.7, actual death recycling, repeated removal and whole-system inventories. They have **not executed** here.

Two Unity batch attempts exited with code 1 **before compilation/test-runner startup**. The startup log contains licensing-client validation/access-token errors; a complete startup-failure diagnosis was not part of this change. The managed compilation/test route above succeeded independently. There is no native test-results XML, and these integration tests must not be counted among the 55 passes.

No full seed-123456 planet run was completed. The currently saved startup file now selects seed 283 and different hydrogen temperature preferences, so it is not the original run configuration; it was read for context and left unchanged. No population, performance or ecological parity conclusion is claimed.

In Unity Test Runner, run the new balance and integration fixtures plus `GeodesicBiologyRuntimeTests`, `GeodesicAbioticChemistryTests`, and `GeodesicHorizontalMixingActivityTests`. Then reproduce the original environmental setup for seed 123456 and inspect model/measured carbon residuals, `inputTransferViolations`, `zeroMeasuredInputWithEnergy`, `hydrogenPrecisionRejected`, death rounding/unplaced carbon and whole-system stocks through t≈2180 s.

Other metabolisms retain their prior formulas, including any previously diagnosed non-hydrogen carbon issues. Mixed gross attribution remains unobservable for other staged reactions. Double/float organism representation can still produce explicitly reported rounding or rejected very small transactions. Passing these accounting tests does not establish unchanged ecological dynamics.
