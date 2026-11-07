using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed partial class GeodesicRiverSystem
{
    public GeodesicValleyCarving.MajorOutletResult MajorOutletBreaching { get; private set; }
    internal IReadOnlyList<InlandNetworkRank> RankedInlandNetworks { get; private set; } = Array.Empty<InlandNetworkRank>();

    internal sealed class InlandNetworkRank
    {
        public int Basin, Network;
        public double Runoff;
    }

    private static int[] LabelOceanComponents(PlanetGenerator owner, GeodesicHydrologyTopology graph, bool[] ocean)
    {
        var mapping = owner.GeodesicCurrentRenderMapping;
        return mapping != null && mapping.SampleCount == ocean.Length && owner.OceanConnectivity != null
            ? GeodesicVisibleHydrology.LabelRetainedComponents(graph, ocean, mapping, owner.OceanConnectivity) : null;
    }

    private void CaptureRankedInlandNetworks()
    {
        if (TinDrainage.ParcelDestinations == null) return;
        var catchments = GeodesicOceanDrainagePlan.CaptureCatchments(TinDrainage, TinLakeBasins);
        var networks = GeodesicValleyCarving.ClosedNetworkGroups(TinDrainage, TinLakeBasins, catchments);
        RankedInlandNetworks = catchments.Where(c => networks.ContainsKey(c.Id))
            .OrderByDescending(c => c.Runoff).ThenBy(c => c.Id)
            .Select(c => new InlandNetworkRank { Basin = c.Id, Network = networks[c.Id][0], Runoff = c.Runoff }).ToArray();
    }

    private void LogMajorOutlets(GeodesicValleyCarving.MajorOutletResult result, string preMajor,
        int[] components, int primary, bool[] ocean, double volumeFraction)
    {
        var tin = TinDrainage;
        var finalIds = tin.Networks.Select((n, id) => (n.SourceTriangle, id)).ToLookup(n => n.SourceTriangle, n => n.id);
        Debug.Log($"[GeodesicMajorOutlets] strategy=basin-spill-corridors eligible={result.Eligible} attempted={result.Attempts.Count} " +
            $"accepted={result.Attempts.Count(a => a.Accepted)} verificationBuilds={result.VerificationBuilds} " +
            $"skipped={result.Skipped ?? "none"} preMajorTerrainHash={preMajor} " +
            $"postMajorTerrainHash={GeodesicGenerationFingerprint.Heights(tin.VisibleHeight)} " +
            $"retainedOceanMaskHash={GeodesicGenerationFingerprint.Mask(ocean)} primaryComponent={primary}", this);
        Debug.Log(FormattableString.Invariant($"[GeodesicMajorOutletBudget] baseRadius={planet.BasePlanetRadius:R} volumeLimitFractionOfRadiusCubed={volumeFraction:R} volumeLimitPlanetUnitsCubed={result.MaximumVolume:R} depthLimitPlanetUnits={result.MaximumDepth:R} lengthLimitPlanetUnits={result.MaximumLength:R} lastProposedBatchVolumePlanetUnitsCubed={result.ProposedBatchVolume:R} committedVolumePlanetUnitsCubed={result.CommittedVolume:R}"), this);
        foreach (var a in result.Attempts)
        {
            double primaryRunoff = a.Parcels.Where(p => GeodesicValleyCarving.PrimaryOcean(tin, components, primary,
                tin.ParcelDestinations[p])).Sum(tin.ParcelRunoff);
            var connected = a.SourceFacets.Where(t => GeodesicValleyCarving.PrimaryOcean(tin, components, primary,
                tin.ParcelDestinations[t])).ToArray();
            var matched = connected.SelectMany(t => finalIds[t]).Distinct().ToArray();
            int missingRibbons = matched.Count(id => HasMissingExtractedRibbon(id));
            int gapChains = matched.Count(id => HasRibbonJunctionGap(id));
            int unmatched = connected.Count(t => !finalIds.Contains(t));
            Debug.Log(FormattableString.Invariant(
                $"[GeodesicMajorOutlet] basin={a.BasinId} priorNetworks=[{string.Join(",", a.NetworkIds)}] sourceFacets=[{string.Join(",", a.SourceFacets)}] independentRunoff={a.Runoff:R} targetVertex={a.TargetVertex} targetFacet={a.TargetFacet} destination={a.Destination} basinsCrossed={a.CrossedBasins.Length} crossedBasins=[{string.Join(",", a.CrossedBasins)}] spillElevation={a.SpillElevation:R} routeLength={a.Length:R} requiredMaxCut={a.RequiredDepth:R} actualMaxCut={(a.Accepted ? a.ActualDepth : 0d):R} volume={(a.Accepted ? a.Volume : 0d):R} affectedArea={(a.Accepted ? a.Area : 0d):R} candidateVolume={a.Volume:R} candidateArea={a.Area:R} outcome={a.Rejection ?? "accepted-descending-primary-witness"} authoritativePrimaryRunoff={primaryRunoff:R} newlyPrimarySourceFacets=[{string.Join(",", connected)}] finalNetworks=[{string.Join(",", matched)}] missingRibbonChains={missingRibbons} ribbonJunctionGapChains={gapChains} unmatchedSourceFacets={unmatched}"), this);
            Debug.Log($"[GeodesicMajorOutletSearch] basin={a.BasinId} {a.Search.Summary()} verificationSourcesChecked={a.VerificationSourcesChecked}", this);
            if (a.Search.CoarseEstimates != null)
                Debug.Log($"[GeodesicMajorOutletCorridors] basin={a.BasinId} {a.Search.CoarseEstimates}", this);
            foreach (var iteration in a.Iterations)
                Debug.Log($"[GeodesicMajorOutletIteration] basin={a.BasinId} corridor=[{string.Join(",", a.Search.CandidateCoarseChain)}] receiver={a.TargetVertex} {iteration.Summary()}", this);
            if (a.TraceFailure != null)
            {
                Debug.Log($"[GeodesicMajorOutletTraceFailure] basin={a.BasinId} {a.TraceFailure.Summary()}", this);
                Debug.Log($"[GeodesicMajorOutletPath] basin={a.BasinId} {a.TraceFailure.PathComparison?.Summary}", this);
            }
            if (a.OriginalPonding != null || a.TraceFailure?.ProposedPonding != null)
                Debug.Log($"[GeodesicMajorOutletPonding] basin={a.BasinId} original=({a.OriginalPonding}) proposed=({a.TraceFailure?.ProposedPonding})", this);
        }
        if (DrainageDelivery != null)
            Debug.Log(DrainageDelivery.Connectivity(TinDiagnostics.UnexpectedBrokenNetworks), this);
    }

    internal string InspectTinOutletAttempt(int basin, out TinPoint[] path,
        out GeodesicValleyCarving.MajorOutletAttempt attempt)
    {
        attempt = MajorOutletBreaching?.Attempts.FirstOrDefault(a => a.BasinId == basin);
        var rank = RankedInlandNetworks.FirstOrDefault(r => r.Basin == basin);
        path = Array.Empty<TinPoint>();
        string network = rank != null ? InspectTinNetwork(rank.Network, out path)
            : GeodesicTinBasinInspection.SelectedReport(TinLakeBasins, TinDrainage, basin);
        if (attempt == null)
            return "No search attempt recorded for this basin on the live planet.\n" + network;

        var search = attempt.Search;
        string summary = FormattableString.Invariant(
            $"Basin {basin} outcome={attempt.Rejection ?? "accepted"} independentRunoff={attempt.Runoff:R}");
        string candidate = FormattableString.Invariant(
            $"targetVertex={attempt.TargetVertex} targetFacet={attempt.TargetFacet} destination={attempt.Destination} routeLength={attempt.Length:R} requiredDepth={attempt.RequiredDepth:R} candidateVolumePlanetUnitsCubed={attempt.Volume:R} volumeLimitPlanetUnitsCubed={MajorOutletBreaching.MaximumVolume:R} crossedBasins=[{string.Join(",", attempt.CrossedBasins)}]");
        var failure = attempt.TraceFailure;
        return $"{summary}\n{search.Summary()}\n{search.CoarseEstimates}\nexploredBasinIds=[{string.Join(",", search.ExploredBasins)}]\n" +
            $"{candidate}\n{string.Join("\n", attempt.Iterations.Select(i => i.Summary()))}\n{failure?.Summary()}\n{failure?.PathComparison?.Summary}\n" +
            $"Original storage {attempt.OriginalPonding}\nProposed storage {failure?.ProposedPonding}\n" +
            $"{failure?.PathComparison?.Details}\n\n{network}";
    }

    private IEnumerable<int> ExtractedNetworkChain(int id)
    {
        var seen = new HashSet<int>();
        while (id >= 0 && id < TinDrainage.Networks.Count && seen.Add(id))
        {
            yield return id;
            var n = TinDrainage.Networks[id];
            id = n.Termination == TinTermination.Confluence ? n.JunctionNetwork : -1;
        }
    }

    private bool HasMissingExtractedRibbon(int id) => ExtractedNetworkChain(id).Any(network =>
    {
        var n = TinDrainage.Networks[network];
        var lakeReach = TinLakeRouting?.RiverReaches[network];
        return !paths.ContainsKey(n.SourceTriangle) && (lakeReach == null || lakeReach.ReceivingLakeId < 0 || lakeReach.Samples.Length >= 2);
    });

    private bool HasRibbonJunctionGap(int id)
    {
        foreach (int link in ExtractedNetworkChain(id))
        {
            var network = TinDrainage.Networks[link];
            var lakeReach = TinLakeRouting?.RiverReaches[link];
            int junction = lakeReach != null ? lakeReach.JunctionNetwork : network.JunctionNetwork;
            if (junction < 0 || !paths.TryGetValue(network.SourceTriangle, out var branch) ||
                !paths.TryGetValue(TinDrainage.Networks[junction].SourceTriangle, out var trunk)) continue;
            Vector3 join = branch[branch.Length - 1];
            bool touches = false;
            for (int i = 1; i < trunk.Length; i++)
            {
                Vector3 edge = trunk[i] - trunk[i - 1];
                if (edge.sqrMagnitude <= 0) continue;
                float fraction = Mathf.Clamp01(Vector3.Dot(join - trunk[i - 1], edge) / edge.sqrMagnitude);
                if (Vector3.Distance(join, (trunk[i - 1] + edge * fraction).normalized) <= 1e-6f)
                { touches = true; break; }
            }
            if (!touches) return true;
        }
        return false;
    }

    internal string InspectTinNetwork(int id, out TinPoint[] path)
    {
        var tin = TinDrainage;
        var network = tin.Networks[id];
        path = tin.TraceLakeOutlet(tin.Centroid(network.SourceTriangle), null, out var end);
        var terminal = path[path.Length - 1];
        var extractedIds = ExtractedNetworkChain(id).ToArray();
        var extracted = tin.Networks[extractedIds[extractedIds.Length - 1]];
        bool missing = HasMissingExtractedRibbon(id);
        bool primaryReached = false;
        int primary = planet.OceanConnectivity?.PrimaryRetainedComponent ?? -1;
        int[] oceanComponents = null;
        if (primary >= 0 && planet.GeodesicCurrentRenderMapping != null)
        {
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(tin.SubdivisionLevel);
            var graph = GeodesicHydrologyTopology.Build(geometry);
            var ocean = Enumerable.Range(0, tin.BasinVertexCount).Select(tin.BasinVertexRetained).ToArray();
            oceanComponents = LabelOceanComponents(planet, graph, ocean);
            primaryReached = oceanComponents != null && GeodesicValleyCarving.PrimaryOcean(tin, oceanComponents, primary,
                new TinParcelDestination(terminal, end));
        }
        var basin = GeodesicOceanDrainagePlan.Closed(end) ? TinLakeBasins.Find(terminal) : null;
        var rank = RankedInlandNetworks.FirstOrDefault(r => r.Network == id || r.Basin == basin?.Id);
        bool sameTerminal = extracted.Samples.Length > 0 && SameDestination(extracted.Samples[extracted.Samples.Length - 1]);
        var body = TinLakeRouting?.RiverReaches[id];
        bool junctionGap = HasRibbonJunctionGap(id);
        string diagnosis = !sameTerminal ? "extraction-destination-disagreement" : missing ? "missing-ribbon" :
            junctionGap ? "ribbon-junction-gap" : body != null && body.ReceivingLakeId >= 0 ? "lake-entry" :
            GeodesicOceanDrainagePlan.Closed(end) ? "closed-basin" : end.ToString();
        var report = new StringBuilder(FormattableString.Invariant(
            $"[GeodesicMajorNetwork] network={id} sourceFacet={network.SourceTriangle} terminalFacet={terminal.Triangle} terminal={end} extractedTerminal={extracted.Termination} extractedChain=[{string.Join(",", extractedIds)}] independentCatchmentRunoff={rank?.Runoff:R} basin={basin?.Id} diagnosis={diagnosis} ribbonVisible={visualRoot != null && visualRoot.activeInHierarchy} missingRibbon={missing} junctionGap={junctionGap} receivingLake={body?.ReceivingLakeId ?? -1} witnessedPrimaryOcean={primaryReached} oceanClassificationAvailable={oceanComponents != null}"));
        if (basin != null)
        {
            report.AppendLine().Append(GeodesicTinBasinInspection.Describe(tin, basin));
            var byId = TinLakeBasins.Basins.ToDictionary(b => b.Id);
            var seen = new HashSet<int>();
            var chain = new List<string>();
            for (var current = basin; current != null && seen.Add(current.Id);)
            {
                chain.Add(FormattableString.Invariant($"{current.Id}@{current.SpillElevation:R}"));
                if (current.DownstreamTriangle < 0 || current.DownstreamVertex < 0)
                { chain.Add("no-downstream-witness"); break; }
                var witness = tin.BasinPoint(current.DownstreamTriangle, current.DownstreamVertex);
                var trace = tin.TraceLakeOutlet(witness, null, out var spillEnd);
                var point = trace[trace.Length - 1];
                if (GeodesicOceanDrainagePlan.Closed(spillEnd))
                {
                    current = TinLakeBasins.Find(point);
                    if (current == null) chain.Add("unmatched-closed-floor");
                    else if (seen.Contains(current.Id)) chain.Add("cycle");
                }
                else
                {
                    bool primarySpill = oceanComponents != null && GeodesicValleyCarving.PrimaryOcean(tin,
                        oceanComponents, primary, new TinParcelDestination(point, spillEnd)) && tin.VerifyExposedOutlet(trace);
                    chain.Add(spillEnd + (primarySpill ? "(primary-witness)" : ""));
                    break;
                }
            }
            var parents = new List<int>();
            seen.Clear();
            for (var current = basin; current != null && seen.Add(current.Id); byId.TryGetValue(current.ParentBasinId, out current))
                parents.Add(current.Id);
            report.AppendLine().Append("spillDownstreamChain=").Append(string.Join(" -> ", chain))
                .Append(" topologyParents=").Append(string.Join(" -> ", parents));
        }
        report.AppendLine().AppendLine("Downstream TIN samples in flow order (facet, barycentrics, visible radius)");
        foreach (var p in path)
            report.AppendLine(FormattableString.Invariant($"{p.Triangle}\t{p.A:R}\t{p.B:R}\t{p.C:R}\t{tin.CompletedVisibleRadius(p):R}"));
        return report.ToString();

        bool SameDestination(TinPoint extractedEnd)
        {
            if (extracted.Termination != end) return false;
            if (basin != null) return TinLakeBasins.Find(extractedEnd)?.Id == basin.Id;
            if (end == TinTermination.OceanCoast && tin.IsRetainedOceanBoundary(terminal) &&
                tin.IsRetainedOceanBoundary(extractedEnd))
                return oceanComponents == null || GeodesicValleyCarving.OceanComponentAt(tin, oceanComponents, terminal) ==
                    GeodesicValleyCarving.OceanComponentAt(tin, oceanComponents, extractedEnd);
            return Vector3.Distance(tin.Direction(extractedEnd), tin.Direction(terminal)) <= 1e-6f;
        }
    }
}
