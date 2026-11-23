using UnityEngine;

/// <summary>
/// While the tracker scope camera is rendering, thermal URP passes run for that camera only.
/// </summary>
public static class TrackerScopeThermalGate
{
    public static Camera ScopeCamera { get; set; }

    /// <summary>True while the scope camera is mid-render with thermal requested (see begin/end camera).</summary>
    public static bool ScopeThermalActive { get; private set; }

    public static void SetScopeThermalActive(bool active) => ScopeThermalActive = active;

    public static bool ShouldRunThermalPasses(Camera cam) =>
        ScopeThermalActive &&
        ScopeCamera != null &&
        cam == ScopeCamera;
}