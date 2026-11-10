using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Lvl2 elevator intro: black vision fades after a short hold, doors stay closed,
/// hand visuals + poke on floor 1 unlock the cabin after a delay.
/// </summary>
public sealed class ElevatorDoorIntro : MonoBehaviour
{
    [Header("Scene refs (auto-found by name if empty)")]
    [SerializeField] Transform elevatorRoot;
    [SerializeField] Transform door01;
    [SerializeField] Transform door02;
    [SerializeField] Transform elevatorButton;
    [SerializeField] XROrigin xrOrigin;

    [Header("Timing")]
    [Min(0f)] [SerializeField] float blackHoldSeconds = 3f;
    [Min(0f)] [SerializeField] float fadeInSeconds = 1.25f;
    [Min(0f)] [SerializeField] float doorDelaySeconds = 5f;
    [Min(0.05f)] [SerializeField] float doorOpenSeconds = 1.5f;

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
    [SerializeField] Vector3 floor1LocalSize = new Vector3(0.08f, 0.08f, 0.06f);

    [Header("Button feedback")]
    [Min(0.05f)] [SerializeField] float buttonShakeSeconds = 1f;
    [Range(0.1f, 1f)] [SerializeField] float buttonShakeStrength = 0.85f;

    CanvasGroup fadeOverlay;
    readonly List<Behaviour> lockedLocomotion = new List<Behaviour>();
    GameObject exitBarrier;
    bool started;
    bool floorPressed;

    void Start()
    {
        if (started) return;
        started = true;

        ResolveRefs();
        if (placePlayerOnStart) PlacePlayerInElevator();

        CloseDoorsInstant();
        EnsureDoorColliders();
        BuildExitBarrier();
        LockLocomotion(true);
        PreferHandVisuals();
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

    void PreferHandVisuals()
    {
        if (xrOrigin == null) return;

        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            var name = child.name;
            if (name.Contains("Controller Visual") || name.Contains("Controller Model"))
                child.gameObject.SetActive(false);
        }

        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Tracked Hands" ||
                child.name == "Hand Visualizer" ||
                child.name == "Right Hand" ||
                child.name == "Left Hand")
                child.gameObject.SetActive(true);
        }
    }

    void LockLocomotion(bool locked)
    {
        if (xrOrigin == null) return;

        if (locked)
        {
            lockedLocomotion.Clear();
            foreach (var move in xrOrigin.GetComponentsInChildren<DynamicMoveProvider>(true))
            {
                if (!move.enabled) continue;
                move.enabled = false;
                lockedLocomotion.Add(move);
            }

            foreach (var teleport in xrOrigin.GetComponentsInChildren<TeleportationProvider>(true))
            {
                if (!teleport.enabled) continue;
                teleport.enabled = false;
                lockedLocomotion.Add(teleport);
            }

            foreach (var provider in xrOrigin.GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (provider is DynamicMoveProvider || provider is TeleportationProvider)
                    continue;
                if (!provider.enabled) continue;
                var typeName = provider.GetType().Name;
                if (typeName.Contains("Move") || typeName.Contains("Teleport") || typeName.Contains("Climb"))
                {
                    provider.enabled = false;
                    lockedLocomotion.Add(provider);
                }
            }
        }
        else
        {
            foreach (var behaviour in lockedLocomotion)
            {
                if (behaviour != null) behaviour.enabled = true;
            }
            lockedLocomotion.Clear();
        }
    }

    void WireFloor1Button()
    {
        if (elevatorButton == null) return;

        var poke = elevatorButton.Find("Floor1Poke");
        GameObject pokeGo;
        if (poke == null)
        {
            pokeGo = new GameObject("Floor1Poke");
            pokeGo.transform.SetParent(elevatorButton, false);
            pokeGo.transform.localPosition = floor1LocalPosition;
            pokeGo.transform.localRotation = Quaternion.identity;
            pokeGo.transform.localScale = Vector3.one;
        }
        else
        {
            pokeGo = poke.gameObject;
            poke.localPosition = floor1LocalPosition;
        }

        var box = pokeGo.GetComponent<BoxCollider>();
        if (box == null) box = pokeGo.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = floor1LocalSize;

        var interactable = pokeGo.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = pokeGo.AddComponent<XRSimpleInteractable>();

        // Allow both poke and grab/select so finger or controller tip can hit floor 1.
        interactable.selectMode = InteractableSelectMode.Multiple;

        var pokeFilter = pokeGo.GetComponent<XRPokeFilter>();
        if (pokeFilter == null)
            pokeFilter = pokeGo.AddComponent<XRPokeFilter>();
        pokeFilter.pokeInteractable = interactable;
        pokeFilter.pokeCollider = box;

        interactable.selectEntered.RemoveListener(OnFloorSelected);
        interactable.selectEntered.AddListener(OnFloorSelected);
        interactable.activated.RemoveListener(OnFloorActivated);
        interactable.activated.AddListener(OnFloorActivated);
    }

    void OnFloorSelected(SelectEnterEventArgs _) => TryOpenDoors();
    void OnFloorActivated(ActivateEventArgs _) => TryOpenDoors();

    void TryOpenDoors()
    {
        if (floorPressed) return;
        floorPressed = true;
        PulseHeadsetShake();
        StartCoroutine(OpenDoorsAfterDelay());
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

    IEnumerator OpenDoorsAfterDelay()
    {
        if (doorDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(doorDelaySeconds);

        yield return AnimateDoorsOpen();

        if (exitBarrier != null)
            Destroy(exitBarrier);

        LockLocomotion(false);
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
