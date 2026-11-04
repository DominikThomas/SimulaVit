using System.Collections;
using TMPro;
using UnityEngine;

public class StartupFadeOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text messageText;
    [SerializeField, Min(0f)] private float defaultFadeSeconds = 0.5f;
    [SerializeField] private bool blockRaycastsDuringLoading = true;

    [Header("Planet Generation Screen")]
    [Tooltip("Optional artwork. Displayed full screen using aspect-preserving cover/crop.")]
    [SerializeField] private Sprite loadingBackground;
    [SerializeField] private Color loadingBackgroundColor = new Color(0.018f, 0.028f, 0.042f, 1f);
    [SerializeField] private Color loadingTextBackingColor = new Color(0f, 0f, 0f, 0.78f);
    private PlanetGenerationLoadingView generationView;

    private Coroutine fadeRoutine;

    public bool IsVisible => (generationView != null && generationView.IsVisible) || (canvasGroup != null && canvasGroup.alpha > 0.001f);

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }

    public void ShowImmediate(string message = "Generating planet...")
    {
        Show(message, true, blockRaycastsDuringLoading);
    }

    public void ShowSetupCurtain(string message = "Planet Simulation Setup")
    {
        generationView?.Hide();
        Show(message, false, false);
    }

    public void ShowLoading(string message = "Generating planet...")
    {
        Show(message, true, blockRaycastsDuringLoading);
        generationView ??= new PlanetGenerationLoadingView(loadingBackgroundColor, loadingTextBackingColor);
        generationView.Show(loadingBackground);
    }

    public void ShowGenerationProgress(PlanetGenerationProgress progress) => generationView?.Report(progress);
    public void ShowFailure(System.Action returnToMenu) => generationView?.ShowFailure(returnToMenu);

    private void OnDisable() => HideImmediate();

    private void OnDestroy()
    {
        generationView?.Dispose();
        generationView = null;
    }

    private void Show(string message, bool interactable, bool blocksRaycasts)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        if (messageText != null)
        {
            messageText.text = message;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = blocksRaycasts;
            canvasGroup.interactable = interactable;
        }
    }

    public void HideImmediate()
    {
        generationView?.Hide();
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    public void FadeOut(float fadeSeconds = -1f)
    {
        generationView?.Hide();
        if (canvasGroup == null)
        {
            return;
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(FadeOutRoutine(fadeSeconds >= 0f ? fadeSeconds : defaultFadeSeconds));
    }

    private IEnumerator FadeOutRoutine(float fadeSeconds)
    {
        if (canvasGroup == null)
        {
            yield break;
        }

        float startAlpha = canvasGroup.alpha;
        float duration = Mathf.Max(0.0001f, fadeSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        fadeRoutine = null;
    }
}
