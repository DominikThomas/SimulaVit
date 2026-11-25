using System;
using System.Collections.Generic;
using Unity.Profiling;

internal static class GeodesicPerformanceMarkers
{
    internal const int SlotSampleStride = 64;
    private const string Prefix = "GeodesicBiology.";

    internal static readonly Stage LifecycleTemperature = new Stage("Lifecycle.TemperatureAndVents", true);
    internal static readonly Stage LifecycleExposure = new Stage("Lifecycle.ThermalExposureAndEligibility", true);
    internal static readonly Stage LifecycleMaintenance = new Stage("Lifecycle.Maintenance", true);
    internal static readonly Stage LifecycleGrowthGate = new Stage("Lifecycle.GrowthAndReproductionEligibility", true);
    internal static readonly Stage LifecycleReproduction = new Stage("Lifecycle.ReproductionAndMutation", true);
    internal static readonly Stage DaughterPlacement = new Stage("Lifecycle.DaughterPlacement", true);
    internal static readonly Stage LifecycleDeath = new Stage("Lifecycle.DeathRecyclingAndCompaction", true);
    internal static readonly Stage ExperimentalFounders = new Stage("FounderLifecycle.Experimental");

    internal static readonly Stage PassiveIntegration = new Stage("PassiveMovement.Integration", true);
    internal static readonly Stage PassiveMapping = new Stage("PassiveMovement.SpatialMapping", true);
    internal static readonly Stage ActiveIntegration = new Stage("ActiveMovement.Integration", true);
    internal static readonly Stage ActiveMapping = new Stage("ActiveMovement.SpatialMapping", true);

    internal static readonly Stage DissolvedTransaction = new Stage("ReactionEvaluation.MeasuredTransaction", true);
    // Indexed by MetabolismType. Predation runs outside the dissolved-resource commit loop.
    internal static readonly Stage[] DissolvedCommits =
    {
        new Stage("ReactionEvaluation.Commit.SulfurChemosynthesis", true),
        new Stage("ReactionEvaluation.Commit.Hydrogenotrophy", true),
        new Stage("ReactionEvaluation.Commit.Photosynthesis", true),
        new Stage("ReactionEvaluation.Commit.Saprotrophy", true),
        default,
        new Stage("ReactionEvaluation.Commit.Fermentation", true),
        new Stage("ReactionEvaluation.Commit.Methanogenesis", true),
        new Stage("ReactionEvaluation.Commit.Methanotrophy", true)
    };

    internal static readonly Stage PredationPass = new Stage("Predation.Capture");
    internal static readonly Stage PredationIndexBuild = new Stage("Predation.IndexBuild");
    internal static readonly Stage PredationSearch = new Stage("Predation.CandidateSearch", true);
    internal static readonly Stage PredationResolution = new Stage("Predation.CaptureResolution", true);

    internal static readonly Stage[] RecordedStages = CreateRecordedStages();

    private static Stage[] CreateRecordedStages()
    {
        var stages = new List<Stage>
        {
            LifecycleTemperature, LifecycleExposure, LifecycleMaintenance, LifecycleGrowthGate,
            LifecycleReproduction, DaughterPlacement, LifecycleDeath, ExperimentalFounders,
            PassiveIntegration, PassiveMapping, ActiveIntegration, ActiveMapping, DissolvedTransaction,
            PredationPass, PredationIndexBuild, PredationSearch, PredationResolution
        };
        foreach (Stage commit in DissolvedCommits)
            if (commit.Name != null) stages.Add(commit);
        return stages.ToArray();
    }

    internal readonly struct Stage
    {
        internal readonly string Name;
        internal readonly bool Sampled;
        private readonly ProfilerMarker marker;

        internal Stage(string name, bool sampled = false)
        {
            Name = Prefix + name;
            Sampled = sampled;
            marker = new ProfilerMarker(Name);
        }

        internal ProfilerMarker.AutoScope Auto() => marker.Auto();
        internal Scope Measure(bool collect) => new Scope(marker, collect);
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly ProfilerMarker marker;
        private readonly bool collect;

        internal Scope(ProfilerMarker marker, bool collect)
        {
            this.marker = marker;
            this.collect = collect;
            if (collect) marker.Begin();
        }

        public void Dispose()
        {
            if (collect) marker.End();
        }
    }
}
