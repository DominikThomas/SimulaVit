using UnityEngine;

public enum MetabolismType
{
    SulfurChemosynthesis,
    Photosynthesis,
    Saprotrophy
}

public enum DeathCause
{
    Unknown,
    OldAge,
    EnergyDepletion,
    Lack_CO2,
    Lack_H2S,
    Lack_Light,
    Lack_OrganicC_Food,
    Lack_O2,
    Lack_StoredC,
    TemperatureTooHigh,
    TemperatureTooLow
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
    public float optimalTemp;
    public float tempTolerance;
    public float lethalTempMargin;
    public float starveCo2Seconds;
    public float starveH2sSeconds;
    public float starveLightSeconds;
    public float starveOrganicCFoodSeconds;
    public float starveO2Seconds;
    public float starveStoredCSeconds;
    public DeathCause lastDeathCauseCandidate;

    // Movement data
    public Vector3 velocity;
    public Vector3 currentDirection; // Normalized position (direction from center)
    public float movementSeed;

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
        currentDirection = pos.normalized;
        lastDeathCauseCandidate = DeathCause.Unknown;
    }
}
