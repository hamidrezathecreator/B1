using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
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
    [Tooltip("Off: the normal controller models are shown instead of hands.")]
    [SerializeField] bool showHandsOnControllers;
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("Wrist position on the controller (right hand; left is mirrored on X).")]
    [SerializeField] Vector3 handOnControllerPosition = new Vector3(0.025f, -0.015f, -0.06f);
    [Tooltip("Extra wrist rotation on the controller, degrees.")]
    [SerializeField] Vector3 handOnControllerEuler = Vector3.zero;
    [SerializeField] TMP_FontAsset handHintFont;
    [SerializeField] string elevatorHintMessage = "روی دکمه آسانسور کلیک کن";

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

    [Header("Floor path to table (shown when doors open)")]
    [SerializeField] bool showPathToTable = true;
    [Tooltip("Optional empty objects to route the path (in order). Empty = straight out of the doors, then sideways to the table.")]
    [SerializeField] Transform[] pathWaypoints;
    [Tooltip("Path start, elevator local space (just outside the doors).")]
    [SerializeField] Vector3 pathStartLocal = new Vector3(-0.95f, 0f, 1.6f);
    [Min(0f)] [SerializeField] float pathStopBeforeTable = 0.45f;
    [Tooltip("How much of the forward distance to the table is walked straight before the bend.")]
    [Range(0.1f, 0.9f)] [SerializeField] float pathStraightFraction = 0.5f;

    [Header("Sound")]
    [Tooltip("Plays from the start, looped, until the cabin arrives.")]
    [SerializeField] AudioClip elevatorSound;
    [Range(0f, 1f)] [SerializeField] float elevatorVolume = 1f;
    [Tooltip("Tunnel music, faded in when the cabin arrives. A scene Audio Source already using this clip is reused.")]
    [SerializeField] AudioClip tunnelMusic;
    [Range(0f, 1f)] [SerializeField] float tunnelVolume = 0.5f;
    [Min(0f)] [SerializeField] float musicCrossfadeSeconds = 1.5f;

    [Header("After all three tablet options are checked")]
    [SerializeField] string nextSceneName = "LVL3_MissileLaunch";
    [Min(0f)] [SerializeField] float delayAfterClicks = 5f;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 0.8f;

    CanvasGroup fadeOverlay;
    Coroutine nextSceneRoutine;
    bool loadingNextScene;
    HandHintBanner handHint;
    GameObject exitBarrier;
    GameObject elevatorFloorTeleport;
    GameObject elevatorLobbyTeleport;
    Transform floor1Poke;
    Vector3 floor1PokeRestLocal;
    Vector3 elevatorButtonRestLocal;
    Vector3 elevatorRestPosition;
    bool started;
    bool floorPressed;
    AudioSource elevatorAudio;
    AudioSource tunnelAudio;

    void Start()
    {
        if (started) return;
        started = true;

        ResolveRefs();
        StartElevatorSound();
        // Do not plant to tunnel floor / BridgeDeckLock here — that left the player
        // floating high while the cabin dropped for the ride.
        if (xrOrigin != null)
            BridgeXrPlaySetup.PrepareElevatorPlaySpace(xrOrigin);

        if (elevatorRoot != null)
        {
            elevatorRestPosition = elevatorRoot.position;
            // Start above the tunnel floor so the ride brings the cabin down.
            elevatorRoot.position = elevatorRestPosition + Vector3.up * rideHeight;
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
        MilitaryTabletLcdUi.ChecklistChanged += OnTabletChecklistChanged;
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
            xrOrigin.CameraFloorOffsetObject.transform.localPosition =
                new Vector3(offset.x, BridgeXrPlaySetup.ExtraCameraLift, offset.z);
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

        // Lvl2 uses the Hands Interaction Demo XR Origin Hands (XR Rig) prefab for hand
        // tracking + locomotion. Do not rebuild ProjectHand meshes or strip XR Hands drivers
        // — that broke the demo hands and parked them on the floor.
        var old = GetComponent<XrHandsRig>();
        if (old != null) Destroy(old);

        // Tablet LCD still needs a fingertip sphere; drive it from the demo hand poke.
        StartCoroutine(FollowDemoHandPoke());
        ShowElevatorHint();
    }

    IEnumerator FollowDemoHandPoke()
    {
        TabletFingerTip tip = null;
        Transform poke = null;
        while (xrOrigin != null)
        {
            if (poke == null)
            {
                foreach (var t in xrOrigin.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (!t.name.Contains("Poke")) continue;
                    // Prefer the right-hand poke under "Right Hand" / "Right Controller".
                    var underRight = false;
                    for (var p = t; p != null; p = p.parent)
                    {
                        if (p.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            underRight = true;
                            break;
                        }
                    }
                    if (!underRight) continue;
                    poke = t;
                    break;
                }
            }

            if (poke != null)
            {
                if (tip == null) tip = TabletFingerTip.Create("RightFingerTipTouch");
                tip.MoveTo(poke.position);
            }
            else if (tip != null)
            {
                tip.Park();
            }

            yield return null;
        }
    }

    void ShowElevatorHint()
    {
        if (handHint != null || xrOrigin == null || xrOrigin.Camera == null) return;
        var hand = FindNamedTransform("Right Hand");
        if (hand == null) hand = FindNamedTransform("Right Controller");
        if (hand == null) return;
        handHint = HandHintBanner.Create(xrOrigin.Camera, handHintFont, elevatorHintMessage);
        handHint.SetHand(hand);
    }

    Transform FindNamedTransform(string targetName) => XrHandsRig.FindNamed(xrOrigin, targetName);

    void OnTabletChecklistChanged(bool allChecked)
    {
        if (loadingNextScene) return;
        if (!allChecked)
        {
            if (nextSceneRoutine != null)
            {
                StopCoroutine(nextSceneRoutine);
                nextSceneRoutine = null;
            }
            return;
        }

        if (nextSceneRoutine == null)
            nextSceneRoutine = StartCoroutine(LoadNextSceneAfterClicks());
    }

    IEnumerator LoadNextSceneAfterClicks()
    {
        if (delayAfterClicks > 0f)
            yield return new WaitForSecondsRealtime(delayAfterClicks);

        loadingNextScene = true;
        EnsureFadeOverlay(startBlack: false);
        yield return FadeTo(1f, fadeOutSeconds);

        if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning("ElevatorDoorIntro: next scene '" + nextSceneName + "' is not in Build Settings.");
            loadingNextScene = false;
            nextSceneRoutine = null;
            yield break;
        }

        SceneManager.LoadScene(nextSceneName);
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

        // Full cabin interior. A smaller inset left the edges hitting other Ground
        // meshes (Plane tiles), so those spots rejected the teleport ray.
        const float minX = -2.55f;
        const float maxX = 1.35f;
        const float minZ = -1.40f;
        const float maxZ = 1.45f;
        const float topY = 0.04f;
        const float thickness = 0.03f;

        elevatorFloorTeleport = new GameObject("ElevatorFloorTeleport");
        elevatorFloorTeleport.layer = groundLayer;
        elevatorFloorTeleport.transform.SetParent(elevatorRoot, false);
        elevatorFloorTeleport.transform.localPosition = new Vector3(
            (minX + maxX) * 0.5f,
            topY - thickness * 0.5f,
            (minZ + maxZ) * 0.5f);
        elevatorFloorTeleport.transform.localRotation = Quaternion.identity;

        var box = elevatorFloorTeleport.AddComponent<BoxCollider>();
        box.size = new Vector3(maxX - minX, thickness, maxZ - minZ);
        box.center = Vector3.zero;

        var area = elevatorFloorTeleport.AddComponent<TeleportationArea>();
        area.enabled = true;
        area.interactionLayers = -1;
        area.filterSelectionByHitNormal = false;
        KeepCabinFloorAboveOtherGround();
        ApplyGroundOnlyTeleportRays();
    }

    /// <summary>
    /// Small Ground colliders inside the cabin steal the teleport ray on some spots.
    /// World planes stay put — the cabin pad sits above them and covers the whole floor.
    /// </summary>
    void KeepCabinFloorAboveOtherGround()
    {
        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) return;

        ClearGroundExceptPad(elevatorFloorTeleport, groundLayer);
        ClearGroundExceptPad(elevatorLobbyTeleport, groundLayer);
    }

    void ClearGroundExceptPad(GameObject padObject, int groundLayer)
    {
        if (padObject == null) return;
        var pad = padObject.GetComponent<Collider>();
        if (pad == null) return;

        var bounds = pad.bounds;
        var hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            padObject.transform.rotation,
            1 << groundLayer,
            QueryTriggerInteraction.Ignore);

        foreach (var col in hits)
        {
            if (col == null || col == pad) continue;
            if (elevatorFloorTeleport != null && col.transform.IsChildOf(elevatorFloorTeleport.transform))
                continue;
            if (elevatorLobbyTeleport != null && col.transform.IsChildOf(elevatorLobbyTeleport.transform))
                continue;

            // Only small surfaces lying inside the pad. The big tunnel floor planes reach far past
            // it; clearing them switched off teleport on the whole plane. Where they overlap, the
            // pad sits higher, so the ray meets the pad first anyway.
            var other = col.bounds;
            const float margin = 0.1f;
            var inside =
                other.min.x >= bounds.min.x - margin && other.max.x <= bounds.max.x + margin &&
                other.min.z >= bounds.min.z - margin && other.max.z <= bounds.max.z + margin;
            if (!inside) continue;

            col.gameObject.layer = 0;
            var otherArea = col.GetComponent<TeleportationArea>();
            if (otherArea != null) otherArea.enabled = false;
        }
    }

    /// <summary>
    /// Tunnel floor planes must stay walkable and teleportable once the doors are open,
    /// including any an earlier pass took off the Ground layer.
    /// </summary>
    void RestoreTunnelFloors(int groundLayer)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t == null || !t.name.StartsWith("Plane")) continue;
            var col = t.GetComponent<Collider>();
            if (col == null) continue;
            if (elevatorLobbyTeleport != null && IsInsidePad(col, elevatorLobbyTeleport)) continue;
            if (elevatorFloorTeleport != null && IsInsidePad(col, elevatorFloorTeleport)) continue;

            t.gameObject.layer = groundLayer;
            col.enabled = true;
            var area = t.GetComponent<TeleportationArea>();
            if (area == null) area = t.gameObject.AddComponent<TeleportationArea>();
            area.enabled = true;
            area.interactionLayers = -1;
        }
    }

    static bool IsInsidePad(Collider col, GameObject padObject)
    {
        var pad = padObject.GetComponent<Collider>();
        if (pad == null) return false;
        var b = pad.bounds;
        var o = col.bounds;
        const float margin = 0.1f;
        return o.min.x >= b.min.x - margin && o.max.x <= b.max.x + margin &&
               o.min.z >= b.min.z - margin && o.max.z <= b.max.z + margin;
    }

    void BuildElevatorLobbyTeleport()
    {
        if (elevatorRoot == null) return;
        if (elevatorLobbyTeleport != null) return;

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;

        // Apron in front of the doors, between the racks. Sits above the floor
        // planes so the teleport ray hits this pad instead of a hole in the mesh.
        elevatorLobbyTeleport = new GameObject("ElevatorLobbyTeleport");
        elevatorLobbyTeleport.layer = groundLayer;
        elevatorLobbyTeleport.transform.SetParent(elevatorRoot, false);
        // Top at 0.07: just above the visible tunnel floor planes (0.064), not hidden under them.
        elevatorLobbyTeleport.transform.localPosition = new Vector3(-0.9f, 0.055f, 8.2f);
        elevatorLobbyTeleport.transform.localRotation = Quaternion.identity;

        var box = elevatorLobbyTeleport.AddComponent<BoxCollider>();
        box.size = new Vector3(12f, 0.03f, 13.4f);
        box.center = Vector3.zero;

        var area = elevatorLobbyTeleport.AddComponent<TeleportationArea>();
        // Enabled after doors open — collider stays so feet have ground when the cabin arrives.
        area.enabled = false;
        area.interactionLayers = -1;
        area.filterSelectionByHitNormal = false;
        KeepCabinFloorAboveOtherGround();
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
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = teleportRayMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
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

        // Mesh + tall Box together eject the tablet (falls, then anything snaps it back).
        // One thin tabletop slab only.
        foreach (var meshCol in table.GetComponentsInChildren<MeshCollider>(true))
            Destroy(meshCol);

        LiftPropOntoFloor(table);

        if (!TryRendererBounds(table, out var world))
            world = new Bounds(table.transform.position + Vector3.up * 0.4f, new Vector3(1.2f, 0.8f, 0.7f));

        const float topThickness = 0.05f;
        var worldCenter = new Vector3(world.center.x, world.max.y - topThickness * 0.5f, world.center.z);
        var worldSize = new Vector3(
            Mathf.Max(0.4f, world.size.x * 0.95f),
            topThickness,
            Mathf.Max(0.4f, world.size.z * 0.95f));

        var box = table.GetComponent<BoxCollider>();
        if (box == null) box = table.AddComponent<BoxCollider>();
        box.enabled = true;
        box.isTrigger = false;
        SetBoxFromWorld(box, worldCenter, worldSize);
        Physics.SyncTransforms();
    }

    static void SetBoxFromWorld(BoxCollider box, Vector3 worldCenter, Vector3 worldSize)
    {
        var t = box.transform;
        box.center = t.InverseTransformPoint(worldCenter);
        var s = t.lossyScale;
        box.size = new Vector3(
            worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
            worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
    }

    /// <summary>
    /// Raise a prop until its rendered bottom sits on the floor under it (stops legs clipping
    /// through Plane floors when the pivot was authored at y≈0).
    /// </summary>
    static void LiftPropOntoFloor(GameObject prop)
    {
        if (prop == null || !TryRendererBounds(prop, out var bounds)) return;

        var origin = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
        var hits = Physics.RaycastAll(origin, Vector3.down, bounds.size.y + 4f, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform.IsChildOf(prop.transform) || prop.transform.IsChildOf(hit.collider.transform))
                continue;
            // Only lift when the mesh is clearly through the floor.
            var delta = hit.point.y - bounds.min.y;
            if (delta > 0.01f && delta < 2.5f)
            {
                prop.transform.position += Vector3.up * delta;
                Physics.SyncTransforms();
            }
            return;
        }
    }

    static bool TryRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
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
        ButtonClickSound.Play();
        if (handHint != null)
        {
            handHint.Hide();
            handHint = null;
        }
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

        // An old XRGrabInteractable from a previous setup: replace it with the tablet grab.
        var oldGrab = tablet.GetComponent<XRGrabInteractable>();
        if (oldGrab != null && oldGrab is not TabletGrabInteractable)
            Destroy(oldGrab);

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

        var box = tablet.GetComponent<BoxCollider>();
        if (box == null) box = tablet.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = center;
        // Thin tablets slip into the floor / table and get stuck; keep a usable grab thickness.
        var scale = tablet.transform.lossyScale;
        size.x = Mathf.Max(size.x, 0.04f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)));
        size.y = Mathf.Max(size.y, 0.035f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)));
        size.z = Mathf.Max(size.z, 0.04f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        box.size = size;

        // Held in front of the chest the tablet hit the player's own body capsule and jittered or dropped.
        if (xrOrigin != null)
        {
            foreach (var own in xrOrigin.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(box, own, true);
        }

        var body = tablet.GetComponent<Rigidbody>();
        if (body == null) body = tablet.AddComponent<Rigidbody>();
        body.mass = 1.2f;
        body.linearDamping = 0.4f;
        body.angularDamping = 2.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.None;

        // Sit still on the table until grabbed. No gravity / rescue loop (that was the fall→snap).
        PlaceTabletOnTable(tablet, table);
        Physics.SyncTransforms();
        SitOnTableTop(tablet, box, table);
        body.isKinematic = true;
        body.useGravity = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        var grab = tablet.GetComponent<TabletGrabInteractable>();
        if (grab == null) grab = tablet.AddComponent<TabletGrabInteractable>();
        grab.interactionLayers = -1;
        grab.selectMode = InteractableSelectMode.Multiple;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.useDynamicAttach = true;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = false;
        // We control gravity ourselves — XR's forceGravityOnDetach fights the table soft-drop.
        grab.forceGravityOnDetach = false;
        grab.colliders.Clear();
        grab.colliders.Add(box);
        NearOnlyGrabFilter.AddTo(grab, 0.35f);
        grab.selectEntered.RemoveAllListeners();
        grab.selectExited.RemoveAllListeners();
        grab.selectEntered.AddListener(_ =>
        {
            body.isKinematic = false;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.None;
        });
        grab.selectExited.AddListener(_ =>
        {
            if (grab.isSelected) return;
            body.constraints = RigidbodyConstraints.None;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            // Released into / over the table: Instantaneous grab leaves it inside the slab and
            // physics ejects it sideways. Soft-seat on the top instead.
            var tableNow = GameObject.Find("SM_Table");
            if (tableNow != null && IsAboveTable(box, tableNow))
            {
                SitOnTableTop(tablet, box, tableNow);
                body.isKinematic = true;
                body.useGravity = false;
                return;
            }

            // Away from the table — fall onto the floor.
            body.isKinematic = false;
            body.useGravity = true;
        });

        AttachTabletLcdUi(tablet);
    }

    /// <summary>Puts the tablet's collider bottom flush on the table's top surface.</summary>
    static void SitOnTableTop(GameObject tablet, BoxCollider tabletBox, GameObject table)
    {
        if (tablet == null || tabletBox == null || table == null) return;
        if (!TryTableTop(table, out var top, out _)) return;

        Physics.SyncTransforms();
        var b = tabletBox.bounds;
        tablet.transform.position += Vector3.up * (top + 0.02f - b.min.y);
        Physics.SyncTransforms();
    }

    /// <summary>True when the tablet is over the tabletop (XZ) and near its height.</summary>
    static bool IsAboveTable(BoxCollider tabletBox, GameObject table)
    {
        if (tabletBox == null || table == null) return false;
        if (!TryTableTop(table, out var top, out var area)) return false;

        var c = tabletBox.bounds.center;
        const float margin = 0.1f;
        if (c.x < area.min.x - margin || c.x > area.max.x + margin) return false;
        if (c.z < area.min.z - margin || c.z > area.max.z + margin) return false;
        // From slightly through the top (held into the slab) up to about arm height above it.
        return c.y > top - 0.3f && c.y < top + 0.85f;
    }

    static bool TryTableTop(GameObject table, out float top, out Bounds area)
    {
        top = 0f;
        area = default;
        if (table == null) return false;

        // Prefer the visible mesh top — the solid BoxCollider is grown downward and its
        // centre can sit beside the real tabletop after lift / scale.
        if (TryRendererBounds(table, out area))
        {
            top = area.max.y;
            return true;
        }

        Collider chosen = null;
        foreach (var col in table.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            if (chosen == null || col is BoxCollider)
                chosen = col;
        }
        if (chosen == null) return false;
        area = chosen.bounds;
        top = area.max.y;
        return true;
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

        tablet.transform.SetParent(null, true);
        tablet.transform.rotation = Quaternion.Euler(0f, 180f, 90f);

        if (table == null) return;

        // Mesh bounds = real tabletop centre (not the grown solid box).
        if (!TryTableTop(table, out var top, out var area)) return;
        tablet.transform.position = new Vector3(area.center.x, top + 0.05f, area.center.z);
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
        // Cabin descends from above onto the tunnel floor.
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

        StartCoroutine(CrossfadeToTunnelMusic());

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
        KeepCabinFloorAboveOtherGround();
        var groundLayer = LayerMask.NameToLayer("Ground");
        RestoreTunnelFloors(groundLayer < 0 ? 0 : groundLayer);
        ApplyGroundOnlyTeleportRays();
        ShowPathToTable();
    }

    void ShowPathToTable()
    {
        if (!showPathToTable || elevatorRoot == null) return;
        var table = GameObject.Find("SM_Table");
        if (table == null) return;

        var start = elevatorRoot.TransformPoint(pathStartLocal);

        var bounds = new Bounds(table.transform.position, Vector3.zero);
        var hasBounds = false;
        foreach (var r in table.GetComponentsInChildren<Renderer>())
        {
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        var points = new List<Vector3> { start };
        var hasCustomRoute = false;
        if (pathWaypoints != null)
        {
            foreach (var w in pathWaypoints)
            {
                if (w == null) continue;
                points.Add(w.position);
                hasCustomRoute = true;
            }
        }

        if (!hasCustomRoute)
        {
            // Straight out of the doors for part of the way, then a soft bend toward the table.
            // The bend starts early so the path stays in the open floor, not along the wall.
            var forward = elevatorRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            var toTable = bounds.center - start;
            toTable.y = 0f;
            var forwardDist = Mathf.Max(0f, Vector3.Dot(toTable, forward));
            var straight = Mathf.Clamp(forwardDist * pathStraightFraction, 1.2f, Mathf.Max(1.2f, forwardDist - 1f));
            var corner = start + forward * straight;
            corner.y = start.y;

            var edge = bounds.ClosestPoint(new Vector3(corner.x, bounds.center.y, corner.z));
            var back = new Vector3(corner.x - edge.x, 0f, corner.z - edge.z);
            if (back.sqrMagnitude < 0.0001f) back = -forward;
            var end = edge + back.normalized * pathStopBeforeTable;
            end.y = start.y;

            points.Add(corner);
            // Quadratic bend: control point pulls the curve forward, like a drawn turn.
            var control = corner + forward * Mathf.Min(1.2f, Vector3.Distance(corner, end) * 0.35f);
            control.y = start.y;
            const int bendSteps = 8;
            for (var i = 1; i <= bendSteps; i++)
            {
                var t = i / (float)bendSteps;
                var a = Vector3.Lerp(corner, control, t);
                var b = Vector3.Lerp(control, end, t);
                points.Add(Vector3.Lerp(a, b, t));
            }
        }
        else
        {
            var from = points[points.Count - 1];
            var edge = bounds.ClosestPoint(new Vector3(from.x, bounds.center.y, from.z));
            var back = new Vector3(from.x - edge.x, 0f, from.z - edge.z);
            if (back.sqrMagnitude < 0.0001f) back = new Vector3(start.x - bounds.center.x, 0f, start.z - bounds.center.z);
            var end = edge + back.normalized * pathStopBeforeTable;
            end.y = start.y;
            points.Add(end);
        }

        // Visible floor: the hidden teleport pads and the tunnel floor planes are not at the same height.
        FloorPathGuide.Create(points, xrOrigin != null ? xrOrigin.transform : null, true,
            table.GetComponentsInChildren<Collider>());
    }

    void StartElevatorSound()
    {
        // The scene's tunnel music plays on awake; hold it back until the cabin arrives.
        if (tunnelMusic != null)
        {
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (source.clip != tunnelMusic && source.resource != tunnelMusic) continue;
                source.Stop();
                if (tunnelAudio == null) tunnelAudio = source;
                else source.enabled = false;
            }
        }

        if (elevatorSound == null) return;
        elevatorAudio = CreateMusicSource("Elevator Sound", elevatorSound);
        elevatorAudio.volume = elevatorVolume;
        elevatorAudio.Play();
    }

    IEnumerator CrossfadeToTunnelMusic()
    {
        if (tunnelMusic != null)
        {
            if (tunnelAudio == null) tunnelAudio = CreateMusicSource("Tunnel Music", tunnelMusic);
            tunnelAudio.resource = tunnelMusic;
            tunnelAudio.loop = true;
            tunnelAudio.volume = 0f;
            tunnelAudio.Play();
        }

        var elevatorStart = elevatorAudio != null ? elevatorAudio.volume : 0f;
        var elapsed = 0f;
        while (elapsed < musicCrossfadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / musicCrossfadeSeconds);
            if (elevatorAudio != null) elevatorAudio.volume = Mathf.Lerp(elevatorStart, 0f, t);
            if (tunnelAudio != null) tunnelAudio.volume = Mathf.Lerp(0f, tunnelVolume, t);
            yield return null;
        }

        if (elevatorAudio != null) elevatorAudio.Stop();
        if (tunnelAudio != null) tunnelAudio.volume = tunnelVolume;
    }

    AudioSource CreateMusicSource(string sourceName, AudioClip clip)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
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

    void OnValidate()
    {
        // Play mode: tune the hand position on the controller live from the inspector.
        if (!Application.isPlaying) return;
        var rig = GetComponent<XrHandsRig>();
        if (rig != null) rig.SetWristOffset(handOnControllerPosition, handOnControllerEuler);
    }

    void OnDestroy()
    {
        MilitaryTabletLcdUi.ChecklistChanged -= OnTabletChecklistChanged;
        if (fadeOverlay != null)
            Destroy(fadeOverlay.gameObject);
    }
}
