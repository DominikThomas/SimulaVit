using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlanetResourceMapMathTests
{
    private GameObject testRoot;
    private PlanetResourceMap resourceMap;

    [SetUp]
    public void SetUp()
    {
        testRoot = new GameObject("ResourceMapMathTests");
        PlanetGenerator generator = testRoot.AddComponent<PlanetGenerator>();
        generator.resolution = 6;
        generator.radius = 1f;

        resourceMap = testRoot.AddComponent<PlanetResourceMap>();
        resourceMap.planetGenerator = generator;

        InvokePrivate(resourceMap, "InitializeIfNeeded");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testRoot);
    }

    [Test]
    public void Add_ClampsAtZero_WhenApplyingNegativeDelta()
    {
        int cell = 0;

        resourceMap.Add(ResourceType.CO2, cell, 0.5f);
        resourceMap.Add(ResourceType.CO2, cell, -2.0f);

        float value = resourceMap.Get(ResourceType.CO2, cell);
        Assert.That(value, Is.EqualTo(0f).Within(1e-5f));
    }

    [Test]
    public void AddAndGet_IgnoreInvalidCellIndices()
    {
        int invalidCell = -1;
        int validCell = 0;
        float before = resourceMap.Get(ResourceType.O2, validCell);

        resourceMap.Add(ResourceType.O2, invalidCell, 10f);
        float invalidRead = resourceMap.Get(ResourceType.O2, invalidCell);
        float after = resourceMap.Get(ResourceType.O2, validCell);

        Assert.That(invalidRead, Is.EqualTo(0f));
        Assert.That(after, Is.EqualTo(before).Within(1e-6f));
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Could not find private method '{methodName}'.");
        method.Invoke(instance, null);
    }
}
