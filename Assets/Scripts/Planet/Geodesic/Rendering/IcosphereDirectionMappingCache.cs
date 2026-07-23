using System.Collections.Generic;
using UnityEngine;

public static class IcosphereDirectionMappingCache
{
    private readonly struct Key
    {
        private readonly int simulationSubdivision;
        private readonly int targetSubdivision;
        private readonly int version;

        public Key(int simulationSubdivision, int targetSubdivision, int version)
        {
            this.simulationSubdivision = simulationSubdivision;
            this.targetSubdivision = targetSubdivision;
            this.version = version;
        }
    }

    private static readonly Dictionary<Key, IcosphereDirectionMapping> Cache = new Dictionary<Key, IcosphereDirectionMapping>();

    public static IcosphereDirectionMapping GetOrBuild(GeodesicGridTopology simulationTopology, IcosphereRenderGeometry targetGeometry)
    {
        int simulationSubdivision = simulationTopology != null ? simulationTopology.SubdivisionLevel : 0;
        int targetSubdivision = targetGeometry.SubdivisionLevel;
        Key key = new Key(simulationSubdivision, targetSubdivision, IcosphereDirectionMapping.Version);
        if (!Cache.TryGetValue(key, out IcosphereDirectionMapping mapping))
        {
            mapping = IcosphereDirectionMappingBuilder.Build(simulationTopology, targetGeometry);
            Cache[key] = mapping;
        }
        return mapping;
    }

    public static void Clear() => Cache.Clear();

    public static int CachedMappingCount => Cache.Count;
}
