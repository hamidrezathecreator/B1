using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

[AddComponentMenu("B1/Lvl1 Tutorial")]
public sealed class Lvl1Tutorial : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] XROrigin origin;
    [SerializeField] Button startButton;
    [SerializeField] Transform rightController;
    [SerializeField] TMP_FontAsset font;
    [SerializeField] XRRayInteractor[] teleportRays = Array.Empty<XRRayInteractor>();

    [Header("Pickup and placement")]
    [SerializeField] XRGrabInteractable[] pickupCases = Array.Empty<XRGrabInteractable>();
    [Tooltip("Scene-authored drop zones. Their transforms define the final object position and rotation.")]
    [SerializeField] MagnetDropZone[] placementZones = Array.Empty<MagnetDropZone>();

    [Header("Instructions")]
    [SerializeField] string startMessage = "روی دکمه شروع بزن";
    [SerializeField] string teleportMessage = "با این استیک میتونی توی محیط حرکت کنی\nاستیک راست رو جلو بده، نشونه بگیر و ول کن";
    [SerializeField] string grabMessage = "حالا برو جعبه رو بردار\nدستت رو ببر نزدیکش و دکمه کناری (گریپ) رو نگه دار";
    [SerializeField] string carryMessage = "به سمت میز داخل غرفه برو و جعبه رو قرار بده\nنزدیک جای زرد که شدی ولش کن";
    [SerializeField] string doneMessage = "آفرین!";
    [Tooltip("A jump larger than this in one frame counts as a teleport.")]
    [SerializeField] float teleportJumpDistance = 0.4f;

    [Min(0f)] [SerializeField] float doneMessageSeconds = 3f;

    enum Step { WaitingForStart, Teleport, Grab, Carry, Done }
    Step step;
    HandHintBanner banner;
    Vector3 lastOriginPosition;
    GameObject marker;
    FloorPathGuide path;
    XRGrabInteractable carriedCase;
    ControllerButtonHighlight buttonHighlight;
    bool ready;

    public event Action PlacementCompleted;
    public event Action Started;
    public event Action CasePickedUp;
    public bool HasCompletedPlacement { get; private set; }
    public float CompletionMessageSeconds => doneMessageSeconds;
    public string EditorTutorialStepName => step.ToString();

    public bool HasValidAssignments
    {
        get
        {
            if (origin == null || startButton == null || pickupCases == null || pickupCases.Length == 0 ||
                placementZones == null || placementZones.Length == 0) return false;
            foreach (var pickup in pickupCases)
                if (pickup == null || pickup.GetComponent<Rigidbody>() == null || pickup.GetComponentInChildren<Collider>() == null)
                    return false;
            foreach (var zone in placementZones)
            {
                if (zone == null || !zone.HasAssignedObjects) return false;
                var acceptsPickup = false;
                foreach (var pickup in pickupCases)
                    acceptsPickup |= zone.Accepts(pickup);
                if (!acceptsPickup) return false;
            }
            return true;
        }
    }

    void Awake()
    {
        if (placementZones == null) return;
        foreach (var zone in placementZones)
            if (zone != null) zone.enabled = false;
    }

    IEnumerator Start()
    {
        if (!HasValidAssignments)
        {
            Debug.LogError("Lvl1 Tutorial: assign the XR Origin, Start button, pickup objects and placement zones in the scene. Placement cannot be skipped.", this);
            yield break;
        }

        foreach (var pickup in pickupCases)
        {
            pickup.selectEntered.AddListener(OnCaseGrabbed);
            pickup.selectExited.AddListener(OnCaseReleased);
        }
        foreach (var zone in placementZones)
            zone.Snapped += OnCaseSnapped;
        startButton.onClick.AddListener(OnStartPressed);
        ready = true;
        yield return null;
        PrepareTeleportFloor();
        XrBuildSceneLocomotion.EnableWalking(origin);
        SetTeleport(false);
        ShowBanner(startMessage);
        HighlightButton("Trigger");
    }

    void OnStartPressed()
    {
        if (!ready || step != Step.WaitingForStart) return;
        Started?.Invoke();
        BeginTeleportStep();
    }

    void BeginTeleportStep()
    {
        step = Step.Teleport;
        XrBuildSceneLocomotion.EnableWalking(origin);
        SetTeleport(true);
        ShowBanner(teleportMessage);
        HighlightButton("ThumbStick");
        lastOriginPosition = origin.transform.position;

        // Show where to go right away: the blue beacon on the case to pick up.
        if (marker == null)
        {
            var nearest = Nearest(pickupCases, origin.transform.position);
            if (nearest != null) marker = TutorialMarker.Create(nearest.transform);
        }
    }

    void BeginGrabStep()
    {
        step = Step.Grab;
        SetWalking(true);
        ShowBanner(grabMessage);
        // The universal controller's grip is the side "Bumper".
        HighlightButton("Grip", "Bumper");
    }

    void OnCaseGrabbed(SelectEnterEventArgs args)
    {
        if (!ready || (step != Step.Teleport && step != Step.Grab && step != Step.Carry)) return;
        if (args.interactorObject is XRSocketInteractor) return;
        var grabbed = args.interactableObject as XRGrabInteractable;
        if (grabbed == null || Array.IndexOf(pickupCases, grabbed) < 0 || MagnetDropZone.IsClaimed(grabbed)) return;
        carriedCase = grabbed;
        var firstTime = step != Step.Carry;
        step = Step.Carry;
        SetWalking(true);
        if (firstTime)
        {
            CasePickedUp?.Invoke();
            ShowBanner(carryMessage);
            ClearButtonHighlight();
            foreach (var zone in placementZones) zone.enabled = true;
        }
        MagnetDropZone target = null;
        var bestDistance = float.MaxValue;
        foreach (var zone in placementZones)
        {
            if (zone == null || zone.IsFilled || !zone.Accepts(carriedCase)) continue;
            var distance = (zone.transform.position - carriedCase.transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            target = zone;
            bestDistance = distance;
        }
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
        if (target == null) return;
        marker = TutorialMarker.Create(target.transform);
        path = BuildPath(origin.transform.position, target.transform.position);
    }

    void OnCaseSnapped(XRGrabInteractable grab)
    {
        if (step != Step.Carry || HasCompletedPlacement || Array.IndexOf(pickupCases, grab) < 0) return;
        var validPlacement = false;
        foreach (var zone in placementZones)
            validPlacement |= zone != null && zone.PlacedObject == grab;
        if (!validPlacement) return;
        HasCompletedPlacement = true;
        StartCoroutine(Finish());
    }

    IEnumerator Finish()
    {
        step = Step.Done;
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
        ClearButtonHighlight();
        ShowBanner(doneMessage);
        origin.GetComponent<ControllerHandsPresentation>()?.CompleteTutorialAfterDelay(doneMessageSeconds);
        PlacementCompleted?.Invoke();
        if (doneMessageSeconds > 0f) yield return new WaitForSecondsRealtime(doneMessageSeconds);
        HideBanner();
    }

    FloorPathGuide BuildPath(Vector3 from, Vector3 to)
    {
        var ignore = new List<Collider>();
        var end = to;

        // End on the floor in front of whatever the drop spot stands on (the table).
        if (Physics.Raycast(to + Vector3.up * 0.05f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            ignore.Add(hit.collider);
            var support = hit.collider.bounds;
            var edge = support.ClosestPoint(new Vector3(from.x, support.center.y, from.z));
            var away = new Vector3(from.x - edge.x, 0f, from.z - edge.z);
            if (away.sqrMagnitude > 0.0001f)
                end = edge + away.normalized * 0.45f;
        }

        if (carriedCase != null) ignore.AddRange(carriedCase.GetComponentsInChildren<Collider>());

        // Both ends on the floor the player stands on, never on a case or table top.
        var floorY = origin.transform.position.y;
        from.y = floorY;
        end.y = floorY;
        return FloorPathGuide.Create(new List<Vector3> { from, end }, origin.transform, ignore.ToArray());
    }

    void Update()
    {
        if (step != Step.Teleport || origin == null) return;

        // Walking is off in this step, so a one-frame jump is the teleport.
        var position = origin.transform.position;
        var moved = position - lastOriginPosition;
        moved.y = 0f;
        lastOriginPosition = position;
        if (moved.magnitude >= teleportJumpDistance)
            BeginGrabStep();
    }

    void ShowBanner(string message)
    {
        HideBanner();
        if (origin.Camera == null) return;
        if (rightController == null) return;
        banner = HandHintBanner.Create(origin.Camera, font, message);
        banner.SetHand(rightController);
    }

    void HideBanner()
    {
        if (banner != null) banner.Hide();
        banner = null;
    }

    void PrepareTeleportFloor()
    {
        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;
        LayerMask groundMask = 1 << groundLayer;

        var floorY = origin.transform.position.y;
        var center = origin.transform.position;
        const float radius = 70f;
        const float step = 1.5f;
        var floors = new HashSet<Collider>();

        for (var x = -radius; x <= radius; x += step)
        for (var z = -radius; z <= radius; z += step)
        {
            var top = new Vector3(center.x + x, floorY + 4f, center.z + z);
            var hits = Physics.RaycastAll(top, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            Collider best = null;
            // Tight enough that table tops (about 0.75 m up) are not taken as floor.
            var bestGap = 0.4f;
            foreach (var hit in hits)
            {
                if (hit.normal.y < 0.85f) continue;
                var gap = Mathf.Abs(hit.point.y - floorY);
                if (gap >= bestGap) continue;
                var col = hit.collider;
                if (col.GetComponentInParent<XROrigin>() != null) continue;
                if (col.GetComponentInParent<XRGrabInteractable>() != null) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                best = col;
                bestGap = gap;
            }
            if (best != null) floors.Add(best);
        }

        foreach (var col in floors)
        {
            col.gameObject.layer = groundLayer;
            if (col.GetComponentInParent<TeleportationArea>() != null) continue;
            var area = col.gameObject.AddComponent<TeleportationArea>();
            area.interactionLayers = -1;
            // Imported meshes are often rotated, so their local up is not world up.
            area.filterSelectionByHitNormal = false;
        }

        // Floor planes far below the bridge would drop the player off the deck.
        foreach (var area in FindObjectsByType<TeleportationArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var col = area.GetComponent<Collider>();
            if (col != null && col.bounds.max.y < floorY - 2f)
                area.enabled = false;
        }

        foreach (var ray in teleportRays)
        {
            if (ray == null) continue;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = groundMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }

        Debug.Log("Lvl1Tutorial: " + floors.Count + " floor surface(s) made teleportable.", this);
    }

    void HighlightButton(params string[] partNames)
    {
        ClearButtonHighlight();
        if (rightController != null)
            buttonHighlight = ControllerButtonHighlight.Create(rightController, partNames);
    }

    void ClearButtonHighlight()
    {
        if (buttonHighlight != null) Destroy(buttonHighlight.gameObject);
        buttonHighlight = null;
    }

    void OnCaseReleased(SelectExitEventArgs args)
    {
        var grab = args.interactableObject as XRGrabInteractable;
        if (grab == null || grab.isSelected || !grab.enabled) return;
        // A magnet snap takes over this case on the same release.
        if (MagnetDropZone.IsClaimed(grab)) return;
        var body = grab.GetComponent<Rigidbody>();
        if (body == null) return;
        body.isKinematic = false;
        body.useGravity = true;
    }

    void SetWalking(bool on)
    {
        foreach (var move in origin.GetComponentsInChildren<DynamicMoveProvider>(true))
            move.enabled = on;
    }

    void SetTeleport(bool on)
    {
        foreach (var provider in origin.GetComponentsInChildren<TeleportationProvider>(true))
            provider.enabled = on;
        // The starter rig toggles the ray GameObjects itself, so switch the component.
        foreach (var ray in teleportRays)
        {
            if (ray != null)
                ray.enabled = on;
        }
    }

    static XRGrabInteractable Nearest(IEnumerable<XRGrabInteractable> list, Vector3 from)
    {
        XRGrabInteractable best = null;
        var bestDistance = float.MaxValue;
        foreach (var grab in list)
        {
            if (grab == null) continue;
            var d = (grab.transform.position - from).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = grab;
        }
        return best;
    }

    public void EditorTestPressStart()
    {
        if (Application.isPlaying) OnStartPressed();
    }

    public void EditorTestAdvanceTutorialStep()
    {
        if (!Application.isPlaying || !ready) return;
        if (step == Step.WaitingForStart) OnStartPressed();
        else if (step == Step.Teleport) BeginGrabStep();
    }

    void OnDestroy()
    {
        if (startButton != null) startButton.onClick.RemoveListener(OnStartPressed);
        foreach (var pickup in pickupCases)
        {
            if (pickup == null) continue;
            pickup.selectEntered.RemoveListener(OnCaseGrabbed);
            pickup.selectExited.RemoveListener(OnCaseReleased);
        }
        foreach (var zone in placementZones)
            if (zone != null) zone.Snapped -= OnCaseSnapped;
        HideBanner();
        ClearButtonHighlight();
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
    }
}
