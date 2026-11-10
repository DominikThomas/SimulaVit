/// <summary>Stable identifiers; presentation copy belongs to the loading UI.</summary>
public enum PlanetGenerationStage
{
    BuildingPlanet = 0,
    GrowingMountains = 1,
    ShapingSeafloor = 2,
    FindingOceans = 3,
    BulgingContinents = 4,
    MixingOceans = 5,
    PreparingSurface = 6,
    RoutingRivers = 7,
    CarvingValleys = 8,
    FillingLakes = 9,
    RoutingLakes = 10,
    BuildingRiverRibbons = 11,
    PreparingAtmosphere = 12,
    PreparingPrimordialWorld = 13,
    AlmostReady = 14,
    CheckingSavedPlanet = 15,
    LoadingSavedPlanet = 16,
    RestoringSavedPlanet = 17,
    SavingPlanetSnapshot = 18,
    LoadingSavedHydrology = 19,
    RestoringSavedHydrology = 20,
    InitializingPlanetEnvironment = 21
}

/// <summary>
/// A boundary BEFORE a synchronous unit of work. Enumerate immediately for synchronous
/// callers, or display the report and yield a frame before advancing for interactive startup.
/// No generation method depends on a UI, a coroutine scheduler, or presentation strings.
/// </summary>
public readonly struct PlanetGenerationProgress
{
    public PlanetGenerationStage Stage { get; }
    public int Pass { get; }
    public int PassCount { get; }

    public PlanetGenerationProgress(PlanetGenerationStage stage, int pass = 0, int passCount = 0)
    {
        Stage = stage;
        Pass = pass;
        PassCount = passCount;
    }
}
