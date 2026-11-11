using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Floor guide: moving yellow arrows on the ground from a start point to a goal, nothing under them.
/// The dark strip, side lines and goal ring are optional and off by default.
/// No colliders — it never blocks walking or teleport. Hides itself when the player arrives.
/// </summary>
public sealed class FloorPathGuide : MonoBehaviour
{
    [SerializeField] float stripWidth = 0.55f;
    [SerializeField] float arrowSpacing = 0.7f;
    [SerializeField] float arrowSpeed = 0.9f;
    [SerializeField] float arriveDistance = 1.2f;
    [SerializeField] bool showStrip;
    [SerializeField] bool showSideLines;
    [SerializeField] bool showGoalRing;
    [SerializeField] Color stripColor = new Color(0.12f, 0.13f, 0.12f, 1f);
    [SerializeField] Color edgeColor = new Color(0.95f, 0.76f, 0.19f, 1f);
    [SerializeField] Color arrowColor = new Color(0.98f, 0.80f, 0.20f, 1f);

    readonly List<Vector3> points = new List<Vector3>();
    readonly List<float> cumulative = new List<float>();
    readonly List<Transform> arrows = new List<Transform>();
    float totalLength;
    float scroll;
    float reveal;
    Transform player;
    Vector3 goal;

    public static FloorPathGuide Create(IList<Vector3> worldPoints, Transform playerRoot, params Collider[] ignoreForFloor)
    {
        var go = new GameObject("PathToTable");
        var guide = go.AddComponent<FloorPathGuide>();
        guide.player = playerRoot;
        guide.Build(worldPoints, ignoreForFloor);
        return guide;
    }

    void Build(IList<Vector3> worldPoints, Collider[] ignore)
    {
        points.Clear();
        foreach (var p in worldPoints)
            points.Add(SnapToFloor(p, ignore));
        if (points.Count < 2) { Destroy(gameObject); return; }

        cumulative.Clear();
        cumulative.Add(0f);
        totalLength = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            totalLength += Flat(points[i] - points[i - 1]).magnitude;
            cumulative.Add(totalLength);
        }
        goal = points[points.Count - 1];

        var arrowMat = MakeMaterial(arrowColor);
        var stripMat = showStrip ? MakeMaterial(stripColor) : null;
        var edgeMat = showSideLines || showGoalRing ? MakeMaterial(edgeColor) : null;

        for (var i = 1; i < points.Count; i++)
        {
            if (showStrip)
                BuildSegment(points[i - 1], points[i], stripWidth, 0.008f, stripMat, "Strip");
            if (!showSideLines) continue;
            var side = Vector3.Cross(Vector3.up, Flat(points[i] - points[i - 1]).normalized);
            var edgeOffset = side * (stripWidth * 0.5f - 0.02f);
            BuildSegment(points[i - 1] + edgeOffset, points[i] + edgeOffset, 0.035f, 0.011f, edgeMat, "EdgeR");
            BuildSegment(points[i - 1] - edgeOffset, points[i] - edgeOffset, 0.035f, 0.011f, edgeMat, "EdgeL");
        }

        var arrowMesh = BuildChevronMesh(stripWidth * 0.75f);
        var count = Mathf.Max(1, Mathf.FloorToInt(totalLength / arrowSpacing));
        for (var i = 0; i < count; i++)
        {
            var arrow = new GameObject("Arrow_" + i);
            arrow.transform.SetParent(transform, false);
            arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
            var r = arrow.AddComponent<MeshRenderer>();
            r.sharedMaterial = arrowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrows.Add(arrow.transform);
        }

        if (showGoalRing)
        {
            var ring = new GameObject("GoalMark");
            ring.transform.SetParent(transform, false);
            ring.transform.position = goal + Vector3.up * 0.012f;
            ring.AddComponent<MeshFilter>().sharedMesh = BuildRingMesh(0.32f, 0.4f, 40);
            var rr = ring.AddComponent<MeshRenderer>();
            rr.sharedMaterial = edgeMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        transform.localScale = new Vector3(1f, 1f, 1f);
        reveal = 0f;
    }

    void Update()
    {
        if (totalLength <= 0f) return;

        // Arrows flow toward the goal.
        scroll = (scroll + Time.deltaTime * arrowSpeed) % arrowSpacing;
        // Reveal sweeps from the elevator to the table once when it appears.
        reveal = Mathf.MoveTowards(reveal, totalLength + arrowSpacing, Time.deltaTime * 3f);

        for (var i = 0; i < arrows.Count; i++)
        {
            var d = i * arrowSpacing + scroll;
            var visible = d <= totalLength - 0.25f && d <= reveal;
            arrows[i].gameObject.SetActive(visible);
            if (!visible) continue;

            Sample(d, out var pos, out var dir);
            // Fade-in scale near the start, fade-out near the end.
            var edge = Mathf.Min(d, totalLength - d);
            var s = Mathf.Clamp01(edge / 0.4f);
            arrows[i].SetPositionAndRotation(pos + Vector3.up * 0.014f, Quaternion.LookRotation(dir, Vector3.up));
            arrows[i].localScale = new Vector3(s, 1f, s);
        }

        if (player != null)
        {
            var head = player.GetComponent<XROrigin>();
            var p = head != null && head.Camera != null ? head.Camera.transform.position : player.position;
            if (Flat(p - goal).magnitude < arriveDistance)
                Destroy(gameObject);
        }
    }

    void Sample(float distance, out Vector3 pos, out Vector3 dir)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (distance > cumulative[i] && i < points.Count - 1) continue;
            var segLen = cumulative[i] - cumulative[i - 1];
            var t = segLen > 0.0001f ? Mathf.Clamp01((distance - cumulative[i - 1]) / segLen) : 0f;
            pos = Vector3.Lerp(points[i - 1], points[i], t);
            dir = Flat(points[i] - points[i - 1]).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            return;
        }
        pos = points[points.Count - 1];
        dir = Vector3.forward;
    }

    void BuildSegment(Vector3 a, Vector3 b, float width, float lift, Material mat, string name)
    {
        var dir = Flat(b - a);
        var len = dir.magnitude;
        if (len < 0.01f) return;
        dir /= len;
        var side = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
        var up = Vector3.up * lift;

        var mesh = new Mesh { name = name };
        mesh.vertices = new[]
        {
            a - side + up, a + side + up, b + side + up, b - side + up
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Mesh BuildChevronMesh(float size)
    {
        var h = size * 0.5f;
        var t = size * 0.22f; // arm thickness
        var mesh = new Mesh { name = "Chevron" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 0f, h),        // 0 tip
            new Vector3(h, 0f, -h * 0.2f), // 1 right outer
            new Vector3(h - t, 0f, -h * 0.2f), // 2 right inner
            new Vector3(0f, 0f, h - t * 1.4f), // 3 inner tip
            new Vector3(-(h - t), 0f, -h * 0.2f), // 4 left inner
            new Vector3(-h, 0f, -h * 0.2f), // 5 left outer
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildRingMesh(float inner, float outer, int segments)
    {
        var verts = new Vector3[segments * 2];
        var tris = new int[segments * 6];
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2f / segments;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = d * inner;
            verts[i * 2 + 1] = d * outer;
            var n = (i + 1) % segments;
            tris[i * 6 + 0] = i * 2;
            tris[i * 6 + 1] = n * 2;
            tris[i * 6 + 2] = i * 2 + 1;
            tris[i * 6 + 3] = i * 2 + 1;
            tris[i * 6 + 4] = n * 2;
            tris[i * 6 + 5] = n * 2 + 1;
        }
        var mesh = new Mesh { name = "GoalRing", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var mat = new Material(shader) { name = "PathGuide" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // double-sided
        return mat;
    }

    /// <summary>
    /// Floor under <paramref name="p"/>. Only the teleport floor (Ground layer) counts, so the path
    /// never lies on a case, table or prop. Falls back to the given height, which callers pass at floor level.
    /// </summary>
    static Vector3 SnapToFloor(Vector3 p, Collider[] ignore)
    {
        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) return p;

        var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 6f, 1 << groundLayer, QueryTriggerInteraction.Ignore);
        var found = false;
        var best = p;
        var bestGap = float.MaxValue;
        foreach (var hit in hits)
        {
            var col = hit.collider;
            if (col.GetComponentInParent<XROrigin>() != null) continue;
            if (col is CharacterController) continue;
            if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
            if (IsIgnored(col, ignore)) continue;
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.8f) continue; // walls / slopes
            // Points are given at floor height; anything well above it is not the floor.
            if (hit.point.y > p.y + 0.3f) continue;

            // Closest to the expected floor height, not the first surface the ray meets.
            var gap = Mathf.Abs(hit.point.y - p.y);
            if (gap >= bestGap) continue;
            bestGap = gap;
            best = hit.point;
            found = true;
        }
        return found ? best : p;
    }

    static bool IsIgnored(Collider col, Collider[] ignore)
    {
        if (ignore == null) return false;
        foreach (var c in ignore)
            if (c != null && col == c) return true;
        return false;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
