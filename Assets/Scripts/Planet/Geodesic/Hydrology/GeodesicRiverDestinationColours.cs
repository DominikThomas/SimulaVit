using System;
using System.Collections.Generic;
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

    private readonly List<(string Route, int Start, int End)> riverColourSpans =
        new List<(string, int, int)>();
    private List<Color> normalRiverColours, destinationRiverColours;
    private GeodesicDrainageDelivery riverColourDelivery;
    public bool ColourRiversByUltimateDestination { get; private set; }

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

    private void RecordRiverColour(string route, int start, int end)
    {
        if (end > start) riverColourSpans.Add((route, start, end));
    }

    private void ApplyRiverColours()
    {
        if (!ColourRiversByUltimateDestination)
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
}
