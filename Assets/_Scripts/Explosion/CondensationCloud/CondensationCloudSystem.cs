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
    [SerializeField, Range(0.05f, 0.9f)] private float attackFraction = 0.5f;
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
    [Tooltip("Distance to the headset drives pressure arrival. Assigning an XR Origin or one of its children resolves to its tracked camera. Empty = scene XR Origin camera.")]
    [SerializeField] private Transform playerListener;
    [SerializeField] private bool notifyWhenCloudReachesPlayer = true;
    [Tooltip("Controller vibration strength when the cloud reaches the player. Zero disables vibration.")]
    [SerializeField, Range(0f, 1f)] private float playerEngulfVibrationAmplitude = 0.8f;
    [SerializeField, Min(0f)] private float playerEngulfVibrationSeconds = 0.4f;
    [Tooltip("Sound when the expanding cloud reaches the player. Empty uses a generated pressure hit.")]
    [SerializeField] private AudioClip playerEngulfSound;
    [SerializeField, Range(0f, 1f)] private float playerEngulfSoundVolume = 0.7f;
    public UnityEvent<Vector3> onPlayerEngulfed = new UnityEvent<Vector3>();
    [Header("Cloud-thrown debris")]
    [SerializeField] private Transform[] objectsToThrow = Array.Empty<Transform>();
    [SerializeField, Min(0.01f)] private float throwSpeed = 85f;
    [SerializeField, Min(0.05f)] private float maximumThrowSeconds = 0.75f;
    [SerializeField, Min(0f)] private float throwSpacing = 0.12f;
    [SerializeField, Min(0.01f)] private float playerHitRadius = 0.65f;
    private readonly System.Collections.Generic.HashSet<Transform> thrownObjects = new();
    private bool firstPressureArrivalReceived;
    private Transform cachedListener;
    private Transform cachedListenerSource;
    private int flyingDebris;
    private int pendingPressureWaves;
    public bool HasFlyingDebris => flyingDebris > 0 || pendingPressureWaves > 0;

    /// <summary>Fired once when the distance-based pressure front reaches the listener.</summary>
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
        float duration = lifetime > 0f ? lifetime : defaultLifetime;
        float worldRadius = Mathf.Max(0.005f, radius);
        var scale = timeScale ?? ExplosionTimeScaleSettings.Current;
        if (notifyWhenCloudReachesPlayer && ResolveListener() != null)
        {
            pendingPressureWaves++;
            StartCoroutine(TrackPressureArrival(position, worldRadius, duration, scale));
        }
        if (_pool == null || activeCloudCount >= Mathf.Max(1, maxSimultaneousClouds)) return null;
        var cloud = _pool.Get();
        activeCloudCount++;
        cloud.transform.SetPositionAndRotation(position, Quaternion.identity);
        float diameter = Mathf.Max(0.01f, radius * 2f);
        cloud.transform.localScale = Vector3.zero;
        var renderer = cloud.GetComponentInChildren<Renderer>();
        SetCloudAlpha(renderer, 0f);
        StartCoroutine(AnimateAndRelease(preDelay, duration, diameter, cloud, renderer, scale));
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
        foreach (var collider in instance.GetComponentsInChildren<Collider>()) collider.enabled = false;
        instance.name = "CondensationCloud";
        return instance;
    }

    private static void OnGet(GameObject cloud) => cloud.SetActive(true);
    private static void OnRelease(GameObject cloud) => cloud.SetActive(false);
    private static void OnDestroy(GameObject cloud) { if (cloud != null) Destroy(cloud); }

    private IEnumerator AnimateAndRelease(float delay, float seconds, float diameter, GameObject cloud, Renderer renderer,
        ExplosionTimeScaleSettings.Profile timeScale)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float attack = Mathf.Clamp(seconds * attackFraction, 0.01f, seconds);
        float playback = 0f;
        float elapsed = 0f;
        yield return null;
        while (elapsed < seconds && cloud != null)
        {
            playback += Time.deltaTime;
            elapsed += Time.deltaTime * timeScale.Evaluate(playback);
            float expansion = Mathf.Clamp01(elapsed / attack);
            float fade = Mathf.SmoothStep(0f, 1f, (elapsed - attack) / Mathf.Max(0.001f, seconds - attack));
            cloud.transform.localScale = Vector3.one * diameter * expansion * Mathf.Lerp(1f, 0.88f, fade);
            SetCloudAlpha(renderer, peakAlpha * Mathf.Clamp01(elapsed / Mathf.Min(0.15f, attack)) * (1f - fade));
            yield return null;
        }
        if (cloud != null)
        {
            cloud.transform.localScale = Vector3.zero;
            SetCloudAlpha(renderer, 0f);
            if (cloud.activeSelf && _pool != null) _pool.Release(cloud);
        }
        activeCloudCount--;
    }

    IEnumerator TrackPressureArrival(Vector3 center, float radius, float seconds, ExplosionTimeScaleSettings.Profile timeScale)
    {
        if (preDelay > 0f) yield return new WaitForSeconds(preDelay);
        float attack = Mathf.Clamp(seconds * attackFraction, 0.01f, seconds);
        float playback = 0f;
        float elapsed = 0f;
        yield return null;
        while (true)
        {
            var listener = ResolveListener();
            if (listener == null) break;
            playback += Time.deltaTime;
            elapsed += Time.deltaTime * timeScale.Evaluate(playback);
            float distanceTraveled = radius * Mathf.Clamp01(elapsed / attack);
            if (Vector3.Distance(listener.position, center) <= distanceTraveled + playerHitRadius)
            {
                NotifyPressureArrival(listener, center, distanceTraveled);
                break;
            }
            if (elapsed >= attack) break;
            yield return null;
        }
        pendingPressureWaves--;
    }

    void NotifyPressureArrival(Transform listener, Vector3 center, float distanceTraveled)
    {
        bool throwDebris = !firstPressureArrivalReceived;
        firstPressureArrivalReceived = true;
        ControllerHaptics.Pulse(playerEngulfVibrationAmplitude, playerEngulfVibrationSeconds);
        PlayerImpactAudio.PlayCloudHit(playerEngulfSound, playerEngulfSoundVolume);
        PlayerEngulfed?.Invoke(center, distanceTraveled);
        onPlayerEngulfed.Invoke(center);
        if (!throwDebris) return;
        for (int objectIndex = 0; objectIndex < objectsToThrow.Length; objectIndex++)
        {
            var debris = objectsToThrow[objectIndex];
            if (debris == null || debris == listener || listener.IsChildOf(debris) || !thrownObjects.Add(debris)) continue;
            flyingDebris++;
            StartCoroutine(ThrowAtPlayer(debris, listener, objectIndex * throwSpacing));
        }
    }

    IEnumerator ThrowAtPlayer(Transform debris, Transform listener, float delaySeconds)
    {
        if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);
        if (debris == null || listener == null) { flyingDebris--; yield break; }
        Tween.StopAll(debris);
        debris.SetParent(null, true);
        debris.gameObject.SetActive(true);
        foreach (var body in debris.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
        foreach (var collider in debris.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var visual = debris.GetComponentInChildren<Renderer>();
        var startPosition = visual != null ? visual.bounds.center : debris.position;
        float travelSeconds = Mathf.Clamp((Vector3.Distance(startPosition, listener.position) - playerHitRadius) / Mathf.Max(0.01f, throwSpeed),
            0.05f, maximumThrowSeconds);
        float elapsed = 0f;
        yield return null;
        while (elapsed < travelSeconds && listener != null)
        {
            elapsed += Time.deltaTime;
            if (debris != null)
            {
                debris.Rotate(new Vector3(80f, 120f, 65f) * Time.deltaTime, Space.Self);
                var center = visual != null ? visual.bounds.center : debris.position;
                debris.position += Vector3.Lerp(startPosition, listener.position, Mathf.Clamp01(elapsed / travelSeconds)) - center;
            }
            if (elapsed >= travelSeconds) break;
            yield return null;
        }
        if (listener != null)
        {
            var shake = listener.GetComponentInParent<XRExplosionShake>();
            if (shake != null) shake.HeavyImpact();
            ControllerHaptics.Pulse(1f, 0.25f);
            PlayerImpactAudio.PlayCloudHit(volume: 0.9f);
        }
        if (debris != null) debris.gameObject.SetActive(false);
        flyingDebris--;
    }

    Transform ResolveListener()
    {
        if (cachedListener != null && cachedListener.gameObject.activeInHierarchy && cachedListenerSource == playerListener)
            return cachedListener;
        cachedListenerSource = playerListener;
        if (playerListener != null)
        {
            var assignedOrigin = playerListener.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            cachedListener = assignedOrigin != null && assignedOrigin.Camera != null
                ? assignedOrigin.Camera.transform : playerListener;
            return cachedListener;
        }
        foreach (var origin in FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (origin.Camera == null || origin.gameObject.scene != gameObject.scene) continue;
            cachedListener = origin.Camera.transform;
            return cachedListener;
        }
        return null;
    }

    private static void SetCloudAlpha(Renderer renderer, float alpha)
    {
        if (renderer == null) return;
        renderer.GetPropertyBlock(SharedBlock);
        Color color = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(MainColorId)
            ? renderer.sharedMaterial.GetColor(MainColorId) : Color.white;
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
