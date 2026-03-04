using NUnit.Framework;
using UnityEngine;

public class PlanetGridIndexingTests
{
    [Test]
    public void DirectionToCellIndex_SameDirection_ReturnsSameIndex()
    {
        const int resolution = 24;
        Vector3 direction = new Vector3(0.31f, 0.92f, -0.21f).normalized;

        int indexA = PlanetGridIndexing.DirectionToCellIndex(direction, resolution);
        int indexB = PlanetGridIndexing.DirectionToCellIndex(direction, resolution);

        Assert.That(indexA, Is.EqualTo(indexB));
    }

    [Test]
    public void DirectionToCellIndex_OppositeDirections_Differ()
    {
        const int resolution = 24;
        Vector3 direction = new Vector3(0.43f, -0.77f, 0.47f).normalized;

        int indexA = PlanetGridIndexing.DirectionToCellIndex(direction, resolution);
        int indexB = PlanetGridIndexing.DirectionToCellIndex(-direction, resolution);

        Assert.That(indexA, Is.Not.EqualTo(indexB));
    }

    [Test]
    public void DirectionToCellIndex_KnownAndRandomDirections_AreInBounds()
    {
        const int resolution = 16;
        int cellCount = PlanetGridIndexing.GetCellCount(resolution);

        Vector3[] directions =
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back,
            new Vector3(0.3f, 0.5f, 0.8f).normalized,
            new Vector3(-0.6f, 0.1f, 0.79f).normalized,
            new Vector3(0.23f, -0.93f, -0.27f).normalized,
        };

        for (int i = 0; i < directions.Length; i++)
        {
            int index = PlanetGridIndexing.DirectionToCellIndex(directions[i], resolution);
            Assert.That(index, Is.InRange(0, cellCount - 1), $"Direction {directions[i]} mapped to invalid index {index}.");
        }
    }
}
