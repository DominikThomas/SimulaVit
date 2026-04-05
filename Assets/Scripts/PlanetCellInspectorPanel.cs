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
    [SerializeField] private Button closeButton;

    [Header("Formatting")]
    [SerializeField] private string titlePrefix = "Cell Inspector";

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
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (titleText != null)
        {
            titleText.text = $"{titlePrefix} • Cell {snapshot.CellIndex}";
        }

        if (summaryText != null)
        {
            BuildSummary(snapshot, summaryBuilder);
            summaryText.text = summaryBuilder.ToString();
        }

        if (layersText != null)
        {
            BuildLayers(snapshot, layersBuilder);
            layersText.text = layersBuilder.ToString();
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

    private static void BuildSummary(PlanetResourceMap.CellInspectionSnapshot snapshot, StringBuilder sb)
    {
        sb.Clear();
        sb.AppendLine(snapshot.IsOcean ? "Type: Ocean" : "Type: Land");
        sb.AppendLine($"Active Layers: {snapshot.ActiveLayerCount}");
        sb.AppendLine($"Insolation: {snapshot.Insolation:0.###}");
        sb.AppendLine($"Vent Strength: {snapshot.VentStrength:0.###}");
        sb.AppendLine($"Temp (K): {snapshot.EffectiveTemperatureKelvin:0.##}");
        sb.AppendLine();
        sb.AppendLine("Effective / Legacy Summary");
        sb.AppendLine($"CO2: {snapshot.EffectiveCO2:0.####}");
        sb.AppendLine($"O2: {snapshot.EffectiveO2:0.####}");
        sb.AppendLine($"CH4: {snapshot.EffectiveCH4:0.####}");
        sb.AppendLine($"OrganicC: {snapshot.EffectiveOrganicC:0.####}");
        sb.AppendLine($"H2: {snapshot.EffectiveH2:0.####}");
        sb.AppendLine($"H2S: {snapshot.EffectiveH2S:0.####}");
        sb.AppendLine($"DissolvedFe2+: {snapshot.EffectiveDissolvedFe2Plus:0.####}");
        sb.AppendLine($"Light Factor: {snapshot.EffectiveLegacy.LightFactor:0.####}");
    }

    private static void BuildLayers(PlanetResourceMap.CellInspectionSnapshot snapshot, StringBuilder sb)
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

        sb.AppendLine("Ocean Layers (top -> bottom)");
        for (int i = 0; i < snapshot.OceanLayers.Length; i++)
        {
            PlanetResourceMap.OceanLayerSnapshot layer = snapshot.OceanLayers[i];
            sb.AppendLine();
            sb.AppendLine($"Layer {layer.LayerIndex + 1}");
            sb.AppendLine($"  O2: {layer.O2:0.####}");
            sb.AppendLine($"  DissolvedFe2+: {layer.DissolvedFe2Plus:0.####}");
            sb.AppendLine($"  CO2 (cell-level): {layer.CO2:0.####}");
            sb.AppendLine($"  CH4: {layer.CH4:0.####}");
            sb.AppendLine($"  OrganicC: {layer.OrganicC:0.####}");
            sb.AppendLine($"  H2: {layer.H2:0.####}");
            sb.AppendLine($"  H2S: {layer.H2S:0.####}");
            sb.AppendLine($"  Light: {layer.LightFactor:0.####}");
            sb.AppendLine($"  Temp Offset (K): {layer.TemperatureOffset:0.###}");
            sb.AppendLine($"  Temp Estimate (K): {layer.TemperatureKelvinEstimate:0.##}");
        }
    }
}
