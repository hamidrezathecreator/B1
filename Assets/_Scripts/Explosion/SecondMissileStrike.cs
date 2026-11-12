using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// After the first missile, a second missile hits a chosen object a few seconds after the first impact
/// and throws that object's children (the scene's "env intractive" group in Lvl 1).
/// </summary>
public sealed class SecondMissileStrike : MonoBehaviour
{
    [Tooltip("Object whose children are thrown. Matched ignoring case, spaces, '_' and '-'.")]
    [SerializeField] string[] targetNames = { "env intractive", "env interactive" };
    [Tooltip("Seconds between the first missile's impact and the second missile's impact.")]
    [Min(0f)] [SerializeField] float secondsAfterFirstImpact = 4f;
    [Tooltip("Launcher used for the second missile. First found name wins; any launcher if none match.")]
    [SerializeField] string[] launcherNames = { "Missiles_2", "Missiles_1" };

    [Header("Throw")]
    [Min(0f)] [SerializeField] float throwRadius = 3f;
    [Min(0f)] [SerializeField] float landingRadius = 8f;
    [Min(0f)] [SerializeField] float throwHeight = 4f;

    Transform target;
    ExplosionScatter scatter;
    bool triggered;

    public static SecondMissileStrike Create(Transform parent)
    {
        var go = new GameObject("Second Missile Strike");
        go.transform.SetParent(parent, false);
        return go.AddComponent<SecondMissileStrike>();
    }

    void OnEnable()
    {
        MissileLauncherSystem.FireRequested += OnFireRequested;
        MissileLauncherSystem.MissileImpacted += OnMissileImpacted;
    }

    void OnDisable()
    {
        MissileLauncherSystem.FireRequested -= OnFireRequested;
        MissileLauncherSystem.MissileImpacted -= OnMissileImpacted;
    }

    void Start() => EnsureScatter();

    void OnFireRequested(MissileLauncherSystem launcher)
    {
        // The first missile lands one flight time after it is fired.
        if (!triggered) Trigger(launcher.FlightDuration);
    }

    void OnMissileImpacted(MissileLauncherSystem launcher, Vector3 point)
    {
        if (!triggered) Trigger(0f);
    }

    void Trigger(float untilFirstImpact)
    {
        if (!EnsureScatter()) return;
        triggered = true;
        Debug.Log("SecondMissileStrike: first missile seen; '" + target.name + "' is hit " +
                  (untilFirstImpact + secondsAfterFirstImpact).ToString("0.0") + " s from now.", this);
        StartCoroutine(Strike(untilFirstImpact + secondsAfterFirstImpact));
    }

    IEnumerator Strike(float untilImpact)
    {
        var launcher = FindLauncher();
        var flight = launcher != null ? launcher.FlightDuration : 0f;
        var wait = untilImpact - flight;
        if (wait > 0f) yield return new WaitForSeconds(wait);

        var landed = false;
        if (launcher != null)
            launcher.FireAt(BlastPoint(), _ => landed = true);

        // The launcher can be reset or switched off mid-flight; throw the objects on time anyway.
        var timeout = flight + 0.5f;
        while (!landed && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        Throw();
    }

    void Throw()
    {
        scatter.ExplodeOnce();
        Debug.Log("SecondMissileStrike: threw the children of '" + target.name + "'.", this);
    }

    bool EnsureScatter()
    {
        if (scatter != null) return true;
        target = FindTarget();
        if (target == null)
        {
            Debug.LogWarning("SecondMissileStrike: no active object named " + string.Join(" / ", targetNames) + ".", this);
            return false;
        }

        var pieces = new List<Transform>();
        foreach (Transform child in target)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            pieces.Add(child);
            foreach (var r in child.GetComponentsInChildren<Renderer>())
            {
                if (!r.isPartOfStaticBatch) continue;
                Debug.LogWarning("SecondMissileStrike: '" + child.name + "' is marked Static (batched), so it " +
                                 "cannot move. Untick Static on it in the Inspector.", child);
                break;
            }
        }

        scatter = target.GetComponent<ExplosionScatter>();
        if (scatter == null) scatter = target.gameObject.AddComponent<ExplosionScatter>();
        var ground = LayerMask.GetMask("Ground");
        scatter.Configure(pieces.ToArray(), target, throwRadius, landingRadius, throwHeight,
            ground != 0 ? (LayerMask)ground : (LayerMask)~0);
        Debug.Log("SecondMissileStrike: '" + target.name + "' ready with " + pieces.Count + " piece(s).", this);
        return true;
    }

    Vector3 BlastPoint()
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return target.position;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    Transform FindTarget()
    {
        var wanted = new List<string>();
        foreach (var name in targetNames)
            if (!string.IsNullOrWhiteSpace(name)) wanted.Add(Normalize(name));
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (wanted.Contains(Normalize(t.name))) return t;
        return null;
    }

    MissileLauncherSystem FindLauncher()
    {
        var all = FindObjectsByType<MissileLauncherSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var name in launcherNames)
            foreach (var launcher in all)
                if (launcher.name == name) return launcher;
        return all.Length > 0 ? all[0] : null;
    }

    static string Normalize(string name) =>
        name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
}
