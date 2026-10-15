using Animation.Motion.Base;
using UnityEngine;

namespace Animation.Motion.EllipseMotionSystem
{
    public class EllipseAutoMotion : AutoMotion
    {
        [SerializeField]
        private Ellipse ellipse;

        protected override void UpdateMotion(float progress)
        {
            if (ellipse == null)
                return;
            var pos = ellipse.Evaluate(progress);
            if (thisTransform != null)
                thisTransform.localPosition = pos;
            else
                transform.localPosition = pos;
        }
        public EllipseAutoMotion Initialize(Ellipse ellipse, AutoMotionData data, float startAngle)
        {
            progress = startAngle;
            Initialize(data);
            this.ellipse = ellipse;
            return this;
        }
    }
}
