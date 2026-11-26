using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Designer-tunable height and locomotion defaults for the XR Origin prefab.
/// Device tracking uses the designer's standing eye height and preserves relative headset movement.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XROrigin))]
public sealed class XrOriginPlaySettings : MonoBehaviour
{
    [Header("Height")]
    [Tooltip("Standing eye height in meters above the rig floor. Device tracking preserves head movement relative to the initial headset pose.")]
    [Min(0.1f)] [SerializeField] float eyeHeightMeters = 1.8f;

    [Tooltip("CharacterController capsule height in meters.")]
    [Min(0.1f)] [SerializeField] float bodyHeightMeters = 1.8f;

    [SerializeField] float bodyRadius = 0.2f;

    [Header("Locomotion")]
    [Tooltip("Left thumbstick: smooth walk. Teleport is disabled on the XR Origin prefab.")]
    [SerializeField] bool enableStickWalking = true;

    [SerializeField] float moveSpeed = 2.5f;

    float heightMultiplier = 1f;

    public float EyeHeightMeters => eyeHeightMeters * heightMultiplier;
    public float BodyHeightMeters => bodyHeightMeters * heightMultiplier;
    public bool IsLocomotionLocked { get; private set; }

    public void SetLocomotionLocked(bool locked) => IsLocomotionLocked = locked;

    public void SetHeightMultiplier(float multiplier)
    {
        heightMultiplier = Mathf.Max(0.1f, multiplier);
        Apply();
    }

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

        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        ApplyCameraHeight();

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.height = BodyHeightMeters;
            body.radius = bodyRadius;
            body.center = new Vector3(0f, BodyHeightMeters * 0.5f, 0f);
        }

        if (!enableStickWalking || IsLocomotionLocked) return;

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move == null) return;

        move.enabled = true;
        move.moveSpeed = moveSpeed;
        move.enableStrafe = true;
        move.enableFly = false;
        move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
    }

    public void ApplyCameraHeight()
    {
        var origin = GetComponent<XROrigin>();
        if (origin == null) return;

        if (!Mathf.Approximately(origin.CameraYOffset, EyeHeightMeters))
            origin.CameraYOffset = EyeHeightMeters;

        if (origin.CameraFloorOffsetObject == null) return;
        var offset = origin.CameraFloorOffsetObject.transform;
        var local = offset.localPosition;
        offset.localPosition = new Vector3(local.x, EyeHeightMeters, local.z);
    }

    void LateUpdate() => ApplyCameraHeight();
}
