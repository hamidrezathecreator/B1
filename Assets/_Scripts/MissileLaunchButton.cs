using System.Collections;
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

    [Header("Events")]
    public UnityEvent onPressed;
    public UnityEvent onArrived;

    BoxCollider touchCollider;
    bool fingerInside;
    bool busy;
    bool launched;

    public GameObject MovingObject => movingObject;
    public Transform PointA { get => pointA; set => pointA = value; }
    public Transform PointB { get => pointB; set => pointB = value; }

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
        StartCoroutine(PressAndLaunch());
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

        var elapsed = 0f;
        while (elapsed < travelSeconds)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / travelSeconds);
            target.position = Vector3.Lerp(start, end, accelerate ? t * t : t);
            yield return null;
        }

        target.position = end;
        onArrived?.Invoke();
        if (hideAtPointB)
            movingObject.SetActive(false);
    }

    public void CaptureRest() => restLocalPosition = transform.localPosition;
    public void CapturePressed() => pressedLocalPosition = transform.localPosition;
    public void PreviewRest() => transform.localPosition = restLocalPosition;
    public void PreviewPressed() => transform.localPosition = pressedLocalPosition;

    void OnDrawGizmos()
    {
        if (pointB == null) return;
        var start = pointA != null ? pointA.position : movingObject != null ? movingObject.transform.position : transform.position;
        var end = pointB.position;
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawLine(start, end);
        var size = Mathf.Max(0.05f, Vector3.Distance(start, end) * 0.02f);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(start, size);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(end, size);
    }
}
