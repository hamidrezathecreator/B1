using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Designer-tunable height and locomotion defaults for the XR Origin prefab.
/// Floor tracking supplies real eye height on headset builds; offsets stay at zero so scenes do not stack extra lift.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XROrigin))]
public sealed class XrOriginPlaySettings : MonoBehaviour
{
    [Header("Height")]
    [Tooltip("Standing eye height in meters (reference for designers; not added on top of floor tracking in VR builds).")]
    [SerializeField] float eyeHeightMeters = 1.65f;

    [Tooltip("CharacterController capsule height in meters.")]
    [SerializeField] float bodyHeightMeters = 1.75f;

    [SerializeField] float bodyRadius = 0.2f;

    [Header("Locomotion")]
    [Tooltip("Left thumbstick: smooth walk. Right thumbstick stays on teleport (see Controller Input Action Manager on each controller).")]
    [SerializeField] bool enableStickWalking = true;

    [SerializeField] float moveSpeed = 2.5f;

    public float EyeHeightMeters => eyeHeightMeters;
    public float BodyHeightMeters => bodyHeightMeters;

    void Awake() => Apply();

    public void SetStickWalkingEnabled(bool on)
    {
        enableStickWalking = on;
        Apply();
    }

    public void Apply()
    {
        var origin = GetComponent<XROrigin>();
        if (origin == null) return;

        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        if (origin.CameraFloorOffsetObject != null)
        {
            var t = origin.CameraFloorOffsetObject.transform;
            var local = t.localPosition;
            t.localPosition = new Vector3(local.x, 0f, local.z);
        }

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.height = bodyHeightMeters;
            body.radius = bodyRadius;
            body.center = new Vector3(0f, bodyHeightMeters * 0.5f, 0f);
        }

        if (!enableStickWalking) return;

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move == null) return;

        move.enabled = true;
        move.moveSpeed = moveSpeed;
        move.enableFly = false;
        move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
    }
}