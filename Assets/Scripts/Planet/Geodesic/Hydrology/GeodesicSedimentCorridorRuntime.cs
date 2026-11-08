using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicRiverSystem
{
    private bool UseSedimentCorridors => planet != null && sourceTerrainMesh != null &&
        planet.generateHydrologicalLakes && integrateTinLakeRivers;

    private GeodesicDrainageDelivery ResolveSedimentCorridors()
    {
        if (!UseSedimentCorridors)
        {
            if (SedimentCorridors != null) DrainageDelivery = null;
            SedimentCorridors = null;
            return null;
        }
        if (SedimentCorridors != null) return null;

        corridorTopology ??= carvingTopology ?? GeodesicHydrologyTopology.Build(IcosphereRenderGeometryCache.GetOrBuild(TinDrainage.SubdivisionLevel));
        if (deliveryOceanComponentByVertex == null)
        {
            var ocean = new bool[TinDrainage.BasinVertexCount];
            for (int v = 0; v < ocean.Length; v++) ocean[v] = TinDrainage.BasinVertexRetained(v);
            deliveryOceanComponentByVertex = LabelOceanComponents(planet, corridorTopology, ocean);
        }
        var oceanComponentAt = deliveryOceanComponentByVertex != null ? DeliveryOceanComponent : (Func<TinPoint, int>)null;
        int primary = planet.OceanConnectivity?.PrimaryRetainedComponent ?? -1;
        if (oceanComponentAt == null || primary < 0)
        {
            Debug.Log("[GeodesicTinSedimentCorridors] skipped=primary ocean is unclassified", this);
            return null;
        }
        bool replayed = TinDrainage.ParcelDestinations == null;
        TinDrainage.EnsureParcelDestinations();
        try
        {
            GeodesicDrainageDelivery Analyze(IReadOnlyDictionary<int, GeodesicTinSedimentCorridors.Connection> connections = null) =>
                GeodesicDrainageDelivery.Analyze(TinDrainage, routingLakeBasins, TinLakeRouting?.Lakes, TinLakeRouting?.RiverReaches,
                    oceanComponentAt: oceanComponentAt, primaryOceanComponent: primary, corridors: connections);

            var baseline = Analyze();
            var plan = GeodesicTinSedimentCorridors.Build(TinDrainage, corridorTopology, routingLakeBasins,
                routingLakeGeometry, TinLakeRouting, baseline, oceanComponentAt, primary, planet.BasePlanetRadius, ResolvedDrainageAreaThreshold * 2);
            var delivery = plan.Connections.Count == 0 ? baseline : plan.Verify(Analyze(plan.Connections));
            SedimentCorridors = plan;
            foreach (string decision in plan.Decisions) Debug.Log("[GeodesicTinSedimentCorridor] " + decision, this);
            return delivery;
        }
        finally { if (replayed && !collectDelivery) TinDrainage.ReleaseParcelDestinations(); }
    }

    private Mesh BuildCorridorSurface()
    {
        if (SedimentCorridors == null || SedimentCorridors.Elevations.Count == 0) return null;
        var positions = sourceTerrainMesh.vertices;
        foreach (var edit in SedimentCorridors.Elevations) positions[edit.Key] = corridorTopology.CellDirections[edit.Key] * edit.Value;
        corridorTerrain = new GeodesicRiverTerrain(IcosphereRenderGeometryCache.GetOrBuild(TinDrainage.SubdivisionLevel), positions, routingHeights, true);
        Mesh surface = Instantiate(sourceTerrainMesh);
        surface.name = "Geodesic Terrain with Sediment Corridors";
        surface.vertices = positions;
        surface.RecalculateNormals(); surface.RecalculateBounds();
        return surface;
    }

    private float RenderRadius(Vector3 direction) => (corridorTerrain ?? terrain).Radius(direction);

    private void DrawSedimentCorridors(bool[] rendered, HashSet<int> renderedLakeOutlets,
        List<Vector3> vertices, List<int> indices, List<Color> colors)
    {
        if (SedimentCorridors == null) return;
        int gaps = 0;
        foreach (var corridor in SedimentCorridors.Connections.Values)
        {
            foreach (int id in corridor.InletNetworks)
                if (!rendered[id]) Gap(corridor.BasinId, $"incoming river {id} did not render");
            foreach (int id in corridor.InletLakes)
                if (!renderedLakeOutlets.Contains(id)) Gap(corridor.BasinId, $"incoming lake outlet {id} did not render");
            if (corridor.TargetNetwork >= 0 && !rendered[corridor.TargetNetwork])
                Gap(corridor.BasinId, $"receiver river {corridor.TargetNetwork} did not render");

            var path = new List<Vector3> { corridorTopology.CellDirections[corridor.Vertices[0]] };
            for (int i = 1; i < corridor.Vertices.Length; i++) AddSegment(corridorTopology.CellDirections[corridor.Vertices[i]]);
            if (corridor.Coast.HasValue) AddSegment(TinDrainage.Direction(corridor.Coast.Value));
            float seaLevel = TinDrainage.BasinSeaLevel;
            Vector3[] samples = path.ToArray();
            int firstVertex = vertices.Count;
            if (drawRawTrianglePaths) AddRawTinLine(samples, vertices, indices, colors, minimumRadius: seaLevel);
            else AddTinRibbon(samples, 0, vertices, indices, colors, Array.ConvertAll(samples, _ => corridor.Runoff),
                shorelineEnd: corridor.Coast.HasValue, spillStart: true, minimumRadius: seaLevel);
            RecordRiverColour("corridor:" + corridor.BasinId, firstVertex, vertices.Count);
            visibleReaches++;

            void AddSegment(Vector3 next)
            {
                Vector3 last = path[path.Count - 1];
                for (int i = 1; i < 3; i++) path.Add(Vector3.Lerp(last, next, i / 3f).normalized);
                path.Add(next);
            }
        }
        Debug.Log(SedimentCorridors.Summary(DrainageDelivery, gaps), this);

        void Gap(int basin, string reason)
        {
            gaps++;
            Debug.LogWarning($"[GeodesicTinSedimentCorridor] basin={basin} unsupportedCrossing={reason}", this);
        }
    }
}
