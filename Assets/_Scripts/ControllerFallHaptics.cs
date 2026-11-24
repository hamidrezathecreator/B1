using UnityEngine;

[DisallowMultipleComponent]
public sealed class ControllerFallHaptics : MonoBehaviour
{
    const float RefreshSeconds = 0.05f;
    const float ImpulseSeconds = 0.15f;

    [SerializeField, Min(0.05f)] float rampSeconds = 1.5f;
    [SerializeField, Range(0f, 1f)] float maximumAmplitude = 1f;

    MonoBehaviour fallOwner;
    float startedAt;
    float nextRefreshAt;
    bool running;

    public float CurrentAmplitude { get; private set; }
    public bool IsVibrating => running;

    public static ControllerFallHaptics Begin(MonoBehaviour owner)
    {
        if (owner == null) return null;
        var effect = owner.GetComponent<ControllerFallHaptics>();
        if (effect == null) effect = owner.gameObject.AddComponent<ControllerFallHaptics>();
        if (effect.running) return effect;

        effect.enabled = true;
        effect.fallOwner = owner;
        effect.startedAt = Time.unscaledTime;
        effect.nextRefreshAt = Time.unscaledTime;
        effect.CurrentAmplitude = 0f;
        effect.running = true;
        ControllerHaptics.Stop();
        return effect;
    }

    void Update()
    {
        if (!running) return;
        if (fallOwner == null || !fallOwner.isActiveAndEnabled)
        {
            StopVibration();
            return;
        }

        CurrentAmplitude = Mathf.Clamp01(maximumAmplitude) *
            Mathf.Clamp01((Time.unscaledTime - startedAt) / Mathf.Max(0.05f, rampSeconds));
        if (Time.unscaledTime < nextRefreshAt) return;
        ControllerHaptics.Pulse(CurrentAmplitude, ImpulseSeconds);
        nextRefreshAt = Time.unscaledTime + RefreshSeconds;
    }

    public void StopVibration()
    {
        if (!running) return;
        running = false;
        fallOwner = null;
        CurrentAmplitude = 0f;
        ControllerHaptics.Stop();
    }

    void OnDisable() => StopVibration();
}
