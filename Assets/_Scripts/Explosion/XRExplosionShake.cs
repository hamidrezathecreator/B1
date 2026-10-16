using PrimeTween;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Event-driven positional shake for an XR rig. Call Shake from a UnityEvent or
/// Timeline SignalReceiver. The dedicated pivot must contain the camera and controllers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XROrigin))]
public sealed class XRExplosionShake : MonoBehaviour
{
    [Tooltip("Dedicated child of Camera Offset containing Main Camera and both controllers. Never assign the tracked camera or XR Origin itself.")]
    [SerializeField] private Transform shakePivot;

    [Header("Small positional shake (local units; normally metres)")]
    [SerializeField] private Vector3 strength = new Vector3(0.003f, 0.002f, 0f);
    [Min(0.01f)] [SerializeField] private float duration = 0.18f;
    [Min(1f)] [SerializeField] private float frequency = 18f;
    [Tooltip("Continue shaking while Time.timeScale is zero.")]
    [SerializeField] private bool useUnscaledTime;
    [Tooltip("Comfort toggle. Turning this off also stops an active shake.")]
    [SerializeField] private bool shakeEnabled = true;

    private Tween activeShake;
    private Transform cachedPivot;
    private Vector3 restPosition;

    private void Awake() => TryInitialize();

    /// <summary>Parameterless reaction for explosion UnityEvents and Timeline signals.</summary>
    [ContextMenu("Test Shake (Play Mode)")]
    public void Shake() => ShakeWithStrength(1f);

    /// <summary>Scales the configured strength from zero to one; hits never stack.</summary>
    public void ShakeWithStrength(float intensity)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !shakeEnabled ||
            float.IsNaN(intensity) || float.IsInfinity(intensity) || intensity <= 0f)
            return;
        if (!TryInitialize()) return;

        // Restart from the original rest position so repeated events cannot drift
        // or accumulate displacement. Only this component owns the pivot position.
        StopShake();
        var settings = new ShakeSettings(
            strength * Mathf.Clamp01(intensity),
            duration: Mathf.Max(0.01f, duration),
            frequency: Mathf.Max(1f, frequency),
            enableFalloff: true,
            useUnscaledTime: useUnscaledTime,
            updateType: UpdateType.LateUpdate);
        activeShake = Tween.ShakeLocalPosition(cachedPivot, settings)
            .OnComplete(this, target => target.RestorePosition());
    }

    /// <summary>Timeline/UnityEvent reaction: stop immediately and restore the pivot.</summary>
    public void StopShake()
    {
        if (activeShake.isAlive) activeShake.Stop();
        RestorePosition();
    }

    /// <summary>Wire to a settings toggle to let the player disable shake.</summary>
    public void SetShakeEnabled(bool value)
    {
        shakeEnabled = value;
        if (!value) StopShake();
    }

    private bool TryInitialize()
    {
        if (cachedPivot != null && cachedPivot == shakePivot) return true;
        StopShake();
        cachedPivot = null;

        var origin = GetComponent<XROrigin>();
        var floorOffset = origin.CameraFloorOffsetObject;
        if (shakePivot == null || floorOffset == null || origin.Camera == null ||
            shakePivot == transform || (origin.Origin != null && shakePivot == origin.Origin.transform) ||
            shakePivot == origin.Camera.transform ||
            shakePivot.parent != floorOffset.transform ||
            !origin.Camera.transform.IsChildOf(shakePivot))
        {
            Debug.LogWarning("XRExplosionShake needs a dedicated pivot directly under Camera Offset, " +
                "with Main Camera and the controllers beneath it. Assign that pivot in the Inspector.", this);
            return false;
        }

        cachedPivot = shakePivot;
        restPosition = cachedPivot.localPosition;
        return true;
    }

    private void RestorePosition()
    {
        if (cachedPivot != null) cachedPivot.localPosition = restPosition;
    }

    private void LateUpdate()
    {
        // Also honor changes made to the comfort toggle in the Inspector at runtime.
        if (!shakeEnabled && activeShake.isAlive) StopShake();
    }

    private void OnDisable() => StopShake();
    private void OnDestroy() => StopShake();
}
