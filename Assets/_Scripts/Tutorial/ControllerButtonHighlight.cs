using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes one button on the controller model glow and pulse (Trigger, ThumbStick, Bumper / grip,
/// Button_A ...) so the player sees which button the tutorial means. Destroy to restore it.
/// </summary>
public sealed class ControllerButtonHighlight : MonoBehaviour
{
    static readonly Color Bright = new Color(1f, 0.85f, 0.1f, 1f);
    static readonly Color Deep = new Color(1f, 0.35f, 0f, 1f);

    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Material[]> originals = new List<Material[]>();
    Material glow;
    Transform halo;
    Material haloMaterial;
    Vector3 haloScale;

    /// <summary>Highlights the first part found under <paramref name="controller"/> matching one of the names.</summary>
    public static ControllerButtonHighlight Create(Transform controller, params string[] partNames)
    {
        if (controller == null) return null;
        var part = FindPart(controller, partNames);
        var go = new GameObject("Button Highlight");
        go.transform.SetParent(part != null ? part : controller, false);
        var highlight = go.AddComponent<ControllerButtonHighlight>();
        highlight.Build(part != null ? part : controller, part != null);
        return highlight;
    }

    static Transform FindPart(Transform root, string[] names)
    {
        foreach (var name in names)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }
        return null;
    }

    void Build(Transform part, bool foundPart)
    {
        glow = NewGlowMaterial();
        if (foundPart)
        {
            foreach (var r in part.GetComponents<Renderer>())
            {
                renderers.Add(r);
                originals.Add(r.sharedMaterials);
                var mats = new Material[r.sharedMaterials.Length];
                for (var i = 0; i < mats.Length; i++) mats[i] = glow;
                r.sharedMaterials = mats;
            }
        }

        // A soft pulsing ball around the button, so it reads even when the part is tiny.
        var size = 0.03f;
        var center = part.position;
        if (renderers.Count > 0)
        {
            var b = renderers[0].bounds;
            center = b.center;
            size = Mathf.Clamp(b.size.magnitude * 1.6f, 0.025f, 0.06f);
        }

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Halo";
        var col = sphere.GetComponent<Collider>();
        if (col != null) Destroy(col);
        sphere.transform.SetParent(transform, false);
        sphere.transform.position = center;
        haloScale = Vector3.one * size / Mathf.Max(0.0001f, transform.lossyScale.x);
        sphere.transform.localScale = haloScale;
        haloMaterial = NewHaloMaterial();
        var sr = sphere.GetComponent<Renderer>();
        sr.sharedMaterial = haloMaterial;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        halo = sphere.transform;
    }

    void LateUpdate()
    {
        var t = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
        var color = Color.Lerp(Deep, Bright, t);
        if (glow != null)
        {
            if (glow.HasProperty("_BaseColor")) glow.SetColor("_BaseColor", color);
            glow.color = color;
        }
        if (halo != null)
        {
            halo.localScale = haloScale * (1f + 0.35f * t);
            var c = new Color(color.r, color.g, color.b, 0.25f + 0.25f * t);
            if (haloMaterial.HasProperty("_BaseColor")) haloMaterial.SetColor("_BaseColor", c);
            haloMaterial.color = c;
        }
    }

    void OnDestroy()
    {
        for (var i = 0; i < renderers.Count; i++)
            if (renderers[i] != null) renderers[i].sharedMaterials = originals[i];
        if (glow != null) Destroy(glow);
        if (haloMaterial != null) Destroy(haloMaterial);
    }

    static Material NewGlowMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "ButtonGlow" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Bright);
        mat.color = Bright;
        return mat;
    }

    static Material NewHaloMaterial()
    {
        // The launch smoke material is a transparent URP particle shader that ships in the build.
        var source = Resources.Load<Material>("Effects/LaunchSmoke");
        Material mat;
        if (source != null)
        {
            mat = new Material(source);
            mat.SetTexture("_BaseMap", null);
            mat.SetTexture("_MainTex", null);
        }
        else
        {
            mat = NewGlowMaterial();
        }
        mat.name = "ButtonHalo";
        var c = new Color(Bright.r, Bright.g, Bright.b, 0.35f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        mat.color = c;
        return mat;
    }
}
