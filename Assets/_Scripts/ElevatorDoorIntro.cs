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
    [SerializeField] Vector3 floor1LocalSize = new Vector3(0.12f, 0.12f, 0.08f);
    [Tooltip("Local push into the panel so the press reads as going backward.")]
    [SerializeField] Vector3 buttonPressLocalOffset = new Vector3(0f, 0f, -0.015f);
    [Min(0.05f)] [SerializeField] float buttonPressSeconds = 0.18f;

    [Header("Button feedback")]
    [Min(0.05f)] [SerializeField] float buttonShakeSeconds = 1f;
    [Range(0.1f, 1f)] [SerializeField] float buttonShakeStrength = 0.85f;

    CanvasGroup fadeOverlay;
    GameObject exitBarrier;
    GameObject elevatorFloorTeleport;
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
        if (xrOrigin != null)
            BridgeXrPlaySetup.EnsurePlaySpace(xrOrigin);

        if (placePlayerOnStart) PlacePlayerInElevator();
        if (elevatorRoot != null)
        {
            elevatorRestPosition = elevatorRoot.position;
            // Start the cabin below the tunnel floor so the ride brings you "into" it.
            elevatorRoot.position = elevatorRestPosition + Vector3.down * rideHeight;
            if (xrOrigin != null)
                xrOrigin.transform.SetParent(elevatorRoot, true);
        }

        CloseDoorsInstant();
        EnsureDoorColliders();
        EnsureWorldColliders();
        BuildExitBarrier();
        BuildElevatorFloorTeleport();
        // Move + teleport work inside the cabin; outside floors stay off until doors open.
        SetOutsideTeleportEnabled(false);
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
        var worldPos = elevatorRoot.TransformPoint(playerLocalPosition);
        xrOrigin.transform.SetPositionAndRotation(worldPos, elevatorRoot.rotation);
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

        // Hide plastic controller meshes, keep tracking roots for poke/grab.
        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Contains("Controller Visual") && !child.name.Contains("UniversalController"))
                continue;

            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        // Soften the "dot + line" look while the hand meshes are up.
        foreach (var line in xrOrigin.GetComponentsInChildren<XRInteractorLineVisual>(true))
        {
            if (line.gameObject.name.Contains("Teleport"))
                continue;
            line.enabled = false;
        }

        AttachHandPrefab("Left Controller", leftHandVisualPrefab);
        AttachHandPrefab("Right Controller", rightHandVisualPrefab);

        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Tracked Hands" ||
                child.name == "Hand Visualizer" ||
                child.name == "Right Hand" ||
                child.name == "Left Hand")
                child.gameObject.SetActive(true);
        }
    }

    void AttachHandPrefab(string controllerName, GameObject prefab)
    {
        if (prefab == null || xrOrigin == null) return;

        Transform controller = null;
        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != controllerName) continue;
            controller = child;
            break;
        }

        if (controller == null) return;
        if (controller.Find(prefab.name) != null) return;

        var hand = Instantiate(prefab, controller, false);
        hand.name = prefab.name;
        hand.transform.localPosition = Vector3.zero;
        hand.transform.localRotation = Quaternion.identity;
        hand.transform.localScale = Vector3.one;
        hand.SetActive(true);

        // Controllers are held, so disable joint-tracking drivers that would zero the mesh.
        foreach (var behaviour in hand.GetComponentsInChildren<Behaviour>(true))
        {
            var typeName = behaviour.GetType().Name;
            if (typeName.Contains("XRHandSkeleton") || typeName.Contains("XRHandMesh") ||
                typeName.Contains("XRHandTracking"))
                behaviour.enabled = false;
        }

        // Drive a simple grip/trigger curl so fingers move even without bare-hand tracking.
        if (hand.GetComponent<ControllerHandFingerDriver>() == null)
            hand.AddComponent<ControllerHandFingerDriver>().Bind(controllerName.Contains("Left"));
    }

    void EnsureWorldColliders()
    {
        // Elevator shell + doors: solid so the body cannot walk through them.
        if (elevatorRoot != null)
            AddMissingColliders(elevatorRoot);

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
                go.name.Contains("ElevatorFloorTeleport"))
                continue;
            if (go.GetComponent<Collider>() != null)
                continue;

            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = renderer.localBounds.center;
                box.size = renderer.localBounds.size;
            }
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

        elevatorFloorTeleport = new GameObject("ElevatorFloorTeleport");
        elevatorFloorTeleport.layer = groundLayer;
        elevatorFloorTeleport.transform.SetParent(elevatorRoot, false);
        elevatorFloorTeleport.transform.localPosition = new Vector3(-0.9f, 0.02f, 0.15f);
        elevatorFloorTeleport.transform.localRotation = Quaternion.identity;

        var box = elevatorFloorTeleport.AddComponent<BoxCollider>();
        box.size = new Vector3(2.2f, 0.05f, 2.0f);
        box.center = Vector3.zero;

        var area = elevatorFloorTeleport.AddComponent<TeleportationArea>();
        area.enabled = true;
        SyncTeleportLayers(area);
    }

    void SetOutsideTeleportEnabled(bool enabled)
    {
        var deck = GameObject.Find("Walkable Deck");
        if (deck != null)
        {
            var area = deck.GetComponent<TeleportationArea>();
            if (area != null) area.enabled = enabled;
            // Keep the collider so feet still have a floor under the tunnel later.
            if (enabled)
                BridgeXrPlaySetup.EnsurePlaySpace(xrOrigin);
        }

        foreach (var plane in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (plane == null || !plane.name.StartsWith("Plane"))
                continue;
            var area = plane.GetComponent<TeleportationArea>();
            if (area != null) area.enabled = enabled;
            else if (enabled)
                EnsureFloorCollider(plane.gameObject, teleportEnabled: true);
        }
    }

    void SyncTeleportLayers(TeleportationArea area)
    {
        if (xrOrigin == null || area == null) return;
        foreach (var ray in xrOrigin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport"))
                continue;
            area.interactionLayers = ray.interactionLayers;
            return;
        }
    }

    void WireFloor1Button()
    {
        if (elevatorButton == null) return;

        elevatorButtonRestLocal = elevatorButton.localPosition;

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
        // Push the whole panel back a little so the press is obvious in VR.
        var panel = elevatorButton;
        var poke = floor1Poke;
        if (panel == null && poke == null) yield break;

        var panelRest = panel != null ? elevatorButtonRestLocal : Vector3.zero;
        var pokeRest = poke != null ? floor1PokeRestLocal : Vector3.zero;
        var panelPressed = panelRest + buttonPressLocalOffset;
        var pokePressed = pokeRest + buttonPressLocalOffset;

        var elapsed = 0f;
        var half = Mathf.Max(0.02f, buttonPressSeconds * 0.5f);
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / half);
            if (panel != null) panel.localPosition = Vector3.Lerp(panelRest, panelPressed, t);
            if (poke != null) poke.localPosition = Vector3.Lerp(pokeRest, pokePressed, t);
            yield return null;
        }

        if (panel != null) panel.localPosition = panelPressed;
        if (poke != null) poke.localPosition = pokePressed;
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
        if (tablet.GetComponentInParent<XRGrabInteractable>() != null) return;

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

        tablet.transform.SetParent(null, true);

        var box = tablet.GetComponent<BoxCollider>();
        if (box == null) box = tablet.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = center;
        box.size = size;

        var body = tablet.GetComponent<Rigidbody>();
        if (body == null) body = tablet.AddComponent<Rigidbody>();
        body.mass = 1.2f;
        body.useGravity = true;
        body.isKinematic = false;
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
            xrOrigin.transform.SetParent(null, true);

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
/// Curls obvious finger bones from controller grip/trigger so the hand mesh feels alive
/// even when bare-hand tracking is not active.
/// </summary>
public sealed class ControllerHandFingerDriver : MonoBehaviour
{
    [SerializeField] bool leftHand;
    [SerializeField] float maxCurlDegrees = 55f;

    readonly List<Transform> fingerBones = new List<Transform>();
    readonly List<Quaternion> restRotations = new List<Quaternion>();
    UnityEngine.XR.InputDevice device;
    bool hasDevice;

    public void Bind(bool isLeft)
    {
        leftHand = isLeft;
        CacheBones();
        CacheDevice();
    }

    void OnEnable()
    {
        CacheBones();
        CacheDevice();
    }

    void CacheBones()
    {
        fingerBones.Clear();
        restRotations.Clear();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLowerInvariant();
            if (!(n.Contains("thumb") || n.Contains("index") || n.Contains("middle") ||
                  n.Contains("ring") || n.Contains("pinky") || n.Contains("little") ||
                  n.Contains("finger")))
                continue;
            fingerBones.Add(t);
            restRotations.Add(t.localRotation);
        }
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
        if (fingerBones.Count == 0) return;
        if (!hasDevice) CacheDevice();
        if (!hasDevice) return;

        float trigger = 0f;
        float grip = 0f;
        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out trigger);
        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out grip);
        var curl = Mathf.Clamp01(Mathf.Max(trigger, grip));
        var angle = curl * maxCurlDegrees;
        var curlRot = Quaternion.Euler(angle, 0f, 0f);
        for (var i = 0; i < fingerBones.Count; i++)
        {
            var bone = fingerBones[i];
            if (bone == null) continue;
            bone.localRotation = restRotations[i] * curlRot;
        }
    }
}
