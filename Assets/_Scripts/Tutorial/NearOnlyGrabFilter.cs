using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Grab only by touching: hover and select are refused unless the hand is within
/// <see cref="maxDistance"/> of the object, so the laser cannot pull it from afar.
/// </summary>
[AddComponentMenu("B1/Near Only Grab Filter")]
public sealed class NearOnlyGrabFilter : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
{
    [Tooltip("How close the hand must be to the object's surface, in meters.")]
    [Min(0.01f)] [SerializeField] float maxDistance = 0.25f;

    public bool canProcess => isActiveAndEnabled;

    /// <summary>Adds the filter to a grabbable once.</summary>
    public static NearOnlyGrabFilter AddTo(XRBaseInteractable interactable, float distance = 0.25f)
    {
        if (interactable == null) return null;
        var filter = interactable.GetComponent<NearOnlyGrabFilter>();
        if (filter == null)
        {
            filter = interactable.gameObject.AddComponent<NearOnlyGrabFilter>();
            interactable.selectFilters.Add(filter);
            interactable.hoverFilters.Add(filter);
        }
        filter.maxDistance = distance;
        return filter;
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        // Select filters are re-checked every frame. Once held, keep it: a teleport moves the hand
        // a frame before the object, and the distance check would drop it.
        if (interactor.IsSelecting(interactable)) return true;
        return IsNear(interactor.transform, interactable as XRBaseInteractable);
    }

    public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
    {
        if (interactor is IXRSelectInteractor selector && interactable is IXRSelectInteractable selectable &&
            selector.IsSelecting(selectable))
            return true;
        return IsNear(interactor.transform, interactable as XRBaseInteractable);
    }

    bool IsNear(Transform hand, XRBaseInteractable interactable)
    {
        if (hand == null || interactable == null) return false;
        var point = hand.position;
        foreach (var col in interactable.colliders)
        {
            if (col == null || !col.enabled) continue;
            if ((col.ClosestPoint(point) - point).sqrMagnitude <= maxDistance * maxDistance)
                return true;
        }
        return false;
    }
}
