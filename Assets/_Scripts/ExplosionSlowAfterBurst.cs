using System.Collections;
using UnityEngine;

/// <summary>
/// Keeps the first second of the explosion at full speed, then eases every child
/// particle system into a slower, longer linger so details fade out calmly.
/// </summary>
public sealed class ExplosionSlowAfterBurst : MonoBehaviour
{
    [SerializeField] float fullSpeedSeconds = 1f;
    [SerializeField] float slowSpeedMultiplier = 0.35f;
    [SerializeField] float slowdownBlendSeconds = 0.5f;

    /// <summary>Overrides the linger timing (used by the VR explosion optimizer).</summary>
    public void Configure(float fullSpeed, float slowMultiplier, float blendSeconds)
    {
        fullSpeedSeconds = Mathf.Max(0.05f, fullSpeed);
        slowSpeedMultiplier = Mathf.Clamp(slowMultiplier, 0.05f, 1f);
        slowdownBlendSeconds = Mathf.Max(0.05f, blendSeconds);
    }

    ParticleSystem[] systems;
    float[] originalSpeeds;

    void Awake()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        originalSpeeds = new float[systems.Length];
        for (var i = 0; i < systems.Length; i++)
        {
            var speed = systems[i].main.simulationSpeed;
            originalSpeeds[i] = speed > 0.0001f ? speed : 1f;
        }
    }

    void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(SlowAfterBurst());
    }

    IEnumerator SlowAfterBurst()
    {
        for (var i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.simulationSpeed = originalSpeeds[i];
        }

        yield return new WaitForSeconds(fullSpeedSeconds);

        var elapsed = 0f;
        while (elapsed < slowdownBlendSeconds)
        {
            elapsed += Time.deltaTime;
            var blend = Mathf.Clamp01(elapsed / slowdownBlendSeconds);
            for (var i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                main.simulationSpeed = Mathf.Lerp(
                    originalSpeeds[i],
                    originalSpeeds[i] * slowSpeedMultiplier,
                    blend);
            }

            yield return null;
        }

        for (var i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.simulationSpeed = originalSpeeds[i] * slowSpeedMultiplier;
        }
    }
}
