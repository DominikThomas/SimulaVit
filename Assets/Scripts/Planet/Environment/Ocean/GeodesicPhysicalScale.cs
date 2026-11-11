/// <summary>
/// Authoritative physical interpretation of Geodesic simulation geometry.
/// This is simulation data and must never be inferred from a rendering transform.
/// </summary>
public static class GeodesicPhysicalScale
{
    public const double BasePlanetRadiusUnity = 8.0;
    public const double PhysicalKilometresPerUnityUnit = 1000.0;
    public const double PhysicalSquareKilometresPerUnityUnitSquared =
        PhysicalKilometresPerUnityUnit * PhysicalKilometresPerUnityUnit;
    public const double PhysicalCubicKilometresPerUnityUnitCubed =
        PhysicalSquareKilometresPerUnityUnitSquared * PhysicalKilometresPerUnityUnit;
    public const double PhysicalPlanetRadiusKm = BasePlanetRadiusUnity * PhysicalKilometresPerUnityUnit;

    public static double LengthKilometres(double unityLength) => unityLength * PhysicalKilometresPerUnityUnit;
    public static double AreaSquareKilometres(double unityArea) => unityArea * PhysicalSquareKilometresPerUnityUnitSquared;
    public static double VolumeCubicKilometres(double unityVolume) => unityVolume * PhysicalCubicKilometresPerUnityUnitCubed;
    /// <summary>Converts a historical concentration*Unity^3/s authoring rate to concentration*km^3/s.</summary>
    public static double PhysicalInventoryRate(double authoringInventoryRatePerSecond)
    {
        if (!double.IsFinite(authoringInventoryRatePerSecond) || authoringInventoryRatePerSecond <= 0d) return 0d;
        double physical = authoringInventoryRatePerSecond * PhysicalCubicKilometresPerUnityUnitCubed;
        return double.IsFinite(physical) ? physical : double.MaxValue;
    }
    public static double InjectedInventory(double physicalInventoryRatePerSecond, double simulatedSeconds)
    {
        if (physicalInventoryRatePerSecond <= 0d || !double.IsFinite(physicalInventoryRatePerSecond) || simulatedSeconds <= 0d || !double.IsFinite(simulatedSeconds)) return 0d;
        double inventory = physicalInventoryRatePerSecond * simulatedSeconds;
        return double.IsFinite(inventory) ? inventory : double.MaxValue;
    }
    public static double VentConcentrationDelta(double physicalInventoryRatePerSecond, double simulatedSeconds, double physicalVolumeKm3)
        => physicalVolumeKm3 > 0d && double.IsFinite(physicalVolumeKm3)
            ? InjectedInventory(physicalInventoryRatePerSecond, simulatedSeconds) / physicalVolumeKm3
            : 0d;
    public static double Inventory(double concentration, double physicalVolumeKm3) => concentration * physicalVolumeKm3;
    public static double Concentration(double inventory, double physicalVolumeKm3) => physicalVolumeKm3 > 0d ? inventory / physicalVolumeKm3 : 0d;
}
