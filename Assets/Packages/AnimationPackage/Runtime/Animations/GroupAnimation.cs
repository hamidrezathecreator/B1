using Animation.Base;
using UnityEngine;

namespace Animations
{
    public class GroupAnimation : BaseAnimation
    {
        [SerializeField] private BaseAnimation[] animations;

        public BaseAnimation[] Animations
        {
            get => animations;
            set => animations = value;
        }

        protected override void Initialize()
        {
            foreach (var anim in animations)
            {
                anim.SetExecuteOrder(ExecuteOrder.None);
                anim.Delay = Delay;
                anim.Duration = Duration;
                Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
        }

        protected override void Animate(float t)
        {
            foreach (var anim in animations)
            {
                anim.ExecuteAnimate(t);
            }
        }
    }
}