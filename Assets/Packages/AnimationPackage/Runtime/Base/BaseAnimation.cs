using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using System;

namespace Animation.Base
{
    public abstract class BaseAnimation : MonoBehaviour, IAnimation
    {
        public float Delay, Duration;
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);
        public ExecuteOrder executeOrder = ExecuteOrder.None;
        public AnimationDirection direction = AnimationDirection.Forward;
        public PlayOrder playOrder = PlayOrder.Once;
        public AnimationState initialState = AnimationState.Default;
        public UnityEvent onStartPlaying = new UnityEvent();
        public UnityEvent onFinishPlaying = new UnityEvent();
        public UnityEvent onStartForward = new UnityEvent();
        public UnityEvent onStartBackward = new UnityEvent();
        public UnityEvent onFinishForward = new UnityEvent();
        public UnityEvent onFinishBackward = new UnityEvent();
        private float currentT;
        private bool initialized = false;
        public AnimationWrapper Wrapper { get; set; }

        public float TotalDuration
        {
            get
            {
                return Duration + Delay;
            }
        }

        public void SetDirection(AnimationDirection direction)
        {
            this.direction = direction;
        }

        public void SetPlayOrder(PlayOrder playOrder)
        {
            this.playOrder = playOrder;
        }

        public void SetExecuteOrder(ExecuteOrder executeOrder)
        {
            this.executeOrder = executeOrder;
        }

        protected virtual void Awake()
        {
            if (!initialized)
            {
                Initialize();
                if (initialState != AnimationState.Default)
                {
                    Play(direction == AnimationDirection.Forward ?
                        (initialState == AnimationState.Start ? AnimationDirection.Backward : AnimationDirection.Forward)
                        : (initialState == AnimationState.Start ? AnimationDirection.Forward : AnimationDirection.Backward),
                        AnimPlayMode.Suddenly);
                }
                initialized = true;
            }
            currentT = direction == AnimationDirection.Forward ? 0f : 1f;
            if (executeOrder == ExecuteOrder.OnAwake)
                Execute();
        }
        protected virtual void Start()
        {
            if (executeOrder == ExecuteOrder.OnStart)
                Execute();
        }
        protected virtual void OnEnable()
        {
            if (executeOrder == ExecuteOrder.OnEnable)
                Execute();
        }

        protected virtual void OnDisable()
        {
            StopAllCoroutines();
        }

        public void Execute()
        {
            switch (playOrder)
            {
                case PlayOrder.Once:
                    Play(direction);
                    break;
                case PlayOrder.Loop:
                    StartCoroutine(PlayLoopRoutine(direction));
                    break;
                case PlayOrder.PingPong:
                    StartCoroutine(PlayPingPongLoopRoutine());
                    break;
            }
        }

        public void Play(AnimationDirection direction, AnimPlayMode playMode, System.Action onDone = null)
        {
            if (Wrapper != null)
            {
                Wrapper.Play(direction, playMode, onDone);
                return;
            }
            if (!initialized)
            {
                Initialize();
                initialized = true;
            }
            enabled = true;
            StopAllCoroutines();
            if (playMode == AnimPlayMode.Suddenly || gameObject.activeInHierarchy == false)
            {
                currentT = direction == AnimationDirection.Forward ? 1 : 0;
                Animate(currentT);
                if (onDone != null)
                {
                    onDone();
                }
            }
            else
            {
                StartCoroutine(PlayRoutine(direction, onDone));
            }
        }
        public void Play(AnimationDirection direction, bool suddenly = false, System.Action onDone = null)
        {
            Play(direction, suddenly ? AnimPlayMode.Suddenly : AnimPlayMode.Lerp, onDone);
        }

        public void Stop()
        {
            StopAllCoroutines();
            currentT = 0;
        }

        public void PlayForward(System.Action onDone = null)
        {
            Play(AnimationDirection.Forward, AnimPlayMode.Lerp, onDone);
        }

        public void PlayBackward(System.Action onDone = null)
        {
            Play(AnimationDirection.Backward, AnimPlayMode.Lerp, onDone);
        }
        public void PlayOnce()
        {
            Play(AnimationDirection.Forward);
        }
        [ContextMenu("PlayForward")]
        public void PlayForward()
        {
            Play(AnimationDirection.Forward);
        }
        [ContextMenu("PlayBackward")]
        public void PlayBackward()
        {
            Play(AnimationDirection.Backward);
        }

        public void PlayPingPong()
        {
            PlayPingPong(direction);
        }

        public void PlayPingPong(AnimationDirection direction)
        {
            if (!initialized)
            {
                Initialize();
                initialized = true;
            }
            enabled = true;
            StopAllCoroutines();
            if (gameObject.activeInHierarchy)
                StartCoroutine(PlayPingPongLoopRoutine());
        }

        public void PlayLoop(AnimationDirection direction)
        {
            StartCoroutine(PlayLoopRoutine(direction));
        }

        private IEnumerator PlayRoutine(AnimationDirection direction, System.Action onDone = null)
        {
            onStartPlaying.Invoke();
            if (direction == AnimationDirection.Forward)
            {
                onStartForward.Invoke();
            }
            else
            {
                onStartBackward.Invoke();
            }
            yield return StartCoroutine(AnimationUtilities.
                AnimationRoutine(Delay, Duration,
                direction, ExecuteAnimate,currentT,true, () => { OnAnimationFinish(onDone); if (direction == AnimationDirection.Forward) { onFinishForward.Invoke(); } else { onFinishBackward.Invoke(); } }));
        }

        private IEnumerator PlayPingPongRoutine(AnimationDirection direction)
        {
            yield return StartCoroutine(
                PlayRoutine(direction));
            yield return StartCoroutine(
                PlayRoutine(direction == AnimationDirection.Forward ? AnimationDirection.Backward : AnimationDirection.Forward));
        }

        private IEnumerator PlayLoopRoutine(AnimationDirection direction)
        {
            while (true)
            {
                yield return StartCoroutine(PlayRoutine(direction));
                currentT = direction == AnimationDirection.Forward ? 0f : 1f;
            }
        }

        private IEnumerator PlayPingPongLoopRoutine()
        {
            while (true)
            {
                yield return StartCoroutine(PlayPingPongRoutine(direction));
            }
        }

        private void updateT(float t)
        {
            currentT = t;
        }

        internal void ExecuteAnimate(float t)
        {
            currentT = t;
            Animate(Curve.Evaluate(t));
        }

        private void OnAnimationFinish(Action onDone)
        {
            if (onDone != null)
            {
                onDone.Invoke();
            }
            onFinishPlaying.Invoke();
        }

        protected abstract void Initialize();

        protected abstract void Animate(float t);
    }
    public enum ExecuteOrder
    {
        None = 0,
        OnAwake = 1,
        OnStart = 2,
        OnEnable = 3
    }
}
