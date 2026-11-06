using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicDepressionRepairTests
{
    private const float Sea = 8f;

    [Test]
    public void TinyBelowSeaPitIsFilledTowardARealOutlet()
    {
        var patch = new Patch();
        int floor = patch.Set(6, 5, Sea - .001f);
        var baseline = patch.Build();
        Assert.That(GeodesicDepressionRepair.VerifiedRoute(baseline, Point(baseline, floor), true), Is.False);

        var result = patch.Plan();
        Assert.That(result.Repaired, Is.EqualTo(1), Status(result));
        Assert.That(result.Area, Is.GreaterThan(0d).And.LessThan(GeodesicDepressionRepairLimits.Defaults.affectedArea));
        Assert.That(result.MaximumLift, Is.InRange(.003d, .006d));
        Assert.That(result.Surface[floor].magnitude, Is.GreaterThan(Sea));
        Assert.That(patch.Ocean.OceanMask[floor], Is.False);
        Assert.That(patch.Ocean.PotentialLakeBasin[floor], Is.True);
        var final = patch.Build(result.Surface);
        Assert.That(GeodesicDepressionRepair.VerifiedRoute(final, Point(final, floor), true), Is.True);
    }

    [Test]
    public void ShallowPitWithDryRimUsesTheLowestWitnessedSaddle()
    {
        var patch = new Patch();
        int floor = patch.Set(6, 5, Sea - .0002f);
        patch.Set(5, 5, Sea + .0022f);
        var result = patch.Plan();
        Assert.That(result.Repaired, Is.EqualTo(1), Status(result));
        var report = result.Components.Single();
        Assert.That(report.Spill, Is.EqualTo(Sea + .0022f).Within(.000004));
        Assert.That(result.Surface[floor].magnitude - report.Spill, Is.InRange(.000001d, .0001d));
        Assert.That(result.Surface[patch.Id(5, 5)], Is.EqualTo(patch.Surface[patch.Id(5, 5)]));
    }

    [Test]
    public void ShallowPitWithNoBoundedSpillIsLeftInPlace()
    {
        var patch = new Patch();
        patch.Set(6, 5, Sea - .0002f);
        foreach (int n in patch.NeighborsOf(patch.Id(6, 5))) patch.Set(n, Sea + .03f);
        var result = patch.Plan();
        Assert.That(result.Surface, Is.Null);
        Assert.That(result.Components.Single().Status, Is.EqualTo("no-bounded-spill"));
    }

    [Test]
    public void DeepAndLargeExcludedBasinsArePreserved()
    {
        var patch = new Patch();
        int deep = patch.Set(6, 3, Sea - .04f);
        int large = patch.Set(8, 7, Sea - .001f);
        patch.UnitCellAreas[large] = .05f;
        var result = patch.Plan();
        Assert.That(result.Surface, Is.Null);
        Assert.That(result.Components.Single(c => c.Id == patch.Ocean.ComponentId[deep]).Status, Is.EqualTo("original-depth"));
        Assert.That(result.Components.Single(c => c.Id == patch.Ocean.ComponentId[large]).Status, Is.EqualTo("component-area"));

        patch.Classify(.001f);
        Assert.That(patch.Ocean.OceanMask[large], Is.True);
        Assert.That(patch.Plan().Components.Any(c => c.Id == patch.Ocean.ComponentId[large]), Is.False);
    }

    [Test]
    public void FlatDepartureFailsEvenWithADownhillRouteAtTheEdge()
    {
        Vector3[] vertices = { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1) };
        int[] triangles = { 0, 1, 2, 1, 3, 2 };
        float[] heights = { 2f, 2f, 2f, 1f };
        var tin = new GeodesicTinHydrology(vertices, triangles, heights, heights,
            new[] { false, false, false, true }, 1.5f, onSphere: false);
        Assert.That(GeodesicDepressionRepair.VerifiedRoute(tin, tin.Centroid(0)), Is.True);
        Assert.That(GeodesicDepressionRepair.VerifiedRoute(tin, tin.Centroid(0), true), Is.False);
    }

    [Test]
    public void FailedFinalVerificationRollsBackTheProposal()
    {
        var patch = new Patch();
        patch.Set(6, 5, Sea - .001f);
        patch.Classify();
        var result = patch.Plan(buildTin: surface => ReferenceEquals(surface, patch.Surface)
            ? patch.Build() : patch.Build(surface, routingHeights: surface.Select(p => Sea).ToArray()));
        Assert.That(result.Surface, Is.Null);
        Assert.That(result.Area, Is.Zero);
        Assert.That(result.Components.Single().Status, Is.EqualTo("final-tin-no-descending-outlet"));
    }

    [Test]
    public void NearbyPitsAreHandledByOriginalIdentityAndRespectTotalBudget()
    {
        var patch = new Patch();
        patch.Set(6, 4, Sea - .001f);
        patch.Set(6, 7, Sea - .001f);
        var result = patch.Plan();
        Assert.That(result.Repaired, Is.EqualTo(2), Status(result));
        var limits = GeodesicDepressionRepairLimits.Defaults;
        limits.totalArea = (float)(result.Area * .75d);
        var bounded = patch.Plan(limits);
        Assert.That(bounded.Repaired, Is.EqualTo(1), Status(bounded));
        Assert.That(bounded.Components.Count(c => c.Status == "total-area"), Is.EqualTo(1));
        Assert.That(bounded.Area, Is.LessThanOrEqualTo(limits.totalArea));
    }

    [Test]
    public void RetainedOceanAndItsShorelineFacetsRemainUntouched()
    {
        var patch = new Patch();
        patch.Set(6, 5, Sea - .001f);
        int coastalPit = patch.Set(4, 8, Sea - .001f);
        patch.Set(3, 8, Sea + .0001f);
        patch.Classify();
        patch.MarkRenderShoreline(3, 8);
        var result = patch.Plan();
        Assert.That(result.Repaired, Is.EqualTo(1), Status(result));
        Assert.That(result.Surface[coastalPit], Is.EqualTo(patch.Surface[coastalPit]));
        for (int i = 0; i < patch.CellCount; i++)
            if (patch.Ocean.OceanMask[i] || patch.Ocean.CoastalLand[i])
                Assert.That(result.Surface[i], Is.EqualTo(patch.Surface[i]));
    }

    [Test]
    public void RepeatedRepairProducesIdenticalTerrainAndConservesIndependentRunoff()
    {
        var patch = new Patch();
        patch.Set(6, 5, Sea - .001f);
        var first = patch.Plan();
        var second = patch.Plan();
        Assert.That(first.Repaired, Is.EqualTo(1), Status(first));
        string initial = GeodesicGenerationFingerprint.Positions(patch.Surface);
        string final = GeodesicGenerationFingerprint.Positions(first.Surface);
        Assert.That(final, Is.Not.EqualTo(initial));
        Assert.That(GeodesicGenerationFingerprint.Positions(second.Surface), Is.EqualTo(final));

        var before = Delivery(patch.Build());
        var after = Delivery(patch.Build(first.Surface));
        Assert.That(Math.Abs(before.Residual), Is.LessThanOrEqualTo(before.Tolerance));
        Assert.That(Math.Abs(after.Residual), Is.LessThanOrEqualTo(after.Tolerance));
        Assert.That(after.TotalRunoff, Is.EqualTo(before.TotalRunoff).Within(before.Tolerance));
        Assert.That(after.InlandClosed, Is.LessThan(before.InlandClosed));
        Assert.That(after.PrimaryOcean, Is.GreaterThan(before.PrimaryOcean));
        Assert.That(after.PrimaryOcean + after.SecondaryOcean + after.UnattributedOcean,
            Is.EqualTo(after.DirectOcean + after.ViaLakesOcean).Within(after.Tolerance));
    }

    [Test]
    public void VerifiedDeliverySeparatesPrimarySecondaryAndUnattributedRunoff()
    {
        var patch = new Patch();
        var tin = patch.Build();
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        var delivery = GeodesicDrainageDelivery.Analyze(tin, oceanComponentAt: endpoint =>
        {
            float latitude = tin.Direction(endpoint).z;
            return latitude < -.001f ? 2 : latitude > .001f ? 4 : -1;
        }, primaryOceanComponent: 2);
        Assert.That(delivery.PrimaryOcean, Is.GreaterThan(0));
        Assert.That(delivery.SecondaryOcean, Is.GreaterThan(0));
        Assert.That(delivery.UnattributedOcean, Is.GreaterThan(0));
        Assert.That(delivery.PrimaryOcean + delivery.SecondaryOcean + delivery.UnattributedOcean,
            Is.EqualTo(delivery.DirectOcean + delivery.ViaLakesOcean).Within(delivery.Tolerance));
    }

    private static GeodesicDrainageDelivery Delivery(GeodesicTinHydrology tin)
    {
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        return GeodesicDrainageDelivery.Analyze(tin, oceanComponentAt: _ => 0, primaryOceanComponent: 0);
    }

    private static TinPoint Point(GeodesicTinHydrology tin, int vertex) =>
        tin.BasinPoint(tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex)), vertex);

    private static string Status(GeodesicDepressionRepair.Result result) =>
        string.Join("\n", result.Components.Select(component => component.Describe()));

    // A small spherical patch with an ocean strip along its downhill edge. The
    // simulation weights represent the patch embedded in a much larger primary ocean.
    private sealed class Patch : IGeodesicHydrologyTopology
    {
        private const int Width = 13, Height = 11;
        public int SubdivisionLevel => -1;
        public int CellCount => Width * Height;
        public int TriangleCount => Triangles.Length / 3;
        public long ApproximateHydrologyMemoryBytes => 0;
        public Vector3[] CellDirections { get; } = new Vector3[Width * Height];
        public byte[] NeighborCounts { get; } = new byte[Width * Height];
        public int[] Neighbors6 { get; } = new int[Width * Height * 6];
        public float[] UnitCellAreas { get; } = new float[Width * Height];
        public int[] Triangles { get; }
        public Vector3[] Surface { get; } = new Vector3[Width * Height];
        private readonly float[] raw = new float[Width * Height];
        public GeodesicOceanConnectivity Ocean { get; private set; }
        private bool[] renderOcean;

        public Patch()
        {
            var triangles = new System.Collections.Generic.List<int>();
            for (int z = 0; z < Height; z++)
                for (int x = 0; x < Width; x++)
                {
                    int v = Id(x, z);
                    CellDirections[v] = new Vector3(x * .0007f, 1f, (z - 5) * .0007f).normalized;
                    UnitCellAreas[v] = x < 3 ? .38f : .00015f;
                    Set(v, Sea + .001f * (x - 2.5f) + .000002f * (z - 5));
                    if (x == 0 || z == 0) continue;
                    AddTriangle(v - Width - 1, v - Width, v - 1);
                    AddTriangle(v - Width, v, v - 1);
                }
            Triangles = triangles.ToArray();

            void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                Link(a, b); Link(b, c); Link(c, a);
            }
            void Link(int a, int b)
            {
                if (NeighborsOf(a).Contains(b)) return;
                Neighbors6[a * 6 + NeighborCounts[a]++] = b;
                Neighbors6[b * 6 + NeighborCounts[b]++] = a;
            }
        }

        public int Id(int x, int z) => x + Width * z;
        public int Set(int x, int z, float height) { int v = Id(x, z); Set(v, height); return v; }
        public void Set(int vertex, float height) { raw[vertex] = height; Surface[vertex] = CellDirections[vertex] * height; }
        public int[] NeighborsOf(int vertex) => Neighbors6.Skip(vertex * 6).Take(NeighborCounts[vertex]).ToArray();
        public float NeighborAngularDistance(int cell, int slot) =>
            Vector3.Angle(CellDirections[cell], CellDirections[Neighbors6[cell * 6 + slot]]) * Mathf.Deg2Rad;

        public void Classify(float filterThreshold = .01f) =>
            Ocean = GeodesicOceanConnectivity.Build(this, raw, Sea, true, true, filterThreshold);

        public void MarkRenderShoreline(int x, int z)
        {
            int v = Id(x, z);
            Surface[v] = CellDirections[v] * (Sea - .0001f);
            renderOcean = (bool[])Ocean.OceanMask.Clone();
            renderOcean[v] = true;
        }

        public GeodesicTinHydrology Build(Vector3[] surface = null, float[] routingHeights = null)
        {
            surface = surface ?? Surface;
            var visible = surface.Select(v => v.magnitude).ToArray();
            if (Ocean == null) Classify();
            return new GeodesicTinHydrology(CellDirections, Triangles, visible, routingHeights ?? visible,
                renderOcean ?? Ocean.OceanMask, Sea, Sea, .000002d, completedSurface: surface);
        }

        public GeodesicDepressionRepair.Result Plan(GeodesicDepressionRepairLimits? limits = null,
            Func<Vector3[], GeodesicTinHydrology> buildTin = null)
        {
            if (Ocean == null) Classify();
            return GeodesicDepressionRepair.Plan(this, Surface, Ocean, raw, Enumerable.Range(0, CellCount).ToArray(),
                renderOcean ?? Ocean.OceanMask, Sea, Sea, limits ?? GeodesicDepressionRepairLimits.Defaults, buildTin ?? (s => Build(s)));
        }
    }
}
