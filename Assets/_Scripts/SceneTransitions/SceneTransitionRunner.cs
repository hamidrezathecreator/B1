using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Temporary persistent runner; keeps the fade alive while scenes change.</summary>
[AddComponentMenu("")]
public sealed class SceneTransitionRunner : MonoBehaviour
{
    private static SceneTransitionRunner active;

    private Camera viewCamera;
    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private RectTransform overlayRect;

    public static void Begin(string sceneName, bool fade, float delay, float fadeOut,
        float holdBlack, float fadeIn)
    {
        if (active != null)
        {
            Debug.LogWarning("Scene transition already in progress; ignoring another load request.", active);
            return;
        }

        var runnerObject = new GameObject("Scene Transition (temporary)");
        DontDestroyOnLoad(runnerObject);
        active = runnerObject.AddComponent<SceneTransitionRunner>();
        active.StartCoroutine(active.Transition(sceneName, fade, delay, fadeOut, holdBlack, fadeIn));
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnDestroy()
    {
        if (active == this) active = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        viewCamera = null;
        PositionOverlay();
    }

    private void LateUpdate()
    {
        if (overlayGroup != null) PositionOverlay();
    }

    private IEnumerator Transition(string sceneName, bool fade, float delay,
        float fadeOut, float holdBlack, float fadeIn)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        if (fade)
        {
            CreateOverlay();
            yield return FadeTo(1f, fadeOut);
        }

        AsyncOperation loading = null;
        try
        {
            loading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (loading == null)
        {
            Debug.LogError($"Could not load scene '{sceneName}'.", this);
            if (overlayGroup != null) yield return FadeTo(0f, fadeIn);
            Destroy(gameObject);
            yield break;
        }

        while (!loading.isDone) yield return null;

        if (fade)
        {
            // Give the new scene's XR camera a frame to initialize behind the black view.
            PositionOverlay();
            yield return null;
            if (holdBlack > 0f) yield return new WaitForSecondsRealtime(holdBlack);
            yield return FadeTo(0f, fadeIn);
        }

        Destroy(gameObject);
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        if (duration <= 0f)
        {
            overlayGroup.alpha = target;
            yield break;
        }

        float start = overlayGroup.alpha;
        for (float elapsed = 0f; elapsed < duration;)
        {
            elapsed += Time.unscaledDeltaTime;
            overlayGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        overlayGroup.alpha = target;
    }

    private void CreateOverlay()
    {
        var overlay = new GameObject("Scene Fade", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.SetParent(transform, false);
        overlayRect.sizeDelta = new Vector2(2000f, 2000f);

        overlayCanvas = overlay.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.WorldSpace;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 32767;

        var image = overlay.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        overlayGroup = overlay.GetComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;
        PositionOverlay();
    }

    private void PositionOverlay()
    {
        if (overlayRect == null) return;
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
            if (viewCamera == null)
            {
                XROrigin origin = FindFirstObjectByType<XROrigin>();
                if (origin != null) viewCamera = origin.Camera;
            }
            if (viewCamera == null) viewCamera = FindFirstObjectByType<Camera>();
        }
        if (viewCamera == null) return;

        float distance = Mathf.Max(viewCamera.nearClipPlane + 0.03f, 0.1f);
        Transform cameraTransform = viewCamera.transform;
        overlayRect.SetPositionAndRotation(cameraTransform.position + cameraTransform.forward * distance,
            cameraTransform.rotation);
        overlayRect.localScale = Vector3.one * (distance * 0.005f);
        overlayCanvas.worldCamera = viewCamera;
    }
}
