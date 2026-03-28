using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Packed struct-of-arrays runtime state for hot per-agent simulation data.
/// This state is the authoritative source for hot-path simulation fields while
/// List&lt;Replicator&gt; remains a companion object list for references, debug, and bridging.
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

    public int AddAgentFromReplicatorData(Replicator agent)
    {
        int index = Count;
        Count++;
        EnsureCapacity(Count);
        CopyAgentToEntry(index, agent);
        return index;
    }

    public void RemoveAgentAtSwapBack(List<Replicator> agents, int index)
    {
        int lastIndex = Count - 1;
        if (index < 0 || index > lastIndex || agents == null || agents.Count != Count)
        {
            return;
        }

        if (index != lastIndex)
        {
            MoveEntry(lastIndex, index);
            Replicator swappedAgent = agents[lastIndex];
            agents[index] = swappedAgent;
            CopyToDebugState(index, swappedAgent);
        }

        agents.RemoveAt(lastIndex);
        Count = lastIndex;
    }

    public void CopyToRenderState(int index, Replicator agent)
    {
        agent.position = Position[index];
        agent.rotation = Rotation[index];
        agent.size = Size[index];
        agent.color = Color[index];
    }

    public void CopyToDebugState(int index, Replicator agent)
    {
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
        agent.metabolism = Metabolism[index];
        agent.locomotion = Locomotion[index];
        agent.optimalTempMin = OptimalTempMin[index];
        agent.optimalTempMax = OptimalTempMax[index];
        agent.lethalTempMargin = LethalTempMargin[index];
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
        agent.movementSeed = MovementSeed[index];
        agent.size = Size[index];
        agent.color = Color[index];
    }

    public void EnsureMatchesAgentCount(List<Replicator> agents)
    {
        if (agents == null)
        {
            Count = 0;
            return;
        }

        if (Count == agents.Count)
        {
            return;
        }

        Count = 0;
        EnsureCapacity(agents.Count);
        for (int i = 0; i < agents.Count; i++)
        {
            AddAgentFromReplicatorData(agents[i]);
        }
    }

    private void CopyAgentToEntry(int index, Replicator a)
    {
        Position[index] = a.position;
        Rotation[index] = a.rotation;
        CurrentDirection[index] = a.currentDirection;
        MoveDirection[index] = a.moveDirection;
        DesiredMoveDirection[index] = a.desiredMoveDir;
        Velocity[index] = a.velocity;
        Energy[index] = a.energy;
        Age[index] = a.age;
        OrganicCStore[index] = a.organicCStore;
        SpeedFactor[index] = a.speedFactor;
        AttackCooldown[index] = a.attackCooldown;
        FearCooldown[index] = a.fearCooldown;
        Alive[index] = true;
        Metabolism[index] = a.metabolism;
        Locomotion[index] = a.locomotion;
        OptimalTempMin[index] = a.optimalTempMin;
        OptimalTempMax[index] = a.optimalTempMax;
        LethalTempMargin[index] = a.lethalTempMargin;
        StarveCo2Seconds[index] = a.starveCo2Seconds;
        StarveH2sSeconds[index] = a.starveH2sSeconds;
        StarveH2Seconds[index] = a.starveH2Seconds;
        StarveLightSeconds[index] = a.starveLightSeconds;
        StarveOrganicCFoodSeconds[index] = a.starveOrganicCFoodSeconds;
        StarveO2Seconds[index] = a.starveO2Seconds;
        StarveCh4Seconds[index] = a.starveCh4Seconds;
        StarveStoredCSeconds[index] = a.starveStoredCSeconds;
        O2ToxicSeconds[index] = a.o2ToxicSeconds;
        O2ComfortMax[index] = a.o2ComfortMax;
        O2StressMax[index] = a.o2StressMax;
        CanReplicate[index] = a.canReplicate;
        LastHabitatValue[index] = a.lastHabitatValue;
        TumbleProbability[index] = a.tumbleProbability;
        NextSenseTime[index] = a.nextSenseTime;
        MovementSeed[index] = a.movementSeed;
        Size[index] = a.size;
        Color[index] = a.color;
    }

    private void MoveEntry(int src, int dst)
    {
        Position[dst] = Position[src];
        Rotation[dst] = Rotation[src];
        CurrentDirection[dst] = CurrentDirection[src];
        MoveDirection[dst] = MoveDirection[src];
        DesiredMoveDirection[dst] = DesiredMoveDirection[src];
        Velocity[dst] = Velocity[src];
        Energy[dst] = Energy[src];
        Age[dst] = Age[src];
        OrganicCStore[dst] = OrganicCStore[src];
        SpeedFactor[dst] = SpeedFactor[src];
        AttackCooldown[dst] = AttackCooldown[src];
        FearCooldown[dst] = FearCooldown[src];
        Alive[dst] = Alive[src];
        Metabolism[dst] = Metabolism[src];
        Locomotion[dst] = Locomotion[src];
        OptimalTempMin[dst] = OptimalTempMin[src];
        OptimalTempMax[dst] = OptimalTempMax[src];
        LethalTempMargin[dst] = LethalTempMargin[src];
        StarveCo2Seconds[dst] = StarveCo2Seconds[src];
        StarveH2sSeconds[dst] = StarveH2sSeconds[src];
        StarveH2Seconds[dst] = StarveH2Seconds[src];
        StarveLightSeconds[dst] = StarveLightSeconds[src];
        StarveOrganicCFoodSeconds[dst] = StarveOrganicCFoodSeconds[src];
        StarveO2Seconds[dst] = StarveO2Seconds[src];
        StarveCh4Seconds[dst] = StarveCh4Seconds[src];
        StarveStoredCSeconds[dst] = StarveStoredCSeconds[src];
        O2ToxicSeconds[dst] = O2ToxicSeconds[src];
        O2ComfortMax[dst] = O2ComfortMax[src];
        O2StressMax[dst] = O2StressMax[src];
        CanReplicate[dst] = CanReplicate[src];
        LastHabitatValue[dst] = LastHabitatValue[src];
        TumbleProbability[dst] = TumbleProbability[src];
        NextSenseTime[dst] = NextSenseTime[src];
        MovementSeed[dst] = MovementSeed[src];
        Size[dst] = Size[src];
        Color[dst] = Color[src];
    }

    private void EnsureCapacity(int required)
    {
        if (Position.Length >= required)
        {
            return;
        }

        int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(4, required));
        System.Array.Resize(ref Position, newCapacity);
        System.Array.Resize(ref Rotation, newCapacity);
        System.Array.Resize(ref CurrentDirection, newCapacity);
        System.Array.Resize(ref MoveDirection, newCapacity);
        System.Array.Resize(ref DesiredMoveDirection, newCapacity);
        System.Array.Resize(ref Velocity, newCapacity);
        System.Array.Resize(ref Energy, newCapacity);
        System.Array.Resize(ref Age, newCapacity);
        System.Array.Resize(ref OrganicCStore, newCapacity);
        System.Array.Resize(ref SpeedFactor, newCapacity);
        System.Array.Resize(ref AttackCooldown, newCapacity);
        System.Array.Resize(ref FearCooldown, newCapacity);
        System.Array.Resize(ref Alive, newCapacity);
        System.Array.Resize(ref Metabolism, newCapacity);
        System.Array.Resize(ref Locomotion, newCapacity);
        System.Array.Resize(ref OptimalTempMin, newCapacity);
        System.Array.Resize(ref OptimalTempMax, newCapacity);
        System.Array.Resize(ref LethalTempMargin, newCapacity);
        System.Array.Resize(ref StarveCo2Seconds, newCapacity);
        System.Array.Resize(ref StarveH2sSeconds, newCapacity);
        System.Array.Resize(ref StarveH2Seconds, newCapacity);
        System.Array.Resize(ref StarveLightSeconds, newCapacity);
        System.Array.Resize(ref StarveOrganicCFoodSeconds, newCapacity);
        System.Array.Resize(ref StarveO2Seconds, newCapacity);
        System.Array.Resize(ref StarveCh4Seconds, newCapacity);
        System.Array.Resize(ref StarveStoredCSeconds, newCapacity);
        System.Array.Resize(ref O2ToxicSeconds, newCapacity);
        System.Array.Resize(ref O2ComfortMax, newCapacity);
        System.Array.Resize(ref O2StressMax, newCapacity);
        System.Array.Resize(ref CanReplicate, newCapacity);
        System.Array.Resize(ref LastHabitatValue, newCapacity);
        System.Array.Resize(ref TumbleProbability, newCapacity);
        System.Array.Resize(ref NextSenseTime, newCapacity);
        System.Array.Resize(ref MovementSeed, newCapacity);
        System.Array.Resize(ref Size, newCapacity);
        System.Array.Resize(ref Color, newCapacity);
    }
}
