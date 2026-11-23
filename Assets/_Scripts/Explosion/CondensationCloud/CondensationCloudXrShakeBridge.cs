using UnityEngine;

/// <summary>
/// Shakes the headset when <see cref="CondensationCloudSystem"/> reports the expanding cloud has reached the listener.
/// Add beside <see cref="XRExplosionShake"/> on the XR Origin.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRExplosionShake))]
public sealed class CondensationCloudXrShakeBridge : MonoBehaviour
{
    [SerializeField] private XRExplosionShake explosionShake;
    [SerializeField, Range(0f, 1f)] private float shakeStrength = 0.9f;

    private void Awake()
    {
        if (explosionShake == null)
            explosionShake = GetComponent<XRExplosionShake>();
    }

    private void OnEnable() => CondensationCloudSystem.PlayerEngulfed += OnPlayerEngulfed;
    private void OnDisable() => CondensationCloudSystem.PlayerEngulfed -= OnPlayerEngulfed;

    private void OnPlayerEngulfed(Vector3 center, float radius)
    {
        if (explosionShake == null || !isActiveAndEnabled) return;
        explosionShake.ShakeWithStrength(shakeStrength);
    }
}