using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
    const float BodyHeight = 1.6f;
    const float SkinWidth = 0.02f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;

        var foundCase = false;
        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!transform.name.StartsWith("SM_Hard_Camera_Case"))
                continue;

            foundCase = true;
            MakeGrabbable(transform.gameObject);
        }

        if (!foundCase)
            return;

        EnsureHeadsetCamera(origin);
        var floorY = FindFloorHeight(origin);
        var groundMask = BuildFlatFloor(origin, floorY);
        PlantBody(origin, floorY, groundMask);
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

        var deck = new GameObject("Walkable Deck");
        deck.layer = groundLayer;
        deck.transform.SetPositionAndRotation(
            new Vector3(origin.transform.position.x, floorY - 0.5f, origin.transform.position.z),
            Quaternion.identity);
        var box = deck.AddComponent<BoxCollider>();
        box.size = new Vector3(80f, 1f, 500f);
        var area = deck.AddComponent<TeleportationArea>();

        var groundMask = (LayerMask)(1 << groundLayer);
        var teleportLayers = area.interactionLayers;
        var foundTeleportRay = false;
        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport"))
                continue;

            ray.raycastMask = groundMask;
            teleportLayers = ray.interactionLayers;
            foundTeleportRay = true;
        }

        if (foundTeleportRay)
            area.interactionLayers = teleportLayers;

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
            body.includeLayers = groundMask;
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
    }

    static void MakeGrabbable(GameObject root)
    {
        if (root.GetComponent<XRGrabInteractable>() != null)
            return;

        root.isStatic = false;
        foreach (var child in root.GetComponentsInChildren<Transform>())
            child.gameObject.isStatic = false;

        if (root.GetComponentInChildren<Collider>() == null)
        {
            var renderer = root.GetComponentInChildren<Renderer>();
            if (renderer == null)
                return;

            var box = renderer.gameObject.AddComponent<BoxCollider>();
            box.center = renderer.localBounds.center;
            box.size = renderer.localBounds.size;
        }

        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            body = root.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = true;
        grab.forceGravityOnDetach = true;
    }
}

/// <summary>
/// The deck under the spawn point is the floor. Stick movement and the A button must not change its height.
/// </summary>
sealed class BridgeDeckLock : MonoBehaviour
{
    public float floorY;

    void LateUpdate()
    {
        var position = transform.position;
        if (Mathf.Abs(position.y - floorY) <= 0.0001f)
            return;

        transform.position = new Vector3(position.x, floorY, position.z);
    }
}
