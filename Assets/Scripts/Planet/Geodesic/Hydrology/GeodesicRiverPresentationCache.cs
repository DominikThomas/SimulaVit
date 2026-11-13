using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using S = GeodesicSnapshotIO;
using Debug = UnityEngine.Debug;

public sealed partial class GeodesicRiverSystem
{
    // Independent of Milestone 2. Bump the algorithm for ribbon/draping/outlet
    // mathematics, and the schema for payload changes. No assembly MVID in keys.
    private const int RiverPresentationStage = 6, RiverPresentationSchema = 1, RiverPresentationAlgorithm = 1;
    private const int MaximumPresentationVertices = 4_000_000, MaximumPresentationIndices = 24_000_000;
    private SnapshotRecord presentationAuthority;
    private GeodesicTinHydrology presentationTin;
    private GeodesicTinLakeRouting presentationRouting;
    private GeodesicTinSedimentCorridors presentationCorridors;
    private byte[] presentationBands, lastPresentationKey, lastPresentationHash;
    private Color presentationBaseColour;
    private List<Color> presentationBuildColours;
    private List<byte> presentationBuildBands;
    private bool forcePresentationRebuild;
    private readonly List<(int Source, int First, int Count, int FirstIndex)> presentationReaches = new List<(int, int, int, int)>();
    private readonly List<RiverTerminationDebugPoint> presentationTerminations = new List<RiverTerminationDebugPoint>();
    private readonly List<(int Network, byte Kind)> presentationObservations = new List<(int, byte)>();
    private readonly List<string> presentationMessages = new List<string>();
    private PresentationTimings presentationTimes;

    private sealed class PresentationTimings
    {
        internal double Authority, Paths, CrossingBuild, Meanders, Containment, Collisions, Mesh, Diagnostics;
        internal long RibbonTicks, PathTerrainTicks, AuditCompleteTicks;
        internal static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        internal string Summary(GeodesicRiverOutletAudit audit, bool hit) => FormattableString.Invariant(
            $"[GeodesicRiverPresentationTiming] hit={hit} authorityAndSurfacesMs={Authority:F2} geometryGenerationMs={Paths + CrossingBuild + Meanders + Milliseconds(RibbonTicks):F2} pathPreparationMs={Paths:F2} centrelineTerrainSamplingMs={Milliseconds(PathTerrainTicks):F2} crossingBuildMs={CrossingBuild:F2} meandersMs={Meanders:F2} containmentMs={Containment:F2} collisionsMs={Collisions:F2} terrainAndRibbonVerticesMs={Milliseconds(RibbonTicks):F2} meshUploadMs={Mesh:F2} outletCaptureMs={Milliseconds(audit?.CaptureTicks ?? 0):F2} outletAuditMs={Milliseconds(AuditCompleteTicks):F2} displayDiagnosticsMs={Diagnostics:F2} constraintAndCentrelineTimingsAreSubsets=true");
    }

    private sealed class PresentationRecord
    {
        internal byte[] Key;
        internal string Path;
        internal string Name => BitConverter.ToString(Key).Replace("-", "").ToLowerInvariant();
    }

    private void BindPresentationAuthority(SnapshotRecord record)
    {
        if (record?.Digest != null && TinLakeRouting != null && SedimentCorridors != null)
        {
            presentationAuthority = record; presentationTin = TinDrainage;
            presentationRouting = TinLakeRouting; presentationCorridors = SedimentCorridors;
        }
        if (!ReferenceEquals(presentationTin, TinDrainage) || !ReferenceEquals(presentationRouting, TinLakeRouting) ||
            !ReferenceEquals(presentationCorridors, SedimentCorridors)) presentationAuthority = null;
    }

    private PresentationRecord RiverPresentationRecord()
    {
        // Unintegrated historical rendering can mutate raw termination fields.
        // Only a verified completed authority record is a safe parent proof.
        if (presentationAuthority?.Digest == null || DrainageDelivery == null || !planet.enableGeodesicRivers ||
            showRejectedUphillRiverSegments || TinLakeRouting == null || lakeSediment == null)
        {
            Debug.Log("[GeodesicRiverPresentationCache] bypass=no completed authority proof, disabled rivers, or rejected-path capture requested", this);
            return null;
        }
        if (DrainageDelivery.CycleNodes != 0 || DrainageDelivery.DuplicateMemberships != 0 || DrainageDelivery.InvalidMemberships != 0 ||
            Math.Abs(DrainageDelivery.Residual) > DrainageDelivery.Tolerance)
            return null;
        var watch = Stopwatch.StartNew();
        byte[] key = PlanetGenerationCache.HashBinaryKey(w =>
        {
            S.RuntimeKey(w); w.Write(RiverPresentationStage); w.Write(RiverPresentationSchema); w.Write(RiverPresentationAlgorithm);
            w.Write(GeodesicTinLakeCrossings.AlgorithmVersion);
            presentationAuthority.WriteDependency(w);
            w.Write(TinDrainage.SubdivisionLevel); w.Write(planet.BasePlanetRadius);
            w.Write(planet.DerivedVisualSeed); w.Write(ResolvedDrainageAreaThreshold);
            w.Write(riverFlowThreshold); w.Write(majorRiverThreshold);
            w.Write(Mathf.Max(.00001f, widthAtThreshold)); w.Write(Mathf.Max(1f, maximumWidthMultiplier));
            w.Write(Mathf.Max(.000001f, surfaceOffset)); w.Write(drawRawTrianglePaths);
            w.Write(planet.minimumLakeBasinAreaFraction); w.Write(planet.minimumLakeDepth);
            S.Ints(w, tinSimulationCells);
        });
        var record = new PresentationRecord { Key = key, Path = PlanetGenerationCache.GeodesicPath(RiverPresentationStage, key) };
        Debug.Log(FormattableString.Invariant($"[GeodesicRiverPresentationCache] key={record.Name} schema={RiverPresentationSchema} algorithm={RiverPresentationAlgorithm} meanders={GeodesicTinLakeCrossings.AlgorithmVersion} keyMs={watch.Elapsed.TotalMilliseconds:F2}"), this);
        return record;
    }

    private sealed class PresentationData
    {
        internal Vector3[] Positions, Normals;
        internal Vector2[] UV;
        internal int[] Indices;
        internal byte[] Bands, Metadata, GeometryHash;
        internal Bounds Bounds;
        internal long RecordBytes;
        internal PresentationMetadata Decoded;

        internal void MeshBody(BinaryWriter w)
        {
            S.Write(w, Positions, S.Vector); S.Write(w, Normals, S.Vector);
            S.Write(w, UV, (b, p) => { b.Write(p.x); b.Write(p.y); });
            S.Ints(w, Indices); w.Write(Bands.Length); w.Write(Bands);
            S.Vector(w, Bounds.center); S.Vector(w, Bounds.size);
        }
        internal void Write(BinaryWriter w)
        {
            w.Write(GeometryHash); MeshBody(w); w.Write(Metadata.Length); w.Write(Metadata);
        }
        internal static PresentationData Read(BinaryReader r, GeodesicRiverSystem owner)
        {
            var data = new PresentationData { GeometryHash = r.ReadBytes(32), RecordBytes = r.BaseStream.Length };
            S.Require(data.GeometryHash.Length == 32, "Missing reference geometry digest.");
            data.Positions = S.Read(r, MaximumPresentationVertices, 12, S.Vector);
            S.Require(data.Positions.Length > 0, "Empty river presentation.");
            data.Normals = S.Read(r, data.Positions.Length, 12, S.Vector, true);
            data.UV = S.Read(r, data.Positions.Length, 8, b => new Vector2(S.Float(b), S.Float(b)));
            S.Require(data.UV.Length == 0 || data.UV.Length == data.Positions.Length, "Invalid river UV channel.");
            data.Indices = S.Read(r, MaximumPresentationIndices, 4, b => S.Id(b, data.Positions.Length));
            S.Require(data.Indices.Length % (owner.drawRawTrianglePaths ? 2 : 3) == 0, "Invalid river primitive count.");
            S.Require(S.Count(r, data.Positions.Length, 1) == data.Positions.Length, "Missing river colour classes.");
            data.Bands = r.ReadBytes(data.Positions.Length);
            S.Require(data.Bands.All(p => p <= 1), "Invalid river colour class.");
            Vector3 centre = S.Vector(r), size = S.Vector(r);
            S.Require(size.x >= 0 && size.y >= 0 && size.z >= 0, "Invalid river bounds.");
            data.Bounds = new Bounds(centre, size);
            S.Require(PlanetGenerationCache.HashBinaryKey(data.MeshBody).AsSpan().SequenceEqual(data.GeometryHash), "River geometry differs from its recorded uncached output.");
            data.Metadata = r.ReadBytes(S.Count(r, 64 * 1024 * 1024, 1));
            using var stream = new MemoryStream(data.Metadata, false);
            using var reader = new BinaryReader(stream);
            data.Decoded = PresentationMetadata.Read(reader, owner, data.Positions.Length, data.Indices.Length);
            S.Require(stream.Position == stream.Length, "Trailing river presentation metadata.");
            return data;
        }
    }

    private bool TryRestoreRiverPresentation(PresentationRecord record, GeodesicDrainageDelivery observationDelivery)
    {
        if (record == null) return false;
        if (forcePresentationRebuild)
        { Debug.Log($"[GeodesicRiverPresentationCache] miss key={record.Name} reason=manual-regeneration", this); return false; }
        var watch = Stopwatch.StartNew(); double validationMs = 0;
        var data = PlanetGenerationCache.LoadGeodesic(record.Path, record.Key, RiverPresentationStage, RiverPresentationSchema,
            TinDrainage.BasinVertexCount, TinDrainage.TriangleCount, r =>
            {
                var validate = Stopwatch.StartNew();
                var value = PresentationData.Read(r, this); validationMs = validate.Elapsed.TotalMilliseconds; return value;
            });
        double readMs = watch.Elapsed.TotalMilliseconds;
        if (data == null) { Debug.Log($"[GeodesicRiverPresentationCache] miss key={record.Name}", this); return false; }
        watch.Restart();
        data.Decoded.Apply(this, observationDelivery);
        presentationBands = data.Bands; RecolourPresentation();
        RestorePresentationDiagnostics(data.Positions, data.Indices);
        var meshWatch = Stopwatch.StartNew();
        BuildRiverPresentationMesh(data.Positions, data.Indices, data.Normals, data.Bounds, data.UV);
        presentationTimes.Mesh = meshWatch.Elapsed.TotalMilliseconds;
        var overlayWatch = Stopwatch.StartNew();
        ApplyPresentationGradeOverlay();
        presentationTimes.Diagnostics += overlayWatch.Elapsed.TotalMilliseconds;
        lastPresentationKey = record.Key; lastPresentationHash = data.GeometryHash;
        foreach (string message in presentationMessages) Debug.Log(message, this);
        if (logDrainageDelivery && OutletAudit != null) Debug.Log(OutletAudit.Summary, this);
        Debug.Log(FormattableString.Invariant($"[GeodesicRiverPresentationCache] hit key={record.Name} bytes={data.RecordBytes} readAndChecksumMs={readMs - validationMs:F2} validationDecodeMs={validationMs:F2} reconstructionMs={watch.Elapsed.TotalMilliseconds:F2} meshes=1 vertices={data.Positions.Length} displayedVertices={riverMesh.vertexCount} triangles={(drawRawTrianglePaths ? 0 : data.Indices.Length / 3)} estimatedMeshCpuBytes={40L * riverMesh.vertexCount + 4L * data.Indices.Length + 8L * data.UV.Length} managedBaseColourBytes={17L * data.Positions.Length} recordedUncachedGeometryMatch=true liveRegenerationCompared=false geometryHash={Hex(data.GeometryHash)} audit=cached-completed-proof authorityValidation=current"), this);
        return true;
    }

    private void SaveRiverPresentation(PresentationRecord record, int[] diagnosticBase)
    {
        if (record == null || riverMesh == null || OutletAudit == null || presentationBuildBands == null) return;
        var watch = Stopwatch.StartNew();
        presentationBands = presentationBuildBands.ToArray();
        var data = new PresentationData { Positions = riverMesh.vertices, Normals = riverMesh.normals, UV = riverMesh.uv,
            Indices = riverMesh.GetIndices(0), Bands = presentationBands, Bounds = riverMesh.bounds };
        S.Require(riverMesh.tangents.Length == 0 && data.Positions.Length <= MaximumPresentationVertices &&
            data.Indices.Length <= MaximumPresentationIndices, "Unsupported river mesh attributes or size.");
        S.Require(data.Positions.Length == data.Bands.Length, "River colour classes do not cover the mesh.");
        data.GeometryHash = PlanetGenerationCache.HashBinaryKey(data.MeshBody);
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) WritePresentationMetadata(writer, diagnosticBase);
            data.Metadata = stream.ToArray();
        }
        bool comparable = lastPresentationKey != null && record.Key.AsSpan().SequenceEqual(lastPresentationKey);
        bool matches = comparable && data.GeometryHash.AsSpan().SequenceEqual(lastPresentationHash);
        long geometryBytes = 25L * data.Positions.Length + 8L * data.UV.Length + 4L * data.Indices.Length;
        S.Require(data.Metadata.Length <= 64 * 1024 * 1024 && 80L + geometryBytes + data.Metadata.Length <= 128L * 1024 * 1024,
            "River presentation exceeds its metadata or the existing 128 MiB per-record limit.");
        Debug.Log(FormattableString.Invariant($"[GeodesicRiverPresentationCache] generated key={record.Name} captureMs={watch.Elapsed.TotalMilliseconds:F2} meshes=1 vertices={data.Positions.Length} triangles={(drawRawTrianglePaths ? 0 : data.Indices.Length / 3)} geometryHash={Hex(data.GeometryHash)} liveRegenerationCompared={comparable} liveRegenerationMatchesRecorded={matches} recordBytes={176L + geometryBytes + data.Metadata.Length} geometryCpuBytes={geometryBytes} metadataBytes={data.Metadata.Length} estimatedMeshCpuBytes={40L * data.Positions.Length + 4L * data.Indices.Length + 8L * data.UV.Length} managedBaseColourBytes={17L * data.Positions.Length}"), this);
        if (comparable && !matches)
        { Debug.LogWarning("[GeodesicRiverPresentationCache] reference mismatch; the existing cache is retained for manual review.", this); return; }
        lastPresentationKey = record.Key; lastPresentationHash = data.GeometryHash;
        string path = record.Path; byte[] key = record.Key;
        int vertices = TinDrainage.BasinVertexCount, triangles = TinDrainage.TriangleCount;
        // Frozen arrays and bytes only: publication cannot retain a Mesh, Material,
        // GameObject, current-planet callback, or live mutable routing object.
        planet.QueueGeodesicSnapshot(() => PlanetGenerationCache.SaveGeodesic(path, key, RiverPresentationStage,
            RiverPresentationSchema, vertices, triangles, data.Write));
    }

    [ContextMenu("Rebuild River Presentation And Compare Cache")]
    private void CompareRiverPresentationWithCache()
    {
        forcePresentationRebuild = true;
        try { RebuildVisuals(); } finally { forcePresentationRebuild = false; }
    }

    [ContextMenu("Refresh River Colours Only")]
    private void RefreshRiverPresentationColours()
    { if (riverMesh != null && presentationBands != null) { RecolourPresentation(); destinationRiverColours = null; ApplyRiverColours(); } }

    private static string Hex(byte[] digest) => BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
    private void RecolourPresentation()
    {
        if (presentationBands == null) return;
        var previous = normalRiverColours;
        normalRiverColours = new List<Color>(presentationBands.Length);
        foreach (byte band in presentationBands) normalRiverColours.Add(band == 0 ? riverColor : Color.Lerp(riverColor, Color.cyan, .3f));
        // The optional grade overlay appends severity-coloured vertices. Their
        // colours do not depend on riverColor; preserve them during recolouring.
        if (previous != null)
            for (int i = presentationBands.Length; i < previous.Count; i++) normalRiverColours.Add(previous[i]);
        presentationBaseColour = riverColor;
    }
    private void RecordPresentationColour(List<Color> colours, bool major, int count)
    {
        if (!ReferenceEquals(colours, presentationBuildColours)) return;
        for (int i = 0; i < count; i++) presentationBuildBands.Add(major ? (byte)1 : (byte)0);
    }
    private void ObservePresentationBreak(GeodesicDrainageDelivery delivery, int network, byte kind)
    {
        presentationObservations.Add((network, kind));
        if (delivery == null) return;
        if (kind <= 1) delivery.ObserveShortLink(kind == 1);
        else delivery.ObserveVisualBreak(network, kind == 2 ? "centreline" : kind == 3 ? "projection" : "missing-trunk");
    }

    private bool PresentationCentrelineValid(Vector3[] path)
    {
        long started = Stopwatch.GetTimestamp();
        try { return terrain.IsCentrelineValid(path, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance); }
        finally { presentationTimes.PathTerrainTicks += Stopwatch.GetTimestamp() - started; }
    }

    private void BuildRiverPresentationMesh(Vector3[] positions, int[] indices, Vector3[] normals = null, Bounds? bounds = null, Vector2[] uv = null)
    {
        riverMesh = new Mesh { name = drawRawTrianglePaths ? "Raw TIN Gradient Rivers" : "TIN River Ribbons", indexFormat = IndexFormat.UInt32 };
        riverMesh.SetVertices(positions); riverMesh.SetIndices(indices, drawRawTrianglePaths ? MeshTopology.Lines : MeshTopology.Triangles, 0);
        if (uv != null && uv.Length > 0) riverMesh.uv = uv;
        ApplyRiverColours();
        if (normals != null) riverMesh.SetNormals(normals);
        else if (!drawRawTrianglePaths) riverMesh.RecalculateNormals();
        else riverMesh.SetNormals(Array.ConvertAll(positions, p => p.normalized));
        if (bounds.HasValue) riverMesh.bounds = bounds.Value; else riverMesh.RecalculateBounds();
        visualRoot = new GameObject(drawRawTrianglePaths ? "Raw TIN Gradient Rivers" : "TIN River Ribbons");
        visualRoot.layer = gameObject.layer; visualRoot.transform.SetParent(transform, false);
        visualRoot.AddComponent<MeshFilter>().sharedMesh = riverMesh;
        var renderer = visualRoot.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
        if (shader != null)
        { riverMaterial = new Material(shader) { name = "TIN Rivers (Runtime)" }; riverMaterial.SetFloat("_AmbientStrength", 1f); riverMaterial.SetFloat("_DiffuseStrength", 0f); renderer.sharedMaterial = riverMaterial; }
        else Debug.LogError("[GeodesicTIN] River shader is missing.", this);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        visualRoot.SetActive(showRivers && planet.enableGeodesicRivers && isActiveAndEnabled);
    }

    private void RestorePresentationDiagnostics(Vector3[] positions, int[] indices)
    {
        var watch = Stopwatch.StartNew();
        if (colourRenderedRiversByVisibleTerrainGrade || showRiverTerminationReasons)
        {
            VisibleGradeAudit = new GeodesicRiverVisibleGradeAudit();
            riverTerminationPoints = new List<RiverTerminationDebugPoint>(presentationTerminations);
            var points = new List<Vector3>(positions);
            foreach (var reach in presentationReaches)
                VisibleGradeAudit.ObserveEmittedPath(reach.Source, points, reach.First, reach.Count, drawRawTrianglePaths, reach.FirstIndex, terrain, planet.BasePlanetRadius);
            VisibleGradeAudit.Resolve(planet.BasePlanetRadius);
            VisibleGradeAudit.ClosedBasinTerminations = TinDiagnostics.ClosedBasinTerminations;
            VisibleGradeAudit.UnexpectedTerminations = TinDiagnostics.UnexpectedBrokenNetworks;
            foreach (var end in riverTerminationPoints)
            { if (end.Reason == TinTermination.TrueClosedBasin) VisibleGradeAudit.ClosedBasinMarkers++; if (end.Reason == TinTermination.RoutingFailure) VisibleGradeAudit.UnexpectedMarkers++; }
        }
        presentationTimes.Diagnostics = watch.Elapsed.TotalMilliseconds;
    }

    private void ApplyPresentationGradeOverlay()
    {
        if (!colourRenderedRiversByVisibleTerrainGrade || VisibleGradeAudit == null || riverMesh == null) return;
        // Preserve the existing diagnostic's duplicated vertices and normals,
        // but keep this cosmetic overlay out of the reusable normal mesh record.
        var vertices = new List<Vector3>(riverMesh.vertices);
        var indices = new List<int>(riverMesh.GetIndices(0));
        VisibleGradeAudit.ApplyColours(vertices, indices, normalRiverColours);
        riverMesh.Clear();
        riverMesh.indexFormat = IndexFormat.UInt32;
        riverMesh.SetVertices(vertices); riverMesh.SetIndices(indices.ToArray(), drawRawTrianglePaths ? MeshTopology.Lines : MeshTopology.Triangles, 0);
        destinationRiverColours = meanderDiagnosticColours = null;
        ApplyRiverColours();
        if (!drawRawTrianglePaths) riverMesh.RecalculateNormals();
        else riverMesh.SetNormals(vertices.ConvertAll(p => p.normalized));
        riverMesh.RecalculateBounds();
    }

    private void WritePresentationMetadata(BinaryWriter w, int[] before)
    {
        w.Write(visibleReaches); w.Write(riverMouths);
        w.Write(TinDiagnostics.RejectedPostProcessing - before[0]); w.Write(TinDiagnostics.UnexpectedBrokenNetworks - before[1]);
        w.Write(TinDiagnostics.UnexpectedInlandBreaks - before[2]); w.Write(TinDiagnostics.Confluences - before[3]);
        S.Write(w, mouths, (b, p) =>
        {
            b.Write(p.UpstreamCell); b.Write(p.OceanCell); b.Write(p.DrainageArea); b.Write(p.AccumulatedRunoff);
            b.Write(p.Strength); b.Write(p.HasVisibleCoastPosition); S.Vector(b, p.LocalCoastPosition);
        });
        S.Write(w, paths.OrderBy(p => p.Key).ToArray(), (b, p) => { b.Write(p.Key); S.Write(b, p.Value, S.Vector); });
        S.Write(w, riverColourSpans, (b, span) =>
        {
            S.Text(b, span.Route); b.Write(span.Start); b.Write(span.End); b.Write((byte)span.Kind);
            b.Write((byte)DrainageDelivery.RiverOutcome(span.Route).RiverColour);
            b.Write(span.Crossing != null);
            if (span.Crossing == null) return;
            var c = span.Crossing;
            b.Write(c.Bed.Id); S.Write(b, c.Path, Vector2Value); S.Write(b, c.FixedPoints ?? Array.Empty<Vector2>(), Vector2Value);
            b.Write(c.Discharge); b.Write(c.DischargeAtInlet); b.Write(c.Length); b.Write(c.Width);
            b.Write(c.PeakOffsetWidths); b.Write(c.MeanOffsetWidths); b.Write(c.PeakLengthFraction);
            b.Write(c.FixedLengthFraction); b.Write(c.BasinWidthUsed); b.Write(c.PinnedSpans); b.Write(c.AttemptFailureMask);
            b.Write(c.HasMeander); b.Write(c.Substantial); S.Vector(b, c.PeakPosition); S.Vector(b, c.PeakBaselinePosition);
        });
        S.Write(w, meanderDiagnosticSpans, (b, p) => { b.Write(p.Start); b.Write(p.End); b.Write((byte)p.Status); });
        S.Write(w, presentationReaches, (b, p) => { b.Write(p.Source); b.Write(p.First); b.Write(p.Count); b.Write(p.FirstIndex); });
        S.Write(w, presentationTerminations, (b, p) => { b.Write(p.SourceTriangle); b.Write((byte)p.Reason); S.Vector(b, p.Position); });
        S.Write(w, presentationObservations, (b, p) => { b.Write(p.Network); b.Write(p.Kind); });
        S.Write(w, presentationMessages, S.Text);
        OutletAudit.WritePresentation(w);
    }

    private static void Vector2Value(BinaryWriter w, Vector2 point) { w.Write(point.x); w.Write(point.y); }
    private static Vector2 Vector2Value(BinaryReader r) => new Vector2(S.Float(r), S.Float(r));

    private sealed class PresentationMetadata
    {
        internal int Visible, MouthCount;
        internal int[] Delta;
        internal GeodesicRiverMouth[] Mouths;
        internal (int Source, Vector3[] Points)[] Paths;
        internal (string Route, int Start, int End, RiverGeometryKind Kind, GeodesicTinLakeCrossings.Crossing Crossing)[] Spans;
        internal (int Start, int End, GeodesicTinLakeCrossings.DiagnosticStatus Status)[] Diagnostics;
        internal (int Source, int First, int Count, int FirstIndex)[] Reaches;
        internal RiverTerminationDebugPoint[] Terminations;
        internal (int Network, byte Kind)[] Observations;
        internal string[] Messages;
        internal GeodesicRiverOutletAudit Audit;

        internal static PresentationMetadata Read(BinaryReader r, GeodesicRiverSystem owner, int vertices, int indices)
        {
            int triangles = owner.TinDrainage.TriangleCount, networks = owner.TinDrainage.Networks.Count;
            int cells = owner.planet.GeodesicTopology.CellCount;
            var m = new PresentationMetadata { Visible = S.Id(r, vertices + 1), MouthCount = S.Id(r, networks + 1), Delta = new int[4] };
            for (int i = 0; i < m.Delta.Length; i++)
            { m.Delta[i] = r.ReadInt32(); S.Require(Math.Abs((long)m.Delta[i]) <= (long)networks * 4, "Invalid presentation observation delta."); }
            m.Mouths = S.Read(r, networks, 37, b => new GeodesicRiverMouth
            {
                UpstreamCell = S.Id(b, cells, -1), OceanCell = S.Id(b, cells, -1), DrainageArea = S.Double(b), AccumulatedRunoff = S.Double(b),
                Strength = S.Float(b), HasVisibleCoastPosition = S.Flag(b), LocalCoastPosition = S.Vector(b)
            });
            S.Require(m.MouthCount == m.Mouths.Length, "Cached mouth count mismatch.");
            m.Paths = S.Read(r, networks, 8, b => (S.Id(b, triangles), S.Read(b, MaximumPresentationVertices, 12, S.Vector)));
            S.Require(m.Paths.All(p => p.Points.Length >= 2) && m.Paths.Select(p => p.Source).Distinct().Count() == m.Paths.Length,
                "Invalid or duplicate cached network path.");
            m.Spans = S.Read(r, vertices, 15, b =>
            {
                string route = S.Text(b);
                S.Require(route != null && owner.DrainageDelivery.OutletLinks.ContainsKey(route), "Unknown cached colour route.");
                int start = S.Id(b, vertices), end = S.Id(b, vertices + 1);
                var kind = (RiverGeometryKind)S.IdByte(b, 5); byte destination = S.IdByte(b, 4);
                S.Require(end > start && (byte)owner.DrainageDelivery.RiverOutcome(route).RiverColour == destination, "Cached river classification mismatch.");
                GeodesicTinLakeCrossings.Crossing c = null;
                if (S.Flag(b))
                {
                    int id = S.Id(b, owner.TinDrainage.BasinVertexCount);
                    S.Require(owner.lakeSediment.Lakes.TryGetValue(id, out var bed), "Cached crossing references an absent sediment bed.");
                    c = new GeodesicTinLakeCrossings.Crossing { Bed = bed,
                        Path = S.Read(b, 100_000, 8, Vector2Value), FixedPoints = S.Read(b, 100_000, 8, Vector2Value),
                        Discharge = S.Double(b), DischargeAtInlet = S.Double(b), Length = S.Float(b), Width = S.Float(b),
                        PeakOffsetWidths = S.Float(b), MeanOffsetWidths = S.Float(b), PeakLengthFraction = S.Float(b),
                        FixedLengthFraction = S.Float(b), BasinWidthUsed = S.Float(b), PinnedSpans = S.Id(b, 100_001), AttemptFailureMask = b.ReadInt32(),
                        HasMeander = S.Flag(b), Substantial = S.Flag(b), PeakPosition = S.Vector(b), PeakBaselinePosition = S.Vector(b) };
                    int allowed = (1 << Enum.GetValues(typeof(GeodesicTinLakeCrossings.MeanderRejection)).Length) - 1;
                    S.Require(c.Path.Length >= 2 && (c.AttemptFailureMask & ~allowed) == 0, "Invalid cached meander metadata.");
                }
                S.Require((kind == RiverGeometryKind.SedimentCrossing) == (c != null), "Cached river producer mismatch.");
                return (route, start, end, kind, c);
            });
            int covered = 0;
            foreach (var span in m.Spans)
            { S.Require(span.Start == covered, "Cached river colour ranges have gaps or overlap."); covered = span.End; }
            S.Require(covered == vertices, "Cached river colour ranges are incomplete.");
            m.Diagnostics = S.Read(r, vertices, 9, b =>
            {
                int start = S.Id(b, vertices), end = S.Id(b, vertices + 1);
                S.Require(end > start, "Invalid diagnostic vertex range.");
                return (start, end, (GeodesicTinLakeCrossings.DiagnosticStatus)S.IdByte(b, 4));
            });
            covered = 0;
            foreach (var span in m.Diagnostics) { S.Require(span.Start >= covered, "Overlapping diagnostic ranges."); covered = span.End; }
            m.Reaches = S.Read(r, networks, 16, b =>
            {
                int source = S.Id(b, triangles), first = S.Id(b, vertices), count = S.Id(b, vertices + 1), index = S.Id(b, indices + 1);
                int stride = owner.drawRawTrianglePaths ? 1 : 3, indexStride = owner.drawRawTrianglePaths ? 2 : 12;
                S.Require(count >= 2 && (long)first + (long)count * stride <= vertices && (long)index + (long)(count - 1) * indexStride <= indices, "Invalid cached grade observation.");
                return (source, first, count, index);
            });
            m.Terminations = S.Read(r, networks, 17, b => new RiverTerminationDebugPoint(S.Id(b, triangles), S.Termination(b), S.Vector(b)));
            m.Observations = S.Read(r, networks * 4, 5, b => (S.Id(b, networks), S.IdByte(b, 5)));
            m.Messages = S.Read(r, 32, 1, S.Text);
            var beds = new HashSet<string>(owner.lakeSediment.Lakes.Keys.Select(id => "bed:" + id));
            m.Audit = GeodesicRiverOutletAudit.ReadPresentation(r, owner.DrainageDelivery, beds, MaximumPresentationVertices);
            return m;
        }

        internal void Apply(GeodesicRiverSystem owner, GeodesicDrainageDelivery observationDelivery)
        {
            owner.visibleReaches = Visible; owner.riverMouths = MouthCount;
            owner.TinDiagnostics.RejectedPostProcessing += Delta[0]; owner.TinDiagnostics.UnexpectedBrokenNetworks += Delta[1];
            owner.TinDiagnostics.UnexpectedInlandBreaks += Delta[2]; owner.TinDiagnostics.Confluences += Delta[3];
            owner.mouths.AddRange(Mouths);
            foreach (var path in Paths) owner.paths.Add(path.Source, path.Points);
            owner.riverColourSpans.AddRange(Spans); owner.meanderDiagnosticSpans.AddRange(Diagnostics);
            owner.presentationReaches.AddRange(Reaches); owner.presentationTerminations.AddRange(Terminations);
            owner.presentationMessages.AddRange(Messages); owner.OutletAudit = Audit;
            foreach (var item in Observations) owner.ObservePresentationBreak(observationDelivery, item.Network, item.Kind);
        }
    }

    private void FinishRiverPresentation(double elapsed, double lakes, double sediment, bool hit)
    {
        TinDiagnostics.RibbonMilliseconds = elapsed;
        Debug.Log(FormattableString.Invariant($"[GeodesicHydrologyVisuals] lakesMs={lakes:F2} sedimentMs={sediment:F2} ribbonsAndOutletAuditMs={elapsed:F2} presentationCacheHit={hit}"), this);
        Debug.Log(presentationTimes.Summary(OutletAudit, hit), this);
        visualMilliseconds = TinDiagnostics.TracingMilliseconds + elapsed;
        riverCells = TinDiagnostics.RiverTriangles; visibleRiverChains = TinDiagnostics.RiverNetworks;
        confluences = TinDiagnostics.Confluences; unexpectedBrokenChains = TinDiagnostics.UnexpectedBrokenNetworks;
        basinTerminatedChains = TinDiagnostics.ClosedBasinTerminations; OceanConnectedChains = TinDiagnostics.OceanTerminations;
        visibleUphillSegments = TinDiagnostics.VisibleMaterialUphillSegments; ridgeCrossings = TinDiagnostics.VisibleRidgeCrossings;
    }
}
