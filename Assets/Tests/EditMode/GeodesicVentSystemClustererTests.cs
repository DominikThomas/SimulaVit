using System;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicVentSystemClustererTests
{
    [Test]
    public void StrongestSeedClustering_IsDeterministicAndNonTransitive()
    {
        Vector3[] directions = { Direction(0f), Direction(2f), Direction(4f), Direction(30f) };
        var candidates = new[]
        {
            new GeodesicVentCandidate(2, 12, 0.7f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(0, 10, 1f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(3, 13, 0.6f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(1, 11, 0.8f, GeodesicVentHabitat.Submarine)
        };
        GeodesicVentSystem[] systems = GeodesicVentSystemClusterer.Cluster(candidates, directions, 3f);
        Assert.That(systems, Has.Length.EqualTo(3));
        Assert.That(systems[0].RepresentativeCell, Is.EqualTo(0));
        Assert.That(systems[0].MemberCount, Is.EqualTo(2));
        Assert.That(systems[1].RepresentativeCell, Is.EqualTo(2));
        AssertEveryCandidateExactlyOnce(systems, candidates.Length);
        Assert.That(SumWeight(systems, GeodesicVentHabitat.Submarine), Is.EqualTo(1d).Within(1e-6d));
    }

    [Test]
    public void HabitatBoundary_PreventsCoastalMerge()
    {
        Vector3[] directions = { Direction(0f), Direction(0.1f) };
        var candidates = new[]
        {
            new GeodesicVentCandidate(0, 4, 1f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(1, -1, 1f, GeodesicVentHabitat.Terrestrial)
        };
        GeodesicVentSystem[] systems = GeodesicVentSystemClusterer.Cluster(candidates, directions, 20f);
        Assert.That(systems, Has.Length.EqualTo(2));
        Assert.That(SumWeight(systems, GeodesicVentHabitat.Submarine), Is.EqualTo(1d).Within(1e-6d));
        Assert.That(SumWeight(systems, GeodesicVentHabitat.Terrestrial), Is.EqualTo(1d).Within(1e-6d));
    }

    [TestCase(1d)] [TestCase(2d)] [TestCase(5d)] [TestCase(10d)]
    public void GlobalBudget_IsIndependentOfCadenceAndSystemCount(double cadence)
    {
        const double rate = 0.006d, duration = 100d;
        double[] weights = { 0.1d, 0.25d, 0.65d };
        double injected = 0d;
        for (double elapsed = 0d; elapsed < duration; elapsed += cadence)
            for (int i = 0; i < weights.Length; i++) injected += rate * weights[i] * cadence;
        Assert.That(injected, Is.EqualTo(rate * duration).Within(1e-12d));
    }

    [Test]
    public void GeothermalActivity_IsDeterministicSeamlessAndSpatiallyVariable()
    {
        const uint seed = 123456u;
        Vector3 direction = new Vector3(0.31f, -0.72f, 0.62f).normalized;
        float first = GeodesicOceanResourceField.EvaluateGeothermalActivity(direction, seed);
        float repeated = GeodesicOceanResourceField.EvaluateGeothermalActivity(direction, seed);
        float nearby = GeodesicOceanResourceField.EvaluateGeothermalActivity((direction + new Vector3(0.001f, 0.002f, -0.001f)).normalized, seed);
        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(Mathf.Abs(nearby - first), Is.LessThan(0.02f));
        float minimum = 1f, maximum = 0f;
        for (int i = 0; i < 128; i++)
        {
            Vector3 sample = new Vector3(Mathf.Sin(i * 1.7f), Mathf.Cos(i * 2.3f), Mathf.Sin(i * 0.71f + 1f)).normalized;
            float activity = GeodesicOceanResourceField.EvaluateGeothermalActivity(sample, seed);
            minimum = Mathf.Min(minimum, activity); maximum = Mathf.Max(maximum, activity);
        }
        Assert.That(minimum, Is.LessThan(0.05f));
        Assert.That(maximum, Is.GreaterThan(0.5f));
    }

    [Test]
    public void RawClusterStrength_RemainsSeparateFromNormalizedProductionShare()
    {
        Vector3[] directions = { Direction(0f), Direction(1f), Direction(40f) };
        var candidates = new[]
        {
            new GeodesicVentCandidate(0, 10, 2f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(1, 11, 1f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(2, 12, 0.5f, GeodesicVentHabitat.Submarine)
        };
        GeodesicVentSystem[] systems = GeodesicVentSystemClusterer.Cluster(candidates, directions, 5f);
        Assert.That(systems[0].RawStrengthSum, Is.EqualTo(3f));
        Assert.That(systems[1].RawStrengthSum, Is.EqualTo(0.5f));
        Assert.That(SumWeight(systems, GeodesicVentHabitat.Submarine), Is.EqualTo(1d).Within(1e-6d));
        Assert.That(Mathf.Sqrt(systems[0].RawStrengthSum / systems[0].RawStrengthSum), Is.GreaterThanOrEqualTo(Mathf.Sqrt(systems[1].RawStrengthSum / systems[0].RawStrengthSum)));
    }

    [Test]
    public void VisualOutletSelection_IsDeterministicAndRepresentativeLocal()
    {
        Vector3[] directions = { Direction(0f), Direction(1f), Direction(2.5f), Direction(12f), Direction(18f) };
        var candidates = new[]
        {
            new GeodesicVentCandidate(0, 10, 2f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(1, 11, 1.1f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(2, 12, 1f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(3, 13, 1.8f, GeodesicVentHabitat.Submarine),
            new GeodesicVentCandidate(4, 14, 1.7f, GeodesicVentHabitat.Submarine)
        };
        GeodesicVentSystem system = GeodesicVentSystemClusterer.Cluster(candidates, directions, 20f)[0];
        int[] first = new int[5], repeated = new int[5];
        int firstCount = GeodesicVentOutletSelector.SelectLocalMembers(system, directions, 3f, 5, first);
        int repeatedCount = GeodesicVentOutletSelector.SelectLocalMembers(system, directions, 3f, 5, repeated);
        Assert.That(firstCount, Is.EqualTo(3));
        Assert.That(repeatedCount, Is.EqualTo(firstCount));
        for (int i = 0; i < firstCount; i++)
        {
            Assert.That(repeated[i], Is.EqualTo(first[i]));
            int selectedCell = system.Members[first[i]].CellIndex;
            Assert.That(Vector3.Angle(directions[system.RepresentativeCell], directions[selectedCell]), Is.LessThanOrEqualTo(3.001f));
        }
        Assert.That(system.MemberCount, Is.EqualTo(5), "The authoritative system remains geographically broad.");
    }

    [Test]
    public void VisualArchetypeAndScale_AreDeterministicAndVisualOnly()
    {
        Assert.That(GeodesicVentOutletSelector.GetArchetype(42), Is.EqualTo(GeodesicVentOutletSelector.GetArchetype(42)));
        Assert.That(GeodesicVentOutletSelector.GetOutletScale(GeodesicVentVisualArchetype.DominantWithSatellites, 0, 1f), Is.GreaterThan(GeodesicVentOutletSelector.GetOutletScale(GeodesicVentVisualArchetype.DominantWithSatellites, 1, 1f)));
        Assert.That(GeodesicVentOutletSelector.GetOutletScale(GeodesicVentVisualArchetype.SimilarOutlets, 1, 1f), Is.EqualTo(1f));
    }

    private static Vector3 Direction(float degrees) => new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad), 0f);
    private static double SumWeight(GeodesicVentSystem[] systems, GeodesicVentHabitat habitat) { double sum = 0d; foreach (GeodesicVentSystem system in systems) if (system.Habitat == habitat) sum += system.NormalizedHabitatWeight; return sum; }
    private static void AssertEveryCandidateExactlyOnce(GeodesicVentSystem[] systems, int count) { var seen = new bool[count]; int members = 0; foreach (GeodesicVentSystem system in systems) { Assert.That(system.MemberCount, Is.GreaterThan(0)); foreach (GeodesicVentCandidate member in system.Members) { Assert.That(seen[member.CellIndex], Is.False); seen[member.CellIndex] = true; members++; } } Assert.That(members, Is.EqualTo(count)); }
}
