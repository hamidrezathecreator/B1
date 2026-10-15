using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    public class RectTransformAnimation : BaseAnimation
    {
        [SerializeField] private RectTransform from;
        [SerializeField] private RectTransform to;
        public RectTransform From
        {
            get => from;
            set
            {
                from = value;
            }
        }
        public RectTransform To
        {
            get => to;
            set
            {
                to = value;
            }
        }
        private RectTransform thisTransform;

        protected override void Animate(float t)
        {
            thisTransform.anchoredPosition = Vector2.Lerp(from.anchoredPosition, to.anchoredPosition, t);
            thisTransform.sizeDelta = Vector2.Lerp(from.sizeDelta, to.sizeDelta, t);
            thisTransform.anchorMin = Vector2.Lerp(from.anchorMin, to.anchorMin, t);
            thisTransform.anchorMax = Vector2.Lerp(from.anchorMax, to.anchorMax, t);
            thisTransform.offsetMin = Vector2.Lerp(from.offsetMin, to.offsetMin, t);
            thisTransform.offsetMax = Vector2.Lerp(from.offsetMax, to.offsetMax, t);
        }

        protected override void Initialize()
        {
            thisTransform = transform as RectTransform;
        }
    }
}
