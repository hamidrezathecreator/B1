using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Drop spot that pulls a grabbed object in like a magnet. Release it within <see cref="snapRadius"/>,
/// or push it right into the spot while holding it, and it slides onto this transform's position
/// and rotation and stays there.
/// </summary>
[AddComponentMenu("B1/Magnet Drop Zone")]
public sealed class MagnetDropZone : MonoBehaviour
{
    [Tooltip("Only this object snaps. Empty = any grabbable object.")]
    [SerializeField] XRGrabInteractable acceptedObject;
    [Tooltip("Release closer than this (meters, center to center) to snap.")]
    [Min(0.05f)] [SerializeField] float snapRadius = 0.8f;
    [Tooltip("Closer than this it snaps even while still held.")]
    [Min(0f)] [SerializeField] float pullInRadius = 0.25f;
    [Min(0.02f)] [SerializeField] float snapSeconds = 0.25f;
    [Tooltip("Optional placeholder shown at the spot, hidden once something snaps in.")]
    [SerializeField] GameObject hideWhenFilled;
    [Tooltip("The snapped object can no longer be picked up.")]
    [SerializeField] bool lockAfterSnap = true;

    public UnityEvent onSnapped;
    public event Action<XRGrabInteractable> Snapped;

    static readonly List<MagnetDropZone> Zones = new List<MagnetDropZone>();
    static readonly HashSet<XRGrabInteractable> Claimed = new HashSet<XRGrabInteractable>();

    readonly List<XRGrabInteractable> watched = new List<XRGrabInteractable>();
    readonly List<XRGrabInteractable> acceptedList = new List<XRGrabInteractable>();
    Vector3 localCenter;
    bool filled;

    public bool IsFilled => filled;
    public float SnapRadius => snapRadius;
    public XRGrabInteractable AcceptedObject => acceptedObject;

    /// <summary>True while (or after) some zone has taken this object.</summary>
    public static bool IsClaimed(XRGrabInteractable grab) => grab != null && Claimed.Contains(grab);

    public void Configure(XRGrabInteractable accepted, GameObject placeholder, float radius)
    {
        acceptedObject = accepted;
        acceptedList.Clear();
        hideWhenFilled = placeholder;
        snapRadius = radius;
        CacheCenter();
        if (isActiveAndEnabled) Rehook();
    }

    /// <summary>Accept any of these objects; each zone takes one, the nearest free zone wins.</summary>
    public void Configure(IEnumerable<XRGrabInteractable> accepted, GameObject placeholder, float radius)
    {
        acceptedObject = null;
        acceptedList.Clear();
        foreach (var grab in accepted)
            if (grab != null) acceptedList.Add(grab);
        hideWhenFilled = placeholder;
        snapRadius = radius;
        CacheCenter();
        if (isActiveAndEnabled) Rehook();
    }

    public void Accept(XRGrabInteractable accepted)
    {
        acceptedObject = accepted;
        acceptedList.Clear();
        if (isActiveAndEnabled) Rehook();
    }

    void Awake() => CacheCenter();

    void OnEnable()
    {
        if (!Zones.Contains(this)) Zones.Add(this);
        Rehook();
    }

    void OnDisable()
    {
        Zones.Remove(this);
        Unhook();
    }

    /// <summary>Spots can sit close together; only the nearest free one that accepts the object takes it.</summary>
    bool IsNearestFreeZone(XRGrabInteractable grab, float myDistance)
    {
        foreach (var zone in Zones)
        {
            if (zone == null || zone == this || zone.filled || !zone.Accepts(grab)) continue;
            if (zone.DistanceTo(grab) < myDistance) return false;
        }
        return true;
    }

    bool Accepts(XRGrabInteractable grab) => watched.Contains(grab);

    /// <summary>Center of the placeholder's mesh, so pivots placed off-center do not matter.</summary>
    void CacheCenter()
    {
        localCenter = Vector3.zero;
        if (hideWhenFilled == null) return;
        if (TryBounds(hideWhenFilled.transform, out var b))
            localCenter = transform.InverseTransformPoint(b.center);
    }

    void Rehook()
    {
        Unhook();
        if (acceptedObject != null)
            watched.Add(acceptedObject);
        else if (acceptedList.Count > 0)
            watched.AddRange(acceptedList);
        else
            watched.AddRange(FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));

        foreach (var grab in watched)
            if (grab != null) grab.selectExited.AddListener(OnReleased);
    }

    void Unhook()
    {
        foreach (var grab in watched)
            if (grab != null) grab.selectExited.RemoveListener(OnReleased);
        watched.Clear();
    }

    void Update()
    {
        if (filled || pullInRadius <= 0f) return;
        foreach (var grab in watched)
        {
            if (grab == null || !grab.isSelected || Claimed.Contains(grab)) continue;
            var distance = DistanceTo(grab);
            if (distance > pullInRadius || !IsNearestFreeZone(grab, distance)) continue;

            // Pushed right into the spot: let go of it for the player and snap.
            var manager = grab.interactionManager;
            if (manager != null)
            {
                for (var i = grab.interactorsSelecting.Count - 1; i >= 0; i--)
                    manager.SelectExit(grab.interactorsSelecting[i], grab);
            }
            if (!filled) StartCoroutine(SnapIn(grab));
            return;
        }
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (filled) return;
        var grab = args.interactableObject as XRGrabInteractable;
        if (grab == null || grab.isSelected || Claimed.Contains(grab)) return;
        var distance = DistanceTo(grab);
        if (distance > snapRadius || !IsNearestFreeZone(grab, distance)) return;
        StartCoroutine(SnapIn(grab));
    }

    float DistanceTo(XRGrabInteractable grab)
    {
        var from = TryBounds(grab.transform, out var b) ? b.center : grab.transform.position;
        return Vector3.Distance(from, transform.TransformPoint(localCenter));
    }

    IEnumerator SnapIn(XRGrabInteractable grab)
    {
        filled = true;
        Claimed.Add(grab);
        if (lockAfterSnap) grab.enabled = false;
        if (hideWhenFilled != null) hideWhenFilled.SetActive(false);

        var body = grab.GetComponent<Rigidbody>();
        var from = grab.transform.position;
        var fromRotation = grab.transform.rotation;
        var elapsed = 0f;
        while (elapsed < snapSeconds)
        {
            elapsed += Time.deltaTime;
            // Keep physics off every frame: the grab's own release can switch it back on and throw it.
            HoldStill(body);
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / snapSeconds));
            grab.transform.SetPositionAndRotation(
                Vector3.Lerp(from, transform.position, t),
                Quaternion.Slerp(fromRotation, transform.rotation, t));
            yield return null;
        }

        grab.transform.SetPositionAndRotation(transform.position, transform.rotation);
        HoldStill(body);

        onSnapped?.Invoke();
        Snapped?.Invoke(grab);
    }

    static void HoldStill(Rigidbody body)
    {
        if (body == null) return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.useGravity = false;
    }

    static bool TryBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var has = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.TransformPoint(localCenter), snapRadius);
    }
}
