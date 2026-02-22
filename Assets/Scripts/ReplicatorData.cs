using UnityEngine;

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

    // Movement data
    public Vector3 velocity;
    public Vector3 currentDirection; // Normalized position (direction from center)

    // Constructor
    public Replicator(Vector3 pos, Quaternion rot, float lifespan, Color col, Traits traits)
    {
        position = pos;
        rotation = rot;
        maxLifespan = lifespan;
        color = col;
        this.traits = traits;
        age = 0;
        currentDirection = pos.normalized;
    }
}
