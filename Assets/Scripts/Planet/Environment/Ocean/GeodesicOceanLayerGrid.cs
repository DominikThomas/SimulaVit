using System;
using UnityEngine;

/// <summary>Immutable geometry and connectivity for globally aligned geodesic ocean layers.</summary>
public sealed class GeodesicOceanLayerGrid
{
    public const int AbsoluteMaximumLayerCount = 5;
    private const float ActiveEpsilon = 1e-7f;

    public GeodesicGridTopology SourceTopology { get; }
    public GeodesicTransportGraph SourceTransportGraph { get; }
    /// <summary>Authoritative generated arrays. Read-only by contract.</summary>
    public bool[] SourceOceanMask { get; }
    /// <summary>Authoritative generated arrays. Read-only by contract.</summary>
    public float[] SourceSeafloorRadius { get; }
    public int CellCount { get; }
    public int MaximumLayerCount { get; }
    public int NodeCapacity { get; }
    public int OceanCellCount { get; }
    public int ActiveNodeCount { get; }
    public float OceanSurfaceRadius { get; }
    public float MaximumOceanDepth { get; }

    /// <summary>Compact runtime arrays are read-only by contract.</summary>
    public byte[] ActiveLayerCountByCell { get; }
    public float[] LayerOuterRadius { get; }
    public float[] LayerInnerRadius { get; }
    public float[] LayerCenterRadius { get; }
    public float[] LayerThickness { get; }
    public float[] LayerVolume { get; }
    public int HorizontalLinkCount { get; }
    public int[] HorizontalNodeA { get; }
    public int[] HorizontalNodeB { get; }
    public int[] HorizontalSourceEdgeIndex { get; }
    public byte[] HorizontalLayerIndex { get; }
    public float[] HorizontalOverlapThickness { get; }
    public int VerticalLinkCount { get; }
    public int[] VerticalUpperNode { get; }
    public int[] VerticalLowerNode { get; }
    public float[] VerticalInterfaceArea { get; }
    public float[] VerticalCenterDistance { get; }
    public long ApproximateMemoryBytes { get; }

    public GeodesicOceanLayerGrid(GeodesicGridTopology topology, GeodesicTransportGraph graph,
        bool[] oceanMask, float[] seafloorRadius, float oceanSurfaceRadius, int maximumLayers, float[] normalizedBoundaries)
    {
        SourceTopology = topology ?? throw new ArgumentNullException(nameof(topology));
        SourceTransportGraph = graph ?? throw new ArgumentNullException(nameof(graph));
        SourceOceanMask = oceanMask ?? throw new ArgumentNullException(nameof(oceanMask));
        SourceSeafloorRadius = seafloorRadius ?? throw new ArgumentNullException(nameof(seafloorRadius));
        if (!ReferenceEquals(graph.SourceTopology, topology)) throw new ArgumentException("Transport graph belongs to another topology.");
        CellCount = topology.CellCount;
        if (oceanMask.Length != CellCount || seafloorRadius.Length != CellCount) throw new ArgumentException("Ocean data count does not match topology.");
        MaximumLayerCount = Mathf.Clamp(maximumLayers, 1, AbsoluteMaximumLayerCount);
        if (normalizedBoundaries == null || normalizedBoundaries.Length != MaximumLayerCount + 1) throw new ArgumentException("Boundary count must be maximumLayers + 1.");
        OceanSurfaceRadius = oceanSurfaceRadius;
        NodeCapacity = CellCount * MaximumLayerCount;

        float maxDepth = 0f;
        int oceanCells = 0;
        for (int cell = 0; cell < CellCount; cell++) if (oceanMask[cell]) { oceanCells++; maxDepth = Mathf.Max(maxDepth, oceanSurfaceRadius - seafloorRadius[cell]); }
        OceanCellCount = oceanCells;
        MaximumOceanDepth = maxDepth;
        float[] depthBoundaries = new float[MaximumLayerCount + 1];
        for (int i = 0; i <= MaximumLayerCount; i++) depthBoundaries[i] = normalizedBoundaries[i] * maxDepth;

        ActiveLayerCountByCell = new byte[CellCount];
        LayerOuterRadius = new float[NodeCapacity]; LayerInnerRadius = new float[NodeCapacity];
        LayerCenterRadius = new float[NodeCapacity]; LayerThickness = new float[NodeCapacity]; LayerVolume = new float[NodeCapacity];
        int activeNodes = 0, horizontalCapacity = graph.EdgeCount * MaximumLayerCount, verticalCapacity = CellCount * (MaximumLayerCount - 1);
        int[] hA = new int[horizontalCapacity], hB = new int[horizontalCapacity], hEdge = new int[horizontalCapacity];
        byte[] hLayer = new byte[horizontalCapacity]; float[] hOverlap = new float[horizontalCapacity];
        int[] vUpper = new int[verticalCapacity], vLower = new int[verticalCapacity];
        float[] vArea = new float[verticalCapacity], vDistance = new float[verticalCapacity];

        for (int cell = 0; cell < CellCount; cell++)
        {
            if (!oceanMask[cell]) continue;
            float floor = seafloorRadius[cell];
            for (int layer = 0; layer < MaximumLayerCount; layer++)
            {
                float outer = oceanSurfaceRadius - depthBoundaries[layer];
                float inner = Mathf.Max(floor, oceanSurfaceRadius - depthBoundaries[layer + 1]);
                float thickness = outer - inner;
                if (thickness <= ActiveEpsilon) break;
                int node = GetNodeIndex(cell, layer);
                LayerOuterRadius[node] = outer; LayerInnerRadius[node] = inner;
                LayerCenterRadius[node] = (outer + inner) * 0.5f; LayerThickness[node] = thickness;
                LayerVolume[node] = topology.UnitCellAreas[cell] * (outer * outer * outer - inner * inner * inner) / 3f;
                ActiveLayerCountByCell[cell]++; activeNodes++;
            }
        }
        ActiveNodeCount = activeNodes;

        int hc = 0;
        for (int edge = 0; edge < graph.EdgeCount; edge++)
        {
            int cellA = graph.EdgeCellA[edge], cellB = graph.EdgeCellB[edge];
            for (int layer = 0; layer < MaximumLayerCount; layer++)
            {
                if (!IsNodeActive(cellA, layer) || !IsNodeActive(cellB, layer)) continue;
                int a = GetNodeIndex(cellA, layer), b = GetNodeIndex(cellB, layer);
                float overlap = Mathf.Min(LayerOuterRadius[a], LayerOuterRadius[b]) - Mathf.Max(LayerInnerRadius[a], LayerInnerRadius[b]);
                if (overlap <= ActiveEpsilon) continue;
                hA[hc] = a; hB[hc] = b; hEdge[hc] = edge; hLayer[hc] = (byte)layer; hOverlap[hc] = overlap; hc++;
            }
        }
        HorizontalLinkCount = hc; HorizontalNodeA = Trim(hA, hc); HorizontalNodeB = Trim(hB, hc);
        HorizontalSourceEdgeIndex = Trim(hEdge, hc); HorizontalLayerIndex = Trim(hLayer, hc); HorizontalOverlapThickness = Trim(hOverlap, hc);

        int vc = 0;
        for (int cell = 0; cell < CellCount; cell++) for (int layer = 0; layer + 1 < ActiveLayerCountByCell[cell]; layer++)
        {
            int upper = GetNodeIndex(cell, layer), lower = upper + 1;
            float interfaceRadius = LayerInnerRadius[upper];
            vUpper[vc] = upper; vLower[vc] = lower;
            vArea[vc] = topology.UnitCellAreas[cell] * interfaceRadius * interfaceRadius;
            vDistance[vc] = LayerCenterRadius[upper] - LayerCenterRadius[lower]; vc++;
        }
        VerticalLinkCount = vc; VerticalUpperNode = Trim(vUpper, vc); VerticalLowerNode = Trim(vLower, vc);
        VerticalInterfaceArea = Trim(vArea, vc); VerticalCenterDistance = Trim(vDistance, vc);
        ApproximateMemoryBytes = CellCount + (long)NodeCapacity * 20L + (long)hc * 17L + (long)vc * 16L;
    }

    public int GetNodeIndex(int cellIndex, int layerIndex) => cellIndex * MaximumLayerCount + layerIndex;
    public bool IsNodeActive(int cellIndex, int layerIndex) => cellIndex >= 0 && cellIndex < CellCount && layerIndex >= 0 && layerIndex < ActiveLayerCountByCell[cellIndex];
    public int GetTopLayerIndex(int cellIndex) => ActiveLayerCountByCell[cellIndex] > 0 ? 0 : -1;
    public int GetBottomLayerIndex(int cellIndex) => ActiveLayerCountByCell[cellIndex] - 1;
    private static T[] Trim<T>(T[] source, int count) { if (source.Length == count) return source; T[] result = new T[count]; Array.Copy(source, result, count); return result; }
}
