using System.Collections.Generic;

public static class IcosphereRenderGeometryCache
{
    private static readonly Dictionary<int, IcosphereRenderGeometry> Cache = new Dictionary<int, IcosphereRenderGeometry>();

    public static IcosphereRenderGeometry GetOrBuild(int subdivision)
    {
        subdivision = UnityEngine.Mathf.Clamp(subdivision, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        if (!Cache.TryGetValue(subdivision, out IcosphereRenderGeometry geometry))
        {
            geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(subdivision);
            Cache[subdivision] = geometry;
        }
        return geometry;
    }

    public static void Clear() => Cache.Clear();

    public static int CachedSubdivisionCount => Cache.Count;
}
