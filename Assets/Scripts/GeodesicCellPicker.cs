using UnityEngine;

public class GeodesicCellPicker : MonoBehaviour
{
    public Camera pickingCamera;
    public int selectedCellIndex = -1;
    public int selectedNeighborCount;
    public bool selectedIsPentagon;
    public float selectedUnitArea;
    public int[] selectedNeighborIndices = System.Array.Empty<int>();
    private GeodesicGridTopology topology;

    public void SetTopology(GeodesicGridTopology t){topology=t; ClearSelection();}
    public void ClearSelection(){selectedCellIndex=-1; selectedNeighborCount=0; selectedIsPentagon=false; selectedUnitArea=0; selectedNeighborIndices=System.Array.Empty<int>();}
    private void Update(){ if(Input.GetMouseButtonDown(0)) Pick(Input.mousePosition); }
    public bool Pick(Vector2 screenPosition)
    {
        if(topology==null) return false; Camera cam=pickingCamera!=null?pickingCamera:Camera.main; if(cam==null)return false;
        if(Physics.Raycast(cam.ScreenPointToRay(screenPosition), out RaycastHit hit)){SelectNearest(transform.InverseTransformPoint(hit.point).normalized); return true;} return false;
    }
    public int SelectNearest(Vector3 dir){float best=-2f; int bestIdx=-1; for(int i=0;i<topology.CellCount;i++){float d=Vector3.Dot(dir,topology.CellDirections[i]); if(d>best+1e-7f || (Mathf.Abs(d-best)<=1e-7f && i<bestIdx)){best=d; bestIdx=i;}} Apply(bestIdx); return bestIdx;}
    private void Apply(int i){selectedCellIndex=i; if(i<0)return; selectedNeighborCount=topology.NeighborCounts[i]; selectedIsPentagon=topology.IsPentagon[i]; selectedUnitArea=topology.UnitCellAreas[i]; selectedNeighborIndices=new int[selectedNeighborCount]; for(int k=0;k<selectedNeighborCount;k++) selectedNeighborIndices[k]=topology.Neighbors6[i*6+k];}
}
