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
    [SerializeField] string carryMessage = "جعبه رو ببر روی میز، جای جعبه زرد\nنزدیکش که شدی ولش کن";
    [SerializeField] string doneMessage = "آفرین!";
    [Tooltip("A jump larger than this in one frame counts as a teleport.")]
    [SerializeField] float teleportJumpDistance = 0.4f;
    [Tooltip("Release the case closer than this to the yellow spot and it snaps in.")]
    [SerializeField] float snapRadius = 0.6f;

    [Header("After the case is placed")]
    [SerializeField] string nextSceneName = "Lvl2_MissileTunnel";
    [Min(0f)] [SerializeField] float delayBeforeFade = 1.5f;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 1f;

    enum Step { WaitingForStart, Teleport, Grab, Carry, Done }

    XROrigin origin;
    TMP_FontAsset font;
    HandHintBanner banner;
    Step step;
    Vector3 lastOriginPosition;
    GameObject marker;
    FloorPathGuide path;
    MagnetDropZone dropZone;
    XRGrabInteractable targetCase;
    XRGrabInteractable carriedCase;
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

        // Let the first-scene setup (floor, body, grabbable cases) finish first.
        yield return null;
        font = FindPersianFont();

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
        lastOriginPosition = origin.transform.position;
    }

    void BeginGrabStep()
    {
        step = Step.Grab;
        SetWalking(true);
        ShowBanner(grabMessage);

        BridgeXrPlaySetup.MakeCasesGrabbable();
        cases.Clear();
        foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (grab.name.StartsWith("SM_Hard_Camera_Case")) cases.Add(grab);
        }

        // The case standing highest (on the table) marks the drop spot; it becomes a yellow ghost.
        targetCase = HighestCase();
        if (targetCase != null) cases.Remove(targetCase);
        foreach (var grab in cases) grab.selectEntered.AddListener(OnCaseGrabbed);

        var nearest = NearestCase();
        if (nearest != null) marker = TutorialMarker.Create(nearest.transform);
        if (cases.Count == 0) StartCoroutine(Finish());
    }

    void OnCaseGrabbed(SelectEnterEventArgs args)
    {
        if (step != Step.Grab) return;
        if (args.interactorObject is XRSocketInteractor) return;
        carriedCase = args.interactableObject as XRGrabInteractable;
        if (targetCase == null || carriedCase == null)
        {
            StartCoroutine(Finish());
            return;
        }
        BeginCarryStep();
    }

    void BeginCarryStep()
    {
        step = Step.Carry;
        foreach (var grab in cases)
            if (grab != null) grab.selectEntered.RemoveListener(OnCaseGrabbed);

        ShowBanner(carryMessage);
        if (marker != null) Destroy(marker);
        marker = TutorialMarker.Create(targetCase.transform);

        // Placeholder: still, see-through, no collisions, so the carried case can slide into it.
        targetCase.enabled = false;
        var targetBody = targetCase.GetComponent<Rigidbody>();
        if (targetBody != null)
        {
            targetBody.isKinematic = true;
            targetBody.useGravity = false;
        }
        foreach (var col in targetCase.GetComponentsInChildren<Collider>()) col.enabled = false;
        MakeGhost(targetCase.gameObject);

        var zoneObject = new GameObject("Case Drop Zone");
        zoneObject.transform.SetPositionAndRotation(targetCase.transform.position, targetCase.transform.rotation);
        dropZone = zoneObject.AddComponent<MagnetDropZone>();
        dropZone.Configure(carriedCase, targetCase.gameObject, snapRadius);
        dropZone.Snapped += _ => StartCoroutine(Finish());

        path = BuildPath(carriedCase.transform.position, targetCase.transform.position);
    }

    IEnumerator Finish()
    {
        step = Step.Done;
        foreach (var grab in cases)
            if (grab != null) grab.selectEntered.RemoveListener(OnCaseGrabbed);
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);

        ShowBanner(doneMessage);
        yield return new WaitForSeconds(delayBeforeFade);
        HideBanner();

        if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning("Lvl1Tutorial: next scene '" + nextSceneName + "' is not in Build Settings.", this);
            yield break;
        }
        SceneTransitionRunner.Begin(nextSceneName, true, 0f, fadeOutSeconds, 0.4f, 0.6f);
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

    XRGrabInteractable HighestCase()
    {
        XRGrabInteractable best = null;
        var bestY = float.MinValue;
        foreach (var grab in cases)
        {
            if (grab == null) continue;
            var y = grab.transform.position.y;
            if (y <= bestY) continue;
            bestY = y;
            best = grab;
        }
        return cases.Count > 1 ? best : null;
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

    XRGrabInteractable NearestCase()
    {
        XRGrabInteractable best = null;
        var bestDistance = float.MaxValue;
        var from = origin.transform.position;
        foreach (var grab in cases)
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
        if (marker != null) Destroy(marker);
        if (path != null) Destroy(path.gameObject);
    }
}

/// <summary>Bobbing yellow arrow above a target, pointing down at it.</summary>
public sealed class TutorialMarker : MonoBehaviour
{
    Transform target;
    float height;

    public static GameObject Create(Transform target)
    {
        var go = new GameObject("Tutorial Marker");
        var marker = go.AddComponent<TutorialMarker>();
        marker.target = target;

        var bounds = new Bounds(target.position, Vector3.zero);
        var has = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        marker.height = (has ? bounds.max.y - target.position.y : 0.3f) + 0.25f;

        var arrow = new GameObject("Arrow");
        arrow.transform.SetParent(go.transform, false);
        arrow.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
        var renderer = arrow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ArrowMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }
        var bob = Mathf.Sin(Time.time * 3f) * 0.05f;
        transform.position = target.position + Vector3.up * (height + bob);
        transform.rotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
    }

    static Mesh ArrowMesh()
    {
        // Four-sided pyramid with its tip pointing down.
        const float w = 0.08f;
        const float h = 0.14f;
        var tip = new Vector3(0f, 0f, 0f);
        var a = new Vector3(-w, h, -w);
        var b = new Vector3(w, h, -w);
        var c = new Vector3(w, h, w);
        var d = new Vector3(-w, h, w);
        var mesh = new Mesh { name = "TutorialArrow" };
        mesh.vertices = new[] { tip, b, a, tip, c, b, tip, d, c, tip, a, d, a, b, c, a, c, d };
        var tris = new int[18];
        for (var i = 0; i < 18; i++) tris[i] = i;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material ArrowMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "TutorialArrow" };
        var color = new Color(1f, 0.78f, 0.15f, 1f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        return mat;
    }
}
