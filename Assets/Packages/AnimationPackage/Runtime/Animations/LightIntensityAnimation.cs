using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    [RequireComponent(typeof(Light))]
    public class LightIntensityAnimation : BaseAnimation
    {
        public float From = 0f;
        public float To = 3.5f;
        private Light _light;

        protected override void Initialize()
        {
            _light = GetComponent<Light>();
        }

        protected override void Animate(float t)
        {
            _light.intensity = Mathf.Lerp(From, To, t);
        }
    }
}
