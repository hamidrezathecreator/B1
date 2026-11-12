using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put on a fighter jet model. It flies a smooth line through the Path Points (looping by default),
/// banking into turns, and can fire a missile at a point (used by Explode And Swap).
/// Right-click the component → "Create Path Points" to start with a circle of points to drag around.
/// </summary>
[AddComponentMenu("B1/Fighter Jet Flight")]
public sealed class FighterJetFlight : MonoBehaviour
{
    [Header("Flight path")]
    [Tooltip("Points the jet flies through, in order. The line between them is smoothed.")]
    [SerializeField] Transform[] pathPoints = new Transform[0];
    [Tooltip("Fly back to the first point and keep circling.")]
    [SerializeField] bool loop = true;
    [Min(0.1f)] [SerializeField] float speed = 60f;
    [Tooltip("Start flying as soon as the level starts.")]
    [SerializeField] bool flyOnStart = true;

    [Header("Model")]
    [Tooltip("Extra rotation so the model's nose points along the flight. Most models: 0,0,0. Nose along +X: 0,-90,0.")]
    [SerializeField] Vector3 modelRotationOffset = Vector3.zero;
    [Tooltip("How far the jet rolls into a turn, degrees.")]
    [Range(0f, 80f)] [SerializeField] float maxBank = 45f;
    [Min(0f)] [SerializeField] float bankStrength = 1.5f;
    [Min(0.1f)] [SerializeField] float bankSmoothing = 3f;

    [Header("Missile")]
    [Tooltip("Missile model. Its nose must point along +Z (or set Missile Rotation Offset). Empty = a simple built-in missile.")]
    [SerializeField] GameObject missilePrefab;
    [SerializeField] Vector3 missileRotationOffset = Vector3.zero;
    [Min(0.01f)] [SerializeField] float missileScale = 1f;
    [Tooltip("Where the missile leaves the jet, in meters relative to the jet (x right, y up, z forward).")]
    [SerializeField] Vector3 missileLaunchOffset = new Vector3(0f, -1f, 2f);
    [Min(0.1f)] [SerializeField] float missileFlightSeconds = 2.5f;
    [Tooltip("How much the missile first keeps the jet's heading before curving onto the target.")]
    [Range(0f, 1f)] [SerializeField] float missileCurve = 0.35f;
    [SerializeField] bool missileSmokeTrail = true;
    [Min(0.1f)] [SerializeField] float missileSmokeSize = 0.8f;
    [SerializeField] AudioClip missileLaunchSound;
    [Range(0f, 1f)] [SerializeField] float missileLaunchVolume = 1f;

    const int SamplesPerSegment = 24;

    readonly List<Vector3> points = new List<Vector3>();
    readonly List<Vector3> samples = new List<Vector3>();
    readonly List<float> lengths = new List<float>();
    float distance;
    float bank;
    bool flying;

    public bool IsFlying => flying;
    public float MissileFlightSeconds => missileFlightSeconds;
    public GameObject MissilePrefab => missilePrefab;

    void Start()
    {
        if (flyOnStart) StartFlying();
    }

    /// <summary>Starts (or restarts) the flight from the first path point.</summary>
    [ContextMenu("Start Flying")]
    public void StartFlying()
    {
        distance = 0f;
        bank = 0f;
        flying = BuildSamples();
        if (flying) Place(0f, 0f);
        else Debug.LogWarning(name + ": Fighter Jet Flight needs at least 2 path points.", this);
    }

    public void StopFlying() => flying = false;

    void Update()
    {
        if (!flying) return;
        if (!BuildSamples()) return;

        var total = lengths[lengths.Count - 1];
        distance += speed * Time.deltaTime;
        if (loop) distance = Mathf.Repeat(distance, total);
        else if (distance >= total)
        {
            distance = total;
            flying = false;
        }
        Place(distance, Time.deltaTime);
    }

    void Place(float at, float deltaTime)
    {
        var position = PointAt(at);
        var ahead = PointAt(Advance(at, Mathf.Max(1f, speed * 0.25f)));
        var behind = PointAt(Advance(at, -Mathf.Max(1f, speed * 0.25f)));
        var forward = ahead - behind;
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        forward.Normalize();

        // Turn rate from how the heading changes around the jet: roll into the turn like a real jet.
        var inDir = Flat(position - behind);
        var outDir = Flat(ahead - position);
        var turn = inDir.sqrMagnitude > 0.0001f && outDir.sqrMagnitude > 0.0001f
            ? Vector3.SignedAngle(inDir, outDir, Vector3.up)
            : 0f;
        var targetBank = Mathf.Clamp(-turn * bankStrength, -maxBank, maxBank);
        bank = deltaTime > 0f ? Mathf.Lerp(bank, targetBank, 1f - Mathf.Exp(-bankSmoothing * deltaTime)) : targetBank;

        var up = Quaternion.AngleAxis(bank, forward) * Vector3.up;
        transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(forward, up) * Quaternion.Euler(modelRotationOffset));
    }

    float Advance(float at, float by)
    {
        var total = lengths.Count > 0 ? lengths[lengths.Count - 1] : 0f;
        return loop ? Mathf.Repeat(at + by, total) : Mathf.Clamp(at + by, 0f, total);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>Flight direction, without the model rotation offset.</summary>
    Vector3 FlightForward => transform.rotation * Quaternion.Inverse(Quaternion.Euler(modelRotationOffset)) * Vector3.forward;

    Quaternion FlightRotation => transform.rotation * Quaternion.Inverse(Quaternion.Euler(modelRotationOffset));

    public void ConfigureMissileLaunchSound(AudioClip launchSound)
    {
        if (launchSound != null) missileLaunchSound = launchSound;
    }

    /// <summary>Fires one missile at a world point; <paramref name="hit"/> runs when it arrives.</summary>
    public void FireMissileAt(Vector3 target, Action hit = null)
    {
        StartCoroutine(MissileFlight(() => target, hit));
    }

    /// <summary>Fires one missile that keeps aiming at a moving object until it arrives.</summary>
    public void FireMissileAt(Transform follow, Action hit = null)
    {
        if (follow == null)
        {
            hit?.Invoke();
            return;
        }
        StartCoroutine(MissileFlight(() => follow.position, hit));
    }

    IEnumerator MissileFlight(Func<Vector3> aim, Action hit)
    {
        var target = aim != null ? aim() : transform.position + FlightForward * 100f;
        var start = transform.position + FlightRotation * missileLaunchOffset;
        var heading = FlightForward;
        var control = start + heading * (Vector3.Distance(start, target) * missileCurve);

        if (missileLaunchSound != null)
            AudioSource.PlayClipAtPoint(missileLaunchSound, start, missileLaunchVolume);

        var offset = Quaternion.Euler(missileRotationOffset);
        var missile = missilePrefab != null
            ? Instantiate(missilePrefab, start, Quaternion.LookRotation(heading) * offset)
            : BuildDefaultMissile(start, heading);
        missile.transform.localScale *= missileScale;
        missile.SetActive(true);

        ParticleSystem trail = null;
        if (missileSmokeTrail)
        {
            trail = LaunchSmoke.CreateTrail(missileSmokeSize);
            trail.transform.SetParent(missile.transform, false);
            trail.transform.localPosition = Vector3.back * 0.4f;
            trail.transform.localRotation = Quaternion.identity;
        }

        var elapsed = 0f;
        var previous = start;
        var flight = Mathf.Clamp(Vector3.Distance(start, target) / 120f, missileFlightSeconds, 8f);
        while (elapsed < flight && missile != null)
        {
            elapsed += Time.deltaTime;
            if (aim != null) target = aim();
            var t = Mathf.Clamp01(elapsed / flight);
            // Speeds up a little after leaving the rail.
            t = Mathf.Lerp(t, t * t, 0.4f);
            var a = Vector3.Lerp(start, control, t);
            var b = Vector3.Lerp(control, target, t);
            var position = Vector3.Lerp(a, b, t);
            var direction = position - previous;
            if (direction.sqrMagnitude > 0.000001f)
                missile.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.normalized) * offset);
            else
                missile.transform.position = position;
            previous = position;
            yield return null;
        }

        // Snap onto the hit point so the blast lines up with the impact.
        if (missile != null)
            missile.transform.position = target;

        if (trail != null)
        {
            trail.transform.SetParent(null, true);
            LaunchSmoke.StopAndRelease(trail);
        }
        if (missile != null) Destroy(missile);
        hit?.Invoke();
    }

    static GameObject BuildDefaultMissile(Vector3 position, Vector3 heading)
    {
        var root = new GameObject("Jet Missile");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.25f, 1.2f, 0.25f);

        var renderer = body.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var material = new Material(shader) { color = new Color(0.75f, 0.75f, 0.72f) };
            renderer.sharedMaterial = material;
        }
        return root;
    }

    bool BuildSamples()
    {
        samples.Clear();
        lengths.Clear();

        points.Clear();
        if (pathPoints != null)
            foreach (var p in pathPoints)
                if (p != null) points.Add(p.position);
        if (points.Count < 2) return false;

        var segments = loop ? points.Count : points.Count - 1;
        for (var s = 0; s < segments; s++)
        {
            var p0 = points[Index(s - 1, points.Count)];
            var p1 = points[Index(s, points.Count)];
            var p2 = points[Index(s + 1, points.Count)];
            var p3 = points[Index(s + 2, points.Count)];
            for (var i = 0; i < SamplesPerSegment; i++)
                samples.Add(CatmullRom(p0, p1, p2, p3, i / (float)SamplesPerSegment));
        }
        samples.Add(loop ? points[0] : points[points.Count - 1]);

        var total = 0f;
        lengths.Add(0f);
        for (var i = 1; i < samples.Count; i++)
        {
            total += Vector3.Distance(samples[i - 1], samples[i]);
            lengths.Add(total);
        }
        return total > 0.01f;
    }

    int Index(int i, int count) => loop ? (i % count + count) % count : Mathf.Clamp(i, 0, count - 1);

    Vector3 PointAt(float at)
    {
        if (samples.Count == 0) return transform.position;
        if (at <= 0f) return samples[0];
        var total = lengths[lengths.Count - 1];
        if (at >= total) return samples[samples.Count - 1];

        int low = 0, high = lengths.Count - 1;
        while (high - low > 1)
        {
            var mid = (low + high) / 2;
            if (lengths[mid] < at) low = mid;
            else high = mid;
        }
        var span = lengths[high] - lengths[low];
        var t = span > 0.0001f ? (at - lengths[low]) / span : 0f;
        return Vector3.Lerp(samples[low], samples[high], t);
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

#if UNITY_EDITOR
    /// <summary>Creates six points in a wide circle around the jet, in their own object (not under the jet).</summary>
    [ContextMenu("Create Path Points")]
    void CreatePathPoints()
    {
        var root = new GameObject(name + " Flight Path");
        UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Create Path Points");
        root.transform.position = transform.position;

        const int count = 6;
        const float radius = 250f;
        var points = new Transform[count];
        for (var i = 0; i < count; i++)
        {
            var angle = i * Mathf.PI * 2f / count;
            var point = new GameObject("Point " + (i + 1)).transform;
            point.SetParent(root.transform, false);
            point.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            points[i] = point;
        }

        UnityEditor.Undo.RecordObject(this, "Create Path Points");
        pathPoints = points;
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.Selection.activeGameObject = root;
    }
#endif

    void OnDrawGizmos()
    {
        if (!BuildSamples()) return;
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        for (var i = 1; i < samples.Count; i++)
            Gizmos.DrawLine(samples[i - 1], samples[i]);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 1f);
        foreach (var p in pathPoints)
            if (p != null) Gizmos.DrawSphere(p.position, Mathf.Max(1f, speed * 0.03f));
    }
}
