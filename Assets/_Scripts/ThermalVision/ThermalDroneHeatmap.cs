using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives drone renderers a heat-map shader so they read as hot in the scope thermal pass.
/// Normal forward shading on the HMD is unchanged.
/// </summary>
public static class ThermalDroneHeatmap
{
    static readonly Dictionary<Renderer, Material[]> Originals = new();

    public static void Apply(Transform root, Material heatmapPrototype)
    {
        if (root == null || heatmapPrototype == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || Originals.ContainsKey(r)) continue;
            var sh = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
            if (sh != null && sh.name.Contains("HeatmapObject"))
                continue;
            Originals[r] = r.sharedMaterials;
            var next = new Material[r.sharedMaterials.Length];
            for (var i = 0; i < next.Length; i++)
            {
                var src = r.sharedMaterials[i];
                var inst = new Material(heatmapPrototype);
                CopyAlbedo(src, inst);
                next[i] = inst;
            }
            r.sharedMaterials = next;
        }
    }

    static void CopyAlbedo(Material src, Material dst)
    {
        if (src == null) return;
        if (src.HasProperty("_BaseMap"))
            dst.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
        else if (src.HasProperty("_MainTex"))
            dst.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
        if (src.HasProperty("_BaseColor"))
            dst.SetColor("_Color", src.GetColor("_BaseColor"));
        else if (src.HasProperty("_Color"))
            dst.SetColor("_Color", src.GetColor("_Color"));
    }
}