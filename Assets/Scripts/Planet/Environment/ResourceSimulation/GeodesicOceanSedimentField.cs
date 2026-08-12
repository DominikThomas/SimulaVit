using System;
using UnityEngine;

/// <summary>Authoritative per-column inventories of settled Geodesic ocean precipitates.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanSedimentField : MonoBehaviour
{
    private double[] elementalSulfurS0ByCell;
    private double[] oxidizedIronFe3ByCell;

    public bool IsInitialized { get; private set; }
    public int CellCount => elementalSulfurS0ByCell != null ? elementalSulfurS0ByCell.Length : 0;
    public long ApproximateRuntimeMemoryBytes => (long)CellCount * sizeof(double) * 2L;

    public void Initialize(int cellCount)
    {
        Clear();
        if (cellCount <= 0) return;
        elementalSulfurS0ByCell = new double[cellCount];
        oxidizedIronFe3ByCell = new double[cellCount];
        IsInitialized = true;
    }

    public void Clear()
    {
        elementalSulfurS0ByCell = null;
        oxidizedIronFe3ByCell = null;
        IsInitialized = false;
    }

    public double GetElementalSulfurInventory(int cellIndex) => IsValidCell(cellIndex) ? elementalSulfurS0ByCell[cellIndex] : 0d;
    public double GetOxidizedIronInventory(int cellIndex) => IsValidCell(cellIndex) ? oxidizedIronFe3ByCell[cellIndex] : 0d;

    internal void DepositSameColumn(int cellIndex, double elementalSulfurS0, double oxidizedIronFe3)
    {
        if (!IsValidCell(cellIndex)) return;
        if (FiniteNonnegative(elementalSulfurS0)) elementalSulfurS0ByCell[cellIndex] += elementalSulfurS0;
        if (FiniteNonnegative(oxidizedIronFe3)) oxidizedIronFe3ByCell[cellIndex] += oxidizedIronFe3;
    }

    private bool IsValidCell(int cellIndex) => IsInitialized && cellIndex >= 0 && cellIndex < CellCount;
    private static bool FiniteNonnegative(double value) => value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}
