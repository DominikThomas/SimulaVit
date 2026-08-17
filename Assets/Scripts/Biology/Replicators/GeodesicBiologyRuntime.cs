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

    private struct Request
    {
        public int Node;
        public int Cell;
        public int Layer;
        public GeodesicOceanResource A, B, Product;
        public double NeedA, NeedB, ProductCoefficient, Desired, Actual;
        public float EnergyPerExtent, StorePerExtent;
        public bool SulfurProduct;
    }

    public bool Initialize(PlanetGenerator generator, List<Replicator> agents, ReplicatorPopulationState state,
        int requestedFounders, float minLife, float maxLife, Color color, float biomassTarget)
    {
        Clear();
        planet = generator;
        resources = generator != null ? generator.GetComponent<GeodesicOceanResourceField>() : null;
        sediment = generator != null ? generator.GetComponent<GeodesicOceanSedimentField>() : null;
        oceanTemperature = generator != null ? generator.GetComponent<GeodesicOceanTemperatureField>() : null;
        experiencedTemperature = generator != null ? generator.GetComponent<GeodesicExperiencedTemperatureField>() : null;
        surfaceTemperature = generator != null ? generator.GetComponent<GeodesicSurfaceTemperatureField>() : null;
        grid = resources != null ? resources.SourceGrid : null;
        if (grid == null || !resources.IsInitialized || oceanTemperature == null || !oceanTemperature.IsInitialized)
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
        int spawned = SpawnFounders(requestedFounders, random, agents, state, minLife, maxLife, color, biomassTarget);
        int sulfur = 0, methane = 0, occupiedCells = 0, occupiedLayers = 0;
        var cellSeen = new bool[grid.CellCount]; var layerSeen = new bool[grid.MaximumLayerCount];
        for (int i = 0; i < spawned; i++) { if (agents[i].metabolism == MetabolismType.SulfurChemosynthesis) sulfur++; else methane++; if (!cellSeen[agents[i].geodesicCellIndex]) { cellSeen[agents[i].geodesicCellIndex] = true; occupiedCells++; } int layer=agents[i].currentOceanLayerIndex; if(!layerSeen[layer]){layerSeen[layer]=true;occupiedLayers++;} }
        Debug.Log($"[GeodesicBiology] mode=Geodesic biologySeed={seed} requested={requestedFounders} spawned={spawned} sulfur={sulfur} methanogenesis={methane} occupiedCells={occupiedCells} occupiedLayers={occupiedLayers} ventFounders={spawned} founders=compact-submarine-vent-bottom");
        return true;
    }

    private int SpawnFounders(int count, System.Random random, List<Replicator> agents, ReplicatorPopulationState state,
        float minLife, float maxLife, Color color, float biomassTarget)
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
            agent.geodesicCellIndex = selected.CellIndex; agent.currentOceanLayerIndex = bottomLayer;
            agent.preferredOceanLayerIndex = bottomLayer; agent.energy = Mathf.Lerp(0.1f, 0.5f, (float)random.NextDouble()); agent.biomassTarget = Mathf.Max(0.0001f, biomassTarget);
            agents.Add(agent); state.AddAgentFromReplicatorData(agent);
        }
        return count;
    }

    public void Step(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float sulfurCo2, float sulfurH2s, float sulfurEnergy, float methaneCo2, float methaneH2, float methaneEnergy,
        float methanotrophyCh4, float methanotrophyO2, float methanotrophyEnergy, float photoCo2, float photoEnergy,
        float maxStore, float reproductionRate, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, float childSplit, int maxPopulation)
    {
        if (agents.Count == 0) return;
        state.EnsureMatchesAgentCount(agents); EnsureRequestCapacity(agents.Count); BeginSparseStep();
        using (Evaluation.Auto()) for (int i = 0; i < state.Count; i++) BuildRequest(i, agents[i], state, dt, sulfurCo2, sulfurH2s, sulfurEnergy, methaneCo2, methaneH2, methaneEnergy, methanotrophyCh4, methanotrophyO2, methanotrophyEnergy, photoCo2, photoEnergy);
        using (Competition.Auto()) ResolveCompetition(state.Count);
        using (Commit.Auto()) CommitRequests(state.Count, state, maxStore);
        using (Lifecycle.Auto()) RunLifecycle(dt, agents, state, maintenance, reproductionRate, carbonDivision, divisionCost, replicationCost, divisionMultiple, childSplit, maxPopulation);
    }

    private void BuildRequest(int i, Replicator agent, ReplicatorPopulationState state, float dt, float sCo2, float sH2s, float sEnergy, float mCo2, float mH2, float mEnergy, float mtCh4, float mtO2, float mtEnergy, float pCo2, float pEnergy)
    {
        int cell = agent.geodesicCellIndex, layer = state.CurrentOceanLayerIndex[i]; Request r = default; r.Cell = cell; r.Layer = layer;
        if (!grid.IsNodeActive(cell, layer)) { requests[i] = r; return; }
        r.Node = grid.GetNodeIndex(cell, layer); float scale = dt / 0.5f;
        switch (state.Metabolism[i])
        {
            case MetabolismType.SulfurChemosynthesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2S; r.NeedA=sCo2; r.NeedB=sH2s; r.EnergyPerExtent=sEnergy; r.StorePerExtent=sCo2; r.SulfurProduct=true; break;
            case MetabolismType.Methanogenesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.Product=GeodesicOceanResource.CH4; r.NeedA=mCo2; r.NeedB=mH2; r.ProductCoefficient=mCo2; r.EnergyPerExtent=mEnergy; r.StorePerExtent=(float)(mCo2*0.15); break;
            case MetabolismType.Methanotrophy: r.A=GeodesicOceanResource.CH4; r.B=GeodesicOceanResource.O2; r.Product=GeodesicOceanResource.CO2; r.NeedA=mtCh4; r.NeedB=mtO2; r.ProductCoefficient=mtCh4; r.EnergyPerExtent=mtEnergy; r.StorePerExtent=(float)(mtCh4*0.15); break;
            case MetabolismType.Photosynthesis: float light=ResolvePhotosyntheticLight(surfaceTemperature.GetCellInsolationCosine(cell),layer); r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.CO2; r.Product=GeodesicOceanResource.O2; r.NeedA=pCo2*light; r.NeedB=0; r.ProductCoefficient=r.NeedA; r.EnergyPerExtent=pEnergy*r.NeedA; r.StorePerExtent=(float)r.NeedA; break;
            default: requests[i]=r; return;
        }
        float temperature;
        if (experiencedTemperature == null || !experiencedTemperature.TryGetLocalTemperatureKelvin(cell, layer, agent.position, out temperature))
            temperature = oceanTemperature.GetLayerTemperatureKelvin(cell, layer);
        float optimumMin = state.OptimalTempMin[i], optimumMax = state.OptimalTempMax[i];
        float temperatureScale = temperature >= optimumMin && temperature <= optimumMax ? 1f
            : Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(temperature - optimumMin), Mathf.Abs(temperature - optimumMax)) / Mathf.Max(1f, state.LethalTempMargin[i]));
        r.Desired=Math.Max(0,scale*temperatureScale); requests[i]=r; if(r.Desired<=0||r.NeedA<=0)return; Touch(r.Node); demand[(int)r.A*grid.NodeCapacity+r.Node]+=r.NeedA*r.Desired; if(r.NeedB>0)demand[(int)r.B*grid.NodeCapacity+r.Node]+=r.NeedB*r.Desired;
    }

    private void ResolveCompetition(int count) { for(int i=0;i<count;i++){Request r=requests[i]; if(r.Desired<=0)continue; double f=1; f=Math.Min(f,AvailableFactor(r,r.A,r.NeedA)); if(r.NeedB>0)f=Math.Min(f,AvailableFactor(r,r.B,r.NeedB)); r.Actual=r.Desired*f; requests[i]=r;} }
    private double AvailableFactor(Request r, GeodesicOceanResource resource, double need){double total=demand[(int)resource*grid.NodeCapacity+r.Node]; if(total<=0)return 0; double available=resources.GetNodeInventory(r.Cell,r.Layer,resource); return Math.Min(1,available/total);}
    private void CommitRequests(int count, ReplicatorPopulationState state, float maxStore){for(int i=0;i<count;i++){Request r=requests[i]; if(r.Actual<=0)continue; delta[(int)r.A*grid.NodeCapacity+r.Node]-=r.NeedA*r.Actual; if(r.NeedB>0)delta[(int)r.B*grid.NodeCapacity+r.Node]-=r.NeedB*r.Actual; if(r.ProductCoefficient>0)delta[(int)r.Product*grid.NodeCapacity+r.Node]+=r.ProductCoefficient*r.Actual; if(r.SulfurProduct)sediment.DepositSameColumn(r.Cell,r.NeedB*r.Actual*0.5,0); state.Energy[i]+=r.EnergyPerExtent*(float)r.Actual; state.OrganicCStore[i]=Mathf.Min(maxStore,state.OrganicCStore[i]+r.StorePerExtent*(float)r.Actual);} for(int t=0;t<touchedCount;t++){int node=touched[t]; for(int k=0;k<ResourceCount;k++){double d=delta[k*grid.NodeCapacity+node]; if(d!=0)resources.ApplyDirectExchangeInventory((GeodesicOceanResource)k,node,d);}}}
    private void RunLifecycle(float dt,List<Replicator> agents,ReplicatorPopulationState state,float maintenance,float rate,bool carbon,float divisionCost,float replicationCost,float multiple,float split,int maxPopulation){for(int i=state.Count-1;i>=0;i--){state.Age[i]+=dt; state.Energy[i]-=maintenance*dt; if(state.Energy[i]<=0||state.Age[i]>agents[i].maxLifespan){Remove(i,agents,state);continue;} bool eligible=state.Energy[i]>=(carbon?divisionCost:replicationCost)&&(!carbon||state.OrganicCStore[i]>=Mathf.Max(1,multiple)*agents[i].biomassTarget); if(eligible&&agents.Count<maxPopulation&&reproductionRandom.NextDouble()<rate*dt){Replicator p=agents[i]; var child=new Replicator(p.position,p.rotation,p.maxLifespan,p.color,p.traits,p.movementSeed,p.metabolism,p.locomotion,p.locomotionSkill); child.geodesicCellIndex=p.geodesicCellIndex;child.currentOceanLayerIndex=p.currentOceanLayerIndex;child.preferredOceanLayerIndex=p.currentOceanLayerIndex;child.biomassTarget=p.biomassTarget; float cost=carbon?divisionCost:replicationCost;state.Energy[i]-=cost;child.energy=Mathf.Max(0.1f,state.Energy[i]*0.5f);if(carbon){child.organicCStore=state.OrganicCStore[i]*Mathf.Clamp01(split);state.OrganicCStore[i]-=child.organicCStore;}agents.Add(child);state.AddAgentFromReplicatorData(child);}}}
    private static void Remove(int i,List<Replicator>a,ReplicatorPopulationState s){int last=a.Count-1;if(i!=last)a[i]=a[last];a.RemoveAt(last);s.RemoveAgentAtSwapBack(i);}
    private void Touch(int node){if(stamps[node]==generation)return;stamps[node]=generation;touched[touchedCount++]=node;for(int r=0;r<ResourceCount;r++){demand[r*grid.NodeCapacity+node]=0;delta[r*grid.NodeCapacity+node]=0;}}
    private void BeginSparseStep(){touchedCount=0;if(++generation==int.MaxValue){Array.Clear(stamps,0,stamps.Length);generation=1;}}
    private void EnsureRequestCapacity(int count){if(requests.Length<count)Array.Resize(ref requests,Mathf.NextPowerOfTwo(count));}
    public static float ResolvePhotosyntheticLight(float daylight, int layer) => Mathf.Clamp01(daylight) * (layer == 0 ? 1f : layer == 1 ? 0.55f : 0f);
    public void Clear(){planet=null;resources=null;sediment=null;oceanTemperature=null;experiencedTemperature=null;surfaceTemperature=null;grid=null;requests=Array.Empty<Request>();demand=delta=Array.Empty<double>();touched=stamps=Array.Empty<int>();touchedCount=0;}
}
