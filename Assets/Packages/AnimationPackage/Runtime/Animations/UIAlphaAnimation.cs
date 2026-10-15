using Animation.Base;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Animations
{
    public class UIAlphaAnimation : BaseAnimation
    {
        [Range(0f, 1f)]
        public float From;
        [Range(0f, 1f)]
        public float To;
        private Graphic[] elements;
        public float currentAlpha { get; private set; }

        protected override void Initialize()
        {
            elements = GetComponentsInChildren<Graphic>();
        }

        protected override void Animate(float t)
        {
            currentAlpha = Mathf.Lerp(From, To, t);
            foreach (var element in elements)
            {
                if (element == null)
                    continue;
                var color = element.color;
                color.a = currentAlpha;
                element.color = color;
            }
        }
    }
}
