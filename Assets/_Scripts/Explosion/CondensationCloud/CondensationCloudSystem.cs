using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Pool;

/// <summary>
/// Shared pooled Wilson-cloud spawn point for explosions. The visual prefab is
/// intentionally replaceable: the first pass only owns lifetime, pooling and
/// radius scaling; the mobile pseudo-volume shader can be swapped in later.
/// </summary>
[DisallowMultipleComponent]
public sealed class CondensationCloudSystem : MonoBehaviour
{
    public static CondensationCloudSystem Instance { get; private set; }

    [Header("Cloud Visual")]
    [SerializeField] private GameObject cloudPrefab;
    [SerializeField] private Material fallbackMaterial;
    [SerializeField, Min(0.01f)] private float defaultLifetime = 0.35f;
    [SerializeField, Range(0f, 0.2f)] private float preDelay = 0.03f;
    [SerializeField, Range(0.05f, 0.5f)] private float attackFraction = 0.2f;
    [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.75f;
    [SerializeField, Min(1)] private int initialCapacity = 12;
    [SerializeField, Min(1)] private int maxPoolSize = 64;
    [Tooltip("Maximum clouds including those waiting for the pre-delay. Extra spawns are skipped.")]
    [SerializeField, Min(1)] private int maxSimultaneousClouds = 12;

    [Header("Explosion time scale")]
    [Tooltip("Playback speed at the first frame of each explosion (quads and Wilson clouds).")]
    [SerializeField, Min(0f)] private float startTimeScale = 0.25f;
    [Tooltip("Playback speed after the ramp finishes.")]
    [SerializeField, Min(0f)] private float endTimeScale = 1f;
    [Tooltip("Wall-clock seconds to ramp from Start to End time scale.")]
    [SerializeField, Min(0f)] private float timeScaleRampDuration = 0.15f;

    [Header("Player engulfment")]
    [Tooltip("Expanding cloud radius is tested against this point (usually the headset). Empty = XR Origin camera in the cloud's scene.")]
    [SerializeField] private Transform playerListener;
    [SerializeField] private bool notifyWhenCloudReachesPlayer = true;
    [Tooltip("Controller vibration strength when the cloud reaches the player. Zero disables vibration.")]
    [SerializeField, Range(0f, 1f)] private float playerEngulfVibrationAmplitude = 0.8f;
    [SerializeField, Min(0f)] private float playerEngulfVibrationSeconds = 0.4f;
    [Tooltip("Sound when the expanding cloud reaches the player. Empty uses a generated pressure hit.")]
    [SerializeField] private AudioClip playerEngulfSound;
    [SerializeField, Range(0f, 1f)] private float playerEngulfSoundVolume = 0.7f;
    public UnityEvent<Vector3> onPlayerEngulfed = new UnityEvent<Vector3>();

    /// <summary>Fired once per cloud when the listener enters the expanding sphere (world center + radius).</summary>
    public static event Action<Vector3, float> PlayerEngulfed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetEngulfEvent() => PlayerEngulfed = null;

    private ObjectPool<GameObject> _pool;
    private int activeCloudCount;
    private static int MainColorId;
    private static MaterialPropertyBlock SharedBlock;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (MainColorId == 0) MainColorId = Shader.PropertyToID("_MainColor");
        SharedBlock ??= new MaterialPropertyBlock();
        _pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroy,
            collectionCheck: true, defaultCapacity: initialCapacity, maxSize: maxPoolSize);
        PublishTimeScale();
    }

    private void OnEnable() => PublishTimeScale();

    private void OnValidate() => PublishTimeScale();

    void PublishTimeScale() =>
        ExplosionTimeScaleSettings.Apply(new ExplosionTimeScaleSettings.Profile(
            startTimeScale, endTimeScale, timeScaleRampDuration));

    public GameObject Spawn(Vector3 position, float radius, float lifetime = -1f,
        ExplosionTimeScaleSettings.Profile? timeScale = null, bool useUnscaledTime = false)
    {
        if (_pool == null || activeCloudCount >= Mathf.Max(1, maxSimultaneousClouds)) return null;
        var cloud = _pool.Get();
        activeCloudCount++;
        cloud.transform.SetPositionAndRotation(position, Quaternion.identity);
        float diameter = Mathf.Max(0.01f, radius * 2f);
        float duration = lifetime > 0f ? lifetime : defaultLifetime;
        cloud.transform.localScale = Vector3.zero;
        var renderer = cloud.GetComponentInChildren<Renderer>();
        SetCloudAlpha(renderer, 0f);
        var scale = timeScale ?? ExplosionTimeScaleSettings.Current;
        StartCoroutine(AnimateAndRelease(preDelay, duration, diameter, cloud, renderer, scale, useUnscaledTime));
        return cloud;
    }

    public static GameObject SpawnAt(Vector3 position, float radius, float lifetime = -1f,
        ExplosionTimeScaleSettings.Profile? timeScale = null, bool useUnscaledTime = false)
        => Instance != null ? Instance.Spawn(position, radius, lifetime, timeScale, useUnscaledTime) : null;

    private GameObject Create()
    {
        GameObject instance;
        if (cloudPrefab != null)
        {
            instance = Instantiate(cloudPrefab, transform);
        }
        else
        {
            instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            instance.name = "CondensationCloud_Fallback";
            instance.transform.SetParent(transform, false);

            if (fallbackMaterial != null)
                instance.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
        }
        var sphere = instance.GetComponent<SphereCollider>();
        if (sphere == null) sphere = instance.AddComponent<SphereCollider>();
        sphere.center = Vector3.zero;
        sphere.radius = 0.5f;
        sphere.isTrigger = true;
        var body = instance.GetComponent<Rigidbody>();
        if (body == null) body = instance.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        instance.name = "CondensationCloud";
        return instance;
    }

    private static void OnGet(GameObject cloud) => cloud.SetActive(true);
    private static void OnRelease(GameObject cloud) => cloud.SetActive(false);
    private static void OnDestroy(GameObject cloud) { if (cloud != null) Destroy(cloud); }

    private IEnumerator AnimateAndRelease(float delay, float seconds, float diameter, GameObject cloud, Renderer renderer,
        ExplosionTimeScaleSettings.Profile timeScale, bool useUnscaledTime)
    {
        if (delay > 0f)
        {
            if (useUnscaledTime) yield return new WaitForSecondsRealtime(delay);
            else yield return new WaitForSeconds(delay);
        }
        float attack = Mathf.Clamp(seconds * attackFraction, 0.01f, seconds);
        float wallPlayback = 0f;
        float virtualElapsed = 0f;
        bool engulfNotified = false;
        // PrimeTween uses the same global scaled clock as particles and bridge debris.
        var expansion = Tween.Scale(cloud.transform, 0f, diameter, attack, ease: Ease.OutCubic, useUnscaledTime: useUnscaledTime);
        Tween contraction = default;
        bool contracting = false;
        // The frame before spawning is not part of this cloud's lifetime.
        yield return null;
        while (virtualElapsed < seconds && cloud != null)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            wallPlayback += dt;
            float playbackScale = timeScale.Evaluate(wallPlayback);
            virtualElapsed += dt * playbackScale;
            if (expansion.isAlive) expansion.timeScale = playbackScale;
            float alpha;
            if (virtualElapsed <= attack)
            {
                float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(virtualElapsed / attack), 3f);
                alpha = rise * peakAlpha;
            }
            else
            {
                if (!contracting)
                {
                    contracting = true;
                    if (expansion.isAlive) expansion.Complete();
                    contraction = Tween.Scale(cloud.transform, diameter, diameter * 0.88f,
                        Mathf.Max(0.001f, seconds - attack), ease: Ease.InOutSine, useUnscaledTime: useUnscaledTime);
                }
                if (contraction.isAlive) contraction.timeScale = playbackScale;
                float fall = Mathf.Clamp01((virtualElapsed - attack) / Mathf.Max(0.001f, seconds - attack));
                fall = fall * fall * (3f - 2f * fall);
                alpha = (1f - fall) * peakAlpha;
            }
            SetCloudAlpha(renderer, alpha);
            // Test the actual sphere each rendered frame. XR headsets need no collider,
            // and this remains accurate while fixed-step physics is frozen.
            var sphere = cloud.GetComponent<SphereCollider>();
            Vector3 center = sphere.transform.TransformPoint(sphere.center);
            Vector3 lossy = sphere.transform.lossyScale;
            float worldRadius = sphere.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
            TryNotifyPlayerEngulfed(cloud, center, worldRadius, ref engulfNotified);
            yield return null;
        }
        if (expansion.isAlive) expansion.Stop();
        if (contraction.isAlive) contraction.Stop();

        if (cloud != null)
        {
            cloud.transform.localScale = Vector3.zero;
            SetCloudAlpha(renderer, 0f);
        }
        if (cloud != null && cloud.activeSelf && _pool != null)
            _pool.Release(cloud);
        activeCloudCount--;
    }

    void TryNotifyPlayerEngulfed(GameObject cloud, Vector3 center, float worldRadius, ref bool fired)
    {
        if (!notifyWhenCloudReachesPlayer || fired || cloud == null || worldRadius <= 0.0001f) return;
        Transform listener = ResolveListener(cloud);
        if (listener == null) return;
        if (Vector3.Distance(listener.position, center) > worldRadius) return;
        fired = true;
        ControllerHaptics.Pulse(playerEngulfVibrationAmplitude, playerEngulfVibrationSeconds);
        PlayerImpactAudio.PlayCloudHit(playerEngulfSound, playerEngulfSoundVolume);
        PlayerEngulfed?.Invoke(center, worldRadius);
        onPlayerEngulfed.Invoke(center);
    }

    Transform ResolveListener(GameObject cloud)
    {
        if (playerListener != null) return playerListener;
        foreach (var origin in FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (origin.Camera == null || origin.gameObject.scene != cloud.scene) continue;
            return origin.Camera.transform;
        }
        return null;
    }

    private static void SetCloudAlpha(Renderer renderer, float alpha)
    {
        if (renderer == null) return;
        renderer.GetPropertyBlock(SharedBlock);
        Color color = SharedBlock.GetColor(MainColorId);
        if (color.r == 0f && color.g == 0f && color.b == 0f && color.a == 0f) color = Color.white;
        color.a = Mathf.Clamp01(alpha);
        SharedBlock.SetColor(MainColorId, color);
        renderer.SetPropertyBlock(SharedBlock);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            ExplosionTimeScaleSettings.Clear();
        }
        _pool?.Dispose();
    }
}
