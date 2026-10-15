using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    [RequireComponent(typeof(TextMesh))]
    public class TextMeshColorAnimation : BaseAnimation
    {
        public Color From;
        public Color To;
        private TextMesh textMesh;
        private Color currentColor;

        protected override void Initialize()
        {
            textMesh = GetComponent<TextMesh>();
        }

        protected override void Animate(float t)
        {
            currentColor = Color.Lerp(From, To, t);
            textMesh.color = currentColor;
        }
    }
}
