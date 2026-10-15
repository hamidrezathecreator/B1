using System;
using UnityEngine;

namespace Animation.Motion.Base
{
    [Serializable]
    public class AutoMotionData
    {
        [Min(0.1f)]
        public float periodInSeconds = 3f;
        public bool initialActive = true;
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public Direction direction = Direction.Forward;
        [HideInInspector]
        public float speedMultiplier = 1f;

        public enum Direction
        {
            Forward,
            Reverse
        }
    }
}
