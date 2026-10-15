using UnityEngine;

/// <summary>Short-lived launch flash with trembling light intensity and billboarded sprite alpha.</summary>
public sealed class MissileLaunchLight : MonoBehaviour
{
    [SerializeField] Light pointLight;
    [SerializeField] SpriteRenderer sprite;
    [SerializeField, Min(0f)] float intensityMinimum = 0.5f;
    [SerializeField, Min(0f)] float intensityMaximum = 1.5f;
    [SerializeField, Min(0f)] float alphaMinimum = 0.5f;
    [SerializeField, Min(0f)] float alphaMaximum = 1f;
    [SerializeField, Min(0.01f)] float trembleSpeed = 18f;
    [SerializeField] Camera billboardCamera;

    void Awake()
    {
        if (pointLight == null) pointLight = GetComponentInChildren<Light>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (billboardCamera == null) billboardCamera = Camera.main;
    }

    void Update()
    {
        var noise = Mathf.PerlinNoise(Time.time * trembleSpeed, 0.37f);
        if (pointLight != null) pointLight.intensity = Mathf.Lerp(intensityMinimum, intensityMaximum, noise);
        if (sprite != null)
        {
            var color = sprite.color;
            color.a = Mathf.Lerp(alphaMinimum, alphaMaximum, noise);
            sprite.color = color;
        }
        if (sprite != null && billboardCamera != null)
            sprite.transform.rotation = Quaternion.LookRotation(billboardCamera.transform.position - sprite.transform.position, billboardCamera.transform.up);
    }
}
