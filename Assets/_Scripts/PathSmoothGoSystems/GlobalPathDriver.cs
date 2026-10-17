using UnityEngine;
using UnityEngine.Serialization;

namespace GoWhere
{
    /// <summary>Add to a car prefab to choose a route and cruise speed on activation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PathWalker))]
    [AddComponentMenu("GoWhere/Car Path Driver")]
    public class GlobalPathDriver : MonoBehaviour
    {
        [Tooltip("Optional scene database. If unset, uses the active PathDatabase.")]
        [SerializeField] private PathDatabase database;
        [Tooltip("Choose uniformly from all usable routes each time this car is activated.")]
        [SerializeField] private bool randomPath = true;
        [Tooltip("Start at a random distance along the chosen route when activated. Later laps still snap to its first point.")]
        [SerializeField] private bool randomStartPosition = true;
        [Tooltip("Used when Random Path is disabled.")]
        [SerializeField] private string pathId;
        [SerializeField] private bool playOnStart = true;

        [Header("Speed (world units per second)")]
        [FormerlySerializedAs("speed")]
        [Min(0.1f)] [SerializeField] private float minSpeed = 5f;
        [Min(0.1f)] [SerializeField] private float maxSpeed = 10f;
        [SerializeField] private bool startAtFullSpeed = true;
        [Min(0.01f)] [SerializeField] private float acceleration = 3f;
        [Min(0f)] [SerializeField] private float rotationSpeed = 180f;

        [Header("Car Detection")]
        [Tooltip("Layers used by other cars' colliders.")]
        [SerializeField] private LayerMask carLayers;
        [Tooltip("Local ray origin. Position this near the front of the car at collider height.")]
        [SerializeField] private Vector3 raycastOffset = new Vector3(0f, 0.5f, 1f);
        [Min(0.1f)] [SerializeField] private float detectionDistance = 8f;
        [Tooltip("Minimum braking time after the last detection.")]
        [Min(0f)] [SerializeField] private float brakeDuration = 0.75f;
        [Min(0.01f)] [SerializeField] private float brakingDeceleration = 10f;

        private PathWalker _walker;
        private bool _started;
        private readonly RaycastHit[] _carHits = new RaycastHit[16];
        private bool _brakingForCar;
        private float _brakeUntil;
        public PathWalker Walker => _walker;
        public string PathId => pathId;
        public float SelectedSpeed { get; private set; }

        private void Awake() => _walker = GetComponent<PathWalker>();
        private void Start()
        {
            _started = true;
            if (playOnStart) StartDriving();
        }

        private void OnEnable()
        {
            if (_started && playOnStart) StartDriving();
        }

        private void Update()
        {
            if (_walker == null || !_walker.isActiveAndEnabled) return;
            if (!_walker.IsMoving && !(_brakingForCar && _walker.State == PathWalker.MoveState.Paused))
            {
                _brakingForCar = false;
                return;
            }

            if (TryGetCarAhead(out _))
            {
                _brakingForCar = true;
                _brakeUntil = Time.time + brakeDuration;
                _walker.Brake();
                return;
            }

            if (_brakingForCar && Time.time >= _brakeUntil)
            {
                _brakingForCar = false;
                _walker.Resume();
            }
        }

        private bool TryGetCarAhead(out RaycastHit nearestHit)
        {
            Vector3 origin = transform.TransformPoint(raycastOffset);
            int hitCount = Physics.RaycastNonAlloc(origin, transform.forward, _carHits,
                detectionDistance, carLayers, QueryTriggerInteraction.Ignore);
            nearestHit = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = _carHits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearestDistance) continue;
                nearestHit = hit;
                nearestDistance = hit.distance;
            }
            return nearestDistance < float.PositiveInfinity;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.TransformPoint(raycastOffset);
            Vector3 forward = transform.forward;
            Vector3 end = origin + forward * detectionDistance;
            bool blocked = TryGetCarAhead(out var hit);
            bool holdingBrake = Application.isPlaying && _brakingForCar && !blocked;
            Color rayColor = blocked ? Color.red : holdingBrake ? new Color(1f, 0.65f, 0f) : Color.green;
            if (!isActiveAndEnabled || carLayers.value == 0) rayColor = Color.gray;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousHandleColor = UnityEditor.Handles.color;
            Gizmos.matrix = Matrix4x4.identity;

            // The thin wire markers show the ray's origin and range, not a sphere cast radius.
            float markerSize = UnityEditor.HandleUtility.GetHandleSize(origin) * 0.06f;
            Gizmos.color = rayColor;
            Gizmos.DrawWireSphere(origin, markerSize);
            Gizmos.DrawLine(origin, blocked ? hit.point : end);
            UnityEditor.Handles.color = rayColor;
            UnityEditor.Handles.DrawWireDisc(end, forward, markerSize);
            float arrowSize = Mathf.Min(detectionDistance * 0.2f, markerSize * 3f);
            Gizmos.DrawLine(end, end - forward * arrowSize + transform.right * arrowSize * 0.5f);
            Gizmos.DrawLine(end, end - forward * arrowSize - transform.right * arrowSize * 0.5f);

            if (blocked)
            {
                UnityEditor.Handles.color = new Color(rayColor.r, rayColor.g, rayColor.b, 0.3f);
                UnityEditor.Handles.DrawDottedLine(hit.point, end, 4f);
                Gizmos.DrawSphere(hit.point, markerSize * 0.6f);
                Gizmos.DrawLine(hit.point, hit.point + hit.normal * markerSize * 4f);
            }

            string status = !isActiveAndEnabled ? "Detection disabled"
                : carLayers.value == 0 ? "Select Car Layers in the Inspector"
                : blocked ? $"Car: {hit.collider.name} ({hit.distance:F1} units)"
                : holdingBrake ? $"Braking: {Mathf.Max(0f, _brakeUntil - Time.time):F2}s"
                : "Clear";
            UnityEditor.Handles.Label(origin + transform.up * markerSize * 3f,
                $"{status}\nDetection range: {detectionDistance:F1} units");

            UnityEditor.Handles.color = previousHandleColor;
            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;
        }
#endif

        public void StartDriving()
        {
            if (randomPath) DriveRandomPath();
            else Drive(pathId);
        }

        public void DriveRandomPath()
        {
            var paths = ResolveDatabase();
            if (paths == null) return;
            if (!paths.TryGetRandomPath(out var id, out var points, out var reversed))
            {
                Debug.LogWarning("[CarPathDriver] The database has no usable car routes.", this);
                return;
            }
            BeginDriving(id, points, reversed);
        }

        public void Drive(string id)
        {
            var paths = ResolveDatabase();
            if (paths == null) return;
            var points = paths.GetGlobalPath(id, out var reversed);
            if (points == null || points.Length < 2)
            {
                Debug.LogWarning("[CarPathDriver] Missing or incomplete route: " + id, this);
                return;
            }
            BeginDriving(id, points, reversed);
        }

        public void SwitchPath(string id) => Drive(id);

        private PathDatabase ResolveDatabase()
        {
            var paths = database != null ? database : PathDatabase.Instance;
            if (paths == null) Debug.LogError("[CarPathDriver] Add a PathDatabase to the scene.", this);
            return paths;
        }

        private void BeginDriving(string id, Vector3[] points, bool[] reversed)
        {
            if (_walker == null) _walker = GetComponent<PathWalker>();
            _brakingForCar = false;
            pathId = id;
            float lower = Mathf.Max(0.1f, Mathf.Min(minSpeed, maxSpeed));
            float upper = Mathf.Max(lower, Mathf.Max(minSpeed, maxSpeed));
            SelectedSpeed = Random.Range(lower, upper);
            _walker.SetMaxSpeed(SelectedSpeed);
            _walker.SetAcceleration(acceleration);
            _walker.SetDeceleration(brakingDeceleration);
            _walker.SetRotationSpeed(rotationSpeed);
            float startProgress = randomStartPosition ? Random.value : 0f;
            _walker.GoTo(points, id, startAtFullSpeed ? SelectedSpeed : 0f, reversed, startProgress);
        }

        private void OnValidate()
        {
            minSpeed = Mathf.Max(0.1f, minSpeed);
            maxSpeed = Mathf.Max(minSpeed, maxSpeed);
            acceleration = Mathf.Max(0.01f, acceleration);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            detectionDistance = Mathf.Max(0.1f, detectionDistance);
            brakeDuration = Mathf.Max(0f, brakeDuration);
            brakingDeceleration = Mathf.Max(0.01f, brakingDeceleration);
        }
    }
}
