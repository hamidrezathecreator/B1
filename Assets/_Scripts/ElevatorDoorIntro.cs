using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public sealed class ElevatorDoorIntro : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] Transform elevatorRoot;
    [SerializeField] Transform elevatorArrival;
    [SerializeField] Transform playerSpawn;
    [SerializeField] Transform door01;
    [SerializeField] Transform door02;
    [SerializeField] Transform door01OpenPose;
    [SerializeField] Transform door02OpenPose;
    [SerializeField] Transform elevatorButton;
    [SerializeField] Transform buttonPressedPose;
    [SerializeField] XRSimpleInteractable[] floorButtons;
    [SerializeField] GameObject exitBarrier;
    [SerializeField] XrPlayBoundaryVolume elevatorBoundary;
    [SerializeField] XrPlayBoundaryVolume levelBoundary;
    [SerializeField] TeleportationArea[] outsideTeleportAreas;
    [SerializeField] XROrigin xrOrigin;
    [SerializeField] Transform rightController;
    [SerializeField] XRExplosionShake buttonShake;
    [SerializeField] TabletGrabInteractable tabletGrab;
    [SerializeField] MilitaryTabletLcdUi tabletUi;
    [SerializeField] BoxCollider tabletCollider;
    [SerializeField] Rigidbody tabletBody;
    [SerializeField] GameObject table;
    [SerializeField] CanvasGroup fadeOverlay;
    [SerializeField] AudioSource elevatorAudio;
    [SerializeField] AudioSource tunnelAudio;
    [Header("Elevator button order voice")]
    [Tooltip("Played once after an accepted floor-button press, alongside the elevator ride.")]
    [SerializeField] AudioSource floorButtonOrderVoice;
    [Header("Hint")]
    [SerializeField] TMP_FontAsset handHintFont;
    [SerializeField] string elevatorHintMessage = "روی دکمه آسانسور کلیک کن";
    [Header("Timing")]
    [Min(0f)] [SerializeField] float blackHoldSeconds = 3f;
    [Min(0f)] [SerializeField] float fadeInSeconds = 1.25f;
    [Min(0f)] [SerializeField] float doorDelaySeconds = 5f;
    [Min(0.05f)] [SerializeField] float doorOpenSeconds = 1.5f;
    [Min(0.1f)] [SerializeField] float rideSeconds = 4f;
    [Min(0.05f)] [SerializeField] float buttonPressSeconds = 0.18f;
    [Min(0.05f)] [SerializeField] float buttonShakeSeconds = 1f;
    [Range(0.1f, 1f)] [SerializeField] float buttonShakeStrength = 0.85f;
    [Header("Path")]
    [SerializeField] bool showPathToTable = true;
    [SerializeField] Transform pathStart;
    [SerializeField] Transform pathTarget;
    [SerializeField] Transform[] pathWaypoints;
    [Tooltip("Raises the arrows above the floor snap. Increase if they sit under the ground mesh.")]
    [Min(0f)] [SerializeField] float pathHeightOffset = 0.08f;
    [Header("Sound")]
    [Range(0f, 1f)] [SerializeField] float elevatorVolume = 1f;
    [Range(0f, 1f)] [SerializeField] float tunnelVolume = 0.5f;
    [Min(0f)] [SerializeField] float musicCrossfadeSeconds = 1.5f;
    [Header("After the doors open and all tablet options are checked")]
    [SerializeField] string nextSceneName = "LVL3_MissileLaunch";
    [Min(0f)] [SerializeField] float delayAfterClicks = 5f;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 0.8f;

    Coroutine nextSceneRoutine;
    Coroutine elevatorRoutine;
    Coroutine fadeRoutine;
    Coroutine musicRoutine;
    bool loadingNextScene;
    bool floorPressed;
    bool doorsOpenForPlayer;
    HandHintBanner handHint;
    Transform playerParent;
    Vector3 buttonRestLocal;

    public bool IsFadeComplete { get; private set; }
    public event System.Action DoorsOpened;
    public event System.Action TransitionStarted;
    public bool IsFloorPressed => floorPressed;
    public bool IsLoadingNextScene => loadingNextScene;
    public bool AreDoorsOpenForPlayer => doorsOpenForPlayer;
    public bool HasValidAssignments => elevatorRoot != null && elevatorArrival != null &&
        playerSpawn != null && door01 != null && door02 != null && door01OpenPose != null &&
        door02OpenPose != null && elevatorButton != null && buttonPressedPose != null &&
        floorButtons != null && floorButtons.Length > 0 && System.Array.TrueForAll(floorButtons, item => item != null) &&
        exitBarrier != null && elevatorBoundary != null && levelBoundary != null &&
        elevatorBoundary != levelBoundary && elevatorBoundary.transform.IsChildOf(elevatorRoot) &&
        !levelBoundary.transform.IsChildOf(elevatorRoot) &&
        xrOrigin != null && tabletGrab != null && tabletUi != null &&
        tabletCollider != null && tabletBody != null && table != null && fadeOverlay != null &&
        (!showPathToTable || (pathStart != null && pathTarget != null));

    void Start()
    {
        if (!HasValidAssignments)
        {
            Debug.LogError("ElevatorDoorIntro: assign the cabin, poses, button, cabin/level boundaries, tablet, table, player and fade overlay in the scene.", this);
            enabled = false;
            return;
        }
        playerParent = xrOrigin.transform.parent;
        buttonRestLocal = elevatorButton.localPosition;
        BridgeXrPlaySetup.PrepareElevatorPlaySpace(xrOrigin);
        xrOrigin.transform.SetPositionAndRotation(playerSpawn.position, playerSpawn.rotation);
        xrOrigin.transform.SetParent(elevatorRoot, true);
        SetElevatorBoundaryActive(true);
        exitBarrier.SetActive(true);
        SetOutsideTeleportEnabled(false);
        foreach (var button in floorButtons)
        {
            button.enabled = false;
            button.selectEntered.AddListener(OnFloorSelected);
            button.activated.AddListener(OnFloorActivated);
        }
        foreach (var own in xrOrigin.GetComponentsInChildren<Collider>(true))
            Physics.IgnoreCollision(tabletCollider, own, true);
        tabletGrab.selectEntered.AddListener(OnTabletSelected);
        tabletGrab.selectExited.AddListener(OnTabletReleased);
        tabletUi.ChecklistUpdated += OnTabletChecklistChanged;
        fadeOverlay.alpha = 1f;
        if (tunnelAudio != null) tunnelAudio.Stop();
        if (elevatorAudio != null)
        {
            elevatorAudio.volume = elevatorVolume;
            elevatorAudio.Play();
        }
        if (rightController != null && xrOrigin.Camera != null)
        {
            handHint = HandHintBanner.Create(xrOrigin.Camera, handHintFont, elevatorHintMessage);
            handHint.SetHand(rightController);
        }
        fadeRoutine = StartCoroutine(AutoFadeIn());
    }

    void OnFloorSelected(SelectEnterEventArgs args) => TryOpenDoors();
    void OnFloorActivated(ActivateEventArgs args) => TryOpenDoors();

    void TryOpenDoors()
    {
        if (!enabled || !HasValidAssignments || !IsFadeComplete || floorPressed) return;
        floorPressed = true;
        foreach (var button in floorButtons) button.enabled = false;
        ButtonClickSound.Play();
        if (floorButtonOrderVoice != null && floorButtonOrderVoice.clip != null)
        {
            floorButtonOrderVoice.loop = false;
            floorButtonOrderVoice.Play();
        }
        if (handHint != null) handHint.Hide();
        handHint = null;
        if (buttonShake != null) buttonShake.ShakeFor(buttonShakeSeconds, buttonShakeStrength);
        StartCoroutine(AnimateButtonPress());
        elevatorRoutine = StartCoroutine(RideThenOpenDoors());
    }

    IEnumerator AnimateButtonPress()
    {
        var destination = elevatorButton.parent != null
            ? elevatorButton.parent.InverseTransformPoint(buttonPressedPose.position) : buttonPressedPose.position;
        var duration = Mathf.Max(0.02f, buttonPressSeconds * 0.5f);
        for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            elevatorButton.localPosition = Vector3.Lerp(buttonRestLocal, destination, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        elevatorButton.localPosition = destination;
    }

    IEnumerator RideThenOpenDoors()
    {
        var start = elevatorRoot.position;
        for (var elapsed = 0f; elapsed < rideSeconds; elapsed += Time.unscaledDeltaTime)
        {
            elevatorRoot.position = Vector3.Lerp(start, elevatorArrival.position, Mathf.SmoothStep(0f, 1f, elapsed / rideSeconds));
            yield return null;
        }
        elevatorRoot.SetPositionAndRotation(elevatorArrival.position, elevatorArrival.rotation);
        if (doorDelaySeconds > rideSeconds) yield return new WaitForSecondsRealtime(doorDelaySeconds - rideSeconds);
        var start01 = door01.localPosition;
        var start02 = door02.localPosition;
        var end01 = door01.parent.InverseTransformPoint(door01OpenPose.position);
        var end02 = door02.parent.InverseTransformPoint(door02OpenPose.position);
        for (var elapsed = 0f; elapsed < doorOpenSeconds; elapsed += Time.unscaledDeltaTime)
        {
            var progress = Mathf.SmoothStep(0f, 1f, elapsed / doorOpenSeconds);
            door01.localPosition = Vector3.Lerp(start01, end01, progress);
            door02.localPosition = Vector3.Lerp(start02, end02, progress);
            yield return null;
        }
        door01.localPosition = end01;
        door02.localPosition = end02;
        CompleteElevatorRide();
    }

    void CompleteElevatorRide()
    {
        exitBarrier.SetActive(false);
        xrOrigin.transform.SetParent(playerParent, true);
        BridgeXrPlaySetup.PrepareElevatorPlaySpace(xrOrigin);
        SetElevatorBoundaryActive(false);
        SetOutsideTeleportEnabled(true);
        doorsOpenForPlayer = true;
        musicRoutine = StartCoroutine(CrossfadeToTunnelMusic());
        DoorsOpened?.Invoke();
        ShowPathToTable();
        OnTabletChecklistChanged(tabletUi.AllOptionsChecked);
    }

    void SetElevatorBoundaryActive(bool value)
    {
        elevatorBoundary.gameObject.SetActive(value);
        levelBoundary.gameObject.SetActive(!value);
        var enforcer = xrOrigin.GetComponent<XrPlayBoundaryEnforcer>();
        if (enforcer == null) enforcer = xrOrigin.gameObject.AddComponent<XrPlayBoundaryEnforcer>();
        enforcer.RefreshVolumes();
    }

    void SetOutsideTeleportEnabled(bool value)
    {
        if (outsideTeleportAreas == null) return;
        foreach (var area in outsideTeleportAreas)
            if (area != null) area.enabled = value;
    }

    void OnTabletSelected(SelectEnterEventArgs args)
    {
        tabletBody.isKinematic = false;
        tabletBody.useGravity = false;
    }

    void OnTabletReleased(SelectExitEventArgs args)
    {
        if (tabletGrab.isSelected) return;
        if (IsAboveTable(tabletCollider, table))
        {
            SitOnTableTop(tabletGrab.gameObject, tabletCollider, table);
            tabletBody.isKinematic = true;
            tabletBody.useGravity = false;
        }
        else
        {
            tabletBody.isKinematic = false;
            tabletBody.useGravity = true;
        }
    }

    void ShowPathToTable()
    {
        if (!showPathToTable || pathStart == null || pathTarget == null) return;
        FloorPathGuide.Create(CollectPathPoints(), xrOrigin.transform, true, pathHeightOffset, table.GetComponentsInChildren<Collider>());
    }

    void OnTabletChecklistChanged(bool allChecked)
    {
        if (loadingNextScene) return;
        if (!doorsOpenForPlayer || !allChecked)
        {
            if (nextSceneRoutine != null) StopCoroutine(nextSceneRoutine);
            nextSceneRoutine = null;
            return;
        }
        if (nextSceneRoutine == null)
        {
            TransitionStarted?.Invoke();
            nextSceneRoutine = StartCoroutine(LoadNextSceneAfterClicks());
        }
    }

    public void FadeOutTunnelMusic()
    {
        if (musicRoutine != null) StopCoroutine(musicRoutine);
        if (elevatorAudio != null) elevatorAudio.Stop();
        musicRoutine = StartCoroutine(FadeOutTunnel());
    }

    IEnumerator FadeOutTunnel()
    {
        if (tunnelAudio == null) yield break;
        var startVolume = tunnelAudio.volume;
        for (var elapsed = 0f; elapsed < musicCrossfadeSeconds; elapsed += Time.unscaledDeltaTime)
        {
            tunnelAudio.volume = Mathf.Lerp(startVolume, 0f, elapsed / musicCrossfadeSeconds);
            yield return null;
        }
        tunnelAudio.Stop();
        musicRoutine = null;
    }

    IEnumerator AutoFadeIn()
    {
        if (blackHoldSeconds > 0f) yield return new WaitForSecondsRealtime(blackHoldSeconds);
        yield return FadeTo(0f, fadeInSeconds);
        FinishFadeIn();
    }

    void FinishFadeIn()
    {
        fadeOverlay.alpha = 0f;
        IsFadeComplete = true;
        foreach (var button in floorButtons) button.enabled = !floorPressed;
    }

    IEnumerator CrossfadeToTunnelMusic()
    {
        if (tunnelAudio != null)
        {
            tunnelAudio.volume = 0f;
            tunnelAudio.Play();
        }
        var elevatorStart = elevatorAudio != null ? elevatorAudio.volume : 0f;
        for (var elapsed = 0f; elapsed < musicCrossfadeSeconds; elapsed += Time.unscaledDeltaTime)
        {
            var progress = Mathf.Clamp01(elapsed / musicCrossfadeSeconds);
            if (elevatorAudio != null) elevatorAudio.volume = Mathf.Lerp(elevatorStart, 0f, progress);
            if (tunnelAudio != null) tunnelAudio.volume = Mathf.Lerp(0f, tunnelVolume, progress);
            yield return null;
        }
        if (elevatorAudio != null) elevatorAudio.Stop();
        if (tunnelAudio != null) tunnelAudio.volume = tunnelVolume;
    }

    public void EditorTestPressElevatorButton()
    {
        if (!Application.isPlaying || !enabled || !HasValidAssignments) return;
        EditorTestFinishFadeIn();
        TryOpenDoors();
    }

    public void EditorTestFinishFadeIn()
    {
        if (!Application.isPlaying || !enabled || !HasValidAssignments) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        FinishFadeIn();
    }

    public void EditorTestCompleteElevatorRide()
    {
        if (!Application.isPlaying || !enabled || !HasValidAssignments || doorsOpenForPlayer) return;
        EditorTestPressElevatorButton();
        if (elevatorRoutine != null) StopCoroutine(elevatorRoutine);
        elevatorRoot.SetPositionAndRotation(elevatorArrival.position, elevatorArrival.rotation);
        door01.position = door01OpenPose.position;
        door02.position = door02OpenPose.position;
        CompleteElevatorRide();
    }

    public void EditorTestLoadNextSceneNow()
    {
        if (Application.isPlaying && doorsOpenForPlayer && tabletUi != null && tabletUi.AllOptionsChecked)
            OnTabletChecklistChanged(true);
    }

    void OnDisable()
    {
        if (floorButtonOrderVoice != null) floorButtonOrderVoice.Stop();
    }

    void OnDestroy()
    {
        if (tabletUi != null) tabletUi.ChecklistUpdated -= OnTabletChecklistChanged;
        if (tabletGrab != null)
        {
            tabletGrab.selectEntered.RemoveListener(OnTabletSelected);
            tabletGrab.selectExited.RemoveListener(OnTabletReleased);
        }
        if (floorButtons != null)
            foreach (var button in floorButtons)
            {
                if (button == null) continue;
                button.selectEntered.RemoveListener(OnFloorSelected);
                button.activated.RemoveListener(OnFloorActivated);
            }
        if (handHint != null) handHint.Hide();
    }

    IEnumerator LoadNextSceneAfterClicks()
    {
        if (delayAfterClicks > 0f)
            yield return new WaitForSecondsRealtime(delayAfterClicks);

        loadingNextScene = true;
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

    static void SitOnTableTop(GameObject tablet, BoxCollider tabletBox, GameObject table)
    {
        if (tablet == null || tabletBox == null || table == null) return;
        if (!TryTableTop(table, out var top, out _)) return;

        Physics.SyncTransforms();
        var b = tabletBox.bounds;
        tablet.transform.position += Vector3.up * (top + 0.02f - b.min.y);
        Physics.SyncTransforms();
    }

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

    void OnDrawGizmos()
    {
        if (!showPathToTable) return;
        DrawPathGizmos(0.45f, 0.12f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showPathToTable) return;
        DrawPathGizmos(1f, 0.18f);
    }

    void DrawPathGizmos(float alpha, float radius)
    {
        var points = CollectPathPoints();
        if (points.Count == 0) return;

        var pathColor = new Color(0.98f, 0.80f, 0.20f, alpha);
        var liftColor = new Color(0.45f, 0.85f, 1f, alpha);
        Gizmos.color = pathColor;
        for (var i = 0; i < points.Count; i++)
        {
            var authored = points[i];
            var lifted = authored + Vector3.up * pathHeightOffset;
            Gizmos.DrawSphere(lifted, radius);
            if (pathHeightOffset > 0.001f)
            {
                Gizmos.color = liftColor;
                Gizmos.DrawLine(authored, lifted);
                Gizmos.color = pathColor;
            }
            if (i > 0)
                Gizmos.DrawLine(points[i - 1] + Vector3.up * pathHeightOffset,
                    lifted);
        }
    }

    List<Vector3> CollectPathPoints()
    {
        var points = new List<Vector3>();
        if (pathStart != null) points.Add(pathStart.position);
        if (pathWaypoints != null)
            foreach (var waypoint in pathWaypoints)
                if (waypoint != null) points.Add(waypoint.position);
        if (pathTarget != null) points.Add(pathTarget.position);
        return points;
    }

}
