using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using ExplosionQuads;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Independent event-driven missile flight and endpoint quad playback.</summary>
[DisallowMultipleComponent]
public sealed class MissileLauncherSystem : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Missile prefab assets. Each shot randomly picks one of the non-empty entries.")]
    [SerializeField] private GameObject[] missilePrefabs = new GameObject[0];
    [SerializeField, Min(1)] private int missileCount = 1;
    [Tooltip("Minimum time in seconds between shots. The first missile launches immediately.")]
    [SerializeField, Min(0f)] private float minLaunchDelay = 0.1f;
    [Tooltip("Maximum time in seconds between shots. Each gap is sampled independently.")]
    [SerializeField, Min(0f)] private float maxLaunchDelay = 0.35f;
    [SerializeField] private Transform pointB;
    [Tooltip("Launch center offset from Point B in world axes. Y controls launch height above the endpoint.")]
    [SerializeField] private Vector3 launchOffset = new Vector3(0f, 100f, 0f);
    [Tooltip("Maximum random launch offset in either direction on each world axis, sampled separately for every missile.")]
    [SerializeField] private Vector3 launchRandomness = new Vector3(25f, 10f, 25f);
    [Tooltip("Random impact radius around Point B on the world XZ plane. Impact height stays at Point B's Y.")]
    [SerializeField, Min(0f)] private float endPointRadius;
    [Tooltip("Flight time in seconds for each missile, regardless of its randomized travel distance.")]
    [SerializeField, Min(0.01f)] private float flightDuration = 4f;
    [Tooltip("The direction the missile nose points in prefab local space. Usually +Z; use +Y for an upright model.")]
    [SerializeField] private Vector3 modelForwardAxis = Vector3.forward;

    [Header("Impact sequence")]
    [Tooltip("A quad prefab with a QuadSequncerController and a Renderer on the same object.")]
    [SerializeField] private GameObject quadSequencerPrefab;
    [Tooltip("Each impact independently chooses a random non-empty material. If empty, the existing material is used.")]
    [SerializeField] private Material[] explosionMaterials = new Material[0];
    [Tooltip("Minimum uniform size multiplier for each impact quad, relative to its prefab scale. 1 keeps the original size.")]
    [SerializeField, Min(0.01f)] private float minQuadSize = 1f;
    [Tooltip("Maximum uniform size multiplier. Each impact samples independently; proportions are preserved.")]
    [SerializeField, Min(0.01f)] private float maxQuadSize = 1f;
    // Preserve material assignments serialized before the variants array was introduced.
    [HideInInspector]
    [SerializeField] private Material explosionMaterial;

    [Header("Presentation")]
    [SerializeField] private ExplosionFacing facing = ExplosionFacing.FaceCamera;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool flipFacing;

    [Header("Events")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent<Vector3> onImpact = new UnityEvent<Vector3>();
    [Tooltip("Invoked once per salvo when all missiles from that Fire call have arrived.")]
    public UnityEvent onCompleted = new UnityEvent();
    public UnityEvent onReset = new UnityEvent();

    private readonly List<Tween> flights = new List<Tween>();
    private readonly Dictionary<GameObject, Vector3> spawnedMissiles = new Dictionary<GameObject, Vector3>();
    private int runId;
    private readonly List<GameObject> effects = new List<GameObject>();

    private void OnDisable()
    {
        StopFlights();
        ClearEffects();
    }

    private void LateUpdate()
    {
        foreach (var flight in spawnedMissiles)
        {
            var missile = flight.Key;
            if (missile == null) continue;
            Vector3 direction = flight.Value - missile.transform.position;
            if (direction.sqrMagnitude > 0.000001f)
                missile.transform.rotation = GetFlightRotation(direction);
        }
    }

    private Quaternion GetFlightRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.000001f) return Quaternion.identity;
        Vector3 forward = direction.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
        Vector3 modelForward = modelForwardAxis.sqrMagnitude > 0.000001f ? modelForwardAxis.normalized : Vector3.forward;
        return Quaternion.LookRotation(forward, up) * Quaternion.FromToRotation(modelForward, Vector3.forward);
    }

    /// <summary>Starts an additional salvo without cancelling earlier launches or flights.</summary>
    [ContextMenu("Fire Missiles")]
    public void Fire()
    {
        if (!isActiveAndEnabled || !ValidatePrefab()) return;
        if (pointB == null || missilePrefabs == null || missilePrefabs.Length == 0)
        {
            Debug.LogWarning("MissileLauncherSystem needs missile prefabs and Point B.", this);
            return;
        }
        var availablePrefabs = new List<GameObject>();
        foreach (var prefab in missilePrefabs)
            if (prefab != null) availablePrefabs.Add(prefab);
        if (availablePrefabs.Count == 0)
        {
            Debug.LogWarning("MissileLauncherSystem needs at least one assigned missile prefab.", this);
            return;
        }
        flights.RemoveAll(flight => !flight.isAlive);
        Vector3 endCenter = pointB.position;
        int count = Mathf.Max(1, missileCount);
        int currentRun = runId;
        onStarted.Invoke();
        if (currentRun != runId) return;
        StartCoroutine(LaunchRoutine(availablePrefabs, endCenter, count, currentRun));
    }

    private IEnumerator LaunchRoutine(List<GameObject> availablePrefabs, Vector3 endCenter, int count, int currentRun)
    {
        // Each invocation owns its completion count; overlapping salvos cannot overwrite it.
        int remaining = count;
        Vector3 launchCenter = endCenter + launchOffset;
        float minimumDelay = Mathf.Max(0f, Mathf.Min(minLaunchDelay, maxLaunchDelay));
        float maximumDelay = Mathf.Max(0f, Mathf.Max(minLaunchDelay, maxLaunchDelay));
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                float delay = Random.Range(minimumDelay, maximumDelay);
                if (delay > 0f) yield return new WaitForSeconds(delay);
            }
            if (currentRun != runId) yield break;
            var prefab = availablePrefabs[Random.Range(0, availablePrefabs.Count)];
            Vector3 start = launchCenter + new Vector3(
                Random.Range(-Mathf.Abs(launchRandomness.x), Mathf.Abs(launchRandomness.x)),
                Random.Range(-Mathf.Abs(launchRandomness.y), Mathf.Abs(launchRandomness.y)),
                Random.Range(-Mathf.Abs(launchRandomness.z), Mathf.Abs(launchRandomness.z)));
            Vector2 endOffset = Random.insideUnitCircle * Mathf.Max(0f, endPointRadius);
            Vector3 destination = endCenter + new Vector3(endOffset.x, 0f, endOffset.y);
            Vector3 direction = destination - start;
            float duration = Mathf.Max(0.01f, flightDuration);
            var missile = Instantiate(prefab, start, GetFlightRotation(direction));
            // Prefab lifecycle callbacks can reset or disable the launcher during instantiation.
            if (currentRun != runId)
            {
                Destroy(missile);
                yield break;
            }
            spawnedMissiles.Add(missile, destination);
            missile.SetActive(true);
            if (currentRun != runId) yield break;
            flights.Add(Tween.Position(missile.transform, destination, duration, Ease.Linear).OnComplete(() =>
            {
                if (currentRun != runId) return;
                if (missile != null)
                {
                    missile.transform.position = destination;
                    spawnedMissiles.Remove(missile);
                    missile.SetActive(false);
                    Destroy(missile);
                    if (currentRun != runId) return;
                    HandleArrival(destination);
                }
                if (currentRun == runId && --remaining == 0) onCompleted.Invoke();
            }));
        }
    }

    [ContextMenu("Reset Missiles")]
    public void ResetSystem()
    {
        StopFlights();
        ClearEffects();
        onReset.Invoke();
    }

    private void StopFlights()
    {
        runId++;
        StopAllCoroutines();
        foreach (var flight in flights)
            if (flight.isAlive) flight.Stop();
        flights.Clear();
        foreach (var missile in spawnedMissiles.Keys)
            if (missile != null) Destroy(missile);
        spawnedMissiles.Clear();
    }

    private bool ValidatePrefab()
    {
        var sequence = quadSequencerPrefab != null
            ? quadSequencerPrefab.GetComponentInChildren<QuadSequncerController>(true)
            : null;
        var renderer = sequence != null ? sequence.GetComponent<Renderer>() : null;
        if (renderer != null && (HasExplosionMaterial() || renderer.sharedMaterial != null)) return true;
        Debug.LogWarning("MissileLauncherSystem needs a quad prefab with a QuadSequncerController, a Renderer on that same object, and a sequence material.", this);
        return false;
    }

    private void HandleArrival(Vector3 impactPosition)
    {
        if (!ValidatePrefab()) return;

        // Anchor to the captured endpoint, then apply the quad's world-space height adjustment.
        var instance = Instantiate(quadSequencerPrefab, impactPosition, quadSequencerPrefab.transform.rotation);
        float minimumSize = Mathf.Max(0.01f, Mathf.Min(minQuadSize, maxQuadSize));
        float maximumSize = Mathf.Max(0.01f, Mathf.Max(minQuadSize, maxQuadSize));
        instance.transform.localScale *= Random.Range(minimumSize, maximumSize);
        effects.RemoveAll(effect => effect == null);
        effects.Add(instance);
        var sequence = instance.GetComponentInChildren<QuadSequncerController>(true);
        var renderer = sequence.GetComponent<Renderer>();
        sequence.transform.position = impactPosition;
        instance.transform.position += Vector3.up * sequence.WorldYOffset;
        sequence.gameObject.SetActive(true);
        instance.SetActive(true);
        sequence.Initialize(PickExplosionMaterial(renderer.sharedMaterial));
        sequence.Play();

        var billboard = instance.GetComponent<ExplosionQuadBillboard>();
        if (billboard == null && facing != ExplosionFacing.PrefabRotation)
            billboard = instance.AddComponent<ExplosionQuadBillboard>();
        if (billboard != null)
            billboard.Configure(targetCamera != null ? targetCamera : Camera.main, facing, flipFacing, true, sequence.FacingAxis);

        Destroy(instance, sequence.GetLifetime());
        onImpact.Invoke(impactPosition);
    }

    private bool HasExplosionMaterial()
    {
        if (explosionMaterial != null) return true;
        if (explosionMaterials != null)
            foreach (var material in explosionMaterials)
                if (material != null) return true;
        return false;
    }

    private Material PickExplosionMaterial(Material fallback)
    {
        int count = 0;
        if (explosionMaterials != null)
            foreach (var material in explosionMaterials)
                if (material != null) count++;

        if (count > 0)
        {
            int index = Random.Range(0, count);
            foreach (var material in explosionMaterials)
                if (material != null && index-- == 0) return material;
        }

        return explosionMaterial != null ? explosionMaterial : fallback;
    }

    private void ClearEffects()
    {
        foreach (var effect in effects)
            if (effect != null) Destroy(effect);
        effects.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (pointB == null) return;
        Vector3 center = pointB.position;
        Gizmos.color = new Color(1f, 0.55f, 0.1f);
        Gizmos.DrawWireCube(center + launchOffset, new Vector3(
            Mathf.Abs(launchRandomness.x), Mathf.Abs(launchRandomness.y), Mathf.Abs(launchRandomness.z)) * 2f);
        Gizmos.DrawLine(center + launchOffset, center);
        Gizmos.color = new Color(0.2f, 0.8f, 1f);
        float radius = Mathf.Max(0f, endPointRadius);
        for (int i = 0; i < 48; i++)
        {
            float angleA = i * Mathf.PI * 2f / 48;
            float angleB = (i + 1) * Mathf.PI * 2f / 48;
            Gizmos.DrawLine(center + new Vector3(Mathf.Cos(angleA), 0f, Mathf.Sin(angleA)) * radius,
                center + new Vector3(Mathf.Cos(angleB), 0f, Mathf.Sin(angleB)) * radius);
        }
    }
}
