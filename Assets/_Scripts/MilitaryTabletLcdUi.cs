using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Poke Option_1/2/3 and swap inactive→active textures (011→01, 022→02, 033→03).
/// Each option image is a mostly transparent full-canvas sprite, so a hit counts
/// only on the opaque bar. Layout stays as authored in the scene.
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

    static readonly List<MilitaryTabletLcdUi> Active = new List<MilitaryTabletLcdUi>();

    RawImage[] optionImages;
    Texture2D[] inactiveTextures;
    Texture2D[] activeTextures;
    bool[] optionActive;
    bool[] fingerInside;
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

    /// <summary>Finger-tip poke. Hits the opaque pixels of each option, not the empty canvas.</summary>
    public static void PokeAll(Vector3 worldPoint)
    {
        for (var i = 0; i < Active.Count; i++)
        {
            if (Active[i] != null)
                Active[i].Poke(worldPoint);
        }
    }

    void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

    void Start()
    {
        ResolveOptionImages();
        ShowInactiveDefaults();
        WireClicks();
    }

    void ResolveOptionImages()
    {
        if (option1Image == null) option1Image = FindRaw("Option_1");
        if (option2Image == null) option2Image = FindRaw("Option_2");
        if (option3Image == null) option3Image = FindRaw("Option_3");

        optionImages = new[] { option1Image, option2Image, option3Image };
        inactiveTextures = new[] { option1Inactive, option2Inactive, option3Inactive };
        activeTextures = new[] { option1Active, option2Active, option3Active };
        optionActive = new bool[3];
        fingerInside = new bool[3];
        lastToggleTime = new float[3];
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

    void WireClicks()
    {
        if (wired) return;
        wired = true;

        for (var i = 0; i < 3; i++)
        {
            if (optionImages[i] == null) continue;
            WirePokeButton(optionImages[i].gameObject);
        }
    }

    void WirePokeButton(GameObject button)
    {
        var rt = button.GetComponent<RectTransform>();
        var col = button.GetComponent<BoxCollider>();
        if (col == null) col = button.AddComponent<BoxCollider>();
        // Trigger so the tablet body stays grabbable by either hand.
        // Clicks are tested from the right index tip, not from this collider.
        col.isTrigger = true;

        var rect = rt.rect;
        var zScale = Mathf.Max(0.00001f, Mathf.Abs(rt.lossyScale.z));
        col.center = new Vector3(rect.center.x, rect.center.y, 0f);
        col.size = new Vector3(
            Mathf.Max(1f, rect.width),
            Mathf.Max(1f, rect.height),
            0.08f / zScale);

        var interactable = button.GetComponent<XRSimpleInteractable>();
        if (interactable != null)
            Destroy(interactable);
        var pokeFilter = button.GetComponent<XRPokeFilter>();
        if (pokeFilter != null)
            Destroy(pokeFilter);
    }

    void Poke(Vector3 worldPoint)
    {
        if (optionImages == null) return;
        for (var i = 0; i < optionImages.Length; i++)
        {
            var inside = HitsOpaque(i, worldPoint);
            if (inside && !fingerInside[i])
                OnOptionClicked(i);
            fingerInside[i] = inside;
        }
    }

    bool HitsOpaque(int index, Vector3 worldPoint)
    {
        var image = optionImages[index];
        if (image == null) return false;
        var rt = image.rectTransform;
        var local = rt.InverseTransformPoint(worldPoint);
        var rect = rt.rect;
        if (local.x < rect.xMin || local.x > rect.xMax || local.y < rect.yMin || local.y > rect.yMax)
            return false;

        var zScale = Mathf.Max(0.00001f, Mathf.Abs(rt.lossyScale.z));
        if (Mathf.Abs(local.z) * zScale > 0.06f)
            return false;

        var tex = image.texture as Texture2D;
        if (tex == null || !tex.isReadable)
            return true;

        var u = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
        var v = Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
        return tex.GetPixelBilinear(u, v).a > 0.35f;
    }

    void OnOptionClicked(int index)
    {
        if (optionImages == null || index < 0 || index >= optionImages.Length)
            return;
        if (optionImages[index] == null)
            return;
        if (Time.unscaledTime - lastToggleTime[index] < 0.35f)
            return;

        lastToggleTime[index] = Time.unscaledTime;
        optionActive[index] = !optionActive[index];
        var tex = optionActive[index] ? activeTextures[index] : inactiveTextures[index];
        if (tex != null)
            optionImages[index].texture = tex;

        var allChecked = optionActive[0] && optionActive[1] && optionActive[2];
        ChecklistChanged?.Invoke(allChecked);
    }
}
