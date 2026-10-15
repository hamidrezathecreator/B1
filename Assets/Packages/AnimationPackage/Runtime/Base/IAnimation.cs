namespace Animation.Base
{
    public interface IAnimation
    {
        void Play(AnimationDirection direction, AnimPlayMode playMode, System.Action onDone = null);
    }
}