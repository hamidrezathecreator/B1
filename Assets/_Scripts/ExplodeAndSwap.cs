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
        [Tooltip("Or the intact model's name in the scene, if not assigned above.")]
        public string intactName;
        [Tooltip("Destroyed model; hidden at the start, switched on when it blows up.")]
        public GameObject destroyed;
        [Tooltip("Or the destroyed model's name in the scene, if not assigned above.")]
        public string destroyedName;
        [Tooltip("Seconds to wait before this shot: first target from the button press, later targets after the previous hit.")]
        [Min(0f)] public float delay;
    }

    [Header("Trigger")]
    [Tooltip("Launch button that starts it. Empty = the launch button under Trigger Object Name.")]
    [SerializeField] MissileLaunchButton triggerButton;
    [Tooltip("After the first ShootMissile.002 shot, the player presses this panel.")]
    [SerializeField] string triggerObjectName = "ShootMissile (1)";
    [Tooltip("Start when the launched object reaches Point B instead of when the button is pressed.")]
    [SerializeField] bool waitForArrival;

    [Header("Targets (in order)")]
    [SerializeField] Target[] targets =
    {
        new Target { intactName = "SM_AirDefense", destroyedName = "SM_AirDefenseCrash", delay = 3f },
        // Delay is after the previous hit finishes (not from the button press).
        new Target { intactName = "SM_Luncher01 (3)", destroyedName = "SM_LuncherCrash", delay = 3f },
    };

    [Header("Fighter jet")]
    [Tooltip("SM_Shahed149's Fighter Jet Flight. Empty = find SM_Shahed149, else any jet in the scene.")]
    [SerializeField] FighterJetFlight jet;
    [SerializeField] string jetObjectName = "SM_Shahed149";

    [Header("Effects")]
    [Tooltip("Explosion prefab, e.g. DAVFX Explosion 13. Empty = built-in fiery burst.")]
    [SerializeField] GameObject explosionPrefab;
    [Min(0.1f)] [SerializeField] float explosionScale = 1f;
    [SerializeField] bool burnAfterwards = true;
    [Tooltip("How long it keeps burning, seconds. 0 = until the level ends.")]
    [Min(0f)] [SerializeField] float burnSeconds = 20f;
    [Min(0.1f)] [SerializeField] float fireSize = 1f;
    [SerializeField] bool shakeHeadset = true;

    bool played;

    IEnumerator Start()
    {
        foreach (var t in targets)
        {
            if (t == null) continue;
            if (t.intact == null && !string.IsNullOrWhiteSpace(t.intactName))
                t.intact = GameObject.Find(t.intactName);
            if (t.destroyed == null && !string.IsNullOrWhiteSpace(t.destroyedName))
                t.destroyed = GameObject.Find(t.destroyedName);
            if (t.destroyed != null) t.destroyed.SetActive(false);
        }

        // Launch buttons may be added by the level setup in its own Start.
        yield return null;
        if (triggerButton == null && !string.IsNullOrWhiteSpace(triggerObjectName))
        {
            var panel = GameObject.Find(triggerObjectName);
            if (panel != null)
            {
                triggerButton = panel.GetComponentInChildren<MissileLaunchButton>(true);
                // ShootMissile (1) only starts this strike — do not AutoWire it to launch the Shahed away.
                if (triggerButton == null)
                    triggerButton = panel.gameObject.AddComponent<MissileLaunchButton>();
            }
        }

        if (jet == null && !string.IsNullOrWhiteSpace(jetObjectName))
        {
            var jetGo = GameObject.Find(jetObjectName);
            if (jetGo != null) jet = jetGo.GetComponentInChildren<FighterJetFlight>(true);
        }

        if (triggerButton == null)
        {
            Debug.LogWarning("ExplodeAndSwap: no launch button found; call Play() from an event instead.", this);
            yield break;
        }
        if (waitForArrival) triggerButton.onArrived.AddListener(Play);
        else triggerButton.onPressed.AddListener(Play);
    }

    /// <summary>Starts the sequence; also usable from a UnityEvent or Timeline signal.</summary>
    public void Play()
    {
        if (played) return;
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

        if (jet == null)
        {
            if (!string.IsNullOrWhiteSpace(jetObjectName))
            {
                var jetGo = GameObject.Find(jetObjectName);
                if (jetGo != null) jet = jetGo.GetComponentInChildren<FighterJetFlight>(true);
            }
            if (jet == null) jet = FindAnyObjectByType<FighterJetFlight>();
        }

        if (jet == null || !jet.isActiveAndEnabled)
        {
            if (!string.IsNullOrWhiteSpace(jetObjectName))
            {
                var jetGo = GameObject.Find(jetObjectName);
                if (jetGo != null)
                {
                    jetGo.SetActive(true);
                    jet = jetGo.GetComponentInChildren<FighterJetFlight>(true);
                }
            }
            if (jet == null) jet = FindAnyObjectByType<FighterJetFlight>();
            if (jet != null && !jet.enabled) jet.enabled = true;
            if (jet != null && !jet.IsFlying) jet.StartFlying();
        }

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

    static void ShakeHeadset(Vector3 at)
    {
        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) return;
        var distance = Vector3.Distance(origin.transform.position, at);
        if (distance > 150f) return;
        var shake = origin.GetComponent<XRExplosionShake>();
        if (shake == null) shake = origin.gameObject.AddComponent<XRExplosionShake>();
        shake.ShakeFor(0.8f, Mathf.Lerp(0.8f, 0.2f, distance / 150f));
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
