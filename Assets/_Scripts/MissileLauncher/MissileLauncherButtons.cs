using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>Physical button adapter. Emits events without knowing about launchers or tutorials.</summary>
public sealed class MissileLauncherButtons : MonoBehaviour
{
    [Tooltip("Add XR Simple Interactable to your rotate button model, then drag that component here. XR Base Interactable is abstract and cannot be added directly. VRPressButton is also supported.")]
    [SerializeField] XRBaseInteractable rotateButton;
    [Tooltip("Add XR Simple Interactable to your fire button model, then drag that component here. Configure its collider and XR Poke Filter for hand pushing.")]
    [SerializeField] XRBaseInteractable fireButton;
    public UnityEvent rotatePressed = new UnityEvent();
    public UnityEvent firePressed = new UnityEvent();
    public bool IsConfigured => rotateButton != null && fireButton != null && rotateButton != fireButton;
    public bool HasEnabledButtons => rotateAllowed || fireAllowed;
    bool rotateAllowed;
    bool fireAllowed;
    bool rotateFingerInside;
    bool fireFingerInside;

    void OnEnable()
    {
        DisableButtons();
        if (rotateButton != null)
        {
            rotateButton.selectEntered.AddListener(OnRotateSelected);
            rotateButton.hoverEntered.AddListener(OnRotateHovered);
        }
        if (fireButton != null)
        {
            fireButton.selectEntered.AddListener(OnFireSelected);
            fireButton.hoverEntered.AddListener(OnFireHovered);
        }
    }

    void OnDisable()
    {
        if (rotateButton != null)
        {
            rotateButton.selectEntered.RemoveListener(OnRotateSelected);
            rotateButton.hoverEntered.RemoveListener(OnRotateHovered);
        }
        if (fireButton != null)
        {
            fireButton.selectEntered.RemoveListener(OnFireSelected);
            fireButton.hoverEntered.RemoveListener(OnFireHovered);
        }
        DisableButtons();
    }

    void OnRotateSelected(SelectEnterEventArgs _) => PressRotate();
    void OnFireSelected(SelectEnterEventArgs _) => PressFire();

    void OnRotateHovered(HoverEnterEventArgs args)
    {
        if (args.interactorObject is XRPokeInteractor) PressRotate();
    }

    void OnFireHovered(HoverEnterEventArgs args)
    {
        if (args.interactorObject is XRPokeInteractor) PressFire();
    }

    void Update()
    {
        var rotateInside = FingerInside(rotateButton);
        var fireInside = FingerInside(fireButton);
        if (rotateInside && !rotateFingerInside) PressRotate();
        if (fireInside && !fireFingerInside) PressFire();
        rotateFingerInside = rotateInside;
        fireFingerInside = fireInside;
    }

    static bool FingerInside(XRBaseInteractable button)
    {
        if (button == null || !button.gameObject.activeInHierarchy) return false;
        foreach (var collider in button.colliders)
            if (collider != null && collider.enabled && TabletFingerTip.AnyInside(collider)) return true;
        return false;
    }

    [ContextMenu("Test/Press Rotate (Play Mode)")]
    public void PressRotate()
    {
        if (!isActiveAndEnabled || !rotateAllowed) return;
        ButtonClickSound.Play();
        DisableButtons();
        rotatePressed.Invoke();
    }

    [ContextMenu("Test/Press Fire (Play Mode)")]
    public void PressFire()
    {
        if (!isActiveAndEnabled || !fireAllowed) return;
        ButtonClickSound.Play();
        DisableButtons();
        firePressed.Invoke();
    }

    public void EnableRotate() => EnableButtons(true, false);

    public void EnableFire() => EnableButtons(false, true);

    public void EnableBoth() => EnableButtons(true, true);

    void EnableButtons(bool allowRotate, bool allowFire)
    {
        DisableButtons();
        if (!isActiveAndEnabled) return;
        if (allowRotate && rotateButton != null)
        {
            if (rotateButton is VRPressButton rotatePressButton) rotatePressButton.ResetButton();
            rotateAllowed = true;
            rotateButton.enabled = true;
        }
        if (allowFire && fireButton != null)
        {
            if (fireButton is VRPressButton firePressButton) firePressButton.ResetButton();
            fireAllowed = true;
            fireButton.enabled = true;
        }
    }

    public void DisableButtons()
    {
        rotateAllowed = false;
        fireAllowed = false;
        if (rotateButton != null) rotateButton.enabled = false;
        if (fireButton != null) fireButton.enabled = false;
    }
}
