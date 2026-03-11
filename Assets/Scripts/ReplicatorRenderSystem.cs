using System.Collections.Generic;
using UnityEngine;

public class ReplicatorRenderSystem
{
    private readonly Matrix4x4[] matrixBatch = new Matrix4x4[1023];
    private readonly Vector4[] colorBatch = new Vector4[1023];
    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    public void RenderAgents(List<Replicator> agents, Mesh replicatorMesh, Material replicatorMaterial)
    {
        int batchCount = 0;
        int totalAgents = agents.Count;

        for (int i = 0; i < totalAgents; i++)
        {
            Replicator a = agents[i];
            matrixBatch[batchCount] = Matrix4x4.TRS(a.position, a.rotation, Vector3.one * (0.1f * Mathf.Max(0.1f, a.size)));
            colorBatch[batchCount] = a.color;
            batchCount++;

            if (batchCount == 1023)
            {
                FlushBatch(batchCount, replicatorMesh, replicatorMaterial);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            FlushBatch(batchCount, replicatorMesh, replicatorMaterial);
        }
    }

    private void FlushBatch(int count, Mesh replicatorMesh, Material replicatorMaterial)
    {
        Vector4 transparentBlack = Vector4.zero;
        for (int i = count; i < 1023; i++)
        {
            colorBatch[i] = transparentBlack;
        }

        propertyBlock.SetVectorArray(BaseColorID, colorBatch);
        propertyBlock.SetVectorArray(ColorID, colorBatch);
        propertyBlock.SetVectorArray(EmissionID, colorBatch);

        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrixBatch,
            count,
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            0,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off
        );
    }
}
