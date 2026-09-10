using System;
using UnityEngine;

public readonly struct IcosphereRenderGeometry
{
    public readonly int SubdivisionLevel;
    public readonly Vector3[] UnitVertices;
    public readonly int[] Triangles;

    public IcosphereRenderGeometry(int subdivisionLevel, Vector3[] unitVertices, int[] triangles)
    {
        SubdivisionLevel = subdivisionLevel;
        UnitVertices = unitVertices ?? throw new ArgumentNullException(nameof(unitVertices));
        Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
    }

    public int VertexCount => UnitVertices != null ? UnitVertices.Length : 0;
    public int TriangleCount => Triangles != null ? Triangles.Length / 3 : 0;
    public long ApproximateManagedBytes => (long)VertexCount * 12L + (long)(Triangles != null ? Triangles.Length : 0) * 4L;
}
