using System.Collections;
using UnityEngine;
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
    }

    public GameObject Spawn(Vector3 position, float radius, float lifetime = -1f)
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
        StartCoroutine(AnimateAndRelease(preDelay, duration, diameter, cloud, renderer));
        return cloud;
    }

    public static GameObject SpawnAt(Vector3 position, float radius, float lifetime = -1f)
        => Instance != null ? Instance.Spawn(position, radius, lifetime) : null;

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
            var collider = instance.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            if (fallbackMaterial != null)
                instance.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
        }
        instance.name = "CondensationCloud";
        return instance;
    }

    private static void OnGet(GameObject cloud) => cloud.SetActive(true);
    private static void OnRelease(GameObject cloud) => cloud.SetActive(false);
    private static void OnDestroy(GameObject cloud) { if (cloud != null) Destroy(cloud); }

    private IEnumerator AnimateAndRelease(float delay, float seconds, float diameter, GameObject cloud, Renderer renderer)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float attack = Mathf.Clamp(seconds * attackFraction, 0.01f, seconds);
        float elapsed = 0f;
        while (elapsed < seconds && cloud != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            float alpha;
            float scale;
            if (elapsed <= attack)
            {
                float rise = Mathf.Clamp01(elapsed / attack);
                rise = 1f - Mathf.Pow(1f - rise, 3f);
                alpha = rise * peakAlpha;
                scale = rise;
            }
            else
            {
                float fall = Mathf.Clamp01((elapsed - attack) / Mathf.Max(0.001f, seconds - attack));
                fall = fall * fall * (3f - 2f * fall);
                alpha = (1f - fall) * peakAlpha;
                // Wilson clouds dissipate in place with a restrained contraction
                // so the fade has a little physical motion without collapsing.
                scale = Mathf.Lerp(1f, 0.88f, fall);
            }

            cloud.transform.localScale = Vector3.one * (diameter * scale);
            SetCloudAlpha(renderer, alpha);
            yield return null;
        }

        if (cloud != null)
        {
            cloud.transform.localScale = Vector3.zero;
            SetCloudAlpha(renderer, 0f);
        }
        if (cloud != null && cloud.activeSelf && _pool != null)
            _pool.Release(cloud);
        activeCloudCount--;
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
        if (Instance == this) Instance = null;
        _pool?.Dispose();
    }
}
