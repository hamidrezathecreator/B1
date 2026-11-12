using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lvl 1: throws the children of the "env intractive" group the moment the second explosion
/// (Explosion_Two, switched on by the Timeline) appears. Every explosion object also gets the
/// explosion sound through <see cref="ExplosionCue"/>.
/// </summary>
public sealed class SecondMissileStrike : MonoBehaviour
{
    [Tooltip("Object whose children are thrown. Matched ignoring case, spaces, '_' and '-'.")]
    [SerializeField] string[] targetNames = { "env intractive", "env interactive" };
    [Tooltip("Explosion that throws the objects when the Timeline switches it on.")]
    [SerializeField] string[] triggerExplosionNames = { "Explosion_Two" };
    [Tooltip("Explosions that play the explosion sound when they appear.")]
    [SerializeField] string[] soundExplosionNames = { "Explosion_One", "Explosion_Two" };

    [Header("Throw")]
    [Min(0f)] [SerializeField] float throwRadius = 3f;
    [Min(0f)] [SerializeField] float landingRadius = 8f;
    [Min(0f)] [SerializeField] float throwHeight = 4f;

    Transform target;
    ExplosionScatter scatter;

    public static SecondMissileStrike Create(Transform parent)
    {
        var go = new GameObject("Second Missile Strike");
        go.transform.SetParent(parent, false);
        return go.AddComponent<SecondMissileStrike>();
    }

    void Start()
    {
        EnsureScatter();

        foreach (var name in soundExplosionNames)
            ExplosionCue.AddTo(FindIncludingInactive(name));

        var found = false;
        foreach (var name in triggerExplosionNames)
        {
            var cue = ExplosionCue.AddTo(FindIncludingInactive(name));
            if (cue == null) continue;
            cue.Shown -= OnExplosionShown;
            cue.Shown += OnExplosionShown;
            found = true;
            // Already on screen (Timeline started earlier): throw right away.
            if (cue.isActiveAndEnabled) OnExplosionShown(cue);
        }
        if (!found)
            Debug.LogWarning("SecondMissileStrike: no explosion named " + string.Join(" / ", triggerExplosionNames) + ".", this);
    }

    void OnExplosionShown(ExplosionCue cue)
    {
        if (!EnsureScatter()) return;
        scatter.ExplodeOnce();
        Debug.Log("SecondMissileStrike: '" + cue.name + "' appeared; threw the children of '" + target.name + "'.", this);
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
        return true;
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

    static GameObject FindIncludingInactive(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var wanted = Normalize(name);
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.gameObject.scene.IsValid() && Normalize(t.name) == wanted) return t.gameObject;
        return null;
    }

    static string Normalize(string name) =>
        name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
}
