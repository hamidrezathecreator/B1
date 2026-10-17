using System;
using System.Collections;
using RTLTMPro;
using TMPro;
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
    private CanvasGroup textGroup;
    private GameObject textOverlay;

    public static void Begin(string sceneName, bool fade, float delay, float fadeOut,
        float holdBlack, float fadeIn, RTLTextMeshPro textPrefab = null,
        float textFadeIn = 0.35f, AudioSource audioSourceToFadeOut = null)
    {
        if (active != null)
        {
            Debug.LogWarning("Scene transition already in progress; ignoring another load request.", active);
            return;
        }

        var runnerObject = new GameObject("Scene Transition (temporary)");
        DontDestroyOnLoad(runnerObject);
        active = runnerObject.AddComponent<SceneTransitionRunner>();
        active.StartCoroutine(active.Transition(sceneName, fade, delay, fadeOut, holdBlack, fadeIn,
            textPrefab, textFadeIn, audioSourceToFadeOut));
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnDestroy()
    {
        if (textOverlay != null) Destroy(textOverlay);
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
        float fadeOut, float holdBlack, float fadeIn, RTLTextMeshPro textPrefab, float textFadeIn,
        AudioSource audioSourceToFadeOut)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        if (textPrefab != null) CreateTextOverlay(textPrefab, 0f);

        if (fade)
        {
            CreateOverlay();
            yield return FadeTo(1f, fadeOut, overlayGroup, audioSourceToFadeOut: audioSourceToFadeOut);
        }
        else if (audioSourceToFadeOut != null) audioSourceToFadeOut.volume = 0f;

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
            if (overlayGroup != null) yield return FadeTo(0f, fadeIn, overlayGroup);
            Destroy(gameObject);
            yield break;
        }

        while (!loading.isDone) yield return null;

        if (fade || textGroup != null)
        {
            // Give the new scene's XR camera a frame to initialize behind the black view.
            PositionOverlay();
            yield return null;
            // Text fades in during the hold, then leaves with the black overlay.
            float holdStarted = Time.realtimeSinceStartup;
            if (textGroup != null) yield return FadeTo(1f, textFadeIn, textGroup);
            float remainingHold = holdBlack - (Time.realtimeSinceStartup - holdStarted);
            if (remainingHold > 0f) yield return new WaitForSecondsRealtime(remainingHold);
            yield return FadeTo(0f, fadeIn, fade ? overlayGroup : textGroup,
                fade ? textGroup : null);
        }

        Destroy(gameObject);
    }

    private IEnumerator FadeTo(float target, float duration, CanvasGroup group,
        CanvasGroup otherGroup = null, AudioSource audioSourceToFadeOut = null)
    {
        if (duration <= 0f)
        {
            group.alpha = target;
            if (otherGroup != null) otherGroup.alpha = target;
            if (audioSourceToFadeOut != null) audioSourceToFadeOut.volume = 0f;
            yield break;
        }

        float start = group.alpha;
        float otherStart = otherGroup != null ? otherGroup.alpha : 0f;
        float startVolume = audioSourceToFadeOut != null ? audioSourceToFadeOut.volume : 0f;
        double started = Time.realtimeSinceStartupAsDouble;
        while (true)
        {
            // Start at the current time instead of consuming the scene-loading frame's delta.
            float progress = Mathf.Clamp01((float)((Time.realtimeSinceStartupAsDouble - started) / duration));
            group.alpha = Mathf.Lerp(start, target, progress);
            if (otherGroup != null) otherGroup.alpha = Mathf.Lerp(otherStart, target, progress);
            if (audioSourceToFadeOut != null)
                audioSourceToFadeOut.volume = Mathf.Lerp(startVolume, 0f, progress);
            if (progress >= 1f) break;
            yield return null;
        }
        group.alpha = target;
        if (otherGroup != null) otherGroup.alpha = target;
        if (audioSourceToFadeOut != null) audioSourceToFadeOut.volume = 0f;
    }

    private void CreateTextOverlay(RTLTextMeshPro textPrefab, float alpha)
    {
        var overlay = new GameObject("Scene Transition Text", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        overlay.SetActive(false);
        // Keep screen-space overlay canvases at the hierarchy root across scene loads.
        textOverlay = overlay;
        DontDestroyOnLoad(overlay);
        overlay.layer = LayerMask.NameToLayer("UI");

        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;

        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RTLTextMeshPro text = Instantiate(textPrefab, overlay.transform, false);
        textGroup = text.GetComponent<CanvasGroup>();
        if (textGroup == null) textGroup = text.gameObject.AddComponent<CanvasGroup>();
        textGroup.enabled = true;
        textGroup.alpha = alpha;
        textGroup.blocksRaycasts = false;
        textGroup.interactable = false;

        RectTransform rect = text.rectTransform;
        Vector2 size = textPrefab.rectTransform.rect.size;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        overlay.SetActive(true);
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
        overlayCanvas.sortingOrder = 32766;

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
