using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional, read-only geodesic movement inspection. Never writes simulation state.</summary>
[Serializable]
public sealed class ReplicatorMovementDiagnostics
{
    [Flags]
    public enum Anomaly
    {
        None = 0,
        InvalidPosition = 1,
        ExcessAngularMovement = 2,
        UnexpectedCellJump = 4,
        DirectionOutsideHabitat = 8,
        PackedPositionMismatch = 16,
        RenderPositionMismatch = 32,
        RenderDiscontinuity = 64
    }

    [Serializable]
    public struct Sample
    {
        public long organismId;
        public int packedIndex, simulationStep;
        public double simulationTime;
        public float simulationDeltaSeconds, integratedMovementSeconds;
        public int previousCell, cell, previousLayer, layer;
        public bool kinematicDirectionInitialized;
        public Vector3 sphericalDirectionLocal;
        public Vector3 previousAuthoritativeLocalPosition;
        public float authoritativeLayerRadius, visualRadius;
        public Vector3 authoritativeWorldPosition, packedWorldPosition;
        public float angularDisplacementRadians, surfaceDistance, authoritativeLocalDistance;
        public float expectedMaxAngularRadians, expectedMaxSurfaceDistance;
        public float cellCenterAngularDisplacementRadians, packedPositionError;
        public int renderFrame;
        public Vector3 renderedWorldPosition;
        public float renderPositionError, renderedAngularDisplacementRadians;
        public float renderedLocalDistance, expectedMaxRenderAngularRadians;
        public Anomaly anomalies;
        public float score;
    }

    [Serializable]
    public sealed class TrackedOrganism
    {
        public long organismId;
        public bool alive;
        public Sample latest, peak;
        [NonSerialized] internal Queue<Vector3> simulationTrail = new Queue<Vector3>();
        [NonSerialized] internal Queue<Vector3> renderTrail = new Queue<Vector3>();
    }

    private struct Observation
    {
        public Sample sample;
        public bool hasRender;
        public Vector3 lastRenderedLocal;
        public double lastRenderKinematicsTime;
    }

    [SerializeField, Tooltip("Scans changes only when enabled. Keeps detailed samples/trails for a few IDs and throttles their Console output.")]
    private bool enabled;
    [SerializeField, Range(1, 8)] private int maximumTrackedOrganisms = 4;
    [SerializeField, Tooltip("0 follows the largest recorded suspicious change. Enter a tracked ID to pin its trail; IDs survive swap-back removal, but not save/load or a new population.")]
    private long selectedOrganismId;
    [SerializeField] private bool drawSelectedTrail = true;
    [SerializeField, Range(8, 256)] private int trailPointCount = 96;
    [SerializeField] private bool logTrackedOrganisms = true;
    [SerializeField, Min(0.5f)] private float logIntervalRealSeconds = 2f;
    [SerializeField, Min(0.000001f)] private float angularToleranceRadians = 0.00001f;
    [SerializeField, Min(0.000001f)] private float localPositionTolerance = 0.0001f;
    [SerializeField, Tooltip("Live readouts and the peak incident for each retained ID. Coordinates/distances are planet-local unless named World.")]
    private List<TrackedOrganism> trackedOrganisms = new List<TrackedOrganism>();

    [NonSerialized] private readonly Dictionary<long, Observation> observations = new Dictionary<long, Observation>();
    [NonSerialized] private readonly List<long> removedIds = new List<long>();
    [NonSerialized] private double kinematicsTime;
    [NonSerialized] private float nextLogTime;
    [NonSerialized] private Transform planetTransform;

    public bool Enabled => enabled;

    public void Reset()
    {
        observations.Clear();
        trackedOrganisms.Clear();
        removedIds.Clear();
        planetTransform = null;
        kinematicsTime = 0d;
        nextLogTime = 0f;
    }

    public void ObserveSimulationStep(List<Replicator> agents, ReplicatorPopulationState state,
        Transform planet, GeodesicOceanLayerGrid grid, int step, double time, float dt, float integratedSeconds)
    {
        if (!enabled)
        {
            if (observations.Count > 0 || trackedOrganisms.Count > 0) Reset();
            return;
        }
        if (planet == null || grid == null) return;
        planetTransform = planet;
        kinematicsTime += integratedSeconds;
        Matrix4x4 worldToLocal = planet.worldToLocalMatrix;
        Matrix4x4 localToWorld = planet.localToWorldMatrix;
        int limit = Mathf.Clamp(maximumTrackedOrganisms, 1, 8);
        while (trackedOrganisms.Count > limit) trackedOrganisms.RemoveAt(trackedOrganisms.Count - 1);
        foreach (TrackedOrganism track in trackedOrganisms) track.alive = false;

        for (int i = 0; i < state.Count; i++)
        {
            if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
            long id = agents[i].DiagnosticId;
            bool hasPrevious = observations.TryGetValue(id, out Observation previous);
            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
            Vector3 packedLocal = worldToLocal.MultiplyPoint3x4(state.Position[i]);
            Vector3 direction = state.PassiveDriftDirection[i];
            bool initialized = IsFinite(direction) && direction.sqrMagnitude > 0.9f;
            // A child is first observed after lifecycle spawning. Its inherited position is
            // the baseline until movement initializes its separate kinematic fields.
            if (!initialized) direction = packedLocal.normalized;
            bool validHabitat = grid.IsNodeActive(cell, layer);
            float radius = validHabitat ? grid.LayerCenterRadius[grid.GetNodeIndex(cell, layer)] : 0f;
            float visualRadius = state.PassiveVisualRadius[i] > 0f ? state.PassiveVisualRadius[i] : packedLocal.magnitude;
            Sample sample = new Sample
            {
                organismId = id, packedIndex = i, simulationStep = step, simulationTime = time,
                simulationDeltaSeconds = dt, integratedMovementSeconds = integratedSeconds,
                cell = cell, layer = layer,
                previousCell = hasPrevious ? previous.sample.cell : cell,
                previousLayer = hasPrevious ? previous.sample.layer : layer,
                kinematicDirectionInitialized = initialized, sphericalDirectionLocal = direction,
                authoritativeLayerRadius = radius, visualRadius = visualRadius,
                previousAuthoritativeLocalPosition = hasPrevious
                    ? previous.sample.sphericalDirectionLocal * previous.sample.authoritativeLayerRadius : direction * radius,
                authoritativeWorldPosition = localToWorld.MultiplyPoint3x4(direction * radius),
                packedWorldPosition = state.Position[i],
                expectedMaxAngularRadians = GeodesicBiologyRuntime.PassiveAngularSpeedRadiansPerSecond * integratedSeconds,
                renderFrame = hasPrevious ? previous.sample.renderFrame : -1,
                renderedWorldPosition = previous.sample.renderedWorldPosition
            };
            if (!validHabitat || !IsFinite(packedLocal) || !IsFinite(direction)
                || direction.sqrMagnitude < 0.9f || packedLocal.sqrMagnitude < 1e-12f)
                sample.anomalies |= Anomaly.InvalidPosition;

            sample.packedPositionError = (packedLocal - direction * visualRadius).magnitude;
            if (sample.packedPositionError > localPositionTolerance)
                sample.anomalies |= Anomaly.PackedPositionMismatch;
            if (validHabitat && AngularDistance(direction, grid.SourceTopology.CellDirections[cell])
                > MaximumNeighborSpacing(grid.SourceTopology, cell) + angularToleranceRadians)
                sample.anomalies |= Anomaly.DirectionOutsideHabitat;

            if (hasPrevious)
            {
                sample.angularDisplacementRadians = AngularDistance(previous.sample.sphericalDirectionLocal, direction);
                float meanRadius = (previous.sample.authoritativeLayerRadius + radius) * 0.5f;
                sample.surfaceDistance = sample.angularDisplacementRadians * meanRadius;
                sample.expectedMaxSurfaceDistance = sample.expectedMaxAngularRadians * meanRadius;
                sample.authoritativeLocalDistance = (direction * radius
                    - previous.sample.sphericalDirectionLocal * previous.sample.authoritativeLayerRadius).magnitude;
                if (sample.angularDisplacementRadians > sample.expectedMaxAngularRadians + angularToleranceRadians)
                    sample.anomalies |= Anomaly.ExcessAngularMovement;
                if (validHabitat && sample.previousCell >= 0 && sample.previousCell < grid.CellCount)
                {
                    sample.cellCenterAngularDisplacementRadians = AngularDistance(grid.SourceTopology.CellDirections[sample.previousCell],
                        grid.SourceTopology.CellDirections[cell]);
                    // Allow the continuous travel plus a conservative cell-width margin at
                    // both ends. Multiple real neighbour crossings in a catch-up are legal.
                    float allowance = sample.expectedMaxAngularRadians + MaximumNeighborSpacing(grid.SourceTopology, sample.previousCell)
                        + MaximumNeighborSpacing(grid.SourceTopology, cell) + angularToleranceRadians;
                    if (sample.cellCenterAngularDisplacementRadians > allowance)
                        sample.anomalies |= Anomaly.UnexpectedCellJump;
                }
            }
            else sample.expectedMaxSurfaceDistance = sample.expectedMaxAngularRadians * radius;

            sample.score = CalculateScore(sample);
            previous.sample = sample;
            observations[id] = previous;
            Retain(sample, direction * radius, false, sample.previousAuthoritativeLocalPosition);
        }

        removedIds.Clear();
        foreach (KeyValuePair<long, Observation> pair in observations)
            if (pair.Value.sample.simulationStep != step) removedIds.Add(pair.Key);
        foreach (long id in removedIds) observations.Remove(id);
    }

    public void LogIfDue(UnityEngine.Object context)
    {
        if (enabled && logTrackedOrganisms && trackedOrganisms.Count > 0 && Time.realtimeSinceStartup >= nextLogTime)
        {
            nextLogTime = Time.realtimeSinceStartup + Mathf.Max(0.5f, logIntervalRealSeconds);
            foreach (TrackedOrganism track in trackedOrganisms)
            {
                Debug.Log($"[ReplicatorMovement] alive={track.alive} {Format(track.latest)} peak=({Format(track.peak)})", context);
            }
        }
    }

    // Called with the translation of the matrix actually submitted to DrawMeshInstanced.
    // This measures the CPU submission, not whether a GPU fragment survives depth/coverage.
    public void ObserveRenderedPosition(Replicator agent, Vector3 renderedWorldPosition)
    {
        if (!enabled || planetTransform == null || !observations.TryGetValue(agent.DiagnosticId, out Observation observation)) return;
        Sample sample = observation.sample;
        Vector3 local = planetTransform.InverseTransformPoint(renderedWorldPosition);
        Vector3 previousRenderedLocal = observation.hasRender ? observation.lastRenderedLocal : local;
        sample.renderFrame = Time.frameCount;
        sample.renderedWorldPosition = renderedWorldPosition;
        sample.renderPositionError = Vector3.Distance(renderedWorldPosition, sample.packedWorldPosition);
        float localError = Vector3.Distance(local, planetTransform.InverseTransformPoint(sample.packedWorldPosition));
        if (!IsFinite(local) || local.sqrMagnitude < 1e-12f) sample.anomalies |= Anomaly.InvalidPosition;
        if (localError > localPositionTolerance) sample.anomalies |= Anomaly.RenderPositionMismatch;
        if (observation.hasRender)
        {
            sample.renderedAngularDisplacementRadians = AngularDistance(observation.lastRenderedLocal.normalized, local.normalized);
            sample.renderedLocalDistance = Vector3.Distance(observation.lastRenderedLocal, local);
            sample.expectedMaxRenderAngularRadians = GeodesicBiologyRuntime.PassiveAngularSpeedRadiansPerSecond
                * (float)(kinematicsTime - observation.lastRenderKinematicsTime);
            if (sample.renderedAngularDisplacementRadians > sample.expectedMaxRenderAngularRadians + angularToleranceRadians)
                sample.anomalies |= Anomaly.RenderDiscontinuity;
        }
        sample.score = CalculateScore(sample);
        observation.sample = sample;
        observation.hasRender = true;
        observation.lastRenderedLocal = local;
        observation.lastRenderKinematicsTime = kinematicsTime;
        observations[agent.DiagnosticId] = observation;
        Retain(sample, local, true, previousRenderedLocal);
    }

    private float CalculateScore(Sample sample)
    {
        float score = Mathf.Max(sample.angularDisplacementRadians / (sample.expectedMaxAngularRadians + angularToleranceRadians),
            sample.renderedAngularDisplacementRadians / (sample.expectedMaxRenderAngularRadians + angularToleranceRadians));
        if (sample.anomalies != Anomaly.None) score += 1000f;
        return float.IsFinite(score) ? score : 1000000f;
    }

    private void Retain(Sample sample, Vector3 localTrailPoint, bool rendered, Vector3 previousTrailPoint)
    {
        TrackedOrganism track = null;
        foreach (TrackedOrganism item in trackedOrganisms)
            if (item.organismId == sample.organismId) { track = item; break; }
        if (track == null)
        {
            if (trackedOrganisms.Count >= Mathf.Clamp(maximumTrackedOrganisms, 1, 8))
            {
                TrackedOrganism weakest = null;
                foreach (TrackedOrganism item in trackedOrganisms)
                    if (item.organismId != selectedOrganismId && (weakest == null || item.peak.score < weakest.peak.score)) weakest = item;
                if (weakest == null || (sample.organismId != selectedOrganismId && sample.score <= weakest.peak.score)) return;
                trackedOrganisms.Remove(weakest);
            }
            track = new TrackedOrganism { organismId = sample.organismId, peak = sample };
            trackedOrganisms.Add(track);
            // Preserve both endpoints when an untracked organism triggers an incident.
            AppendTrail(track.simulationTrail, sample.previousAuthoritativeLocalPosition);
            if (rendered)
            {
                AppendTrail(track.simulationTrail, sample.sphericalDirectionLocal * sample.authoritativeLayerRadius);
                AppendTrail(track.renderTrail, previousTrailPoint);
            }
        }
        track.alive = true;
        track.latest = sample;
        if (sample.score >= track.peak.score) track.peak = sample;
        if (rendered) AppendTrail(track.renderTrail, localTrailPoint);
        else AppendTrail(track.simulationTrail, localTrailPoint);
    }

    private void AppendTrail(Queue<Vector3> trail, Vector3 point)
    {
        if (!IsFinite(point)) return;
        trail.Enqueue(point);
        while (trail.Count > Mathf.Clamp(trailPointCount, 8, 256)) trail.Dequeue();
    }

    private static float AngularDistance(Vector3 a, Vector3 b)
    {
        // atan2 remains accurate for the tiny movements where float acos(dot) rounds to zero.
        return Mathf.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b));
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static float MaximumNeighborSpacing(GeodesicGridTopology topology, int cell)
    {
        float maximum = 0f;
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            maximum = Mathf.Max(maximum, topology.NeighborAngularDistances6[cell * 6 + slot]);
        return maximum;
    }

    private static string Format(Sample s)
    {
        return $"id={s.organismId} index={s.packedIndex} step={s.simulationStep} t={s.simulationTime:F3} "
            + $"cell={s.previousCell}->{s.cell} layer={s.previousLayer}->{s.layer} initialized={s.kinematicDirectionInitialized} "
            + $"directionLocal={s.sphericalDirectionLocal.ToString("F7")} layerRadius={s.authoritativeLayerRadius:G8} visualRadius={s.visualRadius:G8} "
            + $"authoritativeWorld={s.authoritativeWorldPosition.ToString("F7")} packedWorld={s.packedWorldPosition.ToString("F7")} "
            + $"previousAuthoritativeLocal={s.previousAuthoritativeLocalPosition.ToString("F7")} "
            + $"dt={s.simulationDeltaSeconds:G6} integratedDt={s.integratedMovementSeconds:G6} "
            + $"angleRad={s.angularDisplacementRadians:G6}/{s.expectedMaxAngularRadians:G6} surfaceDistance={s.surfaceDistance:G6}/{s.expectedMaxSurfaceDistance:G6} "
            + $"totalLocalDistance={s.authoritativeLocalDistance:G6} cellAngleRad={s.cellCenterAngularDisplacementRadians:G6} packedError={s.packedPositionError:G6} "
            + $"renderFrame={s.renderFrame} renderedWorld={s.renderedWorldPosition.ToString("F7")} renderErrorWorld={s.renderPositionError:G6} "
            + $"renderAngleRad={s.renderedAngularDisplacementRadians:G6}/{s.expectedMaxRenderAngularRadians:G6} renderDistanceLocal={s.renderedLocalDistance:G6} flags={s.anomalies}";
    }

    public void DrawSelectedTrail()
    {
        if (!enabled || !drawSelectedTrail || planetTransform == null) return;
        TrackedOrganism selected = FindSelected();
        if (selected == null) return;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = planetTransform.localToWorldMatrix;
        DrawTrail(selected.simulationTrail, Color.green);
        DrawTrail(selected.renderTrail, Color.magenta);
        Sample sample = selected.latest;
        Vector3 authoritative = sample.sphericalDirectionLocal * sample.authoritativeLayerRadius;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(authoritative, 0.025f);
        if (sample.renderFrame >= 0)
        {
            Vector3 rendered = planetTransform.InverseTransformPoint(sample.renderedWorldPosition);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(rendered, 0.035f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(authoritative, rendered);
        }
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(planetTransform.TransformPoint(authoritative),
            $"ID {sample.organismId} (index {sample.packedIndex}) cell {sample.cell} / layer {sample.layer}\n{sample.anomalies} alive={selected.alive}");
#endif
    }

    private TrackedOrganism FindSelected()
    {
        TrackedOrganism selected = null;
        foreach (TrackedOrganism track in trackedOrganisms)
        {
            if (selectedOrganismId != 0)
            {
                if (track.organismId == selectedOrganismId) { selected = track; break; }
            }
            else if (selected == null || (track.alive && !selected.alive)
                || (track.alive == selected.alive && track.peak.score > selected.peak.score)) selected = track;
        }
        return selected;
    }

    public void FrameSelectedOrganism()
    {
#if UNITY_EDITOR
        TrackedOrganism selected = FindSelected();
        if (!enabled || selected == null || planetTransform == null) return;
        Vector3 local = selected.latest.sphericalDirectionLocal * selected.latest.authoritativeLayerRadius;
        UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(planetTransform.TransformPoint(local), view.rotation, 0.4f);
#endif
    }

    private static void DrawTrail(Queue<Vector3> trail, Color color)
    {
        Gizmos.color = color;
        bool first = true;
        Vector3 previous = default;
        foreach (Vector3 point in trail)
        {
            if (!first) Gizmos.DrawLine(previous, point);
            previous = point;
            first = false;
        }
    }
}
