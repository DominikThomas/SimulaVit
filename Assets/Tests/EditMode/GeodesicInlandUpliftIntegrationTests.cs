using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicInlandUpliftIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Rebuild(PlanetGenerator p)
    {
        typeof(PlanetGenerator).GetMethod("RebuildGeodesicCellTerrainCache", Private).Invoke(p, null);
        typeof(PlanetGenerator).GetMethod("RebuildGeodesicOceanClassification", Private).Invoke(p, null);
    }
    private static Mesh Render(PlanetGenerator p, IcosphereRenderGeometry g, IcosphereDirectionMapping mapping)
    {
        var mesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(g, p.BasePlanetRadius, "Inland uplift test");
        typeof(PlanetGenerator).GetMethod("ApplyGeodesicTerrainDisplacement", Private, null,
            new[] { typeof(Mesh), typeof(IcosphereRenderGeometry), typeof(IcosphereDirectionMapping), typeof(bool), typeof(bool), typeof(bool) }, null)
            .Invoke(p, new object[] { mesh, g, mapping, true, true, true });
        return mesh;
    }
    private static T Field<T>(PlanetGenerator p, string name) => (T)typeof(PlanetGenerator).GetField(name, Private).GetValue(p);

    [Test]
    public void StartupDefaultMissingFieldsRoundTripAndResets()
    {
        var defaults = new SimulationStartupConfig();
        Assert.That(defaults.inlandElevationMagnitude, Is.Zero);
        var enabled = new SimulationStartupConfig { inlandElevationMagnitude = .02f };
        foreach (string json in new[] { "{}", "{\"version\":15}", "{\"version\":16}" })
            Assert.That(SimulationStartupController.DeserializeSavedConfig(json, enabled).inlandElevationMagnitude, Is.Zero);
        var type = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        var saved = type.GetMethod("FromConfig").Invoke(null, new object[] { enabled });
        var loaded = SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), defaults);
        Assert.That(loaded.inlandElevationMagnitude, Is.EqualTo(.02f));
        SimulationStartupController.CopyNormalSettings(defaults, loaded);
        Assert.That(loaded.inlandElevationMagnitude, Is.EqualTo(.02f));
        SimulationStartupController.CopyAdvancedSettings(defaults, loaded);
        Assert.That(loaded.inlandElevationMagnitude, Is.Zero);
        Assert.That(SimulationStartupController.DeserializeSavedConfig("{\"version\":16,\"inlandElevationMagnitude\":1}", defaults).inlandElevationMagnitude,
            Is.EqualTo(GeodesicInlandUplift.MaximumMagnitude));
        Assert.That(SimulationStartupController.DeserializeSavedConfig("{\"version\":16,\"inlandElevationMagnitude\":-1}", defaults).inlandElevationMagnitude, Is.Zero);
    }

    [Test]
    public void GeneratedMeshQueriesColliderOceanAndExistingRiverStateRespectAdditiveOverlay()
    {
        var obj = new GameObject("Inland uplift integration fixture"); obj.SetActive(false);
        Mesh baseline = null, raised = null, repeated = null, restored = null, collider = null;
        try
        {
            var p = obj.AddComponent<PlanetGenerator>();
            p.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 3);
            p.ApplyStartupSeed(123456, false); p.radius = 8f;
            p.enableOcean = true; p.excludeSmallDisconnectedSeas = true;
            p.geodesicSeaLevelControlMode = GeodesicSeaLevelControlMode.TargetAreaCoverage;
            p.geodesicTargetOceanCoveragePercent = 50f;
            var t = GeodesicGridTopology.Build(3);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(p, t);
            var g = IcosphereRenderGeometryCache.GetOrBuild(4); var mapping = IcosphereDirectionMappingBuilder.Build(t, g);
            Assert.That(p.inlandElevationMagnitude, Is.Zero);
            Rebuild(p); baseline = Render(p, g, mapping);
            var mask = p.OceanConnectivity.OceanMask.ToArray();
            var below = p.OceanConnectivity.BelowSeaLevel.ToArray();
            var raw = Field<float[]>(p, "geodesicRawTerrainRadius").ToArray();
            var floor = Field<float[]>(p, "geodesicSeafloorRadius").ToArray();
            var shore = Field<float[]>(p, "geodesicDistanceToShore").ToArray();
            var bathymetryMask = Field<bool[]>(p, "geodesicTerrainBathymetryMask").ToArray();
            float sea = p.GeodesicSeaLevelRadius;
            var original = baseline.vertices;
            // Literal previous render calculation: raw procedural radius -> unchanged bathymetry -> direction * radius.
            for (int i = 0; i < g.VertexCount; i++)
            {
                var d = g.UnitVertices[i];
                float expected = mapping.SampleSeafloorRadius(i, d, p.EvaluateRawGeodesicTerrainRadius(d), sea, bathymetryMask, floor);
                Assert.That(original[i], Is.EqualTo(d * expected));
            }
            obj.AddComponent<MeshFilter>().sharedMesh = baseline;
            var rivers = obj.AddComponent<GeodesicRiverSystem>(); rivers.Initialize(p, g, baseline);
            var tin = rivers.TinDrainage; var receivers = tin.TriangleReceiver.ToArray(); var runoff = tin.AccumulatedRunoff.ToArray();
            var lakeGeometry = rivers.TinLakeGeometry; var lakeRouting = rivers.TinLakeRouting;
            var paths = rivers.RiverPaths.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            p.inlandElevationMagnitude = .02f;
            Rebuild(p); raised = Render(p, g, mapping);
            CollectionAssert.AreEqual(mask, p.OceanConnectivity.OceanMask);
            CollectionAssert.AreEqual(below, p.OceanConnectivity.BelowSeaLevel);
            CollectionAssert.AreEqual(raw, Field<float[]>(p, "geodesicRawTerrainRadius"));
            CollectionAssert.AreEqual(floor, Field<float[]>(p, "geodesicSeafloorRadius"));
            CollectionAssert.AreEqual(shore, Field<float[]>(p, "geodesicDistanceToShore"));
            Assert.That(p.GeodesicSeaLevelRadius, Is.EqualTo(sea));
            var uplift = Field<GeodesicInlandUplift>(p, "geodesicInlandUplift");
            var raisedVertices = raised.vertices; int affected = 0;
            for (int i = 0; i < g.VertexCount; i++)
            {
                Vector3 d = g.UnitVertices[i];
                float offset = uplift.SampleOffset(d, p.inlandElevationMagnitude, mapping.Samples[i].NearestCell);
                if (offset > 1e-6f) affected++;
                if (mask[mapping.Samples[i].NearestCell]) Assert.That(raisedVertices[i], Is.EqualTo(original[i]));
                if (offset == 0f) Assert.That(raisedVertices[i], Is.EqualTo(original[i]));
                float expectedBaseline = mapping.SampleSeafloorRadius(i, d, p.EvaluateRawGeodesicTerrainRadius(d), sea, bathymetryMask, floor);
                Assert.That(raisedVertices[i], Is.EqualTo(d * GeodesicInlandUplift.Add(expectedBaseline, offset)));
                Assert.That(raisedVertices[i].magnitude, Is.EqualTo(p.GetSurfaceRadiusAtDirection(d)).Within(3e-6f));
            }
            Assert.That(affected, Is.GreaterThan(0));
            var cg = IcosphereRenderGeometryCache.GetOrBuild(3); var cm = IcosphereDirectionMappingBuilder.Build(t, cg);
            collider = Render(p, cg, cm);
            CollectionAssert.AreEqual(raisedVertices.Take(cg.VertexCount), collider.vertices);
            for (int c = 0; c < t.CellCount; c++)
            {
                Assert.That(p.GetGeodesicCellSeafloorRadius(c), Is.EqualTo(GeodesicInlandUplift.Add(floor[c], uplift.CellOffset(c, .02f))));
                Assert.That(p.GetCellTerrainHeight(c), Is.EqualTo(GeodesicInlandUplift.Add(p.EvaluateGeodesicTerrainHeight(t.CellDirections[c]), uplift.CellOffset(c, .02f))));
            }
            Assert.That(rivers.TinDrainage, Is.SameAs(tin));
            CollectionAssert.AreEqual(receivers, tin.TriangleReceiver); CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
            Assert.That(rivers.TinLakeGeometry, Is.SameAs(lakeGeometry)); Assert.That(rivers.TinLakeRouting, Is.SameAs(lakeRouting));
            Assert.That(rivers.RiverPaths.Count, Is.EqualTo(paths.Count));
            foreach (var pair in paths) CollectionAssert.AreEqual(pair.Value, rivers.RiverPaths[pair.Key]);
            Rebuild(p); repeated = Render(p, g, mapping);
            CollectionAssert.AreEqual(raisedVertices, repeated.vertices);
            p.inlandElevationMagnitude = 0f;
            Rebuild(p); restored = Render(p, g, mapping);
            CollectionAssert.AreEqual(original, restored.vertices);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(obj);
            foreach (var mesh in new[] { baseline, raised, repeated, restored, collider }) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void DisabledTerrainAndOceanWorldRemainUnchanged()
    {
        var obj = new GameObject("Uplift inactive modes"); obj.SetActive(false);
        try
        {
            var p = obj.AddComponent<PlanetGenerator>();
            p.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 1);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(p, GeodesicGridTopology.Build(1));
            p.enableOcean = true; p.geodesicSeaLevelControlMode = GeodesicSeaLevelControlMode.OceanWorld;
            Rebuild(p);
            var dirs = p.GeodesicTopology.CellDirections;
            var original = dirs.Select(p.GetSurfaceRadiusAtDirection).ToArray();
            p.inlandElevationMagnitude = .02f;
            Rebuild(p);
            CollectionAssert.AreEqual(original, dirs.Select(p.GetSurfaceRadiusAtDirection));
            p.enableGeodesicTerrainDisplacement = false;
            Rebuild(p); original = dirs.Select(p.GetSurfaceRadiusAtDirection).ToArray();
            p.inlandElevationMagnitude = 0f;
            CollectionAssert.AreEqual(original, dirs.Select(p.GetSurfaceRadiusAtDirection));
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }
}
