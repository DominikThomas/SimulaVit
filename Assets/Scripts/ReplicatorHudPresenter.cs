using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorHudPresenter
{
    private readonly int[] totalByLocomotion = new int[4];
    private readonly int[] chemosynthByLocomotion = new int[4];
    private readonly int[] hydrogenByLocomotion = new int[4];
    private readonly int[] photosynthByLocomotion = new int[4];
    private readonly int[] saprotrophByLocomotion = new int[4];
    private readonly int[] predatorByLocomotion = new int[4];
    private readonly int[] fermentByLocomotion = new int[4];
    private readonly int[] methanogenByLocomotion = new int[4];
    private readonly int[] methanotrophByLocomotion = new int[4];

    private GUIStyle hudStyle;
    private GUIStyle hudBackgroundStyle;
    private GUIStyle buttonStyle;

    private float hudMeanTempKelvin;
    private float hudMinTempKelvin;
    private float hudMaxTempKelvin;
    private float nextHudTempSampleTime;

    private bool showMenu;
    private bool portraitShowReplicators;
    private bool initialized;

    private float guiScale = 1f;
    private float masterVolume = 1f;

    private const float ReferenceHeight = 1080f;
    private const float MinGuiScale = 1f;
    private const float MaxGuiScale = 2.5f;

    public void Draw(
        List<Replicator> agents,
        PlanetResourceMap planetResourceMap,
        int chemosynthAgentCount,
        int hydrogenotrophAgentCount,
        int photosynthAgentCount,
        int saprotrophAgentCount,
        int predatorAgentCount,
        int fermenterAgentCount,
        int methanogenAgentCount,
        int methanotrophAgentCount,
        ref TemperatureDisplayUnit temperatureDisplayUnit)
    {
        EnsureInitialized();

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            showMenu = !showMenu;
            SetPauseState(showMenu);
        }

        UpdateGuiScale();
        EnsureHudStyles();

        hudStyle.fontSize = Mathf.RoundToInt(14f * guiScale);
        buttonStyle.fontSize = Mathf.RoundToInt(13f * guiScale);

        int totalAgents = agents.Count;

        Array.Clear(totalByLocomotion, 0, totalByLocomotion.Length);
        Array.Clear(chemosynthByLocomotion, 0, chemosynthByLocomotion.Length);
        Array.Clear(hydrogenByLocomotion, 0, hydrogenByLocomotion.Length);
        Array.Clear(photosynthByLocomotion, 0, photosynthByLocomotion.Length);
        Array.Clear(saprotrophByLocomotion, 0, saprotrophByLocomotion.Length);
        Array.Clear(predatorByLocomotion, 0, predatorByLocomotion.Length);
        Array.Clear(fermentByLocomotion, 0, fermentByLocomotion.Length);
        Array.Clear(methanogenByLocomotion, 0, methanogenByLocomotion.Length);
        Array.Clear(methanotrophByLocomotion, 0, methanotrophByLocomotion.Length);

        for (int i = 0; i < agents.Count; i++)
        {
            int locomotionIndex = Mathf.Clamp((int)agents[i].locomotion, 0, totalByLocomotion.Length - 1);
            totalByLocomotion[locomotionIndex]++;

            if (agents[i].metabolism == MetabolismType.Hydrogenotrophy)
            {
                hydrogenByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Photosynthesis)
            {
                photosynthByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Saprotrophy)
            {
                saprotrophByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Predation)
            {
                predatorByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Fermentation)
            {
                fermentByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Methanogenesis)
            {
                methanogenByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Methanotrophy)
            {
                methanotrophByLocomotion[locomotionIndex]++;
            }
            else
            {
                chemosynthByLocomotion[locomotionIndex]++;
            }
        }

        float globalCo2 = planetResourceMap != null ? planetResourceMap.debugGlobalCO2 : 0f;
        float globalO2 = planetResourceMap != null ? planetResourceMap.debugGlobalO2 : 0f;
        float globalCH4 = planetResourceMap != null ? planetResourceMap.debugGlobalCH4 : 0f;

        float atmosphereTotal = Mathf.Max(0.0001f, globalCo2 + globalO2 + globalCH4);
        float co2Pct = (globalCo2 / atmosphereTotal) * 100f;
        float o2Pct = (globalO2 / atmosphereTotal) * 100f;
        float ch4Pct = (globalCH4 / atmosphereTotal) * 100f;

        float dissolvedFe2Total = planetResourceMap != null ? planetResourceMap.debugDissolvedFe2PlusTotal : 0f;
        float dissolvedFe2OceanMean = planetResourceMap != null ? planetResourceMap.debugDissolvedFe2PlusOceanMean : 0f;
        float dissolvedFe2RemainingPct = planetResourceMap != null ? planetResourceMap.debugDissolvedFe2PlusRemainingFraction * 100f : 0f;

        SampleHudTemperatureStats(planetResourceMap);

        string atmosphereText =
            "Atmosphere (global average)\n" +
            $"CO2: {globalCo2:0.000} ({co2Pct:0.0}%)\n" +
            $"O2: {globalO2:0.000} ({o2Pct:0.0}%)\n" +
            $"CH4: {globalCH4:0.000} ({ch4Pct:0.0}%)\n" +
            $"Dissolved Fe2+: {dissolvedFe2OceanMean:0.000} avg / {dissolvedFe2Total:0.0} total ({dissolvedFe2RemainingPct:0.0}% rem)\n" +
            $"Temp Mean: {ReplicatorManager.FormatTemperature(hudMeanTempKelvin, temperatureDisplayUnit)}\n" +
            $"Temp Min: {ReplicatorManager.FormatTemperature(hudMinTempKelvin, temperatureDisplayUnit)}\n" +
            $"Temp Max: {ReplicatorManager.FormatTemperature(hudMaxTempKelvin, temperatureDisplayUnit)}";

        float safeTotal = Mathf.Max(1f, totalAgents);
        string replicatorsText =
            "Replicators (Passive/Amoeboid/Flagellum/Anchored)\n" +
            $"Total: {FormatLocomotionCounts(totalByLocomotion)}\n" +
            $"<color=#D9FFFF>Hydrogen:</color> {FormatLocomotionCounts(hydrogenByLocomotion)} ({(100f * hydrogenotrophAgentCount / safeTotal):0.0}%)";

        if (chemosynthAgentCount > 0)
        {
            replicatorsText += $"\n<color=#FFD54A>Sulfur:</color> {FormatLocomotionCounts(chemosynthByLocomotion)} ({(100f * chemosynthAgentCount / safeTotal):0.0}%)";
        }

        if (photosynthAgentCount > 0)
        {
            replicatorsText += $"\n<color=#79E07E>Photo:</color> {FormatLocomotionCounts(photosynthByLocomotion)} ({(100f * photosynthAgentCount / safeTotal):0.0}%)";
        }

        if (saprotrophAgentCount > 0)
        {
            replicatorsText += $"\n<color=#62B0FF>Sapro:</color> {FormatLocomotionCounts(saprotrophByLocomotion)} ({(100f * saprotrophAgentCount / safeTotal):0.0}%)";
        }

        if (predatorAgentCount > 0)
        {
            replicatorsText += $"\n<color=#FF5A5A>Predator:</color> {FormatLocomotionCounts(predatorByLocomotion)} ({(100f * predatorAgentCount / safeTotal):0.0}%)";
        }

        if (fermenterAgentCount > 0)
        {
            replicatorsText += $"\n<color=#FF8C1A>Ferment:</color> {FormatLocomotionCounts(fermentByLocomotion)} ({(100f * fermenterAgentCount / safeTotal):0.0}%)";
        }

        if (methanogenAgentCount > 0)
        {
            replicatorsText += $"\n<color=#9955E6>Methanogen:</color> {FormatLocomotionCounts(methanogenByLocomotion)} ({(100f * methanogenAgentCount / safeTotal):0.0}%)";
        }

        if (methanotrophAgentCount > 0)
        {
            replicatorsText += $"\n<color=#FF73BF>Methanotroph:</color> {FormatLocomotionCounts(methanotrophByLocomotion)} ({(100f * methanotrophAgentCount / safeTotal):0.0}%)";
        }

        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));

        float scaledScreenWidth = Screen.width / guiScale;
        float scaledScreenHeight = Screen.height / guiScale;
        bool isPortrait = Screen.height > Screen.width;

        float panelWidth = isPortrait
            ? Mathf.Clamp(scaledScreenWidth * 0.92f, 300f, 520f)
            : Mathf.Clamp(scaledScreenWidth * 0.22f, 360f, 700f);
        const float padding = 14f;
        const float lineHeight = 24f;
        const float edgeMargin = 24f;

        float rightX = scaledScreenWidth - panelWidth - edgeMargin;

        float contentWidth = panelWidth - 2f * padding;

        float atmosphereTextHeight = hudStyle.CalcHeight(new GUIContent(atmosphereText), contentWidth);
        float replicatorTextHeight = hudStyle.CalcHeight(new GUIContent(replicatorsText), contentWidth);

        float atmosphereHeight = atmosphereTextHeight + (padding * 2f);
        float replicatorHeight = replicatorTextHeight + (padding * 2f);

        if (isPortrait)
        {
            DrawPortraitHud(
                rightX,
                edgeMargin,
                panelWidth,
                padding,
                lineHeight,
                atmosphereText,
                replicatorsText,
                atmosphereHeight,
                replicatorHeight,
                ref temperatureDisplayUnit);
        }
        else
        {
            Rect atmosphereRect = new Rect(rightX, edgeMargin, panelWidth, atmosphereHeight);
            GUI.Box(atmosphereRect, GUIContent.none, hudBackgroundStyle);
            GUI.Label(
                new Rect(
                    atmosphereRect.x + padding,
                    atmosphereRect.y + padding,
                    panelWidth - 2f * padding,
                    atmosphereHeight - 2f * padding),
                atmosphereText,
                hudStyle);

            Rect tempUnitButtonRect = new Rect(rightX, atmosphereRect.yMax + 6f, panelWidth, lineHeight);
            if (GUI.Button(tempUnitButtonRect, $"Temp Unit: {GetTemperatureUnitLabel(temperatureDisplayUnit)}", buttonStyle))
            {
                temperatureDisplayUnit =
                    (TemperatureDisplayUnit)(((int)temperatureDisplayUnit + 1) % Enum.GetValues(typeof(TemperatureDisplayUnit)).Length);
            }

            Rect replicatorRect = new Rect(
                rightX,
                scaledScreenHeight - replicatorHeight - edgeMargin,
                panelWidth,
                replicatorHeight);

            GUI.Box(replicatorRect, GUIContent.none, hudBackgroundStyle);
            GUI.Label(
                new Rect(
                    replicatorRect.x + padding,
                    replicatorRect.y + padding,
                    panelWidth - 2f * padding,
                    replicatorHeight - 2f * padding),
                replicatorsText,
                hudStyle);
        }

        DrawMenu(scaledScreenWidth, scaledScreenHeight);

        GUI.matrix = oldMatrix;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        AudioListener.volume = masterVolume;
    }

    private void SampleHudTemperatureStats(PlanetResourceMap planetResourceMap)
    {
        if (planetResourceMap == null)
        {
            hudMeanTempKelvin = 0f;
            hudMinTempKelvin = 0f;
            hudMaxTempKelvin = 0f;
            return;
        }

        if (Time.unscaledTime < nextHudTempSampleTime)
        {
            return;
        }

        nextHudTempSampleTime = Time.unscaledTime + 0.5f;
        planetResourceMap.GetTemperatureStats(out hudMeanTempKelvin, out hudMinTempKelvin, out hudMaxTempKelvin);
    }

    private static string GetTemperatureUnitLabel(TemperatureDisplayUnit unit)
    {
        switch (unit)
        {
            case TemperatureDisplayUnit.Kelvin:
                return "K";
            case TemperatureDisplayUnit.Fahrenheit:
                return "°F";
            default:
                return "°C";
        }
    }

    private void EnsureHudStyles()
    {
        if (hudStyle != null && hudBackgroundStyle != null && buttonStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(14f * guiScale),
            richText = true,
            alignment = TextAnchor.UpperLeft
        };
        hudStyle.normal.textColor = Color.white;
        hudStyle.wordWrap = true;

        Texture2D backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
        backgroundTexture.Apply();

        hudBackgroundStyle = new GUIStyle(GUI.skin.box);
        hudBackgroundStyle.normal.background = backgroundTexture;
        hudBackgroundStyle.padding = new RectOffset(8, 8, 8, 8);

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(13f * guiScale),
            alignment = TextAnchor.MiddleCenter
        };
    }

    private static string FormatLocomotionCounts(int[] counts)
    {
        return $"{counts[0]}/{counts[1]}/{counts[2]}/{counts[3]}";
    }

    private void UpdateGuiScale()
    {
        float scaleFromHeight = Screen.height / ReferenceHeight;
        guiScale = Mathf.Clamp(scaleFromHeight, MinGuiScale, MaxGuiScale);
    }

    private void DrawMenu(float scaledScreenWidth, float scaledScreenHeight)
    {
        if (!showMenu)
        {
            return;
        }

        float width = 360f;
        float height = 240f;

        Rect rect = new Rect(
            (scaledScreenWidth - width) * 0.5f,
            (scaledScreenHeight - height) * 0.5f,
            width,
            height);

        GUI.Box(rect, "Menu", hudBackgroundStyle);

        float x = rect.x + 16f;
        float y = rect.y + 40f;
        float contentWidth = rect.width - 32f;

        GUI.Label(
            new Rect(x, y, contentWidth, 28f),
            $"Master Volume: {Mathf.RoundToInt(masterVolume * 100f)}%",
            hudStyle);

        y += 30f;

        float newVolume = GUI.HorizontalSlider(new Rect(x, y, contentWidth, 24f), masterVolume, 0f, 1f);
        if (!Mathf.Approximately(newVolume, masterVolume))
        {
            masterVolume = newVolume;
            AudioListener.volume = masterVolume;
            PlayerPrefs.SetFloat("MasterVolume", masterVolume);
            PlayerPrefs.Save();
        }

        y += 44f;

        if (GUI.Button(new Rect(x, y, contentWidth, 36f), "Resume", buttonStyle))
        {
            showMenu = false;
            SetPauseState(false);
        }

        y += 46f;

        if (GUI.Button(new Rect(x, y, contentWidth, 36f), "Exit Application", buttonStyle))
        {
            SetPauseState(false);
            QuitGame();
        }
    }

    private void DrawPortraitHud(
        float panelX,
        float panelY,
        float panelWidth,
        float padding,
        float lineHeight,
        string atmosphereText,
        string replicatorsText,
        float atmosphereHeight,
        float replicatorHeight,
        ref TemperatureDisplayUnit temperatureDisplayUnit)
    {
        float compactPanelHeight = Mathf.Min(
            Mathf.Max(atmosphereHeight, replicatorHeight),
            (Screen.height / guiScale) * 0.55f);
        string selectedText = portraitShowReplicators ? replicatorsText : atmosphereText;
        float contentHeight = hudStyle.CalcHeight(new GUIContent(selectedText), panelWidth - 2f * padding);
        float panelHeight = Mathf.Max(compactPanelHeight, contentHeight + (padding * 2f));

        Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);
        GUI.Box(panelRect, GUIContent.none, hudBackgroundStyle);
        GUI.Label(
            new Rect(
                panelRect.x + padding,
                panelRect.y + padding,
                panelWidth - 2f * padding,
                panelHeight - 2f * padding),
            selectedText,
            hudStyle);

        float controlsY = panelRect.yMax + 6f;
        float thirdWidth = (panelWidth - 12f) / 3f;
        if (GUI.Button(new Rect(panelX, controlsY, thirdWidth, lineHeight), "Atmosphere", buttonStyle))
        {
            portraitShowReplicators = false;
        }

        if (GUI.Button(new Rect(panelX + thirdWidth + 6f, controlsY, thirdWidth, lineHeight), "Replicators", buttonStyle))
        {
            portraitShowReplicators = true;
        }

        if (GUI.Button(new Rect(panelX + (thirdWidth + 6f) * 2f, controlsY, thirdWidth, lineHeight), $"Temp: {GetTemperatureUnitLabel(temperatureDisplayUnit)}", buttonStyle))
        {
            temperatureDisplayUnit =
                (TemperatureDisplayUnit)(((int)temperatureDisplayUnit + 1) % Enum.GetValues(typeof(TemperatureDisplayUnit)).Length);
        }
    }

    private static void SetPauseState(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
