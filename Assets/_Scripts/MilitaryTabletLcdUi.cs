using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Runtime only: poke Option_1/2/3 and swap inactive→active textures (011→01, 022→02, 033→03).
/// Does not move or resize anything — layout stays as authored in the scene.
/// </summary>
public sealed class MilitaryTabletLcdUi : MonoBehaviour
{
    [Header("UI refs (auto-found by name if empty)")]
    [SerializeField] RawImage option1Image;
    [SerializeField] RawImage option2Image;
    [SerializeField] RawImage option3Image;

    [Header("Textures — inactive / checked")]
    [SerializeField] Texture2D option1Inactive; // 011
    [SerializeField] Texture2D option1Active;   // 01
    [SerializeField] Texture2D option2Inactive; // 022
    [SerializeField] Texture2D option2Active;   // 02
    [SerializeField] Texture2D option3Inactive; // 033
    [SerializeField] Texture2D option3Active;   // 03

    RawImage[] optionImages;
    Texture2D[] inactiveTextures;
    Texture2D[] activeTextures;
    bool[] optionActive;
    float[] lastToggleTime;
    bool wired;

    public void Configure(
        Texture2D bg,
        Texture2D off1, Texture2D on1,
        Texture2D off2, Texture2D on2,
        Texture2D off3, Texture2D on3)
    {
        // bg kept for ElevatorDoorIntro signature compatibility; layout owns the background.
        option1Inactive = off1;
        option1Active = on1;
        option2Inactive = off2;
        option2Active = on2;
        option3Inactive = off3;
        option3Active = on3;
    }

    /// <summary>No-op kept for ElevatorDoorIntro calls — layout is scene-authored.</summary>
    public void EnsureUiHierarchy() { }

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
            WirePokeButton(optionImages[i].gameObject, i);
        }
    }

    void WirePokeButton(GameObject button, int index)
    {
        var rt = button.GetComponent<RectTransform>();
        var col = button.GetComponent<BoxCollider>();
        if (col == null) col = button.AddComponent<BoxCollider>();
        col.isTrigger = false;
        // Fit collider to the authored rect — do not move the UI.
        var rect = rt.rect;
        col.center = new Vector3(rect.center.x, rect.center.y, 0f);
        col.size = new Vector3(Mathf.Max(1f, rect.width), Mathf.Max(1f, rect.height), 20f);

        var interactable = button.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = button.AddComponent<XRSimpleInteractable>();
        interactable.selectMode = InteractableSelectMode.Multiple;
        interactable.interactionLayers = -1;

        var pokeFilter = button.GetComponent<XRPokeFilter>();
        if (pokeFilter == null)
            pokeFilter = button.AddComponent<XRPokeFilter>();
        pokeFilter.pokeInteractable = interactable;
        pokeFilter.pokeCollider = col;

        var captured = index;
        interactable.selectEntered.RemoveAllListeners();
        interactable.hoverEntered.RemoveAllListeners();
        interactable.selectEntered.AddListener(_ => OnOptionClicked(captured));
        interactable.hoverEntered.AddListener(args =>
        {
            if (args.interactorObject != null &&
                args.interactorObject.transform.name.Contains("Poke"))
                OnOptionClicked(captured);
        });
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
    }
}
