using UnityEngine;

/// <summary>Project layer for heat-shader geometry and thermal decals.</summary>
public static class ThermalMonitorLayers
{
    public const string LayerName = "ThermalSignature";

    public static int Index => LayerMask.NameToLayer(LayerName);

    public static int Mask =>
        Index >= 0 ? 1 << Index : 0;

    public static void SetLayerRecursive(GameObject root)
    {
        if (root == null || Index < 0) return;
        SetLayerOnHierarchy(root.transform, Index);
    }

    static void SetLayerOnHierarchy(Transform node, int layer)
    {
        node.gameObject.layer = layer;
        for (var i = 0; i < node.childCount; i++)
            SetLayerOnHierarchy(node.GetChild(i), layer);
    }

    /// <summary>
    /// Scope: native heat pass on ThermalSignature; ambient replacement on Default + Ground.
    /// </summary>
    public static void GetScopePassMasks(out int nativeMask, out int ambientMask, out int transparentMask)
    {
        var sig = Mask;
        if (sig == 0)
        {
            nativeMask = -1;
            ambientMask = -1;
            transparentMask = -1;
            return;
        }

        nativeMask = sig;
        transparentMask = sig;
        ambientMask = LayerMask.GetMask("Default", "Ground");
        if (ambientMask == 0)
            ambientMask = ~sig;
    }
}