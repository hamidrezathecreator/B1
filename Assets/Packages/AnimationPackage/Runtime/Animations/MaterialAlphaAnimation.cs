using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    public class MaterialAlphaAnimation : BaseAnimation
    {
        public Material Material;
        [Range(0f,1f)]
        public float From;
        [Range(0f, 1f)]
        public float To;

        protected override void Initialize()
        {
        }

        protected override void Animate(float t)
        {
            var color = Material.color;
            color.a = Mathf.Lerp(From, To, t);
            Material.color = color;
        }
    }
}
