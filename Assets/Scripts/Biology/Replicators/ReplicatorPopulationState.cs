using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ReplicatorPopulationState is authoritative for hot per-agent simulation fields.
/// Replicator objects are companion/reference objects, not hot-path authorities.
/// Do not reintroduce broad SyncFromAgents/SyncToAgents in hot paths.
/// </summary>
public class ReplicatorPopulationState
{
    public int Count { get; private set; }

    public Vector3[] Position = new Vector3[0];
    public Quaternion[] Rotation = new Quaternion[0];
    public Vector3[] CurrentDirection = new Vector3[0];
    public Vector3[] MoveDirection = new Vector3[0];
    public Vector3[] DesiredMoveDirection = new Vector3[0];
    public Vector3[] Velocity = new Vector3[0];
    public float[] Energy = new float[0];
    public float[] Age = new float[0];
    public float[] OrganicCStore = new float[0];
    public float[] SpeedFactor = new float[0];
    public float[] AttackCooldown = new float[0];
    public float[] FearCooldown = new float[0];
    public bool[] Alive = new bool[0];

    public MetabolismType[] Metabolism = new MetabolismType[0];
    public LocomotionType[] Locomotion = new LocomotionType[0];

    public float[] OptimalTempMin = new float[0];
    public float[] OptimalTempMax = new float[0];
    public float[] LethalTempMargin = new float[0];

    public float[] StarveCo2Seconds = new float[0];
    public float[] StarveH2sSeconds = new float[0];
    public float[] StarveH2Seconds = new float[0];
    public float[] StarveLightSeconds = new float[0];
    public float[] StarveOrganicCFoodSeconds = new float[0];
    public float[] StarveO2Seconds = new float[0];
    public float[] StarveCh4Seconds = new float[0];
    public float[] StarveStoredCSeconds = new float[0];
    public float[] O2ToxicSeconds = new float[0];
    public float[] O2ComfortMax = new float[0];
    public float[] O2StressMax = new float[0];
    public bool[] CanReplicate = new bool[0];

    public float[] LastHabitatValue = new float[0];
    public float[] TumbleProbability = new float[0];
    public float[] NextSenseTime = new float[0];
    public float[] MovementSeed = new float[0];
    public float[] Size = new float[0];
    public Color[] Color = new Color[0];
    public int[] CurrentOceanLayerIndex = new int[0];
    public int[] PreferredOceanLayerIndex = new int[0];

    public void EnsureMatchesAgentCount(List<Replicator> agents)
    {
        if (agents == null)
        {
            Count = 0;
            return;
        }

        int required = agents.Count;
        EnsureCapacity(required);

        if (Count < required)
        {
            for (int i = Count; i < required; i++)
            {
                CopyFromReplicatorData(i, agents[i]);
            }
        }

        Count = required;
    }

    public void AddAgentFromReplicatorData(Replicator agent)
    {
        EnsureCapacity(Count + 1);
        CopyFromReplicatorData(Count, agent);
        Count++;
    }

    public void RemoveAgentAtSwapBack(int index)
    {
        if (index < 0 || index >= Count)
        {
            return;
        }

        int last = Count - 1;
        if (index != last)
        {
            CopyEntry(last, index);
        }

        ClearEntry(last);
        Count = last;
    }

    public void CopyToRenderState(int index, Replicator agent)
    {
        if (agent == null || index < 0 || index >= Count)
        {
            return;
        }

        agent.position = Position[index];
        agent.rotation = Rotation[index];
        agent.currentDirection = CurrentDirection[index];
        agent.moveDirection = MoveDirection[index];
        agent.size = Size[index];
        agent.color = Color[index];
    }

    public void CopyToDebugState(int index, Replicator agent)
    {
        if (agent == null || index < 0 || index >= Count)
        {
            return;
        }

        agent.position = Position[index];
        agent.currentDirection = CurrentDirection[index];
        agent.metabolism = Metabolism[index];
        agent.locomotion = Locomotion[index];
        agent.energy = Energy[index];
        agent.organicCStore = OrganicCStore[index];
        agent.age = Age[index];
        agent.attackCooldown = AttackCooldown[index];
        agent.starveCo2Seconds = StarveCo2Seconds[index];
        agent.starveH2sSeconds = StarveH2sSeconds[index];
        agent.starveH2Seconds = StarveH2Seconds[index];
        agent.starveLightSeconds = StarveLightSeconds[index];
        agent.starveOrganicCFoodSeconds = StarveOrganicCFoodSeconds[index];
        agent.starveO2Seconds = StarveO2Seconds[index];
        agent.starveCh4Seconds = StarveCh4Seconds[index];
        agent.starveStoredCSeconds = StarveStoredCSeconds[index];
        agent.o2ToxicSeconds = O2ToxicSeconds[index];
        agent.currentOceanLayerIndex = CurrentOceanLayerIndex[index];
        agent.preferredOceanLayerIndex = PreferredOceanLayerIndex[index];
        agent.color = Color[index];
    }

    public void CopyPredationEntryToAgent(int index, Replicator agent)
    {
        if (agent == null || index < 0 || index >= Count)
        {
            return;
        }

        agent.energy = Energy[index];
        agent.organicCStore = OrganicCStore[index];
        agent.attackCooldown = AttackCooldown[index];
    }

    public void CopySteeringEntryToAgent(int index, Replicator agent)
    {
        if (agent == null || index < 0 || index >= Count)
        {
            return;
        }

        agent.moveDirection = MoveDirection[index];
        agent.desiredMoveDir = DesiredMoveDirection[index];
        agent.lastHabitatValue = LastHabitatValue[index];
        agent.tumbleProbability = TumbleProbability[index];
        agent.nextSenseTime = NextSenseTime[index];
        agent.currentOceanLayerIndex = CurrentOceanLayerIndex[index];
        agent.preferredOceanLayerIndex = PreferredOceanLayerIndex[index];
    }

    public void CopyHotStateToAgent(int index, Replicator agent)
    {
        if (agent == null || index < 0 || index >= Count)
        {
            return;
        }

        agent.position = Position[index];
        agent.rotation = Rotation[index];
        agent.currentDirection = CurrentDirection[index];
        agent.moveDirection = MoveDirection[index];
        agent.desiredMoveDir = DesiredMoveDirection[index];
        agent.velocity = Velocity[index];
        agent.energy = Energy[index];
        agent.age = Age[index];
        agent.organicCStore = OrganicCStore[index];
        agent.speedFactor = SpeedFactor[index];
        agent.attackCooldown = AttackCooldown[index];
        agent.fearCooldown = FearCooldown[index];
        agent.starveCo2Seconds = StarveCo2Seconds[index];
        agent.starveH2sSeconds = StarveH2sSeconds[index];
        agent.starveH2Seconds = StarveH2Seconds[index];
        agent.starveLightSeconds = StarveLightSeconds[index];
        agent.starveOrganicCFoodSeconds = StarveOrganicCFoodSeconds[index];
        agent.starveO2Seconds = StarveO2Seconds[index];
        agent.starveCh4Seconds = StarveCh4Seconds[index];
        agent.starveStoredCSeconds = StarveStoredCSeconds[index];
        agent.o2ToxicSeconds = O2ToxicSeconds[index];
        agent.o2ComfortMax = O2ComfortMax[index];
        agent.o2StressMax = O2StressMax[index];
        agent.canReplicate = CanReplicate[index];
        agent.lastHabitatValue = LastHabitatValue[index];
        agent.tumbleProbability = TumbleProbability[index];
        agent.nextSenseTime = NextSenseTime[index];
        agent.currentOceanLayerIndex = CurrentOceanLayerIndex[index];
        agent.preferredOceanLayerIndex = PreferredOceanLayerIndex[index];
        agent.color = Color[index];
        agent.size = Size[index];
    }

    // Compatibility-only broad mirror. Do not use in hot simulation systems.
    public void SyncFromAgents(List<Replicator> agents)
    {
        EnsureMatchesAgentCount(agents);
        for (int i = 0; i < Count; i++)
        {
            CopyFromReplicatorData(i, agents[i]);
        }
    }

    // Compatibility-only broad mirror. Do not use in hot simulation systems.
    public void SyncToAgents(List<Replicator> agents)
    {
        int count = Mathf.Min(Count, agents.Count);
        for (int i = 0; i < count; i++)
        {
            CopyHotStateToAgent(i, agents[i]);
        }
    }

    private void CopyFromReplicatorData(int index, Replicator agent)
    {
        Position[index] = agent.position;
        Rotation[index] = agent.rotation;
        CurrentDirection[index] = agent.currentDirection;
        MoveDirection[index] = agent.moveDirection;
        DesiredMoveDirection[index] = agent.desiredMoveDir;
        Velocity[index] = agent.velocity;
        Energy[index] = agent.energy;
        Age[index] = agent.age;
        OrganicCStore[index] = agent.organicCStore;
        SpeedFactor[index] = agent.speedFactor;
        AttackCooldown[index] = agent.attackCooldown;
        FearCooldown[index] = agent.fearCooldown;
        Metabolism[index] = agent.metabolism;
        Locomotion[index] = agent.locomotion;
        Alive[index] = true;
        OptimalTempMin[index] = agent.optimalTempMin;
        OptimalTempMax[index] = agent.optimalTempMax;
        LethalTempMargin[index] = agent.lethalTempMargin;
        StarveCo2Seconds[index] = agent.starveCo2Seconds;
        StarveH2sSeconds[index] = agent.starveH2sSeconds;
        StarveH2Seconds[index] = agent.starveH2Seconds;
        StarveLightSeconds[index] = agent.starveLightSeconds;
        StarveOrganicCFoodSeconds[index] = agent.starveOrganicCFoodSeconds;
        StarveO2Seconds[index] = agent.starveO2Seconds;
        StarveCh4Seconds[index] = agent.starveCh4Seconds;
        StarveStoredCSeconds[index] = agent.starveStoredCSeconds;
        O2ToxicSeconds[index] = agent.o2ToxicSeconds;
        O2ComfortMax[index] = agent.o2ComfortMax;
        O2StressMax[index] = agent.o2StressMax;
        CanReplicate[index] = agent.canReplicate;
        LastHabitatValue[index] = agent.lastHabitatValue;
        TumbleProbability[index] = agent.tumbleProbability;
        NextSenseTime[index] = agent.nextSenseTime;
        MovementSeed[index] = agent.movementSeed;
        Size[index] = agent.size;
        Color[index] = agent.color;
        CurrentOceanLayerIndex[index] = agent.currentOceanLayerIndex;
        PreferredOceanLayerIndex[index] = agent.preferredOceanLayerIndex;
    }

    private void CopyEntry(int srcIndex, int dstIndex)
    {
        Position[dstIndex] = Position[srcIndex];
        Rotation[dstIndex] = Rotation[srcIndex];
        CurrentDirection[dstIndex] = CurrentDirection[srcIndex];
        MoveDirection[dstIndex] = MoveDirection[srcIndex];
        DesiredMoveDirection[dstIndex] = DesiredMoveDirection[srcIndex];
        Velocity[dstIndex] = Velocity[srcIndex];
        Energy[dstIndex] = Energy[srcIndex];
        Age[dstIndex] = Age[srcIndex];
        OrganicCStore[dstIndex] = OrganicCStore[srcIndex];
        SpeedFactor[dstIndex] = SpeedFactor[srcIndex];
        AttackCooldown[dstIndex] = AttackCooldown[srcIndex];
        FearCooldown[dstIndex] = FearCooldown[srcIndex];
        Alive[dstIndex] = Alive[srcIndex];
        Metabolism[dstIndex] = Metabolism[srcIndex];
        Locomotion[dstIndex] = Locomotion[srcIndex];
        OptimalTempMin[dstIndex] = OptimalTempMin[srcIndex];
        OptimalTempMax[dstIndex] = OptimalTempMax[srcIndex];
        LethalTempMargin[dstIndex] = LethalTempMargin[srcIndex];
        StarveCo2Seconds[dstIndex] = StarveCo2Seconds[srcIndex];
        StarveH2sSeconds[dstIndex] = StarveH2sSeconds[srcIndex];
        StarveH2Seconds[dstIndex] = StarveH2Seconds[srcIndex];
        StarveLightSeconds[dstIndex] = StarveLightSeconds[srcIndex];
        StarveOrganicCFoodSeconds[dstIndex] = StarveOrganicCFoodSeconds[srcIndex];
        StarveO2Seconds[dstIndex] = StarveO2Seconds[srcIndex];
        StarveCh4Seconds[dstIndex] = StarveCh4Seconds[srcIndex];
        StarveStoredCSeconds[dstIndex] = StarveStoredCSeconds[srcIndex];
        O2ToxicSeconds[dstIndex] = O2ToxicSeconds[srcIndex];
        O2ComfortMax[dstIndex] = O2ComfortMax[srcIndex];
        O2StressMax[dstIndex] = O2StressMax[srcIndex];
        CanReplicate[dstIndex] = CanReplicate[srcIndex];
        LastHabitatValue[dstIndex] = LastHabitatValue[srcIndex];
        TumbleProbability[dstIndex] = TumbleProbability[srcIndex];
        NextSenseTime[dstIndex] = NextSenseTime[srcIndex];
        MovementSeed[dstIndex] = MovementSeed[srcIndex];
        Size[dstIndex] = Size[srcIndex];
        Color[dstIndex] = Color[srcIndex];
        CurrentOceanLayerIndex[dstIndex] = CurrentOceanLayerIndex[srcIndex];
        PreferredOceanLayerIndex[dstIndex] = PreferredOceanLayerIndex[srcIndex];
    }

    private void ClearEntry(int index)
    {
        Position[index] = default;
        Rotation[index] = default;
        CurrentDirection[index] = default;
        MoveDirection[index] = default;
        DesiredMoveDirection[index] = default;
        Velocity[index] = default;
        Energy[index] = 0f;
        Age[index] = 0f;
        OrganicCStore[index] = 0f;
        SpeedFactor[index] = 0f;
        AttackCooldown[index] = 0f;
        FearCooldown[index] = 0f;
        Alive[index] = false;
        Metabolism[index] = default;
        Locomotion[index] = default;
    }

    void EnsureCapacity(int required)
    {
        if (Position.Length >= required)
        {
            return;
        }

        int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(4, required));
        Array.Resize(ref Position, newCapacity);
        Array.Resize(ref Rotation, newCapacity);
        Array.Resize(ref CurrentDirection, newCapacity);
        Array.Resize(ref MoveDirection, newCapacity);
        Array.Resize(ref DesiredMoveDirection, newCapacity);
        Array.Resize(ref Velocity, newCapacity);
        Array.Resize(ref Energy, newCapacity);
        Array.Resize(ref Age, newCapacity);
        Array.Resize(ref OrganicCStore, newCapacity);
        Array.Resize(ref SpeedFactor, newCapacity);
        Array.Resize(ref AttackCooldown, newCapacity);
        Array.Resize(ref FearCooldown, newCapacity);
        Array.Resize(ref Alive, newCapacity);
        Array.Resize(ref Metabolism, newCapacity);
        Array.Resize(ref Locomotion, newCapacity);
        Array.Resize(ref OptimalTempMin, newCapacity);
        Array.Resize(ref OptimalTempMax, newCapacity);
        Array.Resize(ref LethalTempMargin, newCapacity);
        Array.Resize(ref StarveCo2Seconds, newCapacity);
        Array.Resize(ref StarveH2sSeconds, newCapacity);
        Array.Resize(ref StarveH2Seconds, newCapacity);
        Array.Resize(ref StarveLightSeconds, newCapacity);
        Array.Resize(ref StarveOrganicCFoodSeconds, newCapacity);
        Array.Resize(ref StarveO2Seconds, newCapacity);
        Array.Resize(ref StarveCh4Seconds, newCapacity);
        Array.Resize(ref StarveStoredCSeconds, newCapacity);
        Array.Resize(ref O2ToxicSeconds, newCapacity);
        Array.Resize(ref O2ComfortMax, newCapacity);
        Array.Resize(ref O2StressMax, newCapacity);
        Array.Resize(ref CanReplicate, newCapacity);
        Array.Resize(ref LastHabitatValue, newCapacity);
        Array.Resize(ref TumbleProbability, newCapacity);
        Array.Resize(ref NextSenseTime, newCapacity);
        Array.Resize(ref MovementSeed, newCapacity);
        Array.Resize(ref Size, newCapacity);
        Array.Resize(ref Color, newCapacity);
        Array.Resize(ref CurrentOceanLayerIndex, newCapacity);
        Array.Resize(ref PreferredOceanLayerIndex, newCapacity);
    }
}
