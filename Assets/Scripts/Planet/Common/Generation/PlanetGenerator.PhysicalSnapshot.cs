using System.IO;
using UnityEngine;
using S = GeodesicSnapshotIO;

public partial class PlanetGenerator
{
    private const int PhysicalStage = 2;
    private const int PhysicalSchema = 1;
    // Bump for changes to canonical terrain topology, geology, ocean classification or infill.
    private const int PhysicalAlgorithm = 1;

    private byte[] PhysicalKey(int simulation, int render) => PlanetGenerationCache.HashBinaryKey(w =>
    {
        S.RuntimeKey(w);
        w.Write(PhysicalStage);
        w.Write(PhysicalSchema);
        w.Write(PhysicalAlgorithm);
        w.Write(GenerationVersion);
        w.Write(IcosphereDirectionMapping.Version);
        w.Write(simulation);
        w.Write(render);
        w.Write(randomSeed);
        w.Write(DerivedTerrainSeed);
        w.Write(DerivedBathymetrySeed);
        w.Write(BasePlanetRadius);
        w.Write(enableGeodesicTerrainDisplacement);
        var settings = GetGeodesicTerrainSettings();
        w.Write(settings.continentAmplitude);
        w.Write(settings.continentScale);
        w.Write(settings.continentBias);
        w.Write(settings.mountainAmplitude);
        w.Write(settings.mountainScale);
        w.Write(settings.mountainCoverageThreshold);
        w.Write(settings.mountainMaskSoftness);
        w.Write(settings.ridgeSharpness);
        w.Write(settings.domainWarpScale);
        w.Write(settings.domainWarpStrength);
        w.Write(settings.fineDetailScale);
        w.Write(settings.fineDetailAmplitude);
        w.Write(settings.minimumTerrainOffset);
        w.Write(settings.maximumTerrainOffset);
        w.Write(settings.octaves);
        w.Write(settings.persistence);
        w.Write(settings.lacunarity);
        w.Write(settings.heightContrast);
        w.Write(enableOcean);
        w.Write((int)geodesicSeaLevelControlMode);
        w.Write(geodesicSeaLevelOffset);
        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.TargetAreaCoverage)
            w.Write(Mathf.Clamp(geodesicTargetOceanCoveragePercent, 0f, 100f));
        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
            w.Write(Mathf.Max(0f, geodesicOceanWorldMinimumDepth));
        w.Write(excludeSmallDisconnectedSeas);
        w.Write(GeodesicOceanConnectivity.NormalizeThreshold(minimumOceanComponentAreaFraction));
        w.Write(inlandElevationMagnitude);
        w.Write(infillExcludedInlandBasins);
        if (infillExcludedInlandBasins)
        {
            var limits = basinInfillLimits.Normalized();
            w.Write(limits.totalArea); w.Write(limits.liftFraction); w.Write(limits.transitionGrade);
        }
        w.Write(enableGeodesicBathymetry);
        w.Write(geodesicShelfWidthDegrees);
        w.Write(geodesicShelfDepth);
        w.Write(geodesicMaximumOceanDepth);
        w.Write(geodesicContinentalSlopeExponent);
        w.Write(enableGeodesicOceanicIslandMargins);
        w.Write(geodesicOceanicIslandShelfWidthDegrees);
        w.Write(geodesicOceanicIslandShelfDepth);
        w.Write(geodesicOceanicIslandSlopeExponent);
        w.Write(geodesicOceanicIslandShelfVariationStrength);
        w.Write(geodesicMixedMarginOceanicBlendStrength);
        w.Write(geodesicContinentalShelfMinWidthMultiplier);
        w.Write(geodesicContinentalShelfMaxWidthMultiplier);
        w.Write(geodesicContinentalShelfWidthVariationStrength);
        w.Write(geodesicContinentalShelfWidthVariationScale);
        w.Write(geodesicContinentalShelfMinDepthMultiplier);
        w.Write(geodesicContinentalShelfMaxDepthMultiplier);
        w.Write(geodesicContinentalShelfDepthVariationStrength);
        w.Write(geodesicContinentalShelfDepthVariationScale);
        w.Write(geodesicContinentalSlopeVariationStrength);
        w.Write(geodesicShelfVariationScale);
        w.Write(geodesicShelfWidthDepthCorrelation);
        w.Write(geodesicOceanicRidgeStrength);
        w.Write(geodesicOceanicRidgeScale);
        w.Write(geodesicOceanicRidgeThreshold);
        w.Write(geodesicOceanicPlateauStrength);
        w.Write(geodesicOceanicPlateauScale);
        w.Write(enableGeodesicSeamounts);
        w.Write(geodesicSeamountDensity);
        w.Write(geodesicSeamountAmplitude);
        w.Write(geodesicSeamountRadiusDegrees);
        w.Write(geodesicSeamountChainProbability);
        w.Write(geodesicSeamountChainLength);
        w.Write(geodesicSeamountChainSpacingDegrees);
        w.Write(geodesicSeamountEmergenceBias);
        w.Write(geodesicBasinNoiseScale);
        w.Write(geodesicBasinNoiseStrength);
        w.Write(geodesicShorelinePreservationDegrees);
        w.Write(geodesicBathymetrySmoothPasses);
        w.Write(geodesicBathymetrySmoothStrength);
        w.Write(geodesicBathymetryStrength);
    });

    // The record contains generation data only. A decoded record can transfer ownership
    // directly to the fresh planet. Capture clones only the arrays that carving edits.
    private sealed class PhysicalSnapshot
    {
        internal float[] TerrainHeightByCell;
        internal float[] NormalizedTerrainByCell;
        internal float[] RawTerrainRadius;
        internal float[] SeafloorRadius;
        internal float[] BaseWaterDepth;
        internal float[] WaterDepth;
        internal float[] DistanceToShore;
        internal float[] BasinNoiseContribution;
        internal float[] ContinentalInfluenceByCell;
        internal float[] OceanicRidgeReliefByCell;
        internal float[] OceanicPlateauReliefByCell;
        internal float[] SeamountReliefByCell;
        internal float[] TotalOceanicReliefByCell;
        internal float[] ContinentalShelfInfluenceByCell;
        internal float[] LocalShelfWidthMultiplierByCell;
        internal float[] LocalShelfDepthByCell;
        internal float[] OceanicIslandShelfInfluenceByCell;
        internal float[] ContinentalProfileShelfWidthByCell;
        internal float[] FinalShelfWidthByCell;
        internal float[] ApproxCellSpacingDegreesByCell;
        internal int[] LandComponentIdByCell;
        internal GeodesicCoastType[] CoastTypeByCell;
        internal GeodesicShelfProfileType[] ShelfProfileTypeByCell;
        internal GeodesicBathymetryRegion[] BathymetryRegion;
        internal bool[] TerrainBathymetryMask;
        internal bool[] CoastlineMask;
        internal byte[] OceanNeighborCounts;
        internal GeodesicOceanConnectivity Connectivity;
        internal float SeaLevel, SeaOffset, OceanWorldMaxSolid;
        internal Vector3[] Positions;
        internal GeodesicRenderTerrainData Terrain;
        internal bool[] InfillOcean, ChangedVertices, AffectedFacets;
        internal float[] PreInfillRadii;
        internal int[] InfillComponents;

        internal void Write(BinaryWriter w)
        {
            w.Write(SeaLevel); w.Write(SeaOffset); w.Write(OceanWorldMaxSolid);
            Connectivity.WriteSnapshot(w);
            S.Floats(w, TerrainHeightByCell);
            S.Floats(w, NormalizedTerrainByCell);
            S.Floats(w, RawTerrainRadius);
            S.Floats(w, SeafloorRadius);
            S.Floats(w, BaseWaterDepth);
            S.Floats(w, WaterDepth);
            S.Floats(w, DistanceToShore);
            S.Floats(w, BasinNoiseContribution);
            S.Floats(w, ContinentalInfluenceByCell);
            S.Floats(w, OceanicRidgeReliefByCell);
            S.Floats(w, OceanicPlateauReliefByCell);
            S.Floats(w, SeamountReliefByCell);
            S.Floats(w, TotalOceanicReliefByCell);
            S.Floats(w, ContinentalShelfInfluenceByCell);
            S.Floats(w, LocalShelfWidthMultiplierByCell);
            S.Floats(w, LocalShelfDepthByCell);
            S.Floats(w, OceanicIslandShelfInfluenceByCell);
            S.Floats(w, ContinentalProfileShelfWidthByCell);
            S.Floats(w, FinalShelfWidthByCell);
            S.Floats(w, ApproxCellSpacingDegreesByCell);
            S.Ints(w, LandComponentIdByCell);
            S.Write(w, CoastTypeByCell, (b, v) => b.Write((byte)v));
            S.Write(w, ShelfProfileTypeByCell, (b, v) => b.Write((byte)v));
            S.Write(w, BathymetryRegion, (b, v) => b.Write((byte)v));
            S.Flags(w, TerrainBathymetryMask);
            S.Flags(w, CoastlineMask);
            S.Write(w, OceanNeighborCounts, (b, v) => b.Write((byte)v));
            S.Write(w, Positions, S.Vector);
            S.Floats(w, Terrain.RawRadii);
            S.Floats(w, Terrain.HydrologicalRadii);
            S.Floats(w, Terrain.SurfaceRadii);
            S.Floats(w, Terrain.Heights);
            S.Floats(w, Terrain.NormalizedHeights);
            S.Floats(w, Terrain.MountainMasks);
            w.Write(InfillOcean != null);
            if (InfillOcean == null) return;
            S.Flags(w, InfillOcean);
            S.Flags(w, ChangedVertices);
            S.Flags(w, AffectedFacets);
            S.Floats(w, PreInfillRadii);
            S.Ints(w, InfillComponents);
        }

        internal static PhysicalSnapshot Read(BinaryReader r, int cells, int vertices, int triangles)
        {
            var result = new PhysicalSnapshot
            {
                SeaLevel = S.Float(r), SeaOffset = S.Float(r), OceanWorldMaxSolid = S.Float(r),
                Connectivity = GeodesicOceanConnectivity.ReadSnapshot(r, cells),
                TerrainHeightByCell = S.Floats(r, cells),
                NormalizedTerrainByCell = S.Floats(r, cells),
                RawTerrainRadius = S.Floats(r, cells),
                SeafloorRadius = S.Floats(r, cells),
                BaseWaterDepth = S.Floats(r, cells),
                WaterDepth = S.Floats(r, cells),
                DistanceToShore = S.Floats(r, cells),
                BasinNoiseContribution = S.Floats(r, cells),
                ContinentalInfluenceByCell = S.Floats(r, cells),
                OceanicRidgeReliefByCell = S.Floats(r, cells),
                OceanicPlateauReliefByCell = S.Floats(r, cells),
                SeamountReliefByCell = S.Floats(r, cells),
                TotalOceanicReliefByCell = S.Floats(r, cells),
                ContinentalShelfInfluenceByCell = S.Floats(r, cells),
                LocalShelfWidthMultiplierByCell = S.Floats(r, cells),
                LocalShelfDepthByCell = S.Floats(r, cells),
                OceanicIslandShelfInfluenceByCell = S.Floats(r, cells),
                ContinentalProfileShelfWidthByCell = S.Floats(r, cells),
                FinalShelfWidthByCell = S.Floats(r, cells),
                ApproxCellSpacingDegreesByCell = S.Floats(r, cells),
                LandComponentIdByCell = S.Read(r, cells, 4, b => S.Id(b, cells, -1), true),
                CoastTypeByCell = S.Read(r, cells, 1, b => (GeodesicCoastType)b.ReadByte(), true),
                ShelfProfileTypeByCell = S.Read(r, cells, 1, b => (GeodesicShelfProfileType)b.ReadByte(), true),
                BathymetryRegion = S.Read(r, cells, 1, b => (GeodesicBathymetryRegion)b.ReadByte(), true),
                TerrainBathymetryMask = S.Flags(r, cells),
                CoastlineMask = S.Flags(r, cells),
                OceanNeighborCounts = S.Read(r, cells, 1, b => (byte)b.ReadByte(), true),
                Positions = S.Read(r, vertices, 12, S.Vector, true),
                Terrain = new GeodesicRenderTerrainData
                {
                    RawRadii = S.Floats(r, vertices),
                    HydrologicalRadii = S.Floats(r, vertices),
                    SurfaceRadii = S.Floats(r, vertices),
                    Heights = S.Floats(r, vertices),
                    NormalizedHeights = S.Floats(r, vertices),
                    MountainMasks = S.Floats(r, vertices),
                }
            };
            if (S.Flag(r))
            {
                result.InfillOcean = S.Flags(r, vertices);
                result.ChangedVertices = S.Flags(r, vertices);
                result.AffectedFacets = S.Flags(r, triangles);
                result.PreInfillRadii = S.Floats(r, vertices);
                result.InfillComponents = S.Read(r, vertices, 4, b => S.Id(b, result.Connectivity.Components.Length, -1), true);
            }
            foreach (Vector3 position in result.Positions)
                S.Require(position.sqrMagnitude > 0 && float.IsFinite(position.sqrMagnitude), "Invalid base terrain position.");
            return result;
        }
    }

    private PhysicalSnapshot CapturePhysicalSnapshot() => new PhysicalSnapshot
    {
        SeaLevel = resolvedGeodesicSeaLevelRadius,
        SeaOffset = resolvedGeodesicSeaLevelOffset,
        OceanWorldMaxSolid = geodesicOceanWorldMaxSimulationSolidRadius,
        Connectivity = OceanConnectivity,
        TerrainHeightByCell = geodesicTerrainHeightByCell,
        NormalizedTerrainByCell = geodesicNormalizedTerrainByCell,
        RawTerrainRadius = geodesicRawTerrainRadius,
        SeafloorRadius = geodesicSeafloorRadius,
        BaseWaterDepth = geodesicBaseWaterDepth,
        WaterDepth = geodesicWaterDepth,
        DistanceToShore = geodesicDistanceToShore,
        BasinNoiseContribution = geodesicBasinNoiseContribution,
        ContinentalInfluenceByCell = geodesicContinentalInfluenceByCell,
        OceanicRidgeReliefByCell = geodesicOceanicRidgeReliefByCell,
        OceanicPlateauReliefByCell = geodesicOceanicPlateauReliefByCell,
        SeamountReliefByCell = geodesicSeamountReliefByCell,
        TotalOceanicReliefByCell = geodesicTotalOceanicReliefByCell,
        ContinentalShelfInfluenceByCell = geodesicContinentalShelfInfluenceByCell,
        LocalShelfWidthMultiplierByCell = geodesicLocalShelfWidthMultiplierByCell,
        LocalShelfDepthByCell = geodesicLocalShelfDepthByCell,
        OceanicIslandShelfInfluenceByCell = geodesicOceanicIslandShelfInfluenceByCell,
        ContinentalProfileShelfWidthByCell = geodesicContinentalProfileShelfWidthByCell,
        FinalShelfWidthByCell = geodesicFinalShelfWidthByCell,
        ApproxCellSpacingDegreesByCell = geodesicApproxCellSpacingDegreesByCell,
        LandComponentIdByCell = geodesicLandComponentIdByCell,
        CoastTypeByCell = geodesicCoastTypeByCell,
        ShelfProfileTypeByCell = geodesicShelfProfileTypeByCell,
        BathymetryRegion = geodesicBathymetryRegion,
        TerrainBathymetryMask = geodesicTerrainBathymetryMask,
        CoastlineMask = geodesicCoastlineMask,
        OceanNeighborCounts = geodesicOceanNeighborCounts,
        Positions = mesh.vertices,
        Terrain = new GeodesicRenderTerrainData
        {
            RawRadii = (float[])geodesicCurrentRenderTerrainData.RawRadii.Clone(),
            HydrologicalRadii = (float[])geodesicCurrentRenderTerrainData.HydrologicalRadii.Clone(),
            SurfaceRadii = (float[])geodesicCurrentRenderTerrainData.SurfaceRadii.Clone(),
            Heights = (float[])geodesicCurrentRenderTerrainData.Heights.Clone(),
            NormalizedHeights = (float[])geodesicCurrentRenderTerrainData.NormalizedHeights.Clone(),
            MountainMasks = (float[])geodesicCurrentRenderTerrainData.MountainMasks.Clone(),
        },
        InfillOcean = GeodesicInfillOceanMask,
        ChangedVertices = GeodesicInfilledRenderVertices,
        AffectedFacets = geodesicInfilledFacets,
        PreInfillRadii = GeodesicPreInfillRenderRadii,
        InfillComponents = GeodesicInfillComponentByVertex
    };

    private void RestorePhysicalCells(PhysicalSnapshot snapshot)
    {
        resolvedGeodesicSeaLevelRadius = snapshot.SeaLevel;
        resolvedGeodesicSeaLevelOffset = snapshot.SeaOffset;
        geodesicOceanWorldMaxSimulationSolidRadius = snapshot.OceanWorldMaxSolid;
        OceanConnectivity = snapshot.Connectivity;
        geodesicOceanMask = OceanConnectivity.OceanMask;
        geodesicTerrainHeightByCell = snapshot.TerrainHeightByCell;
        geodesicNormalizedTerrainByCell = snapshot.NormalizedTerrainByCell;
        geodesicRawTerrainRadius = snapshot.RawTerrainRadius;
        geodesicSeafloorRadius = snapshot.SeafloorRadius;
        geodesicBaseWaterDepth = snapshot.BaseWaterDepth;
        geodesicWaterDepth = snapshot.WaterDepth;
        geodesicDistanceToShore = snapshot.DistanceToShore;
        geodesicBasinNoiseContribution = snapshot.BasinNoiseContribution;
        geodesicContinentalInfluenceByCell = snapshot.ContinentalInfluenceByCell;
        geodesicOceanicRidgeReliefByCell = snapshot.OceanicRidgeReliefByCell;
        geodesicOceanicPlateauReliefByCell = snapshot.OceanicPlateauReliefByCell;
        geodesicSeamountReliefByCell = snapshot.SeamountReliefByCell;
        geodesicTotalOceanicReliefByCell = snapshot.TotalOceanicReliefByCell;
        geodesicContinentalShelfInfluenceByCell = snapshot.ContinentalShelfInfluenceByCell;
        geodesicLocalShelfWidthMultiplierByCell = snapshot.LocalShelfWidthMultiplierByCell;
        geodesicLocalShelfDepthByCell = snapshot.LocalShelfDepthByCell;
        geodesicOceanicIslandShelfInfluenceByCell = snapshot.OceanicIslandShelfInfluenceByCell;
        geodesicContinentalProfileShelfWidthByCell = snapshot.ContinentalProfileShelfWidthByCell;
        geodesicFinalShelfWidthByCell = snapshot.FinalShelfWidthByCell;
        geodesicApproxCellSpacingDegreesByCell = snapshot.ApproxCellSpacingDegreesByCell;
        geodesicLandComponentIdByCell = snapshot.LandComponentIdByCell;
        geodesicCoastTypeByCell = snapshot.CoastTypeByCell;
        geodesicShelfProfileTypeByCell = snapshot.ShelfProfileTypeByCell;
        geodesicBathymetryRegion = snapshot.BathymetryRegion;
        geodesicTerrainBathymetryMask = snapshot.TerrainBathymetryMask;
        geodesicCoastlineMask = snapshot.CoastlineMask;
        geodesicOceanNeighborCounts = snapshot.OceanNeighborCounts;
        geodesicLastShorelineCalculationSkipped = geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld;
        // This query helper is inexpensive and has no evolving state.
        geodesicInlandUplift = new GeodesicInlandUplift(GeodesicTopology, geodesicOceanMask);
        LogGeodesicBathymetryDiagnostics(OceanConnectivity.RetainedCells);
    }

    private void RestoreBaseSurface(PhysicalSnapshot snapshot, IcosphereRenderGeometry geometry)
    {
        mesh.vertices = snapshot.Positions;
        geodesicCurrentRenderTerrainData = snapshot.Terrain;
        GeodesicInfillOceanMask = snapshot.InfillOcean;
        GeodesicInfilledRenderVertices = snapshot.ChangedVertices;
        geodesicInfilledFacets = snapshot.AffectedFacets;
        GeodesicPreInfillRenderRadii = snapshot.PreInfillRadii;
        GeodesicInfillComponentByVertex = snapshot.InfillComponents;
        if (snapshot.AffectedFacets != null)
            geodesicInfilledSurface = new GeodesicRiverTerrain(geometry, snapshot.Positions);
    }
}
