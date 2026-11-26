using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExplosionInitialFacing : MonoBehaviour
{
    ParticleSystemRenderer[] particleRenderers;

    void Awake() => particleRenderers = GetComponentsInChildren<ParticleSystemRenderer>(true);

    void OnEnable()
    {
        var camera = Camera.main;
        if (camera == null) return;
        Vector3 cameraPosition = camera.transform.position;
        foreach (var renderer in particleRenderers)
        {
            if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.Billboard) continue;
            Vector3 direction = cameraPosition - renderer.transform.position;
            if (direction.sqrMagnitude < 0.0001f) continue;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.999f
                ? Vector3.forward : Vector3.up;
            renderer.transform.rotation = Quaternion.LookRotation(direction, up);
        }
    }
}
