using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Transitional struct-of-arrays runtime state for hot per-agent simulation data.
/// In migration step 1, Replicator objects remain the authoritative source of truth.
/// This state is rebuilt from Replicator objects per simulation tick and synced back after updates.
/// </summary>
public class ReplicatorPopulationState
{
    public int Count { get; private set; }

    public Vector3[] Position = new Vector3[0];
    public Vector3[] CurrentDirection = new Vector3[0];
    public Vector3[] MoveDirection = new Vector3[0];
    public Vector3[] Velocity = new Vector3[0];
    public float[] Energy = new float[0];
    public float[] OrganicCStore = new float[0];
    public float[] SpeedFactor = new float[0];
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
    public float[] StarveStoredCSeconds = new float[0];

    public void SyncMovementFieldsFromAgents(List<Replicator> agents)
    {
        Count = agents.Count;
        EnsureCapacity(Count);

        for (int i = 0; i < Count; i++)
        {
            Replicator a = agents[i];
            Position[i] = a.position;
            SpeedFactor[i] = a.speedFactor;
            Locomotion[i] = a.locomotion;
        }
    }

    public void SyncFromAgents(List<Replicator> agents)
    {
        Count = agents.Count;
        EnsureCapacity(Count);

        for (int i = 0; i < Count; i++)
        {
            Replicator a = agents[i];
            Position[i] = a.position;
            CurrentDirection[i] = a.currentDirection;
            MoveDirection[i] = a.moveDirection;
            Velocity[i] = a.velocity;
            Energy[i] = a.energy;
            OrganicCStore[i] = a.organicCStore;
            SpeedFactor[i] = a.speedFactor;
            Metabolism[i] = a.metabolism;
            Locomotion[i] = a.locomotion;
            Alive[i] = true;
            OptimalTempMin[i] = a.optimalTempMin;
            OptimalTempMax[i] = a.optimalTempMax;
            LethalTempMargin[i] = a.lethalTempMargin;
            StarveCo2Seconds[i] = a.starveCo2Seconds;
            StarveH2sSeconds[i] = a.starveH2sSeconds;
            StarveH2Seconds[i] = a.starveH2Seconds;
            StarveLightSeconds[i] = a.starveLightSeconds;
            StarveOrganicCFoodSeconds[i] = a.starveOrganicCFoodSeconds;
            StarveO2Seconds[i] = a.starveO2Seconds;
            StarveStoredCSeconds[i] = a.starveStoredCSeconds;
        }
    }

    public void SyncToAgents(List<Replicator> agents)
    {
        int count = Mathf.Min(Count, agents.Count);
        for (int i = 0; i < count; i++)
        {
            CopyEntryToAgent(i, agents[i]);
        }
    }

    public void CopyEntryToAgent(int index, Replicator agent)
    {
        agent.position = Position[index];
        agent.currentDirection = CurrentDirection[index];
        agent.moveDirection = MoveDirection[index];
        agent.velocity = Velocity[index];
        agent.energy = Energy[index];
        agent.organicCStore = OrganicCStore[index];
        agent.speedFactor = SpeedFactor[index];
        agent.starveCo2Seconds = StarveCo2Seconds[index];
        agent.starveH2sSeconds = StarveH2sSeconds[index];
        agent.starveH2Seconds = StarveH2Seconds[index];
        agent.starveLightSeconds = StarveLightSeconds[index];
        agent.starveOrganicCFoodSeconds = StarveOrganicCFoodSeconds[index];
        agent.starveO2Seconds = StarveO2Seconds[index];
        agent.starveStoredCSeconds = StarveStoredCSeconds[index];
    }

    void EnsureCapacity(int required)
    {
        if (Position.Length >= required)
        {
            return;
        }

        int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(4, required));
        Position = new Vector3[newCapacity];
        CurrentDirection = new Vector3[newCapacity];
        MoveDirection = new Vector3[newCapacity];
        Velocity = new Vector3[newCapacity];
        Energy = new float[newCapacity];
        OrganicCStore = new float[newCapacity];
        SpeedFactor = new float[newCapacity];
        Alive = new bool[newCapacity];
        Metabolism = new MetabolismType[newCapacity];
        Locomotion = new LocomotionType[newCapacity];
        OptimalTempMin = new float[newCapacity];
        OptimalTempMax = new float[newCapacity];
        LethalTempMargin = new float[newCapacity];
        StarveCo2Seconds = new float[newCapacity];
        StarveH2sSeconds = new float[newCapacity];
        StarveH2Seconds = new float[newCapacity];
        StarveLightSeconds = new float[newCapacity];
        StarveOrganicCFoodSeconds = new float[newCapacity];
        StarveO2Seconds = new float[newCapacity];
        StarveStoredCSeconds = new float[newCapacity];
    }
}
