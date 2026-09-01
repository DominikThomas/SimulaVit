using System.Collections;
using TMPro;
using UnityEngine;

public class StartupFadeOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text messageText;
    [SerializeField, Min(0f)] private float defaultFadeSeconds = 0.5f;
    [SerializeField] private bool blockRaycastsDuringLoading = true;

    private Coroutine fadeRoutine;

    public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.001f;

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
        Show(message, false, false);
    }

    public void ShowLoading(string message = "Generating planet...")
    {
        Show(message, true, blockRaycastsDuringLoading);
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
