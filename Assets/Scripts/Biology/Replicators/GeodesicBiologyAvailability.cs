using System;
using System.Collections.Generic;

/// <summary>Availability for Geodesic founder traits and offspring trait changes.</summary>
[Serializable]
public struct GeodesicBiologyAvailability
{
    // Startup JSON masks use the existing enum indices. Keep those indices stable when extending the enums.
    public const int CoreMetabolisms = (1 << (int)MetabolismType.Hydrogenotrophy)
        | (1 << (int)MetabolismType.SulfurChemosynthesis) | (1 << (int)MetabolismType.Fermentation);
    public const int ImplementedMetabolisms = CoreMetabolisms | (1 << (int)MetabolismType.Photosynthesis)
        | (1 << (int)MetabolismType.Methanogenesis) | (1 << (int)MetabolismType.Methanotrophy);
    public const int AllMetabolisms = ImplementedMetabolisms
        | (1 << (int)MetabolismType.Saprotrophy) | (1 << (int)MetabolismType.Predation);
    public const int PassiveLocomotion = 1 << (int)LocomotionType.PassiveDrift;
    public const int AllLocomotions = PassiveLocomotion | (1 << (int)LocomotionType.Amoeboid)
        | (1 << (int)LocomotionType.Flagellum) | (1 << (int)LocomotionType.Anchored);

    public int metabolismMask;
    public int locomotionMask;

    public static GeodesicBiologyAvailability Default => new GeodesicBiologyAvailability
    {
        metabolismMask = CoreMetabolisms,
        locomotionMask = AllLocomotions
    };

    public GeodesicBiologyAvailability Normalized() => new GeodesicBiologyAvailability
    {
        metabolismMask = metabolismMask & AllMetabolisms,
        locomotionMask = locomotionMask & AllLocomotions
    };

    public bool Allows(MetabolismType type) => (metabolismMask & (1 << (int)type)) != 0;
    public bool Allows(LocomotionType type) => (locomotionMask & (1 << (int)type)) != 0;

    public bool AllowsFounders => Allows(GeodesicBiologyRuntime.NormalFounderMetabolism)
        && Allows(GeodesicBiologyRuntime.NormalFounderLocomotion);

    public string FounderStatus
    {
        get
        {
            if (AllowsFounders) return "Hydrogenotrophy with passive drift (subject to existing founder rules).";
            if (metabolismMask == 0 && locomotionMask == 0)
                return "No founders will emerge. No metabolisms or locomotion types are enabled.";
            if (metabolismMask == 0) return "No founders will emerge. No metabolisms are enabled.";
            if (locomotionMask == 0) return "No founders will emerge. No locomotion types are enabled.";
            return "No founders will emerge. Geodesic founders require hydrogenotrophy and passive drift.";
        }
    }

    public string Describe() =>
        $"[GeodesicBiologyAvailability] Metabolisms enabled: {DescribeMask<MetabolismType>(metabolismMask)}"
        + $" | Locomotion enabled: {DescribeMask<LocomotionType>(locomotionMask)} | Founders: {FounderStatus}";

    private static string DescribeMask<T>(int mask) where T : Enum
    {
        var names = new List<string>();
        foreach (T type in Enum.GetValues(typeof(T)))
            if ((mask & (1 << Convert.ToInt32(type))) != 0) names.Add(type.ToString());
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
