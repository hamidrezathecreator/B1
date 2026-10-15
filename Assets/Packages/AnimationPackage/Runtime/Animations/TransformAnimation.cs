using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    public class TransformAnimation : BaseAnimation
    {
        public Transform From;
        public Transform To;
        private Transform thisTransform;

        protected override void Initialize()
        {
            thisTransform = GetComponent<Transform>();
        }

        protected override void Animate(float t)
        {
            thisTransform.position =
                Vector3.Lerp(From.position, To.position, t);
            thisTransform.rotation =
                Quaternion.Lerp(From.rotation, To.rotation, t);
        }
    }
}
