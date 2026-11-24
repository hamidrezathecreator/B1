using UnityEngine;

/// <summary>
/// Spawns thermal heat decals at explosion centers (tracker scope & missile hits).
/// </summary>
public static class ThermalExplosionHeat
{
    public static void Spawn(Vector3 center, Bounds bounds, float scale = 1f, float heat = 0.85f)
    {
        if (ExplosionHeatManager.Instance == null) return;

        var radius = Mathf.Max(bounds.extents.x, bounds.extents.z, bounds.extents.y * 0.5f);
        var size   = Mathf.Clamp(radius * 2.2f, 3f, 14f) * scale;
        ExplosionHeatManager.Instance.SpawnExplosion(center, size, heat);
    }
}