using System.Collections.Generic;
using UnityEngine;

public class ReplicatorRenderSystem
{
    private readonly Matrix4x4[] matrixBatch = new Matrix4x4[1023];
    private readonly Vector4[] colorBatch = new Vector4[1023];
    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    private readonly List<Vector3> smoothedForwardByIndex = new List<Vector3>(1024);

    // Visual-only smoothing.
    // Higher = snappier. Lower = smoother.
    private const float FlagellumVisualTurnSharpness = 28f;

    public void RenderAgents(List<Replicator> agents, ReplicatorPopulationState populationState, Mesh replicatorMesh, Material replicatorMaterial)
    {
        if (agents == null || replicatorMesh == null || replicatorMaterial == null)
        {
            return;
        }

        int batchCount = 0;
        int totalAgents = agents.Count;

        if (populationState != null && populationState.Count == totalAgents)
        {
            for (int i = 0; i < totalAgents; i++)
            {
                Vector3 position = populationState.Position[i];
                Quaternion rotation = populationState.Rotation[i];
                Vector3 currentDirection = populationState.CurrentDirection[i];
                Vector3 moveDirection = populationState.MoveDirection[i];
                float size = populationState.Size[i];
                Color color = populationState.Color[i];
                LocomotionType locomotion = populationState.Locomotion[i];
                float movementSeed = populationState.MovementSeed[i];

                AppendReplicatorVisual(
                    i,
                    position,
                    rotation,
                    currentDirection,
                    moveDirection,
                    size,
                    color,
                    locomotion,
                    movementSeed,
                    ref batchCount,
                    replicatorMesh,
                    replicatorMaterial);
            }
        }
        else
        {
            for (int i = 0; i < totalAgents; i++)
            {
                Replicator a = agents[i];

                AppendReplicatorVisual(
                    i,
                    a.position,
                    a.rotation,
                    a.currentDirection,
                    a.moveDirection,
                    a.size,
                    a.color,
                    a.locomotion,
                    a.movementSeed,
                    ref batchCount,
                    replicatorMesh,
                    replicatorMaterial);
            }
        }

        if (batchCount > 0)
        {
            FlushBatch(batchCount, replicatorMesh, replicatorMaterial);
        }
    }

    private void AppendReplicatorVisual(
        int agentIndex,
        Vector3 position,
        Quaternion fallbackRotation,
        Vector3 currentDirection,
        Vector3 moveDirection,
        float size,
        Color color,
        LocomotionType locomotion,
        float movementSeed,
        ref int batchCount,
        Mesh replicatorMesh,
        Material replicatorMaterial)
    {
        float baseScale = 0.1f * Mathf.Max(0.1f, size);
        Vector3 up = currentDirection.sqrMagnitude > 0.0001f ? currentDirection.normalized : position.normalized;
        if (up.sqrMagnitude <= 0.0001f)
        {
            up = Vector3.up;
        }

        Vector3 tangentForward = GetTangentForward(up, moveDirection, fallbackRotation);

        switch (locomotion)
        {
            case LocomotionType.Flagellum:
                {
                    Vector3 visualForward = GetSmoothedVisualForward(agentIndex, tangentForward, up);
                    Quaternion rodRotation = Quaternion.LookRotation(visualForward, up);

                    // Rod / bacterium-like body: elongated along local Z
                    Vector3 rodScale = new Vector3(
                        baseScale * 0.70f,
                        baseScale * 0.70f,
                        baseScale * 2.00f);

                    AddInstance(Matrix4x4.TRS(position, rodRotation, rodScale), color, ref batchCount, replicatorMesh, replicatorMaterial);
                    break;
                }

            case LocomotionType.Amoeboid:
                {
                    float t = Time.time;
                    float seedA = movementSeed * 0.73f;
                    float seedB = movementSeed * 1.19f;
                    float seedC = movementSeed * 1.61f;

                    float sx = 1f + 0.18f * Mathf.Sin(t * 2.6f + seedA);
                    float sy = 1f + 0.14f * Mathf.Sin(t * 3.3f + seedB);
                    float sz = 1f + 0.20f * Mathf.Sin(t * 2.1f + seedC);

                    Vector3 amoebaScale = new Vector3(
                        baseScale * 1.10f * sx,
                        baseScale * 1.00f * sy,
                        baseScale * 1.10f * sz);

                    Quaternion amoebaRotation = Quaternion.LookRotation(tangentForward, up) *
                                                Quaternion.Euler(
                                                    7f * Mathf.Sin(t * 1.8f + seedA),
                                                    10f * Mathf.Sin(t * 1.3f + seedB),
                                                    8f * Mathf.Sin(t * 1.6f + seedC));

                    AddInstance(Matrix4x4.TRS(position, amoebaRotation, amoebaScale), color, ref batchCount, replicatorMesh, replicatorMaterial);
                    break;
                }

            case LocomotionType.Anchored:
                {
                    Quaternion surfaceUpRotation = Quaternion.FromToRotation(Vector3.up, up);

                    // Thin stalk using the same sphere mesh, stretched strongly along Y
                    float stalkHeight = baseScale * 1.65f;
                    float stalkWidth = baseScale * 0.22f;
                    Vector3 stalkCenter = position - up * (stalkHeight * 0.35f);

                    Vector3 stalkScale = new Vector3(
                        stalkWidth,
                        stalkHeight,
                        stalkWidth);

                    Color stalkColor = color * 0.82f;
                    stalkColor.a = color.a;

                    AddInstance(Matrix4x4.TRS(stalkCenter, surfaceUpRotation, stalkScale), stalkColor, ref batchCount, replicatorMesh, replicatorMaterial);

                    // Body sitting on top of the stalk
                    Vector3 bodyPosition = position + up * (baseScale * 0.38f);
                    Quaternion bodyRotation = Quaternion.LookRotation(tangentForward, up);
                    Vector3 bodyScale = new Vector3(
                        baseScale * 0.95f,
                        baseScale * 1.10f,
                        baseScale * 0.95f);

                    AddInstance(Matrix4x4.TRS(bodyPosition, bodyRotation, bodyScale), color, ref batchCount, replicatorMesh, replicatorMaterial);
                    break;
                }

            case LocomotionType.PassiveDrift:
            default:
                {
                    Vector3 passiveScale = Vector3.one * baseScale * 0.50f;
                    AddInstance(Matrix4x4.TRS(position, fallbackRotation, passiveScale), color, ref batchCount, replicatorMesh, replicatorMaterial);
                    break;
                }
        }
    }

    private Vector3 GetTangentForward(Vector3 surfaceNormal, Vector3 moveDirection, Quaternion fallbackRotation)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(moveDirection, surfaceNormal);

        if (tangent.sqrMagnitude > 0.0001f)
        {
            return tangent.normalized;
        }

        Vector3 fallbackForward = Vector3.ProjectOnPlane(fallbackRotation * Vector3.forward, surfaceNormal);
        if (fallbackForward.sqrMagnitude > 0.0001f)
        {
            return fallbackForward.normalized;
        }

        Vector3 cross = Vector3.Cross(surfaceNormal, Vector3.up);
        if (cross.sqrMagnitude > 0.0001f)
        {
            return cross.normalized;
        }

        return Vector3.Cross(surfaceNormal, Vector3.right).normalized;
    }

    private void AddInstance(Matrix4x4 matrix, Color color, ref int batchCount, Mesh replicatorMesh, Material replicatorMaterial)
    {
        matrixBatch[batchCount] = matrix;
        colorBatch[batchCount] = color;
        batchCount++;

        if (batchCount == 1023)
        {
            FlushBatch(batchCount, replicatorMesh, replicatorMaterial);
            batchCount = 0;
        }
    }

    private MaterialPropertyBlock GetOrCreatePropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        return propertyBlock;
    }

    private void FlushBatch(int count, Mesh replicatorMesh, Material replicatorMaterial)
    {
        Vector4 transparentBlack = Vector4.zero;
        for (int i = count; i < 1023; i++)
        {
            colorBatch[i] = transparentBlack;
        }

        MaterialPropertyBlock activePropertyBlock = GetOrCreatePropertyBlock();
        activePropertyBlock.SetVectorArray(BaseColorID, colorBatch);
        activePropertyBlock.SetVectorArray(ColorID, colorBatch);
        activePropertyBlock.SetVectorArray(EmissionID, colorBatch);

        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrixBatch,
            count,
            activePropertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            0,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off
        );
    }

    private Vector3 GetSmoothedVisualForward(int index, Vector3 targetForward, Vector3 surfaceNormal)
    {
        while (smoothedForwardByIndex.Count <= index)
        {
            smoothedForwardByIndex.Add(targetForward);
        }

        Vector3 previous = smoothedForwardByIndex[index];
        if (previous.sqrMagnitude <= 0.0001f)
        {
            previous = targetForward;
        }

        previous = Vector3.ProjectOnPlane(previous, surfaceNormal);
        if (previous.sqrMagnitude <= 0.0001f)
        {
            previous = targetForward;
        }

        previous.Normalize();
        targetForward = Vector3.ProjectOnPlane(targetForward, surfaceNormal);
        if (targetForward.sqrMagnitude <= 0.0001f)
        {
            targetForward = previous;
        }
        targetForward.Normalize();

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        float lerp = 1f - Mathf.Exp(-FlagellumVisualTurnSharpness * dt);

        Vector3 smoothed = Vector3.Slerp(previous, targetForward, lerp);
        smoothed = Vector3.ProjectOnPlane(smoothed, surfaceNormal);
        if (smoothed.sqrMagnitude <= 0.0001f)
        {
            smoothed = targetForward;
        }

        smoothed.Normalize();
        smoothedForwardByIndex[index] = smoothed;
        return smoothed;
    }
}