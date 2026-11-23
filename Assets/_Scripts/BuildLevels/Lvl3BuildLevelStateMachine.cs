using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lvl3: tutorial two-button launch → auto salvo → drone orbit + kills two air units → path to tracker → lock-on kill.
/// </summary>
[AddComponentMenu("B1/Lvl3 Build Level State Machine")]
public sealed class Lvl3BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Idle",
        "Tutorial Rotate + Fire (2 units)",
        "Auto Launcher Salvo",
        "Drone Orbits",
        "Drone Shoots 2 Units",
        "Path To Tracker Device",
        "Pick Up Tracker",
        "Lock On Drone",
        "Drone Destroyed",
        "Complete"
    };

    [Header("Flow")]
    [SerializeField] bool autoRunSequence = true;
    [SerializeField] bool useTutorialOrchestrator = true;
    [SerializeField] bool suppressOrchestratorFinalLogo = true;

    [Header("Tutorial")]
    [SerializeField] MissileLauncherTutorialOrchestrator orchestrator;
    [SerializeField] MissileLauncherButtons buttons;
    [SerializeField] int airUnitsToTrack = 2;
    [Min(1f)] [SerializeField] float maxWaitTutorialFireSeconds = 180f;

    [Header("Auto launchers (MissileLauncherSystem in scene)")]
    [Min(0)] [SerializeField] int autoLauncherCount = 4;
    [Min(0f)] [SerializeField] float delayAfterTutorialBeforeSalvo = 3f;
    [Min(0f)] [SerializeField] float delayBetweenAutoLaunches = 0.45f;
    [Tooltip("Empty = all MissileLauncherSystem in scene, sorted by name (up to count).")]
    [SerializeField] MissileLauncherSystem[] autoLaunchers;

    [Header("Drone")]
    [SerializeField] string droneObjectName = "SM_Shahed149";
    [SerializeField] bool hideDroneUntilEngagement = true;
    [Min(0f)] [SerializeField] float delayBeforeDroneAppears = 2f;
    [Min(0f)] [SerializeField] float orbitBeforeShootingSeconds = 6f;
    [Min(0.5f)] [SerializeField] float delayBetweenDroneShots = 2f;
    [SerializeField] FighterJetFlight droneFlight;

    [Header("Tracker path")]
    [SerializeField] string[] trackerObjectNames = { "TrackerDevice", "TrackerWeapon", "SM_TrackerWeapon", "LaserWeapon", "SM_LaserWeapon" };
    [SerializeField] TrackerWeaponScope trackerScope;
    [SerializeField] bool showPathLikeLvl2 = true;
    [Min(0f)] [SerializeField] float pathStopBeforeDevice = 0.6f;

    readonly List<Transform> airUnits = new List<Transform>();
    FloorPathGuide pathGuide;
    Coroutine sequenceRoutine;
    bool sequenceRunning;
    XROrigin origin;

    public bool DroneDestroyed { get; private set; }

    protected override string[] StepNames => Steps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != BuildLevelScenes.Lvl3) return;
        if (FindAnyObjectByType<Lvl3BuildLevelStateMachine>() != null) return;
        var go = new GameObject("Lvl3 Build Level State Machine");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<Lvl3BuildLevelStateMachine>();
    }

    void Awake()
    {
        ResolveRefs();
        if (useTutorialOrchestrator && orchestrator != null)
            orchestrator.ConfigureForExternalSequence(true, suppressOrchestratorFinalLogo);
    }

    void Start()
    {
        ResolveRefs();
        PrepareDroneHidden();
        SetStep(0);
        if (autoRunSequence && Application.isPlaying)
            sequenceRoutine = StartCoroutine(RunFullSequence());
    }

    void ResolveRefs()
    {
        origin ??= FindAnyObjectByType<XROrigin>();
        orchestrator ??= FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
        buttons ??= FindAnyObjectByType<MissileLauncherButtons>();
        if (droneFlight == null)
        {
            var drone = FindByName(droneObjectName);
            if (drone != null)
                droneFlight = drone.GetComponent<FighterJetFlight>() ?? drone.GetComponentInChildren<FighterJetFlight>(true);
        }

        if (trackerScope == null)
        {
            foreach (var name in trackerObjectNames)
            {
                var go = FindByName(name);
                if (go == null) continue;
                trackerScope = go.GetComponent<TrackerWeaponScope>();
                if (trackerScope == null) trackerScope = go.GetComponentInChildren<TrackerWeaponScope>(true);
                if (trackerScope != null) break;
            }
        }
    }

    void PrepareDroneHidden()
    {
        if (!hideDroneUntilEngagement) return;
        var drone = FindByName(droneObjectName);
        if (drone != null) drone.SetActive(false);
    }

    public IEnumerator RunFullSequence()
    {
        if (sequenceRunning) yield break;
        sequenceRunning = true;
        airUnits.Clear();
        DroneDestroyed = false;

        ResolveRefs();
        if (useTutorialOrchestrator && orchestrator != null)
        {
            orchestrator.ConfigureForExternalSequence(true, suppressOrchestratorFinalLogo);
            SetStep(1);
            orchestrator.Begin();
            yield return WaitForTutorialFire();
        }
        else
            TrackStep(1);

        SetStep(2);
        yield return RunAutoLauncherSalvo();
        if (airUnits.Count < airUnitsToTrack)
            CaptureAirUnitsFromFlights();

        SetStep(3);
        yield return SpawnAndOrbitDrone();

        SetStep(4);
        yield return DroneDestroyAirUnits();

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
    }

    IEnumerator WaitForTutorialFire()
    {
        var elapsed = 0f;
        var sawFiring = false;
        while (elapsed < maxWaitTutorialFireSeconds)
        {
            orchestrator ??= FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
            if (orchestrator == null) yield break;

            var phase = orchestrator.Phase;
            if (phase == MissileLauncherTutorialOrchestrator.TutorialPhase.Firing)
                sawFiring = true;

            if (sawFiring && phase != MissileLauncherTutorialOrchestrator.TutorialPhase.Firing &&
                phase != MissileLauncherTutorialOrchestrator.TutorialPhase.PlayingFireAudio &&
                phase != MissileLauncherTutorialOrchestrator.TutorialPhase.ShowingFireCard &&
                phase != MissileLauncherTutorialOrchestrator.TutorialPhase.AwaitingFire)
            {
                CaptureAirUnitsFromFlights();
                yield break;
            }

            CaptureAirUnitsFromFlights();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    void CaptureAirUnitsFromFlights()
    {
        foreach (var missile in MissileProjectile.ActiveFlights)
        {
            if (missile == null) continue;
            var t = missile.transform;
            if (airUnits.Contains(t)) continue;
            airUnits.Add(t);
            if (airUnits.Count >= airUnitsToTrack) return;
        }
    }

    IEnumerator RunAutoLauncherSalvo()
    {
        if (delayAfterTutorialBeforeSalvo > 0f)
            yield return new WaitForSeconds(delayAfterTutorialBeforeSalvo);

        var systems = GatherAutoLaunchers();
        var count = Mathf.Min(autoLauncherCount, systems.Count);
        for (var i = 0; i < count; i++)
        {
            if (systems[i] != null) systems[i].Fire();
            if (i < count - 1 && delayBetweenAutoLaunches > 0f)
                yield return new WaitForSeconds(delayBetweenAutoLaunches);
        }
    }

    List<MissileLauncherSystem> GatherAutoLaunchers()
    {
        var list = new List<MissileLauncherSystem>();
        if (autoLaunchers != null && autoLaunchers.Length > 0)
        {
            foreach (var s in autoLaunchers)
                if (s != null && s.gameObject.scene.IsValid()) list.Add(s);
            return list;
        }

        foreach (var s in FindObjectsByType<MissileLauncherSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (s.gameObject.scene.IsValid()) list.Add(s);
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    IEnumerator SpawnAndOrbitDrone()
    {
        ResolveRefs();
        var droneRoot = FindByName(droneObjectName);
        if (droneRoot != null && hideDroneUntilEngagement)
            droneRoot.SetActive(true);

        if (delayBeforeDroneAppears > 0f)
            yield return new WaitForSeconds(delayBeforeDroneAppears);

        if (droneFlight != null)
        {
            droneFlight.StopFlying();
            droneFlight.StartFlying();
        }

        if (orbitBeforeShootingSeconds > 0f)
            yield return new WaitForSeconds(orbitBeforeShootingSeconds);
    }

    IEnumerator DroneDestroyAirUnits()
    {
        if (droneFlight == null) yield break;
        if (airUnits.Count < airUnitsToTrack)
            CaptureAirUnitsFromFlights();

        var shots = Mathf.Min(airUnitsToTrack, airUnits.Count);
        for (var i = 0; i < shots; i++)
        {
            var unit = airUnits[i];
            if (unit == null) continue;
            var done = false;
            droneFlight.FireMissileAt(unit, () =>
            {
                DestroyAirUnit(unit);
                done = true;
            });
            var timeout = 0f;
            while (!done && unit != null && timeout < 12f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }
            if (delayBetweenDroneShots > 0f)
                yield return new WaitForSeconds(delayBetweenDroneShots);
        }
    }

    static void DestroyAirUnit(Transform unit)
    {
        if (unit == null) return;
        foreach (var r in unit.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        foreach (var col in unit.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        var missile = unit.GetComponent<MissileProjectile>();
        if (missile != null) unit.gameObject.SetActive(false);
    }

    void ShowPathToTracker()
    {
        if (!showPathLikeLvl2) return;
        origin ??= FindAnyObjectByType<XROrigin>();
        var target = FindTrackerTransform();
        if (origin == null || target == null) return;

        if (pathGuide != null) Destroy(pathGuide.gameObject);
        var end = target.position;
        if (Physics.Raycast(end + Vector3.up * 0.2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            end = hit.point;

        var start = origin.transform.position;
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

    Transform FindTrackerTransform()
    {
        foreach (var name in trackerObjectNames)
        {
            var go = FindByName(name);
            if (go != null) return go.transform;
        }
        return trackerScope != null ? trackerScope.transform : null;
    }

    IEnumerator WaitForTrackerPickup()
    {
        ResolveRefs();
        while (trackerScope == null)
        {
            ResolveRefs();
            if (trackerScope == null) yield break;
            yield return null;
        }

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
        ResolveRefs();
        var drone = FindByName(droneObjectName);
        if (trackerScope == null || drone == null) return;
        trackerScope.AssignTarget(drone.transform);
    }

    IEnumerator WaitForDroneKill()
    {
        if (trackerScope == null) yield break;
        while (!trackerScope.TargetDestroyed)
            yield return null;
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying || sequenceRunning) return;
        ResolveRefs();
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
                sequenceRoutine = StartCoroutine(SpawnAndOrbitDrone());
                break;
            case 4:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(DroneDestroyAirUnits());
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

    static GameObject FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    public void EditorTestBeginSequence()
    {
        if (!Application.isPlaying) return;
        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = StartCoroutine(RunFullSequence());
    }

    public void EditorTestTutorialOnly() => EditorTestJumpToStep(1);
    public void EditorTestAutoSalvoOnly() => EditorTestJumpToStep(2);
    public void EditorTestDroneEngagement() => EditorTestJumpToStep(4);
    public void EditorTestShowTrackerPath() => EditorTestJumpToStep(5);
    public void EditorTestForceDroneKill() => EditorTestJumpToStep(8);
}