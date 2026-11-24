using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ControllerHandPose
{
    [Serializable]
    public struct Joint
    {
        public string path;
        public Quaternion rotation;
    }

    [Serializable]
    public struct FingerAdjustment
    {
        public Vector3 jointBends;
        public float spread;
        public float twist;
        public float opposition;
    }

    public bool saved;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public float[] fingerCurls = new float[5];
    public FingerAdjustment[] fingerAdjustments = new FingerAdjustment[5];
    public List<Joint> joints = new List<Joint>();
}
