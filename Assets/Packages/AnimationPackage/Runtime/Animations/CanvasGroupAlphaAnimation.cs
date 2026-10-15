using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    [RequireComponent(typeof(CanvasGroup))]
    public class CanvasGroupAlphaAnimation : BaseAnimation
    {
        [Range(0f, 1f)]
        public float From;
        [Range(0f, 1f)]
        public float To;
        private CanvasGroup canvasGroup;
        public float currentAlpha { get; private set; }
        protected override void Animate(float t)
        {
            currentAlpha = Mathf.Lerp(From, To, t);
            canvasGroup.alpha = currentAlpha;
        }

        protected override void Initialize()
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }
}
