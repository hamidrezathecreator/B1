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
        [Tooltip("Total horizontal angle covered by the rays, centered on forward.")]
        [Range(0f, 180f)] [SerializeField] private float raycastAngle = 45f;
        [Tooltip("Number of evenly spaced rays. One ray points straight forward.")]
        [Min(1)] [SerializeField] private int raycastCount = 10;
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
            nearestHit = default;
            float nearestDistance = float.PositiveInfinity;
            int count = Mathf.Max(1, raycastCount);
            for (int i = 0; i < count; i++)
            {
                if (!TryCastCarRay(origin, GetRayDirection(i, count), out var hit) ||
                    hit.distance >= nearestDistance) continue;
                nearestHit = hit;
                nearestDistance = hit.distance;
            }
            return nearestDistance < float.PositiveInfinity;
        }

        private Vector3 GetRayDirection(int index, int count)
        {
            float angle = count <= 1 ? 0f : Mathf.Lerp(-raycastAngle * 0.5f, raycastAngle * 0.5f,
                index / (float)(count - 1));
            return Quaternion.AngleAxis(angle, transform.up) * transform.forward;
        }

        private bool TryCastCarRay(Vector3 origin, Vector3 direction, out RaycastHit nearestHit)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, direction, _carHits,
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
            bool holdingBrake = Application.isPlaying && _brakingForCar;
            bool detectionEnabled = isActiveAndEnabled && carLayers.value != 0;
            Color clearColor = !detectionEnabled ? Color.gray
                : holdingBrake ? new Color(1f, 0.65f, 0f) : Color.green;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousHandleColor = UnityEditor.Handles.color;
            Gizmos.matrix = Matrix4x4.identity;

            // The thin wire markers show the ray's origin and range, not a sphere cast radius.
            float markerSize = UnityEditor.HandleUtility.GetHandleSize(origin) * 0.06f;
            Gizmos.color = clearColor;
            Gizmos.DrawWireSphere(origin, markerSize);
            float arrowSize = Mathf.Min(detectionDistance * 0.2f, markerSize * 3f);
            int count = Mathf.Max(1, raycastCount);
            RaycastHit nearestHit = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = GetRayDirection(i, count);
                Vector3 end = origin + direction * detectionDistance;
                Vector3 right = Vector3.Cross(transform.up, direction);
                bool rayBlocked = TryCastCarRay(origin, direction, out var hit);
                Color rayColor = !detectionEnabled ? Color.gray : rayBlocked ? Color.red : clearColor;
                Gizmos.color = rayColor;
                Gizmos.DrawLine(origin, rayBlocked ? hit.point : end);
                UnityEditor.Handles.color = rayColor;
                UnityEditor.Handles.DrawWireDisc(end, direction, markerSize);
                Gizmos.DrawLine(end, end - direction * arrowSize + right * arrowSize * 0.5f);
                Gizmos.DrawLine(end, end - direction * arrowSize - right * arrowSize * 0.5f);

                if (!rayBlocked) continue;
                UnityEditor.Handles.color = new Color(rayColor.r, rayColor.g, rayColor.b, 0.3f);
                UnityEditor.Handles.DrawDottedLine(hit.point, end, 4f);
                Gizmos.DrawSphere(hit.point, markerSize * 0.6f);
                Gizmos.DrawLine(hit.point, hit.point + hit.normal * markerSize * 4f);
                if (hit.distance < nearestDistance)
                {
                    nearestHit = hit;
                    nearestDistance = hit.distance;
                }
            }

            UnityEditor.Handles.color = clearColor;
            if (count > 1 && raycastAngle > 0f)
                UnityEditor.Handles.DrawWireArc(origin, transform.up, GetRayDirection(0, count),
                    raycastAngle, detectionDistance);

            bool blocked = nearestDistance < float.PositiveInfinity;
            string status = !isActiveAndEnabled ? "Detection disabled"
                : carLayers.value == 0 ? "Select Car Layers in the Inspector"
                : blocked ? $"Car: {nearestHit.collider.name} ({nearestHit.distance:F1} units)"
                : holdingBrake ? $"Braking: {Mathf.Max(0f, _brakeUntil - Time.time):F2}s"
                : "Clear";
            UnityEditor.Handles.Label(origin + transform.up * markerSize * 3f,
                $"{status}\n{count} rays / {raycastAngle:F1} degrees / {detectionDistance:F1} units");

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
            if (!paths.TryGetRandomPath(out var id, out var points, out var direction))
            {
                Debug.LogWarning("[CarPathDriver] The database has no usable car routes.", this);
                return;
            }
            BeginDriving(paths, id, points, direction);
        }

        public void Drive(string id)
        {
            var paths = ResolveDatabase();
            if (paths == null) return;
            var points = paths.GetGlobalPath(id, out var direction);
            if (points == null || points.Length < 2)
            {
                Debug.LogWarning("[CarPathDriver] Missing or incomplete route: " + id, this);
                return;
            }
            BeginDriving(paths, id, points, direction);
        }

        public void SwitchPath(string id) => Drive(id);

        private PathDatabase ResolveDatabase()
        {
            var paths = database != null ? database : PathDatabase.Instance;
            if (paths == null) Debug.LogError("[CarPathDriver] Add a PathDatabase to the scene.", this);
            return paths;
        }

        private void BeginDriving(PathDatabase paths, string id, Vector3[] points, PathDirection direction)
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
            _walker.GoTo(points, id, startAtFullSpeed ? SelectedSpeed : 0f,
                startProgress: startProgress, sourceDatabase: paths, sourceDirection: direction);
        }

        private void OnValidate()
        {
            minSpeed = Mathf.Max(0.1f, minSpeed);
            maxSpeed = Mathf.Max(minSpeed, maxSpeed);
            acceleration = Mathf.Max(0.01f, acceleration);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            detectionDistance = Mathf.Max(0.1f, detectionDistance);
            raycastAngle = Mathf.Clamp(raycastAngle, 0f, 180f);
            raycastCount = Mathf.Max(1, raycastCount);
            brakeDuration = Mathf.Max(0f, brakeDuration);
            brakingDeceleration = Mathf.Max(0.01f, brakingDeceleration);
        }
    }
}
