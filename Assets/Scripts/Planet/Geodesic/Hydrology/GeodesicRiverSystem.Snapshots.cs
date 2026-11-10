using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using S = GeodesicSnapshotIO;
using Debug = UnityEngine.Debug;

public sealed partial class GeodesicRiverSystem
{
    private const int TinStage = 3, LakeStage = 4;
    private const int TinSchema = 1, LakeSchema = 1;
    // Increment the relevant algorithm stamp when drainage or routing mathematics change.
    private const int TinAlgorithm = 1, LakeAlgorithm = 1;

    private sealed class SnapshotRecord
    {
        private readonly PlanetGenerator owner;
        private readonly int stage, schema, vertices, triangles;
        private readonly byte[] key;
        private readonly string path;
        internal byte[] Digest { get; private set; }
        internal bool Exists => File.Exists(path);

        internal SnapshotRecord(PlanetGenerator owner, int stage, int schema, int algorithm,
            int vertices, int triangles, Action<BinaryWriter> inputs)
        {
            this.owner = owner; this.stage = stage; this.schema = schema; this.vertices = vertices; this.triangles = triangles;
            key = PlanetGenerationCache.HashBinaryKey(w =>
            {
                S.RuntimeKey(w);
                w.Write(stage); w.Write(schema); w.Write(algorithm);
                w.Write(PlanetGenerator.GenerationVersion);
                inputs(w);
            });
            path = PlanetGenerationCache.GeodesicPath(stage, key);
        }

        internal T Load<T>(string name, Func<BinaryReader, T> read) where T : class
        {
            var watch = Stopwatch.StartNew();
            var result = PlanetGenerationCache.LoadGeodesic(path, key, stage, schema, vertices, triangles, read, out var digest);
            Digest = digest;
            if (result != null)
                Debug.Log(FormattableString.Invariant($"[GeodesicHydrologySnapshot] stage={name} readValidateRestoreMs={watch.Elapsed.TotalMilliseconds:F2}"), owner);
            return result;
        }

        internal void Capture(Action<BinaryWriter> write)
        {
            try
            {
                Digest = PlanetGenerationCache.HashBinaryKey(write);
                owner.QueueGeodesicSnapshot(() => PlanetGenerationCache.SaveGeodesic(path, key, stage, schema, vertices, triangles, write));
            }
            catch (InvalidDataException ex)
            {
                Debug.LogWarning($"[GeodesicHydrologySnapshot] stage={stage} capture skipped: {ex.Message}", owner);
            }
        }
    }

    private SnapshotRecord TinRecord(IcosphereRenderGeometry geometry, Vector3[] positions, bool[] ocean,
        float[] hydrologyInput, double epsilon, double bump)
    {
        var watch = Stopwatch.StartNew();
        var record = new SnapshotRecord(planet, TinStage, TinSchema, TinAlgorithm, geometry.VertexCount, geometry.TriangleCount, w =>
        {
            // The exact realized geometry and height inputs are the parent output proof.
            // This also permits reuse if an earlier record was absent but rebuilt identically.
            w.Write(geometry.SubdivisionLevel);
            w.Write(planet.GeodesicSeaLevelRadius);
            w.Write(planet.BasePlanetRadius);
            w.Write(epsilon); w.Write(bump);
            w.Write(Math.Max(0d, Math.Min(.1d, tinFilterStrength)));
            w.Write(ResolvedDrainageAreaThreshold);
            w.Write(Mathf.Max(0f, uniformRunoffPerArea));
            S.Write(w, geometry.UnitVertices, S.Vector);
            S.Ints(w, geometry.Triangles);
            S.Write(w, positions, S.Vector);
            S.Flags(w, ocean);
            S.Floats(w, hydrologyInput);
        });
        Debug.Log(FormattableString.Invariant($"[GeodesicHydrologySnapshot] stage=TIN lookupKeyMs={watch.Elapsed.TotalMilliseconds:F2}"), this);
        return record;
    }

    private sealed class RestoredLakes
    {
        internal GeodesicTinBasins Basins;
        internal GeodesicTinLakeGeometry Geometry;
        internal GeodesicTinLakeRouting Routing;

        internal static RestoredLakes Read(BinaryReader r, GeodesicTinHydrology tin)
        {
            var basins = GeodesicTinBasins.RestoreSnapshot(r, tin);
            var geometry = GeodesicTinLakeGeometry.RestoreSnapshot(r, tin, basins);
            return new RestoredLakes
            {
                Basins = basins, Geometry = geometry,
                Routing = S.Flag(r) ? GeodesicTinLakeRouting.RestoreSnapshot(r, tin, basins, geometry) : null
            };
        }
    }

    private SnapshotRecord LakeRecord(SnapshotRecord tinRecord) =>
        new SnapshotRecord(planet, LakeStage, LakeSchema, LakeAlgorithm, TinDrainage.BasinVertexCount,
            TinDrainage.TriangleCount, w =>
            {
                w.Write(tinRecord.Digest);
                w.Write(integrateTinLakeRivers);
                w.Write(ResolvedDrainageAreaThreshold);
            });
}
