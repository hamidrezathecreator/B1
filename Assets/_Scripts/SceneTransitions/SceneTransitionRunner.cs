using System;
using System.Collections;
using System.Collections.Generic;
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
    private Canvas textCanvas;
    private RectTransform textRect;
    private readonly List<Material> textMaterials = new List<Material>();

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

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.onBeforeRender += PositionOverlay;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.onBeforeRender -= PositionOverlay;
    }

    private void OnDestroy()
    {
        if (textOverlay != null) Destroy(textOverlay);
        foreach (Material material in textMaterials) Destroy(material);
        if (active == this) active = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        viewCamera = null;
        PositionOverlay();
    }

    private void LateUpdate()
    {
        PositionOverlay();
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
        // Keep the canvas alive independently of either scene's XR camera.
        textOverlay = overlay;
        DontDestroyOnLoad(overlay);
        overlay.layer = LayerMask.NameToLayer("UI");

        textCanvas = overlay.GetComponent<Canvas>();
        textCanvas.renderMode = RenderMode.WorldSpace;
        textCanvas.overrideSorting = true;
        textCanvas.sortingOrder = 32767;
        textRect = overlay.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(1920f, 1080f);

        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

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
        // Use the stereo-capable TMP overlay shader without modifying the prefab's materials.
        Material overlayTemplate = Resources.Load<Material>("SceneTransitionTextOverlay");
        if (overlayTemplate != null)
        {
            text.ForceMeshUpdate();
            Material[] materials = text.fontSharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty("_GradientScale")) continue;
                Material material = new Material(materials[i]);
                material.shader = overlayTemplate.shader;
                material.renderQueue = 4000;
                textMaterials.Add(material);
                materials[i] = material;
            }
            text.fontSharedMaterials = materials;
        }
        else Debug.LogError("Scene transition text overlay material is missing.", this);
        PositionOverlay();
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
        if (overlayRect == null && textRect == null) return;
        if (viewCamera == null)
        {
            XROrigin origin = FindFirstObjectByType<XROrigin>();
            if (origin != null) viewCamera = origin.Camera;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) viewCamera = FindFirstObjectByType<Camera>();
        }
        if (viewCamera == null) return;

        float distance = Mathf.Max(viewCamera.nearClipPlane + 0.03f, 0.1f);
        Transform cameraTransform = viewCamera.transform;
        if (overlayRect != null)
        {
            overlayRect.SetPositionAndRotation(cameraTransform.position + cameraTransform.forward * distance,
                cameraTransform.rotation);
            overlayRect.localScale = Vector3.one * (distance * 0.005f);
            overlayCanvas.worldCamera = viewCamera;
        }
        if (textRect != null)
        {
            // A one-meter viewing distance gives stereo text a comfortable depth.
            float textDistance = Mathf.Max(1f, viewCamera.nearClipPlane + 0.1f);
            textRect.SetPositionAndRotation(cameraTransform.position + cameraTransform.forward * textDistance,
                cameraTransform.rotation);
            textRect.localScale = Vector3.one * (textDistance * 1.6f / 1920f);
            textCanvas.worldCamera = viewCamera;
        }
    }
}
