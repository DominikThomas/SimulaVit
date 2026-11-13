using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class GeodesicRiverSystem
{
    private static readonly Color[] DestinationPalette =
    {
        (Color)new Color32(0x24, 0xB9, 0xF0, 0xFF),
        (Color)new Color32(0xE4, 0x48, 0x48, 0xFF),
        (Color)new Color32(0xA4, 0x58, 0xD8, 0xFF),
        (Color)new Color32(0xF0, 0xAB, 0x35, 0xFF)
    };

    private enum RiverGeometryKind { Network, LakeOutlet, SedimentCrossing, SedimentCorridor, Other }
    private readonly List<(string Route, int Start, int End, RiverGeometryKind Kind, GeodesicTinLakeCrossings.Crossing Crossing)> riverColourSpans =
        new List<(string, int, int, RiverGeometryKind, GeodesicTinLakeCrossings.Crossing)>();
    private List<Color> normalRiverColours, destinationRiverColours;
    private List<Color> meanderDiagnosticColours;
    private readonly List<(int Start, int End, GeodesicTinLakeCrossings.DiagnosticStatus Status)> meanderDiagnosticSpans =
        new List<(int, int, GeodesicTinLakeCrossings.DiagnosticStatus)>();
    private GeodesicDrainageDelivery riverColourDelivery;
    public bool ColourRiversByUltimateDestination { get; private set; }
    public bool ColourRiversBySedimentMeanders { get; private set; } // Temporary, runtime-only; off by default.

    [ContextMenu("Enable Sediment Meander Diagnostic Colours")]
    private void EnableSedimentMeanderColours() => SetSedimentMeanderColours(true);

    [ContextMenu("Disable Sediment Meander Diagnostic Colours")]
    private void DisableSedimentMeanderColours() => SetSedimentMeanderColours(false);

    public void SetSedimentMeanderColours(bool enabled)
    {
        ColourRiversBySedimentMeanders = enabled;
        if (riverMesh == null) return;
        normalRiverColours ??= new List<Color>(riverMesh.colors);
        ApplyRiverColours();
        if (enabled) LogSedimentMeanderGeometry();
    }

    [ContextMenu("Toggle Ultimate River Destination Colours")]
    private void ToggleRiverDestinationColours() => SetRiverDestinationColours(!ColourRiversByUltimateDestination);

    public void SetRiverDestinationColours(bool enabled)
    {
        if (enabled == ColourRiversByUltimateDestination) return;
        ColourRiversByUltimateDestination = enabled;
        if (riverMesh != null && TinDrainage != null) ApplyRiverColours();
    }

    private GeodesicDrainageDelivery AnalyzeRiverColours()
    {
        bool replayed = TinDrainage.ParcelDestinations == null;
        TinDrainage.EnsureParcelDestinations();
        try
        {
            return GeodesicDrainageDelivery.Analyze(TinDrainage, routingLakeBasins ?? TinLakeBasins,
                TinLakeRouting?.Lakes, TinLakeRouting?.RiverReaches,
                TinLakeRouting == null && TinLakeGeometry != null && TinLakeGeometry.Bodies.Count > 0,
                deliveryOceanComponentByVertex != null ? DeliveryOceanComponent : (Func<TinPoint, int>)null,
                planet.OceanConnectivity?.PrimaryRetainedComponent ?? -1);
        }
        finally { if (replayed) TinDrainage.ReleaseParcelDestinations(); }
    }

    private void RecordRiverColour(string route, int start, int end, GeodesicTinLakeCrossings.Crossing crossing = null)
    {
        var kind = crossing != null ? RiverGeometryKind.SedimentCrossing : route.StartsWith("network:") ? RiverGeometryKind.Network :
            route.StartsWith("corridor:") ? RiverGeometryKind.SedimentCorridor : route.StartsWith("lake:") ? RiverGeometryKind.LakeOutlet : RiverGeometryKind.Other;
        if (end > start) riverColourSpans.Add((route, start, end, kind, crossing));
    }

    private void RecordMeanderDiagnosticSpan(int start, int end, GeodesicTinLakeCrossings.DiagnosticStatus status)
    {
        if (end <= start) return;
        if (meanderDiagnosticSpans.Count > 0)
        {
            var prior = meanderDiagnosticSpans[meanderDiagnosticSpans.Count - 1];
            if (prior.End == start && prior.Status == status)
            { meanderDiagnosticSpans[meanderDiagnosticSpans.Count - 1] = (prior.Start, end, status); return; }
        }
        meanderDiagnosticSpans.Add((start, end, status));
    }

    private static Color MeanderDiagnosticColour(GeodesicTinLakeCrossings.DiagnosticStatus status)
    {
        Color colour = status == GeodesicTinLakeCrossings.DiagnosticStatus.Meandered ? Color.green :
            status == GeodesicTinLakeCrossings.DiagnosticStatus.Containment ? new Color(1f, .45f, 0f) :
            status == GeodesicTinLakeCrossings.DiagnosticStatus.BranchClearance ? Color.yellow : new Color(.7f, .15f, 1f);
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;
    }

    private void ApplyRiverColours()
    {
        if (presentationBands != null && presentationBaseColour != riverColor)
        { RecolourPresentation(); destinationRiverColours = null; }
        if (ColourRiversBySedimentMeanders)
        {
            if (meanderDiagnosticColours == null)
            {
                meanderDiagnosticColours = Enumerable.Repeat(Color.cyan, normalRiverColours.Count).ToList();
                // Non-network producers are purple until an emitted bed segment
                // supplies its actual outcome. No geometry or material is replaced.
                foreach (var span in riverColourSpans)
                    if (span.Kind != RiverGeometryKind.Network)
                        for (int i = span.Start; i < span.End; i++) meanderDiagnosticColours[i] = MeanderDiagnosticColour(GeodesicTinLakeCrossings.DiagnosticStatus.Other);
                foreach (var span in meanderDiagnosticSpans)
                    for (int i = span.Start; i < span.End; i++) meanderDiagnosticColours[i] = MeanderDiagnosticColour(span.Status);
            }
            riverMesh.SetColors(meanderDiagnosticColours);
            return; // This override wins over both destination and grade colours.
        }
        if (!ColourRiversByUltimateDestination || TinDrainage == null)
        {
            riverMesh.SetColors(normalRiverColours);
            return;
        }

        if (destinationRiverColours == null)
        {
            riverColourDelivery ??= AnalyzeRiverColours();
            destinationRiverColours = new List<Color>(normalRiverColours);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var span in riverColourSpans)
            {
                Color colour = DestinationPalette[(int)riverColourDelivery.RiverOutcome(span.Route).RiverColour];
                if (linear) colour = colour.linear;
                for (int i = span.Start; i < span.End; i++) destinationRiverColours[i] = colour;
            }
            string summary = riverColourDelivery.RiverDestinationSummary();
            if (riverColourDelivery.RiverDestinationAccountingPass) Debug.Log(summary, this);
            else Debug.LogWarning(summary, this);
        }
        riverMesh.SetColors(destinationRiverColours);
    }

    [ContextMenu("Log Longest Rendered River Geometry")]
    private void LogSedimentMeanderGeometry()
    {
        if (riverMesh == null) return;
        var positions = riverMesh.vertices;
        Debug.Log("[SedimentMeanderColours] green=changed-channel orange=boundary-constrained yellow=branch/topology-constrained purple=fixed/other-producer cyan=ordinary-network. Inspect River At Scene View Centre identifies the exact producer; colours override grade/destination while enabled.", this);
        foreach (var group in riverColourSpans.GroupBy(p => p.Kind))
        {
            var largest = group.Select(span =>
            {
                var bounds = new Bounds(positions[span.Start], Vector3.zero);
                for (int i = span.Start + 1; i < span.End; i++) bounds.Encapsulate(positions[i]);
                return (Span: span, Bounds: bounds);
            }).OrderByDescending(p => p.Bounds.size.sqrMagnitude).Take(4);
            foreach (var entry in largest)
                Debug.Log($"[SedimentMeanderGeometry] producer={entry.Span.Kind} route={entry.Span.Route} boundsCentre={entry.Bounds.center} boundsSize={entry.Bounds.size} " + DescribeCrossing(entry.Span.Crossing), this);
        }
    }

    private string DescribeCrossing(GeodesicTinLakeCrossings.Crossing crossing)
    {
        if (crossing == null) return "sedimentCrossing=false";
        string pixels = "unavailable";
        var camera = Camera.main;
        if (camera != null && crossing.PeakOffsetWidths > 0f)
        {
            Vector3 actual = camera.WorldToScreenPoint(transform.TransformPoint(crossing.PeakPosition));
            Vector3 baseline = camera.WorldToScreenPoint(transform.TransformPoint(crossing.PeakBaselinePosition));
            bool inView = actual.z > 0f && baseline.z > 0f && actual.x >= 0f && actual.x <= camera.pixelWidth && actual.y >= 0f && actual.y <= camera.pixelHeight;
            pixels = actual.z > 0f && baseline.z > 0f ? Vector2.Distance(actual, baseline).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + (inView ? "" : "(offscreen)") : "behind-camera";
        }
        string reasons = string.Join(",", Enum.GetValues(typeof(GeodesicTinLakeCrossings.MeanderRejection)).Cast<GeodesicTinLakeCrossings.MeanderRejection>()
            .Where(p => p != GeodesicTinLakeCrossings.MeanderRejection.None && (crossing.AttemptFailureMask & (1 << (int)p)) != 0));
        if (reasons.Length == 0) reasons = "none";
        return FormattableString.Invariant($"basin={crossing.Bed.Id} chartLength={crossing.Length:R} lengthWidths={crossing.Length / Mathf.Max(1e-20f, crossing.Width):F2} pinnedJunctions={crossing.PinnedSpans} fixedLengthFraction={crossing.FixedLengthFraction:F3} basinWidthUsed={crossing.BasinWidthUsed:F3} peakWidths={crossing.PeakOffsetWidths:F3} meanWidths={crossing.MeanOffsetWidths:F3} peakLengthFraction={crossing.PeakLengthFraction:F4} mainCameraPeakPixels={pixels} localConstraints={reasons}");
    }

#if UNITY_EDITOR
    [ContextMenu("Inspect River At Scene View Centre")]
    private void InspectRiverAtSceneViewCentre()
    {
        var camera = UnityEditor.SceneView.lastActiveSceneView?.camera;
        if (camera == null || riverMesh == null || riverMesh.GetTopology(0) != MeshTopology.Triangles) return;
        Ray worldRay = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        var ray = new Ray(transform.InverseTransformPoint(worldRay.origin), transform.InverseTransformDirection(worldRay.direction).normalized);
        var positions = riverMesh.vertices; var triangles = riverMesh.triangles;
        float nearest = float.PositiveInfinity; int vertex = -1;
        // On-demand picking of the live mesh; front faces only, no helper collider.
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = positions[triangles[i]], e1 = positions[triangles[i + 1]] - a, e2 = positions[triangles[i + 2]] - a;
            Vector3 p = Vector3.Cross(ray.direction, e2), s = ray.origin - a;
            float determinant = Vector3.Dot(e1, p);
            if (determinant <= 1e-12f) continue;
            float u = Vector3.Dot(s, p) / determinant;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(ray.direction, q) / determinant, distance = Vector3.Dot(e2, q) / determinant;
            if (v < 0f || u + v > 1f || distance <= 0f || distance >= nearest) continue;
            nearest = distance; vertex = triangles[i];
        }
        if (vertex < 0) { Debug.Log("[SedimentMeanderPick] Centre the Scene view on a visible ribbon, then invoke this action again.", this); return; }
        var route = riverColourSpans.FirstOrDefault(p => vertex >= p.Start && vertex < p.End);
        var segment = meanderDiagnosticSpans.FirstOrDefault(p => vertex >= p.Start && vertex < p.End);
        string producer = route.Route != null ? route.Kind.ToString() : "LegacyRiver";
        string status = route.Crossing != null ? segment.Status.ToString() : "not-a-sediment-crossing";
        Debug.Log($"[SedimentMeanderPick] producer={producer} route={route.Route ?? "legacy river"} segmentStatus={status} localHit={ray.GetPoint(nearest)} " + DescribeCrossing(route.Crossing), this);
    }
#endif
}
