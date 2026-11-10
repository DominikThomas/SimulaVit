using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using S = GeodesicSnapshotIO;

public sealed partial class GeodesicTinSedimentCorridors
{
    internal Action<BinaryWriter> CaptureSnapshot(GeodesicDrainageDelivery delivery, int shorelineHint)
    {
        var baselineRecord = baseline.SnapshotCopy();
        var finalRecord = ReferenceEquals(baseline, delivery) ? null : delivery.SnapshotCopy();
        return w =>
        {
            w.Write(shorelineHint);
            baselineRecord.WriteSnapshot(w);
            w.Write(finalRecord != null);
            finalRecord?.WriteSnapshot(w);
            w.Write(candidates); w.Write(primaryConnections);
            w.Write(area); w.Write(volume); w.Write(runoff);
            S.Text(w, Failure);
            S.Write(w, connections, (b, item) =>
            {
                var edge = item.Value;
                b.Write(edge.BasinId); b.Write(edge.TargetNetwork);
                S.OptionalPoint(b, edge.Coast);
                S.Ints(b, edge.Vertices);
                S.Ints(b, edge.InletNetworks);
                S.Ints(b, edge.InletLakes);
                b.Write(edge.Runoff); b.Write(edge.Length); b.Write(edge.Area); b.Write(edge.Volume);
                b.Write(edge.ShorePath != null);
                if (edge.ShorePath != null) S.Write(b, edge.ShorePath, S.Vector);
            });
            S.Write(w, elevations, (b, item) => { b.Write(item.Key); b.Write(item.Value); });
            S.Write(w, decisions, S.Text);
            S.Write(w, rejected, (b, item) => { b.Write(item.Key); S.Text(b, item.Value); });
            S.Write(w, receiverApproaches, (b, item) => { b.Write(item.Key); b.Write(item.Value); });
        };
    }

    internal sealed class Restored
    {
        internal GeodesicTinSedimentCorridors Plan;
        internal GeodesicDrainageDelivery Delivery;
        internal int ShorelineHint;
    }

    internal static Restored RestoreSnapshot(BinaryReader r, GeodesicTinHydrology tin,
        GeodesicTinBasins basins, GeodesicTinLakeRouting routing, GeodesicOceanConnectivity ocean, int simulationCells)
    {
        int hint = S.Id(r, simulationCells);
        int lakeCount = routing?.Lakes.Count ?? 0;
        var baseline = GeodesicDrainageDelivery.RestoreSnapshot(r, tin, basins, ocean.Components.Length, lakeCount);
        var final = S.Flag(r)
            ? GeodesicDrainageDelivery.RestoreSnapshot(r, tin, basins, ocean.Components.Length, lakeCount) : baseline;
        var plan = new GeodesicTinSedimentCorridors(baseline)
        {
            candidates = r.ReadInt32(), primaryConnections = S.Id(r, MaximumPrimaryConnections + 1),
            area = S.Double(r), volume = S.Double(r), runoff = S.Double(r), Failure = S.Text(r)
        };
        int vertices = tin.BasinVertexCount, triangles = tin.TriangleCount, networks = tin.Networks.Count;
        var lakeIds = routing != null ? new HashSet<int>(routing.Lakes.Select(node => node.BasinId)) : new HashSet<int>();
        int count = S.Count(r, MaximumPrimaryConnections + MaximumSupplementalConnections, 53);
        for (int i = 0; i < count; i++)
        {
            int basin = S.Id(r, vertices), network = S.Id(r, networks, -1);
            var coast = S.OptionalPoint(r, triangles);
            var path = S.Read(r, vertices, 4, b => S.Id(b, vertices));
            var inletNetworks = S.Read(r, networks, 4, b => S.Id(b, networks));
            var inletLakes = S.Read(r, lakeCount, 4, b =>
            {
                int id = S.Id(b, vertices);
                S.Require(lakeIds.Contains(id), "Corridor inlet references a missing lake.");
                return id;
            });
            double runoff = S.Double(r);
            float length = S.Float(r);
            double area = S.Double(r), volume = S.Double(r);
            var shore = S.Flag(r) ? S.Read(r, triangles, 12, S.Vector) : null;
            S.Require(path.Length > 0 && (network >= 0 || coast.HasValue) && (shore == null || shore.Length >= 2) &&
                runoff > 0 && length >= 0 && area >= 0 && volume >= 0, "Invalid cached sediment corridor.");
            plan.connections.Add(basin, new Connection(basin, network, coast, path, inletNetworks, inletLakes,
                runoff, length, area, volume, shore));
        }
        int edits = S.Count(r, vertices, 8);
        for (int i = 0; i < edits; i++) plan.elevations.Add(S.Id(r, vertices), S.Float(r));
        plan.decisions.AddRange(S.Read(r, vertices * 2, 1, S.Text));
        int rejections = S.Count(r, vertices, 5);
        for (int i = 0; i < rejections; i++) plan.rejected.Add(S.Id(r, vertices), S.Text(r));
        int approaches = S.Count(r, vertices, 8);
        for (int i = 0; i < approaches; i++) plan.receiverApproaches.Add(S.Id(r, vertices), S.Float(r));
        S.Require(count >= plan.primaryConnections && plan.candidates >= count &&
            (count > 0 || edits == 0) && (count == 0) == ReferenceEquals(final, baseline) &&
            ReferenceEquals(plan.Verify(final), final), "Cached connections fail the existing delivery verification.");
        return new Restored { Plan = plan, Delivery = final, ShorelineHint = hint };
    }
}
