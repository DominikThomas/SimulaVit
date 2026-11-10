using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using S = GeodesicSnapshotIO;

public sealed partial class GeodesicTinHydrology
{
    // Capture before ribbons annotate diagnostics or reclassify a missing visual trunk.
    internal Action<BinaryWriter> CaptureSnapshot(IReadOnlyCollection<(int Source, TinPoint[] Samples)> rejected)
    {
        var raw = networks.Select(n => new TinRiverNetwork
        {
            SourceTriangle = n.SourceTriangle, JunctionNetwork = n.JunctionNetwork, Termination = n.Termination,
            Samples = n.Samples, Centreline = n.Centreline, Length = n.Length
        }).ToArray();
        var endpoints = ParcelDestinations;
        var d = Diagnostics.SnapshotCopy();
        return w =>
        {
            S.Floats(w, HydrologyHeight);
            w.Write(HasVisibleDisagreement);
            S.Ints(w, TriangleReceiver);
            S.Doubles(w, LocalArea);
            S.Doubles(w, DrainageArea);
            S.Doubles(w, localRunoff);
            S.Doubles(w, AccumulatedRunoff);
            S.Ints(w, routingFailureTriangles);
            S.Write(w, endpoints, (b, end) => { S.Point(b, end.Point); b.Write((byte)end.Termination); });
            S.Write(w, raw, (b, network) =>
            {
                b.Write(network.SourceTriangle); b.Write(network.JunctionNetwork); b.Write((byte)network.Termination);
                b.Write(network.Length);
                S.Write(b, network.Samples, S.Point);
                S.Write(b, network.Centreline, S.Vector);
            });
            w.Write(d.HydrologyTriangles);
            w.Write(d.RiverTriangles);
            w.Write(d.RiverNetworks);
            w.Write(d.Sources);
            w.Write(d.Confluences);
            w.Write(d.OceanTerminations);
            w.Write(d.ClosedBasinTerminations);
            w.Write(d.NoiseDepressionTerminations);
            w.Write(d.FlatTerminations);
            w.Write(d.UnexpectedBrokenNetworks);
            w.Write(d.UnexpectedInlandBreaks);
            w.Write(d.HydrologyDownhillSegments);
            w.Write(d.HydrologyFlatSegments);
            w.Write(d.HydrologyUphillSegments);
            w.Write(d.VisibleDownhillSegments);
            w.Write(d.VisibleMinorBumpSegments);
            w.Write(d.VisibleMaterialUphillSegments);
            w.Write(d.VisibleRidgeCrossings);
            w.Write(d.RejectedHydrologySteps);
            w.Write(d.RejectedVisibleSteps);
            w.Write(d.RejectedPostProcessing);
            w.Write(d.FilterRetries);
            w.Write(d.MeanNetworkLength);
            w.Write(d.MaxNetworkLength);
            w.Write(d.FilterStrength);
            w.Write(d.MeanFilterChange);
            w.Write(d.MaxFilterChange);
            S.Write(w, rejected, (b, reach) => { b.Write(reach.Source); S.Write(b, reach.Samples, S.Point); });
        };
    }

    internal sealed class Restored
    {
        internal GeodesicTinHydrology Tin;
        internal (int Source, TinPoint[] Samples)[] Rejected;
    }

    internal static Restored RestoreSnapshot(BinaryReader r, IcosphereRenderGeometry geometry, Vector3[] positions,
        float[] visible, bool[] ocean, float sea, float radius, double epsilon, double bump)
    {
        var heights = S.Floats(r, geometry.VertexCount);
        // Recreate geometric facet/incident/flat indexes through the normal constructor.
        // The saved parcel accumulation and streamlines are installed without tracing.
        var tin = new GeodesicTinHydrology(geometry.UnitVertices, geometry.Triangles, visible, heights, ocean,
            sea, radius, epsilon, bump, subdivisionLevel: geometry.SubdivisionLevel, completedSurface: positions);
        int triangles = geometry.TriangleCount;
        tin.HasVisibleDisagreement = S.Flag(r);
        S.Require(S.Count(r, triangles, 4) == triangles, "TIN receiver count mismatch.");
        for (int i = 0; i < triangles; i++) tin.TriangleReceiver[i] = S.Id(r, triangles, -1);
        ReadAmounts(tin.LocalArea);
        ReadAmounts(tin.DrainageArea);
        ReadAmounts(tin.localRunoff);
        ReadAmounts(tin.AccumulatedRunoff);
        int failureCount = S.Count(r, triangles, 4);
        for (int i = 0; i < failureCount; i++) tin.routingFailureTriangles.Add(S.Id(r, triangles));
        tin.ParcelDestinations = S.Read(r, triangles, 29, b => new TinParcelDestination(
            S.Point(b, triangles), S.Termination(b)), true);
        tin.CaptureParcelDestinations = true;
        int count = S.Count(r, triangles, 25);
        for (int i = 0; i < count; i++)
            tin.networks.Add(new TinRiverNetwork
            {
                SourceTriangle = S.Id(r, triangles),
                JunctionNetwork = S.Id(r, i, -1),
                Termination = S.Termination(r),
                Length = S.Double(r),
                Samples = S.Read(r, 32769, 28, b => S.Point(b, triangles)),
                Centreline = S.Read(r, 32769, 12, S.Vector)
            });
        var d = tin.Diagnostics;
        d.HydrologyTriangles = r.ReadInt32();
        d.RiverTriangles = r.ReadInt32();
        d.RiverNetworks = r.ReadInt32();
        d.Sources = r.ReadInt32();
        d.Confluences = r.ReadInt32();
        d.OceanTerminations = r.ReadInt32();
        d.ClosedBasinTerminations = r.ReadInt32();
        d.NoiseDepressionTerminations = r.ReadInt32();
        d.FlatTerminations = r.ReadInt32();
        d.UnexpectedBrokenNetworks = r.ReadInt32();
        d.UnexpectedInlandBreaks = r.ReadInt32();
        d.HydrologyDownhillSegments = r.ReadInt32();
        d.HydrologyFlatSegments = r.ReadInt32();
        d.HydrologyUphillSegments = r.ReadInt32();
        d.VisibleDownhillSegments = r.ReadInt32();
        d.VisibleMinorBumpSegments = r.ReadInt32();
        d.VisibleMaterialUphillSegments = r.ReadInt32();
        d.VisibleRidgeCrossings = r.ReadInt32();
        d.RejectedHydrologySteps = r.ReadInt32();
        d.RejectedVisibleSteps = r.ReadInt32();
        d.RejectedPostProcessing = r.ReadInt32();
        d.FilterRetries = r.ReadInt32();
        d.MeanNetworkLength = S.Double(r);
        d.MaxNetworkLength = S.Double(r);
        d.FilterStrength = S.Double(r);
        d.MeanFilterChange = S.Double(r);
        d.MaxFilterChange = S.Double(r);
        S.Require(d.HydrologyTriangles == triangles && d.RiverNetworks == count, "TIN diagnostics do not match the graph.");
        return new Restored
        {
            Tin = tin,
            Rejected = S.Read(r, triangles * 8, 8, b =>
                (S.Id(b, triangles), S.Read(b, 32769, 28, point => S.Point(point, triangles))))
        };

        void ReadAmounts(double[] values)
        {
            S.Require(S.Count(r, triangles, 8) == triangles, "TIN accumulation count mismatch.");
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = S.Double(r);
                S.Require(values[i] >= 0, "TIN areas and runoff must be nonnegative.");
            }
        }
    }
}
