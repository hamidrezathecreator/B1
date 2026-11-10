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
    const float EyeHeight = 1.65f;
    // Between the old too-high Device offset and the last Floor height that felt too short.
    const float ExtraCameraLift = 0.12f;
    const float SkinWidth = 0.02f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;

        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!transform.name.StartsWith("SM_Hard_Camera_Case"))
                continue;
            MakeGrabbable(transform.gameObject);
        }

        // Always plant the body/floor/teleport deck. Waiting for button A used to
        // accidentally "fix" height only after a jump pulse; do it on load instead.
        EnsurePlaySpace(origin);
    }

    /// <summary>Floor height, walk/teleport deck, and eye height for the active XR Origin.</summary>
    public static void EnsurePlaySpace(XROrigin origin)
    {
        if (origin == null)
            return;

        EnsureHeadsetCamera(origin);
        MatchEyeHeight(origin);
        WidenGrabReach(origin);
        var floorY = FindFloorHeight(origin);
        var groundMask = BuildFlatFloor(origin, floorY);
        PlantBody(origin, floorY, groundMask);
        if (origin.GetComponent<BridgeDeckLock>() == null)
            KeepFeetOnFloor(origin, floorY);
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

    static void MatchEyeHeight(XROrigin origin)
    {
        // Floor keeps Quest tracking from stacking a full Device offset again.
        // ExtraCameraLift nudges the view a little above the last too-low height.
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = EyeHeight;
        ApplyCameraLift(origin);
    }

    static void ApplyCameraLift(XROrigin origin)
    {
        if (origin.CameraFloorOffsetObject == null)
            return;

        var offset = origin.CameraFloorOffsetObject.transform.localPosition;
        origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, ExtraCameraLift, offset.z);
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

        var defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0) defaultLayer = 0;
        var groundMask = (LayerMask)(1 << groundLayer);
        // Include Default so walls/props stop the teleport ray; only Ground has TeleportationArea.
        var teleportRayMask = groundMask | (1 << defaultLayer);
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
        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.enabled = true;
            body.height = BodyHeight;
            body.radius = 0.2f;
            body.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
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

        var move = origin.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move == null)
            return;

        move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        move.enableFly = false;
    }

    static void KeepFeetOnFloor(XROrigin origin, float floorY)
    {
        var lockHeight = origin.gameObject.AddComponent<BridgeDeckLock>();
        lockHeight.floorY = floorY + SkinWidth;
        lockHeight.cameraLift = ExtraCameraLift;
        lockHeight.origin = origin;
    }

    static void WidenGrabReach(XROrigin origin)
    {
        foreach (var caster in origin.GetComponentsInChildren<SphereInteractionCaster>(true))
            caster.castRadius = 0.25f;
    }

    static void MakeGrabbable(GameObject root)
    {
        if (root.GetComponentInParent<XRGrabInteractable>() != null)
            return;

        var parent = root.transform.parent;
        while (parent != null)
        {
            if (parent.name.StartsWith("SM_Hard_Camera_Case"))
                return;
            parent = parent.parent;
        }

        if (!TryFitBox(root, out var center, out var size))
            return;

        foreach (var child in root.GetComponentsInChildren<Transform>())
        {
            child.gameObject.isStatic = false;
            child.gameObject.layer = 0;
        }

        foreach (var meshCollider in root.GetComponentsInChildren<MeshCollider>())
            meshCollider.enabled = false;

        root.transform.SetParent(null, true);

        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = center;
        box.size = size;

        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            body = root.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.interactionLayers = -1;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.farAttachMode = InteractableFarAttachMode.Near;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = true;
        grab.forceGravityOnDetach = true;
    }

    static bool TryFitBox(GameObject root, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        var found = false;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            var local = renderer.localBounds;
            var extents = local.extents;
            var localCenter = local.center;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = renderer.transform.TransformPoint(localCenter + Vector3.Scale(extents, new Vector3(x, y, z)));
                        var inRoot = root.transform.InverseTransformPoint(corner);
                        min = Vector3.Min(min, inRoot);
                        max = Vector3.Max(max, inRoot);
                        found = true;
                    }
                }
            }
        }

        if (!found)
            return false;

        center = (min + max) * 0.5f;
        size = max - min;
        return size.sqrMagnitude > 0.0001f;
    }
}

/// <summary>
/// The deck under the spawn point is the floor. Stick movement and the A button must not change its height.
/// </summary>
sealed class BridgeDeckLock : MonoBehaviour
{
    public float floorY;
    public float cameraLift;
    public XROrigin origin;

    void LateUpdate()
    {
        var position = transform.position;
        if (Mathf.Abs(position.y - floorY) > 0.0001f)
            transform.position = new Vector3(position.x, floorY, position.z);

        if (origin == null || origin.CameraFloorOffsetObject == null)
            return;

        var offset = origin.CameraFloorOffsetObject.transform;
        var local = offset.localPosition;
        if (Mathf.Abs(local.y - cameraLift) <= 0.0001f)
            return;

        offset.localPosition = new Vector3(local.x, cameraLift, local.z);
    }
}
