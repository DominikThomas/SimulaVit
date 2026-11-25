using System;
using NUnit.Framework;

public sealed class GeodesicLocomotionEvolutionDiagnosticsTests
{
    private static GeodesicLocomotionMutationPolicy.Result Mutation(LocomotionType source, LocomotionType target) =>
        new GeodesicLocomotionMutationPolicy.Result { source = source, target = target, attempted = source != target };

    [Test]
    public void OneAnchoredOriginCanProduceManyInheritedDescendantsWithoutExtraMutations()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics(); d.SetEnabled(true, 0d);
        d.Observe(0, 10, LocomotionType.PassiveDrift, 1f, 1d);
        d.Birth(0, 10, LocomotionType.PassiveDrift, 1, 11, LocomotionType.Anchored, 1d, Mutation(LocomotionType.PassiveDrift, LocomotionType.Anchored));
        for (int i = 2; i < 1002; i++) d.Birth(1, 11, LocomotionType.Anchored, i, 10 + i, LocomotionType.Anchored, i,
            Mutation(LocomotionType.Anchored, LocomotionType.Anchored));
        Assert.That(d.MutantBirths[3], Is.EqualTo(1));
        Assert.That(d.InheritedBirths[3], Is.EqualTo(1000));
        Assert.That(d.GetCohort(24).alive, Is.EqualTo(1001));
        Assert.That(d.GetCohort(24).births, Is.EqualTo(1001));
        Assert.That(d.ParentBirths[3], Is.EqualTo(1000));
        Assert.That(d.BirthTransitions[15], Is.EqualTo(1000));
        int reasons = Enum.GetValues(typeof(GeodesicLocomotionMutationPolicy.NoChangeReason)).Length;
        Assert.That(d.NoChangeReasons[3 * reasons + (int)GeodesicLocomotionMutationPolicy.NoChangeReason.AttemptGateNotSelected], Is.EqualTo(1000));
    }

    [Test]
    public void SwapBackKeepsCohortAndLifetimeOffspringWithTheCorrectStableId()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics(); d.SetEnabled(true, 0d);
        d.Observe(0, 10, LocomotionType.PassiveDrift, 1f, 1d);
        d.Birth(0, 10, LocomotionType.PassiveDrift, 1, 11, LocomotionType.Anchored, 1d, Mutation(LocomotionType.PassiveDrift, LocomotionType.Anchored));
        d.Birth(1, 11, LocomotionType.Anchored, 2, 12, LocomotionType.Anchored, 2d, Mutation(LocomotionType.Anchored, LocomotionType.Anchored));
        d.Death(1, 11, LocomotionType.Anchored, 3d); d.SwapBackRemoval(1, 2);
        d.Observe(1, 12, LocomotionType.Anchored, 2f, 5d);
        d.Birth(1, 12, LocomotionType.Anchored, 2, 13, LocomotionType.Anchored, 6d, Mutation(LocomotionType.Anchored, LocomotionType.Anchored));
        Assert.That(d.GetCohort(24).alive, Is.EqualTo(2));
        Assert.That(d.GetCohort(24).births, Is.EqualTo(3));
        Assert.That(d.GetCohort(24).deaths, Is.EqualTo(1));
        Assert.That(d.CompleteLifetimeDeaths[3], Is.EqualTo(1));
        Assert.That(d.CompleteLifetimeOffspring[3], Is.EqualTo(1));
        Assert.That(d.BaselineOrganisms, Is.EqualTo(1), "The swapped child is not imported as an unrelated baseline.");
    }

    [Test]
    public void CohortSamplesAreBoundedAndBaselineLifetimesAreExplicitlyCensored()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics(); d.SetEnabled(true, 50d);
        d.Observe(0, 1, LocomotionType.PassiveDrift, 1f, 51d);
        for (int i = 0; i < 32; i++) d.Birth(0, 1, LocomotionType.PassiveDrift, i + 1, i + 2,
            LocomotionType.Amoeboid, 52d + i, Mutation(LocomotionType.PassiveDrift, LocomotionType.Amoeboid));
        Assert.That(d.UntrackedMutantRoots, Is.EqualTo(24));
        Assert.That(d.FirstSeen[1], Is.EqualTo(52d));
        Assert.That(d.FirstSeen[2], Is.EqualTo(double.PositiveInfinity));
        d.Death(0, 1, LocomotionType.PassiveDrift, 100d);
        Assert.That(d.CompleteLifetimeDeaths[0], Is.Zero);
        Assert.That(d.CoverageStart, Is.EqualTo(50d));
    }

    [Test]
    public void AdultDiagnosticAssignmentsAreNotCountedAsEvolutionaryBirths()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics(); d.SetEnabled(true, 0d);
        d.Observe(0, 1, LocomotionType.PassiveDrift, 1f, 1d);
        d.Observe(0, 1, LocomotionType.Flagellum, 1f, 2d);
        Assert.That(d.ObservedAssignments[2], Is.EqualTo(1));
        Assert.That(d.MutantBirths[2], Is.Zero);
        Assert.That(d.FirstSeen[2], Is.EqualTo(2d));
    }

    [Test]
    public void BirthDepthMismatchIsOnlyAnObservationAndIsBoundedByPhenotype()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics(); d.SetEnabled(true, 0d);
        d.ObserveBirthDepth(LocomotionType.Anchored, .04d);
        d.ObserveBirthDepth(LocomotionType.Anchored, .02d);
        d.ObserveBirthDepth(LocomotionType.Anchored, 0d);
        Assert.That(d.BirthDepthMismatches[3], Is.EqualTo(2));
        Assert.That(d.MaximumBirthDepthMismatchKm[3], Is.EqualTo(.04d));
        Assert.That(d.MutantBirths[3], Is.Zero);
    }

    [Test]
    public void DisabledDiagnosticsDoNotCreateLifetimeOrCohortState()
    {
        var d = new GeodesicLocomotionEvolutionDiagnostics();
        d.Observe(0, 1, LocomotionType.PassiveDrift, 1f, 1d);
        d.Birth(0, 1, LocomotionType.PassiveDrift, 1, 2, LocomotionType.Anchored, 1d, Mutation(LocomotionType.PassiveDrift, LocomotionType.Anchored));
        Assert.That(d.BaselineOrganisms, Is.Zero); Assert.That(d.ParentBirths[0], Is.Zero);
        Assert.That(d.GetCohort(24).founderId, Is.Zero);
    }
}
