using System;
using Unity.Profiling;
using UnityEngine;

public enum GeodesicOceanResource { CO2 = 0, O2 = 1, CH4 = 2, H2 = 3, H2S = 4, Fe2 = 5, OrganicC = 6 }

[Serializable]
public struct GeodesicOceanResourceDiagnostics
{
    public GeodesicOceanResource resource;
    public float minimumActiveConcentration;
    public double volumeWeightedMeanConcentration;
    public float maximumActiveConcentration;
    public double globalInventory;
}

/// <summary>
/// Scene-owned authoritative dissolved-resource state for active geodesic ocean-layer nodes.
/// Values are concentrations per layer volume; inventory is concentration * GeodesicOceanLayerGrid.LayerVolume[node].
/// Storage is one channel-major float buffer indexed as resource * nodeCapacity + fixed node index from GeodesicOceanLayerGrid.
/// At subdivision 5 with 10,242 cells, five layers and seven channels this buffer is about 1.37 MiB plus active-node traversal and volume caches.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanResourceField : MonoBehaviour
{
    private const int ResourceCount = 7;
    private static readonly ProfilerMarker InitializeMarker = new ProfilerMarker("GeodesicOceanResource.Initialize");

    [Header("Startup Concentrations (Geodesic Dissolved Ocean)")]
    [SerializeField, Min(0f)] private float initialCO2Concentration = 1f;
    [SerializeField, Min(0f)] private float initialO2Concentration = 0.21f;
    [SerializeField, Min(0f)] private float initialCH4Concentration = 0f;
    [SerializeField, Min(0f)] private float initialFe2Concentration = 0f;

    [Header("Runtime Diagnostics (Read Only)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int cellCount;
    [SerializeField] private int nodeCapacity;
    [SerializeField] private int activeNodeCount;
    [SerializeField] private double activeOceanVolume;
    [SerializeField] private long approximateRuntimeMemoryBytes;
    [SerializeField] private int initializationCount;
    [SerializeField] private int clearCount;
    [SerializeField] private int invalidQueryCount;
    [SerializeField] private int rejectedNonfiniteWriteCount;
    [SerializeField] private int rejectedNegativeWriteCount;
    [SerializeField] private GeodesicOceanResourceDiagnostics[] resourceDiagnostics = CreateDiagnosticsArray();

    private GeodesicOceanLayerDomain domain;
    private GeodesicOceanLayerGrid sourceGrid;
    private float[] concentrationsByResourceThenNode;
    private int[] activeNodeIndices;
    private float[] activeNodeVolumes;
    private float[] configuredInitialConcentrations = new float[ResourceCount];

    public bool IsInitialized => initialized;
    public int CellCount => cellCount;
    public int NodeCapacity => nodeCapacity;
    public int ActiveNodeCount => activeNodeCount;
    public double ActiveOceanVolume => activeOceanVolume;
    public long ApproximateRuntimeMemoryBytes => approximateRuntimeMemoryBytes;

    private void Awake() => domain = GetComponent<GeodesicOceanLayerDomain>();
    private void OnDestroy() => ClearField();

    public void SetStartupConcentrations(float co2, float o2, float ch4, float fe2)
    {
        initialCO2Concentration = Mathf.Max(0f, co2);
        initialO2Concentration = Mathf.Max(0f, o2);
        initialCH4Concentration = Mathf.Max(0f, ch4);
        initialFe2Concentration = Mathf.Max(0f, fe2);
    }

    public void InitializeForCurrentDomain()
    {
        using (InitializeMarker.Auto())
        {
            ClearField(false);
            domain ??= GetComponent<GeodesicOceanLayerDomain>();
            GeodesicOceanLayerGrid grid = domain != null ? domain.Grid : null;
            PlanetGenerator generator = GetComponent<PlanetGenerator>();
            if (generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || domain == null || !domain.Initialized || grid == null) { ClearField(); return; }

            sourceGrid = grid; cellCount = grid.CellCount; nodeCapacity = grid.NodeCapacity; activeNodeCount = grid.ActiveNodeCount;
            concentrationsByResourceThenNode = new float[ResourceCount * nodeCapacity];
            activeNodeIndices = new int[activeNodeCount]; activeNodeVolumes = new float[activeNodeCount];
            configuredInitialConcentrations[(int)GeodesicOceanResource.CO2] = initialCO2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.O2] = initialO2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.CH4] = initialCH4Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.H2] = 0f;
            configuredInitialConcentrations[(int)GeodesicOceanResource.H2S] = 0f;
            configuredInitialConcentrations[(int)GeodesicOceanResource.Fe2] = initialFe2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.OrganicC] = 0f;
            int cursor = 0; double volume = 0d;
            for (int cell = 0; cell < grid.CellCount; cell++) for (int layer = 0; layer < grid.ActiveLayerCountByCell[cell]; layer++)
            {
                int node = grid.GetNodeIndex(cell, layer); activeNodeIndices[cursor] = node; activeNodeVolumes[cursor] = grid.LayerVolume[node]; volume += grid.LayerVolume[node];
                for (int r = 0; r < ResourceCount; r++) concentrationsByResourceThenNode[r * nodeCapacity + node] = configuredInitialConcentrations[r];
                cursor++;
            }
            activeOceanVolume = volume; approximateRuntimeMemoryBytes = (long)concentrationsByResourceThenNode.Length * sizeof(float) + (long)activeNodeIndices.Length * sizeof(int) + (long)activeNodeVolumes.Length * sizeof(float) + ResourceCount * 32L;
            initialized = true; initializationCount++; RecomputeDiagnostics();
            Debug.Log($"[GeodesicOceanResource] initialized dissolved-ocean concentrations (not atmosphere; not legacy normalized totals): cells={cellCount}, nodeCapacity={nodeCapacity}, activeNodes={activeNodeCount}, volume={activeOceanVolume:G6}, memory={approximateRuntimeMemoryBytes} bytes, CO2={initialCO2Concentration:G6}, O2={initialO2Concentration:G6}, CH4={initialCH4Concentration:G6}, H2=0, H2S=0, Fe2={initialFe2Concentration:G6}, OrganicC=0", this);
        }
    }

    public void ClearField() => ClearField(true);
    private void ClearField(bool countClear)
    {
        concentrationsByResourceThenNode = null; activeNodeIndices = null; activeNodeVolumes = null; sourceGrid = null; initialized = false; cellCount = nodeCapacity = activeNodeCount = 0; activeOceanVolume = 0d; approximateRuntimeMemoryBytes = 0; ResetDiagnostics(); if (countClear) clearCount++;
    }

    public bool TryGetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, out float concentration)
    {
        concentration = 0f; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; concentration = concentrationsByResourceThenNode[offset]; return true;
    }
    public bool TrySetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float concentration)
    {
        if (!ValidateWriteValue(concentration)) return false; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; concentrationsByResourceThenNode[offset] = concentration; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float deltaConcentration)
    {
        if (!Finite(deltaConcentration)) { rejectedNonfiniteWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; float next = concentrationsByResourceThenNode[offset] + deltaConcentration; if (next < 0f) { rejectedNegativeWriteCount++; return false; } concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddInventory(int cellIndex, int layerIndex, GeodesicOceanResource resource, double inventoryDelta)
    {
        if (!Finite(inventoryDelta)) { rejectedNonfiniteWriteCount++; return false; } if (inventoryDelta < 0d) { rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); float next = concentrationsByResourceThenNode[offset] + (float)(inventoryDelta / sourceGrid.LayerVolume[node]); if (next < 0f) { rejectedNegativeWriteCount++; return false; } concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryWithdrawInventoryBounded(int cellIndex, int layerIndex, GeodesicOceanResource resource, double requestedInventory, out double withdrawnInventory)
    {
        withdrawnInventory = 0d; if (!Finite(requestedInventory) || requestedInventory < 0d) { if (!Finite(requestedInventory)) rejectedNonfiniteWriteCount++; else rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); double available = concentrationsByResourceThenNode[offset] * (double)sourceGrid.LayerVolume[node]; withdrawnInventory = Math.Min(requestedInventory, available); concentrationsByResourceThenNode[offset] = (float)((available - withdrawnInventory) / sourceGrid.LayerVolume[node]); RecomputeDiagnosticsFor(resource); return true;
    }
    public float GetNodeInventory(int cellIndex, int layerIndex, GeodesicOceanResource resource) => TryGetConcentration(cellIndex, layerIndex, resource, out float c) ? c * sourceGrid.LayerVolume[sourceGrid.GetNodeIndex(cellIndex, layerIndex)] : 0f;
    public double GetGlobalInventory(GeodesicOceanResource resource) => IsResourceValid(resource) && resourceDiagnostics != null ? resourceDiagnostics[(int)resource].globalInventory : 0d;
    public double GetVolumeWeightedMeanConcentration(GeodesicOceanResource resource) => IsResourceValid(resource) && resourceDiagnostics != null ? resourceDiagnostics[(int)resource].volumeWeightedMeanConcentration : 0d;

    private bool TryResolveNode(int cellIndex, int layerIndex, GeodesicOceanResource resource, out int offset)
    {
        offset = 0; if (!initialized || sourceGrid == null || concentrationsByResourceThenNode == null || !IsResourceValid(resource) || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) { invalidQueryCount++; return false; } offset = (int)resource * nodeCapacity + sourceGrid.GetNodeIndex(cellIndex, layerIndex); return true;
    }
    private bool ValidateWriteValue(float value) { if (!Finite(value)) { rejectedNonfiniteWriteCount++; return false; } if (value < 0f) { rejectedNegativeWriteCount++; return false; } return true; }
    private static bool IsResourceValid(GeodesicOceanResource r) => (int)r >= 0 && (int)r < ResourceCount;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private void RecomputeDiagnostics() { for (int r = 0; r < ResourceCount; r++) RecomputeDiagnosticsFor((GeodesicOceanResource)r); }
    private void RecomputeDiagnosticsFor(GeodesicOceanResource resource)
    {
        if (resourceDiagnostics == null || resourceDiagnostics.Length != ResourceCount) resourceDiagnostics = CreateDiagnosticsArray(); int r = (int)resource; double inventory = 0d; float min = float.PositiveInfinity, max = 0f;
        for (int i = 0; i < activeNodeCount; i++) { float c = concentrationsByResourceThenNode[r * nodeCapacity + activeNodeIndices[i]]; if (c < min) min = c; if (c > max) max = c; inventory += c * (double)activeNodeVolumes[i]; }
        resourceDiagnostics[r].resource = resource; resourceDiagnostics[r].minimumActiveConcentration = activeNodeCount > 0 ? min : 0f; resourceDiagnostics[r].maximumActiveConcentration = activeNodeCount > 0 ? max : 0f; resourceDiagnostics[r].globalInventory = inventory; resourceDiagnostics[r].volumeWeightedMeanConcentration = activeOceanVolume > 0d ? inventory / activeOceanVolume : 0d;
    }
    private void ResetDiagnostics() { if (resourceDiagnostics == null || resourceDiagnostics.Length != ResourceCount) resourceDiagnostics = CreateDiagnosticsArray(); for (int i = 0; i < ResourceCount; i++) resourceDiagnostics[i] = new GeodesicOceanResourceDiagnostics { resource = (GeodesicOceanResource)i }; }
    private static GeodesicOceanResourceDiagnostics[] CreateDiagnosticsArray() { var a = new GeodesicOceanResourceDiagnostics[ResourceCount]; for (int i = 0; i < ResourceCount; i++) a[i].resource = (GeodesicOceanResource)i; return a; }

    [ContextMenu("Validate Geodesic Ocean Resource Field")]
    private void ValidateContextMenu()
    {
        bool valid = ValidateField(out string report); if (valid) Debug.Log($"[GeodesicOceanResourceValidation] {report}", this); else Debug.LogError($"[GeodesicOceanResourceValidation] {report}", this);
    }

    public bool ValidateField(out string report)
    {
        if (!initialized || sourceGrid == null || concentrationsByResourceThenNode == null) { report = "invalid: field is not initialized"; return false; }
        int errors = 0; string first = null; void Fail(string m) { errors++; if (first == null) first = m; }
        if (domain == null || !ReferenceEquals(domain.Grid, sourceGrid)) Fail("source grid identity mismatch");
        if (concentrationsByResourceThenNode.Length != ResourceCount * nodeCapacity || nodeCapacity != sourceGrid.NodeCapacity) Fail("storage dimensions mismatch");
        int active = 0; double volume = 0d; for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            if (!sourceGrid.SourceOceanMask[cell] && sourceGrid.ActiveLayerCountByCell[cell] != 0) Fail("land cell has active resource nodes");
            for (int layer = 0; layer < sourceGrid.MaximumLayerCount; layer++)
            {
                int node = sourceGrid.GetNodeIndex(cell, layer); bool isActive = sourceGrid.IsNodeActive(cell, layer); if (isActive) { active++; volume += sourceGrid.LayerVolume[node]; }
                for (int r = 0; r < ResourceCount; r++) { float c = concentrationsByResourceThenNode[r * nodeCapacity + node]; if (isActive) { if (!Finite(c) || c < 0f) Fail("active concentration is non-finite or negative"); } else if (c != 0f) Fail("inactive concentration slot is non-zero"); }
            }
        }
        if (active != sourceGrid.ActiveNodeCount || active != activeNodeCount) Fail("active-node count mismatch");
        for (int r = 0; r < ResourceCount; r++) { double sum = 0d; for (int i = 0; i < activeNodeCount; i++) sum += concentrationsByResourceThenNode[r * nodeCapacity + activeNodeIndices[i]] * (double)activeNodeVolumes[i]; if (Math.Abs(sum - resourceDiagnostics[r].globalInventory) > Math.Max(1e-6, Math.Abs(sum) * 1e-6)) Fail("global inventory mismatch"); double mean = volume > 0d ? sum / volume : 0d; if (Math.Abs(mean - resourceDiagnostics[r].volumeWeightedMeanConcentration) > 1e-6) Fail("volume-weighted mean mismatch"); }
        int invalidBefore = invalidQueryCount; double o2Before = resourceDiagnostics[(int)GeodesicOceanResource.O2].globalInventory; TryGetConcentration(-1, -1, GeodesicOceanResource.O2, out _); if (invalidQueryCount != invalidBefore + 1 || Math.Abs(o2Before - resourceDiagnostics[(int)GeodesicOceanResource.O2].globalInventory) > 0d) Fail("invalid query mutated state or counter did not advance");
        if (!RunSentinels(out int sentinelCell, out int sentinelLayer)) Fail("sentinel coverage unavailable");
        if (sentinelCell >= 0)
        {
            double before = GetNodeInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2);
            if (!TryWithdrawInventoryBounded(sentinelCell, sentinelLayer, GeodesicOceanResource.O2, before * 2d + 1d, out double withdrawn) || withdrawn < -1e-9 || GetNodeInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2) < -1e-9f) Fail("bounded withdrawal failed");
            if (!TryAddInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2, withdrawn)) Fail("bounded withdrawal restore failed");
        }
        float[] saved = concentrationsByResourceThenNode; int[] savedActive = activeNodeIndices; float[] savedVolumes = activeNodeVolumes; GeodesicOceanLayerGrid savedGrid = sourceGrid; int savedNodeCapacity = nodeCapacity;
        ClearField(false); if (concentrationsByResourceThenNode != null || activeNodeIndices != null || activeNodeVolumes != null || initialized) Fail("cleanup retained arrays");
        InitializeForCurrentDomain(); if (!initialized || concentrationsByResourceThenNode == null || ReferenceEquals(concentrationsByResourceThenNode, saved) || nodeCapacity != savedNodeCapacity || !ReferenceEquals(sourceGrid, savedGrid)) Fail("reinitialization duplicated or changed source state");
        if (saved != null && concentrationsByResourceThenNode != null && saved.Length == concentrationsByResourceThenNode.Length) Array.Copy(saved, concentrationsByResourceThenNode, saved.Length);
        if (savedActive != null && activeNodeIndices != null && savedActive.Length == activeNodeIndices.Length) Array.Copy(savedActive, activeNodeIndices, savedActive.Length);
        if (savedVolumes != null && activeNodeVolumes != null && savedVolumes.Length == activeNodeVolumes.Length) Array.Copy(savedVolumes, activeNodeVolumes, savedVolumes.Length);
        RecomputeDiagnostics(); report = errors == 0 ? $"valid; cells={cellCount}; nodeCapacity={nodeCapacity}; activeNodes={activeNodeCount}; volume={activeOceanVolume:G6}; memory={approximateRuntimeMemoryBytes} bytes; sentinel=pass; cleanup=pass; reinit=pass" : $"invalid; errors={errors}; first={first}"; return errors == 0;
    }
    private bool RunSentinels(out int sentinelCell, out int sentinelLayer) { bool one = false, partial = false, five = false, land = false; sentinelCell = -1; sentinelLayer = 0; for (int c = 0; c < sourceGrid.CellCount; c++) { int n = sourceGrid.ActiveLayerCountByCell[c]; if (n == 0) land = true; else { if (sentinelCell < 0) sentinelCell = c; if (n == 1) one = true; else if (n > 1 && n < sourceGrid.MaximumLayerCount) partial = true; else if (n == 5) five = true; } } return one && partial && five && land; }
}
