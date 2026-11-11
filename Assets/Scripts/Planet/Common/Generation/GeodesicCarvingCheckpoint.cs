using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>Initial-generation terrain and numeric inspection evidence from before the final TIN.</summary>
internal sealed class GeodesicCarvingCheckpoint
{
    private const int Stage = 1;
    private const int Schema = 1;
    // Bump for changes to ordinary carving, its intermediate TIN/basin rules or surface application.
    private const int AlgorithmVersion = 1;
    private const int MaximumEvidenceStringBytes = 16384;

    internal Vector3[] Positions;
    internal float[] RawRadii, HydrologicalRadii, SurfaceRadii, Heights, NormalizedHeights;
    internal bool[] Ocean;
    internal bool Attempted, AppliedEdits;
    internal List<GeodesicValleyCarving.Result> Passes;
    internal IReadOnlyList<GeodesicOceanDrainagePlan.Catchment> InitialCatchments;

    internal static byte[] BuildKey(PlanetGenerator owner, IcosphereRenderGeometry geometry,
        Vector3[] positions, bool[] ocean, int passes, float depth, float width, double runoff,
        double riverThreshold, float runoffDensity, double epsilon, float bumpFraction) =>
        PlanetGenerationCache.HashBinaryKey(writer =>
        {
            writer.Write(Stage);
            writer.Write(Schema);
            writer.Write(AlgorithmVersion);
            writer.Write(PlanetGenerator.GenerationVersion);
            writer.Write(GeodesicOceanDrainagePlan.Version);
            writer.Write(IcosphereDirectionMapping.Version);
            writer.Write(Application.unityVersion);
            writer.Write((int)Application.platform);
            writer.Write((int)RuntimeInformation.ProcessArchitecture);
#if ENABLE_IL2CPP
            writer.Write(true);
#else
            writer.Write(false);
#endif
            writer.Write(owner.randomSeed);
            writer.Write(owner.DerivedTerrainSeed);
            writer.Write(owner.DerivedBathymetrySeed);
            writer.Write(owner.GeodesicTopology.SubdivisionLevel);
            writer.Write(geometry.SubdivisionLevel);
            writer.Write(owner.BasePlanetRadius);
            writer.Write(owner.GeodesicSeaLevelRadius);
            writer.Write(passes);
            writer.Write(depth);
            writer.Write(width);
            writer.Write(runoff);
            writer.Write(riverThreshold);
            writer.Write(runoffDensity);
            writer.Write(epsilon);
            writer.Write(bumpFraction);
            // Exact realized inputs close over the terrain, bathymetry, classification and infill algorithms.
            WriteArray(writer, geometry.UnitVertices, WriteVector);
            WriteIndices(writer, geometry.Triangles);
            WriteArray(writer, positions, WriteVector);
            WriteArray(writer, ocean, (w, value) => w.Write(value));
            owner.WriteGeodesicCarvingTerrainKey(writer);
        });

    internal static string PathFor(byte[] key) => PlanetGenerationCache.GeodesicPath(Stage, key);

    internal static GeodesicCarvingCheckpoint Load(string path, byte[] key, Vector3[] input, bool[] ocean,
        int triangles, int passes, double maximumIncision) =>
        PlanetGenerationCache.LoadGeodesic(path, key, Stage, Schema, input.Length, triangles,
            reader => Decode(reader, input, ocean, triangles, passes, maximumIncision));

    internal void Save(byte[] key, int triangles) =>
        PlanetGenerationCache.SaveGeodesic(PathFor(key), key, Stage, Schema, Positions.Length, triangles, Encode);

    private void Encode(BinaryWriter writer)
    {
        writer.Write(Attempted);
        writer.Write(AppliedEdits);
        WriteArray(writer, Positions, WriteVector);
        WriteFloats(writer, RawRadii);
        WriteFloats(writer, HydrologicalRadii);
        WriteFloats(writer, SurfaceRadii);
        WriteFloats(writer, Heights);
        WriteFloats(writer, NormalizedHeights);
        WriteArray(writer, Ocean, (w, value) => w.Write(value));
        WriteArray(writer, Passes, WritePass);
        WriteArray(writer, InitialCatchments, (w, catchment) =>
        {
            w.Write(catchment.Runoff);
            WriteIndices(w, catchment.Parcels);
        });
    }

    private static GeodesicCarvingCheckpoint Decode(BinaryReader reader, Vector3[] input, bool[] ocean,
        int triangles, int passes, double maximumIncision)
    {
        int vertices = input.Length;
        var snapshot = new GeodesicCarvingCheckpoint
        {
            Attempted = ReadFlag(reader),
            AppliedEdits = ReadFlag(reader),
            Positions = ReadArray(reader, vertices, 12, ReadVector, exact: true),
            RawRadii = ReadFloats(reader, vertices),
            HydrologicalRadii = ReadFloats(reader, vertices),
            SurfaceRadii = ReadFloats(reader, vertices),
            Heights = ReadFloats(reader, vertices),
            NormalizedHeights = ReadFloats(reader, vertices),
            Ocean = ReadArray(reader, vertices, 1, ReadFlag, exact: true),
            Passes = new List<GeodesicValleyCarving.Result>(
                ReadArray(reader, passes, 4, r => ReadPass(r, vertices, triangles), exact: true)),
            InitialCatchments = ReadArray(reader, vertices, 12, r =>
            {
                double runoff = r.ReadDouble();
                if (!double.IsFinite(runoff) || runoff < 0) throw new InvalidDataException("Invalid catchment runoff.");
                var catchment = new GeodesicOceanDrainagePlan.Catchment { Runoff = runoff };
                catchment.Parcels.AddRange(ReadArray(r, triangles, 4, p => ReadIndex(p, triangles)));
                return catchment;
            })
        };
        if (!snapshot.Attempted || snapshot.AppliedEdits != snapshot.Passes.Exists(p => p.AffectedVertices > 0) ||
            !snapshot.Ocean.AsSpan().SequenceEqual(ocean))
            throw new InvalidDataException("Invalid carving state or frozen ocean mask.");
        for (int i = 0; i < vertices; i++)
        {
            Vector3 position = snapshot.Positions[i];
            double cut = input[i].magnitude - (double)position.magnitude;
            if (position.sqrMagnitude <= 0 || !float.IsFinite(position.sqrMagnitude) ||
                cut < -0.00001d || cut > maximumIncision ||
                (position.normalized - input[i].normalized).sqrMagnitude > 1e-10f ||
                snapshot.NormalizedHeights[i] < 0 || snapshot.NormalizedHeights[i] > 1 ||
                (!snapshot.AppliedEdits && !position.Equals(input[i])))
                throw new InvalidDataException("Snapshot terrain is inconsistent with the pre-carving surface.");
        }
        return snapshot;
    }

    private static void WritePass(BinaryWriter writer, GeodesicValleyCarving.Result pass)
    {
        WriteFloats(writer, pass.Heights);
        writer.Write(pass.Eligible);
        writer.Write(pass.Searched);
        writer.Write(pass.SkippedLake);
        writer.Write(pass.SkippedDuplicate);
        writer.Write(pass.SkippedUnsupported);
        writer.Write(pass.MatchedToBasin);
        writer.Write(pass.SpillDirected);
        writer.Write(pass.FallbackSearched);
        writer.Write(pass.InvalidSpills);
        writer.Write(pass.AffectedVertices);
        writer.Write(pass.AffectedFacets);
        writer.Write(pass.OceanRooted);
        writer.Write(pass.Unrooted);
        writer.Write(pass.SelectionConflicts);
        writer.Write(pass.CandidateBudgetSkipped);
        writer.Write(pass.VerificationBuilds);
        writer.Write(pass.UnrootedMissing);
        writer.Write(pass.UnrootedMissingPhysical);
        writer.Write(pass.UnrootedMissingBudget);
        writer.Write(pass.UnrootedCycles);
        writer.Write(pass.UnrootedUnknown);
        writer.Write(pass.ChainBudgetRejected);
        writer.Write(pass.PhysicalInvalidEdges);
        writer.Write(pass.MaximumCut);
        writer.Write(pass.MeanCut);
        writer.Write(pass.PathLength);
        writer.Write(pass.CoastalSubmergenceLimit);
        writer.Write(pass.EligibleIndependentRunoff);
        writer.Write(pass.EligibleTinyNoiseRunoff);
        WriteArray(writer, pass.Candidates, WriteCandidate);
        WriteArray(writer, pass.Chains, (w, chain) =>
        {
            w.Write(chain.SourceId);
            WriteIndices(w, chain.Edges);
            w.Write(chain.AddedCost);
            w.Write(chain.AddedRunoff);
        });
    }

    private static GeodesicValleyCarving.Result ReadPass(BinaryReader reader, int vertices, int triangles)
    {
        var pass = new GeodesicValleyCarving.Result
        {
            Heights = ReadFloats(reader, vertices),
            Eligible = reader.ReadInt32(),
            Searched = reader.ReadInt32(),
            SkippedLake = reader.ReadInt32(),
            SkippedDuplicate = reader.ReadInt32(),
            SkippedUnsupported = reader.ReadInt32(),
            MatchedToBasin = reader.ReadInt32(),
            SpillDirected = reader.ReadInt32(),
            FallbackSearched = reader.ReadInt32(),
            InvalidSpills = reader.ReadInt32(),
            AffectedVertices = reader.ReadInt32(),
            AffectedFacets = reader.ReadInt32(),
            OceanRooted = reader.ReadInt32(),
            Unrooted = reader.ReadInt32(),
            SelectionConflicts = reader.ReadInt32(),
            CandidateBudgetSkipped = reader.ReadInt32(),
            VerificationBuilds = reader.ReadInt32(),
            UnrootedMissing = reader.ReadInt32(),
            UnrootedMissingPhysical = reader.ReadInt32(),
            UnrootedMissingBudget = reader.ReadInt32(),
            UnrootedCycles = reader.ReadInt32(),
            UnrootedUnknown = reader.ReadInt32(),
            ChainBudgetRejected = reader.ReadInt32(),
            PhysicalInvalidEdges = reader.ReadInt32(),
            MaximumCut = reader.ReadDouble(),
            MeanCut = reader.ReadDouble(),
            PathLength = reader.ReadDouble(),
            CoastalSubmergenceLimit = reader.ReadDouble(),
            EligibleIndependentRunoff = reader.ReadDouble(),
            EligibleTinyNoiseRunoff = reader.ReadDouble(),
        };
        pass.Candidates.AddRange(ReadArray(reader, GeodesicOceanDrainagePlan.MaximumCandidates, 100,
            r => ReadCandidate(r, vertices, triangles)));
        pass.Chains.AddRange(ReadArray(reader, GeodesicOceanDrainagePlan.MaximumCandidates, 24, r =>
            new GeodesicOceanDrainagePlan.Chain
            {
                SourceId = ReadIndex(r, vertices),
                Edges = ReadArray(r, GeodesicOceanDrainagePlan.MaximumChainEdges, 4, e => ReadIndex(e, vertices)),
                AddedCost = r.ReadDouble(),
                AddedRunoff = r.ReadDouble()
            }));
        if (pass.AffectedVertices < 0 || pass.AffectedVertices > vertices || pass.AffectedFacets < 0 ||
            pass.AffectedFacets > triangles)
            throw new InvalidDataException("Invalid carving pass dimensions.");
        return pass;
    }

    private static void WriteCandidate(BinaryWriter writer, GeodesicValleyCarving.Candidate candidate)
    {
        WriteVector(writer, candidate.Origin);
        WriteVector(writer, candidate.TargetPoint);
        WriteArray(writer, candidate.Path, WriteVector);
        WriteString(writer, candidate.Rejection);
        WriteString(writer, candidate.ProfileFailure);
        WriteString(writer, candidate.VerificationFailure);
        WriteString(writer, candidate.FallbackReason);
        writer.Write(candidate.RequiresRetrace);
        writer.Write(candidate.RequiresCollectiveTrace);
        writer.Write(candidate.SpillDirected);
        writer.Write(candidate.CoastalAttempt);
        writer.Write(candidate.Length);
        writer.Write(candidate.MaximumCut);
        writer.Write(candidate.MeanCut);
        writer.Write(candidate.OriginalSpillBarrier);
        writer.Write(candidate.CoastalLengthLimit);
        writer.Write(candidate.IndependentRunoff);
        writer.Write(candidate.TinyNoiseRunoff);
        writer.Write(candidate.AffectedVertices);
        writer.Write(candidate.AffectedFacets);
        writer.Write(candidate.BasinId);
        writer.Write(candidate.DownstreamBasinId);
        writer.Write(candidate.TargetKind);
        WritePoint(writer, candidate.SourceFloor);
        WriteIndices(writer, candidate.ParcelIds);
        // Incoming paths are only used after planning for accepted coastal verification.
        var incoming = candidate.Accepted && candidate.CoastalAttempt ? candidate.Incoming : null;
        writer.Write(incoming?.Count ?? 0);
        if (incoming != null)
            foreach (var path in incoming) WriteArray(writer, path, WritePoint);
    }

    private static GeodesicValleyCarving.Candidate ReadCandidate(BinaryReader reader, int vertices, int triangles)
    {
        var candidate = new GeodesicValleyCarving.Candidate
        {
            Origin = ReadVector(reader),
            TargetPoint = ReadVector(reader),
            Path = ReadArray(reader, vertices, 12, ReadVector),
            Rejection = ReadString(reader),
            ProfileFailure = ReadString(reader),
            VerificationFailure = ReadString(reader),
            FallbackReason = ReadString(reader),
            RequiresRetrace = ReadFlag(reader),
            RequiresCollectiveTrace = ReadFlag(reader),
            SpillDirected = ReadFlag(reader),
            CoastalAttempt = ReadFlag(reader),
            Length = reader.ReadDouble(),
            MaximumCut = reader.ReadDouble(),
            MeanCut = reader.ReadDouble(),
            OriginalSpillBarrier = reader.ReadDouble(),
            CoastalLengthLimit = reader.ReadDouble(),
            IndependentRunoff = reader.ReadDouble(),
            TinyNoiseRunoff = reader.ReadDouble(),
            AffectedVertices = reader.ReadInt32(),
            AffectedFacets = reader.ReadInt32(),
            BasinId = reader.ReadInt32(),
            DownstreamBasinId = reader.ReadInt32(),
            TargetKind = reader.ReadInt32(),
            SourceFloor = ReadPoint(reader, triangles),
            ParcelIds = ReadArray(reader, triangles, 4, r => ReadIndex(r, triangles)),
            Incoming = new List<TinPoint[]>(ReadArray(reader, triangles, 4,
                r => ReadArray(r, triangles, 28, p => ReadPoint(p, triangles)))),
        };
        if (candidate.BasinId < 0 || candidate.BasinId >= vertices || candidate.DownstreamBasinId < -2 ||
            candidate.DownstreamBasinId >= vertices || candidate.Incoming.Exists(path => path.Length == 0))
            throw new InvalidDataException("Invalid carving candidate references.");
        return candidate;
    }

    internal static void WriteFloats(BinaryWriter writer, float[] values) =>
        WriteArray(writer, values, (w, value) => w.Write(value));

    private static float[] ReadFloats(BinaryReader reader, int count) =>
        ReadArray(reader, count, 4, ReadFloat, exact: true);

    private static float ReadFloat(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite snapshot value.");
        return value;
    }

    private static void WriteIndices(BinaryWriter writer, IReadOnlyList<int> values) =>
        WriteArray(writer, values, (w, value) => w.Write(value));

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static Vector3 ReadVector(BinaryReader reader) =>
        new Vector3(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));

    private static void WritePoint(BinaryWriter writer, TinPoint value)
    {
        writer.Write(value.Triangle);
        writer.Write(value.A);
        writer.Write(value.B);
        writer.Write(value.C);
    }

    private static TinPoint ReadPoint(BinaryReader reader, int triangles)
    {
        int triangle = ReadIndex(reader, triangles);
        double a = reader.ReadDouble(), b = reader.ReadDouble(), c = reader.ReadDouble();
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c))
            throw new InvalidDataException("Invalid snapshot barycentrics.");
        return new TinPoint(triangle, a, b, c);
    }

    private static int ReadIndex(BinaryReader reader, int limit)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value >= limit) throw new InvalidDataException("Snapshot index is out of range.");
        return value;
    }

    private static bool ReadFlag(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        if (value > 1) throw new InvalidDataException("Invalid snapshot flag.");
        return value == 1;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (value == null) { writer.Write(-1); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumEvidenceStringBytes) throw new InvalidDataException("Oversized carving evidence.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count == -1) return null;
        CheckCount(reader, count, MaximumEvidenceStringBytes, 1, false);
        return Encoding.UTF8.GetString(reader.ReadBytes(count));
    }

    private static void WriteArray<T>(BinaryWriter writer, IReadOnlyList<T> values, Action<BinaryWriter, T> write)
    {
        writer.Write(values.Count);
        for (int i = 0; i < values.Count; i++) write(writer, values[i]);
    }

    private static T[] ReadArray<T>(BinaryReader reader, int limit, int minimumElementBytes,
        Func<BinaryReader, T> read, bool exact = false)
    {
        int count = reader.ReadInt32();
        CheckCount(reader, count, limit, minimumElementBytes, exact);
        var values = new T[count];
        for (int i = 0; i < count; i++) values[i] = read(reader);
        return values;
    }

    private static void CheckCount(BinaryReader reader, int count, int limit, int minimumElementBytes, bool exact)
    {
        if (count < 0 || count > limit || (exact && count != limit) ||
            (long)count * minimumElementBytes > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Snapshot record count is out of bounds.");
    }
}
