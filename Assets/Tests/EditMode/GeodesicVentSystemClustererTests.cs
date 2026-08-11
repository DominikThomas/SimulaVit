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

    private static Vector3 Direction(float degrees) => new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad), 0f);
    private static double SumWeight(GeodesicVentSystem[] systems, GeodesicVentHabitat habitat) { double sum = 0d; foreach (GeodesicVentSystem system in systems) if (system.Habitat == habitat) sum += system.NormalizedHabitatWeight; return sum; }
    private static void AssertEveryCandidateExactlyOnce(GeodesicVentSystem[] systems, int count) { var seen = new bool[count]; int members = 0; foreach (GeodesicVentSystem system in systems) { Assert.That(system.MemberCount, Is.GreaterThan(0)); foreach (GeodesicVentCandidate member in system.Members) { Assert.That(seen[member.CellIndex], Is.False); seen[member.CellIndex] = true; members++; } } Assert.That(members, Is.EqualTo(count)); }
}
