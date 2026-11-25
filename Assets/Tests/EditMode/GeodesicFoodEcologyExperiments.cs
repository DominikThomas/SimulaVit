using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicFoodEcologyExperiments
{
    [Serializable] private sealed class Reports { public GeodesicLocomotionEcologyExperiments.Report[] reports; }

    [TestCase("A-current")]
    [TestCase("B-independent")]
    [TestCase("C-smooth")]
    [TestCase("D-response")]
    [TestCase("E-cadence")]
    [TestCase("E-speed")]
    public void CompareLocalSignalAndResponseHypothesesWithoutChangingNormalSimulation(string alternative)
    {
        var reports = new GeodesicLocomotionEcologyExperiments.Report[8];
        for (int habitat = 0; habitat < 2; habitat++)
        for (int phenotype = 0; phenotype < 4; phenotype++)
        using (var fixture = new GeodesicLocomotionEcologyExperiments.Fixture((LocomotionType)phenotype, true, false, habitat == 1))
        {
            fixture.ConfigureAlternative(alternative);
            var report = fixture.Run(alternative + (habitat == 0 ? "-compact" : "-shoulder"));
            reports[habitat * 4 + phenotype] = report;
            Assert.That(report.survivors, Is.EqualTo(report.founders + report.births - report.deaths));
            Assert.That(Math.Abs(report.carbonBalanceResidual), Is.LessThan(1e-5));
            Assert.That(report.metabolicallyViableSeconds, Is.InRange(0d, report.organismSeconds));
            // Characterization: no test asserts a preferred ecological ranking.
        }
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/FoodTaxisAudit"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, alternative + "-ecology.json"), JsonUtility.ToJson(new Reports { reports = reports }, true));
    }

    [Test]
    public void IdenticalSeedAndAuthoritativeStepsRepeatTheSameCandidateOutcome()
    {
        string first;
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, true))
        { f.ConfigureAlternative("E-speed"); first = JsonUtility.ToJson(f.Run("repeatability")); }
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, true))
        { f.ConfigureAlternative("E-speed"); Assert.That(JsonUtility.ToJson(f.Run("repeatability")), Is.EqualTo(first)); }
    }
}
