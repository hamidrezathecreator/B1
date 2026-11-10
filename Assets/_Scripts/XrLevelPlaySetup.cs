using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

/// <summary>
/// Open-ground level setup (Lvl 3): floor tracking, stick walking with gravity on uneven
/// ground, teleport onto the ground mesh only, and the project hands on both controllers.
/// Same rules as the Lvl 2 elevator scene, without the elevator.
/// </summary>
public sealed class XrLevelPlaySetup : MonoBehaviour
{
    const float BodyHeight = 1.75f;
    const float SkinWidth = 0.02f;

    [Header("Hands")]
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("Wrist position on the controller (right hand; left is mirrored on X).")]
    [SerializeField] Vector3 handOnControllerPosition = new Vector3(-0.01f, -0.03f, -0.07f);
    [Tooltip("Extra wrist rotation on the controller, degrees.")]
    [SerializeField] Vector3 handOnControllerEuler = Vector3.zero;

    [Header("Ground")]
    [Tooltip("Objects whose meshes are walkable and teleportable. Empty = Topology / Terrain / TerrainProxy.")]
    [SerializeField] Transform[] groundRoots;
    [Tooltip("Steeper ground than this blocks walking and turns the teleport reticle red.")]
    [Range(10f, 80f)] [SerializeField] float maxSlopeDegrees = 40f;
    [SerializeField] bool placePlayerOnGround = true;

    XROrigin origin;

    void Start()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) return;

        // The first-scene auto setup pins the body to one height and adds a flat deck.
        // Uneven ground needs neither.
        BridgeXrPlaySetup.SuspendDeckLock(origin);
        var deck = GameObject.Find("Walkable Deck");
        if (deck != null) deck.SetActive(false);

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;
        LayerMask groundMask = 1 << groundLayer;

        UseOnlyHeadsetCamera();
        PrepareGround(groundLayer);
        SetupBody(groundMask);
        SetupTeleportRays(groundMask);
        if (placePlayerOnGround) PlantOnGround(groundMask);

        var rig = GetComponent<XrHandsRig>();
        if (rig == null) rig = gameObject.AddComponent<XrHandsRig>();
        rig.Configure(leftHandVisualPrefab, rightHandVisualPrefab, handOnControllerPosition, handOnControllerEuler);
        rig.Setup(origin);
    }

    void UseOnlyHeadsetCamera()
    {
        var headset = origin.Camera;
        if (headset == null) return;

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (cam == headset || !cam.CompareTag("MainCamera")) continue;
            cam.enabled = false;
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }

    IEnumerable<Transform> GroundRoots()
    {
        if (groundRoots != null && groundRoots.Length > 0)
        {
            foreach (var root in groundRoots)
                if (root != null) yield return root;
            yield break;
        }

        foreach (var name in new[] { "Topology", "TerrainProxy", "Terrain" })
        {
            var found = GameObject.Find(name);
            if (found != null) yield return found.transform;
        }

        foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            yield return terrain.transform;
    }

    void PrepareGround(int groundLayer)
    {
        foreach (var root in GroundRoots())
        {
            var hasCollider = false;

            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain.GetComponent<TerrainCollider>() == null)
                {
                    var tc = terrain.gameObject.AddComponent<TerrainCollider>();
                    tc.terrainData = terrain.terrainData;
                }
            }

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;
                // Topology.fbx imports with colliders; this only covers meshes that still lack one.
                var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
            }

            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                col.gameObject.layer = groundLayer;
                hasCollider = true;
            }

            if (!hasCollider) continue;

            // One area per root collects every collider under it.
            var area = root.GetComponent<TeleportationArea>();
            if (area == null) area = root.gameObject.AddComponent<TeleportationArea>();
            area.enabled = true;
            area.interactionLayers = -1;
            area.filterSelectionByHitNormal = true;
            area.upNormalToleranceDegrees = maxSlopeDegrees;
        }
    }

    void SetupBody(LayerMask groundMask)
    {
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        if (origin.CameraFloorOffsetObject != null)
        {
            var offset = origin.CameraFloorOffsetObject.transform.localPosition;
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, 0f, offset.z);
        }

        var defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0) defaultLayer = 0;

        var body = origin.GetComponent<CharacterController>();
        if (body != null)
        {
            body.enabled = true;
            body.height = BodyHeight;
            body.radius = 0.2f;
            body.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
            body.stepOffset = 0.3f;
            body.slopeLimit = maxSlopeDegrees;
            body.skinWidth = SkinWidth;
            body.includeLayers = groundMask | (1 << defaultLayer);
        }

        foreach (var gravity in origin.GetComponentsInChildren<GravityProvider>(true))
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
        if (move != null)
        {
            move.enabled = true;
            move.leftHandMovementDirection = DynamicMoveProvider.MovementDirection.HeadRelative;
            move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            move.enableFly = false;
        }
    }

    void SetupTeleportRays(LayerMask groundMask)
    {
        // Ground only: props and launchers in front of the ground would turn the reticle red.
        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport")) continue;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = groundMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }

        foreach (var provider in origin.GetComponentsInChildren<TeleportationProvider>(true))
            provider.enabled = true;
    }

    void PlantOnGround(LayerMask groundMask)
    {
        var start = origin.transform.position + Vector3.up * 50f;
        if (!Physics.Raycast(start, Vector3.down, out var hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        var body = origin.GetComponent<CharacterController>();
        var wasEnabled = body != null && body.enabled;
        if (body != null) body.enabled = false;
        var p = origin.transform.position;
        origin.transform.position = new Vector3(p.x, hit.point.y + SkinWidth, p.z);
        if (body != null) body.enabled = wasEnabled;
    }
}
