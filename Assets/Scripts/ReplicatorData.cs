using UnityEngine;

[System.Serializable]
public class Replicator
{
    public Vector3 position;
    public Quaternion rotation;
    public float age;
    public float maxLifespan;
    public Color color;

    // Movement data
    public Vector3 velocity;
    public Vector3 currentDirection; // Normalized position (direction from center)
    public float movementSeed;
    public float moveSpeedMultiplier;
    public float turnSpeedMultiplier;

    // Constructor
    public Replicator(
        Vector3 pos,
        Quaternion rot,
        float lifespan,
        Color col,
        float seed,
        float moveMul,
        float turnMul)
    {
        position = pos;
        rotation = rot;
        maxLifespan = lifespan;
        color = col;
        movementSeed = seed;
        moveSpeedMultiplier = moveMul;
        turnSpeedMultiplier = turnMul;
        age = 0;
        currentDirection = pos.normalized;
    }
}
