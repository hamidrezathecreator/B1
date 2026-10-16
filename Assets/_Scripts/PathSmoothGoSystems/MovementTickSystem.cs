using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GoWhere
{
    public interface ITickableMovement
    {
        bool   IsAlive { get; }
        void   MovementTick(float tickDelta);
        void   SmoothStep(float interpolationSpeed, float frameDelta);
    }

    public class MovementTickSystem : MonoBehaviour
    {
        public static MovementTickSystem Instance { get; private set; }

        [Header("Tick Rate")]
        [Range(5, 60)] [SerializeField] private int tickRate = 15;

        [Header("Interpolation")]
        [SerializeField] private bool interpolate = true;
        [Range(2f, 20f)] [SerializeField] private float interpolationSpeed = 8f;

        private readonly List<ITickableMovement> _registered = new(32);
        private readonly List<ITickableMovement> _tickSnapshot = new(32);

        public static MovementTickSystem EnsureInstance()
        {
            if (Instance == null)
                new GameObject("Movement Tick System").AddComponent<MovementTickSystem>();
            return Instance;
        }
        private WaitForSeconds _tickWait;
        private float TickInterval => 1f / tickRate;

        private void OnValidate() => tickRate = Mathf.Clamp(tickRate, 5, 60);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _tickWait = new WaitForSeconds(TickInterval);
            StartCoroutine(TickLoop());
        }

        private IEnumerator TickLoop()
        {
            while (true)
            {
                float tickDelta = TickInterval;

                _tickSnapshot.Clear();
                _tickSnapshot.AddRange(_registered);
                for (int i = _tickSnapshot.Count - 1; i >= 0; i--)
                {
                    var mover = _tickSnapshot[i];
                    if (mover == null || !mover.IsAlive)
                    {
                        _registered.Remove(mover);
                        continue;
                    }
                    mover.MovementTick(tickDelta);
                    if (!interpolate && mover.IsAlive) mover.SmoothStep(float.PositiveInfinity, tickDelta);
                }

                yield return _tickWait;
            }
        }

        private void Update()
        {
            if (!interpolate) return;

            float frameDelta = Time.deltaTime;
            for (int i = _registered.Count - 1; i >= 0; i--)
            {
                var mover = _registered[i];
                if (mover == null || !mover.IsAlive) continue;
                mover.SmoothStep(interpolationSpeed, frameDelta);
            }
        }

        public void Register(ITickableMovement mover)
        {
            if (mover != null && !_registered.Contains(mover))
                _registered.Add(mover);
        }

        public void Unregister(ITickableMovement mover)
        {
            _registered.Remove(mover);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}