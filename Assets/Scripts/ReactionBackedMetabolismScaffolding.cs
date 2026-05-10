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


public readonly struct MetabolismReactionRuntimeBinding
{
    public readonly MetabolismType Metabolism;
    public readonly ResourceType PrimaryInput0;
    public readonly ResourceType PrimaryInput1;
    public readonly ResourceType PrimaryOutput0;
    public readonly ResourceType PrimaryOutput1;
    public readonly ResourceType SecondaryInput0;
    public readonly ResourceType SecondaryOutput0;
    public readonly ResourceType SecondaryOutput1;
    public readonly ResourceType SecondaryOutput2;

    public MetabolismReactionRuntimeBinding(
        MetabolismType metabolism,
        ResourceType primaryInput0,
        ResourceType primaryInput1,
        ResourceType primaryOutput0,
        ResourceType primaryOutput1,
        ResourceType secondaryInput0 = default,
        ResourceType secondaryOutput0 = default,
        ResourceType secondaryOutput1 = default,
        ResourceType secondaryOutput2 = default)
    {
        Metabolism = metabolism;
        PrimaryInput0 = primaryInput0;
        PrimaryInput1 = primaryInput1;
        PrimaryOutput0 = primaryOutput0;
        PrimaryOutput1 = primaryOutput1;
        SecondaryInput0 = secondaryInput0;
        SecondaryOutput0 = secondaryOutput0;
        SecondaryOutput1 = secondaryOutput1;
        SecondaryOutput2 = secondaryOutput2;
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
                    // Scaffold resource identities should mirror legacy Fermentation runtime behavior.
                    // Coefficients remain non-authoritative during Phase 1 scaffolding.
                    outputs: new[] { Out(ResourceType.CO2, 0.5f), Out(ResourceType.H2, 0.1f) },
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
    private static readonly MetabolismReactionRuntimeBinding[] RuntimeBindings = BuildRuntimeBindings(PackageDefinitions);

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

    public static bool TryGetRuntimeBinding(MetabolismType metabolism, out MetabolismReactionRuntimeBinding binding)
    {
        int metabolismIndex = (int)metabolism;
        if ((uint)metabolismIndex < (uint)RuntimeBindings.Length)
        {
            binding = RuntimeBindings[metabolismIndex];
            if (binding.Metabolism == metabolism)
                return true;
        }

        binding = default;
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

    private static MetabolismReactionRuntimeBinding[] BuildRuntimeBindings(ReactionPackageDefinition[] packages)
    {
        int metabolismCount = Enum.GetValues(typeof(MetabolismType)).Length;
        var bindings = new MetabolismReactionRuntimeBinding[metabolismCount];

        for (int i = 0; i < packages.Length; i++)
        {
            ReactionPackageDefinition package = packages[i];
            if ((uint)package.Metabolism >= (uint)bindings.Length)
                continue;

            bindings[(int)package.Metabolism] = ResolveRuntimeBinding(package);
        }

        return bindings;
    }

    private static MetabolismReactionRuntimeBinding ResolveRuntimeBinding(ReactionPackageDefinition package)
    {
        switch (package.Metabolism)
        {
            case MetabolismType.Hydrogenotrophy:
            {
                ResourceType co2 = ResourceType.CO2;
                ResourceType h2 = ResourceType.H2;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction) && reaction.Inputs != null && reaction.Inputs.Length >= 2)
                {
                    co2 = reaction.Inputs[0].Resource;
                    h2 = reaction.Inputs[1].Resource;
                }
                return new MetabolismReactionRuntimeBinding(package.Metabolism, co2, h2, default, default);
            }
            case MetabolismType.SulfurChemosynthesis:
            {
                ResourceType co2 = ResourceType.CO2;
                ResourceType h2s = ResourceType.H2S;
                ResourceType sulfur = ResourceType.S0;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction))
                {
                    if (reaction.Inputs != null && reaction.Inputs.Length >= 2)
                    {
                        co2 = reaction.Inputs[0].Resource;
                        h2s = reaction.Inputs[1].Resource;
                    }
                    if (reaction.Outputs != null && reaction.Outputs.Length >= 2)
                        sulfur = reaction.Outputs[1].Resource;
                }
                return new MetabolismReactionRuntimeBinding(package.Metabolism, co2, h2s, sulfur, default);
            }
            case MetabolismType.Photosynthesis:
            {
                ResourceType lightInput = ResourceType.CO2;
                ResourceType lightOutput = ResourceType.O2;
                ResourceType darkAerobicInput = ResourceType.O2;
                ResourceType darkAerobicOutput = ResourceType.CO2;
                ResourceType darkAnoxicOutput0 = ResourceType.CO2;
                ResourceType darkAnoxicOutput1 = ResourceType.H2;
                ResourceType darkAnoxicOutput2 = ResourceType.DissolvedOrganicLeak;

                ReactionDefinition[] reactions = package.OrderedReactions;
                if (reactions != null && reactions.Length > 0)
                {
                    ReactionDefinition light = reactions[0];
                    if (light.Inputs != null && light.Inputs.Length >= 1)
                        lightInput = light.Inputs[0].Resource;
                    if (light.Outputs != null)
                    {
                        for (int i = 0; i < light.Outputs.Length; i++)
                        {
                            if (light.Outputs[i].Resource == ResourceType.O2)
                            {
                                lightOutput = light.Outputs[i].Resource;
                                break;
                            }
                        }
                    }

                    if (reactions.Length > 1)
                    {
                        ReactionDefinition darkAerobic = reactions[1];
                        if (darkAerobic.Inputs != null)
                        {
                            for (int i = 0; i < darkAerobic.Inputs.Length; i++)
                            {
                                if (darkAerobic.Inputs[i].Resource == ResourceType.O2)
                                {
                                    darkAerobicInput = darkAerobic.Inputs[i].Resource;
                                    break;
                                }
                            }
                        }
                        if (darkAerobic.Outputs != null)
                        {
                            for (int i = 0; i < darkAerobic.Outputs.Length; i++)
                            {
                                if (darkAerobic.Outputs[i].Resource == ResourceType.CO2)
                                {
                                    darkAerobicOutput = darkAerobic.Outputs[i].Resource;
                                    break;
                                }
                            }
                        }
                    }

                    if (reactions.Length > 2)
                    {
                        ReactionDefinition darkAnoxic = reactions[2];
                        if (darkAnoxic.Outputs != null)
                        {
                            for (int i = 0; i < darkAnoxic.Outputs.Length; i++)
                            {
                                ResourceType output = darkAnoxic.Outputs[i].Resource;
                                if (output == ResourceType.CO2)
                                    darkAnoxicOutput0 = output;
                                else if (output == ResourceType.H2)
                                    darkAnoxicOutput1 = output;
                                else if (output == ResourceType.DissolvedOrganicLeak)
                                    darkAnoxicOutput2 = output;
                            }
                        }
                    }
                }

                return new MetabolismReactionRuntimeBinding(
                    package.Metabolism,
                    lightInput,
                    darkAerobicInput,
                    lightOutput,
                    darkAerobicOutput,
                    ResourceType.OrganicCStore,
                    darkAnoxicOutput0,
                    darkAnoxicOutput1,
                    darkAnoxicOutput2);
            }
            case MetabolismType.Saprotrophy:
            {
                ResourceType organicC = ResourceType.OrganicC;
                ResourceType o2 = ResourceType.O2;
                ResourceType co2 = ResourceType.CO2;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction))
                {
                    if (reaction.Inputs != null && reaction.Inputs.Length >= 1)
                        organicC = reaction.Inputs[0].Resource;

                    bool foundO2 = false;
                    if (reaction.Inputs != null)
                    {
                        for (int i = 1; i < reaction.Inputs.Length; i++)
                        {
                            ResourceType input = reaction.Inputs[i].Resource;
                            if (input == ResourceType.O2)
                            {
                                o2 = input;
                                foundO2 = true;
                                break;
                            }
                        }
                    }

                    bool foundCo2 = false;
                    if (reaction.Outputs != null)
                    {
                        for (int i = 0; i < reaction.Outputs.Length; i++)
                        {
                            ResourceType output = reaction.Outputs[i].Resource;
                            if (output == ResourceType.CO2)
                            {
                                co2 = output;
                                foundCo2 = true;
                                break;
                            }
                        }
                    }

                    if (!foundO2)
                        o2 = ResourceType.O2;
                    if (!foundCo2)
                        co2 = ResourceType.CO2;
                }

                return new MetabolismReactionRuntimeBinding(package.Metabolism, organicC, o2, co2, default);
            }
            case MetabolismType.Fermentation:
            {
                ResourceType organicC = ResourceType.OrganicC;
                ResourceType h2 = ResourceType.H2;
                ResourceType co2 = ResourceType.CO2;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction))
                {
                    if (reaction.Inputs != null && reaction.Inputs.Length >= 1)
                        organicC = reaction.Inputs[0].Resource;
                    if (reaction.Outputs != null)
                    {
                        bool foundH2 = false;
                        bool foundCo2 = false;
                        for (int i = 0; i < reaction.Outputs.Length; i++)
                        {
                            ResourceType output = reaction.Outputs[i].Resource;
                            if (!foundH2 && output == ResourceType.H2)
                            {
                                h2 = output;
                                foundH2 = true;
                            }
                            else if (!foundCo2 && output == ResourceType.CO2)
                            {
                                co2 = output;
                                foundCo2 = true;
                            }
                        }
                    }
                }
                return new MetabolismReactionRuntimeBinding(package.Metabolism, organicC, default, h2, co2);
            }
            case MetabolismType.Methanogenesis:
            {
                ResourceType co2 = ResourceType.CO2;
                ResourceType h2 = ResourceType.H2;
                ResourceType ch4 = ResourceType.CH4;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction))
                {
                    if (reaction.Inputs != null && reaction.Inputs.Length >= 2)
                    {
                        co2 = reaction.Inputs[0].Resource;
                        h2 = reaction.Inputs[1].Resource;
                    }
                    if (reaction.Outputs != null && reaction.Outputs.Length >= 1)
                        ch4 = reaction.Outputs[0].Resource;
                }
                return new MetabolismReactionRuntimeBinding(package.Metabolism, co2, h2, ch4, default);
            }
            case MetabolismType.Methanotrophy:
            {
                ResourceType ch4 = ResourceType.CH4;
                ResourceType o2 = ResourceType.O2;
                ResourceType co2 = ResourceType.CO2;
                if (TryGetPrimaryReaction(package, out ReactionDefinition reaction))
                {
                    if (reaction.Inputs != null && reaction.Inputs.Length >= 2)
                    {
                        ch4 = reaction.Inputs[0].Resource;
                        o2 = reaction.Inputs[1].Resource;
                    }
                    if (reaction.Outputs != null && reaction.Outputs.Length >= 1)
                        co2 = reaction.Outputs[0].Resource;
                }
                return new MetabolismReactionRuntimeBinding(package.Metabolism, ch4, o2, co2, default);
            }
            default:
                return new MetabolismReactionRuntimeBinding(package.Metabolism, default, default, default, default);
        }
    }

    private static bool TryGetPrimaryReaction(ReactionPackageDefinition package, out ReactionDefinition reaction)
    {
        if (package.OrderedReactions != null && package.OrderedReactions.Length > 0)
        {
            reaction = package.OrderedReactions[0];
            return true;
        }

        reaction = default;
        return false;
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
