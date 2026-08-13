using System;
using System.Collections.Generic;
using UnityEngine;

public enum GeodesicVentHabitat { Submarine, Terrestrial }

public readonly struct GeodesicVentCandidate
{
    public readonly int CellIndex;
    public readonly int SourceNode;
    public readonly float RawStrength;
    public readonly GeodesicVentHabitat Habitat;

    public GeodesicVentCandidate(int cellIndex, int sourceNode, float rawStrength, GeodesicVentHabitat habitat)
    { CellIndex = cellIndex; SourceNode = sourceNode; RawStrength = rawStrength; Habitat = habitat; }
}

public sealed class GeodesicVentSystem
{
    public int RepresentativeCell { get; internal set; }
    public int RepresentativeBottomNode { get; internal set; }
    public GeodesicVentHabitat Habitat { get; internal set; }
    public float RawStrengthSum { get; internal set; }
    public float RawStrengthMax { get; internal set; }
    public float NormalizedHabitatWeight { get; internal set; }
    public GeodesicVentCandidate[] Members { get; internal set; }
    public int MemberCount => Members != null ? Members.Length : 0;
}

/// <summary>Post-clustering physical vent mouth. Raw members never emit directly after this set is built.</summary>
public readonly struct GeodesicVentSourceOutlet
{
    public readonly GeodesicVentHabitat Habitat;
    public readonly int CellIndex;
    public readonly int SourceNode;
    public readonly int SystemIndex;
    public readonly float RawStrength;
    public readonly float SystemBudgetWeight;
    public readonly float WithinSystemWeight;

    public GeodesicVentSourceOutlet(GeodesicVentHabitat habitat, int cellIndex, int sourceNode, int systemIndex, float rawStrength, float systemBudgetWeight, float withinSystemWeight)
    { Habitat = habitat; CellIndex = cellIndex; SourceNode = sourceNode; SystemIndex = systemIndex; RawStrength = rawStrength; SystemBudgetWeight = systemBudgetWeight; WithinSystemWeight = withinSystemWeight; }
}

/// <summary>Generation-only, deterministic strongest-seed angular clustering.</summary>
public static class GeodesicVentSystemClusterer
{
    public static GeodesicVentSystem[] Cluster(IReadOnlyList<GeodesicVentCandidate> input, Vector3[] cellDirections, float radiusDegrees)
    {
        if (input == null || input.Count == 0) return Array.Empty<GeodesicVentSystem>();
        var candidates = new List<GeodesicVentCandidate>(input.Count);
        for (int i = 0; i < input.Count; i++) candidates.Add(input[i]);
        candidates.Sort(CompareCandidates);
        bool[] assigned = new bool[candidates.Count];
        var systems = new List<GeodesicVentSystem>();
        float minimumDot = Mathf.Cos(Mathf.Clamp(radiusDegrees, 0.01f, 180f) * Mathf.Deg2Rad);

        for (int seedIndex = 0; seedIndex < candidates.Count; seedIndex++)
        {
            if (assigned[seedIndex]) continue;
            GeodesicVentCandidate seed = candidates[seedIndex];
            var members = new List<GeodesicVentCandidate>();
            double sum = 0d;
            float maximum = 0f;
            for (int candidateIndex = seedIndex; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (assigned[candidateIndex]) continue;
                GeodesicVentCandidate candidate = candidates[candidateIndex];
                if (candidate.Habitat != seed.Habitat || Vector3.Dot(cellDirections[seed.CellIndex], cellDirections[candidate.CellIndex]) < minimumDot) continue;
                assigned[candidateIndex] = true;
                members.Add(candidate);
                sum += candidate.RawStrength;
                maximum = Mathf.Max(maximum, candidate.RawStrength);
            }
            systems.Add(new GeodesicVentSystem
            {
                RepresentativeCell = seed.CellIndex,
                RepresentativeBottomNode = seed.SourceNode,
                Habitat = seed.Habitat,
                RawStrengthSum = (float)sum,
                RawStrengthMax = maximum,
                Members = members.ToArray()
            });
        }

        NormalizeByHabitat(systems, GeodesicVentHabitat.Submarine);
        NormalizeByHabitat(systems, GeodesicVentHabitat.Terrestrial);
        return systems.ToArray();
    }

    private static int CompareCandidates(GeodesicVentCandidate a, GeodesicVentCandidate b)
    {
        int habitat = a.Habitat.CompareTo(b.Habitat);
        if (habitat != 0) return habitat;
        int strength = b.RawStrength.CompareTo(a.RawStrength);
        return strength != 0 ? strength : a.CellIndex.CompareTo(b.CellIndex);
    }

    private static void NormalizeByHabitat(List<GeodesicVentSystem> systems, GeodesicVentHabitat habitat)
    {
        double total = 0d;
        for (int i = 0; i < systems.Count; i++) if (systems[i].Habitat == habitat) total += systems[i].RawStrengthSum;
        if (total <= 0d) return;
        for (int i = 0; i < systems.Count; i++) if (systems[i].Habitat == habitat) systems[i].NormalizedHabitatWeight = (float)(systems[i].RawStrengthSum / total);
    }
}

public enum GeodesicVentVisualArchetype { SingleDominant, DominantWithSatellites, SimilarOutlets }

/// <summary>Pure deterministic compact physical-outlet selection; it never mutates clustered system weights.</summary>
public static class GeodesicVentOutletSelector
{
    public static GeodesicVentVisualArchetype GetArchetype(int representativeCell)
    {
        uint value = unchecked((uint)representativeCell) ^ 0xB5297A4Du;
        value ^= value >> 16; value *= 0x68E31DA4u; value ^= value >> 15;
        return (GeodesicVentVisualArchetype)(value % 3u);
    }

    public static int SelectLocalMembers(GeodesicVentSystem system, Vector3[] cellDirections, float radiusDegrees, int maximumOutlets, int[] destination)
    {
        if (system == null || system.Members == null || cellDirections == null || destination == null || maximumOutlets <= 0) return 0;
        int capacity = Mathf.Min(maximumOutlets, destination.Length);
        float minimumDot = Mathf.Cos(Mathf.Clamp(radiusDegrees, 0.1f, 180f) * Mathf.Deg2Rad);
        Vector3 representativeDirection = cellDirections[system.RepresentativeCell];
        int count = 0;
        while (count < capacity)
        {
            int best = -1; float bestDot = -2f; float bestStrength = -1f; int bestCell = int.MaxValue;
            for (int member = 0; member < system.Members.Length; member++)
            {
                int cell = system.Members[member].CellIndex;
                float dot = Vector3.Dot(representativeDirection, cellDirections[cell]);
                if (dot < minimumDot || AlreadySelected(destination, count, member)) continue;
                float strength = system.Members[member].RawStrength;
                if (dot > bestDot + 1e-7f || (Mathf.Abs(dot - bestDot) <= 1e-7f && (strength > bestStrength || (Mathf.Approximately(strength, bestStrength) && cell < bestCell))))
                { best = member; bestDot = dot; bestStrength = strength; bestCell = cell; }
            }
            if (best < 0) break;
            destination[count++] = best;
        }
        return count;
    }

    public static float GetOutletScale(GeodesicVentVisualArchetype archetype, int outletIndex, float relativeMemberStrength)
    {
        if (outletIndex == 0) return archetype == GeodesicVentVisualArchetype.SimilarOutlets ? 1f : 1.2f;
        float memberScale = Mathf.Lerp(0.72f, 1f, Mathf.Sqrt(Mathf.Clamp01(relativeMemberStrength)));
        return archetype == GeodesicVentVisualArchetype.DominantWithSatellites ? memberScale * 0.72f : memberScale;
    }

    private static bool AlreadySelected(int[] selected, int count, int candidate)
    { for (int i = 0; i < count; i++) if (selected[i] == candidate) return true; return false; }
}
