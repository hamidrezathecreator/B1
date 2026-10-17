using System;
using UnityEngine;
using UnityEngine.Events;

namespace GoWhere
{
    /// <summary>Follows an open route and teleports to its start after reaching its end.</summary>
    public class PathWalker : MonoBehaviour, ITickableMovement
    {
        [Header("Events")]
        public UnityEvent onPathStarted;
        public UnityEvent onWaypointReached;
        public UnityEvent onPaused;
        public UnityEvent onResumed;
        public UnityEvent<MoveState> onStateChanged;
        public UnityEvent onLoopCompleted;

        public event Action<MoveState> StateChanged;
        public event Action OnPathStarted;
        public event Action OnPaused;
        public event Action OnResumed;
        public event Action<int> OnWaypointReached;
        public event Action OnStopped;
        public event Action OnLoopCompleted;

        public enum MoveState { Idle, Accelerating, Cruising, Braking, Paused, Stopped }

        private float maxSpeed = 5f;
        private float acceleration = 3f;
        private float brakingDeceleration = 5f;
        private float rotationSpeed = 180f;
        private bool lockY;
        private Vector3[] _pathPoints;
        private bool[] _reversedFlags;
        private int _waypointIndex;
        private Vector3 _tickPos;
        private Quaternion _tickRot;
        private Vector3 _tickVelocityDir = Vector3.forward;
        private float _currentSpeed;
        private MoveState _state = MoveState.Idle;
        private string _currentPathId;
        private PathDatabase _sourceDatabase;
        private PathDirection _routeDirection;

        public MoveState State => _state;
        public bool IsMoving => _state is MoveState.Accelerating or MoveState.Cruising or MoveState.Braking;
        public bool IsAlive => this != null && isActiveAndEnabled;
        public float CurrentSpeed => _currentSpeed;
        public float MaxSpeed => maxSpeed;
        public float SpeedRatio => _currentSpeed / maxSpeed;
        public Vector3 TickPosition => _tickPos;
        public Quaternion TickRotation => _tickRot;
        public Vector3 TickVelocityDir => _tickVelocityDir;
        public string CurrentPathId => _currentPathId;
        public PathDirection CurrentPathDirection
        {
            get
            {
                var paths = _sourceDatabase != null ? _sourceDatabase : PathDatabase.Instance;
                return paths != null && paths.TryGetPathDirection(_currentPathId, out var direction)
                    ? direction : _routeDirection;
            }
        }
        public bool IsCurrentSegmentReversed => IsSegmentReversed(_waypointIndex);
        public bool CurrentPathInvertWheelSpin
        {
            get
            {
                var paths = _sourceDatabase != null ? _sourceDatabase : PathDatabase.Instance;
                return paths != null && paths.TryGetPathInvertWheelSpin(_currentPathId, out bool invert) && invert;
            }
        }

        public float RemainingPathDistance
        {
            get
            {
                if (_pathPoints == null) return 0f;
                float distance = 0f;
                Vector3 from = _tickPos;
                for (int i = _waypointIndex; i < _pathPoints.Length; i++)
                {
                    var target = GetWaypoint(i);
                    distance += Vector3.Distance(from, target);
                    from = target;
                }
                return distance;
            }
        }

        public void GoTo(string pathId)
        {
            var database = PathDatabase.Instance;
            if (database == null)
            {
                Debug.LogError("[PathWalker] No PathDatabase in scene.", this);
                return;
            }
            var points = database.GetGlobalPath(pathId, out var direction);
            GoTo(points, pathId, 0f, sourceDatabase: database, sourceDirection: direction);
        }

        public void GoTo(Vector3[] points, string label = null, bool[] reversedFlags = null) =>
            GoTo(points, label, 0f, reversedFlags);

        /// <param name="startProgress">Initial fraction of total route distance. Later laps begin at zero.</param>
        public void GoTo(Vector3[] points, string label, float initialSpeed, bool[] reversedFlags = null,
            float startProgress = 0f, PathDatabase sourceDatabase = null,
            PathDirection sourceDirection = PathDirection.Forward)
        {
            if (points == null || points.Length < 2)
            {
                Debug.LogWarning("[PathWalker] A route requires at least two waypoints.", this);
                return;
            }
            bool hasLength = false;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 delta = points[i] - points[i - 1];
                if (lockY) delta.y = 0f;
                if (delta.sqrMagnitude > 0.000001f) { hasLength = true; break; }
            }
            if (!hasLength)
            {
                Debug.LogWarning("[PathWalker] The route has no travel distance.", this);
                return;
            }

            _pathPoints = (Vector3[])points.Clone();
            _reversedFlags = sourceDatabase == null && reversedFlags != null && reversedFlags.Length == points.Length
                ? (bool[])reversedFlags.Clone() : null;
            _currentPathId = label;
            _sourceDatabase = sourceDatabase;
            _routeDirection = sourceDirection;
            _currentSpeed = Mathf.Clamp(initialSpeed, 0f, maxSpeed);
            SnapToStart(startProgress);
            TransitionState(_currentSpeed >= maxSpeed ? MoveState.Cruising : MoveState.Accelerating);
            if (isActiveAndEnabled) MovementTickSystem.EnsureInstance().Register(this);
            onPathStarted?.Invoke();
            OnPathStarted?.Invoke();
        }

        public void SetMaxSpeed(float speed)
        {
            maxSpeed = Mathf.Max(0.1f, speed);
            _currentSpeed = Mathf.Min(_currentSpeed, maxSpeed);
            if (_state == MoveState.Cruising && _currentSpeed < maxSpeed)
                TransitionState(MoveState.Accelerating);
        }

        public void SetAcceleration(float value) => acceleration = Mathf.Max(0.01f, value);
        public void SetDeceleration(float value) => brakingDeceleration = Mathf.Max(0.01f, value);
        public void SetRotationSpeed(float value) => rotationSpeed = Mathf.Max(0f, value);
        public void SetLockY(bool value) => lockY = value;

        public void Brake()
        {
            if (IsMoving) TransitionState(MoveState.Braking);
        }

        public void Pause()
        {
            if (!IsMoving) return;
            _currentSpeed = 0f;
            TransitionState(MoveState.Paused);
            onPaused?.Invoke();
            OnPaused?.Invoke();
        }

        public void Resume()
        {
            if (_pathPoints == null || _state is not (MoveState.Paused or MoveState.Stopped or MoveState.Braking)) return;
            TransitionState(MoveState.Accelerating);
            if (isActiveAndEnabled) MovementTickSystem.EnsureInstance().Register(this);
            onResumed?.Invoke();
            OnResumed?.Invoke();
        }

        public void Stop()
        {
            _currentSpeed = 0f;
            MovementTickSystem.Instance?.Unregister(this);
            if (_state == MoveState.Stopped) return;
            TransitionState(MoveState.Stopped);
            OnStopped?.Invoke();
        }

        public void MovementTick(float tickDelta)
        {
            RefreshPathDirection();
            if (!IsMoving || _pathPoints == null || tickDelta <= 0f) return;
            if (_state == MoveState.Braking)
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, brakingDeceleration * tickDelta);
                if (_currentSpeed <= 0f) { Pause(); return; }
            }
            else
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, maxSpeed, acceleration * tickDelta);
                if (_currentSpeed >= maxSpeed) TransitionState(MoveState.Cruising);
            }

            float budget = _currentSpeed * tickDelta;
            // Consume each segment exactly so dense smoothed points cannot cap speed
            // or cause cars to cut across corners.
            while (budget > 0f && _waypointIndex < _pathPoints.Length)
            {
                Vector3 target = GetWaypoint(_waypointIndex);
                Vector3 delta = target - _tickPos;
                float distance = delta.magnitude;
                if (distance > 0.000001f)
                {
                    Vector3 direction = delta / distance;
                    _tickVelocityDir = IsSegmentReversed(_waypointIndex) ? -direction : direction;
                    float travel = Mathf.Min(distance, budget);
                    if (rotationSpeed > 0f)
                        _tickRot = Quaternion.RotateTowards(_tickRot,
                            Quaternion.LookRotation(_tickVelocityDir, Vector3.up),
                            rotationSpeed * travel / Mathf.Max(_currentSpeed, 0.0001f));
                    _tickPos += direction * travel;
                    budget -= travel;
                    if (travel < distance) return;
                }
                _tickPos = target;
                int reached = _waypointIndex++;
                OnWaypointReached?.Invoke(reached);
                onWaypointReached?.Invoke();
                if (!IsMoving || !isActiveAndEnabled) return;
                if (_waypointIndex == _pathPoints.Length)
                {
                    // Teleport both simulation and rendered pose. Interpolating this
                    // discontinuity would draw a car travelling across the whole map.
                    SnapToStart();
                    OnLoopCompleted?.Invoke();
                    onLoopCompleted?.Invoke();
                    if (!IsMoving || !isActiveAndEnabled) return;
                }
            }
        }

        private void RefreshPathDirection()
        {
            if (_sourceDatabase == null || _pathPoints == null ||
                !_sourceDatabase.TryGetPathDirection(_currentPathId, out var direction) ||
                direction == _routeDirection) return;

            // After a script reload, recover the direction from the actual route copy.
            var desiredPoints = _sourceDatabase.GetGlobalPath(_currentPathId);
            _routeDirection = direction;
            if (desiredPoints != null && desiredPoints.Length == _pathPoints.Length &&
                desiredPoints[0] == _pathPoints[0] &&
                desiredPoints[desiredPoints.Length - 1] == _pathPoints[_pathPoints.Length - 1]) return;

            // Keep the same physical segment and position, targeting its opposite end.
            Array.Reverse(_pathPoints);
            _waypointIndex = _pathPoints.Length - _waypointIndex;
            _tickVelocityDir = -_tickVelocityDir;
            if (rotationSpeed > 0f)
                _tickRot = Quaternion.LookRotation(_tickVelocityDir, Vector3.up);
        }

        public void SmoothStep(float interpolationSpeed, float frameDelta)
        {
            float blend = 1f - Mathf.Exp(-interpolationSpeed * frameDelta);
            transform.position = Vector3.Lerp(transform.position, _tickPos, blend);
            if (rotationSpeed > 0f)
                transform.rotation = Quaternion.Slerp(transform.rotation, _tickRot, blend);
        }

        private Vector3 GetWaypoint(int index)
        {
            Vector3 point = _pathPoints[index];
            if (lockY) point.y = _tickPos.y;
            return point;
        }

        private bool IsSegmentReversed(int index) =>
            _sourceDatabase == null && _reversedFlags != null &&
            index >= 0 && index < _reversedFlags.Length && _reversedFlags[index];

        private void SnapToStart(float startProgress = 0f)
        {
            float height = transform.position.y;
            _tickPos = _pathPoints[0];
            if (lockY) _tickPos.y = height;
            _waypointIndex = 1;
            if (startProgress > 0f)
            {
                float totalLength = 0f;
                Vector3 previous = _tickPos;
                for (int i = 1; i < _pathPoints.Length; i++)
                {
                    Vector3 target = GetWaypoint(i);
                    totalLength += Vector3.Distance(previous, target);
                    previous = target;
                }

                // Choose by distance rather than waypoint index so densely sampled
                // corners do not attract more cars. Stay just short of the endpoint.
                float remaining = Mathf.Clamp(startProgress, 0f, 0.999999f) * totalLength;
                while (_waypointIndex < _pathPoints.Length)
                {
                    Vector3 target = GetWaypoint(_waypointIndex);
                    float length = Vector3.Distance(_tickPos, target);
                    if (length > 0.000001f && remaining < length)
                    {
                        _tickPos = Vector3.Lerp(_tickPos, target, remaining / length);
                        break;
                    }
                    remaining = Mathf.Max(0f, remaining - length);
                    _tickPos = target;
                    _waypointIndex++;
                }
                _waypointIndex = Mathf.Min(_waypointIndex, _pathPoints.Length - 1);
            }
            // Skip duplicate starting points before choosing the initial heading.
            while (_waypointIndex < _pathPoints.Length - 1 &&
                (GetWaypoint(_waypointIndex) - _tickPos).sqrMagnitude <= 0.000001f)
                _waypointIndex++;
            Vector3 direction = GetWaypoint(_waypointIndex) - _tickPos;
            if (direction.sqrMagnitude > 0.000001f)
            {
                _tickVelocityDir = IsSegmentReversed(_waypointIndex) ? -direction.normalized : direction.normalized;
                _tickRot = Quaternion.LookRotation(_tickVelocityDir, Vector3.up);
            }
            else _tickRot = transform.rotation;
            transform.position = _tickPos;
            if (rotationSpeed > 0f) transform.rotation = _tickRot;
        }

        private void TransitionState(MoveState state)
        {
            if (_state == state) return;
            _state = state;
            StateChanged?.Invoke(state);
            onStateChanged?.Invoke(state);
        }

        private void OnEnable()
        {
            if (_pathPoints != null && IsMoving)
                MovementTickSystem.EnsureInstance().Register(this);
        }

        private void OnDisable() => MovementTickSystem.Instance?.Unregister(this);
        private void OnDestroy() => MovementTickSystem.Instance?.Unregister(this);
    }
}
