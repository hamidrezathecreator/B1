using Animation.Base;
using Animation.Motion.SphereMotionSystem;
using UnityEngine;

namespace Animation.Misc.MiscAnimations
{
    [RequireComponent(typeof(SphereMotion))]
    public class SphereMotionAnimation : BaseAnimation
    {
        [SerializeField] private Vector2 fromRotation;
        [SerializeField] private Vector2 toRotation;

        public Vector2 FromRotation
        {
            get => fromRotation;
            set => fromRotation = value;
        }

        public Vector2 ToRotation
        {
            get => toRotation;
            set => toRotation = value;
        }
        
        private SphereMotion sphereMotion;
        protected override void Initialize()
        {
            sphereMotion = GetComponent<SphereMotion>();
        }

        protected override void Animate(float t)
        {
            var targetRotation = Vector2.Lerp(fromRotation, toRotation, t);
            var targetProgress = sphereMotion.RotationToProgress(targetRotation);
            sphereMotion.Progress = targetProgress;
        }
    }
}