using System.Collections.Generic;
using UnityEngine;

public static class GeodesicTopologyCache
{
    private static readonly Dictionary<int, GeodesicGridTopology> Cache = new Dictionary<int, GeodesicGridTopology>();

    public static GeodesicGridTopology GetOrBuild(int subdivision, out bool cacheHit)
    {
        subdivision = Mathf.Clamp(subdivision, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        cacheHit = Cache.TryGetValue(subdivision, out GeodesicGridTopology topology);
        if (!cacheHit)
        {
            topology = GeodesicGridTopology.Build(subdivision);
            Cache[subdivision] = topology;
        }
        return topology;
    }

    public static void Clear() => Cache.Clear();

    public static int CachedSubdivisionCount => Cache.Count;
}
