using System.Globalization;
using UnityEngine;

public partial class SimulationStartupController
{
    private enum SetupTab { Planet, Ocean, Atmosphere, Biology, Advanced, Debug }
    private static readonly string[] SetupTabNames = { "Planet", "Ocean", "Atmosphere", "Biology", "Advanced", "Debug" };
    private static readonly string[] GridNames = { "Cube Sphere (Legacy)", "Geodesic Icosphere" };
    private static readonly string[] SeaLevelNames = { "Manual offset", "Target area coverage", "Ocean World" };
    private static readonly string[] HydrologyNames = { "Visible terrain", "Fine detail removed (full ridges)", "Reduced ridge + fine detail (reference)" };
    private static readonly string[] MetabolismNames = { "Hydrogenotrophy", "Sulfur chemosynthesis", "Fermentation", "Methanogenesis", "Methanotrophy", "Photosynthesis", "Saprotrophy", "Predation" };
    private readonly Vector2[] tabScrollPositions = new Vector2[6];
    private SetupTab selectedSetupTab;
    private int selectedMetabolism;
    private GUIStyle setupHelpStyle, setupSectionStyle, setupTabStyle, selectedSetupTabStyle;
    private readonly System.Collections.Generic.Dictionary<string, GUIContent> setupLabelContents = new();
    private sealed class SetupNumericEdit
    {
        public float value;
        public string text, formatted;
        public SetupNumericEdit(float number)
        {
            value = number;
            text = formatted = number.ToString("G9", CultureInfo.InvariantCulture);
        }
    }
    private bool IsGeodesicSetup => currentConfig.gridType == PlanetGridType.GeodesicIcosphere;

    private void EnsureSetupStyles()
    {
        if (setupHelpStyle != null) return;
        setupHelpStyle = new GUIStyle(labelStyle) { wordWrap = true, fontSize = 13 };
        setupSectionStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = 16, wordWrap = true };
        setupTabStyle = new GUIStyle(buttonStyle) { fontSize = 14, padding = new RectOffset(4, 4, 7, 7) };
        selectedSetupTabStyle = new GUIStyle(setupTabStyle);
        selectedSetupTabStyle.normal.textColor = new Color(0.4f, 0.9f, 1f);
        selectedSetupTabStyle.normal.background = GUI.skin.button.active.background;
    }

    private void DrawBuiltInConfigScreen()
    {
        EnsureSetupStyles();
        // Keep six readable tabs on small displays. Scale the entire setup uniformly;
        // GUILayout still measures its content, so no content-height estimates are needed.
        float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 760f, Screen.height / 520f));
        scale = Mathf.Max(0.01f, scale);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float screenWidth = Screen.width / scale;
        float screenHeight = Screen.height / scale;
        float width = Mathf.Min(Mathf.Max(900f, setupGuiWidth), screenWidth - 24f);
        float top = Mathf.Clamp(setupGuiTopPadding, 12f, Mathf.Max(12f, screenHeight * 0.06f));
        var area = new Rect((screenWidth - width) * 0.5f, top, width, screenHeight - top - 12f);
        int nextTab = (int)selectedSetupTab;
        try
        {
            GUILayout.BeginArea(area, GUIContent.none, boxStyle);
            GUILayout.Label("Planet Simulation Setup", titleStyle, GUILayout.Height(38f));
            SetupBool("Use Random Seed", ref currentConfig.useRandomSeed);
            GUILayout.BeginHorizontal();
            SetupInt("Planet Seed", ref currentConfig.planetSeed, int.MinValue, int.MaxValue, !currentConfig.useRandomSeed);
            if (GUILayout.Button("Randomize", buttonStyle, GUILayout.Width(105f), GUILayout.Height(28f)))
            {
                RandomizeSeed();
                integerFieldEdits.Remove("Planet Seed");
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Planet Grid", setupHelpStyle, GUILayout.Width(150f));
            int grid = GUILayout.SelectionGrid(IsGeodesicSetup ? 1 : 0, GridNames, 2, buttonStyle, GUILayout.Height(30f));
            currentConfig.gridType = grid == 1 ? PlanetGridType.GeodesicIcosphere : PlanetGridType.LegacyCubeSphere;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < SetupTabNames.Length; i++)
                if (GUILayout.Button(SetupTabNames[i], i == (int)selectedSetupTab ? selectedSetupTabStyle : setupTabStyle,
                    GUILayout.Height(34f), GUILayout.ExpandWidth(true))) nextTab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            int tab = (int)selectedSetupTab;
            tabScrollPositions[tab] = GUILayout.BeginScrollView(tabScrollPositions[tab], false, true, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            switch (selectedSetupTab)
            {
                case SetupTab.Planet: DrawPlanetTab(); break;
                case SetupTab.Ocean: DrawOceanTab(); break;
                case SetupTab.Atmosphere: DrawAtmosphereTab(); break;
                case SetupTab.Biology: DrawBiologyTab(); break;
                case SetupTab.Advanced: DrawAdvancedTab(); break;
                case SetupTab.Debug: DrawDebugTab(); break;
            }
            GUILayout.Space(8f);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            // Fixed footer: neither actions nor tooltip are part of a tab's scroll area.
            GUILayout.Label(GUI.tooltip, setupHelpStyle, GUILayout.Height(38f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start", buttonStyle, GUILayout.Height(34f))) StartSimulation();
            if (GUILayout.Button("Start Paused", buttonStyle, GUILayout.Height(34f))) StartSimulationPaused();
            if (GUILayout.Button("Reset Defaults", buttonStyle, GUILayout.Height(34f))) ResetDefaults();
            if (GUILayout.Button("Back", buttonStyle, GUILayout.Height(34f))) BackToStartMenu();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        finally { GUI.matrix = previousMatrix; }
        if (nextTab != (int)selectedSetupTab)
        {
            // Every tab keeps its own scroll position, including after changing a setting.
            GUI.FocusControl(null);
            NormalizeAdditionalSettings(currentConfig, defaults);
            selectedSetupTab = (SetupTab)nextTab;
            GUIUtility.ExitGUI();
        }
    }

    private void DrawPlanetTab()
    {
        SetupSection("Planet and rotation");
        SetupFloat("Axis Tilt (degrees)", ref currentConfig.axisTiltDegrees, AxisTiltMinDegrees, AxisTiltMaxDegrees, true);
        SetupFloat("Day Length (simulated seconds)", ref currentConfig.dayLengthSeconds, DayLengthMinSeconds, DayLengthMaxSeconds, true);
        SetupFloat("Year Length (days)", ref currentConfig.yearLengthInDays, YearLengthMinDays, YearLengthMaxDays, true);
        if (!IsGeodesicSetup)
        {
            SetupInt("Cube Sphere Resolution", ref currentConfig.cubeSphereResolution, 3, 240);
            SetupHelp("Ocean coverage, river generation, lakes and inland-sea filtering below use geodesic terrain. Legacy ocean geometry remains configured on PlanetGenerator in the Inspector.");
            return;
        }
        SetupHelp("Geodesic simulation subdivision and technical geometry settings are in Advanced.");
        SetupSection("Ocean coverage");
        DrawTargetCoverageToggle();
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && currentConfig.geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.TargetAreaCoverage;
        SetupFloat("Target Ocean Coverage (%)", ref currentConfig.geodesicTargetOceanCoveragePercent, 0f, 100f, true,
            "Physical spherical area below the resolved sea level. Applied before generation; not a retained-ocean guarantee.");
        GUI.enabled = enabled;
        SetupHelp("Target coverage may differ from retained-ocean coverage after disconnected seas are excluded or geological constraints apply. Manual offset and Ocean World modes are in Advanced.");

        SetupSection("Rivers");
        SetupBool("Generate Rivers", ref currentConfig.enableGeodesicRivers);
        GUI.enabled = enabled && currentConfig.enableGeodesicRivers;
        SetupFloat("River flow threshold", ref currentConfig.riverFlowThreshold, 0.01f, float.MaxValue, tooltip: "Minimum accumulated runoff for a visible river; mean-cell-area equivalents at unit runoff.");
        SetupFloat("Major river threshold", ref currentConfig.majorRiverThreshold, 0.01f, float.MaxValue, tooltip: "Accumulated runoff threshold for classifying/scaling major rivers.");
        GUI.enabled = enabled;
        SetupHelp("Disabling rivers suppresses river generation/presentation. Lakes, carving or basin infill may still require drainage computation.");

        SetupSection("Lakes");
        SetupBool("Generate Lakes", ref currentConfig.generateHydrologicalLakes);
        GUI.enabled = enabled && currentConfig.generateHydrologicalLakes;
        SetupPercent("Minimum lake basin area (% of planet)", ref currentConfig.minimumLakeBasinAreaFraction, 5f);
        SetupFloat("Minimum lake depth (planet units)", ref currentConfig.minimumLakeDepth, 0f, 1f);
        GUI.enabled = enabled;
        SetupHelp("Significant valid depressions fill to their spill elevation. Some planets have no basins that meet these thresholds.");

        SetupSection("Inland seas");
        SetupBool("Exclude small disconnected inland seas", ref currentConfig.excludeSmallDisconnectedSeas);
        GUI.enabled = enabled && currentConfig.excludeSmallDisconnectedSeas;
        SetupPercent("Minimum disconnected-ocean component area (% of planet)", ref currentConfig.minimumOceanComponentAreaFraction,
            GeodesicOceanConnectivity.MaximumAreaFraction * 100f);
        GUI.enabled = enabled;
    }

    private void DrawOceanTab()
    {
        SetupSection("Initial dissolved resources");
        SetupHelp("Initial ocean concentrations in the existing resource concentration units. Atmospheric gas fractions are configured separately in Atmosphere.");
        SetupFloat("Initial Ocean CO2", ref currentConfig.initialCO2, InitialAtmosphereMin, InitialCO2Max, true);
        SetupFloat("Initial Ocean O2", ref currentConfig.initialO2, InitialAtmosphereMin, InitialO2Max, true);
        SetupFloat("Initial Ocean CH4", ref currentConfig.initialCH4, InitialAtmosphereMin, InitialCH4Max, true);
        SetupFloat("Initial Ocean Fe2+", ref currentConfig.initialDissolvedFe2Plus, InitialFe2Min, InitialFe2Max, true);
        SetupSection("Ocean temperature forcing");
        SetupFloat("Base Temperature (K)", ref currentConfig.baseTempKelvin, BaseTempMinKelvin, BaseTempMaxKelvin, true);
        SetupFloat("Insolation Temperature Gain (K)", ref currentConfig.insolationTempGain, InsolationGainMin, InsolationGainMax, true);
        SetupHelp("Planetary temperature forcing; organism temperature preferences are in Biology. Geodesic temperature fields retain their existing thermal model and layer behavior.");
        SetupSection("Hydrothermal vents");
        SetupHelp("These controls set geological resource production. Biological reaction energy yields are in Biology.");
        if (IsGeodesicSetup)
        {
            SetupFloat("Vent clustering", ref currentConfig.ventClustering, 0f, 1f, true);
            SetupFloat("Terrestrial vent fraction", ref currentConfig.terrestrialVentFraction, 0f, 1f, true);
            SetupFloat("Global vent production multiplier", ref currentConfig.geodesicVentProductionMultiplier, 0f, SimulationStartupConfig.MaximumVentProductionMultiplier);
            SetupHelp("Geodesic rates: concentration × km³ per simulated second, before the global multiplier. H2S, CO2 and Fe2+ are provisional physical sources; zero disables each source.");
            SetupFloat("H2 production (concentration·km³/s)", ref currentConfig.geodesicVentH2PhysicalPerSecond, 0f, GeodesicVentPhysicalMaxPerSecond);
            SetupFloat("H2S production (concentration·km³/s)", ref currentConfig.geodesicVentH2SPhysicalPerSecond, 0f, GeodesicVentPhysicalMaxPerSecond);
            SetupFloat("CO2 production (concentration·km³/s)", ref currentConfig.geodesicVentCO2PhysicalPerSecond, 0f, GeodesicVentPhysicalMaxPerSecond);
            SetupFloat("Fe2+ production (concentration·km³/s)", ref currentConfig.geodesicVentFe2PhysicalPerSecond, 0f, GeodesicVentPhysicalMaxPerSecond);
        }
        else
        {
            SetupHelp("Legacy resource amounts per metabolism/resource tick, with existing source scaling. Geodesic clustering, terrestrial fraction and production multiplier do not affect this mode.");
            SetupFloat("H2 production / legacy tick", ref currentConfig.ventH2PerTick, VentPerTickMin, VentH2MaxPerTick);
            SetupFloat("H2S production / legacy tick", ref currentConfig.ventH2SPerTick, VentPerTickMin, VentH2SMaxPerTick);
            SetupFloat("CO2 production / legacy tick", ref currentConfig.ventCO2PerTick, VentPerTickMin, VentCO2MaxPerTick);
            SetupFloat("Fe2+ production / legacy tick", ref currentConfig.ventFe2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        }
    }

    private void DrawAtmosphereTab()
    {
        if (!IsGeodesicSetup)
        {
            SetupHelp("The finite atmosphere and air–sea exchange startup controls apply to Geodesic Icosphere. Legacy dissolved gases remain in Ocean.");
            return;
        }
        SetupSection("Initial atmosphere");
        SetupBool("Allow Dense Atmosphere (up to 600 bar)", ref currentConfig.allowDenseAtmosphere);
        SetupFloat("Initial atmospheric pressure (bar)", ref currentConfig.initialAtmospherePressureBar, 0f,
            currentConfig.allowDenseAtmosphere ? DenseAtmospherePressureMaxBar : NormalAtmospherePressureMaxBar, true,
            "Normal range: 0–5 bar. Dense atmosphere opt-in: 0–600 bar.");
        SetupFloat("Atmospheric CO2 fraction", ref currentConfig.atmosphericCO2Fraction, 0f, 1f);
        SetupFloat("Atmospheric O2 fraction", ref currentConfig.atmosphericO2Fraction, 0f, 1f);
        SetupFloat("Atmospheric CH4 fraction", ref currentConfig.atmosphericCH4Fraction, 0f, 1f);
        SetupFloat("Atmospheric H2 fraction", ref currentConfig.atmosphericH2Fraction, 0f, 1f);
        SetupFloat("Atmospheric H2S fraction", ref currentConfig.atmosphericH2SFraction, 0f, 1f);
        string focus = GUI.GetNameOfFocusedControl();
        bool editingComposition = focus == "Atmospheric CO2 fraction" || focus == "Atmospheric O2 fraction"
            || focus == "Atmospheric CH4 fraction" || focus == "Atmospheric H2 fraction" || focus == "Atmospheric H2S fraction";
        // Normalize on completion, rather than rewriting the focused field after each
        // keystroke when the five fractions temporarily exceed one.
        if (!editingComposition) NormalizeAtmosphereComposition(currentConfig);
        float remainder = Mathf.Max(0f, 1f - currentConfig.atmosphericCO2Fraction - currentConfig.atmosphericO2Fraction
            - currentConfig.atmosphericCH4Fraction - currentConfig.atmosphericH2Fraction - currentConfig.atmosphericH2SFraction);
        SetupHelp($"Derived N2 remainder: {remainder:P2} ({remainder * currentConfig.initialAtmospherePressureBar:G6} bar). Trace fractions exceeding 1 are normalized together after editing; N2 receives the remainder.");
        float traceSum = currentConfig.atmosphericCO2Fraction + currentConfig.atmosphericO2Fraction
            + currentConfig.atmosphericCH4Fraction + currentConfig.atmosphericH2Fraction + currentConfig.atmosphericH2SFraction;
        float pressureScale = currentConfig.initialAtmospherePressureBar / Mathf.Max(1f, traceSum);
        SetupHelp($"Derived gas partials after normalization (bar): CO2 {currentConfig.atmosphericCO2Fraction * pressureScale:G6} · O2 {currentConfig.atmosphericO2Fraction * pressureScale:G6} · CH4 {currentConfig.atmosphericCH4Fraction * pressureScale:G6} · H2 {currentConfig.atmosphericH2Fraction * pressureScale:G6} · H2S {currentConfig.atmosphericH2SFraction * pressureScale:G6}");
        SetupSection("Air–sea gas exchange");
        SetupFloat("Air–sea gas exchange half-life (sim s; 0 off)", ref currentConfig.airSeaExchangeHalfLifeSeconds, 0f, 1000000f);
        SetupHelp("Surface-ocean L0 concentration relaxes toward atmosphere-controlled equilibrium. This is not the depletion half-life of the finite atmosphere. Inventory scaling is in Advanced.");
    }

    private void DrawBiologyTab()
    {
        var b = currentConfig.biology;
        if (IsGeodesicSetup) DrawBiologyAvailability(ref b.geodesicAvailability);
        SetupSection("Population and reproduction");
        if (IsGeodesicSetup)
        {
            currentConfig.geodesicFounderEmergenceMode = (GeodesicFounderEmergenceMode)GUILayout.SelectionGrid(
                (int)currentConfig.geodesicFounderEmergenceMode, new[] { "Founder batch", "Gradual emergence" }, 2, buttonStyle, GUILayout.Height(30f));
        }
        bool gradualEmergence = IsGeodesicSetup && currentConfig.geodesicFounderEmergenceMode == GeodesicFounderEmergenceMode.Gradual;
        SetupInt(gradualEmergence ? "Maximum initial founder events" : "Initial replicator count", ref currentConfig.initialSpawnCount, InitialSpawnMin, InitialSpawnMax);
        if (IsGeodesicSetup)
        {
            SetupFloat("Initial emergence delay (sim s)", ref currentConfig.geodesicBiologySpawnDelaySeconds, 0f, 5000f);
            if (gradualEmergence)
            {
                SetupFloat("Mean initial emergence interval (sim s)", ref currentConfig.geodesicFounderMeanIntervalSeconds, .01f, 1000000f);
                SetupBool("Ongoing independent emergence", ref currentConfig.geodesicFounderOngoingEmergence);
                if (currentConfig.geodesicFounderOngoingEmergence)
                    SetupFloat("Mean ongoing emergence interval (sim s)", ref currentConfig.geodesicFounderOngoingMeanIntervalSeconds, .01f, 1000000f);
                SetupHelp("Individual stochastic events start after the delay. The initial count caps attempts, including rejected events; it does not guarantee survivors. Ongoing emergence is independent of reproduction and starts after the initial event phase. Only locally viable submarine vent injection habitats are eligible. All founders respect the population cap.");
            }
            else SetupHelp("The whole founder batch appears immediately at zero delay, or together after the simulated-time delay. Placement retains vent-production weighting and respects the population cap.");
            SetupInt("Initial photosynthetic founders", ref b.geodesicPhotosyntheticFounderCount, InitialSpawnMin, InitialSpawnMax);
            SetupHelp("Zero disables photosynthetic founding. A positive count requests an immediate batch in suitable sunlit ocean habitats, independent of vent emergence and subject to the population cap.");
            if (!b.geodesicAvailability.AllowsPhotosyntheticFounders)
                SetupHelp("Photosynthetic founders require Oxygenic photosynthesis and Passive drift.");
        }
        SetupInt("Maximum population", ref b.maxPopulation, 0, int.MaxValue);
        SetupFloat("Reproduction rate (/sim s)", ref b.reproductionRate);
        SetupBool("Carbon-limited division", ref b.enableCarbonLimitedDivision);
        SetupFloat("Replication energy cost (non-carbon division)", ref b.replicationEnergyCost);
        SetupFloat("Division energy cost (carbon-limited division)", ref b.divisionEnergyCost);
        SetupFloat("Basal maintenance energy / sim s", ref b.basalEnergyCostPerSecond);
        if (IsGeodesicSetup)
        {
            SetupFloat("Amoeboid movement energy / sim s", ref b.geodesicAmoeboidEnergyPerSecond, 0f, 1000f);
            SetupFloat("Flagellar movement energy / sim s", ref b.geodesicFlagellumEnergyPerSecond, 0f, 1000f);
        }
        else SetupFloat("Movement energy / sim s", ref b.moveEnergyCostPerSecond);
        SetupFloat("Initial biomass target", ref b.defaultBiomassTarget, 0.000001f);
        SetupFloat("Division biomass multiple", ref b.divisionBiomassMultiple, 1.2f, 3f);
        SetupFloat("Division carbon fraction to child", ref b.divisionCarbonSplitToChild, 0.3f, 0.7f);
        SetupHelp(IsGeodesicSetup
            ? "Energy costs and biomass thresholds retain the existing division gates. Geodesic population limits gate both founder emergence and reproduction."
            : "Energy costs and biomass thresholds retain the existing division gates. Legacy population limits apply to initial/spontaneous spawning and reproduction.");

        SetupSection("Evolution");
        DrawMetabolicEvolution(b);
        SetupSection("Locomotion evolution");
        SetupFloat("Locomotion mutation chance / birth", ref b.locomotionMutationChance, 0f, 1f);
        SetupFloat("Locomotion upgrade probability", ref b.locomotionUpgradeChance, 0f, 1f);
        SetupFloat("Anchoring mutation probability", ref b.locomotionAnchoredMutationChance, 0f, 1f);
        SetupBool("Allow anchored → amoeboid mutation", ref b.allowAnchoredToAmoeboidMutation);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && b.allowAnchoredToAmoeboidMutation;
        SetupFloat("Anchored → amoeboid probability", ref b.anchoredToAmoeboidMutationChance, 0f, 1f);
        GUI.enabled = enabled;
        if (IsGeodesicSetup) SetupHelp("Existing locomotion diagnostic mutation overrides, if enabled in the Inspector, still take precedence over the normal mutation chance.");
        SetupSection("Temperature adaptation");
        SetupFloat("Temperature adaptation mutation chance", ref b.tempMutationChance, 0f, 1f);
        SetupFloat("Temperature mutation scale (K)", ref b.tempMutationScale);

        SetupSection("Initial / inherited temperature preferences");
        SetupHelp("Editable in °C; stored in Kelvin. These ranges seed temperature traits, not absolute species-wide survival limits. Offspring retain the current inheritance and adaptation behavior. Reversed endpoints are ordered when editing finishes.");
        if (IsGeodesicSetup) SetupHelp("Vent founders use the Hydrogenotrophy template. Photosynthetic founders use the Photosynthesis template. New sulfur, fermentation and photosynthesis mutants start from their destination template before individual thermal mutation.");
        SetupTemperatureRange("Hydrogenotrophy", ref b.hydrogenTempRange, true);
        SetupTemperatureRange("Sulfur chemosynthesis", ref b.sulfurChemoTempRange, true);
        SetupTemperatureRange("Photosynthesis", ref b.photoTempRange, true);
        SetupTemperatureRange("Saprotrophy", ref b.saproTempRange, !IsGeodesicSetup);
        SetupTemperatureRange("Predation", ref b.predatorTempRange, !IsGeodesicSetup);
        SetupTemperatureRange("Fermentation", ref b.fermentationTempRange, true);
        SetupTemperatureRange("Methanogenesis", ref b.methanogenesisTempRange, !IsGeodesicSetup);
        SetupTemperatureRange("Methanotrophy", ref b.methanotrophyTempRange, !IsGeodesicSetup);
        SetupFloat("Initial lethal temperature margin (K)", ref b.defaultLethalMargin, 1f);
        SetupFloat("Temperature dormancy floor", ref b.temperatureDormancyFloor, 0f, 1f);
        SetupFloat("Minimum temperature efficiency for reproduction", ref b.temperatureReplicationMinEfficiency, 0f, 1f);
        SetupBool("Temperature direct damage", ref b.temperatureDirectDamageEnabled);
        SetupCelsius("Extreme cold threshold (°C)", ref b.extremeColdDeathTempKelvin);
        SetupCelsius("Extreme heat threshold (°C)", ref b.extremeHeatDeathTempKelvin);
        if (IsGeodesicSetup)
        {
            SetupFloat("Temperature stress maintenance multiplier", ref b.geodesicTemperatureStressMaintenanceMultiplier);
            SetupFloat("Cold exposure death time (sim s; 0 off)", ref b.geodesicColdExposureDeathSeconds);
            SetupFloat("Heat exposure death time (sim s; 0 off)", ref b.geodesicHeatExposureDeathSeconds);
            SetupFloat("Temperature exposure recovery / sim s", ref b.geodesicTemperatureExposureRecoveryPerSecond);
        }
        SetupSection("Metabolism Parameters");
        DrawMetabolismParameters(b);
        // Future Oxygen Stress, UV Exposure and Protective Traits can be separate
        // section methods here. No placeholder controls or changes to oxygen mechanics.
    }

    private void DrawBiologyAvailability(ref GeodesicBiologyAvailability availability)
    {
        SetupSection("Biology availability");
        SetupHelp("Controls which traits Geodesic founders and mutations can acquire. Living organisms keep their traits and can pass them to offspring.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("All implemented pathways", buttonStyle))
        {
            availability.metabolismMask = GeodesicBiologyAvailability.ImplementedMetabolisms;
            availability.locomotionMask = GeodesicBiologyAvailability.AllLocomotions;
        }
        if (GUILayout.Button("Existing core biology only", buttonStyle))
            availability = GeodesicBiologyAvailability.Default;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Passive-only movement", buttonStyle))
            availability.locomotionMask = GeodesicBiologyAvailability.PassiveLocomotion;
        if (GUILayout.Button("Photosynthesis-focused", buttonStyle))
            availability.metabolismMask = (1 << (int)MetabolismType.Hydrogenotrophy) | (1 << (int)MetabolismType.Photosynthesis);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Metabolisms", setupSectionStyle);
        DrawAvailabilityToggle("Hydrogenotrophy", ref availability.metabolismMask, (int)MetabolismType.Hydrogenotrophy);
        DrawAvailabilityToggle("Sulfur chemosynthesis", ref availability.metabolismMask, (int)MetabolismType.SulfurChemosynthesis);
        DrawAvailabilityToggle("Fermentation", ref availability.metabolismMask, (int)MetabolismType.Fermentation);
        DrawAvailabilityToggle("Oxygenic photosynthesis", ref availability.metabolismMask, (int)MetabolismType.Photosynthesis);
        DrawAvailabilityToggle("Aerobic saprotrophy", ref availability.metabolismMask, (int)MetabolismType.Saprotrophy);
        DrawAvailabilityToggle("Methanogenesis", ref availability.metabolismMask, (int)MetabolismType.Methanogenesis);
        DrawAvailabilityToggle("Methanotrophy", ref availability.metabolismMask, (int)MetabolismType.Methanotrophy);
        DrawAvailabilityToggle("Predation", ref availability.metabolismMask, (int)MetabolismType.Predation);
        GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Locomotion", setupSectionStyle);
        DrawAvailabilityToggle("Passive drift", ref availability.locomotionMask, (int)LocomotionType.PassiveDrift);
        DrawAvailabilityToggle("Amoeboid", ref availability.locomotionMask, (int)LocomotionType.Amoeboid);
        DrawAvailabilityToggle("Flagellar", ref availability.locomotionMask, (int)LocomotionType.Flagellum);
        DrawAvailabilityToggle("Anchored / sessile", ref availability.locomotionMask, (int)LocomotionType.Anchored);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        SetupHelp("Hydrogenotrophy, sulfur chemosynthesis, fermentation and oxygenic photosynthesis can arise in Geodesic mode. The other toggles save availability for future transitions.");
        SetupHelp(availability.FounderStatus);
    }

    private void DrawAvailabilityToggle(string label, ref int mask, int typeIndex)
    {
        int bit = 1 << typeIndex;
        bool available = (mask & bit) != 0;
        SetupBool(label, ref available);
        mask = available ? mask | bit : mask & ~bit;
    }

    private void DrawMetabolicEvolution(SimulationStartupBiologyConfig b)
    {
        SetupSection("Metabolic evolution");
        SetupFloat("General metabolism mutation chance", ref b.metabolismMutationChance, 0f, 1f);
        if (IsGeodesicSetup) SetupFloat("Hydrogen → photosynthesis probability", ref b.hydrogenToPhotosynthesisMutationChance, 0f, 1f);
        SetupFloat("Hydrogen → sulfur chemosynthesis probability", ref b.hydrogenToSulfurMutationChance, 0f, 1f);
        SetupFloat("Hydrogen → fermentation probability", ref b.hydrogenToFermentationMutationChance, 0f, 1f);
        SetupHelp(IsGeodesicSetup
            ? "After the general roll, photosynthesis can arise in a lit, CO2-bearing habitat within its thermal tolerance once the evolution unlock is reached. If photosynthesis is not acquired, a selected sulfur attempt retains priority over fermentation even if the sulfur habitat gate fails. Inherited carbon can satisfy fermentation eligibility."
            : "For Hydrogen → Sulfur or Fermentation, general metabolism mutation chance is checked first, followed by the ordered conditional rolls. Earlier transition checks, unlocks and local resource requirements still apply; other legacy transitions retain their existing ordering and gates.");

        SetupSection("Other metabolic transitions");
        if (IsGeodesicSetup) SetupHelp("Not yet implemented in geodesic evolution. These settings are inactive here and retained for Legacy Cube Sphere.");
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !IsGeodesicSetup;
        if (!IsGeodesicSetup) SetupFloat("Hydrogen → photosynthesis probability", ref b.hydrogenToPhotosynthesisMutationChance, 0f, 1f);
        SetupFloat("Hydrogen → saprotrophy probability", ref b.hydrogenToSaprotrophyMutationChance, 0f, 1f);
        SetupFloat("Fermentation → methanogenesis probability", ref b.fermentationToMethanogenesisMutationChance, 0f, 1f);
        SetupFloat("Hydrogen → methanotrophy probability", ref b.hydrogenToMethanotrophyMutationChance, 0f, 1f);
        SetupFloat("Saprotrophy mutation chance", ref b.saprotrophyMutationChance, 0f, 1f);
        SetupFloat("Predator mutation chance", ref b.predatorMutationChance, 0f, 1f);
        GUI.enabled = enabled;

        SetupSection("Biomass mutation");
        if (IsGeodesicSetup) SetupHelp("Not yet implemented in geodesic biology. Offspring inherit their parent's biomass target; these mutation settings apply only to Legacy Cube Sphere.");
        GUI.enabled = enabled && !IsGeodesicSetup;
        SetupFloat("Biomass mutation chance", ref b.biomassMutationChance, 0f, 1f);
        SetupFloat("Biomass mutation scale", ref b.biomassMutationScale);
        GUI.enabled = enabled;
        if (!IsGeodesicSetup) SetupHelp("Legacy metabolism-changing births use the destination template; same-metabolism births inherit the parent's thermal traits, then mutate them.");
    }

    private void DrawMetabolismParameters(SimulationStartupBiologyConfig b)
    {
        selectedMetabolism = GUILayout.SelectionGrid(selectedMetabolism, MetabolismNames, 2, buttonStyle, GUILayout.MinHeight(112f));
        bool supported = selectedMetabolism != 6 && selectedMetabolism != 7;
        if (IsGeodesicSetup && !supported)
        {
            SetupHelp("This reaction is not implemented by geodesic biology. Its parameters remain available in Legacy Cube Sphere. No new reaction or mutation path is added by setup.");
            return;
        }
        SetupHelp(IsGeodesicSetup
            ? "Supported geodesic reaction parameters. Per-tick quantities refer to a completed 0.5 simulated-second reference reaction tick; actual uptake remains resource-limited. Normal geodesic founders are hydrogenotrophs."
            : "Legacy reaction quantities use the manager's existing metabolism tick. Fermentation, methanogenesis and methanotrophy energy parameters scale with the catabolized share of substrate. Energy per tick, per CO2 and per carbon unit remain distinct.");
        switch (selectedMetabolism)
        {
            case 0:
                SetupFloat("H2 consumed / reaction tick", ref b.hydrogenotrophyH2PerTick);
                SetupFloat("CO2 consumed / reaction tick", ref b.hydrogenotrophyCO2PerTick);
                SetupFloat("Energy / completed reaction tick", ref b.hydrogenotrophyEnergyPerTick);
                SetupFloat(IsGeodesicSetup ? "Whole-reaction activity fraction" : "Consumed CO2 stored as organic carbon (fraction)", ref b.hydrogenotrophyStoreFraction, 0f, 1f);
                if (IsGeodesicSetup) SetupHelp("Scales CO2/H2 uptake, energy and carbon fixation together. All withdrawn CO2 becomes stored carbon; available store capacity limits the reaction.");
                break;
            case 1:
                SetupFloat("CO2 consumed / reaction tick", ref b.chemosynthesisCo2NeedPerTick);
                SetupFloat("H2S consumed / reaction tick", ref b.chemosynthesisH2sNeedPerTick);
                SetupFloat("Energy / completed reaction tick", ref b.chemosynthesisEnergyPerTick);
                if (!IsGeodesicSetup)
                {
                    SetupFloat("Consumed CO2 stored as carbon (fraction)", ref b.chemosynthStoreFraction, 0f, 1f);
                    SetupFloat("Fallback store respiration carbon / tick", ref b.chemoRespirationCPerTick);
                }
                else SetupHelp("Geodesic sulfur uses its configured whole-reaction activity fraction, with all withdrawn CO2 stored within available capacity.");
                break;
            case 2:
                SetupFloat("Organic carbon consumed / tick", ref b.fermentationOrganicCPerTick);
                SetupFloat("Energy parameter / reaction tick", ref b.fermentationEnergyPerTick);
                SetupFloat("Carbon assimilation fraction", ref b.fermentationAssimilationFraction, 0f, 1f);
                if (IsGeodesicSetup) SetupHelp("Dissolved OrganicC feeds fermentation. Unstored carbon returns as equal CO2 and H2 inventory; energy scales with the fermented portion. Inherited stored carbon permits mutation eligibility but is not consumed as food.");
                break;
            case 3:
                SetupFloat("H2 consumed / reaction tick", ref b.methanogenesisH2PerTick);
                SetupFloat("CO2 consumed / reaction tick", ref b.methanogenesisCO2PerTick);
                SetupFloat("Energy parameter / completed reaction tick", ref b.methanogenesisEnergyPerTick);
                if (!IsGeodesicSetup) SetupFloat("Carbon assimilation fraction", ref b.methanogenesisAssimilationFraction, 0f, 1f);
                else SetupHelp("Geodesic uses the existing fixed 0.15 assimilation fraction and 0.85 energy multiplier. Editing the legacy assimilation fraction would not affect this reaction.");
                break;
            case 4:
                SetupFloat("CH4 consumed / reaction tick", ref b.methanotrophyCH4PerTick);
                SetupFloat("O2 consumed / reaction tick", ref b.methanotrophyO2PerTick);
                SetupFloat("Energy parameter / reaction tick", ref b.methanotrophyEnergyPerTick);
                if (!IsGeodesicSetup) SetupFloat("Carbon assimilation fraction", ref b.methanotrophyAssimilationFraction, 0f, 1f);
                else SetupHelp("Geodesic retains its fixed 0.15 assimilation fraction; the legacy assimilation parameter is not used.");
                break;
            case 5:
                SetupFloat("CO2 consumed / tick at full light", ref b.photosynthesisCo2PerTickAtFullInsolation);
                SetupFloat("Energy / unit CO2 consumed", ref b.photosynthesisEnergyPerCo2);
                if (!IsGeodesicSetup)
                {
                    SetupFloat("Photosynthetic carbon store fraction", ref b.photosynthStoreFraction);
                    SetupFloat("Night respiration carbon / tick", ref b.nightRespirationCPerTick);
                }
                else SetupHelp("Geodesic converts withdrawn CO2 into stored carbon with equal O2 output. Light and available store capacity limit the reaction. In darkness, organisms draw down existing energy for maintenance.");
                break;
            case 6:
                SetupFloat("Organic carbon intake / tick", ref b.saproCPerTick);
                SetupFloat("Carbon assimilation fraction", ref b.saproAssimilationFraction, 0f, 1f);
                SetupFloat("Fallback store respiration carbon / tick", ref b.saproRespireStoreCPerTick);
                break;
            case 7:
                SetupFloat("Organic carbon / bite", ref b.predatorBiteOrganicC);
                SetupFloat("Maximum prey energy taken / bite", ref b.predatorBiteEnergy);
                SetupFloat("Carbon assimilation fraction", ref b.predatorAssimilationFraction, 0f, 1f);
                SetupHelp("Predation takes prey energy only when carbon intake falls short, and transfers the existing 50% of that energy to the predator. Predator enablement, motility and attack gates remain on the manager; predatorEnergyPerC is not consumed by the current implementation.");
                break;
        }
        SetupFloat("Maximum organic carbon store", ref b.maxOrganicCStore);
        if (!IsGeodesicSetup)
        {
            SetupHelp("Shared aerobic yields below drive saprotrophy and stored-carbon respiration, including photosynthetic nights. The separate saprotrophy and night energy/O2 Inspector fields are not consumed by the current implementation.");
            SetupFloat("Aerobic respiration O2 / carbon", ref b.aerobicO2PerC);
            SetupFloat("Aerobic respiration energy / carbon", ref b.aerobicEnergyPerC);
        }
    }

    private void DrawAdvancedTab()
    {
        DrawVentVisualEffects();
        if (IsGeodesicSetup)
        {
            SetupSection("Geodesic geometry and sea level");
            SetupInt("Geodesic subdivision", ref currentConfig.geodesicSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
            currentConfig.geodesicSeaLevelControlMode = (GeodesicSeaLevelControlMode)GUILayout.SelectionGrid(
                (int)currentConfig.geodesicSeaLevelControlMode, SeaLevelNames, 3, buttonStyle, GUILayout.Height(32f));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && currentConfig.geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset;
            SetupFloat("Manual sea-level offset (planet units)", ref currentConfig.geodesicSeaLevelOffset, -float.MaxValue);
            GUI.enabled = enabled && currentConfig.geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld;
            SetupFloat("Ocean World minimum depth (planet units)", ref currentConfig.geodesicOceanWorldMinimumDepth);
            GUI.enabled = enabled;
            SetupHelp("Manual offset preserves the configured sea-level radius offset. Target area coverage uses the percentage in Planet. Ocean World covers the highest solid point by the configured minimum depth.");
            SetupSection("Terrain and hydrology");
            SetupFloat("Inland elevation magnitude", ref currentConfig.inlandElevationMagnitude, 0f, GeodesicInlandUplift.MaximumMagnitude);
            SetupHelp(GeodesicInlandUplift.Help);
            GUILayout.Label("Hydrology terrain input", setupSectionStyle);
            currentConfig.hydrologyTerrainMode = (HydrologyTerrainMode)GUILayout.SelectionGrid(
                (int)currentConfig.hydrologyTerrainMode, HydrologyNames, 1, buttonStyle, GUILayout.MinHeight(90f));
            SetupBool("Enable termination valley carving", ref currentConfig.enableTerminationValleyCarving);
            GUI.enabled = enabled && currentConfig.enableTerminationValleyCarving;
            SetupInt("Valley carving passes", ref currentConfig.valleyCarvingPasses, 1, 5);
            SetupFloat("Maximum carve depth (planet units)", ref currentConfig.maximumValleyCarveDepth, 0f, 1f);
            SetupFloat("Minimum river runoff for carving", ref currentConfig.minimumValleyCarveRunoff, 0f, 1000000f);
            SetupFloat("Carve width (planet units)", ref currentConfig.valleyCarveWidth, 0f, 1f);
            GUI.enabled = enabled;
            SetupHelp(GeodesicValleyCarving.Help);
            SetupSection("Basin infill");
            GUI.enabled = enabled && currentConfig.excludeSmallDisconnectedSeas;
            SetupBool("Infill excluded inland basins (experimental)", ref currentConfig.infillExcludedInlandBasins);
            GUI.enabled = enabled && currentConfig.excludeSmallDisconnectedSeas && currentConfig.infillExcludedInlandBasins;
            var limits = currentConfig.basinInfillLimits;
            SetupPercent("Infill maximum affected area (% of planet)", ref limits.totalArea, GeodesicBasinInfillLimits.MaximumTotalArea * 100f);
            SetupPercent("Infill maximum local lift (% of radius)", ref limits.liftFraction, GeodesicBasinInfillLimits.MaximumLiftFraction * 100f);
            SetupPercent("Infill maximum new slope (%)", ref limits.transitionGrade, GeodesicBasinInfillLimits.MaximumGrade * 100f);
            currentConfig.basinInfillLimits = limits;
            GUI.enabled = enabled;
            SetupHelp("Requires inland-sea exclusion in Planet, and a render subdivision at least as high as the simulation subdivision.");
            SetupSection("Environment timing");
            SetupPreset("Temperature update interval (sim s)", ref currentConfig.approximateThermalIntervalSeconds, ApproximateThermalIntervalPresets);
            SetupHelp("ApproximateEcologicalProfiles only. Lower intervals update more often at higher CPU cost. Recommended: 2 s.");
            SetupPreset("Resource transport interval (sim s)", ref currentConfig.geodesicResourceTransportIntervalSeconds, ResourceTransportIntervalPresets);
            SetupHelp("Lower intervals resolve transport more frequently at higher CPU cost. Recommended: 5 s.");
            SetupFloat("Chemistry telemetry interval (sim s; ≤0 off)", ref currentConfig.chemistryTelemetryIntervalSimSeconds, -1f, 3600f);
            SetupSection("Atmosphere inventory");
            SetupFloat("Atmosphere inventory / bar (authoring units)", ref currentConfig.atmosphereInventoryPerBar, 0.000001f, 1000000f);
            SetupHelp("Simulation inventory authoring units represented by one bar, before physical-volume scaling; not an Earth constant.");
        }
        else SetupHelp("Geodesic subdivision, hydrology, lake/terrain reconstruction, finite atmosphere and environment timing controls apply only to Geodesic Icosphere. Existing Inspector terrain settings remain available.");
        GUILayout.Space(8f);
        if (GUILayout.Button("Reset Advanced to Defaults", buttonStyle, GUILayout.Height(32f))) ResetAdvancedToDefaults();
        SetupHelp("Reset Advanced includes vent visuals, base temperature, terrestrial vent fraction, air–sea exchange, prebiotic delay, and the advanced planet settings, even where their controls live in other tabs.");
    }

    private void DrawDebugTab()
    {
        if (!IsGeodesicSetup)
        {
            SetupHelp("The current startup diagnostics apply to Geodesic Icosphere.");
            return;
        }
        SetupSection("River diagnostics");
        SetupBool("Colour rivers by ultimate destination", ref currentConfig.colourRiversByUltimateDestination);
        SetupHelp("Blue: ocean; red: closed inland basin; purple: terminal lake; orange: unresolved.");
        SetupBool("Show rejected / uphill river segments", ref currentConfig.showRejectedUphillRiverSegments);
        SetupHelp("Omitted uphill reaches are shown from yellow through orange to red. These do not become valid routes.");
        SetupBool("Colour rendered rivers by visible-terrain grade", ref currentConfig.colourRenderedRiversByVisibleTerrainGrade);
        SetupHelp("Audits the completed visible terrain. Downhill retains its normal colour; uphill segments are yellow, orange or red. Routing is unchanged.");
        SetupBool("Show river termination reasons", ref currentConfig.showRiverTerminationReasons);
        SetupHelp("Purple: declared closed basin. White: unexpected routing failure. No lakes are created.");
        SetupBool("Show valley carving debug (Scene gizmos)", ref currentConfig.showValleyCarvingDebug);
        SetupSection("Geodesic chemotaxis A/B");
        SetupBool("Enable geodesic chemotactic steering", ref currentConfig.biology.enableGeodesicChemotacticSteering);
        SetupHelp("These saved startup values are applied to the manager before initialization, overriding its scene Inspector defaults. A/B diagnostics observe the selected steering mode: steering on is A; steering off is B.");
        SetupHelp("Uses the existing manager switches and diagnostic implementation. No steering, sensing or diagnostic algorithms are changed by setup.");
    }

    private void SetupSection(string title)
    {
        GUILayout.Space(10f);
        GUILayout.Label(title, setupSectionStyle);
        GUILayout.Space(3f);
    }

    private void SetupHelp(string text) => GUILayout.Label(text, setupHelpStyle);

    private void SetupBool(string label, ref bool value)
    {
        value = GUILayout.Toggle(value, label, GUILayout.MinHeight(26f));
    }

    private void DrawTargetCoverageToggle()
    {
        bool target = currentConfig.geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.TargetAreaCoverage;
        bool next = GUILayout.Toggle(target, "Use target ocean coverage", GUILayout.MinHeight(26f));
        if (target != next) currentConfig.geodesicSeaLevelControlMode = next
            ? GeodesicSeaLevelControlMode.TargetAreaCoverage : GeodesicSeaLevelControlMode.ManualOffset;
    }

    private bool SetupFloat(string label, ref float value, float min = 0f, float max = float.MaxValue, bool slider = false, string tooltip = null)
    {
        GUILayout.BeginHorizontal();
        if (!setupLabelContents.TryGetValue(label, out var content))
        {
            string bounds = max != float.MaxValue ? $"Range: {min:G6} to {max:G6}."
                : min == -float.MaxValue ? "Finite number." : $"Minimum: {min:G6}.";
            content = new GUIContent(label, tooltip ?? bounds + " Decimal point or comma accepted.");
            setupLabelContents.Add(label, content);
        }
        GUILayout.Label(content, setupHelpStyle, GUILayout.MinHeight(28f), GUILayout.ExpandWidth(true));
        bool changed = false;
        if (slider)
        {
            float moved = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(150f));
            if (moved != value) { value = moved; numericFieldEdits.Remove(label); changed = true; }
        }
        changed |= SetupNumber(label, ref value, min, max);
        GUILayout.EndHorizontal();
        return changed;
    }

    private bool SetupNumber(string id, ref float value, float min, float max)
    {
        bool focused = GUI.GetNameOfFocusedControl() == id;
        if (!numericFieldEdits.TryGetValue(id, out var edit) || edit.value != value)
        {
            edit = new SetupNumericEdit(value);
            numericFieldEdits[id] = edit;
        }
        string text = focused ? edit.text : edit.formatted;
        GUI.SetNextControlName(id);
        string next = GUILayout.TextField(text, GUILayout.Width(126f), GUILayout.Height(28f));
        float previous = value;
        if (next != text && TryParseSetupFloat(next, out float parsed)) value = Mathf.Clamp(parsed, min, max);
        if (previous != value)
        {
            edit.value = value;
            edit.formatted = value.ToString("G9", CultureInfo.InvariantCulture);
        }
        edit.text = next;
        return value != previous;
    }

    internal static bool TryParseSetupFloat(string text, out float value) =>
        float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private void SetupInt(string label, ref int value, int min, int max, bool enabled = true)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && enabled;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, setupHelpStyle, GUILayout.MinHeight(28f), GUILayout.ExpandWidth(true));
        // Integers have a separate text buffer to preserve a sign or an empty field.
        string id = label;
        bool focused = GUI.GetNameOfFocusedControl() == id;
        string text = focused && integerFieldEdits.TryGetValue(id, out var edit) && edit.value == value
            ? edit.text : value.ToString(CultureInfo.InvariantCulture);
        GUI.SetNextControlName(id);
        string next = GUILayout.TextField(text, GUILayout.Width(126f), GUILayout.Height(28f));
        if (int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) value = Mathf.Clamp(parsed, min, max);
        integerFieldEdits[id] = (value, next);
        GUILayout.EndHorizontal();
        GUI.enabled = previousEnabled;
    }
    private readonly System.Collections.Generic.Dictionary<string, (int value, string text)> integerFieldEdits = new();

    private void SetupPercent(string label, ref float fraction, float maximumPercent)
    {
        float percent = fraction * 100f;
        if (SetupFloat(label, ref percent, 0f, maximumPercent)) fraction = percent / 100f;
    }

    private void SetupCelsius(string label, ref float kelvin)
    {
        float celsius = kelvin - 273.15f;
        // Never convert unchanged values back to Kelvin: untouched floats retain all bits.
        if (SetupFloat(label, ref celsius, -273.15f)) kelvin = (float)((double)celsius + 273.15);
    }

    private void SetupTemperatureRange(string metabolism, ref Vector2 range, bool editable)
    {
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && editable;
        GUILayout.BeginHorizontal();
        GUILayout.Label(metabolism + (editable ? " (°C)" : " (legacy template, °C)"), setupHelpStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label("Min", GUILayout.Width(28f));
        float min = range.x - 273.15f, max = range.y - 273.15f;
        string minId = metabolism + ":tempMin", maxId = metabolism + ":tempMax";
        if (SetupNumber(minId, ref min, -123.15f, 226.85f)) range.x = (float)((double)min + 273.15);
        GUILayout.Label("Max", GUILayout.Width(28f));
        if (SetupNumber(maxId, ref max, -123.15f, 226.85f)) range.y = (float)((double)max + 273.15);
        GUILayout.EndHorizontal();
        string focus = GUI.GetNameOfFocusedControl();
        if (range.x > range.y)
        {
            if (focus != minId && focus != maxId) range = new Vector2(range.y, range.x);
            else SetupHelp("Minimum exceeds maximum. The endpoints will be ordered after editing.");
        }
        GUI.enabled = enabled;
    }

    private void SetupPreset(string label, ref float value, float[] presets)
    {
        GUILayout.Label(label, setupHelpStyle);
        GUILayout.BeginHorizontal();
        foreach (float preset in presets)
            if (GUILayout.Button(preset.ToString("G3", CultureInfo.InvariantCulture) + " s", value == preset ? selectedSetupTabStyle : setupTabStyle, GUILayout.Height(28f))) value = preset;
        GUILayout.EndHorizontal();
    }
}
