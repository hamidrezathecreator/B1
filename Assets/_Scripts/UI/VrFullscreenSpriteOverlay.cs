using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Full-screen (per eye) UI texture on the XR camera — e.g. glass shards after an explosion.</summary>
[AddComponentMenu("B1/VR Fullscreen Sprite Overlay")]
public sealed class VrFullscreenSpriteOverlay : MonoBehaviour
{
    [SerializeField] Texture2D texture;
    [SerializeField] Color tint = Color.white;
    [Tooltip("Screen Space Camera distance from the headset.")]
    [Min(0.01f)] [SerializeField] float planeDistance = 0.05f;
    [SerializeField] int sortingOrder = 32755;
    [SerializeField] Camera targetCamera;

    Canvas canvas;
    CanvasGroup group;
    RawImage image;
    Coroutine fadeRoutine;

    public void SetTexture(Texture2D tex)
    {
        texture = tex;
        ApplyTexture();
    }

    public void Show(float fadeInSeconds = 0.3f)
    {
        if (texture == null)
        {
            Debug.LogWarning("VrFullscreenSpriteOverlay: assign a texture before showing.", this);
            return;
        }

        EnsureBuilt();
        gameObject.SetActive(true);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(1f, fadeInSeconds));
    }

    public void Hide(float fadeOutSeconds = 0.2f)
    {
        if (group == null) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(0f, fadeOutSeconds, deactivateWhenDone: true));
    }

    IEnumerator FadeTo(float targetAlpha, float duration, bool deactivateWhenDone = false)
    {
        if (group == null) yield break;
        var start = group.alpha;
        if (duration <= 0f)
        {
            group.alpha = targetAlpha;
        }
        else
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            group.alpha = targetAlpha;
        }

        if (deactivateWhenDone && targetAlpha <= 0.001f)
            gameObject.SetActive(false);
        fadeRoutine = null;
    }

    void EnsureBuilt()
    {
        if (canvas != null) return;

        if (targetCamera == null)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null) targetCamera = origin.Camera;
        }

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        var root = new GameObject("Fullscreen Overlay Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);

        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = targetCamera;
        canvas.planeDistance = Mathf.Max(targetCamera.nearClipPlane + 0.01f, planeDistance);
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var imageGo = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
        imageGo.transform.SetParent(root.transform, false);
        image = imageGo.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = tint;

        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        ApplyTexture();
    }

    void ApplyTexture()
    {
        if (image == null) return;
        image.texture = texture;
        image.enabled = texture != null;
    }
}