using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CentralScopeCamera : MonoBehaviour
{
    private float shakeEndsAt;
    private float shakeDuration;
    private float shakeStrength;

    public bool IsFalling { get; private set; }
    public float Intensity => IsFalling ? 1f : shakeStrength *
        Mathf.Clamp01((shakeEndsAt - Time.unscaledTime) / Mathf.Max(0.01f, shakeDuration));

    public void PlayShake(float seconds, float strength = 1f)
    {
        if (!isActiveAndEnabled || IsFalling) return;
        shakeStrength = Mathf.Max(Intensity, Mathf.Clamp01(strength));
        shakeDuration = Mathf.Max(0.01f, seconds);
        shakeEndsAt = Time.unscaledTime + shakeDuration;
    }

    [ContextMenu("Test Shake (Play Mode)")]
    private void TestShake()
    {
        if (Application.isPlaying) PlayShake(0.65f);
    }

    [ContextMenu("Test Fall (Play Mode)")]
    public void BeginFall()
    {
        if (Application.isPlaying && isActiveAndEnabled) IsFalling = true;
    }

    [ContextMenu("Stop CentralScope")]
    public void StopEffect()
    {
        IsFalling = false;
        shakeEndsAt = 0f;
        shakeStrength = 0f;
    }

    private void OnDisable() => StopEffect();
}
