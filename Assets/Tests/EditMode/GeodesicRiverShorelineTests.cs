using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverShorelineTests
{
    [Test]
    public void L7CoastContinuesToTheL6OceanFanUsingTheSameRetainedWetSide()
    {
        const float sea = 8f;
        var simulation = GeodesicGridTopology.Build(6);
        var radii = simulation.CellDirections.Select(d => d.x > .03f ? sea - .02f : sea + .02f).ToArray();
        var authority = GeodesicOceanConnectivity.Build(simulation, radii, sea, true, true, .01f);
        var ocean = GeodesicMaskedOceanGeometry.Build(simulation, authority.OceanMask, 5, out _);
        var shoreline = new GeodesicRiverShoreline(simulation, authority, ocean, sea, true);
        int component = authority.PrimaryRetainedComponent;
        var render = IcosphereRenderGeometryCache.GetOrBuild(7);
        var graph = GeodesicHydrologyTopology.Build(render);
        int dry = -1, wet = -1, facet = -1;
        for (int t = 0; t < render.TriangleCount && facet < 0; t++)
            for (int k = 0; k < 3 && facet < 0; k++)
            {
                int a = render.Triangles[t * 3 + k], b = render.Triangles[t * 3 + (k + 1) % 3];
                Vector3 direction = render.UnitVertices[b];
                if (Mathf.Abs(direction.x - .03f) > .015f || shoreline.TryWaterRadius(direction, component, out _) ||
                    shoreline.TryWaterRadius(render.UnitVertices[a], component, out _)) continue;
                for (int slot = 0; slot < graph.NeighborCounts[b]; slot++)
                    if (shoreline.TryWaterRadius(render.UnitVertices[graph.Neighbors6[b * 6 + slot]], component, out _))
                    { dry = a; wet = b; facet = t; break; }
            }
        Assert.That(facet, Is.GreaterThanOrEqualTo(0));
        var heights = Enumerable.Repeat(sea - .01f, render.VertexCount).ToArray();
        var retained = Enumerable.Repeat(true, render.VertexCount).ToArray();
        heights[dry] = sea + .003f;
        retained[dry] = false;
        var terrainPositions = render.UnitVertices.Select((d, i) => d * heights[i]).ToArray();
        var terrain = new GeodesicRiverTerrain(render, terrainPositions);
        var tin = new GeodesicTinHydrology(render.UnitVertices, render.Triangles, heights, heights, retained,
            sea, sea, subdivisionLevel: 7, completedSurface: terrainPositions);
        Assert.That(tin.TryCoast(tin.BasinPoint(facet, dry), tin.BasinPoint(facet, wet), out var coast), Is.True);
        var logical = tin.Direction(coast);
        Assert.That(shoreline.TryWaterRadius(logical, component, out _), Is.False);

        var path = new[] { render.UnitVertices[dry], logical };
        Assert.That(shoreline.TryContact(tin, graph, coast, component, path, terrain, out int keep, out var extension), Is.True);
        Assert.That(keep, Is.EqualTo(path.Length));
        Vector3 contact = extension.Last();
        Assert.That(shoreline.TryWaterRadius(contact, component, out float water), Is.True);
        Assert.That(water, Is.GreaterThan(terrain.Radius(contact)));
        Assert.That(Vector3.Distance(logical, contact), Is.InRange(1e-5f, .02f));
        Assert.That(shoreline.TryContact(tin, graph, coast, component + 1, path, terrain, out _, out _), Is.False);
    }
}
