using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class GeodesicValleyCarving
{
    public const float DefaultMajorOutletDepth = .01f;
    public const float DefaultMajorOutletVolume = .000002f;
    public const float DefaultMajorOutletDegrees = 20f;
    private const int MaximumMajorAttempts = 12;
    private const int MaximumMajorTargetProbes = 512;
    private const int MaximumMajorProfiles = 24;

    internal enum MajorOutletSearchStop { BeforeSearch, NoCoarseCorridor, FrontierExhausted, LabelLimit, TargetProbeLimit, ProfileLimit }

    internal sealed class MajorOutletSearchEvidence
    {
        internal MajorOutletSearchStop Stop;
        internal int AdmittedLabels, ExpandedLabels, DiscardedLabels, PeakFrontier, QueuedLabelsAtStop;
        internal int TargetProbes, CachedProbes, ReceiverHits, Profiles, FeasibleProfiles;
        internal int DepthPrunes, VolumePrunes, LengthPrunes, LakePrunes, ShorePrunes;
        internal int CoarseExpansions, CorridorsFound, CorridorsRefined, DominatedLabels, CyclePrunes, RemainingLengthPrunes, ReceiverBedPrunes;
        internal int LabelLimitedCorridors, ProbeLimitedCorridors, ProfileLimitedCorridors;
        internal int ReceiverAtSource, ReceiverBedClipped, ReceiverOutsideBand, ReceiverProtected,
            ReceiverNoBoundary, ReceiverWrongOcean, ReceiverGrade, ReceiverAboveSource, CorridorsDisconnected;
        internal int[] BestCoarseChain = Array.Empty<int>(), CandidateCoarseChain = Array.Empty<int>();
        internal int ClosestHintVertex = -1, ClosestHintHops = -1;
        internal int[] ExpandedVertices = Array.Empty<int>(), ExploredBasins = Array.Empty<int>(), FrontierVertices = Array.Empty<int>();
        internal int[] CandidateVertices = Array.Empty<int>(), HintPath = Array.Empty<int>();
        internal TinPoint[] ReceiverPath = Array.Empty<TinPoint>();
        internal string CandidateProfileOutcome, CoarseEstimates, ClosestHintEligibility;

        internal string Summary() =>
            $"stop={Stop} admittedLabels={AdmittedLabels}/{MaximumLabels} expandedLabels={ExpandedLabels} expandedVertices={ExpandedVertices.Length} exploredBasins={ExploredBasins.Length} retiredQueuedLabels={DiscardedLabels} peakQueuedLabels={PeakFrontier} queuedLabelsAtStop={QueuedLabelsAtStop} liveFrontierVertices={FrontierVertices.Length} " +
            $"uncachedTargetProbes={TargetProbes}/{MaximumMajorTargetProbes} cachedTargetProbes={CachedProbes} receiverHits={ReceiverHits} profiles={Profiles}/{MaximumMajorProfiles} feasibleProfiles={FeasibleProfiles} " +
            $"prunedEdges(depth/volume/length/lake/shore)={DepthPrunes}/{VolumePrunes}/{LengthPrunes}/{LakePrunes}/{ShorePrunes} closestReceiverHintVertex={ClosestHintVertex} closestReceiverHintHops={ClosestHintHops} closestReceiverHintEligibility={ClosestHintEligibility ?? "none"} candidateProfile={CandidateProfileOutcome ?? "none"} " +
            $"coarseExpanded={CoarseExpansions} corridors={CorridorsRefined}/{CorridorsFound} disconnectedCorridors={CorridorsDisconnected} limitedCorridors(labels/probes/profiles)={LabelLimitedCorridors}/{ProbeLimitedCorridors}/{ProfileLimitedCorridors} bestCoarseChain=[{string.Join(",", BestCoarseChain)}] candidateCoarseChain=[{string.Join(",", CandidateCoarseChain)}] " +
            $"prunedLabels(dominated/cyclic/remaining-length/receiver-bed)={DominatedLabels}/{CyclePrunes}/{RemainingLengthPrunes}/{ReceiverBedPrunes} " +
            $"receiverEligibility(source/above-source/entry-cut/outside-exit-band/protected/no-retained-boundary/wrong-ocean/grade)={ReceiverAtSource}/{ReceiverAboveSource}/{ReceiverBedClipped}/{ReceiverOutsideBand}/{ReceiverProtected}/{ReceiverNoBoundary}/{ReceiverWrongOcean}/{ReceiverGrade}";
    }

    public sealed class MajorOutletAttempt
    {
        public int BasinId, TargetVertex = -1, TargetFacet = -1;
        public int[] NetworkIds, SourceFacets, Parcels;
        public int[] CrossedBasins = Array.Empty<int>();
        public string Destination = "none", Rejection;
        public double Runoff, SpillElevation, RequiredDepth = double.NaN, ActualDepth, Volume, Area, Length;
        internal Candidate Cut;
        internal readonly MajorOutletSearchEvidence Search = new MajorOutletSearchEvidence();
        internal MajorOutletTraceFailure TraceFailure;
        internal int VerificationSourcesChecked;
        public bool Accepted => Rejection == null;
    }

    public sealed class MajorOutletResult
    {
        public float[] Heights;
        public readonly List<MajorOutletAttempt> Attempts = new List<MajorOutletAttempt>();
        public int Eligible, VerificationBuilds;
        public string Skipped;
        internal double MaximumDepth, MaximumVolume, MaximumLength;
        internal double ProposedBatchVolume, CommittedVolume;
        public bool Changed => Attempts.Any(a => a.Accepted);
    }

    internal static int OceanComponentAt(GeodesicTinHydrology tin, int[] components, TinPoint endpoint)
    {
        int component = -1;
        for (int k = 0; k < 3; k++)
        {
            int label = components[tin.BasinVertex(endpoint.Triangle, k)];
            if (label == -2 || (label >= 0 && component >= 0 && label != component)) return -1;
            if (label >= 0) component = label;
        }
        return component;
    }

    internal static bool PrimaryOcean(GeodesicTinHydrology tin, int[] components, int primary, TinParcelDestination end) =>
        primary >= 0 && GeodesicOceanDrainagePlan.ConfirmedOcean(tin, end) &&
        OceanComponentAt(tin, components, end.Point) == primary;

    internal static Dictionary<int, int[]> ClosedNetworkGroups(GeodesicTinHydrology tin, GeodesicTinBasins basins,
        IReadOnlyList<GeodesicOceanDrainagePlan.Catchment> catchments)
    {
        var parcelBasin = new int[tin.TriangleCount];
        Array.Fill(parcelBasin, -1);
        foreach (var c in catchments) foreach (int parcel in c.Parcels) parcelBasin[parcel] = c.Id;
        return Enumerable.Range(0, tin.Networks.Count).Select(id =>
        {
            var n = tin.Networks[id];
            int basin = GeodesicOceanDrainagePlan.Closed(n.Termination) && n.Samples.Length > 0
                ? basins.Find(n.Samples[n.Samples.Length - 1])?.Id ?? -1 : -1;
            int independentBasin = parcelBasin[n.SourceTriangle];
            return (id, basin: independentBasin, direct: basin >= 0 && basin == independentBasin, n.Length);
        }).Where(n => n.basin >= 0).GroupBy(n => n.basin).ToDictionary(g => g.Key, g => g
            .OrderByDescending(n => n.direct).ThenByDescending(n => n.Length).ThenBy(n => n.id).Select(n => n.id).ToArray());
    }

    internal static double MajorOutletVolumeInPlanetUnits(double radius, double fractionOfRadiusCubed) =>
        fractionOfRadiusCubed * Math.Pow(radius, 3);

    internal static MajorOutletResult PlanMajorOutlets(GeodesicTinHydrology tin, GeodesicHydrologyTopology graph,
        bool[] ocean, int[] components, int primary, GeodesicTinBasins basins, GeodesicTinLakeGeometry lakes,
        double radius, double seaLevel, float maxDepth, double maxVolume, double maxDegrees,
        double minimumRunoff, float width, Vector3[] completedSurface, float microBumpFraction,
        double lakeAreaFraction, double lakeDepth)
    {
        var result = new MajorOutletResult {
            Heights = (float[])tin.VisibleHeight.Clone(), MaximumDepth = maxDepth, MaximumVolume = maxVolume,
            MaximumLength = radius * maxDegrees * Math.PI / 180d
        };
        if (completedSurface == null || completedSurface.Length != tin.BasinVertexCount || tin.ParcelDestinations == null)
        { result.Skipped = "missing-completed-surface-or-parcels"; return result; }
        if (components == null || primary < 0 || !components.Contains(primary))
        { result.Skipped = "primary-ocean-unclassified"; return result; }
        if (maxDepth <= 0 || maxVolume <= 0 || maxDegrees <= 0)
        { result.Skipped = "zero-excavation-or-distance-budget"; return result; }
        var carving = new GeodesicValleyCarving(tin, graph, ocean, basins, lakes, null,
            radius, seaLevel, maxDepth, width, completedSurface, microBumpFraction);
        var planner = new MajorOutletPlanner(carving, components, primary, maxVolume, result.MaximumLength, lakes);
        planner.Build(result, minimumRunoff, lakes, lakeAreaFraction, lakeDepth);
        return result;
    }

    internal enum MajorReceiverFailure { None, Protected, NoRetainedBoundary, WrongOcean, Grade }

    internal static MajorReceiverFailure CheckMajorReceiver(GeodesicTinHydrology tin, TinPoint[] path,
        TinTermination termination, int[] components, int primary, HashSet<int> protectedFacets)
    {
        if (path.Length == 0) return MajorReceiverFailure.NoRetainedBoundary;
        if (path.Any(p => protectedFacets.Contains(p.Triangle))) return MajorReceiverFailure.Protected;
        var end = new TinParcelDestination(path[path.Length - 1], termination);
        if (!GeodesicOceanDrainagePlan.ConfirmedOcean(tin, end)) return MajorReceiverFailure.NoRetainedBoundary;
        if (!PrimaryOcean(tin, components, primary, end)) return MajorReceiverFailure.WrongOcean;
        return tin.VerifyExposedOutlet(path) ? MajorReceiverFailure.None : MajorReceiverFailure.Grade;
    }

    internal static bool MajorOutletPrefixDominates(Label a, Label b)
    {
        if (a.Length > b.Length || a.Cost > b.Cost || a.MaxCut > b.MaxCut || a.Floor < b.Floor) return false;
        // Distinct centrelines can produce different banks and TIN flow even with the
        // same resource vector. Dominate only geometry-equivalent prefixes.
        for (; a != null && b != null; a = a.Previous, b = b.Previous)
        {
            if (a.Vertex != b.Vertex || a.Floor != b.Floor) return false;
            if (ReferenceEquals(a, b)) return true;
        }
        return a == null && b == null;
    }

    private sealed partial class MajorOutletPlanner
    {
        private readonly GeodesicValleyCarving carving;
        private readonly GeodesicTinHydrology tin;
        private readonly int[] components;
        private readonly int primary;
        private readonly double maxVolume, lengthLimit;
        private readonly bool[] shore;
        private readonly int[] receiverHops;
        private readonly bool[] receiverHints;
        private readonly HashSet<int> protectedReceiverFacets;
        private readonly Dictionary<int, (Escape Escape, MajorReceiverFailure Failure)> receivers =
            new Dictionary<int, (Escape, MajorReceiverFailure)>();

        public MajorOutletPlanner(GeodesicValleyCarving carving, int[] components, int primary,
            double maxVolume, double lengthLimit, GeodesicTinLakeGeometry lakes)
        {
            this.carving = carving; tin = carving.tin; this.components = components; this.primary = primary;
            this.maxVolume = maxVolume; this.lengthLimit = lengthLimit;
            shore = new bool[carving.graph.CellCount];
            receiverHints = new bool[carving.graph.CellCount];
            protectedReceiverFacets = lakes == null ? new HashSet<int>() : new HashSet<int>(lakes.TerrainPoints.Select(p => p.Triangle));
            receiverHops = new int[carving.graph.CellCount];
            Array.Fill(receiverHops, -1);
            var queue = new Queue<int>();
            // Freeze whole coastal facets, including the dry corners that define their shoreline.
            for (int t = 0; t < tin.TriangleCount; t++)
            {
                bool coastal = tin.BasinFacetRetained(t);
                bool primaryParcel = PrimaryOcean(tin, components, primary, tin.ParcelDestinations[t]);
                if (!primaryParcel && !coastal) continue;
                for (int k = 0; k < 3; k++)
                {
                    int vertex = tin.BasinVertex(t, k);
                    if (coastal) shore[vertex] = true;
                    if (!primaryParcel && (!carving.ocean[vertex] || components[vertex] != primary)) continue;
                    receiverHints[vertex] = true;
                    if (carving.protectedLake[vertex] || receiverHops[vertex] >= 0) continue;
                    receiverHops[vertex] = 0;
                    queue.Enqueue(vertex);
                }
            }
            // Proximity orders the search. Every target still needs an exact TIN trace.
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                for (int k = 0; k < carving.graph.NeighborCounts[v]; k++)
                {
                    int next = carving.graph.Neighbors6[v * 6 + k];
                    if (receiverHops[next] >= 0 || carving.protectedLake[next]) continue;
                    receiverHops[next] = receiverHops[v] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        public void Build(MajorOutletResult result, double minimumRunoff,
            GeodesicTinLakeGeometry lakes, double lakeAreaFraction, double lakeDepth)
        {
            var catchments = GeodesicOceanDrainagePlan.CaptureCatchments(tin, carving.basins);
            var networks = ClosedNetworkGroups(tin, carving.basins, catchments);
            var eligible = catchments.Where(c => c.Runoff >= minimumRunoff && networks.ContainsKey(c.Id))
                .OrderByDescending(c => c.Runoff).ThenBy(c => c.Id).ToArray();
            result.Eligible = eligible.Length;
            if (eligible.Length == 0) return;
            int[] owners = BuildCoarseGraph(catchments);
            foreach (var c in eligible.Take(MaximumMajorAttempts))
            {
                int[] ids = networks[c.Id];
                var attempt = new MajorOutletAttempt {
                    BasinId = c.Id, Runoff = c.Runoff, NetworkIds = ids,
                    SourceFacets = ids.Select(id => tin.Networks[id].SourceTriangle).Distinct().ToArray(),
                    Parcels = c.Parcels.ToArray(), SpillElevation = c.Basin.HasEscape ? c.Basin.SpillElevation : double.NaN
                };
                Search(c, attempt, owners);
                result.Attempts.Add(attempt);
            }
            Verify(result, lakes, lakeAreaFraction, lakeDepth);
        }

        private void Search(GeodesicOceanDrainagePlan.Catchment source, MajorOutletAttempt attempt, int[] parcelBasin)
        {
            int start = source.Id;
            if (carving.original[start] <= carving.seaFloor + carving.step)
            { attempt.Rejection = "below-dry-channel-floor-would-require-storage"; return; }
            if (carving.protectedLake[start]) { attempt.Rejection = "protected-lake-floor"; return; }

            var evidence = attempt.Search;
            var corridors = FindCorridors(start, source.Runoff, evidence);
            evidence.CorridorsFound = corridors.Count;
            if (corridors.Count == 0)
            {
                evidence.Stop = MajorOutletSearchStop.NoCoarseCorridor;
                attempt.Rejection = oceanSteps.ContainsKey(start) ? "coarse-search-budget" : "no-coarse-primary-corridor";
                return;
            }

            evidence.BestCoarseChain = BasinChain(corridors[0]);
            evidence.CoarseEstimates = string.Join(" | ", corridors.Select(DescribeCorridor));
            evidence.Stop = MajorOutletSearchStop.FrontierExhausted;
            double boundSlack = Math.Max(tin.Epsilon, carving.radius * 1e-12);
            var expanded = new HashSet<int>();
            var queued = new HashSet<int>();
            Label closestHint = null;
            double blockedCut = double.PositiveInfinity, bestScore = double.PositiveInfinity;
            Candidate best = null, rejectedProfile = null;
            double bestVolume = 0, bestArea = 0, bestActualDepth = 0, bestRequiredDepth = 0;
            double rejectedVolume = 0, rejectedArea = 0, rejectedRequiredDepth = 0;
            for (int i = 0; i < corridors.Count; i++)
            {
                var corridor = corridors[i];
                var distance = RefineRegion(corridor, start, evidence, out var minimumReceiverBed);
                if (!distance.ContainsKey(start)) { evidence.CorridorsDisconnected++; continue; }
                evidence.CorridorsRefined++;
                int remainingCorridors = corridors.Count - i;
                int labelLimit = Share(MaximumLabels, evidence.AdmittedLabels, remainingCorridors);
                int probeLimit = Share(MaximumMajorTargetProbes, evidence.TargetProbes, remainingCorridors);
                int profileLimit = Share(MaximumMajorProfiles, evidence.Profiles, remainingCorridors);
                var frontier = new SortedSet<Label>(new LabelOrder());
                var first = new Label { Vertex = start, Floor = carving.original[start], Id = evidence.AdmittedLabels++ };
                var labels = new Dictionary<int, List<Label>> { [start] = new List<Label> { first } };
                frontier.Add(first);
                evidence.PeakFrontier = Math.Max(evidence.PeakFrontier, frontier.Count);
                var stop = MajorOutletSearchStop.FrontierExhausted;
                while (frontier.Count > 0 && stop == MajorOutletSearchStop.FrontierExhausted)
                {
                    Label current = frontier.Min;
                    frontier.Remove(current);
                    int v = current.Vertex;
                    evidence.ExpandedLabels++;
                    expanded.Add(v);
                    if (receiverHops[v] >= 0 && (closestHint == null ||
                        receiverHops[v] < receiverHops[closestHint.Vertex] ||
                        (receiverHops[v] == receiverHops[closestHint.Vertex] && current.Priority < closestHint.Priority)))
                    {
                        closestHint = current;
                        evidence.ClosestHintEligibility = "requires-approach";
                    }
                    if (receiverHints[v] || distance[v] == 0)
                    {
                        if (v == start) { evidence.ReceiverAtSource++; RecordHint(current, "source-vertex"); }
                        else if (carving.original[v] > current.Floor)
                        { evidence.ReceiverBedClipped++; RecordHint(current, "entry-requires-cut"); }
                        else if (distance[v] != 0)
                        { evidence.ReceiverOutsideBand++; RecordHint(current, "outside-selected-exit-band"); }
                        else
                        {
                            if (!receivers.TryGetValue(v, out var probe))
                            {
                                if (evidence.TargetProbes == probeLimit)
                                { RecordHint(current, "probe-limit"); stop = MajorOutletSearchStop.TargetProbeLimit; break; }
                                evidence.TargetProbes++;
                                probe = TraceReceiver(v);
                                receivers.Add(v, probe);
                            }
                            else evidence.CachedProbes++;
                            RecordHint(current, probe.Failure == MajorReceiverFailure.None
                                ? "verified-primary-receiver" : probe.Failure.ToString());
                            switch (probe.Failure)
                            {
                                case MajorReceiverFailure.Protected: evidence.ReceiverProtected++; break;
                                case MajorReceiverFailure.NoRetainedBoundary: evidence.ReceiverNoBoundary++; break;
                                case MajorReceiverFailure.WrongOcean: evidence.ReceiverWrongOcean++; break;
                                case MajorReceiverFailure.Grade: evidence.ReceiverGrade++; break;
                                default:
                                    Evaluate(current, probe.Escape, corridor);
                                    if (evidence.Profiles == profileLimit) stop = MajorOutletSearchStop.ProfileLimit;
                                    continue;
                            }
                        }
                    }
                    if (carving.ocean[v]) continue;
                    for (int k = 0; k < carving.graph.NeighborCounts[v]; k++)
                    {
                        int next = carving.graph.Neighbors6[v * 6 + k];
                        if (carving.protectedLake[next])
                        {
                            evidence.LakePrunes++;
                            if (receiverHints[next]) evidence.ReceiverProtected++;
                            continue;
                        }
                        if (!distance.TryGetValue(next, out int hops)) continue;
                        if (InPrefix(current, next)) { evidence.CyclePrunes++; continue; }
                        double angle = carving.Angle(v, next), length = current.Length + angle * carving.radius;
                        if (length > lengthLimit) { evidence.LengthPrunes++; continue; }
                        if (length + hops * carving.minimumEdgeLength > lengthLimit + boundSlack)
                        { evidence.RemainingLengthPrunes++; continue; }
                        double floor = Math.Min(carving.original[next], current.Floor * Math.Cos(angle) - carving.step);
                        double cut = carving.original[next] - floor;
                        if (cut > 0 && (shore[next] || carving.protectedSubsea[next] || floor < carving.seaFloor))
                        { evidence.ShorePrunes++; continue; }
                        if (cut > carving.depth) { evidence.DepthPrunes++; blockedCut = Math.Min(blockedCut, cut); continue; }
                        double volume = current.Cost + cut * carving.graph.UnitCellAreas[next] * carving.radius * carving.radius;
                        if (volume > maxVolume) { evidence.VolumePrunes++; continue; }
                        if (!minimumReceiverBed.TryGetValue(next, out double requiredBed) || floor + boundSlack < requiredBed)
                        { evidence.ReceiverBedPrunes++; continue; }
                        double peak = Math.Max(current.MaxCut, cut);
                        double score = Score(volume, length, peak);
                        // Progress down the corridor orders work. All physical bounds and
                        // the final candidate score still use the actual length and costs.
                        var label = new Label { Vertex = next, Previous = current, Id = evidence.AdmittedLabels,
                            Floor = floor, Cost = volume, Length = length, MaxCut = peak,
                            Priority = hops + score / (1 + score) };
                        if (!labels.TryGetValue(next, out var list)) labels[next] = list = new List<Label>();
                        if (list.Any(previous => MajorOutletPrefixDominates(previous, label))) { evidence.DominatedLabels++; continue; }
                        if (evidence.AdmittedLabels >= labelLimit)
                        {
                            stop = MajorOutletSearchStop.LabelLimit;
                            break;
                        }
                        for (int l = list.Count - 1; l >= 0; l--)
                        {
                            if (!MajorOutletPrefixDominates(label, list[l])) continue;
                            if (frontier.Remove(list[l])) evidence.DiscardedLabels++;
                            list.RemoveAt(l);
                        }
                        evidence.AdmittedLabels++;
                        list.Add(label);
                        frontier.Add(label);
                        evidence.PeakFrontier = Math.Max(evidence.PeakFrontier, frontier.Count);
                    }
                }
                if (stop == MajorOutletSearchStop.LabelLimit) evidence.LabelLimitedCorridors++;
                if (stop == MajorOutletSearchStop.TargetProbeLimit) evidence.ProbeLimitedCorridors++;
                if (stop == MajorOutletSearchStop.ProfileLimit) evidence.ProfileLimitedCorridors++;
                evidence.QueuedLabelsAtStop += frontier.Count;
                foreach (var label in frontier) queued.Add(label.Vertex);
            }
            if (evidence.LabelLimitedCorridors > 0) evidence.Stop = MajorOutletSearchStop.LabelLimit;
            else if (evidence.ProbeLimitedCorridors > 0) evidence.Stop = MajorOutletSearchStop.TargetProbeLimit;
            else if (evidence.ProfileLimitedCorridors > 0) evidence.Stop = MajorOutletSearchStop.ProfileLimit;
            evidence.FrontierVertices = queued.OrderBy(v => v).ToArray();
            evidence.ExpandedVertices = expanded.OrderBy(v => v).ToArray();
            var exploredBasins = new HashSet<int>();
            foreach (int vertex in expanded)
                for (int i = tin.BasinIncidentStart(vertex); i < tin.BasinIncidentEnd(vertex); i++)
                {
                    int basin = parcelBasin[tin.BasinIncidentTriangle(i)];
                    if (basin >= 0) exploredBasins.Add(basin);
                }
            evidence.ExploredBasins = exploredBasins.OrderBy(b => b).ToArray();
            if (closestHint != null)
            {
                evidence.ClosestHintVertex = closestHint.Vertex;
                evidence.ClosestHintHops = receiverHops[closestHint.Vertex];
                var path = new List<int>();
                for (var label = closestHint; label != null; label = label.Previous) path.Add(label.Vertex);
                path.Reverse();
                evidence.HintPath = path.ToArray();
            }
            var proposal = best ?? rejectedProfile;
            if (proposal != null)
            {
                attempt.TargetVertex = proposal.Vertices[proposal.Vertices.Length - 1];
                var receiver = receivers[attempt.TargetVertex].Escape;
                attempt.TargetFacet = receiver.Path[0].Triangle;
                evidence.ReceiverPath = receiver.Path;
                evidence.CandidateVertices = proposal.Vertices;
                evidence.CandidateProfileOutcome = proposal.Rejection ?? "within-budgets";
                attempt.Destination = ((Target)proposal.TargetKind).ToString();
                attempt.Length = proposal.Length;
                attempt.RequiredDepth = Math.Max(proposal.MaximumCut, best != null ? bestRequiredDepth : rejectedRequiredDepth);
                attempt.CrossedBasins = CrossedBasins(proposal, source.Id);
            }
            if (best == null)
            {
                attempt.Volume = rejectedVolume;
                attempt.Area = rejectedArea;
                if (proposal == null && !double.IsPositiveInfinity(blockedCut)) attempt.RequiredDepth = blockedCut;
                attempt.Rejection = evidence.Stop != MajorOutletSearchStop.FrontierExhausted ? "search-budget" :
                    rejectedProfile?.Rejection ?? (evidence.LakePrunes > 0 ? "protected-lake" :
                    evidence.VolumePrunes > 0 ? "volume-budget" : evidence.DepthPrunes > 0 ? "depth-budget" :
                    evidence.LengthPrunes + evidence.RemainingLengthPrunes > 0 ? "length-budget" :
                    evidence.ShorePrunes > 0 ? "protected-coast-or-subsea" :
                    evidence.ReceiverBedPrunes > 0 ? "receiver-entry-bed-bound" : "no-descending-primary-outlet");
                return;
            }
            attempt.Cut = best; attempt.Volume = bestVolume; attempt.Area = bestArea; attempt.ActualDepth = bestActualDepth;

            int Share(int total, int used, int remaining) => used + (total - used) / remaining;

            void RecordHint(Label current, string reason)
            {
                if (ReferenceEquals(current, closestHint)) evidence.ClosestHintEligibility = reason;
            }

            void Evaluate(Label current, Escape receiver, Corridor corridor)
            {
                evidence.ReceiverHits++;
                var candidate = new Candidate { TargetKind = (int)receiver.Kind };
                var chain = carving.SetPath(candidate, current);
                carving.BuildProfile(candidate, chain, receiver, lengthLimit);
                if (candidate.Surface.Keys.Any(vertex => shore[vertex])) candidate.Rejection = "protected-coast";
                Metrics(candidate.Surface, out double volume, out double area, out double actualDepth);
                if (volume > maxVolume) candidate.Rejection ??= "volume-budget";
                if (actualDepth > carving.depth) candidate.Rejection ??= "depth-budget-after-projection";
                double score = Score(volume, current.Length, actualDepth);
                evidence.Profiles++;
                if (candidate.Accepted)
                {
                    evidence.FeasibleProfiles++;
                    if (score >= bestScore) return;
                    best = candidate;
                    bestScore = score;
                    bestVolume = volume;
                    bestArea = area;
                    bestActualDepth = actualDepth;
                    bestRequiredDepth = current.MaxCut;
                    evidence.CandidateCoarseChain = BasinChain(corridor);
                }
                else if (rejectedProfile == null)
                {
                    rejectedProfile = candidate;
                    rejectedVolume = volume;
                    rejectedArea = area;
                    rejectedRequiredDepth = current.MaxCut;
                    if (best == null) evidence.CandidateCoarseChain = BasinChain(corridor);
                }
            }
        }

        private double Score(double volume, double length, double peak) =>
            volume / maxVolume + .2 * length / lengthLimit + .2 * peak / carving.depth;

        private (Escape, MajorReceiverFailure) TraceReceiver(int vertex)
        {
            if (carving.protectedLake[vertex]) return (null, MajorReceiverFailure.Protected);
            var path = TraceVertex(vertex, out var termination);
            var failure = CheckMajorReceiver(tin, path, termination, components, primary, protectedReceiverFacets);
            if (failure != MajorReceiverFailure.None) return (null, failure);
            bool direct = shore[vertex] && path.All(p => ContainsVertex(p.Triangle, vertex));
            return (new Escape { Kind = direct ? Target.Coastline : Target.OceanDrainage, Path = path }, MajorReceiverFailure.None);
        }

        private static bool InPrefix(Label path, int vertex)
        {
            for (; path != null; path = path.Previous) if (path.Vertex == vertex) return true;
            return false;
        }

        private TinPoint[] TraceVertex(int vertex, out TinTermination termination)
        {
            int t = tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex));
            return tin.TraceLakeOutlet(tin.BasinPoint(t, vertex), null, out termination);
        }

        private int[] CrossedBasins(Candidate cut, int source)
        {
            var crossed = new HashSet<int> { source };
            var order = new List<int>();
            foreach (int vertex in cut.Vertices)
            {
                var path = TraceVertex(vertex, out var termination);
                if (!GeodesicOceanDrainagePlan.Closed(termination) || path.Length == 0) continue;
                var basin = carving.basins.Find(path[path.Length - 1]);
                if (basin != null && crossed.Add(basin.Id)) order.Add(basin.Id);
            }
            return order.ToArray();
        }

        private void Metrics(Dictionary<int, float> edits, out double volume, out double area, out double peak)
        {
            var facets = new HashSet<int>();
            var projected = new Dictionary<int, Vector3>();
            volume = area = peak = 0d;
            foreach (var cut in edits)
            {
                Vector3 vertex = carving.completedSurface[cut.Key].normalized * cut.Value;
                double actual = carving.original[cut.Key] - (double)vertex.magnitude;
                peak = Math.Max(peak, actual);
                projected.Add(cut.Key, vertex);
                carving.AddFacets(cut.Key, facets);
            }
            foreach (int facet in facets)
            {
                int a = tin.BasinVertex(facet, 0), b = tin.BasinVertex(facet, 1), c = tin.BasinVertex(facet, 2);
                Vector3 p = carving.completedSurface[a], q = carving.completedSurface[b], r = carving.completedSurface[c];
                area += Vector3.Cross(q - p, r - p).magnitude * .5d;
                volume += FacetVolume(p, q, r) - FacetVolume(Position(a), Position(b), Position(c));
            }
            Vector3 Position(int v) => projected.TryGetValue(v, out var p) ? p : carving.completedSurface[v];
        }

        // The radial wedge below a triangular facet is a tetrahedron with apex at the planet centre.
        private static double FacetVolume(Vector3 a, Vector3 b, Vector3 c) => Math.Abs(
            a.x * ((double)b.y * c.z - (double)b.z * c.y) +
            a.y * ((double)b.z * c.x - (double)b.x * c.z) +
            a.z * ((double)b.x * c.y - (double)b.y * c.x)) / 6d;

        private void Verify(MajorOutletResult result, GeodesicTinLakeGeometry lakes, double lakeAreaFraction, double lakeDepth)
        {
            var selected = new Dictionary<int, float>();
            var sources = new Dictionary<int, TinPoint>();
            foreach (var attempt in result.Attempts.Where(a => a.Accepted))
            {
                if (attempt.Cut.Surface.Keys.Any(selected.ContainsKey) ||
                    result.Attempts.Any(a => a != attempt && sources.ContainsKey(a.BasinId) &&
                        (a.Cut.Vertices.Any(attempt.Cut.Surface.ContainsKey) || attempt.Cut.Vertices.Any(a.Cut.Surface.ContainsKey))))
                { attempt.Rejection = "overlapping-outlet"; continue; }
                if (result.Attempts.Where(a => sources.ContainsKey(a.BasinId)).Sum(a => a.Volume) + attempt.Volume > maxVolume)
                { attempt.Rejection = "combined-volume-budget"; continue; }
                foreach (var cut in attempt.Cut.Surface) selected.Add(cut.Key, cut.Value);
                int v = attempt.BasinId;
                sources.Add(v, tin.BasinPoint(tin.BasinIncidentTriangle(tin.BasinIncidentStart(v)), v));
            }
            for (int pass = 0; pass < 3; pass++)
            {
                var active = result.Attempts.Where(a => a.Accepted).ToArray();
                Array.Copy(carving.original, result.Heights, result.Heights.Length);
                if (active.Length == 0) return;
                var merged = new Dictionary<int, float>();
                var editedFacets = new HashSet<int>();
                foreach (var a in active)
                    foreach (var cut in a.Cut.Surface)
                    {
                        merged.Add(cut.Key, cut.Value);
                        carving.AddFacets(cut.Key, editedFacets);
                    }
                Metrics(merged, out result.ProposedBatchVolume, out _, out double projectedDepth);
                if (result.ProposedBatchVolume > maxVolume || projectedDepth > carving.depth)
                {
                    foreach (var a in active) a.Rejection = result.ProposedBatchVolume > maxVolume
                        ? "projected-batch-volume-budget" : "projected-batch-depth-budget";
                    return;
                }
                foreach (var cut in merged) result.Heights[cut.Key] = cut.Value;
                var proposed = carving.ProjectProposedTin(result.Heights, requireSeaSphereCrossing: false);
                result.VerificationBuilds++;
                GeodesicTinBasins proposedBasins = null;
                bool removed = false;
                foreach (var a in active)
                {
                    bool connected = Connects(a, sources[a.BasinId], "basin-floor") &&
                        a.SourceFacets.All(t => Connects(a, proposed.Centroid(t), "prior-network"));
                    if (!connected) { a.Rejection = "modified-tin-no-descending-primary-path"; removed = true; }
                }
                if (removed) continue;
                if (lakes != null)
                {
                    var nextBasins = GeodesicTinBasins.Build(proposed, lakeAreaFraction, lakeDepth);
                    var nextLakes = GeodesicTinLakeGeometry.Build(proposed, nextBasins);
                    if (!SameLakes(lakes, nextLakes))
                    { foreach (var a in active) a.Rejection = "lake-geometry-or-ownership-changed"; break; }
                }
                if (!PreservesExistingReceivers(proposed))
                { foreach (var a in active) a.Rejection = "existing-ocean-network-changed-component"; break; }
                result.CommittedVolume = result.ProposedBatchVolume;
                return;

                bool Connects(MajorOutletAttempt attempt, TinPoint source, string role)
                {
                    attempt.VerificationSourcesChecked++;
                    var failure = InspectMajorOutletTrace(tin, proposed, source, components, primary, editedFacets);
                    if (failure == null) return true;
                    failure.SourceRole = role;
                    failure.VerificationPass = pass + 1;
                    failure.PathComparison = CompareOutletPath(tin, proposed, failure.Trace, attempt.Cut.Vertices, merged);
                    if (GeodesicOceanDrainagePlan.Closed(failure.Termination))
                    {
                        proposedBasins ??= GeodesicTinBasins.Build(proposed, 0, double.MaxValue);
                        failure.OriginalPonding = DescribeOutletPonding(tin, carving.basins, sources[attempt.BasinId], components, primary);
                        failure.ProposedPonding = DescribeOutletPonding(proposed, proposedBasins, failure.Terminal, components, primary);
                    }
                    attempt.TraceFailure = failure;
                    return false;
                }
            }
            foreach (var a in result.Attempts.Where(a => a.Accepted)) a.Rejection = "verification-budget";
            Array.Copy(carving.original, result.Heights, result.Heights.Length);
        }

        private bool PreservesExistingReceivers(GeodesicTinHydrology proposed)
        {
            foreach (int source in tin.Networks.Select(n => n.SourceTriangle).Distinct())
            {
                var before = tin.ParcelDestinations[source];
                if (!GeodesicOceanDrainagePlan.ConfirmedOcean(tin, before)) continue;
                var path = proposed.TraceLakeOutlet(proposed.Centroid(source), null, out var termination);
                if (path.Length == 0 || !GeodesicOceanDrainagePlan.ConfirmedOcean(proposed,
                    new TinParcelDestination(path[path.Length - 1], termination)) ||
                    OceanComponentAt(tin, components, before.Point) != OceanComponentAt(proposed, components, path[path.Length - 1]))
                    return false;
            }
            return true;
        }

        private static bool SameLakes(GeodesicTinLakeGeometry a, GeodesicTinLakeGeometry b) =>
            a.Bodies.Select(l => (l.BasinId, l.WaterLevel, l.VertexCount, l.IndexCount))
                .SequenceEqual(b.Bodies.Select(l => (l.BasinId, l.WaterLevel, l.VertexCount, l.IndexCount))) &&
            a.TerrainPoints.SequenceEqual(b.TerrainPoints) && a.Vertices.SequenceEqual(b.Vertices) &&
            a.Triangles.SequenceEqual(b.Triangles);
    }
}
