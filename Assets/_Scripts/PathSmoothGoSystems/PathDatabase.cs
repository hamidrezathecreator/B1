using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoWhere
{
    [Serializable]
    public class GlobalPathData
    {
        [Tooltip("Unique name for this car route.")]
        public string id = "CarPath";

        [Tooltip("Ordered world-space waypoints. Cars snap from the end back to the start; do not close the path.")]
        public List<Vector3> points = new List<Vector3>();

        [Tooltip("Per-waypoint reverse flag — same length as points.")]
        public List<bool> pointReversed = new List<bool>();

        [Tooltip("Per-waypoint sharp flag — same length as points.")]
        public List<bool> pointSharp = new List<bool>();

        [Tooltip("When true, this path uses its own SmoothMode and InterpolationSteps\n" +
                 "instead of the database-wide defaults.")]
        public bool overrideSmoothing = false;

        public PathDatabase.SmoothMode smoothMode = PathDatabase.SmoothMode.None;

        [Range(2, 30)]
        public int interpolationSteps = 8;

        public string DisplayLabel => string.IsNullOrEmpty(id) ? "Unnamed Global Path" : id;

        public bool IsReversed(int pointIndex) =>
            pointReversed != null && pointIndex > 0 &&
            pointIndex < pointReversed.Count && pointReversed[pointIndex];

        public bool IsSharp(int pointIndex) =>
            pointSharp != null && pointIndex < pointSharp.Count && pointSharp[pointIndex];

        public bool IsReversalBoundary(int pointIndex)
        {
            if (pointIndex <= 0 || pointIndex >= (pointReversed?.Count ?? 0)) return false;
            return pointReversed[pointIndex] != pointReversed[pointIndex - 1];
        }

        public bool IsEffectivelySharp(int pointIndex) =>
            IsSharp(pointIndex) || IsReversalBoundary(pointIndex);
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
                globalSmoothedCache[i] = globalPaths[i] == null
                    ? Array.Empty<Vector3>() : BuildSmoothedGlobal(globalPaths[i]);
        }

        public bool HasGlobalPath(string id) => IndexOfGlobal(id) >= 0;
        public Vector3[] GetPath(string id) => GetGlobalPath(id);
        public Vector3[] GetRawPath(string id) => GetRawGlobalPath(id);
        public Vector3[] GetGlobalPath(string id) => GetGlobalPath(id, out _);

        // Return copies: offsets and other per-car changes must never modify the shared cache.
        public Vector3[] GetGlobalPath(string id, out bool[] reversedFlags)
        {
            reversedFlags = null;
            int index = IndexOfGlobal(id);
            return index < 0 ? null : GetGlobalPath(index, out reversedFlags);
        }

        public Vector3[] GetGlobalPath(int index, out bool[] reversedFlags)
        {
            reversedFlags = null;
            if (index < 0 || index >= globalPaths.Count || globalPaths[index] == null) return null;
            if (globalSmoothedCache == null || globalSmoothedCache.Length != globalPaths.Count)
                RebuildCache();
            reversedFlags = BuildReversedFlagsGlobal(globalPaths[index]);
            return (Vector3[])globalSmoothedCache[index].Clone();
        }

        public Vector3[] GetRawGlobalPath(string id)
        {
            int index = IndexOfGlobal(id);
            return index < 0 ? null : globalPaths[index].points?.ToArray();
        }

        /// <summary>Uniformly selects a usable route, ignoring empty and stationary paths.</summary>
        public bool TryGetRandomPath(out string id, out Vector3[] points, out bool[] reversedFlags)
        {
            if (globalSmoothedCache == null || globalSmoothedCache.Length != globalPaths.Count)
                RebuildCache();
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
            points = chosen < 0 ? null : GetGlobalPath(chosen, out _);
            reversedFlags = chosen < 0 ? null : BuildReversedFlagsGlobal(globalPaths[chosen]);
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

        private Vector3[] BuildSmoothedGlobal(GlobalPathData path)
        {
            if (path.points == null || path.points.Count < 2)
                return path.points?.ToArray() ?? Array.Empty<Vector3>();

            Vector3[]  arr   = path.points.ToArray();
            SmoothMode mode  = path.overrideSmoothing ? path.smoothMode        : smoothMode;
            int        steps = Mathf.Clamp(path.overrideSmoothing ? path.interpolationSteps : interpolationSteps, 2, 30);

            if (mode == SmoothMode.None) return arr;

            var result   = new List<Vector3>();
            int n        = arr.Length;
            int segStart = 0;

            for (int i = 1; i < n; i++)
            {
                bool atSharp = path.IsEffectivelySharp(i);
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

            return result.ToArray();
        }

        private bool[] BuildReversedFlagsGlobal(GlobalPathData path)
        {
            if (path.points == null || path.points.Count < 2) return null;
            if (path.pointReversed == null || path.pointReversed.Count == 0) return null;

            SmoothMode mode  = path.overrideSmoothing ? path.smoothMode        : smoothMode;
            int        steps = Mathf.Clamp(path.overrideSmoothing ? path.interpolationSteps : interpolationSteps, 2, 30);
            int        rawCount = path.points.Count;

            if (mode == SmoothMode.None)
            {
                var flags = new bool[rawCount];
                for (int i = 0; i < rawCount; i++) flags[i] = path.IsReversed(i);
                return flags;
            }

            var result   = new List<bool>();
            int segStart = 0;

            for (int i = 1; i < rawCount; i++)
            {
                if (!path.IsEffectivelySharp(i) && i != rawCount - 1) continue;

                int subRawSegs  = i - segStart;
                int subSmoothed = subRawSegs > 0 ? subRawSegs * steps + 1 : 1;
                int appendFrom  = result.Count == 0 ? 0 : 1;

                for (int k = appendFrom; k < subSmoothed; k++)
                {
                    int  rawSeg = subRawSegs > 0 ? Mathf.Min(Mathf.Max(0, k - 1) / steps, subRawSegs - 1) : 0;
                    bool rev    = path.IsReversed(segStart + rawSeg + 1);
                    result.Add(rev);
                }
                segStart = i;
            }
            return result.ToArray();
        }


#if UNITY_EDITOR
        private void OnValidate() => RebuildCache();

        public Vector3[] Editor_GetSmoothedGlobalPoints(int index)
        {
            if (globalSmoothedCache == null || globalSmoothedCache.Length != globalPaths.Count) RebuildCache();
            return index >= 0 && index < globalSmoothedCache.Length ? globalSmoothedCache[index] : null;
        }

        private void OnDrawGizmos()
        {
            if (globalSmoothedCache == null) RebuildCache();
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            foreach (var points in globalSmoothedCache)
            {
                if (points == null || points.Length < 2) continue;
                for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1], points[i]);
                Gizmos.DrawWireSphere(points[0], 0.3f);
                Gizmos.DrawWireCube(points[points.Length - 1], Vector3.one * 0.5f);
            }
        }
#endif
    }
}
