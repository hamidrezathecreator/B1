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
    [SerializeField] HelicopterHover helicopterIntro;
    [SerializeField] Transform rightController;
    [SerializeField] Transform leftController;
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
    [Tooltip("Walking this far from the starting position advances to the grab instruction.")]
    [SerializeField] float teleportJumpDistance = 0.4f;

    [Min(0f)] [SerializeField] float doneMessageSeconds = 3f;

    enum Step { WaitingForStart, Transitioning, Move, Grab, Carry, Done }
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
    public event Action StartRequested;
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
        SetTeleport(false);
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
        XrBuildSceneLocomotion.EnableWalking(origin);
        SetTeleport(false);
        if (step == Step.WaitingForStart)
        {
            ShowBanner(startMessage);
            HighlightButton("Trigger");
        }
    }

    void OnStartPressed()
    {
        if (!ready || step != Step.WaitingForStart) return;
        if (StartRequested == null && helicopterIntro != null && helicopterIntro.IsRiding)
        {
            Debug.LogError("Lvl1 Tutorial: assign this tutorial to the level state machine for the helicopter fade transition.", this);
            return;
        }
        step = Step.Transitioning;
        startButton.interactable = false;
        HideBanner();
        ClearButtonHighlight();
        if (StartRequested != null) StartRequested.Invoke();
        else CompleteStartTransition();
    }

    public void CompleteStartTransition()
    {
        if (!ready || (step != Step.WaitingForStart && step != Step.Transitioning)) return;
        startButton.interactable = false;
        Started?.Invoke();
        BeginMoveStep();
    }

    public void CancelStartTransition()
    {
        if (!ready || step != Step.Transitioning) return;
        step = Step.WaitingForStart;
        startButton.interactable = true;
        ShowBanner(startMessage);
        HighlightButton("Trigger");
    }

    void BeginMoveStep()
    {
        step = Step.Move;
        XrBuildSceneLocomotion.EnableWalking(origin);
        SetTeleport(false);
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
        ClearButtonHighlight();
        origin.GetComponent<ControllerHandsPresentation>()?.ShowHands();
        ShowBanner(grabMessage);
    }

    void OnCaseGrabbed(SelectEnterEventArgs args)
    {
        if (!ready || (step != Step.Move && step != Step.Grab && step != Step.Carry)) return;
        if (args.interactorObject is XRSocketInteractor) return;
        var grabbed = args.interactableObject as XRGrabInteractable;
        if (grabbed == null || Array.IndexOf(pickupCases, grabbed) < 0 || MagnetDropZone.IsClaimed(grabbed)) return;
        if (step == Step.Move) BeginGrabStep();
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
        if (step != Step.Move || origin == null) return;

        var position = origin.transform.position;
        var moved = position - lastOriginPosition;
        moved.y = 0f;
        if (moved.magnitude >= teleportJumpDistance)
            BeginGrabStep();
    }

    void ShowBanner(string message)
    {
        HideBanner();
        if (origin.Camera == null) return;
        var controller = step == Step.Move ? leftController : rightController;
        if (controller == null) return;
        banner = HandHintBanner.Create(origin.Camera, font, message);
        banner.SetHand(controller);
    }

    void HideBanner()
    {
        if (banner != null) banner.Hide();
        banner = null;
    }

    void HighlightButton(params string[] partNames)
    {
        ClearButtonHighlight();
        var controller = step == Step.Move ? leftController : rightController;
        if (controller != null)
            buttonHighlight = ControllerButtonHighlight.Create(controller, partNames);
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
        if (origin == null) return;
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
        else if (step == Step.Move) BeginGrabStep();
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
