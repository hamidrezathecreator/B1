using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Black caption floating just above a hand, facing the headset. Same idea as a
/// "Click Right Thumbstick" prompt: one line, then it goes away.
/// </summary>
public sealed class HandHintBanner : MonoBehaviour
{
    const string DefaultMessage = "روی دکمه آسانسور کلیک کن";

    Transform hand;
    Camera view;
    RectTransform rect;
    LineRenderer line;
    Transform dot;
    Vector3 shownScale;

    public static HandHintBanner Create(Camera headset, TMP_FontAsset font, string message = null)
    {
        var root = new GameObject("HandHintBanner");
        var banner = root.AddComponent<HandHintBanner>();
        banner.view = headset;
        banner.Build(font, string.IsNullOrWhiteSpace(message) ? DefaultMessage : message);
        return banner;
    }

    public void SetHand(Transform target) => hand = target;

    public void Hide()
    {
        if (this != null)
            Destroy(gameObject);
    }

    void LateUpdate()
    {
        var visible = hand != null && view != null;
        rect.localScale = visible ? shownScale : Vector3.zero;
        if (line != null) line.enabled = visible;
        if (dot != null) dot.gameObject.SetActive(visible);
        if (!visible) return;

        var up = view.transform.up;
        var pos = hand.position + up * 0.16f + view.transform.right * 0.02f;
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - view.transform.position, up));

        var start = hand.position + up * 0.03f;
        var end = pos - up * 0.025f;
        var bend = Vector3.Lerp(start, end, 0.5f) + view.transform.right * 0.05f;
        const int steps = 14;
        line.positionCount = steps;
        for (var i = 0; i < steps; i++)
        {
            var t = i / (steps - 1f);
            var a = Vector3.Lerp(start, bend, t);
            var b = Vector3.Lerp(bend, end, t);
            line.SetPosition(i, Vector3.Lerp(a, b, t));
        }
        dot.position = end;
    }

    void Build(TMP_FontAsset font, string message)
    {
        rect = gameObject.AddComponent<RectTransform>();
        var lines = message.Split('\n').Length;
        rect.sizeDelta = new Vector2(720f, 78f + (lines - 1) * 44f);
        BuildCurve();
        shownScale = Vector3.one * 0.00062f;
        rect.localScale = shownScale;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200;

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(rect, false);
        var backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        var image = background.GetComponent<Image>();
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.04f, 0.04f, 0.04f, 0.94f);
        image.raycastTarget = false;

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(rect, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 6f);
        labelRect.offsetMax = new Vector2(-18f, -6f);

        var label = labelObject.AddComponent<RTLTextMeshPro>();
        if (font != null)
            label.font = font;
        label.Farsi = true;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 34f;
        // Long tutorial lines shrink to fit instead of running off the banner.
        label.enableAutoSizing = true;
        label.fontSizeMin = 20f;
        label.fontSizeMax = 34f;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableWordWrapping = lines > 1;
        label.text = message;
    }

    void BuildCurve()
    {
        var lineObject = new GameObject("HintCurve");
        lineObject.transform.SetParent(transform, false);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = 2;
        line.startWidth = 0.0045f;
        line.endWidth = 0.0045f;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            line.material = new Material(shader);
            if (line.material.HasProperty("_BaseColor"))
                line.material.SetColor("_BaseColor", Color.white);
            line.material.color = Color.white;
        }
        line.startColor = Color.white;
        line.endColor = Color.white;

        var dotObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObject.name = "HintDot";
        dotObject.transform.SetParent(transform, false);
        dotObject.transform.localScale = Vector3.one * 0.012f;
        var collider = dotObject.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        var renderer = dotObject.GetComponent<Renderer>();
        if (renderer != null && line.material != null)
            renderer.sharedMaterial = line.material;
        dot = dotObject.transform;
    }

    static Sprite RoundedSprite()
    {
        const int width = 64;
        const int height = 32;
        const int radius = 14;
        var pixels = new Color32[width * height];
        var fill = new Color32(255, 255, 255, 255);
        var clear = new Color32(255, 255, 255, 0);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var inside = x >= radius && x < width - radius || y >= radius && y < height - radius;
            if (!inside)
            {
                var cx = x < radius ? radius : width - 1 - radius;
                var cy = y < radius ? radius : height - 1 - radius;
                var dx = x - cx;
                var dy = y - cy;
                inside = dx * dx + dy * dy <= radius * radius;
            }
            pixels[y * width + x] = inside ? fill : clear;
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        var border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}
