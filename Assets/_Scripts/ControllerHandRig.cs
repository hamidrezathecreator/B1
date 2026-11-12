using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

/// <summary>
/// One hand, two modes:
/// - Hand tracking (controllers put down): the rig follows the real hand through XR Hands,
///   every finger joint moving like the player's own.
/// - Controller in hand: the rig sits on the controller; trigger bends the index, grip the
///   other fingers, the thumb follows the grip halfway.
/// The bones move in a hidden skinned rig and the result is baked into a plain MeshRenderer,
/// because skinned rendering showed nothing on Quest in this project.
/// </summary>
public sealed class ControllerHandRig : MonoBehaviour
{
    // Degrees per joint at a full press.
    const float ProximalCurl = 70f;
    const float IntermediateCurl = 85f;
    const float DistalCurl = 60f;
    const float ThumbCurl = 40f;
    const float Smoothing = 18f;
    const float TrackingLossGraceSeconds = 1f;

    enum Finger { Thumb, Index, Other }

    struct FingerBone
    {
        public Transform bone;
        public Quaternion rest;
        public Finger finger;
        public float maxDegrees;
    }

    struct RestPose
    {
        public Transform transform;
        public Vector3 position;
        public Quaternion rotation;
    }

    readonly List<FingerBone> bones = new List<FingerBone>();
    readonly List<RestPose> restPoses = new List<RestPose>();
    readonly List<InputDevice> devices = new List<InputDevice>();
    readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
    SkinnedMeshRenderer skinned;
    Transform meshTransform;
    MeshFilter meshFilter;
    Mesh baked;
    bool leftHand;
    float index;
    float grip;
    float lastIndex = -1f;
    float lastGrip = -1f;

    Transform rigRoot;
    Transform trackingSpace;
    Transform wristTransform;
    Transform wristOriginalParent;
    Vector3 wristOriginalLocalPosition;
    Quaternion wristOriginalLocalRotation;
    Vector3 rigOnControllerPosition;
    Quaternion rigOnControllerRotation;
    XRHandSkeletonDriver driver;
    XRHandTrackingEvents trackingEvents;
    bool tracked;
    bool preferHandTrackingWhenAvailable;
    float trackingLossTimer;

    public Transform IndexTip { get; private set; }
    public int FingerBoneCount => bones.Count;
    public bool IsHandTracked => tracked;

    public void PreferHandTrackingWhenAvailable(bool prefer) => preferHandTrackingWhenAvailable = prefer;

    /// <param name="rig">Root of the hidden rig, already placed so its wrist sits on this anchor.</param>
    /// <param name="handTrackingSpace">XR Origin camera offset; XR Hands poses are relative to it.</param>
    public void Bind(SkinnedMeshRenderer rigRenderer, Transform meshObject, bool isLeft, Transform rig, Transform handTrackingSpace)
    {
        skinned = rigRenderer;
        meshTransform = meshObject;
        meshFilter = meshObject.GetComponent<MeshFilter>();
        leftHand = isLeft;
        rigRoot = rig;
        trackingSpace = handTrackingSpace;
        rigOnControllerPosition = rigRoot.localPosition;
        rigOnControllerRotation = rigRoot.localRotation;

        // The skinned renderer only feeds the bake; the plain MeshRenderer draws the hand.
        // Kept enabled (just not drawn) so baking always has a live renderer to read.
        skinned.enabled = true;
        skinned.forceRenderingOff = true;
        baked = new Mesh { name = skinned.sharedMesh.name + " (baked)" };
        meshFilter.sharedMesh = baked;

        driver = rigRoot.GetComponentInChildren<XRHandSkeletonDriver>(true);
        trackingEvents = rigRoot.GetComponentInChildren<XRHandTrackingEvents>(true);
        wristTransform = driver != null && driver.rootTransform != null ? driver.rootTransform : skinned.rootBone;
        if (wristTransform != null)
        {
            wristOriginalParent = wristTransform.parent;
            wristOriginalLocalPosition = wristTransform.localPosition;
            wristOriginalLocalRotation = wristTransform.localRotation;
        }
        if (driver != null) driver.enabled = false;
        if (trackingEvents != null) trackingEvents.enabled = false;

        restPoses.Clear();
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == rigRoot) continue;
            restPoses.Add(new RestPose { transform = t, position = t.localPosition, rotation = t.localRotation });
        }

        CollectFingerBones();
        Rebake();
    }

    void CollectFingerBones()
    {
        bones.Clear();
        if (driver != null && driver.jointTransformReferences != null && driver.jointTransformReferences.Count > 0)
        {
            foreach (var reference in driver.jointTransformReferences)
            {
                if (reference.jointTransform == null) continue;
                var id = reference.xrHandJointID;
                if (id == XRHandJointID.IndexTip) IndexTip = reference.jointTransform;
                if (TryClassify(id, out var finger, out var degrees))
                    Add(reference.jointTransform, finger, degrees);
            }
        }
        else
        {
            // Rigs without XR Hands joint references: go by bone names.
            foreach (var bone in skinned.bones)
            {
                if (bone == null) continue;
                var n = bone.name.ToLowerInvariant();
                if (n.Contains("tip") || n.Contains("end") || n.Contains("metacarpal")) continue;
                var finger = n.Contains("thumb") ? Finger.Thumb : n.Contains("index") ? Finger.Index :
                    n.Contains("middle") || n.Contains("ring") || n.Contains("little") || n.Contains("pinky") ? Finger.Other : (Finger?)null;
                if (finger == null) continue;
                var degrees = finger == Finger.Thumb ? ThumbCurl :
                    n.Contains("proximal") ? ProximalCurl : n.Contains("distal") ? DistalCurl : IntermediateCurl;
                Add(bone, finger.Value, degrees);
            }
        }

        if (IndexTip == null)
            IndexTip = XrHandsRig.FindIndexTipBone(skinned, rigRoot);
    }

    void Add(Transform bone, Finger finger, float degrees)
    {
        bones.Add(new FingerBone { bone = bone, rest = bone.localRotation, finger = finger, maxDegrees = degrees });
    }

    static bool TryClassify(XRHandJointID id, out Finger finger, out float degrees)
    {
        finger = Finger.Other;
        degrees = 0f;
        switch (id)
        {
            case XRHandJointID.ThumbProximal:
            case XRHandJointID.ThumbDistal:
                finger = Finger.Thumb; degrees = ThumbCurl; return true;
            case XRHandJointID.IndexProximal:
                finger = Finger.Index; degrees = ProximalCurl; return true;
            case XRHandJointID.IndexIntermediate:
                finger = Finger.Index; degrees = IntermediateCurl; return true;
            case XRHandJointID.IndexDistal:
                finger = Finger.Index; degrees = DistalCurl; return true;
            case XRHandJointID.MiddleProximal:
            case XRHandJointID.RingProximal:
            case XRHandJointID.LittleProximal:
                degrees = ProximalCurl; return true;
            case XRHandJointID.MiddleIntermediate:
            case XRHandJointID.RingIntermediate:
            case XRHandJointID.LittleIntermediate:
                degrees = IntermediateCurl; return true;
            case XRHandJointID.MiddleDistal:
            case XRHandJointID.RingDistal:
            case XRHandJointID.LittleDistal:
                degrees = DistalCurl; return true;
        }
        return false;
    }

    void LateUpdate()
    {
        if (skinned == null || meshTransform == null) return;

        if (IsTrackedByXRHands())
        {
            trackingLossTimer = 0f;
            if (!tracked) EnterHandTracking();
        }
        else if (tracked)
        {
            trackingLossTimer += Time.unscaledDeltaTime;
            if (trackingLossTimer >= TrackingLossGraceSeconds)
                EnterController();
        }

        if (tracked)
        {
            // XR Hands moves every joint itself; follow it every frame.
            meshTransform.SetPositionAndRotation(skinned.transform.position, skinned.transform.rotation);
            Rebake();
            return;
        }

        ReadInput(out var targetIndex, out var targetGrip);
        var k = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
        index = Mathf.Lerp(index, targetIndex, k);
        grip = Mathf.Lerp(grip, targetGrip, k);

        // The baked mesh follows the hidden rig's renderer transform.
        meshTransform.SetPositionAndRotation(skinned.transform.position, skinned.transform.rotation);

        if (Mathf.Abs(index - lastIndex) < 0.01f && Mathf.Abs(grip - lastGrip) < 0.01f) return;

        foreach (var b in bones)
        {
            var amount = b.finger == Finger.Index ? index : b.finger == Finger.Thumb ? grip * 0.5f : grip;
            // XR hand joints bend toward the palm around their local X axis.
            b.bone.localRotation = b.rest * Quaternion.Euler(amount * b.maxDegrees, 0f, 0f);
        }
        Rebake();
    }

    bool IsTrackedByXRHands()
    {
        if (driver == null || trackingEvents == null || trackingSpace == null) return false;

        if (!preferHandTrackingWhenAvailable)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller |
                (leftHand ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right), devices);
            foreach (var device in devices)
            {
                if (!device.TryGetFeatureValue(CommonUsages.isTracked, out var controllerTracked) || controllerTracked)
                    return false;
            }
        }

        SubsystemManager.GetSubsystems(handSubsystems);
        foreach (var subsystem in handSubsystems)
        {
            if (subsystem == null || !subsystem.running) continue;
            var hand = leftHand ? subsystem.leftHand : subsystem.rightHand;
            if (hand.isTracked) return true;
        }
        return false;
    }

    void EnterHandTracking()
    {
        tracked = true;
        trackingLossTimer = 0f;
        // XRHandSkeletonDriver applies the wrist pose locally, so its parent must be tracking space.
        rigRoot.SetParent(trackingSpace, false);
        rigRoot.localPosition = Vector3.zero;
        rigRoot.localRotation = Quaternion.identity;
        if (wristTransform != null)
        {
            wristTransform.SetParent(trackingSpace, false);
            wristTransform.localPosition = Vector3.zero;
            wristTransform.localRotation = Quaternion.identity;
        }
        trackingEvents.enabled = true;
        driver.enabled = true;
    }

    void EnterController()
    {
        tracked = false;
        if (driver != null) driver.enabled = false;
        if (trackingEvents != null) trackingEvents.enabled = false;

        // Back to the open rest pose on the controller.
        foreach (var pose in restPoses)
        {
            if (pose.transform == null) continue;
            pose.transform.localPosition = pose.position;
            pose.transform.localRotation = pose.rotation;
        }
        if (wristTransform != null && wristOriginalParent != null)
        {
            wristTransform.SetParent(wristOriginalParent, false);
            wristTransform.localPosition = wristOriginalLocalPosition;
            wristTransform.localRotation = wristOriginalLocalRotation;
        }
        rigRoot.SetParent(transform, false);
        rigRoot.localPosition = rigOnControllerPosition;
        rigRoot.localRotation = rigOnControllerRotation;

        lastIndex = -1f;
        lastGrip = -1f;
    }

    void Rebake()
    {
        skinned.BakeMesh(baked, false);
        baked.RecalculateBounds();
        lastIndex = index;
        lastGrip = grip;
    }

    void ReadInput(out float trigger, out float squeeze)
    {
        trigger = 0f;
        squeeze = 0f;
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller |
            (leftHand ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right), devices);
        foreach (var device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.trigger, out var t)) trigger = Mathf.Max(trigger, t);
            if (device.TryGetFeatureValue(CommonUsages.grip, out var g)) squeeze = Mathf.Max(squeeze, g);
        }
    }

    void OnDestroy()
    {
        if (baked != null) Destroy(baked);
        if (wristTransform != null && wristOriginalParent != null && wristTransform.parent != wristOriginalParent)
        {
            wristTransform.SetParent(wristOriginalParent, false);
            wristTransform.localPosition = wristOriginalLocalPosition;
            wristTransform.localRotation = wristOriginalLocalRotation;
        }
        if (rigRoot != null && rigRoot.parent != transform) Destroy(rigRoot.gameObject);
    }
}
