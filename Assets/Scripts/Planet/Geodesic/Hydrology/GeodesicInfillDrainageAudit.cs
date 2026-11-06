using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Final TIN evidence for comparing manual infill runs. Replays selected
/// paths on the existing TIN without accumulating runoff or changing receivers.</summary>
internal static class GeodesicInfillDrainageAudit
{
    internal static IEnumerable<string> Logs(PlanetGenerator planet, GeodesicTinHydrology tin, GeodesicTinBasins basins,
        GeodesicDrainageDelivery delivery, GeodesicTinLakeRouting lakes, Vector3[] surface, int[] oceanLabels)
    {
        var mapping = planet.GeodesicCurrentRenderMapping;
        var connectivity = planet.OceanConnectivity;
        var changed = planet.GeodesicInfilledRenderVertices;
        var owner = planet.GeodesicInfillComponentByVertex;
        var before = planet.GeodesicPreInfillRenderRadii;
        string OriginalComponent(int vertex)
        {
            if (mapping == null || connectivity == null) return "?";
            int cell = mapping.Samples[vertex].NearestCell;
            return connectivity.ComponentId[cell].ToString();
        }
        int EditComponent(int vertex) => changed != null && owner != null && changed[vertex] ? owner[vertex] : -1;

        // A descriptor floor ID is its render vertex. Exact sorted IDs allow
        // A/B set subtraction, including minima that move along a level surface.
        var floorIds = basins.Basins.Select(b => b.Id).ToArray();
        for (int start = 0; start < floorIds.Length; start += 128)
            yield return $"[GeodesicInfillBasinIds] offset={start} floorIds={string.Join(",", floorIds.Skip(start).Take(128))}";

        if (before != null && planet.hydrologyTerrainMode == HydrologyTerrainMode.Visible &&
            tin.Diagnostics.FilterStrength == 0d && !planet.GeodesicValleyCarvingAttempted)
        {
            int core = 0, collar = 0, beside = 0, away = 0;
            var newMinima = new List<(TinBasinDescriptor Basin, string Location, int[] Components)>();
            foreach (var basin in basins.Basins)
            {
                var neighbors = new HashSet<int>();
                for (int i = tin.BasinIncidentStart(basin.Id); i < tin.BasinIncidentEnd(basin.Id); i++)
                    for (int corner = 0; corner < 3; corner++)
                        neighbors.Add(tin.BasinVertex(tin.BasinIncidentTriangle(i), corner));
                if (!neighbors.Any(v => before[v] < before[basin.Id])) continue;
                int edit = EditComponent(basin.Id);
                var affected = neighbors.Select(EditComponent).Where(id => id >= 0).Distinct().OrderBy(id => id).ToArray();
                string location;
                if (edit >= 0)
                {
                    bool originalCore = OriginalComponent(basin.Id) == edit.ToString();
                    if (originalCore) core++; else collar++;
                    location = originalCore ? "edited-core" : "edited-collar";
                }
                else if (affected.Length > 0) { beside++; location = "next-to-edit"; }
                else { away++; location = "away-from-edit"; }
                newMinima.Add((basin, location, affected));
            }
            yield return $"[GeodesicInfillNewMinima] method=strict-lower-neighbor-before editedCore={core} editedCollar={collar} nextToEdit={beside} awayFromEdit={away}";
            foreach (var cohort in newMinima.GroupBy(x => x.Location))
            {
                var ids = cohort.Select(x => x.Basin.Id).ToArray();
                for (int start = 0; start < ids.Length; start += 128)
                    yield return $"[GeodesicInfillNewMinimaIds] location={cohort.Key} offset={start} floorIds={string.Join(",", ids.Skip(start).Take(128))}";
            }
            foreach (var entry in newMinima.OrderByDescending(x => x.Basin.Area).ThenBy(x => x.Basin.Id).Take(20))
            {
                var b = entry.Basin;
                yield return FormattableString.Invariant($"[GeodesicInfillNewMinimum] floor={b.Id} location={entry.Location} components=[{string.Join(",", entry.Components)}] before={before[b.Id]:R} final={b.FloorElevation:R} spill={b.SpillElevation:R} depth={b.MaximumDepth:R} footprint={b.Area:R} floorPoint={Point(b.Floor)}");
            }
        }

        var shore = new List<Vector3>();
        int mixed = 0, primaryMixed = 0, secondaryMixed = 0, ambiguous = 0;
        int primary = connectivity?.PrimaryRetainedComponent ?? -1;
        for (int f = 0; f < tin.TriangleCount; f++)
        {
            if (!tin.BasinFacetRetained(f)) continue;
            int a = tin.BasinVertex(f, 0), b = tin.BasinVertex(f, 1), c = tin.BasinVertex(f, 2);
            if (tin.BasinVertexRetained(a) && tin.BasinVertexRetained(b) && tin.BasinVertexRetained(c)) continue;
            mixed++; shore.Add(surface[a]); shore.Add(surface[b]); shore.Add(surface[c]);
            int label = -1;
            if (oceanLabels != null)
            {
                label = oceanLabels[a];
                Merge(oceanLabels[b]); Merge(oceanLabels[c]);
            }
            if (label < 0) ambiguous++;
            else if (label == primary) primaryMixed++;
            else secondaryMixed++;

            void Merge(int other)
            {
                if (other == -2 || (other >= 0 && label >= 0 && other != label)) label = -2;
                else if (label == -1) label = other;
            }
        }
        yield return $"[GeodesicInfillShore] mixedFacets={mixed} primaryMixedFacets={primaryMixed} secondaryMixedFacets={secondaryMixed} ambiguousMixedFacets={ambiguous} mixedFacetPositionsHash={GeodesicGenerationFingerprint.Positions(shore.ToArray())} descriptorContacts=[{string.Join(",", basins.Basins.Where(b => b.TouchesRetainedOcean).Select(b => b.Id))}]";

        int rawFailures = 0, legacyMissing = 0, legacyUnretained = 0, emittedLegacy = 0;
        for (int network = 0; network < tin.Networks.Count; network++)
        {
            var raw = tin.Networks[network];
            var end = delivery.NetworkOutcome(network);
            var adjusted = lakes?.RiverReaches[network];
            var replay = tin.ReplayTrace(tin.Centroid(raw.SourceTriangle));
            bool failure = raw.Termination == TinTermination.RoutingFailure;
            bool previousMissing = replay.PreviousCoast == "no-crossing-in-accepted-prefix";
            bool previousUnretained = replay.PreviousCoast == "unretained-crossing";
            if (failure) rawFailures++;
            if (previousMissing) legacyMissing++;
            if (previousUnretained) legacyUnretained++;
            if (!failure && !previousMissing && !previousUnretained) continue;
            if (!failure && emittedLegacy++ >= 20) continue;
            var rejection = replay.Failure;
            string failedStep = rejection.Kind == GeodesicTinHydrology.TraceFailureKind.None ? "-" :
                FormattableString.Invariant($"{Point(rejection.Start)}>{Point(rejection.End)},lowH:{rejection.LowestHydrology:R},lowRadius:{rejection.LowestVisibleRadius:R}");
            var finalPath = adjusted?.Samples ?? raw.Samples;
            yield return $"[GeodesicInfillNetwork] network={network} sourceFacet={raw.SourceTriangle} raw={raw.Termination} adjusted={adjusted?.Termination ?? raw.Termination} resolved={end.Type} lake={adjusted?.ReceivingLakeId ?? -1} join={adjusted?.JunctionNetwork ?? raw.JunctionNetwork} replay={replay.Termination} failure={rejection.Kind} failedStep={failedStep} previousCoast={replay.PreviousCoast ?? "-"} previousEndpoint={(replay.PreviousEndpoint.HasValue ? Point(replay.PreviousEndpoint.Value) : "-")} resolvedPoint={(end.Point.HasValue ? Point(end.Point.Value) : "-")}";
            var relevantFacets = new HashSet<int> { replay.Path[replay.Path.Length - 1].Triangle };
            if (rejection.Kind != GeodesicTinHydrology.TraceFailureKind.None)
            {
                relevantFacets.Add(rejection.Start.Triangle);
                relevantFacets.Add(rejection.End.Triangle);
            }
            if (replay.PreviousEndpoint.HasValue) relevantFacets.Add(replay.PreviousEndpoint.Value.Triangle);
            foreach (int facet in relevantFacets.OrderBy(f => f))
                yield return $"[GeodesicInfillNetworkFacet] network={network} facet={facet} geometry={Facet(facet)}";
            foreach (string line in PathLogs(network, "raw", raw.Samples)) yield return line;
            if (adjusted != null && !ReferenceEquals(finalPath, raw.Samples))
                foreach (string line in PathLogs(network, "lake-adjusted", finalPath)) yield return line;
            if (raw.Termination == TinTermination.Confluence || rejection.Kind != GeodesicTinHydrology.TraceFailureKind.None)
                foreach (string line in PathLogs(network, "replayed", replay.Path)) yield return line;
        }
        yield return $"[GeodesicInfillNetworkSummary] rawFailures={rawFailures} logicalBreakEvents={delivery.LogicalDisconnected} extractionAmbiguities={delivery.ExtractionAmbiguities} previousCoastMissingCrossing={legacyMissing} previousCoastUnretainedCrossing={legacyUnretained} scope=replay-of-current-network-sources";

        foreach (var catchment in delivery.LargestDisconnected)
        {
            var basin = catchment.Terminal.Basin;
            if (basin == null || !basin.Spill.HasValue) continue;
            yield return FormattableString.Invariant($"[GeodesicInfillCatchmentTerrain] basin={basin.Id} runoff={catchment.Runoff:R} floorToSpill={basin.MaximumDepth:R} floor={Point(basin.Floor)} spill={Point(basin.Spill.Value)} downstreamFacet={basin.DownstreamTriangle} downstreamVertex={basin.DownstreamVertex}");
        }

        string Point(TinPoint p)
        {
            int a = tin.BasinVertex(p.Triangle, 0), b = tin.BasinVertex(p.Triangle, 1), c = tin.BasinVertex(p.Triangle, 2);
            var d = tin.Direction(p);
            return FormattableString.Invariant($"{p.Triangle}:{p.A:R}/{p.B:R}/{p.C:R},v:{a}/{b}/{c},orig:{OriginalComponent(a)}/{OriginalComponent(b)}/{OriginalComponent(c)},edit:{EditComponent(a)}/{EditComponent(b)}/{EditComponent(c)},dir:{d.x:R}/{d.y:R}/{d.z:R},h:{tin.Height(p):R},radius:{tin.CompletedVisibleRadius(p):R},retainedFacet:{tin.BasinFacetRetained(p.Triangle)}");
        }

        string Facet(int f)
        {
            return string.Join("|", Enumerable.Range(0, 3).Select(Corner));

            string Corner(int k)
            {
                int v = tin.BasinVertex(f, k);
                Vector3 p = surface[v];
                return FormattableString.Invariant($"{v}:pos:{p.x:R}/{p.y:R}/{p.z:R},h:{tin.HydrologyHeight[v]:R},preInfillRadius:{(before == null ? double.NaN : before[v]):R},retained:{tin.BasinVertexRetained(v)},originalComponent:{OriginalComponent(v)},editComponent:{EditComponent(v)}");
            }
        }

        IEnumerable<string> PathLogs(int network, string kind, TinPoint[] path)
        {
            for (int start = 0; start < path.Length; start += 16)
                yield return $"[GeodesicInfillNetworkPath] network={network} kind={kind} offset={start} points=[{string.Join("|", path.Skip(start).Take(16).Select(Point))}]";
        }
    }
}
