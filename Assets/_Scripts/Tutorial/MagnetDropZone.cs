using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Drop spot that pulls a grabbed object in like a magnet: release it within
/// <see cref="snapRadius"/> and it slides onto this transform's position and rotation and stays there.
/// </summary>
[AddComponentMenu("B1/Magnet Drop Zone")]
public sealed class MagnetDropZone : MonoBehaviour
{
    [Tooltip("Only this object snaps. Empty = any grabbable object.")]
    [SerializeField] XRGrabInteractable acceptedObject;
    [Tooltip("Release closer than this (meters) to snap.")]
    [Min(0.05f)] [SerializeField] float snapRadius = 0.6f;
    [Min(0.02f)] [SerializeField] float snapSeconds = 0.25f;
    [Tooltip("Optional placeholder shown at the spot, hidden once something snaps in.")]
    [SerializeField] GameObject hideWhenFilled;
    [Tooltip("The snapped object can no longer be picked up.")]
    [SerializeField] bool lockAfterSnap = true;

    public UnityEvent onSnapped;
    public event Action<XRGrabInteractable> Snapped;

    readonly List<XRGrabInteractable> watched = new List<XRGrabInteractable>();
    bool filled;

    public bool IsFilled => filled;
    public float SnapRadius => snapRadius;

    public void Configure(XRGrabInteractable accepted, GameObject placeholder, float radius)
    {
        acceptedObject = accepted;
        hideWhenFilled = placeholder;
        snapRadius = radius;
        if (isActiveAndEnabled) Rehook();
    }

    void OnEnable() => Rehook();

    void OnDisable() => Unhook();

    void Rehook()
    {
        Unhook();
        if (acceptedObject != null)
            watched.Add(acceptedObject);
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

    void OnReleased(SelectExitEventArgs args)
    {
        if (filled) return;
        var grab = args.interactableObject as XRGrabInteractable;
        if (grab == null || grab.isSelected) return;
        if (Vector3.Distance(grab.transform.position, transform.position) > snapRadius) return;
        StartCoroutine(SnapIn(grab));
    }

    IEnumerator SnapIn(XRGrabInteractable grab)
    {
        filled = true;
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
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / snapSeconds));
            grab.transform.SetPositionAndRotation(
                Vector3.Lerp(from, transform.position, t),
                Quaternion.Slerp(fromRotation, transform.rotation, t));
            yield return null;
        }

        grab.transform.SetPositionAndRotation(transform.position, transform.rotation);
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        onSnapped?.Invoke();
        Snapped?.Invoke(grab);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, snapRadius);
    }
}
