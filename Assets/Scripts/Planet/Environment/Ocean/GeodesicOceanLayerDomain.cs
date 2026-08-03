using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

/// <summary>Scene-owned configuration and runtime authority for immutable geodesic ocean-layer geometry.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanLayerDomain : MonoBehaviour
{
    [Header("Geodesic Layered Ocean")]
    [SerializeField, Tooltip("Builds the shared layered physical ocean domain after final geodesic bathymetry. No transported state is created.")] private bool enableGeodesicLayeredOcean = true;
    [SerializeField, Range(1, GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount), Tooltip("Maximum globally aligned depth bands (one through five).")] private int maximumLayerCount = 5;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized maximum-depth fraction at which global layer 1 ends and layer 2 begins.")] private float secondLayerBeginsAtDepthFraction = 0.12f;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized maximum-depth fraction at which global layer 2 ends and layer 3 begins.")] private float thirdLayerBeginsAtDepthFraction = 0.32f;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized maximum-depth fraction at which global layer 3 ends and layer 4 begins.")] private float fourthLayerBeginsAtDepthFraction = 0.58f;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized maximum-depth fraction at which global layer 4 ends and layer 5 begins.")] private float fifthLayerBeginsAtDepthFraction = 0.82f;

    [Header("Runtime Diagnostics (Read Only)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int runtimeCellCount, oceanCellCount, runtimeMaximumLayers, activeNodeCount, horizontalLinkCount, verticalLinkCount;
    [SerializeField] private int minimumActiveLayers, maximumActiveLayers;
    [SerializeField] private float meanActiveLayers;
    [SerializeField] private int oceanCellsWithOneLayer, oceanCellsWithTwoLayers, oceanCellsWithThreeLayers, oceanCellsWithFourLayers, oceanCellsWithFiveLayers;
    [SerializeField] private float minimumLayerVolume, meanLayerVolume, maximumLayerVolume, maximumOceanDepth;
    [SerializeField] private double buildDurationMilliseconds;
    [SerializeField] private long approximateMemoryBytes;

    [Header("Manual Sample (Refresh On Change)")]
    [SerializeField, Min(0)] private int sampleCellIndex;
    [SerializeField, TextArea(8, 12)] private string sampleCellOutput = "Grid not initialized.";
    [NonSerialized] private GeodesicOceanLayerGrid grid;
    private PlanetGenerator sourceGenerator;
    public GeodesicOceanLayerGrid Grid => grid;
    public bool Initialized => initialized;

    public void Initialize(PlanetGenerator generator, GeodesicGridTopology topology, GeodesicTransportGraph graph, bool[] oceanMask, float[] seafloorRadius, float seaLevelRadius)
    {
        ClearGrid();
        if (!enableGeodesicLayeredOcean) return;
        if (!TryGetBoundaries(out float[] boundaries, out string error)) { UnityEngine.Debug.LogError($"[GeodesicOceanLayers] Invalid configuration: {error}", this); return; }
        Stopwatch watch = Stopwatch.StartNew();
        try { grid = new GeodesicOceanLayerGrid(topology, graph, oceanMask, seafloorRadius, seaLevelRadius, maximumLayerCount, boundaries); }
        catch (Exception exception) { UnityEngine.Debug.LogError($"[GeodesicOceanLayers] Initialization failed: {exception.Message}", this); ClearGrid(); return; }
        watch.Stop(); sourceGenerator = generator; buildDurationMilliseconds = watch.Elapsed.TotalMilliseconds; initialized = true;
        CacheDiagnostics(); RefreshSampleDiagnostics();
        UnityEngine.Debug.Log($"[GeodesicOceanLayers] initialized cells={runtimeCellCount}, oceanCells={oceanCellCount}, nodeCapacity={grid.NodeCapacity}, activeNodes={activeNodeCount}, horizontalLinks={horizontalLinkCount}, verticalLinks={verticalLinkCount}, histogram1to5={oceanCellsWithOneLayer}/{oceanCellsWithTwoLayers}/{oceanCellsWithThreeLayers}/{oceanCellsWithFourLayers}/{oceanCellsWithFiveLayers}, maxDepth={maximumOceanDepth:F6}, buildMs={buildDurationMilliseconds:F3}, approximateMemory={approximateMemoryBytes} bytes", this);
    }

    public void ClearGrid()
    {
        grid = null; sourceGenerator = null; initialized = false; runtimeCellCount = oceanCellCount = runtimeMaximumLayers = activeNodeCount = horizontalLinkCount = verticalLinkCount = 0;
        minimumActiveLayers = maximumActiveLayers = 0; meanActiveLayers = 0f; oceanCellsWithOneLayer = oceanCellsWithTwoLayers = oceanCellsWithThreeLayers = oceanCellsWithFourLayers = oceanCellsWithFiveLayers = 0;
        minimumLayerVolume = meanLayerVolume = maximumLayerVolume = maximumOceanDepth = 0f; buildDurationMilliseconds = 0d; approximateMemoryBytes = 0; sampleCellOutput = "Grid not initialized.";
    }

    private void OnDestroy() => ClearGrid();
    private void OnValidate() { maximumLayerCount = Mathf.Clamp(maximumLayerCount, 1, 5); if (grid != null) RefreshSampleDiagnostics(); }
    [ContextMenu("Refresh Geodesic Ocean Layer Sample")] private void RefreshSample() => RefreshSampleDiagnostics();

    private bool TryGetBoundaries(out float[] result, out string error)
    {
        float[] configured = { 0f, secondLayerBeginsAtDepthFraction, thirdLayerBeginsAtDepthFraction, fourthLayerBeginsAtDepthFraction, fifthLayerBeginsAtDepthFraction, 1f };
        result = new float[maximumLayerCount + 1]; result[0] = 0f; result[maximumLayerCount] = 1f;
        for (int i = 1; i < maximumLayerCount; i++) result[i] = configured[i];
        for (int i = 0; i < result.Length; i++) if (!Finite(result[i]) || result[i] < 0f || result[i] > 1f) { error = $"boundary {i} must be finite and within [0,1]"; return false; }
        for (int i = 1; i < result.Length; i++) if (result[i] <= result[i - 1]) { error = $"boundaries must be strictly increasing (indices {i - 1}/{i})"; return false; }
        error = null; return true;
    }

    private void CacheDiagnostics()
    {
        runtimeCellCount = grid.CellCount; oceanCellCount = grid.OceanCellCount; runtimeMaximumLayers = grid.MaximumLayerCount; activeNodeCount = grid.ActiveNodeCount;
        horizontalLinkCount = grid.HorizontalLinkCount; verticalLinkCount = grid.VerticalLinkCount; maximumOceanDepth = grid.MaximumOceanDepth; approximateMemoryBytes = grid.ApproximateMemoryBytes;
        minimumActiveLayers = int.MaxValue; double layerSum = 0d, volumeSum = 0d; minimumLayerVolume = float.PositiveInfinity;
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            int layers = grid.ActiveLayerCountByCell[cell]; if (layers == 0) continue;
            minimumActiveLayers = Mathf.Min(minimumActiveLayers, layers); maximumActiveLayers = Mathf.Max(maximumActiveLayers, layers); layerSum += layers;
            if (layers == 1) oceanCellsWithOneLayer++; else if (layers == 2) oceanCellsWithTwoLayers++; else if (layers == 3) oceanCellsWithThreeLayers++; else if (layers == 4) oceanCellsWithFourLayers++; else if (layers == 5) oceanCellsWithFiveLayers++;
            for (int layer = 0; layer < layers; layer++) { float volume = grid.LayerVolume[grid.GetNodeIndex(cell, layer)]; minimumLayerVolume = Mathf.Min(minimumLayerVolume, volume); maximumLayerVolume = Mathf.Max(maximumLayerVolume, volume); volumeSum += volume; }
        }
        if (minimumActiveLayers == int.MaxValue) minimumActiveLayers = 0;
        meanActiveLayers = oceanCellCount > 0 ? (float)(layerSum / oceanCellCount) : 0f; meanLayerVolume = activeNodeCount > 0 ? (float)(volumeSum / activeNodeCount) : 0f;
        if (activeNodeCount == 0) minimumLayerVolume = 0f;
    }

    private void RefreshSampleDiagnostics()
    {
        if (grid == null) { sampleCellOutput = "Grid not initialized."; return; }
        int cell = Mathf.Clamp(sampleCellIndex, 0, grid.CellCount - 1); sampleCellIndex = cell; int[] hd = new int[5], vd = new int[5];
        for (int i = 0; i < grid.HorizontalLinkCount; i++) { int a = grid.HorizontalNodeA[i], b = grid.HorizontalNodeB[i]; if (a / grid.MaximumLayerCount == cell) hd[a % grid.MaximumLayerCount]++; if (b / grid.MaximumLayerCount == cell) hd[b % grid.MaximumLayerCount]++; }
        for (int i = 0; i < grid.VerticalLinkCount; i++) { int a = grid.VerticalUpperNode[i], b = grid.VerticalLowerNode[i]; if (a / grid.MaximumLayerCount == cell) vd[a % grid.MaximumLayerCount]++; if (b / grid.MaximumLayerCount == cell) vd[b % grid.MaximumLayerCount]++; }
        var s = new StringBuilder(); bool ocean = grid.SourceOceanMask[cell]; float depth = ocean ? grid.OceanSurfaceRadius - grid.SourceSeafloorRadius[cell] : 0f;
        s.AppendLine($"Cell {cell}: {(ocean ? "Ocean" : "Land")}; depth={depth:F6}; activeLayers={grid.ActiveLayerCountByCell[cell]}");
        for (int layer = 0; layer < 5; layer++) { bool active = grid.IsNodeActive(cell, layer); int node = layer < grid.MaximumLayerCount ? grid.GetNodeIndex(cell, layer) : -1; s.AppendLine($"L{layer}: {(active ? "active" : "inactive")}; thickness={(active ? grid.LayerThickness[node] : 0f):F6}; volume={(active ? grid.LayerVolume[node] : 0f):G6}; centerDepth={(active ? grid.OceanSurfaceRadius - grid.LayerCenterRadius[node] : 0f):F6}; H/V degree={hd[layer]}/{vd[layer]}"); }
        sampleCellOutput = s.ToString();
    }

    [ContextMenu("Validate Geodesic Ocean Layer Grid")]
    private void ValidateGrid()
    {
        bool valid = Validate(out string report); RefreshSampleDiagnostics();
        if (valid) UnityEngine.Debug.Log($"[GeodesicOceanLayerValidation] {report}", this); else UnityEngine.Debug.LogError($"[GeodesicOceanLayerValidation] {report}", this);
    }

    public bool Validate(out string report)
    {
        if (grid == null) { report = "invalid: grid is not initialized"; return false; }
        int errors = 0; string first = null; void Fail(string message) { errors++; if (first == null) first = message; }
        if (sourceGenerator == null || !ReferenceEquals(sourceGenerator.GeodesicTopology, grid.SourceTopology) || !ReferenceEquals(sourceGenerator.GeodesicTransportGraph, grid.SourceTransportGraph)) Fail("source topology/graph identity mismatch");
        if (grid.CellCount != grid.SourceTopology.CellCount || grid.NodeCapacity != grid.CellCount * grid.MaximumLayerCount) Fail("cell/node capacity mismatch");
        var horizontalKeys = new HashSet<ulong>(); var verticalKeys = new HashSet<ulong>(); int[] verticalDegree = new int[grid.NodeCapacity];
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            int count = grid.ActiveLayerCountByCell[cell]; float depth = grid.SourceOceanMask[cell] ? Mathf.Max(0f, grid.OceanSurfaceRadius - grid.SourceSeafloorRadius[cell]) : 0f;
            if (!grid.SourceOceanMask[cell] && count != 0) Fail("land cell has active layers"); if (grid.SourceOceanMask[cell] && depth > 1e-7f && count == 0) Fail("positive-depth ocean has no layer");
            double thickness = 0d, volume = 0d;
            for (int layer = 0; layer < grid.MaximumLayerCount; layer++) { int node = grid.GetNodeIndex(cell, layer); bool active = layer < count; if (active != (grid.LayerThickness[node] > 0f)) Fail("non-contiguous active layers"); if (!active) { if (grid.LayerThickness[node] != 0f || grid.LayerVolume[node] != 0f) Fail("inactive node has geometry"); continue; } float t = grid.LayerThickness[node], v = grid.LayerVolume[node]; if (!Finite(t) || t <= 0f || !Finite(v) || v <= 0f) Fail("non-finite/non-positive active geometry"); thickness += t; volume += v; }
            float tolerance = 2e-5f * Mathf.Max(1f, depth); if (count > 0 && Mathf.Abs(grid.LayerOuterRadius[grid.GetNodeIndex(cell, 0)] - grid.OceanSurfaceRadius) > tolerance) Fail("top layer does not begin at surface"); if (count > 0 && Mathf.Abs(grid.LayerInnerRadius[grid.GetNodeIndex(cell, count - 1)] - grid.SourceSeafloorRadius[cell]) > tolerance) Fail("bottom layer does not reach seafloor");
            if (Math.Abs(thickness - depth) > tolerance) Fail("layer thickness sum mismatch"); double expectedVolume = grid.SourceTopology.UnitCellAreas[cell] * (Math.Pow(grid.OceanSurfaceRadius, 3) - Math.Pow(grid.OceanSurfaceRadius - depth, 3)) / 3d; if (Math.Abs(volume - expectedVolume) > 5e-5 * Math.Max(1d, expectedVolume)) Fail("column volume sum mismatch");
        }
        for (int i = 0; i < grid.HorizontalLinkCount; i++) { int a = grid.HorizontalNodeA[i], b = grid.HorizontalNodeB[i], edge = grid.HorizontalSourceEdgeIndex[i], layer = grid.HorizontalLayerIndex[i]; if (a < 0 || b < 0 || a >= grid.NodeCapacity || b >= grid.NodeCapacity || a >= b || a % grid.MaximumLayerCount != layer || b % grid.MaximumLayerCount != layer || !grid.IsNodeActive(a / grid.MaximumLayerCount, layer) || !grid.IsNodeActive(b / grid.MaximumLayerCount, layer)) Fail("invalid horizontal link"); if (edge < 0 || edge >= grid.SourceTransportGraph.EdgeCount || grid.SourceTransportGraph.EdgeCellA[edge] != a / grid.MaximumLayerCount || grid.SourceTransportGraph.EdgeCellB[edge] != b / grid.MaximumLayerCount) Fail("invalid horizontal source edge"); if (!Finite(grid.HorizontalOverlapThickness[i]) || grid.HorizontalOverlapThickness[i] <= 0f) Fail("invalid horizontal overlap"); ulong key = ((ulong)(uint)a << 32) | (uint)b; if (!horizontalKeys.Add(key)) Fail("duplicate horizontal link"); }
        for (int i = 0; i < grid.VerticalLinkCount; i++) { int a = grid.VerticalUpperNode[i], b = grid.VerticalLowerNode[i]; if (a < 0 || b >= grid.NodeCapacity || a / grid.MaximumLayerCount != b / grid.MaximumLayerCount || b != a + 1 || !grid.IsNodeActive(a / grid.MaximumLayerCount, a % grid.MaximumLayerCount) || !grid.IsNodeActive(b / grid.MaximumLayerCount, b % grid.MaximumLayerCount)) Fail("invalid vertical link"); if (!Finite(grid.VerticalInterfaceArea[i]) || grid.VerticalInterfaceArea[i] <= 0f || !Finite(grid.VerticalCenterDistance[i]) || grid.VerticalCenterDistance[i] <= 0f) Fail("invalid vertical geometry"); ulong key = ((ulong)(uint)a << 32) | (uint)b; if (!verticalKeys.Add(key)) Fail("duplicate vertical link"); verticalDegree[a]++; verticalDegree[b]++; }
        for (int cell = 0; cell < grid.CellCount; cell++) for (int layer = 0; layer < grid.ActiveLayerCountByCell[cell]; layer++) { int expected = grid.ActiveLayerCountByCell[cell] == 1 ? 0 : (layer == 0 || layer == grid.ActiveLayerCountByCell[cell] - 1 ? 1 : 2); if (verticalDegree[grid.GetNodeIndex(cell, layer)] != expected) Fail("vertical degree mismatch"); }
        report = errors == 0 ? $"valid; cells={grid.CellCount}, oceanCells={grid.OceanCellCount}, activeNodes={grid.ActiveNodeCount}, horizontalLinks={grid.HorizontalLinkCount}, verticalLinks={grid.VerticalLinkCount}" : $"invalid; errors={errors}, first={first}"; return errors == 0;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
