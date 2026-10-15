using System;
using UnityEngine;

namespace Animation.Base
{
    [Serializable]
    public class AnimationConfig
    {
        public float Delay = 0f;
        public float Duration = 1f;
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);
    }
}
