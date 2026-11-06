using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicBasinInfillTests
{
    private const float Sea = 8f;
    private const float TrendScale = 60f * Mathf.Deg2Rad;

    [Test]
    public void TinyNearCoastDepressionFitsTheLowLandAroundIt()
    {
        var patch = new Patch();
        int floor = patch.Set(6, 14, Sea - .08f);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        Assert.That(result.Surface[floor].magnitude, Is.InRange(Sea + .01f, Sea + .07f));
        Assert.That(result.Components.Single().MaximumLift, Is.GreaterThan(.02d));
        Assert.That(result.Volume, Is.GreaterThan(0d));
        Assert.That(patch.Ocean.OceanMask[floor], Is.False);
    }

    [Test]
    public void BroadShallowBasinFillsAboveSeaToItsContinentalRim()
    {
        var patch = new Patch();
        patch.Land((x, z) => Sea + .28f + .0008f * (x + z));
        patch.Basin(12, 23, 9, 18, Sea - .025f);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        var basin = result.Components.Single();
        Assert.That(basin.OriginalArea, Is.GreaterThan(.00005d));
        Assert.That(basin.OriginalArea, Is.LessThan(.01d));
        Assert.That(basin.FinalMinimum, Is.GreaterThan(Sea + .25f));
        Assert.That(basin.FinalMean, Is.EqualTo(basin.Target).Within(.025d));
        Assert.That(result.Area, Is.GreaterThan(0d));
    }

    [Test]
    public void DeepClosedBasinCanInfillPartwayAndRemainAnActualTinSink()
    {
        var patch = new Patch();
        patch.Land((x, z) => Sea + .40f);
        patch.Basin(18, 22, 12, 16, Sea - .7f);
        int floor = patch.Set(20, 14, Sea - 2.2f);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        var basin = result.Components.Single();
        Assert.That(basin.MaximumLift, Is.EqualTo(Sea * GeodesicBasinInfillLimits.Defaults.liftFraction).Within(.00002));
        Assert.That(basin.LiftLimitedVertices, Is.GreaterThan(0));
        Assert.That(basin.LargeRelativeToRelief, Is.True);
        var tin = patch.Tin(result.Surface);
        tin.Trace(Vertex(tin, floor), out var termination);
        Assert.That(termination, Is.EqualTo(TinTermination.TrueClosedBasin));
        Assert.That(patch.Ocean.PotentialLakeBasin[floor], Is.True);
    }

    [Test]
    public void InlandTrendSelectsAHigherRimPercentileWithoutAddingUplift()
    {
        var patch = new Patch();
        patch.Land((x, z) => Sea + .70f + .018f * z);
        patch.Basin(15, 25, 9, 19, Sea - .04f, Sea + .40f);
        int mountain = patch.Set(14, 14, Sea + 2f);
        var coastal = patch.Plan(distance: 0f);
        var inland = patch.Plan(distance: TrendScale * 1.2f);
        Assert.That(coastal.Infilled, Is.EqualTo(1), Describe(coastal));
        Assert.That(inland.Infilled, Is.EqualTo(1), Describe(inland));
        var coast = coastal.Components.Single();
        var interior = inland.Components.Single();
        Assert.That(interior.Target, Is.GreaterThan(coast.Target + .035d));
        Assert.That(inland.Surface[patch.Id(20, 14)].magnitude, Is.GreaterThan(coastal.Surface[patch.Id(20, 14)].magnitude));
        Assert.That(interior.FinalMean, Is.InRange(Sea + .84d, Sea + 1.09d));
        Assert.That(inland.Surface[mountain], Is.EqualTo(patch.Surface[mountain]));
    }

    [Test]
    public void AdjacentBasinsKeepTheirOriginalIdentitiesAndDryDivide()
    {
        var patch = new Patch();
        patch.Basin(12, 14, 11, 16, Sea - .10f);
        patch.Basin(16, 18, 11, 16, Sea - .15f);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(2), Describe(result));
        Assert.That(result.Components.Select(c => c.Id).Distinct().Count(), Is.EqualTo(2));
        for (int z = 11; z <= 16; z++)
            Assert.That(result.Surface[patch.Id(15, z)], Is.EqualTo(patch.Surface[patch.Id(15, z)]));
    }

    [Test]
    public void OnePercentExclusionPreservesMajorInlandSeaAndPrimaryOcean()
    {
        var patch = new Patch();
        patch.Basin(18, 23, 7, 12, Sea - .24f);
        for (int z = 7; z <= 12; z++)
            for (int x = 18; x <= 23; x++)
                patch.UnitCellAreas[patch.Id(x, z)] = .012f;
        int secondary = patch.Id(20, 10);
        patch.Set(11, 20, Sea - .09f);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        int secondaryId = patch.Ocean.ComponentId[secondary];
        Assert.That(secondaryId, Is.Not.EqualTo(patch.Ocean.PrimaryRetainedComponent));
        Assert.That(patch.Ocean.Components[secondaryId].Retained, Is.True);
        Assert.That(patch.Ocean.Components[secondaryId].PlanetAreaFraction, Is.GreaterThan(.01d));
        for (int i = 0; i < patch.CellCount; i++)
            if (patch.Ocean.OceanMask[i]) Assert.That(result.Surface[i], Is.EqualTo(patch.Surface[i]));
    }

    [Test]
    public void AFacetAdjoiningRetainedRenderWaterStaysFixed()
    {
        var patch = new Patch();
        int coastalPit = patch.Set(5, 14, Sea - .12f);
        patch.Set(13, 21, Sea - .08f);
        int renderShore = patch.MarkRenderShoreline(4, 14);
        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        Assert.That(result.Surface[coastalPit], Is.EqualTo(patch.Surface[coastalPit]));
        var protectedBasin = result.Components.Single(c => c.Id == patch.Ocean.ComponentId[coastalPit]);
        Assert.That(protectedBasin.Status, Is.EqualTo("protected-shoreline"));
        for (int f = 0; f < patch.TriangleCount; f++)
            if (patch.Triangles.Skip(f * 3).Take(3).Contains(renderShore))
                Assert.That(result.AffectedFacets[f], Is.False);
    }

    [Test]
    public void SubmergedRenderCollarBlendsIntoDryLandWithoutFillingAnUnrelatedFreshwaterBasin()
    {
        var patch = new Patch();
        patch.Land((x, z) => Sea + .30f);
        int floor = patch.Set(15, 14, Sea - .15f);
        foreach (int v in patch.NeighborsOf(floor))
            patch.SetRenderHeight(v, Sea - .06f);
        int freshwater = patch.Set(22, 14, Sea + .05f);

        var result = patch.Plan();
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        Assert.That(result.Components.Single().CollarVertices, Is.EqualTo(patch.NeighborsOf(floor).Length));
        foreach (int v in patch.NeighborsOf(floor))
            Assert.That(result.Surface[v].magnitude, Is.GreaterThan(Sea + .26f));
        Assert.That(result.Surface[freshwater], Is.EqualTo(patch.Surface[freshwater]));
    }

    [Test]
    public void BlendingOnlyRaisesTerrainAndLimitsNewBoundaryGrade()
    {
        var patch = new Patch();
        patch.Land((x, z) => Sea + .10f + .035f * z);
        patch.Basin(14, 23, 7, 18, Sea - .19f);
        int lowRim = patch.Set(13, 11, Sea + .02f);
        var limits = GeodesicBasinInfillLimits.Defaults;
        limits.transitionGrade = .15f;
        var result = patch.Plan(limits);
        Assert.That(result.Infilled, Is.EqualTo(1), Describe(result));
        Assert.That(result.Components.Single().GradeLimitedVertices, Is.GreaterThan(0));
        Assert.That(result.Surface[lowRim], Is.EqualTo(patch.Surface[lowRim]));
        for (int v = 0; v < patch.CellCount; v++)
        {
            Assert.That(result.Surface[v].magnitude, Is.GreaterThanOrEqualTo(patch.Surface[v].magnitude));
            if (!result.ChangedVertices[v]) continue;
            foreach (int n in patch.NeighborsOf(v))
            {
                float length = Sea * (patch.CellDirections[v] - patch.CellDirections[n]).magnitude;
                float original = patch.Surface[v].magnitude - patch.Surface[n].magnitude;
                float final = result.Surface[v].magnitude - result.Surface[n].magnitude;
                Assert.That(final, Is.LessThanOrEqualTo(Math.Max(original, limits.transitionGrade * length) + .00001f));
            }
        }
    }

    [Test]
    public void InfillIsDeterministicAndAreaBudgetUsesWholeAffectedFacets()
    {
        var patch = new Patch();
        patch.Basin(12, 14, 11, 16, Sea - .10f);
        patch.Basin(16, 18, 11, 16, Sea - .15f);
        var first = patch.Plan();
        var second = patch.Plan();
        Assert.That(first.Infilled, Is.EqualTo(2), Describe(first));
        Assert.That(GeodesicGenerationFingerprint.Positions(first.Surface),
            Is.EqualTo(GeodesicGenerationFingerprint.Positions(second.Surface)));
        Assert.That(first.Volume, Is.EqualTo(second.Volume));
        var limits = GeodesicBasinInfillLimits.Defaults;
        limits.totalArea = (float)(first.Area * .75d);
        var bounded = patch.Plan(limits);
        Assert.That(bounded.Infilled, Is.EqualTo(1), Describe(bounded));
        Assert.That(bounded.Components.Count(c => c.Status == "total-area-budget"), Is.EqualTo(1));
        Assert.That(bounded.Area, Is.LessThanOrEqualTo(limits.totalArea));
    }

    [Test]
    public void RebuiltDrainageConservesIndependentRunoffWithNoMaterialUphillSegments()
    {
        var patch = new Patch();
        patch.Basin(12, 17, 10, 16, Sea - .11f);
        var plan = patch.Plan();
        Assert.That(plan.Infilled, Is.EqualTo(1), Describe(plan));
        var baseline = patch.Tin(patch.Surface);
        var final = patch.Tin(plan.Surface);
        var before = Delivery(baseline);
        var after = Delivery(final);
        Assert.That(Math.Abs(before.Residual), Is.LessThanOrEqualTo(before.Tolerance));
        Assert.That(Math.Abs(after.Residual), Is.LessThanOrEqualTo(after.Tolerance));
        Assert.That(after.TotalRunoff, Is.EqualTo(before.TotalRunoff).Within(before.Tolerance));
        Assert.That(after.PrimaryOcean + after.SecondaryOcean + after.UnattributedOcean,
            Is.EqualTo(after.DirectOcean + after.ViaLakesOcean).Within(after.Tolerance));
        Assert.That(final.Networks.Count, Is.GreaterThan(0));
        Assert.That(final.Diagnostics.VisibleMaterialUphillSegments, Is.Zero);
        Assert.That(final.Diagnostics.HydrologyUphillSegments, Is.Zero);
    }

    [Test]
    public void AlternativeRoutingHeightModesRetainTheInfilledElevations()
    {
        Vector3[] directions = { Vector3.up, Vector3.right };
        float[] visible = { 9f, 8f }, raw = { 7f, 8f }, largeScale = { 7.1f, 8.1f };
        bool[] retained = { false, true }, changed = { true, false };
        foreach (var mode in new[] { HydrologyTerrainMode.FineDetailRemoved, HydrologyTerrainMode.ReducedRidgeAndFineDetail })
        {
            var routing = GeodesicHydrologyTerrain.Build(directions, visible, retained, 123456,
                default, Sea, mode, true, largeScale, raw, changed);
            Assert.That(routing, Is.EqualTo(visible));
        }
    }

    private static GeodesicDrainageDelivery Delivery(GeodesicTinHydrology tin)
    {
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        tin.ExtractNetworks(.005d, Sea);
        return GeodesicDrainageDelivery.Analyze(tin, oceanComponentAt: _ => 0, primaryOceanComponent: 0);
    }

    private static TinPoint Vertex(GeodesicTinHydrology tin, int vertex) =>
        tin.BasinPoint(tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex)), vertex);

    private static string Describe(GeodesicBasinInfill.Result result) =>
        string.Join("\n", result.Components.Select(component => component.Describe()));

    // A small spherical terrain fixture. Extra area assigned to the primary strip
    // represents the rest of the planet for the simulation component threshold.
    private sealed class Patch : IGeodesicHydrologyTopology
    {
        private const int Width = 40, Height = 29;
        private readonly float[] raw = new float[Width * Height];
        private int shoreline = -1;
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
        public GeodesicOceanConnectivity Ocean { get; private set; }

        public Patch()
        {
            var triangles = new List<int>();
            for (int z = 0; z < Height; z++)
                for (int x = 0; x < Width; x++)
                {
                    int v = Id(x, z);
                    CellDirections[v] = new Vector3(x * .008f, 1f, (z - 14) * .008f).normalized;
                    Set(x, z, Sea + .015f * (x - 3.5f) + .00005f * (z - 14));
                    if (x == 0 || z == 0) continue;
                    AddTriangle(v - Width - 1, v - Width, v - 1);
                    AddTriangle(v - Width, v, v - 1);
                }
            Triangles = triangles.ToArray();
            for (int z = 0; z < Height; z++)
                for (int x = 0; x < 4; x++) UnitCellAreas[Id(x, z)] += .107f;

            void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                Link(a, b); Link(b, c); Link(c, a);
                float area = GeodesicHydrologyTopology.SphericalTriangleArea(CellDirections[a], CellDirections[b], CellDirections[c]) / 3f;
                UnitCellAreas[a] += area; UnitCellAreas[b] += area; UnitCellAreas[c] += area;
            }

            void Link(int a, int b)
            {
                if (NeighborsOf(a).Contains(b)) return;
                Neighbors6[a * 6 + NeighborCounts[a]++] = b;
                Neighbors6[b * 6 + NeighborCounts[b]++] = a;
            }
        }

        public int Id(int x, int z) => x + Width * z;
        public int Set(int x, int z, float height, float? completed = null)
        {
            int v = Id(x, z);
            raw[v] = height;
            Surface[v] = CellDirections[v] * (completed ?? height);
            return v;
        }

        public void Land(Func<int, int, float> height)
        {
            for (int z = 0; z < Height; z++)
                for (int x = 4; x < Width; x++) Set(x, z, height(x, z));
        }

        public void Basin(int minX, int maxX, int minZ, int maxZ, float height, float? completed = null)
        {
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++) Set(x, z, height, completed);
        }

        public void SetRenderHeight(int vertex, float height) => Surface[vertex] = CellDirections[vertex] * height;

        public int MarkRenderShoreline(int x, int z)
        {
            shoreline = Id(x, z);
            Surface[shoreline] = CellDirections[shoreline] * (Sea - .001f);
            return shoreline;
        }

        public int[] NeighborsOf(int vertex) => Neighbors6.Skip(vertex * 6).Take(NeighborCounts[vertex]).ToArray();
        public float NeighborAngularDistance(int cell, int slot) =>
            Vector3.Angle(CellDirections[cell], CellDirections[Neighbors6[cell * 6 + slot]]) * Mathf.Deg2Rad;

        private bool[] RenderOcean()
        {
            var result = (bool[])Ocean.OceanMask.Clone();
            if (shoreline >= 0) result[shoreline] = true;
            return result;
        }

        public GeodesicTinHydrology Tin(Vector3[] surface)
        {
            var visible = surface.Select(v => v.magnitude).ToArray();
            return new GeodesicTinHydrology(CellDirections, Triangles, visible, visible,
                RenderOcean(), Sea, Sea, .000002d, completedSurface: surface);
        }

        public GeodesicBasinInfill.Result Plan(GeodesicBasinInfillLimits? limits = null, float distance = .23f)
        {
            Ocean ??= GeodesicOceanConnectivity.Build(this, raw, Sea, true, true, .01f);
            var coastDistance = Enumerable.Repeat(distance, CellCount).ToArray();
            return GeodesicBasinInfill.Plan(this, Surface, Ocean, raw, Enumerable.Range(0, CellCount).ToArray(),
                RenderOcean(), coastDistance, Sea, Sea, TrendScale, limits ?? GeodesicBasinInfillLimits.Defaults);
        }
    }
}
