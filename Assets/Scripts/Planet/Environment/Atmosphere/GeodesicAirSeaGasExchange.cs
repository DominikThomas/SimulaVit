using System;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GeodesicAirSeaGasExchange : MonoBehaviour
{
    private const int ExchangeGasCount = 5;
    private static readonly ProfilerMarker ExchangeMarker = new ProfilerMarker("GeodesicAtmosphere.AirSeaExchange");
    [SerializeField, Tooltip("CO2, O2, CH4, H2, H2S: equilibrium dissolved concentration per atmospheric bar; ecological model coefficients, not universal Henry constants.")]
    private double[] equilibriumConcentrationPerBar = { 1d, 1d, 1d, 1d, 1d };
    [SerializeField, Tooltip("CO2, O2, CH4, H2, H2S surface-ocean L0 relaxation half-lives in simulated seconds. This controls relaxation toward atmosphere-controlled equilibrium, not depletion of the finite atmosphere. Non-positive disables that gas.")]
    private double[] exchangeHalfLifeSeconds = new double[ExchangeGasCount];
    private GeodesicAtmosphereField atmosphere;

    public long StaticRuntimeMemoryBytes => ExchangeGasCount * sizeof(double) * 2L;
    public double EffectiveCommonHalfLifeSeconds => exchangeHalfLifeSeconds != null && exchangeHalfLifeSeconds.Length == ExchangeGasCount ? exchangeHalfLifeSeconds[0] : 0d;
    public static double ExchangeFraction(double dt, double halfLifeSeconds) => dt > 0d && halfLifeSeconds > 0d && double.IsFinite(dt) && double.IsFinite(halfLifeSeconds) ? 1d - Math.Exp(-Math.Log(2d) * dt / halfLifeSeconds) : 0d;
    public void SetCommonHalfLife(double seconds) { EnsureArrays(); for (int i = 0; i < ExchangeGasCount; i++) exchangeHalfLifeSeconds[i] = double.IsFinite(seconds) ? Math.Max(0d, seconds) : 0d; }
    public void SetParameters(GeodesicAtmosphericGas gas, double concentrationPerBar, double halfLifeSeconds) { int i = ExchangeIndex(gas); if (i < 0) return; EnsureArrays(); equilibriumConcentrationPerBar[i] = Math.Max(0d, concentrationPerBar); exchangeHalfLifeSeconds[i] = Math.Max(0d, halfLifeSeconds); }
    internal void InitializeForWorld(GeodesicAtmosphereField field)
    {
        atmosphere = field; EnsureArrays();
        string state = EffectiveCommonHalfLifeSeconds > 0d ? $"{EffectiveCommonHalfLifeSeconds:G6}s" : "disabled";
        Debug.Log($"[GeodesicAtmosphereExchange] exchange=surface-L0-relaxation-toward-atmosphere-equilibrium, effectiveHalfLife={state}, gasHalfLifeSeconds={Format(exchangeHalfLifeSeconds)}, concentrationPerBar={Format(equilibriumConcentrationPerBar)}, finite-atmosphere-depletion-half-life=false", this);
    }
    internal void ClearWorld() => atmosphere = null;

    internal void Step(GeodesicOceanResourceField ocean, float dt)
    {
        if (atmosphere == null || !atmosphere.IsInitialized || ocean == null || !ocean.IsInitialized || !(dt > 0f) || !float.IsFinite(dt)) return;
        using (ExchangeMarker.Auto())
        {
            for (int i = 0; i < ExchangeGasCount; i++) ExchangeGas(ocean, i, dt);
            atmosphere.CompleteExchangeTick();
        }
    }

    private void ExchangeGas(GeodesicOceanResourceField ocean, int exchangeIndex, double dt)
    {
        double halfLife = exchangeHalfLifeSeconds[exchangeIndex];
        if (!(halfLife > 0d) || !(equilibriumConcentrationPerBar[exchangeIndex] >= 0d)) return;
        GeodesicAtmosphericGas gas = (GeodesicAtmosphericGas)(exchangeIndex + 1);
        GeodesicOceanResource resource = (GeodesicOceanResource)exchangeIndex;
        double fraction = ExchangeFraction(dt, halfLife);
        double equilibrium = atmosphere.GetPartialPressureBar(gas) * equilibriumConcentrationPerBar[exchangeIndex];
        double uptakeDemand = 0d, outgasRequest = 0d;
        GeodesicOceanLayerGrid grid = ocean.SourceGrid;
        for (int cell = 0; cell < ocean.CellCount; cell++) if (grid.IsNodeActive(cell, 0))
        {
            int node = grid.GetNodeIndex(cell, 0); double current = ocean.GetRawConcentration(resource, node);
            double request = (equilibrium - current) * fraction * grid.PhysicalLayerVolumeKm3[node];
            if (request > 0d) uptakeDemand += request; else outgasRequest += request;
        }
        double available = atmosphere.GetInventory(gas);
        double uptakeScale = uptakeDemand > available && uptakeDemand > 0d ? available / uptakeDemand : 1d;
        double actualTotal = 0d;
        for (int cell = 0; cell < ocean.CellCount; cell++) if (grid.IsNodeActive(cell, 0))
        {
            int node = grid.GetNodeIndex(cell, 0); double volume = grid.PhysicalLayerVolumeKm3[node]; double current = ocean.GetRawConcentration(resource, node);
            double request = (equilibrium - current) * fraction * volume;
            double proposed = request > 0d ? request * uptakeScale : request;
            double actual = ocean.ApplyDirectExchangeInventory(resource, node, proposed);
            actualTotal += actual;
        }
        // Outgassing and uptake are netted in the same deterministic batch. Uptake is limited
        // solely by the pre-exchange atmosphere, so simultaneous outgassing cannot favor a cell.
        atmosphere.CommitExchange(gas, actualTotal);
    }

    private void EnsureArrays() { if (equilibriumConcentrationPerBar == null || equilibriumConcentrationPerBar.Length != ExchangeGasCount) equilibriumConcentrationPerBar = new[] { 1d, 1d, 1d, 1d, 1d }; if (exchangeHalfLifeSeconds == null || exchangeHalfLifeSeconds.Length != ExchangeGasCount) exchangeHalfLifeSeconds = new double[ExchangeGasCount]; }
    private static int ExchangeIndex(GeodesicAtmosphericGas gas) => gas == GeodesicAtmosphericGas.N2 ? -1 : (int)gas - 1;
    private static string Format(double[] values) => values == null ? "null" : $"[{values[0]:G4},{values[1]:G4},{values[2]:G4},{values[3]:G4},{values[4]:G4}]";
}
