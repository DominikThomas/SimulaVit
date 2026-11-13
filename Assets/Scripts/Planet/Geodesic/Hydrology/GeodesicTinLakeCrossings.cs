using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal sealed class GeodesicTinLakeCrossings
{
    internal sealed class Crossing
    {
        internal GeodesicTinLakeSediment.Footprint Bed;
        internal Vector2[] Path, FixedPoints;
        internal double Discharge, DischargeAtInlet;
        internal DiagnosticStatus[] SegmentStatus;
        internal float Length, Width, PeakOffsetWidths, MeanOffsetWidths, PeakLengthFraction, FixedLengthFraction = 1f, BasinWidthUsed;
        internal int PinnedSpans, AttemptFailureMask;
        internal bool HasMeander, Substantial;
        internal Vector3 PeakPosition, PeakBaselinePosition;
    }

    internal enum DiagnosticStatus { Other, Meandered, Containment, BranchClearance }
    internal enum MeanderRejection { None, Containment, BranchClearance, Topology, NoLateralSpace, Numerical }
    private static DiagnosticStatus StatusFor(int failures) =>
        (failures & (1 << (int)MeanderRejection.Containment)) != 0 ? DiagnosticStatus.Containment :
        (failures & ((1 << (int)MeanderRejection.BranchClearance) | (1 << (int)MeanderRejection.Topology))) != 0 ?
        DiagnosticStatus.BranchClearance : DiagnosticStatus.Other;

    internal readonly List<Crossing> Paths = new List<Crossing>();
    internal readonly List<string> Failures = new List<string>();
    internal readonly HashSet<int> VisibleOutlets = new HashSet<int>();
    internal int Direct, Interior, Junctions, LogicalConnections, Gaps;
    internal int MeanderedCrossings => Paths.Count(p => p.HasMeander);
    private Crossing[] longestCrossings = Array.Empty<Crossing>();
    private readonly List<(float Offset, float Weight)> displacementSamples = new List<(float, float)>();

    // Whole-crossing spline, with basin-sized offsets and only genuine junction pins.
    // Local relaxation feathers around obstacles; it never scales the whole river down.
    private const int CurveSamples = 64;
    private const int SmoothAdaptPasses = 6;
    private const float GeometryTolerance = 2e-7f;

    private sealed class Control
    {
        internal float Distance;
        internal Vector2 Base, Target;
        internal Vector2 Normal, Tangent;
        internal float Negative, Positive, Value, Along, AlongNegative, AlongPositive;
        internal bool Pinned;
    }

    internal string MeanderSummary()
    {
        int terrain = (1 << (int)MeanderRejection.Containment) | (1 << (int)MeanderRejection.NoLateralSpace);
        int topology = (1 << (int)MeanderRejection.BranchClearance) | (1 << (int)MeanderRejection.Topology);
        string unchanged = string.Join(",", Enum.GetValues(typeof(MeanderRejection)).Cast<MeanderRejection>().Where(r => r != MeanderRejection.None)
            .Select(r => r + ":" + Paths.Count(p => !p.HasMeander && (p.AttemptFailureMask & (1 << (int)r)) != 0)));
        return FormattableString.Invariant($"[GeodesicSedimentMeanders] crossings={Paths.Count} nonzero={MeanderedCrossings} substantial={Paths.Count(p => p.Substantial)} medianWidths={WeightedMedian(.5f):F3} p90Widths={WeightedMedian(.9f):F3} maxBasinWidthUsed={Paths.Select(p => p.BasinWidthUsed).DefaultIfEmpty().Max():F3} constrainedTerrain={Paths.Count(p => (p.AttemptFailureMask & terrain) != 0)} constrainedTopology={Paths.Count(p => (p.AttemptFailureMask & topology) != 0)} unchanged={Paths.Count(p => !p.HasMeander)} unchangedReasons={unchanged}");
    }

    internal string DisplacementSummary() =>
        FormattableString.Invariant($"[GeodesicSedimentMeanderLongest] crossings={longestCrossings.Length} nonzero={longestCrossings.Count(p => p.HasMeander)} substantial={longestCrossings.Count(p => p.Substantial)} medianPeakWidths={Percentile(longestCrossings.Select(p => p.PeakOffsetWidths), .5f):F3} p90PeakWidths={Percentile(longestCrossings.Select(p => p.PeakOffsetWidths), .9f):F3} medianPeakLengthFraction={Percentile(longestCrossings.Select(p => p.PeakLengthFraction), .5f):F3} medianFixedFraction={Percentile(longestCrossings.Select(p => p.FixedLengthFraction), .5f):F3}");

    private float WeightedMedian(float fraction)
    {
        double target = displacementSamples.Sum(p => (double)p.Weight) * fraction, sum = 0;
        foreach (var sample in displacementSamples.OrderBy(p => p.Offset))
        { sum += sample.Weight; if (sum >= target) return sample.Offset; }
        return 0f;
    }

    private static float Percentile(IEnumerable<float> source, float fraction)
    {
        var values = source.OrderBy(p => p).ToArray();
        if (values.Length == 0) return 0f;
        float i = (values.Length - 1) * fraction;
        return Mathf.Lerp(values[Mathf.FloorToInt(i)], values[Mathf.CeilToInt(i)], i - Mathf.Floor(i));
    }
    internal static GeodesicTinLakeCrossings Build(GeodesicTinHydrology tin, GeodesicTinLakeRouting routing,
        GeodesicTinLakeSediment sediment, bool[] renderedRivers)
    {
        var result = new GeodesicTinLakeCrossings();
        result.LogicalConnections = routing.RiverReaches.Count(r => r.ReceivingLakeId >= 0 || r.JunctionNetwork >= 0) +
            routing.Lakes.Count(l => !l.Terminal);
        foreach (var failure in sediment.GeometryFailures) result.Fail(failure.Key, failure.Value);
        if (sediment.LakeCount == 0) return result;
        var inlets = routing.Lakes.ToDictionary(l => l.BasinId, _ => new List<(TinPoint Point, double Flow)>());
        var lakes = routing.Lakes.ToDictionary(l => l.BasinId);
        var queue = new Queue<int>();
        void AddInlet(int id, TinPoint point, double flow)
        {
            if (inlets[id].Count == 0) queue.Enqueue(id);
            inlets[id].Add((point, flow));
        }

        foreach (var reach in routing.RiverReaches)
        {
            if (reach.ReceivingLakeId < 0) continue;
            if (reach.Changed && routing.Directions(reach.Samples).Length < 2) continue;
            if (renderedRivers[reach.RawNetworkId])
                AddInlet(reach.ReceivingLakeId, reach.Samples.Last(), routing.RawReachDischarge(reach.RawNetworkId).Last());
            else if (sediment.Lakes.ContainsKey(reach.ReceivingLakeId))
                result.Fail(reach.ReceivingLakeId, $"inlet river {reach.RawNetworkId} did not render");
        }

        // Keep existing visible outlets and continue a visible inlet through a filtered
        // bed even when the lake outlet alone would fall below the ribbon threshold.
        var followed = new HashSet<int>();
        void Follow(TinLakeFlowNode lake)
        {
            if (!followed.Add(lake.BasinId)) return;
            if (lake.OutletPath.Length >= 2) result.VisibleOutlets.Add(lake.BasinId);
            if (lake.DownstreamLakeEntry.HasValue)
                AddInlet(lake.DownstreamLakeId, lake.DownstreamLakeEntry.Value, lake.Discharge);
            if (sediment.Lakes.ContainsKey(lake.BasinId) && lake.JoinNetwork >= 0 && !renderedRivers[lake.JoinNetwork])
                result.Fail(lake.BasinId, $"outlet river junction {lake.JoinNetwork} did not render");
        }
        foreach (var lake in routing.Lakes)
            if (lake.OutletReachId >= 0 || lake.DownstreamLakeEntry.HasValue && lake.Discharge > 0 && lake.OutletPath.Length == 0 &&
                !sediment.Lakes.ContainsKey(lake.BasinId))
                Follow(lake);
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (sediment.Lakes.ContainsKey(id)) Follow(lakes[id]);
        }

        foreach (var lake in routing.Lakes)
        {
            if (!sediment.Lakes.TryGetValue(lake.BasinId, out var bed)) continue;
            if (lake.Terminal || !lake.Basin.Spill.HasValue) continue;
            var entries = inlets[lake.BasinId].GroupBy(i => tin.Direction(i.Point))
                .Select(g => (Point: g.Key, Flow: g.Max(i => i.Flow))).OrderByDescending(i => i.Flow);
            var tree = new List<Vector2[]>();
            Vector2 outlet = bed.Project(tin.Direction(lake.Basin.Spill.Value));
            bool merged = false;
            foreach (var entry in entries)
            {
                Vector2 inlet = bed.Project(entry.Point);
                if ((inlet - outlet).sqrMagnitude < 1e-14f) continue; // a shared rim point needs no ribbon
                var path = bed.Route(inlet, outlet, out bool direct);
                if (path == null)
                {
                    result.Fail(lake.BasinId, "no interior route between the original inlet and outlet");
                    continue;
                }
                bool trunk = tree.Count == 0;
                if (!trunk)
                {
                    // Prefer a shared interior join. A straight branch to a trunk
                    // waypoint stays inside the same polygon union as the direct route.
                    foreach (var join in tree[0].Skip(1).Take(tree[0].Length - 2).OrderBy(p => (p - inlet).sqrMagnitude))
                    {
                        if (!bed.Covers(inlet, join)) continue;
                        path = new[] { inlet, (inlet + join) * .5f, join };
                        direct = true;
                        break;
                    }
                    path = TrimAtJoin(path, tree);
                    merged = true;
                }
                if (path.Length < 2) continue;
                tree.Add(path);
                result.Paths.Add(new Crossing
                {
                    Bed = bed, Path = path, DischargeAtInlet = entry.Flow,
                    Discharge = trunk ? lake.Discharge : entry.Flow
                });
                if (direct) result.Direct++; else result.Interior++;
            }
            if (merged) result.Junctions++;
        }
        return result;
    }

    internal void Fail(int basin, string reason)
    {
        Failures.Add($"basin={basin} {reason}"); Gaps++;
    }

    private static Vector2[] TrimAtJoin(Vector2[] path, List<Vector2[]> tree)
    {
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1], ab = path[i] - a;
            float best = float.PositiveInfinity;
            foreach (var branch in tree)
            for (int j = 1; j < branch.Length; j++)
            {
                Vector2 c = branch[j - 1], cd = branch[j] - c;
                float cross = GeodesicTinLakeSediment.Cross(ab, cd);
                if (Mathf.Abs(cross) > 1e-14f)
                {
                    float t = GeodesicTinLakeSediment.Cross(c - a, cd) / cross;
                    float u = GeodesicTinLakeSediment.Cross(c - a, ab) / cross;
                    if (t >= 0 && t <= 1 && u >= 0 && u <= 1) best = Mathf.Min(best, t);
                }
                else if (ab.sqrMagnitude > 0 && Mathf.Abs(GeodesicTinLakeSediment.Cross(c - a, ab)) <= 1e-7f * ab.magnitude)
                {
                    float t = Vector2.Dot(c - a, ab) / ab.sqrMagnitude;
                    float u = Vector2.Dot(branch[j] - a, ab) / ab.sqrMagnitude;
                    if (Mathf.Max(t, u) >= 0 && Mathf.Min(t, u) <= 1) best = Mathf.Min(best, Mathf.Max(0, Mathf.Min(t, u)));
                }
            }
            if (float.IsPositiveInfinity(best)) continue;
            var trimmed = path.Take(i).ToList();
            if (best > 0) trimmed.Add(a + ab * best);
            return trimmed.ToArray();
        }
        return path;
    }

    internal void AddMeanders(int visualSeed, GeodesicRiverWidthProfile widths, float surfaceOffset)
    {
        longestCrossings = Paths.OrderByDescending(p => PathLength(p.Path) * p.Bed.Level)
            .Take(Mathf.CeilToInt(Paths.Count * .25f)).ToArray();
        var longSet = new HashSet<Crossing>(longestCrossings);
        foreach (var basin in Paths.GroupBy(p => p.Bed.Id))
        {
            var branches = basin.ToArray();
            foreach (var crossing in branches) Fit(crossing, branches, longSet.Contains(crossing));
        }

        void Fit(Crossing crossing, Crossing[] branches, bool longCrossing)
        {
            var original = crossing.Path;
            var originalArc = ArcLengths(original);
            float length = originalArc[originalArc.Length - 1];
            float inletWidth = widths.Width(crossing.DischargeAtInlet) / crossing.Bed.Level;
            float outletWidth = widths.Width(crossing.Discharge) / crossing.Bed.Level;
            crossing.Length = length; crossing.Width = Mathf.Max(inletWidth, outletWidth);
            crossing.FixedPoints = new[] { original[0], original[original.Length - 1] };
            if (!(length > 0f) || !float.IsFinite(length))
            { crossing.AttemptFailureMask = 1 << (int)MeanderRejection.Numerical; return; }

            uint random = unchecked((uint)visualSeed ^ (uint)crossing.Bed.Id * 747796405u);
            foreach (Vector2 point in new[] { original[0], original[original.Length - 1] })
            { random = unchecked((random ^ (uint)Mathf.RoundToInt(point.x * 1048576f)) * 2891336453u); random ^= unchecked((uint)Mathf.RoundToInt(point.y * 1048576f)); }
            if (random == 0) random = 1;
            float Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (random & 0xffffffu) / 16777216f; }
            int bends = 1 + Mathf.Min(2, Mathf.FloorToInt(Next() * 3f));
            float character = longCrossing ? Mathf.Lerp(.65f, .95f, Next()) : Mathf.Lerp(.18f, .95f, Next());
            var phase = new float[bends + 2]; var signal = new float[bends + 2];
            phase[phase.Length - 1] = length;
            float sign = Next() < .5f ? -1f : 1f, strength = Mathf.Lerp(.65f, 1f, Next());
            for (int i = 1; i <= bends; i++)
            {
                phase[i] = length * (i + Mathf.Lerp(-.22f, .22f, Next())) / (bends + 1f);
                strength = Mathf.Lerp(strength, Mathf.Lerp(.55f, 1f, Next()), .4f);
                signal[i] = sign * strength * character;
                if (Next() > .18f) sign = -sign;
            }

            var keys = new List<Control>();
            for (int i = 0; i < original.Length; i++) AddKey(originalArc[i], original[i], i == 0 || i == original.Length - 1);
            foreach (var other in branches)
            {
                if (other == crossing) continue;
                foreach (var point in new[] { other.Path[0], other.Path[other.Path.Length - 1] })
                    if (Locate(original, originalArc, point, out float distance)) AddKey(distance, point, true);
            }
            var pins = keys.Where(p => p.Pinned).OrderBy(p => p.Distance).ToArray();
            crossing.FixedPoints = pins.Select(p => p.Base).ToArray();
            crossing.PinnedSpans = Mathf.Max(0, pins.Length - 2);
            for (int i = 1; i < phase.Length - 1; i++) AddKey(phase[i], Guide(phase[i]), false);
            // Add an opportunity between true joins without making a separate
            // meander or forcing a straight approach on either side of the join.
            for (int i = 1; i < pins.Length; i++)
            {
                float distance = Mathf.Lerp(pins[i - 1].Distance, pins[i].Distance, Mathf.Lerp(.35f, .65f, Next()));
                AddKey(distance, Guide(distance), false);
            }
            var entry = keys.First(p => p.Pinned && p.Distance == 0f && p.Base.Equals(original[0]));
            var exit = keys.First(p => p.Pinned && p.Distance == length && p.Base.Equals(original[original.Length - 1]));
            keys.Sort((a, b) =>
            {
                int order = a.Distance.CompareTo(b.Distance);
                if (order != 0 || ReferenceEquals(a, b)) return order;
                if (ReferenceEquals(a, entry) || ReferenceEquals(b, exit)) return -1;
                if (ReferenceEquals(b, entry) || ReferenceEquals(a, exit)) return 1;
                return 0;
            });
            float available = 0f, opposite = 0f;
            foreach (var key in keys)
            {
                if (key.Pinned) { key.Target = key.Base; continue; }
                Vector2 tangent = Unit(Guide(Mathf.Min(length, key.Distance + length * .01f)) - Guide(Mathf.Max(0f, key.Distance - length * .01f)));
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                crossing.Bed.LateralRoom(key.Base, normal, out float left, out float right);
                float value = Scalar(key.Distance), halfWidth = Mathf.Lerp(inletWidth, outletWidth, key.Distance / length) * .5f;
                key.Normal = normal; key.Value = value;
                key.Tangent = tangent;
                key.Along = Mathf.Clamp((Scalar(Mathf.Min(length, key.Distance + length * .03f)) -
                    Scalar(Mathf.Max(0f, key.Distance - length * .03f))) * 4f, -1f, 1f) * .3f;
                crossing.Bed.LateralRoom(key.Base, tangent, out float behind, out float ahead);
                key.AlongNegative = behind - Mathf.Min(halfWidth, behind * .5f);
                key.AlongPositive = ahead - Mathf.Min(halfWidth, ahead * .5f);
                // Preserve full width where it fits; still propose a channel in
                // narrow areas where the original ribbon already clips its banks.
                key.Negative = left - Mathf.Min(halfWidth, left * .5f);
                key.Positive = right - Mathf.Min(halfWidth, right * .5f);
                available += Mathf.Abs(value) * (value < 0f ? key.Negative : key.Positive);
                opposite += Mathf.Abs(value) * (value < 0f ? key.Positive : key.Negative);
            }
            float orientation = opposite > available * 1.15f ? -1f : 1f;
            foreach (var key in keys)
                if (!key.Pinned)
                {
                    float value = key.Value * orientation;
                    float along = key.Along * orientation;
                    key.Target = key.Base + key.Normal * (value * (value < 0f ? key.Negative : key.Positive)) +
                        key.Tangent * (along * (along < 0f ? key.AlongNegative : key.AlongPositive));
                }

            var bases = new List<Vector2> { keys[0].Base };
            var targets = new List<Vector2> { keys[0].Target };
            for (int k = 0; k + 1 < keys.Count; k++)
            {
                float interval = keys[k + 1].Distance - keys[k].Distance;
                int steps = Mathf.Max(1, Mathf.CeilToInt(CurveSamples * interval / length));
                Vector2 m0 = Tangent(k), m1 = Tangent(k + 1);
                for (int j = 1; j <= steps; j++)
                {
                    float t = j / (float)steps, distance = Mathf.Lerp(keys[k].Distance, keys[k + 1].Distance, t);
                    bases.Add(j == steps ? keys[k + 1].Base : Guide(distance));
                    targets.Add(j == steps ? keys[k + 1].Target : Hermite(keys[k].Target, keys[k + 1].Target, m0 * interval, m1 * interval, t));
                }
            }

            var baseline = bases.ToArray(); var target = targets.ToArray();
            var localReasons = new int[baseline.Length];
            var baselineArc = ArcLengths(baseline);
            // Fit the interpolated field to actual local space before checking
            // curves and collisions. No arbitrary amplitude retry is involved.
            for (int i = 0; i < target.Length; i++)
            {
                Vector2 delta = target[i] - baseline[i];
                float requested = delta.magnitude;
                if (!(requested > 0f)) continue;
                Vector2 direction = delta / requested;
                crossing.Bed.LateralRoom(baseline[i], direction, out _, out float room);
                float halfWidth = Mathf.Lerp(inletWidth, outletWidth, baselineArc[i] / baselineArc[baselineArc.Length - 1]) * .5f;
                float availableRoom = room - Mathf.Min(halfWidth, room * .5f);
                if (requested <= availableRoom) continue;
                target[i] = baseline[i] + direction * availableRoom;
                crossing.AttemptFailureMask |= 1 << (int)MeanderRejection.Containment;
                localReasons[i] |= 1 << (int)MeanderRejection.Containment;
            }
            var weights = Enumerable.Repeat(1f, baseline.Length).ToArray();
            var path = new Vector2[baseline.Length];
            var bad = new MeanderRejection[path.Length - 1];
            var baselineNormals = Normals(baseline);
            var others = branches.Where(p => p != crossing).Select(p =>
                (River: p, Banks: Banks(p.Path, widths.Width(p.DischargeAtInlet) / p.Bed.Level, widths.Width(p.Discharge) / p.Bed.Level, out _),
                 Joins: SharedJoins(original, p.Path))).ToArray();
            bool safe = false;
            // Each late pass zeros at least one unsafe moving neighbourhood. This
            // bounds the solve by the samples, and confines fallback to obstacles.
            for (int pass = 0; pass <= SmoothAdaptPasses + path.Length; pass++)
            {
                for (int i = 0; i < path.Length; i++) path[i] = baseline[i] + (target[i] - baseline[i]) * weights[i];
                Array.Clear(bad, 0, bad.Length);
                var banks = Banks(path, inletWidth, outletWidth, out var arc);
                for (int i = 1; i < path.Length; i++)
                {
                    Vector2 a = path[i - 1], b = path[i], step = b - a, bankStep = banks[i] - banks[i - 1];
                    if (!crossing.Bed.Covers(a, b)) Mark(i, MeanderRejection.Containment);
                    else
                    {
                        float fraction = crossing.Bed.Coverage(a - banks[i - 1], b - banks[i], b + banks[i], a + banks[i - 1]);
                        if (fraction < 1f - 1e-5f)
                        {
                            Vector2 bankA = baselineNormals[i - 1] * banks[i - 1].magnitude, bankB = baselineNormals[i] * banks[i].magnitude;
                            float originalFraction = crossing.Bed.Coverage(baseline[i - 1] - bankA, baseline[i] - bankB, baseline[i] + bankB, baseline[i - 1] + bankA);
                            if (fraction + 1e-5f < originalFraction) Mark(i, MeanderRejection.Containment);
                        }
                    }
                    bool coincidentPins = step.sqrMagnitude <= GeometryTolerance * GeometryTolerance &&
                        a.Equals(baseline[i - 1]) && b.Equals(baseline[i]);
                    if (!coincidentPins && (Vector2.Dot(step, step + bankStep) <= 0f || Vector2.Dot(step, step - bankStep) <= 0f))
                    {
                        Vector2 baseStep = baseline[i] - baseline[i - 1];
                        Vector2 baseBankStep = baselineNormals[i] * banks[i].magnitude - baselineNormals[i - 1] * banks[i - 1].magnitude;
                        if (Vector2.Dot(baseStep, baseStep + baseBankStep) > 0f && Vector2.Dot(baseStep, baseStep - baseBankStep) > 0f)
                            Mark(i, MeanderRejection.Topology);
                    }
                    foreach (var other in others)
                    for (int j = 1; j < other.River.Path.Length; j++)
                    {
                        float clearance = Mathf.Max(banks[i - 1].magnitude, banks[i].magnitude) + Mathf.Max(other.Banks[j - 1].magnitude, other.Banks[j].magnitude);
                        Vector2 c = other.River.Path[j - 1], d = other.River.Path[j];
                        float separation = SegmentDistanceSquared(a, b, c, d);
                        if (separation >= clearance * clearance) continue;
                        bool sharedContact = other.Joins.Any(p => SegmentDistanceSquared(p, p, a, b) <= GeometryTolerance * GeometryTolerance &&
                            SegmentDistanceSquared(p, p, c, d) <= GeometryTolerance * GeometryTolerance);
                        // Finite ribbons already overlap at acute confluences.
                        // Keep exact shared-node contact and do not worsen existing
                        // buffer overlap; unrelated new contacts still fail.
                        if (!sharedContact &&
                            separation + GeometryTolerance * GeometryTolerance < SegmentDistanceSquared(baseline[i - 1], baseline[i], c, d))
                            Mark(i, MeanderRejection.BranchClearance);
                    }
                    for (int j = 1; j < i - 1; j++)
                    {
                        float clearance = Mathf.Max(banks[i - 1].magnitude, banks[i].magnitude) + Mathf.Max(banks[j - 1].magnitude, banks[j].magnitude);
                        float separation = SegmentDistanceSquared(a, b, path[j - 1], path[j]);
                        if (separation < clearance * clearance && (arc[i - 1] - arc[j] > clearance * 2f || separation == 0f))
                        { Mark(i, MeanderRejection.Topology); Mark(j, MeanderRejection.Topology); }
                    }
                }
                if (bad.All(p => p == MeanderRejection.None)) { safe = true; break; }
                bool changed = false;
                var nextWeights = (float[])weights.Clone();
                for (int i = 0; i < bad.Length; i++)
                {
                    if (bad[i] == MeanderRejection.None) continue;
                    crossing.AttemptFailureMask |= 1 << (int)bad[i];
                    for (int j = Mathf.Max(0, i - 5); j <= Mathf.Min(path.Length - 1, i + 6); j++)
                    {
                        float distance = Mathf.Max(0f, Mathf.Abs(j - (i + .5f)) - 1.5f) / 4f;
                        float feather = .5f + .5f * Mathf.Cos(Mathf.Min(1f, distance) * Mathf.PI);
                        float factor = pass < SmoothAdaptPasses ? 1f - .55f * feather : distance == 0f ? 0f : 1f - feather;
                        if (weights[j] > 0f && (target[j] - baseline[j]).sqrMagnitude > 0f && factor < 1f)
                        {
                            nextWeights[j] = Mathf.Min(nextWeights[j], weights[j] * factor);
                            localReasons[j] |= 1 << (int)bad[i]; changed = true;
                        }
                    }
                }
                weights = nextWeights;
                if (!changed) break;
            }
            crossing.Path = safe ? path : baseline;
            Measure(crossing, original, baseline, inletWidth, outletWidth, surfaceOffset);
            if (!crossing.HasMeander && crossing.AttemptFailureMask == 0)
                crossing.AttemptFailureMask = 1 << (int)MeanderRejection.NoLateralSpace;
            crossing.SegmentStatus = new DiagnosticStatus[path.Length - 1];
            for (int i = 1; i < path.Length; i++)
            {
                bool moved = Moved(i) || Moved(i - 1);
                crossing.SegmentStatus[i - 1] = moved ? DiagnosticStatus.Meandered : StatusFor(localReasons[i - 1] | localReasons[i]);
            }

            bool Moved(int i) => !crossing.Path[i].Equals(baseline[i]) &&
                Vector2.Distance(crossing.Path[i], ClosestPoint(original, crossing.Path[i])) > length * 1e-6f;

            void Mark(int segment, MeanderRejection reason)
            {
                crossing.AttemptFailureMask |= 1 << (int)reason;
                if (bad[segment - 1] == MeanderRejection.None) bad[segment - 1] = reason;
            }
            Vector2 Guide(float distance)
            {
                int i = 0;
                while (i + 2 < original.Length && originalArc[i + 1] < distance) i++;
                float span = originalArc[i + 1] - originalArc[i];
                return Vector2.Lerp(original[i], original[i + 1], span > 0f ? (distance - originalArc[i]) / span : 0f);
            }
            void AddKey(float distance, Vector2 point, bool pinned)
            {
                var existing = keys.FirstOrDefault(p => p.Distance == distance && (!p.Pinned || !pinned || p.Base.Equals(point)));
                if (existing != null) { if (pinned) { existing.Base = point; existing.Pinned = true; } return; }
                keys.Add(new Control { Distance = distance, Base = point, Pinned = pinned });
            }
            Vector2 Tangent(int k)
            {
                if (k == 0) return Unit(original[1] - original[0]);
                if (k == keys.Count - 1) return Unit(original[original.Length - 1] - original[original.Length - 2]);
                return (keys[k + 1].Target - keys[k - 1].Target) / Mathf.Max(1e-20f, keys[k + 1].Distance - keys[k - 1].Distance);
            }
            float Scalar(float distance)
            {
                int k = 0; while (k + 2 < phase.Length && phase[k + 1] < distance) k++;
                float h = phase[k + 1] - phase[k], t = (distance - phase[k]) / h;
                float m0 = k == 0 ? 0f : (signal[k + 1] - signal[k - 1]) / (phase[k + 1] - phase[k - 1]);
                float m1 = k + 2 == phase.Length ? 0f : (signal[k + 2] - signal[k]) / (phase[k + 2] - phase[k]);
                return Hermite(new Vector2(signal[k], 0f), new Vector2(signal[k + 1], 0f), new Vector2(m0 * h, 0f), new Vector2(m1 * h, 0f), t).x;
            }
        }
    }

    private void Measure(Crossing crossing, Vector2[] guide, Vector2[] baseline, float inletWidth, float outletWidth, float offset)
    {
        var arc = ArcLengths(crossing.Path);
        float total = arc[arc.Length - 1], fixedLength = 0f, integral = 0f, prior = 0f;
        Vector2 normal = new Vector2(-(baseline[baseline.Length - 1] - baseline[0]).y, (baseline[baseline.Length - 1] - baseline[0]).x);
        if (normal.sqrMagnitude == 0f) normal = Vector2.up;
        normal = Unit(normal);
        crossing.Bed.LateralExtent(normal, out float bedLow, out float bedHigh);
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        float resolution = crossing.Length * 1e-6f; // Reporting precision, never an eligibility gate.
        Vector2 peak = baseline[0], peakReference = baseline[0];
        bool priorFixed = true;
        for (int i = 0; i < crossing.Path.Length; i++)
        {
            Vector2 reference = ClosestPoint(guide, crossing.Path[i]);
            float displacement = crossing.Path[i].Equals(baseline[i]) ? 0f : Vector2.Distance(crossing.Path[i], reference);
            float width = Mathf.Lerp(inletWidth, outletWidth, total > 0f ? arc[i] / total : 0f);
            float ratio = displacement / width;
            if (ratio > crossing.PeakOffsetWidths)
            {
                crossing.PeakOffsetWidths = ratio; peak = crossing.Path[i]; peakReference = reference;
            }
            crossing.PeakLengthFraction = Mathf.Max(crossing.PeakLengthFraction, displacement / crossing.Length);
            low = Mathf.Min(low, Vector2.Dot(normal, crossing.Path[i])); high = Mathf.Max(high, Vector2.Dot(normal, crossing.Path[i]));
            bool fixedPoint = displacement <= resolution;
            if (i == 0) { priorFixed = fixedPoint; continue; }
            float step = arc[i] - arc[i - 1], sample = (prior + ratio) * .5f;
            displacementSamples.Add((sample, step)); integral += sample * step;
            if (fixedPoint && priorFixed) fixedLength += step;
            priorFixed = fixedPoint;
            prior = ratio;
        }
        crossing.HasMeander = crossing.PeakLengthFraction > 1e-6f;
        crossing.Substantial = crossing.PeakOffsetWidths >= 3f && crossing.PeakLengthFraction >= .05f; // Reporting only.
        crossing.MeanOffsetWidths = total > 0f ? integral / total : 0f;
        crossing.FixedLengthFraction = total > 0f ? fixedLength / total : 1f;
        crossing.BasinWidthUsed = bedHigh > bedLow ? (high - low) / (bedHigh - bedLow) : 0f;
        crossing.PeakPosition = crossing.Bed.SurfacePosition(peak, offset);
        crossing.PeakBaselinePosition = crossing.Bed.SurfacePosition(peakReference, offset);
    }

    private static Vector2 Hermite(Vector2 a, Vector2 b, Vector2 m0, Vector2 m1, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * b + (t3 - t2) * m1;
    }
    private static Vector2 Unit(Vector2 p) => p.sqrMagnitude > 0f ? p / p.magnitude : Vector2.zero;
    private static Vector2 ClosestPoint(Vector2[] path, Vector2 point)
    {
        Vector2 result = path[0]; float best = float.PositiveInfinity;
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 edge = path[i] - path[i - 1];
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - path[i - 1], edge) / edge.sqrMagnitude) : 0f;
            Vector2 candidate = path[i - 1] + edge * t;
            float distance = (candidate - point).sqrMagnitude;
            if (distance < best) { best = distance; result = candidate; }
        }
        return result;
    }
    private static float PathLength(Vector2[] path) => ArcLengths(path)[path.Length - 1];
    private static float[] ArcLengths(Vector2[] path)
    {
        var arc = new float[path.Length];
        for (int i = 1; i < path.Length; i++) arc[i] = arc[i - 1] + Vector2.Distance(path[i - 1], path[i]);
        return arc;
    }
    private static Vector2[] Normals(Vector2[] path)
    {
        var normals = new Vector2[path.Length];
        for (int i = 0; i < path.Length; i++)
        { Vector2 tangent = Unit(path[Math.Min(i + 1, path.Length - 1)] - path[Math.Max(0, i - 1)]); normals[i] = new Vector2(-tangent.y, tangent.x); }
        return normals;
    }
    private static Vector2[] Banks(Vector2[] path, float inlet, float outlet, out float[] arc)
    {
        arc = ArcLengths(path); var banks = Normals(path); float total = arc[arc.Length - 1];
        for (int i = 0; i < banks.Length; i++) banks[i] *= Mathf.Lerp(inlet, outlet, total > 0f ? arc[i] / total : 0f) * .5f;
        return banks;
    }
    private static bool Locate(Vector2[] path, float[] arc, Vector2 point, out float distance)
    {
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 edge = path[i] - path[i - 1];
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - path[i - 1], edge) / edge.sqrMagnitude) : 0f;
            if ((path[i - 1] + edge * t - point).sqrMagnitude > GeometryTolerance * GeometryTolerance) continue;
            distance = Mathf.Lerp(arc[i - 1], arc[i], t); return true;
        }
        distance = 0f; return false;
    }
    private static Vector2[] SharedJoins(Vector2[] a, Vector2[] b)
    {
        var result = new List<Vector2>(); var aa = ArcLengths(a); var bb = ArcLengths(b);
        foreach (Vector2 point in new[] { a[0], a[a.Length - 1], b[0], b[b.Length - 1] })
            if (Locate(a, aa, point, out _) && Locate(b, bb, point, out _) && !result.Contains(point)) result.Add(point);
        return result.ToArray();
    }
    private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a, cd = d - c;
        float cross = GeodesicTinLakeSediment.Cross(ab, cd);
        if (Mathf.Abs(cross) > 1e-14f)
        {
            float t = GeodesicTinLakeSediment.Cross(c - a, cd) / cross;
            float u = GeodesicTinLakeSediment.Cross(c - a, ab) / cross;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }
        return Mathf.Min(Mathf.Min(PointDistance(a, c, d), PointDistance(b, c, d)),
            Mathf.Min(PointDistance(c, a, b), PointDistance(d, a, b)));

        float PointDistance(Vector2 p, Vector2 x, Vector2 y)
        {
            Vector2 edge = y - x;
            return (p - x - edge * Mathf.Clamp01(Vector2.Dot(p - x, edge) / Mathf.Max(1e-20f, edge.sqrMagnitude))).sqrMagnitude;
        }
    }

    // Emit the fitted whole channel through the existing clipped ribbon builder.
    // Its terrain triangles supply the height; true pins retain exact old contacts.
    internal static void AddRibbon(Crossing crossing, float inletWidth, float outletWidth, float offset, Color color, bool lines,
        List<Vector3> vertices, List<int> indices, List<Color> colors, Action<int, int, DiagnosticStatus> recordDiagnostic = null)
    {
        var bed = crossing.Bed;
        var path = crossing.Path;
        DiagnosticStatus Status(int segment) => crossing.SegmentStatus != null ? crossing.SegmentStatus[segment] : DiagnosticStatus.Other;
        void Add(Vector2 p, GeodesicTinLakeSediment.Facet triangle = null)
        {
            // Keep the exact existing 3D connection at true hydraulic pins.
            bool pinned = false;
            if (crossing.FixedPoints != null)
            {
                foreach (var point in crossing.FixedPoints)
                    if (point.Equals(p)) { p = point; pinned = true; break; }
                if (!pinned)
                {
                    float closest = 1e-14f; Vector2 snap = p;
                    foreach (var point in crossing.FixedPoints)
                        if ((point - p).sqrMagnitude < closest) { closest = (point - p).sqrMagnitude; snap = point; pinned = true; }
                    p = snap;
                }
            }
            vertices.Add(triangle != null && !pinned ? bed.SurfacePosition(p, triangle, offset) : bed.Position(p, offset));
            colors.Add(color);
        }
        if (lines)
        {
            Add(path[0]);
            for (int i = 1; i < path.Length; i++)
            {
                int first = vertices.Count;
                int steps = Mathf.Max(1, Mathf.CeilToInt((path[i] - path[i - 1]).magnitude / .002f));
                for (int j = 1; j <= steps; j++)
                {
                    indices.Add(vertices.Count - 1); indices.Add(vertices.Count);
                    Add(Vector2.Lerp(path[i - 1], path[i], (float)j / steps));
                }
                recordDiagnostic?.Invoke(first, vertices.Count, Status(i - 1));
            }
            return;
        }

        var banks = Banks(path, inletWidth, outletWidth, out _);
        var polygon = new List<Vector2>(8); var buffer = new List<Vector2>(8);
        void Strip(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            foreach (var facet in bed.Facets.Values)
            foreach (var triangle in bed.SurfaceTriangles(facet))
            {
                polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c); polygon.Add(d);
                triangle.ClipPolygon(polygon, buffer);
                if (polygon.Count < 3) continue;
                int first = vertices.Count;
                foreach (var p in polygon) Add(p, triangle);
                for (int j = 1; j + 1 < polygon.Count; j++)
                {
                    float cross = GeodesicTinLakeSediment.Cross(polygon[j] - polygon[0], polygon[j + 1] - polygon[0]);
                    if (cross == 0) continue;
                    indices.Add(first); indices.Add(first + (cross > 0 ? j : j + 1)); indices.Add(first + (cross > 0 ? j + 1 : j));
                }
            }
        }
        void AddSeam(int end, int neighbor)
        {
            // A lakebed strip and the adjacent reach can approach the same
            // shore at different angles. Clip a short overlap to the bed so
            // their differing cross-sections do not leave a wedge uncovered.
            Vector2 point = path[end], bank = banks[end];
            Vector2 past = point + (point - path[neighbor]).normalized * bank.magnitude;
            Strip(point, point - bank, past - bank, past);
            Strip(point, past, past + bank, point + bank);
        }
        int seamStart = vertices.Count;
        AddSeam(0, 1);
        recordDiagnostic?.Invoke(seamStart, vertices.Count, Status(0));
        for (int i = 1; i < path.Length; i++)
        {
            int first = vertices.Count;
            Vector2 a = path[i - 1], b = path[i];
            Strip(a, a - banks[i - 1], b - banks[i], b);
            Strip(a, b, b + banks[i], a + banks[i - 1]);
            recordDiagnostic?.Invoke(first, vertices.Count, Status(i - 1));
        }
        seamStart = vertices.Count;
        AddSeam(path.Length - 1, path.Length - 2);
        recordDiagnostic?.Invoke(seamStart, vertices.Count, Status(path.Length - 2));
    }
}
