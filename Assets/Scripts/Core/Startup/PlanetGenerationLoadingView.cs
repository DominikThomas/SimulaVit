using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owned by StartupFadeOverlay. Only temporary UI objects are created here.</summary>
internal sealed class PlanetGenerationLoadingView : IDisposable
{
    private readonly GameObject root;
    private readonly Image artwork;
    private readonly AspectRatioFitter cover;
    private readonly TMP_Text title;
    private readonly TMP_Text stage;
    private readonly TMP_Text subtitle;
    private readonly TMP_Text pass;
    private readonly Button returnButton;
    private readonly Texture2D backingTexture;
    private Action returnToMenu;
    private PlanetGenerationProgress? lastProgress;

    public bool IsVisible => root != null && root.activeSelf;

    public PlanetGenerationLoadingView(Color fallback, Color backing)
    {
        root = new GameObject("Planet Generation Loading Screen", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var background = Create<Image>("Fallback", root.transform);
        Stretch(background.rectTransform);
        fallback.a = 1f; // Never expose a partially generated world, even with transparent artwork.
        background.color = fallback;
        background.raycastTarget = true;

        var viewport = Create<RectMask2D>("Artwork crop", root.transform);
        Stretch((RectTransform)viewport.transform);
        artwork = Create<Image>("Optional background sprite", viewport.transform);
        artwork.raycastTarget = false;
        artwork.preserveAspect = true;
        cover = artwork.gameObject.AddComponent<AspectRatioFitter>();
        cover.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

        // A soft, local scrim fades into the artwork rather than a large opaque panel.
        backingTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false)
        { name = "Loading text scrim (Runtime)", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            Color colour = backing;
            colour.a *= (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, x / 63f))) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, y / 63f)));
            backingTexture.SetPixel(x, y, colour);
        }
        backingTexture.Apply(false, true);
        var scrim = Create<RawImage>("Text backing", root.transform);
        scrim.texture = backingTexture;
        scrim.raycastTarget = false;
        scrim.rectTransform.anchorMin = Vector2.zero;
        scrim.rectTransform.anchorMax = new Vector2(0.65f, 0.6f);
        scrim.rectTransform.offsetMin = scrim.rectTransform.offsetMax = Vector2.zero;

        var column = Create<VerticalLayoutGroup>("Bottom left progress", root.transform);
        var rect = (RectTransform)column.transform;
        rect.anchorMin = new Vector2(0.045f, 0.065f);
        rect.anchorMax = new Vector2(0.52f, 0.065f);
        rect.pivot = Vector2.zero;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        column.spacing = 14;
        column.childAlignment = TextAnchor.LowerLeft;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = CreateText("Title", column.transform, 24, FontStyles.Bold, new Color(0.68f, 0.78f, 0.82f));
        stage = CreateText("Stage", column.transform, 42, FontStyles.Bold, Color.white);
        subtitle = CreateText("Subtitle", column.transform, 25, FontStyles.Italic, new Color(0.82f, 0.86f, 0.88f));
        pass = CreateText("Pass", column.transform, 22, FontStyles.Normal, new Color(0.68f, 0.78f, 0.82f));
        returnButton = Create<Button>("Return to configuration", column.transform);
        var buttonImage = returnButton.gameObject.AddComponent<Image>();
        buttonImage.color = new Color(0.16f, 0.23f, 0.28f, 0.96f);
        returnButton.targetGraphic = buttonImage;
        returnButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        var buttonText = CreateText("Label", returnButton.transform, 24, FontStyles.Bold, Color.white);
        buttonText.text = "Return to configuration";
        buttonText.alignment = TextAlignmentOptions.Center;
        Stretch(buttonText.rectTransform);
        returnButton.onClick.AddListener(() => returnToMenu?.Invoke());
        root.SetActive(false);
    }

    public void Show(Sprite background)
    {
        artwork.sprite = background;
        artwork.enabled = background != null;
        cover.enabled = background != null;
        if (background != null) cover.aspectRatio = background.rect.width / background.rect.height;
        returnButton.gameObject.SetActive(false);
        returnToMenu = null;
        lastProgress = null;
        SetTitle("SIMULAVIT");
        root.SetActive(true);
        Report(new PlanetGenerationProgress(PlanetGenerationStage.BuildingPlanet));
    }

    public void Report(PlanetGenerationProgress progress)
    {
        if (lastProgress.HasValue && lastProgress.Value.Stage == progress.Stage &&
            lastProgress.Value.Pass == progress.Pass && lastProgress.Value.PassCount == progress.PassCount) return;
        bool stageChanged = !lastProgress.HasValue || lastProgress.Value.Stage != progress.Stage;
        lastProgress = progress;
        if (stageChanged)
        {
            var copy = Copy(progress.Stage);
            SetStage(copy.label);
            SetSubtitle(copy.subtitle);
        }
        SetPass(progress.Pass > 0 && progress.PassCount > 0 ? $"Pass {progress.Pass} of {progress.PassCount}" : string.Empty);
    }

    public void ShowFailure(Action onReturn)
    {
        lastProgress = null;
        SetStage("Planet generation failed");
        SetSubtitle("The world could not be prepared. Return to configuration to try again.");
        SetPass(string.Empty);
        returnToMenu = onReturn;
        returnButton.gameObject.SetActive(true);
        root.SetActive(true);
    }

    public void SetTitle(string value) { if (title.text != value) title.text = value; }
    public void SetStage(string value) { if (stage.text != value) stage.text = value; }
    public void SetSubtitle(string value) { if (subtitle.text != value) subtitle.text = value; }
    public void SetPass(string value)
    {
        if (pass.text != value) pass.text = value;
        pass.gameObject.SetActive(!string.IsNullOrEmpty(value));
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        returnToMenu = null;
        lastProgress = null;
    }

    public void Dispose()
    {
        returnToMenu = null;
        UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(backingTexture);
    }

    private static T Create<T>(string name, Transform parent) where T : Component
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.AddComponent<T>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style, Color colour)
    {
        var text = Create<TextMeshProUGUI>(name, parent);
        text.fontSize = size;
        text.fontStyle = style;
        text.color = colour;
        text.alignment = TextAlignmentOptions.Left;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static (string label, string subtitle) Copy(PlanetGenerationStage stage)
    {
        switch (stage)
        {
            case PlanetGenerationStage.GrowingMountains: return ("Growing mountains", "Applying geology without waiting a hundred million years.");
            case PlanetGenerationStage.ShapingSeafloor: return ("Shaping the seafloor", "Digging trenches where nobody will ever see them.");
            case PlanetGenerationStage.FindingOceans: return ("Finding the oceans", "Checking which puddles deserve to be called seas.");
            case PlanetGenerationStage.BulgingContinents: return ("Bulging continents", "Making the middle slightly higher than the edges.");
            case PlanetGenerationStage.MixingOceans: return ("Mixing the oceans", "Stirring several billion cubic kilometres very carefully.");
            case PlanetGenerationStage.PreparingSurface: return ("Preparing the surface", "Making sure the ground is ready to stand on.");
            case PlanetGenerationStage.RoutingRivers: return ("Routing rivers", "Convincing water that downhill is preferable.");
            case PlanetGenerationStage.CarvingValleys: return ("Carving valleys", "Removing a few inconvenient saddles.");
            case PlanetGenerationStage.FillingLakes: return ("Filling lakes", "Water has found another hole.");
            case PlanetGenerationStage.RoutingLakes: return ("Connecting lakes and rivers", "Giving every puddle forwarding instructions.");
            case PlanetGenerationStage.BuildingRiverRibbons: return ("Drawing river ribbons", "Putting the blue lines where the water went.");
            case PlanetGenerationStage.PreparingAtmosphere: return ("Preparing the atmosphere", "Adding just enough gas to complicate everything.");
            case PlanetGenerationStage.PreparingPrimordialWorld: return ("Preparing the primordial world", "Setting the table for several billion years of bad decisions.");
            case PlanetGenerationStage.AlmostReady: return ("Almost ready", "Checking that the oceans are still on the outside.");
            case PlanetGenerationStage.CheckingSavedPlanet: return ("Checking saved planet", "Looking for terrain generated with these settings.");
            case PlanetGenerationStage.LoadingSavedPlanet: return ("Loading saved planet", "Reading and verifying saved terrain.");
            case PlanetGenerationStage.RestoringSavedPlanet: return ("Restoring saved planet", "Rebuilding the surface from saved terrain.");
            case PlanetGenerationStage.SavingPlanetSnapshot: return ("Saving planet snapshot", "Saving completed generation data for the next game.");
            case PlanetGenerationStage.LoadingSavedHydrology: return ("Loading saved hydrology", "Checking saved drainage and runoff data.");
            case PlanetGenerationStage.RestoringSavedHydrology: return ("Restoring saved hydrology", "Connecting the saved routes to this planet.");
            default: return ("Building the planet", "Starting with triangles. An unreasonable number of triangles.");
        }
    }
}
