using System.Collections;
using UnityEngine;
using System; 

namespace Animation.Base
{
    public static class AnimationUtilities
    {
        public static IEnumerator AnimationRoutine(float delay,
            float duration, AnimationDirection direction,
            Action<float> tAction, float initialT = -1,
            bool realTime = false, Action onFinish = null)
        {
            var t = initialT == -1 ? 
                (direction == AnimationDirection.Forward ? 0f : 1f) : initialT;
            if (delay > 0f && (t == 0f || t == 1f))
            {
                if (realTime)
                {
                    yield return new WaitForSecondsRealtime(delay);
                }
                else
                {
                    yield return new WaitForSeconds(delay);
                }
            }
            if (duration == 0)
            {
                tAction(direction == AnimationDirection.Forward ? 1f: 0f);
                yield break;
            }
            var startTime = (realTime ? Time.unscaledTime : Time.time) - (duration * ( direction == AnimationDirection.Forward ? t : 1 - t));
            //duration = duration * (direction == AnimationDirection.Forward ? (1 - t) : t);
            while (direction == AnimationDirection.Forward ? t < 1f : t > 0f)
            {
                t = ((realTime ? Time.unscaledTime : Time.time) - startTime) / duration;
                if (direction == AnimationDirection.Backward)
                {
                    t = 1 - t;
                }
                tAction(t);
                yield return null;
            }
            tAction(direction == AnimationDirection.Forward ? 1f : 0f);
            onFinish?.Invoke();
        }

        public static IEnumerator IntervalRoutine(float delay,
            float interval, Action intervalAction, Func<bool> finishCondition,
             Action onFinish = null, bool realTime = false)
        {
            if (interval <= 0f)
            {
                yield break;
            }
            if (realTime)
            {
                yield return new WaitForSecondsRealtime(delay);
            }
            else
            {
                yield return new WaitForSeconds(delay);
            }
            while(finishCondition() == false)
            {
                if (realTime)
                {
                    yield return new WaitForSecondsRealtime(interval);
                }
                else
                {
                    yield return new WaitForSeconds(interval);
                }
                intervalAction();
            }
            onFinish?.Invoke();
        }
    }
    public enum AnimationDirection
    {
        Forward = 0,
        Backward = 1
    }
    public enum AnimationState
    {
        Default = 1,
        Start = 2,
        End = 3
    }
}
