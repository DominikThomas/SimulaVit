# Biology Milestone 1: growth, division and resource diagnostics

Implemented on `codex/geodesic-biology-parity`, starting at `037420e`. That commit contains the earlier parity audit; its file has been preserved byte-for-byte. No simulation rules, settings, reaction formulas, movement, thermal policy, renderers, ocean transport, caches or mutation logic were changed. No commit or merge was made.

**What changed**

- [Growth snapshots] expose a computed view of packed population state, with manager APIs by packed index or stable diagnostic ID. These are observational copies, not another biomass system.
- [Reaction accounting] observes requested versus achieved inputs, measured inventory exchanges, stored carbon and energy, cap losses and conservation residuals. It does not repair or compensate transfers.
- [Division accounting] observes every successful division numerically, retains one latest event and at most three unusual examples per report, and checks allocation, gates, habitat, position and ages.
- [Manager controls] expose the diagnostics in the Inspector and provide explicit refresh/reset context-menu commands. [Reporting] uses the existing ten-simulated-second schedule, including extinction.

**Hydrogenotroph lifecycle traced**

| Stage | Current implementation and diagnostic interpretation |
| --- | --- |
| 1. Consumption request | [Request building] sets CO2/H2 needs per 0.5 s reference tick. Desired extent is `reactionDt / 0.5 × temperaturePerformance × localO2Efficiency`. Reactions accumulate at 0.25 s. This is the requested demand before competition. |
| 2. Competition and withdrawal | Shared node/resource availability limits each achieved extent. [Commit] submits the same existing aggregated inventory deltas in the same order. Diagnostics now retain the measured return from `ApplyDirectExchangeInventory`; no extra resource exchange is performed. |
| 3. Energy | Configured hydrogen yield times achieved extent is added to packed energy. The ledger distinguishes the awarded amount from the actual float energy increment. |
| 4. Stored carbon | Desired storage is `CO2Need × storeFraction × achievedExtent`. The existing float addition and storage cap remain unchanged. Diagnostics distinguish desired storage, actual packed-store increment, cap loss and float-addition residual. |
| 5. Maintenance | [Lifecycle] increments age and subtracts `max(0, basalMaintenance × dt) × thermalStressMultiplier` every authoritative step. Passive movement has no separate propulsion charge. Reaction accounting is recorded before this charge and before any removal. |
| 6. Division threshold | Carbon must reach `max(1, divisionBiomassMultiple) × packed BiomassTarget` when carbon-limited division is enabled. Energy must be **strictly greater** than the nonnegative selected division cost. BiomassTarget is a threshold, not another carbon stock. |
| 7. Temperature and probability | The existing thermal policy sets eligibility; the population must be below its existing cap. An eligible parent then undergoes the original `clamp01(rate × dt × performance)` random trial. Readiness does not guarantee a birth. A zero reproduction rate can leave an organism ready with probability zero. |
| 8. Allocation | [Energy splitting] pays the cost once and halves the remaining energy. Carbon-limited division transfers the configured clamped fraction of existing parent C to the child and subtracts that amount from the parent. No carbon cost or environmental exchange occurs at birth. In energy-only division, child C stays zero and parent C is retained. |
| 9. Newborn | The [constructor] starts energy/C at zero and size at 1; diagnostics capture those preallocation values. The child receives the parent's packed world position and cell/layer, inherited traits and existing deterministic thermal mutation. Parent age is retained, child age is zero. Existing passive newborn initialization is untouched. |
| 10. Death | Old age, energy depletion and thermal exposure use the existing death policy. [Recycling] clears the remaining packed/companion store once and returns it to the exact active death node as dissolved OrganicC, with its existing measured-transfer residual. Generic removal/reset does not create a carbon source. |

Visual size remains fixed. Neither the new progress ratio nor its minimum/maximum display invents structural biomass, changes uptake, or enables a growth animation.

**Available growth values**

`TryGetGeodesicGrowthSnapshot(index, out snapshot)` and `TryGetGeodesicGrowthSnapshotById(id, out snapshot)` on [ReplicatorManager] expose ID/index, metabolism, cell/layer, world position, age, visual size, OrganicC, biomass target, required C, **unclamped** `carbonProgress = OrganicC / requiredC`, energy, required energy, temperature/performance, current probability per step, and individual readiness flags.

`divisionPermitted` means the diagnostic deterministic gates pass; it does not consume or predict a random draw. `principalBlock` has the documented priority habitat → invalid state → pending death → temperature → population cap → energy → carbon. Individual flags remain available when several requirements fail. Missing local substrates affect subsequent metabolic uptake and store/energy availability; division itself does not directly consume ocean H2/CO2, so it must not acquire a new substrate gate in this milestone.

Snapshots recompute thermal suitability using the current authoritative ocean temperature and the runtime's last-applied thermal settings. `packedCanReplicate` separately exposes the last biology decision. They can differ after an environmental update before the next biology step. The manager API uses the last authoritative biology dt for `probabilityPerStep`; reading it while paused does not run a trial. Invalid indices/absent geodesic runtime return false. ID lookup scans only on an explicit query, not every frame. IDs remain valid through swap-back but not a new world/save reload.

**Inspection and telemetry**

Select the object containing `ReplicatorManager`. Expand **Geodesic Growth and Accounting Diagnostics → Biology Diagnostics**. Aggregate diagnostics are enabled by default; **Log Detailed Division Examples** is off by default. Set **Selected Organism Id** to an ID from a birth record or movement diagnostics; zero follows the first current packed organism. **Selected Growth** refreshes on the existing report cycle. The component context menu **Biology Diagnostics → Refresh Selected Growth** refreshes it on demand, including while paused. **Reset Accounting Readouts** clears only these diagnostic counters/readouts; the next report may cover a partial interval, whose observed duration is shown.

| Console prefix / Inspector readout | Meaning |
| --- | --- |
| `[GeodesicGrowth]` | Current population, division-ready count, five C-progress bins (<25%, 25–50%, 50–75%, 75–100%, >=100%), invalid progress, principal blocker counts and selected-organism values. Snapshot counts include the population cap gate. |
| `[GeodesicDivisionAccounting]` / Last Division Interval | Births during observed interval; total paid division energy; maximum absolute C/E residuals; violation totals and categories; maximum newborn placement error. These counters are interval values, unlike existing cumulative biology birth totals. |
| Latest Division / Unusual Examples | Full pre-division parent snapshot, child preallocation E/C, post-division parent/child E/C, split, IDs, positions, habitat and ages. At most three unusual events are retained from an interval. Latest Division remains inspectable without detailed Console logging. |
| `[GeodesicResourceAccounting]` | Requested and measured **net** environmental exchanges by resource, measured-minus-requested residuals, transfer violation count, mixed-pair count and aggregate reaction carbon residual. Zero channels print as `none`; that does not imply a measured gross zero for a mixed pair. |
| `[GeodesicReactionAccounting]` / Last Reactions | Per-metabolism requested/achieved reactions, gross planned substrates/products, attributable actual transfers and unobservable mixed transfers, storage components, energy awards/increments, model/realized C residuals and violation counts. Array order is `MetabolismType`. Dormant/absent strategies have zero readouts and no reaction log line. |
| `[GeodesicDivisionExample]` | Optional: one latest birth plus at most three unusual events at the report boundary. Never one Console log per birth. |
| Existing temperature / death recycling logs | Continue to describe thermal births/deaths and actual remaining-C return. Their cumulative accounting is separate from this reaction-only interval ledger. |

Division checks use `1e-7 + 2e-6 × abs(reference)` in model units; position error tolerance is 1e-5 world units. Nonfinite/negative states, a blocked birth, unexpected initial child grants, incorrect split, wrong habitat/placement and age changes have separate flags. A birth with several flags increments the overall violation count once, plus its applicable category counts. Maxima exclude nonfinite residuals, which are counted as invalid instead. A sample contains pre-division **post-maintenance** parent energy, not its earlier pre-maintenance value.

**What a measured transfer can establish**

The resource field nets all requests for each node/resource before writing its float concentration. Therefore an individual request is not a separately measurable environmental transaction.

For an unmixed node/resource pair (only uptake or only products), the ledger proportionally attributes the **measured net exchange** to its requests using `actualNet / plannedNet`. These amounts are labelled `actualAttributedInputs/Products`, not independently measured per-organism withdrawals. If both uptake and product generation share a pair, gross transfers are unobservable; their planned values go into `netOnlyInputs/Products`, and carbon attribution is excluded for affected reactions. Exact measured net accounting remains available globally, including perfectly cancelling pairs. No arbitrary attribution is substituted for unknown gross flows.

Carbon values use the existing model's CO2/CH4/OrganicC-to-store equivalents, not molecular-mass stoichiometry. S0 is a mineral output covered by existing suspended-mineral telemetry, not a carbon destination.

For each reaction:

```text
modelCResidual   = planned carbon inputs − defined carbon products − desired store gain
plannedCResidual = planned carbon inputs − defined carbon products − actual store gain
plannedCResidual = modelCResidual + capLoss + storeFloatResidual
measuredAttributedCResidual = attributed actual C inputs − attributed actual C products − actual store gain
global measuredUnaccountedCarbon = −measured net ocean C exchange − summed actual store gain
```

Positive C residual means carbon is unaccounted for; negative means excess credit. Planned missing/excess totals are also accumulated separately so cancellation does not hide them. The global equation remains valid when per-reaction gross attribution is unavailable. It excludes death recycling, transport, vents, chemistry and air-sea exchange because only the existing biological reaction commit calls contribute. `inputTransferViolations` detects known substrate transfer discrepancies; `zeroMeasuredInputWithEnergy` specifically counts energy awards with a required, attributable substrate withdrawal rounded to zero. Unknown mixed transfers are not asserted to be zero.

**Confirmed source findings for Milestone 2**

| Finding | Status and interpretation |
| --- | --- |
| Division E/C conservation | The unchanged source partitions existing inventory: `Eparent + Echild = Ebefore − cost`; `Cparent + Cchild = Cbefore`. The child constructor grants neither E nor C. New diagnostics check these equations and the intended split using the actual packed post-birth values. No runtime zero-residual result is claimed here. |
| Hydrogen non-assimilated carbon | Confirmed: full achieved CO2 is withdrawn, but only the configured storage fraction is credited and no defined carbon product receives the remainder. At fraction 0.8 this is 20% missing before cap/rounding effects. Legacy hydrogen code has the same limitation. The source does not define that remainder as OrganicC or CO2, so this milestone does not invent a destination. |
| Store cap loss | Confirmed: uptake is not limited by storage capacity; post-addition `Min(maxStore, ...)` can discard assimilated C. `capLoss` isolates that effect from float addition. It can also expose discarded existing store if an Inspector cap is lowered below current holdings. |
| Dormant methanotrophy excess | Confirmed below the cap: it produces CO2 equal to all consumed CH4 and also desires another 15% as stored C. That is 115% carbon credit before rounding. It remains unreachable from normal founders, and was not enabled or corrected here. |
| Ocean transfer rounding | Confirmed mechanism, unmeasured magnitude: [inventory exchange] returns actual float-backed transfer, but biology still rewards the planned extent. The new ledger observes that difference without changing reward, consumption or state precision. It also separates rounding of organism C/E additions. |

All reaction corrections are deferred. Even an apparently simple change to an active carbon sink changes resource competition; returning unspecified carbon or scaling against a rounded-zero CO2 transfer could alter the accepted ecology substantially. No new invalid state or unbacked grant is introduced by the observational code.

Recommended subsequent work, in separate reviews:

1. Define hydrogenotrophy's carbon-fixation/energy semantics and the explicit fate of non-assimilated carbon. Choose between reduced fixation/uptake and an explicitly defined product; do not automatically deposit the remainder as dissolved organics.
2. Define storage-cap policy: capacity-limited assimilation with substrate left available, or a conservative specified overflow product. Reconcile actual withdrawals, credited storage and products in the same transaction.
3. Choose a reliable transfer-precision policy, then ensure rewards are backed by all required actual substrates. Do not simply treat a rounded-zero bulk-CO2 transfer as proof that no substrate was available. Keep any ocean storage/precision change separately scoped and reviewed.
4. Before enabling methane evolution, correct methanotrophy so product C is input C minus **actual stored C**, and review its energy yield/assimilation settings. Review methanogenesis's hardcoded fraction and cap handling separately. Neither dormant path should be enabled merely because its enum exists.

**Manual acceptance checklist**

1. Start the accepted geodesic configuration (seed 123456, 100 founders, vent multiplier 10 and the user's H2/CO2 production settings, 15–80 °C hydrogen optimum). Confirm reports appear at the existing simulated-time cadence. Population observations around 5,300–5,500 remain context, not an assertion.
2. Pin a live ID. Check required carbon/energy against actual manager settings, progress greater than 1 when stores exceed the threshold, visual size still 1, and current blocker flags. A resource shortage should affect energy/carbon through metabolism rather than adding a new direct ocean-substrate division gate.
3. With detailed logging temporarily enabled, follow a birth. Check zero child preallocation E/C; parent/child sums against the before values and paid cost; unchanged parent age; child age zero; identical valid birth node/layer/world positions; and no unexplained grant. Expected division violations are zero within the stated tolerance.
4. Observe thermal-ineligible and energy/carbon-ineligible organisms. Division remains blocked under the original gates. Ready organisms may wait for the stochastic trial; no minimum age, new cooldown or growth rule was added.
5. Read hydrogen `modelCResidual`, `capLoss`, storage rounding and actual attributed transfers separately. The known 20% model gap is expected at fraction 0.8; additional measured differences reveal float effects. Do not interpret those existing metabolic violations as a newly broken division ledger.
6. Follow a death using existing recycling telemetry: only the remaining store returns once to its death node. Neither resetting accounting readouts nor extinction should recycle C again. Reporting continues at zero population.
7. Pause, refresh the selected snapshot and reset accounting readouts. No biological step, birth, energy/C change or RNG draw should result from inspection. Detailed logging stays bounded; after disabling it, numerical aggregates/Inspector records still work. Existing environment backlog may drain while paused, as documented in the audit.
8. Disable accounting for a control observation at the same intended timestep conditions. Existing movement/newborn rendering, thermal decisions, resource settings and division rules should remain intact. Do not demand identical histories from different rendered-frame/timestep groupings.

**Static verification and limits**

C# 9 syntax parsing and diff/whitespace review were performed. Source-token comparison confirmed unchanged bodies for request formulas, passive motion/newborn kinematics/scheduling, founder spawning, division energy splitting, temperature performance and death recycling; reproduction/temperature RNG call order is unchanged. No Unity, semantic compilation, automated tests, builds, simulations, generation, performance measurement or parameter sweep was run. Runtime validation remains manual.

No new per-frame managed allocations or ocean-sized diagnostic arrays are introduced in the stepping path. The ledger reuses demand/availability scratch only **after** competition has finished reading it; original achieved extents and inventory deltas stay intact. Gross scratch clearing visits only existing touched node/resource pairs. Reaction observations are folded into the existing reverse lifecycle pass before swap-back/removal; growth summaries reuse the existing reporting population traversal. Formatting, bounded readout copies and logs occur only at report boundaries. Per-request observations add two floats and one double; these store deltas/timing evidence, not another authoritative biomass pool. Inspector array-size repair occurs only at report/reset boundaries if a readout array was edited. Disabled diagnostic time is excluded from the reported observed-duration coverage.

**Stop for manual review.** No subsequent biology milestone was started.

[Growth snapshots]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:154
[Reaction accounting]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyDiagnostics.cs:209
[Division accounting]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyDiagnostics.cs:261
[Manager controls]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:469
[Reporting]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyDiagnostics.cs:322
[ReplicatorManager]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorManager.cs:955
[Request building]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:505
[Commit]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:625
[Lifecycle]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:673
[Energy splitting]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:1181
[constructor]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/ReplicatorData.cs:136
[Recycling]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Biology/Replicators/GeodesicBiologyRuntime.cs:759
[inventory exchange]: C:/Users/domin/OneDrive/Dokumenty/Unity/GPT/SimulaVit/Assets/Scripts/Planet/Environment/ResourceSimulation/GeodesicOceanResourceField.cs:867
