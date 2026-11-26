using UnityEngine;

/// <summary>Trembling light intensity and billboarded sprite alpha for launch flashes or attached exhaust lights.</summary>
public sealed class MissileLaunchLight : MonoBehaviour
{
    [SerializeField] Light pointLight;
    [SerializeField] SpriteRenderer sprite;
    [SerializeField] UnityEngine.Rendering.LensFlareComponentSRP lensFlare;
    [SerializeField, Min(0f)] float flareIntensity = 0.8f;
    [SerializeField, Min(0f)] float intensityMinimum = 0.5f;
    [SerializeField, Min(0f)] float intensityMaximum = 1.5f;
    [SerializeField, Min(0f)] float alphaMinimum = 0.5f;
    [SerializeField, Min(0f)] float alphaMaximum = 1f;
    [SerializeField, Min(0.01f)] float trembleSpeed = 18f;
    [SerializeField] Camera billboardCamera;
    float noiseOffset;
    float power = 1f;

    void Awake()
    {
        if (pointLight == null) pointLight = GetComponentInChildren<Light>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (lensFlare == null) lensFlare = GetComponentInChildren<UnityEngine.Rendering.LensFlareComponentSRP>();
        if (billboardCamera == null) billboardCamera = Camera.main;
        noiseOffset = Random.Range(0f, 1000f);
    }

    public void SetPower(float value)
    {
        power = Mathf.Clamp01(value);
        UpdateBrightness();
    }

    void Update()
    {
        UpdateBrightness();
        if (sprite != null && billboardCamera != null)
            sprite.transform.rotation = Quaternion.LookRotation(billboardCamera.transform.position - sprite.transform.position, billboardCamera.transform.up);
    }

    void UpdateBrightness()
    {
        var noise = Mathf.PerlinNoise(Time.time * trembleSpeed + noiseOffset, 0.37f);
        var intensity = Mathf.Lerp(intensityMinimum, intensityMaximum, noise) * power;
        if (pointLight != null) pointLight.intensity = intensity;
        if (lensFlare != null)
            lensFlare.intensity = intensityMaximum > 0f ? flareIntensity * intensity / intensityMaximum : 0f;
        if (sprite != null)
        {
            var color = sprite.color;
            color.a = Mathf.Lerp(alphaMinimum, alphaMaximum, noise) * power;
            sprite.color = color;
        }
    }
}
