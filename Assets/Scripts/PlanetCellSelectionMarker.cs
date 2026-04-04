using UnityEngine;

[DisallowMultipleComponent]
public class PlanetCellSelectionMarker : MonoBehaviour
{
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private Transform markerTransform;
    [SerializeField] private float normalOffset = 0.02f;
    [SerializeField] private bool orientToSurface = true;

    private void Awake()
    {
        if (planetGenerator == null)
        {
            planetGenerator = FindFirstObjectByType<PlanetGenerator>();
        }

        if (markerTransform == null)
        {
            markerTransform = transform;
        }

        Hide();
    }

    public void ShowSelection(int cellIndex, Vector3 directionFromCenter, bool isOcean)
    {
        if (planetGenerator == null || markerTransform == null)
        {
            return;
        }

        Vector3 dir = directionFromCenter.sqrMagnitude > 0f ? directionFromCenter.normalized : Vector3.up;
        float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
        float radiusOffset = Mathf.Max(0f, normalOffset);

        markerTransform.position = planetGenerator.transform.position + dir * (surfaceRadius + radiusOffset);
        markerTransform.gameObject.SetActive(true);

        if (orientToSurface)
        {
            markerTransform.rotation = Quaternion.LookRotation(dir, markerTransform.up);
        }
    }

    public void Hide()
    {
        if (markerTransform != null)
        {
            markerTransform.gameObject.SetActive(false);
        }
    }
}
