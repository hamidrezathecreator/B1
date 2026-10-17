using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>Missile flight for prefab instances and missiles placed in a scene.</summary>
public sealed class MissileProjectile : MonoBehaviour
{
    // Register only this projectile system, avoiding scene searches during flight.
    static readonly HashSet<MissileProjectile> activeFlights = new HashSet<MissileProjectile>();
    public static IReadOnlyCollection<MissileProjectile> ActiveFlights => activeFlights;
    public bool IsInFlight { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActiveFlights() => activeFlights.Clear();

    void EndFlight()
    {
        IsInFlight = false;
        activeFlights.Remove(this);
        if (flightAudioSource != null) flightAudioSource.Stop();
    }

    void OnDisable() => EndFlight();
    void OnDestroy() => EndFlight();

    [Header("Flight")]
    [SerializeField, Min(0.01f)] float flightDuration = 2.5f;
    [Tooltip("1 is constant progress; 2 starts slowly and continuously accelerates. Higher values spend longer near the launcher.")]
    [SerializeField, Min(1f)] float accelerationPower = 2f;
    [SerializeField] float defaultArcHeight = 8f;
    [SerializeField] AnimationCurve defaultFlightCurve;
    [SerializeField] bool pointForwardAlongPath = true;

    [Header("Launch effects")]
    [SerializeField] ParticleSystem[] launchParticles;
    [Tooltip("Optional exhaust/trail prefab instantiated as a child of this missile on launch.")]
    [SerializeField] GameObject flightParticlePrefab;
    [Tooltip("Optional nozzle transform on this missile. Defaults to the missile root.")]
    [SerializeField] Transform flightParticleAnchor;
    [SerializeField] Vector3 flightParticleLocalOffset;
    [SerializeField] Vector3 flightParticleLocalEulerAngles;
    [Tooltip("Initial fraction of the attached effect's authored emission, size, and speed.")]
    [SerializeField, Range(0f, 1f)] float particleStartingPower = 0.05f;
    [Tooltip("Seconds after launch until the attached effect reaches its authored full power.")]
    [SerializeField, Min(0f)] float particleRampDuration = 1.25f;
    [SerializeField] GameObject launchLightPrefab;
    [SerializeField] Vector3 launchLightOffset;
    [SerializeField, Min(0f)] float launchLightLifetime = 0.45f;
    [Header("Attached flight light")]
    [Tooltip("Light prefab with MissileLaunchLight for intensity trembling. Follows this missile and ramps with the exhaust.")]
    [SerializeField] MissileLaunchLight flightLightPrefab;
    [Tooltip("Optional child nozzle transform. Defaults to the missile root.")]
    [SerializeField] Transform flightLightAnchor;
    [SerializeField] Vector3 flightLightLocalOffset;
    [SerializeField] Vector3 flightLightLocalEulerAngles;
    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip[] initiationSounds;
    [SerializeField] AudioClip[] flightSounds;
    [Tooltip("Flight loop volume before distance attenuation.")]
    [SerializeField, Range(0f, 1f)] float flightSoundVolume = 1f;
    [Tooltip("Distance from the listener within which the flight loop plays at full volume.")]
    [SerializeField, Min(0.01f)] float flightSoundMinDistance = 1f;
    [Tooltip("Distance from the listener at which the flight loop becomes silent.")]
    [SerializeField, Min(0.02f)] float flightSoundMaxDistance = 100f;

    public UnityEvent launched;
    public UnityEvent flightCompleted;
    bool launchedOnce;
    public bool HasLaunched => launchedOnce;
    bool poseCaptured;
    Transform initialParent;
    Vector3 initialLocalPosition;
    Quaternion initialLocalRotation;
    Vector3 initialLocalScale;
    bool initialActive;
    GameObject launchLightInstance;
    MissileLaunchLight flightLightInstance;
    GameObject flightParticleInstance;
    ParticleSystem[] flightParticleSystems;
    ParticlePowerSettings[] particlePowerSettings;
    AudioSource flightAudioSource;

    sealed class ParticlePowerSettings
    {
        public float rateOverTime, rateOverDistance, speed, size, sizeX, sizeY, sizeZ;
        public ParticleSystem.Burst[] bursts;
    }

    public void CaptureInitialPose()
    {
        if (poseCaptured) return;
        poseCaptured = true;
        initialParent = transform.parent;
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;
        initialActive = gameObject.activeSelf;
    }

    [ContextMenu("Test/Reset Projectile (Play Mode)")]
    public void ResetProjectile()
    {
        if (!Application.isPlaying || !poseCaptured) return;
        EndFlight();
        StopAllCoroutines();
        if (launchLightInstance != null) Destroy(launchLightInstance);
        ClearFlightParticles();
        ClearFlightLight();
        if (launchParticles != null)
            foreach (var particle in launchParticles)
                if (particle != null) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (audioSource != null) audioSource.Stop();
        transform.SetParent(initialParent, false);
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        transform.localScale = initialLocalScale;
        launchedOnce = false;
        gameObject.SetActive(initialActive);
    }

    void Reset()
    {
        defaultFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        audioSource = GetComponent<AudioSource>();
    }

    void Awake()
    {
        CaptureInitialPose();
        if (defaultFlightCurve == null || defaultFlightCurve.length == 0) defaultFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public void Launch(Vector3 target, float arcHeight, AnimationCurve curve)
    {
        if (!Application.isPlaying || launchedOnce) return;
        CaptureInitialPose();
        gameObject.SetActive(true);
        enabled = true;
        transform.SetParent(null, true);
        launchedOnce = true;
        IsInFlight = true;
        activeFlights.Add(this);
        if (launchParticles != null)
            foreach (var particle in launchParticles) if (particle != null) { particle.gameObject.SetActive(true); particle.Play(true); }
        PlayRandom(initiationSounds);
        SpawnLaunchLight();
        SpawnFlightParticles();
        SpawnFlightLight();
        launched?.Invoke();
        StartCoroutine(FlightRoutine(target, arcHeight, curve));
    }

    public void Launch(Vector3 target) => Launch(target, defaultArcHeight, defaultFlightCurve);

    System.Collections.IEnumerator FlightRoutine(Vector3 target, float arcHeight, AnimationCurve curve)
    {
        var start = transform.position;
        var duration = Mathf.Max(0.01f, flightDuration);
        var activeCurve = curve != null && curve.length > 0 ? curve : defaultFlightCurve;
        PlayFlightSound();
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            UpdateFlightEffectPower(elapsed);
            var t = Mathf.Clamp01(elapsed / duration);
            var progress = Mathf.Pow(t, Mathf.Max(1f, accelerationPower));
            var position = FlightPosition(start, target, progress, arcHeight, activeCurve);
            if (pointForwardAlongPath && t < 0.999f)
            {
                var next = FlightPosition(start, target, Mathf.Min(1f, progress + 0.001f), arcHeight, activeCurve);
                if ((next - position).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(next - position, Vector3.up);
            }
            transform.position = position;
            yield return null;
        }
        transform.position = target;
        EndFlight();
        StopFlightParticles();
        ClearFlightLight();
        flightCompleted?.Invoke();
    }

    static Vector3 FlightPosition(Vector3 start, Vector3 target, float progress, float arcHeight, AnimationCurve curve)
    {
        // Keep both endpoints on the path: the old height curve left an offset that snapped away on completion.
        var arc = 4f * progress * (1f - progress) * curve.Evaluate(progress);
        return Vector3.LerpUnclamped(start, target, progress) + Vector3.up * (arcHeight * arc);
    }

    void SpawnFlightParticles()
    {
        if (flightParticlePrefab == null) return;
        var anchor = flightParticleAnchor != null && flightParticleAnchor.IsChildOf(transform) ? flightParticleAnchor : transform;
        flightParticleInstance = Instantiate(flightParticlePrefab, anchor, false);
        flightParticleInstance.transform.localPosition = flightParticleLocalOffset;
        flightParticleInstance.transform.localRotation = Quaternion.Euler(flightParticleLocalEulerAngles);
        flightParticleInstance.SetActive(true);
        flightParticleSystems = flightParticleInstance.GetComponentsInChildren<ParticleSystem>(true);
        particlePowerSettings = new ParticlePowerSettings[flightParticleSystems.Length];
        for (var i = 0; i < flightParticleSystems.Length; i++)
        {
            var particle = flightParticleSystems[i];
            if (particle == null) continue;
            var main = particle.main;
            var emission = particle.emission;
            var settings = new ParticlePowerSettings
            {
                rateOverTime = emission.rateOverTimeMultiplier,
                rateOverDistance = emission.rateOverDistanceMultiplier,
                speed = main.startSpeedMultiplier,
                size = main.startSizeMultiplier,
                sizeX = main.startSizeXMultiplier,
                sizeY = main.startSizeYMultiplier,
                sizeZ = main.startSizeZMultiplier,
                bursts = new ParticleSystem.Burst[emission.burstCount]
            };
            emission.GetBursts(settings.bursts);
            particlePowerSettings[i] = settings;
            // Clear any play-on-awake/prewarmed particles before starting at reduced power.
            main.stopAction = ParticleSystemStopAction.None;
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            main.prewarm = false;
            main.playOnAwake = false;
        }
        UpdateFlightEffectPower(0f);
        foreach (var particle in flightParticleSystems)
            if (particle != null && particle.gameObject.activeInHierarchy) particle.Play(false);
    }

    void UpdateFlightEffectPower(float elapsed)
    {
        var power = FlightEffectPower(elapsed);
        if (flightLightInstance != null) flightLightInstance.SetPower(power);
        if (flightParticleSystems == null || particlePowerSettings == null) return;
        for (var i = 0; i < flightParticleSystems.Length; i++)
        {
            var particle = flightParticleSystems[i];
            var settings = particlePowerSettings[i];
            if (particle == null || settings == null) continue;
            var main = particle.main;
            var emission = particle.emission;
            emission.rateOverTimeMultiplier = settings.rateOverTime * power;
            emission.rateOverDistanceMultiplier = settings.rateOverDistance * power;
            main.startSpeedMultiplier = settings.speed * power;
            if (main.startSize3D)
            {
                main.startSizeXMultiplier = settings.sizeX * power;
                main.startSizeYMultiplier = settings.sizeY * power;
                main.startSizeZMultiplier = settings.sizeZ * power;
            }
            else main.startSizeMultiplier = settings.size * power;
            for (var j = 0; j < settings.bursts.Length; j++)
            {
                var burst = settings.bursts[j];
                var count = burst.count;
                switch (count.mode)
                {
                    case ParticleSystemCurveMode.Constant: count.constant *= power; break;
                    case ParticleSystemCurveMode.TwoConstants:
                        count.constantMin *= power;
                        count.constantMax *= power;
                        break;
                    default: count.curveMultiplier *= power; break;
                }
                burst.count = count;
                emission.SetBurst(j, burst);
            }
        }
    }

    float FlightEffectPower(float elapsed)
    {
        var t = particleRampDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / particleRampDuration);
        return Mathf.SmoothStep(Mathf.Clamp01(particleStartingPower), 1f, t);
    }

    void SpawnFlightLight()
    {
        if (flightLightPrefab == null) return;
        var anchor = flightLightAnchor != null && flightLightAnchor.IsChildOf(transform) ? flightLightAnchor : transform;
        flightLightInstance = Instantiate(flightLightPrefab, anchor, false);
        flightLightInstance.transform.localPosition = flightLightLocalOffset;
        flightLightInstance.transform.localRotation = Quaternion.Euler(flightLightLocalEulerAngles);
        flightLightInstance.gameObject.SetActive(true);
        flightLightInstance.SetPower(FlightEffectPower(0f));
    }

    void ClearFlightLight()
    {
        if (flightLightInstance == null) return;
        flightLightInstance.gameObject.SetActive(false);
        Destroy(flightLightInstance.gameObject);
        flightLightInstance = null;
    }

    void StopFlightParticles()
    {
        if (flightParticleSystems == null) return;
        foreach (var particle in flightParticleSystems)
            if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    void ClearFlightParticles()
    {
        if (flightParticleInstance != null)
        {
            foreach (var particle in flightParticleSystems)
                if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            flightParticleInstance.SetActive(false);
            Destroy(flightParticleInstance);
        }
        flightParticleInstance = null;
        flightParticleSystems = null;
        particlePowerSettings = null;
    }

    void SpawnLaunchLight()
    {
        if (launchLightPrefab == null) return;
        var instance = Instantiate(launchLightPrefab, transform.position + transform.TransformVector(launchLightOffset), Quaternion.identity);
        launchLightInstance = instance;
        if (launchLightLifetime > 0f) Destroy(instance, launchLightLifetime);
    }

    void PlayFlightSound()
    {
        if (flightSounds == null || flightSounds.Length == 0) return;
        var clip = flightSounds[Random.Range(0, flightSounds.Length)];
        if (clip == null) return;
        if (flightAudioSource == null)
        {
            var soundObject = new GameObject("Flight Audio");
            soundObject.transform.SetParent(transform, false);
            flightAudioSource = soundObject.AddComponent<AudioSource>();
        }
        flightAudioSource.playOnAwake = false;
        flightAudioSource.loop = true;
        flightAudioSource.spatialBlend = 1f;
        flightAudioSource.rolloffMode = AudioRolloffMode.Linear;
        flightAudioSource.minDistance = Mathf.Max(0.01f, flightSoundMinDistance);
        flightAudioSource.maxDistance = Mathf.Max(flightAudioSource.minDistance + 0.01f, flightSoundMaxDistance);
        flightAudioSource.dopplerLevel = 0f;
        flightAudioSource.volume = Mathf.Clamp01(flightSoundVolume) * (audioSource != null ? audioSource.volume : 1f);
        if (audioSource != null)
        {
            flightAudioSource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
            flightAudioSource.pitch = audioSource.pitch;
            flightAudioSource.priority = audioSource.priority;
            flightAudioSource.mute = audioSource.mute;
        }
        flightAudioSource.clip = clip;
        flightAudioSource.Play();
    }

    void PlayRandom(AudioClip[] clips)
    {
        if (audioSource == null || clips == null || clips.Length == 0) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) audioSource.PlayOneShot(clip);
    }
}
