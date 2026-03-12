using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Profiling;

public class ReplicatorMovementSystem
{
    private static readonly ProfilerMarker MovementSyncFromPopulationStateMarker = new ProfilerMarker("ReplicatorMovementSystem.SyncFromPopulationState");
    private static readonly ProfilerMarker MovementSyncToAgentsMarker = new ProfilerMarker("ReplicatorMovementSystem.SyncToAgents");

    public struct Settings
    {
        public float MoveSpeed;
        public float TurnSpeed;
        public float AmoeboidTurnRate;
        public float AmoeboidMoveSpeedMultiplier;
        public float FlagellumTurnRate;
        public float FlagellumMoveSpeedMultiplier;
        public float FlagellumDriftSuppression;
        public float AnchoredDriftMultiplier;
        public float MinSpeedFactor;
    }

    private NativeArray<Vector3> jobPositions;
    private NativeArray<Quaternion> jobRotations;
    private NativeArray<bool> jobMoveOnlyInSea;
    private NativeArray<float> jobSurfaceMoveSpeedMultipliers;
    private NativeArray<float> jobMovementSeeds;
    private NativeArray<float> jobSpeedFactors;
    private NativeArray<int> jobLocomotionTypes;
    private NativeArray<Vector3> jobDesiredMoveDirs;
    private int jobCapacity;

    public void RunMovementJob(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        Settings settings,
        PlanetGenerator planetGenerator,
        float deltaTime,
        float timeValue)
    {
        int count = agents.Count;
        if (count == 0)
        {
            return;
        }

        EnsureJobBufferCapacity(count);

        populationState.SyncMovementFieldsFromAgents(agents);

        using (MovementSyncFromPopulationStateMarker.Auto())
        {
            for (int i = 0; i < count; i++)
            {
                Replicator agent = agents[i];
                jobPositions[i] = populationState.Position[i];
                jobRotations[i] = agent.rotation;
                jobMoveOnlyInSea[i] = agent.traits.moveOnlyInSea;
                jobSurfaceMoveSpeedMultipliers[i] = Mathf.Max(0.01f, agent.traits.surfaceMoveSpeedMultiplier);
                jobMovementSeeds[i] = agent.movementSeed;
                jobSpeedFactors[i] = Mathf.Clamp(populationState.SpeedFactor[i], settings.MinSpeedFactor, 1f);
                jobLocomotionTypes[i] = (int)populationState.Locomotion[i];
                jobDesiredMoveDirs[i] = agent.desiredMoveDir;
            }
        }

        ReplicatorUpdateJob job = new ReplicatorUpdateJob
        {
            Positions = jobPositions,
            Rotations = jobRotations,
            MoveOnlyInSea = jobMoveOnlyInSea,
            SurfaceMoveSpeedMultipliers = jobSurfaceMoveSpeedMultipliers,
            MovementSeeds = jobMovementSeeds,
            SpeedFactors = jobSpeedFactors,
            LocomotionTypes = jobLocomotionTypes,
            DesiredMoveDirs = jobDesiredMoveDirs,
            DeltaTime = deltaTime,
            MoveSpeed = settings.MoveSpeed,
            TurnSpeed = settings.TurnSpeed,
            Radius = planetGenerator.radius,
            TimeVal = timeValue,
            AmoebaTurnRate = Mathf.Max(0f, settings.AmoeboidTurnRate),
            AmoebaMoveSpeedMultiplier = Mathf.Max(0f, settings.AmoeboidMoveSpeedMultiplier),
            FlagellumTurnRate = Mathf.Max(0f, settings.FlagellumTurnRate),
            FlagellumMoveSpeedMultiplier = Mathf.Max(0f, settings.FlagellumMoveSpeedMultiplier),
            FlagellumDriftSuppression = Mathf.Clamp01(settings.FlagellumDriftSuppression),
            AnchoredDriftMultiplier = settings.AnchoredDriftMultiplier,
            NoiseMagnitude = planetGenerator.noiseMagnitude,
            NoiseRoughness = planetGenerator.noiseRoughness,
            NoiseOffset = planetGenerator.noiseOffset,
            NumLayers = planetGenerator.numLayers,
            Persistence = planetGenerator.persistence,
            OceanThreshold = planetGenerator.OceanThresholdNoise,
            OceanDepth = planetGenerator.oceanDepth,
            OceanEnabled = planetGenerator.OceanEnabled
        };

        JobHandle handle = job.Schedule(count, 32);
        handle.Complete();

        using (MovementSyncToAgentsMarker.Auto())
        {
            for (int i = 0; i < count; i++)
            {
                Replicator agent = agents[i];
                Vector3 newPosition = jobPositions[i];
                Vector3 newDirection = newPosition.normalized;
                populationState.Position[i] = newPosition;
                populationState.CurrentDirection[i] = newDirection;
                agent.position = newPosition;
                agent.rotation = jobRotations[i];
                agent.currentDirection = newDirection;
            }
        }
    }

    public void Dispose()
    {
        if (jobPositions.IsCreated) jobPositions.Dispose();
        if (jobRotations.IsCreated) jobRotations.Dispose();
        if (jobMoveOnlyInSea.IsCreated) jobMoveOnlyInSea.Dispose();
        if (jobSurfaceMoveSpeedMultipliers.IsCreated) jobSurfaceMoveSpeedMultipliers.Dispose();
        if (jobMovementSeeds.IsCreated) jobMovementSeeds.Dispose();
        if (jobSpeedFactors.IsCreated) jobSpeedFactors.Dispose();
        if (jobLocomotionTypes.IsCreated) jobLocomotionTypes.Dispose();
        if (jobDesiredMoveDirs.IsCreated) jobDesiredMoveDirs.Dispose();
    }

    private void EnsureJobBufferCapacity(int requiredCount)
    {
        if (jobCapacity >= requiredCount)
        {
            return;
        }

        Dispose();

        jobCapacity = Mathf.NextPowerOfTwo(requiredCount);
        jobPositions = new NativeArray<Vector3>(jobCapacity, Allocator.Persistent);
        jobRotations = new NativeArray<Quaternion>(jobCapacity, Allocator.Persistent);
        jobMoveOnlyInSea = new NativeArray<bool>(jobCapacity, Allocator.Persistent);
        jobSurfaceMoveSpeedMultipliers = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobMovementSeeds = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobSpeedFactors = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobLocomotionTypes = new NativeArray<int>(jobCapacity, Allocator.Persistent);
        jobDesiredMoveDirs = new NativeArray<Vector3>(jobCapacity, Allocator.Persistent);
    }

    public struct ReplicatorUpdateJob : IJobParallelFor
    {
        public NativeArray<Vector3> Positions;
        public NativeArray<Quaternion> Rotations;
        public NativeArray<bool> MoveOnlyInSea;
        public NativeArray<float> SurfaceMoveSpeedMultipliers;
        public NativeArray<float> MovementSeeds;
        public NativeArray<float> SpeedFactors;
        public NativeArray<int> LocomotionTypes;
        public NativeArray<Vector3> DesiredMoveDirs;

        public float DeltaTime;
        public float MoveSpeed;
        public float TurnSpeed;
        public float Radius;
        public float TimeVal;
        public float AmoebaTurnRate;
        public float AmoebaMoveSpeedMultiplier;
        public float FlagellumTurnRate;
        public float FlagellumMoveSpeedMultiplier;
        public float FlagellumDriftSuppression;
        public float AnchoredDriftMultiplier;
        public float NoiseMagnitude;
        public float NoiseRoughness;
        public Vector3 NoiseOffset;
        public int NumLayers;
        public float Persistence;
        public float OceanThreshold;
        public float OceanDepth;
        public bool OceanEnabled;

        public void Execute(int index)
        {
            Vector3 pos = Positions[index];
            Quaternion rot = Rotations[index];
            Vector3 surfaceNormal = pos.normalized;

            int locomotion = LocomotionTypes[index];
            if (locomotion == (int)LocomotionType.Anchored)
            {
                Positions[index] = pos;
                Rotations[index] = rot;
                return;
            }

            float driftMultiplier = locomotion == (int)LocomotionType.Anchored ? AnchoredDriftMultiplier : 1f;
            float seed = MovementSeeds[index];
            float turnNoiseA = SimpleNoise.Evaluate(surfaceNormal * 3.1f + new Vector3(seed, TimeVal * 0.31f, 0f));
            float turnNoiseB = SimpleNoise.Evaluate(surfaceNormal * 4.7f + new Vector3(TimeVal * 0.19f, seed * 1.37f, 0f));
            float turnNoise = Mathf.Clamp(turnNoiseA + turnNoiseB, -1f, 1f);
            float turnAmount = turnNoise * TurnSpeed * DeltaTime * 35f * driftMultiplier;
            rot = Quaternion.AngleAxis(turnAmount, surfaceNormal) * rot;

            Vector3 forward = rot * Vector3.forward;
            Vector3 lateralAxis = Vector3.Cross(surfaceNormal, forward);
            float wobble = SimpleNoise.Evaluate(surfaceNormal * 6.2f + new Vector3(MovementSeeds[index] * 0.73f, 0f, TimeVal * 0.43f));
            forward = (forward + lateralAxis * wobble * 0.35f * driftMultiplier).normalized;

            bool isAmoeboid = locomotion == (int)LocomotionType.Amoeboid;
            bool isFlagellum = locomotion == (int)LocomotionType.Flagellum;

            if (isAmoeboid || isFlagellum)
            {
                Vector3 desiredDir = DesiredMoveDirs[index];
                Vector3 desiredTangent = Vector3.ProjectOnPlane(desiredDir, surfaceNormal);
                if (desiredTangent.sqrMagnitude > 0.0001f)
                {
                    Vector3 desiredForward = desiredTangent.normalized;
                    float activeTurnRate = isFlagellum ? FlagellumTurnRate : AmoebaTurnRate;
                    forward = Vector3.Slerp(forward, desiredForward, Mathf.Clamp01(activeTurnRate * DeltaTime));

                    if (isFlagellum)
                    {
                        float suppression = Mathf.Clamp01(FlagellumDriftSuppression);
                        forward = Vector3.Slerp(forward, desiredForward, suppression);
                    }
                }
            }

            bool moveOnlyInSea = MoveOnlyInSea[index];
            float currentNoise = CalculateNoise(surfaceNormal);
            bool currentlyInSea = !OceanEnabled || currentNoise < OceanThreshold;
            float speedMultiplier = currentlyInSea ? 1f : SurfaceMoveSpeedMultipliers[index];
            if (isAmoeboid)
            {
                speedMultiplier *= AmoebaMoveSpeedMultiplier;
            }
            else if (isFlagellum)
            {
                speedMultiplier *= FlagellumMoveSpeedMultiplier;
            }

            float speedFactor = SpeedFactors[index];
            Quaternion travelRot = Quaternion.AngleAxis((MoveSpeed * speedMultiplier * speedFactor) * DeltaTime / Radius, Vector3.Cross(surfaceNormal, forward));
            Vector3 newDirection = (travelRot * pos).normalized;

            if (OceanEnabled && moveOnlyInSea)
            {
                float nextNoise = CalculateNoise(newDirection);
                bool nextInSea = nextNoise < OceanThreshold;
                if (!nextInSea)
                {
                    newDirection = surfaceNormal;
                }
            }

            float terrainNoise = CalculateNoise(newDirection);
            float displacement = GetSurfaceRadiusFromNoise(terrainNoise);
            Vector3 newPos = newDirection * (displacement + 0.05f);

            Vector3 newNormal = newPos.normalized;
            Quaternion targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, newNormal), newNormal);
            rot = Quaternion.Slerp(rot, targetRot, DeltaTime * 5f);

            Positions[index] = newPos;
            Rotations[index] = rot;
        }

        private float GetSurfaceRadiusFromNoise(float noise)
        {
            float finalNoise = noise;

            if (OceanEnabled && noise < OceanThreshold)
            {
                float t = OceanThreshold > 0f ? Mathf.Clamp01(noise / OceanThreshold) : 0f;
                float minNoise = OceanThreshold * (1f - OceanDepth);
                finalNoise = Mathf.Lerp(minNoise, OceanThreshold, t);
            }

            return Radius * (1f + finalNoise * NoiseMagnitude);
        }

        private float CalculateNoise(Vector3 point)
        {
            float noiseValue = 0;
            float frequency = NoiseRoughness;
            float amplitude = 1;
            float maxPossibleHeight = 0;

            for (int i = 0; i < NumLayers; i++)
            {
                Vector3 samplePoint = point * frequency + NoiseOffset;
                float singleLayerNoise = SimpleNoise.Evaluate(samplePoint);
                singleLayerNoise = (singleLayerNoise + 1) * 0.5f;

                noiseValue += singleLayerNoise * amplitude;
                maxPossibleHeight += amplitude;

                amplitude *= Persistence;
                frequency *= 2;
            }

            return maxPossibleHeight > 0f ? noiseValue / maxPossibleHeight : 0f;
        }
    }
}
