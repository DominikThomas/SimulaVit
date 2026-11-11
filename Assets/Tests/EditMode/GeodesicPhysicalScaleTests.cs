using NUnit.Framework;

public sealed class GeodesicPhysicalScaleTests
{
    [Test]
    public void LengthAreaAndVolumeConversionsAreAuthoritative()
    {
        Assert.That(GeodesicPhysicalScale.LengthKilometres(1d), Is.EqualTo(1000d));
        Assert.That(GeodesicPhysicalScale.LengthKilometres(8d), Is.EqualTo(8000d));
        Assert.That(GeodesicPhysicalScale.AreaSquareKilometres(1d), Is.EqualTo(1e6d));
        Assert.That(GeodesicPhysicalScale.VolumeCubicKilometres(1d), Is.EqualTo(1e9d));
    }

    [Test]
    public void PhysicalVolumeScalesSphericalWedgeResultAndPreservesRatios()
    {
        const double geometricA = 0.00125d;
        double physicalA = GeodesicPhysicalScale.VolumeCubicKilometres(geometricA);
        double physicalB = GeodesicPhysicalScale.VolumeCubicKilometres(geometricA * 2d);
        Assert.That(physicalA, Is.EqualTo(geometricA * 1e9d).Within(1e-6d));
        Assert.That(physicalB / physicalA, Is.EqualTo(2d).Within(1e-12d));
    }

    [Test]
    public void ConcentrationInventoryRoundTripUsesDoublePhysicalVolume()
    {
        const double concentration = 0.0375d;
        double volumeKm3 = GeodesicPhysicalScale.VolumeCubicKilometres(0.001234d);
        double inventory = GeodesicPhysicalScale.Inventory(concentration, volumeKm3);
        Assert.That(GeodesicPhysicalScale.Concentration(inventory, volumeKm3), Is.EqualTo(concentration).Within(1e-14d));
    }

    [Test]
    public void TransferBetweenUnequalPhysicalVolumesConservesInventory()
    {
        double volumeA = GeodesicPhysicalScale.VolumeCubicKilometres(0.001d);
        double volumeB = GeodesicPhysicalScale.VolumeCubicKilometres(0.0025d);
        double concentrationA = 2d, concentrationB = 0.25d;
        double before = concentrationA * volumeA + concentrationB * volumeB;
        double transferred = 0.1d * (concentrationA - concentrationB) * System.Math.Min(volumeA, volumeB);
        concentrationA -= transferred / volumeA;
        concentrationB += transferred / volumeB;
        Assert.That(concentrationA * volumeA + concentrationB * volumeB, Is.EqualTo(before).Within(before * 1e-12d));
    }

    [Test]
    public void PhysicalInterpretationDoesNotMutateUnityGeometryOrLegacyConstants()
    {
        const double renderedRadiusUnity = 8d, geometricDepthUnity = 0.2d;
        _ = GeodesicPhysicalScale.LengthKilometres(renderedRadiusUnity);
        _ = GeodesicPhysicalScale.LengthKilometres(geometricDepthUnity);
        Assert.That(renderedRadiusUnity, Is.EqualTo(8d));
        Assert.That(geometricDepthUnity, Is.EqualTo(0.2d));
        Assert.That(typeof(GeodesicPhysicalScale).Assembly, Is.EqualTo(typeof(PlanetResourceMap).Assembly));
        Assert.That(typeof(PlanetResourceMap).IsAssignableFrom(typeof(GeodesicOceanResourceField)), Is.False,
            "Geodesic physical inventory remains isolated from the legacy PlanetResourceMap authority.");
    }

    [Test]
    public void ResolutionDependenceDiagnosticReportsSubdivisionFiveToSixRatio()
    {
        const double oceanAreaUnity2 = 4d * System.Math.PI * 8d * 8d;
        const double representativeThicknessUnity = 0.1d;
        const double concentration = 1d;
        const double hydrogenPerReferenceTick = 0.02d;
        int cells5 = 10 * (1 << 10) + 2, cells6 = 10 * (1 << 12) + 2;
        double area5 = GeodesicPhysicalScale.AreaSquareKilometres(oceanAreaUnity2 / cells5);
        double area6 = GeodesicPhysicalScale.AreaSquareKilometres(oceanAreaUnity2 / cells6);
        double volume5 = area5 * GeodesicPhysicalScale.LengthKilometres(representativeThicknessUnity);
        double volume6 = area6 * GeodesicPhysicalScale.LengthKilometres(representativeThicknessUnity);
        double reactions5 = concentration * volume5 / hydrogenPerReferenceTick;
        double reactions6 = concentration * volume6 / hydrogenPerReferenceTick;
        TestContext.WriteLine($"subdivision5/6 representativePhysicalAreaKm2={area5:G9}/{area6:G9}; representativePhysicalLayerVolumeKm3={volume5:G9}/{volume6:G9}; sameConcentrationInventory={volume5:G9}/{volume6:G9}; hydrogenotrophyReferenceTickReactions={reactions5:G9}/{reactions6:G9}; ratio={reactions5 / reactions6:G9}");
        Assert.That(volume5 / volume6, Is.EqualTo((double)cells6 / cells5).Within(1e-12d));
        Assert.That(reactions5 / reactions6, Is.GreaterThan(3.99d).And.LessThan(4.01d));
    }
}
