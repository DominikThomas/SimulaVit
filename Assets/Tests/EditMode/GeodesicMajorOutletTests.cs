using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicMajorOutletTests
{
    [Test]
    public void SingleSaddleConnectsToAnExistingDownhillReceiver()
    {
        var fixture = new Fixture(1);
        var result = fixture.Plan();
        Assert.That(result.Attempts, Has.Count.EqualTo(1), Summary(result));
        var attempt = result.Attempts.Single();
        Assert.That(attempt.Accepted, Is.True, Summary(result));
        Assert.That(attempt.Destination, Is.EqualTo("OceanDrainage"));
        Assert.That(attempt.CrossedBasins, Is.Empty);
        Assert.That(attempt.Search.FeasibleProfiles, Is.GreaterThan(0));
        Assert.That(attempt.Search.CandidateVertices.Last(), Is.EqualTo(attempt.TargetVertex));
        Assert.That(attempt.Search.ReceiverPath, Is.Not.Empty);
        Assert.That(attempt.TraceFailure, Is.Null);
        Assert.That(result.CommittedVolume, Is.EqualTo(attempt.Volume).Within(1e-12));
        Assert.That(result.CommittedVolume, Is.LessThanOrEqualTo(result.MaximumVolume));
        fixture.AssertRebuild(result);
        CollectionAssert.AreEqual(result.Heights, fixture.Plan().Heights);
    }

    [Test]
    public void MultibasinCutReachesPrimaryOceanWithOneDescendingProfile()
    {
        var fixture = new Fixture(3);
        var result = fixture.Plan();
        Assert.That(result.Attempts.Any(a => a.Accepted && a.CrossedBasins.Length >= 1), Is.True, Summary(result));
        fixture.AssertRebuild(result);
    }

    [Test]
    public void ThreeConsecutiveBasinsCanBreachToAnUnchangedCoastline()
    {
        var fixture = new Fixture(0, corridor: new[] { 1.11f, 1.13f, 1.09f, 1.11f, 1.07f, 1.085f, 1.05f, 1f });
        var result = fixture.Plan();
        var attempt = result.Attempts.FirstOrDefault(a => a.Accepted && a.CrossedBasins.Length == 2 &&
            a.Destination == "Coastline");
        Assert.That(attempt, Is.Not.Null, Summary(result));
        Assert.That(attempt.Search.CandidateCoarseChain.Length, Is.GreaterThanOrEqualTo(4), attempt.Search.Summary());
        Assert.That(attempt.Search.CandidateCoarseChain.Last(), Is.EqualTo(GeodesicOceanDrainagePlan.Ocean));
        Assert.That(attempt.Search.CorridorsRefined, Is.InRange(1, 3));
        Assert.That(attempt.Search.AdmittedLabels, Is.LessThan(8192));
        Assert.That(attempt.Search.CyclePrunes, Is.GreaterThan(0));
        fixture.AssertRebuild(result);
    }

    [Test]
    public void HighRunoffBasinUsesALongCoarseSpillChainWithLittleTerrainSearch()
    {
        var fixture = new Fixture(0, corridor: new[] {
            1.20f, 1.22f, 1.17f, 1.19f, 1.14f, 1.16f, 1.11f, 1.13f, 1.08f, 1.10f, 1.05f, 1f
        });
        var result = fixture.Plan();
        var attempt = result.Attempts.FirstOrDefault(a => a.Accepted && a.CrossedBasins.Length >= 4);
        Assert.That(attempt, Is.Not.Null, Summary(result));
        Assert.That(attempt.Search.CandidateCoarseChain.Length, Is.GreaterThanOrEqualTo(6), attempt.Search.Summary());
        Assert.That(attempt.Search.AdmittedLabels, Is.LessThan(512), attempt.Search.Summary());
        fixture.AssertRebuild(result);
    }

    [Test]
    public void AdaptiveProfileRetainsFlowPastSeveralCompetingBranches()
    {
        var fixture = BranchedCorridor();
        var result = fixture.Plan();
        var attempt = result.Attempts[0];
        string evidence = Summary(result) + "\n" + string.Join("\n", attempt.Iterations.Select(i => i.Summary()));
        Assert.That(attempt.Iterations.Any(i => i.Outcome == "revised" && i.CorrectedJunctions >= 2), Is.True, evidence);
        Assert.That(attempt.Accepted, Is.True, evidence);
        Assert.That(attempt.Iterations.Last().Termination, Is.EqualTo(TinTermination.OceanCoast), evidence);
        Assert.That(attempt.TestedTrace.Any(p => Vector3.Distance(fixture.Tin.Direction(p),
            fixture.Tin.Direction(attempt.Search.ReceiverPath[0])) < 1e-6f), Is.True, evidence);
        fixture.AssertRebuild(result);
        CollectionAssert.AreEqual(result.Heights, fixture.Plan().Heights);
    }

    [Test]
    public void RebuiltBanksExposeASecondDiversionFartherDownstream()
    {
        var fixture = BranchedCorridor();
        var result = fixture.Plan(width: .045f);
        var attempt = result.Attempts[0];
        string evidence = Summary(result) + "\n" + string.Join("\n", attempt.Iterations.Select(i => i.Summary()));
        Assert.That(attempt.Accepted, Is.True, evidence);
        Assert.That(attempt.Iterations.Select(i => i.Outcome), Is.EqualTo(new[] { "revised", "revised", "verified-primary-ocean" }), evidence);
        Assert.That(attempt.Iterations[1].MovedDownstream, Is.True, evidence);
        Assert.That(attempt.Iterations[1].RevisedVolume, Is.GreaterThan(attempt.Iterations[0].RevisedVolume));
        Assert.That(attempt.Iterations.Any(i => i.RepeatedState), Is.False, evidence);
        fixture.AssertRebuild(result);
    }

    [Test]
    public void SecondRevisionOverVolumeBudgetLeavesBothCutsUncommitted()
    {
        var fixture = BranchedCorridor();
        const double volume = .00045;
        var result = fixture.Plan(volume: volume, width: .045f);
        var attempt = result.Attempts[0];
        Assert.That(attempt.Iterations[0].Outcome, Is.EqualTo("revised"), Summary(result));
        Assert.That(attempt.Iterations[0].RevisedVolume, Is.LessThan(volume));
        Assert.That(attempt.Iterations[1].RevisedVolume, Is.GreaterThan(volume));
        Assert.That(attempt.Rejection, Is.EqualTo("adaptive-volume-budget"), Summary(result));
        Assert.That(attempt.OriginalPonding, Does.Contain("estimatedStorageToFirstSpillPlanetUnitsCubed"));
        Assert.That(result.CommittedVolume, Is.Zero);
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    private static Fixture BranchedCorridor() => new Fixture(0, corridor: new[] {
        1.092f, 1.100f, 1.084f, 1.088f, 1.076f, 1.080f, 1.064f, 1.068f, 1.048f, 1.052f, 1.032f, 1.012f
    }, branches: new[] { (2, 1.0736f), (4, 1.0612f), (6, 1.047f) });

    [Test]
    public void ProtectedLakeShoreBlocksAFeasibleOutlet()
    {
        var fixture = new Fixture(1);
        Assert.That(fixture.Plan().Changed, Is.True);
        var basins = GeodesicTinBasins.Build(fixture.Tin, 0, .00001);
        var lakes = GeodesicTinLakeGeometry.Build(fixture.Tin, basins);
        Assert.That(lakes.Bodies.Count, Is.GreaterThan(0));
        var result = fixture.Plan(lakes: lakes);
        Assert.That(result.Changed, Is.False, Summary(result));
        Assert.That(result.Attempts.Single().Rejection, Does.Contain("lake"));
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void ExcessivelyDeepBarrierRemainsClosed()
    {
        var fixture = new Fixture(1);
        var result = fixture.Plan(depth: .00025f);
        Assert.That(result.Changed, Is.False, Summary(result));
        Assert.That(result.Attempts.Single().Rejection, Is.EqualTo("depth-budget"));
        Assert.That(result.Attempts.Single().RequiredDepth, Is.GreaterThan(.00025));
        var search = result.Attempts.Single().Search;
        Assert.That(search.Stop, Is.EqualTo(GeodesicValleyCarving.MajorOutletSearchStop.FrontierExhausted));
        Assert.That(search.DepthPrunes, Is.GreaterThan(0));
        Assert.That(search.QueuedLabelsAtStop, Is.Zero);
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void SecondaryWaterCannotAuthorizeAnOutlet()
    {
        var fixture = new Fixture(1, secondary: true);
        var result = fixture.Plan();
        Assert.That(result.Attempts, Has.Count.EqualTo(1), Summary(result));
        Assert.That(result.Changed, Is.False, Summary(result));
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void BelowSeaFloorRequiresStorageAndCannotBecomeADryOutlet()
    {
        var fixture = new Fixture(1, belowSea: true);
        var result = fixture.Plan();
        Assert.That(result.Changed, Is.False, Summary(result));
        Assert.That(result.Attempts.Any(a => a.Rejection == "below-dry-channel-floor-would-require-storage"), Is.True, Summary(result));
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void ExcavationVolumeBudgetLeavesOriginalTinIntact()
    {
        var fixture = new Fixture(1);
        var result = fixture.Plan(volume: 1e-9);
        Assert.That(result.Changed, Is.False, Summary(result));
        Assert.That(result.Attempts.Single().Rejection, Is.EqualTo("volume-budget"));
        Assert.That(result.CommittedVolume, Is.Zero);
        Assert.That(result.VerificationBuilds, Is.Zero);
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void UnreachablePrimaryReceiverStopsBeforeTerrainLabelsAreSpent()
    {
        var fixture = new Fixture(1, secondary: true);
        var result = fixture.Plan(depth: .2f, volume: .05);
        var attempt = result.Attempts.Single();
        var search = attempt.Search;
        Assert.That(attempt.Rejection, Is.EqualTo("no-coarse-primary-corridor"), Summary(result) + search.Summary());
        Assert.That(search.Stop, Is.EqualTo(GeodesicValleyCarving.MajorOutletSearchStop.NoCoarseCorridor));
        Assert.That(search.AdmittedLabels, Is.Zero);
        Assert.That(result.VerificationBuilds, Is.Zero);
        Assert.That(result.CommittedVolume, Is.Zero);
    }

    [Test]
    public void DuplicatePrefixLabelsAreDominatedButDistinctChannelGeometryIsRetained()
    {
        var origin = new GeodesicValleyCarving.Label { Vertex = 0, Floor = 1.3 };
        var first = new GeodesicValleyCarving.Label { Vertex = 1, Previous = origin, Floor = 1.2 };
        var existing = new GeodesicValleyCarving.Label { Vertex = 3, Previous = first, Floor = 1.1, Length = .2, Cost = .001, MaxCut = .01 };
        var queue = new List<GeodesicValleyCarving.Label> { existing };
        for (int i = 0; i < 8192; i++)
        {
            var duplicate = new GeodesicValleyCarving.Label { Vertex = 3, Previous = first, Floor = 1.1,
                Length = .2, Cost = .001, MaxCut = .01 };
            if (!queue.Any(a => GeodesicValleyCarving.MajorOutletPrefixDominates(a, duplicate))) queue.Add(duplicate);
        }
        Assert.That(queue, Has.Count.EqualTo(1));
        var alternate = new GeodesicValleyCarving.Label { Vertex = 3, Floor = 1.1, Length = .2, Cost = .001, MaxCut = .01,
            Previous = new GeodesicValleyCarving.Label { Vertex = 2, Previous = origin, Floor = 1.2 } };
        Assert.That(GeodesicValleyCarving.MajorOutletPrefixDominates(existing, alternate), Is.False);
        alternate.Previous = first;
        alternate.Floor += .00001;
        Assert.That(GeodesicValleyCarving.MajorOutletPrefixDominates(existing, alternate), Is.False);
    }

    [Test]
    public void ExactReceiverTraceDistinguishesProtectedWaterAndWrongOcean()
    {
        var fixture = new Fixture(1);
        var path = fixture.Plan().Attempts.Single(a => a.Accepted).Search.ReceiverPath;
        var protectedFacets = new HashSet<int>();
        Assert.That(fixture.ReceiverCheck(path, protectedFacets), Is.EqualTo(GeodesicValleyCarving.MajorReceiverFailure.None));
        Assert.That(fixture.ReceiverCheck(path, protectedFacets, primary: 1),
            Is.EqualTo(GeodesicValleyCarving.MajorReceiverFailure.WrongOcean));
        protectedFacets.Add(path[path.Length / 2].Triangle);
        Assert.That(fixture.ReceiverCheck(path, protectedFacets), Is.EqualTo(GeodesicValleyCarving.MajorReceiverFailure.Protected));
    }

    [Test]
    public void ConfiguredVolumeFractionIsConvertedToCubicPlanetUnits()
    {
        double runA = GeodesicValleyCarving.MajorOutletVolumeInPlanetUnits(8, .000002);
        Assert.That(runA, Is.EqualTo(.001024).Within(1e-12));
        Assert.That(.00008198, Is.LessThan(runA));
        Assert.That(GeodesicValleyCarving.MajorOutletVolumeInPlanetUnits(8, .000005), Is.EqualTo(.00256).Within(1e-12));
    }

    [Test]
    public void VerificationDistinguishesATraceRejectionFromAStrictGradeFailureAfterOceanContact()
    {
        var vertices = new[] { new Vector3(0, 0), new Vector3(0, 1), new Vector3(1, 0),
            new Vector3(1, 1), new Vector3(2, 0), new Vector3(2, 1) };
        var indices = new[] { 0, 2, 1, 2, 3, 1, 2, 4, 3, 4, 5, 3 };
        var ocean = new[] { false, false, false, false, true, true };
        var components = new[] { -1, -1, -1, -1, 0, 0 };
        var flow = new[] { 2f, 2f, 1f, 1f, 0f, 0f };
        var original = Build(1.12f, .02);
        var source = original.Centroid(0);
        var edits = new HashSet<int> { 0, 1 };
        var strict = GeodesicValleyCarving.InspectMajorOutletTrace(original, Build(1.1f, .02),
            source, components, 0, edits);
        Assert.That(strict.Reason, Is.EqualTo("strict-visible-grade"), strict.Summary());
        Assert.That(strict.Termination, Is.EqualTo(TinTermination.OceanCoast));
        Assert.That(strict.HasSegment && strict.StartFacetEdited, Is.True);
        Assert.That(strict.End, Is.EqualTo(strict.Trace[strict.SampleIndex]));
        Assert.That(strict.BeforeEnd, Is.LessThan(strict.BeforeStart));
        Assert.That(strict.AfterEnd - strict.LowestRadius, Is.GreaterThan(strict.Epsilon));
        var trace = GeodesicValleyCarving.InspectMajorOutletTrace(original, Build(1.1f, .001),
            source, components, 0, edits);
        Assert.That(trace.Reason, Is.EqualTo("tin-trace-VisibleGrade"), trace.Summary());
        Assert.That(trace.Termination, Is.EqualTo(TinTermination.RoutingFailure));
        Assert.That(trace.HasSegment, Is.True);
        Assert.That(trace.SampleIndex, Is.EqualTo(-1));

        GeodesicTinHydrology Build(float first, double bump) => new GeodesicTinHydrology(vertices, indices,
            new[] { first, first, 1.11f, 1.11f, 1f, 1f }, flow, ocean, 1.04f,
            visibleBumpTolerance: bump, onSphere: false);
    }

    [Test]
    public void OceanRootedCorridorWithAnUphillIncomingTraceCannotCommit()
    {
        // The existing trace tolerates a tiny sagitta rise on the near-level
        // upstream edge. The proposed outlet's strict check still rejects it.
        var fixture = new Fixture(0, corridor: new[] { 1.11002f, 1.11f, 1.13f, 1.06f, 1.04f, 1f });
        var result = fixture.Plan();
        var attempt = result.Attempts.Single();
        Assert.That(attempt.Search.CorridorsFound, Is.GreaterThan(0), attempt.Search.Summary());
        Assert.That(attempt.Search.FeasibleProfiles, Is.GreaterThan(0), attempt.Search.Summary());
        Assert.That(result.VerificationBuilds, Is.GreaterThan(0));
        Assert.That(attempt.TraceFailure?.Reason, Is.EqualTo("strict-visible-grade"), Summary(result));
        Assert.That(attempt.TraceFailure.SourceRole, Is.EqualTo("prior-network"));
        Assert.That(attempt.TraceFailure.PathComparison.Details, Does.Contain("Original TIN trace"));
        Assert.That(result.Changed, Is.False);
        CollectionAssert.AreEqual(fixture.Tin.VisibleHeight, result.Heights);
    }

    [Test]
    public void PathComparisonIdentifiesTheFirstSteeperBranchOffThePlannedCut()
    {
        var vertices = new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) };
        int[] facets = { 0, 1, 2, 1, 3, 2 };
        var before = new[] { 1.2f, 1.4f, 1.3f, 1.5f };
        var after = new[] { 1.2f, 1.19f, 1.12f, 1.5f };
        var original = new GeodesicTinHydrology(vertices, facets, before, before, onSphere: false);
        var proposed = new GeodesicTinHydrology(vertices, facets, after, after, onSphere: false);
        var trace = proposed.TraceLakeOutlet(proposed.BasinPoint(0, 0), null, out var terminal);
        var comparison = GeodesicValleyCarving.CompareOutletPath(original, proposed, trace,
            new[] { 0, 1 }, new Dictionary<int, float> { [1] = after[1], [2] = after[2] });
        Assert.That(terminal, Is.EqualTo(TinTermination.TrueClosedBasin));
        Assert.That(comparison.DepartureIndex, Is.EqualTo(1), comparison.Summary);
        Assert.That(comparison.PlannedNextVertex, Is.EqualTo(1));
        Assert.That(comparison.ActualSlope, Is.GreaterThan(comparison.PlannedSlope));
        Assert.That(comparison.FirstEditedSample, Is.Zero);
        Assert.That(comparison.Details, Does.Contain("vertices=[0:1.2->1.2,1:1.4->1.19,2:1.3->1.12]"));
    }

    [TestCase(111289, 186269, 186266, 27940, 111284, .22141253, .14380658)]
    [TestCase(104041, 167210, 167208, 104056, 104054, .37324315, .09213819)]
    public void Seed123456DepartureFacetsReproduceTheSteeperUneditedBranch(int basin, int branchFacet,
        int intendedFacet, int departureVertex, int intendedVertex, double branchSlope, double intendedSlope)
    {
        // The two competing facets and their vertex heights come from the
        // hash-matched 1% exclusion replay, before and after its rejected cuts.
        var heights = basin == 111289
            ? new Dictionary<int, (float Before, float After)> {
                [27940] = (8.042542f, 8.040097f), [111285] = (8.043056f, 8.043056f),
                [111284] = (8.038839f, 8.038839f), [111282] = (8.03819752f, 8.03819752f) }
            : new Dictionary<int, (float Before, float After)> {
                [26111] = (8.063778f, 8.063778f), [104056] = (8.059257f, 8.059257f),
                [104055] = (8.05546f, 8.05546f), [6577] = (8.063456f, 8.063456f),
                [104054] = (8.058432f, 8.058432f) };
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(7);
        int[] global = new[] { intendedFacet, branchFacet }
            .SelectMany(f => geometry.Triangles.Skip(f * 3).Take(3)).ToArray();
        var local = global.Distinct().Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        var positions = local.Keys.Select(v => geometry.UnitVertices[v]).ToArray();
        int[] facets = global.Select(v => local[v]).ToArray();
        var before = local.Keys.Select(v => heights[v].Before).ToArray();
        var after = local.Keys.Select(v => heights[v].After).ToArray();
        var edits = local.Keys.Where(v => heights[v].Before != heights[v].After)
            .ToDictionary(v => local[v], v => heights[v].After);
        var original = Build(before);
        var proposed = Build(after);
        var trace = proposed.TraceLakeOutlet(proposed.BasinPoint(1, local[departureVertex]), null, out _, maximumSteps: 1);
        var comparison = GeodesicValleyCarving.CompareOutletPath(original, proposed, trace,
            new[] { local[departureVertex], local[intendedVertex] }, edits);
        Assert.That(comparison.DepartureIndex, Is.EqualTo(1), comparison.Summary);
        Assert.That(comparison.ActualNext.Value.Triangle, Is.EqualTo(1));
        Assert.That(comparison.PlannedNext.Value.Triangle, Is.Zero);
        Assert.That(comparison.ActualSlope, Is.EqualTo(branchSlope).Within(.00001));
        Assert.That(comparison.PlannedSlope, Is.EqualTo(intendedSlope).Within(.00001));
        Assert.That(original.Height(comparison.ActualNext.Value),
            Is.EqualTo(proposed.Height(comparison.ActualNext.Value)).Within(1e-10));

        int next = local[intendedVertex];
        var revised = (float[])after.Clone();
        revised[next] = (float)GeodesicValleyCarving.OutletBranchBedCeiling(proposed,
            comparison.Departure.Value, comparison.ActualNext.Value, comparison.PlannedNext.Value,
            local[departureVertex], next, p => proposed.Height(p), 8 * proposed.Epsilon, constrainFacet: true);
        Assert.That(revised[next], Is.LessThan(after[next]));
        Assert.That(before[next] - revised[next], Is.LessThan(GeodesicValleyCarving.DefaultMajorOutletDepth));
        var corrected = Build(revised);
        var redirected = corrected.TraceLakeOutlet(corrected.BasinPoint(1, local[departureVertex]), null, out _, maximumSteps: 1);
        Assert.That(corrected.OutletDistance(redirected.Last(), comparison.PlannedNext.Value), Is.LessThan(1e-10));

        GeodesicTinHydrology Build(float[] h) => new GeodesicTinHydrology(positions, facets, h, h,
            radius: 8, epsilon: .000002, visibleBumpTolerance: .0007);
    }

    [Test]
    public void StorageEstimateIntegratesDepthAcrossTheSpillFootprint()
    {
        var vertices = new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1) };
        var heights = new[] { 0f, 1f, 1f };
        var tin = new GeodesicTinHydrology(vertices, new[] { 0, 1, 2 }, heights, heights, onSphere: false);
        var basin = new TinBasinDescriptor(0, tin.BasinPoint(0, 0), 0, 1, tin.BasinPoint(0, 1),
            -1, -1, -1, new[] { 0 }, .5, false, false, 0, 0);
        Assert.That(GeodesicValleyCarving.EstimateSpillStorage(tin, basin, out bool oceanExcluded),
            Is.EqualTo(1d / 6d).Within(1e-12));
        Assert.That(oceanExcluded, Is.False);
    }

    private static string Summary(GeodesicValleyCarving.MajorOutletResult result) =>
        $"eligible={result.Eligible} skipped={result.Skipped} " + string.Join(", ", result.Attempts.Select(a =>
            $"{a.BasinId}:{a.Rejection ?? "accepted"}:{a.Destination}:crossed={a.CrossedBasins.Length}:depth={a.RequiredDepth:R}"));

    private sealed class Fixture
    {
        private const float Sea = 1.02f;
        private const double Threshold = .0001;
        private readonly IcosphereRenderGeometry geometry = IcosphereRenderGeometryCache.GetOrBuild(5);
        private readonly GeodesicHydrologyTopology graph;
        private readonly Vector3[] surface;
        private readonly bool[] ocean;
        private readonly int[] components;
        private readonly double[] runoff;
        public GeodesicTinHydrology Tin { get; }

        public Fixture(int basinCount, bool secondary = false, bool belowSea = false, float[] corridor = null,
            (int Station, float Height)[] branches = null)
        {
            graph = GeodesicHydrologyTopology.Build(geometry);
            surface = geometry.UnitVertices.Select(d => d * (1.1f - .3f * d.x - (d.z > 0f ?
                (float)((belowSea ? .11 : .025) * Math.Exp(-(d.x * d.x + d.y * d.y) / .003f) +
                    (basinCount > 1 ? .04 * Math.Exp(-((d.x + .16f) * (d.x + .16f) + d.y * d.y) / .003f) : 0) +
                    (basinCount > 2 ? .04 * Math.Exp(-((d.x + .32f) * (d.x + .32f) + d.y * d.y) / .003f) : 0)) : 0f))).ToArray();
            ocean = geometry.UnitVertices.Select(d => d.x > .2f || (secondary && d.x < -.98f)).ToArray();
            components = geometry.UnitVertices.Select((d, v) => !ocean[v] ? -1 : secondary && d.x > .2f ? 1 : 0).ToArray();
            if (corridor != null)
            {
                var baseMesh = IcosphereRenderGeometryCache.GetOrBuild(0);
                int vertex = baseMesh.Triangles[0], target = baseMesh.Triangles[1];
                Vector3 direction = geometry.UnitVertices[target];
                surface = geometry.UnitVertices.Select(d => d * 1.5f).ToArray();
                Array.Fill(ocean, false);
                Array.Fill(components, -1);
                runoff = new double[geometry.Triangles.Length / 3];
                var route = new List<int>();
                for (int i = 0; i < corridor.Length; i++)
                {
                    route.Add(vertex);
                    float height = corridor[i];
                    surface[vertex] = geometry.UnitVertices[vertex] * height;
                    if (height < Sea) { ocean[vertex] = true; components[vertex] = 0; }
                    if (i == 0 || i == 2 || i == 4)
                        for (int t = 0; t < runoff.Length; t++)
                            if (geometry.Triangles[t * 3] == vertex || geometry.Triangles[t * 3 + 1] == vertex ||
                                geometry.Triangles[t * 3 + 2] == vertex) runoff[t] = .01 / (i + 1);
                    vertex = graph.Neighbors6.Skip(vertex * 6).Take(graph.NeighborCounts[vertex])
                        .OrderByDescending(v => Vector3.Dot(geometry.UnitVertices[v], direction)).First();
                }
                if (branches != null)
                    foreach (var branch in branches)
                    {
                        int junction = route[branch.Station];
                        int side = Neighbors(junction).Where(v => !route.Contains(v))
                            .OrderByDescending(v => Vector3.Dot(geometry.UnitVertices[v], direction)).First();
                        surface[side] = geometry.UnitVertices[side] * branch.Height;
                    }

                IEnumerable<int> Neighbors(int v) => graph.Neighbors6.Skip(v * 6).Take(graph.NeighborCounts[v]);
            }
            Tin = Build(surface);
        }

        public GeodesicValleyCarving.MajorOutletResult Plan(float depth = .05f, double volume = .001,
            GeodesicTinLakeGeometry lakes = null, float width = .007f) => GeodesicValleyCarving.PlanMajorOutlets(Tin, graph,
                ocean, components, 0, GeodesicTinBasins.Build(Tin, 0, double.MaxValue), lakes, 1, Sea,
                depth, volume, 45, 0, width, surface, .001f, 0, .00001);

        public GeodesicValleyCarving.MajorReceiverFailure ReceiverCheck(TinPoint[] path, HashSet<int> protectedFacets,
            int primary = 0) => GeodesicValleyCarving.CheckMajorReceiver(Tin, path, TinTermination.OceanCoast,
                components, primary, protectedFacets);

        private GeodesicTinHydrology Build(Vector3[] positions)
        {
            var heights = positions.Select(p => p.magnitude).ToArray();
            var tin = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, heights, ocean, Sea, 1,
                .00000025, (heights.Max() - (double)heights.Min()) * .001, 0, Threshold,
                subdivision: geometry.SubdivisionLevel, completedSurface: positions, captureParcelDestinations: true);
            if (runoff != null)
            {
                tin.Accumulate(triangleLocalRunoff: runoff);
                tin.ExtractNetworks(Threshold);
            }
            return tin;
        }

        public void AssertRebuild(GeodesicValleyCarving.MajorOutletResult result)
        {
            var projected = surface.Select((p, v) => result.Heights[v] < p.magnitude ? p.normalized * result.Heights[v] : p).ToArray();
            var final = Build(projected);
            double recovered = 0;
            foreach (var attempt in result.Attempts.Where(a => a.Accepted))
            {
                Assert.That(attempt.Volume, Is.InRange(double.Epsilon, .001));
                Assert.That(attempt.ActualDepth, Is.InRange(double.Epsilon, .05));
                foreach (int t in attempt.SourceFacets)
                {
                    var path = final.TraceLakeOutlet(final.Centroid(t), null, out var termination);
                    Assert.That(GeodesicValleyCarving.PrimaryOcean(final, components, 0,
                        new TinParcelDestination(path.Last(), termination)), Is.True);
                    Assert.That(final.VerifyExposedOutlet(path), Is.True);
                }
                recovered += attempt.Parcels.Where(t => GeodesicValleyCarving.PrimaryOcean(final, components, 0,
                    final.ParcelDestinations[t])).Sum(final.ParcelRunoff);
            }
            Assert.That(recovered, Is.GreaterThan(0));
            var original = GeodesicDrainageDelivery.Analyze(Tin, GeodesicTinBasins.Build(Tin));
            var rebuilt = GeodesicDrainageDelivery.Analyze(final, GeodesicTinBasins.Build(final));
            Assert.That(rebuilt.TotalRunoff, Is.EqualTo(original.TotalRunoff).Within(1e-12));
            Assert.That(Math.Abs(rebuilt.Residual), Is.LessThanOrEqualTo(rebuilt.Tolerance));
            Assert.That(final.Diagnostics.VisibleMaterialUphillSegments, Is.Zero);
            for (int t = 0; t < Tin.TriangleCount; t++)
                if (Tin.BasinFacetRetained(t))
                    for (int k = 0; k < 3; k++)
                    {
                        int v = Tin.BasinVertex(t, k);
                        Assert.That(result.Heights[v], Is.EqualTo(Tin.VisibleHeight[v]));
                    }
        }
    }
}
