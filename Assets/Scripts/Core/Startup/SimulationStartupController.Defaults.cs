using System;
using UnityEngine;

public partial class SimulationStartupController
{
    private void CaptureAdditionalSceneDefaults()
    {
        if (planetGenerator == null) return;
        defaults.geodesicSeaLevelControlMode = planetGenerator.geodesicSeaLevelControlMode;
        defaults.geodesicTargetOceanCoveragePercent = planetGenerator.geodesicTargetOceanCoveragePercent;
        defaults.geodesicSeaLevelOffset = planetGenerator.geodesicSeaLevelOffset;
        defaults.geodesicOceanWorldMinimumDepth = planetGenerator.geodesicOceanWorldMinimumDepth;
        defaults.enableGeodesicRivers = planetGenerator.enableGeodesicRivers;
        defaults.excludeSmallDisconnectedSeas = planetGenerator.excludeSmallDisconnectedSeas;
        defaults.minimumOceanComponentAreaFraction = planetGenerator.minimumOceanComponentAreaFraction;
        defaults.hydrologyTerrainMode = planetGenerator.hydrologyTerrainMode;
        defaults.infillExcludedInlandBasins = planetGenerator.infillExcludedInlandBasins;
        defaults.basinInfillLimits = planetGenerator.basinInfillLimits;
        defaults.inlandElevationMagnitude = planetGenerator.inlandElevationMagnitude;
        defaults.enableTerminationValleyCarving = planetGenerator.enableTerminationValleyCarving;
        defaults.valleyCarvingPasses = planetGenerator.valleyCarvingPasses;
        defaults.maximumValleyCarveDepth = planetGenerator.maximumValleyCarveDepth;
        defaults.minimumValleyCarveRunoff = planetGenerator.minimumValleyCarveRunoff;
        defaults.valleyCarveWidth = planetGenerator.valleyCarveWidth;
        defaults.showValleyCarvingDebug = planetGenerator.showValleyCarvingDebug;
        defaults.generateHydrologicalLakes = planetGenerator.generateHydrologicalLakes;
        defaults.minimumLakeBasinAreaFraction = planetGenerator.minimumLakeBasinAreaFraction;
        defaults.minimumLakeDepth = planetGenerator.minimumLakeDepth;

        var rivers = planetGenerator.GetComponent<GeodesicRiverSystem>();
        if (rivers != null)
        {
            defaults.riverFlowThreshold = rivers.riverFlowThreshold;
            defaults.majorRiverThreshold = rivers.majorRiverThreshold;
            defaults.colourRiversByUltimateDestination = rivers.ColourRiversByUltimateDestination;
            defaults.showRejectedUphillRiverSegments = rivers.showRejectedUphillRiverSegments;
            defaults.colourRenderedRiversByVisibleTerrainGrade = rivers.colourRenderedRiversByVisibleTerrainGrade;
            defaults.showRiverTerminationReasons = rivers.showRiverTerminationReasons;
        }
        // Snapshot scene settings once, before deferred startup clears runtime state.
        planetGenerator.GetComponent<GeodesicOceanResourceField>()?.CaptureStartupDefaults(defaults,
            defaults.gridType == PlanetGridType.GeodesicIcosphere);
        var telemetry = planetGenerator.GetComponent<GeodesicChemistryTelemetry>();
        if (telemetry != null) defaults.chemistryTelemetryIntervalSimSeconds = telemetry.IntervalSimSeconds;
        var atmosphere = planetGenerator.GetComponent<GeodesicAtmosphereField>();
        if (atmosphere != null) defaults.atmosphereInventoryPerBar = (float)atmosphere.AtmosphereInventoryPerBarAuthoring;
    }

    private static void NormalizeAdditionalSettings(SimulationStartupConfig config, SimulationStartupConfig fallback)
    {
        fallback ??= new SimulationStartupConfig();
        config.ventVisuals = config.ventVisuals.Normalized();
        config.performanceSummaryIntervalSeconds = SimulationPerformanceAnalyzer.NormalizeSummaryInterval(config.performanceSummaryIntervalSeconds);
        if (!Enum.IsDefined(typeof(GeodesicSeaLevelControlMode), config.geodesicSeaLevelControlMode))
            config.geodesicSeaLevelControlMode = fallback.geodesicSeaLevelControlMode;
        config.geodesicTargetOceanCoveragePercent = float.IsFinite(config.geodesicTargetOceanCoveragePercent)
            ? Mathf.Clamp(config.geodesicTargetOceanCoveragePercent, 0f, 100f) : fallback.geodesicTargetOceanCoveragePercent;
        if (!float.IsFinite(config.geodesicSeaLevelOffset)) config.geodesicSeaLevelOffset = fallback.geodesicSeaLevelOffset;
        config.geodesicOceanWorldMinimumDepth = float.IsFinite(config.geodesicOceanWorldMinimumDepth)
            ? Mathf.Max(0f, config.geodesicOceanWorldMinimumDepth) : fallback.geodesicOceanWorldMinimumDepth;
        config.biology ??= (fallback.biology ?? new SimulationStartupBiologyConfig()).Clone();
        config.biology.Normalize(fallback.biology);
    }

    private void SaveSetupDraft()
    {
        if (startupUiState != StartupUiState.ConfigScreen || applyingConfig || !saveStartupConfigOnStart) return;
        if (startupScreenRoot != null)
            foreach (var panel in startupScreenRoot.GetComponentsInChildren<SimulationStartupPanel>(true)) panel.PushToConfig();
        ClampLoadedConfig(currentConfig);
        SaveStartupConfig(currentConfig);
    }

    private void OnApplicationQuit() => SaveSetupDraft();
}
