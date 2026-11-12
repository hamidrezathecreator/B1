using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Tablet grab: the left hand holds it in a fixed pose, screen toward the player and beside the
/// hand so the right hand can press it. The right hand keeps the grab where it touched.
/// </summary>
public sealed class TabletGrabInteractable : XRGrabInteractable
{
    [Header("Left hand hold")]
    [Tooltip("Screen centre relative to the left controller, meters: x right, y up, z forward.")]
    [SerializeField] Vector3 leftHoldOffset = new Vector3(0.12f, 0.05f, 0.1f);
    [Tooltip("Tilts the screen up toward the face, degrees.")]
    [Range(0f, 80f)] [SerializeField] float leftHoldTilt = 40f;
    [Tooltip("Turns the screen in toward the middle of the body, degrees.")]
    [Range(-60f, 60f)] [SerializeField] float leftHoldTurn = 20f;

    Transform leftAttach;
    MovementType rightMovementType = MovementType.VelocityTracking;

    protected override void Awake()
    {
        base.Awake();
        rightMovementType = movementType;
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        var left = IsLeftHand(args.interactorObject);
        // Decided before the base grab, which reads these to set up the attach pose.
        useDynamicAttach = !left;
        if (left)
        {
            // Instantaneous + fixed attach: VelocityTracking fought the pose and the left hand lost it.
            movementType = MovementType.Instantaneous;
            attachTransform = LeftAttach();
        }
        else
        {
            movementType = rightMovementType;
            // Keep a previous left attach out of a right-hand grab.
            if (attachTransform == leftAttach) attachTransform = null;
        }
        base.OnSelectEntering(args);
    }

    protected override void OnSelectExiting(SelectExitEventArgs args)
    {
        base.OnSelectExiting(args);
        // Next grab starts from the default movement again unless it is another left grab.
        if (!isSelected) movementType = rightMovementType;
    }

    static bool IsLeftHand(IXRInteractor interactor)
    {
        if (interactor == null) return false;
        // Near-Far / poke / ray under the left controller all count as the left hand.
        for (var t = interactor.transform; t != null; t = t.parent)
        {
            var n = t.name;
            if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        }
        return false;
    }

    /// <summary>A point on the tablet that lines up with the left controller when held.</summary>
    Transform LeftAttach()
    {
        if (leftAttach == null)
        {
            leftAttach = new GameObject("Left Hand Hold").transform;
            leftAttach.SetParent(transform, false);
        }

        // Screen frame in world space: from the LCD canvas if there is one.
        var lcd = GetComponentInChildren<MilitaryTabletLcdUi>(true);
        Vector3 center, toViewer, up;
        if (lcd != null)
        {
            var canvas = lcd.transform;
            center = canvas.position;
            // A world-space canvas reads from its -Z side.
            toViewer = -canvas.forward;
            up = canvas.up;
        }
        else
        {
            var box = GetComponent<BoxCollider>();
            center = box != null ? transform.TransformPoint(box.center) : transform.position;
            toViewer = transform.up;
            up = transform.forward;
        }

        if (toViewer.sqrMagnitude < 1e-6f) toViewer = Vector3.up;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.forward;
        toViewer.Normalize();
        up = Vector3.ProjectOnPlane(up, toViewer).normalized;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(toViewer, Vector3.right).normalized;

        // Wanted, in the controller's frame: screen toward the player, tilted up and turned inward.
        var wanted = Quaternion.Euler(leftHoldTilt, -leftHoldTurn, 0f);
        var wantedToViewer = wanted * Vector3.back;
        var wantedUp = wanted * Vector3.up;
        var screenInController = Quaternion.LookRotation(wantedToViewer, wantedUp);
        var screenInWorld = Quaternion.LookRotation(toViewer, up);

        // Controller pose relative to the tablet (as it is now), so the screen lands as wanted.
        var controllerRotation = screenInWorld * Quaternion.Inverse(screenInController);
        var controllerPosition = center - controllerRotation * leftHoldOffset;
        leftAttach.SetPositionAndRotation(controllerPosition, controllerRotation);
        return leftAttach;
    }
}
