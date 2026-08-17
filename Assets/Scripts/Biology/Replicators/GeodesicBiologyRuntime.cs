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
    private const int ResourceCount = 7;

    private PlanetGenerator planet;
    private GeodesicOceanResourceField resources;
    private GeodesicOceanSedimentField sediment;
    private GeodesicOceanTemperatureField oceanTemperature;
    private GeodesicExperiencedTemperatureField experiencedTemperature;
    private GeodesicSurfaceTemperatureField surfaceTemperature;
    private GeodesicOceanLayerGrid grid;
    private Request[] requests = Array.Empty<Request>();
    private double[] demand = Array.Empty<double>();
    private double[] delta = Array.Empty<double>();
    private int[] touched = Array.Empty<int>();
    private int[] stamps = Array.Empty<int>();
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
        Vector2 sulfurTemperatureRange, Vector2 methanogenesisTemperatureRange, float lethalTemperatureMargin)
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
        delta = new double[grid.NodeCapacity * ResourceCount];
        touched = new int[grid.NodeCapacity];
        stamps = new int[grid.NodeCapacity];
        requests = new Request[Mathf.Max(1, requestedFounders)];
        int seed = PlanetSeedUtility.DeriveSeed(generator.randomSeed, PlanetSeedDomain.Biology, PlanetGenerator.GenerationVersion);
        var random = new System.Random(seed);
        reproductionRandom = new System.Random(seed ^ 0x5EED123);
        int spawned = SpawnFounders(requestedFounders, random, agents, state, minLife, maxLife, color, biomassTarget,
            sulfurTemperatureRange, methanogenesisTemperatureRange, lethalTemperatureMargin);
        int sulfur = 0, methane = 0, occupiedCells = 0, occupiedLayers = 0;
        var cellSeen = new bool[grid.CellCount]; var layerSeen = new bool[grid.MaximumLayerCount];
        for (int i = 0; i < spawned; i++) { if (agents[i].metabolism == MetabolismType.SulfurChemosynthesis) sulfur++; else methane++; if (!cellSeen[agents[i].geodesicCellIndex]) { cellSeen[agents[i].geodesicCellIndex] = true; occupiedCells++; } int layer=agents[i].currentOceanLayerIndex; if(!layerSeen[layer]){layerSeen[layer]=true;occupiedLayers++;} }
        Debug.Log($"[GeodesicBiology] mode=Geodesic biologySeed={seed} requested={requestedFounders} spawned={spawned} sulfur={sulfur} methanogenesis={methane} occupiedCells={occupiedCells} occupiedLayers={occupiedLayers} ventFounders={spawned} founders=compact-submarine-vent-bottom");
        return true;
    }

    private int SpawnFounders(int count, System.Random random, List<Replicator> agents, ReplicatorPopulationState state,
        float minLife, float maxLife, Color color, float biomassTarget, Vector2 sulfurTemperatureRange,
        Vector2 methanogenesisTemperatureRange, float lethalTemperatureMargin)
    {
        int validVents = 0;
        for (int i = 0; i < resources.CompactOutletCount; i++)
            if (resources.TryGetVentOutlet(i, out GeodesicVentSourceOutlet outlet) && outlet.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(outlet.CellIndex, outlet.SourceNode % grid.MaximumLayerCount)) validVents++;
        if (count > 0 && validVents == 0) { Debug.LogWarning("[GeodesicBiology] No valid submarine vent habitat; no founders were spawned."); return 0; }
        for (int i = 0; i < count; i++)
        {
            int pick = random.Next(validVents), seen = 0; GeodesicVentSourceOutlet selected = default;
            for (int v = 0; v < resources.CompactOutletCount; v++) if (resources.TryGetVentOutlet(v, out var candidate) && candidate.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(candidate.CellIndex, candidate.SourceNode % grid.MaximumLayerCount) && seen++ == pick) { selected = candidate; break; }
            MetabolismType metabolism = random.Next(2) == 0 ? MetabolismType.SulfurChemosynthesis : MetabolismType.Methanogenesis;
            Vector3 direction = planet.GeodesicTopology.CellDirections[selected.CellIndex];
            int bottomLayer = selected.SourceNode % grid.MaximumLayerCount;
            Vector3 position = planet.transform.TransformPoint(direction * grid.LayerCenterRadius[grid.GetNodeIndex(selected.CellIndex, bottomLayer)]);
            float life = Mathf.Lerp(minLife, maxLife, (float)random.NextDouble());
            var agent = new Replicator(position, Quaternion.FromToRotation(Vector3.up, direction), life, color,
                new Replicator.Traits(true, true, true, 0f), (float)random.NextDouble(), metabolism, LocomotionType.Anchored);
            Vector2 thermalRange = metabolism == MetabolismType.Methanogenesis
                ? methanogenesisTemperatureRange
                : sulfurTemperatureRange;
            InitializeFounderBiologicalState(agent, selected.CellIndex, bottomLayer, thermalRange,
                lethalTemperatureMargin, Mathf.Lerp(0.1f, 0.5f, (float)random.NextDouble()),
                Mathf.Lerp(0f, life * 0.5f, (float)random.NextDouble()), biomassTarget);
            agents.Add(agent); state.AddAgentFromReplicatorData(agent);
        }
        return count;
    }

    public void Step(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float sulfurCo2, float sulfurH2s, float sulfurEnergy, float methaneCo2, float methaneH2, float methaneEnergy,
        float methanotrophyCh4, float methanotrophyO2, float methanotrophyEnergy, float photoCo2, float photoEnergy,
        float maxStore, float reproductionRate, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, float childSplit, int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause)
    {
        if (agents.Count == 0)
        {
            diagnosticElapsed += dt;
            if (diagnosticElapsed >= 10d) { LogDiagnostics(state); diagnosticElapsed = 0d; }
            return;
        }
        state.EnsureMatchesAgentCount(agents); EnsureRequestCapacity(agents.Count); BeginSparseStep();
        using (Evaluation.Auto()) for (int i = 0; i < state.Count; i++) BuildRequest(i, agents[i], state, dt, sulfurCo2, sulfurH2s, sulfurEnergy, methaneCo2, methaneH2, methaneEnergy, methanotrophyCh4, methanotrophyO2, methanotrophyEnergy, photoCo2, photoEnergy);
        using (Competition.Auto()) ResolveCompetition(state.Count);
        using (Commit.Auto()) CommitRequests(state.Count, state, maxStore);
        using (Lifecycle.Auto()) RunLifecycle(dt, agents, state, maintenance, reproductionRate, carbonDivision, divisionCost, replicationCost, divisionMultiple, childSplit, maxPopulation, registerDeathCause);
        diagnosticElapsed += dt;
        if (diagnosticElapsed >= 10d)
        {
            LogDiagnostics(state);
            diagnosticElapsed = 0d;
        }
    }

    private void BuildRequest(int i, Replicator agent, ReplicatorPopulationState state, float dt, float sCo2, float sH2s, float sEnergy, float mCo2, float mH2, float mEnergy, float mtCh4, float mtO2, float mtEnergy, float pCo2, float pEnergy)
    {
        int cell = agent.geodesicCellIndex, layer = state.CurrentOceanLayerIndex[i]; Request r = default; r.Cell = cell; r.Layer = layer; r.Metabolism = state.Metabolism[i];
        if (!grid.IsNodeActive(cell, layer)) { invalidHabitats++; requests[i] = r; return; }
        r.Node = grid.GetNodeIndex(cell, layer); float scale = dt / 0.5f;
        switch (state.Metabolism[i])
        {
            case MetabolismType.SulfurChemosynthesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2S; r.NeedA=sCo2; r.NeedB=sH2s; r.EnergyPerExtent=sEnergy; r.StorePerExtent=sCo2; r.SulfurProduct=true; break;
            case MetabolismType.Methanogenesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.Product=GeodesicOceanResource.CH4; r.NeedA=mCo2; r.NeedB=mH2; r.ProductCoefficient=mCo2*0.85; r.EnergyPerExtent=mEnergy*0.85f; r.StorePerExtent=(float)(mCo2*0.15); break;
            case MetabolismType.Methanotrophy: r.A=GeodesicOceanResource.CH4; r.B=GeodesicOceanResource.O2; r.Product=GeodesicOceanResource.CO2; r.NeedA=mtCh4; r.NeedB=mtO2; r.ProductCoefficient=mtCh4; r.EnergyPerExtent=mtEnergy; r.StorePerExtent=(float)(mtCh4*0.15); break;
            case MetabolismType.Photosynthesis:
                float light = ResolvePhotosyntheticLight(surfaceTemperature.GetCellInsolationCosine(cell), layer);
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
        // Replicator.position is visual-only in Geodesic mode. Until biology owns an
        // authoritative sub-cell coordinate, feeding that position into the vent-core
        // query would make rendering placement control biology and can expose founders
        // to source-fluid temperature. Cell/layer temperature is the authoritative
        // organism temperature for this foundation.
        float temperature = oceanTemperature.GetLayerTemperatureKelvin(cell, layer);
        float optimumMin = state.OptimalTempMin[i], optimumMax = state.OptimalTempMax[i];
        float temperatureScale = CalculateTemperaturePerformance(temperature, optimumMin, optimumMax, state.LethalTempMargin[i]);
        r.Temperature = temperature;
        r.TemperaturePerformance = temperatureScale;
        AccumulateTemperature(temperature, temperatureScale);
        if (!float.IsFinite(state.Energy[i]) || !float.IsFinite(state.OrganicCStore[i]) || !float.IsFinite(temperature)) invalidBiologicalStates++;
        r.Desired=Math.Max(0,scale*temperatureScale); requests[i]=r; if(r.Desired<=0||r.NeedA<=0)return; Touch(r.Node); demand[(int)r.A*grid.NodeCapacity+r.Node]+=r.NeedA*r.Desired; if(r.NeedB>0)demand[(int)r.B*grid.NodeCapacity+r.Node]+=r.NeedB*r.Desired;
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
                factor = Math.Min(factor, AvailableFactor(r, r.A, r.NeedA));
                if (r.NeedB > 0) factor = Math.Min(factor, AvailableFactor(r, r.B, r.NeedB));
            }
            r.AvailabilityFactor = factor;
            r.Actual = r.Desired * factor;
            if (factor < 1d && r.Desired > 0) resourceLimited++;
            if (r.Actual > 0) achievedByMetabolism[metabolism]++; else zeroAchieved++;
            requests[i] = r;
        }
    }
    private double AvailableFactor(Request r, GeodesicOceanResource resource, double need){double total=demand[(int)resource*grid.NodeCapacity+r.Node]; if(total<=0)return 0; double available=resources.GetNodeInventory(r.Cell,r.Layer,resource); return Math.Min(1,available/total);}
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
            var child = new Replicator(parent.position, parent.rotation, parent.maxLifespan, parent.color, parent.traits,
                parent.movementSeed, state.Metabolism[i], parent.locomotion, parent.locomotionSkill);
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
    private void Touch(int node){if(stamps[node]==generation)return;stamps[node]=generation;touched[touchedCount++]=node;for(int r=0;r<ResourceCount;r++){demand[r*grid.NodeCapacity+node]=0;delta[r*grid.NodeCapacity+node]=0;}}
    private void BeginSparseStep(){touchedCount=0;if(++generation==int.MaxValue){Array.Clear(stamps,0,stamps.Length);generation=1;}}
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
        int sulfur=0,methanogenesis=0,photosynthesis=0,methanotrophy=0;
        for(int i=0;i<state.Count;i++){switch(state.Metabolism[i]){case MetabolismType.SulfurChemosynthesis:sulfur++;break;case MetabolismType.Methanogenesis:methanogenesis++;break;case MetabolismType.Photosynthesis:photosynthesis++;break;case MetabolismType.Methanotrophy:methanotrophy++;break;}}
        double samples=Math.Max(1,temperatureSamples);
        int s=(int)MetabolismType.SulfurChemosynthesis,m=(int)MetabolismType.Methanogenesis,p=(int)MetabolismType.Photosynthesis,mt=(int)MetabolismType.Methanotrophy;
        Debug.Log($"[GeodesicBiologyTelemetry] population={state.Count} byMetabolism(sulfur/methanogenesis/photosynthesis/methanotrophy)={sulfur}/{methanogenesis}/{photosynthesis}/{methanotrophy} births={births} deaths={deaths} starvation={starvationDeaths} lifespan={lifespanDeaths} requests(s/m/p/mt)={requestedByMetabolism[s]}/{requestedByMetabolism[m]}/{requestedByMetabolism[p]}/{requestedByMetabolism[mt]} achieved(s/m/p/mt)={achievedByMetabolism[s]}/{achievedByMetabolism[m]}/{achievedByMetabolism[p]}/{achievedByMetabolism[mt]} zeroAchieved={zeroAchieved} resourceLimited={resourceLimited} extent(s/m/p/mt)={extentByMetabolism[s]:G6}/{extentByMetabolism[m]:G6}/{extentByMetabolism[p]:G6}/{extentByMetabolism[mt]:G6} energy(s/m/p/mt)={energyByMetabolism[s]:G6}/{energyByMetabolism[m]:G6}/{energyByMetabolism[p]:G6}/{energyByMetabolism[mt]:G6} maintenance={maintenancePaid:G6} biologyTemperatureAuthority=coarseOceanLayer temperatureK(min/avg/max)={temperatureMin:G6}/{temperatureSum/samples:G6}/{temperatureMax:G6} performance(min/avg/max)={performanceMin:G6}/{performanceSum/samples:G6}/{performanceMax:G6} zeroPerformance={zeroTemperaturePerformance} invalidHabitat={invalidHabitats} invalidState={invalidBiologicalStates}");
    }
    public static float ResolvePhotosyntheticLight(float daylight, int layer) => Mathf.Clamp01(daylight) * (layer == 0 ? 1f : layer == 1 ? 0.55f : 0f);
    public void Clear(){planet=null;resources=null;sediment=null;oceanTemperature=null;experiencedTemperature=null;surfaceTemperature=null;grid=null;requests=Array.Empty<Request>();demand=delta=Array.Empty<double>();touched=stamps=Array.Empty<int>();touchedCount=0;Array.Clear(requestedByMetabolism,0,requestedByMetabolism.Length);Array.Clear(achievedByMetabolism,0,achievedByMetabolism.Length);Array.Clear(extentByMetabolism,0,extentByMetabolism.Length);Array.Clear(energyByMetabolism,0,energyByMetabolism.Length);births=deaths=starvationDeaths=lifespanDeaths=zeroAchieved=resourceLimited=invalidHabitats=invalidBiologicalStates=zeroTemperaturePerformance=temperatureSamples=0;maintenancePaid=diagnosticElapsed=temperatureSum=performanceSum=0d;temperatureMin=performanceMin=float.PositiveInfinity;temperatureMax=performanceMax=float.NegativeInfinity;}
}
