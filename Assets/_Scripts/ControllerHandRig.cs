using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

/// <summary>
/// Curls the fingers of a controller hand from the controller buttons: trigger bends the index,
/// grip bends the other fingers, the thumb follows the grip halfway. The bones move in a hidden
/// skinned rig and the result is baked into a plain MeshRenderer each time it changes, because
/// skinned rendering showed nothing on Quest in this project.
/// </summary>
public sealed class ControllerHandRig : MonoBehaviour
{
    // Degrees per joint at a full press.
    const float ProximalCurl = 70f;
    const float IntermediateCurl = 85f;
    const float DistalCurl = 60f;
    const float ThumbCurl = 40f;
    const float Smoothing = 18f;

    enum Finger { Thumb, Index, Other }

    struct FingerBone
    {
        public Transform bone;
        public Quaternion rest;
        public Finger finger;
        public float maxDegrees;
    }

    readonly List<FingerBone> bones = new List<FingerBone>();
    readonly List<InputDevice> devices = new List<InputDevice>();
    SkinnedMeshRenderer skinned;
    Transform meshTransform;
    MeshFilter meshFilter;
    Mesh baked;
    bool leftHand;
    float index;
    float grip;
    float lastIndex = -1f;
    float lastGrip = -1f;

    public Transform IndexTip { get; private set; }
    public int FingerBoneCount => bones.Count;

    public void Bind(SkinnedMeshRenderer rigRenderer, Transform meshObject, bool isLeft)
    {
        skinned = rigRenderer;
        meshTransform = meshObject;
        meshFilter = meshObject.GetComponent<MeshFilter>();
        leftHand = isLeft;

        // The skinned renderer only feeds the bake; the plain MeshRenderer draws the hand.
        // Kept enabled (just not drawn) so baking always has a live renderer to read.
        skinned.enabled = true;
        skinned.forceRenderingOff = true;
        baked = new Mesh { name = skinned.sharedMesh.name + " (baked)" };
        meshFilter.sharedMesh = baked;

        CollectFingerBones();
        Rebake();
    }

    void CollectFingerBones()
    {
        bones.Clear();
        var driver = skinned.GetComponentInParent<XRHandSkeletonDriver>(true);
        if (driver == null) driver = skinned.transform.root.GetComponentInChildren<XRHandSkeletonDriver>(true);

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
            IndexTip = XrHandsRig.FindIndexTipBone(skinned, skinned.transform.root);
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
    }
}
