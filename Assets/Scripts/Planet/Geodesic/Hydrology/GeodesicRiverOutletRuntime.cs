using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicRiverSystem
{
    internal GeodesicRiverOutletAudit OutletAudit { get; private set; }
    private GeodesicHydrologyTopology shorelineTopology;

    private void BeginOutletAudit()
    {
        if (!planet.enableGeodesicRivers) return;
        var delivery = DrainageDelivery;
        if (delivery == null && planet.RiverShoreline != null)
            delivery = GeodesicDrainageDelivery.Analyze(TinDrainage, routingLakeBasins ?? TinLakeBasins,
                TinLakeRouting?.Lakes, TinLakeRouting?.RiverReaches,
                oceanComponentAt: deliveryOceanComponentByVertex != null ? DeliveryOceanComponent : (Func<TinPoint, int>)null,
                primaryOceanComponent: planet.OceanConnectivity?.PrimaryRetainedComponent ?? -1);
        if (delivery != null) OutletAudit = new GeodesicRiverOutletAudit(delivery);
    }

    private void FitOceanOutlet(string route, TinPoint logicalCoast, ref Vector3[] path, ref double[] discharge)
    {
        var shoreline = planet.RiverShoreline;
        if (shoreline == null || !planet.enableOcean) return;
        int component = deliveryOceanComponentByVertex != null ? DeliveryOceanComponent(logicalCoast) : -1;
        shorelineTopology ??= corridorTopology ?? carvingTopology ??
            GeodesicHydrologyTopology.Build(IcosphereRenderGeometryCache.GetOrBuild(TinDrainage.SubdivisionLevel));
        var surface = corridorTerrain ?? terrain;
        if (!shoreline.TryContact(TinDrainage, shorelineTopology, logicalCoast, component, path, surface,
            out int keep, out var extension, planet.GeodesicCurrentRenderMapping) || keep == 0) return;

        // Put a sample at each crossed terrain facet before projecting the ribbon.
        // A chord between two distant draped points can otherwise cut through land.
        var samples = new List<Vector3>();
        var tail = new Vector3[extension.Length + 1];
        tail[0] = path[keep - 1];
        extension.CopyTo(tail, 1);
        surface.SampleCompletedVisiblePath(tail, samples, new List<double>());
        var fitted = new List<Vector3>(keep + samples.Count);
        for (int i = 0; i < keep; i++) fitted.Add(path[i]);
        foreach (Vector3 sample in samples)
            if ((fitted[fitted.Count - 1] - sample).sqrMagnitude > 1e-14f) fitted.Add(sample);
        Vector3 contact = extension[extension.Length - 1];
        if ((fitted[fitted.Count - 1] - contact).sqrMagnitude > 1e-14f) fitted.Add(contact);
        if (fitted.Count < 2) return;
        if (discharge != null)
        {
            double last = discharge[keep - 1];
            Array.Resize(ref discharge, fitted.Count);
            for (int i = keep; i < discharge.Length; i++) discharge[i] = last;
        }
        path = fitted.ToArray();
        OutletAudit?.FitCoast(route, contact);
    }

    private void CompleteOutletAudit()
    {
        if (OutletAudit == null) return;
        OutletAudit.Complete(TinDrainage, planet.RiverShoreline, RenderRadius, TinLakeGeometry,
            planet.BasePlanetRadius, Mathf.Max(.000001f, surfaceOffset));
        if (logDrainageDelivery) Debug.Log(OutletAudit.Summary, this);
    }

#if UNITY_EDITOR
    internal float OutletOriginalRadius(Vector3 direction) => terrain.Radius(direction);
    internal float OutletRenderRadius(Vector3 direction) => RenderRadius(direction);
    internal bool OutletIsSedimentLake(int basin) => lakeSediment?.Lakes.ContainsKey(basin) == true;
#endif
}
