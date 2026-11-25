using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class VentVisualizerVfxTests
{
    private GameObject planet;
    private PlanetGenerator generator;
    private VentVisualizer visualizer;
    private GameObject Root => (GameObject)typeof(VentVisualizer).GetField("effectsRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(visualizer);

    [SetUp]
    public void SetUp()
    {
        // Inactive generator: no planet generation, resource initialization or gameplay simulation.
        planet = new GameObject("Vent VFX test planet");
        planet.SetActive(false);
        generator = planet.AddComponent<PlanetGenerator>();
        generator.radius = 8f;
        visualizer = planet.AddComponent<VentVisualizer>();
        visualizer.sootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Vents/Soot_Particle.prefab").GetComponent<ParticleSystem>();
        visualizer.heatDistortionMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Vents/Vent_HeatDistortion_Mat.mat");
        visualizer.BeginGeodesicEffects(generator);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(planet);

    private ParticleSystem AddOutlet(Vector3 direction, float depth = 0.5f, int cell = 1)
    {
        float floor = generator.GeodesicSeaLevelRadius - depth;
        visualizer.AddGeodesicOutletEffect(new GeodesicVentOutlet(GeodesicVentHabitat.Submarine,
            cell, 0, 0, direction.normalized * floor, direction.normalized, 0.75f, 0.08f));
        ParticleSystem[] systems = Root.GetComponentsInChildren<ParticleSystem>(true);
        return systems[systems.Length - 2];
    }

    private static IEnumerable<Vector3> Directions()
    {
        yield return Vector3.up; yield return Vector3.down;
        yield return Vector3.left; yield return Vector3.right;
        yield return Vector3.forward; yield return Vector3.back;
        yield return new Vector3(1f, -2f, 3f).normalized;
    }

    [TestCaseSource(nameof(Directions))]
    public void PrefabParticlesInitiallyTravelRadiallyWithIndependentUnitScale(Vector3 direction)
    {
        planet.transform.SetPositionAndRotation(new Vector3(3f, -7f, 5f), Quaternion.Euler(21f, 37f, -12f));
        planet.transform.localScale = new Vector3(2f, 3f, 4f);
        visualizer.BeginGeodesicEffects(generator);
        ParticleSystem smoke = AddOutlet(direction);
        Vector3 radial = (smoke.transform.position - planet.transform.position).normalized;
        Assert.That(Vector3.Dot(smoke.transform.forward, radial), Is.GreaterThan(0.99999f));
        Assert.That((smoke.transform.lossyScale - Vector3.one).magnitude, Is.LessThan(0.00001f));
        Assert.That(smoke.shape.angle, Is.Zero);
        Assert.That(smoke.shape.randomDirectionAmount, Is.Zero);
        Assert.That(smoke.shape.sphericalDirectionAmount, Is.Zero);
        Assert.That(smoke.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
        Assert.That(smoke.main.prewarm, Is.True);
        Root.SetActive(true); smoke.transform.parent.gameObject.SetActive(true);
        smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        smoke.Emit(24);
        var particles = new ParticleSystem.Particle[24];
        Assert.That(smoke.GetParticles(particles), Is.EqualTo(24));
        foreach (var particle in particles)
        {
            Vector3 velocity = smoke.transform.TransformDirection(particle.velocity);
            Assert.That(Vector3.Dot(velocity.normalized, radial), Is.GreaterThan(0.99999f));
        }
    }

    [Test]
    public void RuntimeFallbackUsesSameRadialConfigurationAndSharedMaterial()
    {
        visualizer.sootPrefab = null;
        ParticleSystem first = AddOutlet(Vector3.down);
        ParticleSystem second = AddOutlet(Vector3.left, cell: 2);
        Assert.That(Vector3.Dot(first.transform.forward, Vector3.down), Is.GreaterThan(0.99999f));
        Assert.That(first.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
        Assert.That(first.main.useUnscaledTime, Is.True);
        Assert.That(first.main.simulationSpeed, Is.EqualTo(1f));
        Assert.That(first.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
        Assert.That(first.main.prewarm, Is.True);
        Assert.That(first.GetComponent<ParticleSystemRenderer>().sharedMaterial,
            Is.SameAs(second.GetComponent<ParticleSystemRenderer>().sharedMaterial));
        Assert.That(first.main.maxParticles, Is.LessThanOrEqualTo(96));
    }

    [TestCase(0.003f)] [TestCase(0.03f)] [TestCase(0.5f)]
    public void LifetimeAndBillboardExtentStayBelowOceanSurfaceEvenWithLargeMultipliers(float depth)
    {
        visualizer.smokeHeightMultiplier = 100f;
        visualizer.smokeWidthMultiplier = 100f;
        visualizer.smokeUpwardSpeedMultiplier = 100f;
        ParticleSystem smoke = AddOutlet(Vector3.forward, depth);
        float distanceToSurface = generator.GeodesicSeaLevelRadius - smoke.transform.position.magnitude;
        float maxTravel = smoke.main.startSpeed.constantMax * smoke.main.startLifetime.constantMax;
        float maxBillboardRadius = smoke.main.startSize.constantMax * 3.6f * 0.5f * Mathf.Sqrt(2f);
        Assert.That(maxTravel + maxBillboardRadius + smoke.shape.radius,
            Is.LessThan(distanceToSurface));
    }

    [Test]
    public void AgedPlumeKeepsUpwardVelocityAndSmallLateralMotion()
    {
        ParticleSystem smoke = AddOutlet(Vector3.down);
        Root.SetActive(true); smoke.transform.parent.gameObject.SetActive(true);
        smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var emission = smoke.emission; emission.enabled = false;
        smoke.Emit(32);
        smoke.Simulate(smoke.main.startLifetime.constantMin * 0.65f, false, false, false);
        var particles = new ParticleSystem.Particle[32];
        int count = smoke.GetParticles(particles);
        Assert.That(count, Is.EqualTo(32));
        for (int i = 0; i < count; i++)
        {
            Assert.That(particles[i].velocity.z, Is.GreaterThan(0f));
            Assert.That(new Vector2(particles[i].velocity.x, particles[i].velocity.y).magnitude,
                Is.LessThan(particles[i].velocity.z * 0.2f));
        }
        Assert.That(smoke.noise.strengthZ.constant, Is.Zero);
    }

    [Test]
    public void VariantsDifferAndMixedAppearanceDoesNotConsumeSimulationRandomness()
    {
        visualizer.smokerAppearance = VentVisualizer.SmokerAppearance.Black;
        ParticleSystem black = AddOutlet(Vector3.forward);
        visualizer.smokerAppearance = VentVisualizer.SmokerAppearance.White;
        ParticleSystem white = AddOutlet(Vector3.right, cell: 2);
        Assert.That(white.main.startColor.color.r, Is.GreaterThan(black.main.startColor.color.r + 0.4f));
        var state = Random.state;
        bool hasBlack = false, hasWhite = false;
        for (int cell = 0; cell < 32; cell++)
        {
            bool isWhite = VentVisualizer.UsesWhiteSmoker(VentVisualizer.SmokerAppearance.Mixed, cell, 2026);
            hasWhite |= isWhite; hasBlack |= !isWhite;
        }
        Assert.That(hasBlack && hasWhite, Is.True);
        Assert.That(Random.state, Is.EqualTo(state));
    }

    [Test]
    public void HeatIsIndependentAndRenderOrderCompositesOceanOverBothEffects()
    {
        visualizer.enableSmoke = false;
        visualizer.smokeHeightMultiplier = 0f;
        visualizer.smokeWidthMultiplier = 0f;
        ParticleSystem smoke = AddOutlet(Vector3.forward);
        var heat = smoke.transform.parent.GetComponentsInChildren<ParticleSystem>(true)[1];
        Assert.That(heat.main.startSize.constantMax, Is.GreaterThan(0.01f));
        Assert.That(heat.main.startLifetime.constantMax, Is.GreaterThan(0.1f));
        Material heatMaterial = heat.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Assert.That(heatMaterial.IsKeywordEnabled("_DISTORTION_ON"), Is.True);
        Assert.That(heatMaterial.IsKeywordEnabled("_SOFTPARTICLES_ON"), Is.True);
        Assert.That(heatMaterial.GetTexture("_BumpMap"), Is.Not.Null);
        Assert.That(heatMaterial.renderQueue, Is.EqualTo(2980));
        Assert.That(smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue, Is.EqualTo(2990));
    }

    [Test]
    public void WiderBaseUsesRadiusAndParticleSizeWithoutChangingLaunchAngles()
    {
        visualizer.smokerAppearance = VentVisualizer.SmokerAppearance.Black;
        visualizer.smokeWidthMultiplier = 1f;
        ParticleSystem original = AddOutlet(Vector3.forward, 1f);
        visualizer.smokeWidthMultiplier = 1.75f;
        ParticleSystem wider = AddOutlet(Vector3.right, 1f, 2);
        Assert.That(wider.shape.radius / original.shape.radius, Is.EqualTo(1.75f).Within(0.0001f));
        Assert.That(wider.main.startSize.constantMax / original.main.startSize.constantMax, Is.EqualTo(1.75f).Within(0.0001f));
        Assert.That(wider.shape.angle, Is.Zero);
        Assert.That(wider.shape.randomDirectionAmount, Is.Zero);
    }

    [Test]
    public void GlowScalingIsUniformNonCumulativeAndIndependentOfParticlesAcrossRebuilds()
    {
        var config = new SimulationStartupConfig();
        visualizer.ApplyStartupVisualSettings(config);
        ParticleSystem smoke = AddOutlet(Vector3.forward);
        float radius = smoke.shape.radius, size = smoke.main.startSize.constantMax;
        var marker = new GameObject("test glow");
        try
        {
            visualizer.RegisterVentGlow(marker.transform, 0.16f);
            Assert.That(marker.transform.localScale.x, Is.EqualTo(0.136f).Within(0.00001f));
            config.ventVisuals.glowSizeMultiplier = 0.7f;
            visualizer.ApplyStartupVisualSettings(config);
            visualizer.ApplyStartupVisualSettings(config);
            Assert.That(marker.transform.localScale.x, Is.EqualTo(0.112f).Within(0.00001f));
            Assert.That(smoke.shape.radius, Is.EqualTo(radius));
            Assert.That(smoke.main.startSize.constantMax, Is.EqualTo(size));
            visualizer.BeginGeodesicEffects(generator);
            smoke = AddOutlet(Vector3.down);
            Assert.That(visualizer.CaptureVisualSettings(), Is.EqualTo(config.ventVisuals));
            Assert.That(smoke.shape.radius, Is.EqualTo(radius));
        }
        finally { Object.DestroyImmediate(marker); }
    }

    [Test]
    public void RegenerationDestroysAllPreviousEmittersAndLandOutletsHaveNoUnderwaterVfx()
    {
        AddOutlet(Vector3.forward);
        GameObject previousRoot = Root;
        visualizer.BeginGeodesicEffects(generator);
        Assert.That(previousRoot == null, Is.True);
        Assert.That(Root.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
        visualizer.AddGeodesicOutletEffect(new GeodesicVentOutlet(GeodesicVentHabitat.Terrestrial,
            2, -1, 0, Vector3.up * 8f, Vector3.up, 1f, 0.08f));
        Assert.That(Root.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
        AddOutlet(Vector3.down);
        Assert.That(Root.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(2));
        visualizer.ClearLegacyRuntimeVisuals("geodesic startup legacy isolation");
        Assert.That(Root.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(2));
        visualizer.ClearRuntimeVisuals("test cleanup");
        Assert.That(Root, Is.Null);
    }

    [Test]
    public void RenderingBudgetAndRepeatedVisibilityChangesNeverResetSmoke()
    {
        visualizer.maxActiveVentEffects = 1;
        ParticleSystem nearest = AddOutlet(Vector3.forward, 0.3f);
        ParticleSystem farther = AddOutlet(new Vector3(0.01f, 0f, 1f), 0.6f, 2);
        Root.SetActive(true);
        typeof(VentVisualizer).GetMethod("ResumeSmokeSimulation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(visualizer, null);
        var cameraObject = new GameObject("Vent VFX test camera");
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = Vector3.forward * (generator.GeodesicSeaLevelRadius + 0.2f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var update = typeof(VentVisualizer).GetMethod("UpdateEffectVisibility", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(visualizer, new object[] { camera });
            Assert.That(nearest.transform.parent.gameObject.activeSelf, Is.True);
            Assert.That(farther.transform.parent.gameObject.activeSelf, Is.True);
            Assert.That(nearest.GetComponent<ParticleSystemRenderer>().enabled, Is.True);
            Assert.That(farther.GetComponent<ParticleSystemRenderer>().enabled, Is.False);
            Assert.That(nearest.particleCount, Is.GreaterThan(0), "Looping smoke is already prewarmed before entering view.");
            Assert.That(farther.particleCount, Is.GreaterThan(0));
            var before = new ParticleSystem.Particle[96];
            var after = new ParticleSystem.Particle[96];
            int count = nearest.GetParticles(before);
            float rate = nearest.emission.rateOverTime.constant;
            for (int rotation = 0; rotation < 3; rotation++)
            {
                camera.transform.position = Vector3.forward * 100f;
                update.Invoke(visualizer, new object[] { camera });
                Assert.That(nearest.GetComponent<ParticleSystemRenderer>().enabled, Is.False);
                camera.transform.position = Vector3.forward * (generator.GeodesicSeaLevelRadius + 0.2f);
                update.Invoke(visualizer, new object[] { camera });
                Assert.That(nearest.GetParticles(after), Is.EqualTo(count));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(after[i].randomSeed, Is.EqualTo(before[i].randomSeed));
                    Assert.That(after[i].remainingLifetime, Is.EqualTo(before[i].remainingLifetime));
                }
                Assert.That(nearest.emission.rateOverTime.constant, Is.EqualTo(rate));
                Assert.That(nearest.isPlaying && farther.isPlaying, Is.True);
            }
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward); // Vent is behind the camera.
            update.Invoke(visualizer, new object[] { camera });
            nearest.Simulate(nearest.main.startLifetime.constantMax + 1f, false, false, false);
            Assert.That(nearest.particleCount, Is.GreaterThan(0), "Offscreen smoke keeps replacing particles over a full lifetime.");
            count = nearest.GetParticles(before);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            update.Invoke(visualizer, new object[] { camera });
            Assert.That(nearest.GetParticles(after), Is.EqualTo(count));
            for (int i = 0; i < count; i++) Assert.That(after[i].randomSeed, Is.EqualTo(before[i].randomSeed));
        }
        finally { Object.DestroyImmediate(cameraObject); }
    }
}
