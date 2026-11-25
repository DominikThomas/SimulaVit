public partial class SimulationStartupController
{
    private partial class SavedStartupConfig
    {
        public GeodesicFounderEmergenceMode geodesicFounderEmergenceMode;
        public float geodesicFounderMeanIntervalSeconds = GeodesicFounderEmergenceSchedule.DefaultMeanInterval;
        public bool geodesicFounderOngoingEmergence;
        public float geodesicFounderOngoingMeanIntervalSeconds = GeodesicFounderEmergenceSchedule.DefaultOngoingMeanInterval;

        private void CaptureV24(SimulationStartupConfig config)
        {
            geodesicFounderEmergenceMode = config.geodesicFounderEmergenceMode;
            geodesicFounderMeanIntervalSeconds = config.geodesicFounderMeanIntervalSeconds;
            geodesicFounderOngoingEmergence = config.geodesicFounderOngoingEmergence;
            geodesicFounderOngoingMeanIntervalSeconds = config.geodesicFounderOngoingMeanIntervalSeconds;
        }

        private void RestoreV24(SimulationStartupConfig config)
        {
            // Existing profiles keep their exact batch semantics even if the fallback opts into gradual mode.
            config.geodesicFounderEmergenceMode = version >= 24 && geodesicFounderEmergenceMode == GeodesicFounderEmergenceMode.Gradual ? GeodesicFounderEmergenceMode.Gradual : GeodesicFounderEmergenceMode.Batch;
            config.geodesicFounderMeanIntervalSeconds = GeodesicFounderEmergenceSchedule.NormalizeInterval(geodesicFounderMeanIntervalSeconds);
            config.geodesicFounderOngoingEmergence = version >= 24 && geodesicFounderOngoingEmergence;
            config.geodesicFounderOngoingMeanIntervalSeconds = GeodesicFounderEmergenceSchedule.NormalizeInterval(geodesicFounderOngoingMeanIntervalSeconds,300f);
        }
    }
}
