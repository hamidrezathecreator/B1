using UnityEngine;

/// <summary>
/// Swaps Shahed, air-defense, and launcher renderers to <see cref="Custom/HeatmapObject_Lit"/>.
/// Preserves each renderer's albedo; tuned heat per category for the tracker scope.
/// </summary>
public static class Lvl3ThermalSceneSetup
{
    static readonly string[] NameTokens =
    {
        "Shahed", "SM_Shahed", "Shahed-136",
        "AirDefense", "Airdefense",
        "Luncher", "Launcher", "136Luncher", "Luncher01", "Luncher2", "Luncher3",
        "LuncherCrash", "AirDefenseCrash"
    };

    static Material s_prototype;

    public static bool IsThermalTarget(GameObject go) => Matches(go);

    public static void Apply(Material prototype)
    {
        if (prototype == null)
        {
            Debug.LogWarning("Lvl3ThermalSceneSetup: no thermal lit prototype material.");
            return;
        }

        s_prototype = prototype;
        var count = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!Matches(r.gameObject)) continue;
            ApplyToRenderer(r, prototype);
            count++;
        }

        Debug.Log("Lvl3ThermalSceneSetup: applied thermal lit to " + count + " renderers.");
    }

    /// <summary>Call when a target is spawned mid-level (e.g. SM_Shahed149 after it was hidden).</summary>
    public static void ApplyToRoot(Transform root, Material prototype = null)
    {
        if (prototype != null)
            s_prototype = prototype;
        if (s_prototype == null || root == null) return;
        if (!Matches(root.gameObject)) return;

        ThermalMonitorLayers.SetLayerRecursive(root.gameObject);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            ApplyMaterialsOnly(r, s_prototype);
        }
    }

    static void AssignThermalSignatureLayer(Transform hit)
    {
        if (ThermalMonitorLayers.Index < 0) return;

        Transform root = null;
        for (var t = hit; t != null; t = t.parent)
        {
            if (Matches(t.gameObject)) root = t;
        }

        if (root != null)
            ThermalMonitorLayers.SetLayerRecursive(root.gameObject);
    }

    static bool Matches(GameObject go)
    {
        if (go == null) return false;
        for (var t = go.transform; t != null; t = t.parent)
        {
            var n = t.name;
            for (var i = 0; i < NameTokens.Length; i++)
            {
                if (n.IndexOf(NameTokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    static void ApplyMaterialsOnly(Renderer r, Material prototype)
    {
        var src = r.sharedMaterials;
        var dst = new Material[src.Length];
        for (var i = 0; i < src.Length; i++)
        {
            var inst = new Material(prototype);
            CopySurface(src[i], inst);
            inst.SetFloat("_HeatIntensity", HeatFor(r.gameObject));
            dst[i] = inst;
        }
        r.sharedMaterials = dst;
    }

    static void ApplyToRenderer(Renderer r, Material prototype)
    {
        AssignThermalSignatureLayer(r.transform);
        ApplyMaterialsOnly(r, prototype);
    }

    static float HeatFor(GameObject go)
    {
        for (var t = go.transform; t != null; t = t.parent)
        {
            var n = t.name;
            if (n.IndexOf("Shahed", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0.92f;
            if (n.IndexOf("AirDefense", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0.72f;
            if (n.IndexOf("Luncher", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Launcher", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("136Luncher", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.58f;
        }
        return 0.65f;
    }

    static void CopySurface(Material src, Material dst)
    {
        if (src == null) return;

        if (src.HasProperty("_BaseMap"))
            dst.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
        else if (src.HasProperty("_MainTex"))
            dst.SetTexture("_BaseMap", src.GetTexture("_MainTex"));

        if (src.HasProperty("_BaseColor"))
            dst.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        else if (src.HasProperty("_Color"))
            dst.SetColor("_BaseColor", src.GetColor("_Color"));

        if (src.HasProperty("_BumpMap"))
            dst.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));

        if (src.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        if (src.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
        else if (src.HasProperty("_Glossiness"))
            dst.SetFloat("_Smoothness", src.GetFloat("_Glossiness"));
    }
}