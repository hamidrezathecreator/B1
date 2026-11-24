using System.Collections;
using UnityEngine;

/// <summary>
/// Starts child particle systems in slow-mo, then ramps simulation speed up to normal.
/// Opposite of <see cref="ExplosionSlowAfterBurst"/> (full speed first, then linger slow).
/// </summary>
[DisallowMultipleComponent]
public sealed class ExplosionPlaybackRamp : MonoBehaviour
{
    ParticleSystem[] systems;
    float[] baseSpeeds;

    public void Play(ExplosionTimeScaleSettings.Profile profile)
    {
        var linger = GetComponent<ExplosionSlowAfterBurst>();
        if (linger != null) linger.enabled = false;

        systems = GetComponentsInChildren<ParticleSystem>(true);
        if (baseSpeeds == null || baseSpeeds.Length != systems.Length)
        {
            baseSpeeds = new float[systems.Length];
            for (var i = 0; i < systems.Length; i++)
                baseSpeeds[i] = 1f;
        }

        StopAllCoroutines();
        StartCoroutine(Ramp(profile));
    }

    IEnumerator Ramp(ExplosionTimeScaleSettings.Profile profile)
    {
        var elapsed = 0f;
        ApplyScale(profile.Evaluate(0f));
        if (profile.RampDuration <= 0f || Mathf.Approximately(profile.Start, profile.End))
            yield break;

        while (elapsed < profile.RampDuration)
        {
            elapsed += Time.deltaTime;
            ApplyScale(profile.Evaluate(elapsed));
            yield return null;
        }

        ApplyScale(profile.Evaluate(profile.RampDuration));
    }

    void ApplyScale(float scale)
    {
        if (systems == null) return;
        var mul = Mathf.Max(0f, scale);
        for (var i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            var main = systems[i].main;
            main.simulationSpeed = baseSpeeds[i] * mul;
        }
    }
}
