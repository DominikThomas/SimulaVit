using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class PlanetCellInspectorPanel : MonoBehaviour
{
    [Header("Panel Roots")]
    [SerializeField] private GameObject panelRoot;

    [Header("UI References")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private TMP_Text layersText;
    [SerializeField] private ScrollRect layersScrollRect;
    [SerializeField] private Button closeButton;

    [Header("Formatting")]
    [SerializeField] private ReplicatorManager replicatorManager;

    private readonly StringBuilder summaryBuilder = new StringBuilder(1024);
    private readonly StringBuilder layersBuilder = new StringBuilder(2048);

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }

        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
        }
    }

    public void ShowSnapshot(PlanetResourceMap.CellInspectionSnapshot snapshot)
    {
        bool wasVisible = IsVisible();

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (titleText != null)
        {
            titleText.text = $"Cell {snapshot.CellIndex}";
        }

        if (summaryText != null)
        {
            BuildSummary(snapshot, summaryBuilder, GetTemperatureDisplayUnit());
            summaryText.text = summaryBuilder.ToString();
        }

        if (layersText != null)
        {
            BuildLayers(snapshot, layersBuilder, GetTemperatureDisplayUnit());
            layersText.text = layersBuilder.ToString();

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(layersText.rectTransform);
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public bool IsVisible()
    {
        return panelRoot != null && panelRoot.activeSelf;
    }

    private TemperatureDisplayUnit GetTemperatureDisplayUnit()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        return replicatorManager != null
            ? replicatorManager.temperatureDisplayUnit
            : TemperatureDisplayUnit.Celsius;
    }

    private static void BuildSummary(PlanetResourceMap.CellInspectionSnapshot snapshot, StringBuilder sb, TemperatureDisplayUnit temperatureDisplayUnit)
    {
        sb.Clear();
        sb.AppendLine(snapshot.IsOcean ? "Type: Ocean" : "Type: Land");
        sb.AppendLine($"Active Layers: {snapshot.ActiveLayerCount}");
        sb.AppendLine($"Insolation: {snapshot.Insolation:0.###}");
        sb.AppendLine($"Vent Strength: {snapshot.VentStrength:0.###}");
        sb.AppendLine($"Temp: {ReplicatorManager.FormatTemperature(snapshot.EffectiveTemperatureKelvin, temperatureDisplayUnit)}");
        sb.AppendLine();
        sb.AppendLine("Effective Summary");
        sb.AppendLine($"CO2: {snapshot.EffectiveCO2:0.####}");
        sb.AppendLine($"O2: {snapshot.EffectiveO2:0.####}");
        sb.AppendLine($"CH4: {snapshot.EffectiveCH4:0.####}");
        sb.AppendLine($"OrganicC: {snapshot.EffectiveOrganicC:0.####}");
        sb.AppendLine($"H2: {snapshot.EffectiveH2:0.####}");
        sb.AppendLine($"H2S: {snapshot.EffectiveH2S:0.####}");
        sb.AppendLine($"DissolvedFe2+: {snapshot.EffectiveDissolvedFe2Plus:0.####}");
        sb.AppendLine($"Light Factor: {snapshot.EffectiveLegacy.LightFactor:0.####}");
    }

    private static void BuildLayers(PlanetResourceMap.CellInspectionSnapshot snapshot, StringBuilder sb, TemperatureDisplayUnit temperatureDisplayUnit)
    {
        sb.Clear();

        if (!snapshot.IsOcean)
        {
            sb.AppendLine("Land cell: no ocean layer stack.");
            return;
        }

        if (snapshot.OceanLayers == null || snapshot.OceanLayers.Length == 0)
        {
            sb.AppendLine("Ocean cell has no active layers.");
            return;
        }

        for (int i = 0; i < snapshot.OceanLayers.Length; i++)
        {
            PlanetResourceMap.OceanLayerSnapshot layer = snapshot.OceanLayers[i];
            if (layer.LayerIndex == 0)
            {
                sb.AppendLine($"Surface layer");
            }
            else if (layer.LayerIndex == snapshot.OceanLayers.Length - 1) {
                sb.AppendLine($"Bottom layer");
            }
            else
            {
                sb.AppendLine($"Layer {layer.LayerIndex}");
            }
            sb.AppendLine($"  O2: {layer.O2:0.###}, CO2: {layer.CO2:0.###}");
            sb.AppendLine($"  DissolvedFe2+: {layer.DissolvedFe2Plus:0.###}");
            sb.AppendLine($"  CH4: {layer.CH4:0.###}, H2: {layer.H2:0.###}, H2S: {layer.H2S:0.###}");
            sb.AppendLine($"  Light: {layer.LightFactor:0.###}, OrganicC: {layer.OrganicC:0.###}");
            sb.AppendLine($"  Temp Estimate: {ReplicatorManager.FormatTemperature(layer.TemperatureKelvinEstimate, temperatureDisplayUnit)}");
            sb.AppendLine();
        }
    }
}
