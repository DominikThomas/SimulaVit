using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using S = GeodesicSnapshotIO;

public sealed partial class GeodesicDrainageDelivery
{
    // The graph and its classifications are complete. Only observation counters change
    // during presentation, so retain their values at the authority boundary.
    internal GeodesicDrainageDelivery SnapshotCopy() => (GeodesicDrainageDelivery)MemberwiseClone();

    internal void WriteSnapshot(BinaryWriter w)
    {
        w.Write(PrimaryOcean);
        w.Write(SecondaryOcean);
        w.Write(UnattributedOcean);
        w.Write(CorridorOcean);
        w.Write(TotalRunoff);
        w.Write(DirectOcean);
        w.Write(ViaLakesOcean);
        w.Write(InlandClosed);
        w.Write(Unresolved);
        w.Write(TotalArea);
        w.Write(IndependentParcels);
        w.Write(DuplicateMemberships);
        w.Write(InvalidMemberships);
        w.Write(CycleNodes);
        w.Write(LogicalDisconnected);
        w.Write(VisualOnlyBreaks);
        w.Write(UnknownBreaks);
        w.Write(ExtractionAmbiguities);
        w.Write(LakeBoundaryGaps);
        w.Write(ShortUnrenderedLinks);
        w.Write(ThresholdClipped);
        w.Write(CentrelineRejected);
        w.Write(ProjectionRejected);
        w.Write(MissingTrunkRibbons);
        w.Write(InitialUnexpectedBrokenNetworks);
        w.Write(ApproximateMemoryBytes);
        w.Write(lakeTraversalUnavailable);
        S.Doubles(w, runoffByRiverDestination);
        w.Write(confirmedOceanParcels.Length);
        w.Write(confirmedOceanParcels);
        S.Write(w, links, (b, item) =>
        {
            S.Text(b, item.Key); S.Text(b, item.Value.Next);
            WriteOutcome(b, item.Value.End);
            S.OptionalPoint(b, item.Value.Contact);
            b.Write(item.Value.Lake); b.Write(item.Value.Corridor);
        });
        S.Write(w, resolved, (b, item) => { S.Text(b, item.Key); WriteOutcome(b, item.Value); });
        S.Write(w, catchments, (b, item) =>
        {
            S.Text(b, item.Key); WriteOutcome(b, item.Value.Terminal);
            b.Write(item.Value.Runoff); b.Write(item.Value.Area); b.Write(item.Value.Parcels);
            b.Write(item.Value.SourcePaths); b.Write(item.Value.ViaLake);
        });
        S.Write(w, terminalRunoff, (b, item) => { S.Text(b, item.Key); b.Write(item.Value); });
    }

    internal static GeodesicDrainageDelivery RestoreSnapshot(BinaryReader r, GeodesicTinHydrology tin,
        GeodesicTinBasins basins, int oceanComponents, int lakeCount)
    {
        var result = new GeodesicDrainageDelivery(tin, basins)
        {
            PrimaryOcean = S.Double(r),
            SecondaryOcean = S.Double(r),
            UnattributedOcean = S.Double(r),
            CorridorOcean = S.Double(r),
            TotalRunoff = S.Double(r),
            DirectOcean = S.Double(r),
            ViaLakesOcean = S.Double(r),
            InlandClosed = S.Double(r),
            Unresolved = S.Double(r),
            TotalArea = S.Double(r),
            IndependentParcels = r.ReadInt32(),
            DuplicateMemberships = r.ReadInt32(),
            InvalidMemberships = r.ReadInt32(),
            CycleNodes = r.ReadInt32(),
            LogicalDisconnected = r.ReadInt32(),
            VisualOnlyBreaks = r.ReadInt32(),
            UnknownBreaks = r.ReadInt32(),
            ExtractionAmbiguities = r.ReadInt32(),
            LakeBoundaryGaps = r.ReadInt32(),
            ShortUnrenderedLinks = r.ReadInt32(),
            ThresholdClipped = r.ReadInt32(),
            CentrelineRejected = r.ReadInt32(),
            ProjectionRejected = r.ReadInt32(),
            MissingTrunkRibbons = r.ReadInt32(),
            InitialUnexpectedBrokenNetworks = r.ReadInt32(),
            ApproximateMemoryBytes = r.ReadInt64(),
            lakeTraversalUnavailable = S.Flag(r),
            terminalCache = null
        };
        S.Read(r, 4, 8, S.Double, true).CopyTo(result.runoffByRiverDestination, 0);
        int triangles = tin.TriangleCount;
        S.Require(S.Count(r, triangles, 1) == triangles, "Delivery parcel count mismatch.");
        for (int i = 0; i < triangles; i++) result.confirmedOceanParcels[i] = S.IdByte(r, 3);
        var descriptors = basins.Basins.ToDictionary(basin => basin.Id);
        int graphCount = tin.Networks.Count + lakeCount + 40;
        int linkCount = S.Count(r, graphCount, 11);
        for (int i = 0; i < linkCount; i++)
        {
            string id = S.Text(r);
            result.links.Add(id, new Link
            {
                Next = S.Text(r), End = ReadOutcome(r),
                Contact = S.OptionalPoint(r, triangles), Lake = S.Flag(r), Corridor = S.Flag(r)
            });
        }
        S.Require(S.Count(r, graphCount, 9) == linkCount, "Delivery outcomes do not match the graph.");
        for (int i = 0; i < linkCount; i++)
        {
            string id = S.Text(r);
            S.Require(id != null && result.links.ContainsKey(id), "Unknown cached delivery outcome.");
            result.resolved.Add(id, ReadOutcome(r));
        }
        int catchmentCount = S.Count(r, triangles, 26);
        for (int i = 0; i < catchmentCount; i++)
            result.catchments.Add(S.Text(r), new Catchment
            {
                Terminal = ReadOutcome(r), Runoff = S.Double(r), Area = S.Double(r),
                Parcels = S.Id(r, triangles + 1), SourcePaths = S.Id(r, tin.Networks.Count + 1), ViaLake = S.Flag(r)
            });
        int terminalCount = S.Count(r, triangles, 9);
        for (int i = 0; i < terminalCount; i++) result.terminalRunoff.Add(S.Text(r), S.Double(r));
        for (int i = 0; i < result.networkOutcomes.Length; i++)
        {
            S.Require(result.resolved.TryGetValue("network:" + i, out var outcome), "Delivery is missing a raw network.");
            result.networkOutcomes[i] = outcome;
        }
        result.VerifySnapshotGraph();
        return result;

        Outcome ReadOutcome(BinaryReader b)
        {
            var outcome = new Outcome
            {
                Destination = (Destination)S.IdByte(b, 4), TerminalId = S.Text(b), Type = S.Text(b),
                Point = S.OptionalPoint(b, triangles)
            };
            int basinId = S.Id(b, tin.BasinVertexCount, -1);
            if (basinId >= 0)
                S.Require(descriptors.TryGetValue(basinId, out outcome.Basin), "Delivery references an unknown basin.");
            outcome.ViaLake = S.Flag(b); outcome.ViaCorridor = S.Flag(b);
            outcome.OceanComponent = S.Id(b, oceanComponents, -1);
            outcome.TerminalLakeId = S.Id(b, tin.BasinVertexCount, -1);
            outcome.TerminalVertex = S.Id(b, tin.BasinVertexCount, -1);
            S.Require(outcome.TerminalId != null && outcome.Type != null, "Delivery terminal has no identity.");
            return outcome;
        }
    }

    private static void WriteOutcome(BinaryWriter w, Outcome outcome)
    {
        w.Write((byte)outcome.Destination);
        S.Text(w, outcome.TerminalId); S.Text(w, outcome.Type);
        S.OptionalPoint(w, outcome.Point);
        w.Write(outcome.Basin?.Id ?? -1);
        w.Write(outcome.ViaLake); w.Write(outcome.ViaCorridor);
        w.Write(outcome.OceanComponent); w.Write(outcome.TerminalLakeId); w.Write(outcome.TerminalVertex);
    }

    private void VerifySnapshotGraph()
    {
        var verified = new HashSet<string>(StringComparer.Ordinal);
        var chain = new HashSet<string>(StringComparer.Ordinal);
        foreach (string start in links.Keys)
        {
            for (string id = start; id != null && !verified.Contains(id); id = links[id].Next)
                S.Require(links.ContainsKey(id) && chain.Add(id), "Invalid directed link or cycle in cached delivery.");
            verified.UnionWith(chain);
            chain.Clear();
        }
        double total = 0, direct = 0, viaLakes = 0;
        for (int i = 0; i < tin.TriangleCount; i++)
        {
            double runoff = tin.ParcelRunoff(i);
            total += runoff;
            if (confirmedOceanParcels[i] == 1) direct += runoff;
            else if (confirmedOceanParcels[i] == 2) viaLakes += runoff;
        }
        S.Require(CycleNodes == 0 && DuplicateMemberships == 0 && InvalidMemberships == 0 &&
            RiverDestinationAccountingPass && Math.Abs(total - TotalRunoff) <= Tolerance &&
            Math.Abs(direct - DirectOcean) <= Tolerance && Math.Abs(viaLakes - ViaLakesOcean) <= Tolerance &&
            Math.Abs(PrimaryOcean + SecondaryOcean + UnattributedOcean - DirectOcean - ViaLakesOcean) <= Tolerance,
            "Cached ocean delivery does not conserve independent runoff.");
    }
}
