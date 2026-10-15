using System;
using UnityEngine;

namespace Animation.Base
{
    public class AnimationWrapper : MonoBehaviour, IAnimation
    {
        [SerializeField] private BaseAnimation[] animations;

        private void Awake()
        {
            foreach (var animation in animations)
            {
                animation.Wrapper = this;
            }
        }
        
        public void Play(AnimationDirection direction, AnimPlayMode playMode, Action onDone = null)
        {
            foreach (var animation in animations)
            {
                animation.Wrapper = null;
                animation.Play(direction, playMode, onDone);
                animation.Wrapper = this;
            }
        }
    }
}