using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    [RequireComponent(typeof(RectTransform))]
    public class RectDimensionAnimation : BaseAnimation
    {
        public Vector2 From;
        public Vector2 To;
        private RectTransform rectTransform;

        protected override void Initialize()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        protected override void Animate(float t)
        {
            rectTransform.sizeDelta = Vector2.Lerp(From, To, t);
        }        
    }
}
