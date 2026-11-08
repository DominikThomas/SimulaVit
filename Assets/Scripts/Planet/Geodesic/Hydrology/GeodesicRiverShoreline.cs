using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Queries the facets of the generated ocean shell, including its retained-cell boundary.</summary>
internal sealed class GeodesicRiverShoreline
{
    private readonly GeodesicGridTopology simulation;
    private readonly GeodesicOceanConnectivity authority;
    private readonly IcosphereRenderGeometry ocean;
    private readonly int[] cellStarts;
    private readonly GeodesicRiverTerrain fullShell;
    private readonly float radius;
    private int lastCell;

    internal GeodesicRiverShoreline(GeodesicGridTopology simulation, GeodesicOceanConnectivity authority,
        IcosphereRenderGeometry ocean, float radius, bool masked)
    {
        this.simulation = simulation;
        this.authority = authority;
        this.ocean = ocean;
        this.radius = radius;
        if (!masked)
        {
            fullShell = new GeodesicRiverTerrain(ocean, Array.ConvertAll(ocean.UnitVertices, d => d * radius));
            return;
        }
        // The masked builder emits each Voronoi cell's fan in cell order, with
        // each refinement replacing a triangle by four consecutive children.
        int indicesPerWedge = 3 << (2 * (ocean.SubdivisionLevel - simulation.SubdivisionLevel));
        cellStarts = new int[simulation.CellCount + 1];
        for (int cell = 0; cell < simulation.CellCount; cell++)
            cellStarts[cell + 1] = cellStarts[cell] + (authority.OceanMask[cell] ? simulation.NeighborCounts[cell] * indicesPerWedge : 0);
    }

    internal int Cell(Vector3 direction) => lastCell = GeodesicOceanConnectivity.FindNearestCell(simulation, direction, lastCell);

    internal bool TryWaterRadius(Vector3 direction, int component, out float waterRadius)
    {
        int cell = Cell(direction);
        waterRadius = 0f;
        if (fullShell != null)
        {
            if (component >= 0 && authority != null && authority.ComponentId[cell] >= 0 && authority.ComponentId[cell] != component) return false;
            waterRadius = fullShell.Radius(direction);
            return true;
        }
        if (TryCell(cell, out waterRadius)) return true;
        // Nearest-cell dot products can tie at the mask edge. The actual fan
        // determines coverage there, including the boundary of a wet neighbor.
        for (int slot = 0; slot < simulation.NeighborCounts[cell]; slot++)
            if (TryCell(simulation.Neighbors6[cell * 6 + slot], out waterRadius)) return true;
        return false;

        bool TryCell(int candidate, out float height)
        {
            height = 0f;
            if (component >= 0 && authority.ComponentId[candidate] != component) return false;
            for (int t = cellStarts[candidate]; t < cellStarts[candidate + 1]; t += 3)
            {
                Vector3 a = ocean.UnitVertices[ocean.Triangles[t]];
                Vector3 b = ocean.UnitVertices[ocean.Triangles[t + 1]];
                Vector3 c = ocean.UnitVertices[ocean.Triangles[t + 2]];
                if (!Inside(a, b) || !Inside(b, c) || !Inside(c, a)) continue;
                Vector3 normal = Vector3.Cross(b - a, c - a);
                height = radius * Vector3.Dot(normal, a) / Vector3.Dot(normal, direction);
                return true;
            }
            return false;
        }

        bool Inside(Vector3 a, Vector3 b) =>
            ((double)a.y * b.z - (double)a.z * b.y) * direction.x +
            ((double)a.z * b.x - (double)a.x * b.z) * direction.y +
            ((double)a.x * b.y - (double)a.y * b.x) * direction.z >= -1e-11;
    }

    /// <summary>Follow the final route or its retained TIN wet side to the visible shore.</summary>
    internal bool TryContact(GeodesicTinHydrology tin, IGeodesicHydrologyTopology graph,
        TinPoint logicalCoast, int component, Vector3[] path, GeodesicRiverTerrain terrain,
        out int keep, out Vector3[] continuation, IcosphereDirectionMapping renderMapping = null)
    {
        keep = path.Length;
        continuation = null;
        if (path.Length == 0 || !tin.IsRetainedOceanBoundary(logicalCoast)) return false;
        Vector3 end = path[path.Length - 1];
        if (Wet(end))
        {
            while (keep > 1 && Wet(path[keep - 2])) keep--;
            continuation = new[] { keep == 1 ? path[0] : FirstWet(path[keep - 2], path[keep - 1]) };
            if (keep > 1) keep--;
            return true;
        }

        // The shoreline can fall on the far side of an L6 land cell that the
        // L7 flood fills through a low channel. Follow those retained L7 edges
        // for at most two L6 cell spacings; never cross a dry saddle to get there.
        float maxLength = 2f * Mathf.Sqrt(4f * Mathf.PI / simulation.CellCount);
        var nodes = new Dictionary<int, (int Parent, float Length)>();
        var pending = new GeodesicInlandUplift.DistanceHeap();
        for (int corner = 0; corner < 3; corner++) Enqueue(tin.BasinVertex(logicalCoast.Triangle, corner), -1, end, 0f);
        while (pending.TryPop(out int vertex, out float length))
        {
            var node = nodes[vertex];
            if (node.Length != length) continue;
            Vector3 direction = graph.CellDirections[vertex];
            if (Wet(direction))
            {
                Vector3 previous = node.Parent < 0 ? end : graph.CellDirections[node.Parent];
                var extension = new List<Vector3> { FirstWet(previous, direction) };
                for (int v = node.Parent; v >= 0; v = nodes[v].Parent)
                {
                    extension.Add(graph.CellDirections[v]);
                }
                extension.Reverse();
                continuation = extension.ToArray();
                return true;
            }
            for (int slot = 0; slot < graph.NeighborCounts[vertex]; slot++)
                Enqueue(graph.Neighbors6[vertex * 6 + slot], vertex, direction, length);
        }

        // A below-sea render vertex can be seeded by an adjacent simulation
        // ocean cell even across a shallow exposed shelf. Use only the original
        // mapping witnesses of this outlet's wet facet to bridge that mismatch.
        if (component < 0 || authority == null || renderMapping == null) return false;
        float bestDistance = maxLength;
        Vector3 best = default;
        var samples = new List<Vector3>();
        var heights = new List<double>();
        for (int corner = 0; corner < 3; corner++)
        {
            int vertex = tin.BasinVertex(logicalCoast.Triangle, corner);
            if (!tin.BasinVertexRetained(vertex)) continue;
            var mapped = renderMapping.Samples[vertex];
            Consider(mapped.NearestCell);
            for (int i = mapped.NeighborStart; i < mapped.NeighborStart + mapped.NeighborCount; i++)
                Consider(renderMapping.NeighborIndices[i]);
        }
        if (best == default) return false;
        continuation = new[] { best };
        return true;

        void Consider(int cell)
        {
            if (cell < 0 || !authority.OceanMask[cell] || authority.ComponentId[cell] != component) return;
            Vector3 wet = simulation.CellDirections[cell];
            if (!Wet(wet)) return;
            Vector3 candidate = FirstWet(end, wet);
            float distance = Vector3.Distance(end, candidate);
            if (distance >= bestDistance) return;
            samples.Clear(); heights.Clear();
            terrain.SampleCompletedVisiblePath(new[] { end, candidate }, samples, heights);
            // This is a visual relief limit across the shelf. Horizontal
            // connections and modest rises are both allowed.
            foreach (double height in heights)
                if (height > radius * 1.002f) return;
            best = candidate;
            bestDistance = distance;
        }
        bool Wet(Vector3 direction) => TryWaterRadius(direction, component, out float water) && water > terrain.Radius(direction);
        void Enqueue(int vertex, int parent, Vector3 previous, float length)
        {
            if (!tin.BasinVertexRetained(vertex)) return;
            Vector3 direction = graph.CellDirections[vertex];
            length += Vector3.Distance(previous, direction);
            if (length > maxLength || nodes.TryGetValue(vertex, out var old) && length >= old.Length) return;
            float ceiling = radius + radius * .00001f;
            if (terrain.Radius(direction) > ceiling || terrain.Radius((previous + direction).normalized) > ceiling) return;
            nodes[vertex] = (parent, length);
            pending.Push(vertex, length);
        }
        Vector3 FirstWet(Vector3 dry, Vector3 wet)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * .5f;
                if (Wet(Vector3.Lerp(dry, wet, mid).normalized)) hi = mid;
                else lo = mid;
            }
            // A small wet overlap survives shoreline float rounding. Keep it
            // within the witnessed segment so the mouth cannot jump a channel.
            float overlap = Mathf.Min(1f, .00001f / Vector3.Distance(dry, wet));
            Vector3 inside = Vector3.Lerp(dry, wet, Mathf.Min(1f, hi + overlap)).normalized;
            return Wet(inside) ? inside : Vector3.Lerp(dry, wet, hi).normalized;
        }
    }
}
