using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
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

    [Tooltip("Rigged hand model or prefab, e.g. LeftHandAndroidXR.fbx.")]
    [SerializeField] GameObject leftHandVisualPrefab;
    [SerializeField] GameObject rightHandVisualPrefab;
    [Tooltip("Wrist position on the controller (right hand; left is mirrored on X).")]
    [SerializeField] Vector3 handOnControllerPosition = new Vector3(-0.01f, -0.03f, -0.07f);
    [Tooltip("Extra wrist rotation on the controller, degrees.")]
    [SerializeField] Vector3 handOnControllerEuler = Vector3.zero;

    readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
    XROrigin origin;
    XRPokeInteractor leftPoke;
    XRPokeInteractor rightPoke;
    Transform rightIndexTip;
    TabletFingerTip rightFingerTip;
    bool ready;

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

        HideControllerMeshes(origin.transform);
        BuildControllerHand(leftHandVisualPrefab, "Left Controller", true, out _);
        BuildControllerHand(rightHandVisualPrefab, "Right Controller", false, out rightIndexTip);
        leftPoke = EnsurePokeOnController(origin, "Left Controller");
        rightPoke = EnsurePokeOnController(origin, "Right Controller");
        ready = true;
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

        // No prefab assigned (or the reference went missing): use the default XR hand in the scene.
        var template = prefab != null ? prefab : FindSceneHand(isLeft);
        if (template == null)
        {
            Debug.LogWarning("XrHandsRig: no hand model for '" + controllerName + "'. Assign a hand prefab.", this);
            return;
        }

        var old = controller.Find(HandObjectName);
        if (old != null) Destroy(old.gameObject);

        var source = Instantiate(template);
        // Tracking scripts on hand prefabs would move or hide the bones; the rest pose is what we want.
        foreach (var behaviour in source.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
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

        var wrist = FindBone(skinned, source.transform, "wrist") ?? skinned.rootBone ?? skinned.transform;
        var tipBone = FindIndexTipBone(skinned, source.transform);

        var baked = new Mesh { name = template.name + " (baked)" };
        skinned.BakeMesh(baked, false);

        // Without scale: BakeMesh(…, false) bakes for the renderer's position and rotation only,
        // and the hand keeps its real size whatever scale the rig carries.
        var meshFrame = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
        var wristFrame = Matrix4x4.TRS(wrist.position, wrist.rotation, Vector3.one);
        var meshInWrist = wristFrame.inverse * meshFrame;
        var tipInWrist = tipBone != null ? wristFrame.inverse.MultiplyPoint3x4(tipBone.position) : (Vector3?)null;
        Destroy(source);

        var offset = handOnControllerPosition;
        var euler = handOnControllerEuler;
        if (isLeft)
        {
            offset.x = -offset.x;
            euler.y = -euler.y;
            euler.z = -euler.z;
        }

        // Wrist anchor: XR hand convention, +Z toward the fingers, +Y out of the back of the hand.
        // Holding a controller the back of the right hand faces +X, the left hand faces -X.
        var anchor = new GameObject(HandObjectName).transform;
        anchor.SetParent(controller, false);
        anchor.localPosition = offset;
        anchor.localRotation = Quaternion.Euler(0f, 0f, isLeft ? 90f : -90f) * Quaternion.Euler(euler);
        anchor.localScale = Vector3.one;

        var meshObject = new GameObject("Hand Mesh");
        meshObject.transform.SetParent(anchor, false);
        meshObject.transform.localPosition = meshInWrist.GetColumn(3);
        meshObject.transform.localRotation = meshInWrist.rotation;
        meshObject.transform.localScale = Vector3.one;
        meshObject.AddComponent<MeshFilter>().sharedMesh = baked;

        var renderer = meshObject.AddComponent<MeshRenderer>();
        var skin = OpaqueHandMaterial();
        var mats = new Material[Mathf.Max(1, baked.subMeshCount)];
        for (var i = 0; i < mats.Length; i++) mats[i] = skin;
        renderer.sharedMaterials = mats;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (tipInWrist.HasValue)
        {
            var tip = new GameObject("Index Tip").transform;
            tip.SetParent(anchor, false);
            tip.localPosition = tipInWrist.Value;
            indexTip = tip;
        }

        Debug.Log("XrHandsRig: '" + template.name + "' hand on " + controllerName +
                  " (" + baked.vertexCount + " vertices, size " + baked.bounds.size.magnitude.ToString("0.00") + " m).", this);
    }

    /// <summary>The default XR hand already in the scene (e.g. Tracked Hands → Left Hand).</summary>
    static GameObject FindSceneHand(bool isLeft)
    {
        var side = isLeft ? "left" : "right";
        foreach (var skinned in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (skinned.sharedMesh == null) continue;
            // Walk up to the object named after the hand side, e.g. "Left Hand".
            for (var t = skinned.transform; t != null; t = t.parent)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains(side) && n.Contains("hand"))
                    return t.gameObject;
            }
        }
        return null;
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
    static Transform FindIndexTipBone(SkinnedMeshRenderer skinned, Transform root)
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

    static Material OpaqueHandMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader) { name = "ProjectHandOpaque" };
        var color = new Color(0.72f, 0.78f, 0.84f, 1f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        mat.color = color;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.35f);
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

    static Transform TrackingSpace(XROrigin xrOrigin)
    {
        if (xrOrigin == null) return null;
        return xrOrigin.CameraFloorOffsetObject != null
            ? xrOrigin.CameraFloorOffsetObject.transform
            : xrOrigin.transform;
    }

    static bool TryIndexTip(XRHand hand, out Vector3 tip)
    {
        tip = default;
        if (!hand.isTracked) return false;
        var joint = hand.GetJoint(XRHandJointID.IndexTip);
        if (!joint.TryGetPose(out var pose)) return false;
        tip = pose.position;
        return true;
    }

    void LateUpdate()
    {
        if (!ready) return;

        SubsystemManager.GetSubsystems(handSubsystems);
        Vector3 leftTip = default, rightTip = default;
        var hasLeft = false;
        var hasRight = false;
        foreach (var subsystem in handSubsystems)
        {
            if (subsystem == null || !subsystem.running) continue;
            hasLeft |= TryIndexTip(subsystem.leftHand, out leftTip);
            hasRight |= TryIndexTip(subsystem.rightHand, out rightTip);
        }

        // XR Hands joint poses are in tracking space, not world space.
        var space = TrackingSpace(origin);
        if (space != null)
        {
            if (hasLeft) leftTip = space.TransformPoint(leftTip);
            if (hasRight) rightTip = space.TransformPoint(rightTip);
        }

        if (hasLeft && leftPoke != null)
            leftPoke.transform.position = leftTip;

        // Right fingertip touches UI. Tracked joint wins; then the index tip of the hand on the
        // controller; then the controller poke point.
        var clickPoint = Vector3.zero;
        var hasClick = false;
        if (hasRight)
        {
            clickPoint = rightTip;
            hasClick = true;
        }
        else if (rightIndexTip != null && rightIndexTip.gameObject.activeInHierarchy)
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
        {
            if (rightPoke != null && (hasRight || rightIndexTip != null))
                rightPoke.transform.position = clickPoint;
            rightFingerTip.MoveTo(clickPoint);
        }
        else
        {
            rightFingerTip.Park();
        }
    }
}
