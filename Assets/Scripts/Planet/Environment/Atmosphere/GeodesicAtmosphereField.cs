using System;
using UnityEngine;

public enum GeodesicAtmosphericGas { N2 = 0, CO2 = 1, O2 = 2, CH4 = 3, H2 = 4, H2S = 5 }

/// <summary>Authoritative, global well-mixed Geodesic atmospheric inventory.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicAtmosphereField : MonoBehaviour
{
    private const int GasCount = 6;
    [SerializeField, Min(1e-6f), Tooltip("Simulation inventory units represented by one bar. This is an explicit model capacity, not an Earth-derived constant.")]
    private double atmosphereInventoryPerBar = 1d;
    [SerializeField] private double[] initialPartialPressureBar = new double[GasCount];
    [SerializeField] private double[] inventory = new double[GasCount];
    [SerializeField] private double[] cumulativeNetTransferToOcean = new double[GasCount];
    [SerializeField] private long completedExchangeTicks;
    [SerializeField] private bool initialized;

    public bool IsInitialized => initialized;
    public double AtmosphereInventoryPerBar => atmosphereInventoryPerBar;
    public long CompletedExchangeTicks => completedExchangeTicks;
    public long StaticRuntimeMemoryBytes => GasCount * sizeof(double) * 3L;
    public double TotalPressureBar { get { double sum = 0d; for (int i = 0; i < GasCount; i++) { double next = sum + GetPartialPressureBar((GeodesicAtmosphericGas)i); sum = double.IsFinite(next) ? next : double.MaxValue; } return sum; } }
    public double GetInventory(GeodesicAtmosphericGas gas) => Valid(gas) && initialized ? inventory[(int)gas] : 0d;
    public double GetPartialPressureBar(GeodesicAtmosphericGas gas) => atmosphereInventoryPerBar > 0d ? GetInventory(gas) / atmosphereInventoryPerBar : 0d;
    public double GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas gas) => Valid(gas) && initialized ? cumulativeNetTransferToOcean[(int)gas] : 0d;

    public void Configure(double inventoryPerBar, double n2, double co2, double o2, double ch4, double h2, double h2s)
    {
        atmosphereInventoryPerBar = FinitePositive(inventoryPerBar) ? inventoryPerBar : 1d;
        SetInitial(0, n2); SetInitial(1, co2); SetInitial(2, o2); SetInitial(3, ch4); SetInitial(4, h2); SetInitial(5, h2s);
    }

    public void InitializeForWorld()
    {
        EnsureArrays();
        for (int i = 0; i < GasCount; i++) { double value = initialPartialPressureBar[i] * atmosphereInventoryPerBar; inventory[i] = double.IsFinite(value) ? value : double.MaxValue; cumulativeNetTransferToOcean[i] = 0d; }
        completedExchangeTicks = 0; initialized = true;
        Debug.Log($"[GeodesicAtmosphere] authority=Geodesic atmosphere, exchange=surface-L0-only, inventoryPerBar={atmosphereInventoryPerBar:G6}, totalPressureBar={TotalPressureBar:G6}, N2={GetPartialPressureBar(GeodesicAtmosphericGas.N2):G6}, CO2={GetPartialPressureBar(GeodesicAtmosphericGas.CO2):G6}, O2={GetPartialPressureBar(GeodesicAtmosphericGas.O2):G6}, CH4={GetPartialPressureBar(GeodesicAtmosphericGas.CH4):G6}, H2={GetPartialPressureBar(GeodesicAtmosphericGas.H2):G6}, H2S={GetPartialPressureBar(GeodesicAtmosphericGas.H2S):G6}", this);
    }

    public void ClearField() { EnsureArrays(); Array.Clear(inventory, 0, GasCount); Array.Clear(cumulativeNetTransferToOcean, 0, GasCount); completedExchangeTicks = 0; initialized = false; }
    internal double CommitExchange(GeodesicAtmosphericGas gas, double requestedToOcean)
    {
        if (!initialized || !Valid(gas) || !double.IsFinite(requestedToOcean)) return 0d;
        int index = (int)gas;
        double actual = requestedToOcean > 0d ? Math.Min(requestedToOcean, inventory[index]) : requestedToOcean;
        inventory[index] = Math.Max(0d, inventory[index] - actual);
        cumulativeNetTransferToOcean[index] += actual;
        return actual;
    }
    internal void CompleteExchangeTick() => completedExchangeTicks++;
    internal void SetInventoryForTests(GeodesicAtmosphericGas gas, double value) { EnsureArrays(); inventory[(int)gas] = Math.Max(0d, value); initialized = true; }
    private void OnDestroy() => ClearField();
    private void SetInitial(int index, double value) { EnsureArrays(); initialPartialPressureBar[index] = double.IsFinite(value) ? Math.Max(0d, value) : 0d; }
    private void EnsureArrays() { if (initialPartialPressureBar == null || initialPartialPressureBar.Length != GasCount) initialPartialPressureBar = new double[GasCount]; if (inventory == null || inventory.Length != GasCount) inventory = new double[GasCount]; if (cumulativeNetTransferToOcean == null || cumulativeNetTransferToOcean.Length != GasCount) cumulativeNetTransferToOcean = new double[GasCount]; }
    private static bool Valid(GeodesicAtmosphericGas gas) => (int)gas >= 0 && (int)gas < GasCount;
    private static bool FinitePositive(double value) => double.IsFinite(value) && value > 0d;
}
