using System;
using PrimeTween;
using UnityEngine;

namespace GoWhere
{
    /// <summary>Animates tire meshes from the path agent's speed and heading changes.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(GlobalPathDriver))]
    [AddComponentMenu("GoWhere/Car Wheel Animator")]
    public class CarWheelAnimator : MonoBehaviour
    {
        [Serializable]
        public class Wheel
        {
            public Transform tire;
            [Tooltip("Tire radius in world units. Controls spin speed.")]
            [Min(0.01f)] public float radius = 0.35f;
            [Tooltip("Axle axis in the tire mesh's own local space.")]
            public Vector3 spinAxis = Vector3.right;
            [Tooltip("Enable if this mesh spins the wrong way, for example a mirrored wheel.")]
            public bool invertSpin;
            [Tooltip("Enable for wheels that steer, usually the front pair.")]
            public bool steer;

            [NonSerialized] internal Transform cachedTire;
            [NonSerialized] internal Quaternion restRotation;
            [NonSerialized] internal float spinAngle;
            [NonSerialized] internal float previousPhase;
            [NonSerialized] internal float spinDirection = 1f;
            [NonSerialized] internal Quaternion steeringRotation = Quaternion.identity;
            [NonSerialized] internal Tween spinTween;

            internal void StartSpin()
            {
                if (tire == null) return;
                if (cachedTire != tire)
                {
                    if (cachedTire != null) cachedTire.localRotation = restRotation;
                    cachedTire = tire;
                    restRotation = tire.localRotation;
                }
                if (spinTween.isAlive) spinTween.Stop();
                spinAngle = 0f;
                previousPhase = 0f;
                steeringRotation = Quaternion.identity;
                spinTween = Tween.Custom(this,
                    new TweenSettings<float>(0f, 360f,
                        new TweenSettings(SpinCycleDuration, Ease.Linear, cycles: -1, updateType: UpdateType.LateUpdate)),
                    static (wheel, phase) => wheel.ApplySpin(phase));
                spinTween.timeScale = 0f;
            }

            private void ApplySpin(float phase)
            {
                float delta = phase - previousPhase;
                if (delta < 0f) delta += 360f;
                previousPhase = phase;
                spinAngle = Mathf.Repeat(spinAngle + delta * spinDirection, 360f);
                ApplyRotation();
            }

            internal void ApplyRotation()
            {
                if (cachedTire == null) return;
                Vector3 axis = spinAxis.sqrMagnitude > 0.000001f ? spinAxis.normalized : Vector3.right;
                cachedTire.localRotation = steeringRotation * restRotation * Quaternion.AngleAxis(spinAngle, axis);
            }
        }

        private const float SpinCycleDuration = 0.01f;
        [SerializeField] private Wheel[] wheels = Array.Empty<Wheel>();
        [Tooltip("Distance between the front and rear axles in world units. Used to estimate steering from turning speed.")]
        [Min(0.01f)] [SerializeField] private float wheelbase = 2.5f;
        [Range(0f, 80f)] [SerializeField] private float maxSteeringAngle = 35f;
        [Tooltip("How quickly the steering wheels turn, in degrees per second. Zero snaps to the target angle.")]
        [Min(0f)] [SerializeField] private float steeringSpeed = 180f;
        [Tooltip("Camera used for wheel distance culling. Leave empty to use the main camera.")]
        [SerializeField] private Camera cullingCamera;
        [Tooltip("Pause wheel spin and steering beyond this camera distance, in world units. Zero disables culling.")]
        [Min(0f)] [SerializeField] private float cullDistance = 60f;

        private PathWalker _walker;
        private Camera _mainCamera;
        private bool _isCulled;
        private Vector3 _previousForward;
        private float _steeringAngle;
        private float _targetSteering;
        private Tween _steeringTween;

        private void OnEnable()
        {
            _walker = GetComponent<PathWalker>();
            _isCulled = false;
            foreach (var wheel in wheels) wheel?.StartSpin();
            ResetHeading();
            UpdateDistanceCulling();
            if (_walker == null) return;
            _walker.OnPathStarted += ResetHeading;
            _walker.OnLoopCompleted += ResetHeading;
        }

        private void OnDisable()
        {
            if (_walker != null)
            {
                _walker.OnPathStarted -= ResetHeading;
                _walker.OnLoopCompleted -= ResetHeading;
            }
            if (_steeringTween.isAlive) _steeringTween.Stop();
            foreach (var wheel in wheels)
            {
                if (wheel == null) continue;
                if (wheel.spinTween.isAlive) wheel.spinTween.Stop();
                if (wheel.cachedTire != null) wheel.cachedTire.localRotation = wheel.restRotation;
                wheel.spinAngle = 0f;
            }
        }

        private void ResetHeading()
        {
            _previousForward = transform.forward;
            if (_steeringTween.isAlive) _steeringTween.Stop();
            _steeringAngle = 0f;
            _targetSteering = 0f;
            if (!_isCulled) ApplyWheelPoses();
        }

        private void Update()
        {
            UpdateWheelTargets(Time.deltaTime);
        }

        private void UpdateWheelTargets(float deltaTime)
        {
            if (UpdateDistanceCulling()) return;
            if (_walker == null || deltaTime <= 0f) return;

            float speed = _walker.isActiveAndEnabled && _walker.IsMoving ? _walker.CurrentSpeed : 0f;
            // Backward routes still drive nose-first. Only actual backing changes tire spin direction.
            float signedSpeed = _walker.IsCurrentSegmentReversed ? -speed : speed;
            Vector3 previousHeading = Vector3.ProjectOnPlane(_previousForward, transform.up);
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, transform.up);
            float yawRate = Vector3.SignedAngle(previousHeading, heading, transform.up)
                * Mathf.Deg2Rad / deltaTime;
            _previousForward = transform.forward;
            float targetSteering = Mathf.Abs(signedSpeed) > 0.01f
                ? Mathf.Atan(wheelbase * yawRate / signedSpeed) * Mathf.Rad2Deg : 0f;
            targetSteering = Mathf.Clamp(targetSteering, -maxSteeringAngle, maxSteeringAngle);
            foreach (var wheel in wheels)
            {
                if (wheel == null) continue;
                if (wheel.tire == null)
                {
                    if (wheel.spinTween.isAlive) wheel.spinTween.Stop();
                    continue;
                }
                if (wheel.cachedTire != wheel.tire || !wheel.spinTween.isAlive) wheel.StartSpin();
                wheel.spinDirection = (signedSpeed < 0f ? -1f : 1f) * (wheel.invertSpin ? -1f : 1f);
                wheel.spinTween.timeScale = Mathf.Abs(signedSpeed) * SpinCycleDuration
                    / (2f * Mathf.PI * Mathf.Max(0.01f, wheel.radius));
            }
            SetSteering(targetSteering);
            ApplyWheelPoses();
        }

        private bool UpdateDistanceCulling()
        {
            bool culled = false;
            if (cullDistance > 0f)
            {
                Camera camera = cullingCamera;
                if (camera == null)
                {
                    if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
                        _mainCamera = Camera.main;
                    camera = _mainCamera;
                }
                if (camera != null)
                    culled = (camera.transform.position - transform.position).sqrMagnitude
                        > cullDistance * cullDistance;
            }

            if (culled == _isCulled) return culled;
            _isCulled = culled;
            foreach (var wheel in wheels)
            {
                if (wheel != null && wheel.spinTween.isAlive) wheel.spinTween.isPaused = culled;
            }
            if (_steeringTween.isAlive) _steeringTween.isPaused = culled;
            // Heading changes while offscreen must not become one large steering input on return.
            if (!culled) ResetHeading();
            return culled;
        }

        private void SetSteering(float target)
        {
            if (_steeringTween.isAlive && Mathf.Abs(target - _targetSteering) < 0.5f) return;
            if (!_steeringTween.isAlive && Mathf.Abs(target - _steeringAngle) < 0.01f) return;
            if (_steeringTween.isAlive) _steeringTween.Stop();
            _targetSteering = target;
            if (steeringSpeed <= 0f || Mathf.Abs(target - _steeringAngle) < 0.01f)
            {
                _steeringAngle = target;
                ApplyWheelPoses();
                return;
            }
            float duration = Mathf.Abs(target - _steeringAngle) / steeringSpeed;
            _steeringTween = Tween.Custom(this,
                new TweenSettings<float>(_steeringAngle, target,
                    new TweenSettings(duration, Ease.Linear, updateType: UpdateType.LateUpdate)),
                static (animator, angle) =>
                {
                    animator._steeringAngle = angle;
                    animator.ApplyWheelPoses();
                });
        }

        private void ApplyWheelPoses()
        {
            foreach (var wheel in wheels)
            {
                if (wheel == null || wheel.tire == null) continue;
                Vector3 steeringAxis = wheel.tire.parent != null
                    ? wheel.tire.parent.InverseTransformDirection(transform.up) : transform.up;
                wheel.steeringRotation = Quaternion.AngleAxis(wheel.steer ? _steeringAngle : 0f, steeringAxis);
                wheel.ApplyRotation();
            }
        }

        private void OnValidate()
        {
            wheelbase = Mathf.Max(0.01f, wheelbase);
            maxSteeringAngle = Mathf.Clamp(maxSteeringAngle, 0f, 80f);
            steeringSpeed = Mathf.Max(0f, steeringSpeed);
            cullDistance = Mathf.Max(0f, cullDistance);
            if (wheels == null) wheels = Array.Empty<Wheel>();
            foreach (var wheel in wheels)
            {
                if (wheel == null) continue;
                wheel.radius = Mathf.Max(0.01f, wheel.radius);
            }
        }
    }
}
