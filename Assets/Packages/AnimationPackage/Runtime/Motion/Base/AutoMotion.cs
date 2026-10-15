using System;
using System.Collections;
using UnityEngine;

namespace Animation.Motion.Base
{
    public abstract class AutoMotion : MotionBase
    {
        [SerializeField]
        private AutoMotionData data;
        private bool active = false;
        private Coroutine currentMotionRoutine;


        public bool Active
        {
            get
            {
                return active;
            }
            set
            {
                if (active == value)
                {
                    return;
                }
                active = value;
                if (value)
                {
                    if (currentMotionRoutine != null)
                        StopCoroutine(currentMotionRoutine);
                    currentMotionRoutine = StartCoroutine(MotionRoutiune());
                }
            }
        }

        public float Speed
        {
            get
            {
                return (1f / data.periodInSeconds) * data.speedMultiplier;
            }
        }

        protected virtual void BeforeStart() { }

        private void Start()
        {
            BeforeStart();
            UpdateMotion(data.curve.Evaluate(progress));
            Active = data.initialActive;
            AfterStart();
        }

        private void OnEnable()
        {
            if (active)
            {
                if (currentMotionRoutine != null)
                    StopCoroutine(currentMotionRoutine);
                currentMotionRoutine = StartCoroutine(MotionRoutiune());
            }
        }

        protected virtual void AfterStart() { }

        private IEnumerator MotionRoutiune()
        {
            while (Active)
            {
                progress += Time.deltaTime * Speed;
                progress %= 1f;
                UpdateMotion(data.curve.Evaluate((data.direction == AutoMotionData.Direction.Reverse ? 1 - progress : progress)));
                yield return null;
            }
        }

        public void Initialize(AutoMotionData data)
        {
            this.data = data;
        }

        public void SetSpeedMultiplier(float speedMultiplier)
        {
            data.speedMultiplier = speedMultiplier;
        }
    }
}
