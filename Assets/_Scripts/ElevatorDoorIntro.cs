using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Lvl2 elevator intro: fade in, hand visuals, walk inside cabin, ride on button press,
/// then open doors and unlock teleport into the tunnel.
/// </summary>
public sealed class ElevatorDoorIntro : MonoBehaviour
{
    [Header("Scene refs (auto-found by name if empty)")]
    [SerializeField] Transform elevatorRoot;
    [SerializeField] Transform door01;
    [SerializeField] Transform door02;
    [SerializeField] Transform elevatorButton;
    [SerializeField] XROrigin xrOrigin;

    [Header("Hand visuals")]
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("URP Lit skin material — Quest pink hands were caused by Shader Graph missing in build.")]
    [SerializeField] Material handSkinMaterial;

    [Header("Timing")]
    [Min(0f)] [SerializeField] float blackHoldSeconds = 3f;
    [Min(0f)] [SerializeField] float fadeInSeconds = 1.25f;
    [Min(0f)] [SerializeField] float doorDelaySeconds = 5f;
    [Min(0.05f)] [SerializeField] float doorOpenSeconds = 1.5f;

    [Header("Elevator ride (so the cabin clearly moves)")]
    [SerializeField] float rideHeight = 1.6f;
    [Min(0.1f)] [SerializeField] float rideSeconds = 4f;

    [Header("Spawn inside elevator (elevator local space)")]
    [SerializeField] Vector3 playerLocalPosition = new Vector3(-0.9f, 0.05f, 0f);
    [SerializeField] bool placePlayerOnStart = true;

    [Header("Door locals")]
    [SerializeField] Vector3 door01OpenLocal = new Vector3(0.267f, 0.1f, 1.214f);
    [SerializeField] Vector3 door01ClosedLocal = new Vector3(-0.436f, 0.1f, 1.214f);
    [SerializeField] Vector3 door02OpenLocal = new Vector3(-2.164f, 0.1f, 1.207f);
    [SerializeField] Vector3 door02ClosedLocal = new Vector3(-1.445f, 0.1f, 1.207f);

    [Header("Floor 1 poke (button local)")]
    [SerializeField] Vector3 floor1LocalPosition = new Vector3(-0.04f, -0.16f, 0.02f);
    [SerializeField] Vector3 floor1LocalSize = new Vector3(0.18f, 0.18f, 0.12f);
    [SerializeField] Vector3 buttonRestLocal = new Vector3(0.894f, 1.338f, 1.0577f);
    [SerializeField] Vector3 buttonPressedLocal = new Vector3(0.894f, 1.338f, 1.0766f);
    [Min(0.05f)] [SerializeField] float buttonPressSeconds = 0.18f;

    [Header("Button feedback")]
    [Min(0.05f)] [SerializeField] float buttonShakeSeconds = 1f;
    [Range(0.1f, 1f)] [SerializeField] float buttonShakeStrength = 0.85f;

    [Header("Tablet LCD UI (Assets/UI/UI_lvl2)")]
    [SerializeField] Texture2D tabletUiBackground;
    [SerializeField] Texture2D tabletOpt1Inactive;
    [SerializeField] Texture2D tabletOpt1Active;
    [SerializeField] Texture2D tabletOpt2Inactive;
    [SerializeField] Texture2D tabletOpt2Active;
    [SerializeField] Texture2D tabletOpt3Inactive;
    [SerializeField] Texture2D tabletOpt3Active;

    CanvasGroup fadeOverlay;
    GameObject exitBarrier;
    GameObject elevatorFloorTeleport;
    GameObject elevatorLobbyTeleport;
    Transform floor1Poke;
    Vector3 floor1PokeRestLocal;
    Vector3 elevatorButtonRestLocal;
    Vector3 elevatorRestPosition;
    bool started;
    bool floorPressed;

    void Start()
    {
        if (started) return;
        started = true;

        ResolveRefs();
        // Do not plant to tunnel floor / BridgeDeckLock here — that left the player
        // floating high while the cabin dropped for the ride.
        if (xrOrigin != null)
            BridgeXrPlaySetup.PrepareElevatorPlaySpace(xrOrigin);

        if (elevatorRoot != null)
        {
            elevatorRestPosition = elevatorRoot.position;
            // Start the cabin below the tunnel floor so the ride brings you "into" it.
            elevatorRoot.position = elevatorRestPosition + Vector3.down * rideHeight;
        }

        // Place AFTER the cabin drops, then parent so player + cabin share the ride.
        if (placePlayerOnStart) PlacePlayerInElevator();
        if (elevatorRoot != null && xrOrigin != null)
            xrOrigin.transform.SetParent(elevatorRoot, true);

        CloseDoorsInstant();
        EnsureDoorColliders();
        BuildCabinCage();
        EnsureWorldColliders();
        BuildExitBarrier();
        BuildElevatorFloorTeleport();
        BuildElevatorLobbyTeleport();
        EnsureStaticTable();
        // Move + teleport work inside the cabin; outside floors stay off until doors open.
        // Walkable Deck is a thick world box at spawn — disable it so it cannot pierce the cabin.
        SetWalkableDeckCollision(false);
        SetOutsideTeleportEnabled(false);
        ApplyGroundOnlyTeleportRays();
        SetupHandVisuals();
        WireFloor1Button();
        MakeMilitaryTabletGrabbable();
        EnsureFadeOverlay(startBlack: true);
        StartCoroutine(AutoFadeIn());
    }

    void Update()
    {
        if (!floorPressed && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space)))
            TryOpenDoors();
    }

    void ResolveRefs()
    {
        if (elevatorRoot == null)
        {
            var elev = GameObject.Find("Elevator");
            if (elev != null) elevatorRoot = elev.transform;
        }

        if (door01 == null)
        {
            var found = GameObject.Find("SM_DoorElevator01");
            if (found != null) door01 = found.transform;
        }

        if (door02 == null)
        {
            var found = GameObject.Find("SM_DoorElevator02");
            if (found != null) door02 = found.transform;
        }

        if (elevatorButton == null)
        {
            var found = GameObject.Find("SM_ElevatorButton");
            if (found != null) elevatorButton = found.transform;
        }

        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();
    }

    void PlacePlayerInElevator()
    {
        if (xrOrigin == null || elevatorRoot == null) return;
        // Local Y is cabin floor. Floor tracking supplies eye height from the headset.
        var worldPos = elevatorRoot.TransformPoint(playerLocalPosition);
        xrOrigin.transform.SetPositionAndRotation(worldPos, elevatorRoot.rotation);
        if (xrOrigin.CameraFloorOffsetObject != null)
        {
            var offset = xrOrigin.CameraFloorOffsetObject.transform.localPosition;
            xrOrigin.CameraFloorOffsetObject.transform.localPosition = new Vector3(offset.x, 0f, offset.z);
        }
    }

    void BuildCabinCage()
    {
        if (elevatorRoot == null) return;
        if (elevatorRoot.Find("ElevatorCabinCage") != null) return;

        // Interior from real cabin props:
        // doors ~x -2.16..-0.44 at z≈1.21, button panel at x≈0.89.
        // Old cage right wall sat at x≈0.4 and blocked the walk to the button,
        // while the real right/bottom had no collider so players clipped out.
        const float minX = -2.40f;
        const float maxX = 1.20f;
        const float minZ = -1.20f;
        const float maxZ = 1.28f;
        const float minY = 0.00f;
        const float maxY = 2.45f;
        const float wall = 0.28f;

        var cx = (minX + maxX) * 0.5f;
        var cz = (minZ + maxZ) * 0.5f;
        var width = maxX - minX;
        var depth = maxZ - minZ;
        var height = maxY - minY;

        var cage = new GameObject("ElevatorCabinCage");
        cage.transform.SetParent(elevatorRoot, false);
        cage.transform.localPosition = Vector3.zero;
        cage.transform.localRotation = Quaternion.identity;

        // Ceiling only here. Floor comes from ElevatorFloorTeleport (Ground + teleport).
        AddCageBox(cage.transform, "Ceiling",
            new Vector3(cx, maxY + wall * 0.5f, cz),
            new Vector3(width + wall, wall, depth + wall));
        AddCageBox(cage.transform, "Back",
            new Vector3(cx, height * 0.5f, minZ - wall * 0.5f),
            new Vector3(width + wall, height, wall));
        AddCageBox(cage.transform, "Left",
            new Vector3(minX - wall * 0.5f, height * 0.5f, cz),
            new Vector3(wall, height, depth + wall));
        AddCageBox(cage.transform, "Right",
            new Vector3(maxX + wall * 0.5f, height * 0.5f, cz),
            new Vector3(wall, height, depth + wall));

        // Front panels beside the door gap (doors + exit barrier cover the middle).
        const float doorGap = 1.15f;
        const float doorCenterX = -0.94f;
        var frontZ = maxZ + wall * 0.5f;
        var gapLeft = doorCenterX - doorGap * 0.5f;
        var gapRight = doorCenterX + doorGap * 0.5f;
        var leftFrontW = Mathf.Max(0.15f, gapLeft - minX);
        var rightFrontW = Mathf.Max(0.15f, maxX - gapRight);
        AddCageBox(cage.transform, "FrontLeft",
            new Vector3(minX + leftFrontW * 0.5f, height * 0.5f, frontZ),
            new Vector3(leftFrontW, height, wall));
        AddCageBox(cage.transform, "FrontRight",
            new Vector3(maxX - rightFrontW * 0.5f, height * 0.5f, frontZ),
            new Vector3(rightFrontW, height, wall));
    }

    static void AddCageBox(Transform parent, string name, Vector3 localPos, Vector3 size)
    {
        var go = new GameObject(name);
        go.layer = 0; // Default — blocks body; teleport rays use Ground-only in cabin
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = size;
    }

    void CloseDoorsInstant()
    {
        if (door01 != null) door01.localPosition = door01ClosedLocal;
        if (door02 != null) door02.localPosition = door02ClosedLocal;
    }

    void EnsureDoorColliders()
    {
        AddBlockingCollider(door01);
        AddBlockingCollider(door02);
    }

    static void AddBlockingCollider(Transform door)
    {
        if (door == null) return;
        if (door.GetComponentInChildren<Collider>() != null) return;

        var renderers = door.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var box = door.gameObject.AddComponent<BoxCollider>();
        box.center = door.InverseTransformPoint(bounds.center);
        var lossy = door.lossyScale;
        box.size = new Vector3(
            SafeDiv(bounds.size.x, Mathf.Abs(lossy.x)),
            SafeDiv(bounds.size.y, Mathf.Abs(lossy.y)),
            SafeDiv(bounds.size.z, Mathf.Abs(lossy.z)));
    }

    static float SafeDiv(float value, float by) => Mathf.Abs(by) < 0.0001f ? value : value / by;

    void BuildExitBarrier()
    {
        if (elevatorRoot == null || door01 == null || door02 == null) return;
        if (exitBarrier != null) return;

        var mid = (door01.position + door02.position) * 0.5f;
        mid.y = elevatorRoot.position.y + 1.1f;

        exitBarrier = new GameObject("ElevatorExitBarrier");
        exitBarrier.transform.SetParent(elevatorRoot, true);
        exitBarrier.transform.SetPositionAndRotation(mid, elevatorRoot.rotation);

        var box = exitBarrier.AddComponent<BoxCollider>();
        box.size = new Vector3(2.6f, 2.4f, 0.25f);
    }

    void SetupHandVisuals()
    {
        if (xrOrigin == null) return;

        // Soften non-teleport rays so hands/finger tip read clearly.
        foreach (var line in xrOrigin.GetComponentsInChildren<XRInteractorLineVisual>(true))
        {
            if (line.gameObject.name.Contains("Teleport"))
                continue;
            line.enabled = false;
        }

        // Simple URP palm+fingers — Quest skinned prefabs often render invisible in player builds.
        AttachHandPrefab("Left Controller", isLeft: true);
        AttachHandPrefab("Right Controller", isLeft: false);
        EnsurePokeInteractors();
    }

    void HideControllerMeshes(Transform controller)
    {
        if (controller == null) return;
        foreach (var child in controller.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Contains("Controller Visual") &&
                !child.name.Contains("UniversalController"))
                continue;
            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }
    }

    void AttachHandPrefab(string controllerName, bool isLeft)
    {
        if (xrOrigin == null) return;

        Transform controller = null;
        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != controllerName) continue;
            controller = child;
            break;
        }

        if (controller == null) return;

        var existing = controller.Find("ControllerHandModel");
        if (existing != null)
            Destroy(existing.gameObject);

        var hand = BuildSimpleControllerHand(controller, isLeft);
        HideControllerMeshes(controller);

        var driver = hand.GetComponent<ControllerHandFingerDriver>();
        if (driver == null)
            driver = hand.AddComponent<ControllerHandFingerDriver>();
        driver.Bind(isLeft);
    }

    GameObject BuildSimpleControllerHand(Transform controller, bool isLeft)
    {
        var skin = ResolveHandSkinMaterial();
        var hand = new GameObject("ControllerHandModel");
        hand.transform.SetParent(controller, false);
        hand.transform.localPosition = new Vector3(0f, -0.02f, 0.02f);
        hand.transform.localRotation = Quaternion.identity;
        hand.transform.localScale = Vector3.one;

        // Palm
        var palm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        palm.name = "Palm";
        palm.transform.SetParent(hand.transform, false);
        palm.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        palm.transform.localScale = new Vector3(0.07f, 0.025f, 0.09f);
        ApplySkin(palm, skin);
        Object.Destroy(palm.GetComponent<Collider>());

        // Index finger (trigger) + other fingers (grip)
        CreateFinger(hand.transform, "index_finger", new Vector3(isLeft ? 0.02f : -0.02f, 0f, 0.08f), skin);
        CreateFinger(hand.transform, "middle_finger", new Vector3(isLeft ? 0.005f : -0.005f, 0f, 0.085f), skin);
        CreateFinger(hand.transform, "ring_finger", new Vector3(isLeft ? -0.012f : 0.012f, 0f, 0.08f), skin);
        CreateFinger(hand.transform, "pinky_finger", new Vector3(isLeft ? -0.028f : 0.028f, 0f, 0.07f), skin);
        CreateFinger(hand.transform, "thumb_finger", new Vector3(isLeft ? 0.035f : -0.035f, 0.01f, 0.04f), skin, length: 0.045f);

        return hand;
    }

    static void CreateFinger(Transform parent, string name, Vector3 localPos, Material skin, float length = 0.055f)
    {
        var finger = new GameObject(name);
        finger.transform.SetParent(parent, false);
        finger.transform.localPosition = localPos;
        finger.transform.localRotation = Quaternion.identity;

        var seg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        seg.name = name + "_seg";
        seg.transform.SetParent(finger.transform, false);
        seg.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        seg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        seg.transform.localScale = new Vector3(0.012f, length * 0.5f, 0.012f);
        ApplySkin(seg, skin);
        Object.Destroy(seg.GetComponent<Collider>());
    }

    static void ApplySkin(GameObject go, Material skin)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        if (skin != null)
            renderer.sharedMaterial = skin;
        else
        {
            // Last resort — unlit color so Quest never shows pink error shader.
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.91f, 0.74f, 0.62f, 1f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", new Color(0.91f, 0.74f, 0.62f, 1f));
                renderer.sharedMaterial = mat;
            }
        }
    }

    Material ResolveHandSkinMaterial()
    {
        if (handSkinMaterial != null)
            return handSkinMaterial;

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
            lit = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (lit == null)
            lit = Shader.Find("Universal Render Pipeline/Unlit");
        if (lit == null)
            return null;

        var mat = new Material(lit);
        mat.name = "RuntimeHandSkin";
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(0.91f, 0.74f, 0.62f, 1f));
        mat.color = new Color(0.91f, 0.74f, 0.62f, 1f);
        return mat;
    }

    void EnsurePokeInteractors()
    {
        if (xrOrigin == null) return;

        var pokes = xrOrigin.GetComponentsInChildren<XRPokeInteractor>(true);
        if (pokes == null || pokes.Length == 0)
        {
            EnsurePokeOnController("Left Controller");
            EnsurePokeOnController("Right Controller");
            pokes = xrOrigin.GetComponentsInChildren<XRPokeInteractor>(true);
        }

        var skin = ResolveHandSkinMaterial();
        foreach (var poke in pokes)
        {
            if (poke == null) continue;
            poke.gameObject.SetActive(true);
            poke.enabled = true;
            poke.interactionLayers = -1;

            // Keep poke point near index tip of the controller hand model when present.
            var controllerRoot = poke.transform;
            while (controllerRoot.parent != null &&
                   controllerRoot.name != "Left Controller" &&
                   controllerRoot.name != "Right Controller")
                controllerRoot = controllerRoot.parent;

            var hand = controllerRoot.Find("ControllerHandModel");
            if (hand != null)
            {
                var index = FindNamedChild(hand, "index_finger");
                if (index != null)
                    poke.transform.position = index.TransformPoint(new Vector3(0f, 0f, 0.055f));
            }

            var oldTip = poke.transform.Find("PokeTipVisual");
            if (oldTip != null)
                Destroy(oldTip.gameObject);

            // Visible fingertip so the user can aim at tablet options.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "PokeTipVisual";
            tip.transform.SetParent(poke.transform, false);
            tip.transform.localPosition = Vector3.zero;
            tip.transform.localRotation = Quaternion.identity;
            tip.transform.localScale = Vector3.one * 0.018f;
            ApplySkin(tip, skin);
            var tipCollider = tip.GetComponent<Collider>();
            if (tipCollider != null) Destroy(tipCollider);
        }
    }

    void EnsurePokeOnController(string controllerName)
    {
        Transform controller = null;
        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != controllerName) continue;
            controller = child;
            break;
        }
        if (controller == null) return;
        if (controller.GetComponentInChildren<XRPokeInteractor>(true) != null) return;

        var pokeGo = new GameObject("Poke Interactor");
        pokeGo.transform.SetParent(controller, false);
        pokeGo.transform.localPosition = new Vector3(0f, -0.01f, 0.09f);
        pokeGo.transform.localRotation = Quaternion.identity;
        var poke = pokeGo.AddComponent<XRPokeInteractor>();
        poke.interactionLayers = -1;
    }

    static Transform FindNamedChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindNamedChild(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    void EnsureWorldColliders()
    {
        // Cabin walls come from BuildCabinCage (box shell). Only seal the tunnel ENV here.
        var env = GameObject.Find("ENV");
        if (env != null)
            AddMissingColliders(env.transform);

        // Floor planes: walk + teleport only on these flat surfaces.
        foreach (var plane in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (plane == null || !plane.name.StartsWith("Plane"))
                continue;
            EnsureFloorCollider(plane.gameObject, teleportEnabled: false);
        }
    }

    static void AddMissingColliders(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var go = renderer.gameObject;
            if (go.GetComponent<XRGrabInteractable>() != null)
                continue;
            if (go.GetComponentInParent<XROrigin>() != null)
                continue;
            if (go.name.Contains("Floor1Poke") || go.name.Contains("ElevatorExitBarrier") ||
                go.name.Contains("ElevatorFloorTeleport") || go.name.Contains("ElevatorCabinCage") ||
                go.name.Contains("PokeTipVisual"))
                continue;
            if (go.GetComponent<Collider>() != null)
                continue;

            // Prefer boxes: FBX meshes here are often non-readable and MeshCollider fails silently.
            var box = go.AddComponent<BoxCollider>();
            box.center = renderer.localBounds.center;
            box.size = renderer.localBounds.size;
        }
    }

    void EnsureFloorCollider(GameObject floor, bool teleportEnabled)
    {
        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;
        floor.layer = groundLayer;

        if (floor.GetComponent<Collider>() == null)
        {
            var meshFilter = floor.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var meshCollider = floor.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
            }
            else
            {
                floor.AddComponent<BoxCollider>();
            }
        }

        var area = floor.GetComponent<TeleportationArea>();
        if (area == null)
            area = floor.AddComponent<TeleportationArea>();
        area.enabled = teleportEnabled;
        SyncTeleportLayers(area);
    }

    void BuildElevatorFloorTeleport()
    {
        if (elevatorRoot == null) return;
        if (elevatorFloorTeleport != null) return;

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;

        // Inset slightly from walls so destination validation does not go red.
        const float minX = -2.20f;
        const float maxX = 1.00f;
        const float minZ = -1.00f;
        const float maxZ = 1.10f;

        elevatorFloorTeleport = new GameObject("ElevatorFloorTeleport");
        elevatorFloorTeleport.layer = groundLayer;
        elevatorFloorTeleport.transform.SetParent(elevatorRoot, false);
        elevatorFloorTeleport.transform.localPosition = new Vector3(
            (minX + maxX) * 0.5f,
            0.04f,
            (minZ + maxZ) * 0.5f);
        elevatorFloorTeleport.transform.localRotation = Quaternion.identity;

        var box = elevatorFloorTeleport.AddComponent<BoxCollider>();
        box.size = new Vector3(maxX - minX, 0.12f, maxZ - minZ);
        box.center = Vector3.zero;

        var area = elevatorFloorTeleport.AddComponent<TeleportationArea>();
        area.enabled = true;
        area.interactionLayers = -1;
        ApplyGroundOnlyTeleportRays();
    }

    void BuildElevatorLobbyTeleport()
    {
        if (elevatorRoot == null) return;
        if (elevatorLobbyTeleport != null) return;

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;

        // Pad just outside the doors so the concrete apron is walkable + teleportable.
        elevatorLobbyTeleport = new GameObject("ElevatorLobbyTeleport");
        elevatorLobbyTeleport.layer = groundLayer;
        elevatorLobbyTeleport.transform.SetParent(elevatorRoot, false);
        elevatorLobbyTeleport.transform.localPosition = new Vector3(-0.95f, 0.04f, 2.35f);
        elevatorLobbyTeleport.transform.localRotation = Quaternion.identity;

        var box = elevatorLobbyTeleport.AddComponent<BoxCollider>();
        box.size = new Vector3(3.6f, 0.12f, 2.8f);
        box.center = Vector3.zero;

        var area = elevatorLobbyTeleport.AddComponent<TeleportationArea>();
        // Enabled after doors open — collider stays so feet have ground when the cabin arrives.
        area.enabled = false;
        area.interactionLayers = -1;
    }

    void ApplyGroundOnlyTeleportRays()
    {
        if (xrOrigin == null) return;

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;
        // Ground-only: Default walls/props were turning the reticle red before any valid pad.
        LayerMask teleportRayMask = 1 << groundLayer;

        foreach (var ray in xrOrigin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport"))
                continue;
            ray.raycastMask = teleportRayMask;
        }

        foreach (var provider in xrOrigin.GetComponentsInChildren<TeleportationProvider>(true))
            provider.enabled = true;

        if (elevatorFloorTeleport != null)
        {
            var area = elevatorFloorTeleport.GetComponent<TeleportationArea>();
            if (area != null) area.interactionLayers = -1;
        }

        if (elevatorLobbyTeleport != null)
        {
            var area = elevatorLobbyTeleport.GetComponent<TeleportationArea>();
            if (area != null) area.interactionLayers = -1;
        }
    }

    void SetWalkableDeckCollision(bool enabled)
    {
        var deck = GameObject.Find("Walkable Deck");
        if (deck == null) return;
        foreach (var col in deck.GetComponents<Collider>())
            col.enabled = enabled;
        deck.SetActive(enabled);
    }

    void SetOutsideTeleportEnabled(bool enabled)
    {
        var deck = GameObject.Find("Walkable Deck");
        if (deck != null)
        {
            var area = deck.GetComponent<TeleportationArea>();
            if (area != null) area.enabled = enabled;
            SetWalkableDeckCollision(enabled);
            // Replant footing, then force Ground-only rays again (EnsurePlaySpace adds Default).
            if (enabled)
            {
                BridgeXrPlaySetup.EnsurePlaySpace(xrOrigin);
                ApplyGroundOnlyTeleportRays();
            }
        }

        foreach (var plane in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (plane == null || !plane.name.StartsWith("Plane"))
                continue;
            var area = plane.GetComponent<TeleportationArea>();
            if (area != null)
            {
                area.enabled = enabled;
                area.interactionLayers = -1;
            }
            else if (enabled)
                EnsureFloorCollider(plane.gameObject, teleportEnabled: true);
        }

        if (elevatorLobbyTeleport != null)
        {
            var lobbyArea = elevatorLobbyTeleport.GetComponent<TeleportationArea>();
            if (lobbyArea != null)
            {
                lobbyArea.enabled = enabled;
                lobbyArea.interactionLayers = -1;
            }
        }

        if (elevatorFloorTeleport != null)
        {
            var cabinArea = elevatorFloorTeleport.GetComponent<TeleportationArea>();
            if (cabinArea != null)
            {
                cabinArea.enabled = true;
                cabinArea.interactionLayers = -1;
            }
        }

        ApplyGroundOnlyTeleportRays();
    }

    void SyncTeleportLayers(TeleportationArea area)
    {
        if (area == null) return;
        area.interactionLayers = -1;
        ApplyGroundOnlyTeleportRays();
    }

    void EnsureStaticTable()
    {
        var table = GameObject.Find("SM_Table");
        if (table == null) return;

        // Table must never be grabbable / movable.
        foreach (var grab in table.GetComponentsInChildren<XRGrabInteractable>(true))
            Destroy(grab);
        foreach (var body in table.GetComponentsInChildren<Rigidbody>(true))
            Destroy(body);

        // Non-readable FBX mesh colliders often fail — use a solid box instead.
        foreach (var meshCol in table.GetComponentsInChildren<MeshCollider>(true))
            meshCol.enabled = false;

        if (!TryFitBox(table, out var center, out var size))
        {
            center = Vector3.zero;
            size = new Vector3(1.2f, 0.8f, 0.7f);
        }

        // Slightly thicker top so the tablet rests reliably.
        size.y = Mathf.Max(size.y, 0.75f);

        var box = table.GetComponent<BoxCollider>();
        if (box == null) box = table.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = center;
        box.size = size;
    }

    void WireFloor1Button()
    {
        if (elevatorButton == null) return;

        elevatorButton.localPosition = buttonRestLocal;
        elevatorButtonRestLocal = buttonRestLocal;

        // Whole panel is pokeable so a finger tip can hit floor 1 easily.
        var panelCollider = elevatorButton.GetComponent<BoxCollider>();
        if (panelCollider == null)
        {
            panelCollider = elevatorButton.gameObject.AddComponent<BoxCollider>();
            if (TryFitBox(elevatorButton.gameObject, out var center, out var size))
            {
                panelCollider.center = center;
                panelCollider.size = Vector3.Scale(size, new Vector3(1.05f, 1.05f, 1.2f));
            }
            else
            {
                panelCollider.size = new Vector3(0.28f, 0.45f, 0.08f);
            }
        }

        floor1Poke = elevatorButton.Find("Floor1Poke");
        GameObject pokeGo;
        if (floor1Poke == null)
        {
            pokeGo = new GameObject("Floor1Poke");
            floor1Poke = pokeGo.transform;
            floor1Poke.SetParent(elevatorButton, false);
            floor1Poke.localPosition = floor1LocalPosition;
            floor1Poke.localRotation = Quaternion.identity;
            floor1Poke.localScale = Vector3.one;
        }
        else
        {
            pokeGo = floor1Poke.gameObject;
            floor1Poke.localPosition = floor1LocalPosition;
        }

        floor1PokeRestLocal = floor1Poke.localPosition;

        var box = pokeGo.GetComponent<BoxCollider>();
        if (box == null) box = pokeGo.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = floor1LocalSize;

        WirePokeInteractable(pokeGo, box);
        WirePokeInteractable(elevatorButton.gameObject, panelCollider);
    }

    void WirePokeInteractable(GameObject target, Collider pokeCollider)
    {
        var interactable = target.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = target.AddComponent<XRSimpleInteractable>();
        interactable.selectMode = InteractableSelectMode.Multiple;
        interactable.interactionLayers = -1;

        var pokeFilter = target.GetComponent<XRPokeFilter>();
        if (pokeFilter == null)
            pokeFilter = target.AddComponent<XRPokeFilter>();
        pokeFilter.pokeInteractable = interactable;
        pokeFilter.pokeCollider = pokeCollider;

        interactable.selectEntered.RemoveListener(OnFloorSelected);
        interactable.selectEntered.AddListener(OnFloorSelected);
        interactable.activated.RemoveListener(OnFloorActivated);
        interactable.activated.AddListener(OnFloorActivated);
        interactable.hoverEntered.RemoveListener(OnFloorHovered);
        interactable.hoverEntered.AddListener(OnFloorHovered);
    }

    void OnFloorHovered(HoverEnterEventArgs args)
    {
        // Finger poke often reports hover strongly; accept poke interactors as a press.
        if (args.interactorObject != null && args.interactorObject.transform.name.Contains("Poke"))
            TryOpenDoors();
    }

    void OnFloorSelected(SelectEnterEventArgs _) => TryOpenDoors();
    void OnFloorActivated(ActivateEventArgs _) => TryOpenDoors();

    void TryOpenDoors()
    {
        if (floorPressed) return;
        floorPressed = true;
        StartCoroutine(AnimateButtonPress());
        PulseHeadsetShake();
        StartCoroutine(RideThenOpenDoors());
    }

    IEnumerator AnimateButtonPress()
    {
        // Move the panel from rest → pressed local positions (Z pushes into the wall).
        var panel = elevatorButton;
        if (panel == null) yield break;

        var panelRest = buttonRestLocal;
        var panelPressed = buttonPressedLocal;
        panel.localPosition = panelRest;

        var elapsed = 0f;
        var half = Mathf.Max(0.02f, buttonPressSeconds * 0.5f);
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / half);
            panel.localPosition = Vector3.Lerp(panelRest, panelPressed, t);
            yield return null;
        }

        panel.localPosition = panelPressed;
    }

    void PulseHeadsetShake()
    {
        if (xrOrigin == null) return;
        var shake = xrOrigin.GetComponent<XRExplosionShake>();
        if (shake == null)
            shake = xrOrigin.gameObject.AddComponent<XRExplosionShake>();
        shake.ShakeFor(buttonShakeSeconds, buttonShakeStrength);
    }

    void MakeMilitaryTabletGrabbable()
    {
        var tablet = GameObject.Find("SM_Military_tablet");
        if (tablet == null) return;
        if (tablet.GetComponent<XRGrabInteractable>() != null) return;

        foreach (var child in tablet.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.isStatic = false;
            child.gameObject.layer = 0;
        }

        foreach (var meshCollider in tablet.GetComponentsInChildren<MeshCollider>(true))
            meshCollider.enabled = false;

        if (!TryFitBox(tablet, out var center, out var size))
        {
            center = Vector3.zero;
            size = new Vector3(0.22f, 0.02f, 0.3f);
        }

        EnsureStaticTable();
        var table = GameObject.Find("SM_Table");
        PlaceTabletOnTable(tablet, table);

        var box = tablet.GetComponent<BoxCollider>();
        if (box == null) box = tablet.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = center;
        box.size = size;

        var body = tablet.GetComponent<Rigidbody>();
        if (body == null) body = tablet.AddComponent<Rigidbody>();
        body.mass = 1.2f;
        // Rest on the table until grabbed — gravity was dropping it to the floor.
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = tablet.AddComponent<XRGrabInteractable>();
        grab.interactionLayers = -1;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.useDynamicAttach = true;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = true;
        grab.forceGravityOnDetach = true;
        // Only the tablet body is grabbable — LCD poke buttons stay separate for the other hand.
        grab.colliders.Clear();
        grab.colliders.Add(box);
        grab.selectEntered.AddListener(_ =>
        {
            body.isKinematic = false;
            body.useGravity = false;
        });
        grab.selectExited.AddListener(_ =>
        {
            body.isKinematic = false;
            body.useGravity = true;
        });

        AttachTabletLcdUi(tablet);
    }

    void AttachTabletLcdUi(GameObject tablet)
    {
        if (tablet == null) return;

        // Prefer a real scene UI object under the tablet so it can be moved in the editor
        // and grabbed/moved with the free hand at runtime.
        var lcd = tablet.GetComponentInChildren<MilitaryTabletLcdUi>(true);
        if (lcd == null)
        {
            var lcdGo = new GameObject("MilitaryTabletLcd", typeof(RectTransform), typeof(Canvas), typeof(MilitaryTabletLcdUi));
            lcdGo.transform.SetParent(tablet.transform, false);
            // Default on the LCD face — tweak Position/Rotation/Scale in the Scene view.
            lcdGo.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            lcdGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            lcdGo.transform.localScale = Vector3.one; // MilitaryTabletLcdUi sizes this to meters
            lcd = lcdGo.GetComponent<MilitaryTabletLcdUi>();
        }

        // Only supply textures for click swaps — do not touch the user's scene layout.
        lcd.Configure(
            tabletUiBackground,
            tabletOpt1Inactive, tabletOpt1Active,
            tabletOpt2Inactive, tabletOpt2Active,
            tabletOpt3Inactive, tabletOpt3Active);
    }

    static void PlaceTabletOnTable(GameObject tablet, GameObject table)
    {
        if (tablet == null) return;

        if (table == null)
        {
            tablet.transform.SetParent(null, true);
            return;
        }

        var bounds = new Bounds(table.transform.position, Vector3.one * 0.5f);
        var found = false;
        foreach (var renderer in table.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        // Rest on the tabletop; table collider (EnsureStaticTable) holds it after drop.
        tablet.transform.SetParent(null, true);
        var pos = tablet.transform.position;
        var tableTopY = bounds.max.y + 0.025f;
        if (pos.y < tableTopY + 0.05f)
            pos = new Vector3(bounds.center.x, tableTopY, bounds.center.z);
        tablet.transform.position = pos;
        tablet.transform.rotation = Quaternion.Euler(0f, 180f, 90f);
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
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = renderer.transform.TransformPoint(
                    localCenter + Vector3.Scale(extents, new Vector3(x, y, z)));
                var inRoot = root.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, inRoot);
                max = Vector3.Max(max, inRoot);
                found = true;
            }
        }

        if (!found) return false;
        center = (min + max) * 0.5f;
        size = max - min;
        return size.sqrMagnitude > 0.0001f;
    }

    IEnumerator AutoFadeIn()
    {
        if (blackHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(blackHoldSeconds);
        EnsureFadeOverlay(startBlack: false);
        yield return FadeTo(0f, fadeInSeconds);
    }

    IEnumerator RideThenOpenDoors()
    {
        // Cabin rises into the tunnel so the player clearly feels the elevator ride.
        if (elevatorRoot != null && rideSeconds > 0f)
        {
            var start = elevatorRoot.position;
            var end = elevatorRestPosition;
            var elapsed = 0f;
            while (elapsed < rideSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / rideSeconds));
                elevatorRoot.position = Vector3.Lerp(start, end, t);
                yield return null;
            }
            elevatorRoot.position = end;
        }

        var remaining = doorDelaySeconds - rideSeconds;
        if (remaining > 0f)
            yield return new WaitForSecondsRealtime(remaining);

        yield return AnimateDoorsOpen();

        if (exitBarrier != null)
            Destroy(exitBarrier);

        if (xrOrigin != null)
        {
            xrOrigin.transform.SetParent(null, true);
            BridgeXrPlaySetup.EnsurePlaySpace(xrOrigin);
        }

        SetOutsideTeleportEnabled(true);
    }

    IEnumerator AnimateDoorsOpen()
    {
        if (door01 == null || door02 == null) yield break;

        var start01 = door01.localPosition;
        var start02 = door02.localPosition;
        if (doorOpenSeconds <= 0f)
        {
            door01.localPosition = door01OpenLocal;
            door02.localPosition = door02OpenLocal;
            yield break;
        }

        var elapsed = 0f;
        while (elapsed < doorOpenSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / doorOpenSeconds));
            door01.localPosition = Vector3.Lerp(start01, door01OpenLocal, t);
            door02.localPosition = Vector3.Lerp(start02, door02OpenLocal, t);
            yield return null;
        }

        door01.localPosition = door01OpenLocal;
        door02.localPosition = door02OpenLocal;
    }

    void EnsureFadeOverlay(bool startBlack)
    {
        if (xrOrigin == null || xrOrigin.Camera == null) return;

        if (fadeOverlay == null)
        {
            var overlay = new GameObject("Elevator Intro Fade", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasGroup), typeof(Image));
            var rect = overlay.GetComponent<RectTransform>();
            rect.SetParent(xrOrigin.Camera.transform, false);
            var distance = Mathf.Max(xrOrigin.Camera.nearClipPlane + 0.03f, 0.1f);
            rect.localPosition = new Vector3(0f, 0f, distance);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * (distance * 0.005f);
            rect.sizeDelta = new Vector2(2000f, 2000f);

            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32767;

            var image = overlay.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            fadeOverlay = overlay.GetComponent<CanvasGroup>();
            fadeOverlay.blocksRaycasts = false;
            fadeOverlay.interactable = false;
        }

        if (startBlack) fadeOverlay.alpha = 1f;
    }

    IEnumerator FadeTo(float target, float duration)
    {
        if (fadeOverlay == null) yield break;

        var start = fadeOverlay.alpha;
        if (duration <= 0f)
        {
            fadeOverlay.alpha = target;
            yield break;
        }

        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        fadeOverlay.alpha = target;
    }

    void OnDestroy()
    {
        if (fadeOverlay != null)
            Destroy(fadeOverlay.gameObject);
    }
}

/// <summary>
/// Keeps controller-attached hand meshes visible and curls finger bones from grip/trigger.
/// </summary>
public sealed class ControllerHandFingerDriver : MonoBehaviour
{
    [SerializeField] bool leftHand;
    [SerializeField] float maxCurlDegrees = 60f;

    readonly List<Transform> fingerBones = new List<Transform>();
    readonly List<Quaternion> restRotations = new List<Quaternion>();
    readonly List<bool> isIndexBone = new List<bool>();
    Renderer[] renderers;
    UnityEngine.XR.InputDevice device;
    bool hasDevice;

    public void Bind(bool isLeft)
    {
        leftHand = isLeft;
        CacheBones();
        CacheDevice();
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnEnable()
    {
        CacheBones();
        CacheDevice();
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    void CacheBones()
    {
        fingerBones.Clear();
        restRotations.Clear();
        isIndexBone.Clear();

        var seen = new HashSet<Transform>();
        void Consider(Transform t)
        {
            if (t == null || !seen.Add(t)) return;
            var n = t.name.ToLowerInvariant();
            var isFinger =
                n.Contains("thumb") || n.Contains("index") || n.Contains("middle") ||
                n.Contains("ring") || n.Contains("pinky") || n.Contains("little") ||
                n.Contains("finger") || n.Contains("_ind_") || n.Contains("_mid_") ||
                n.Contains("_rin_") || n.Contains("_pin_") || n.Contains("_th_") ||
                n.Contains("index_") || n.Contains("middle_") || n.Contains("ring_") ||
                n.Contains("pinky_") || n.Contains("thumb_");
            if (!isFinger) return;
            // Skip tip leaves / mesh segments with no useful curl.
            if (n.Contains("_seg")) return;
            if (n.Contains("tip") && t.childCount == 0) return;

            fingerBones.Add(t);
            restRotations.Add(t.localRotation);
            isIndexBone.Add(n.Contains("index") || n.Contains("_ind_"));
        }

        foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.bones == null) continue;
            foreach (var bone in skin.bones)
                Consider(bone);
        }

        foreach (var t in GetComponentsInChildren<Transform>(true))
            Consider(t);
    }

    void CacheDevice()
    {
        var characteristics = UnityEngine.XR.InputDeviceCharacteristics.HeldInHand |
                              UnityEngine.XR.InputDeviceCharacteristics.Controller |
                              (leftHand
                                  ? UnityEngine.XR.InputDeviceCharacteristics.Left
                                  : UnityEngine.XR.InputDeviceCharacteristics.Right);
        var devices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
        hasDevice = devices.Count > 0;
        if (hasDevice) device = devices[0];
    }

    void LateUpdate()
    {
        // Tracking scripts can hide meshes every frame — force them back on.
        if (renderers != null)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (!renderers[i].enabled) renderers[i].enabled = true;
                if (!renderers[i].gameObject.activeSelf) renderers[i].gameObject.SetActive(true);
            }
        }

        if (fingerBones.Count == 0) return;
        if (!hasDevice) CacheDevice();
        if (!hasDevice) return;

        float trigger = 0f;
        float grip = 0f;
        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out trigger);
        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out grip);
        var indexCurl = Mathf.Clamp01(trigger);
        var otherCurl = Mathf.Clamp01(Mathf.Max(grip, trigger * 0.25f));

        for (var i = 0; i < fingerBones.Count; i++)
        {
            var bone = fingerBones[i];
            if (bone == null) continue;
            var amount = isIndexBone[i] ? indexCurl : otherCurl;
            // Local X curl matches most XR Hands / Quest hand hierarchies.
            var curlRot = Quaternion.Euler(amount * maxCurlDegrees, 0f, 0f);
            bone.localRotation = restRotations[i] * curlRot;
        }
    }
}
