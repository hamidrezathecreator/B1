using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Tablet grab: the left hand holds it in a fixed pose, screen toward the player and beside the
/// hand so the right hand can press it. The right hand keeps the grab where it touched.
/// </summary>
public sealed class TabletGrabInteractable : XRGrabInteractable
{
    [Header("Left hand hold")]
    [Tooltip("Screen centre relative to the left controller, meters: x right, y up, z forward.")]
    [SerializeField] Vector3 leftHoldOffset = new Vector3(0.1f, 0.04f, 0.08f);
    [Tooltip("Tilts the screen up toward the face, degrees.")]
    [Range(0f, 80f)] [SerializeField] float leftHoldTilt = 35f;
    [Tooltip("Turns the screen in toward the middle of the body, degrees.")]
    [Range(-60f, 60f)] [SerializeField] float leftHoldTurn = 15f;

    Transform leftAttach;

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        var left = IsLeftHand(args.interactorObject != null ? args.interactorObject.transform : null);
        // Decided before the base grab, which reads these to set up the attach pose.
        useDynamicAttach = !left;
        // Left only: the right hand's grab uses its own touch point, and the left hand may still hold on.
        if (left) attachTransform = LeftAttach();
        base.OnSelectEntering(args);
    }

    static bool IsLeftHand(Transform interactor)
    {
        for (var t = interactor; t != null; t = t.parent)
            if (t.name.Contains("Left")) return true;
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
