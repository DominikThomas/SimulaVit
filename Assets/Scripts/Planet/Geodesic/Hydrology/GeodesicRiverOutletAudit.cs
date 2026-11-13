using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using S = GeodesicSnapshotIO;

/// <summary>Checks the emitted river geometry against the directed outlet graph.</summary>
internal sealed class GeodesicRiverOutletAudit
{
    internal enum Status { Contact, Gap, ExtractionAmbiguity, Closed, Unresolved }

    internal sealed class Result
    {
        internal string Route, Receiver, Reason;
        internal Status State;
        internal Vector3? End, Expected, Logical;
        internal float Distance => End.HasValue && Expected.HasValue ? Vector3.Distance(End.Value, Expected.Value) : float.NaN;
    }

    private sealed class Reach
    {
        internal Vector3[] Vertices;
        internal int[] Triangles;
        internal Vector3[] Path;
        internal bool Lines;
        internal int Count => Path?.Length ?? (Lines ? Vertices.Length : Vertices.Length / 3);
        internal Vector3 Center(int i) => Path != null ? Path[i] : Vertices[Lines ? i : i * 3 + 1];
    }

    private readonly Dictionary<string, List<Reach>> geometry = new Dictionary<string, List<Reach>>();
    private readonly Dictionary<string, Vector3> fittedCoasts = new Dictionary<string, Vector3>();
    private readonly GeodesicDrainageDelivery delivery;
    internal readonly Dictionary<string, Result> Results = new Dictionary<string, Result>();
    internal IReadOnlyDictionary<string, GeodesicDrainageDelivery.Link> Links => delivery.OutletLinks;
    internal GeodesicDrainageDelivery.Outcome NetworkOutcome(int network) => delivery.NetworkOutcome(network);
    internal double ClosedRunoff(GeodesicDrainageDelivery.Outcome end) => delivery.DisconnectedCatchments
        .Where(c => c.Terminal.Destination == GeodesicDrainageDelivery.Destination.InlandClosed &&
            (end.Basin != null ? c.Terminal.Basin?.Id == end.Basin.Id : c.Terminal.TerminalId == end.TerminalId))
        .Sum(c => c.Runoff);
    internal string Summary { get; private set; }
    internal long CaptureTicks { get; private set; }
    private bool completedSnapshot;

    internal GeodesicRiverOutletAudit(GeodesicDrainageDelivery delivery) { this.delivery = delivery; }

    internal void Record(string route, List<Vector3> vertices, int first, bool lines)
    {
        if (vertices.Count - first < (lines ? 2 : 6)) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var reach = new Reach { Vertices = new Vector3[vertices.Count - first], Lines = lines };
        vertices.CopyTo(first, reach.Vertices, 0, reach.Vertices.Length);
        Add(route, reach);
        CaptureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    internal void RecordBed(string route, List<Vector3> vertices, int first, List<int> indices, int firstIndex,
        GeodesicTinLakeCrossings.Crossing crossing, float offset, bool lines)
    {
        if (lines) { Record(route, vertices, first, true); return; }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var reach = new Reach
        {
            Vertices = new Vector3[vertices.Count - first],
            Triangles = new int[indices.Count - firstIndex],
            Path = Array.ConvertAll(crossing.Path, p => crossing.Bed.Position(p, offset))
        };
        vertices.CopyTo(first, reach.Vertices, 0, reach.Vertices.Length);
        for (int i = 0; i < reach.Triangles.Length; i++) reach.Triangles[i] = indices[firstIndex + i] - first;
        Add(route, reach);
        CaptureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    private void Add(string route, Reach reach)
    {
        if (!geometry.TryGetValue(route, out var reaches)) geometry.Add(route, reaches = new List<Reach>());
        reaches.Add(reach);
    }

    internal void FitCoast(string route, Vector3 direction) => fittedCoasts[route] = direction;
    internal IEnumerable<string> EmittedRoutes => geometry.Keys;
    internal IEnumerable<Vector3[]> Centerlines(string route) => geometry.TryGetValue(route, out var reaches)
        ? reaches.Select(r => Enumerable.Range(0, r.Count).Select(r.Center).ToArray()) : Enumerable.Empty<Vector3[]>();

    internal void Complete(GeodesicTinHydrology tin, GeodesicRiverShoreline shoreline, Func<Vector3, float> terrain,
        GeodesicTinLakeGeometry lakes, float radius, float offset)
    {
        if (completedSnapshot) throw new InvalidOperationException("Rebuild river presentation to repeat a cached geometric outlet audit.");
        var retained = lakes?.Bodies.ToDictionary(l => l.BasinId) ?? new Dictionary<int, TinLakeBody>();
        // This is a numerical contact margin, about 0.02% of an L7 edge.
        float tolerance = radius * .000002f;
        foreach (var entry in Links)
        {
            string id = entry.Key;
            var link = entry.Value;
            var result = new Result { Route = id, Receiver = link.Next ?? link.End.TerminalId };
            Results.Add(id, result);
            if (geometry.TryGetValue(id, out var reaches)) result.End = reaches[0].Center(reaches[0].Count - 1);
            Vector3? witness = link.Contact.HasValue ? tin.Direction(link.Contact.Value) : (Vector3?)null;
            if (witness.HasValue) result.Expected = result.Logical = witness.Value * (terrain(witness.Value) + offset);

            if (link.Next == null && link.End.Destination == GeodesicDrainageDelivery.Destination.InlandClosed)
                Set(Status.Closed, link.End.Type);
            else if (link.Next == null && link.End.Destination == GeodesicDrainageDelivery.Destination.Unresolved)
                Set(Status.Unresolved, link.End.Type);
            else if (!result.End.HasValue)
                Set(Status.ExtractionAmbiguity, "no emitted reach (short, below threshold or rejected during extraction)");
            else if (link.Next == null)
            {
                Vector3 direction = result.End.Value.normalized;
                if (fittedCoasts.TryGetValue(id, out var coast))
                    result.Expected = coast * (terrain(coast) + offset);
                if (shoreline == null) Set(Status.ExtractionAmbiguity, "rendered ocean geometry unavailable");
                else if (!shoreline.TryWaterRadius(direction, link.End.OceanComponent, out float water) || water <= terrain(direction))
                    Set(Status.Gap, "emitted reach stops outside its rendered ocean");
                else if (!HasExposedApproach(reaches[0], link.End.OceanComponent))
                    Set(Status.Gap, "approach is covered by terrain or ocean");
                else Set(Status.Contact, "rendered ocean contact");
            }
            else if (link.Next.StartsWith("lake:", StringComparison.Ordinal) &&
                int.TryParse(link.Next.Substring(5), out int basin) && retained.TryGetValue(basin, out var lake) && witness.HasValue)
            {
                result.Expected = witness.Value * ((float)lake.WaterLevel + offset);
                Set(Vector3.Distance(result.End.Value, result.Expected.Value) <= tolerance ? Status.Contact : Status.Gap, "retained lake shoreline");
            }
            else
            {
                string receiver = link.Next;
                bool converted = receiver.StartsWith("lake:", StringComparison.Ordinal);
                Vector3? best = Closest(receiver, result.End.Value);
                if (converted)
                {
                    Vector3? bed = Closest("bed:" + receiver.Substring(5), result.End.Value);
                    if (bed.HasValue && (!best.HasValue || (bed.Value - result.End.Value).sqrMagnitude < (best.Value - result.End.Value).sqrMagnitude))
                        best = bed;
                }
                if (!best.HasValue) Set(Status.ExtractionAmbiguity, "intended receiver has no emitted reach");
                else
                {
                    result.Expected = best;
                    Set(Vector3.Distance(result.End.Value, best.Value) <= tolerance ? Status.Contact : Status.Gap,
                        converted ? "converted lakebed crossing" : "receiving river or corridor");
                }
            }
            void Set(Status status, string reason) { result.State = status; result.Reason = reason; }
        }

        int bound = 0, verified = 0, gaps = 0, ambiguous = 0, closed = 0, unresolved = 0;
        var checkedRoutes = new Dictionary<string, Status>();
        var systems = new Dictionary<string, Status>();
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var outcome = delivery.NetworkOutcome(i);
            if (outcome.Destination == GeodesicDrainageDelivery.Destination.InlandClosed) { closed++; continue; }
            if (outcome.Destination == GeodesicDrainageDelivery.Destination.Unresolved) { unresolved++; continue; }
            bound++;
            var status = Chain("network:" + i);
            if (status == Status.Contact) verified++;
            else if (status == Status.Gap) gaps++;
            else ambiguous++;
            if (!systems.TryGetValue(outcome.TerminalId, out var prior) || prior == Status.Contact || status == Status.Gap)
                systems[outcome.TerminalId] = status;
        }
        int verifiedCorridors = Links.Keys.Count(k => Links[k].Corridor && Chain(k) == Status.Contact);
        Summary = $"[GeodesicRiverOutlets] oceanSystems={systems.Count} verifiedSystems={systems.Values.Count(s => s == Status.Contact)} " +
            $"oceanBoundNetworks={bound} verifiedNetworks={verified} geometricGapNetworks={gaps} " +
            $"ambiguousNetworks={ambiguous} closedNetworks={closed} unresolvedNetworks={unresolved} " +
            $"verifiedCorridors={verifiedCorridors}/{Links.Values.Count(l => l.Corridor)} rawExtractionAmbiguities={delivery.ExtractionAmbiguities}";

        Status Chain(string id)
        {
            var pending = new List<string>();
            var seen = new HashSet<string>();
            Status downstream = Status.Contact;
            while (id != null && !checkedRoutes.TryGetValue(id, out downstream))
            {
                if (!Results.ContainsKey(id) || !seen.Add(id)) { downstream = Status.Unresolved; break; }
                pending.Add(id);
                id = Links[id].Next;
                downstream = Status.Contact;
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                string key = pending[i];
                var local = Results[key].State;
                if (local == Status.Gap || downstream != Status.Gap && local != Status.Contact) downstream = local;
                checkedRoutes.Add(key, downstream);
            }
            return downstream;
        }

        bool HasExposedApproach(Reach reach, int component)
        {
            for (int i = reach.Count - 1; i > 0; i--)
            {
                Vector3 a = reach.Center(i - 1), b = reach.Center(i);
                for (int j = 0; j < 4; j++)
                {
                    Vector3 p = Vector3.Lerp(a, b, j * .25f), direction = p.normalized;
                    float ground = terrain(direction);
                    if (!shoreline.TryWaterRadius(direction, component, out float ocean) || ground >= ocean)
                        return p.magnitude >= ground - tolerance;
                }
            }
            return false;
        }
    }

    // Only completed results and diagnostic centrelines are persisted. The parent
    // delivery graph and the exact mesh digest bind this proof to its geometry.
    internal void WritePresentation(BinaryWriter w)
    {
        S.Text(w, Summary);
        S.Write(w, Results.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), (b, item) =>
        {
            var r = item.Value;
            S.Text(b, r.Route); S.Text(b, r.Receiver); S.Text(b, r.Reason); b.Write((byte)r.State);
            Optional(b, r.End); Optional(b, r.Expected); Optional(b, r.Logical);
        });
        S.Write(w, geometry.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray(), (b, route) =>
        {
            S.Text(b, route);
            S.Write(b, Centerlines(route).ToArray(), (writer, points) => S.Write(writer, points, S.Vector));
        });

        void Optional(BinaryWriter b, Vector3? value) { b.Write(value.HasValue); if (value.HasValue) S.Vector(b, value.Value); }
    }

    internal static GeodesicRiverOutletAudit ReadPresentation(BinaryReader r, GeodesicDrainageDelivery delivery,
        HashSet<string> bedRoutes, int maximumPoints)
    {
        var result = new GeodesicRiverOutletAudit(delivery) { Summary = S.Text(r), completedSnapshot = true };
        S.Require(result.Summary != null, "Missing completed outlet audit.");
        int count = S.Count(r, delivery.OutletLinks.Count, 7);
        S.Require(count == delivery.OutletLinks.Count, "Outlet audit does not cover the authority graph.");
        for (int i = 0; i < count; i++)
        {
            var value = new Result { Route = S.Text(r), Receiver = S.Text(r), Reason = S.Text(r), State = (Status)S.IdByte(r, 5),
                End = Optional(r), Expected = Optional(r), Logical = Optional(r) };
            S.Require(value.Route != null, "Missing cached outlet route.");
            S.Require(delivery.OutletLinks.TryGetValue(value.Route, out var link), "Unknown cached outlet route.");
            S.Require(value.Receiver == (link.Next ?? link.End.TerminalId) && value.Reason != null, "Cached outlet receiver mismatch.");
            result.Results.Add(value.Route, value);
        }
        count = S.Count(r, delivery.OutletLinks.Count + bedRoutes.Count, 5);
        for (int i = 0; i < count; i++)
        {
            string route = S.Text(r);
            S.Require(route != null && (delivery.OutletLinks.ContainsKey(route) || bedRoutes.Contains(route)) && !result.geometry.ContainsKey(route), "Unknown or duplicate emitted outlet route.");
            var reaches = S.Read(r, maximumPoints, 4, b => S.Read(b, maximumPoints, 12, S.Vector));
            S.Require(reaches.Length > 0, "Empty cached outlet geometry.");
            foreach (var points in reaches)
            {
                S.Require(points.Length >= 2, "Invalid cached diagnostic centreline.");
                result.Add(route, new Reach { Path = points, Vertices = Array.Empty<Vector3>() });
            }
        }
        return result;

        Vector3? Optional(BinaryReader b) => S.Flag(b) ? S.Vector(b) : (Vector3?)null;
    }

    private Vector3? Closest(string route, Vector3 endpoint)
    {
        if (!geometry.TryGetValue(route, out var reaches)) return null;
        Vector3 closest = default;
        float distance = float.PositiveInfinity;
        foreach (var reach in reaches)
        {
            if (reach.Triangles != null)
            {
                var v = reach.Vertices;
                var t = reach.Triangles;
                for (int i = 0; i < t.Length; i += 3) Triangle(v[t[i]], v[t[i + 1]], v[t[i + 2]]);
                continue;
            }
            for (int i = 1; i < reach.Count; i++)
            {
                Consider(ClosestOnSegment(endpoint, reach.Center(i - 1), reach.Center(i)));
                if (reach.Lines) continue;
                // The receiver banks can overlap even if its centre misses. Test
                // the actual two strips, using the same triangle layout as emission.
                int first = (i - 1) * 3;
                for (int column = 0; column < 2; column++)
                {
                    int p = first + column;
                    var v = reach.Vertices;
                    Triangle(v[p], v[p + 3], v[p + 1]);
                    Triangle(v[p + 1], v[p + 3], v[p + 4]);
                }
            }
        }
        return closest;

        void Consider(Vector3 candidate)
        {
            float d = (candidate - endpoint).sqrMagnitude;
            if (d < distance) { distance = d; closest = candidate; }
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude > 1e-22f)
            {
                Vector3 p = endpoint - n * (Vector3.Dot(n, endpoint - a) / n.sqrMagnitude);
                if (Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= 0 && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= 0 &&
                    Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= 0)
                { Consider(p); return; }
            }
            Consider(ClosestOnSegment(endpoint, a, b));
            Consider(ClosestOnSegment(endpoint, b, c));
            Consider(ClosestOnSegment(endpoint, c, a));
        }
    }

    internal static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        float length = edge.sqrMagnitude;
        return length > 0f ? a + edge * Mathf.Clamp01(Vector3.Dot(p - a, edge) / length) : a;
    }
}
