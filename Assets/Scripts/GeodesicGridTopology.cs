using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GeodesicGridTopology
{
    public const int MaxSupportedSubdivision = 5;
    public int SubdivisionLevel { get; private set; }
    public Vector3[] CellDirections { get; private set; }
    public int[] Triangles { get; private set; }
    public byte[] NeighborCounts { get; private set; }
    public int[] Neighbors6 { get; private set; }
    public bool[] IsPentagon { get; private set; }
    public float[] UnitCellAreas { get; private set; }
    public float[] NeighborAngularDistances6 { get; private set; }
    public float[] SharedDualEdgeAngularLengths6 { get; private set; }
    public Vector3[][] DualCorners { get; private set; }
    public int CellCount => CellDirections != null ? CellDirections.Length : 0;
    public int TriangleCount => Triangles != null ? Triangles.Length / 3 : 0;
    public int EdgeCount => TriangleCount * 3 / 2;
    public long ApproximateMemoryBytes => (long)CellCount * (12 + 1 + 24 + 1 + 4 + 24 + 24) + (long)Triangles.Length * 4;

    public static int ExpectedCellCount(int s) => 10 * (int)Mathf.Pow(4, s) + 2;
    public static int ExpectedTriangleCount(int s) => 20 * (int)Mathf.Pow(4, s);
    public static int ExpectedEdgeCount(int s) => 30 * (int)Mathf.Pow(4, s);

    public static GeodesicGridTopology Build(int subdivision)
    {
        subdivision = Mathf.Clamp(subdivision, 0, MaxSupportedSubdivision);
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        BuildIcosahedron(verts, tris);
        for (int level = 0; level < subdivision; level++) Subdivide(verts, tris);
        var t = new GeodesicGridTopology { SubdivisionLevel = subdivision, CellDirections = verts.ToArray(), Triangles = tris.ToArray() };
        t.BuildAdjacencyAndMetrics();
        return t;
    }

    private static void BuildIcosahedron(List<Vector3> v, List<int> tri)
    {
        float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] p = { new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0), new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi), new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1) };
        foreach (var x in p) v.Add(x.normalized);
        int[] f = {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        tri.AddRange(f);
    }

    private static void Subdivide(List<Vector3> v, List<int> tri)
    {
        Dictionary<ulong,int> mid = new Dictionary<ulong,int>();
        List<int> n = new List<int>(tri.Count * 4);
        for (int i=0;i<tri.Count;i+=3){int a=tri[i],b=tri[i+1],c=tri[i+2]; int ab=Mid(v,mid,a,b), bc=Mid(v,mid,b,c), ca=Mid(v,mid,c,a); n.AddRange(new[]{a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});}
        tri.Clear(); tri.AddRange(n);
    }
    private static int Mid(List<Vector3> v, Dictionary<ulong,int> c, int a, int b){int lo=Mathf.Min(a,b),hi=Mathf.Max(a,b); ulong key=((ulong)(uint)lo<<32)|(uint)hi; if(c.TryGetValue(key,out int idx))return idx; idx=v.Count; v.Add(((v[a]+v[b])*0.5f).normalized); c[key]=idx; return idx;}

    private void BuildAdjacencyAndMetrics()
    {
        var sets = new List<HashSet<int>>(CellCount); var triByCell = new List<List<int>>(CellCount); for(int i=0;i<CellCount;i++){sets.Add(new HashSet<int>()); triByCell.Add(new List<int>());}        
        Vector3[] triCenters = new Vector3[TriangleCount];
        for(int t=0;t<TriangleCount;t++){int a=Triangles[t*3],b=Triangles[t*3+1],c=Triangles[t*3+2]; sets[a].Add(b);sets[a].Add(c);sets[b].Add(a);sets[b].Add(c);sets[c].Add(a);sets[c].Add(b); triByCell[a].Add(t);triByCell[b].Add(t);triByCell[c].Add(t); triCenters[t]=(CellDirections[a]+CellDirections[b]+CellDirections[c]).normalized;}
        NeighborCounts=new byte[CellCount]; Neighbors6=new int[CellCount*6]; IsPentagon=new bool[CellCount]; UnitCellAreas=new float[CellCount]; NeighborAngularDistances6=new float[CellCount*6]; SharedDualEdgeAngularLengths6=new float[CellCount*6]; DualCorners=new Vector3[CellCount][]; Array.Fill(Neighbors6,-1);
        for(int i=0;i<CellCount;i++){var list=new List<int>(sets[i]); list.Sort((x,y)=>CompareAround(CellDirections[i],CellDirections[x],CellDirections[y])); NeighborCounts[i]=(byte)list.Count; IsPentagon[i]=list.Count==5; for(int k=0;k<list.Count && k<6;k++){Neighbors6[i*6+k]=list[k]; NeighborAngularDistances6[i*6+k]=Mathf.Acos(Mathf.Clamp(Vector3.Dot(CellDirections[i],CellDirections[list[k]]),-1f,1f));}
            var corners=new List<Vector3>(); foreach(int ti in triByCell[i]) corners.Add(triCenters[ti]); corners.Sort((x,y)=>CompareAround(CellDirections[i],x,y)); DualCorners[i]=corners.ToArray(); UnitCellAreas[i]=SphericalPolygonArea(DualCorners[i]); for(int k=0;k<list.Count && k<6;k++) SharedDualEdgeAngularLengths6[i*6+k]=EstimateSharedEdge(i,list[k]); }
    }
    private static int CompareAround(Vector3 n, Vector3 a, Vector3 b){Vector3 r=Vector3.Cross(Mathf.Abs(n.y)<.9f?Vector3.up:Vector3.right,n).normalized; Vector3 u=Vector3.Cross(n,r); float aa=Mathf.Atan2(Vector3.Dot(a,u),Vector3.Dot(a,r)); float bb=Mathf.Atan2(Vector3.Dot(b,u),Vector3.Dot(b,r)); return aa.CompareTo(bb);}    
    private static float SphericalPolygonArea(Vector3[] p){ if(p==null||p.Length<3)return 0; double sum=0; for(int i=0;i<p.Length;i++){Vector3 a=p[(i+p.Length-1)%p.Length], b=p[i], c=p[(i+1)%p.Length]; Vector3 ab=Vector3.Cross(b,a).normalized, cb=Vector3.Cross(b,c).normalized; sum+=Math.Acos(Mathf.Clamp(Vector3.Dot(ab,cb),-1f,1f));} return (float)Math.Max(0,sum-(p.Length-2)*Math.PI); }
    private float EstimateSharedEdge(int a,int b){Vector3[] ca=DualCorners[a]; List<Vector3> common=new(); foreach(var x in ca) foreach(var y in DualCorners[b]) if(Vector3.Dot(x,y)>0.999999f) common.Add(x); return common.Count>=2?Mathf.Acos(Mathf.Clamp(Vector3.Dot(common[0],common[1]),-1f,1f)):0f;}
}
