using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoWhere
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Smoothing algorithms — pure, no dependencies
    // ─────────────────────────────────────────────────────────────────────────
    public static class PathSmoothing
    {
        public enum Mode { None, CatmullRom, LinearResample }

        public static Vector3[] Smooth(Vector3[] raw, Mode mode, int steps)
        {
            if (raw == null || raw.Length < 2) return raw;

            return mode switch
            {
                Mode.CatmullRom     => CatmullRom(raw, steps),
                Mode.LinearResample => LinearResample(raw, steps),
                _                   => raw
            };
        }

        public static Vector3[] CatmullRom(Vector3[] points, int steps)
        {
            if (points.Length < 2) return points;
            if (points.Length == 2) return LinearResample(points, steps);

            var result = new List<Vector3>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                var p0 = i > 0              ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i < points.Length - 2 ? points[i + 2] : points[i + 1];

                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    result.Add(EvalCatmullRom(p0, p1, p2, p3, t));
                }
            }
            result.Add(points[^1]);
            return result.ToArray();
        }

        private static Vector3 EvalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public static Vector3[] LinearResample(Vector3[] points, int steps)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < points.Length - 1; i++)
                for (int s = 0; s < steps; s++)
                    result.Add(Vector3.Lerp(points[i], points[i + 1], s / (float)steps));

            result.Add(points[^1]);
            return result.ToArray();
        }
    }
}