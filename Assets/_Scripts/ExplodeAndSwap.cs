using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// When a launch button is pressed, each target in turn blows up: an explosion, the intact model
/// switches off and its destroyed version switches on, then it keeps burning for a while.
/// Destroyed versions are hidden at the start.
/// </summary>
[AddComponentMenu("B1/Explode And Swap")]
public sealed class ExplodeAndSwap : MonoBehaviour
{
    [Serializable]
    public sealed class Target
    {
        [Tooltip("Intact model; switched off when it blows up.")]
        public GameObject intact;
        [Tooltip("Destroyed model; hidden at the start, switched on when it blows up.")]
        public GameObject destroyed;
        [Tooltip("Seconds to wait before this shot: first target from the button press, later targets after the previous hit.")]
        [Min(0f)] public float delay;
    }

    [Header("Trigger")]
    [Tooltip("Assigned launch button. Leave empty when the level state machine calls Play.")]
    [SerializeField] MissileLaunchButton triggerButton;
    [SerializeField] bool controlledBySequence;
    [SerializeField] XRExplosionShake headsetShake;
    [Tooltip("Start when the launched object reaches Point B instead of when the button is pressed.")]
    [SerializeField] bool waitForArrival;

    [Header("Targets (in order)")]
    [SerializeField] Target[] targets =
    {
        new Target { delay = 3f },
        // Delay is after the previous hit finishes (not from the button press).
        new Target { delay = 3f },
    };

    [Header("Fighter jet")]
    [Tooltip("Assigned flight used for the strikes.")]
    [SerializeField] FighterJetFlight jet;

    [Header("Effects")]
    [Tooltip("Explosion prefab, e.g. DAVFX Explosion 13. Empty = built-in fiery burst.")]
    [SerializeField] GameObject explosionPrefab;
    [Min(0.1f)] [SerializeField] float explosionScale = 1f;
    [SerializeField] bool burnAfterwards = true;
    [Tooltip("How long it keeps burning, seconds. 0 = until the level ends.")]
    [Min(0f)] [SerializeField] float burnSeconds = 20f;
    [Min(0.1f)] [SerializeField] float fireSize = 1f;
    [SerializeField] bool shakeHeadset = true;
    [SerializeField] AudioClip impactSound;
    [Range(0f, 1f)] [SerializeField] float impactVolume = 1f;

    bool played;
    public bool IsComplete { get; private set; }
    public bool HasValidAssignments => jet != null && targets != null && targets.Length > 0 &&
        Array.TrueForAll(targets, item => item != null && item.intact != null && item.destroyed != null);

    public void SetImpactSound(AudioClip clip)
    {
        if (clip != null) impactSound = clip;
    }

    void Start()
    {
        if (!HasValidAssignments)
        {
            Debug.LogError("ExplodeAndSwap: assign the flight and every intact and destroyed target.", this);
            enabled = false;
            return;
        }
        foreach (var target in targets) target.destroyed.SetActive(false);
        if (controlledBySequence || triggerButton == null) return;
        if (waitForArrival) triggerButton.onArrived.AddListener(Play);
        else triggerButton.onPressed.AddListener(Play);
    }

    void OnDestroy()
    {
        if (triggerButton == null) return;
        triggerButton.onArrived.RemoveListener(Play);
        triggerButton.onPressed.RemoveListener(Play);
    }

    /// <summary>Starts the sequence; also usable from a UnityEvent or Timeline signal.</summary>
    public void Play()
    {
        if (played || !HasValidAssignments) return;
        played = true;
        StartCoroutine(RunAll());
    }

    IEnumerator RunAll()
    {
        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (t == null) continue;
            yield return Run(t);
        }
        IsComplete = true;
    }

    IEnumerator Run(Target t)
    {
        if (t.delay > 0f) yield return new WaitForSeconds(t.delay);

        var reference = t.intact != null && t.intact.activeInHierarchy ? t.intact : t.destroyed;
        if (reference == null || (t.intact != null && !t.intact.activeInHierarchy && t.destroyed == null))
        {
            Debug.LogWarning("ExplodeAndSwap: a target has neither an intact nor a destroyed model.", this);
            yield break;
        }
        if (t.intact != null && !t.intact.activeInHierarchy)
            yield break;

        var bounds = WorldBounds(t.intact != null ? t.intact : reference);
        var center = bounds.center;

        if (jet != null && jet.isActiveAndEnabled)
        {
            // Home onto the live object so a circling jet still hits it.
            var hit = false;
            var follow = t.intact != null ? t.intact.transform : reference.transform;
            var timeout = 12f;
            jet.FireMissileAt(follow, () => hit = true);
            while (!hit && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (t.intact != null && t.intact.activeInHierarchy)
                center = WorldBounds(t.intact).center;
        }

        SpawnHitEffects(center, bounds);

        if (shakeHeadset) ShakeHeadset(center);

        // Keep the wreck at the exact position and orientation of the intact target.
        if (t.destroyed != null && t.intact != null && t.destroyed != t.intact)
            t.destroyed.transform.SetPositionAndRotation(t.intact.transform.position, t.intact.transform.rotation);

        // Swap at the flash so the change is hidden by the explosion.
        if (t.destroyed != null) t.destroyed.SetActive(true);
        if (t.intact != null && t.intact != t.destroyed) t.intact.SetActive(false);

        if (burnAfterwards)
        {
            var burning = t.destroyed != null && t.destroyed.activeInHierarchy
                ? WorldBounds(t.destroyed)
                : bounds;
            var radius = Mathf.Clamp(Mathf.Max(burning.extents.x, burning.extents.z) * 0.6f, 0.4f, 4f);
            var size = Mathf.Clamp(burning.size.magnitude / 5f, 0.6f, 3f) * fireSize;
            var basePoint = new Vector3(burning.center.x, burning.min.y + 0.1f, burning.center.z);
            LaunchSmoke.CreateFire(basePoint, radius, size, burnSeconds);
        }
    }

    void SpawnHitEffects(Vector3 center, Bounds bounds)
    {
        ThermalExplosionHeat.Spawn(center, bounds, explosionScale);

        if (impactSound != null)
            AudioSource.PlayClipAtPoint(impactSound, center, impactVolume);

        if (explosionPrefab != null)
        {
            var boom = Instantiate(explosionPrefab, center, Quaternion.identity);
            boom.transform.localScale *= explosionScale;
            foreach (var ps in boom.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.gameObject.SetActive(true);
                ps.Clear(true);
                ps.Play(true);
            }
            Destroy(boom, 15f);
        }
        else
        {
            var size = Mathf.Clamp(bounds.size.magnitude / 4f, 0.6f, 4f) * explosionScale;
            LaunchSmoke.CreateExplosionBurst(center, size);
            LaunchSmoke.CreateFire(center, size * 0.4f, size * 0.85f, 3f);
        }
    }

    void ShakeHeadset(Vector3 at)
    {
        if (headsetShake == null) return;
        var distance = Vector3.Distance(headsetShake.transform.position, at);
        if (distance <= 150f) headsetShake.ShakeFor(0.8f, Mathf.Lerp(0.8f, 0.2f, distance / 150f));
    }

    static Bounds WorldBounds(GameObject go)
    {
        var bounds = new Bounds(go.transform.position, Vector3.one);
        var has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }
}
