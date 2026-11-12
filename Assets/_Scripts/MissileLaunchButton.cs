using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Drop on any button. Touching it with the right fingertip (or a controller poke / select)
/// pushes the button in, then moves <see cref="movingObject"/> from Point A to Point B and
/// switches it off at Point B. Use the inspector buttons to capture positions and create points.
/// </summary>
[AddComponentMenu("B1/Button → Move Object A to B")]
public sealed class MissileLaunchButton : MonoBehaviour
{
    [Header("Button press (local position)")]
    [Tooltip("Where the button sits normally. Use 'Capture Rest' in the inspector.")]
    [SerializeField] Vector3 restLocalPosition;
    [Tooltip("Where the button sits when pushed in. Use 'Capture Pressed' in the inspector.")]
    [SerializeField] Vector3 pressedLocalPosition;
    [Min(0.02f)] [SerializeField] float pressSeconds = 0.12f;
    [Min(0f)] [SerializeField] float holdPressedSeconds = 0.15f;
    [Min(0.02f)] [SerializeField] float releaseSeconds = 0.2f;

    [Header("Object that travels A → B")]
    [SerializeField] GameObject movingObject;
    [Tooltip("Start point. Empty = where the object already is.")]
    [SerializeField] Transform pointA;
    [SerializeField] Transform pointB;
    [Min(0.1f)] [SerializeField] float travelSeconds = 3f;
    [Tooltip("Speeds up over the flight like a launch, instead of a constant speed.")]
    [SerializeField] bool accelerate = true;
    [Tooltip("Turn the object to face its travel direction (only if its model points along +Z).")]
    [SerializeField] bool faceTravelDirection;
    [Tooltip("Keep the object hidden until the button is pressed.")]
    [SerializeField] bool hideUntilLaunch;
    [Tooltip("Switch the object off when it reaches Point B.")]
    [SerializeField] bool hideAtPointB = true;
    [SerializeField] bool launchOnlyOnce = true;

    [Header("Smoke")]
    [Tooltip("Smoke trail from the tail of the object while it travels.")]
    [SerializeField] bool smokeTrail = true;
    [Tooltip("Smoke cloud on the ground at Point A when it launches.")]
    [SerializeField] bool groundSmoke = true;
    [Tooltip("Optional: your own trail prefab (follows the tail). Empty = built-in launch smoke.")]
    [SerializeField] GameObject smokePrefab;
    [Tooltip("1 = sized to the object automatically. Raise or lower to taste.")]
    [Min(0.1f)] [SerializeField] float smokeSize = 1f;

    [Header("Events")]
    public UnityEvent onPressed;
    public UnityEvent onArrived;

    BoxCollider touchCollider;
    bool fingerInside;
    bool busy;
    bool launched;

    public GameObject MovingObject { get => movingObject; set => movingObject = value; }
    public Transform PointA { get => pointA; set => pointA = value; }
    public Transform PointB { get => pointB; set => pointB = value; }

    // Press positions measured on the ShootMissile panel buttons (panel-local).
    static readonly Vector3 MissileButtonRest = new Vector3(0.000764f, 0.014691f, 0.00113f);
    static readonly Vector3 MissileButtonPressed = new Vector3(0.00076f, 0.014605f, 0.000978f);
    static readonly Vector3 DroneButtonRest = new Vector3(-0.0008622f, 0.014691f, 0.00113f);
    static readonly Vector3 DroneButtonPressed = new Vector3(-0.0008662f, 0.014605f, 0.000978f);

    /// <summary>
    /// Wires every "ShootMissile.002" button in the loaded scenes: "ShootMissile.002" launches the
    /// nearest SM_missile straight up, "ShootMissile.002 (1)" the nearest Shahed forward.
    /// Buttons that already have a Moving Object are left alone.
    /// </summary>
    /// <param name="makePoint">Creates a path point (editor passes an Undo-aware version).</param>
    /// <param name="beforeChange">Called before a component is changed (editor records Undo).</param>
    /// <returns>Number of buttons wired.</returns>
    public static int AutoWireScene(Func<string, Vector3, Transform, Transform> makePoint = null,
        Action<UnityEngine.Object> beforeChange = null)
    {
        if (makePoint == null) makePoint = DefaultMakePoint;

        var buttons = new List<Transform>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name.StartsWith("ShootMissile.002", StringComparison.Ordinal))
                buttons.Add(t);
        }
        buttons.Sort((a, b) => string.CompareOrdinal(GetPath(a), GetPath(b)));

        var used = new HashSet<GameObject>();
        foreach (var existing in FindObjectsByType<MissileLaunchButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.movingObject != null) used.Add(existing.movingObject);
        }

        Transform pathRoot = null;
        var wired = 0;
        foreach (var buttonTransform in buttons)
        {
            var button = buttonTransform.GetComponent<MissileLaunchButton>();
            if (button != null && button.movingObject != null) continue;

            var panel = buttonTransform.parent != null ? buttonTransform.parent : buttonTransform;
            // Old two-button panels: "(1)" is the drone button. One-button panels (the ShootMissile
            // prefab) launch whichever missile or Shahed stands closest.
            var twoButtonDrone = buttonTransform.name.Contains("(1)");
            var target = twoButtonDrone
                ? Nearest(panel.position, true, used)
                : NearestAny(panel.position, used);
            if (target == null) continue;
            used.Add(target);
            var isDrone = IsLaunchable(target.transform, true);

            if (button == null)
            {
                button = buttonTransform.gameObject.AddComponent<MissileLaunchButton>();
                // Only a freshly added button gets the measured press positions; a button set up
                // in the scene or prefab keeps its own.
                button.restLocalPosition = twoButtonDrone ? DroneButtonRest : MissileButtonRest;
                button.pressedLocalPosition = twoButtonDrone ? DroneButtonPressed : MissileButtonPressed;
            }
            beforeChange?.Invoke(button);

            if (pathRoot == null)
            {
                var root = GameObject.Find("Launch Paths");
                pathRoot = root != null ? root.transform : makePoint("Launch Paths", Vector3.zero, null);
            }

            var a = target.transform.position;
            Vector3 b;
            if (isDrone)
            {
                var away = a - panel.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = target.transform.forward;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
                b = a + away.normalized * 300f + Vector3.up * 40f;
            }
            else
            {
                b = a + Vector3.up * 250f;
            }

            button.movingObject = target;
            button.pointA = makePoint(target.name + " Point A", a, pathRoot);
            button.pointB = makePoint(target.name + " Point B", b, pathRoot);
            button.accelerate = true;
            button.hideAtPointB = true;
            wired++;
        }

        return wired;
    }

    static Transform DefaultMakePoint(string pointName, Vector3 position, Transform parent)
    {
        var go = new GameObject(pointName);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go.transform;
    }

    static GameObject Nearest(Vector3 from, bool drone, HashSet<GameObject> used)
    {
        GameObject best = null;
        var bestDistance = float.MaxValue;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var go = t.gameObject;
            if (used.Contains(go)) continue;
            if (!IsLaunchable(t, drone)) continue;

            var d = (t.position - from).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = go;
        }
        return best;
    }

    static GameObject NearestAny(Vector3 from, HashSet<GameObject> used)
    {
        var missile = Nearest(from, false, used);
        var drone = Nearest(from, true, used);
        if (missile == null) return drone;
        if (drone == null) return missile;
        return (missile.transform.position - from).sqrMagnitude <= (drone.transform.position - from).sqrMagnitude
            ? missile
            : drone;
    }

    static bool IsLaunchable(Transform t, bool drone)
    {
        var n = t.name;
        var match = drone
            ? n.StartsWith("Shahed", StringComparison.Ordinal) || n.StartsWith("SM_Shahed", StringComparison.Ordinal)
            : n.StartsWith("SM_missile", StringComparison.Ordinal);
        if (!match) return false;
        // Top object of the model only, not its mesh children.
        var parent = t.parent;
        return parent == null || !IsLaunchable(parent, drone);
    }

    static string GetPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    void Reset()
    {
        restLocalPosition = transform.localPosition;
        pressedLocalPosition = transform.localPosition;
    }

    void Start()
    {
        if (restLocalPosition == Vector3.zero && pressedLocalPosition == Vector3.zero)
        {
            restLocalPosition = transform.localPosition;
            pressedLocalPosition = transform.localPosition;
        }
        transform.localPosition = restLocalPosition;

        if (hideUntilLaunch && movingObject != null)
            movingObject.SetActive(false);

        EnsureTouchCollider();

        var interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null) interactable = gameObject.AddComponent<XRSimpleInteractable>();
        interactable.interactionLayers = -1;
        interactable.selectMode = InteractableSelectMode.Multiple;
        interactable.selectEntered.AddListener(_ => Press());
    }

    void EnsureTouchCollider()
    {
        touchCollider = GetComponent<BoxCollider>();
        if (touchCollider != null) return;

        touchCollider = gameObject.AddComponent<BoxCollider>();
        var filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            var b = filter.sharedMesh.bounds;
            touchCollider.center = b.center;
            // A little deeper than the cap so a fingertip registers before it sinks through.
            touchCollider.size = new Vector3(b.size.x * 1.15f, b.size.y * 1.15f, Mathf.Max(b.size.z, b.size.x) * 1.6f);
        }
    }

    void Update()
    {
        if (touchCollider == null) return;
        var inside = TabletFingerTip.AnyInside(touchCollider);
        if (inside && !fingerInside) Press();
        fingerInside = inside;
    }

    /// <summary>Press from code, a UnityEvent, or a Timeline signal.</summary>
    public void Press()
    {
        if (busy) return;
        if (launchOnlyOnce && launched) return;
        ButtonClickSound.Play();
        StartCoroutine(PressAndLaunch());
    }

    /// <summary>For testing: put the object back on Point A and launch again, even if it already launched.</summary>
    public void TestLaunch()
    {
        StopAllCoroutines();
        busy = false;
        launched = false;
        transform.localPosition = restLocalPosition;
        if (movingObject != null)
        {
            movingObject.SetActive(true);
            if (pointA != null) movingObject.transform.position = pointA.position;
        }
        Press();
    }

    IEnumerator PressAndLaunch()
    {
        busy = true;
        yield return MoveButton(restLocalPosition, pressedLocalPosition, pressSeconds);

        launched = true;
        onPressed?.Invoke();
        if (movingObject != null && pointB != null)
            StartCoroutine(Travel());
        else
            Debug.LogWarning(name + ": assign Moving Object and Point B to launch.", this);

        if (holdPressedSeconds > 0f)
            yield return new WaitForSeconds(holdPressedSeconds);
        yield return MoveButton(pressedLocalPosition, restLocalPosition, releaseSeconds);
        busy = false;
    }

    IEnumerator MoveButton(Vector3 from, Vector3 to, float seconds)
    {
        var elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }
        transform.localPosition = to;
    }

    IEnumerator Travel()
    {
        var target = movingObject.transform;
        movingObject.SetActive(true);

        var start = pointA != null ? pointA.position : target.position;
        var end = pointB.position;
        target.position = start;

        var direction = end - start;
        if (faceTravelDirection && direction.sqrMagnitude > 0.0001f)
            target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        var travelDir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

        var scale = SmokeScale(target);
        // The object only translates during the flight, so the tail stays at a fixed offset.
        var tailOffset = TailPoint(target, travelDir) - target.position;
        var upHint = Mathf.Abs(Vector3.Dot(travelDir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        var trailRotation = Quaternion.LookRotation(-travelDir, upHint);
        if (groundSmoke)
            LaunchSmoke.CreateGroundBurst(target.position + tailOffset, scale);

        Transform emitter = null;
        ParticleSystem trail = null;
        GameObject customTrail = null;
        if (smokeTrail)
        {
            if (smokePrefab != null)
            {
                customTrail = Instantiate(smokePrefab, target.position + tailOffset, trailRotation);
                emitter = customTrail.transform;
            }
            else
            {
                trail = LaunchSmoke.CreateTrail(scale);
                emitter = trail.transform;
                emitter.SetPositionAndRotation(target.position + tailOffset, trailRotation);
            }
        }

        var elapsed = 0f;
        while (elapsed < travelSeconds)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / travelSeconds);
            target.position = Vector3.Lerp(start, end, accelerate ? t * t : t);
            if (emitter != null) emitter.SetPositionAndRotation(target.position + tailOffset, trailRotation);
            yield return null;
        }

        target.position = end;
        if (emitter != null) emitter.SetPositionAndRotation(target.position + tailOffset, trailRotation);
        onArrived?.Invoke();

        // The trail stops emitting at B; smoke already in the air fades out on its own.
        if (trail != null) LaunchSmoke.StopAndRelease(trail);
        if (customTrail != null) ReleaseCustomTrail(customTrail);

        if (hideAtPointB)
            movingObject.SetActive(false);
    }

    float SmokeScale(Transform target)
    {
        var bounds = ObjectBounds(target);
        var auto = bounds.size.sqrMagnitude > 0f ? Mathf.Clamp(bounds.size.magnitude / 6f, 0.4f, 4f) : 1f;
        return auto * smokeSize;
    }

    /// <summary>Back end of the object along its travel direction (the bottom for a vertical launch).</summary>
    static Vector3 TailPoint(Transform target, Vector3 travelDir)
    {
        var bounds = ObjectBounds(target);
        if (bounds.size.sqrMagnitude <= 0f) return target.position;
        var e = bounds.extents;
        var along = Mathf.Abs(travelDir.x) * e.x + Mathf.Abs(travelDir.y) * e.y + Mathf.Abs(travelDir.z) * e.z;
        return bounds.center - travelDir * along;
    }

    static Bounds ObjectBounds(Transform target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        var has = false;
        var bounds = new Bounds(target.position, Vector3.zero);
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    static void ReleaseCustomTrail(GameObject trail)
    {
        var longest = 0f;
        foreach (var ps in trail.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var life = ps.main.startLifetime;
            longest = Mathf.Max(longest, Mathf.Max(life.constant, life.constantMax));
        }
        Destroy(trail, longest + 1f);
    }

    public void CaptureRest() => restLocalPosition = transform.localPosition;
    public void CapturePressed() => pressedLocalPosition = transform.localPosition;
    public void PreviewRest() => transform.localPosition = restLocalPosition;
    public void PreviewPressed() => transform.localPosition = pressedLocalPosition;

    void OnDrawGizmosSelected()
    {
        if (pointB == null) return;
        var start = pointA != null ? pointA.position : movingObject != null ? movingObject.transform.position : transform.position;
        var end = pointB.position;
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawLine(start, end);
    }
}
