using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GeodesicGridDebugRenderer : MonoBehaviour
{
    public bool showCellOutlines = true;
    public bool highlightPentagons = true;
    public bool showCellCentres;
    public bool showSelectedCell = true;
    public bool highlightOceanCells;
    public bool highlightCoastlineCells;
    public int selectedCellIndex = -1;
    public Material lineMaterial;
    public Color defaultLineColor = Color.black;
    public Color pentagonLineColor = new Color(1f, 0.75f, 0.05f, 1f);
    public Color oceanLineColor = new Color(0.05f, 0.45f, 1f, 1f);
    public Color coastlineLineColor = new Color(1f, 0.95f, 0.1f, 1f);
    [NonSerialized] public Func<Vector3, float> surfaceRadiusSampler;
    [NonSerialized] public bool[] oceanMask;
    [NonSerialized] public bool[] coastlineMask;
    [Range(0f, 0.05f)] public float radialOffset = 0.003f;
    private Mesh mesh;

    public void Render(GeodesicGridTopology t, float radius)
    {
        if (mesh == null) mesh = new Mesh { name = "Geodesic Cell Debug Lines" }; else mesh.Clear();
        if (t == null || !showCellOutlines) { GetComponent<MeshFilter>().sharedMesh = mesh; return; }
        var verts = new System.Collections.Generic.List<Vector3>();
        var indices = new System.Collections.Generic.List<int>();
        var colors = new System.Collections.Generic.List<Color>();
        for (int c = 0; c < t.CellCount; c++)
        {
            var p = t.DualCorners[c];
            if (p == null) continue;
            Color color = ResolveCellColor(t, c);
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 a = p[i].normalized;
                Vector3 b = p[(i + 1) % p.Length].normalized;
                indices.Add(verts.Count);
                verts.Add(a * ((surfaceRadiusSampler != null ? surfaceRadiusSampler(a) : radius) + radialOffset));
                colors.Add(color);
                indices.Add(verts.Count);
                verts.Add(b * ((surfaceRadiusSampler != null ? surfaceRadiusSampler(b) : radius) + radialOffset));
                colors.Add(color);
            }
        }
        if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = GetComponent<MeshRenderer>();
        if (lineMaterial != null) mr.sharedMaterial = lineMaterial;
        else if (mr.sharedMaterial == null) mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
        mr.enabled = true;
    }

    private Color ResolveCellColor(GeodesicGridTopology t, int cellIndex)
    {
        if (highlightCoastlineCells && coastlineMask != null && cellIndex < coastlineMask.Length && coastlineMask[cellIndex]) return coastlineLineColor;
        if (highlightOceanCells && oceanMask != null && cellIndex < oceanMask.Length && oceanMask[cellIndex]) return oceanLineColor;
        if (highlightPentagons && t.IsPentagon[cellIndex]) return pentagonLineColor;
        return defaultLineColor;
    }
}
