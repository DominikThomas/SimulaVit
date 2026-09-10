using System.Collections.Generic;
using UnityEngine;

public static class GeodesicGridValidation
{
    public static bool Validate(GeodesicGridTopology t, out string message)
    {
        if (t == null) { message = "Topology is null."; return false; }
        if (t.CellCount != GeodesicGridTopology.ExpectedCellCount(t.SubdivisionLevel)) { message = "Unexpected cell count."; return false; }
        if (t.TriangleCount != GeodesicGridTopology.ExpectedTriangleCount(t.SubdivisionLevel)) { message = "Unexpected triangle count."; return false; }
        if (t.EdgeCount != GeodesicGridTopology.ExpectedEdgeCount(t.SubdivisionLevel)) { message = "Unexpected edge count."; return false; }
        int pent = 0; double area = 0;
        for (int i=0;i<t.CellCount;i++)
        {
            Vector3 d=t.CellDirections[i]; if (!IsFinite(d) || d.sqrMagnitude < .999f) { message=$"Invalid direction at {i}."; return false; }
            int n=t.NeighborCounts[i]; if (n==5) pent++; else if(n!=6){ message=$"Invalid degree {n} at {i}."; return false; }
            var seen=new HashSet<int>(); for(int k=0;k<n;k++){int nb=t.Neighbors6[i*6+k]; if(nb==i){message=$"Self-neighbor at {i}.";return false;} if(nb<0||nb>=t.CellCount||!seen.Add(nb)){message=$"Duplicate/invalid neighbor at {i}.";return false;} if(!HasNeighbor(t,nb,i)){message=$"Non-reciprocal edge {i}-{nb}.";return false;}}
            if (t.UnitCellAreas[i] <= 0f) { message=$"Non-positive area at {i}."; return false; } area += t.UnitCellAreas[i];
        }
        if (pent != 12) { message = $"Expected 12 pentagons, found {pent}."; return false; }
        if (Mathf.Abs((float)(area - 4.0 * System.Math.PI)) > 0.05f) { message = $"Area sum {area:F6} differs from 4π."; return false; }
        for(int i=0;i<t.Triangles.Length;i+=3){Vector3 a=t.CellDirections[t.Triangles[i]],b=t.CellDirections[t.Triangles[i+1]],c=t.CellDirections[t.Triangles[i+2]]; if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c).normalized)<=0){message=$"Inward winding at triangle {i/3}.";return false;}}
        message = $"OK: cells={t.CellCount}, triangles={t.TriangleCount}, edges={t.EdgeCount}, pentagons={pent}, areaSum={area:F6}."; return true;
    }
    private static bool HasNeighbor(GeodesicGridTopology t,int c,int n){for(int k=0;k<t.NeighborCounts[c];k++) if(t.Neighbors6[c*6+k]==n)return true; return false;}
    private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
