using UnityEngine;

public enum MetabolismType
{
    SulfurChemosynthesis,
    Hydrogenotrophy,
    Photosynthesis,
    Saprotrophy,
    Predation,
    Fermentation,
    Methanogenesis,
    Methanotrophy
}

public enum DeathCause
{
    Unknown,
    OldAge,
    EnergyDepletion,
    Lack_CO2,
    Lack_H2S,
    Lack_H2,
    Lack_Light,
    Lack_OrganicC_Food,
    Lack_O2,
    Lack_CH4,
    Lack_StoredC,
    Predation,
    TemperatureTooHigh,
    TemperatureTooLow,
    O2_Toxicity
}

public enum LocomotionType
{
    PassiveDrift,
    Amoeboid,
    Flagellum,
    Anchored
}

[System.Serializable]
public class Replicator
{
    [System.Serializable]
    public struct Traits
    {
        public bool spawnOnlyInSea;
        public bool replicateOnlyInSea;
        public bool moveOnlyInSea;
        public float surfaceMoveSpeedMultiplier;

        public Traits(bool spawnOnlyInSea, bool replicateOnlyInSea, bool moveOnlyInSea, float surfaceMoveSpeedMultiplier)
        {
            this.spawnOnlyInSea = spawnOnlyInSea;
            this.replicateOnlyInSea = replicateOnlyInSea;
            this.moveOnlyInSea = moveOnlyInSea;
            this.surfaceMoveSpeedMultiplier = surfaceMoveSpeedMultiplier;
        }
    }

    public Vector3 position;
    public Quaternion rotation;
    public float age;
    public float maxLifespan;
    public Color color;
    public Traits traits;
    public float energy;
    public float size;
    public float organicCStore;
    public float biomassTarget;
    public float speedFactor;
    public LocomotionType locomotion;
    public float locomotionSkill;
    public MetabolismType metabolism;
    public float attackCooldown;
    public float fearCooldown;
    public float optimalTempMin;
    public float optimalTempMax;
    public float lethalTempMargin;
    public float starveCo2Seconds;
    public float starveH2sSeconds;
    public float starveH2Seconds;
    public float starveLightSeconds;
    public float starveOrganicCFoodSeconds;
    public float starveO2Seconds;
    public float starveCh4Seconds;
    public float starveStoredCSeconds;
    public float o2ToxicSeconds;
    public float o2ComfortMax;
    public float o2StressMax;
    public bool canReplicate;
    public DeathCause lastDeathCauseCandidate;

    // Movement data
    public Vector3 velocity;
    public Vector3 currentDirection; // Normalized position (direction from center)
    public Vector3 desiredMoveDir;
    public Vector3 moveDirection;
    public float lastHabitatValue;
    public float tumbleProbability;
    public float nextSenseTime;
    public float movementSeed;
    [Tooltip("Current logical ocean layer index used for layered chemistry queries. World-space movement remains surface-constrained for now.")]
    public int currentOceanLayerIndex;
    [Tooltip("Temporary exploratory ocean-layer intent/bias used for local up/down probing. Not a global target depth.")]
    public int preferredOceanLayerIndex;

    // Constructor
    public Replicator(Vector3 pos, Quaternion rot, float lifespan, Color col, Traits traits, float movementSeed, MetabolismType metabolism, LocomotionType locomotion = LocomotionType.PassiveDrift, float locomotionSkill = 0f)
    {
        position = pos;
        rotation = rot;
        maxLifespan = lifespan;
        color = col;
        this.traits = traits;
        this.movementSeed = movementSeed;
        energy = 0f;
        size = 1f;
        organicCStore = 0f;
        biomassTarget = 0f;
        speedFactor = 1f;
        this.locomotion = locomotion;
        this.locomotionSkill = Mathf.Clamp01(locomotionSkill);
        age = 0;
        this.metabolism = metabolism;
        attackCooldown = 0f;
        fearCooldown = 0f;
        currentDirection = pos.normalized;
        desiredMoveDir = Vector3.zero;
        moveDirection = currentDirection;
        lastHabitatValue = 0f;
        tumbleProbability = 0f;
        nextSenseTime = 0f;
        preferredOceanLayerIndex = GetDefaultPreferredOceanLayerIndex(metabolism, locomotion);
        currentOceanLayerIndex = preferredOceanLayerIndex;
        starveCo2Seconds = 0f;
        starveH2sSeconds = 0f;
        starveH2Seconds = 0f;
        starveLightSeconds = 0f;
        starveOrganicCFoodSeconds = 0f;
        starveO2Seconds = 0f;
        starveCh4Seconds = 0f;
        starveStoredCSeconds = 0f;
        o2ToxicSeconds = 0f;
        o2ComfortMax = 1f;
        o2StressMax = 1f;
        canReplicate = true;
        lastDeathCauseCandidate = DeathCause.Unknown;
    }

    public static Replicator CreateFromSnapshot(ReplicatorSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        MetabolismType metabolism;
        if (!System.Enum.TryParse(snapshot.metabolism, out metabolism))
        {
            metabolism = MetabolismType.Hydrogenotrophy;
        }

        LocomotionType locomotion;
        if (!System.Enum.TryParse(snapshot.locomotion, out locomotion))
        {
            locomotion = LocomotionType.PassiveDrift;
        }

        Replicator.Traits traits = snapshot.traits != null
            ? new Replicator.Traits(
                snapshot.traits.spawnOnlyInSea,
                snapshot.traits.replicateOnlyInSea,
                snapshot.traits.moveOnlyInSea,
                snapshot.traits.surfaceMoveSpeedMultiplier)
            : new Replicator.Traits(false, false, false, 1f);

        Replicator agent = new Replicator(
            snapshot.position.ToVector3(),
            snapshot.rotation.ToQuaternion(),
            snapshot.maxLifespan,
            snapshot.color.ToColor(),
            traits,
            snapshot.movementSeed,
            metabolism,
            locomotion,
            snapshot.locomotionSkill);

        agent.currentDirection = snapshot.currentDirection.ToVector3();
        agent.moveDirection = snapshot.moveDirection.ToVector3();
        agent.desiredMoveDir = snapshot.desiredMoveDirection.ToVector3();
        agent.velocity = snapshot.velocity.ToVector3();
        agent.energy = snapshot.energy;
        agent.age = snapshot.age;
        agent.organicCStore = snapshot.organicCStore;
        agent.speedFactor = snapshot.speedFactor;
        agent.attackCooldown = snapshot.attackCooldown;
        agent.fearCooldown = snapshot.fearCooldown;
        agent.optimalTempMin = snapshot.optimalTempMin;
        agent.optimalTempMax = snapshot.optimalTempMax;
        agent.lethalTempMargin = snapshot.lethalTempMargin;
        agent.starveCo2Seconds = snapshot.starveCo2Seconds;
        agent.starveH2sSeconds = snapshot.starveH2sSeconds;
        agent.starveH2Seconds = snapshot.starveH2Seconds;
        agent.starveLightSeconds = snapshot.starveLightSeconds;
        agent.starveOrganicCFoodSeconds = snapshot.starveOrganicCFoodSeconds;
        agent.starveO2Seconds = snapshot.starveO2Seconds;
        agent.starveCh4Seconds = snapshot.starveCh4Seconds;
        agent.starveStoredCSeconds = snapshot.starveStoredCSeconds;
        agent.o2ToxicSeconds = snapshot.o2ToxicSeconds;
        agent.o2ComfortMax = snapshot.o2ComfortMax;
        agent.o2StressMax = snapshot.o2StressMax;
        agent.canReplicate = snapshot.canReplicate;
        agent.lastHabitatValue = snapshot.lastHabitatValue;
        agent.tumbleProbability = snapshot.tumbleProbability;
        agent.nextSenseTime = snapshot.nextSenseTime;
        agent.size = snapshot.size;
        agent.currentOceanLayerIndex = snapshot.currentOceanLayerIndex;
        agent.preferredOceanLayerIndex = snapshot.preferredOceanLayerIndex;
        agent.biomassTarget = snapshot.biomassTarget;
        return agent;
    }

    private static int GetDefaultPreferredOceanLayerIndex(MetabolismType metabolism, LocomotionType locomotion)
    {
        switch (metabolism)
        {
            case MetabolismType.SulfurChemosynthesis:
            case MetabolismType.Hydrogenotrophy:
                return 4;
            default:
                return locomotion == LocomotionType.Anchored ? 4 : -1;
        }
    }
}
