using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    public class LocalPositionAnimation : BaseAnimation
    {
        public Vector3 From;
        public Vector3 To;

        private Vector3 currentLocalPos;

        protected override void Initialize()
        {
        }

        protected override void Animate(float t)
        {
            if (From == Vector3.zero)
            {
                From = transform.position;
            }
            currentLocalPos = Vector3.Lerp(From, To, t);
            transform.localPosition = currentLocalPos;
        }
    }
}
