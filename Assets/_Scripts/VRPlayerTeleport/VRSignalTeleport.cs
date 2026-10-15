using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>
/// Bind a Timeline Signal Track to the SignalReceiver on this object, then
/// select TeleportPlayer, FadeOut or FadeIn as reactions to different Signal Assets.
/// The destination marker represents a point on the floor and the direction to face.
/// </summary>
[RequireComponent(typeof(SignalReceiver))]
public class VRSignalTeleport : MonoBehaviour
{
    [SerializeField] private XROrigin playerOrigin;
    [SerializeField] private Transform destination;
    [Header("Optional Fade")]
    [SerializeField] private bool useFade;
    [Min(0f)] [SerializeField] private float fadeOutSeconds = 0.25f;
    [Min(0f)] [SerializeField] private float fadeInSeconds = 0.25f;

    private CanvasGroup fadeOverlay;
    private Coroutine transition;

    public void TeleportPlayer()
    {
        if (playerOrigin == null || playerOrigin.Camera == null || destination == null)
        {
            Debug.LogWarning("VRSignalTeleport needs an XR Origin and a destination marker.", this);
            return;
        }

        // A Timeline signal can move the player at any point between FadeOut
        // and FadeIn. Teleporting does not change an ongoing fade.
        MovePlayer();
    }

    /// <summary>Signal reaction: fade to black and stay black until FadeIn is called.</summary>
    public void FadeOut()
    {
        if (!useFade) return;
        if (playerOrigin == null || playerOrigin.Camera == null)
        {
            Debug.LogWarning("VRSignalTeleport needs an XR Origin to fade the headset view.", this);
            return;
        }

        StopTransition();
        EnsureFadeOverlay();
        transition = StartCoroutine(FadeOnly(1f, fadeOutSeconds));
    }

    /// <summary>Signal reaction: restore the view after a separate fade or teleport.</summary>
    public void FadeIn()
    {
        StopTransition();
        if (!useFade)
        {
            if (fadeOverlay != null) fadeOverlay.alpha = 0f;
            return;
        }
        if (fadeOverlay != null)
            transition = StartCoroutine(FadeOnly(0f, fadeInSeconds));
    }

    private IEnumerator FadeOnly(float target, float duration)
    {
        yield return FadeTo(target, duration);
        transition = null;
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        float start = fadeOverlay.alpha;
        if (duration <= 0f)
        {
            fadeOverlay.alpha = target;
            yield break;
        }

        for (float elapsed = 0f; elapsed < duration;)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        fadeOverlay.alpha = target;
    }

    private void EnsureFadeOverlay()
    {
        if (fadeOverlay != null) return;

        // A world-space canvas attached to the headset covers both VR eyes.
        // Build it only if the fade is actually used.
        var overlay = new GameObject("VR Teleport Fade", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        var rect = overlay.GetComponent<RectTransform>();
        rect.SetParent(playerOrigin.Camera.transform, false);
        float distance = Mathf.Max(playerOrigin.Camera.nearClipPlane + 0.03f, 0.1f);
        rect.localPosition = new Vector3(0f, 0f, distance);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * (distance * 0.005f);
        rect.sizeDelta = new Vector2(2000f, 2000f);

        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        var image = overlay.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        fadeOverlay = overlay.GetComponent<CanvasGroup>();
        fadeOverlay.alpha = 0f;
        fadeOverlay.blocksRaycasts = false;
        fadeOverlay.interactable = false;
    }

    private void OnDisable()
    {
        StopTransition();
        if (fadeOverlay != null) fadeOverlay.alpha = 0f;
    }

    private void StopTransition()
    {
        if (transition == null) return;
        StopCoroutine(transition);
        transition = null;
    }

    private void OnDestroy()
    {
        if (fadeOverlay != null) Destroy(fadeOverlay.gameObject);
    }

    private void MovePlayer()
    {
        // Turn the user's current view to the marker's heading without overriding
        // the tracked headset rotation. The headset's pitch remains theirs.
        playerOrigin.MatchOriginUpCameraForward(Vector3.up, destination.forward);

        // Put the origin on the destination floor, then account for the headset's
        // physical offset within the play space. Keep the tracked eye height.
        playerOrigin.transform.position = destination.position;
        Vector3 offset = destination.position - playerOrigin.Camera.transform.position;
        offset.y = 0f;
        playerOrigin.transform.position += offset;
    }
}
