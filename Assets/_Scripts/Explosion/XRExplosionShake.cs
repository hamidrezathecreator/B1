using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Event-driven positional shake applied above XR tracked poses.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XROrigin))]
public sealed class XRExplosionShake : MonoBehaviour
{
    /// <summary>Raised when a small shake pulse actually starts, after any configured delay. Rising shake does not raise this event.</summary>
    public static event System.Action<XRExplosionShake> SmallShakeStarted;

    [Tooltip("Optional dedicated child of Camera Offset containing camera and controllers. An invalid or empty assignment creates a pivot at runtime.")]
    [SerializeField] private Transform shakePivot;

    [Header("Small positional shake (local units; normally metres)")]
    [Tooltip("Seconds from a launch/impact or Shake call until its positional shake starts. Rising shake is unaffected.")]
    [Min(0f)] [SerializeField] private float delay = 0f;
    [SerializeField] private Vector3 strength = new Vector3(0.003f, 0.002f, 0f);
    [Min(0.01f)] [SerializeField] private float duration = 0.18f;
    [Min(1f)] [SerializeField] private float frequency = 18f;
    [Tooltip("Continue shaking while Time.timeScale is zero.")]
    [SerializeField] private bool useUnscaledTime;
    [Tooltip("Comfort toggle. Turning this off also stops an active shake.")]
    [SerializeField] private bool shakeEnabled = true;

    [Header("Automatic missile events (same scene only)")]
    [SerializeField] private bool shakeOnAToBImpact = true;
    [SerializeField] private bool shakeOnLauncherFire = true;

    [Header("Rising shake (container-launched missiles only)")]
    [SerializeField] private bool risingShakeEnabled = true;
    [Tooltip("Fraction of Strength at close range. Shares the maximum displacement with launch/impact shakes.")]
    [SerializeField, Range(0f, 1f)] private float risingIntensity = 0.6f;
    [Tooltip("World-space distance from the headset within which rising shake is at full intensity.")]
    [SerializeField, Min(0f)] private float risingFullStrengthDistance = 5f;
    [Tooltip("World-space distance from the headset beyond which rising shake is zero.")]
    [SerializeField, Min(0.01f)] private float risingMaxDistance = 60f;
    [Tooltip("Seconds to ramp one full intensity unit up/down, including when the last flight ends.")]
    [SerializeField, Min(0.01f)] private float risingFadeTime = 0.15f;

    private Transform cachedPivot;
    private Vector3 restPosition;
    private float elapsed;
    private float intensity;
    private float waveElapsed;
    private float currentRisingIntensity;
    private bool shaking;

    private void OnEnable()
    {
        MissileLauncherSystem.MissileImpacted -= OnAToBImpact;
        MissileLauncherSystem.MissileImpacted += OnAToBImpact;
        MissileLauncherController.MissileFired -= OnLauncherFire;
        MissileLauncherController.MissileFired += OnLauncherFire;
        Application.onBeforeRender += ApplyShake;
    }

    private void OnAToBImpact(MissileLauncherSystem source, Vector3 position)
    {
        if (shakeOnAToBImpact && source.gameObject.scene == gameObject.scene) Shake();
    }

    private void OnLauncherFire(MissileLauncherController source)
    {
        if (shakeOnLauncherFire && source.gameObject.scene == gameObject.scene) Shake();
    }

    /// <summary>Parameterless reaction for gameplay events.</summary>
    [ContextMenu("Test Shake (Play Mode)")]
    public void Shake() => ShakeWithStrength(1f);

    /// <summary>Scales the configured strength from zero to one; hits never stack.</summary>
    public void ShakeWithStrength(float value)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !shakeEnabled ||
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            return;
        if (!TryInitialize()) return;

        if (delay > 0f)
            StartCoroutine(DelayedPulse(Mathf.Clamp01(value), delay));
        else
            StartPulse(Mathf.Clamp01(value));
    }

    private IEnumerator DelayedPulse(float value, float remaining)
    {
        while (remaining > 0f)
        {
            yield return null;
            remaining -= useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }
        if (shakeEnabled) StartPulse(value);
    }

    private void StartPulse(float value)
    {
        // Keep one waveform running while missiles overlap. Hits never add to
        // the offset or weaken an existing stronger pulse.
        intensity = Mathf.Max(GetPulseIntensity(), Mathf.Clamp01(value));
        elapsed = 0f;
        BeginShake();
        ApplyShake();
        SmallShakeStarted?.Invoke(this);
    }

    /// <summary>Stop immediately and restore the pivot.</summary>
    public void StopShake()
    {
        StopAllCoroutines();
        StopMotion();
    }

    private void StopMotion()
    {
        if (shaking && cachedPivot != null) cachedPivot.localPosition = restPosition;
        shaking = false;
        intensity = 0f;
        currentRisingIntensity = 0f;
    }

    /// <summary>Wire to a settings toggle to let the player disable shake.</summary>
    public void SetShakeEnabled(bool value)
    {
        shakeEnabled = value;
        if (!value) StopShake();
    }

    private bool TryInitialize()
    {
        if (cachedPivot != null) return true;

        var origin = GetComponent<XROrigin>();
        var floorOffset = origin.CameraFloorOffsetObject;
        if (floorOffset == null || origin.Camera == null ||
            floorOffset.transform == origin.Camera.transform ||
            !origin.Camera.transform.IsChildOf(floorOffset.transform))
        {
            Debug.LogWarning("XRExplosionShake needs the XR Origin's Camera and Camera Floor Offset Object assigned, with the camera beneath the offset object.", this);
            return false;
        }

        if (shakePivot == null ||
            shakePivot == transform || (origin.Origin != null && shakePivot == origin.Origin.transform) ||
            shakePivot == origin.Camera.transform ||
            shakePivot.parent != floorOffset.transform ||
            !origin.Camera.transform.IsChildOf(shakePivot))
        {
            // Group all tracked roots together without changing their local poses.
            // XROrigin still owns floor height; locomotion still owns the origin.
            var pivot = new GameObject("Explosion Shake Pivot").transform;
            pivot.SetParent(floorOffset.transform, false);
            for (int i = floorOffset.transform.childCount - 1; i >= 0; i--)
            {
                var child = floorOffset.transform.GetChild(i);
                if (child != pivot) child.SetParent(pivot, false);
            }
            shakePivot = pivot;
        }

        cachedPivot = shakePivot;
        restPosition = cachedPivot.localPosition;
        return true;
    }

    private void LateUpdate()
    {
        if (!shakeEnabled)
        {
            StopShake();
            return;
        }

        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        elapsed += delta;
        currentRisingIntensity = Mathf.MoveTowards(currentRisingIntensity,
            GetRisingIntensity(), delta / Mathf.Max(0.01f, risingFadeTime));
        if (!shaking && Mathf.Max(GetPulseIntensity(), currentRisingIntensity) > 0f)
        {
            if (!TryInitialize()) return;
            BeginShake();
        }
        if (shaking) waveElapsed += delta;
        ApplyShake();
    }

    private void BeginShake()
    {
        if (shaking) return;
        // Preserve external adjustments made while the pivot was idle.
        restPosition = cachedPivot.localPosition;
        waveElapsed = 0f;
        shaking = true;
    }

    private float GetPulseIntensity()
    {
        float fade = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        return intensity * fade * fade;
    }

    private float GetRisingIntensity()
    {
        if (!risingShakeEnabled || MissileProjectile.ActiveFlights.Count == 0) return 0f;
        var camera = GetComponent<XROrigin>().Camera;
        if (camera == null) return 0f;

        Vector3 listenerPosition = camera.transform.position;
        // Our own displacement must not feed back into the distance calculation.
        if (shaking && cachedPivot != null && cachedPivot.parent != null)
            listenerPosition -= cachedPivot.parent.TransformVector(cachedPivot.localPosition - restPosition);

        float fullDistance = Mathf.Max(0f, risingFullStrengthDistance);
        float maxDistance = Mathf.Max(fullDistance + 0.01f, risingMaxDistance);
        float strongest = 0f;
        foreach (var missile in MissileProjectile.ActiveFlights)
        {
            if (missile == null || !missile.IsInFlight || !missile.isActiveAndEnabled ||
                missile.gameObject.scene != gameObject.scene) continue;
            float distance = Vector3.Distance(listenerPosition, missile.transform.position);
            float proximity = 1f - Mathf.InverseLerp(fullDistance, maxDistance, distance);
            strongest = Mathf.Max(strongest, proximity * Mathf.Clamp01(risingIntensity));
        }
        return strongest;
    }

    // Reapply the same offset before rendering without advancing time twice.
    private void ApplyShake()
    {
        if (!shaking) return;
        float combinedIntensity = Mathf.Clamp01(Mathf.Max(GetPulseIntensity(), currentRisingIntensity));
        if (!shakeEnabled || cachedPivot == null || combinedIntensity <= 0f)
        {
            if (!shakeEnabled || cachedPivot == null) StopShake();
            else StopMotion();
            return;
        }

        float phase = waveElapsed * Mathf.Max(1f, frequency) * Mathf.PI * 2f;
        var wave = new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 1.17f), Mathf.Sin(phase * 0.83f));
        cachedPivot.localPosition = restPosition + Vector3.Scale(strength, wave) * combinedIntensity;
    }

    private void OnDisable()
    {
        MissileLauncherSystem.MissileImpacted -= OnAToBImpact;
        MissileLauncherController.MissileFired -= OnLauncherFire;
        Application.onBeforeRender -= ApplyShake;
        StopShake();
    }
    private void OnDestroy() => StopShake();
}
