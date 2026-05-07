using System;
using System.Collections.Generic;

public enum ReactionId
{
    Hydrogenotrophy,
    SulfurChemosynthesis,
    OxygenicPhotosynthesis,
    Saprotrophy,
    Fermentation,
    Methanogenesis,
    Methanotrophy,
    DarkAerobicRespiration,
    DarkAnoxicFallback,
    O2DetoxPlaceholder,
    UVProtectionPlaceholder,
    PhotoferrotrophyPlaceholder,
    SulfurAnoxygenicPhotosynthesisPlaceholder,
    HydrogenAnoxygenicPhotosynthesisPlaceholder,
    PredationPlaceholder
}

[Flags]
public enum ReactionModifierFlags
{
    None = 0,
    RequiresLight = 1 << 0,
    RequiresDarkness = 1 << 1,
    TemperatureScaled = 1 << 2,
    OxygenInhibitionPlaceholder = 1 << 3,
    OxygenToxicityPlaceholder = 1 << 4,
    UVStressPlaceholder = 1 << 5,
}

public readonly struct ReactionStoichiometryTerm
{
    public readonly ResourceType Resource;
    public readonly float Coefficient;

    public ReactionStoichiometryTerm(ResourceType resource, float coefficient)
    {
        Resource = resource;
        Coefficient = coefficient;
    }
}

public readonly struct ReactionDefinition
{
    public readonly ReactionId Id;
    public readonly ReactionStoichiometryTerm[] Inputs;
    public readonly ReactionStoichiometryTerm[] Outputs;
    public readonly float BaseEnergyDelta;
    public readonly float MaintenanceCost;
    public readonly float RunningCost;
    public readonly ReactionModifierFlags ModifierFlags;
    public readonly bool ToxicityPlaceholder;
    public readonly bool InhibitionPlaceholder;

    public ReactionDefinition(
        ReactionId id,
        ReactionStoichiometryTerm[] inputs,
        ReactionStoichiometryTerm[] outputs,
        float baseEnergyDelta,
        float maintenanceCost,
        float runningCost,
        ReactionModifierFlags modifierFlags,
        bool toxicityPlaceholder = false,
        bool inhibitionPlaceholder = false)
    {
        Id = id;
        Inputs = inputs ?? Array.Empty<ReactionStoichiometryTerm>();
        Outputs = outputs ?? Array.Empty<ReactionStoichiometryTerm>();
        BaseEnergyDelta = baseEnergyDelta;
        MaintenanceCost = maintenanceCost;
        RunningCost = runningCost;
        ModifierFlags = modifierFlags;
        ToxicityPlaceholder = toxicityPlaceholder;
        InhibitionPlaceholder = inhibitionPlaceholder;
    }
}

public readonly struct MetabolismReactionBinding
{
    public readonly MetabolismType Metabolism;
    public readonly int PackageIndex;

    public MetabolismReactionBinding(MetabolismType metabolism, int packageIndex)
    {
        Metabolism = metabolism;
        PackageIndex = packageIndex;
    }
}

public readonly struct ReactionPackageDefinition
{
    public readonly string PackageId;
    public readonly MetabolismType Metabolism;
    public readonly ReactionDefinition[] OrderedReactions;
    public readonly ReactionModifierFlags PackageFlags;

    public ReactionPackageDefinition(string packageId, MetabolismType metabolism, ReactionDefinition[] orderedReactions, ReactionModifierFlags packageFlags = ReactionModifierFlags.None)
    {
        PackageId = packageId;
        Metabolism = metabolism;
        OrderedReactions = orderedReactions ?? Array.Empty<ReactionDefinition>();
        PackageFlags = packageFlags;
    }
}

public readonly struct ReactionExecutionContext
{
    public readonly int AgentIndex;
    public readonly int CellIndex;
    public readonly int OceanLayerIndex;
    public readonly float Dt;
    public readonly float PerformanceScalar;
    public readonly float TemperatureKelvin;
    public readonly float LightScalar;

    public ReactionExecutionContext(int agentIndex, int cellIndex, int oceanLayerIndex, float dt, float performanceScalar, float temperatureKelvin, float lightScalar)
    {
        AgentIndex = agentIndex;
        CellIndex = cellIndex;
        OceanLayerIndex = oceanLayerIndex;
        Dt = dt;
        PerformanceScalar = performanceScalar;
        TemperatureKelvin = temperatureKelvin;
        LightScalar = lightScalar;
    }
}

public struct ReactionExecutionResult
{
    public float EnergyDelta;
    public float OrganicCStoreDelta;
    public float O2ToxicSecondsDelta;
    public DeathCause DeathCauseCandidate;
    public bool IncurredResourceStarvation;
}

public static class ReactionDefinitionRegistry
{
    // Phase 1 scaffolding only:
    // - These stoichiometries are provisional/non-authoritative placeholders.
    // - They are intentionally NOT wired into runtime metabolism execution yet.
    // - Do not use these values for balance tuning in this phase.
    private static readonly ReactionPackageDefinition[] PackageDefinitions =
    {
        new ReactionPackageDefinition(
            "sulfur_chemosynthesis_v0_scaffold",
            MetabolismType.SulfurChemosynthesis,
            new[]
            {
                Define(ReactionId.SulfurChemosynthesis,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.H2S, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.5f), Out(ResourceType.S0, 0.5f) },
                    baseEnergyDelta: 1f)
            }),
        new ReactionPackageDefinition(
            "hydrogenotrophy_v0_scaffold",
            MetabolismType.Hydrogenotrophy,
            new[]
            {
                Define(ReactionId.Hydrogenotrophy,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.H2, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.5f) },
                    baseEnergyDelta: 1f)
            }),
        new ReactionPackageDefinition(
            "photosynthesis_v0_scaffold",
            MetabolismType.Photosynthesis,
            new[]
            {
                Define(ReactionId.OxygenicPhotosynthesis,
                    inputs: new[] { In(ResourceType.CO2, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.7f), Out(ResourceType.O2, 0.7f) },
                    baseEnergyDelta: 1f,
                    modifierFlags: ReactionModifierFlags.RequiresLight | ReactionModifierFlags.TemperatureScaled),
                Define(ReactionId.DarkAerobicRespiration,
                    inputs: new[] { In(ResourceType.OrganicC, 1f), In(ResourceType.O2, 1f) },
                    outputs: new[] { Out(ResourceType.CO2, 1f) },
                    baseEnergyDelta: 0.5f,
                    modifierFlags: ReactionModifierFlags.RequiresDarkness),
                Define(ReactionId.DarkAnoxicFallback,
                    inputs: new[] { In(ResourceType.OrganicC, 1f) },
                    outputs: new[] { Out(ResourceType.CO2, 0.6f), Out(ResourceType.H2, 0.2f), Out(ResourceType.DissolvedOrganicLeak, 0.2f) },
                    baseEnergyDelta: 0.1f,
                    modifierFlags: ReactionModifierFlags.RequiresDarkness | ReactionModifierFlags.OxygenInhibitionPlaceholder,
                    inhibitionPlaceholder: true),
                Define(ReactionId.O2DetoxPlaceholder,
                    inputs: Array.Empty<ReactionStoichiometryTerm>(),
                    outputs: Array.Empty<ReactionStoichiometryTerm>(),
                    baseEnergyDelta: 0f,
                    toxicityPlaceholder: true,
                    modifierFlags: ReactionModifierFlags.OxygenToxicityPlaceholder),
                Define(ReactionId.UVProtectionPlaceholder,
                    inputs: Array.Empty<ReactionStoichiometryTerm>(),
                    outputs: Array.Empty<ReactionStoichiometryTerm>(),
                    baseEnergyDelta: 0f,
                    toxicityPlaceholder: true,
                    modifierFlags: ReactionModifierFlags.UVStressPlaceholder),
                Define(ReactionId.PhotoferrotrophyPlaceholder,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.DissolvedFe2Plus, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.5f), Out(ResourceType.Fe, 1f) },
                    baseEnergyDelta: 0f,
                    modifierFlags: ReactionModifierFlags.RequiresLight),
                Define(ReactionId.SulfurAnoxygenicPhotosynthesisPlaceholder,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.H2S, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.5f), Out(ResourceType.S0, 0.5f) },
                    baseEnergyDelta: 0f,
                    modifierFlags: ReactionModifierFlags.RequiresLight),
                Define(ReactionId.HydrogenAnoxygenicPhotosynthesisPlaceholder,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.H2, 1f) },
                    outputs: new[] { Out(ResourceType.OrganicC, 0.5f) },
                    baseEnergyDelta: 0f,
                    modifierFlags: ReactionModifierFlags.RequiresLight)
            }),
        new ReactionPackageDefinition(
            "saprotrophy_v0_scaffold",
            MetabolismType.Saprotrophy,
            new[]
            {
                Define(ReactionId.Saprotrophy,
                    inputs: new[] { In(ResourceType.OrganicC, 1f), In(ResourceType.O2, 0.5f) },
                    outputs: new[] { Out(ResourceType.CO2, 1f) },
                    baseEnergyDelta: 1f)
            }),
        new ReactionPackageDefinition(
            "predation_v0_scaffold",
            MetabolismType.Predation,
            new[]
            {
                Define(ReactionId.PredationPlaceholder,
                    inputs: Array.Empty<ReactionStoichiometryTerm>(),
                    outputs: Array.Empty<ReactionStoichiometryTerm>(),
                    baseEnergyDelta: 0f)
            }),
        new ReactionPackageDefinition(
            "fermentation_v0_scaffold",
            MetabolismType.Fermentation,
            new[]
            {
                Define(ReactionId.Fermentation,
                    inputs: new[] { In(ResourceType.OrganicC, 1f) },
                    outputs: new[] { Out(ResourceType.CO2, 0.5f), Out(ResourceType.CH4, 0.1f) },
                    baseEnergyDelta: 0.4f)
            }),
        new ReactionPackageDefinition(
            "methanogenesis_v0_scaffold",
            MetabolismType.Methanogenesis,
            new[]
            {
                Define(ReactionId.Methanogenesis,
                    inputs: new[] { In(ResourceType.CO2, 1f), In(ResourceType.H2, 1f) },
                    outputs: new[] { Out(ResourceType.CH4, 1f) },
                    baseEnergyDelta: 0.8f)
            }),
        new ReactionPackageDefinition(
            "methanotrophy_v0_scaffold",
            MetabolismType.Methanotrophy,
            new[]
            {
                Define(ReactionId.Methanotrophy,
                    inputs: new[] { In(ResourceType.CH4, 1f), In(ResourceType.O2, 1f) },
                    outputs: new[] { Out(ResourceType.CO2, 1f) },
                    baseEnergyDelta: 0.8f)
            })
    };

    private static readonly MetabolismReactionBinding[] MetabolismBindings = BuildBindings(PackageDefinitions);
    private static readonly int[] PackageIndexByMetabolism = BuildPackageIndexLookup(PackageDefinitions);

    // Debug/editor-facing view only; do not enumerate in per-agent hot loops.
    public static ReactionPackageDefinition[] Packages => PackageDefinitions;

    // Hot-loop-safe lookup path (O(1) table lookup by enum index). Kept allocation-free.
    public static bool TryGetPackage(MetabolismType metabolism, out ReactionPackageDefinition package)
    {
        int metabolismIndex = (int)metabolism;
        if ((uint)metabolismIndex < (uint)PackageIndexByMetabolism.Length)
        {
            int packageIndex = PackageIndexByMetabolism[metabolismIndex];
            if (packageIndex >= 0)
            {
                package = PackageDefinitions[packageIndex];
                return true;
            }
        }

        package = default;
        return false;
    }

    public static void ValidateOrThrow()
    {
        Array metabolisms = Enum.GetValues(typeof(MetabolismType));

        for (int i = 0; i < metabolisms.Length; i++)
        {
            var metabolism = (MetabolismType)metabolisms.GetValue(i);
            if (!TryGetPackage(metabolism, out ReactionPackageDefinition package))
                throw new InvalidOperationException("Missing reaction package binding for metabolism: " + metabolism + ".");

            if (package.OrderedReactions == null)
                throw new InvalidOperationException("Null reaction array for metabolism package: " + package.PackageId + ".");

            if (package.OrderedReactions.Length == 0)
                throw new InvalidOperationException("Reaction package has no reactions: " + package.PackageId + ".");
        }
    }

    private static MetabolismReactionBinding[] BuildBindings(ReactionPackageDefinition[] packages)
    {
        var bindings = new MetabolismReactionBinding[packages.Length];
        for (int i = 0; i < packages.Length; i++)
            bindings[i] = new MetabolismReactionBinding(packages[i].Metabolism, i);
        return bindings;
    }

    private static int[] BuildPackageIndexLookup(ReactionPackageDefinition[] packages)
    {
        int metabolismCount = Enum.GetValues(typeof(MetabolismType)).Length;
        var lookup = new int[metabolismCount];

        for (int i = 0; i < lookup.Length; i++)
            lookup[i] = -1;

        for (int i = 0; i < packages.Length; i++)
        {
            int metabolismIndex = (int)packages[i].Metabolism;
            if ((uint)metabolismIndex >= (uint)lookup.Length)
                continue;

            lookup[metabolismIndex] = i;
        }

        return lookup;
    }

    private static ReactionStoichiometryTerm In(ResourceType type, float value) => new ReactionStoichiometryTerm(type, value);
    private static ReactionStoichiometryTerm Out(ResourceType type, float value) => new ReactionStoichiometryTerm(type, value);

    private static ReactionDefinition Define(
        ReactionId id,
        ReactionStoichiometryTerm[] inputs,
        ReactionStoichiometryTerm[] outputs,
        float baseEnergyDelta,
        float maintenanceCost = 0f,
        float runningCost = 0f,
        ReactionModifierFlags modifierFlags = ReactionModifierFlags.None,
        bool toxicityPlaceholder = false,
        bool inhibitionPlaceholder = false)
    {
        return new ReactionDefinition(
            id,
            inputs,
            outputs,
            baseEnergyDelta,
            maintenanceCost,
            runningCost,
            modifierFlags,
            toxicityPlaceholder,
            inhibitionPlaceholder);
    }
}
