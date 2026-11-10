using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// The Complete XR Origin already moves, turns, and teleports.
/// This scene still needs a teleport surface under the player and a grabbable camera case.
/// </summary>
static class BridgeXrPlaySetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;

        var cases = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var foundCase = false;
        foreach (var transform in cases)
        {
            if (!transform.name.StartsWith("SM_Hard_Camera_Case"))
                continue;
            foundCase = true;
            MakeGrabbable(transform.gameObject);
        }

        if (!foundCase)
            return;

        EnsureHeadsetCamera(origin);
        EnsureWalkAndTeleport(origin);
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

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
            body.enabled = true;
    }

    static void EnsureWalkAndTeleport(XROrigin origin)
    {
        var start = origin.transform.position + Vector3.up * 2f;
        var hits = Physics.RaycastAll(start, Vector3.down, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<XROrigin>() != null)
                continue;
            if (hit.collider.GetComponentInParent<XRGrabInteractable>() != null)
                continue;

            if (hit.collider.GetComponent<TeleportationArea>() == null)
                hit.collider.gameObject.AddComponent<TeleportationArea>();
            return;
        }

        var deck = new GameObject("Walkable Deck");
        deck.transform.SetPositionAndRotation(
            origin.transform.position + Vector3.down * 0.05f,
            Quaternion.Euler(0f, origin.transform.eulerAngles.y, 0f));
        var box = deck.AddComponent<BoxCollider>();
        box.size = new Vector3(40f, 0.1f, 500f);
        deck.AddComponent<TeleportationArea>();
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
