using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Mode boundary for the first ocean-only Geodesic biology foundation.</summary>
public sealed class GeodesicBiologyRuntime
{
    private static readonly ProfilerMarker Evaluation = new ProfilerMarker("GeodesicBiology.ReactionEvaluation");
    private static readonly ProfilerMarker Competition = new ProfilerMarker("GeodesicBiology.CompetitionResolution");
    private static readonly ProfilerMarker Commit = new ProfilerMarker("GeodesicBiology.EnvironmentCommit");
    private static readonly ProfilerMarker Lifecycle = new ProfilerMarker("GeodesicBiology.LifecycleReproduction");
    private static readonly ProfilerMarker Movement = new ProfilerMarker("GeodesicBiology.PassiveMovement");
    private const int ResourceCount = 7;
    public const float PassiveHorizontalOpportunitiesPerSecond = 0.8f;
    public const float PassiveVerticalOpportunitiesPerSecond = 0.08f;

    private PlanetGenerator planet;
    private GeodesicOceanResourceField resources;
    private GeodesicOceanSedimentField sediment;
    private GeodesicOceanTemperatureField oceanTemperature;
    private GeodesicExperiencedTemperatureField experiencedTemperature;
    private GeodesicSurfaceTemperatureField surfaceTemperature;
    private GeodesicOceanLayerGrid grid;
    private Request[] requests = Array.Empty<Request>();
    private double[] demand = Array.Empty<double>();
    private double[] availabilityFactor = Array.Empty<double>();
    private double[] delta = Array.Empty<double>();
    private int[] touched = Array.Empty<int>();
    private int[] stamps = Array.Empty<int>();
    private int[] lightStamps = Array.Empty<int>();
    private float[] temperatureByNode = Array.Empty<float>();
    private float[] lightByNode = Array.Empty<float>();
    private float[] oxygenByNode = Array.Empty<float>();
    private int generation = 1;
    private int touchedCount;
    private System.Random reproductionRandom;
    private readonly long[] requestedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly long[] achievedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] extentByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] energyByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private long births;
    private long deaths;
    private long starvationDeaths;
    private long lifespanDeaths;
    private long zeroAchieved;
    private long resourceLimited;
    private long invalidHabitats;
    private long invalidBiologicalStates;
    private double maintenancePaid;
    private double diagnosticElapsed;
    private double temperatureSum;
    private float temperatureMin = float.PositiveInfinity;
    private float temperatureMax = float.NegativeInfinity;
    private double performanceSum;
    private float performanceMin = float.PositiveInfinity;
    private float performanceMax = float.NegativeInfinity;
    private long temperatureSamples;
    private long zeroTemperaturePerformance;
    private long biologySteps;
    private long agentEvaluations;
    private long habitatSamples;
    private long oceanTemperatureReads;
    private long photosyntheticLightReads;
    private long resourceInventoryReads;
    private long competitionPairs;
    private float lastBiologyStepSeconds;
    private long movementUpdates, horizontalTransitions, verticalTransitions, rejectedTransitions;

    public long BiologySteps => biologySteps;
    public long AgentEvaluations => agentEvaluations;
    public long HabitatSamples => habitatSamples;
    public long OceanTemperatureReads => oceanTemperatureReads;
    public long PhotosyntheticLightReads => photosyntheticLightReads;
    public long ResourceInventoryReads => resourceInventoryReads;
    public long CompetitionPairs => competitionPairs;
    public float LastBiologyStepSeconds => lastBiologyStepSeconds;
    public static MetabolismType NormalFounderMetabolism => MetabolismType.Hydrogenotrophy;
    public static LocomotionType NormalFounderLocomotion => LocomotionType.PassiveDrift;

    private struct Request
    {
        public int Node;
        public int Cell;
        public int Layer;
        public GeodesicOceanResource A, B, Product;
        public MetabolismType Metabolism;
        public double NeedA, NeedB, ProductCoefficient, Desired, Actual;
        public float EnergyPerExtent, StorePerExtent;
        public bool SulfurProduct;
        public bool Supported;
        public float Temperature;
        public float TemperaturePerformance;
        public double AvailabilityFactor;
    }

    public bool Initialize(PlanetGenerator generator, List<Replicator> agents, ReplicatorPopulationState state,
        int requestedFounders, float minLife, float maxLife, Color color, float biomassTarget,
        Vector2 hydrogenTemperatureRange, float lethalTemperatureMargin)
    {
        Clear();
        planet = generator;
        resources = generator != null ? generator.GetComponent<GeodesicOceanResourceField>() : null;
        sediment = generator != null ? generator.GetComponent<GeodesicOceanSedimentField>() : null;
        oceanTemperature = generator != null ? generator.GetComponent<GeodesicOceanTemperatureField>() : null;
        experiencedTemperature = generator != null ? generator.GetComponent<GeodesicExperiencedTemperatureField>() : null;
        surfaceTemperature = generator != null ? generator.GetComponent<GeodesicSurfaceTemperatureField>() : null;
        grid = resources != null ? resources.SourceGrid : null;
        if (grid == null || !resources.IsInitialized || oceanTemperature == null || !oceanTemperature.IsInitialized
            || experiencedTemperature == null || !experiencedTemperature.IsInitialized)
        {
            Debug.LogError("[GeodesicBiology] Required layered ocean/resource/temperature authority is not initialized.");
            Clear();
            return false;
        }

        demand = new double[grid.NodeCapacity * ResourceCount];
        availabilityFactor = new double[grid.NodeCapacity * ResourceCount];
        delta = new double[grid.NodeCapacity * ResourceCount];
        touched = new int[grid.NodeCapacity];
        stamps = new int[grid.NodeCapacity];
        lightStamps = new int[grid.NodeCapacity];
        temperatureByNode = new float[grid.NodeCapacity];
        lightByNode = new float[grid.NodeCapacity];
        oxygenByNode = new float[grid.NodeCapacity];
        requests = new Request[Mathf.Max(1, requestedFounders)];
        int seed = PlanetSeedUtility.DeriveSeed(generator.randomSeed, PlanetSeedDomain.Biology, PlanetGenerator.GenerationVersion);
        var random = new System.Random(seed);
        var visualRandom = new System.Random(seed ^ 0x3419A7D);
        reproductionRandom = new System.Random(seed ^ 0x5EED123);
        int spawned = SpawnFounders(requestedFounders, random, visualRandom, agents, state, minLife, maxLife, color, biomassTarget,
            hydrogenTemperatureRange, lethalTemperatureMargin);
        int occupiedCells = 0, occupiedLayers = 0;
        var cellSeen = new bool[grid.CellCount]; var layerSeen = new bool[grid.MaximumLayerCount];
        for (int i = 0; i < spawned; i++) { if (!cellSeen[agents[i].geodesicCellIndex]) { cellSeen[agents[i].geodesicCellIndex] = true; occupiedCells++; } int layer=agents[i].currentOceanLayerIndex; if(!layerSeen[layer]){layerSeen[layer]=true;occupiedLayers++;} }
        Debug.Log($"[GeodesicBiology] mode=Geodesic biologySeed={seed} requested={requestedFounders} spawned={spawned} hydrogenotrophy={spawned} passiveDrift={spawned} occupiedCells={occupiedCells} occupiedLayers={occupiedLayers} ventFounders={spawned} founders=hydrogenotrophy-passive-drift-compact-submarine-vent-bottom");
        return true;
    }

    private int SpawnFounders(int count, System.Random random, System.Random visualRandom,
        List<Replicator> agents, ReplicatorPopulationState state,
        float minLife, float maxLife, Color color, float biomassTarget, Vector2 hydrogenTemperatureRange,
        float lethalTemperatureMargin)
    {
        int validVents = 0;
        for (int i = 0; i < resources.CompactOutletCount; i++)
            if (resources.TryGetVentOutlet(i, out GeodesicVentSourceOutlet outlet) && outlet.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(outlet.CellIndex, outlet.SourceNode % grid.MaximumLayerCount)) validVents++;
        if (count > 0 && validVents == 0) { Debug.LogWarning("[GeodesicBiology] No valid submarine vent habitat; no founders were spawned."); return 0; }
        for (int i = 0; i < count; i++)
        {
            int pick = random.Next(validVents), seen = 0; GeodesicVentSourceOutlet selected = default;
            for (int v = 0; v < resources.CompactOutletCount; v++) if (resources.TryGetVentOutlet(v, out var candidate) && candidate.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(candidate.CellIndex, candidate.SourceNode % grid.MaximumLayerCount) && seen++ == pick) { selected = candidate; break; }
            MetabolismType metabolism = NormalFounderMetabolism;
            Vector3 direction = planet.GeodesicTopology.CellDirections[selected.CellIndex];
            int bottomLayer = selected.SourceNode % grid.MaximumLayerCount;
            float layerRadius = grid.LayerCenterRadius[grid.GetNodeIndex(selected.CellIndex, bottomLayer)];
            float meanSpacingRadians = GetMeanNeighborSpacingRadians(selected.CellIndex);
            Vector3 visualLocalPosition = CalculateVisualFounderPosition(direction, layerRadius, meanSpacingRadians,
                (float)visualRandom.NextDouble(), (float)visualRandom.NextDouble());
            Vector3 position = planet.transform.TransformPoint(visualLocalPosition);
            float life = Mathf.Lerp(minLife, maxLife, (float)random.NextDouble());
            var agent = new Replicator(position, Quaternion.FromToRotation(Vector3.up, visualLocalPosition.normalized), life, color,
                new Replicator.Traits(true, true, true, 0f), (float)random.NextDouble(), metabolism, NormalFounderLocomotion);
            InitializeFounderBiologicalState(agent, selected.CellIndex, bottomLayer, hydrogenTemperatureRange,
                lethalTemperatureMargin, Mathf.Lerp(0.1f, 0.5f, (float)random.NextDouble()),
                Mathf.Lerp(0f, life * 0.5f, (float)random.NextDouble()), biomassTarget);
            agents.Add(agent); state.AddAgentFromReplicatorData(agent);
        }
        return count;
    }

    private float GetMeanNeighborSpacingRadians(int cellIndex)
    {
        GeodesicGridTopology topology = grid.SourceTopology;
        int count = topology.NeighborCounts[cellIndex];
        if (count <= 0) return 0f;
        float sum = 0f;
        int offset = cellIndex * 6;
        for (int i = 0; i < count; i++) sum += topology.NeighborAngularDistances6[offset + i];
        return sum / count;
    }

    public void Step(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float hydrogenCo2, float hydrogenH2, float hydrogenEnergy, float hydrogenStoreFraction,
        bool hydrogenO2InhibitionEnabled, float o2ComfortMax, float o2StressMax, float hydrogenMinimumO2Efficiency,
        float sulfurCo2, float sulfurH2s, float sulfurEnergy, float methaneCo2, float methaneH2, float methaneEnergy,
        float methanotrophyCh4, float methanotrophyO2, float methanotrophyEnergy, float photoCo2, float photoEnergy,
        float maxStore, float reproductionRate, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, float childSplit, int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause)
    {
        biologySteps++;
        lastBiologyStepSeconds = dt;
        if (agents.Count == 0)
        {
            diagnosticElapsed += dt;
            if (diagnosticElapsed >= 10d) { LogDiagnostics(state); diagnosticElapsed = 0d; }
            return;
        }
        state.EnsureMatchesAgentCount(agents); EnsureRequestCapacity(agents.Count); BeginSparseStep();
        using (Movement.Auto()) RunPassiveMovement(dt, agents, state);
        using (Evaluation.Auto()) for (int i = 0; i < state.Count; i++) BuildRequest(i, agents[i], state, dt,
            hydrogenCo2, hydrogenH2, hydrogenEnergy, hydrogenStoreFraction, hydrogenO2InhibitionEnabled,
            o2ComfortMax, o2StressMax, hydrogenMinimumO2Efficiency, sulfurCo2, sulfurH2s, sulfurEnergy,
            methaneCo2, methaneH2, methaneEnergy, methanotrophyCh4, methanotrophyO2, methanotrophyEnergy, photoCo2, photoEnergy);
        using (Competition.Auto())
        {
            ResolveAvailabilityFactors();
            ResolveCompetition(state.Count);
        }
        using (Commit.Auto()) CommitRequests(state.Count, state, maxStore);
        using (Lifecycle.Auto()) RunLifecycle(dt, agents, state, maintenance, reproductionRate, carbonDivision, divisionCost, replicationCost, divisionMultiple, childSplit, maxPopulation, registerDeathCause);
        diagnosticElapsed += dt;
        if (diagnosticElapsed >= 10d)
        {
            LogDiagnostics(state);
            diagnosticElapsed = 0d;
        }
    }

    private void BuildRequest(int i, Replicator agent, ReplicatorPopulationState state, float dt,
        float hCo2, float hH2, float hEnergy, float hStoreFraction, bool hO2InhibitionEnabled,
        float o2ComfortMax, float o2StressMax, float hMinimumO2Efficiency,
        float sCo2, float sH2s, float sEnergy, float mCo2, float mH2, float mEnergy,
        float mtCh4, float mtO2, float mtEnergy, float pCo2, float pEnergy)
    {
        agentEvaluations++;
        int cell = agent.geodesicCellIndex, layer = state.CurrentOceanLayerIndex[i]; Request r = default; r.Cell = cell; r.Layer = layer; r.Metabolism = state.Metabolism[i];
        if (!grid.IsNodeActive(cell, layer)) { invalidHabitats++; requests[i] = r; return; }
        r.Node = grid.GetNodeIndex(cell, layer); float scale = dt / 0.5f;
        switch (state.Metabolism[i])
        {
            case MetabolismType.Hydrogenotrophy: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.NeedA=hCo2; r.NeedB=hH2; r.EnergyPerExtent=hEnergy; r.StorePerExtent=hCo2*Mathf.Clamp01(hStoreFraction); break;
            case MetabolismType.SulfurChemosynthesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2S; r.NeedA=sCo2; r.NeedB=sH2s; r.EnergyPerExtent=sEnergy; r.StorePerExtent=sCo2; r.SulfurProduct=true; break;
            case MetabolismType.Methanogenesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.Product=GeodesicOceanResource.CH4; r.NeedA=mCo2; r.NeedB=mH2; r.ProductCoefficient=mCo2*0.85; r.EnergyPerExtent=mEnergy*0.85f; r.StorePerExtent=(float)(mCo2*0.15); break;
            case MetabolismType.Methanotrophy: r.A=GeodesicOceanResource.CH4; r.B=GeodesicOceanResource.O2; r.Product=GeodesicOceanResource.CO2; r.NeedA=mtCh4; r.NeedB=mtO2; r.ProductCoefficient=mtCh4; r.EnergyPerExtent=mtEnergy; r.StorePerExtent=(float)(mtCh4*0.15); break;
            case MetabolismType.Photosynthesis:
                float light = GetCachedPhotosyntheticLight(r.Node, cell, layer);
                r.A = GeodesicOceanResource.CO2;
                r.B = GeodesicOceanResource.CO2;
                r.Product = GeodesicOceanResource.O2;
                r.NeedA = pCo2 * light;
                r.NeedB = 0;
                r.ProductCoefficient = r.NeedA;
                r.EnergyPerExtent = (float)(pEnergy * r.NeedA);
                r.StorePerExtent = (float)r.NeedA;
                break;
            default: requests[i]=r; return;
        }
        r.Supported = true;
        EnsureHabitatSample(r.Node, cell, layer);
        // Replicator.position is visual-only in Geodesic mode. Until biology owns an
        // authoritative sub-cell coordinate, feeding that position into the vent-core
        // query would make rendering placement control biology and can expose founders
        // to source-fluid temperature. Cell/layer temperature is the authoritative
        // organism temperature for this foundation.
        float temperature = temperatureByNode[r.Node];
        float optimumMin = state.OptimalTempMin[i], optimumMax = state.OptimalTempMax[i];
        float temperatureScale = CalculateTemperaturePerformance(temperature, optimumMin, optimumMax, state.LethalTempMargin[i]);
        float oxygenEfficiency = state.Metabolism[i] == MetabolismType.Hydrogenotrophy && hO2InhibitionEnabled
            ? CalculateAnaerobeO2Efficiency(oxygenByNode[r.Node], o2ComfortMax, o2StressMax, hMinimumO2Efficiency)
            : 1f;
        r.Temperature = temperature;
        r.TemperaturePerformance = temperatureScale;
        AccumulateTemperature(temperature, temperatureScale);
        if (!float.IsFinite(state.Energy[i]) || !float.IsFinite(state.OrganicCStore[i]) || !float.IsFinite(temperature)) invalidBiologicalStates++;
        r.Desired=Math.Max(0,scale*temperatureScale*oxygenEfficiency); requests[i]=r; if(r.Desired<=0||r.NeedA<=0)return; demand[(int)r.A*grid.NodeCapacity+r.Node]+=r.NeedA*r.Desired; if(r.NeedB>0)demand[(int)r.B*grid.NodeCapacity+r.Node]+=r.NeedB*r.Desired;
    }

    private void ResolveAvailabilityFactors()
    {
        for (int touchedIndex = 0; touchedIndex < touchedCount; touchedIndex++)
        {
            int node = touched[touchedIndex];
            int cell = node / grid.MaximumLayerCount;
            int layer = node % grid.MaximumLayerCount;
            for (int resourceIndex = 0; resourceIndex < ResourceCount; resourceIndex++)
            {
                int scratch = resourceIndex * grid.NodeCapacity + node;
                double totalDemand = demand[scratch];
                if (totalDemand <= 0d) continue;
                double available = resources.GetNodeInventory(cell, layer, (GeodesicOceanResource)resourceIndex);
                availabilityFactor[scratch] = CalculateAvailabilityFactor(available, totalDemand);
                resourceInventoryReads++;
                competitionPairs++;
            }
        }
    }

    private void ResolveCompetition(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Request r = requests[i];
            if (!r.Supported) continue;
            int metabolism = (int)r.Metabolism;
            requestedByMetabolism[metabolism]++;
            double factor = r.Desired > 0 ? 1d : 0d;
            if (r.Desired > 0)
            {
                factor = Math.Min(factor, availabilityFactor[(int)r.A * grid.NodeCapacity + r.Node]);
                if (r.NeedB > 0) factor = Math.Min(factor, availabilityFactor[(int)r.B * grid.NodeCapacity + r.Node]);
            }
            r.AvailabilityFactor = factor;
            r.Actual = r.Desired * factor;
            if (factor < 1d && r.Desired > 0) resourceLimited++;
            if (r.Actual > 0) achievedByMetabolism[metabolism]++; else zeroAchieved++;
            requests[i] = r;
        }
    }
    private void CommitRequests(int count, ReplicatorPopulationState state, float maxStore){for(int i=0;i<count;i++){Request r=requests[i]; if(r.Actual<=0)continue; delta[(int)r.A*grid.NodeCapacity+r.Node]-=r.NeedA*r.Actual; if(r.NeedB>0)delta[(int)r.B*grid.NodeCapacity+r.Node]-=r.NeedB*r.Actual; if(r.ProductCoefficient>0)delta[(int)r.Product*grid.NodeCapacity+r.Node]+=r.ProductCoefficient*r.Actual; if(r.SulfurProduct)sediment.DepositSameColumn(r.Cell,r.NeedB*r.Actual,0); float gained=r.EnergyPerExtent*(float)r.Actual; state.Energy[i]+=gained; state.OrganicCStore[i]=Mathf.Min(maxStore,state.OrganicCStore[i]+r.StorePerExtent*(float)r.Actual); int metabolism=(int)state.Metabolism[i]; extentByMetabolism[metabolism]+=r.Actual;energyByMetabolism[metabolism]+=gained;} for(int t=0;t<touchedCount;t++){int node=touched[t]; for(int k=0;k<ResourceCount;k++){double d=delta[k*grid.NodeCapacity+node]; if(d!=0)resources.ApplyDirectExchangeInventory((GeodesicOceanResource)k,node,d);}}}
    private void RunLifecycle(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float rate, bool carbon, float divisionCost, float replicationCost, float multiple, float split,
        int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause)
    {
        for (int i = state.Count - 1; i >= 0; i--)
        {
            state.Age[i] += dt;
            float paid = Mathf.Max(0f, maintenance * dt);
            state.Energy[i] -= paid;
            maintenancePaid += paid;
            DeathCause cause = ClassifyLifecycleDeath(state.Energy[i], state.Age[i], agents[i].maxLifespan);
            if (cause != DeathCause.Unknown)
            {
                deaths++;
                if (cause == DeathCause.EnergyDepletion) starvationDeaths++; else lifespanDeaths++;
                registerDeathCause?.Invoke(state.Metabolism[i], cause);
                RemoveAgentAtSwapBack(i, agents, state);
                continue;
            }

            bool eligible = state.Energy[i] >= (carbon ? divisionCost : replicationCost)
                && (!carbon || state.OrganicCStore[i] >= Mathf.Max(1, multiple) * agents[i].biomassTarget);
            if (!eligible || agents.Count >= maxPopulation || reproductionRandom.NextDouble() >= rate * dt) continue;
            Replicator parent = agents[i];
            float childMovementSeed = (float)reproductionRandom.NextDouble();
            var child = new Replicator(state.Position[i], state.Rotation[i], parent.maxLifespan, parent.color, parent.traits,
                childMovementSeed, state.Metabolism[i], state.Locomotion[i], parent.locomotionSkill);
            child.geodesicCellIndex = parent.geodesicCellIndex;
            child.currentOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.preferredOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.biomassTarget = parent.biomassTarget;
            child.optimalTempMin = state.OptimalTempMin[i];
            child.optimalTempMax = state.OptimalTempMax[i];
            child.lethalTempMargin = state.LethalTempMargin[i];
            state.Energy[i] -= carbon ? divisionCost : replicationCost;
            child.energy = Mathf.Max(0.1f, state.Energy[i] * 0.5f);
            if (carbon) { child.organicCStore = state.OrganicCStore[i] * Mathf.Clamp01(split); state.OrganicCStore[i] -= child.organicCStore; }
            agents.Add(child);
            state.AddAgentFromReplicatorData(child);
            births++;
        }
    }
    public static void RemoveAgentAtSwapBack(int i,List<Replicator>a,ReplicatorPopulationState s){int last=a.Count-1;if(i<0||i>last)return;if(i!=last)a[i]=a[last];a.RemoveAt(last);s.RemoveAgentAtSwapBack(i);}
    private void RunPassiveMovement(float dt, List<Replicator> agents, ReplicatorPopulationState state)
    {
        if (!(dt > 0f) || state.Count == 0) return;
        GeodesicGridTopology topology = grid.SourceTopology;
        for (int i = 0; i < state.Count; i++)
        {
            if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
            movementUpdates++;
            uint sequence = state.PassiveMovementSequence[i];
            uint seed = MovementSeedBits(state.MovementSeed[i]);
            int cell = agents[i].geodesicCellIndex;
            int layer = state.CurrentOceanLayerIndex[i];

            if (IsOpportunity(seed, sequence++, dt, PassiveHorizontalOpportunitiesPerSecond))
            {
                int neighborCount = topology.NeighborCounts[cell];
                int slot = DeterministicIndex(seed, sequence++, neighborCount);
                int targetCell = neighborCount > 0 ? topology.Neighbors6[cell * 6 + slot] : -1;
                int targetLayer = ResolveHorizontalTargetLayer(layer, targetCell >= 0 && targetCell < grid.CellCount
                    ? grid.ActiveLayerCountByCell[targetCell] : 0);
                if (targetCell >= 0 && targetLayer >= 0 && grid.IsNodeActive(targetCell, targetLayer))
                {
                    cell = targetCell;
                    layer = targetLayer;
                    agents[i].geodesicCellIndex = cell;
                    horizontalTransitions++;
                }
                else rejectedTransitions++;
            }

            if (IsOpportunity(seed, sequence++, dt, PassiveVerticalOpportunitiesPerSecond))
            {
                int direction = (Hash(seed, sequence++) & 1u) == 0u ? -1 : 1;
                int targetLayer = ResolveAdjacentVerticalLayer(layer, direction, grid.ActiveLayerCountByCell[cell]);
                if (targetLayer != layer && grid.IsNodeActive(cell, targetLayer)) { layer = targetLayer; verticalTransitions++; }
                else rejectedTransitions++;
            }
            state.PassiveMovementSequence[i] = sequence;
            state.CurrentOceanLayerIndex[i] = layer;
            state.PreferredOceanLayerIndex[i] = layer;
            agents[i].currentOceanLayerIndex = layer;
            agents[i].preferredOceanLayerIndex = layer;

            int node = grid.GetNodeIndex(cell, layer);
            Vector3 directionToCell = planet.GeodesicTopology.CellDirections[cell];
            float scatterA = ToUnitFloat(Hash(seed, 0xA511E9B3u));
            float scatterB = ToUnitFloat(Hash(seed, 0x63D83595u));
            Vector3 localTarget = CalculateVisualFounderPosition(directionToCell, grid.LayerCenterRadius[node],
                GetMeanNeighborSpacingRadians(cell), scatterA, scatterB);
            Vector3 worldTarget = planet.transform.TransformPoint(localTarget);
            state.Position[i] = Vector3.Lerp(state.Position[i], worldTarget, Mathf.Clamp01(dt * 3f));
            state.CurrentDirection[i] = planet.transform.TransformDirection(localTarget.normalized);
            state.Rotation[i] = planet.transform.rotation * Quaternion.FromToRotation(Vector3.up, localTarget.normalized);
        }
    }

    public static int ResolveHorizontalTargetLayer(int sourceLayer, int targetActiveLayerCount)
        => targetActiveLayerCount <= 0 ? -1 : Mathf.Clamp(sourceLayer, 0, targetActiveLayerCount - 1);

    public static int ResolveAdjacentVerticalLayer(int sourceLayer, int direction, int activeLayerCount)
    {
        if (activeLayerCount <= 0) return -1;
        int current = Mathf.Clamp(sourceLayer, 0, activeLayerCount - 1);
        return Mathf.Clamp(current + (direction < 0 ? -1 : 1), 0, activeLayerCount - 1);
    }

    public static bool IsOpportunity(uint seed, uint sequence, float dt, float opportunitiesPerSecond)
    {
        if (!(dt > 0f) || !(opportunitiesPerSecond > 0f)) return false;
        float probability = 1f - Mathf.Exp(-opportunitiesPerSecond * dt);
        return ToUnitFloat(Hash(seed, sequence)) < probability;
    }

    public static int DeterministicIndex(uint seed, uint sequence, int count)
        => count <= 0 ? -1 : (int)(Hash(seed, sequence) % (uint)count);

    private static uint MovementSeedBits(float seed) => unchecked((uint)Mathf.RoundToInt(seed * 16777213f)) ^ 0x9E3779B9u;
    private static float ToUnitFloat(uint value) => (value >> 8) * (1f / 16777216f);
    private static uint Hash(uint seed, uint sequence)
    {
        uint x = seed ^ (sequence * 0x9E3779B9u);
        x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
        return x;
    }
    private void EnsureHabitatSample(int node, int cell, int layer)
    {
        if (!MarkTouchedNode(node, generation, stamps)) return;
        touched[touchedCount++] = node;
        temperatureByNode[node] = oceanTemperature.GetLayerTemperatureKelvin(cell, layer);
        resources.TryGetConcentration(cell, layer, GeodesicOceanResource.O2, out oxygenByNode[node]);
        habitatSamples++;
        oceanTemperatureReads++;
        for (int resource = 0; resource < ResourceCount; resource++)
        {
            int scratch = resource * grid.NodeCapacity + node;
            demand[scratch] = 0d;
            availabilityFactor[scratch] = 0d;
            delta[scratch] = 0d;
        }
    }
    private float GetCachedPhotosyntheticLight(int node, int cell, int layer)
    {
        EnsureHabitatSample(node, cell, layer);
        if (lightStamps[node] == generation) return lightByNode[node];
        lightStamps[node] = generation;
        lightByNode[node] = ResolvePhotosyntheticLight(surfaceTemperature.GetCellInsolationCosine(cell), layer);
        photosyntheticLightReads++;
        return lightByNode[node];
    }
    public static bool MarkTouchedNode(int node, int generationValue, int[] generationStamps)
    {
        if (generationStamps == null || node < 0 || node >= generationStamps.Length) return false;
        if (generationStamps[node] == generationValue) return false;
        generationStamps[node] = generationValue;
        return true;
    }
    private void BeginSparseStep(){touchedCount=0;if(++generation==int.MaxValue){Array.Clear(stamps,0,stamps.Length);Array.Clear(lightStamps,0,lightStamps.Length);generation=1;}}
    private void EnsureRequestCapacity(int count){if(requests.Length<count)Array.Resize(ref requests,Mathf.NextPowerOfTwo(count));}
    public static float CalculateTemperaturePerformance(float temperature, float optimumMin, float optimumMax, float lethalMargin)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(optimumMin) || !float.IsFinite(optimumMax)
            || !float.IsFinite(lethalMargin) || optimumMax < optimumMin || lethalMargin <= 0f) return 0f;
        if (temperature >= optimumMin && temperature <= optimumMax) return 1f;
        float distance = temperature < optimumMin ? optimumMin - temperature : temperature - optimumMax;
        return Mathf.Clamp01(1f - distance / lethalMargin);
    }
    public static void InitializeFounderBiologicalState(Replicator agent, int cell, int layer, Vector2 thermalRange,
        float lethalMargin, float energy, float age, float biomassTarget)
    {
        if (agent == null) return;
        agent.geodesicCellIndex = cell; agent.currentOceanLayerIndex = layer; agent.preferredOceanLayerIndex = layer;
        agent.energy = Mathf.Max(0f, energy); agent.age = Mathf.Max(0f, age); agent.organicCStore = 0f;
        agent.optimalTempMin = Mathf.Min(thermalRange.x, thermalRange.y);
        agent.optimalTempMax = Mathf.Max(thermalRange.x, thermalRange.y);
        agent.lethalTempMargin = Mathf.Max(1f, lethalMargin);
        agent.biomassTarget = Mathf.Max(0.0001f, biomassTarget);
    }
    public static double CalculateAchievedExtent(double requestedExtent, double availableA, double needA, double availableB, double needB)
    {
        if (!(requestedExtent > 0d) || !(needA > 0d)) return 0d;
        double factor = Math.Min(1d, Math.Max(0d, availableA) / (needA * requestedExtent));
        if (needB > 0d) factor = Math.Min(factor, Math.Max(0d, availableB) / (needB * requestedExtent));
        return requestedExtent * factor;
    }
    public static double CalculateAvailabilityFactor(double availableInventory, double totalDemand)
    {
        if (!(totalDemand > 0d)) return 0d;
        return Math.Min(1d, Math.Max(0d, availableInventory) / totalDemand);
    }
    public static float CalculateAnaerobeO2Efficiency(float localO2, float comfortMax, float stressMax, float minimumEfficiency)
    {
        comfortMax = Mathf.Max(0f, comfortMax);
        stressMax = Mathf.Max(comfortMax + 0.0001f, stressMax);
        if (localO2 <= comfortMax) return 1f;
        float t = Mathf.Clamp01((localO2 - comfortMax) / (stressMax - comfortMax));
        float inhibition = t * t * (3f - 2f * t);
        return Mathf.Lerp(1f, Mathf.Clamp01(minimumEfficiency), inhibition);
    }
    public static Vector3 CalculateVisualFounderPosition(Vector3 cellDirection, float layerRadius,
        float meanNeighborSpacingRadians, float radialRandom01, float angleRandom01)
    {
        Vector3 normal = cellDirection.sqrMagnitude > 1e-12f ? cellDirection.normalized : Vector3.up;
        Vector3 reference = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 tangentA = Vector3.Cross(reference, normal).normalized;
        Vector3 tangentB = Vector3.Cross(normal, tangentA);
        float angularRadius = Mathf.Max(0f, meanNeighborSpacingRadians) * 0.18f * Mathf.Sqrt(Mathf.Clamp01(radialRandom01));
        float angle = Mathf.Clamp01(angleRandom01) * Mathf.PI * 2f;
        Vector3 tangent = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
        Vector3 scatteredDirection = (normal + tangent * Mathf.Tan(angularRadius)).normalized;
        return scatteredDirection * Mathf.Max(0f, layerRadius);
    }
    public static DeathCause ClassifyLifecycleDeath(float energy, float age, float lifespan)
    {
        if (float.IsFinite(age) && float.IsFinite(lifespan) && age > lifespan) return DeathCause.OldAge;
        if (!float.IsFinite(energy) || energy <= 0f) return DeathCause.EnergyDepletion;
        return DeathCause.Unknown;
    }
    private void AccumulateTemperature(float temperature, float performance)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(performance)) return;
        temperatureSum += temperature; temperatureMin = Mathf.Min(temperatureMin, temperature); temperatureMax = Mathf.Max(temperatureMax, temperature);
        performanceSum += performance; performanceMin = Mathf.Min(performanceMin, performance); performanceMax = Mathf.Max(performanceMax, performance);
        temperatureSamples++; if (performance <= 0f) zeroTemperaturePerformance++;
    }
    private void LogDiagnostics(ReplicatorPopulationState state)
    {
        int hydrogen=0,sulfur=0,methanogenesis=0,photosynthesis=0,methanotrophy=0,passive=0,amoeboid=0,flagellum=0,anchored=0;
        for(int i=0;i<state.Count;i++){switch(state.Metabolism[i]){case MetabolismType.Hydrogenotrophy:hydrogen++;break;case MetabolismType.SulfurChemosynthesis:sulfur++;break;case MetabolismType.Methanogenesis:methanogenesis++;break;case MetabolismType.Photosynthesis:photosynthesis++;break;case MetabolismType.Methanotrophy:methanotrophy++;break;}switch(state.Locomotion[i]){case LocomotionType.PassiveDrift:passive++;break;case LocomotionType.Amoeboid:amoeboid++;break;case LocomotionType.Flagellum:flagellum++;break;case LocomotionType.Anchored:anchored++;break;}}
        double samples=Math.Max(1,temperatureSamples);
        int h=(int)MetabolismType.Hydrogenotrophy,s=(int)MetabolismType.SulfurChemosynthesis,m=(int)MetabolismType.Methanogenesis,p=(int)MetabolismType.Photosynthesis,mt=(int)MetabolismType.Methanotrophy;
        double evaluationsPerHabitat=agentEvaluations/(double)Math.Max(1L,habitatSamples);
        double evaluationsPerTemperatureRead=agentEvaluations/(double)Math.Max(1L,oceanTemperatureReads);
        Debug.Log($"[GeodesicBiologyTelemetry] population={state.Count} biologySteps={biologySteps} lastStepSeconds={lastBiologyStepSeconds:G6} agentEvaluations={agentEvaluations} habitatSamples={habitatSamples} oceanTemperatureReads={oceanTemperatureReads} photosyntheticLightReads={photosyntheticLightReads} resourceInventoryReads={resourceInventoryReads} competitionPairs={competitionPairs} evaluationsPerHabitat={evaluationsPerHabitat:G6} evaluationsPerTemperatureRead={evaluationsPerTemperatureRead:G6} metabolismOrder=h/s/m/p/mt populationByMetabolism={hydrogen}/{sulfur}/{methanogenesis}/{photosynthesis}/{methanotrophy} locomotionOrder=passive/amoeboid/flagellum/anchored populationByLocomotion={passive}/{amoeboid}/{flagellum}/{anchored} movementUpdates={movementUpdates} horizontalTransitions={horizontalTransitions} verticalTransitions={verticalTransitions} rejectedMovement={rejectedTransitions} births={births} deaths={deaths} starvation={starvationDeaths} lifespan={lifespanDeaths} requests={requestedByMetabolism[h]}/{requestedByMetabolism[s]}/{requestedByMetabolism[m]}/{requestedByMetabolism[p]}/{requestedByMetabolism[mt]} achieved={achievedByMetabolism[h]}/{achievedByMetabolism[s]}/{achievedByMetabolism[m]}/{achievedByMetabolism[p]}/{achievedByMetabolism[mt]} zeroAchieved={zeroAchieved} resourceLimited={resourceLimited} extent={extentByMetabolism[h]:G6}/{extentByMetabolism[s]:G6}/{extentByMetabolism[m]:G6}/{extentByMetabolism[p]:G6}/{extentByMetabolism[mt]:G6} energy={energyByMetabolism[h]:G6}/{energyByMetabolism[s]:G6}/{energyByMetabolism[m]:G6}/{energyByMetabolism[p]:G6}/{energyByMetabolism[mt]:G6} maintenance={maintenancePaid:G6} biologyTemperatureAuthority=coarseOceanLayer temperatureK(min/avg/max)={temperatureMin:G6}/{temperatureSum/samples:G6}/{temperatureMax:G6} performance(min/avg/max)={performanceMin:G6}/{performanceSum/samples:G6}/{performanceMax:G6} zeroPerformance={zeroTemperaturePerformance} invalidHabitat={invalidHabitats} invalidState={invalidBiologicalStates}");
        biologySteps=agentEvaluations=habitatSamples=oceanTemperatureReads=photosyntheticLightReads=resourceInventoryReads=competitionPairs=movementUpdates=horizontalTransitions=verticalTransitions=rejectedTransitions=0;
    }
    public static float ResolvePhotosyntheticLight(float daylight, int layer) => Mathf.Clamp01(daylight) * (layer == 0 ? 1f : layer == 1 ? 0.55f : 0f);
    public void Clear(){planet=null;resources=null;sediment=null;oceanTemperature=null;experiencedTemperature=null;surfaceTemperature=null;grid=null;requests=Array.Empty<Request>();demand=availabilityFactor=delta=Array.Empty<double>();touched=stamps=lightStamps=Array.Empty<int>();temperatureByNode=lightByNode=oxygenByNode=Array.Empty<float>();touchedCount=0;Array.Clear(requestedByMetabolism,0,requestedByMetabolism.Length);Array.Clear(achievedByMetabolism,0,achievedByMetabolism.Length);Array.Clear(extentByMetabolism,0,extentByMetabolism.Length);Array.Clear(energyByMetabolism,0,energyByMetabolism.Length);births=deaths=starvationDeaths=lifespanDeaths=zeroAchieved=resourceLimited=invalidHabitats=invalidBiologicalStates=zeroTemperaturePerformance=temperatureSamples=biologySteps=agentEvaluations=habitatSamples=oceanTemperatureReads=photosyntheticLightReads=resourceInventoryReads=competitionPairs=movementUpdates=horizontalTransitions=verticalTransitions=rejectedTransitions=0;maintenancePaid=diagnosticElapsed=temperatureSum=performanceSum=0d;lastBiologyStepSeconds=0f;temperatureMin=performanceMin=float.PositiveInfinity;temperatureMax=performanceMax=float.NegativeInfinity;}
}
