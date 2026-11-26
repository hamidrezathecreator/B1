using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Lvl 1 already has a Complete XR Origin. This keeps the player on the floor they spawn on:
/// left stick walks with the head, A no longer jumps, and the body cannot climb off that height.
/// </summary>
static class BridgeXrPlaySetup
{
    const float BodyHeight = 1.75f;
    const float SkinWidth = 0.02f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;


        if (origin.gameObject.scene.name == BuildLevelScenes.Lvl2 || origin.gameObject.scene.name == BuildLevelScenes.Lvl3)
            return;

        // Always plant the body/floor/teleport deck. Waiting for button A used to
        // accidentally "fix" height only after a jump pulse; do it on load instead.
        EnsurePlaySpace(origin);
    }

    /// <summary>Floor height, walk/teleport deck, and eye height for the active XR Origin.</summary>
    public static void EnsurePlaySpace(XROrigin origin)
    {
        if (origin == null)
            return;
        if (HelicopterHover.IsPassenger(origin))
            return;

        EnsureHeadsetCamera(origin);
        ApplyOriginPlaySettings(origin);
        WidenGrabReach(origin);
        var floorY = FindFloorHeight(origin);
        var groundMask = BuildFlatFloor(origin, floorY);
        PlantBody(origin, floorY, groundMask);
        if (origin.GetComponent<BridgeDeckLock>() == null)
            KeepFeetOnFloor(origin, floorY);
    }

    /// <summary>
    /// Elevator cabin: designer eye height, and no world-Y lock
    /// so the player can ride with the cabin when it starts below the tunnel.
    /// </summary>
    public static void PrepareElevatorPlaySpace(XROrigin origin)
    {
        if (origin == null)
            return;

        SuspendDeckLock(origin);
        EnsureHeadsetCamera(origin);
        WidenGrabReach(origin);

        ApplyOriginPlaySettings(origin);

        var keeper = origin.GetComponent<CameraLiftKeeper>();
        if (keeper == null) keeper = origin.gameObject.AddComponent<CameraLiftKeeper>();
        keeper.origin = origin;

        var body = origin.GetComponent<CharacterController>();
        var bodyHeight = origin.GetComponent<XrOriginPlaySettings>()?.BodyHeightMeters ?? BodyHeight;
        if (body != null)
        {
            body.enabled = true;
            body.height = bodyHeight;
            // Slightly tighter so the cabin walls do not pinch the body in the middle.
            body.radius = 0.18f;
            body.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
            body.stepOffset = 0.08f;
            body.slopeLimit = 20f;
            body.skinWidth = SkinWidth;
            var defaultLayer = LayerMask.NameToLayer("Default");
            if (defaultLayer < 0) defaultLayer = 0;
            var groundLayer = LayerMask.NameToLayer("Ground");
            var groundMask = groundLayer >= 0 ? (1 << groundLayer) : 0;
            body.includeLayers = groundMask | (1 << defaultLayer);
        }

        foreach (var gravity in origin.GetComponentsInChildren<GravityProvider>(true))
            gravity.enabled = false;

        foreach (var jump in origin.GetComponentsInChildren<JumpProvider>(true))
            jump.enabled = false;
    }

    public static void SuspendDeckLock(XROrigin origin)
    {
        if (origin == null)
            return;

        foreach (var lockHeight in origin.GetComponents<BridgeDeckLock>())
            Object.Destroy(lockHeight);
    }

    static void EnsureHeadsetCamera(XROrigin origin)
    {
        var headset = origin.Camera;
        if (headset == null)
            return;

        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (camera != headset && camera.CompareTag("MainCamera"))
                camera.enabled = false;
        }
    }

    static void ApplyOriginPlaySettings(XROrigin origin)
    {
        if (origin == null)
            return;

        var settings = origin.GetComponent<XrOriginPlaySettings>();
        if (settings != null)
        {
            settings.Apply();
            return;
        }

        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        if (origin.CameraFloorOffsetObject != null)
        {
            var offset = origin.CameraFloorOffsetObject.transform.localPosition;
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, 0f, offset.z);
        }
    }

    static float FindFloorHeight(XROrigin origin)
    {
        var originPosition = origin.transform.position;
        var hits = Physics.RaycastAll(
            originPosition + Vector3.up * 3f,
            Vector3.down,
            40f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        var floorY = originPosition.y;
        var bestGap = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<XROrigin>() != null)
                continue;
            if (hit.collider.GetComponentInParent<XRGrabInteractable>() != null)
                continue;

            var gap = Mathf.Abs(hit.point.y - originPosition.y);
            if (gap >= bestGap)
                continue;

            bestGap = gap;
            floorY = hit.point.y;
        }

        return floorY;
    }

    static LayerMask BuildFlatFloor(XROrigin origin, float floorY)
    {
        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
            groundLayer = 0;

        var deck = GameObject.Find("Walkable Deck");
        if (deck == null)
        {
            deck = new GameObject("Walkable Deck");
            deck.AddComponent<BoxCollider>();
            deck.AddComponent<TeleportationArea>();
        }

        deck.layer = groundLayer;
        deck.transform.SetPositionAndRotation(
            new Vector3(origin.transform.position.x, floorY - 0.5f, origin.transform.position.z),
            Quaternion.identity);
        var box = deck.GetComponent<BoxCollider>();
        // Footing only — teleport uses explicit Plane / elevator floor areas so walls stay blocked.
        box.size = new Vector3(12f, 1f, 12f);
        var area = deck.GetComponent<TeleportationArea>();
        area.enabled = false;

        var groundMask = (LayerMask)(1 << groundLayer);
        // Ground-only: hitting Default walls/props before a pad makes the reticle red.
        var teleportRayMask = groundMask;
        InteractionLayerMask teleportLayers = area.interactionLayers;
        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport"))
                continue;

            ray.raycastMask = teleportRayMask;
            teleportLayers = ray.interactionLayers;
        }

        // Floor planes: walk + teleport. Walls/objects stay Default without TeleportationArea.
        foreach (var plane in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (plane == null || !plane.gameObject.name.StartsWith("Plane"))
                continue;
            plane.gameObject.layer = groundLayer;
            var planeArea = plane.GetComponent<TeleportationArea>();
            if (planeArea == null)
                planeArea = plane.gameObject.AddComponent<TeleportationArea>();
            planeArea.interactionLayers = teleportLayers;
        }

        return groundMask;
    }

    static void PlantBody(XROrigin origin, float floorY, LayerMask groundMask)
    {
        var settings = origin.GetComponent<XrOriginPlaySettings>();
        var bodyHeight = settings != null ? settings.BodyHeightMeters : BodyHeight;

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.enabled = true;
            body.height = bodyHeight;
            body.radius = 0.2f;
            body.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
            body.stepOffset = 0.08f;
            body.slopeLimit = 20f;
            body.skinWidth = SkinWidth;
            // Ground for feet + Default for walls/props so the body cannot walk through them.
            var defaultLayer = LayerMask.NameToLayer("Default");
            if (defaultLayer < 0) defaultLayer = 0;
            body.includeLayers = groundMask | (1 << defaultLayer);
        }

        var position = origin.transform.position;
        origin.transform.position = new Vector3(position.x, floorY + SkinWidth, position.z);

        var gravity = origin.GetComponentInChildren<GravityProvider>(true);
        if (gravity != null)
        {
            gravity.enabled = true;
            gravity.useGravity = true;
            gravity.useLocalSpaceGravity = false;
            gravity.sphereCastLayerMask = groundMask;
            gravity.sphereCastDistanceBuffer = 0.25f;
        }

        foreach (var jump in origin.GetComponentsInChildren<JumpProvider>(true))
            jump.enabled = false;

        if (settings != null)
            settings.Apply();
        else
        {
            var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
            if (move != null)
            {
                move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
                move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
                move.enableFly = false;
            }
        }
    }

    static void KeepFeetOnFloor(XROrigin origin, float floorY)
    {
        var lockHeight = origin.gameObject.AddComponent<BridgeDeckLock>();
        lockHeight.floorY = floorY + SkinWidth;
        lockHeight.origin = origin;
    }

    static void WidenGrabReach(XROrigin origin)
    {
        foreach (var caster in origin.GetComponentsInChildren<SphereInteractionCaster>(true))
            caster.castRadius = 0.25f;
    }


}

/// <summary>
/// Keeps only the camera lift above the floor, without pinning the body's height
/// (so the player can still ride the elevator cabin up and down).
/// </summary>
sealed class CameraLiftKeeper : MonoBehaviour
{
    public XROrigin origin;

    void LateUpdate()
    {
        if (origin == null) return;
        var settings = origin.GetComponent<XrOriginPlaySettings>();
        if (settings != null)
        {
            settings.ApplyCameraHeight();
            return;
        }
        if (origin.CameraYOffset != 0f)
            origin.CameraYOffset = 0f;
        if (origin.CameraFloorOffsetObject == null) return;
        var offset = origin.CameraFloorOffsetObject.transform;
        var local = offset.localPosition;
        if (Mathf.Abs(local.y) <= 0.0001f) return;
        offset.localPosition = new Vector3(local.x, 0f, local.z);
    }
}

/// <summary>
/// The deck under the spawn point is the floor. Stick movement and the A button must not change its height.
/// </summary>
sealed class BridgeDeckLock : MonoBehaviour
{
    public float floorY;
    public XROrigin origin;

    void LateUpdate()
    {
        var position = transform.position;
        if (Mathf.Abs(position.y - floorY) > 0.0001f)
            transform.position = new Vector3(position.x, floorY, position.z);

        if (origin == null)
            return;

        var settings = origin.GetComponent<XrOriginPlaySettings>();
        if (settings != null)
        {
            settings.ApplyCameraHeight();
            return;
        }

        if (origin.CameraYOffset != 0f)
            origin.CameraYOffset = 0f;

        if (origin.CameraFloorOffsetObject == null)
            return;

        var offset = origin.CameraFloorOffsetObject.transform;
        var local = offset.localPosition;
        if (Mathf.Abs(local.y) <= 0.0001f)
            return;

        offset.localPosition = new Vector3(local.x, 0f, local.z);
    }
}
