using System;
using System.Text;
using UnityEngine;

public enum GeodesicDivisionBlockReason
{
    None, InvalidHabitat, InvalidState, PendingDeath, Temperature, PopulationCap, Energy, Carbon
}

/// <summary>Computed view of packed state, not a second biomass or eligibility authority.</summary>
[Serializable]
public struct GeodesicGrowthSnapshot
{
    public long organismId;
    public int packedIndex, cell, layer;
    public MetabolismType metabolism;
    public float organicC, biomassTarget, requiredCarbon, carbonProgress;
    public float energy, requiredEnergy, temperatureKelvin, temperaturePerformance;
    public float probabilityPerStep, age, visualSize;
    public Vector3 worldPosition;
    public bool carbonLimited, validHabitat, validState, pendingDeath;
    public bool carbonReady, energyReady, temperatureReady, belowPopulationCap, packedCanReplicate;
    public bool divisionPermitted;
    public GeodesicDivisionBlockReason principalBlock;

    internal void Classify()
    {
        // Priority is diagnostic only. The simulation keeps its original gate and RNG order.
        principalBlock = !validHabitat ? GeodesicDivisionBlockReason.InvalidHabitat
            : !validState ? GeodesicDivisionBlockReason.InvalidState
            : pendingDeath ? GeodesicDivisionBlockReason.PendingDeath
            : !temperatureReady ? GeodesicDivisionBlockReason.Temperature
            : !belowPopulationCap ? GeodesicDivisionBlockReason.PopulationCap
            : !energyReady ? GeodesicDivisionBlockReason.Energy
            : !carbonReady ? GeodesicDivisionBlockReason.Carbon
            : GeodesicDivisionBlockReason.None;
        // Eligibility precedes the random trial; probabilityPerStep can still be zero.
        divisionPermitted = principalBlock == GeodesicDivisionBlockReason.None;
    }
}

/// <summary>Read-only interval accounting. No random draws, resource writes or biological decisions.</summary>
[Serializable]
public sealed class GeodesicBiologyDiagnostics
{
    private static readonly int MetabolismCount = Enum.GetValues(typeof(MetabolismType)).Length;
    private const int ExampleLimit = 3;
    // Float-backed state is checked with an absolute + relative tolerance in model inventory units.
    public const double AbsoluteTolerance = 1e-7;
    public const double RelativeTolerance = 2e-6;

    [Flags]
    public enum DivisionViolation
    {
        None = 0, NonFiniteOrNegative = 1, EnergyBalance = 2, CarbonBalance = 4,
        Allocation = 8, BlockedBirth = 16, Habitat = 32, Position = 64, Age = 128
    }

    [Serializable]
    public struct ResourceAmounts
    {
        public double co2, o2, ch4, h2, h2s, fe2, organicC;
        public double Carbon => co2 + ch4 + organicC;

        public void Add(GeodesicOceanResource resource, double value)
        {
            switch (resource)
            {
                case GeodesicOceanResource.CO2: co2 += value; break;
                case GeodesicOceanResource.O2: o2 += value; break;
                case GeodesicOceanResource.CH4: ch4 += value; break;
                case GeodesicOceanResource.H2: h2 += value; break;
                case GeodesicOceanResource.H2S: h2s += value; break;
                case GeodesicOceanResource.Fe2: fe2 += value; break;
                case GeodesicOceanResource.OrganicC: organicC += value; break;
            }
        }

        public override string ToString()
        {
            if (co2 == 0d && o2 == 0d && ch4 == 0d && h2 == 0d && h2s == 0d && fe2 == 0d && organicC == 0d) return "none";
            var text = new StringBuilder(96); // Formatting occurs only at the reporting boundary.
            Append(text, "CO2", co2); Append(text, "O2", o2); Append(text, "CH4", ch4);
            Append(text, "H2", h2); Append(text, "H2S", h2s); Append(text, "Fe2", fe2); Append(text, "OrganicC", organicC);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string name, double value)
        {
            if (value == 0d) return;
            if (text.Length > 0) text.Append(',');
            text.Append(name).Append(':').Append(value.ToString("G6"));
        }
    }

    [Serializable]
    public struct ReactionAccounting
    {
        public long requests, achieved, unattributedCarbonReactions, modelViolations, measuredViolations, invalid;
        public long inputTransferViolations, zeroMeasuredInputWithEnergy;
        public ResourceAmounts requestedInputs, plannedInputs, attributedInputs, netOnlyInputs;
        public ResourceAmounts definedProducts, attributedProducts, netOnlyProducts;
        public double requestedStore, storedCarbon, capLoss, storeFloatResidual;
        public double modelCarbonResidual, plannedCarbonResidual, measuredCarbonResidual;
        public double missingCarbon, excessCarbon, maxAbsMeasuredCarbonResidual;
        public double energyAwarded, energyDelta;
    }

    [Serializable]
    public struct DivisionSample
    {
        public GeodesicGrowthSnapshot before;
        public long childId;
        public float parentEnergy, childEnergy, parentCarbon, childCarbon, splitToChild;
        public float childInitialEnergy, childInitialCarbon;
        public float parentAge, childAge;
        public int childCell, childLayer;
        public Vector3 parentPosition, childPosition;
        public double energyResidual, carbonResidual, placementError;
        public DivisionViolation violations;
        // There are no carbon costs, environmental exchanges or other inventory grants at birth.
    }

    [Serializable]
    public struct DivisionAccounting
    {
        public long births, violations, energyViolations, carbonViolations, allocationViolations;
        public long gateViolations, habitatViolations, positionViolations, ageViolations, invalid;
        public double energyCost, maxAbsEnergyResidual, maxAbsCarbonResidual, maxPlacementError;
    }

    [SerializeField, Tooltip("Read-only growth and accounting aggregates on the existing ten-simulated-second biology schedule.")]
    private bool enabled = true;
    [SerializeField, Tooltip("Off by default. At most one latest birth plus three unusual examples per report, never a log per birth.")]
    private bool logDetailedDivisionExamples;
    [SerializeField, Tooltip("0 inspects the first current packed organism. Pin a diagnostic ID to follow it through swap-back removal; IDs do not survive a new world or reload.")]
    private long selectedOrganismId;
    [SerializeField] private bool selectedOrganismFound;
    [SerializeField, Tooltip("Diagnostic copy only. Editing this readout never edits the organism.")]
    private GeodesicGrowthSnapshot selectedGrowth;
    [SerializeField] private int population, divisionReady, invalidProgress;
    [SerializeField, Tooltip("Carbon/required carbon: <0.25, 0.25-0.5, 0.5-0.75, 0.75-1, >=1. No clamping to 100%.")]
    private int[] progressBins = new int[5];
    [SerializeField] private int[] principalBlockCounts = new int[8];
    [SerializeField] private double lastObservedSimulationSeconds;
    [SerializeField] private DivisionAccounting lastDivisionInterval;
    [SerializeField] private bool hasLatestDivision;
    [SerializeField] private DivisionSample latestDivision;
    [SerializeField] private int unusualExampleCount;
    [SerializeField] private DivisionSample[] unusualExamples = new DivisionSample[ExampleLimit];
    [SerializeField, Tooltip("Indexed by MetabolismType. Actual gross transfers are proportionally attributed only for unmixed node/resource pairs; netOnly fields mark ambiguous mixed pairs.")]
    private ReactionAccounting[] lastReactions = new ReactionAccounting[MetabolismCount];
    [SerializeField] private ResourceAmounts lastRequestedNet, lastMeasuredNet, lastNetTransferResidual;
    [SerializeField] private long lastMixedPairs, lastTransferViolations;
    [SerializeField] private double lastMeasuredCarbonResidual;

    [NonSerialized] private double observedSeconds;
    [NonSerialized] private DivisionAccounting divisionInterval;
    [NonSerialized] private readonly ReactionAccounting[] reactions = new ReactionAccounting[MetabolismCount];
    [NonSerialized] private readonly DivisionSample[] pendingExamples = new DivisionSample[ExampleLimit];
    [NonSerialized] private int pendingExampleCount;
    [NonSerialized] private ResourceAmounts requestedNet, measuredNet, netTransferResidual;
    [NonSerialized] private long mixedPairs, transferViolations;
    [NonSerialized] private bool divisionRecordedThisInterval;

    public bool Enabled => enabled;
    public long SelectedOrganismId => selectedOrganismId;
    public bool SelectedOrganismFound => selectedOrganismFound;
    public GeodesicGrowthSnapshot SelectedGrowth => selectedGrowth;

    internal void AdvanceObservedTime(float dt) => observedSeconds += Math.Max(0d, dt);

    internal void BeginPopulationReport()
    {
        EnsureReadoutStorage();
        population = divisionReady = invalidProgress = 0;
        selectedOrganismFound = false;
        selectedGrowth = default;
        Array.Clear(progressBins, 0, progressBins.Length);
        Array.Clear(principalBlockCounts, 0, principalBlockCounts.Length);
    }

    internal void ObserveGrowth(in GeodesicGrowthSnapshot snapshot)
    {
        population++;
        if (snapshot.divisionPermitted) divisionReady++;
        principalBlockCounts[(int)snapshot.principalBlock]++;
        if (!float.IsFinite(snapshot.carbonProgress) || snapshot.carbonProgress < 0f) invalidProgress++;
        else progressBins[snapshot.carbonProgress >= 1f ? 4 : Mathf.Min(3, (int)(snapshot.carbonProgress * 4f))]++;
        if (snapshot.organismId == selectedOrganismId || (selectedOrganismId == 0 && !selectedOrganismFound))
            SetSelectedGrowth(true, snapshot);
    }

    internal void SetSelectedGrowth(bool found, in GeodesicGrowthSnapshot snapshot)
    {
        selectedOrganismFound = found;
        selectedGrowth = found ? snapshot : default;
    }

    internal void RecordNodeExchange(GeodesicOceanResource resource, double planned, double actual, bool mixed)
    {
        requestedNet.Add(resource, planned);
        measuredNet.Add(resource, actual);
        netTransferResidual.Add(resource, actual - planned);
        if (mixed) mixedPairs++;
        if (!Finite(planned) || !Finite(actual) || OutsideTolerance(actual - planned, planned)) transferViolations++;
    }

    internal void RecordReaction(MetabolismType metabolism, GeodesicOceanResource a, GeodesicOceanResource b,
        GeodesicOceanResource product, double requestedA, double requestedB, double inputA, double inputB,
        double output, double ratioA, double ratioB, double ratioProduct, float desiredStore,
        float storeBefore, float storeAfter, float awardedEnergy, double energyDelta, bool achieved)
    {
        ref ReactionAccounting totals = ref reactions[(int)metabolism];
        totals.requests++;
        if (achieved) totals.achieved++;
        totals.requestedInputs.Add(a, requestedA); totals.requestedInputs.Add(b, requestedB);
        totals.plannedInputs.Add(a, inputA); totals.plannedInputs.Add(b, inputB);
        totals.definedProducts.Add(product, output);
        Attribute(ref totals.attributedInputs, ref totals.netOnlyInputs, a, inputA, ratioA);
        Attribute(ref totals.attributedInputs, ref totals.netOnlyInputs, b, inputB, ratioB);
        Attribute(ref totals.attributedProducts, ref totals.netOnlyProducts, product, output, ratioProduct);

        double stored = (double)storeAfter - storeBefore;
        // Reproduce only the existing float addition for observation; never write the result back.
        double unclampedDelta = (double)(storeBefore + desiredStore) - storeBefore;
        double modelInput = Carbon(a, inputA) + Carbon(b, inputB), modelOutput = Carbon(product, output);
        double modelResidual = modelInput - modelOutput - desiredStore;
        double plannedResidual = modelInput - modelOutput - stored;
        totals.requestedStore += desiredStore; totals.storedCarbon += stored;
        totals.capLoss += Math.Max(0d, unclampedDelta - stored);
        totals.storeFloatResidual += desiredStore - unclampedDelta;
        totals.modelCarbonResidual += modelResidual;
        totals.plannedCarbonResidual += plannedResidual;
        totals.missingCarbon += Math.Max(0d, plannedResidual);
        totals.excessCarbon += Math.Max(0d, -plannedResidual);
        totals.energyAwarded += awardedEnergy; totals.energyDelta += energyDelta;
        if (OutsideTolerance(modelResidual, modelInput)) totals.modelViolations++;
        if (InputTransferDiffers(inputA, ratioA) || InputTransferDiffers(inputB, ratioB)) totals.inputTransferViolations++;
        if (awardedEnergy > 0f && ((inputA > 0d && ratioA == 0d) || (inputB > 0d && ratioB == 0d)))
            totals.zeroMeasuredInputWithEnergy++;

        bool carbonKnown = KnownCarbon(a, inputA, ratioA) && KnownCarbon(b, inputB, ratioB)
            && KnownCarbon(product, output, ratioProduct);
        if (!carbonKnown) totals.unattributedCarbonReactions++;
        else
        {
            double actualInput = AttributedCarbon(a, inputA, ratioA) + AttributedCarbon(b, inputB, ratioB);
            double actualOutput = AttributedCarbon(product, output, ratioProduct);
            double residual = actualInput - actualOutput - stored;
            totals.measuredCarbonResidual += residual;
            if (Finite(residual)) totals.maxAbsMeasuredCarbonResidual = Math.Max(totals.maxAbsMeasuredCarbonResidual, Math.Abs(residual));
            if (OutsideTolerance(residual, actualInput)) totals.measuredViolations++;
        }
        if (!Finite(plannedResidual) || !Finite(energyDelta) || !Finite(awardedEnergy)
            || !Finite(modelInput) || !Finite(modelOutput) || !Finite(stored) || storeBefore < 0f || storeAfter < 0f
            || requestedA < 0d || requestedB < 0d || inputA < 0d || inputB < 0d || output < 0d || desiredStore < 0f || awardedEnergy < 0f)
            totals.invalid++;
    }

    internal void RecordDivision(in GeodesicGrowthSnapshot before, int parentIndex, int childIndex,
        long childId, ReplicatorPopulationState state, GeodesicOceanLayerGrid grid, float split,
        float childInitialEnergy, float childInitialCarbon)
    {
        var sample = new DivisionSample
        {
            before = before, childId = childId, splitToChild = before.carbonLimited ? Mathf.Clamp01(split) : 0f,
            childInitialEnergy = childInitialEnergy, childInitialCarbon = childInitialCarbon,
            parentEnergy = state.Energy[parentIndex], childEnergy = state.Energy[childIndex],
            parentCarbon = state.OrganicCStore[parentIndex], childCarbon = state.OrganicCStore[childIndex],
            parentAge = state.Age[parentIndex], childAge = state.Age[childIndex],
            childCell = state.GeodesicCellIndex[childIndex], childLayer = state.CurrentOceanLayerIndex[childIndex],
            parentPosition = state.Position[parentIndex], childPosition = state.Position[childIndex]
        };
        double expectedEnergy = Math.Max(0d, (double)before.energy - before.requiredEnergy);
        sample.energyResidual = (double)sample.parentEnergy + sample.childEnergy - expectedEnergy;
        sample.carbonResidual = (double)sample.parentCarbon + sample.childCarbon - before.organicC;
        sample.placementError = Vector3.Distance(before.worldPosition, sample.childPosition);
        double expectedChildCarbon = before.carbonLimited ? before.organicC * (double)Mathf.Clamp01(split) : 0d;
        if (!Finite(sample.energyResidual) || !Finite(sample.carbonResidual) || !Finite(sample.placementError)
            || sample.parentEnergy < 0f || sample.childEnergy < 0f || sample.parentCarbon < 0f || sample.childCarbon < 0f)
            sample.violations |= DivisionViolation.NonFiniteOrNegative;
        if (OutsideTolerance(sample.energyResidual, expectedEnergy)) sample.violations |= DivisionViolation.EnergyBalance;
        if (OutsideTolerance(sample.carbonResidual, before.organicC)) sample.violations |= DivisionViolation.CarbonBalance;
        if (OutsideTolerance(sample.childEnergy - expectedEnergy * 0.5d, expectedEnergy)
            || OutsideTolerance(sample.parentEnergy - expectedEnergy * 0.5d, expectedEnergy)
            || OutsideTolerance(sample.childCarbon - expectedChildCarbon, before.organicC)
            || OutsideTolerance(sample.parentCarbon - (before.organicC - expectedChildCarbon), before.organicC))
            sample.violations |= DivisionViolation.Allocation;
        if (childInitialEnergy != 0f || childInitialCarbon != 0f) sample.violations |= DivisionViolation.Allocation;
        if (!before.divisionPermitted || !(before.probabilityPerStep > 0f)) sample.violations |= DivisionViolation.BlockedBirth;
        if (!grid.IsNodeActive(sample.childCell, sample.childLayer) || sample.childCell != before.cell || sample.childLayer != before.layer
            || state.GeodesicCellIndex[parentIndex] != before.cell || state.CurrentOceanLayerIndex[parentIndex] != before.layer)
            sample.violations |= DivisionViolation.Habitat;
        if (!FinitePosition(sample.childPosition) || !FinitePosition(sample.parentPosition)
            || sample.placementError > 1e-5d || Vector3.Distance(before.worldPosition, sample.parentPosition) > 1e-5f)
            sample.violations |= DivisionViolation.Position;
        if (sample.childAge != 0f || sample.parentAge != before.age) sample.violations |= DivisionViolation.Age;

        divisionInterval.births++;
        divisionInterval.energyCost += before.requiredEnergy;
        if (Finite(sample.energyResidual)) divisionInterval.maxAbsEnergyResidual = Math.Max(divisionInterval.maxAbsEnergyResidual, Math.Abs(sample.energyResidual));
        if (Finite(sample.carbonResidual)) divisionInterval.maxAbsCarbonResidual = Math.Max(divisionInterval.maxAbsCarbonResidual, Math.Abs(sample.carbonResidual));
        if (Finite(sample.placementError)) divisionInterval.maxPlacementError = Math.Max(divisionInterval.maxPlacementError, sample.placementError);
        if (sample.violations != DivisionViolation.None)
        {
            divisionInterval.violations++;
            if ((sample.violations & DivisionViolation.EnergyBalance) != 0) divisionInterval.energyViolations++;
            if ((sample.violations & DivisionViolation.CarbonBalance) != 0) divisionInterval.carbonViolations++;
            if ((sample.violations & DivisionViolation.Allocation) != 0) divisionInterval.allocationViolations++;
            if ((sample.violations & DivisionViolation.BlockedBirth) != 0) divisionInterval.gateViolations++;
            if ((sample.violations & DivisionViolation.Habitat) != 0) divisionInterval.habitatViolations++;
            if ((sample.violations & DivisionViolation.Position) != 0) divisionInterval.positionViolations++;
            if ((sample.violations & DivisionViolation.Age) != 0) divisionInterval.ageViolations++;
            if ((sample.violations & DivisionViolation.NonFiniteOrNegative) != 0) divisionInterval.invalid++;
            if (pendingExampleCount < ExampleLimit) pendingExamples[pendingExampleCount++] = sample;
        }
        latestDivision = sample;
        hasLatestDivision = divisionRecordedThisInterval = true;
    }

    internal void LogAndResetInterval()
    {
        EnsureReadoutStorage();
        lastObservedSimulationSeconds = observedSeconds;
        lastDivisionInterval = divisionInterval;
        Array.Copy(reactions, lastReactions, MetabolismCount);
        Array.Clear(unusualExamples, 0, unusualExamples.Length);
        unusualExampleCount = pendingExampleCount;
        Array.Copy(pendingExamples, unusualExamples, pendingExampleCount);
        lastRequestedNet = requestedNet; lastMeasuredNet = measuredNet; lastNetTransferResidual = netTransferResidual;
        lastMixedPairs = mixedPairs; lastTransferViolations = transferViolations;
        double stored = 0d;
        for (int i = 0; i < MetabolismCount; i++) stored += reactions[i].storedCarbon;
        lastMeasuredCarbonResidual = -measuredNet.Carbon - stored;
        Debug.Log($"[GeodesicGrowth] population={population} divisionReady={divisionReady} progressBins(<25/25-50/50-75/75-100/100+percent)={progressBins[0]}/{progressBins[1]}/{progressBins[2]}/{progressBins[3]}/{progressBins[4]} invalidProgress={invalidProgress} principalBlocks(none/habitat/state/death/temperature/cap/energy/carbon)={principalBlockCounts[0]}/{principalBlockCounts[1]}/{principalBlockCounts[2]}/{principalBlockCounts[3]}/{principalBlockCounts[4]}/{principalBlockCounts[5]}/{principalBlockCounts[6]}/{principalBlockCounts[7]} selectedFound={selectedOrganismFound} selectedId={selectedGrowth.organismId} selectedC={selectedGrowth.organicC:G6} target={selectedGrowth.biomassTarget:G6} requiredC={selectedGrowth.requiredCarbon:G6} progress={selectedGrowth.carbonProgress:G6} E={selectedGrowth.energy:G6} requiredE(strictlyGreater)={selectedGrowth.requiredEnergy:G6} temperaturePerformance={selectedGrowth.temperaturePerformance:G6} permitted={selectedGrowth.divisionPermitted} block={selectedGrowth.principalBlock} probabilityPerStep={selectedGrowth.probabilityPerStep:G6} visualSize={selectedGrowth.visualSize:G6}");
        Debug.Log($"[GeodesicDivisionAccounting] observedSimulationSeconds={observedSeconds:G6} birthsInterval={divisionInterval.births} energyCost={divisionInterval.energyCost:G9} maxAbsResidual(E/C)={divisionInterval.maxAbsEnergyResidual:G9}/{divisionInterval.maxAbsCarbonResidual:G9} violations={divisionInterval.violations} violations(E/C/allocation/gate/habitat/position/age/invalid)={divisionInterval.energyViolations}/{divisionInterval.carbonViolations}/{divisionInterval.allocationViolations}/{divisionInterval.gateViolations}/{divisionInterval.habitatViolations}/{divisionInterval.positionViolations}/{divisionInterval.ageViolations}/{divisionInterval.invalid} maxPlacementError={divisionInterval.maxPlacementError:G9} explicitCarbonCost=0 birthEnvironmentalTransfer=0 tolerance=abs({AbsoluteTolerance:G4})+rel({RelativeTolerance:G4})*scale");
        Debug.Log($"[GeodesicResourceAccounting] requestedNet=({requestedNet}) measuredNet=({measuredNet}) measuredMinusRequested=({netTransferResidual}) transferViolations={transferViolations} mixedNodeResourcePairs={mixedPairs} measuredUnaccountedCarbon(+missing/-excess)={lastMeasuredCarbonResidual:G9} scope=reactionsOnly deathRecycling=separate grossAttribution=proportionalUnmixedOnly mixedGross=unobservableNetOnly");
        for (int i = 0; i < MetabolismCount; i++)
        {
            ref ReactionAccounting r = ref reactions[i];
            if (r.requests == 0) continue;
            Debug.Log($"[GeodesicReactionAccounting] metabolism={(MetabolismType)i} requests/achieved={r.requests}/{r.achieved}"
                + $" requestedInputs=({r.requestedInputs}) afterCompetitionInputs=({r.plannedInputs}) actualAttributedInputs=({r.attributedInputs}) netOnlyInputs=({r.netOnlyInputs})"
                + $" definedProducts=({r.definedProducts}) actualAttributedProducts=({r.attributedProducts}) netOnlyProducts=({r.netOnlyProducts})"
                + $" desiredStore={r.requestedStore:G9} actualStoreDelta={r.storedCarbon:G9} capLoss={r.capLoss:G9} storeFloatResidual={r.storeFloatResidual:G9}"
                + $" modelCResidual={r.modelCarbonResidual:G9} plannedCResidual={r.plannedCarbonResidual:G9} plannedMissing/excess={r.missingCarbon:G9}/{r.excessCarbon:G9}"
                + $" measuredAttributedCResidual={r.measuredCarbonResidual:G9} maxAbsMeasuredCResidual={r.maxAbsMeasuredCarbonResidual:G9} unobservableCarbonReactions={r.unattributedCarbonReactions}"
                + $" energyAwarded/actualDelta={r.energyAwarded:G9}/{r.energyDelta:G9} inputTransferViolations={r.inputTransferViolations} zeroMeasuredInputWithEnergy={r.zeroMeasuredInputWithEnergy}"
                + $" violations(model/measured/invalid)={r.modelViolations}/{r.measuredViolations}/{r.invalid}");
        }
        if (logDetailedDivisionExamples)
        {
            if (divisionRecordedThisInterval) LogDivisionExample("latest", latestDivision);
            for (int i = 0; i < pendingExampleCount; i++) LogDivisionExample("unusual", pendingExamples[i]);
        }
        ResetInterval();
    }

    private static void LogDivisionExample(string kind, in DivisionSample s)
        => Debug.Log($"[GeodesicDivisionExample] kind={kind} parentId={s.before.organismId} childId={s.childId} cell/layer={s.before.cell}/{s.before.layer} childCell/layer={s.childCell}/{s.childLayer}"
            + $" E(before/parent/child/cost)={s.before.energy:G9}/{s.parentEnergy:G9}/{s.childEnergy:G9}/{s.before.requiredEnergy:G9} C(before/parent/child)={s.before.organicC:G9}/{s.parentCarbon:G9}/{s.childCarbon:G9}"
            + $" childPreallocation(E/C)={s.childInitialEnergy:G9}/{s.childInitialCarbon:G9} split={s.splitToChild:G6} residual(E/C)={s.energyResidual:G9}/{s.carbonResidual:G9}"
            + $" parentBeforeWorld={s.before.worldPosition.ToString("G9")} parentAfterWorld={s.parentPosition.ToString("G9")} childWorld={s.childPosition.ToString("G9")} placementError={s.placementError:G9}"
            + $" childAge={s.childAge:G6} thermalPerformance={s.before.temperaturePerformance:G6} gates={s.before.principalBlock} violations={s.violations}");

    private static void Attribute(ref ResourceAmounts attributed, ref ResourceAmounts netOnly,
        GeodesicOceanResource resource, double planned, double ratio)
    {
        if (!(planned > 0d)) return;
        if (Finite(ratio)) attributed.Add(resource, planned * ratio);
        else netOnly.Add(resource, planned);
    }

    private static double Carbon(GeodesicOceanResource resource, double value)
        => IsCarbon(resource) ? value : 0d;
    private static bool IsCarbon(GeodesicOceanResource resource)
        => resource == GeodesicOceanResource.CO2 || resource == GeodesicOceanResource.CH4 || resource == GeodesicOceanResource.OrganicC;
    private static bool KnownCarbon(GeodesicOceanResource resource, double amount, double ratio)
        => !IsCarbon(resource) || amount == 0d || Finite(ratio);
    private static bool InputTransferDiffers(double planned, double ratio)
        => planned > 0d && Finite(ratio) && OutsideTolerance(planned * (ratio - 1d), planned);
    private static double AttributedCarbon(GeodesicOceanResource resource, double amount, double ratio)
        => IsCarbon(resource) && amount != 0d ? amount * ratio : 0d;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool FinitePosition(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool OutsideTolerance(double residual, double scale)
        => !Finite(residual) || !Finite(scale) || Math.Abs(residual) > AbsoluteTolerance + RelativeTolerance * Math.Abs(scale);

    private void ResetInterval()
    {
        observedSeconds = 0d; divisionInterval = default; pendingExampleCount = 0;
        requestedNet = measuredNet = netTransferResidual = default;
        mixedPairs = transferViolations = 0; divisionRecordedThisInterval = false;
        Array.Clear(reactions, 0, reactions.Length);
    }

    private void EnsureReadoutStorage()
    {
        // Serialized arrays are inspectable. Repair an edited size at a report/reset boundary,
        // never allocate on a normal simulation step or let readout edits affect biology.
        if (progressBins == null || progressBins.Length != 5) progressBins = new int[5];
        if (principalBlockCounts == null || principalBlockCounts.Length != 8) principalBlockCounts = new int[8];
        if (lastReactions == null || lastReactions.Length != MetabolismCount) lastReactions = new ReactionAccounting[MetabolismCount];
        if (unusualExamples == null || unusualExamples.Length != ExampleLimit) unusualExamples = new DivisionSample[ExampleLimit];
    }

    public void Reset()
    {
        ResetInterval(); BeginPopulationReport();
        lastObservedSimulationSeconds = lastMeasuredCarbonResidual = 0d;
        lastDivisionInterval = default; hasLatestDivision = false; latestDivision = default;
        unusualExampleCount = 0; lastRequestedNet = lastMeasuredNet = lastNetTransferResidual = default;
        lastMixedPairs = lastTransferViolations = 0;
        Array.Clear(lastReactions, 0, lastReactions.Length);
        Array.Clear(unusualExamples, 0, unusualExamples.Length);
        Array.Clear(pendingExamples, 0, pendingExamples.Length);
    }
}
