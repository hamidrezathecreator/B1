using Animation.Motion.Base;
using UnityEngine;

namespace Animation.Motion.EllipseMotionSystem
{
    public class EllipseMotion : MotionBase
    {
        [SerializeField]
        protected Ellipse ellipse;

        protected override void UpdateMotion(float progress)
        {
            if (ellipse == null)
            {
                return;
            }
            var pos = ellipse.Evaluate(progress);
            if (thisTransform != null)
                thisTransform.localPosition = pos;
            else
                transform.localPosition = pos;
        }
        public void SetEllipse(Ellipse ellipse)
        {
            this.ellipse = ellipse;
        }
    }
}
