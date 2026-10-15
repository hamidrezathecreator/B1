using Animation.Base;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Animations
{
    [RequireComponent(typeof(Graphic))]
    public class ImageColorAnimation : BaseAnimation
    {
        public Color From;
        public Color To;
        private Graphic image;
        private Color currentColor;

        protected override void Initialize()
        {
            image = GetComponent<Graphic>();
        }

        protected override void Animate(float t)
        {
            currentColor = Color.Lerp(From, To, t);
            image.color = currentColor;
        }
    }
}
