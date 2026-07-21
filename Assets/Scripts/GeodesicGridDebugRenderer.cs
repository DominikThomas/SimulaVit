using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GeodesicGridDebugRenderer : MonoBehaviour
{
    public bool showCellOutlines = true;
    public bool highlightPentagons = true;
    public bool showCellCentres;
    public bool showSelectedCell = true;
    public int selectedCellIndex = -1;
    public Material lineMaterial;
    private Mesh mesh;

    public void Render(GeodesicGridTopology t, float radius)
    {
        if (mesh == null) mesh = new Mesh { name = "Geodesic Cell Debug Lines" }; else mesh.Clear();
        if (t == null || !showCellOutlines) { GetComponent<MeshFilter>().sharedMesh = mesh; return; }
        var verts = new System.Collections.Generic.List<Vector3>(); var indices = new System.Collections.Generic.List<int>(); float r=radius*1.002f;
        for(int c=0;c<t.CellCount;c++){var p=t.DualCorners[c]; if(p==null)continue; for(int i=0;i<p.Length;i++){indices.Add(verts.Count); verts.Add(p[i]*r); indices.Add(verts.Count); verts.Add(p[(i+1)%p.Length]*r);}}
        if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32; mesh.SetVertices(verts); mesh.SetIndices(indices, MeshTopology.Lines, 0); mesh.RecalculateBounds(); GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr=GetComponent<MeshRenderer>();
        if(lineMaterial!=null) mr.sharedMaterial=lineMaterial;
        else if (mr.sharedMaterial == null) mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Color.black };
        mr.enabled=true;
    }
}
