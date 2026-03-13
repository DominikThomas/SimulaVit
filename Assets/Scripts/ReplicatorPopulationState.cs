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
    public float[] StarveStoredCSeconds = new float[0];

    public float[] LastHabitatValue = new float[0];
    public float[] TumbleProbability = new float[0];
    public float[] NextSenseTime = new float[0];
    public float[] MovementSeed = new float[0];
    public float[] Size = new float[0];
    public Color[] Color = new Color[0];

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
            Rotation[i] = a.rotation;
            CurrentDirection[i] = a.currentDirection;
            MoveDirection[i] = a.moveDirection;
            DesiredMoveDirection[i] = a.desiredMoveDir;
            Velocity[i] = a.velocity;
            Energy[i] = a.energy;
            Age[i] = a.age;
            OrganicCStore[i] = a.organicCStore;
            SpeedFactor[i] = a.speedFactor;
            AttackCooldown[i] = a.attackCooldown;
            FearCooldown[i] = a.fearCooldown;
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
            LastHabitatValue[i] = a.lastHabitatValue;
            TumbleProbability[i] = a.tumbleProbability;
            NextSenseTime[i] = a.nextSenseTime;
            MovementSeed[i] = a.movementSeed;
            Size[i] = a.size;
            Color[i] = a.color;
        }
    }

    public void SyncSteeringFieldsFromAgents(List<Replicator> agents)
    {
        Count = agents.Count;
        EnsureCapacity(Count);

        for (int i = 0; i < Count; i++)
        {
            Replicator a = agents[i];
            Position[i] = a.position;
            CurrentDirection[i] = a.currentDirection;
            MoveDirection[i] = a.moveDirection;
            DesiredMoveDirection[i] = a.desiredMoveDir;
            SpeedFactor[i] = a.speedFactor;
            Locomotion[i] = a.locomotion;
            Metabolism[i] = a.metabolism;
            OptimalTempMin[i] = a.optimalTempMin;
            OptimalTempMax[i] = a.optimalTempMax;
            LethalTempMargin[i] = a.lethalTempMargin;
            LastHabitatValue[i] = a.lastHabitatValue;
            TumbleProbability[i] = a.tumbleProbability;
            NextSenseTime[i] = a.nextSenseTime;
            MovementSeed[i] = a.movementSeed;
        }
    }

    public void SyncLifecycleFieldsFromAgents(List<Replicator> agents)
    {
        Count = agents.Count;
        EnsureCapacity(Count);

        for (int i = 0; i < Count; i++)
        {
            Replicator a = agents[i];
            Age[i] = a.age;
        }
    }

    public void SyncSteeringFieldsToAgents(List<Replicator> agents)
    {
        int count = Mathf.Min(Count, agents.Count);
        for (int i = 0; i < count; i++)
        {
            Replicator agent = agents[i];
            agent.moveDirection = MoveDirection[i];
            agent.desiredMoveDir = DesiredMoveDirection[i];
            agent.lastHabitatValue = LastHabitatValue[i];
            agent.tumbleProbability = TumbleProbability[i];
            agent.nextSenseTime = NextSenseTime[i];
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

    public void SyncPredationFieldsFromAgents(List<Replicator> agents)
    {
        Count = agents.Count;
        EnsureCapacity(Count);

        for (int i = 0; i < Count; i++)
        {
            Replicator a = agents[i];
            Position[i] = a.position;
            Metabolism[i] = a.metabolism;
            Energy[i] = a.energy;
            OrganicCStore[i] = a.organicCStore;
            AttackCooldown[i] = a.attackCooldown;
        }
    }

    public void SyncPredationFieldsToAgents(List<Replicator> agents)
    {
        int count = Mathf.Min(Count, agents.Count);
        for (int i = 0; i < count; i++)
        {
            CopyPredationEntryToAgent(i, agents[i]);
        }
    }

    public void CopyPredationEntryToAgent(int index, Replicator agent)
    {
        agent.energy = Energy[index];
        agent.organicCStore = OrganicCStore[index];
        agent.attackCooldown = AttackCooldown[index];
    }

    public void CopyEntryToAgent(int index, Replicator agent)
    {
        agent.position = Position[index];
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
        agent.starveStoredCSeconds = StarveStoredCSeconds[index];
        agent.lastHabitatValue = LastHabitatValue[index];
        agent.tumbleProbability = TumbleProbability[index];
        agent.nextSenseTime = NextSenseTime[index];
    }

    void EnsureCapacity(int required)
    {
        if (Position.Length >= required)
        {
            return;
        }

        int newCapacity = Mathf.NextPowerOfTwo(Mathf.Max(4, required));
        Position = new Vector3[newCapacity];
        Rotation = new Quaternion[newCapacity];
        CurrentDirection = new Vector3[newCapacity];
        MoveDirection = new Vector3[newCapacity];
        DesiredMoveDirection = new Vector3[newCapacity];
        Velocity = new Vector3[newCapacity];
        Energy = new float[newCapacity];
        Age = new float[newCapacity];
        OrganicCStore = new float[newCapacity];
        SpeedFactor = new float[newCapacity];
        AttackCooldown = new float[newCapacity];
        FearCooldown = new float[newCapacity];
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
        LastHabitatValue = new float[newCapacity];
        TumbleProbability = new float[newCapacity];
        NextSenseTime = new float[newCapacity];
        MovementSeed = new float[newCapacity];
        Size = new float[newCapacity];
        Color = new Color[newCapacity];
    }
}
