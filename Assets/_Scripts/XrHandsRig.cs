using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Project hand meshes on both controllers, a poke point on each controller, and a
/// right fingertip touch sphere (<see cref="TabletFingerTip"/>). Shared by every level.
/// </summary>
public sealed class XrHandsRig : MonoBehaviour
{
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
        SpawnHand(leftHandVisualPrefab, "Left Hand Tracking", "Left Controller", true);
        SpawnHand(rightHandVisualPrefab, "Right Hand Tracking", "Right Controller", false);
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

    void SpawnHand(GameObject prefab, string handName, string controllerName, bool isLeft)
    {
        if (prefab == null) return;
        var existing = GameObject.Find(handName);
        var hand = existing != null ? existing : Instantiate(prefab);
        hand.name = handName;
        hand.SetActive(true);

        // Turns the mesh off when hand tracking is lost, so a controller build shows nothing.
        var meshController = hand.GetComponent<XRHandMeshController>();
        if (meshController != null)
            meshController.enabled = false;

        var skin = OpaqueHandMaterial();
        foreach (var renderer in hand.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
            // Sample material is transparent Lit; that pass is often stripped from the Quest build.
            if (skin != null)
                renderer.sharedMaterial = skin;
            if (renderer is SkinnedMeshRenderer skinned)
                skinned.updateWhenOffscreen = true;
        }

        BuildStaticHandMesh(hand, skin);

        var pose = hand.GetComponent<ProjectHandPose>();
        if (pose == null)
            pose = hand.AddComponent<ProjectHandPose>();
        var offset = handOnControllerPosition;
        var euler = handOnControllerEuler;
        if (isLeft)
        {
            offset.x = -offset.x;
            euler.y = -euler.y;
            euler.z = -euler.z;
        }
        pose.Bind(FindNamed(origin, controllerName), TrackingSpace(origin), isLeft, offset, euler);
        if (!isLeft)
            rightIndexTip = FindIndexTip(hand);
    }

    /// <summary>
    /// Draws the project hand mesh with a plain MeshRenderer in its rest pose, attached to
    /// the wrist bone. The skinned path (tracking events, bones, GPU skinning) renders nothing
    /// on Quest in this project, while plain MeshRenderers do.
    /// </summary>
    static void BuildStaticHandMesh(GameObject hand, Material skin)
    {
        var skinned = hand.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinned == null || skinned.sharedMesh == null) return;

        var driver = hand.GetComponent<XRHandSkeletonDriver>();
        var wrist = driver != null && driver.rootTransform != null ? driver.rootTransform : skinned.rootBone;
        if (wrist == null) wrist = hand.transform;
        if (wrist.Find("StaticHandMesh") != null) return;

        var mesh = skinned.sharedMesh;
        var bind = Matrix4x4.identity;
        var poses = mesh.bindposes;
        var bones = skinned.bones;
        var index = bones != null ? System.Array.IndexOf(bones, wrist) : -1;
        if (index >= 0 && poses != null && index < poses.Length)
            bind = poses[index];
        else
            bind = wrist.worldToLocalMatrix * skinned.transform.localToWorldMatrix;

        var go = new GameObject("StaticHandMesh");
        go.layer = hand.layer;
        go.transform.SetParent(wrist, false);
        go.transform.localPosition = bind.GetColumn(3);
        go.transform.localRotation = bind.rotation;
        go.transform.localScale = bind.lossyScale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        var count = Mathf.Max(1, mesh.subMeshCount);
        var mats = new Material[count];
        for (var i = 0; i < count; i++)
            mats[i] = skin != null ? skin : skinned.sharedMaterial;
        renderer.sharedMaterials = mats;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        skinned.enabled = false;
    }

    static Transform FindIndexTip(GameObject hand)
    {
        var driver = hand.GetComponent<XRHandSkeletonDriver>();
        if (driver != null && driver.jointTransformReferences != null)
        {
            var refs = driver.jointTransformReferences;
            for (var i = 0; i < refs.Count; i++)
            {
                if (refs[i].xrHandJointID == XRHandJointID.IndexTip && refs[i].jointTransform != null)
                    return refs[i].jointTransform;
            }
        }

        foreach (var t in hand.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("index") && (n.Contains("tip") || n.Contains("end")))
                return t;
        }

        return null;
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
            if (!child.name.Contains("Controller Visual") &&
                !child.name.Contains("UniversalController"))
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

        // Right fingertip touches UI. Tracked joint wins; then the visible index
        // bone on the controller hand; then the controller poke point.
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

/// <summary>
/// Keeps the project hand mesh visible. Tracked hands follow XR Hands.
/// Without tracking the same mesh stays on the controller.
/// </summary>
public sealed class ProjectHandPose : MonoBehaviour
{
    Transform controller;
    Transform trackingSpace;
    Vector3 offsetPosition;
    Quaternion offsetRotation;
    XRHandSkeletonDriver skeleton;
    XRHandTrackingEvents tracking;
    Transform wrist;
    Renderer[] renderers;
    bool onController;
    bool bound;
    bool hasStaticMesh;

    public void Bind(Transform controllerAnchor, Transform handTrackingSpace, bool isLeft,
        Vector3 localOffset, Vector3 localEuler)
    {
        controller = controllerAnchor;
        trackingSpace = handTrackingSpace;
        offsetPosition = localOffset;
        // XR Hands wrist: +Z toward the fingers, +Y out of the back of the hand.
        // Holding a controller the back of the right hand faces +X, the left hand faces -X.
        offsetRotation = Quaternion.Euler(0f, 0f, isLeft ? 90f : -90f) * Quaternion.Euler(localEuler);
        skeleton = GetComponent<XRHandSkeletonDriver>();
        tracking = GetComponent<XRHandTrackingEvents>();
        wrist = skeleton != null ? skeleton.rootTransform : null;
        renderers = GetComponentsInChildren<Renderer>(true);
        hasStaticMesh = false;
        foreach (var r in renderers)
        {
            if (r != null && r.gameObject.name == "StaticHandMesh")
                hasStaticMesh = true;
        }
        onController = false;
        bound = true;
    }

    void LateUpdate()
    {
        if (renderers != null)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                // The static copy draws the hand; keep the skinned original off so it does not double up.
                var show = !(renderers[i] is SkinnedMeshRenderer) || !hasStaticMesh;
                renderers[i].enabled = show;
                if (show && !renderers[i].gameObject.activeSelf)
                    renderers[i].gameObject.SetActive(true);
            }
        }

        if (!bound) return;

        var tracked = tracking != null && tracking.handIsTracked;
        if (tracked)
        {
            // Skeleton writes wrist poses in tracking space, so live under the camera offset.
            if (onController || transform.parent != trackingSpace)
            {
                transform.SetParent(trackingSpace, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                onController = false;
            }
            if (skeleton != null && !skeleton.enabled)
                skeleton.enabled = true;
            return;
        }

        if (skeleton != null && skeleton.enabled)
            skeleton.enabled = false;
        if (controller == null) return;

        if (!onController)
        {
            transform.SetParent(controller, false);
            onController = true;
        }

        // The sample prefab parks the wrist 0.5 m ahead and 0.2 m to the side,
        // which pushed the mesh far away from the controller. Zero it here.
        if (wrist != null && wrist != transform)
        {
            wrist.localPosition = Vector3.zero;
            wrist.localRotation = Quaternion.identity;
        }

        transform.localPosition = offsetPosition;
        transform.localRotation = offsetRotation;
        transform.localScale = Vector3.one;
    }
}
