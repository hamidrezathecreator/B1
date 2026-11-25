using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Lvl3: two-button launch, auto salvo, destroy two units, drone orbit, tracker pickup and lock-on kill.
/// </summary>
[AddComponentMenu("B1/Lvl3 Build Level State Machine")]
public sealed class Lvl3BuildLevelStateMachine : BuildLevelStateMachineBase
{
    [System.Serializable]
    public sealed class AutomaticLaunch
    {
        public MissileProjectile projectile;
        public Transform destination;
    }

    static readonly string[] Steps =
    {
        "Idle",
        "Tutorial Launch Buttons",
        "Auto Launcher Salvo",
        "Drone Shoots 2 Units",
        "Drone Orbits",
        "Path To Tracker Device",
        "Pick Up Tracker",
        "Lock On Drone",
        "Drone Destroyed",
        "Complete"
    };

    [Header("Scene references")]
    [SerializeField] XROrigin origin;

    [Header("Flow")]
    [SerializeField] bool autoRunSequence = true;
    [SerializeField] bool suppressOrchestratorFinalLogo = true;

    [Header("Tutorial")]
    [SerializeField] MissileLauncherTutorialOrchestrator orchestrator;
    [SerializeField] ExplodeAndSwap droneStrikes;

    [Header("Automatic missile launches")]
    [Min(0f)] [SerializeField] float delayAfterTutorialBeforeSalvo = 3f;
    [Tooltip("Random wait between automatic missile launches, after the tutorial fire button is accepted.")]
    [Min(0f)] [SerializeField] float minDelayBetweenAutoLaunches = 0.35f;
    [Min(0f)] [SerializeField] float maxDelayBetweenAutoLaunches = 1.1f;
    [Tooltip("Scene missiles and their heading waypoints, launched directly by the state machine in this order.")]
    [SerializeField] AutomaticLaunch[] automaticLaunches;

    [Header("Drone")]
    [SerializeField] GameObject droneRoot;
    [Min(0f)] [SerializeField] float delayBeforeDroneAppears = 2f;
    [SerializeField] FighterJetFlight droneFlight;

    [Header("Completion")]
    [Min(0f)] [SerializeField] float delayBeforeReturnToLvl1 = 4f;

    [Header("Tracker path")]
    [SerializeField] Transform trackerPathTarget;
    [SerializeField] TrackerWeaponScope trackerScope;
    [SerializeField] bool showPathLikeLvl2 = true;
    [Min(0f)] [SerializeField] float pathStopBeforeDevice = 0.6f;

    FloorPathGuide pathGuide;
    Coroutine sequenceRoutine;
    bool sequenceRunning;
    bool automaticSalvoCompleted;

    public bool DroneDestroyed { get; private set; }

    protected override string[] StepNames => Steps;

    void Awake()
    {
        if (orchestrator != null)
            orchestrator.ConfigureForExternalSequence(true, suppressOrchestratorFinalLogo);
        PrepareDroneHidden();
        trackerScope?.SetEngagementEnabled(false);
    }

    void Start()
    {
        if (!HasValidAssignments)
        {
            Debug.LogError("Lvl3: assign the player, tutorial, ordered auto launchers, drone, tracker and path target in the scene.", this);
            enabled = false;
            return;
        }
        PrepareDroneHidden();
        SetStep(0);
        if (autoRunSequence && Application.isPlaying)
            sequenceRoutine = StartCoroutine(RunFullSequence());
    }

    public bool HasValidAssignments
    {
        get
        {
            if (origin == null || droneRoot == null || droneFlight == null || trackerScope == null ||
                trackerPathTarget == null || orchestrator == null || !orchestrator.HasValidAssignments ||
                droneStrikes == null || !droneStrikes.HasValidAssignments || automaticLaunches == null ||
                automaticLaunches.Length == 0) return false;
            var assigned = new HashSet<MissileProjectile>();
            foreach (var launch in automaticLaunches)
                if (launch == null || launch.projectile == null || launch.destination == null ||
                    !launch.projectile.gameObject.scene.IsValid() || !assigned.Add(launch.projectile)) return false;
            return true;
        }
    }

    void PrepareDroneHidden()
    {
        if (droneFlight != null) droneFlight.StopFlying();
        if (droneRoot != null) droneRoot.SetActive(false);
    }

    public IEnumerator RunFullSequence()
    {
        if (sequenceRunning || !HasValidAssignments) yield break;
        sequenceRunning = true;
        SequenceComplete = false;
        DroneDestroyed = false;
        PrepareDroneHidden();
        trackerScope.SetEngagementEnabled(false);

        orchestrator.ConfigureForExternalSequence(true, suppressOrchestratorFinalLogo);
        SetStep(1);
        orchestrator.Begin();
        yield return WaitForTutorialFire();
        if (orchestrator == null || !orchestrator.isActiveAndEnabled || !orchestrator.FireAccepted || orchestrator.Phase != MissileLauncherTutorialOrchestrator.TutorialPhase.Completed)
        {
            sequenceRunning = false;
            Debug.LogError("Lvl3: the tutorial fire button was not accepted; automatic launches remain blocked.", this);
            yield break;
        }

        SetStep(2);
        yield return RunAutoLauncherSalvo();
        if (!automaticSalvoCompleted)
        {
            sequenceRunning = false;
            yield break;
        }

        SetStep(3);
        PrepareDroneHidden();
        droneStrikes.Play();
        while (!droneStrikes.IsComplete) yield return null;

        SetStep(4);
        yield return SpawnAndOrbitDrone();

        SetStep(5);
        ShowPathToTracker();

        SetStep(6);
        yield return WaitForTrackerPickup();

        SetStep(7);
        WireTrackerToDrone();
        yield return WaitForDroneKill();

        SetStep(8);
        DroneDestroyed = true;
        SetStep(9);
        SequenceComplete = true;
        sequenceRunning = false;
        if (delayBeforeReturnToLvl1 > 0f)
            yield return new WaitForSeconds(delayBeforeReturnToLvl1);
        SceneTransitionRunner.Begin(BuildLevelScenes.Lvl1, true, 0f, 0.35f, 0f, 0.35f);
    }

    IEnumerator WaitForTutorialFire()
    {
        while (orchestrator != null && (!orchestrator.FireAccepted ||
            orchestrator.Phase != MissileLauncherTutorialOrchestrator.TutorialPhase.Completed))
        {
            if (!orchestrator.isActiveAndEnabled || orchestrator.Phase == MissileLauncherTutorialOrchestrator.TutorialPhase.Failed)
                yield break;
            yield return null;
        }
    }

    IEnumerator RunAutoLauncherSalvo()
    {
        automaticSalvoCompleted = false;
        if (delayAfterTutorialBeforeSalvo > 0f)
            yield return new WaitForSeconds(delayAfterTutorialBeforeSalvo);

        var count = automaticLaunches.Length;
        var gapMin = Mathf.Max(0f, Mathf.Min(minDelayBetweenAutoLaunches, maxDelayBetweenAutoLaunches));
        var gapMax = Mathf.Max(0f, Mathf.Max(minDelayBetweenAutoLaunches, maxDelayBetweenAutoLaunches));
        for (var i = 0; i < count; i++)
        {
            if (sequenceRunning && (orchestrator == null || !orchestrator.isActiveAndEnabled ||
                !orchestrator.FireAccepted || orchestrator.Phase == MissileLauncherTutorialOrchestrator.TutorialPhase.Failed))
                yield break;
            var launch = automaticLaunches[i];
            if (launch.projectile == null || launch.destination == null)
            {
                Debug.LogError("Lvl3: an automatic missile or its destination became unavailable.", this);
                yield break;
            }
            launch.projectile.Launch(launch.destination.position);
            if (!launch.projectile.HasLaunched || !launch.projectile.gameObject.activeInHierarchy)
            {
                Debug.LogError("Lvl3: an automatic missile could not launch.", this);
                yield break;
            }
            if (i < count - 1 && gapMax > 0f)
                yield return new WaitForSeconds(Random.Range(gapMin, gapMax));
        }
        automaticSalvoCompleted = true;
    }

    IEnumerator SpawnAndOrbitDrone()
    {
        if (delayBeforeDroneAppears > 0f)
            yield return new WaitForSeconds(delayBeforeDroneAppears);

        if (droneRoot != null) droneRoot.SetActive(true);
        if (droneFlight != null)
        {
            droneFlight.StopFlying();
            droneFlight.StartFlying();
        }

    }

    void ShowPathToTracker()
    {
        if (!showPathLikeLvl2) return;
        var target = trackerPathTarget;
        if (origin == null || target == null) return;

        if (pathGuide != null) Destroy(pathGuide.gameObject);
        var end = target.position;
        if (Physics.Raycast(end + Vector3.up * 0.2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            end = hit.point;

        var start = origin.Camera != null ? origin.Camera.transform.position : origin.transform.position;
        start.y = end.y;
        var forward = new Vector3(target.position.x - start.x, 0f, target.position.z - start.z);
        if (forward.sqrMagnitude > 0.01f)
        {
            forward.Normalize();
            end -= forward * pathStopBeforeDevice;
            end.y = start.y;
        }

        pathGuide = FloorPathGuide.Create(new List<Vector3> { start, end }, origin.transform, true);
    }

    IEnumerator WaitForTrackerPickup()
    {
        if (trackerScope == null) yield break;
        while (!trackerScope.IsHeld)
            yield return null;

        if (pathGuide != null)
        {
            Destroy(pathGuide.gameObject);
            pathGuide = null;
        }
    }

    void WireTrackerToDrone()
    {
        if (trackerScope == null || droneRoot == null) return;
        trackerScope.AssignTarget(droneRoot.transform);
        trackerScope.SetEngagementEnabled(true);
    }

    IEnumerator WaitForDroneKill()
    {
        if (trackerScope == null) yield break;
        while (!trackerScope.TargetDestroyed)
            yield return null;
    }

    protected override void PrepareForEditorStepEnter()
    {
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }
        sequenceRunning = false;
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying || sequenceRunning) return;
        switch (index)
        {
            case 1:
                orchestrator?.ConfigureForExternalSequence(true, suppressFinalLogo: suppressOrchestratorFinalLogo);
                orchestrator?.Begin();
                break;
            case 2:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(RunAutoLauncherSalvo());
                break;
            case 3:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                PrepareDroneHidden();
                droneStrikes?.Play();
                break;
            case 4:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(SpawnAndOrbitDrone());
                break;
            case 5:
                ShowPathToTracker();
                break;
            case 7:
                WireTrackerToDrone();
                break;
            case 8:
                trackerScope?.EditorTestForceTargetKill();
                break;
        }
    }

    void Update()
    {
        if (!Application.isPlaying || sequenceRunning) return;
        if (StepIndex == 6 && trackerScope != null && trackerScope.IsHeld)
            TrackStep(6);
        if (StepIndex == 7 && trackerScope != null && trackerScope.TargetDestroyed)
            TrackStep(8);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        sequenceRoutine = null;
        sequenceRunning = false;
    }

    public void EditorTestBeginSequence()
    {
        if (!Application.isPlaying) return;
        PrepareForEditorStepEnter();
        sequenceRoutine = StartCoroutine(RunFullSequence());
    }

    public void EditorTestTutorialOnly() => EditorTestJumpToStep(1);
    public void EditorTestAutoSalvoOnly() => EditorTestJumpToStep(2);
    public void EditorTestDroneEngagement() => EditorTestJumpToStep(3);
    public void EditorTestShowTrackerPath() => EditorTestJumpToStep(5);
    public void EditorTestForceDroneKill() => EditorTestJumpToStep(8);
}
