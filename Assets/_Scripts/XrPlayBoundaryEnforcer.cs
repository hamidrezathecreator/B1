using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Keeps the XR Origin inside <see cref="XrPlayBoundaryVolume"/> regions (walk + teleport).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XROrigin))]
public sealed class XrPlayBoundaryEnforcer : MonoBehaviour
{
    XROrigin origin;
    CharacterController body;
    XrPlayBoundaryVolume[] volumes;
    bool active;

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        body = GetComponent<CharacterController>();
        RefreshVolumes();
    }

    public void RefreshVolumes()
    {
        volumes = FindObjectsByType<XrPlayBoundaryVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        active = volumes != null && volumes.Length > 0;
    }

    void LateUpdate()
    {
        if (!active || origin == null || HelicopterHover.IsMovementLocked(origin)) return;

        var pos = origin.transform.position;
        var playerPoint = origin.Camera != null ? origin.Camera.transform.position : pos;
        playerPoint.y = pos.y;
        if (IsInsideAny(playerPoint)) return;

        var clamped = pos + ClosestAllowedPoint(playerPoint) - playerPoint;
        if ((clamped - pos).sqrMagnitude <= 1e-8f) return;

        if (body != null && body.enabled)
        {
            body.enabled = false;
            origin.transform.position = clamped;
            body.enabled = true;
        }
        else
            origin.transform.position = clamped;
    }

    bool IsInsideAny(Vector3 worldPoint)
    {
        foreach (var volume in volumes)
        {
            if (IsActive(volume) && volume.Contains(worldPoint))
                return true;
        }

        return false;
    }

    Vector3 ClosestAllowedPoint(Vector3 worldPoint)
    {
        var best = worldPoint;
        var bestDist = float.MaxValue;
        foreach (var volume in volumes)
        {
            if (!IsActive(volume)) continue;
            var clamped = volume.ClampPoint(worldPoint);
            var dist = (clamped - worldPoint).sqrMagnitude;
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = clamped;
        }

        return best;
    }

    static bool IsActive(XrPlayBoundaryVolume volume) =>
        volume != null && volume.isActiveAndEnabled && volume.VolumeCollider != null &&
        volume.VolumeCollider.enabled;
}
