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
        [Tooltip("Height above the route, e.g. to match the car prefab's pivot.")]
        [SerializeField] private float yOffset;

        private PathWalker _walker;
        private bool _started;
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
            pathId = id;
            float lower = Mathf.Max(0.1f, Mathf.Min(minSpeed, maxSpeed));
            float upper = Mathf.Max(lower, Mathf.Max(minSpeed, maxSpeed));
            SelectedSpeed = Random.Range(lower, upper);
            // The database returns a private copy for each car.
            for (int i = 0; i < points.Length; i++) points[i].y += yOffset;
            _walker.SetMaxSpeed(SelectedSpeed);
            _walker.SetAcceleration(acceleration);
            _walker.SetRotationSpeed(rotationSpeed);
            _walker.GoTo(points, id, startAtFullSpeed ? SelectedSpeed : 0f, reversed);
        }

        private void OnValidate()
        {
            minSpeed = Mathf.Max(0.1f, minSpeed);
            maxSpeed = Mathf.Max(minSpeed, maxSpeed);
            acceleration = Mathf.Max(0.01f, acceleration);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
        }
    }
}
