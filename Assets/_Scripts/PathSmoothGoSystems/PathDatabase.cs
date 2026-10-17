using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace GoWhere
{
    public enum PathDirection { Forward, Backward }

    [Serializable]
    public class GlobalPathData : ISerializationCallbackReceiver
    {
        [Tooltip("Unique name for this car route.")]
        public string id = "CarPath";

        [Tooltip("Forward travels from waypoint 0 to the last waypoint. Backward travels from the last waypoint to waypoint 0. Cars face their travel direction.")]
        public PathDirection direction = PathDirection.Forward;

        [Tooltip("Flip every wheel's spin on cars using this path. Combines with each wheel's Invert Spin setting, independently of Direction.")]
        public bool invertWheelSpin;

        [FormerlySerializedAs("reverse")]
        [SerializeField, HideInInspector] private bool legacyBackward;
        [SerializeField, HideInInspector] private bool directionMigrated;

        [Tooltip("Ordered world-space waypoints. Cars snap from the end back to the start; do not close the path.")]
        public List<Vector3> points = new List<Vector3>();

        [Header("Ground Placement (whole path)")]
        [Tooltip("Project every smoothed waypoint onto ground colliders before applying Y Offset.")]
        public bool snapToGround = true;

        [Tooltip("Ground collider layers. Nothing automatically uses the Ground layer, or all raycast layers if it is absent.")]
        public LayerMask groundLayers;

        [Tooltip("Vertical offset added to the whole path after ground snapping. Also applies with snapping disabled.")]
        public float yOffset;

        [Min(0.01f)]
        [Tooltip("Start each downward ground ray this far above the waypoint's authored height.")]
        public float groundRayHeight = 100f;

        [Min(0.01f)]
        [Tooltip("Maximum downward distance from the ray start. A missed ray keeps the authored height plus Y Offset.")]
        public float groundRayDistance = 1000f;

        [Tooltip("Per-waypoint sharp flag — same length as points.")]
        public List<bool> pointSharp = new List<bool>();

        [Tooltip("When true, this path uses its own SmoothMode and InterpolationSteps\n" +
                 "instead of the database-wide defaults.")]
        public bool overrideSmoothing = false;

        public PathDatabase.SmoothMode smoothMode = PathDatabase.SmoothMode.None;

        [Range(2, 30)]
        public int interpolationSteps = 8;

        public string DisplayLabel => string.IsNullOrEmpty(id) ? "Unnamed Global Path" : id;

        public bool IsSharp(int pointIndex) =>
            pointSharp != null && pointIndex < pointSharp.Count && pointSharp[pointIndex];

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (directionMigrated) return;
            if (legacyBackward) direction = PathDirection.Backward;
            directionMigrated = true;
        }
    }


    /// <summary>Collection of open car routes, repeated by teleporting from end to start.</summary>
    [DefaultExecutionOrder(-200)]
    public class PathDatabase : MonoBehaviour
    {
        public static PathDatabase Instance { get; private set; }

        [Tooltip("Open car routes. Keep the last waypoint at the actual end of the road.")]
        [SerializeField] private List<GlobalPathData> globalPaths = new List<GlobalPathData>();
        [SerializeField] private SmoothMode smoothMode = SmoothMode.CatmullRom;
        [Range(2, 30)] [SerializeField] private int interpolationSteps = 8;

        [Header("Path Gizmos")]
        [SerializeField] private Color forwardPathColor = new Color(0.3f, 0.8f, 1f);
        [FormerlySerializedAs("reversePathColor")]
        [SerializeField] private Color backwardPathColor = new Color(1f, 0.55f, 0.15f);

        public enum SmoothMode { None, CatmullRom, LinearResample }
        public IReadOnlyList<GlobalPathData> GlobalPaths => globalPaths;
        private Vector3[][] globalSmoothedCache;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[PathDatabase] Only one path database may be active.", this);
                enabled = false;
                return;
            }
            Instance = this;
            RebuildCache();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void RebuildCache()
        {
            globalSmoothedCache = new Vector3[globalPaths.Count][];
            for (int i = 0; i < globalPaths.Count; i++)
            {
                globalSmoothedCache[i] = globalPaths[i] == null
                    ? Array.Empty<Vector3>() : BuildSmoothedGlobal(globalPaths[i]);
            }
        }

        public bool HasGlobalPath(string id) => IndexOfGlobal(id) >= 0;

        public bool TryGetPathDirection(string id, out PathDirection direction)
        {
            int index = IndexOfGlobal(id);
            direction = index >= 0 ? globalPaths[index].direction : PathDirection.Forward;
            return index >= 0;
        }

        public bool TryGetPathInvertWheelSpin(string id, out bool invert)
        {
            int index = IndexOfGlobal(id);
            invert = index >= 0 && globalPaths[index].invertWheelSpin;
            return index >= 0;
        }

        private void EnsureCache()
        {
            if (globalSmoothedCache == null || globalSmoothedCache.Length != globalPaths.Count)
                RebuildCache();
        }
        public Vector3[] GetPath(string id) => GetGlobalPath(id);
        public Vector3[] GetRawPath(string id) => GetRawGlobalPath(id);
        public Vector3[] GetGlobalPath(string id) => GetGlobalPath(id, out _);

        // Return copies: offsets and other per-car changes must never modify the shared cache.
        public Vector3[] GetGlobalPath(string id, out PathDirection direction)
        {
            direction = PathDirection.Forward;
            int index = IndexOfGlobal(id);
            return index < 0 ? null : GetGlobalPath(index, out direction);
        }

        public Vector3[] GetGlobalPath(int index, out PathDirection direction)
        {
            direction = PathDirection.Forward;
            if (index < 0 || index >= globalPaths.Count || globalPaths[index] == null) return null;
            EnsureCache();
            direction = globalPaths[index].direction;
            var points = (Vector3[])globalSmoothedCache[index].Clone();
            if (direction == PathDirection.Backward) Array.Reverse(points);
            return points;
        }

        public Vector3[] GetRawGlobalPath(string id)
        {
            int index = IndexOfGlobal(id);
            if (index < 0) return null;
            var path = globalPaths[index];
            var points = ApplyGroundPlacement(path, path.points?.ToArray());
            if (points != null && path.direction == PathDirection.Backward) Array.Reverse(points);
            return points;
        }

        /// <summary>Uniformly selects a usable route, ignoring empty and stationary paths.</summary>
        public bool TryGetRandomPath(out string id, out Vector3[] points, out PathDirection direction)
        {
            EnsureCache();
            int chosen = -1;
            int count = 0;
            for (int i = 0; i < globalPaths.Count; i++)
            {
                if (globalPaths[i] == null || string.IsNullOrWhiteSpace(globalPaths[i].id)) continue;
                var route = globalSmoothedCache[i];
                if (route == null || route.Length < 2) continue;
                bool hasLength = false;
                for (int j = 1; j < route.Length; j++)
                    if ((route[j] - route[j - 1]).sqrMagnitude > 0.000001f) { hasLength = true; break; }
                if (hasLength && UnityEngine.Random.Range(0, ++count) == 0) chosen = i;
            }
            id = chosen < 0 ? null : globalPaths[chosen].id;
            direction = PathDirection.Forward;
            points = chosen < 0 ? null : GetGlobalPath(chosen, out direction);
            return chosen >= 0;
        }

        public void RegisterGlobalPath(string id, Vector3[] waypoints)
        {
            if (string.IsNullOrWhiteSpace(id) || waypoints == null || waypoints.Length < 2)
                throw new ArgumentException("A car path requires an id and at least two waypoints.");
            if (HasGlobalPath(id)) throw new ArgumentException("Path ids must be unique: " + id);
            globalPaths.Add(new GlobalPathData { id = id, points = new List<Vector3>(waypoints) });
            RebuildCache();
        }

        private int IndexOfGlobal(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return -1;
            for (int i = 0; i < globalPaths.Count; i++)
                if (globalPaths[i] != null && string.Equals(globalPaths[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public void BakeSmoothedPaths() => RebuildCache();

        private Vector3[] CatmullRomSmooth(Vector3[] points, int steps) =>
            PathSmoothing.CatmullRom(points, steps);
        private Vector3[] LinearResample(Vector3[] points, int steps) =>
            PathSmoothing.LinearResample(points, steps);

        /// <summary>Resolve one authored position using the route's ground and height settings.</summary>
        public Vector3 GetPlacedPoint(GlobalPathData path, Vector3 point)
        {
            if (path.snapToGround)
            {
                int mask = path.groundLayers.value;
                if (mask == 0)
                {
                    mask = LayerMask.GetMask("Ground");
                    if (mask == 0) mask = Physics.DefaultRaycastLayers;
                }
                Vector3 origin = point + Vector3.up * Mathf.Max(0.01f, path.groundRayHeight);
                if (Physics.Raycast(origin, Vector3.down, out var hit,
                    Mathf.Max(0.01f, path.groundRayDistance), mask, QueryTriggerInteraction.Ignore))
                    point.y = hit.point.y;
            }
            point.y += path.yOffset;
            return point;
        }

        private Vector3[] ApplyGroundPlacement(GlobalPathData path, Vector3[] points)
        {
            if (points == null) return null;
            for (int i = 0; i < points.Length; i++) points[i] = GetPlacedPoint(path, points[i]);
            // Cache only authored waypoint order. Direction is applied once when retrieving a route.
            return points;
        }

        private Vector3[] BuildSmoothedGlobal(GlobalPathData path)
        {
            if (path.points == null || path.points.Count < 2)
                return ApplyGroundPlacement(path, path.points?.ToArray() ?? Array.Empty<Vector3>());

            Vector3[]  arr   = path.points.ToArray();
            SmoothMode mode  = path.overrideSmoothing ? path.smoothMode        : smoothMode;
            int        steps = Mathf.Clamp(path.overrideSmoothing ? path.interpolationSteps : interpolationSteps, 2, 30);

            if (mode == SmoothMode.None) return ApplyGroundPlacement(path, arr);

            var result   = new List<Vector3>();
            int n        = arr.Length;
            int segStart = 0;

            for (int i = 1; i < n; i++)
            {
                bool atSharp = path.IsSharp(i);
                bool atEnd   = i == n - 1;

                if (atSharp || atEnd)
                {
                    int subLen = i - segStart + 1;
                    var sub    = new Vector3[subLen];
                    for (int k = 0; k < subLen; k++) sub[k] = arr[segStart + k];

                    Vector3[] smoothed = subLen < 2 ? sub : mode switch
                    {
                        SmoothMode.CatmullRom     => CatmullRomSmooth(sub, steps),
                        SmoothMode.LinearResample => LinearResample(sub, steps),
                        _                         => sub
                    };

                    int appendFrom = result.Count == 0 ? 0 : 1;
                    for (int k = appendFrom; k < smoothed.Length; k++) result.Add(smoothed[k]);

                    segStart = i;
                }
            }

            // Project after smoothing so intermediate samples follow uneven ground too.
            // Authored points stay untouched, so rebuilding never accumulates offsets.
            return ApplyGroundPlacement(path, result.ToArray());
        }

#if UNITY_EDITOR
        // Physics queries run when rebuilding on the main thread, not in OnValidate.
        private void OnValidate() => globalSmoothedCache = null;

        public Vector3[] Editor_GetSmoothedGlobalPoints(int index)
        {
            return GetGlobalPath(index, out _);
        }

        public Color Editor_GetPathColor(GlobalPathData path) =>
            path.direction == PathDirection.Backward ? backwardPathColor : forwardPathColor;

        private void OnDrawGizmos()
        {
            EnsureCache();
            Color previousColor = Gizmos.color;
            Color previousHandleColor = UnityEditor.Handles.color;
            for (int pathIndex = 0; pathIndex < globalSmoothedCache.Length; pathIndex++)
            {
                var path = globalPaths[pathIndex];
                if (path == null) continue;
                var points = GetGlobalPath(pathIndex, out var direction);
                if (points == null || points.Length < 2) continue;
                Gizmos.color = Editor_GetPathColor(path);
                for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1], points[i]);
                Gizmos.DrawWireSphere(points[0], 0.3f);
                Gizmos.DrawWireCube(points[points.Length - 1], Vector3.one * 0.5f);

                var labelStyle = new GUIStyle(UnityEditor.EditorStyles.label);
                labelStyle.normal.textColor = Gizmos.color;
                UnityEditor.Handles.Label(points[0] + Vector3.up * 0.5f,
                    $"{path.DisplayLabel} | {direction} | START", labelStyle);
                UnityEditor.Handles.Label(points[points.Length - 1] + Vector3.up * 0.5f,
                    $"{path.DisplayLabel} | {direction} | END", labelStyle);

                // Sample arrows by distance so smoothing density cannot change their spacing.
                float distanceToArrow = 0f;
                UnityEditor.Handles.color = Gizmos.color;
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 segment = points[i] - points[i - 1];
                    float length = segment.magnitude;
                    if (length <= 0.000001f) continue;
                    Vector3 heading = segment / length;
                    while (distanceToArrow <= length)
                    {
                        Vector3 position = points[i - 1] + heading * distanceToArrow;
                        float size = UnityEditor.HandleUtility.GetHandleSize(position) * 0.6f;
                        UnityEditor.Handles.ArrowHandleCap(0, position,
                            Quaternion.LookRotation(heading), size, EventType.Repaint);
                        distanceToArrow += Mathf.Max(5f, size * 3f);
                    }
                    distanceToArrow -= length;
                }
            }
            Gizmos.color = previousColor;
            UnityEditor.Handles.color = previousHandleColor;
        }
#endif
    }
}
