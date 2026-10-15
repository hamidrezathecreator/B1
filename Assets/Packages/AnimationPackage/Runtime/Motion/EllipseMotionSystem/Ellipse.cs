using System;
using Animation.Motion.Base;
using UnityEngine;

namespace Animation.Motion.EllipseMotionSystem
{
    [Serializable]
    public class Ellipse
    {
        [SerializeField]
        private AxisPair axis = AxisPair.XZ;
        [SerializeField]
        private float axisA;
        [SerializeField]
        private float axisB;

        public float a { get { return axisA; } }
        public float b { get { return axisB; } }

        public Ellipse(float axisA, float axisB)
        {
            this.axisA = axisA;
            this.axisB = axisB;
        }

        public void Set(float axisA, float axisB)
        {
            this.axisA = axisA;
            this.axisB = axisB;
        }

        public Vector3 Evaluate(float t)
        {
            float angle = Mathf.Deg2Rad * 360f * t;
            float a = Mathf.Sin(angle) * axisA;
            float b = Mathf.Cos(angle) * axisB;
            switch (axis)
            {
                case AxisPair.XZ:
                    return new Vector3(a, 0f, b);
                case AxisPair.XY:
                    return new Vector3(a, b, 0f);
                case AxisPair.YZ:
                    return new Vector3(0f, a, b);
                default:
                    throw new NotSupportedException("Invalid Axis Value");
            }
        }
    }
}
