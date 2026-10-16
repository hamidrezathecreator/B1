using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
    bool rotateAllowed;
    bool fireAllowed;

    void OnEnable()
    {
        DisableButtons();
        if (rotateButton != null) rotateButton.selectEntered.AddListener(OnRotateSelected);
        if (fireButton != null) fireButton.selectEntered.AddListener(OnFireSelected);
    }

    void OnDisable()
    {
        if (rotateButton != null) rotateButton.selectEntered.RemoveListener(OnRotateSelected);
        if (fireButton != null) fireButton.selectEntered.RemoveListener(OnFireSelected);
        DisableButtons();
    }

    void OnRotateSelected(SelectEnterEventArgs _) => PressRotate();
    void OnFireSelected(SelectEnterEventArgs _) => PressFire();

    [ContextMenu("Test/Press Rotate (Play Mode)")]
    public void PressRotate()
    {
        if (!isActiveAndEnabled || !rotateAllowed) return;
        DisableButtons();
        rotatePressed.Invoke();
    }

    [ContextMenu("Test/Press Fire (Play Mode)")]
    public void PressFire()
    {
        if (!isActiveAndEnabled || !fireAllowed) return;
        DisableButtons();
        firePressed.Invoke();
    }

    public void EnableRotate()
    {
        DisableButtons();
        if (!isActiveAndEnabled || rotateButton == null) return;
        if (rotateButton is VRPressButton tutorialButton) tutorialButton.ResetButton();
        rotateAllowed = true;
        rotateButton.enabled = true;
    }

    public void EnableFire()
    {
        DisableButtons();
        if (!isActiveAndEnabled || fireButton == null) return;
        if (fireButton is VRPressButton tutorialButton) tutorialButton.ResetButton();
        fireAllowed = true;
        fireButton.enabled = true;
    }

    public void DisableButtons()
    {
        rotateAllowed = false;
        fireAllowed = false;
        if (rotateButton != null) rotateButton.enabled = false;
        if (fireButton != null) fireButton.enabled = false;
    }
}
