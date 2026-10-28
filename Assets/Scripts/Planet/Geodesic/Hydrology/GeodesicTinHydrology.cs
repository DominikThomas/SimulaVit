using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public enum TinTermination
{
    OceanCoast, TrueClosedBasin, TinyNoiseDepression, FlatRequiringRouting, RoutingFailure, Confluence
}

/// <summary>A point on a particular linear facet. Double barycentrics avoid edge nudges.</summary>
public readonly struct TinPoint
{
    public readonly int Triangle;
    public readonly double A, B, C;
    public TinPoint(int triangle, double a, double b, double c)
    { Triangle = triangle; A = a; B = b; C = c; }
}

public sealed class TinRiverNetwork
{
    public int SourceTriangle;
    public int JunctionNetwork = -1;
    public TinTermination Termination;
    public TinPoint[] Samples;
    public Vector3[] Centreline;
    public double Length;
}

public sealed class TinHydrologyDiagnostics
{
    public int HydrologyTriangles, RiverTriangles, RiverNetworks, Sources, Confluences;
    public int OceanTerminations, ClosedBasinTerminations, NoiseDepressionTerminations, FlatTerminations;
    public int UnexpectedBrokenNetworks, UnexpectedInlandBreaks;
    public int HydrologyDownhillSegments, HydrologyFlatSegments, HydrologyUphillSegments;
    public int VisibleDownhillSegments, VisibleMinorBumpSegments, VisibleMaterialUphillSegments, VisibleRidgeCrossings;
    public int RejectedHydrologySteps, RejectedVisibleSteps, RejectedPostProcessing;
    public double MeanNetworkLength, MaxNetworkLength;
    public double FilteringMilliseconds, AdjacencyMilliseconds, GradientMilliseconds, TopologyMilliseconds, AccumulationMilliseconds;
    public double TracingMilliseconds, RibbonMilliseconds, FilterStrength, MeanFilterChange, MaxFilterChange;
    public int FilterRetries;
    public string Summary() =>
        $"[GeodesicTIN] hydrologyTriangles={HydrologyTriangles} riverTriangles={RiverTriangles} " +
        $"riverNetworks={RiverNetworks} sources={Sources} confluences={Confluences} " +
        $"oceanTerminations={OceanTerminations} closedBasinTerminations={ClosedBasinTerminations} " +
        $"noiseDepressions={NoiseDepressionTerminations} flatTerminations={FlatTerminations} " +
        $"unexpectedBrokenNetworks={UnexpectedBrokenNetworks} unexpectedInlandBreaks={UnexpectedInlandBreaks} " +
        $"hydrologyDownhillSegments={HydrologyDownhillSegments} hydrologyFlatSegments={HydrologyFlatSegments} " +
        $"hydrologyUphillSegments={HydrologyUphillSegments} visibleDownhillSegments={VisibleDownhillSegments} " +
        $"visibleMinorBumpSegments={VisibleMinorBumpSegments} visibleMaterialUphillSegments={VisibleMaterialUphillSegments} " +
        $"visibleRidgeCrossings={VisibleRidgeCrossings} rejectedHydrologySteps={RejectedHydrologySteps} " +
        $"rejectedVisibleSteps={RejectedVisibleSteps} rejectedPostProcessing={RejectedPostProcessing} " +
        $"meanNetworkLength={MeanNetworkLength:G6} maxNetworkLength={MaxNetworkLength:G6} " +
        $"filterStrength={FilterStrength:G4} filterRetries={FilterRetries} meanFilterChange={MeanFilterChange:G6} " +
        $"maxFilterChange={MaxFilterChange:G6} filteringMs={FilteringMilliseconds:F2} adjacencyMs={AdjacencyMilliseconds:F2} " +
        $"triangleGradientMs={GradientMilliseconds:F2} flowTopologyMs={TopologyMilliseconds:F2} " +
        $"accumulationMs={AccumulationMilliseconds:F2} streamlineTracingMs={TracingMilliseconds:F2} " +
        $"ribbonMs={RibbonMilliseconds:F2} lakes=OFF";
}

/// <summary>
/// Continuous flow on the completed render TIN. Receivers identify triangle connectivity;
/// they are never geometric vertices or centroid-to-centroid river segments. Entry position
/// matters: runoff is accumulated by tracing each facet's area parcel through the actual
/// field, rather than assuming every entry into a facet has the centroid's exit edge.
/// No priority filling, lake creation, terrain edits, or per-frame drainage is performed.
/// </summary>
public sealed class GeodesicTinHydrology
{
    private readonly Vector3[] vertices;
    private readonly Vector3[] completedVertices;
    private readonly int[] triangles;
    private readonly bool spherical, boundaryOutflow;
    private readonly bool[] oceanVertices;
    private readonly float seaLevel;
    private readonly int[] incidentOffsets, incidentTriangles, flatNextEdge, flatDistance;
    private readonly Face[] faces;
    private readonly double[] localRunoff;
    public readonly float[] VisibleHeight, HydrologyHeight;
    public readonly int[] Neighbors, TriangleReceiver;
    public readonly double[] LocalArea, DrainageArea, AccumulatedRunoff;
    public readonly double Epsilon, VisibleBumpTolerance;
    public int TriangleCount => faces.Length;
    public int SubdivisionLevel { get; }
    public bool HasVisibleDisagreement { get; private set; }
    public TinHydrologyDiagnostics Diagnostics { get; } = new TinHydrologyDiagnostics();
    public IReadOnlyList<TinRiverNetwork> Networks => networks;
    private readonly List<TinRiverNetwork> networks = new List<TinRiverNetwork>();
    public IReadOnlyList<int> RoutingFailureTriangles => routingFailureTriangles;
    private readonly List<int> routingFailureTriangles = new List<int>();

    private struct D3
    {
        public double X, Y, Z;
        public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public D3(Vector3 v) { X = v.x; Y = v.y; Z = v.z; }
        public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
        public double Length => Math.Sqrt(Dot(this, this));
        public Vector3 Vector => new Vector3((float)X, (float)Y, (float)Z);
        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
    private struct Face
    {
        public D3 Downhill, VisiblePlane;
        public double DA, DB, DC, Min, Max;
        public bool Valid, Flat, RetainedOcean;
    }

    private sealed class EdgeHash : IEqualityComparer<ulong>
    {
        public bool Equals(ulong a, ulong b) => a == b;
        public int GetHashCode(ulong key)
        {
            // Sequential mesh vertex ids collide badly under ulong's high-XOR-low hash.
            key ^= key >> 30; key *= 0xbf58476d1ce4e5b9UL;
            key ^= key >> 27; key *= 0x94d049bb133111ebUL; key ^= key >> 31;
            return unchecked((int)key);
        }
    }

    public GeodesicTinHydrology(Vector3[] positions, int[] indices, float[] visibleHeights,
        float[] hydrologyHeights, bool[] retainedOceanVertices = null, float seaLevelRadius = 0f,
        double radius = 1d, double epsilon = .000002d, double visibleBumpTolerance = 0d,
        bool onSphere = true, bool allowBoundaryOutflow = false, int subdivisionLevel = -1, Vector3[] completedSurface = null)
    {
        if (positions == null || indices == null || indices.Length % 3 != 0 ||
            visibleHeights == null || hydrologyHeights == null || positions.Length != visibleHeights.Length ||
            positions.Length != hydrologyHeights.Length || (retainedOceanVertices != null && retainedOceanVertices.Length != positions.Length) ||
            (completedSurface != null && completedSurface.Length != positions.Length))
            throw new ArgumentException("TIN positions, triangles and height fields must match.");
        vertices = positions; triangles = indices; VisibleHeight = visibleHeights; HydrologyHeight = hydrologyHeights;
        completedVertices = completedSurface;
        spherical = onSphere; boundaryOutflow = allowBoundaryOutflow; oceanVertices = retainedOceanVertices;
        seaLevel = seaLevelRadius; SubdivisionLevel = subdivisionLevel;
        Epsilon = Math.Max(0d, epsilon); VisibleBumpTolerance = Math.Max(0d, visibleBumpTolerance);
        faces = new Face[indices.Length / 3]; Neighbors = new int[indices.Length]; Array.Fill(Neighbors, -1);
        TriangleReceiver = new int[faces.Length]; Array.Fill(TriangleReceiver, -1);
        LocalArea = new double[faces.Length]; DrainageArea = new double[faces.Length];
        AccumulatedRunoff = new double[faces.Length]; localRunoff = new double[faces.Length];
        flatNextEdge = new int[faces.Length]; flatDistance = new int[faces.Length];
        Array.Fill(flatNextEdge, -1); Array.Fill(flatDistance, -1);
        var watch = Stopwatch.StartNew();
        var edges = new Dictionary<ulong, int>(indices.Length / 2, new EdgeHash());
        incidentOffsets = new int[positions.Length + 1];
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= positions.Length) throw new ArgumentException("Invalid TIN vertex index.");
            incidentOffsets[indices[i] + 1]++;
        }
        for (int i = 1; i < incidentOffsets.Length; i++) incidentOffsets[i] += incidentOffsets[i - 1];
        incidentTriangles = new int[indices.Length]; var offsets = (int[])incidentOffsets.Clone();
        for (int t = 0; t < faces.Length; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                incidentTriangles[offsets[indices[t * 3 + k]]++] = t;
                int a = indices[t * 3 + (k + 1) % 3], b = indices[t * 3 + (k + 2) % 3];
                ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                int edge = t * 3 + k;
                if (edges.TryGetValue(key, out int other))
                {
                    if (Neighbors[other] >= 0) throw new ArgumentException("Non-manifold TIN edge.");
                    Neighbors[edge] = other / 3; Neighbors[other] = t;
                }
                else edges.Add(key, edge);
            }
        }
        Diagnostics.AdjacencyMilliseconds = watch.Elapsed.TotalMilliseconds; watch.Restart();
        for (int t = 0; t < faces.Length; t++) BuildFace(t, radius);
        Diagnostics.HydrologyTriangles = faces.Length;
        Diagnostics.GradientMilliseconds = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); BuildFlatRouting(); Diagnostics.TopologyMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private D3 Vertex(int t, int k) => new D3(vertices[triangles[t * 3 + k]]);
    private double Height(int t, int k, bool visible = false) => (visible ? VisibleHeight : HydrologyHeight)[triangles[t * 3 + k]];
    public double Height(TinPoint p, bool visible = false) => p.A * Height(p.Triangle, 0, visible) + p.B * Height(p.Triangle, 1, visible) + p.C * Height(p.Triangle, 2, visible);
    private D3 Position(TinPoint p) => Vertex(p.Triangle, 0) * p.A + Vertex(p.Triangle, 1) * p.B + Vertex(p.Triangle, 2) * p.C;
    public Vector3 Direction(TinPoint p) { var v = Position(p); return spherical ? (v * (1d / v.Length)).Vector : v.Vector; }
    public Vector3 DownhillGradient(int triangle) => faces[triangle].Downhill.Vector;
    public TinPoint Centroid(int t) => new TinPoint(t, 1d / 3d, 1d / 3d, 1d / 3d);

    public double CompletedVisibleRadius(TinPoint p)
    {
        if (!spherical) return Height(p, true);
        D3 d = Position(p);
        return d.Length / D3.Dot(faces[p.Triangle].VisiblePlane, d);
    }

    private D3 RenderVertex(int t, int k)
    {
        D3 v = Vertex(t, k);
        return completedVertices != null ? new D3(completedVertices[triangles[t * 3 + k]]) : v * (Height(t, k, true) / v.Length);
    }

    private double MinimumVisibleRadius(TinPoint start, TinPoint end)
    {
        double minimum = Math.Min(CompletedVisibleRadius(start), CompletedVisibleRadius(end));
        if (!spherical) return minimum;
        D3 s = Position(start), d = Position(end) - s, n = faces[start.Triangle].VisiblePlane;
        double ss = D3.Dot(s, s), sd = D3.Dot(s, d), dd = D3.Dot(d, d), ns = D3.Dot(n, s), nd = D3.Dot(n, d);
        double denominator = dd * ns - sd * nd;
        if (Math.Abs(denominator) > 1e-30)
        {
            double t = (nd * ss - sd * ns) / denominator;
            if (t > 0d && t < 1d) minimum = Math.Min(minimum, CompletedVisibleRadius(Mix(start, end, t)));
        }
        return minimum;
    }

    private void BuildFace(int t, double radius)
    {
        D3 a = Vertex(t, 0), b = Vertex(t, 1), c = Vertex(t, 2), e1 = b - a, e2 = c - a;
        double aa = D3.Dot(e1, e1), ab = D3.Dot(e1, e2), bb = D3.Dot(e2, e2), determinant = aa * bb - ab * ab;
        double h0 = Height(t, 0), h1 = Height(t, 1), h2 = Height(t, 2);
        bool valid = determinant > 1e-24 && Finite(h0) && Finite(h1) && Finite(h2);
        var f = new Face { Valid = valid, Min = Math.Min(h0, Math.Min(h1, h2)), Max = Math.Max(h0, Math.Max(h1, h2)) };
        f.Flat = f.Max - f.Min <= Epsilon * .25d;
        for (int k = 0; k < 3; k++) if (oceanVertices != null && oceanVertices[triangles[t * 3 + k]]) f.RetainedOcean = true;
        if (valid)
        {
            if (spherical)
            {
                D3 ra = RenderVertex(t, 0), rb = RenderVertex(t, 1), rc = RenderVertex(t, 2);
                D3 normal = D3.Cross(rb - ra, rc - ra);
                f.VisiblePlane = normal * (1d / D3.Dot(normal, ra));
            }
            // Fit h=h0+u(h1-h0)+v(h2-h0) in the triangle's local plane (the
            // chord plane on a sphere). These are the exact barycentric rates of -grad h.
            f.DB = -(bb * (h1 - h0) - ab * (h2 - h0)) / determinant;
            f.DC = -(aa * (h2 - h0) - ab * (h1 - h0)) / determinant;
            f.DA = -f.DB - f.DC; f.Downhill = e1 * f.DB + e2 * f.DC;
            LocalArea[t] = spherical
                ? radius * radius * 2d * Math.Atan2(Math.Abs(D3.Dot(a, D3.Cross(b, c))), 1d + D3.Dot(a, b) + D3.Dot(b, c) + D3.Dot(c, a))
                : D3.Cross(e1, e2).Length * .5d;
        }
        faces[t] = f;
    }

    public bool IsOcean(TinPoint p) => faces[p.Triangle].RetainedOcean && CompletedVisibleRadius(p) <= seaLevel;
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    private static double Weight(TinPoint p, int k) => k == 0 ? p.A : k == 1 ? p.B : p.C;
    private static TinPoint Mix(TinPoint a, TinPoint b, double fraction) => new TinPoint(a.Triangle,
        a.A + (b.A - a.A) * fraction, a.B + (b.B - a.B) * fraction, a.C + (b.C - a.C) * fraction);

    private TinPoint Transfer(TinPoint p, int t)
    {
        double a = 0d, b = 0d, c = 0d;
        for (int k = 0; k < 3; k++)
        for (int j = 0; j < 3; j++)
            if (triangles[p.Triangle * 3 + k] == triangles[t * 3 + j])
            { if (j == 0) a += Weight(p, k); else if (j == 1) b += Weight(p, k); else c += Weight(p, k); }
        return new TinPoint(t, a, b, c);
    }

    private void ContainingFaces(TinPoint p, List<int> result)
    {
        result.Clear(); result.Add(p.Triangle);
        int zeros = 0, vertexSlot = -1;
        for (int k = 0; k < 3; k++) { if (Weight(p, k) <= 1e-10) zeros++; else vertexSlot = k; }
        if (zeros >= 2 && vertexSlot >= 0)
        {
            int v = triangles[p.Triangle * 3 + vertexSlot]; result.Clear();
            for (int i = incidentOffsets[v]; i < incidentOffsets[v + 1]; i++) result.Add(incidentTriangles[i]);
        }
        else if (zeros == 1)
            for (int k = 0; k < 3; k++)
                if (Weight(p, k) <= 1e-10 && Neighbors[p.Triangle * 3 + k] >= 0) result.Add(Neighbors[p.Triangle * 3 + k]);
    }

    /// <summary>Choose steepest feasible descent in the incident facet cone, with no nudge,
    /// uphill hop or receiver jump. A valley edge is followed only when descent is constrained
    /// to that edge; at vertices the incident fan is examined for an interior continuation.</summary>
    private bool DownhillStep(TinPoint current, List<int> candidates, out TinPoint start, out TinPoint next)
    {
        TinPoint bestStart = current, bestNext = current; double bestSlope = 1e-12;
        ContainingFaces(current, candidates);
        foreach (int t in candidates)
        {
            Face f = faces[t]; if (!f.Valid || f.Flat) continue;
            TinPoint p = Transfer(current, t);
            if (Math.Abs(p.A + p.B + p.C - 1d) > 1e-9) continue;
            Consider(f.DA, f.DB, f.DC);
            // Constrained gradient along an edge. This is required for a V-shaped valley,
            // not a fallback vertex receiver graph or an escape from a depression.
            if (p.A <= 1e-10 || p.B <= 1e-10 || p.C <= 1e-10)
                for (int k = 0; k < 3; k++)
                {
                    bool allowed = true;
                    // A vertex target must lie on every boundary plane that the edge follows.
                    if (p.A <= 1e-10 && k == 0 && p.B > 1e-10 && p.C > 1e-10) allowed = false;
                    if (p.B <= 1e-10 && k == 1 && p.A > 1e-10 && p.C > 1e-10) allowed = false;
                    if (p.C <= 1e-10 && k == 2 && p.A > 1e-10 && p.B > 1e-10) allowed = false;
                    if (allowed) Consider((k == 0 ? 1d : 0d) - p.A, (k == 1 ? 1d : 0d) - p.B, (k == 2 ? 1d : 0d) - p.C);
                }

            void Consider(double da, double db, double dc)
            {
                double scale = Math.Max(Math.Abs(da), Math.Max(Math.Abs(db), Math.Abs(dc)));
                if (scale <= 1e-30) return;
                da /= scale; db /= scale; dc /= scale;
                if ((p.A <= 1e-10 && da < -1e-12) || (p.B <= 1e-10 && db < -1e-12) || (p.C <= 1e-10 && dc < -1e-12)) return;
                double time = double.PositiveInfinity;
                if (da < -1e-12) time = Math.Min(time, p.A / -da);
                if (db < -1e-12) time = Math.Min(time, p.B / -db);
                if (dc < -1e-12) time = Math.Min(time, p.C / -dc);
                if (!Finite(time) || time <= 1e-12) return;
                double a = Math.Max(0d, p.A + time * da), b = Math.Max(0d, p.B + time * db), c = Math.Max(0d, p.C + time * dc);
                double sum = a + b + c;
                TinPoint q = new TinPoint(t, a / sum, b / sum, c / sum);
                double distance = (Position(q) - Position(p)).Length;
                if (distance <= 1e-14) return;
                double slope = (Height(p) - Height(q)) / distance;
                if (slope <= bestSlope + Math.Max(1e-12, bestSlope * 1e-10)) return;
                bestSlope = slope; bestStart = p; bestNext = q;
            }
        }
        start = bestStart; next = bestNext;
        return bestSlope > 1e-12;
    }

    private TinPoint EdgeMidpoint(int t, int edge) => edge == 0 ? new TinPoint(t, 0d, .5d, .5d) :
        edge == 1 ? new TinPoint(t, .5d, 0d, .5d) : new TinPoint(t, .5d, .5d, 0d);

    private void BuildFlatRouting()
    {
        var queue = new Queue<int>(); var candidates = new List<int>(8);
        for (int t = 0; t < TriangleCount; t++)
        {
            if (!faces[t].Flat || !faces[t].Valid) continue;
            for (int edge = 0; edge < 3; edge++)
            {
                TinPoint p = EdgeMidpoint(t, edge); int neighbor = Neighbors[t * 3 + edge];
                if ((neighbor < 0 && boundaryOutflow) || IsOcean(p) ||
                    (neighbor >= 0 && !faces[neighbor].Flat && DownhillStep(p, candidates, out _, out _)))
                { flatNextEdge[t] = edge; flatDistance[t] = 0; queue.Enqueue(t); break; }
            }
        }
        // Breadth-first distance to a genuine exit resolves plateaus deterministically.
        // Heights remain authoritative; the running-low-water check still guards every move.
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            for (int edge = 0; edge < 3; edge++)
            {
                int neighbor = Neighbors[t * 3 + edge];
                if (neighbor < 0 || !faces[neighbor].Flat || flatDistance[neighbor] >= 0 ||
                    Math.Abs(faces[t].Min - faces[neighbor].Min) > Epsilon * .25d) continue;
                for (int k = 0; k < 3; k++) if (Neighbors[neighbor * 3 + k] == t) flatNextEdge[neighbor] = k;
                flatDistance[neighbor] = flatDistance[t] + 1; queue.Enqueue(neighbor);
            }
        }
    }

    private bool FlatStep(TinPoint p, List<int> candidates, out TinPoint start, out TinPoint next)
    {
        start = next = p;
        ContainingFaces(p, candidates); int best = -1;
        foreach (int candidate in candidates)
            if (flatDistance[candidate] >= 0 && (best < 0 || flatDistance[candidate] < flatDistance[best] ||
                (flatDistance[candidate] == flatDistance[best] && candidate < best))) best = candidate;
        if (best < 0) return false;
        p = Transfer(p, best); int t = best;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (flatNextEdge[t] < 0) return false;
            TinPoint target = EdgeMidpoint(t, flatNextEdge[t]);
            if ((Position(target) - Position(p)).Length > 1e-12) { start = p; next = target; return true; }
            int neighbor = Neighbors[t * 3 + flatNextEdge[t]];
            if (neighbor < 0) return false;
            p = Transfer(p, neighbor); t = neighbor;
        }
        return false;
    }

    private bool Boundary(TinPoint p, List<int> candidates)
    {
        if (!boundaryOutflow) return false;
        ContainingFaces(p, candidates);
        foreach (int t in candidates)
        {
            TinPoint q = Transfer(p, t);
            for (int k = 0; k < 3; k++) if (Weight(q, k) <= 1e-10 && Neighbors[t * 3 + k] < 0) return true;
        }
        return false;
    }

    private TinPoint Coast(TinPoint start, TinPoint next)
    {
        // Intersect the completed visible facet with the sea sphere, not an L6 centre
        // or a linear-radius approximation of the rendered triangle's shoreline.
        if (CompletedVisibleRadius(next) > seaLevel) return next;
        if (CompletedVisibleRadius(start) <= seaLevel) return start;
        double lo = 0d, hi = 1d;
        for (int i = 0; i < 48; i++)
        {
            double mid = (lo + hi) * .5d;
            if (CompletedVisibleRadius(Mix(start, next, mid)) > seaLevel) lo = mid; else hi = mid;
        }
        return Mix(start, next, hi);
    }

    private bool TrimToExactCoast(List<TinPoint> samples)
    {
        // A facet may gain retained-ocean authority at an edge already just below the
        // sea sphere (radial sag can submerge an edge with above-sea endpoints). Confirm
        // the ocean first, then clip the same continuous path back to its actual crossing.
        // The retained mask is never expanded into an excluded inland component.
        for (int i = samples.Count - 1; i > 0; i--)
        {
            TinPoint a = samples[i - 1], b = samples[i];
            if (a.Triangle != b.Triangle || CompletedVisibleRadius(a) <= seaLevel || CompletedVisibleRadius(b) > seaLevel) continue;
            TinPoint coast = Coast(a, b);
            samples.RemoveRange(i, samples.Count - i); samples.Add(coast); return true;
        }
        return samples.Count > 0 && Math.Abs(CompletedVisibleRadius(samples[samples.Count - 1]) - seaLevel) <= 1e-9;
    }

    private bool AcceptStep(TinPoint start, TinPoint next, ref double lowestH, ref double lowestVisible,
        ref double lowestRadius, bool countRejection)
    {
        // On a linear facet, H's extrema on this segment occur at its endpoints.
        // Comparing against the running minimum also prevents epsilon staircases on flats.
        double startH = Height(start), h = Height(next);
        double hydrologyLow = Math.Min(lowestH, startH);
        if (!Finite(startH) || !Finite(h) || startH > lowestH + Epsilon || h > hydrologyLow + Epsilon)
        { if (countRejection) Diagnostics.RejectedHydrologySteps++; return false; }
        double va = Height(start, true), vb = Height(next, true), ra = CompletedVisibleRadius(start), rb = CompletedVisibleRadius(next);
        double visibleLow = Math.Min(lowestVisible, Math.Min(va, vb));
        double radiusLow = Math.Min(lowestRadius, MinimumVisibleRadius(start, next));
        // The facet's visible altitude is linear. Its exact radial surface can also have
        // an interior minimum; include that analytic extremum before allowing any rise.
        if (!Finite(va) || !Finite(vb) || !Finite(ra) || !Finite(rb) || !Finite(radiusLow) ||
            Math.Max(va, vb) > lowestVisible + VisibleBumpTolerance + Epsilon ||
            ra > lowestRadius + VisibleBumpTolerance + Epsilon || rb > radiusLow + VisibleBumpTolerance + Epsilon)
        {
            HasVisibleDisagreement = true;
            if (countRejection) Diagnostics.RejectedVisibleSteps++;
            return false;
        }
        lowestH = Math.Min(hydrologyLow, h); lowestVisible = visibleLow; lowestRadius = radiusLow;
        return true;
    }

    public TinPoint[] Trace(TinPoint source, out TinTermination termination, int maximumSteps = 32768)
    {
        var samples = new List<TinPoint>(128); var candidates = new List<int>(8);
        termination = TraceInto(source, samples, candidates, maximumSteps);
        return samples.ToArray();
    }

    private TinTermination TraceInto(TinPoint source, List<TinPoint> samples, List<int> candidates, int maximumSteps, List<TinPoint[]> rejected = null)
    {
        samples.Clear(); samples.Add(source);
        TinPoint p = source;
        double lowestH = Height(p), lowestV = Height(p, true), lowestR = CompletedVisibleRadius(p);
        if (!Finite(lowestH) || !Finite(lowestV) || !faces[p.Triangle].Valid) return TinTermination.RoutingFailure;
        for (int step = 0; step < maximumSteps; step++)
        {
            if (IsOcean(p) || Boundary(p, candidates)) return TinTermination.OceanCoast;
            if (!DownhillStep(p, candidates, out TinPoint start, out TinPoint next) && !FlatStep(p, candidates, out start, out next))
            {
                ContainingFaces(p, candidates);
                foreach (int t in candidates) if (faces[t].Flat) return TinTermination.FlatRequiringRouting;
                // A level valley gutter can be flat even when neither adjacent facet is.
                // Do not mislabel an unresolved constrained flat as a closed depression.
                foreach (int t in candidates)
                {
                    TinPoint q = Transfer(p, t); int zeros = 0;
                    for (int k = 0; k < 3; k++) if (Weight(q, k) <= 1e-10) zeros++;
                    if (zeros == 0) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        if (zeros == 1 && Weight(q, k) <= 1e-10) continue;
                        if (Weight(q, k) < 1d - 1e-10 && Math.Abs(Height(t, k) - Height(q)) <= Epsilon * .25d)
                            return TinTermination.FlatRequiringRouting;
                    }
                }
                double depth = double.PositiveInfinity;
                foreach (int t in candidates) depth = Math.Min(depth, faces[t].Max - lowestH);
                return depth <= VisibleBumpTolerance ? TinTermination.TinyNoiseDepression : TinTermination.TrueClosedBasin;
            }
            // Explicit zero-length facet handoff preserves the very same point at the edge.
            // It cannot connect to any unrelated downstream receiver.
            bool reachedCoast = IsOcean(next) || IsOcean(start);
            if (reachedCoast && CompletedVisibleRadius(start) <= seaLevel)
                return TrimToExactCoast(samples) ? TinTermination.OceanCoast : TinTermination.RoutingFailure;
            if (reachedCoast) next = Coast(start, next);
            if (!AcceptStep(start, next, ref lowestH, ref lowestV, ref lowestR, true))
            {
                if (rejected != null && Finite(Height(start)) && Finite(Height(next)) &&
                    Finite(CompletedVisibleRadius(start)) && Finite(CompletedVisibleRadius(next)))
                {
                    var failed = new List<TinPoint>(samples) { start, next };
                    rejected.Add(failed.ToArray());
                }
                return TinTermination.RoutingFailure;
            }
            if ((Position(next) - Position(start)).Length <= 1e-14) return IsOcean(next) ? TinTermination.OceanCoast : TinTermination.RoutingFailure;
            if (start.Triangle != p.Triangle) samples.Add(start);
            samples.Add(next); p = next;
            if (reachedCoast) return TinTermination.OceanCoast;
        }
        return TinTermination.RoutingFailure;
    }

    /// <summary>Area parcels terminate exactly where their valid continuous path terminates.
    /// No runoff is propagated through an uphill receiver or across a failed continuation.</summary>
    public void Accumulate(double uniformRunoffPerArea = 1d, double[] triangleLocalRunoff = null)
    {
        if (triangleLocalRunoff != null && triangleLocalRunoff.Length != TriangleCount) throw new ArgumentException("Runoff must match triangles.");
        if (!Finite(uniformRunoffPerArea)) throw new ArgumentException("Runoff density must be finite.");
        if (triangleLocalRunoff != null)
            foreach (double value in triangleLocalRunoff) if (!Finite(value) || value < 0d) throw new ArgumentException("Runoff must be finite and nonnegative.");
        Array.Clear(DrainageArea, 0, TriangleCount); Array.Clear(AccumulatedRunoff, 0, TriangleCount);
        Array.Fill(TriangleReceiver, -1);
        var watch = Stopwatch.StartNew(); var samples = new List<TinPoint>(128); var candidates = new List<int>(8);
        var stamps = new int[TriangleCount];
        for (int t = 0; t < TriangleCount; t++)
        {
            TinPoint source = Centroid(t);
            localRunoff[t] = IsOcean(source) ? 0d : triangleLocalRunoff == null ? LocalArea[t] * Math.Max(0d, uniformRunoffPerArea) : Math.Max(0d, triangleLocalRunoff[t]);
            if (IsOcean(source) || !faces[t].Valid) continue;
            TraceInto(source, samples, candidates, 32768);
            int previous = t;
            foreach (TinPoint p in samples)
            {
                if (p.Triangle != previous && TriangleReceiver[t] < 0) TriangleReceiver[t] = p.Triangle;
                previous = p.Triangle;
                if (stamps[p.Triangle] == t + 1) continue;
                stamps[p.Triangle] = t + 1;
                DrainageArea[p.Triangle] += LocalArea[t]; AccumulatedRunoff[p.Triangle] += localRunoff[t];
            }
        }
        Diagnostics.AccumulationMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private struct ChannelSegment { public int Network; public TinPoint Start, End; }

    /// <summary>Start whole streams at threshold crossings. A tributary can merge only by
    /// constructing an accepted continuous downhill segment inside the same facet to the
    /// exact existing channel point. Sharing a triangle alone never creates a connection.</summary>
    public void ExtractNetworks(double threshold, double radius = 1d, Action<int, TinPoint[]> rejectedUphill = null)
    {
        networks.Clear(); routingFailureTriangles.Clear(); threshold = Math.Max(1e-20, threshold);
        var watch = Stopwatch.StartNew();
        var active = new bool[TriangleCount]; var incoming = new int[TriangleCount];
        var channels = new Dictionary<int, List<ChannelSegment>>(); var covered = new bool[TriangleCount];
        Diagnostics.RiverTriangles = 0;
        Diagnostics.HydrologyDownhillSegments = Diagnostics.HydrologyFlatSegments = Diagnostics.HydrologyUphillSegments = 0;
        Diagnostics.VisibleDownhillSegments = Diagnostics.VisibleMinorBumpSegments = Diagnostics.VisibleMaterialUphillSegments = Diagnostics.VisibleRidgeCrossings = 0;
        Diagnostics.OceanTerminations = Diagnostics.ClosedBasinTerminations = Diagnostics.NoiseDepressionTerminations = Diagnostics.FlatTerminations = 0;
        Diagnostics.UnexpectedBrokenNetworks = Diagnostics.UnexpectedInlandBreaks = Diagnostics.Confluences = 0;
        Diagnostics.MeanNetworkLength = Diagnostics.MaxNetworkLength = 0d;
        for (int t = 0; t < TriangleCount; t++)
        { active[t] = !IsOcean(Centroid(t)) && AccumulatedRunoff[t] >= threshold; if (active[t]) Diagnostics.RiverTriangles++; }
        for (int t = 0; t < TriangleCount; t++)
            if (active[t] && TriangleReceiver[t] >= 0 && active[TriangleReceiver[t]]) incoming[TriangleReceiver[t]]++;
        var sources = new List<int>();
        for (int t = 0; t < TriangleCount; t++) if (active[t] && incoming[t] == 0) sources.Add(t);
        // Build downstream trunks first; later tributaries can end at exact trunk junctions.
        sources.Sort((a, b) => { int order = AccumulatedRunoff[b].CompareTo(AccumulatedRunoff[a]); return order != 0 ? order : a.CompareTo(b); });
        Diagnostics.Sources = sources.Count;
        var samples = new List<TinPoint>(128); var candidates = new List<int>(8);
        var rejectedPaths = rejectedUphill != null ? new List<TinPoint[]>(1) : null;
        foreach (int source in sources)
        {
            rejectedPaths?.Clear();
            TinTermination termination = TraceInto(Centroid(source), samples, candidates, 32768,
                rejectedPaths);
            int junction = -1;
            double lowH = Height(samples[0]), lowV = Height(samples[0], true), lowR = CompletedVisibleRadius(samples[0]);
            var accepted = new List<TinPoint>(samples.Count) { samples[0] };
            for (int i = 1; i < samples.Count; i++)
            {
                TinPoint p = samples[i - 1], q = samples[i];
                if (p.Triangle == q.Triangle && channels.TryGetValue(p.Triangle, out var entries))
                {
                    // A join is an optional geometric candidate. Rejecting it retains the
                    // original gradient path; it never jumps to the downstream reach.
                    foreach (var entry in entries)
                    {
                        TinPoint target = entry.End;
                        if (Height(target) > Height(p)) continue;
                        double h = lowH, v = lowV, r = lowR;
                        if (!AcceptStep(p, target, ref h, ref v, ref r, false)) continue;
                        if (!ValidateDownstreamJoin(entry.Network, target, h, v, r)) continue;
                        accepted.Add(target); junction = entry.Network; termination = TinTermination.Confluence;
                        break; // target is already an exact sample on the existing trunk
                    }
                }
                if (junction >= 0) break;
                accepted.Add(q);
                lowH = Math.Min(lowH, Height(q)); lowV = Math.Min(lowV, Height(q, true)); lowR = Math.Min(lowR, CompletedVisibleRadius(q));
            }
            if (junction < 0 && rejectedPaths != null && rejectedPaths.Count > 0) rejectedUphill(source, rejectedPaths[0]);
            var network = new TinRiverNetwork { SourceTriangle = source, Samples = accepted.ToArray(), Termination = termination, JunctionNetwork = junction };
            network.Centreline = Directions(network.Samples);
            int id = networks.Count; networks.Add(network);
            foreach (TinPoint p in network.Samples) covered[p.Triangle] = true;
            for (int i = 1; i < network.Samples.Length; i++)
            {
                TinPoint p = network.Samples[i - 1], q = network.Samples[i];
                if (p.Triangle != q.Triangle || (Position(q) - Position(p)).Length <= 1e-14) continue;
                if (!channels.TryGetValue(p.Triangle, out var list)) channels.Add(p.Triangle, list = new List<ChannelSegment>(2));
                list.Add(new ChannelSegment { Network = id, Start = p, End = q });
                double dh = Height(q) - Height(p);
                double visibleRise = Math.Max(0d, CompletedVisibleRadius(q) - MinimumVisibleRadius(p, q));
                if (dh < -Epsilon) Diagnostics.HydrologyDownhillSegments++; else if (dh <= Epsilon) Diagnostics.HydrologyFlatSegments++; else Diagnostics.HydrologyUphillSegments++;
                if (visibleRise <= Epsilon) Diagnostics.VisibleDownhillSegments++;
                else if (visibleRise <= VisibleBumpTolerance + Epsilon) Diagnostics.VisibleMinorBumpSegments++;
                else { Diagnostics.VisibleMaterialUphillSegments++; Diagnostics.VisibleRidgeCrossings++; }
                network.Length += spherical ? radius * Math.Acos(Math.Max(-1d, Math.Min(1d, D3.Dot(Position(p), Position(q)) / (Position(p).Length * Position(q).Length)))) : (Position(q) - Position(p)).Length;
            }
            Diagnostics.MeanNetworkLength += network.Length; Diagnostics.MaxNetworkLength = Math.Max(Diagnostics.MaxNetworkLength, network.Length);
            if (termination == TinTermination.Confluence) Diagnostics.Confluences++;
            else if (termination == TinTermination.OceanCoast) Diagnostics.OceanTerminations++;
            else if (termination == TinTermination.TrueClosedBasin) Diagnostics.ClosedBasinTerminations++;
            else if (termination == TinTermination.TinyNoiseDepression) Diagnostics.NoiseDepressionTerminations++;
            else if (termination == TinTermination.FlatRequiringRouting) Diagnostics.FlatTerminations++;
            else { Diagnostics.UnexpectedBrokenNetworks++; Diagnostics.UnexpectedInlandBreaks++; }
        }
        // An active component with no source is a topology ambiguity, not permission to
        // invent a downstream restart. Count it for inspection and leave it undrawn.
        var unresolved = new bool[TriangleCount];
        for (int t = 0; t < TriangleCount; t++)
            if (active[t] && !covered[t] && !unresolved[t])
            {
                Diagnostics.UnexpectedBrokenNetworks++; int p = t;
                while (p >= 0 && active[p] && !covered[p] && !unresolved[p])
                { unresolved[p] = true; routingFailureTriangles.Add(p); p = TriangleReceiver[p]; }
            }
        Diagnostics.RiverNetworks = networks.Count;
        if (networks.Count > 0) Diagnostics.MeanNetworkLength /= networks.Count;
        Diagnostics.TracingMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private Vector3[] Directions(TinPoint[] points)
    {
        var result = new List<Vector3>(points.Length);
        foreach (TinPoint p in points)
        {
            Vector3 d = Direction(p);
            if (result.Count == 0 || d.x != result[result.Count - 1].x || d.y != result[result.Count - 1].y || d.z != result[result.Count - 1].z) result.Add(d);
        }
        return result.ToArray();
    }

    private bool ValidateDownstreamJoin(int networkId, TinPoint target, double h, double v, double r)
    {
        for (int depth = 0; depth <= networks.Count; depth++)
        {
            var network = networks[networkId]; int at = -1;
            for (int i = 0; i < network.Samples.Length; i++)
                if (network.Samples[i].Triangle == target.Triangle &&
                    (Position(network.Samples[i]) - Position(target)).Length <= 1e-12) { at = i; break; }
            if (at < 0) return false;
            for (int i = at + 1; i < network.Samples.Length; i++)
            {
                TinPoint a = network.Samples[i - 1], b = network.Samples[i];
                if (!AcceptStep(a.Triangle == b.Triangle ? a : b, b, ref h, ref v, ref r, false)) return false;
            }
            if (network.JunctionNetwork < 0) return true;
            target = network.Samples[network.Samples.Length - 1]; networkId = network.JunctionNetwork;
        }
        return false;
    }

    /// <summary>Useful for tests and every optional geometry operation. A candidate must
    /// preserve endpoints and the exact facet extrema; failure returns the original path.</summary>
    public TinPoint[] AcceptProcessedPath(TinPoint[] original, TinPoint[] candidate)
    {
        if (candidate == null || candidate.Length < 2 || original == null || original.Length < 2 ||
            (Position(candidate[0]) - Position(original[0])).Length > 1e-12 ||
            (Position(candidate[candidate.Length - 1]) - Position(original[original.Length - 1])).Length > 1e-12 || !Validate(candidate))
        { Diagnostics.RejectedPostProcessing++; return original; }
        return candidate;
    }

    public bool Validate(TinPoint[] path)
    {
        if (path == null || path.Length < 2) return false;
        double h = Height(path[0]), v = Height(path[0], true), r = CompletedVisibleRadius(path[0]);
        if (!Finite(h) || !Finite(v) || !Finite(r)) return false;
        for (int i = 1; i < path.Length; i++)
        {
            TinPoint start = path[i - 1], end = path[i];
            if (start.Triangle != end.Triangle)
            {
                // A facet change must be an exact shared edge/vertex handoff. General
                // splines must first be split at *all* crossed facets, not just sampled sparsely.
                TinPoint transferred = Transfer(start, end.Triangle);
                if (Math.Abs(transferred.A + transferred.B + transferred.C - 1d) > 1e-9 ||
                    (Position(start) - Position(end)).Length > 1e-10) return false;
                if (!AcceptStep(end, end, ref h, ref v, ref r, false)) return false;
                continue;
            }
            if (start.A < -1e-10 || start.B < -1e-10 || start.C < -1e-10 || end.A < -1e-10 || end.B < -1e-10 || end.C < -1e-10 ||
                !AcceptStep(start, end, ref h, ref v, ref r, false)) return false;
        }
        return true;
    }

    /// <summary>One optional, weak, area-neighbor pass; raw heights are always available.
    /// The caller must retry with less filtering if any accepted routing candidate disagrees
    /// materially with the visible mesh. No increase of epsilon is allowed.</summary>
    public static float[] FilterVisible(Vector3[] vertices, int[] triangles, float[] visible,
        double strength, out double meanChange, out double maxChange)
    {
        strength = Math.Max(0d, Math.Min(.1d, strength));
        var sum = new double[visible.Length]; var count = new int[visible.Length];
        for (int t = 0; t < triangles.Length; t += 3)
            for (int k = 0; k < 3; k++)
            { int a = triangles[t + k], b = triangles[t + (k + 1) % 3], c = triangles[t + (k + 2) % 3]; sum[a] += visible[b] + (double)visible[c]; count[a] += 2; }
        var result = new float[visible.Length]; meanChange = maxChange = 0d;
        for (int i = 0; i < visible.Length; i++)
        {
            result[i] = count[i] == 0 ? visible[i] : (float)(visible[i] + strength * (sum[i] / count[i] - visible[i]));
            double change = Math.Abs(result[i] - (double)visible[i]); meanChange += change; maxChange = Math.Max(maxChange, change);
        }
        if (visible.Length > 0) meanChange /= visible.Length;
        return result;
    }

    public static GeodesicTinHydrology BuildAdaptive(Vector3[] vertices, int[] triangles, float[] visible,
        bool[] retainedOcean, float sea, double radius, double epsilon, double visibleBumpTolerance,
        double filterStrength, double threshold, double runoffDensity = 1d, int subdivision = -1,
        bool onSphere = true, bool boundaryOutflow = false, Vector3[] completedSurface = null)
    {
        double strength = Math.Max(0d, Math.Min(.1d, filterStrength)), filteringMs = 0d;
        int retries = 0;
        while (true)
        {
            var watch = Stopwatch.StartNew();
            float[] filtered = FilterVisible(vertices, triangles, visible, strength, out double mean, out double max);
            filteringMs += watch.Elapsed.TotalMilliseconds;
            var tin = new GeodesicTinHydrology(vertices, triangles, visible, filtered, retainedOcean, sea, radius,
                epsilon, visibleBumpTolerance, onSphere, boundaryOutflow, subdivision, completedSurface);
            tin.Accumulate(runoffDensity); tin.ExtractNetworks(threshold, radius);
            tin.Diagnostics.FilterStrength = strength; tin.Diagnostics.MeanFilterChange = mean;
            tin.Diagnostics.MaxFilterChange = max; tin.Diagnostics.FilterRetries = retries;
            tin.Diagnostics.FilteringMilliseconds = filteringMs;
            if (!tin.HasVisibleDisagreement || strength == 0d) return tin;
            strength = strength <= .00625d ? 0d : strength * .5d; retries++;
        }
    }
}
