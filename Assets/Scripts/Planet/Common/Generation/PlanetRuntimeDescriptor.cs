using System;

[Serializable]
public struct PlanetRuntimeDescriptor
{
    public PlanetGridType GridType;
    public int MasterSeed;
    public int GenerationVersion;
    public float BaseRadius;
    public float MinimumGeneratedRadius;
    public float MaximumGeneratedRadius;
    public int CellCount;
    public int CubeSphereResolution;
    public int GeodesicSubdivision;
}

public interface IPlanetSurfaceGeometry
{
    float BasePlanetRadius { get; }
    float MinimumSurfaceRadius { get; }
    float MaximumSurfaceRadius { get; }
    float GetTerrainHeightAtDirection(UnityEngine.Vector3 direction);
    float GetSurfaceRadiusAtDirection(UnityEngine.Vector3 direction);
}

public interface IPlanetGridTopology
{
    int CellCount { get; }
    int DirectionToCell(UnityEngine.Vector3 direction);
    UnityEngine.Vector3 GetCellDirection(int cellIndex);
    int GetNeighborCount(int cellIndex);
    int GetNeighbor(int cellIndex, int neighborSlot);
    float GetCellArea(int cellIndex);
}

public enum PlanetSeedDomain
{
    Terrain = 1,
    SurfaceVisuals = 2,
    Vents = 3,
    Climate = 4,
    Resources = 5,
    Biology = 6,
    Ocean = 7,
    Bathymetry = 8
}

public static class PlanetSeedUtility
{
    public static int DeriveSeed(int masterSeed, PlanetSeedDomain domain, int generationVersion)
    {
        unchecked
        {
            uint hash = 2166136261u;
            Mix(ref hash, (uint)masterSeed);
            Mix(ref hash, (uint)domain);
            Mix(ref hash, (uint)generationVersion);
            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (int)(hash & 0x7fffffffu);
        }
    }

    private static void Mix(ref uint hash, uint value)
    {
        hash ^= value & 0xffu; hash *= 16777619u;
        hash ^= (value >> 8) & 0xffu; hash *= 16777619u;
        hash ^= (value >> 16) & 0xffu; hash *= 16777619u;
        hash ^= (value >> 24) & 0xffu; hash *= 16777619u;
    }
}
