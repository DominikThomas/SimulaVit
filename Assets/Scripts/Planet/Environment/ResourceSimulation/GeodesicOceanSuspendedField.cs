using System;
using System.Text;
using UnityEngine;

public enum GeodesicSuspendedMineral { S0 = 0, Fe3 = 1, FeS = 2 }

/// <summary>Physical mineral inventories (concentration * km3), separate from dissolved water and deposited sediment.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanSuspendedField : MonoBehaviour
{
    private const int SpeciesCount = 3;
    private const int LayerCount = GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount;

    [Header("Settling speeds (metres per simulated second)")]
    [SerializeField, Min(0f), Tooltip("Tunable effective particle speed, not a reaction rate. Zero suspends settling.")]
    private float sulfurSettlingMetresPerSecond = 0.0002f;
    [SerializeField, Min(0f)] private float ironOxideSettlingMetresPerSecond = 0.0005f;
    [SerializeField, Min(0f)] private float ironSulphideSettlingMetresPerSecond = 0.001f;

    private GeodesicOceanLayerGrid grid;
    private double[] inventories;
    private double[] incoming;
    private double[] outgoing;
    private readonly double[] produced = new double[SpeciesCount];
    private readonly double[] deposited = new double[SpeciesCount];
    // L0..L4 record downward transfers OUT of that layer; the bottom transfer is deposition instead.
    private readonly double[] downward = new double[SpeciesCount * LayerCount];
    private readonly double[] telemetryLayerSums = new double[SpeciesCount * LayerCount];

    public bool IsInitialized => grid != null && inventories != null;
    public long ApproximateRuntimeMemoryBytes => IsInitialized
        ? ((long)inventories.Length + incoming.Length + outgoing.Length + produced.Length + deposited.Length + downward.Length + telemetryLayerSums.Length) * sizeof(double) : 0L;

    internal void Initialize(GeodesicOceanLayerGrid sourceGrid)
    {
        Clear();
        grid = sourceGrid;
        inventories = new double[SpeciesCount * grid.NodeCapacity];
        incoming = new double[grid.NodeCapacity];
        outgoing = new double[grid.NodeCapacity];
    }

    internal void Clear()
    {
        grid = null;
        inventories = incoming = outgoing = null;
        Array.Clear(produced, 0, produced.Length);
        Array.Clear(deposited, 0, deposited.Length);
        Array.Clear(downward, 0, downward.Length);
        Array.Clear(telemetryLayerSums, 0, telemetryLayerSums.Length);
    }

    public double GetInventory(int cell, int layer, GeodesicSuspendedMineral species)
        => IsInitialized && grid.IsNodeActive(cell, layer) && (int)species >= 0 && (int)species < SpeciesCount
            ? inventories[(int)species * grid.NodeCapacity + grid.GetNodeIndex(cell, layer)] : 0d;

    internal void AddPrecipitate(int cell, int layer, double sulfur, double ironOxide, double ironSulphide = 0d)
    {
        if (!IsInitialized || !grid.IsNodeActive(cell, layer)) throw new InvalidOperationException("Precipitate requires an active ocean layer.");
        int node = grid.GetNodeIndex(cell, layer);
        Add(node, 0, sulfur); Add(node, 1, ironOxide); Add(node, 2, ironSulphide);
    }

    private void Add(int node, int species, double amount)
    {
        if (!double.IsFinite(amount) || amount < 0d) throw new ArgumentOutOfRangeException(nameof(amount));
        inventories[species * grid.NodeCapacity + node] += amount;
        produced[species] += amount;
    }

    internal void Step(GeodesicOceanResourceField resources, GeodesicOceanSedimentField sediment, float dt)
    {
        if (!IsInitialized || !float.IsFinite(dt) || dt <= 0f) return;
        for (int species = 0; species < SpeciesCount; species++)
        {
            if (produced[species] <= 0d) continue;
            int offset = species * grid.NodeCapacity;
            // Particles follow the existing bulk-water mixing geometry; they are not dissolved reactants.
            resources.MixSuspendedInventory(inventories, offset, incoming, outgoing, dt);
            double speedKmPerSecond = GetSpeed(species) * 0.001d;
            if (!double.IsFinite(speedKmPerSecond) || speedKmPerSecond <= 0d) continue;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int bottom = grid.GetBottomLayerIndex(cell);
                // Bottom first: freshly arrived particles cannot fall through multiple layers in this tick.
                for (int layer = bottom; layer >= 0; layer--)
                {
                    int node = grid.GetNodeIndex(cell, layer);
                    double inventory = inventories[offset + node];
                    if (inventory <= 0d) continue;
                    // Well-mixed finite-volume removal: mean residence time = physical thickness / speed.
                    double fraction = 1d - Math.Exp(-speedKmPerSecond * dt / grid.PhysicalLayerThicknessKm[node]);
                    double moved = inventory * fraction;
                    inventories[offset + node] = inventory - moved;
                    if (layer < bottom)
                    {
                        inventories[offset + grid.GetNodeIndex(cell, layer + 1)] += moved;
                        downward[species * LayerCount + layer] += moved;
                    }
                    else if (moved > 0d)
                    {
                        sediment.DepositSameColumn(cell, species == 0 ? moved : 0d, species == 1 ? moved : 0d, species == 2 ? moved : 0d);
                        deposited[species] += moved;
                    }
                }
            }
        }
    }

    private double GetSpeed(int species) => species == 0 ? sulfurSettlingMetresPerSecond
        : species == 1 ? ironOxideSettlingMetresPerSecond : ironSulphideSettlingMetresPerSecond;

    internal void AppendTelemetry(StringBuilder text, double sedimentS0, double sedimentFe3, double sedimentFeS)
    {
        if (!IsInitialized) return;
        Array.Clear(telemetryLayerSums, 0, telemetryLayerSums.Length);
        for (int cell = 0; cell < grid.CellCount; cell++)
            for (int layer = 0; layer < grid.ActiveLayerCountByCell[cell]; layer++)
                for (int species = 0; species < SpeciesCount; species++)
                    telemetryLayerSums[species * LayerCount + layer] += inventories[species * grid.NodeCapacity + grid.GetNodeIndex(cell, layer)];
        text.Append(" particles={unit=concentration*km3,settling=physicalLayerResidence,mixing=bulkWater,organicParticulate=unsupported,organicSediment=unsupported");
        for (int species = 0; species < SpeciesCount; species++)
        {
            double suspended = 0d;
            text.Append(',').Append((GeodesicSuspendedMineral)species).Append("={layers=[");
            for (int layer = 0; layer < LayerCount; layer++)
            {
                if (layer > 0) text.Append(',');
                double amount = telemetryLayerSums[species * LayerCount + layer];
                suspended += amount; text.Append(amount.ToString("G9"));
            }
            text.Append("],settlingDownwardOutTotal=[");
            for (int layer = 0; layer < LayerCount; layer++)
            {
                if (layer > 0) text.Append(',');
                text.Append(downward[species * LayerCount + layer].ToString("G9"));
            }
            double settled = species == 0 ? sedimentS0 : species == 1 ? sedimentFe3 : sedimentFeS;
            double total = suspended + settled;
            text.Append("],speedMPerS=").Append(GetSpeed(species).ToString("G6"))
                .Append(",suspended=").Append(suspended.ToString("G9"))
                .Append(",toSeabedTotal=").Append(deposited[species].ToString("G9"))
                .Append(",suspendedPlusDeposited=").Append(total.ToString("G9"))
                .Append(",producedTotal=").Append(produced[species].ToString("G9"))
                .Append(",conservationResidual=").Append((total - produced[species]).ToString("G9"))
                .Append(",relativeResidual=").Append((produced[species] > 0d ? (total - produced[species]) / produced[species] : 0d).ToString("G6")).Append('}');
        }
        text.Append('}');
    }
}
