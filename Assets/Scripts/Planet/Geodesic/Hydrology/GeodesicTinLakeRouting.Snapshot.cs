using System.Collections.Generic;
using System.IO;
using System.Linq;
using S = GeodesicSnapshotIO;

public sealed partial class GeodesicTinLakeRouting
{
    private const int MaximumSnapshotMemberships = 6_000_000;

    internal void WriteSnapshot(BinaryWriter w)
    {
        int memberships = rawContributors.Values.Sum(set => set.Count) +
            Lakes.Sum(node => node.DirectParcels.Count + node.ContributingParcels.Count);
        S.Require(memberships <= MaximumSnapshotMemberships, "All-lakes membership exceeds the restore budget.");
        var parcelOwners = new Dictionary<HashSet<int>, int>();
        w.Write(Lakes.Count);
        foreach (var node in Lakes)
        {
            parcelOwners.Add(node.ContributingParcels, node.BasinId);
            w.Write(node.BasinId); w.Write(node.DownstreamLakeId); w.Write(node.OutletReachId);
            S.Write(w, node.OutletPath, S.Point);
            S.OptionalPoint(w, node.CoastContactAtSpill);
            w.Write((byte)node.OutletTermination); w.Write(node.JoinNetwork);
            S.OptionalPoint(w, node.DownstreamSample);
            S.OptionalPoint(w, node.DownstreamLakeEntry);
            w.Write(node.ExitsAtSpill);
            S.Ints(w, node.DirectParcels);
            S.Ints(w, node.ContributingParcels);
            S.Doubles(w, node.InletDischarges);
            w.Write(node.Discharge);
            S.Text(w, node.CycleContext);
        }
        w.Write(RiverReaches.Count);
        foreach (var reach in RiverReaches)
        {
            w.Write(reach.RawNetworkId); w.Write(reach.SourceTriangle);
            w.Write(reach.ReceivingLakeId); w.Write(reach.JunctionNetwork);
            S.Write(w, reach.Samples, S.Point);
            w.Write(reach.Changed); w.Write(reach.InletDischarge); w.Write((byte)reach.Termination);
        }
        S.Write(w, rawContributors, (b, pair) => { b.Write(pair.Key); S.Ints(b, pair.Value); });
        S.Write(w, addedToNetwork, (b, pair) =>
        {
            b.Write(pair.Key);
            S.Write(b, pair.Value, (writer, item) =>
            { writer.Write(item.Start); writer.Write(parcelOwners[item.Parcels]); });
        });
        w.Write(ConservationInput); w.Write(ConservationOutput);
    }

    internal static GeodesicTinLakeRouting RestoreSnapshot(BinaryReader r, GeodesicTinHydrology tin,
        GeodesicTinBasins basins, GeodesicTinLakeGeometry geometry)
    {
        var result = new GeodesicTinLakeRouting(tin, basins, geometry);
        int triangles = tin.TriangleCount, vertices = tin.BasinVertexCount, networks = tin.Networks.Count;
        int membershipBudget = MaximumSnapshotMemberships;
        S.Require(S.Count(r, result.Lakes.Count, 50) == result.Lakes.Count, "All-lakes count mismatch.");
        var firstOwners = new bool[triangles];
        foreach (var node in result.Lakes)
        {
            S.Require(S.Id(r, vertices) == node.BasinId, "Unexpected lake order.");
            node.DownstreamLakeId = LakeId(r, true);
            node.OutletReachId = S.Id(r, networks + result.Lakes.Count, -1);
            node.OutletPath = ReadPath(r);
            node.CoastContactAtSpill = S.OptionalPoint(r, triangles);
            node.OutletTermination = S.Termination(r);
            node.JoinNetwork = S.Id(r, networks, -1);
            node.DownstreamSample = S.OptionalPoint(r, triangles);
            node.DownstreamLakeEntry = S.OptionalPoint(r, triangles);
            node.ExitsAtSpill = S.Flag(r);
            ReadMembership(r, node.DirectParcels);
            ReadMembership(r, node.ContributingParcels);
            foreach (int id in node.DirectParcels)
            {
                S.Require(!firstOwners[id] && node.ContributingParcels.Contains(id), "Invalid first-lake parcel ownership.");
                firstOwners[id] = true;
            }
            node.InletDischarges.AddRange(S.Read(r, triangles, 8, S.Double));
            node.Discharge = S.Double(r);
            node.CycleContext = S.Text(r);
            double contribution = node.ContributingParcels.Sum(tin.ParcelRunoff);
            S.Require(node.Discharge >= 0 && System.Math.Abs(contribution - node.Discharge) <= System.Math.Max(1e-10, contribution * 1e-10),
                "Cached lake discharge does not conserve unique parcel contributions.");
        }
        result.RiverReaches = S.Read(r, networks, 30, b => new TinLakeAdjustedReach
        {
            RawNetworkId = S.Id(b, networks), SourceTriangle = S.Id(b, triangles),
            ReceivingLakeId = LakeId(b, true), JunctionNetwork = S.Id(b, networks, -1),
            Samples = ReadPath(b), Changed = S.Flag(b), InletDischarge = S.Double(b), Termination = S.Termination(b)
        }, true);
        for (int i = 0; i < networks; i++)
            S.Require(result.RiverReaches[i].RawNetworkId == i &&
                result.RiverReaches[i].SourceTriangle == tin.Networks[i].SourceTriangle, "Cached reach is bound to the wrong network.");
        int contributors = S.Count(r, triangles, 8);
        for (int i = 0; i < contributors; i++)
        {
            int facet = S.Id(r, triangles);
            var membership = new HashSet<int>();
            ReadMembership(r, membership);
            result.rawContributors.Add(facet, membership);
        }
        int joins = S.Count(r, networks, 8);
        int eventBudget = MaximumSnapshotMemberships;
        for (int i = 0; i < joins; i++)
        {
            int network = S.Id(r, networks);
            var events = S.Read(r, eventBudget, 8, b =>
                (S.Id(b, result.RiverReaches[network].Samples.Length), result.nodes[LakeId(b, false)].ContributingParcels));
            eventBudget -= events.Length;
            result.addedToNetwork.Add(network, new List<(int, HashSet<int>)>(events));
        }
        result.ConservationInput = S.Double(r);
        result.ConservationOutput = S.Double(r);
        result.VerifySnapshotGraph();
        return result;

        int LakeId(BinaryReader b, bool optional)
        {
            int id = S.Id(b, vertices, optional ? -1 : 0);
            S.Require(id == -1 || result.nodes.ContainsKey(id), "Cached edge references a missing lake.");
            return id;
        }

        TinPoint[] ReadPath(BinaryReader b) => S.Read(b, 65536, 28, point => S.Point(point, triangles));

        void ReadMembership(BinaryReader b, HashSet<int> set)
        {
            int count = S.Count(b, System.Math.Min(triangles, membershipBudget), 4);
            membershipBudget -= count;
            for (int i = 0; i < count; i++)
                S.Require(set.Add(S.Id(b, triangles)), "Duplicate parcel in cached membership.");
        }
    }

    private void VerifySnapshotGraph()
    {
        var degree = nodes.Keys.ToDictionary(id => id, id => 0);
        foreach (var node in Lakes)
            if (node.DownstreamLakeId >= 0) degree[node.DownstreamLakeId]++;
        var ready = new Queue<int>(degree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        int processed = 0;
        while (ready.Count > 0)
        {
            int next = nodes[ready.Dequeue()].DownstreamLakeId;
            processed++;
            if (next >= 0 && --degree[next] == 0) ready.Enqueue(next);
        }
        double direct = Lakes.Sum(node => node.DirectParcels.Sum(tin.ParcelRunoff));
        double outflow = Lakes.Where(node => node.DownstreamLakeId < 0).Sum(node => node.Discharge);
        double tolerance = System.Math.Max(1e-9, direct * 1e-9);
        S.Require(processed == Lakes.Count && System.Math.Abs(direct - outflow) <= tolerance &&
            System.Math.Abs(direct - ConservationInput) <= tolerance && System.Math.Abs(outflow - ConservationOutput) <= tolerance,
            "Cached lake graph contains a cycle or does not conserve runoff.");
    }
}
