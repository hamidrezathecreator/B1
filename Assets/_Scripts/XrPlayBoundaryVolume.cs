using UnityEngine;

/// <summary>
/// Allowed play area. Add a BoxCollider or convex MeshCollider (trigger).
/// The XR Origin is clamped inside any active volume in the scene (union).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class XrPlayBoundaryVolume : MonoBehaviour
{
    [Tooltip("Off: only block walking on XZ (good for uneven ground). On: full 3D box/mesh clamp.")]
    [SerializeField] bool constrainVertical;

    [Tooltip("Pull the rig slightly inward when clamping so it does not snag on the boundary shell.")]
    [SerializeField] float inwardPadding = 0.05f;

    Collider col;

    public Collider VolumeCollider => col != null ? col : col = GetComponent<Collider>();

    void Awake() => col = GetComponent<Collider>();

    void OnValidate()
    {
        col = GetComponent<Collider>();
        if (col == null) return;
        col.isTrigger = true;
    }

    public bool Contains(Vector3 worldPoint)
    {
        var clamped = ClampPoint(worldPoint);
        return (clamped - worldPoint).sqrMagnitude <= 1e-6f;
    }

    public Vector3 ClampPoint(Vector3 worldPoint)
    {
        var c = VolumeCollider;
        if (c == null) return worldPoint;

        if (c is BoxCollider box)
            return ClampBox(box, worldPoint);

        var onShell = c.ClosestPoint(worldPoint);
        if ((onShell - worldPoint).sqrMagnitude <= 1e-6f)
            return worldPoint;

        if (!constrainVertical)
            onShell.y = worldPoint.y;

        var towardCenter = c.bounds.center - onShell;
        if (towardCenter.sqrMagnitude > 1e-6f)
            onShell += towardCenter.normalized * inwardPadding;

        return onShell;
    }

    Vector3 ClampBox(BoxCollider box, Vector3 worldPoint)
    {
        var t = box.transform;
        var local = t.InverseTransformPoint(worldPoint);
        var center = box.center;
        var half = box.size * 0.5f;
        var clampedLocal = new Vector3(
            Mathf.Clamp(local.x, center.x - half.x, center.x + half.x),
            constrainVertical
                ? Mathf.Clamp(local.y, center.y - half.y, center.y + half.y)
                : local.y,
            Mathf.Clamp(local.z, center.z - half.z, center.z + half.z));
        return t.TransformPoint(clampedLocal);
    }

    void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;
        Gizmos.color = new Color(0.2f, 0.85f, 0.35f, 0.22f);
        Gizmos.matrix = c.transform.localToWorldMatrix;
        if (c is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
    }
}