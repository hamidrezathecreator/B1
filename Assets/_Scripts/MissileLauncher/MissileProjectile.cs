using UnityEngine;
using UnityEngine.Events;

/// <summary>Reusable missile prefab behaviour. Call Launch after instantiation.</summary>
public sealed class MissileProjectile : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField, Min(0.01f)] float flightDuration = 2.5f;
    [SerializeField] float defaultArcHeight = 8f;
    [SerializeField] AnimationCurve defaultFlightCurve;
    [SerializeField] bool pointForwardAlongPath = true;

    [Header("Launch effects")]
    [SerializeField] ParticleSystem[] launchParticles;
    [SerializeField] GameObject launchLightPrefab;
    [SerializeField] Vector3 launchLightOffset;
    [SerializeField, Min(0f)] float launchLightLifetime = 0.45f;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip[] initiationSounds;
    [SerializeField] AudioClip[] flightSounds;

    public UnityEvent launched;
    public UnityEvent flightCompleted;
    bool launchedOnce;

    void Reset()
    {
        defaultFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        audioSource = GetComponent<AudioSource>();
    }

    void Awake()
    {
        if (defaultFlightCurve == null || defaultFlightCurve.length == 0) defaultFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public void Launch(Vector3 target, float arcHeight, AnimationCurve curve)
    {
        if (launchedOnce) return;
        launchedOnce = true;
        foreach (var particle in launchParticles) if (particle != null) { particle.gameObject.SetActive(true); particle.Play(true); }
        PlayRandom(initiationSounds);
        SpawnLaunchLight();
        launched?.Invoke();
        StartCoroutine(FlightRoutine(target, arcHeight, curve));
    }

    public void Launch(Vector3 target) => Launch(target, defaultArcHeight, defaultFlightCurve);

    System.Collections.IEnumerator FlightRoutine(Vector3 target, float arcHeight, AnimationCurve curve)
    {
        var start = transform.position;
        var direction = target - start;
        var duration = Mathf.Max(0.01f, flightDuration);
        var activeCurve = curve != null && curve.length > 0 ? curve : defaultFlightCurve;
        PlayRandom(flightSounds);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var position = Vector3.LerpUnclamped(start, target, t) + Vector3.up * (arcHeight * activeCurve.Evaluate(t));
            if (pointForwardAlongPath && t < 0.999f)
            {
                var next = Vector3.LerpUnclamped(start, target, Mathf.Min(1f, t + 0.01f)) + Vector3.up * (arcHeight * activeCurve.Evaluate(Mathf.Min(1f, t + 0.01f)));
                if ((next - position).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(next - position, Vector3.up);
            }
            transform.position = position;
            yield return null;
        }
        transform.position = target;
        flightCompleted?.Invoke();
    }

    void SpawnLaunchLight()
    {
        if (launchLightPrefab == null) return;
        var instance = Instantiate(launchLightPrefab, transform.position + transform.TransformVector(launchLightOffset), Quaternion.identity);
        if (launchLightLifetime > 0f) Destroy(instance, launchLightLifetime);
    }

    void PlayRandom(AudioClip[] clips)
    {
        if (audioSource == null || clips == null || clips.Length == 0) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) audioSource.PlayOneShot(clip);
    }
}
