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
    public static double Inventory(double concentration, double physicalVolumeKm3) => concentration * physicalVolumeKm3;
    public static double Concentration(double inventory, double physicalVolumeKm3) => physicalVolumeKm3 > 0d ? inventory / physicalVolumeKm3 : 0d;
}
