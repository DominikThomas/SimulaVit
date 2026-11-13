using System;
using System.Text;
using UnityEngine;

/// <summary>Ten-second aggregate diagnostics. Vent rings are read-only labels, never biological inputs.</summary>
public sealed class GeodesicBiologyTemperatureTelemetry
{
    private const int RingBandCount = 6;
    private const int EnvironmentSamplesPerRing = 64;
    private static readonly string[] RingNames = { "0", "1", "2", "3-4", "5+", "unreachable" };
    private GeodesicOceanLayerGrid grid;
    private int[] ringByCell = Array.Empty<int>();
    private int[][] environmentCells = Array.Empty<int[]>();
    private readonly int[] environmentCellCounts = new int[RingBandCount];
    private readonly int[] totalCellsByRing = new int[RingBandCount];
    private readonly long[] birthsByBand = new long[3];
    private long coldDeaths, heatDeaths, starvationDeaths, oldAgeDeaths;
    private int activeOutlets, strongestOutletCell = -1;
    private float strongestOutletInfluence;

    private struct Range
    {
        private int count;
        private float minimum, maximum;
        private double sum;

        public void Add(float value)
        {
            if (!float.IsFinite(value)) return;
            if (count == 0) minimum = maximum = value;
            else { minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value); }
            count++;
            sum += value;
        }

        public override string ToString() => count == 0 ? "n=0" : $"n={count},min/avg/max={minimum:G6}/{sum / count:G6}/{maximum:G6}";
    }

    public void Initialize(GeodesicOceanLayerGrid sourceGrid, GeodesicOceanResourceField resources, int seed)
    {
        Clear();
        grid = sourceGrid;
        ringByCell = new int[grid.CellCount];
        for (int cell = 0; cell < ringByCell.Length; cell++) ringByCell[cell] = -1;
        int[] queue = new int[grid.CellCount];
        int read = 0, write = 0;
        float strongest = -1f;
        for (int i = 0; i < resources.CompactOutletCount; i++)
        {
            if (!resources.TryGetVentInjectionHabitat(i, out GeodesicVentSourceOutlet outlet, out int cell, out int layer)
                || outlet.Habitat != GeodesicVentHabitat.Submarine || !(outlet.RawStrength > 0f)
                || !grid.IsNodeActive(cell, layer)) continue;
            activeOutlets++;
            float influence = resources.GetSubmarineThermalInfluence(cell);
            if (influence > strongest) { strongest = influence; strongestOutletCell = cell; strongestOutletInfluence = influence; }
            if (ringByCell[cell] >= 0) continue;
            ringByCell[cell] = 0;
            queue[write++] = cell;
        }
        // Shortest ocean-neighbour ring from any active submarine outlet, built once
        // for this runtime. This map is not consulted by metabolism or lifecycle.
        GeodesicGridTopology topology = grid.SourceTopology;
        while (read < write)
        {
            int cell = queue[read++];
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + slot];
                if (grid.ActiveLayerCountByCell[neighbor] <= 0 || ringByCell[neighbor] >= 0) continue;
                ringByCell[neighbor] = ringByCell[cell] + 1;
                queue[write++] = neighbor;
            }
        }
        environmentCells = new int[RingBandCount][];
        for (int band = 0; band < RingBandCount; band++) environmentCells[band] = new int[EnvironmentSamplesPerRing];
        var sampleRandom = new System.Random(seed ^ 0x54454C45);
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            if (grid.ActiveLayerCountByCell[cell] <= 0) continue;
            int band = RingBand(cell);
            int seen = ++totalCellsByRing[band];
            if (environmentCellCounts[band] < EnvironmentSamplesPerRing)
                environmentCells[band][environmentCellCounts[band]++] = cell;
            else
            {
                int slot = sampleRandom.Next(seen);
                if (slot < EnvironmentSamplesPerRing) environmentCells[band][slot] = cell;
            }
        }
        // Include a strongest outlet even when ring 0 needs reservoir sampling.
        if (strongestOutletCell >= 0 && Array.IndexOf(environmentCells[0], strongestOutletCell, 0, environmentCellCounts[0]) < 0)
            environmentCells[0][0] = strongestOutletCell;
    }

    public void RecordBirth(float performance, in GeodesicBiologyTemperatureSettings settings)
        => birthsByBand[(int)GeodesicBiologyTemperaturePolicy.GetBand(performance, settings)]++;

    public void RecordDeath(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.TemperatureTooLow: coldDeaths++; break;
            case DeathCause.TemperatureTooHigh: heatDeaths++; break;
            case DeathCause.EnergyDepletion: starvationDeaths++; break;
            case DeathCause.OldAge: oldAgeDeaths++; break;
        }
    }

    public void Log(ReplicatorPopulationState state, GeodesicOceanTemperatureField ocean,
        in GeodesicBiologyTemperatureSettings settings, bool carbonDivision, float divisionCost,
        float replicationCost, float divisionMultiple, int maxPopulation, Func<int, int, float> getTemperature)
    {
        if (grid == null) return;
        var populations = new int[3];
        var organismTemperatures = new Range[RingBandCount];
        var organismPerformances = new Range[RingBandCount];
        var optimumMinimums = new Range();
        var optimumMaximums = new Range();
        var margins = new Range();
        int invalid = 0, dormant = 0, coldStressed = 0, heatStressed = 0, coldExposure = 0, heatExposure = 0;
        int temperatureBlocked = 0, temperatureOnlyBlocked = 0;
        double unsuitableEnergy = 0d;
        for (int i = 0; i < state.Count; i++)
        {
            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
            float temperature = getTemperature(cell, layer);
            if (!grid.IsNodeActive(cell, layer) || !float.IsFinite(temperature)) { invalid++; continue; }
            float performance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(temperature,
                state.OptimalTempMin[i], state.OptimalTempMax[i], state.LethalTempMargin[i], settings.DormancyFloor);
            var band = GeodesicBiologyTemperaturePolicy.GetBand(performance, settings);
            populations[(int)band]++;
            if (band == GeodesicBiologyTemperaturePolicy.SuitabilityBand.Unsuitable) unsuitableEnergy += state.Energy[i];
            if (GeodesicBiologyTemperaturePolicy.IsDormant(performance, settings)) dormant++;
            if (temperature < state.OptimalTempMin[i]) coldStressed++;
            if (temperature > state.OptimalTempMax[i]) heatStressed++;
            if (state.ColdTemperatureExposureSeconds[i] > 0f) coldExposure++;
            if (state.HeatTemperatureExposureSeconds[i] > 0f) heatExposure++;
            if (!GeodesicBiologyTemperaturePolicy.CanReproduce(performance, settings))
            {
                temperatureBlocked++;
                bool energyReady = state.Energy[i] > Mathf.Max(0f, carbonDivision ? divisionCost : replicationCost);
                bool carbonReady = !carbonDivision || state.OrganicCStore[i] >= Mathf.Max(1f, divisionMultiple) * state.BiomassTarget[i];
                if (energyReady && carbonReady && state.Count < maxPopulation) temperatureOnlyBlocked++;
            }
            int ring = RingBand(cell);
            organismTemperatures[ring].Add(temperature);
            organismPerformances[ring].Add(performance);
            optimumMinimums.Add(state.OptimalTempMin[i]);
            optimumMaximums.Add(state.OptimalTempMax[i]);
            margins.Add(state.LethalTempMargin[i]);
        }
        double meanUnsuitableEnergy = populations[2] > 0 ? unsuitableEnergy / populations[2] : double.NaN;
        Debug.Log($"[GeodesicBiologyTemperature] bandOrder=suitable/marginal/unsuitable reproductionMinEfficiency={settings.ReplicationMinEfficiency:G6} dormancyFloor={settings.DormancyFloor:G6} directDamage={settings.DirectDamageEnabled} population={populations[0]}/{populations[1]}/{populations[2]} birthsCumulative={birthsByBand[0]}/{birthsByBand[1]}/{birthsByBand[2]} deathsCumulative(cold/heat/starvation/oldAge)={coldDeaths}/{heatDeaths}/{starvationDeaths}/{oldAgeDeaths} temperatureBlocked={temperatureBlocked} temperatureOnlyBlocked(energy+carbonReady+belowCap)={temperatureOnlyBlocked} unsuitableMeanEnergy={meanUnsuitableEnergy:G6} dormant={dormant} stressed(cold/heat)={coldStressed}/{heatStressed} accumulatedExposure(cold/heat)={coldExposure}/{heatExposure} invalidHabitatTemperature={invalid} optimumMinK({optimumMinimums}) optimumMaxK({optimumMaximums}) toleranceMarginK({margins})");

        var text = new StringBuilder("[GeodesicBiologyVentRings] authority=coarseOceanCellLayer ring=oceanNeighborHopsFromNearestActiveSubmarineOutlet");
        for (int band = 0; band < RingBandCount; band++)
            text.Append(" ring=").Append(RingNames[band]).Append(" organismK(").Append(organismTemperatures[band])
                .Append(") performance(").Append(organismPerformances[band]).Append(')');
        Debug.Log(text.ToString());

        text.Clear();
        text.Append("[GeodesicVentHabitatProfile] oceanTick=").Append(ocean.LastCompletedOceanTemperatureTick.ToString("G9"))
            .Append(" activeSubmarineOutlets=").Append(activeOutlets).Append(" strongestOutletCell=").Append(strongestOutletCell)
            .Append(" strongestOutletBottomLayer=").Append(strongestOutletCell >= 0 ? grid.GetBottomLayerIndex(strongestOutletCell) : -1)
            .Append(" strongestInfluence=").Append(strongestOutletInfluence.ToString("G6"))
            .Append(" strongestOutletBottomK=").Append((strongestOutletCell >= 0 ? ocean.GetBottomLayerTemperatureKelvin(strongestOutletCell) : float.NaN).ToString("G6"))
            .Append(" sampleLimitPerRing=").Append(EnvironmentSamplesPerRing);
        for (int band = 0; band < RingBandCount; band++)
        {
            Range bottomTemperatures = default, aboveTemperatures = default;
            for (int i = 0; i < environmentCellCounts[band]; i++)
            {
                int cell = environmentCells[band][i], bottom = grid.GetBottomLayerIndex(cell);
                bottomTemperatures.Add(ocean.GetLayerTemperatureKelvin(cell, bottom));
                if (bottom > 0) aboveTemperatures.Add(ocean.GetLayerTemperatureKelvin(cell, bottom - 1));
            }
            text.Append(" ring=").Append(RingNames[band]).Append(" cells=").Append(environmentCellCounts[band]).Append('/')
                .Append(totalCellsByRing[band]).Append(" bottomK(").Append(bottomTemperatures)
                .Append(") aboveBottomK(").Append(aboveTemperatures).Append(')');
        }
        Debug.Log(text.ToString());
    }

    private int RingBand(int cell)
    {
        int ring = ringByCell[cell];
        return ring < 0 ? 5 : ring <= 2 ? ring : ring <= 4 ? 3 : 4;
    }

    public void Clear()
    {
        grid = null;
        ringByCell = Array.Empty<int>();
        environmentCells = Array.Empty<int[]>();
        Array.Clear(environmentCellCounts, 0, environmentCellCounts.Length);
        Array.Clear(totalCellsByRing, 0, totalCellsByRing.Length);
        Array.Clear(birthsByBand, 0, birthsByBand.Length);
        coldDeaths = heatDeaths = starvationDeaths = oldAgeDeaths = 0;
        activeOutlets = 0;
        strongestOutletCell = -1;
        strongestOutletInfluence = 0f;
    }
}
