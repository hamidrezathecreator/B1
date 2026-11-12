using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Finger-touch Option_1/2/3 and swap inactive→active textures (011→01, 022→02, 033→03).
/// Each option image covers the whole canvas, so its collider is shrunk to the opaque bar
/// only; otherwise the three colliders overlap and the wrong option (or none) toggles.
/// A touch counts when a <see cref="TabletFingerTip"/> enters that bar's collider.
/// Layout stays as authored in the scene.
/// </summary>
public sealed class MilitaryTabletLcdUi : MonoBehaviour
{
    public static event Action<bool> ChecklistChanged;

    [Header("UI refs (auto-found by name if empty)")]
    [SerializeField] RawImage option1Image;
    [SerializeField] RawImage option2Image;
    [SerializeField] RawImage option3Image;

    [Header("Textures — inactive / checked")]
    [SerializeField] Texture2D option1Inactive;
    [SerializeField] Texture2D option1Active;
    [SerializeField] Texture2D option2Inactive;
    [SerializeField] Texture2D option2Active;
    [SerializeField] Texture2D option3Inactive;
    [SerializeField] Texture2D option3Active;

    [Header("Touch")]
    [Tooltip("Collider depth in meters, centred on the screen surface.")]
    [SerializeField] float touchDepth = 0.06f;
    [Tooltip("Alpha above this counts as part of an option bar.")]
    [Range(0.05f, 0.95f)] [SerializeField] float opaqueThreshold = 0.35f;
    [Tooltip("Ignore a second touch on the same option for this long, seconds.")]
    [SerializeField] float retouchDelay = 0.4f;

    RawImage[] optionImages;
    Texture2D[] inactiveTextures;
    Texture2D[] activeTextures;
    BoxCollider[] optionColliders;
    bool[] optionActive;
    bool[] fingerInside;
    int[] triggerCount;
    float[] lastToggleTime;
    bool wired;

    public void Configure(
        Texture2D bg,
        Texture2D off1, Texture2D on1,
        Texture2D off2, Texture2D on2,
        Texture2D off3, Texture2D on3)
    {
        option1Inactive = off1;
        option1Active = on1;
        option2Inactive = off2;
        option2Active = on2;
        option3Inactive = off3;
        option3Active = on3;
    }

    public void EnsureUiHierarchy() { }

    void Start()
    {
        ResolveOptionImages();
        ShowInactiveDefaults();
        WireTouchTargets();
    }

    void ResolveOptionImages()
    {
        if (option1Image == null) option1Image = FindRaw("Option_1");
        if (option2Image == null) option2Image = FindRaw("Option_2");
        if (option3Image == null) option3Image = FindRaw("Option_3");

        optionImages = new[] { option1Image, option2Image, option3Image };
        inactiveTextures = new[] { option1Inactive, option2Inactive, option3Inactive };
        activeTextures = new[] { option1Active, option2Active, option3Active };
        optionColliders = new BoxCollider[3];
        optionActive = new bool[3];
        fingerInside = new bool[3];
        triggerCount = new int[3];
        lastToggleTime = new float[3];
        for (var i = 0; i < 3; i++)
            lastToggleTime[i] = -999f;
    }

    RawImage FindRaw(string name)
    {
        var t = transform.Find(name);
        return t != null ? t.GetComponent<RawImage>() : null;
    }

    void ShowInactiveDefaults()
    {
        for (var i = 0; i < 3; i++)
        {
            if (optionImages[i] == null) continue;
            if (inactiveTextures[i] != null)
                optionImages[i].texture = inactiveTextures[i];
            optionActive[i] = false;
        }
    }

    void WireTouchTargets()
    {
        if (wired) return;
        wired = true;

        for (var i = 0; i < 3; i++)
        {
            var image = optionImages[i];
            if (image == null) continue;

            var go = image.gameObject;
            // Old poke setup on these objects fought the tablet grab and never fired.
            var oldPoke = go.GetComponent<XRPokeFilter>();
            if (oldPoke != null) Destroy(oldPoke);
            var oldInteractable = go.GetComponent<XRSimpleInteractable>();
            if (oldInteractable != null) Destroy(oldInteractable);

            var col = go.GetComponent<BoxCollider>();
            if (col == null) col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            FitColliderToOpaqueBar(image, col, i);
            optionColliders[i] = col;

            var touch = go.GetComponent<TabletOptionTouch>();
            if (touch == null) touch = go.AddComponent<TabletOptionTouch>();
            touch.Bind(this, i);
        }
    }

    void FitColliderToOpaqueBar(RawImage image, BoxCollider col, int index)
    {
        var rt = image.rectTransform;
        var rect = rt.rect;
        var uv = OpaqueUvRect(inactiveTextures[index] != null ? inactiveTextures[index] : image.texture as Texture2D, index);

        var xMin = rect.xMin + uv.xMin * rect.width;
        var xMax = rect.xMin + uv.xMax * rect.width;
        var yMin = rect.yMin + uv.yMin * rect.height;
        var yMax = rect.yMin + uv.yMax * rect.height;

        var zScale = Mathf.Max(0.000001f, Mathf.Abs(rt.lossyScale.z));
        col.center = new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, 0f);
        col.size = new Vector3(
            Mathf.Max(1f, xMax - xMin),
            Mathf.Max(1f, yMax - yMin),
            touchDepth / zScale);
    }

    /// <summary>UV bounds of the opaque pixels. Falls back to thirds when the texture is not readable.</summary>
    Rect OpaqueUvRect(Texture2D tex, int index)
    {
        if (tex != null && tex.isReadable)
        {
            var pixels = tex.GetPixels32();
            var w = tex.width;
            var h = tex.height;
            var limit = (byte)Mathf.RoundToInt(opaqueThreshold * 255f);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (var y = 0; y < h; y += 2)
            {
                var row = y * w;
                for (var x = 0; x < w; x += 2)
                {
                    if (pixels[row + x].a <= limit) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX >= minX && maxY >= minY)
                return Rect.MinMaxRect(minX / (float)w, minY / (float)h, (maxX + 1f) / w, (maxY + 1f) / h);
        }

        // Option_1 is the top bar, Option_3 the bottom.
        var band = 1f / 3f;
        var top = 1f - index * band;
        return Rect.MinMaxRect(0.04f, top - band, 0.96f, top);
    }

    internal void FingerEnter(int index)
    {
        if (triggerCount == null || index < 0 || index >= triggerCount.Length) return;
        triggerCount[index]++;
    }

    internal void FingerExit(int index)
    {
        if (triggerCount == null || index < 0 || index >= triggerCount.Length) return;
        triggerCount[index] = Mathf.Max(0, triggerCount[index] - 1);
    }

    void Update()
    {
        if (optionColliders == null) return;

        for (var i = 0; i < optionColliders.Length; i++)
        {
            var col = optionColliders[i];
            if (col == null) continue;

            // Trigger callbacks, plus a direct overlap test in case physics missed the
            // contact (fast poke, or the tablet moved this frame while being held).
            var inside = triggerCount[i] > 0 || TabletFingerTip.AnyInside(col);
            if (inside && !fingerInside[i])
                OnOptionTouched(i);
            fingerInside[i] = inside;
        }
    }

    void OnOptionTouched(int index)
    {
        if (optionImages[index] == null) return;
        if (Time.unscaledTime - lastToggleTime[index] < retouchDelay) return;

        lastToggleTime[index] = Time.unscaledTime;
        ButtonClickSound.Play();
        optionActive[index] = !optionActive[index];
        var tex = optionActive[index] ? activeTextures[index] : inactiveTextures[index];
        if (tex != null)
            optionImages[index].texture = tex;

        var allChecked = optionActive[0] && optionActive[1] && optionActive[2];
        ChecklistChanged?.Invoke(allChecked);
    }
}

/// <summary>Trigger relay on each option bar; forwards finger contacts to the tablet UI.</summary>
public sealed class TabletOptionTouch : MonoBehaviour
{
    MilitaryTabletLcdUi owner;
    int index;

    public void Bind(MilitaryTabletLcdUi ui, int optionIndex)
    {
        owner = ui;
        index = optionIndex;
    }

    void OnTriggerEnter(Collider other)
    {
        if (owner != null && other.GetComponent<TabletFingerTip>() != null)
            owner.FingerEnter(index);
    }

    void OnTriggerExit(Collider other)
    {
        if (owner != null && other.GetComponent<TabletFingerTip>() != null)
            owner.FingerExit(index);
    }
}

/// <summary>
/// Small trigger sphere that follows a fingertip. Kinematic rigidbody so it reports
/// trigger contacts with the tablet bars even though the tablet is kinematic too.
/// </summary>
public sealed class TabletFingerTip : MonoBehaviour
{
    static readonly List<TabletFingerTip> Active = new List<TabletFingerTip>();

    SphereCollider sphere;

    public static TabletFingerTip Create(string name, float radius = 0.012f)
    {
        var go = new GameObject(name);
        var tip = go.AddComponent<TabletFingerTip>();
        tip.sphere = go.AddComponent<SphereCollider>();
        tip.sphere.isTrigger = true;
        tip.sphere.radius = radius;
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        return tip;
    }

    public void MoveTo(Vector3 worldPosition)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        transform.position = worldPosition;
    }

    public void Park()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>True when any active fingertip sphere overlaps the collider.</summary>
    public static bool AnyInside(Collider target)
    {
        for (var i = 0; i < Active.Count; i++)
        {
            var tip = Active[i];
            if (tip == null || tip.sphere == null) continue;
            var center = tip.transform.position;
            var closest = target.ClosestPoint(center);
            var reach = tip.sphere.radius * Mathf.Max(tip.transform.lossyScale.x, 0.0001f);
            if ((closest - center).sqrMagnitude <= reach * reach)
                return true;
        }
        return false;
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }
}
