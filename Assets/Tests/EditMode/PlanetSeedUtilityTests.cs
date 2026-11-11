using NUnit.Framework;

public class PlanetSeedUtilityTests
{
    [Test]
    public void DeriveSeed_IsStableForDomainAndGenerationVersion()
    {
        int first = PlanetSeedUtility.DeriveSeed(12345, PlanetSeedDomain.Terrain, PlanetGenerator.GenerationVersion);
        int second = PlanetSeedUtility.DeriveSeed(12345, PlanetSeedDomain.Terrain, PlanetGenerator.GenerationVersion);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void DeriveSeed_SeparatesTerrainAndSurfaceVisualDomains()
    {
        int terrain = PlanetSeedUtility.DeriveSeed(12345, PlanetSeedDomain.Terrain, PlanetGenerator.GenerationVersion);
        int visuals = PlanetSeedUtility.DeriveSeed(12345, PlanetSeedDomain.SurfaceVisuals, PlanetGenerator.GenerationVersion);

        Assert.That(visuals, Is.Not.EqualTo(terrain));
    }
}
