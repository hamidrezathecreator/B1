using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Project hand meshes in place of both controller models, a poke point on each controller, and a
/// right fingertip touch sphere (<see cref="TabletFingerTip"/>). Shared by every level.
/// The hand is baked once into a plain mesh on a MeshRenderer: the skinned path (bones, tracking
/// scripts, GPU skinning) rendered nothing on Quest, while plain MeshRenderers do.
/// </summary>
public sealed class XrHandsRig : MonoBehaviour
{
    const string HandObjectName = "ProjectHand";
    static string HandObjectNameFor(bool isLeft) => isLeft ? "ProjectHand_L" : "ProjectHand_R";

    [Tooltip("Rigged hand model or prefab, e.g. LeftHandAndroidXR.fbx.")]
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("Wrist position on the controller (right hand; left is mirrored on X).")]
    [SerializeField] Vector3 handOnControllerPosition = new Vector3(0.025f, -0.015f, -0.06f);
    [Tooltip("Extra wrist rotation on the controller, degrees.")]
    [SerializeField] Vector3 handOnControllerEuler = Vector3.zero;

    XROrigin origin;
    XRPokeInteractor rightPoke;
    Transform rightIndexTip;
    TabletFingerTip rightFingerTip;
    Transform leftAnchor;
    Transform rightAnchor;
    bool ready;
    bool controllerModels;
    bool preferHandTrackingWhenAvailable;

    public void PreferHandTrackingWhenAvailable(bool prefer) => preferHandTrackingWhenAvailable = prefer;
    /// <summary>
    /// Show the normal controller models instead of hands. Pokes and the right fingertip touch
    /// still work, from the controller's poke point. Call before <see cref="Setup"/>.
    /// </summary>
    public void UseControllerModels(bool on) => controllerModels = on;

    /// <summary>Updates the wrist offset only (live tuning in Play mode).</summary>
    public void SetWristOffset(Vector3 wristPosition, Vector3 wristEuler)
    {
        handOnControllerPosition = wristPosition;
        handOnControllerEuler = wristEuler;
    }

    public void Configure(GameObject leftPrefab, GameObject rightPrefab, Vector3 wristPosition, Vector3 wristEuler)
    {
        if (leftPrefab != null) leftHandVisualPrefab = leftPrefab;
        if (rightPrefab != null) rightHandVisualPrefab = rightPrefab;
        handOnControllerPosition = wristPosition;
        handOnControllerEuler = wristEuler;
    }

    public void Setup(XROrigin xrOrigin)
    {
        origin = xrOrigin;
        if (origin == null) return;

        // The rig's modality manager turns the controller off when the hand-tracker
        // button reports hands. That drops the hand to the origin (~1 m) and can leave
        // only one side active, so left and right appear to swap.
        KeepControllerObjects(origin);
        // Scene / prefab hand-tracking components under the rig also snap wrists to Origin (0,0,0).
        StripHandTrackingUnder(origin.transform);

        foreach (var line in origin.GetComponentsInChildren<XRInteractorLineVisual>(true))
        {
            if (line.gameObject.name.Contains("Teleport"))
                continue;
            line.enabled = false;
        }

        foreach (var child in origin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "ControllerHandModel")
                Destroy(child.gameObject);
        }

        if (!controllerModels)
        {
            HideControllerMeshes(origin.transform);
            BuildControllerHand(leftHandVisualPrefab, "Left Controller", true, out _);
            BuildControllerHand(rightHandVisualPrefab, "Right Controller", false, out rightIndexTip);
            HideSceneHandVisuals();
        }
        else
        {
            ShowControllerMeshes(origin.transform);
            // Controllers only — hide any tracked-hand meshes that would draw over the grips.
            HideSceneHandVisuals();
        }
        EnsurePokeOnController(origin, "Left Controller");
        rightPoke = EnsurePokeOnController(origin, "Right Controller");
        ready = true;
    }

    /// <summary>
    /// The scene's own tracked-hand meshes (e.g. VR template Tracked Hands) would draw a second
    /// hand on top of ours while hand tracking. Their interactors stay; only the meshes are hidden.
    /// </summary>
    static void HideSceneHandVisuals()
    {
        foreach (var skinned in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (skinned.GetComponentInParent<ControllerHandRig>(true) != null) continue;
            var isHand = skinned.GetComponentInParent<XRHandSkeletonDriver>(true) != null;
            for (var t = skinned.transform; !isHand && t != null; t = t.parent)
                if (t.name.Contains("Tracked Hands")) isHand = true;
            if (isHand) skinned.forceRenderingOff = true;
        }
    }

    public static Transform FindNamed(XROrigin xrOrigin, string targetName)
    {
        if (xrOrigin == null) return null;
        foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName) return child;
        }
        return null;
    }

    /// <summary>
    /// Bakes the rigged hand in its rest pose into a plain mesh and attaches it to the controller,
    /// wrist on the handle, fingers forward. Also returns the index fingertip for touching UI.
    /// </summary>
    void BuildControllerHand(GameObject prefab, string controllerName, bool isLeft, out Transform indexTip)
    {
        indexTip = null;
        try
        {
            BuildControllerHandUnsafe(prefab, controllerName, isLeft, out indexTip);
        }
        catch (System.Exception e)
        {
            // A failed hand must not stop the other hand, the poke points or the level setup.
            Debug.LogException(e, this);
        }
    }

    void BuildControllerHandUnsafe(GameObject prefab, string controllerName, bool isLeft, out Transform indexTip)
    {
        indexTip = null;
        var controller = FindNamed(origin, controllerName);
        if (controller == null)
        {
            Debug.LogWarning("XrHandsRig: no '" + controllerName + "' under the XR Origin.", this);
            return;
        }

        // Assigned project hand only. Scene / XR Hands sample templates are not used.
        var template = prefab;
        if (template == null)
        {
            Debug.LogWarning("XrHandsRig: no hand model for '" + controllerName + "'. Assign a hand prefab.", this);
            return;
        }

        // Old builds parented under the Controller; current builds under Camera Offset.
        // Left and right must use distinct names — both sit under the same Camera Offset.
        var space = origin.CameraFloorOffsetObject != null
            ? origin.CameraFloorOffsetObject.transform
            : origin.transform;
        var handName = HandObjectNameFor(isLeft);
        foreach (var parent in new[] { controller, space, origin.transform })
        {
            if (parent == null) continue;
            foreach (var name in new[] { handName, HandObjectName })
            {
                var old = parent.Find(name);
                if (old != null) Destroy(old.gameObject);
            }
        }

        var source = Instantiate(template);
        // Disable tracking before Bind. Bind reads joint refs then DestroyImmediate's the
        // drivers — a deferred Destroy would still LateUpdate and break finger joints.
        foreach (var driver in source.GetComponentsInChildren<XRHandSkeletonDriver>(true))
            driver.enabled = false;
        foreach (var events in source.GetComponentsInChildren<XRHandTrackingEvents>(true))
            events.enabled = false;
        foreach (var meshCtrl in source.GetComponentsInChildren<XRHandMeshController>(true))
        {
            meshCtrl.enabled = false;
            DestroyImmediate(meshCtrl);
        }
        // Any other tracking scripts on hand prefabs would move or hide the bones.
        foreach (var behaviour in source.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is XRHandSkeletonDriver || behaviour is XRHandTrackingEvents)
                continue;
            behaviour.enabled = false;
        }
        // The hand is only a picture. Hand prefabs (e.g. the scene's Tracked Hands) carry colliders
        // and rigidbodies for their own interactors, which bumped and shoved the tablet and cases.
        // Switched off rather than removed: other (disabled) components may require them.
        foreach (var col in source.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        foreach (var body in source.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        source.SetActive(true);

        var skinned = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinned == null || skinned.sharedMesh == null)
        {
            Debug.LogWarning("XrHandsRig: '" + template.name + "' has no skinned hand mesh.", this);
            Destroy(source);
            return;
        }
        for (var t = skinned.transform; t != null && t != source.transform; t = t.parent)
            t.gameObject.SetActive(true);
        // Only our baked hand mesh is drawn; poke spheres, rays etc. from the hand prefab are not.
        foreach (var r in source.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = true;

        var wrist = FindBone(skinned, source.transform, "wrist") ?? skinned.rootBone ?? skinned.transform;

        var offset = handOnControllerPosition;
        var euler = handOnControllerEuler;
        if (isLeft)
        {
            offset.x = -offset.x;
            euler.y = -euler.y;
            euler.z = -euler.z;
        }

        // On the controller so the mesh follows the tracked grip. Modality manager stays off
        // so the action button cannot park that controller at the origin.
        var anchor = new GameObject(handName).transform;
        if (isLeft) leftAnchor = anchor; else rightAnchor = anchor;
        anchor.SetParent(controller, false);
        anchor.localPosition = offset;
        anchor.localRotation = Quaternion.Euler(0f, 0f, isLeft ? 90f : -90f) * Quaternion.Euler(euler);
        anchor.localScale = Vector3.one;

        // Keep the rig (bones) under the anchor, moved so its wrist sits exactly on the anchor.
        // The sample prefab parks the wrist away from its root; this cancels that offset.
        var rigRoot = source.transform;
        rigRoot.SetParent(anchor, false);
        rigRoot.rotation = anchor.rotation * Quaternion.Inverse(wrist.rotation) * rigRoot.rotation;
        rigRoot.position += anchor.position - wrist.position;

        var meshObject = new GameObject("Hand Mesh");
        meshObject.transform.SetParent(anchor, false);
        meshObject.AddComponent<MeshFilter>();
        var renderer = meshObject.AddComponent<MeshRenderer>();
        var skin = BlueHandMaterial();
        var mats = new Material[Mathf.Max(1, skinned.sharedMesh.subMeshCount)];
        for (var i = 0; i < mats.Length; i++) mats[i] = skin;
        renderer.sharedMaterials = mats;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var rig = anchor.gameObject.AddComponent<ControllerHandRig>();
        // Prefer must be set before Bind — Bind strips XR Hands drivers when prefer is off.
        rig.PreferHandTrackingWhenAvailable(preferHandTrackingWhenAvailable);
        rig.Bind(skinned, meshObject.transform, isLeft, rigRoot, origin.transform);
        indexTip = rig.IndexTip;

        Debug.Log("XrHandsRig: '" + template.name + "' hand on " + controllerName +
                  " (" + skinned.sharedMesh.vertexCount + " vertices, " + rig.FingerBoneCount + " finger bones).", this);
    }

    static Transform FindBone(SkinnedMeshRenderer skinned, Transform root, string contains)
    {
        if (skinned.bones != null)
        {
            foreach (var bone in skinned.bones)
                if (bone != null && bone.name.ToLowerInvariant().Contains(contains)) return bone;
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLowerInvariant().Contains(contains)) return t;
        return null;
    }

    /// <summary>Tip of the index finger: a bone named index…tip/end, else the last bone of the index chain.</summary>
    internal static Transform FindIndexTipBone(SkinnedMeshRenderer skinned, Transform root)
    {
        Transform deepestIndex = null;
        var deepest = -1;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLowerInvariant();
            if (!n.Contains("index")) continue;
            if (n.Contains("tip") || n.Contains("end")) return t;
            var depth = 0;
            for (var p = t; p != null; p = p.parent) depth++;
            if (depth <= deepest) continue;
            deepest = depth;
            deepestIndex = t;
        }
        return deepestIndex;
    }

    /// <summary>Light blue, slightly glowing, like the default XR hand. Opaque Lit so it always renders on Quest.</summary>
    static Material BlueHandMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader) { name = "ProjectHandBlue" };
        var color = new Color(0.38f, 0.68f, 1f, 1f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        mat.color = color;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.55f);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.06f, 0.16f, 0.32f, 1f));
        }
        return mat;
    }

    static void HideControllerMeshes(Transform root)
    {
        if (root == null) return;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            // Controller model, and the white ball the starter rig draws at the poke point.
            if (!child.name.Contains("Controller Visual") &&
                !child.name.Contains("UniversalController") &&
                !child.name.Contains("Poke Point"))
                continue;
            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }
    }

    /// <summary>Controller models back on, and any project hand left on the rig removed.</summary>
    static void ShowControllerMeshes(Transform root)
    {
        if (root == null) return;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == null) continue;
            if (child.name == HandObjectName || child.name.StartsWith("ProjectHand"))
            {
                Destroy(child.gameObject);
                continue;
            }
            if (!child.name.Contains("Controller Visual") && !child.name.Contains("UniversalController"))
                continue;
            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
        }
    }

    static XRPokeInteractor EnsurePokeOnController(XROrigin xrOrigin, string controllerName)
    {
        var controller = FindNamed(xrOrigin, controllerName);
        if (controller == null) return null;

        var poke = controller.GetComponentInChildren<XRPokeInteractor>(true);
        if (poke == null)
        {
            var pokeGo = new GameObject("Poke Interactor");
            pokeGo.transform.SetParent(controller, false);
            pokeGo.transform.localPosition = new Vector3(0f, -0.01f, 0.09f);
            pokeGo.transform.localRotation = Quaternion.identity;
            poke = pokeGo.AddComponent<XRPokeInteractor>();
        }

        poke.gameObject.SetActive(true);
        poke.enabled = true;
        poke.interactionLayers = -1;
        return poke;
    }

    /// <summary>
    /// Leave both controller objects on. XR Input Modality Manager hides a side when that
    /// side's hand tracking wakes up (the controller hand-tracker button), which parked the
    /// hand mesh at the rig origin.
    /// </summary>
    static void KeepControllerObjects(XROrigin xrOrigin)
    {
        if (xrOrigin == null) return;

        foreach (var modality in xrOrigin.GetComponentsInChildren<XRInputModalityManager>(true))
        {
            if (modality.enabled) modality.enabled = false;
        }

        foreach (var name in new[] { "Left Controller", "Right Controller" })
        {
            var controller = FindNamed(xrOrigin, name);
            if (controller != null && !controller.gameObject.activeSelf)
                controller.gameObject.SetActive(true);
        }

        StripHandTrackingUnder(xrOrigin.transform);
    }

    static void StripHandTrackingUnder(Transform root)
    {
        if (root == null) return;
        foreach (var driver in root.GetComponentsInChildren<XRHandSkeletonDriver>(true))
        {
            // ProjectHand already stripped its driver in Bind; anything else under Origin must go.
            if (driver.GetComponentInParent<ControllerHandRig>(true) != null) continue;
            driver.enabled = false;
            Destroy(driver);
        }
        foreach (var events in root.GetComponentsInChildren<XRHandTrackingEvents>(true))
        {
            if (events.GetComponentInParent<ControllerHandRig>(true) != null) continue;
            events.enabled = false;
            Destroy(events);
        }
        foreach (var mesh in root.GetComponentsInChildren<XRHandMeshController>(true))
        {
            mesh.enabled = false;
            Destroy(mesh);
        }
    }

    void ApplyWristOffset(Transform anchor, bool isLeft)
    {
        if (anchor == null) return;
        var offset = handOnControllerPosition;
        var euler = handOnControllerEuler;
        if (isLeft)
        {
            offset.x = -offset.x;
            euler.y = -euler.y;
            euler.z = -euler.z;
        }
        anchor.localPosition = offset;
        anchor.localRotation = Quaternion.Euler(0f, 0f, isLeft ? 90f : -90f) * Quaternion.Euler(euler);
        anchor.localScale = Vector3.one;
    }

    void LateUpdate()
    {
        if (!ready) return;

        KeepControllerObjects(origin);

        // Wrist offset from the inspector, still local on the controller.
        ApplyWristOffset(leftAnchor, true);
        ApplyWristOffset(rightAnchor, false);

        var clickPoint = Vector3.zero;
        var hasClick = false;
        if (rightIndexTip != null && rightIndexTip.gameObject.activeInHierarchy)
        {
            clickPoint = rightIndexTip.position;
            hasClick = true;
        }
        else if (rightPoke != null)
        {
            clickPoint = rightPoke.transform.position;
            hasClick = true;
        }

        if (rightFingerTip == null)
            rightFingerTip = TabletFingerTip.Create("RightFingerTipTouch");

        if (hasClick)
            rightFingerTip.MoveTo(clickPoint);
        else
            rightFingerTip.Park();
    }
}
