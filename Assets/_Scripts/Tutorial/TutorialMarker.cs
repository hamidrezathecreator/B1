using UnityEngine;

/// <summary>
/// "Go here" beacon on a target: a blue glowing ring around its base, a soft beam of light
/// fading upward, and a bobbing diamond above it pointing down.
/// </summary>
public sealed class TutorialMarker : MonoBehaviour
{
    static readonly Color Blue = new Color(0.15f, 0.65f, 1f, 1f);
    static readonly Color BlueBright = new Color(0.55f, 0.88f, 1f, 1f);

    const float BeamHeight = 0.55f;
    const float RingWidth = 0.025f;

    Transform target;
    Vector3 baseOffset;
    float diamondHeight;
    Transform diamond;
    Transform ring;
    Material solidMaterial;
    Material beamMaterial;

    public static GameObject Create(Transform target)
    {
        var go = new GameObject("Tutorial Marker");
        var marker = go.AddComponent<TutorialMarker>();
        marker.Build(target);
        return go;
    }

    void Build(Transform followTarget)
    {
        target = followTarget;

        var bounds = new Bounds(target.position, Vector3.one * 0.3f);
        var has = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }

        // Base of the beacon sits at the bottom center of the target.
        var basePoint = new Vector3(bounds.center.x, bounds.min.y + 0.005f, bounds.center.z);
        baseOffset = basePoint - target.position;
        diamondHeight = bounds.size.y + 0.25f;
        var radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.25f + 0.04f;

        solidMaterial = NewSolidMaterial();
        beamMaterial = NewBeamMaterial();

        ring = NewPart("Ring", RingMesh(radius, RingWidth, 48), solidMaterial).transform;
        NewPart("Beam", BeamMesh(radius, BeamHeight, 48), beamMaterial);
        diamond = NewPart("Diamond", DiamondMesh(), solidMaterial).transform;

        LateUpdate();
    }

    GameObject NewPart(string partName, Mesh mesh, Material material)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }
        // Target switched off (e.g. a placeholder filled in): hide with it.
        if (!target.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        transform.SetPositionAndRotation(target.position + baseOffset, Quaternion.identity);

        var pulse = (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f;
        if (diamond != null)
        {
            diamond.localPosition = Vector3.up * (diamondHeight + pulse * 0.06f);
            diamond.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
        }
        if (ring != null)
            ring.localScale = Vector3.one * (1f + pulse * 0.08f);

        var color = Color.Lerp(Blue, BlueBright, pulse);
        SetColor(solidMaterial, color);
        SetColor(beamMaterial, new Color(color.r, color.g, color.b, 0.45f + 0.3f * pulse));
    }

    void OnDestroy()
    {
        if (solidMaterial != null) Destroy(solidMaterial);
        if (beamMaterial != null) Destroy(beamMaterial);
    }

    static void SetColor(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
    }

    static Mesh RingMesh(float radius, float width, int segments)
    {
        var verts = new Vector3[segments * 2];
        var tris = new int[segments * 6];
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2f / segments;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = d * (radius - width * 0.5f);
            verts[i * 2 + 1] = d * (radius + width * 0.5f);
            var n = (i + 1) % segments;
            tris[i * 6 + 0] = i * 2;
            tris[i * 6 + 1] = n * 2;
            tris[i * 6 + 2] = i * 2 + 1;
            tris[i * 6 + 3] = i * 2 + 1;
            tris[i * 6 + 4] = n * 2;
            tris[i * 6 + 5] = n * 2 + 1;
        }
        var mesh = new Mesh { name = "MarkerRing", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Open tube whose vertex alpha fades from the ground up.</summary>
    static Mesh BeamMesh(float radius, float height, int segments)
    {
        var verts = new Vector3[segments * 2];
        var colors = new Color[segments * 2];
        var tris = new int[segments * 6];
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2f / segments;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            verts[i * 2] = d;
            verts[i * 2 + 1] = d + Vector3.up * height;
            colors[i * 2] = new Color(1f, 1f, 1f, 1f);
            colors[i * 2 + 1] = new Color(1f, 1f, 1f, 0f);
            var n = (i + 1) % segments;
            tris[i * 6 + 0] = i * 2;
            tris[i * 6 + 1] = i * 2 + 1;
            tris[i * 6 + 2] = n * 2;
            tris[i * 6 + 3] = n * 2;
            tris[i * 6 + 4] = i * 2 + 1;
            tris[i * 6 + 5] = n * 2 + 1;
        }
        var mesh = new Mesh { name = "MarkerBeam", vertices = verts, colors = colors, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Two four-sided pyramids back to back, long tip pointing down.</summary>
    static Mesh DiamondMesh()
    {
        const float w = 0.06f;
        const float down = 0.12f;
        const float up = 0.05f;
        var bottom = new Vector3(0f, 0f, 0f);
        var top = new Vector3(0f, down + up, 0f);
        var a = new Vector3(-w, down, -w);
        var b = new Vector3(w, down, -w);
        var c = new Vector3(w, down, w);
        var d = new Vector3(-w, down, w);
        var mesh = new Mesh { name = "MarkerDiamond" };
        mesh.vertices = new[]
        {
            bottom, b, a, bottom, c, b, bottom, d, c, bottom, a, d,
            top, a, b, top, b, c, top, c, d, top, d, a,
        };
        var tris = new int[24];
        for (var i = 0; i < 24; i++) tris[i] = i;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material NewSolidMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader) { name = "MarkerBlue" };
        SetColor(mat, Blue);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        return mat;
    }

    static Material NewBeamMaterial()
    {
        // Transparent URP particle material that ships in the build; it multiplies vertex color,
        // so the beam mesh's alpha fades it out toward the top.
        var source = Resources.Load<Material>("Effects/LaunchSmoke");
        if (source == null) return NewSolidMaterial();
        var mat = new Material(source) { name = "MarkerBeam" };
        mat.SetTexture("_BaseMap", null);
        mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        SetColor(mat, new Color(Blue.r, Blue.g, Blue.b, 0.6f));
        return mat;
    }
}
