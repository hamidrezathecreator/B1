using System.Collections;
using System.Collections.Generic;
using RTLTMPro;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Lvl 1 controller tutorial, shown as a banner above the right controller:
/// 1. Press the Start button (no movement until then).
/// 2. Teleport with the right thumbstick (teleport only; walking stays off).
/// 3. Walk to a case and grab it with the grip button.
/// 4. Carry it along the yellow floor path to the yellow ghost on the table; released close by, it snaps in.
/// 5. Fade to black and load the next level.
/// Starts by itself whenever the Lvl_1 scene loads.
/// </summary>
public sealed class Lvl1Tutorial : MonoBehaviour
{
    const string SceneName = "Lvl_1";

    [SerializeField] string startMessage = "روی دکمه شروع بزن";
    [SerializeField] string teleportMessage = "با این استیک میتونی توی محیط حرکت کنی\nاستیک راست رو جلو بده، نشونه بگیر و ول کن";
    [SerializeField] string grabMessage = "حالا برو جعبه رو بردار\nدستت رو ببر نزدیکش و دکمه کناری (گریپ) رو نگه دار";
    [SerializeField] string carryMessage = "به سمت میز داخل غرفه برو و جعبه رو قرار بده\nنزدیک جای زرد که شدی ولش کن";
    [SerializeField] string doneMessage = "آفرین!";
    [Tooltip("A jump larger than this in one frame counts as a teleport.")]
    [SerializeField] float teleportJumpDistance = 0.4f;

    [Header("Cases")]
    [Tooltip("Cases the player picks up.")]
    [SerializeField] string[] pickupCaseNames = { "SM_Hard_Camera_Case_01 (2)", "SM_Hard_Camera_Case_01" };
    [Tooltip("Cases on the booth table that mark where a picked-up case goes (shown as yellow ghosts).")]
    [SerializeField] string[] slotCaseNames = { "SM_Hard_Camera_Case_01 (4)", "SM_Hard_Camera_Case_01 (3)" };
    [Tooltip("Release a case closer than this to a yellow spot and it snaps in (the nearest free spot wins).")]
    [SerializeField] float snapRadius = 0.7f;

    [Header("After the case is placed")]
    [SerializeField] string nextSceneName = "Lvl2_MissileTunnel";
    [Tooltip("Hidden at the start, switched on a moment after the first case snaps into place.")]
    [SerializeField] string explosionObjectName = "Explosion 13";
    [Min(0f)] [SerializeField] float delayBeforeExplosion = 2f;
    [Tooltip("Timeline (PlayableDirector) started from the beginning when the case snaps into place.")]
    [SerializeField] string directorObjectName = "Director";
    [Tooltip("Intact bridge: on at the start, off when the timeline starts. First name found is used.")]
    [SerializeField] string[] intactBridgeNames = { "BridgMain", "BridgeMain", "Bridge Main" };
    [Tooltip("Destroyed bridge: off at the start, on when the timeline starts.")]
    [SerializeField] string destroyedBridgeName = "BridgeDestroyed";
    [Tooltip("Timeline time, in seconds, at which the XR Origin drops down (the bridge gives way).")]
    [Min(0f)] [SerializeField] float fallAtTimelineSeconds = 5f;
    [Tooltip("The player drops this long after this explosion appears on the Timeline. Empty name = Fall At Timeline Seconds.")]
    [SerializeField] string fallAfterExplosionName = "Explosion_Two";
    [Min(0f)] [SerializeField] float fallAfterExplosionSeconds = 3f;
    [Tooltip("2 = falls twice as fast.")]
    [Min(0.1f)] [SerializeField] float fallSpeedMultiplier = 2f;
    [Tooltip("Longest wait after the case snaps in before the player falls anyway.")]
    [Min(1f)] [SerializeField] float maxWaitForFallSignal = 30f;
    [Tooltip("Seconds of falling before the eyes close.")]
    [Min(0f)] [SerializeField] float closeEyesAfterFallSeconds = 1f;
    [Tooltip("Longest wait for the eyes to close before loading the next level anyway.")]
    [Min(0.1f)] [SerializeField] float maxEyeCloseSeconds = 2.5f;
    [Tooltip("Without a timeline: time to watch the explosion before the screen fades to black.")]
    [Min(0f)] [SerializeField] float delayBeforeFade = 3f;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 1f;
    [Min(0f)] [SerializeField] float doneMessageSeconds = 3f;

    enum Step { WaitingForStart, Teleport, Grab, Carry, Done }

    XROrigin origin;
    TMP_FontAsset font;
    HandHintBanner banner;
    Step step;
    Vector3 lastOriginPosition;
    GameObject marker;
    FloorPathGuide path;
    XRGrabInteractable carriedCase;
    readonly List<XRGrabInteractable> slots = new List<XRGrabInteractable>();
    readonly List<MagnetDropZone> zones = new List<MagnetDropZone>();
    ControllerButtonHighlight buttonHighlight;
    GameObject explosion;
    ExplosionCue fallCue;
    float fallExplosionShownAt = -1f;
    GameObject intactBridge;
    GameObject destroyedBridge;
    readonly List<XRGrabInteractable> cases = new List<XRGrabInteractable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneName) return;
        if (FindAnyObjectByType<Lvl1Tutorial>() != null) return;
        var go = new GameObject("Lvl1 Tutorial");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<Lvl1Tutorial>();
    }

    IEnumerator Start()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) yield break;

        // Before the first physics step: tables get colliders and the cases rest instead of falling.
        EnsureTableColliders();
        BridgeXrPlaySetup.MakeCasesGrabbable();
        RestCasesUntilGrabbed();
        SetupCases();

        explosion = string.IsNullOrWhiteSpace(explosionObjectName) ? null : GameObject.Find(explosionObjectName);
        if (explosion != null) explosion.SetActive(false);

        // Start: intact bridge shown, destroyed one hidden. Swapped when the timeline starts.
        intactBridge = FindByName(intactBridgeNames);
        destroyedBridge = FindByName(destroyedBridgeName);
        if (intactBridge == null)
            Debug.LogWarning("Lvl1Tutorial: no intact bridge named " + string.Join(" / ", intactBridgeNames) + ".", this);
        if (destroyedBridge == null)
            Debug.LogWarning("Lvl1Tutorial: no destroyed bridge named " + destroyedBridgeName + ".", this);
        SetBridgeDestroyed(false);
        if (GetComponentInChildren<SecondMissileStrike>() == null) SecondMissileStrike.Create(transform);

        // Let the first-scene setup (floor, body) finish first.
        yield return null;
        font = FindPersianFont();

        PrepareTeleportFloor();
        SetWalking(false);
        SetTeleport(false);

        var startButton = FindStartButton();
        if (startButton == null)
        {
            BeginTeleportStep();
            yield break;
        }

        step = Step.WaitingForStart;
        ShowBanner(startMessage);
        HighlightButton("Trigger");
        startButton.onClick.AddListener(OnStartPressed);

        // The button also hides its panel; treat that as pressed too.
        var panel = startButton.transform.parent != null ? startButton.transform.parent.gameObject : null;
        while (step == Step.WaitingForStart)
        {
            if (panel != null && !panel.activeInHierarchy) OnStartPressed();
            yield return null;
        }
    }

    void OnStartPressed()
    {
        if (step != Step.WaitingForStart) return;
        BeginTeleportStep();
    }

    void BeginTeleportStep()
    {
        step = Step.Teleport;
        SetTeleport(true);
        ShowBanner(teleportMessage);
        HighlightButton("ThumbStick");
        lastOriginPosition = origin.transform.position;

        // Show where to go right away: the blue beacon on the case to pick up.
        if (marker == null)
        {
            var nearest = Nearest(cases, origin.transform.position);
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

    /// <summary>
    /// Only the pickup cases can be grabbed, and only by touching them with the hand.
    /// Slot cases are placeholders and every other case stays where it is.
    /// </summary>
    void SetupCases()
    {
        cases.Clear();
        slots.Clear();
        FindCases(slotCaseNames, slots);
        FindCases(pickupCaseNames, cases);
        if (cases.Count == 0)
        {
            // Names changed in the scene: any other case can be picked up.
            foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (grab.name.StartsWith("SM_Hard_Camera_Case") && !slots.Contains(grab)) cases.Add(grab);
        }
        if (cases.Count == 0 || slots.Count == 0)
            Debug.LogWarning("Lvl1Tutorial: missing pickup or slot cases; check the case names on the tutorial.", this);

        foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!grab.name.StartsWith("SM_Hard_Camera_Case")) continue;
            if (cases.Contains(grab))
            {
                NearOnlyGrabFilter.AddTo(grab);
                grab.selectEntered.AddListener(OnCaseGrabbed);
            }
            else if (slots.Contains(grab))
            {
                FreezeSlot(grab);
            }
            else
            {
                LockInPlace(grab);
            }
        }
    }

    static void LockInPlace(XRGrabInteractable grab)
    {
        grab.enabled = false;
        var body = grab.GetComponent<Rigidbody>();
        if (body == null) return;
        body.isKinematic = true;
        body.useGravity = false;
    }

    void OnCaseGrabbed(SelectEnterEventArgs args)
    {
        if (step != Step.Teleport && step != Step.Grab && step != Step.Carry) return;
        if (args.interactorObject is XRSocketInteractor) return;
        var grabbed = args.interactableObject as XRGrabInteractable;
        if (grabbed == null || MagnetDropZone.IsClaimed(grabbed)) return;
        // Walked over and grabbed it before teleporting: that is fine too.
        if (step == Step.Teleport) SetWalking(true);

        carriedCase = grabbed;
        if (slots.Count == 0)
        {
            StartCoroutine(Finish());
            return;
        }
        BeginCarryStep();
    }

    void BeginCarryStep()
    {
        var firstTime = step != Step.Carry;
        step = Step.Carry;

        if (firstTime)
        {
            // The grab explanation is done; now point at the table in the booth.
            ShowBanner(carryMessage);
            ClearButtonHighlight();

            foreach (var slot in slots)
            {
                MakeGhost(slot.gameObject);
                var zoneObject = new GameObject("Case Drop Zone (" + slot.name + ")");
                zoneObject.transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
                var zone = zoneObject.AddComponent<MagnetDropZone>();
                zone.Configure(cases, slot.gameObject, snapRadius);
                zone.Snapped += OnCaseSnapped;
                zones.Add(zone);
            }
        }

        // Point at the free spot nearest the case in hand.
        var target = NearestFreeSlot(carriedCase.transform.position);
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
        if (target == null) return;
        marker = TutorialMarker.Create(target.transform);
        // Start at the player's feet: the case in hand is above whatever it was picked up from.
        path = BuildPath(origin.transform.position, target.transform.position);
    }

    void OnCaseSnapped(XRGrabInteractable grab)
    {
        if (step == Step.Done) return;
        StartCoroutine(Finish());
    }

    IEnumerator Finish()
    {
        step = Step.Done;
        foreach (var grab in cases)
            if (grab != null) grab.selectEntered.RemoveListener(OnCaseGrabbed);
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
        ClearButtonHighlight();

        ShowBanner(doneMessage);

        var director = FindDirector();
        // A Timeline fall signal (Player And Plane Fall) decides when the player drops.
        var planeFall = FindAnyObjectByType<PlayerPlaneFall>();
        SetBridgeDestroyed(true);
        if (director != null && director.playOnAwake)
        {
            // Play On Awake: the timeline already started with the scene, so it is not restarted here.
            WatchFallExplosion();
            var sinceSnap = 0f;
            while (!FallTimeReached(sinceSnap, sinceSnap, planeFall))
            {
                sinceSnap += Time.deltaTime;
                if (sinceSnap >= doneMessageSeconds) HideBanner();
                yield return null;
            }
        }
        else if (director != null)
        {
            // Fresh pieces: every Timeline signal throws its share again from the start.
            ResetScatters();
            director.Stop();
            director.time = 0;
            WatchFallExplosion();
            director.Play();
            StartCoroutine(ExplosionAfterDelay());

            // Follow the timeline clock; if it ends early or is paused, keep counting in game time.
            var elapsed = 0f;
            var bannerHidden = false;
            while (true)
            {
                elapsed += Time.deltaTime;
                var playing = director.state == UnityEngine.Playables.PlayState.Playing;
                var timelineTime = playing ? (float)director.time : elapsed;
                if (FallTimeReached(timelineTime, elapsed, planeFall)) break;
                if (!bannerHidden && elapsed >= doneMessageSeconds)
                {
                    HideBanner();
                    bannerHidden = true;
                }
                yield return null;
            }
        }
        else
        {
            if (delayBeforeExplosion > 0f) yield return new WaitForSeconds(delayBeforeExplosion);
            PlayExplosion();
            yield return new WaitForSeconds(delayBeforeFade);
        }
        HideBanner();

        // The bridge gives way: the player drops.
        if (planeFall != null) planeFall.Fall();
        else StartCoroutine(FallDown());
        if (closeEyesAfterFallSeconds > 0f) yield return new WaitForSeconds(closeEyesAfterFallSeconds);

        // Eyes close, then on to the next level.
        var blink = FindAnyObjectByType<VrBlinkEffect>();
        if (blink == null) blink = new GameObject("Eye Blink").AddComponent<VrBlinkEffect>();
        blink.CloseSlowly();
        var waited = 0f;
        while (blink != null && blink.Amount < 0.99f && waited < maxEyeCloseSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning("Lvl1Tutorial: next scene '" + nextSceneName + "' is not in Build Settings.", this);
            yield break;
        }
        // Eyes are already shut; only a short fade so nothing pops while the next level loads.
        SceneTransitionRunner.Begin(nextSceneName, true, 0f, Mathf.Min(0.3f, fadeOutSeconds), 0.4f, 0.6f);
    }

    /// <summary>
    /// Drops the XR Origin like a fall: the height lock, body collider and locomotion are switched
    /// off and the rig accelerates downward until the next level loads.
    /// </summary>
    IEnumerator FallDown()
    {
        if (origin == null) yield break;

        BridgeXrPlaySetup.SuspendDeckLock(origin);
        var body = origin.GetComponent<CharacterController>();
        if (body != null) body.enabled = false;
        SetWalking(false);
        SetTeleport(false);
        foreach (var gravity in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(true))
            gravity.enabled = false;

        var speed = 0f;
        while (origin != null)
        {
            speed = Mathf.Min(speed + 9.81f * fallSpeedMultiplier * Time.deltaTime, 30f * fallSpeedMultiplier);
            origin.transform.position += Vector3.down * speed * Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>Starts watching for the explosion the fall waits for (switched on by the Timeline).</summary>
    void WatchFallExplosion()
    {
        fallExplosionShownAt = -1f;
        var go = string.IsNullOrWhiteSpace(fallAfterExplosionName) ? null : FindByName(fallAfterExplosionName);
        if (go == null)
        {
            fallCue = null;
            return;
        }
        // Trim the DAVFX burst for Quest before the Timeline switches it on.
        VrExplosionOptimizer.Apply(go);
        fallCue = ExplosionCue.AddTo(go);
        fallCue.Shown -= OnFallExplosionShown;
        fallCue.Shown += OnFallExplosionShown;
        // Already on screen (Timeline started with the scene): count from now.
        if (go.activeInHierarchy) fallExplosionShownAt = Time.time;
    }

    void OnFallExplosionShown(ExplosionCue _) => fallExplosionShownAt = Time.time;

    bool FallTimeReached(float timelineTime, float elapsed, PlayerPlaneFall planeFall)
    {
        if (elapsed >= maxWaitForFallSignal) return true;
        if (fallCue != null)
            return fallExplosionShownAt >= 0f && Time.time - fallExplosionShownAt >= fallAfterExplosionSeconds;
        if (planeFall != null) return planeFall.HasFallen;
        return timelineTime >= fallAtTimelineSeconds;
    }

    void SetBridgeDestroyed(bool destroyed)
    {
        if (intactBridge != null) intactBridge.SetActive(!destroyed);
        if (destroyedBridge != null) destroyedBridge.SetActive(destroyed);
    }

    static GameObject FindByName(params string[] names)
    {
        if (names == null) return null;
        // Includes switched-off objects: the destroyed bridge is usually off in the scene.
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            foreach (var t in all)
                if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        }
        return null;
    }

    static void ResetScatters()
    {
        foreach (var scatter in FindObjectsByType<ExplosionScatter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (scatter.gameObject.scene.IsValid()) scatter.ResetExplosion();
    }

    IEnumerator ExplosionAfterDelay()
    {
        if (delayBeforeExplosion > 0f) yield return new WaitForSeconds(delayBeforeExplosion);
        PlayExplosion();
    }

    UnityEngine.Playables.PlayableDirector FindDirector()
    {
        if (string.IsNullOrWhiteSpace(directorObjectName)) return null;
        var go = GameObject.Find(directorObjectName);
        if (go == null) return null;
        var director = go.GetComponent<UnityEngine.Playables.PlayableDirector>();
        if (director == null) director = go.GetComponentInChildren<UnityEngine.Playables.PlayableDirector>(true);
        return director;
    }

    static void FindCases(string[] names, List<XRGrabInteractable> into)
    {
        if (names == null) return;
        var all = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var name in names)
        {
            foreach (var grab in all)
            {
                if (grab.name != name || into.Contains(grab)) continue;
                into.Add(grab);
                break;
            }
        }
    }

    static void FreezeSlot(XRGrabInteractable slot)
    {
        slot.enabled = false;
        var body = slot.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        // No collisions, so the carried case can slide right into the spot.
        foreach (var col in slot.GetComponentsInChildren<Collider>()) col.enabled = false;
    }

    XRGrabInteractable NearestFreeSlot(Vector3 from)
    {
        var free = new List<XRGrabInteractable>();
        for (var i = 0; i < slots.Count; i++)
        {
            var filled = i < zones.Count && zones[i] != null && zones[i].IsFilled;
            if (!filled && slots[i] != null) free.Add(slots[i]);
        }
        return Nearest(free, from);
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

    static void MakeGhost(GameObject target)
    {
        var source = Resources.Load<Material>("Effects/LaunchSmoke");
        Material ghost;
        if (source != null)
        {
            ghost = new Material(source) { name = "DropSpotGhost" };
            ghost.SetTexture("_BaseMap", null);
            ghost.SetTexture("_MainTex", null);
        }
        else
        {
            ghost = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "DropSpotGhost" };
        }
        var color = new Color(1f, 0.8f, 0.2f, 0.4f);
        if (ghost.HasProperty("_BaseColor")) ghost.SetColor("_BaseColor", color);
        ghost.color = color;

        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (var i = 0; i < mats.Length; i++) mats[i] = ghost;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
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
        var hand = XrHandsRig.FindNamed(origin, "Right Controller");
        if (hand == null) return;
        banner = HandHintBanner.Create(origin.Camera, font, message);
        banner.SetHand(hand);
    }

    void HideBanner()
    {
        if (banner != null) banner.Hide();
        banner = null;
    }

    /// <summary>
    /// The bridge deck had no teleport surface: only a 12 m pad at the spawn and a plane far below
    /// the bridge. Find every surface at walking height around the player and make it a teleport floor.
    /// </summary>
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

        // The pad under the spawn stays teleportable too.
        var deck = GameObject.Find("Walkable Deck");
        if (deck != null)
        {
            deck.layer = groundLayer;
            var deckArea = deck.GetComponent<TeleportationArea>();
            if (deckArea == null) deckArea = deck.AddComponent<TeleportationArea>();
            deckArea.enabled = true;
            deckArea.interactionLayers = -1;
        }

        // Floor planes far below the bridge would drop the player off the deck.
        foreach (var area in FindObjectsByType<TeleportationArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!area.name.StartsWith("Plane")) continue;
            var col = area.GetComponent<Collider>();
            if (col != null && col.bounds.max.y < floorY - 2f)
                area.enabled = false;
        }

        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (!ray.gameObject.name.Contains("Teleport")) continue;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.raycastMask = groundMask;
            ray.interactionLayers = -1;
            ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }

        Debug.Log("Lvl1Tutorial: " + floors.Count + " floor surface(s) made teleportable.", this);
    }

    void PlayExplosion()
    {
        if (explosion == null) return;
        explosion.SetActive(true);
        foreach (var ps in explosion.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
        foreach (var audio in explosion.GetComponentsInChildren<AudioSource>(true))
        {
            if (audio.clip != null && !audio.isPlaying) audio.Play();
        }
    }

    void HighlightButton(params string[] partNames)
    {
        ClearButtonHighlight();
        var controller = XrHandsRig.FindNamed(origin, "Right Controller");
        buttonHighlight = ControllerButtonHighlight.Create(controller, partNames);
    }

    void ClearButtonHighlight()
    {
        if (buttonHighlight != null) Destroy(buttonHighlight.gameObject);
        buttonHighlight = null;
    }

    /// <summary>Tables have no colliders, so the cases on them fell to the floor. Give each a solid box.</summary>
    static void EnsureTableColliders()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("SM_Table")) continue;
            if (t.parent != null && t.parent.name.StartsWith("SM_Table")) continue;
            if (t.GetComponentInChildren<Collider>() != null) continue;
            if (!TryLocalBounds(t, out var center, out var size)) continue;

            var box = t.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = center;
            box.size = size;
        }
    }

    /// <summary>
    /// Cases stay put until picked up; after the first release they fall and land like normal objects.
    /// </summary>
    void RestCasesUntilGrabbed()
    {
        foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!grab.name.StartsWith("SM_Hard_Camera_Case")) continue;
            var body = grab.GetComponent<Rigidbody>();
            if (body == null) continue;
            body.isKinematic = true;
            body.useGravity = false;
            grab.selectExited.AddListener(OnCaseReleased);
        }
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

    static bool TryLocalBounds(Transform root, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        var found = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var local = r.localBounds;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = r.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                var inRoot = root.InverseTransformPoint(corner);
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
        foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (ray.gameObject.name.Contains("Teleport"))
                ray.enabled = on;
        }
    }

    static Button FindStartButton()
    {
        var byName = GameObject.Find("B1Btn");
        if (byName != null && byName.TryGetComponent<Button>(out var named)) return named;

        foreach (var label in FindObjectsByType<RTLTextMeshPro>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!label.OriginalText.Contains("شروع")) continue;
            var button = label.GetComponentInParent<Button>();
            if (button != null) return button;
        }
        return null;
    }

    static TMP_FontAsset FindPersianFont()
    {
        foreach (var label in FindObjectsByType<RTLTextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label.font != null) return label.font;
        }
        return null;
    }

    static XRGrabInteractable Nearest(List<XRGrabInteractable> list, Vector3 from)
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

    void OnDestroy()
    {
        HideBanner();
        ClearButtonHighlight();
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
    }
}
